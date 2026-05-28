// System
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;

// ClosedXML
using ClosedXML.Excel;

// Microsoft
using Microsoft.Xrm.Sdk;

// Newtonsoft
using Newtonsoft.Json;

// DataMigrationTool
using Dataverse.XrmTools.DataMigrationTool.Enums;
using Dataverse.XrmTools.DataMigrationTool.Models;
using Dataverse.XrmTools.DataMigrationTool.Repositories;

namespace Dataverse.XrmTools.DataMigrationTool.Logic
{
    public static class SqliteFileAdapter
    {
        private const string DataSheetName = "Data";
        private const string MetaSheetName = "_dmt";

        private static readonly JsonSerializerSettings _json = new JsonSerializerSettings
        {
            NullValueHandling = NullValueHandling.Ignore
        };

        // ─── JSON ──────────────────────────────────────────────────────────────

        public static DmtSnapshot LoadFromJson(
            SqliteProjectService project,
            string filePath,
            string snapshotName,
            string sourceEnvId,
            DataTableConfig config,
            BackgroundWorker worker)
        {
            return LoadFromJson(project, filePath, snapshotName, sourceEnvId, config, null, worker);
        }

        public static DmtSnapshot LoadFromJson(
            SqliteProjectService project,
            string filePath,
            string snapshotName,
            string sourceEnvId,
            DataTableConfig config,
            IOrganizationService sourceClient,
            BackgroundWorker worker)
        {
            return LoadFromJson(project, filePath, snapshotName, sourceEnvId, config, sourceClient, worker, null);
        }

        public static DmtSnapshot LoadFromJson(
            SqliteProjectService project,
            string filePath,
            string snapshotName,
            string sourceEnvId,
            DataTableConfig config,
            IOrganizationService sourceClient,
            BackgroundWorker worker,
            string sourceFilePath)
        {
            worker?.ReportProgress(5, "Reading JSON file...");
            var json = File.ReadAllText(filePath);
            var collection = JsonConvert.DeserializeObject<RecordCollection>(json, _json);
            if (collection == null) throw new InvalidOperationException("JSON file is empty or invalid.");

            var tableLogicalName = collection.LogicalName;
            var primaryIdAttr = collection.PrimaryIdAttribute;

            // Use stored column config when available; fall back to attribute keys from first record
            var columns = config?.AllColumns?.Any() == true
                ? config.AllColumns
                : BuildColumnsFromCollection(collection, primaryIdAttr);

            worker?.ReportProgress(20, $"Converting {collection.Count} records...");
            var rows = ConvertJsonCollection(collection, columns, primaryIdAttr);
            ResolveSourceIdentity(rows, columns, tableLogicalName, primaryIdAttr, config, sourceClient, worker);

            return WriteSnapshot(project, rows, columns, snapshotName, tableLogicalName,
                primaryIdAttr, sourceEnvId, "JSON", config, worker, sourceFilePath);
        }

        // ─── Excel ─────────────────────────────────────────────────────────────

        public static DmtSnapshot LoadFromExcel(
            SqliteProjectService project,
            string filePath,
            string snapshotName,
            string sourceEnvId,
            DataTableConfig config,
            BackgroundWorker worker)
        {
            return LoadFromExcel(project, filePath, snapshotName, sourceEnvId, config, null, worker);
        }

        public static DmtSnapshot LoadFromExcel(
            SqliteProjectService project,
            string filePath,
            string snapshotName,
            string sourceEnvId,
            DataTableConfig config,
            IOrganizationService sourceClient,
            BackgroundWorker worker)
        {
            return LoadFromExcel(project, filePath, snapshotName, sourceEnvId, config, sourceClient, worker, null);
        }

