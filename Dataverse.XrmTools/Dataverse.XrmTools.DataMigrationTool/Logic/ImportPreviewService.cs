// System
using System;
using System.Linq;
using System.Text.RegularExpressions;
using System.Collections.Generic;

// Microsoft
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Metadata;

// 3rd Party
using Newtonsoft.Json.Linq;

// DataMigrationTool
using Dataverse.XrmTools.DataMigrationTool.Helpers;
using Dataverse.XrmTools.DataMigrationTool.Models;

namespace Dataverse.XrmTools.DataMigrationTool.Logic
{
    public static class ImportPreviewService
    {
        public static ExcelImportPreview BuildPreview(ExcelImportPreviewRequest request)
        {
            if (request == null) throw new ArgumentNullException(nameof(request));
            if (request.TableData == null) throw new ArgumentNullException(nameof(request.TableData));
            if (request.Collection == null) throw new ArgumentNullException(nameof(request.Collection));

            var table = request.TableData.Table;
            var metadata = request.TableData.Metadata;
            var settings = request.Settings ?? new UiSettings();
            var sourceCollection = request.Collection.ToEntityCollection(metadata?.Attributes ?? new AttributeMetadata[0]);
            var sourceRecords = request.Collection.Records?.ToList() ?? new List<Record>();
            var warningsByRow = GroupImportWarningsByRow(request.Collection.ImportErrors);
            var previewRows = new HashSet<int>();
            var sourceIds = sourceCollection.Entities.Select(e => e.Id).ToList();
            var targetIds = request.ExistingTargetIdsProvider?.Invoke(sourceIds)
                ?? new HashSet<Guid>();
            var targetIdSet = new HashSet<Guid>(targetIds);
            var availableMatchKeys = GetAvailableImportMatchKeys(request.Collection, request.Config);
            var valueColumns = GetPreviewValueColumns(request.Collection, request.Config);

            var preview = new ExcelImportPreview
            {
                FilePath = request.FilePath,
                SourceType = request.Config == null ? "JSON" : "Excel",
                SettingsSource = request.Config?.ImportSettings != null ? "Excel export metadata" : "Current settings",
                TableLogicalName = request.Collection.LogicalName,
                TargetName = request.TargetName ?? string.Empty,
                MatchKey = request.Collection.ImportMatchKey,
                MatchKeyMode = request.Collection.ImportMatchKeyMode,
                MatchKeys = request.Collection.ImportMatchKeys ?? new List<string>(),
                MatchAlternateKeyName = request.Config?.MatchAlternateKeyName,
                TotalRows = request.Collection.Count,
                ImportErrors = request.Collection.ImportErrors ?? new List<string>(),
                Settings = settings,
                MappingCount = request.MappingCount,
                AvailableMatchKeys = availableMatchKeys,
                AvailableAlternateKeys = GetAvailableImportAlternateKeys(metadata, availableMatchKeys),
                ValueColumns = valueColumns
            };

            var entityIndex = 0;
            foreach (var entity in sourceCollection.Entities)
            {
                var sourceRecord = entityIndex < sourceRecords.Count ? sourceRecords[entityIndex] : null;
                var rowNumber = sourceRecord != null && sourceRecord.SourceRowNumber > 0 ? sourceRecord.SourceRowNumber : entityIndex + 1;
                previewRows.Add(rowNumber);
                var rowWarnings = warningsByRow.TryGetValue(rowNumber, out var warnings)
                    ? string.Join(" | ", warnings)
                    : string.Empty;
                var exists = targetIdSet.Contains(entity.Id);
                var action = exists ? Enums.Action.Update : Enums.Action.Create;
                var enabled = (settings.Action & action) == action;

                if (request.Config != null && action == Enums.Action.Create && sourceRecord != null && !sourceRecord.PrimaryIdWasBlank)
                {
                    AddSuppliedGuidCreateWarning(preview, warningsByRow, rowNumber, request.Collection.PrimaryIdAttribute);
                    rowWarnings = string.Join(" | ", warningsByRow[rowNumber]);
                }

                var actionText = enabled ? action.ToString() : "Skip";
                var description = enabled
                    ? (exists ? "Target record found" : "Target record not found")
                    : $"{action} is not enabled in operation settings";
                if (!string.IsNullOrWhiteSpace(rowWarnings))
                    description = $"{description}; Warning: {rowWarnings}";
                var name = !string.IsNullOrWhiteSpace(table.NameAttribute) && entity.Attributes.Contains(table.NameAttribute)
                    ? entity[table.NameAttribute]?.ToString() ?? string.Empty
                    : string.Empty;

                if (actionText == "Create") preview.CreateCount++;
                else if (actionText == "Update") preview.UpdateCount++;
                else preview.SkippedCount++;

                preview.Items.Add(new ExcelImportPreviewItem
                {
                    RowNumber = rowNumber,
                    Action = actionText,
                    RecordId = entity.Id.ToString("D"),
                    MatchValue = GetPreviewMatchValue(entity, request.Collection.ImportMatchKeys),
                    Name = name,
                    Description = description,
                    Warnings = rowWarnings,
                    Values = GetPreviewValues(sourceRecord, valueColumns)
                });
                entityIndex++;
            }

            foreach (var rowWarning in warningsByRow.Where(w => !previewRows.Contains(w.Key)).OrderBy(w => w.Key))
            {
                preview.SkippedCount++;
                preview.Items.Add(new ExcelImportPreviewItem
                {
                    RowNumber = rowWarning.Key,
                    Action = "Skip",
                    RecordId = string.Empty,
                    MatchValue = string.Empty,
                    Name = string.Empty,
                    Description = "Skipped while reading source file",
                    Warnings = string.Join(" | ", rowWarning.Value)
                });
            }

            preview.Items = preview.Items.OrderBy(i => i.RowNumber).ToList();
            preview.TotalRows = preview.Items.Count;
            return preview;
        }

