// System
using System;
using System.Collections.Generic;

// DataMigrationTool
using Dataverse.XrmTools.DataMigrationTool.AppSettings;
using Dataverse.XrmTools.DataMigrationTool.Enums;
using Dataverse.XrmTools.DataMigrationTool.Logic;
using Dataverse.XrmTools.DataMigrationTool.Models;

// 3rd Party
using Xunit;

namespace Dataverse.XrmTools.DataMigrationTool.Tests
{
    public class ExecutionPlanServiceTests
    {
        [Theory]
        [InlineData("ExportToJson", "Export JSON")]
        [InlineData("ExportToExcel", "Export Excel")]
        [InlineData("ImportFromJson", "Import JSON")]
        [InlineData("SomethingElse", "SomethingElse")]
        public void GetOperationDisplayName_ReturnsFriendlyNames(string operation, string expected)
        {
            Assert.Equal(expected, ExecutionPlanService.GetOperationDisplayName(operation));
        }

        [Fact]
        public void CanMoveStep_BlocksLinkedImportBeforeSourceExport()
        {
            var export = Step("export", "ExportToJson");
            var import = Step("import", "ImportFromJson");
            import.Input.Mode = "FromStepOutput";
            import.Input.SourceStepId = export.Id;
            var plan = Plan(export, import);

            var canMove = ExecutionPlanService.CanMoveStep(plan, import, 0, out var reason);

            Assert.False(canMove);
            Assert.Contains("must stay after", reason);
        }

        [Fact]
        public void CanMoveStep_BlocksExportAfterDependentImport()
        {
            var export = Step("export", "ExportToJson");
            var import = Step("import", "ImportFromJson");
            import.Input.Mode = "FromStepOutput";
            import.Input.SourceStepId = export.Id;
            var plan = Plan(export, import);

            var canMove = ExecutionPlanService.CanMoveStep(plan, export, 1, out var reason);

            Assert.False(canMove);
            Assert.Contains("must stay before", reason);
        }

        [Fact]
        public void ApplyAutomaticStepLink_LinksImportToMatchingPreviousExport()
        {
            var export = Step("export", "ExportToExcel", "account");
            export.Output.PathTemplate = @"C:\temp\accounts.xlsx";
            var import = Step("import", "ImportFromExcel", "account");
            import.Input.Path = @"C:\temp\accounts.xlsx";
            var plan = Plan(export, import);

            ExecutionPlanService.ApplyAutomaticStepLink(plan, import, import.Input.Path);

            Assert.Equal("FromStepOutput", import.Input.Mode);
            Assert.Equal(export.Id, import.Input.SourceStepId);
            Assert.Null(import.Input.Path);
        }

        [Fact]
        public void ResolveExecutionStepPath_UsesLinkedExportOutput()
        {
            var export = Step("export", "ExportToJson", "account");
            export.Name = "Export Accounts";
            export.Output.PathTemplate = @"C:\out\{stepIndex}-{table}-{source}-{target}-{planName}-{date}.json";
            export.TargetEnvironment = new DmtEnvironmentInfo { FriendlyName = "DEV/One" };
            var import = Step("import", "ImportFromJson", "account");
            import.Input.Mode = "FromStepOutput";
            import.Input.SourceStepId = export.Id;
            var plan = Plan(export, import);
            plan.Name = "Migration: Plan";

            var path = ExecutionPlanService.ResolveExecutionStepPath(plan, import, 2, new ExecutionPlanPathContext
            {
                PlanName = plan.Name,
                SourceName = "Source*Org",
                FallbackTargetName = "Fallback",
                Now = new DateTime(2026, 5, 15, 9, 30, 0)
            });

            Assert.Contains("01-account", path);
            Assert.Contains("2026-05-15", path);
            Assert.DoesNotContain("{", path);
            Assert.DoesNotContain("*", path);
        }

        [Fact]
        public void HasReachedFailureThreshold_UsesStepPolicyBeforePlanDefaults()
        {
            var step = Step("import", "ImportFromJson");
            step.FailurePolicy.MaxFailedRecords = 2;
            step.FailurePolicy.MaxFailedPercent = 90m;
            var plan = Plan(step);
            plan.Defaults.MaxFailedRecords = 10;
            plan.Defaults.MaxFailedPercent = 90m;

            Assert.True(ExecutionPlanService.HasReachedFailureThreshold(plan, step, 2, 100));
            Assert.False(ExecutionPlanService.HasReachedFailureThreshold(plan, step, 1, 100));
        }

        [Fact]
        public void TryValidateTargetConnection_AllowsFallbackTargetWhenStepHasNoTarget()
        {
            var step = Step("import", "ImportFromJson");
            step.TargetEnvironment = null;

            var valid = ExecutionPlanService.TryValidateTargetConnection(step, new string[0], hasFallbackTarget: true, out var error);

            Assert.True(valid);
            Assert.Null(error);
        }