        public static DmtSnapshot LoadFromExcel(
            SqliteProjectService project,
            string filePath,
            string snapshotName,
            string sourceEnvId,
            DataTableConfig config,
            IOrganizationService sourceClient,
            BackgroundWorker worker,
            string sourceFilePath)
        {
            worker?.ReportProgress(5, "Reading Excel file...");
            using (var wb = new XLWorkbook(filePath))
            {
                if (!wb.Worksheets.Contains(MetaSheetName))
                    throw new InvalidOperationException("This Excel file was not exported from Data Migration Tool — '_dmt' metadata sheet is missing.");

                var metaSheet = wb.Worksheet(MetaSheetName);
                var metaJson = metaSheet.Cell("A1").GetString();
                var excelConfig = JsonConvert.DeserializeObject<ExcelExportConfig>(metaJson, _json)
                    ?? throw new InvalidOperationException("Excel metadata sheet is empty or corrupt.");

                var tableLogicalName = excelConfig.Table?.LogicalName
                    ?? throw new InvalidOperationException("Excel metadata is missing table information.");
                var primaryIdAttr = excelConfig.Table.PrimaryIdAttribute;
                var effectiveConfig = BuildConfigFromExcel(config, excelConfig);

                var columns = BuildColumnsFromExcelConfig(excelConfig);

                if (!wb.Worksheets.Contains(DataSheetName))
                    throw new InvalidOperationException("Data sheet 'Data' not found in the Excel file.");

                var dataSheet = wb.Worksheet(DataSheetName);
                worker?.ReportProgress(20, "Parsing Excel rows...");
                var rows = ReadExcelRows(dataSheet, excelConfig, columns, primaryIdAttr, worker);
                ResolveSourceIdentity(rows, columns, tableLogicalName, primaryIdAttr, effectiveConfig, sourceClient, worker);

                var snapshot = WriteSnapshot(project, rows, columns, snapshotName, tableLogicalName,
                    primaryIdAttr, sourceEnvId, "Excel", effectiveConfig, worker, sourceFilePath);
                SaveExcelOptionSetValues(project, tableLogicalName, excelConfig);
                return snapshot;
            }
        }

        // ─── Shared write ──────────────────────────────────────────────────────

        private static DmtSnapshot WriteSnapshot(
            SqliteProjectService project,
            List<Dictionary<string, object>> rows,
            List<DataTableColumnConfig> columns,
            string snapshotName,
            string tableLogicalName,
            string primaryIdAttr,
            string sourceEnvId,
            string source,
            DataTableConfig config,
            BackgroundWorker worker,
            string sourceFilePath = null)
        {
            var existing = project.GetSnapshot(snapshotName);
            string tableSuffix;
            DmtSnapshot snapshot;

            if (existing != null)
            {
                snapshot = existing;
                tableSuffix = existing.TableSuffix;
                snapshot.UpdatedOn = DateTime.UtcNow;
                snapshot.RowCount = rows.Count;
                snapshot.ColumnConfig = columns;
                snapshot.Source = source;
                snapshot.SourceEnvId = sourceEnvId;
                snapshot.SourceFilePath = sourceFilePath ?? snapshot.SourceFilePath;
                snapshot.PrimaryIdAttribute = primaryIdAttr;
                snapshot.LoadMatchKeyMode = config?.LoadMatchKeyMode ?? snapshot.LoadMatchKeyMode ?? "Guid";
                snapshot.LoadMatchKeyFields = config?.LoadMatchKeyFields ?? snapshot.LoadMatchKeyFields ?? new List<string>();
            }
            else
            {
                tableSuffix = project.ResolveTableSuffix(snapshotName);
                snapshot = new DmtSnapshot
                {
                    Name = snapshotName,
                    TableSuffix = tableSuffix,
                    TableLogicalName = tableLogicalName,
                    SourceEnvId = sourceEnvId,
                    Source = source,
                    SourceFilePath = sourceFilePath,
                    PrimaryIdAttribute = primaryIdAttr,
                    RowCount = rows.Count,
                    LoadMatchKeyMode = config?.LoadMatchKeyMode ?? "Guid",
                    LoadMatchKeyFields = config?.LoadMatchKeyFields ?? new List<string>(),
                    ColumnConfig = columns
                };
            }

            worker?.ReportProgress(70, $"Writing {rows.Count} rows to project snapshot...");
            snapshot.TableSuffix = tableSuffix;
            project.ReplaceSnapshotData(snapshot, columns, rows);

            worker?.ReportProgress(100, "Load complete.");
            return snapshot;
        }

        // ─── JSON conversion ───────────────────────────────────────────────────

