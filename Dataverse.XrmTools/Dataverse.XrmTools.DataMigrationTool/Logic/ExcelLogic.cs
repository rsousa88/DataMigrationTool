// System
using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Globalization;
using System.ComponentModel;

// Microsoft
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Metadata;
using Microsoft.Xrm.Sdk.Query;

// ClosedXML
using ClosedXML.Excel;

// DataMigrationTool
using Dataverse.XrmTools.DataMigrationTool.Models;
using Dataverse.XrmTools.DataMigrationTool.Helpers;
using Dataverse.XrmTools.DataMigrationTool.Repositories;

// 3rd Party
using Newtonsoft.Json;

namespace Dataverse.XrmTools.DataMigrationTool.Logic
{
    public class ExcelLogic
    {
        private const string DataSheetName = "Data";
        private const string MetaSheetName = "_dmt";
        private Dictionary<string, Guid?> _lookupResolutionCache;
        private Dictionary<string, Guid?> _matchKeyResolutionCache;

        #region Export

        public void Export(ExcelExportConfig config, IEnumerable<Entity> records, string filePath, IOrganizationService sourceService = null)
        {
            using (var wb = new XLWorkbook())
            {
                var dataSheet = wb.Worksheets.Add(DataSheetName);
                var metaSheet = wb.Worksheets.Add(MetaSheetName);
                metaSheet.Visibility = XLWorksheetVisibility.Hidden;

                WriteMetadata(metaSheet, config);
                WriteHeaders(dataSheet, config);
                WriteData(dataSheet, config, records, sourceService);

                // Hide the GUID column for lookups resolved via alt-key or custom — users only need the key field columns
                for (var i = 0; i < config.Columns.Count; i++)
                {
                    var col = config.Columns[i];
                    if ((col.Type == "Lookup" && col.Resolution != "Guid")
                        || (col.Type == "LookupKeyField" && col.KeyFieldType == "Lookup" && col.Resolution != "Guid"))
                        dataSheet.Column(i + 1).Hide();
                }

                ApplyDataRange(dataSheet, config);
                dataSheet.SheetView.FreezeRows(1);
                dataSheet.Columns().AdjustToContents();

                wb.SaveAs(filePath);
            }
        }

        private void WriteMetadata(IXLWorksheet sheet, ExcelExportConfig config)
        {
            sheet.Cell("A1").Value = JsonConvert.SerializeObject(config, Formatting.None);
        }

        private void WriteHeaders(IXLWorksheet sheet, ExcelExportConfig config)
        {
            var col = 1;
            foreach (var column in config.Columns)
            {
                var cell = sheet.Cell(1, col);
                cell.Value = column.LogicalName;
                cell.Style.Font.Bold = true;
                cell.Style.Fill.BackgroundColor = IsRelatedColumn(column)
                    ? XLColor.FromHtml("#E2F0D9")
                    : XLColor.FromHtml("#D9E1F2");
                AddHintComment(cell, GetHintText(column, config));
                col++;
            }
        }

        private void AddHintComment(IXLCell cell, string hint)
        {
            if (string.IsNullOrWhiteSpace(hint)) return;

            cell.Comment.AddText(hint);
            cell.Comment.SetAuthor("Data Migration Tool");
            cell.Comment.Style.Size.SetAutomaticSize(false);
            cell.Comment.Style.Size.SetWidth(40);
            cell.Comment.Style.Size.SetHeight(25);
        }

        private bool IsRelatedColumn(ExcelColumnConfig column)
        {
            return column.Type == "LookupKeyField";
        }

        private void ApplyDataRange(IXLWorksheet sheet, ExcelExportConfig config)
        {
            if (!config.Columns.Any()) return;

            var lastRow = Math.Max(1, sheet.LastRowUsed()?.RowNumber() ?? 1);
            var range = sheet.Range(1, 1, lastRow, config.Columns.Count);
            range.SetAutoFilter();
        }

        private string GetHintText(ExcelColumnConfig column, ExcelExportConfig config)
        {
            if (!string.IsNullOrEmpty(column.HintOverride)) return column.HintOverride;
            if (!string.IsNullOrEmpty(config.MatchKey)
                && string.Equals(column.LogicalName, config.MatchKey, StringComparison.OrdinalIgnoreCase))
                return "Match key (used for import upsert)";

            switch (column.Type)
            {
                case "Lookup":
                    return string.IsNullOrEmpty(column.RelatedTable)
                        ? "Lookup (GUID)"
                        : $"Lookup → {column.RelatedTable} (GUID)";
                case "LookupKeyField":
                    if (column.KeyFieldType == "OptionSet")
                        return column.ExportMode == "Label" ? "Key choice label" : "Key choice value";
                    if (column.KeyFieldType == "Lookup")
                        return string.IsNullOrEmpty(column.RelatedTable)
                            ? $"Lookup key for {column.OwnerAttribute}"
                            : $"Lookup key -> {column.RelatedTable}";
                    return $"Key for {column.OwnerAttribute}";
                case "OptionSet":
                    return column.ExportMode == "Label" ? "Option Label" : "Option Value (Integer)";
                case "MultiOptionSet":
                    return column.ExportMode == "Label" ? "Labels (comma-separated)" : "Values (comma-separated integers)";
                case "DateTime":
                    return "DateTime (UTC ISO 8601)";
                case "Money":
                    return "Money (Decimal)";
                case "Boolean":
                    return "true / false";
                default:
                    return column.Type;
            }
        }

