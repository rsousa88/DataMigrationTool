// System
using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;

// Newtonsoft
using Newtonsoft.Json;

// DataMigrationTool
using Dataverse.XrmTools.DataMigrationTool.AppSettings;
using Dataverse.XrmTools.DataMigrationTool.Models;

namespace Dataverse.XrmTools.DataMigrationTool.Logic
{
    public class ExecutionPlanPathContext
    {
        public string PlanName { get; set; }
        public string SourceName { get; set; }
        public string FallbackTargetName { get; set; }
        public string ProjectDirectory { get; set; }
        public DateTime? Now { get; set; }
    }

    public static class ExecutionPlanService
    {
        public static string BuildPushSnapshotStepName(DmtSnapshot snapshot, DmtEnvironmentInfo targetEnvironment)
        {
            var tableName = snapshot?.TableLogicalName ?? "snapshot";
            var snapshotName = snapshot?.Name ?? "snapshot";
            var sortOrder = snapshot?.SortOrder ?? 0;
            return $"[{EnvironmentTagHelper.GetTag(targetEnvironment)}] Push {tableName} (#{sortOrder} {snapshotName})";
        }

        public static ExecutionPlan CreateNewProjectPlan(string name, DmtEnvironmentInfo sourceEnvironment, DmtEnvironmentInfo targetEnvironment, IEnumerable<DmtEnvironmentInfo> targetEnvironments)
        {
            var targets = (targetEnvironments ?? Enumerable.Empty<DmtEnvironmentInfo>())
                .Where(env => env != null && !string.IsNullOrWhiteSpace(env.UniqueName))
                .GroupBy(env => env.UniqueName, StringComparer.OrdinalIgnoreCase)
                .Select(group => Clone(group.First()))
                .ToList();

            if (targetEnvironment != null
                && !string.IsNullOrWhiteSpace(targetEnvironment.UniqueName)
                && !targets.Any(env => string.Equals(env.UniqueName, targetEnvironment.UniqueName, StringComparison.OrdinalIgnoreCase)))
                targets.Add(Clone(targetEnvironment));

            return new ExecutionPlan
            {
                Name = string.IsNullOrWhiteSpace(name) ? "Migration Plan" : name.Trim(),
                SourceEnvironment = Clone(sourceEnvironment),
                TargetEnvironment = Clone(targetEnvironment),
                TargetEnvironments = targets
            };
        }

        public static DmtPlan ToProjectPlan(ExecutionPlan plan, string planId)
        {
            if (plan == null) throw new ArgumentNullException(nameof(plan));

            return new DmtPlan
            {
                Id = string.IsNullOrWhiteSpace(planId) ? Guid.NewGuid().ToString("D") : planId,
                Name = string.IsNullOrWhiteSpace(plan.Name) ? "Migration Plan" : plan.Name,
                CreatedOn = plan.CreatedOn == default(DateTime) ? DateTime.UtcNow : plan.CreatedOn,
                UpdatedOn = DateTime.UtcNow,
                Defaults = Clone(plan.Defaults) ?? new ExecutionPlanDefaults()
            };
        }

        public static List<DmtPlanStep> ToProjectPlanSteps(ExecutionPlan plan, string planId, string sourceEnvId)
        {
            return (plan?.Steps ?? new List<ExecutionPlanStep>())
                .Select((step, index) => ToProjectPlanStep(step, planId, sourceEnvId, index))
                .ToList();
        }

        public static ExecutionPlan FromProjectPlan(DmtPlan plan, IEnumerable<DmtPlanStep> steps, DmtEnvironmentInfo sourceEnvironment, IEnumerable<DmtEnvironmentInfo> targetEnvironments)
        {
            if (plan == null) return null;

            var targets = (targetEnvironments ?? Enumerable.Empty<DmtEnvironmentInfo>())
                .Where(env => env != null && !string.IsNullOrWhiteSpace(env.UniqueName))
                .GroupBy(env => env.UniqueName, StringComparer.OrdinalIgnoreCase)
                .Select(group => Clone(group.First()))
                .ToList();

            var executionPlan = new ExecutionPlan
            {
                Name = plan.Name,
                CreatedOn = plan.CreatedOn,
                UpdatedOn = plan.UpdatedOn,
                SourceEnvironment = Clone(sourceEnvironment),
                TargetEnvironment = targets.FirstOrDefault(),
                TargetEnvironments = targets,
                Defaults = Clone(plan.Defaults) ?? new ExecutionPlanDefaults(),
                Steps = (steps ?? Enumerable.Empty<DmtPlanStep>())
                    .OrderBy(step => step.SortOrder)
                    .Select(FromProjectPlanStep)
                    .ToList()
            };

            EnsurePlanDefaults(executionPlan);
            return executionPlan;
        }