        public static void ExportToJson(
            SqliteProjectService project,
            string snapshotName,
            string filePath,
            BackgroundWorker worker = null)
        {
            var snapshot = project.GetSnapshot(snapshotName)
                ?? throw new InvalidOperationException($"Snapshot '{snapshotName}' not found.");
            var primaryIdAttr = GetPrimaryIdAttribute(project, snapshot);
            var rows = project.ReadSnapshotRecords(snapshot.TableSuffix, snapshot.ColumnConfig).ToList();

            worker?.ReportProgress(20, $"Exporting {rows.Count} rows to JSON...");
            var collection = new RecordCollection
            {
                LogicalName = snapshot.TableLogicalName,
                PrimaryIdAttribute = primaryIdAttr,
                Count = rows.Count,
                ImportMatchKeyMode = snapshot.LoadMatchKeyMode,
                ImportMatchKeys = snapshot.LoadMatchKeyFields != null ? new List<string>(snapshot.LoadMatchKeyFields) : new List<string>(),
                Records = rows.Select((row, index) => new Record
                {
                    SourceRowNumber = index + 1,
                    Attributes = snapshot.ColumnConfig.Select(col => new RecordAttribute
                    {
                        Key = col.LogicalName,
                        Type = ToRecordAttributeType(col),
                        Value = row.TryGetValue(col.LogicalName, out var value) ? value : null
                    }).ToList()
                }).ToList()
            };

            File.WriteAllText(filePath, JsonConvert.SerializeObject(collection, Formatting.Indented, _json));
            worker?.ReportProgress(100, "JSON export complete.");
        }

        public static void ExportToExcel(
            SqliteProjectService project,
            string snapshotName,
            string filePath,
            BackgroundWorker worker = null)
        {
            var snapshot = project.GetSnapshot(snapshotName)
                ?? throw new InvalidOperationException($"Snapshot '{snapshotName}' not found.");
            var primaryIdAttr = GetPrimaryIdAttribute(project, snapshot);
            var rows = project.ReadSnapshotRecords(snapshot.TableSuffix, snapshot.ColumnConfig).ToList();
            var excelConfig = BuildExcelExportConfig(snapshot, primaryIdAttr);

            worker?.ReportProgress(20, $"Exporting {rows.Count} rows to Excel...");
            using (var wb = new XLWorkbook())
            {
                var meta = wb.AddWorksheet(MetaSheetName);
                meta.Cell("A1").SetValue(JsonConvert.SerializeObject(excelConfig, _json));
                meta.Visibility = XLWorksheetVisibility.Hidden;

                var data = wb.AddWorksheet(DataSheetName);
                for (var i = 0; i < snapshot.ColumnConfig.Count; i++)
                {
                    var col = snapshot.ColumnConfig[i];
                    data.Cell(1, i + 1).SetValue(col.LogicalName);
                    data.Cell(2, i + 1).SetValue(string.IsNullOrWhiteSpace(col.DisplayName) ? col.LogicalName : $"({col.DisplayName})");
                }

                for (var r = 0; r < rows.Count; r++)
                {
                    var row = rows[r];
                    for (var c = 0; c < snapshot.ColumnConfig.Count; c++)
                    {
                        var col = snapshot.ColumnConfig[c];
                        row.TryGetValue(col.LogicalName, out var value);
                        data.Cell(r + 3, c + 1).SetValue(value?.ToString() ?? string.Empty);
                    }
                }

                wb.SaveAs(filePath);
            }
            worker?.ReportProgress(100, "Excel export complete.");
        }

        private static List<Dictionary<string, object>> ConvertJsonCollection(
            RecordCollection collection,
            List<DataTableColumnConfig> columns,
            string primaryIdAttr)
        {
            var colIndex = columns.ToDictionary(c => c.LogicalName, c => c, StringComparer.OrdinalIgnoreCase);
            var rows = new List<Dictionary<string, object>>();

            foreach (var record in collection.Records ?? Enumerable.Empty<Record>())
            {
                var row = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
                var attrs = record.Attributes?.ToDictionary(a => a.Key, a => a, StringComparer.OrdinalIgnoreCase)
                            ?? new Dictionary<string, RecordAttribute>();

                string sourceId = null;
                if (attrs.TryGetValue(primaryIdAttr, out var idAttr) && idAttr.Value != null)
                    sourceId = idAttr.Value.ToString();

                foreach (var col in columns)
                {
                    if (!attrs.TryGetValue(col.LogicalName, out var ra))
                    {
                        row[col.LogicalName] = null;
                        continue;
                    }

                    row[col.LogicalName] = ConvertJsonAttributeValue(ra, col);
                }

                // Set _source_id from primary id attribute
                if (!string.IsNullOrEmpty(sourceId))
                    row["_source_id"] = sourceId;

                rows.Add(row);
            }

            return rows;
        }