        private void WriteData(IXLWorksheet sheet, ExcelExportConfig config, IEnumerable<Entity> records, IOrganizationService sourceService)
        {
            if (records == null) return;

            var childColumns = config.Columns
                .Where(c => c.Type == "LookupKeyField")
                .GroupBy(c => c.OwnerAttribute)
                .ToDictionary(g => g.Key, g => g.ToList());
            var columnIndexes = config.Columns
                .Select((column, index) => new { column, index })
                .ToDictionary(x => x.column.LogicalName, x => x.index + 1);
            var relatedRecordCache = new Dictionary<string, Entity>();

            var row = 2;
            foreach (var entity in records)
            {
                var col = 1;
                foreach (var column in config.Columns)
                {
                    var cell = sheet.Cell(row, col);
                    WriteCell(cell, column, entity);
                    col++;
                }

                foreach (var lookupColumn in config.Columns.Where(c => c.Type == "Lookup" && c.Resolution != "Guid"))
                    WriteRelatedLookupColumns(sheet, row, lookupColumn, entity, childColumns, columnIndexes, relatedRecordCache, sourceService);
                row++;
            }
        }

        private void WriteRelatedLookupColumns(
            IXLWorksheet sheet,
            int row,
            ExcelColumnConfig lookupColumn,
            Entity ownerRecord,
            Dictionary<string, List<ExcelColumnConfig>> childColumns,
            Dictionary<string, int> columnIndexes,
            Dictionary<string, Entity> relatedRecordCache,
            IOrganizationService sourceService)
        {
            var ownerFieldName = lookupColumn.Type == "LookupKeyField"
                ? GetOwnedFieldName(lookupColumn)
                : lookupColumn.LogicalName;

            if (!ownerRecord.Attributes.Contains(ownerFieldName)
                || !(ownerRecord[ownerFieldName] is EntityReference reference)
                || string.IsNullOrWhiteSpace(lookupColumn.RelatedTable))
            {
                return;
            }

            if (columnIndexes.TryGetValue(lookupColumn.LogicalName, out var lookupColumnIndex))
                WriteRelatedLookupCell(sheet.Cell(row, lookupColumnIndex), reference.Id, lookupColumn);

            var relatedRecord = GetRelatedRecord(reference, lookupColumn, relatedRecordCache, sourceService);
            if (relatedRecord == null || !childColumns.TryGetValue(lookupColumn.LogicalName, out var keys)) return;

            foreach (var keyColumn in keys)
            {
                if (!columnIndexes.TryGetValue(keyColumn.LogicalName, out var keyColumnIndex)) continue;

                var keyField = GetOwnedFieldName(keyColumn);
                var value = relatedRecord.Attributes.Contains(keyField)
                    ? relatedRecord[keyField]
                    : null;
                WriteRelatedLookupCell(sheet.Cell(row, keyColumnIndex), value, keyColumn);

                if (keyColumn.KeyFieldType == "Lookup" && keyColumn.Resolution != "Guid")
                {
                    WriteRelatedLookupColumns(sheet, row, keyColumn, relatedRecord, childColumns, columnIndexes, relatedRecordCache, sourceService);
                }
            }
        }

        private Entity GetRelatedRecord(
            EntityReference reference,
            ExcelColumnConfig lookupColumn,
            Dictionary<string, Entity> relatedRecordCache,
            IOrganizationService sourceService)
        {
            var relatedTable = !string.IsNullOrWhiteSpace(reference.LogicalName)
                ? reference.LogicalName
                : lookupColumn.RelatedTable;
            var cacheKey = $"{relatedTable}:{reference.Id:D}:{string.Join(",", lookupColumn.AlternateKeyFields)}";
            if (relatedRecordCache.TryGetValue(cacheKey, out var cached)) return cached;

            Entity relatedRecord = null;
            try
            {
                relatedRecord = sourceService?.Retrieve(
                    relatedTable,
                    reference.Id,
                    new ColumnSet(lookupColumn.AlternateKeyFields.ToArray()));
            }
            catch
            {
                // Leave related columns blank if the referenced row cannot be loaded.
            }

            relatedRecordCache[cacheKey] = relatedRecord;
            return relatedRecord;
        }

        private void WriteRelatedLookupCell(IXLCell cell, object value, ExcelColumnConfig column)
        {
            if (value == null)
            {
                cell.SetValue(string.Empty);
                return;
            }

            if (value is AliasedValue aliased)
            {
                WriteRelatedLookupCell(cell, aliased.Value, column);
                return;
            }

            switch (value)
            {
                case EntityReference reference:
                    cell.SetValue(reference.Id.ToString("D"));
                    cell.Style.NumberFormat.NumberFormatId = 49;
                    break;
                case OptionSetValue option:
                    if (column.ExportMode == "Label")
                    {
                        var label = column.Options?.FirstOrDefault(o => o.Value == option.Value)?.Label ?? option.Value.ToString();
                        cell.SetValue(label);
                    }
                    else
                    {
                        cell.SetValue(option.Value);
                    }
                    break;
                case Money money:
                    cell.SetValue(money.Value);
                    break;
                case int integer:
                    cell.SetValue(integer);
                    break;
                case long bigInt:
                    cell.SetValue(bigInt);
                    break;
                case decimal decimalValue:
                    cell.SetValue(decimalValue);
                    break;
                case double doubleValue:
                    cell.SetValue(doubleValue);
                    break;
                case float floatValue:
                    cell.SetValue(floatValue);
                    break;
                case DateTime date:
                    cell.SetValue(date.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture));
                    cell.Style.NumberFormat.NumberFormatId = 49;
                    break;
                case bool boolean:
                    cell.SetValue(boolean.ToString().ToLowerInvariant());
                    cell.Style.NumberFormat.NumberFormatId = 49;
                    break;
                case Guid guid:
                    cell.SetValue(guid.ToString("D"));
                    cell.Style.NumberFormat.NumberFormatId = 49;
                    break;
                default:
                    cell.SetValue(Convert.ToString(value, CultureInfo.InvariantCulture) ?? string.Empty);
                    cell.Style.NumberFormat.NumberFormatId = 49;
                    break;
            }
        }