        public static Dictionary<int, List<string>> GroupImportWarningsByRow(IEnumerable<string> importErrors)
        {
            var result = new Dictionary<int, List<string>>();
            if (importErrors == null) return result;

            foreach (var error in importErrors.Where(e => !string.IsNullOrWhiteSpace(e)))
            {
                var rowNumber = GetWarningRowNumber(error);
                if (!rowNumber.HasValue) continue;

                if (!result.ContainsKey(rowNumber.Value))
                    result[rowNumber.Value] = new List<string>();

                result[rowNumber.Value].Add(error);
            }

            return result;
        }

        public static string GetSuppliedGuidCreateWarning(int rowNumber, string primaryIdAttribute)
        {
            return $"Row {rowNumber}, column '{primaryIdAttribute}': supplied record GUID was not found in target. Create will use this GUID; clear it for a Dataverse-generated ID and workbook writeback.";
        }

        public static bool AddSuppliedGuidCreateWarning(
            ExcelImportPreview preview,
            Dictionary<int, List<string>> warningsByRow,
            int rowNumber,
            string primaryIdAttribute)
        {
            if (preview == null || warningsByRow == null) return false;

            var warning = GetSuppliedGuidCreateWarning(rowNumber, primaryIdAttribute);
            if (!warningsByRow.ContainsKey(rowNumber))
                warningsByRow[rowNumber] = new List<string>();

            if (warningsByRow[rowNumber].Contains(warning)) return false;

            warningsByRow[rowNumber].Add(warning);
            preview.ImportErrors.Add(warning);
            return true;
        }

        public static int? GetWarningRowNumber(string warning)
        {
            var match = Regex.Match(warning ?? string.Empty, @"^Row\s+(?<row>\d+)", RegexOptions.IgnoreCase);
            if (!match.Success) return null;
            return int.TryParse(match.Groups["row"].Value, out var row) ? row : (int?)null;
        }