        private static object ConvertJsonAttributeValue(RecordAttribute ra, DataTableColumnConfig col)
        {
            if (ra.Value == null) return null;

            var raw = ra.Value.ToString();
            if (string.IsNullOrEmpty(raw)) return null;

            switch (col.SqliteType)
            {
                case "INTEGER":
                    if (long.TryParse(raw, out var lv)) return lv;
                    return null;
                case "REAL":
                    if (double.TryParse(raw, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out var dv)) return dv;
                    return null;
                default:
                    return raw;
            }
        }

        private static List<DataTableColumnConfig> BuildColumnsFromCollection(RecordCollection collection, string primaryIdAttr)
        {
            var firstRecord = collection.Records?.FirstOrDefault();
            if (firstRecord?.Attributes == null) return new List<DataTableColumnConfig>();

            return firstRecord.Attributes.Select(a => new DataTableColumnConfig
            {
                LogicalName = a.Key,
                DisplayName = a.Key,
                Type = a.Type.ToString(),
                SqliteType = a.Type == AttributeType.OptionSet || a.Type == AttributeType.MultiOptionSet ? "INTEGER" : "TEXT"
            }).ToList();
        }

        // ─── Excel conversion ──────────────────────────────────────────────────

        private static List<Dictionary<string, object>> ReadExcelRows(
            IXLWorksheet sheet,
            ExcelExportConfig excelConfig,
            List<DataTableColumnConfig> columns,
            string primaryIdAttr,
            BackgroundWorker worker)
        {
            var lastRow = sheet.LastRowUsed()?.RowNumber() ?? 2;

            // Build header column index: logicalName → column number (1-based)
            var headerMap = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            var headerRow = sheet.Row(1);
            foreach (var cell in headerRow.CellsUsed())
                headerMap[cell.GetString()] = cell.Address.ColumnNumber;

            var firstDataRow = DetectFirstDataRow(sheet, headerMap, primaryIdAttr);
            var colIndex = columns.ToDictionary(c => c.LogicalName, c => c, StringComparer.OrdinalIgnoreCase);
            var excelColumns = (excelConfig.Columns ?? new List<ExcelColumnConfig>())
                .Where(c => !string.IsNullOrWhiteSpace(c.LogicalName))
                .GroupBy(c => c.LogicalName, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);
            var rows = new List<Dictionary<string, object>>();
            var total = Math.Max(0, lastRow - firstDataRow + 1);

            for (var r = firstDataRow; r <= lastRow; r++)
            {
                if ((r - firstDataRow) % 25 == 0)
                    worker?.ReportProgress(20 + (int)(40.0 * (r - firstDataRow) / Math.Max(1, total)),
                        $"Parsing row {r - firstDataRow + 1} of {total}...");

                var row = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
                string sourceId = null;

                foreach (var col in columns)
                {
                    if (!headerMap.TryGetValue(col.LogicalName, out var colNum))
                    {
                        row[col.LogicalName] = null;
                        continue;
                    }
                    var cellVal = sheet.Cell(r, colNum).GetString();
                    excelColumns.TryGetValue(col.LogicalName, out var excelColumn);
                    row[col.LogicalName] = ParseCellValue(cellVal, col, excelColumn);

                    if (string.Equals(col.LogicalName, primaryIdAttr, StringComparison.OrdinalIgnoreCase)
                        && !string.IsNullOrWhiteSpace(cellVal))
                        sourceId = cellVal;
                }

                foreach (var helper in (excelConfig.Columns ?? new List<ExcelColumnConfig>())
                    .Where(c => c.Type == "LookupKeyField"))
                {
                    if (headerMap.TryGetValue(helper.LogicalName, out var helperColNum))
                        row[helper.LogicalName] = sheet.Cell(r, helperColNum).GetString();
                }

                if (!string.IsNullOrEmpty(sourceId))
                    row["_source_id"] = sourceId;

                rows.Add(row);
            }

            return rows;
        }

