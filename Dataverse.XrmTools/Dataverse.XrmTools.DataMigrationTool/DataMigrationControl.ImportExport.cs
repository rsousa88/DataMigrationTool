// System
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Windows.Forms;

// Microsoft
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
using Microsoft.Xrm.Tooling.Connector;

// DataMigrationTool
using Dataverse.XrmTools.DataMigrationTool.AppSettings;
using Dataverse.XrmTools.DataMigrationTool.Forms;
using Dataverse.XrmTools.DataMigrationTool.Helpers;
using Dataverse.XrmTools.DataMigrationTool.Logic;
using Dataverse.XrmTools.DataMigrationTool.Models;
using Dataverse.XrmTools.DataMigrationTool.Repositories;
using XrmToolBox.Extensibility.Args;

namespace Dataverse.XrmTools.DataMigrationTool
{
    public partial class DataMigrationControl
    {
        private List<Mapping> BuildMappingsForImport(UiSettings uiSettings)
        {
            uiSettings = uiSettings ?? GetDefaultImportSettings(Enums.Action.None);
            var mappings = ActiveTargetInstance == null || _sourceInstance?.Mappings == null
                ? new List<Mapping>(_mappings ?? new List<Mapping>())
                : _sourceInstance.Mappings
                    .Where(map => string.Equals(map.TargetInstanceName, ActiveTargetInstance.FriendlyName, StringComparison.OrdinalIgnoreCase))
                    .ToList();
            if (ActiveTargetClient == null || ActiveTargetInstance == null || _sourceInstance == null)
                return mappings;

            if (uiSettings.MapUsers || uiSettings.MapTeams || uiSettings.MapBu)
            {
                var mappingsLogic = new MappingsLogic(_sourceClient, ActiveTargetClient);
                if (uiSettings.MapUsers)
                    mappings.AddRange(mappingsLogic.GetUserMappings(_sourceInstance.FriendlyName, ActiveTargetInstance.FriendlyName));
                if (uiSettings.MapTeams)
                    mappings.AddRange(mappingsLogic.GetTeamMappings(_sourceInstance.FriendlyName, ActiveTargetInstance.FriendlyName));
                if (uiSettings.MapBu)
                {
                    var buMapping = mappingsLogic.GetBusinessUnitMapping(_sourceInstance.FriendlyName, ActiveTargetInstance.FriendlyName);
                    if (buMapping != null) mappings.Add(buMapping);
                }
            }
            return mappings;
        }

        private RecordCollection FilterRecordCollection(RecordCollection collection, IEnumerable<Guid> ids)
        {
            return RecordCollectionService.FilterByIds(collection, ids);
        }

        private bool TryGetRecordId(Record record, string primaryIdAttribute, out Guid id)
        {
            return RecordCollectionService.TryGetRecordId(record, primaryIdAttribute, out id);
        }

        private HashSet<Guid> GetSuccessfulResultIds(IEnumerable<ListViewItem> resultItems)
        {
            return new HashSet<Guid>(GetSuccessfulResultIdMap(resultItems).Keys);
        }

        private PlanLookupContext BuildPlanLookupContextForPriorSteps(DmtEnvironmentInfo targetEnvironment, ExecutionPlanStep currentStep)
        {
            return BuildPlanLookupContextForPriorSteps(targetEnvironment, currentStep, null, false);
        }

        private PlanLookupContext BuildPlanLookupContextForPriorSteps(
            DmtEnvironmentInfo targetEnvironment,
            ExecutionPlanStep currentStep,
            BackgroundWorker worker,
            bool hydrateMissingCollections,
            IOrganizationService targetService = null,
            ISet<string> requiredLookupTables = null)
        {
            var context = new PlanLookupContext();
            if (_executionPlan?.Steps == null)
                return context;

            var stepIndex = 0;
            foreach (var step in _executionPlan.Steps)
            {
                stepIndex++;
                if (currentStep != null && string.Equals(step.Id, currentStep.Id, StringComparison.OrdinalIgnoreCase))
                    break;
                if (!step.Enabled || !(step.Operation ?? string.Empty).StartsWith("Import", StringComparison.OrdinalIgnoreCase))
                    continue;
                if (!SameExecutionTarget(step.TargetEnvironment, targetEnvironment))
                    continue;
                if (!IsPlanLookupStepRequired(step, requiredLookupTables))
                    continue;

                var collection = step.Snapshot?.RecordCollection;
                if (collection == null && hydrateMissingCollections)
                    collection = TryLoadExecutionPlanImportCollectionForLookupContext(step, stepIndex, context, worker, targetService);

                context.AddRecordCollection(collection);
            }

            return context;
        }