        public static string GetPreviewMatchValue(Entity entity, List<string> matchKeys)
        {
            if (entity == null) return string.Empty;
            if (matchKeys == null || !matchKeys.Any()) return entity.Id.ToString("D");
            return string.Join(", ", matchKeys.Select(key =>
            {
                var value = entity.Attributes.Contains(key) && entity[key] != null ? FormatPreviewMatchValue(entity[key]) : string.Empty;
                return $"{key}={value}";
            }));
        }

        public static string FormatPreviewMatchValue(object value)
        {
            if (value == null) return string.Empty;
            if (value is OptionSetValue option) return option.Value.ToString();
            if (value is OptionSetValueCollection options) return string.Join(", ", options.Select(o => o.Value));
            if (value is Money money) return money.Value.ToString();
            if (value is EntityReference reference) return reference.Id.ToString("D");
            return value.ToString();
        }

        public static string GetImportMatchKeyDisplay(string mode, List<string> fields, string alternateKeyName)
        {
            if (string.Equals(mode, "Guid", StringComparison.OrdinalIgnoreCase)) return null;
            if (string.Equals(mode, "AlternateKey", StringComparison.OrdinalIgnoreCase) && !string.IsNullOrWhiteSpace(alternateKeyName))
                return $"{alternateKeyName} ({string.Join(", ", fields ?? new List<string>())})";
            return fields?.Any() == true ? string.Join(", ", fields) : null;
        }

        public static List<string> NormalizeImportMatchKeyFields(ExcelImportMatchKeySelection selection)
        {
            return selection?.Fields?
                .Where(f => !string.IsNullOrWhiteSpace(f))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList() ?? new List<string>();
        }

        public static void ApplyImportMatchKeySelection(ExcelExportConfig config, ExcelImportMatchKeySelection selection)
        {
            if (config == null || selection == null) return;

            var fields = NormalizeImportMatchKeyFields(selection);

            config.MatchKeyMode = fields.Any() ? selection.Mode : "Guid";
            config.MatchKeys = fields;
            config.MatchKey = fields.Count == 1 ? fields[0] : null;
            config.MatchAlternateKeyName = string.Equals(config.MatchKeyMode, "AlternateKey", StringComparison.OrdinalIgnoreCase)
                ? selection.AlternateKeyName
                : null;

            if (config.ImportSettings != null)
            {
                config.ImportSettings.MatchKeyMode = config.MatchKeyMode;
                config.ImportSettings.MatchKeyFields = fields;
                config.ImportSettings.MatchAlternateKeyName = config.MatchAlternateKeyName;
            }
        }

        public static List<string> GetAvailableImportMatchKeys(RecordCollection collection, ExcelExportConfig config)
        {
            if (config?.Columns != null)
            {
                return config.Columns
                    .Where(c => c.Type != "Lookup" && c.Type != "LookupKeyField" && c.LogicalName != config.Table.PrimaryIdAttribute)
                    .Select(c => c.LogicalName)
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .OrderBy(c => c)
                    .ToList();
            }

            return (collection?.Records ?? Enumerable.Empty<Record>())
                .SelectMany(record => record.Attributes ?? Enumerable.Empty<RecordAttribute>())
                .Select(attr => attr.Key)
                .Where(key => !string.IsNullOrWhiteSpace(key) && !key.Equals(collection.PrimaryIdAttribute, StringComparison.OrdinalIgnoreCase))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(key => key)
                .ToList();
        }

        public static List<string> GetPreviewValueColumns(RecordCollection collection, ExcelExportConfig config)
        {
            if (config?.Columns != null)
            {
                return config.Columns
                    .Where(c => c.Type != "LookupKeyField" && !string.IsNullOrWhiteSpace(c.LogicalName))
                    .Select(c => c.LogicalName)
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .OrderBy(c => c)
                    .ToList();
            }

            return (collection?.Records ?? Enumerable.Empty<Record>())
                .SelectMany(record => record.Attributes ?? Enumerable.Empty<RecordAttribute>())
                .Select(attr => attr.Key)
                .Where(key => !string.IsNullOrWhiteSpace(key))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(key => key)
                .ToList();
        }