        private static int DetectFirstDataRow(IXLWorksheet sheet, Dictionary<string, int> headerMap, string primaryIdAttr)
        {
            if (!string.IsNullOrEmpty(primaryIdAttr) && headerMap.TryGetValue(primaryIdAttr, out var idCol))
            {
                var row2Value = sheet.Cell(2, idCol).GetString();
                if (Guid.TryParse(row2Value, out _)) return 2;
            }
            return 3;
        }

        private static object ParseCellValue(string raw, DataTableColumnConfig col, ExcelColumnConfig excelColumn = null)
        {
            if (string.IsNullOrWhiteSpace(raw)) return null;

            if (excelColumn?.Options?.Any() == true
                && (string.Equals(excelColumn.ExportMode, "Label", StringComparison.OrdinalIgnoreCase)
                    || !long.TryParse(raw, out _)))
            {
                if (col.IsMultiSelect || string.Equals(col.Type, "MultiSelectPicklist", StringComparison.OrdinalIgnoreCase))
                {
                    var values = raw.Split(new[] { ',', '|' }, StringSplitOptions.RemoveEmptyEntries)
                        .Select(label => FindOptionValue(excelColumn.Options, label.Trim()))
                        .Where(value => value.HasValue)
                        .Select(value => value.Value.ToString());
                    return string.Join(",", values);
                }

                var optionValue = FindOptionValue(excelColumn.Options, raw.Trim());
                if (optionValue.HasValue) return (long)optionValue.Value;
            }

            switch (col.SqliteType)
            {
                case "INTEGER":
                    if (long.TryParse(raw, out var lv)) return lv;
                    return null;
                case "REAL":
                    if (double.TryParse(raw, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out var dv)) return dv;
                    return null;
                default:
                    return raw;
            }
        }

        private static List<DataTableColumnConfig> BuildColumnsFromExcelConfig(ExcelExportConfig config)
        {
            var cols = new List<DataTableColumnConfig>();
            foreach (var c in config.Columns ?? new List<ExcelColumnConfig>())
            {
                if (c.Type == "LookupKeyField") continue; // helper col — skip

                var typeCode = c.Type;
                cols.Add(new DataTableColumnConfig
                {
                    LogicalName = c.LogicalName,
                    DisplayName = c.DisplayName ?? c.LogicalName,
                    Type = typeCode,
                    SqliteType = SqliteProjectService.GetSqliteType(typeCode),
                    RelatedTable = c.RelatedTable,
                    Resolution = c.Resolution,
                    AlternateKeyFields = c.AlternateKeyFields != null ? new List<string>(c.AlternateKeyFields) : new List<string>(),
                    IsMultiSelect = c.Type == "MultiSelectPicklist"
                });
            }
            return cols;
        }

        private static void ResolveSourceIdentity(
            List<Dictionary<string, object>> rows,
            List<DataTableColumnConfig> columns,
            string tableLogicalName,
            string primaryIdAttr,
            DataTableConfig config,
            IOrganizationService sourceClient,
            BackgroundWorker worker)
        {
            if (rows == null) return;

            var repo = sourceClient == null ? null : new CrmRepo(sourceClient, worker);
            for (var i = 0; i < rows.Count; i++)
            {
                var row = rows[i];
                ResolveLookupValues(row, columns, tableLogicalName, repo);

                var sourceId = ResolveMainSourceId(row, tableLogicalName, primaryIdAttr, config, repo);
                if (string.IsNullOrWhiteSpace(sourceId))
                {
                    sourceId = Guid.NewGuid().ToString("D");
                    row["_is_new"] = true;
                }
                row["_source_id"] = sourceId;
            }
        }

        private static string ResolveMainSourceId(
            Dictionary<string, object> row,
            string tableLogicalName,
            string primaryIdAttr,
            DataTableConfig config,
            CrmRepo repo)
        {
            var mode = config?.LoadMatchKeyMode ?? "Guid";
            if (string.Equals(mode, "Guid", StringComparison.OrdinalIgnoreCase))
            {
                var candidate = GetValue(row, primaryIdAttr) ?? GetValue(row, "_source_id");
                return Guid.TryParse(candidate, out var id) ? id.ToString("D") : null;
            }

            var fields = config?.LoadMatchKeyFields?.Where(f => !string.IsNullOrWhiteSpace(f)).ToList()
                ?? new List<string>();
            if (!fields.Any() || repo == null) return null;

            var keyValues = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
            foreach (var field in fields)
            {
                if (row.TryGetValue(field, out var value) && value != null && !string.IsNullOrWhiteSpace(value.ToString()))
                    keyValues[field] = value;
            }

            if (!keyValues.Any()) return null;
            var found = repo.FindByFieldValues(tableLogicalName, keyValues);
            return found?.Id.ToString("D");
        }