        private bool IsPlanLookupStepRequired(ExecutionPlanStep step, ISet<string> requiredLookupTables)
        {
            if (requiredLookupTables == null)
                return true;
            if (!requiredLookupTables.Any())
                return false;

            var logicalName = step?.Snapshot?.RecordCollection?.LogicalName ?? step?.Table?.LogicalName;
            return !string.IsNullOrWhiteSpace(logicalName) && requiredLookupTables.Contains(logicalName);
        }

        private HashSet<string> GetPlanLookupTablesRequiredByImportConfig(ExcelExportConfig config)
        {
            var requiredTables = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (config?.Columns == null)
                return requiredTables;

            foreach (var column in config.Columns)
            {
                var isLookupColumn = string.Equals(column.Type, "Lookup", StringComparison.OrdinalIgnoreCase);
                var isLookupKeyField = string.Equals(column.Type, "LookupKeyField", StringComparison.OrdinalIgnoreCase)
                    && string.Equals(column.KeyFieldType, "Lookup", StringComparison.OrdinalIgnoreCase);

                if ((isLookupColumn || isLookupKeyField)
                    && !string.Equals(column.Resolution, "Guid", StringComparison.OrdinalIgnoreCase)
                    && !string.IsNullOrWhiteSpace(column.RelatedTable))
                {
                    requiredTables.Add(column.RelatedTable);
                }
            }

            return requiredTables;
        }

        private RecordCollection TryLoadExecutionPlanImportCollectionForLookupContext(
            ExecutionPlanStep step,
            int stepIndex,
            IPlanLookupResolver planLookupResolver,
            BackgroundWorker worker,
            IOrganizationService targetService)
        {
            try
            {
                var path = ResolveExecutionStepPath(step, stepIndex);
                if (string.IsNullOrWhiteSpace(path) || !System.IO.File.Exists(path))
                    return null;

                worker?.ReportProgress(0, $"Execution plan: reading prior import context for {step.Name}...");

                if (string.Equals(step.Operation, "ImportFromExcel", StringComparison.OrdinalIgnoreCase))
                {
                    var excelLogic = new ExcelLogic();
                    return excelLogic.ImportFromExcel(
                        path,
                        out ExcelExportConfig _,
                        targetService ?? ActiveTargetClient,
                        worker,
                        importConfig =>
                        {
                            if (step.Snapshot?.ExcelConfig != null)
                            {
                                importConfig.MatchKey = step.Snapshot.ExcelConfig.MatchKey;
                                importConfig.MatchKeyMode = step.Snapshot.ExcelConfig.MatchKeyMode;
                                importConfig.MatchKeys = step.Snapshot.ExcelConfig.MatchKeys;
                                importConfig.MatchAlternateKeyName = step.Snapshot.ExcelConfig.MatchAlternateKeyName;
                                importConfig.ImportSettings = step.Snapshot.ExcelConfig.ImportSettings;
                            }
                            ApplyImportMatchKeySelection(importConfig, step.Snapshot?.ImportMatchKeySelection);
                            EnsureExcelImportSettings(importConfig, BuildExcelImportSettings(step.Snapshot?.ImportSettings, importConfig));
                        },
                        planLookupResolver);
                }

                if (string.Equals(step.Operation, "ImportFromJson", StringComparison.OrdinalIgnoreCase))
                {
                    var json = System.IO.File.ReadAllText(path);
                    var collection = json.DeserializeObject<RecordCollection>();
                    ImportFileDataChecks(collection);
                    var tableData = BuildTableDataForExecutionStep(step);
                    ApplyJsonImportMatchKeySelection(collection, tableData, step.Snapshot?.ImportMatchKeySelection);
                    return collection;
                }
            }
            catch (Exception ex)
            {
                _logger?.Log(Enums.LogLevel.WARN, $"Could not read prior import context for '{step?.Name}': {ex.Message}");
            }

            return null;
        }

        private PlanLookupContext GetRuntimePlanLookupContext(Dictionary<string, PlanLookupContext> contexts, ExecutionPlanStep step)
        {
            if (contexts == null)
                return null;

            var key = GetExecutionTargetKey(step?.TargetEnvironment);
            return contexts.TryGetValue(key, out var context)
                ? context
                : new PlanLookupContext();
        }