        [Fact]
        public void TryValidateTargetConnection_FlagsMissingFallbackTarget()
        {
            var step = Step("import", "ImportFromJson");
            step.TargetEnvironment = null;

            var valid = ExecutionPlanService.TryValidateTargetConnection(step, new string[0], hasFallbackTarget: false, out var error);

            Assert.False(valid);
            Assert.Equal("No target connection is available.", error);
        }

        [Fact]
        public void TryValidateTargetConnection_RequiresSpecificConnectedTarget()
        {
            var step = Step("import-dev", "ImportFromJson");
            step.TargetEnvironment = new DmtEnvironmentInfo { UniqueName = "dev", FriendlyName = "DEV" };

            var valid = ExecutionPlanService.TryValidateTargetConnection(step, new[] { "test" }, hasFallbackTarget: true, out var error);

            Assert.False(valid);
            Assert.Equal("Target environment is not connected: DEV", error);

            Assert.True(ExecutionPlanService.TryValidateTargetConnection(step, new[] { "DEV" }, hasFallbackTarget: false, out _));
        }

        [Fact]
        public void CanExecuteValidatedPlan_RequiresValidationEnabledStepsAndNoEnabledErrors()
        {
            var disabledError = Step("disabled", "ImportFromJson");
            disabledError.Enabled = false;
            disabledError.Validation.Status = "Error";
            var ready = Step("ready", "ImportFromJson");
            ready.Validation.Status = "Ready";
            var plan = Plan(disabledError, ready);

            Assert.True(ExecutionPlanService.CanExecuteValidatedPlan(plan, validatedForExecution: true));
            Assert.False(ExecutionPlanService.CanExecuteValidatedPlan(plan, validatedForExecution: false));

            ready.Validation.Status = "Error";
            Assert.False(ExecutionPlanService.CanExecuteValidatedPlan(plan, validatedForExecution: true));
        }

        [Fact]
        public void GetExecutableSteps_SkipsDisabledSteps()
        {
            var enabled = Step("enabled", "ImportFromJson");
            var disabled = Step("disabled", "ImportFromJson");
            disabled.Enabled = false;
            var plan = Plan(enabled, disabled);

            var steps = ExecutionPlanService.GetExecutableSteps(plan);

            Assert.Single(steps);
            Assert.Equal(enabled.Id, steps[0].Id);
        }

        [Fact]
        public void BuildExecutionStepResult_CalculatesFailureSummaryAndThreshold()
        {
            var step = Step("import", "ImportFromJson");
            step.FailurePolicy.MaxFailedRecords = 2;
            var plan = Plan(step);

            var result = ExecutionPlanService.BuildExecutionStepResult(
                plan,
                step,
                "imported JSON",
                new[] { "Created", "ERROR: bad row", "ERROR: other bad row" });

            Assert.Equal(3, result.TotalRecords);
            Assert.Equal(2, result.FailedRecords);
            Assert.Equal(66.67m, result.FailedPercent);
            Assert.True(result.HasFailures);
            Assert.True(result.ShouldStopPlan);
            Assert.Contains("failure threshold reached", result.Summary);
        }

        [Fact]
        public void RunLogHelpers_CreateStepLogsAndApplyResults()
        {
            var export = Step("export", "ExportToJson");
            export.Name = "Export Accounts";
            export.Output.PathTemplate = @"C:\out\{stepIndex}-{table}-{target}.json";
            export.TargetEnvironment = new DmtEnvironmentInfo { UniqueName = "dev", FriendlyName = "DEV" };
            var plan = Plan(export);
            plan.Name = "Plan A";
            var started = new DateTime(2026, 5, 15, 10, 0, 0, DateTimeKind.Utc);

            var runLog = ExecutionPlanService.CreateRunLog(
                plan,
                @"C:\plans\plan.dmtplan.json",
                new DmtEnvironmentInfo { UniqueName = "source", FriendlyName = "Source" },
                new DmtEnvironmentInfo { UniqueName = "default", FriendlyName = "Default" },
                new[] { export.TargetEnvironment },
                started);
            var stepLog = ExecutionPlanService.CreateRunStepLog(
                plan,
                export,
                1,
                new ExecutionPlanPathContext { Now = started, FallbackTargetName = "Default" });
            var result = ExecutionPlanService.BuildExecutionStepResult(plan, export, "exported JSON", 10, 0);
            ExecutionPlanService.ApplyExecutionResultToLog(stepLog, result);
            runLog.Steps.Add(stepLog);

            Assert.Equal("Plan A", runLog.PlanName);
            Assert.Equal(started, runLog.StartedOn);
            Assert.Single(runLog.TargetEnvironments);
            Assert.Equal("Success", stepLog.Status);
            Assert.Equal(10, stepLog.TotalRecords);
            Assert.Contains("01-account-DEV", stepLog.Path);
        }