        private static void ResolveLookupValues(
            Dictionary<string, object> row,
            List<DataTableColumnConfig> columns,
            string tableLogicalName,
            CrmRepo repo)
        {
            if (repo == null) return;

            foreach (var col in columns.Where(c => c.Type == "Lookup" || c.Type == "Owner" || c.Type == "Customer"))
            {
                if (!row.TryGetValue(col.LogicalName, out var raw) || raw == null || string.IsNullOrWhiteSpace(raw.ToString()))
                    continue;

                if (Guid.TryParse(raw.ToString(), out var lookupId))
                {
                    row[col.LogicalName] = lookupId.ToString("D");
                    continue;
                }

                var fields = col.AlternateKeyFields?.Where(f => !string.IsNullOrWhiteSpace(f)).ToList()
                    ?? new List<string>();
                if (!fields.Any() && string.Equals(col.Resolution, "Name", StringComparison.OrdinalIgnoreCase))
                    fields.Add("name");
                if (!fields.Any()) continue;

                var keyValues = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
                if (fields.Count == 1 && !row.ContainsKey(fields[0]))
                {
                    keyValues[fields[0]] = raw.ToString();
                }
                else
                {
                    foreach (var field in fields)
                    {
                        if (row.TryGetValue(field, out var value) && value != null && !string.IsNullOrWhiteSpace(value.ToString()))
                            keyValues[field] = value;
                    }
                }

                if (!keyValues.Any()) continue;
                var found = repo.FindByFieldValues(col.RelatedTable ?? tableLogicalName, keyValues);
                if (found != null)
                    row[col.LogicalName] = found.Id.ToString("D");
            }
        }

        private static string GetValue(Dictionary<string, object> row, string key)
        {
            return !string.IsNullOrWhiteSpace(key) && row.TryGetValue(key, out var value) ? value?.ToString() : null;
        }

        private static int? FindOptionValue(List<OptionConfig> options, string label)
        {
            if (options == null || string.IsNullOrWhiteSpace(label)) return null;
            var option = options.FirstOrDefault(o => string.Equals(o.Label, label, StringComparison.OrdinalIgnoreCase));
            return option?.Value;
        }

        private static void SaveExcelOptionSetValues(SqliteProjectService project, string tableLogicalName, ExcelExportConfig config)
        {
            foreach (var column in config?.Columns ?? new List<ExcelColumnConfig>())
            {
                if (column.Options?.Any() == true)
                    project.SaveOptionSetValues(tableLogicalName, column.LogicalName, column.Options);
            }
        }