        private static DmtPlanStep ToProjectPlanStep(ExecutionPlanStep step, string planId, string sourceEnvId, int index)
        {
            var isExport = IsExportStep(step);
            var filePath = isExport ? step.Output?.PathTemplate : step.Input?.Path;
            var snapshot = step.Snapshot ?? new ExecutionPlanStepSnapshot();
            return new DmtPlanStep
            {
                Id = string.IsNullOrWhiteSpace(step.Id) ? Guid.NewGuid().ToString("D") : step.Id,
                PlanId = planId,
                SortOrder = index,
                Name = step.Name,
                Enabled = step.Enabled,
                Operation = step.Operation,
                TableLogicalName = step.Table?.LogicalName,
                SourceEnvId = isExport ? sourceEnvId : null,
                TargetEnvId = step.TargetEnvironment?.UniqueName,
                SnapshotName = step.Input?.SnapshotName,
                FileType = GetStepFileType(step),
                FilePath = filePath,
                Snapshot = new DmtPlanStepSnapshot
                {
                    Table = Clone(step.Table),
                    TargetEnvironment = Clone(step.TargetEnvironment),
                    Input = Clone(step.Input) ?? new ExecutionPlanStepInput(),
                    Output = Clone(step.Output) ?? new ExecutionPlanStepOutput(),
                    SettingsProvenance = Clone(step.SettingsProvenance),
                    SelectedAttributes = snapshot.SelectedAttributes?.ToList() ?? new List<string>(),
                    Filter = snapshot.Filter,
                    ExcelConfig = CloneExcelConfig(snapshot.ExcelConfig),
                    RecordCollection = Clone(snapshot.RecordCollection),
                    ImportMatchKeySelection = CloneImportMatchKeySelection(snapshot.ImportMatchKeySelection),
                    LoadMatchKeyMode = snapshot.ImportMatchKeySelection?.Mode,
                    LoadMatchKeyFields = snapshot.ImportMatchKeySelection?.Fields?.ToList() ?? new List<string>(),
                    LoadMatchAlternateKeyName = snapshot.ImportMatchKeySelection?.AlternateKeyName,
                    PushMatchKeyMode = snapshot.PushMatchKeyMode,
                    PushMatchKeyFields = snapshot.PushMatchKeyFields?.ToList() ?? new List<string>(),
                    PushMatchAlternateKeyName = snapshot.PushMatchAlternateKeyName,
                    ImportSettings = Clone(snapshot.ImportSettings),
                    ExportSettings = Clone(snapshot.ExportSettings),
                    LookupMatchKeys = Clone(snapshot.LookupMatchKeys)
                },
                FailurePolicy = Clone(step.FailurePolicy) ?? new ExecutionPlanFailurePolicy(),
                Validation = Clone(step.Validation) ?? new ExecutionPlanValidation()
            };
        }

        private static ExecutionPlanStep FromProjectPlanStep(DmtPlanStep step)
        {
            var snapshot = step.Snapshot ?? new DmtPlanStepSnapshot();
            var executionStep = new ExecutionPlanStep
            {
                Id = string.IsNullOrWhiteSpace(step.Id) ? Guid.NewGuid().ToString("D") : step.Id,
                Name = step.Name,
                Enabled = step.Enabled,
                Operation = step.Operation,
                Table = Clone(snapshot.Table) ?? new DmtTableInfo { LogicalName = step.TableLogicalName },
                TargetEnvironment = Clone(snapshot.TargetEnvironment) ?? (!string.IsNullOrWhiteSpace(step.TargetEnvId) ? new DmtEnvironmentInfo { UniqueName = step.TargetEnvId } : null),
                Input = Clone(snapshot.Input) ?? new ExecutionPlanStepInput(),
                Output = Clone(snapshot.Output) ?? new ExecutionPlanStepOutput(),
                SettingsProvenance = Clone(snapshot.SettingsProvenance),
                Snapshot = new ExecutionPlanStepSnapshot
                {
                    SelectedAttributes = snapshot.SelectedAttributes?.ToList() ?? new List<string>(),
                    Filter = snapshot.Filter,
                    ExcelConfig = CloneExcelConfig(snapshot.ExcelConfig),
                    RecordCollection = Clone(snapshot.RecordCollection),
                    ImportMatchKeySelection = CloneImportMatchKeySelection(snapshot.ImportMatchKeySelection),
                    PushMatchKeyMode = snapshot.PushMatchKeyMode,
                    PushMatchKeyFields = snapshot.PushMatchKeyFields?.ToList() ?? new List<string>(),
                    PushMatchAlternateKeyName = snapshot.PushMatchAlternateKeyName,
                    ImportSettings = Clone(snapshot.ImportSettings),
                    ExportSettings = Clone(snapshot.ExportSettings),
                    LookupMatchKeys = Clone(snapshot.LookupMatchKeys)
                },
                FailurePolicy = Clone(step.FailurePolicy) ?? new ExecutionPlanFailurePolicy(),
                Validation = Clone(step.Validation) ?? new ExecutionPlanValidation()
            };

            if (string.IsNullOrWhiteSpace(executionStep.Input?.SnapshotName))
                executionStep.Input.SnapshotName = step.SnapshotName;
            if (string.IsNullOrWhiteSpace(executionStep.Input?.Path) && !IsExportStep(executionStep))
                executionStep.Input.Path = step.FilePath;
            if (string.IsNullOrWhiteSpace(executionStep.Output?.PathTemplate) && IsExportStep(executionStep))
                executionStep.Output.PathTemplate = step.FilePath;
            EnsureStepDefaults(executionStep);
            return executionStep;
        }

