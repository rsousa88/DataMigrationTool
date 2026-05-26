// System
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Xml;

// Microsoft
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Metadata;
using Microsoft.Xrm.Sdk.Query;

// DataMigrationTool
using Dataverse.XrmTools.DataMigrationTool.AppSettings;
using Dataverse.XrmTools.DataMigrationTool.Models;
using Dataverse.XrmTools.DataMigrationTool.Repositories;
using DmtAction = Dataverse.XrmTools.DataMigrationTool.Enums.Action;

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

        // ─── Push ──────────────────────────────────────────────────────────────

        public class PushResult
        {
            public int Created { get; set; }
            public int Updated { get; set; }
            public int Deleted { get; set; }
            public int Skipped { get; set; }
            public List<string> Errors { get; } = new List<string>();
        }

        public class PushPreview
        {
            public int TotalRows { get; set; }
            public int CreateCount { get; set; }
            public int UpdateCount { get; set; }
            public int DeleteCount { get; set; }
            public int WarningCount { get; set; }
            public List<PushPreviewItem> Items { get; set; } = new List<PushPreviewItem>();
        }

        public class PushPreviewItem
        {
            public int RowNumber { get; set; }
            public string SourceId { get; set; }
            public string Operation { get; set; }  // "Create" | "Update" | "Delete" | "Skip"
            public string Warnings { get; set; }
        }

        public static PushPreview PreviewPush(
            SqliteProjectService project,
            string snapshotName,
            string sourceEnvId,
            string targetEnvId,
            UiSettings settings,
            ExcelImportMatchKeySelection matchKeyOverride = null)
        {
            var preview = new PushPreview();

            var snapshot = project.GetSnapshot(snapshotName);
            if (snapshot == null) return preview;

            var cols = snapshot.ColumnConfig;
            var lookupCols = cols.Where(c => c.Type == "Lookup" || c.Type == "Owner" || c.Type == "Customer").ToList();

            bool doCreate = settings == null || (settings.Action & DmtAction.Create) != 0;
            bool doUpdate = settings == null || (settings.Action & DmtAction.Update) != 0;
            bool doDelete = settings != null && (settings.Action & DmtAction.Delete) != 0;

            var allRows = project.ReadSnapshotRecords(snapshot.TableSuffix, cols).ToList();
            preview.TotalRows = allRows.Count;

            for (var idx = 0; idx < allRows.Count; idx++)
            {
                var row = allRows[idx];
                var sourceId = row.TryGetValue("_source_id", out var sid) ? sid?.ToString() : null;
                var isNew = row.TryGetValue("_is_new", out var isn) && isn is long l && l == 1L;

                var warnings = new List<string>();

                // Check lookup resolvability
                foreach (var col in lookupCols)
                {
                    if (!row.TryGetValue(col.LogicalName, out var lv) || lv == null) continue;
                    var srcGuidStr = lv.ToString();
                    if (!Guid.TryParse(srcGuidStr, out _)) continue;
                    var targetResolved = project.ResolveTargetId(col.RelatedTable ?? snapshot.TableLogicalName, sourceEnvId, srcGuidStr, targetEnvId);
                    if (string.IsNullOrEmpty(targetResolved))
                        warnings.Add($"Lookup '{col.LogicalName}' → {col.RelatedTable}: no ID mapping (source GUID will be used as fallback)");
                }

                string operation;
                if (isNew)
                {
                    operation = doCreate ? "Create" : "Skip";
                }
                else if (string.IsNullOrEmpty(sourceId))
                {
                    operation = "Skip";
                }
                else
                {
                    var existingStr = project.ResolveTargetId(snapshot.TableLogicalName, sourceEnvId, sourceId, targetEnvId);
                    var hasMapping = !string.IsNullOrEmpty(existingStr);

                    if (doDelete)
                        operation = hasMapping ? "Delete" : "Skip";
                    else if (hasMapping)
                        operation = doUpdate ? "Update" : "Skip";
                    else
                        operation = doCreate ? "Create" : "Skip";
                }

                var item = new PushPreviewItem
                {
                    RowNumber = idx + 1,
                    SourceId = sourceId,
                    Operation = operation,
                    Warnings = warnings.Any() ? string.Join("; ", warnings) : string.Empty
                };
                preview.Items.Add(item);

                switch (operation)
                {
                    case "Create": preview.CreateCount++; break;
                    case "Update": preview.UpdateCount++; break;
                    case "Delete": preview.DeleteCount++; break;
                }
                if (warnings.Any()) preview.WarningCount++;
            }

            return preview;
        }

        public static PushResult Push(
            SqliteProjectService project,
            string snapshotName,
            string sourceEnvId,
            string targetEnvId,
            IOrganizationService targetClient,
            UiSettings settings,
            BackgroundWorker worker,
            ExcelImportMatchKeySelection matchKeyOverride = null,
            List<PushLookupMatchKey> lookupMatchKeys = null)
        {
            var result = new PushResult();

            var snapshot = project.GetSnapshot(snapshotName)
                ?? throw new InvalidOperationException($"Snapshot '{snapshotName}' not found in project.");

            var (tableConfig, _, primaryIdAttr, _) = project.GetTableConfig(snapshot.TableLogicalName);
            if (string.IsNullOrEmpty(primaryIdAttr))
                throw new InvalidOperationException($"No table config found for '{snapshot.TableLogicalName}'. Load the table attributes first.");

            var cols = snapshot.ColumnConfig;
            var matchKeyMode = matchKeyOverride?.Mode ?? snapshot.LoadMatchKeyMode ?? "Guid";
            var matchKeyFields = matchKeyOverride?.Fields ?? snapshot.LoadMatchKeyFields ?? new List<string>();

            // Load user/team/BU mappings for substitution
            var mappings = project.GetMappings(sourceEnvId, targetEnvId) ?? new List<Mapping>();

            // Read all rows
            var allRows = project.ReadSnapshotRecords(snapshot.TableSuffix, cols).ToList();
            var total = allRows.Count;
            if (total == 0)
            {
                worker?.ReportProgress(100, "Snapshot is empty — nothing to push.");
                return result;
            }

            worker?.ReportProgress(5, $"Pushing {total} records to target...");
            var targetRepo = new CrmRepo(targetClient, worker);
            var batchSize = tableConfig?.BatchSize > 0 ? tableConfig.BatchSize : 25;

            // Partition rows into operation buckets
            var toCreate  = new List<(Dictionary<string, object> row, string sourceId, bool isNew)>();
            var toUpdate  = new List<(Dictionary<string, object> row, string sourceId, Guid targetId)>();
            var toDelete  = new List<(string sourceId, Guid targetId)>();
            var warnings  = new List<string>();

            bool doCreate = settings == null || (settings.Action & DmtAction.Create) != 0;
            bool doUpdate = settings == null || (settings.Action & DmtAction.Update) != 0;
            bool doDelete = settings != null  && (settings.Action & DmtAction.Delete) != 0;

            for (var idx = 0; idx < total; idx++)
            {
                if (idx % 50 == 0)
                    worker?.ReportProgress(5 + (int)(40.0 * idx / total),
                        $"Analysing record {idx + 1} of {total}...");

                var row = allRows[idx];
                var sourceId = row.TryGetValue("_source_id", out var sid) ? sid?.ToString() : null;
                var isNew = row.TryGetValue("_is_new", out var isn) && isn is long l && l == 1L;

                if (doDelete)
                {
                    // Delete mode: resolve target GUID and queue for deletion
                    if (!string.IsNullOrEmpty(sourceId))
                    {
                        var targetIdStr = project.ResolveTargetId(snapshot.TableLogicalName, sourceEnvId, sourceId, targetEnvId);
                        if (!string.IsNullOrEmpty(targetIdStr) && Guid.TryParse(targetIdStr, out var targetIdDel))
                            toDelete.Add((sourceId, targetIdDel));
                        else
                            warnings.Add($"Delete: no target mapping for source {sourceId} — skipped.");
                    }
                    continue;
                }

                if (isNew)
                {
                    if (doCreate)
                        toCreate.Add((row, sourceId, true));
                    continue;
                }

                if (string.IsNullOrEmpty(sourceId)) { result.Skipped++; continue; }

                // Check _id_mappings first
                var existingStr = string.IsNullOrEmpty(sourceId) ? null
                    : project.ResolveTargetId(snapshot.TableLogicalName, sourceEnvId, sourceId, targetEnvId);
                var existingGuid = !string.IsNullOrEmpty(existingStr) && Guid.TryParse(existingStr, out var eg) ? eg : (Guid?)null;

                if (existingGuid.HasValue)
                {
                    if (doUpdate)
                        toUpdate.Add((row, sourceId, existingGuid.Value));
                    else
                        result.Skipped++;
                }
                else if (matchKeyMode == "Guid" && Guid.TryParse(sourceId, out var srcGuidDirect))
                {
                    // Try the source GUID directly as target GUID (optimistic)
                    if (doCreate)
                        toCreate.Add((row, sourceId, false));
                }
                else
                {
                    // Query target by match key fields
                    if (matchKeyFields?.Any() == true)
                    {
                        var keyValues = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
                        foreach (var kf in matchKeyFields)
                        {
                            if (row.TryGetValue(kf, out var kv) && kv != null)
                                keyValues[kf] = kv;
                        }
                        if (keyValues.Any())
                        {
                            var found = targetRepo.FindByFieldValues(snapshot.TableLogicalName, keyValues);
                            if (found != null)
                            {
                                project.SaveIdMapping(snapshot.TableLogicalName, sourceEnvId, sourceId, targetEnvId, found.Id.ToString("D"));
                                if (doUpdate)
                                    toUpdate.Add((row, sourceId, found.Id));
                                else
                                    result.Skipped++;
                            }
                            else if (doCreate)
                                toCreate.Add((row, sourceId, false));
                            else
                                result.Skipped++;
                        }
                        else if (doCreate)
                            toCreate.Add((row, sourceId, false));
                    }
                    else if (doCreate)
                        toCreate.Add((row, sourceId, false));
                }
            }

            // Execute Creates
            var createIdx = 0;
            for (var i = 0; i < toCreate.Count; i += batchSize)
            {
                var batch = toCreate.Skip(i).Take(batchSize).ToList();
                var entities = batch.Select(t =>
                {
                    var entity = RowToEntity(t.row, cols, snapshot.TableLogicalName, primaryIdAttr,
                        mappings, project, sourceEnvId, targetEnvId, targetRepo, lookupMatchKeys);
                    // If using GUID mode and source has a real GUID, try to preserve it
                    if (!t.isNew && matchKeyMode == "Guid"
                        && Guid.TryParse(t.sourceId, out var g))
                        entity.Id = g;
                    else
                        entity.Id = Guid.Empty; // Let Dataverse assign
                    return entity;
                }).ToList();

                worker?.ReportProgress(45 + (int)(25.0 * createIdx / Math.Max(1, toCreate.Count)),
                    $"Creating records {i + 1}–{Math.Min(i + batchSize, toCreate.Count)} of {toCreate.Count}...");

                var responses = targetRepo.CreateRecords(entities).ToList();
                for (var j = 0; j < responses.Count; j++)
                {
                    var resp = responses[j];
                    if (resp.Success)
                    {
                        result.Created++;
                        var srcId = batch[j].sourceId;
                        if (!string.IsNullOrEmpty(srcId))
                            project.SaveIdMapping(snapshot.TableLogicalName, sourceEnvId, srcId, targetEnvId, resp.ResponseId.ToString("D"));
                    }
                    else
                    {
                        result.Errors.Add($"Create failed: {resp.Message}");
                    }
                }
                createIdx += batch.Count;
            }

            // Execute Updates
            var updateIdx = 0;
            for (var i = 0; i < toUpdate.Count; i += batchSize)
            {
                var batch = toUpdate.Skip(i).Take(batchSize).ToList();
                var entities = batch.Select(t =>
                {
                    var entity = RowToEntity(t.row, cols, snapshot.TableLogicalName, primaryIdAttr,
                        mappings, project, sourceEnvId, targetEnvId, targetRepo, lookupMatchKeys);
                    entity.Id = t.targetId;
                    return entity;
                }).ToList();

                worker?.ReportProgress(70 + (int)(20.0 * updateIdx / Math.Max(1, toUpdate.Count)),
                    $"Updating records {i + 1}–{Math.Min(i + batchSize, toUpdate.Count)} of {toUpdate.Count}...");

                var responses = targetRepo.UpdateRecords(entities).ToList();
                foreach (var resp in responses)
                {
                    if (resp.Success) result.Updated++;
                    else result.Errors.Add($"Update failed: {resp.Message}");
                }
                updateIdx += batch.Count;
            }

            // Execute Deletes
            if (toDelete.Any())
            {
                worker?.ReportProgress(90, $"Deleting {toDelete.Count} records...");
                var deleteEntities = toDelete.Select(t => new Entity(snapshot.TableLogicalName) { Id = t.targetId }).ToList();
                var responses = targetRepo.DeleteRecords(deleteEntities).ToList();
                for (var j = 0; j < responses.Count; j++)
                {
                    var resp = responses[j];
                    if (resp.Success)
                    {
                        result.Deleted++;
                        project.RemoveIdMapping(snapshot.TableLogicalName, sourceEnvId, toDelete[j].sourceId, targetEnvId);
                    }
                    else
                    {
                        result.Errors.Add($"Delete failed: {resp.Message}");
                    }
                }
            }

            result.Errors.AddRange(warnings);
            worker?.ReportProgress(100, $"Push complete: {result.Created} created, {result.Updated} updated, {result.Deleted} deleted.");

            // Write run log
            var stepLog = new ExecutionPlanRunStepLog
            {
                Index = 1,
                Name = $"Push: {snapshotName}",
                Operation = "Push",
                Status = result.Errors.Any() ? "CompletedWithErrors" : "Completed",
                TotalRecords = total,
                FailedRecords = result.Errors.Count,
                Summary = $"{result.Created} created, {result.Updated} updated, {result.Deleted} deleted, {result.Skipped} skipped.",
                ErrorDetails = result.Errors
            };
            var runLog = new DmtRunLog
            {
                PlanName = $"Push: {snapshotName} → {targetEnvId}",
                StartedOn = DateTime.UtcNow,
                CompletedOn = DateTime.UtcNow,
                Status = result.Errors.Any() ? "CompletedWithErrors" : "Completed",
                Log = new ExecutionPlanRunLog
                {
                    PlanName = $"Push: {snapshotName}",
                    StartedOn = DateTime.UtcNow,
                    CompletedOn = DateTime.UtcNow,
                    Steps = new List<ExecutionPlanRunStepLog> { stepLog }
                }
            };
            try { project.SaveRunLog(runLog); } catch { /* non-critical */ }

            return result;
        }

        private static Entity RowToEntity(
            Dictionary<string, object> row,
            List<DataTableColumnConfig> cols,
            string tableLogicalName,
            string primaryIdAttr,
            List<Mapping> mappings,
            SqliteProjectService project,
            string sourceEnvId,
            string targetEnvId,
            CrmRepo targetRepo = null,
            List<PushLookupMatchKey> lookupMatchKeys = null)
        {
            var entity = new Entity(tableLogicalName);

            foreach (var col in cols)
            {
                if (string.Equals(col.LogicalName, primaryIdAttr, StringComparison.OrdinalIgnoreCase))
                    continue; // Set by caller, not from row data

                if (!row.TryGetValue(col.LogicalName, out var raw) || raw == null)
                    continue;

                var value = ConvertRowValue(raw, col, tableLogicalName, mappings, project, sourceEnvId, targetEnvId, targetRepo, lookupMatchKeys);
                if (value != null)
                    entity[col.LogicalName] = value;
            }
            return entity;
        }

        private static object ConvertRowValue(
            object raw,
            DataTableColumnConfig col,
            string tableLogicalName,
            List<Mapping> mappings,
            SqliteProjectService project,
            string sourceEnvId,
            string targetEnvId,
            CrmRepo targetRepo = null,
            List<PushLookupMatchKey> lookupMatchKeys = null)
        {
            var type = col.Type ?? string.Empty;

            switch (type)
            {
                case "Lookup":
                case "Owner":
                case "Customer":
                {
                    if (!Guid.TryParse(raw.ToString(), out var srcGuid)) return null;

                    // Apply user/team/BU mapping first
                    var mapped = mappings?.FirstOrDefault(m =>
                        string.Equals(m.TableLogicalName, col.RelatedTable, StringComparison.OrdinalIgnoreCase)
                        && m.SourceId == srcGuid);
                    if (mapped != null)
                        return new EntityReference(col.RelatedTable, mapped.TargetId);

                    // Resolve via _id_mappings
                    var relatedTable = col.RelatedTable ?? tableLogicalName;
                    var targetIdStr = project.ResolveTargetId(relatedTable, sourceEnvId, srcGuid.ToString("D"), targetEnvId);
                    if (!string.IsNullOrEmpty(targetIdStr) && Guid.TryParse(targetIdStr, out var resolvedId))
                        return new EntityReference(col.RelatedTable, resolvedId);

                    // Check per-lookup match key override
                    var lookupKey = lookupMatchKeys?.FirstOrDefault(k =>
                        string.Equals(k.LogicalName, col.LogicalName, StringComparison.OrdinalIgnoreCase));
                    if (string.Equals(lookupKey?.Mode, "Skip", StringComparison.OrdinalIgnoreCase))
                        return null; // intentionally omit the field when no mapping found

                    // Fall back to source GUID (may exist in target already)
                    return new EntityReference(col.RelatedTable, srcGuid);
                }

                case "Uniqueidentifier":
                    return Guid.TryParse(raw.ToString(), out var ug) ? ug : (object)null;

                case "String":
                case "Memo":
                case "EntityName":
                    return raw.ToString();

                case "Integer":
                case "BigInt":
                    return raw is long li ? (int)li : int.TryParse(raw.ToString(), out var iv) ? iv : (object)null;

                case "Boolean":
                    if (raw is long lb) return lb != 0L;
                    if (raw is bool bb) return bb;
                    return null;

                case "Decimal":
                    return raw is double dd ? (decimal)dd : decimal.TryParse(raw.ToString(), out var dv) ? dv : (object)null;

                case "Double":
                    return raw is double dbl ? dbl : double.TryParse(raw.ToString(), out var dbv) ? dbv : (object)null;

                case "Money":
                    return raw is double dm ? new Money((decimal)dm)
                        : decimal.TryParse(raw.ToString(), out var mv) ? new Money(mv) : (object)null;

                case "DateTime":
                    return DateTime.TryParse(raw.ToString(), null,
                        System.Globalization.DateTimeStyles.RoundtripKind, out var dt) ? dt : (object)null;

                case "Picklist":
                case "State":
                case "Status":
                    return raw is long lo ? new OptionSetValue((int)lo)
                        : int.TryParse(raw.ToString(), out var ov) ? new OptionSetValue(ov) : (object)null;

                case "MultiSelectPicklist":
                {
                    var parts = raw.ToString().Split(new[] { ',', '|' }, StringSplitOptions.RemoveEmptyEntries);
                    var values = parts.Select(p => int.TryParse(p.Trim(), out var pv) ? (int?)pv : null)
                                      .Where(v => v.HasValue)
                                      .Select(v => new OptionSetValue(v.Value));
                    var col2 = new OptionSetValueCollection(values.ToList());
                    return col2;
                }

                default:
                    return raw.ToString();
            }
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