        private void AddImportedRecordsToPlanLookupContext(
            Dictionary<string, PlanLookupContext> contexts,
            ExecutionPlanStep step,
            RecordCollection collection,
            IDictionary<Guid, Guid> importedIdMap)
        {
            if (contexts == null || collection == null)
                return;

            var key = GetExecutionTargetKey(step?.TargetEnvironment);
            if (!contexts.TryGetValue(key, out var context))
            {
                context = new PlanLookupContext();
                contexts[key] = context;
            }

            context.AddRecordCollection(collection, importedIdMap);
        }

        private bool SameExecutionTarget(DmtEnvironmentInfo left, DmtEnvironmentInfo right)
        {
            return string.Equals(GetExecutionTargetKey(left), GetExecutionTargetKey(right), StringComparison.OrdinalIgnoreCase);
        }

        private string GetExecutionTargetKey(DmtEnvironmentInfo environment)
        {
            return environment?.UniqueName
                ?? environment?.FriendlyName
                ?? ActiveTargetClient?.ConnectedOrgUniqueName
                ?? ActiveTargetClient?.ConnectedOrgFriendlyName
                ?? string.Empty;
        }

        private Dictionary<Guid, Guid> GetSuccessfulResultIdMap(IEnumerable<ListViewItem> resultItems)
        {
            return ExecutionResultService.GetSuccessfulIdMap((resultItems ?? Enumerable.Empty<ListViewItem>())
                .Where(item => item.SubItems.Count >= 4)
                .Select(item => new ExecutionResultRow
                {
                    Action = item.SubItems[0].Text,
                    Id = item.SubItems[1].Text,
                    Description = item.SubItems[3].Text
                }));
        }

        private int BeginExcelImportOperation()
        {
            _activeExcelImportOperationId++;
            _importPreviewDialogOpen = false;
            return _activeExcelImportOperationId;
        }

        private bool IsCurrentExcelImportOperation(int operationId)
        {
            return operationId > 0 && operationId == _activeExcelImportOperationId;
        }

        private bool ShouldIgnoreExcelImportCallback(RunWorkerCompletedEventArgs evt, int operationId, string cancelledMessage)
        {
            if (!IsCurrentExcelImportOperation(operationId)) return true;
            if (!evt.Cancelled) return false;

            _activeExcelImportOperationId++;
            _logger.Log(Enums.LogLevel.INFO, cancelledMessage);
            SendMessageToStatusBar?.Invoke(this, new StatusBarMessageEventArgs(cancelledMessage));
            return true;
        }

        private void ThrowIfCancelled(BackgroundWorker worker)
        {
            if (worker != null && worker.CancellationPending)
                throw new OperationCanceledException();
        }

        private ExcelImportPreview BuildExcelImportPreview(TableData tableData, RecordCollection collection, ExcelExportConfig config, UiSettings uiSettings, string filePath, IPlanLookupResolver planLookupResolver = null)
        {
            return ImportPreviewService.BuildPreview(new ExcelImportPreviewRequest
            {
                TableData = tableData,
                Collection = collection,
                Config = config,
                Settings = uiSettings,
                FilePath = filePath,
                TargetName = ActiveTargetInstance?.FriendlyName ?? string.Empty,
                MappingCount = BuildMappingsForImport(uiSettings).Count,
                ExistingTargetIdsProvider = sourceIds => GetExistingTargetIdsIncludingPlanMatches(
                    tableData.Table.LogicalName,
                    tableData.Table.IdAttribute,
                    sourceIds,
                    uiSettings.BatchSize,
                    planLookupResolver)
            });
        }

        private ISet<Guid> GetExistingTargetIdsIncludingPlanMatches(
            string logicalName,
            string idAttribute,
            IEnumerable<Guid> sourceIds,
            int batchSize,
            IPlanLookupResolver planLookupResolver)
        {
            var ids = sourceIds?.Distinct().ToList() ?? new List<Guid>();
            var existing = GetExistingTargetIds(logicalName, idAttribute, ids, batchSize);
            if (planLookupResolver == null) return existing;

            foreach (var id in ids)
            {
                var planMatch = planLookupResolver.ResolveBySourceId(logicalName, id);
                if (planMatch.HasValue && planMatch.Value != Guid.Empty)
                    existing.Add(planMatch.Value);
            }

            return existing;
        }

        private void ApplyImportMatchKeySelection(ExcelExportConfig config, ExcelImportMatchKeySelection selection)
        {
            ImportPreviewService.ApplyImportMatchKeySelection(config, selection);
        }