        public static void ValidatePlan(ExecutionPlan plan)
        {
            if (plan == null) return;
            EnsurePlanDefaults(plan);

            var ids = plan.Steps.Select(s => s.Id).Where(id => !string.IsNullOrWhiteSpace(id)).ToList();
            var duplicateIds = ids.GroupBy(id => id, StringComparer.OrdinalIgnoreCase).Where(g => g.Count() > 1).Select(g => g.Key).ToList();
            var order = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            var steps = new Dictionary<string, ExecutionPlanStep>(StringComparer.OrdinalIgnoreCase);
            for (var i = 0; i < plan.Steps.Count; i++)
            {
                var step = plan.Steps[i];
                if (string.IsNullOrWhiteSpace(step.Id) || order.ContainsKey(step.Id)) continue;
                order[step.Id] = i;
                steps[step.Id] = step;
            }

            foreach (var step in plan.Steps)
                ValidateStep(step, duplicateIds, order, steps);
        }

        public static string GetOperationDisplayName(string operation)
        {
            switch (operation)
            {
                case "ExportToJson":      return "Export JSON";
                case "ExportToExcel":     return "Export Excel";
                case "ImportFromJson":    return "Import JSON";
                case "ImportFromExcel":   return "Import Excel";
                case "PushFromSnapshot":  return "Push Snapshot";
                default: return operation;
            }
        }

        public static bool IsExportStep(ExecutionPlanStep step)
        {
            return (step?.Operation ?? string.Empty).StartsWith("Export", StringComparison.OrdinalIgnoreCase);
        }

        public static bool IsPushSnapshotStep(ExecutionPlanStep step)
        {
            return string.Equals(step?.Operation, "PushFromSnapshot", StringComparison.OrdinalIgnoreCase);
        }

        public static int GetStepNumber(ExecutionPlan plan, string stepId)
        {
            var index = plan?.Steps?.FindIndex(s => string.Equals(s.Id, stepId, StringComparison.OrdinalIgnoreCase)) ?? -1;
            return index >= 0 ? index + 1 : 0;
        }

        public static string GetStepInputOutputText(ExecutionPlan plan, ExecutionPlanStep step)
        {
            if (string.Equals(step?.Input?.Mode, "FromStepOutput", StringComparison.OrdinalIgnoreCase))
                return $"From Step {GetStepNumber(plan, step.Input.SourceStepId)}";
            if (string.Equals(step?.Input?.Mode, "Snapshot", StringComparison.OrdinalIgnoreCase))
                return $"Snapshot: {step.Input.SnapshotName}";
            if (!string.IsNullOrWhiteSpace(step?.Output?.PathTemplate))
                return step.Output.PathTemplate;
            return step?.Input?.Path ?? string.Empty;
        }

        public static bool CanMoveStep(ExecutionPlan plan, ExecutionPlanStep step, int newIndex, out string reason)
        {
            reason = null;
            if (plan?.Steps == null || step == null) return true;

            if (string.Equals(step.Input?.Mode, "FromStepOutput", StringComparison.OrdinalIgnoreCase)
                && !string.IsNullOrWhiteSpace(step.Input.SourceStepId))
            {
                var sourceIndex = plan.Steps.FindIndex(s => string.Equals(s.Id, step.Input.SourceStepId, StringComparison.OrdinalIgnoreCase));
                if (sourceIndex >= 0 && newIndex <= sourceIndex)
                {
                    reason = "This import is linked to an earlier export and must stay after it.";
                    return false;
                }
            }

            var firstDependentIndex = plan.Steps
                .Select((candidate, index) => new { candidate, index })
                .Where(x => string.Equals(x.candidate.Input?.Mode, "FromStepOutput", StringComparison.OrdinalIgnoreCase)
                    && string.Equals(x.candidate.Input?.SourceStepId, step.Id, StringComparison.OrdinalIgnoreCase))
                .Select(x => x.index)
                .DefaultIfEmpty(int.MaxValue)
                .Min();

            if (newIndex >= firstDependentIndex)
            {
                reason = "This export has linked import steps and must stay before them.";
                return false;
            }

            return true;
        }

        public static bool IsBlockedByFailedDependency(ExecutionPlanStep step, HashSet<string> failedStepIds)
        {
            return string.Equals(step?.Input?.Mode, "FromStepOutput", StringComparison.OrdinalIgnoreCase)
                && !string.IsNullOrWhiteSpace(step.Input.SourceStepId)
                && failedStepIds != null
                && failedStepIds.Contains(step.Input.SourceStepId);
        }

        public static List<ExecutionPlanStep> GetExecutableSteps(ExecutionPlan plan)
        {
            return (plan?.Steps ?? new List<ExecutionPlanStep>())
                .Where(step => step.Enabled)
                .ToList();
        }

