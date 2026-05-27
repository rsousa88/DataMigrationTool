// System
using System;
using System.Collections.Generic;

// DataMigrationTool
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
            Assert.Equal(new[] { "ERROR: bad row", "ERROR: other bad row" }, result.ErrorDetails);
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
                @"C:\projects\migration.dmtproj",
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
        public void CloneStepForEnvironment_PreservesConfigurationWithNewIdAndTarget()
        {
            var source = Step("import-dev", "ImportFromExcel");
            source.Name = "Import Accounts";
            source.TargetEnvironment = new DmtEnvironmentInfo { UniqueName = "dev", FriendlyName = "DEV" };
            source.Input.Mode = "FromStepOutput";
            source.Input.SourceStepId = "export-1";
            source.Snapshot.SelectedAttributes.Add("name");
            source.Validation.Status = "Ready";
            source.Validation.Preview = new ExecutionPlanPreviewSummary { Rows = 10, Creates = 4, Source = "Captured preview" };
            var target = new DmtEnvironmentInfo { UniqueName = "test", FriendlyName = "TEST" };

            var clone = ExecutionPlanService.CloneStepForEnvironment(source, target);

            Assert.NotEqual(source.Id, clone.Id);
            Assert.Equal("Import Accounts - TEST", clone.Name);
            Assert.Equal("test", clone.TargetEnvironment.UniqueName);
            Assert.Equal("FromStepOutput", clone.Input.Mode);
            Assert.Equal("export-1", clone.Input.SourceStepId);
            Assert.Equal("Unknown", clone.Validation.Status);
            Assert.True(clone.Validation.Preview.IsStale);
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

        [Fact]
        public void ProjectPlanConversion_RoundTripsStepsWithoutDmtPlanFile()
        {
            var export = Step("export-1", "ExportToExcel");
            export.Name = "Export Accounts";
            export.Output.PathTemplate = @"exports\accounts.xlsx";
            export.Snapshot.SelectedAttributes.Add("name");
            export.Snapshot.ExportSettings = new UiSettings { BatchSize = 50 };

            var import = Step("import-1", "ImportFromExcel");
            import.TargetEnvironment = new DmtEnvironmentInfo { UniqueName = "test-env", FriendlyName = "TEST" };
            import.Input.Mode = "FromStepOutput";
            import.Input.SourceStepId = export.Id;
            import.Snapshot.ImportMatchKeySelection = new ExcelImportMatchKeySelection
            {
                Mode = "Custom",
                Fields = new List<string> { "accountnumber" }
            };
            var plan = Plan(export, import);
            plan.Name = "Project Plan";
            plan.Defaults.MaxFailedRecords = 3;
            var planRow = ExecutionPlanService.ToProjectPlan(plan, "plan-1");
            var stepRows = ExecutionPlanService.ToProjectPlanSteps(plan, planRow.Id, "source-env");

            var loaded = ExecutionPlanService.FromProjectPlan(
                planRow,
                stepRows,
                new DmtEnvironmentInfo { UniqueName = "source-env", FriendlyName = "Source" },
                new[] { new DmtEnvironmentInfo { UniqueName = "test-env", FriendlyName = "TEST" } });

            Assert.Equal("Project Plan", loaded.Name);
            Assert.Equal(3, loaded.Defaults.MaxFailedRecords);
            Assert.Equal(2, loaded.Steps.Count);
            Assert.Equal(@"exports\accounts.xlsx", loaded.Steps[0].Output.PathTemplate);
            Assert.Equal("name", Assert.Single(loaded.Steps[0].Snapshot.SelectedAttributes));
            Assert.Equal("FromStepOutput", loaded.Steps[1].Input.Mode);
            Assert.Equal("export-1", loaded.Steps[1].Input.SourceStepId);
            Assert.Equal("test-env", loaded.Steps[1].TargetEnvironment.UniqueName);
            Assert.Equal("accountnumber", Assert.Single(loaded.Steps[1].Snapshot.ImportMatchKeySelection.Fields));
        }

        [Theory]
        [InlineData(@"C:\projects\migration.dmtproj", @"C:\projects\exports\accounts.json", @"exports\accounts.json")]
        [InlineData(@"C:\projects\migration.dmtproj", @"C:\projects\accounts.json", @"accounts.json")]
        [InlineData(@"C:\projects\migration.dmtproj", @"D:\other\accounts.json", @"D:\other\accounts.json")]
        [InlineData(@"C:\projects\migration.dmtproj", @"exports\accounts.json", @"exports\accounts.json")]
        [InlineData(@"C:\projects\migration.dmtproj", null, null)]
        public void NormalizePlanPathForStorage_MakesInsideProjectRelative(string projectFile, string inputPath, string expected)
        {
            var result = ExecutionPlanService.NormalizePlanPathForStorage(inputPath, projectFile);
            Assert.Equal(expected, result);
        }

        [Fact]
        public void ResolveExecutionStepPath_ResolvesRelativePathAgainstProjectDirectory()
        {
            var step = Step("export", "ExportToJson", "account");
            step.Output.PathTemplate = @"exports\accounts.json";
            var plan = Plan(step);

            var path = ExecutionPlanService.ResolveExecutionStepPath(plan, step, 1, new ExecutionPlanPathContext
            {
                ProjectDirectory = @"C:\projects",
                Now = new DateTime(2026, 5, 15)
            });

            Assert.Equal(@"C:\projects\exports\accounts.json", path);
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
