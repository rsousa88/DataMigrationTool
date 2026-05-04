// System
using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Globalization;

// Microsoft
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Metadata;

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

        #region Export

        public void Export(ExcelExportConfig config, IEnumerable<Entity> records, string filePath)
        {
            using (var wb = new XLWorkbook())
            {
                var dataSheet = wb.Worksheets.Add(DataSheetName);
                var metaSheet = wb.Worksheets.Add(MetaSheetName);
                metaSheet.Visibility = XLWorksheetVisibility.Hidden;

                WriteMetadata(metaSheet, config);
                WriteHeaders(dataSheet, config);
                WriteHints(dataSheet, config);
                WriteData(dataSheet, config, records);

                // Hide the GUID column for lookups resolved via alt-key or custom — users only need the key field columns
                for (var i = 0; i < config.Columns.Count; i++)
                {
                    var col = config.Columns[i];
                    if (col.Type == "Lookup" && col.Resolution != "Guid")
                        dataSheet.Column(i + 1).Hide();
                }

                dataSheet.SheetView.FreezeRows(2);
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
                cell.Style.Fill.BackgroundColor = XLColor.FromHtml("#D9E1F2");
                col++;
            }
        }

        private void WriteHints(IXLWorksheet sheet, ExcelExportConfig config)
        {
            var col = 1;
            foreach (var column in config.Columns)
            {
                var cell = sheet.Cell(2, col);
                cell.Value = GetHintText(column);
                cell.Style.Font.Italic = true;
                cell.Style.Font.FontColor = XLColor.Gray;
                cell.Style.Fill.BackgroundColor = XLColor.FromHtml("#F2F2F2");
                col++;
            }
        }

        private string GetHintText(ExcelColumnConfig column)
        {
            switch (column.Type)
            {
                case "Lookup":
                    return string.IsNullOrEmpty(column.RelatedTable)
                        ? "Lookup (GUID)"
                        : $"Lookup → {column.RelatedTable} (GUID)";
                case "LookupKeyField":
                    return string.IsNullOrEmpty(column.RelatedTable)
                        ? $"Key for {column.OwnerAttribute}"
                        : $"Lookup → {column.RelatedTable}";
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

        private void WriteData(IXLWorksheet sheet, ExcelExportConfig config, IEnumerable<Entity> records)
        {
            if (records == null) return;

            var row = 3;
            foreach (var entity in records)
            {
                var col = 1;
                foreach (var column in config.Columns)
                {
                    var cell = sheet.Cell(row, col);
                    WriteCell(cell, column, entity);
                    col++;
                }
                row++;
            }
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

            var row = 3;
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
                if (!wb.Worksheets.Contains(MetaSheetName))
                    throw new Exception("This file was not exported from Data Migration Tool — metadata sheet '_dmt' is missing.");

                var metaSheet = wb.Worksheet(MetaSheetName);
                var json = metaSheet.Cell("A1").GetString();
                if (string.IsNullOrWhiteSpace(json))
                    throw new Exception("Metadata sheet '_dmt' is empty.");

                return JsonConvert.DeserializeObject<ExcelExportConfig>(json);
            }
        }

        public RecordCollection ImportFromExcel(string filePath, ExcelExportConfig config, IOrganizationService targetService)
        {
            using (var wb = new XLWorkbook(filePath))
            {
                if (!wb.Worksheets.Contains(DataSheetName))
                    throw new Exception("Data sheet 'Data' not found in the file.");

                var dataSheet = wb.Worksheet(DataSheetName);

                var records = new List<Record>();
                var lastRow = dataSheet.LastRowUsed()?.RowNumber() ?? 2;

                // Build column map: header name → column config
                var headerMap = BuildHeaderMap(dataSheet, config);

                // Group LookupKeyField columns by their owner attribute for resolution
                var altKeyGroups = config.Columns
                    .Where(c => c.Type == "LookupKeyField")
                    .GroupBy(c => c.OwnerAttribute)
                    .ToDictionary(g => g.Key, g => g.ToList());

                var targetRepo = targetService != null ? new CrmRepo(targetService) : null;

                for (var row = 3; row <= lastRow; row++)
                {
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
                        catch (Exception ex)
                        {
                            rowErrors.Add($"Row {row}, column '{column.LogicalName}': {ex.Message}");
                        }
                    }

                    if (rowErrors.Any())
                    {
                        // Log errors but skip row — surfaced in results
                        // TODO: expose errors through results view
                        continue;
                    }

                    records.Add(new Record { Attributes = attributes });
                }

                return new RecordCollection
                {
                    LogicalName = config.Table.LogicalName,
                    PrimaryIdAttribute = config.Table.PrimaryIdAttribute,
                    Records = records,
                    Count = records.Count
                };
            }
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
            if (string.IsNullOrWhiteSpace(cellValue)) return null;

            object value;
            var type = column.Type;

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
                        var match = column.Options?.FirstOrDefault(o => o.Label.Equals(cellValue, StringComparison.OrdinalIgnoreCase));
                        if (match == null) throw new Exception($"Option label '{cellValue}' not found.");
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
                            var match = column.Options?.FirstOrDefault(o => o.Label.Equals(part, StringComparison.OrdinalIgnoreCase));
                            if (match == null) throw new Exception($"Option label '{part}' not found.");
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
                    if (column.Resolution == "AlternateKey" && altKeyGroups.ContainsKey(column.LogicalName))
                    {
                        // Collect key field values from their dedicated columns
                        var keyValues = new Dictionary<string, string>();
                        foreach (var keyCol in altKeyGroups[column.LogicalName])
                        {
                            if (headerMap.TryGetValue(keyCol.LogicalName, out var keyColNum))
                            {
                                var keyVal = sheet.Cell(row, keyColNum).GetString();
                                var fieldName = keyCol.LogicalName.Substring(column.LogicalName.Length + 1);
                                keyValues[fieldName] = keyVal;
                            }
                        }

                        if (targetRepo == null) throw new Exception("Target connection required for alternate key resolution.");
                        var resolvedId = targetRepo.ResolveByAlternateKey(column.RelatedTable, keyValues);
                        if (!resolvedId.HasValue) throw new Exception($"No match found in '{column.RelatedTable}' for keys: {string.Join(", ", keyValues.Select(kv => $"{kv.Key}='{kv.Value}'"))}");

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