        [Fact]
        public void MarkSkippedDueToFailedDependency_ProducesSkippedLogSummary()
        {
            var step = Step("import", "ImportFromJson");
            step.Name = "Import Accounts";
            var log = new ExecutionPlanRunStepLog();

            ExecutionPlanService.MarkSkippedDueToFailedDependency(log, step);

            Assert.Equal("Skipped", log.Status);
            Assert.Contains("linked dependency failed", log.Summary);
        }

        [Fact]
        public void AddValidationMessage_RefreshesStepStatus()
        {
            var step = Step("import", "ImportFromJson");

            ExecutionPlanService.AddValidationMessage(step, "Warning", "Check this");
            Assert.Equal("Warning", step.Validation.Status);

            ExecutionPlanService.AddValidationMessage(step, "Error", "Fix this");
            Assert.Equal("Error", step.Validation.Status);
            Assert.NotNull(step.Validation.ValidatedAt);
        }

        [Fact]
        public void GetCompatibleExportSteps_ReturnsEnabledExportsWithOutputAndTable()
        {
            var included = Step("included", "ExportToJson", "account");
            included.Output.PathTemplate = "accounts.json";
            var disabled = Step("disabled", "ExportToJson", "account");
            disabled.Enabled = false;
            disabled.Output.PathTemplate = "disabled.json";
            var noOutput = Step("no-output", "ExportToJson", "account");
            var import = Step("import", "ImportFromJson", "account");
            var plan = Plan(included, disabled, noOutput, import);

            var result = ExecutionPlanService.GetCompatibleExportSteps(plan, "ExportToJson");

            Assert.Single(result);
            Assert.Equal(included.Id, result[0].Id);
        }

        [Fact]
        public void CloneMappings_ReturnsIndependentCopies()
        {
            var mappings = new List<Mapping>
            {
                new Mapping
                {
                    Type = MappingType.Attribute,
                    TableLogicalName = "account",
                    AttributeLogicalName = "ownerid",
                    SourceInstanceName = "Source",
                    TargetInstanceName = "Target",
                    State = MappingState.Existing
                }
            };

            var clone = ExecutionPlanService.CloneMappings(mappings);
            clone[0].TargetInstanceName = "Changed";

            Assert.Single(clone);
            Assert.Equal("Target", mappings[0].TargetInstanceName);
            Assert.Equal("Changed", clone[0].TargetInstanceName);
        }

        [Fact]
        public void CloneExcelConfig_ReturnsIndependentCopy()
        {
            var config = new ExcelExportConfig
            {
                MatchKeyMode = "AlternateKey",
                MatchKeys = new List<string> { "accountnumber" },
                Table = new ExcelTableConfig { LogicalName = "account" }
            };

            var clone = ExecutionPlanService.CloneExcelConfig(config);
            clone.MatchKeys.Add("name");

            Assert.NotSame(config, clone);
            Assert.Single(config.MatchKeys);
            Assert.Equal(2, clone.MatchKeys.Count);
        }

        [Fact]
        public void CloneImportMatchKeySelection_ReturnsIndependentNormalizedCopy()
        {
            var selection = new ExcelImportMatchKeySelection
            {
                Mode = "Custom",
                Fields = new List<string> { "accountnumber", "AccountNumber", null, "name" },
                AlternateKeyName = "account_alt_key"
            };

            var clone = ExecutionPlanService.CloneImportMatchKeySelection(selection);
            clone.Fields.Add("emailaddress1");

            Assert.NotSame(selection, clone);
            Assert.Equal("Custom", clone.Mode);
            Assert.Equal(new[] { "accountnumber", "name", "emailaddress1" }, clone.Fields);
            Assert.Equal(new[] { "accountnumber", "AccountNumber", null, "name" }, selection.Fields);
        }

        [Fact]
        public void CreateBaseStep_CapturesTableTargetAndSettingsProvenance()
        {
            var tableData = new TableData
            {
                Table = new Table
                {
                    LogicalName = "account",
                    DisplayName = "Account",
                    IdAttribute = "accountid",
                    NameAttribute = "name"
                }
            };
            var target = new DmtEnvironmentInfo { UniqueName = "dev", FriendlyName = "DEV" };

            var step = ExecutionPlanService.CreateBaseStep("ExportToJson", tableData, target, @"C:\plans\account.dmt.json");

            Assert.Equal("ExportToJson", step.Operation);
            Assert.Equal("account", step.Table.LogicalName);
            Assert.Equal("accountid", step.Table.PrimaryIdAttribute);
            Assert.Equal(target, step.TargetEnvironment);
            Assert.Equal(@"C:\plans\account.dmt.json", step.SettingsProvenance.SettingsFilePath);
        }

        private static ExecutionPlan Plan(params ExecutionPlanStep[] steps)
        {
            var plan = new ExecutionPlan();
            plan.Steps.AddRange(steps);
            return plan;
        }

        private static ExecutionPlanStep Step(string id, string operation, string table = "account")
        {
            return new ExecutionPlanStep
            {
                Id = id,
                Name = id,
                Operation = operation,
                Table = new DmtTableInfo { LogicalName = table, DisplayName = table }
            };
        }
    }
}
