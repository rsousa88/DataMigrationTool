// System
using System;
using System.IO;
using System.Linq;

// Newtonsoft
using Newtonsoft.Json;

// DataMigrationTool
using Dataverse.XrmTools.DataMigrationTool.Models;

namespace Dataverse.XrmTools.DataMigrationTool.Logic
{
    public static class ExecutionPlanFileService
    {
        private static readonly JsonSerializerSettings _jsonSettings = new JsonSerializerSettings
        {
            Formatting = Formatting.Indented,
            NullValueHandling = NullValueHandling.Ignore
        };

        public static ExecutionPlan Load(string filePath)
        {
            var json = File.ReadAllText(filePath);
            var plan = JsonConvert.DeserializeObject<ExecutionPlan>(json, _jsonSettings)
                ?? throw new Exception("Execution plan file is empty or invalid.");

            EnsureDefaults(plan, filePath);
            return plan;
        }

        public static void Save(string filePath, ExecutionPlan plan)
        {
            if (plan == null) throw new ArgumentNullException(nameof(plan));

            plan.UpdatedOn = DateTime.UtcNow;
            if (string.IsNullOrWhiteSpace(plan.Name))
                plan.Name = Path.GetFileNameWithoutExtension(filePath);

            var json = JsonConvert.SerializeObject(plan, _jsonSettings);
            File.WriteAllText(filePath, json);
        }

        public static void SaveRunLog(string filePath, ExecutionPlanRunLog runLog)
        {
            var json = JsonConvert.SerializeObject(runLog, _jsonSettings);
            File.WriteAllText(filePath, json);
        }

        public static ExecutionPlan CreateNew(string filePath, string sourceUniqueName, string sourceFriendlyName, string targetUniqueName, string targetFriendlyName)
        {
            return new ExecutionPlan
            {
                Name = string.IsNullOrWhiteSpace(filePath) ? "Execution Plan" : Path.GetFileNameWithoutExtension(filePath),
                SourceEnvironment = new DmtEnvironmentInfo
                {
                    UniqueName = sourceUniqueName,
                    FriendlyName = sourceFriendlyName
                },
                TargetEnvironment = new DmtEnvironmentInfo
                {
                    UniqueName = targetUniqueName,
                    FriendlyName = targetFriendlyName
                },
                TargetEnvironments = string.IsNullOrWhiteSpace(targetUniqueName)
                    ? new System.Collections.Generic.List<DmtEnvironmentInfo>()
                    : new System.Collections.Generic.List<DmtEnvironmentInfo>
                    {
                        new DmtEnvironmentInfo
                        {
                            UniqueName = targetUniqueName,
                            FriendlyName = targetFriendlyName
                        }
                    }
            };
        }

        public static void ValidatePlan(ExecutionPlan plan)
        {
            if (plan == null) return;

            var ids = plan.Steps.Select(s => s.Id).Where(id => !string.IsNullOrWhiteSpace(id)).ToList();
            var duplicateIds = ids.GroupBy(id => id, StringComparer.OrdinalIgnoreCase).Where(g => g.Count() > 1).Select(g => g.Key).ToList();
            var order = new System.Collections.Generic.Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            var steps = new System.Collections.Generic.Dictionary<string, ExecutionPlanStep>(StringComparer.OrdinalIgnoreCase);
            for (var i = 0; i < plan.Steps.Count; i++)
            {
                var step = plan.Steps[i];
                if (string.IsNullOrWhiteSpace(step.Id) || order.ContainsKey(step.Id)) continue;

                order[step.Id] = i;
                steps[step.Id] = step;
            }

            foreach (var step in plan.Steps)
            {
                ValidateStep(step, duplicateIds, order, steps);
            }
        }

        private static void ValidateStep(ExecutionPlanStep step, System.Collections.Generic.List<string> duplicateIds, System.Collections.Generic.Dictionary<string, int> order, System.Collections.Generic.Dictionary<string, ExecutionPlanStep> steps)
        {
            step.Validation = step.Validation ?? new ExecutionPlanValidation();
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
            var isExport = (step.Operation ?? string.Empty).StartsWith("Export", StringComparison.OrdinalIgnoreCase);

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

        private static void ValidateInput(ExecutionPlanStep step, System.Collections.Generic.Dictionary<string, int> order, System.Collections.Generic.Dictionary<string, ExecutionPlanStep> steps)
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
                    if (!(sourceStep.Operation ?? string.Empty).StartsWith("Export", StringComparison.OrdinalIgnoreCase))
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

        private static void EnsureDefaults(ExecutionPlan plan, string filePath)
        {
            plan.Defaults = plan.Defaults ?? new ExecutionPlanDefaults();
            plan.Steps = plan.Steps ?? new System.Collections.Generic.List<ExecutionPlanStep>();
            plan.TargetEnvironments = plan.TargetEnvironments ?? new System.Collections.Generic.List<DmtEnvironmentInfo>();
            if (plan.TargetEnvironment != null
                && !string.IsNullOrWhiteSpace(plan.TargetEnvironment.UniqueName)
                && !plan.TargetEnvironments.Any(env => string.Equals(env.UniqueName, plan.TargetEnvironment.UniqueName, StringComparison.OrdinalIgnoreCase)))
                plan.TargetEnvironments.Add(plan.TargetEnvironment);
            if (string.IsNullOrWhiteSpace(plan.Name))
                plan.Name = Path.GetFileNameWithoutExtension(filePath);

            foreach (var step in plan.Steps)
            {
                if (string.IsNullOrWhiteSpace(step.Id)) step.Id = Guid.NewGuid().ToString("D");
                step.Input = step.Input ?? new ExecutionPlanStepInput();
                step.Output = step.Output ?? new ExecutionPlanStepOutput();
                step.Snapshot = step.Snapshot ?? new ExecutionPlanStepSnapshot();
                step.FailurePolicy = step.FailurePolicy ?? new ExecutionPlanFailurePolicy();
                step.Validation = step.Validation ?? new ExecutionPlanValidation();
            }
        }
    }
}