        private void ApplyJsonImportMatchKeySelection(RecordCollection collection, TableData tableData, ExcelImportMatchKeySelection selection)
        {
            if (collection == null || tableData == null || selection == null) return;

            var fields = ImportPreviewService.NormalizeImportMatchKeyFields(selection);
            var mode = string.IsNullOrWhiteSpace(selection.Mode) || !fields.Any()
                ? "Guid"
                : selection.Mode;

            collection.ImportMatchKeyMode = mode;
            collection.ImportMatchKeys = string.Equals(mode, "Guid", StringComparison.OrdinalIgnoreCase)
                ? new List<string>()
                : fields;
            collection.ImportMatchKey = ImportPreviewService.GetImportMatchKeyDisplay(mode, fields, selection.AlternateKeyName);

            if (string.Equals(mode, "Guid", StringComparison.OrdinalIgnoreCase)) return;

            var targetRepo = new CrmRepo(ActiveTargetClient);
            var records = collection.Records?.ToList() ?? new List<Record>();
            var warnings = collection.ImportErrors ?? new List<string>();
            var rowIndex = 1;

            foreach (var record in records)
            {
                var attributes = record.Attributes?.ToList() ?? new List<RecordAttribute>();
                var rowNumber = record.SourceRowNumber > 0 ? record.SourceRowNumber : rowIndex;
                var keyValues = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
                var hasBlankKey = false;

                foreach (var field in fields)
                {
                    var attr = attributes.FirstOrDefault(a => a.Key.Equals(field, StringComparison.OrdinalIgnoreCase));
                    if (attr == null || ImportPreviewService.IsImportValueBlank(attr.Value))
                    {
                        warnings.Add($"Row {rowNumber}, field '{field}': match key is blank.");
                        hasBlankKey = true;
                        continue;
                    }

                    keyValues[field] = ImportPreviewService.ToImportQueryValue(attr.Value);
                }

                if (!hasBlankKey && keyValues.Any())
                {
                    var target = targetRepo.FindByFieldValues(tableData.Table.LogicalName, keyValues);
                    if (target != null)
                        ImportPreviewService.SetRecordPrimaryId(collection.PrimaryIdAttribute, target.Id, attributes);
                    else
                        ImportPreviewService.EnsureRecordPrimaryId(collection.PrimaryIdAttribute, attributes);
                }

                record.Attributes = attributes;
                rowIndex++;
            }

            collection.Records = records;
            collection.Count = records.Count;
            collection.ImportErrors = warnings;
        }

        private void SaveDmtImportSettings(UiSettings uiSettings, ExcelImportMatchKeySelection selection)
        {
            if (_dmtSettings == null) return;

            _dmtSettings.ImportSettings = new DmtImportSettings
            {
                BatchSize = uiSettings != null && uiSettings.BatchSize > 0 ? Math.Min(uiSettings.BatchSize, 25) : 25,
                MatchKeyMode = selection?.Mode ?? "Guid",
                MatchKeyFields = selection?.Fields?.ToList() ?? new List<string>(),
                MatchAlternateKeyName = selection?.AlternateKeyName
            };

            AutoSaveDmtSettings();
        }

        private HashSet<Guid> GetExistingTargetIds(string logicalName, string idAttribute, IEnumerable<Guid> sourceIds, int batchSize)
        {
            var existing = new HashSet<Guid>();
            var ids = sourceIds.Distinct().ToList();
            if (!ids.Any() || ActiveTargetClient == null) return existing;

            var repo = new CrmRepo(ActiveTargetClient);

            foreach (var batch in BatchingService.Batch(ids, batchSize))
            {
                if (!batch.Any()) continue;

                var query = new QueryExpression(logicalName)
                {
                    ColumnSet = new ColumnSet(idAttribute),
                    Criteria = new FilterExpression(LogicalOperator.And)
                };
                query.Criteria.Conditions.Add(new ConditionExpression(idAttribute, ConditionOperator.In, batch.Cast<object>().ToArray()));

                var targetCollection = repo.GetCollectionByExpression(query, Math.Max(batchSize, 1));
                foreach (var entity in targetCollection.Entities)
                    existing.Add(entity.Id);
            }

            return existing;
        }

        private class ExcelImportSession
        {
            public string FilePath { get; set; }
            public string SourceType { get; set; }
            public ExcelExportConfig Config { get; set; }
            public RecordCollection Collection { get; set; }
            public int OperationId { get; set; }
            public DmtEnvironmentInfo TargetEnvironment { get; set; }
        }

        private class ExcelImportPreflightResult
        {
            public string FilePath { get; set; }
            public int RowCount { get; set; }
            public ExcelExportConfig Config { get; set; }
        }
    }
}