        public static bool CanExecuteValidatedPlan(ExecutionPlan plan, bool validatedForExecution)
        {
            return validatedForExecution
                && GetExecutableSteps(plan).Any()
                && !GetExecutableSteps(plan).Any(s => string.Equals(s.Validation?.Status, "Error", StringComparison.OrdinalIgnoreCase));
        }

        public static bool TryValidateTargetConnection(
            ExecutionPlanStep step,
            IEnumerable<string> connectedTargetUniqueNames,
            bool hasFallbackTarget,
            out string error)
        {
            error = null;
            var uniqueName = step?.TargetEnvironment?.UniqueName;
            if (string.IsNullOrWhiteSpace(uniqueName))
            {
                if (!hasFallbackTarget)
                {
                    error = "No target connection is available.";
                    return false;
                }

                return true;
            }

            var connected = new HashSet<string>(connectedTargetUniqueNames ?? Enumerable.Empty<string>(), StringComparer.OrdinalIgnoreCase);
            if (connected.Contains(uniqueName)) return true;

            error = $"Target environment is not connected: {step.TargetEnvironment.FriendlyName ?? uniqueName}";
            return false;
        }

        public static bool GetStepStopOnFatalError(ExecutionPlan plan, ExecutionPlanStep step)
        {
            return step?.FailurePolicy?.StopOnFatalError ?? plan?.Defaults?.StopOnFatalError ?? true;
        }

        public static int GetStepMaxFailedRecords(ExecutionPlan plan, ExecutionPlanStep step)
        {
            return step?.FailurePolicy?.MaxFailedRecords ?? plan?.Defaults?.MaxFailedRecords ?? 10;
        }

        public static decimal GetStepMaxFailedPercent(ExecutionPlan plan, ExecutionPlanStep step)
        {
            return step?.FailurePolicy?.MaxFailedPercent ?? plan?.Defaults?.MaxFailedPercent ?? 20m;
        }

        public static decimal GetFailedPercent(int failed, int total)
        {
            return total > 0 ? decimal.Round((decimal)failed / total * 100m, 2) : 0m;
        }

        public static bool HasReachedFailureThreshold(ExecutionPlan plan, ExecutionPlanStep step, int failed, int total)
        {
            var percent = GetFailedPercent(failed, total);
            return failed > 0 && (failed >= GetStepMaxFailedRecords(plan, step) || percent >= GetStepMaxFailedPercent(plan, step));
        }

