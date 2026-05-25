// System
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Xml;

// Microsoft
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Metadata;

// DataMigrationTool
using Dataverse.XrmTools.DataMigrationTool.Models;
using Dataverse.XrmTools.DataMigrationTool.Repositories;

namespace Dataverse.XrmTools.DataMigrationTool.Logic
{
    public static class SqliteDataLogic
    {
        // ─── Pull ──────────────────────────────────────────────────────────────

        public static DmtSnapshot Pull(
            SqliteProjectService project,
            IOrganizationService sourceClient,
            string tableLogicalName,
            string primaryIdAttr,
            string sourceEnvId,
            DataTableConfig config,
            string snapshotName,
            AttributeMetadata[] attributeMetadata,
            BackgroundWorker worker)
        {
            // Build selected column list — always include primary id
            var columns = (config.SelectedAttributes?.Any() == true
                ? config.SelectedAttributes.ToList()
                : config.AllColumns.Select(c => c.LogicalName).ToList());
            if (!columns.Contains(primaryIdAttr, StringComparer.OrdinalIgnoreCase))
                columns.Add(primaryIdAttr);

            worker?.ReportProgress(5, $"Pulling {tableLogicalName} records from Dataverse...");
            var fetch = BuildFetchXml(tableLogicalName, columns, config.Filter);
            var repo = new CrmRepo(sourceClient, worker);
            var entities = repo.GetCollectionByFetchXml(fetch, config.BatchSize > 0 ? config.BatchSize : 25);

            worker?.ReportProgress(60, $"Converting {entities?.Entities.Count ?? 0} records...");

            // Freeze column config for this snapshot (only selected cols)
            var colsInSnapshot = config.AllColumns
                .Where(c => columns.Contains(c.LogicalName, StringComparer.OrdinalIgnoreCase))
                .ToList();

            var rows = entities?.Entities.Select(e => EntityToRow(e, colsInSnapshot)).ToList()
                       ?? new List<Dictionary<string, object>>();

            // Resolve or create snapshot row
            var existing = project.GetSnapshot(snapshotName);
            string tableSuffix;
            DmtSnapshot snapshot;
            if (existing != null)
            {
                snapshot = existing;
                tableSuffix = existing.TableSuffix;
                snapshot.UpdatedOn = DateTime.UtcNow;
                snapshot.RowCount = rows.Count;
                snapshot.ColumnConfig = colsInSnapshot;
                snapshot.LoadMatchKeyMode = config.LoadMatchKeyMode;
                snapshot.LoadMatchKeyFields = config.LoadMatchKeyFields ?? new List<string>();
            }
            else
            {
                tableSuffix = ReserveUniqueSuffix(project, snapshotName);
                snapshot = new DmtSnapshot
                {
                    Name = snapshotName,
                    TableSuffix = tableSuffix,
                    TableLogicalName = tableLogicalName,
                    SourceEnvId = sourceEnvId,
                    Source = "Pull",
                    RowCount = rows.Count,
                    LoadMatchKeyMode = config.LoadMatchKeyMode ?? "Guid",
                    LoadMatchKeyFields = config.LoadMatchKeyFields ?? new List<string>(),
                    ColumnConfig = colsInSnapshot
                };
            }

            worker?.ReportProgress(70, $"Writing {rows.Count} rows to project snapshot...");
            project.SaveSnapshot(snapshot);
            project.CreateSnapshotTable(tableSuffix, colsInSnapshot);
            project.WriteSnapshotRecords(tableSuffix, rows, colsInSnapshot);

            worker?.ReportProgress(85, "Saving option-set labels...");
            if (attributeMetadata != null)
                SaveOptionSetValues(project, tableLogicalName, attributeMetadata);

            worker?.ReportProgress(100, "Pull complete.");
            return snapshot;
        }

        // ─── Helpers ───────────────────────────────────────────────────────────

        private static string ReserveUniqueSuffix(SqliteProjectService project, string name)
        {
            var candidate = SqliteProjectService.SanitizeSnapshotName(name);
            if (!project.HasSnapshot(candidate)) return candidate;

            for (var i = 2; i < 1000; i++)
            {
                var numbered = $"{candidate}_{i}";
                if (!project.HasSnapshot(numbered)) return numbered;
            }
            return $"{candidate}_{DateTime.UtcNow:yyyyMMddHHmmss}";
        }