        private static Dictionary<string, string> GetPreviewValues(Record record, IEnumerable<string> columns)
        {
            var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            var attributes = record?.Attributes ?? new List<RecordAttribute>();
            foreach (var column in columns ?? Enumerable.Empty<string>())
            {
                var value = attributes.FirstOrDefault(a => string.Equals(a.Key, column, StringComparison.OrdinalIgnoreCase))?.Value;
                result[column] = FormatPreviewMatchValue(ToImportQueryValue(value));
            }
            return result;
        }

        public static List<ExcelImportAlternateKeyOption> GetAvailableImportAlternateKeys(EntityMetadata metadata, IEnumerable<string> availableFields)
        {
            return GetAvailableImportAlternateKeys(metadata?.Keys, availableFields);
        }

        public static List<ExcelImportAlternateKeyOption> GetAvailableImportAlternateKeys(IEnumerable<EntityKeyMetadata> keys, IEnumerable<string> availableFields)
        {
            // null availableFields = no filter; empty list = all keys filtered out
            HashSet<string> availableColumns = availableFields != null
                ? new HashSet<string>(availableFields, StringComparer.OrdinalIgnoreCase)
                : null;

            return keys?
                .Where(key => key.KeyAttributes?.Any() == true)
                .Select(key => new ExcelImportAlternateKeyOption
                {
                    Name = key.LogicalName,
                    DisplayName = key.DisplayName?.UserLocalizedLabel?.Label ?? key.LogicalName,
                    Fields = key.KeyAttributes.ToList()
                })
                .Where(key => availableColumns == null || key.Fields.All(availableColumns.Contains))
                .OrderBy(key => key.Name)
                .ToList() ?? new List<ExcelImportAlternateKeyOption>();
        }

        public static bool IsImportValueBlank(object value)
        {
            if (value == null) return true;
            if (value is JValue jValue) return jValue.Value == null || string.IsNullOrWhiteSpace(jValue.Value.ToString());
            return string.IsNullOrWhiteSpace(value.ToString());
        }

        public static object ToImportQueryValue(object value)
        {
            if (value == null) return null;
            if (value is OptionSetValue option) return option.Value;
            if (value is Money money) return money.Value;
            if (value is EntityReference reference) return reference.Id;
            if (value is JValue jValue) return jValue.Value;
            if (value is JObject obj)
            {
                if (obj.ContainsKey("Value")) return ToImportQueryValue(obj["Value"]);
                if (obj.ContainsKey("Id") && Guid.TryParse(obj["Id"]?.ToString(), out var id)) return id;
            }
            return value;
        }

        public static void EnsureRecordPrimaryId(string primaryIdAttribute, List<RecordAttribute> attributes)
        {
            var existing = attributes.FirstOrDefault(a => a.Key.Equals(primaryIdAttribute, StringComparison.OrdinalIgnoreCase));
            if (existing != null && !IsImportValueBlank(existing.Value)) return;

            SetRecordPrimaryId(primaryIdAttribute, Guid.NewGuid(), attributes);
        }

        public static void SetRecordPrimaryId(string primaryIdAttribute, Guid id, List<RecordAttribute> attributes)
        {
            var existing = attributes.FirstOrDefault(a => a.Key.Equals(primaryIdAttribute, StringComparison.OrdinalIgnoreCase));
            if (existing == null)
            {
                attributes.Add(new RecordAttribute { Key = primaryIdAttribute, Type = Enums.AttributeType.Identifier, Value = id });
                return;
            }

            existing.Type = Enums.AttributeType.Identifier;
            existing.Value = id;
        }
    }
}