        private static string GetPrimaryIdAttribute(SqliteProjectService project, DmtSnapshot snapshot)
        {
            var (_, _, primaryIdAttr, _) = project.GetTableConfig(snapshot.TableLogicalName);
            if (!string.IsNullOrWhiteSpace(primaryIdAttr)) return primaryIdAttr;

            return snapshot.ColumnConfig.FirstOrDefault(c =>
                    string.Equals(c.Type, "Uniqueidentifier", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(c.Type, "Guid", StringComparison.OrdinalIgnoreCase)
                    || c.LogicalName.EndsWith("id", StringComparison.OrdinalIgnoreCase))
                ?.LogicalName;
        }

        private static ExcelExportConfig BuildExcelExportConfig(DmtSnapshot snapshot, string primaryIdAttr)
        {
            return new ExcelExportConfig
            {
                MatchKeyMode = snapshot.LoadMatchKeyMode,
                MatchKeys = snapshot.LoadMatchKeyFields != null ? new List<string>(snapshot.LoadMatchKeyFields) : new List<string>(),
                Table = new ExcelTableConfig
                {
                    LogicalName = snapshot.TableLogicalName,
                    PrimaryIdAttribute = primaryIdAttr
                },
                Columns = snapshot.ColumnConfig.Select(c => new ExcelColumnConfig
                {
                    LogicalName = c.LogicalName,
                    DisplayName = c.DisplayName,
                    Type = ToExcelColumnType(c),
                    RelatedTable = c.RelatedTable,
                    Resolution = c.Resolution,
                    AlternateKeyFields = c.AlternateKeyFields != null ? new List<string>(c.AlternateKeyFields) : new List<string>()
                }).ToList()
            };
        }

        private static AttributeType ToRecordAttributeType(DataTableColumnConfig col)
        {
            var type = col.Type ?? string.Empty;
            if (string.Equals(type, "Uniqueidentifier", StringComparison.OrdinalIgnoreCase)
                || string.Equals(type, "Guid", StringComparison.OrdinalIgnoreCase))
                return AttributeType.Identifier;
            if (string.Equals(type, "Lookup", StringComparison.OrdinalIgnoreCase)
                || string.Equals(type, "Owner", StringComparison.OrdinalIgnoreCase)
                || string.Equals(type, "Customer", StringComparison.OrdinalIgnoreCase))
                return AttributeType.EntityReference;
            if (string.Equals(type, "Picklist", StringComparison.OrdinalIgnoreCase)
                || string.Equals(type, "OptionSet", StringComparison.OrdinalIgnoreCase)
                || string.Equals(type, "State", StringComparison.OrdinalIgnoreCase)
                || string.Equals(type, "Status", StringComparison.OrdinalIgnoreCase))
                return AttributeType.OptionSet;
            if (string.Equals(type, "MultiSelectPicklist", StringComparison.OrdinalIgnoreCase)
                || string.Equals(type, "MultiOptionSet", StringComparison.OrdinalIgnoreCase))
                return AttributeType.MultiOptionSet;
            return AttributeType.Standard;
        }

        private static string ToExcelColumnType(DataTableColumnConfig col)
        {
            var type = col.Type ?? "String";
            if (string.Equals(type, "Picklist", StringComparison.OrdinalIgnoreCase)) return "OptionSet";
            if (string.Equals(type, "MultiSelectPicklist", StringComparison.OrdinalIgnoreCase)) return "MultiOptionSet";
            if (string.Equals(type, "Uniqueidentifier", StringComparison.OrdinalIgnoreCase)) return "Guid";
            return type;
        }

        private static DataTableConfig BuildConfigFromExcel(DataTableConfig config, ExcelExportConfig excelConfig)
        {
            var effective = new DataTableConfig
            {
                Filter = config?.Filter,
                SelectedAttributes = config?.SelectedAttributes != null ? new List<string>(config.SelectedAttributes) : new List<string>(),
                BatchSize = config?.BatchSize > 0 ? config.BatchSize : 25,
                AllColumns = config?.AllColumns != null ? new List<DataTableColumnConfig>(config.AllColumns) : new List<DataTableColumnConfig>(),
                LoadMatchKeyMode = config?.LoadMatchKeyMode ?? "Guid",
                LoadMatchKeyFields = config?.LoadMatchKeyFields != null ? new List<string>(config.LoadMatchKeyFields) : new List<string>(),
                LoadMatchAlternateKeyName = config?.LoadMatchAlternateKeyName,
                PushMatchKeyMode = config?.PushMatchKeyMode,
                PushMatchKeyFields = config?.PushMatchKeyFields != null ? new List<string>(config.PushMatchKeyFields) : new List<string>(),
                PushMatchAlternateKeyName = config?.PushMatchAlternateKeyName,
                ExcelConfig = config?.ExcelConfig
            };
            var import = excelConfig?.ImportSettings;
            var mode = import?.MatchKeyMode ?? excelConfig?.MatchKeyMode;
            var fields = import?.MatchKeyFields ?? excelConfig?.MatchKeys;
            var alternateKey = import?.MatchAlternateKeyName ?? excelConfig?.MatchAlternateKeyName;

            if (!string.IsNullOrWhiteSpace(mode))
                effective.LoadMatchKeyMode = mode;
            if (fields != null)
                effective.LoadMatchKeyFields = new List<string>(fields);
            if (!string.IsNullOrWhiteSpace(alternateKey))
                effective.LoadMatchAlternateKeyName = alternateKey;

            return effective;
        }
    }
}