        private static Dictionary<string, object> EntityToRow(Entity entity, List<DataTableColumnConfig> columns)
        {
            var row = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
            foreach (var col in columns)
            {
                if (!entity.Attributes.TryGetValue(col.LogicalName, out var value))
                {
                    row[col.LogicalName] = null;
                    continue;
                }
                row[col.LogicalName] = ConvertValue(value, col);
                if (value is EntityReference er && er.Name != null)
                    row[col.LogicalName + "_name"] = er.Name;
            }
            return row;
        }

        private static object ConvertValue(object value, DataTableColumnConfig col)
        {
            if (value == null) return null;

            switch (value)
            {
                case string s: return s;
                case int i: return (long)i;
                case long l: return l;
                case bool b: return b ? 1L : 0L;
                case decimal d: return (double)d;
                case double db: return db;
                case Money m: return (double)m.Value;
                case DateTime dt: return dt.ToString("O");
                case Guid g: return g.ToString("D");
                case OptionSetValue osv: return (long)osv.Value;
                case OptionSetValueCollection osvc:
                    return string.Join(",", osvc.Select(v => v.Value));
                case EntityReference er: return er.Id.ToString("D");
                default: return value.ToString();
            }
        }

        private static void SaveOptionSetValues(SqliteProjectService project, string tableLogicalName, AttributeMetadata[] attrs)
        {
            foreach (var attr in attrs)
            {
                List<OptionConfig> options = null;

                if (attr is PicklistAttributeMetadata picklist && picklist.OptionSet?.Options != null)
                {
                    options = picklist.OptionSet.Options
                        .Where(o => o.Value.HasValue)
                        .Select(o => new OptionConfig { Value = o.Value.Value, Label = o.Label?.UserLocalizedLabel?.Label })
                        .ToList();
                }
                else if (attr is MultiSelectPicklistAttributeMetadata ms && ms.OptionSet?.Options != null)
                {
                    options = ms.OptionSet.Options
                        .Where(o => o.Value.HasValue)
                        .Select(o => new OptionConfig { Value = o.Value.Value, Label = o.Label?.UserLocalizedLabel?.Label })
                        .ToList();
                }
                else if (attr is BooleanAttributeMetadata boolAttr && boolAttr.OptionSet != null)
                {
                    options = new List<OptionConfig>();
                    if (boolAttr.OptionSet.TrueOption?.Value.HasValue == true)
                        options.Add(new OptionConfig { Value = 1, Label = boolAttr.OptionSet.TrueOption.Label?.UserLocalizedLabel?.Label });
                    if (boolAttr.OptionSet.FalseOption?.Value.HasValue == true)
                        options.Add(new OptionConfig { Value = 0, Label = boolAttr.OptionSet.FalseOption.Label?.UserLocalizedLabel?.Label });
                }
                else if (attr is StatusAttributeMetadata status && status.OptionSet?.Options != null)
                {
                    options = status.OptionSet.Options
                        .Where(o => o.Value.HasValue)
                        .Select(o => new OptionConfig { Value = o.Value.Value, Label = o.Label?.UserLocalizedLabel?.Label })
                        .ToList();
                }
                else if (attr is StateAttributeMetadata state && state.OptionSet?.Options != null)
                {
                    options = state.OptionSet.Options
                        .Where(o => o.Value.HasValue)
                        .Select(o => new OptionConfig { Value = o.Value.Value, Label = o.Label?.UserLocalizedLabel?.Label })
                        .ToList();
                }

                if (options?.Any() == true)
                    project.SaveOptionSetValues(tableLogicalName, attr.LogicalName, options);
            }
        }

        private static string BuildFetchXml(string logicalName, List<string> columns, string filterXml)
        {
            var doc = new XmlDocument();
            var root = doc.CreateElement("fetch");
            doc.AppendChild(root);

            var entity = doc.CreateElement("entity");
            entity.SetAttribute("name", logicalName);
            root.AppendChild(entity);

            foreach (var col in columns)
            {
                var attr = doc.CreateElement("attribute");
                attr.SetAttribute("name", col);
                entity.AppendChild(attr);
            }

            if (!string.IsNullOrWhiteSpace(filterXml))
            {
                try
                {
                    var filterDoc = new XmlDocument();
                    filterDoc.LoadXml($"<root>{filterXml}</root>");
                    foreach (XmlNode node in filterDoc.DocumentElement.ChildNodes)
                    {
                        var imported = doc.ImportNode(node, true);
                        entity.AppendChild(imported);
                    }
                }
                catch { /* ignore malformed filter */ }
            }

            return doc.OuterXml;
        }
    }
}