        public static bool IsFailedResultDescription(string description)
        {
            description = description ?? string.Empty;
            return description.StartsWith("ERROR:", StringComparison.OrdinalIgnoreCase)
                || description.IndexOf("ERROR:", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        public static ExecutionPlanStepExecutionResult BuildExecutionStepResult(ExecutionPlan plan, ExecutionPlanStep step, string action, IEnumerable<string> resultDescriptions)
        {
            var descriptions = (resultDescriptions ?? Enumerable.Empty<string>()).ToList();
            var failedDescriptions = descriptions.Where(IsFailedResultDescription).ToList();
            return BuildExecutionStepResult(plan, step, action, descriptions.Count, failedDescriptions.Count, failedDescriptions);
        }

        public static ExecutionPlanStepExecutionResult BuildExecutionStepResult(ExecutionPlan plan, ExecutionPlanStep step, string action, int total, int failed)
        {
            return BuildExecutionStepResult(plan, step, action, total, failed, Enumerable.Empty<string>());
        }

        public static ExecutionPlanStepExecutionResult BuildExecutionStepResult(
            ExecutionPlan plan,
            ExecutionPlanStep step,
            string action,
            int total,
            int failed,
            IEnumerable<string> errorDetails)
        {
            var percent = GetFailedPercent(failed, total);
            var thresholdHit = HasReachedFailureThreshold(plan, step, failed, total);
            return new ExecutionPlanStepExecutionResult
            {
                TotalRecords = total,
                FailedRecords = failed,
                FailedPercent = percent,
                HasFailures = failed > 0,
                ShouldStopPlan = thresholdHit,
                ErrorDetails = (errorDetails ?? Enumerable.Empty<string>())
                    .Where(error => !string.IsNullOrWhiteSpace(error))
                    .ToList(),
                Summary = thresholdHit
                    ? $"{step?.Name}: {action} with {failed}/{total} failed record(s); failure threshold reached, plan stopped."
                    : $"{step?.Name}: {action} ({total} result row(s), {failed} failed)"
            };
        }

        public static ExecutionPlanRunLog CreateRunLog(
            ExecutionPlan plan,
            string planFilePath,
            DmtEnvironmentInfo sourceEnvironment,
            DmtEnvironmentInfo targetEnvironment,
            IEnumerable<DmtEnvironmentInfo> targetEnvironments,
            DateTime? startedOn = null)
        {
            return new ExecutionPlanRunLog
            {
                PlanName = plan?.Name,
                PlanFilePath = planFilePath,
                StartedOn = startedOn ?? DateTime.UtcNow,
                SourceEnvironment = sourceEnvironment,
                TargetEnvironment = targetEnvironment,
                TargetEnvironments = (targetEnvironments ?? Enumerable.Empty<DmtEnvironmentInfo>()).ToList()
            };
        }

        public static ExecutionPlanRunStepLog CreateRunStepLog(
            ExecutionPlan plan,
            ExecutionPlanStep step,
            int index,
            ExecutionPlanPathContext context)
        {
            return new ExecutionPlanRunStepLog
            {
                Index = index,
                StepId = step?.Id,
                Name = step?.Name,
                Operation = step?.Operation,
                TargetEnvironment = step?.TargetEnvironment,
                Path = ResolveExecutionStepPath(plan, step, index, context)
            };
        }

        public static void MarkSkippedDueToFailedDependency(ExecutionPlanRunStepLog stepLog, ExecutionPlanStep step)
        {
            if (stepLog == null) return;
            stepLog.Status = "Skipped";
            stepLog.Summary = $"{step?.Name}: skipped because a linked dependency failed.";
        }

        public static void ApplyExecutionResultToLog(ExecutionPlanRunStepLog stepLog, ExecutionPlanStepExecutionResult result)
        {
            if (stepLog == null || result == null) return;

            stepLog.Summary = result.Summary;
            stepLog.TotalRecords = result.TotalRecords;
            stepLog.FailedRecords = result.FailedRecords;
            stepLog.FailedPercent = result.FailedPercent;
            stepLog.ErrorDetails = result.ErrorDetails ?? new List<string>();
            stepLog.Status = result.ShouldStopPlan ? "Failed" : result.HasFailures ? "Warning" : "Success";
        }

        public static void AddValidationMessage(ExecutionPlanStep step, string severity, string message)
        {
            if (step == null) return;

            step.Validation = step.Validation ?? new ExecutionPlanValidation();
            step.Validation.Messages = step.Validation.Messages ?? new List<ExecutionPlanValidationMessage>();
            step.Validation.Messages.Add(new ExecutionPlanValidationMessage { Severity = severity, Message = message });
            RefreshValidationStatus(step);
        }

        public static void RefreshValidationStatus(ExecutionPlanStep step)
        {
            if (step == null) return;

            step.Validation = step.Validation ?? new ExecutionPlanValidation();
            step.Validation.Messages = step.Validation.Messages ?? new List<ExecutionPlanValidationMessage>();
            step.Validation.ValidatedAt = DateTime.UtcNow;
            step.Validation.Status = step.Validation.Messages.Any(m => string.Equals(m.Severity, "Error", StringComparison.OrdinalIgnoreCase))
                ? "Error"
                : step.Validation.Messages.Any(m => string.Equals(m.Severity, "Warning", StringComparison.OrdinalIgnoreCase))
                    ? "Warning"
                    : "Ready";
        }

        public static ExecutionPlanPreviewSummary ToPreviewSummary(ExcelImportPreview preview, string source, bool estimated, bool stale)
        {
            if (preview == null) return null;

            return new ExecutionPlanPreviewSummary
            {
                Rows = preview.TotalRows,
                Creates = preview.CreateCount,
                Updates = preview.UpdateCount,
                Skips = preview.SkippedCount,
                Warnings = preview.ImportErrors?.Count ?? 0,
                Errors = preview.Items?.Count(i => string.Equals(i.Action, "Skip", StringComparison.OrdinalIgnoreCase) && !string.IsNullOrWhiteSpace(i.Warnings)) ?? 0,
                Source = source,
                IsEstimated = estimated,
                IsStale = stale
            };
        }

        public static List<ExecutionPlanStep> GetCompatibleExportSteps(ExecutionPlan plan, string exportOperation)
        {
            return (plan?.Steps ?? new List<ExecutionPlanStep>())
                .Where(step => step.Enabled
                    && string.Equals(step.Operation, exportOperation, StringComparison.OrdinalIgnoreCase)
                    && !string.IsNullOrWhiteSpace(step.Output?.PathTemplate)
                    && !string.IsNullOrWhiteSpace(step.Table?.LogicalName))
                .ToList();
        }

        public static ExcelExportConfig CloneExcelConfig(ExcelExportConfig config)
        {
            if (config == null) return null;
            return JsonConvert.DeserializeObject<ExcelExportConfig>(JsonConvert.SerializeObject(config));
        }

        public static ExcelImportMatchKeySelection CloneImportMatchKeySelection(ExcelImportMatchKeySelection selection)
        {
            if (selection == null) return null;
            return new ExcelImportMatchKeySelection
            {
                Mode = selection.Mode,
                Fields = selection.Fields?.Where(f => !string.IsNullOrWhiteSpace(f)).Distinct(StringComparer.OrdinalIgnoreCase).ToList() ?? new List<string>(),
                AlternateKeyName = selection.AlternateKeyName
            };
        }

        public static ExecutionPlanStep CloneStepForEnvironment(ExecutionPlanStep source, DmtEnvironmentInfo targetEnvironment = null)
        {
            if (source == null) throw new ArgumentNullException(nameof(source));

            var clone = JsonConvert.DeserializeObject<ExecutionPlanStep>(JsonConvert.SerializeObject(source));
            clone.Id = Guid.NewGuid().ToString("D");
            clone.TargetEnvironment = CloneEnvironment(targetEnvironment ?? source.TargetEnvironment);
            clone.Name = BuildClonedStepName(source.Name, clone.TargetEnvironment);
            clone.Validation = new ExecutionPlanValidation();
            if (source.Validation?.Preview != null)
            {
                clone.Validation.Preview = JsonConvert.DeserializeObject<ExecutionPlanPreviewSummary>(
                    JsonConvert.SerializeObject(source.Validation.Preview));
                clone.Validation.Preview.IsStale = true;
                if (string.IsNullOrWhiteSpace(clone.Validation.Preview.Source))
                    clone.Validation.Preview.Source = "Cloned preview";
            }

            return clone;
        }

        private static DmtEnvironmentInfo CloneEnvironment(DmtEnvironmentInfo environment)
        {
            if (environment == null) return null;
            return new DmtEnvironmentInfo
            {
                UniqueName = environment.UniqueName,
                FriendlyName = environment.FriendlyName,
                Tag = environment.Tag
            };
        }

        private static string BuildClonedStepName(string sourceName, DmtEnvironmentInfo targetEnvironment)
        {
            var name = string.IsNullOrWhiteSpace(sourceName) ? "Cloned step" : sourceName.Trim();
            var targetName = targetEnvironment?.FriendlyName ?? targetEnvironment?.UniqueName;
            return string.IsNullOrWhiteSpace(targetName)
                ? $"{name} copy"
                : $"{name} - {targetName}";
        }

        public static ExecutionPlanStep CreateBaseStep(string operation, TableData tableData, DmtEnvironmentInfo activeTargetEnvironment, string settingsFilePath)
        {
            if (tableData?.Table == null) throw new ArgumentNullException(nameof(tableData));

            return new ExecutionPlanStep
            {
                Operation = operation,
                Table = new DmtTableInfo
                {
                    LogicalName = tableData.Table.LogicalName,
                    DisplayName = tableData.Table.DisplayName,
                    PrimaryIdAttribute = tableData.Table.IdAttribute,
                    PrimaryNameAttribute = tableData.Table.NameAttribute
                },
                TargetEnvironment = activeTargetEnvironment,
                SettingsProvenance = new ExecutionPlanSettingsProvenance
                {
                    SettingsFilePath = settingsFilePath,
                    CapturedAt = DateTime.UtcNow
                }
            };
        }

        public static string ResolveExecutionStepPath(ExecutionPlan plan, ExecutionPlanStep step, int stepIndex, ExecutionPlanPathContext context)
        {
            if (string.Equals(step?.Input?.Mode, "FromStepOutput", StringComparison.OrdinalIgnoreCase))
            {
                var sourceIndex = plan?.Steps?.FindIndex(s => string.Equals(s.Id, step.Input.SourceStepId, StringComparison.OrdinalIgnoreCase)) ?? -1;
                if (sourceIndex < 0) throw new Exception($"Linked source step was not found for '{step?.Name}'.");

                var sourceStep = plan.Steps[sourceIndex];
                return ResolvePlanPath(sourceStep.Output?.PathTemplate, sourceStep, sourceIndex + 1, context);
            }

            return ResolvePlanPath(!string.IsNullOrWhiteSpace(step?.Output?.PathTemplate) ? step.Output.PathTemplate : step?.Input?.Path, step, stepIndex, context);
        }

        public static string ResolvePlanPath(string template, ExecutionPlanStep step, int stepIndex, ExecutionPlanPathContext context)
        {
            if (string.IsNullOrWhiteSpace(template)) return template;

            var now = context?.Now ?? DateTime.Now;
            var targetName = step?.TargetEnvironment?.FriendlyName
                ?? step?.TargetEnvironment?.UniqueName
                ?? context?.FallbackTargetName;

            var resolved = template
                .Replace("{date}", now.ToString("yyyy-MM-dd"))
                .Replace("{datetime}", now.ToString("yyyy-MM-dd_HHmm"))
                .Replace("{table}", SanitizePathToken(step?.Table?.LogicalName))
                .Replace("{stepIndex}", stepIndex.ToString("00"))
                .Replace("{stepName}", SanitizePathToken(step?.Name))
                .Replace("{source}", SanitizePathToken(context?.SourceName))
                .Replace("{target}", SanitizePathToken(targetName))
                .Replace("{planName}", SanitizePathToken(context?.PlanName));

            if (!Path.IsPathRooted(resolved) && !string.IsNullOrWhiteSpace(context?.ProjectDirectory))
                resolved = Path.GetFullPath(Path.Combine(context.ProjectDirectory, resolved));

            return resolved;
        }

        public static string NormalizePlanPathForStorage(string path, string projectFilePath)
        {
            if (string.IsNullOrWhiteSpace(path) || string.IsNullOrWhiteSpace(projectFilePath) || !Path.IsPathRooted(path))
                return path;

            try
            {
                var projectDir = Path.GetDirectoryName(Path.GetFullPath(projectFilePath));
                if (string.IsNullOrWhiteSpace(projectDir)) return path;

                var fullPath = Path.GetFullPath(path);
                var baseUri = new Uri(AppendDirectorySeparatorChar(projectDir), UriKind.Absolute);
                var pathUri = new Uri(fullPath, UriKind.Absolute);
                if (baseUri.Scheme != pathUri.Scheme || !baseUri.IsBaseOf(pathUri))
                    return path;

                return Uri.UnescapeDataString(baseUri.MakeRelativeUri(pathUri).ToString())
                    .Replace('/', Path.DirectorySeparatorChar);
            }
            catch
            {
                return path;
            }
        }

        private static string AppendDirectorySeparatorChar(string path)
        {
            if (string.IsNullOrEmpty(path)) return path;
            var last = path[path.Length - 1];
            return last == Path.DirectorySeparatorChar || last == Path.AltDirectorySeparatorChar
                ? path
                : path + Path.DirectorySeparatorChar;
        }

        public static string SanitizePathToken(string value)
        {
            if (string.IsNullOrWhiteSpace(value)) return string.Empty;
            var invalid = Path.GetInvalidFileNameChars();
            return new string(value.Select(ch => invalid.Contains(ch) ? '_' : ch).ToArray());
        }

        public static void ApplyAutomaticStepLink(ExecutionPlan plan, ExecutionPlanStep importStep, string inputPath)
        {
            if (plan?.Steps == null || importStep?.Input == null || string.IsNullOrWhiteSpace(inputPath)) return;

            var expectedExportOperation = string.Equals(importStep.Operation, "ImportFromExcel", StringComparison.OrdinalIgnoreCase)
                ? "ExportToExcel"
                : string.Equals(importStep.Operation, "ImportFromJson", StringComparison.OrdinalIgnoreCase)
                    ? "ExportToJson"
                    : null;
            if (expectedExportOperation == null) return;

            var sourceStep = plan.Steps.LastOrDefault(step =>
                string.Equals(step.Operation, expectedExportOperation, StringComparison.OrdinalIgnoreCase)
                && string.Equals(step.Table?.LogicalName, importStep.Table?.LogicalName, StringComparison.OrdinalIgnoreCase)
                && PathsEqual(step.Output?.PathTemplate, inputPath));

            if (sourceStep == null) return;

            importStep.Input.Mode = "FromStepOutput";
            importStep.Input.SourceStepId = sourceStep.Id;
            importStep.Input.Path = null;
        }

        public static bool PathsEqual(string left, string right)
        {
            if (string.IsNullOrWhiteSpace(left) || string.IsNullOrWhiteSpace(right)) return false;

            try
            {
                return string.Equals(Path.GetFullPath(left), Path.GetFullPath(right), StringComparison.OrdinalIgnoreCase);
            }
            catch
            {
                return string.Equals(left, right, StringComparison.OrdinalIgnoreCase);
            }
        }

        private static string GetStepFileType(ExecutionPlanStep step)
        {
            if (string.Equals(step?.Operation, "ExportToExcel", StringComparison.OrdinalIgnoreCase)
                || string.Equals(step?.Operation, "ImportFromExcel", StringComparison.OrdinalIgnoreCase))
                return "Excel";
            if (string.Equals(step?.Operation, "ExportToJson", StringComparison.OrdinalIgnoreCase)
                || string.Equals(step?.Operation, "ImportFromJson", StringComparison.OrdinalIgnoreCase))
                return "JSON";
            return null;
        }

        private static T Clone<T>(T value) where T : class
        {
            return value == null ? null : JsonConvert.DeserializeObject<T>(JsonConvert.SerializeObject(value));
        }

        private static void EnsurePlanDefaults(ExecutionPlan plan)
        {
            if (plan == null) return;
            plan.Defaults = plan.Defaults ?? new ExecutionPlanDefaults();
            plan.Steps = plan.Steps ?? new List<ExecutionPlanStep>();
            plan.TargetEnvironments = plan.TargetEnvironments ?? new List<DmtEnvironmentInfo>();
            if (plan.TargetEnvironment != null
                && !string.IsNullOrWhiteSpace(plan.TargetEnvironment.UniqueName)
                && !plan.TargetEnvironments.Any(env => string.Equals(env.UniqueName, plan.TargetEnvironment.UniqueName, StringComparison.OrdinalIgnoreCase)))
                plan.TargetEnvironments.Add(Clone(plan.TargetEnvironment));

            foreach (var step in plan.Steps)
                EnsureStepDefaults(step);
        }

        private static void EnsureStepDefaults(ExecutionPlanStep step)
        {
            if (step == null) return;
            if (string.IsNullOrWhiteSpace(step.Id)) step.Id = Guid.NewGuid().ToString("D");
            step.Input = step.Input ?? new ExecutionPlanStepInput();
            step.Output = step.Output ?? new ExecutionPlanStepOutput();
            step.Snapshot = step.Snapshot ?? new ExecutionPlanStepSnapshot();
            step.Snapshot.SelectedAttributes = step.Snapshot.SelectedAttributes ?? new List<string>();
            step.Snapshot.PushMatchKeyFields = step.Snapshot.PushMatchKeyFields ?? new List<string>();
            step.FailurePolicy = step.FailurePolicy ?? new ExecutionPlanFailurePolicy();
            step.Validation = step.Validation ?? new ExecutionPlanValidation();
            step.Validation.Messages = step.Validation.Messages ?? new List<ExecutionPlanValidationMessage>();
        }

        private static void ValidateStep(ExecutionPlanStep step, List<string> duplicateIds, Dictionary<string, int> order, Dictionary<string, ExecutionPlanStep> steps)
        {
            EnsureStepDefaults(step);
            step.Validation.Messages.Clear();

            if (string.IsNullOrWhiteSpace(step.Id))
                AddMessage(step, "Error", "Step has no id.");
            else if (duplicateIds.Contains(step.Id))
                AddMessage(step, "Error", "Step id is duplicated.");

            if (string.IsNullOrWhiteSpace(step.Name))
                AddMessage(step, "Warning", "Step has no name.");

            if (string.IsNullOrWhiteSpace(step.Operation))
                AddMessage(step, "Error", "Step has no operation.");

            if (step.Table == null || string.IsNullOrWhiteSpace(step.Table.LogicalName))
                AddMessage(step, "Error", "Step has no table snapshot.");

            var isImport = (step.Operation ?? string.Empty).StartsWith("Import", StringComparison.OrdinalIgnoreCase);
            var isExport = IsExportStep(step);

            if (isImport)
                ValidateInput(step, order, steps);

            if (isExport)
                ValidateOutput(step);

            step.Validation.ValidatedAt = DateTime.UtcNow;
            step.Validation.Status = step.Validation.Messages.Any(m => string.Equals(m.Severity, "Error", StringComparison.OrdinalIgnoreCase))
                ? "Error"
                : step.Validation.Messages.Any(m => string.Equals(m.Severity, "Warning", StringComparison.OrdinalIgnoreCase))
                    ? "Warning"
                    : "Ready";
        }

        private static void ValidateInput(ExecutionPlanStep step, Dictionary<string, int> order, Dictionary<string, ExecutionPlanStep> steps)
        {
            var input = step.Input ?? new ExecutionPlanStepInput();
            if (string.Equals(input.Mode, "FromStepOutput", StringComparison.OrdinalIgnoreCase))
            {
                if (string.IsNullOrWhiteSpace(input.SourceStepId))
                {
                    AddMessage(step, "Error", "Linked input has no source step.");
                    return;
                }

                if (!order.ContainsKey(input.SourceStepId))
                {
                    AddMessage(step, "Error", "Linked source step was not found.");
                    return;
                }

                if (order.TryGetValue(step.Id, out var stepIndex) && order[input.SourceStepId] > stepIndex)
                    AddMessage(step, "Error", "Linked import must run after its export step.");

                if (steps.TryGetValue(input.SourceStepId, out var sourceStep))
                {
                    if (!IsExportStep(sourceStep))
                        AddMessage(step, "Error", "Linked source step is not an export step.");

                    if (!string.Equals(sourceStep.Table?.LogicalName, step.Table?.LogicalName, StringComparison.OrdinalIgnoreCase))
                        AddMessage(step, "Error", "Linked source step table does not match the import step table.");

                    if (!LinkedFileTypeMatches(sourceStep.Operation, step.Operation))
                        AddMessage(step, "Error", "Linked source step file type does not match the import operation.");
                }
                return;
            }

            if (string.IsNullOrWhiteSpace(input.Path))
                AddMessage(step, "Error", "Import input path is missing.");
            else if (!File.Exists(input.Path))
                AddMessage(step, "Warning", $"Import input file does not exist: {input.Path}");
        }

        private static bool LinkedFileTypeMatches(string sourceOperation, string importOperation)
        {
            if (string.Equals(sourceOperation, "ExportToJson", StringComparison.OrdinalIgnoreCase))
                return string.Equals(importOperation, "ImportFromJson", StringComparison.OrdinalIgnoreCase);
            if (string.Equals(sourceOperation, "ExportToExcel", StringComparison.OrdinalIgnoreCase))
                return string.Equals(importOperation, "ImportFromExcel", StringComparison.OrdinalIgnoreCase);
            return false;
        }

        private static void ValidateOutput(ExecutionPlanStep step)
        {
            var path = step.Output?.PathTemplate;
            if (string.IsNullOrWhiteSpace(path))
            {
                AddMessage(step, "Error", "Export output path is missing.");
                return;
            }

            try
            {
                var directory = Path.GetDirectoryName(path);
                if (!string.IsNullOrWhiteSpace(directory) && !Directory.Exists(directory))
                    AddMessage(step, "Info", $"Output directory will be created: {directory}");
            }
            catch (Exception ex)
            {
                AddMessage(step, "Error", $"Output path is invalid: {ex.Message}");
            }
        }

        private static void AddMessage(ExecutionPlanStep step, string severity, string message)
        {
            step.Validation.Messages.Add(new ExecutionPlanValidationMessage
            {
                Severity = severity,
                Message = message
            });
        }
    }
}
