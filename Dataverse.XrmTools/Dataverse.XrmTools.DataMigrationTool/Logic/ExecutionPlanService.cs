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
        public DateTime? Now { get; set; }
    }

    public static class ExecutionPlanService
    {
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

        public static List<Mapping> CloneMappings(IEnumerable<Mapping> mappings)
        {
            return (mappings ?? Enumerable.Empty<Mapping>())
                .Select(m => new Mapping
                {
                    Type = m.Type,
                    TableDisplayName = m.TableDisplayName,
                    TableLogicalName = m.TableLogicalName,
                    AttributeDisplayName = m.AttributeDisplayName,
                    AttributeLogicalName = m.AttributeLogicalName,
                    SourceInstanceName = m.SourceInstanceName,
                    SourceId = m.SourceId,
                    TargetId = m.TargetId,
                    TargetInstanceName = m.TargetInstanceName,
                    State = m.State
                })
                .ToList();
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
                FriendlyName = environment.FriendlyName
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

            return template
                .Replace("{date}", now.ToString("yyyy-MM-dd"))
                .Replace("{datetime}", now.ToString("yyyy-MM-dd_HHmm"))
                .Replace("{table}", SanitizePathToken(step?.Table?.LogicalName))
                .Replace("{stepIndex}", stepIndex.ToString("00"))
                .Replace("{stepName}", SanitizePathToken(step?.Name))
                .Replace("{source}", SanitizePathToken(context?.SourceName))
                .Replace("{target}", SanitizePathToken(targetName))
                .Replace("{planName}", SanitizePathToken(context?.PlanName));
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
    }
}
