// System
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;

// ClosedXML
using ClosedXML.Excel;

// Newtonsoft
using Newtonsoft.Json;

// DataMigrationTool
using Dataverse.XrmTools.DataMigrationTool.Enums;
using Dataverse.XrmTools.DataMigrationTool.Models;

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

            return WriteSnapshot(project, rows, columns, snapshotName, tableLogicalName,
                primaryIdAttr, sourceEnvId, "JSON", config, worker);
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

                var columns = BuildColumnsFromExcelConfig(excelConfig);

                if (!wb.Worksheets.Contains(DataSheetName))
                    throw new InvalidOperationException("Data sheet 'Data' not found in the Excel file.");

                var dataSheet = wb.Worksheet(DataSheetName);
                worker?.ReportProgress(20, "Parsing Excel rows...");
                var rows = ReadExcelRows(dataSheet, excelConfig, columns, primaryIdAttr, worker);

                return WriteSnapshot(project, rows, columns, snapshotName, tableLogicalName,
                    primaryIdAttr, sourceEnvId, "Excel", config, worker);
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
            BackgroundWorker worker)
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
            }
            else
            {
                tableSuffix = SqliteProjectService.SanitizeSnapshotName(snapshotName);
                snapshot = new DmtSnapshot
                {
                    Name = snapshotName,
                    TableSuffix = tableSuffix,
                    TableLogicalName = tableLogicalName,
                    SourceEnvId = sourceEnvId,
                    Source = source,
                    RowCount = rows.Count,
                    LoadMatchKeyMode = config?.LoadMatchKeyMode ?? "Guid",
                    LoadMatchKeyFields = config?.LoadMatchKeyFields ?? new List<string>(),
                    ColumnConfig = columns
                };
            }

            worker?.ReportProgress(70, $"Writing {rows.Count} rows to project snapshot...");
            project.SaveSnapshot(snapshot);
            project.CreateSnapshotTable(tableSuffix, columns);
            project.WriteSnapshotRecords(tableSuffix, rows, columns);

            worker?.ReportProgress(100, "Load complete.");
            return snapshot;
        }

        // ─── JSON conversion ───────────────────────────────────────────────────

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
                    row[col.LogicalName] = ParseCellValue(cellVal, col);

                    if (string.Equals(col.LogicalName, primaryIdAttr, StringComparison.OrdinalIgnoreCase)
                        && !string.IsNullOrWhiteSpace(cellVal))
                        sourceId = cellVal;
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

        private static object ParseCellValue(string raw, DataTableColumnConfig col)
        {
            if (string.IsNullOrWhiteSpace(raw)) return null;
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
                    IsMultiSelect = c.Type == "MultiSelectPicklist"
                });
            }
            return cols;
        }
    }
}