        private string GetOwnedFieldName(ExcelColumnConfig column)
        {
            return !string.IsNullOrWhiteSpace(column.OwnerAttribute)
                && column.LogicalName.StartsWith(column.OwnerAttribute + ".", StringComparison.Ordinal)
                ? column.LogicalName.Substring(column.OwnerAttribute.Length + 1)
                : column.LogicalName;
        }

        private void WriteCell(IXLCell cell, ExcelColumnConfig column, Entity entity)
        {
            // LookupKeyField columns are written when the parent Lookup column is processed
            if (column.Type == "LookupKeyField") return;

            var logicalName = column.Type == "Lookup" && column.Resolution == "AlternateKey"
                ? column.LogicalName  // parent lookup attribute
                : column.LogicalName;

            if (!entity.Attributes.Contains(logicalName) || entity[logicalName] == null)
            {
                cell.SetValue(string.Empty);
                return;
            }

            var value = entity[logicalName];

            switch (column.Type)
            {
                case "Lookup":
                    if (value is EntityReference er)
                    {
                        // GUID mode: write the guid; alt-key mode handled separately at caller
                        cell.SetValue(er.Id.ToString("D"));
                        cell.Style.NumberFormat.NumberFormatId = 49; // Text
                    }
                    break;

                case "OptionSet":
                    if (value is OptionSetValue osv)
                    {
                        if (column.ExportMode == "Label")
                        {
                            var label = column.Options?.FirstOrDefault(o => o.Value == osv.Value)?.Label ?? osv.Value.ToString();
                            cell.SetValue(label);
                        }
                        else
                        {
                            cell.SetValue(osv.Value);
                        }
                    }
                    break;

                case "MultiOptionSet":
                    if (value is OptionSetValueCollection osvc)
                    {
                        if (column.ExportMode == "Label")
                        {
                            var labels = osvc.Select(o => column.Options?.FirstOrDefault(opt => opt.Value == o.Value)?.Label ?? o.Value.ToString());
                            cell.SetValue(string.Join(", ", labels));
                        }
                        else
                        {
                            cell.SetValue(string.Join(",", osvc.Select(o => o.Value)));
                        }
                    }
                    break;

                case "Money":
                    if (value is Money money)
                        cell.SetValue(money.Value);
                    break;

                case "Integer":
                    cell.SetValue(Convert.ToInt32(value, CultureInfo.InvariantCulture));
                    break;

                case "BigInt":
                    cell.SetValue(Convert.ToInt64(value, CultureInfo.InvariantCulture));
                    break;

                case "Decimal":
                    cell.SetValue(Convert.ToDecimal(value, CultureInfo.InvariantCulture));
                    break;

                case "Double":
                    cell.SetValue(Convert.ToDouble(value, CultureInfo.InvariantCulture));
                    break;

                case "Boolean":
                    cell.SetValue(value.ToString().ToLowerInvariant());
                    cell.Style.NumberFormat.NumberFormatId = 49; // Text
                    break;

                case "DateTime":
                    if (value is DateTime dt)
                    {
                        cell.SetValue(dt.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture));
                        cell.Style.NumberFormat.NumberFormatId = 49; // Text
                    }
                    break;

                case "Guid":
                    cell.SetValue(value.ToString().ToLowerInvariant());
                    cell.Style.NumberFormat.NumberFormatId = 49; // Text
                    break;

                default:
                    cell.SetValue(value?.ToString() ?? string.Empty);
                    break;
            }
        }

        #endregion Export

        #region Export — Lookup alt-key column writing

        public void WriteDataWithAltKeys(IXLWorksheet sheet, ExcelExportConfig config, IEnumerable<Entity> records)
        {
            if (records == null) return;

            // Build column index map
            var colIndex = new Dictionary<string, int>();
            for (var i = 0; i < config.Columns.Count; i++)
                colIndex[config.Columns[i].LogicalName] = i + 1;

            var row = 2;
            foreach (var entity in records)
            {
                for (var i = 0; i < config.Columns.Count; i++)
                {
                    var column = config.Columns[i];
                    if (column.Type == "LookupKeyField") continue;

                    var cell = sheet.Cell(row, i + 1);
                    WriteCell(cell, column, entity);

                    // For alt-key lookups, write key field values into their dedicated columns
                    if (column.Type == "Lookup" && column.Resolution == "AlternateKey"
                        && entity.Attributes.Contains(column.LogicalName)
                        && entity[column.LogicalName] is EntityReference er)
                    {
                        foreach (var keyField in column.AlternateKeyFields)
                        {
                            var keyColName = $"{column.LogicalName}.{keyField}";
                            if (!colIndex.TryGetValue(keyColName, out var keyCol)) continue;

                            // Try to read the key value from the entity's extended properties or formatted values
                            var keyValue = er.KeyAttributes.Contains(keyField)
                                ? er.KeyAttributes[keyField]?.ToString()
                                : entity.FormattedValues.Contains(keyColName)
                                    ? entity.FormattedValues[keyColName]
                                    : string.Empty;

                            sheet.Cell(row, keyCol).SetValue(keyValue ?? string.Empty);
                        }

                        // Clear the parent guid cell — alt-key mode uses key columns instead
                        cell.SetValue(string.Empty);
                    }
                }
                row++;
            }
        }

        #endregion

        #region Import

        public ExcelExportConfig ReadMetadata(string filePath)
        {
            using (var wb = new XLWorkbook(filePath))
            {
                return ReadMetadata(wb);
            }
        }

        public RecordCollection ImportFromExcel(string filePath, ExcelExportConfig config, IOrganizationService targetService, BackgroundWorker worker = null)
        {
            using (var wb = new XLWorkbook(filePath))
            {
                return ImportFromWorkbook(wb, config, targetService, worker);
            }
        }

        public RecordCollection ImportFromExcel(string filePath, out ExcelExportConfig config, IOrganizationService targetService, BackgroundWorker worker = null)
        {
            return ImportFromExcel(filePath, out config, targetService, worker, null);
        }

        public int GetImportRowCount(string filePath, out ExcelExportConfig config)
        {
            using (var wb = new XLWorkbook(filePath))
            {
                config = ReadMetadata(wb);
                if (!wb.Worksheets.Contains(DataSheetName))
                    throw new Exception("Data sheet 'Data' not found in the file.");

                var dataSheet = wb.Worksheet(DataSheetName);
                var headerMap = BuildHeaderMap(dataSheet, config);
                var firstDataRow = GetFirstDataRow(dataSheet, config, headerMap);
                var lastRow = dataSheet.LastRowUsed()?.RowNumber() ?? 2;
                return Math.Max(0, lastRow - firstDataRow + 1);
            }
        }

        public RecordCollection ImportFromExcel(string filePath, out ExcelExportConfig config, IOrganizationService targetService, BackgroundWorker worker, Action<ExcelExportConfig> configure)
        {
            using (var wb = new XLWorkbook(filePath))
            {
                config = ReadMetadata(wb);
                var metadataBeforeConfigure = JsonConvert.SerializeObject(config, Formatting.None);
                configure?.Invoke(config);
                var metadataAfterConfigure = JsonConvert.SerializeObject(config, Formatting.None);
                if (!string.Equals(metadataBeforeConfigure, metadataAfterConfigure, StringComparison.Ordinal))
                {
                    WriteMetadata(wb.Worksheet(MetaSheetName), config);
                    wb.Save();
                }

                return ImportFromWorkbook(wb, config, targetService, worker);
            }
        }

        public int UpdateImportedGuids(string filePath, ExcelExportConfig config, RecordCollection collection, ISet<Guid> importedIds, BackgroundWorker worker = null)
        {
            if (string.IsNullOrWhiteSpace(filePath) || config == null || collection == null || importedIds == null || !importedIds.Any())
                return 0;

            using (var wb = new XLWorkbook(filePath))
            {
                ThrowIfCancelled(worker);
                if (!wb.Worksheets.Contains(DataSheetName))
                    throw new Exception("Data sheet 'Data' not found in the file.");

                var dataSheet = wb.Worksheet(DataSheetName);
                var headerMap = BuildHeaderMap(dataSheet, config);
                var updatedCells = 0;
                var rows = (collection.Records ?? Enumerable.Empty<Record>()).ToList();
                var primaryIdAttribute = config.Table?.PrimaryIdAttribute ?? collection.PrimaryIdAttribute;
                var lookupColumns = (config.Columns ?? new List<ExcelColumnConfig>())
                    .Where(c => c.Type == "Lookup")
                    .ToList();

                foreach (var record in rows)
                {
                    ThrowIfCancelled(worker);
                    var row = record.SourceRowNumber;
                    if (row <= 0) continue;

                    var attributes = record.Attributes?.ToList() ?? new List<RecordAttribute>();
                    var recordId = GetRecordGuid(attributes, primaryIdAttribute);
                    if (!recordId.HasValue || !importedIds.Contains(recordId.Value)) continue;

                    updatedCells += SetCellGuid(dataSheet, headerMap, primaryIdAttribute, row, recordId.Value, hidden: true);

                    foreach (var column in lookupColumns)
                    {
                        var lookup = attributes.FirstOrDefault(a => a.Key.Equals(column.LogicalName, StringComparison.OrdinalIgnoreCase))?.Value as EntityReference;
                        if (lookup == null || lookup.Id == Guid.Empty) continue;

                        updatedCells += SetCellGuid(dataSheet, headerMap, column.LogicalName, row, lookup.Id, hidden: false);
                    }
                }

                if (updatedCells > 0)
                {
                    worker?.ReportProgress(0, $"Excel import: writing {updatedCells} resolved GUID value(s) back to workbook...");
                    wb.Save();
                }

                return updatedCells;
            }
        }

        private ExcelExportConfig ReadMetadata(XLWorkbook wb)
        {
            if (!wb.Worksheets.Contains(MetaSheetName))
                throw new Exception("This file was not exported from Data Migration Tool — metadata sheet '_dmt' is missing.");

            var metaSheet = wb.Worksheet(MetaSheetName);
            var json = metaSheet.Cell("A1").GetString();
            if (string.IsNullOrWhiteSpace(json))
                throw new Exception("Metadata sheet '_dmt' is empty.");

            return JsonConvert.DeserializeObject<ExcelExportConfig>(json);
        }

        private RecordCollection ImportFromWorkbook(XLWorkbook wb, ExcelExportConfig config, IOrganizationService targetService, BackgroundWorker worker = null)
        {
            _lookupResolutionCache = new Dictionary<string, Guid?>(StringComparer.OrdinalIgnoreCase);
            _matchKeyResolutionCache = new Dictionary<string, Guid?>(StringComparer.OrdinalIgnoreCase);

            ThrowIfCancelled(worker);
            if (!wb.Worksheets.Contains(DataSheetName))
                throw new Exception("Data sheet 'Data' not found in the file.");

            var dataSheet = wb.Worksheet(DataSheetName);
            var records = new List<Record>();
            var importErrors = new List<string>();
            var lastRow = dataSheet.LastRowUsed()?.RowNumber() ?? 2;

            var headerMap = BuildHeaderMap(dataSheet, config);
            var firstDataRow = GetFirstDataRow(dataSheet, config, headerMap);
            var totalRows = Math.Max(0, lastRow - firstDataRow + 1);

            var altKeyGroups = config.Columns
                .Where(c => c.Type == "LookupKeyField")
                .GroupBy(c => c.OwnerAttribute)
                .ToDictionary(g => g.Key, g => g.ToList());

            var targetRepo = targetService != null ? new CrmRepo(targetService, worker) : null;

            for (var row = firstDataRow; row <= lastRow; row++)
            {
                ThrowIfCancelled(worker);
                if ((row - firstDataRow) % 25 == 0)
                    worker?.ReportProgress(0, $"Excel import: reading row {row - firstDataRow + 1} of {totalRows}...");

                var attributes = new List<RecordAttribute>();
                var rowErrors = new List<string>();

                foreach (var column in config.Columns)
                {
                    if (column.Type == "LookupKeyField") continue;
                    if (!headerMap.TryGetValue(column.LogicalName, out var colNum)) continue;

                    var cellValue = dataSheet.Cell(row, colNum).GetString();

                    try
                    {
                        var attr = ParseCell(cellValue, column, row, colNum, dataSheet, headerMap, altKeyGroups, config, targetRepo);
                        if (attr != null) attributes.Add(attr);
                    }
                    catch (OperationCanceledException)
                    {
                        throw;
                    }
                    catch (Exception ex)
                    {
                        rowErrors.Add($"Row {row}, column '{column.LogicalName}': {ex.Message}");
                    }
                }

                if (rowErrors.Any())
                {
                    importErrors.AddRange(rowErrors);
                    continue;
                }

                if (GetMatchKeys(config).Any())
                {
                    try
                    {
                        ApplyMatchKey(config, attributes, targetRepo);
                    }
                    catch (OperationCanceledException)
                    {
                        throw;
                    }
                    catch (Exception ex)
                    {
                        importErrors.Add($"Row {row}: {ex.Message}");
                        continue;
                    }
                }

                EnsurePrimaryIdAttribute(config.Table.PrimaryIdAttribute, attributes);

                records.Add(new Record { SourceRowNumber = row, Attributes = attributes });
            }

            return new RecordCollection
            {
                LogicalName = config.Table.LogicalName,
                PrimaryIdAttribute = config.Table.PrimaryIdAttribute,
                Records = records,
                Count = records.Count,
                ImportErrors = importErrors,
                ImportMatchKey = GetMatchKeyDisplay(config),
                ImportMatchKeys = GetMatchKeys(config),
                ImportMatchKeyMode = GetMatchKeys(config).Any()
                    ? (string.IsNullOrWhiteSpace(config.MatchKeyMode) ? "Custom" : config.MatchKeyMode)
                    : "Guid"
            };
        }

        private void ThrowIfCancelled(BackgroundWorker worker)
        {
            if (worker != null && worker.CancellationPending)
                throw new OperationCanceledException();
        }

        private Dictionary<string, int> BuildHeaderMap(IXLWorksheet sheet, ExcelExportConfig config)
        {
            var map = new Dictionary<string, int>();
            var lastCol = sheet.LastColumnUsed()?.ColumnNumber() ?? 1;

            for (var col = 1; col <= lastCol; col++)
            {
                var header = sheet.Cell(1, col).GetString();
                if (!string.IsNullOrWhiteSpace(header))
                    map[header] = col;
            }

            return map;
        }

        private Guid? GetRecordGuid(List<RecordAttribute> attributes, string logicalName)
        {
            var value = attributes.FirstOrDefault(a => a.Key.Equals(logicalName, StringComparison.OrdinalIgnoreCase))?.Value;
            if (value == null) return null;
            if (value is Guid guid) return guid;
            return Guid.TryParse(value.ToString(), out var parsed) ? parsed : (Guid?)null;
        }

        private int SetCellGuid(IXLWorksheet sheet, Dictionary<string, int> headerMap, string logicalName, int row, Guid id, bool hidden)
        {
            if (string.IsNullOrWhiteSpace(logicalName) || id == Guid.Empty) return 0;

            if (!headerMap.TryGetValue(logicalName, out var col))
            {
                col = (sheet.LastColumnUsed()?.ColumnNumber() ?? 0) + 1;
                sheet.Cell(1, col).Value = logicalName;
                sheet.Cell(1, col).Style.Font.Bold = true;
                sheet.Cell(1, col).Style.Fill.BackgroundColor = hidden
                    ? XLColor.FromHtml("#D9E1F2")
                    : XLColor.FromHtml("#E2F0D9");
                if (hidden) sheet.Column(col).Hide();
                headerMap[logicalName] = col;
            }

            var cell = sheet.Cell(row, col);
            var value = id.ToString("D");
            if (string.Equals(cell.GetString(), value, StringComparison.OrdinalIgnoreCase)) return 0;

            cell.Value = value;
            return 1;
        }

        private int GetFirstDataRow(IXLWorksheet sheet, ExcelExportConfig config, Dictionary<string, int> headerMap)
        {
            var checkedColumns = 0;
            var matchingHints = 0;

            foreach (var column in config.Columns)
            {
                if (!headerMap.TryGetValue(column.LogicalName, out var colNum)) continue;

                var rowTwoValue = sheet.Cell(2, colNum).GetString();
                if (string.IsNullOrWhiteSpace(rowTwoValue)) continue;

                checkedColumns++;
                if (string.Equals(rowTwoValue, GetHintText(column, config), StringComparison.Ordinal))
                    matchingHints++;
            }

            return checkedColumns > 0 && matchingHints >= Math.Max(1, checkedColumns / 2)
                ? 3
                : 2;
        }

        private void ApplyMatchKey(ExcelExportConfig config, List<RecordAttribute> attributes, CrmRepo targetRepo)
        {
            if (targetRepo == null) throw new Exception("Target connection required for match key resolution.");

            var keyValues = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
            foreach (var key in GetMatchKeys(config))
            {
                var matchAttr = attributes.FirstOrDefault(a => a.Key.Equals(key, StringComparison.OrdinalIgnoreCase));
                if (matchAttr == null) throw new Exception($"Match key field '{key}' not present in this record.");
                if (matchAttr.Value == null || string.IsNullOrWhiteSpace(matchAttr.Value.ToString()))
                    throw new Exception($"Match key field '{key}' is blank.");

                keyValues[key] = ToQueryValue(matchAttr.Value);
            }

            var targetId = FindByFieldValuesCached(targetRepo, config.Table.LogicalName, keyValues);
            if (targetId.HasValue)
                SetPrimaryIdAttribute(config.Table.PrimaryIdAttribute, targetId.Value, attributes);
            else
                EnsurePrimaryIdAttribute(config.Table.PrimaryIdAttribute, attributes);
        }

        private List<string> GetMatchKeys(ExcelExportConfig config)
        {
            if (config?.MatchKeys?.Any() == true)
                return config.MatchKeys.Where(k => !string.IsNullOrWhiteSpace(k)).Distinct(StringComparer.OrdinalIgnoreCase).ToList();

            return string.IsNullOrWhiteSpace(config?.MatchKey)
                ? new List<string>()
                : new List<string> { config.MatchKey };
        }

        private string GetMatchKeyDisplay(ExcelExportConfig config)
        {
            var keys = GetMatchKeys(config);
            if (!keys.Any()) return null;
            if (string.Equals(config.MatchKeyMode, "AlternateKey", StringComparison.OrdinalIgnoreCase)
                && !string.IsNullOrWhiteSpace(config.MatchAlternateKeyName))
                return $"{config.MatchAlternateKeyName} ({string.Join(", ", keys)})";
            return string.Join(", ", keys);
        }

        private object ToQueryValue(object value)
        {
            if (value is OptionSetValue option) return option.Value;
            if (value is Money money) return money.Value;
            if (value is EntityReference reference) return reference.Id;
            return value;
        }

        private void SetPrimaryIdAttribute(string primaryIdAttribute, Guid id, List<RecordAttribute> attributes)
        {
            var existing = attributes.FirstOrDefault(a => a.Key.Equals(primaryIdAttribute, StringComparison.OrdinalIgnoreCase));
            if (existing != null)
            {
                existing.Value = id;
                existing.Type = Enums.AttributeType.Identifier;
            }
            else
            {
                attributes.Add(new RecordAttribute
                {
                    Key = primaryIdAttribute,
                    Type = Enums.AttributeType.Identifier,
                    Value = id
                });
            }
        }

        private void EnsurePrimaryIdAttribute(string primaryIdAttribute, List<RecordAttribute> attributes)
        {
            var existing = attributes.FirstOrDefault(a => a.Key.Equals(primaryIdAttribute, StringComparison.OrdinalIgnoreCase));
            if (existing == null || existing.Value == null || string.IsNullOrWhiteSpace(existing.Value.ToString()))
                SetPrimaryIdAttribute(primaryIdAttribute, Guid.NewGuid(), attributes);
        }

        private RecordAttribute ParseCell(
            string cellValue,
            ExcelColumnConfig column,
            int row,
            int colNum,
            IXLWorksheet sheet,
            Dictionary<string, int> headerMap,
            Dictionary<string, List<ExcelColumnConfig>> altKeyGroups,
            ExcelExportConfig config,
            CrmRepo targetRepo)
        {
            object value;
            var type = column.Type;
            var canResolveLookupFromKeys = type == "Lookup" && CanResolveLookupFromKeyColumns(column, altKeyGroups);
            if (string.IsNullOrWhiteSpace(cellValue) && !canResolveLookupFromKeys) return null;

            switch (type)
            {
                case "String":
                    value = cellValue;
                    break;

                case "Integer":
                    value = int.Parse(cellValue, CultureInfo.InvariantCulture);
                    break;

                case "BigInt":
                    value = long.Parse(cellValue, CultureInfo.InvariantCulture);
                    break;

                case "Decimal":
                    value = decimal.Parse(cellValue, CultureInfo.InvariantCulture);
                    break;

                case "Double":
                    value = double.Parse(cellValue, CultureInfo.InvariantCulture);
                    break;

                case "Money":
                    value = new Money(decimal.Parse(cellValue, CultureInfo.InvariantCulture));
                    break;

                case "Boolean":
                    value = bool.Parse(cellValue);
                    break;

                case "DateTime":
                    value = DateTime.Parse(cellValue, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind);
                    break;

                case "Guid":
                    value = Guid.Parse(cellValue);
                    break;

                case "OptionSet":
                    if (column.ExportMode == "Label")
                    {
                        var match = MatchOptionLabel(column, cellValue);
                        value = new OptionSetValue(match.Value);
                    }
                    else
                    {
                        value = new OptionSetValue(int.Parse(cellValue, CultureInfo.InvariantCulture));
                    }
                    break;

                case "MultiOptionSet":
                    var parts = cellValue.Split(',').Select(p => p.Trim()).Where(p => !string.IsNullOrEmpty(p));
                    var optionValues = new List<OptionSetValue>();
                    foreach (var part in parts)
                    {
                        if (column.ExportMode == "Label")
                        {
                            var match = MatchOptionLabel(column, part);
                            optionValues.Add(new OptionSetValue(match.Value));
                        }
                        else
                        {
                            optionValues.Add(new OptionSetValue(int.Parse(part, CultureInfo.InvariantCulture)));
                        }
                    }
                    value = new OptionSetValueCollection(optionValues);
                    break;

                case "Lookup":
                    if (canResolveLookupFromKeys)
                    {
                        if (targetRepo == null) throw new Exception("Target connection required for alternate key resolution.");
                        var keyValues = BuildLookupKeyValues(column, row, sheet, headerMap, altKeyGroups, targetRepo);
                        if (AllKeyValuesBlank(keyValues)) return null;
                        var resolvedId = ResolveByAlternateKeyCached(targetRepo, column.RelatedTable, keyValues);
                        if (!resolvedId.HasValue) throw new Exception($"No match found in '{column.RelatedTable}' for keys: {FormatKeyValues(keyValues)}");

                        value = new EntityReference(column.RelatedTable, resolvedId.Value);
                    }
                    else
                    {
                        value = new EntityReference(column.RelatedTable, Guid.Parse(cellValue));
                    }
                    break;

                default:
                    value = cellValue;
                    break;
            }

            return new RecordAttribute
            {
                Key = column.LogicalName,
                Type = MapToAttributeType(type),
                Value = value
            };
        }

        private Dictionary<string, object> BuildLookupKeyValues(
            ExcelColumnConfig lookupColumn,
            int row,
            IXLWorksheet sheet,
            Dictionary<string, int> headerMap,
            Dictionary<string, List<ExcelColumnConfig>> altKeyGroups,
            CrmRepo targetRepo)
        {
            var keyValues = new Dictionary<string, object>();
            if (!altKeyGroups.TryGetValue(lookupColumn.LogicalName, out var keyColumns)) return keyValues;

            foreach (var keyColumn in keyColumns)
            {
                if (!headerMap.TryGetValue(keyColumn.LogicalName, out var keyColNum)) continue;

                var cellValue = sheet.Cell(row, keyColNum).GetString();
                var fieldName = GetOwnedFieldName(keyColumn);
                keyValues[fieldName] = string.IsNullOrWhiteSpace(cellValue) && !CanResolveLookupFromKeyColumns(keyColumn, altKeyGroups)
                    ? null
                    : ParseLookupKeyFieldValue(keyColumn, cellValue, row, sheet, headerMap, altKeyGroups, targetRepo);
            }

            return keyValues;
        }

        private object ParseLookupKeyFieldValue(
            ExcelColumnConfig keyColumn,
            string cellValue,
            int row,
            IXLWorksheet sheet,
            Dictionary<string, int> headerMap,
            Dictionary<string, List<ExcelColumnConfig>> altKeyGroups,
            CrmRepo targetRepo)
        {
            switch (keyColumn.KeyFieldType ?? "String")
            {
                case "Integer":
                    return int.Parse(cellValue, CultureInfo.InvariantCulture);
                case "BigInt":
                    return long.Parse(cellValue, CultureInfo.InvariantCulture);
                case "Decimal":
                    return decimal.Parse(cellValue, CultureInfo.InvariantCulture);
                case "Double":
                    return double.Parse(cellValue, CultureInfo.InvariantCulture);
                case "Money":
                    return decimal.Parse(cellValue, CultureInfo.InvariantCulture);
                case "Boolean":
                    return bool.Parse(cellValue);
                case "DateTime":
                    return DateTime.Parse(cellValue, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind);
                case "Guid":
                    return Guid.Parse(cellValue);
                case "OptionSet":
                    return keyColumn.ExportMode == "Label"
                        ? MatchOptionLabel(keyColumn, cellValue).Value
                        : int.Parse(cellValue, CultureInfo.InvariantCulture);
                case "Lookup":
                    return ResolveNestedLookupKey(keyColumn, cellValue, row, sheet, headerMap, altKeyGroups, targetRepo);
                default:
                    return cellValue;
            }
        }

        private object ResolveNestedLookupKey(
            ExcelColumnConfig keyColumn,
            string cellValue,
            int row,
            IXLWorksheet sheet,
            Dictionary<string, int> headerMap,
            Dictionary<string, List<ExcelColumnConfig>> altKeyGroups,
            CrmRepo targetRepo)
        {
            if (CanResolveLookupFromKeyColumns(keyColumn, altKeyGroups))
            {
                var nestedKeyValues = BuildLookupKeyValues(keyColumn, row, sheet, headerMap, altKeyGroups, targetRepo);
                if (AllKeyValuesBlank(nestedKeyValues)) return null;
                var resolvedId = ResolveByAlternateKeyCached(targetRepo, keyColumn.RelatedTable, nestedKeyValues);
                if (!resolvedId.HasValue) throw new Exception($"No match found in '{keyColumn.RelatedTable}' for keys: {FormatKeyValues(nestedKeyValues)}");
                return resolvedId.Value;
            }

            if (string.IsNullOrWhiteSpace(cellValue)) return null;
            return Guid.Parse(cellValue);
        }

        private bool CanResolveLookupFromKeyColumns(ExcelColumnConfig column, Dictionary<string, List<ExcelColumnConfig>> altKeyGroups)
        {
            var isLookupColumn = string.Equals(column?.Type, "Lookup", StringComparison.OrdinalIgnoreCase);
            var isLookupKeyField = string.Equals(column?.Type, "LookupKeyField", StringComparison.OrdinalIgnoreCase)
                && string.Equals(column?.KeyFieldType, "Lookup", StringComparison.OrdinalIgnoreCase);

            return column != null
                && (isLookupColumn || isLookupKeyField)
                && !string.Equals(column.Resolution, "Guid", StringComparison.OrdinalIgnoreCase)
                && altKeyGroups.ContainsKey(column.LogicalName);
        }

        private Guid? ResolveByAlternateKeyCached(CrmRepo targetRepo, string logicalName, Dictionary<string, object> keyValues)
        {
            var cacheKey = BuildResolutionCacheKey(logicalName, keyValues);
            if (_lookupResolutionCache != null && _lookupResolutionCache.TryGetValue(cacheKey, out var cached))
                return cached;

            var resolvedId = targetRepo.ResolveByAlternateKey(logicalName, keyValues);
            if (_lookupResolutionCache != null)
                _lookupResolutionCache[cacheKey] = resolvedId;

            return resolvedId;
        }

        private Guid? FindByFieldValuesCached(CrmRepo targetRepo, string logicalName, Dictionary<string, object> keyValues)
        {
            var cacheKey = BuildResolutionCacheKey(logicalName, keyValues);
            if (_matchKeyResolutionCache != null && _matchKeyResolutionCache.TryGetValue(cacheKey, out var cached))
                return cached;

            var target = targetRepo.FindByFieldValues(logicalName, keyValues);
            var resolvedId = target?.Id;
            if (_matchKeyResolutionCache != null)
                _matchKeyResolutionCache[cacheKey] = resolvedId;

            return resolvedId;
        }

        private string BuildResolutionCacheKey(string logicalName, Dictionary<string, object> keyValues)
        {
            return $"{logicalName}|{string.Join("|", keyValues.OrderBy(kv => kv.Key, StringComparer.OrdinalIgnoreCase).Select(kv => $"{kv.Key}={FormatCacheValue(kv.Value)}"))}";
        }

        private string FormatCacheValue(object value)
        {
            if (value == null) return "<null>";
            if (value is EntityReference reference) return reference.Id.ToString("D");
            if (value is OptionSetValue option) return option.Value.ToString(CultureInfo.InvariantCulture);
            if (value is Money money) return money.Value.ToString(CultureInfo.InvariantCulture);
            if (value is IFormattable formattable) return formattable.ToString(null, CultureInfo.InvariantCulture);
            return value.ToString();
        }

        private bool AllKeyValuesBlank(Dictionary<string, object> keyValues)
        {
            return keyValues == null
                || keyValues.Count == 0
                || keyValues.Values.All(v => v == null || string.IsNullOrWhiteSpace(v.ToString()));
        }

        private OptionConfig MatchOptionLabel(ExcelColumnConfig column, string label)
        {
            var matches = column.Options?
                .Where(o => o.Label.Equals(label, StringComparison.OrdinalIgnoreCase))
                .ToList() ?? new List<OptionConfig>();

            if (matches.Count == 0) throw new Exception($"Option label '{label}' not found.");
            if (matches.Count > 1) throw new Exception($"Option label '{label}' is ambiguous. Export/import this column using option values.");
            return matches[0];
        }

        private string FormatKeyValues(Dictionary<string, object> keyValues)
        {
            return string.Join(", ", keyValues.Select(kv => $"{kv.Key}='{kv.Value}'"));
        }

        private Enums.AttributeType MapToAttributeType(string type)
        {
            switch (type)
            {
                case "Guid":        return Enums.AttributeType.Identifier;
                case "Lookup":      return Enums.AttributeType.EntityReference;
                case "OptionSet":   return Enums.AttributeType.OptionSet;
                case "MultiOptionSet": return Enums.AttributeType.MultiOptionSet;
                default:            return Enums.AttributeType.Standard;
            }
        }

        #endregion Import
    }
}
