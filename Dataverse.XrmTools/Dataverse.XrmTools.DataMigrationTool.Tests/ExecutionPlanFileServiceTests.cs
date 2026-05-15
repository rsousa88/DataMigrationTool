// System
using System.Linq;

// DataMigrationTool
using Dataverse.XrmTools.DataMigrationTool.Logic;
using Dataverse.XrmTools.DataMigrationTool.Models;
using Dataverse.XrmTools.DataMigrationTool.Tests.TestSupport;

// 3rd Party
using Xunit;

namespace Dataverse.XrmTools.DataMigrationTool.Tests
{
    public class ExecutionPlanFileServiceTests
    {
        [Fact]
        public void CreateNew_CapturesSourceAndTargetEnvironments()
        {
            var plan = ExecutionPlanFileService.CreateNew(@"C:\plans\migration.dmtplan.json", "source", "Source", "target", "Target");

            Assert.Equal("migration.dmtplan", plan.Name);
            Assert.Equal("source", plan.SourceEnvironment.UniqueName);
            Assert.Equal("target", plan.TargetEnvironment.UniqueName);
            Assert.Single(plan.TargetEnvironments);
        }

        [Fact]
        public void SaveAndLoad_RoundTripsPlanAndEnsuresDefaults()
        {
            using (var scope = new TemporaryFileScope())
            {
                var path = scope.GetPath("migration.dmtplan.json");
                var step = TestDataBuilder.ExecutionPlanStep("export", "ExportToJson");
                step.Output.PathTemplate = scope.GetPath("accounts.json");
                step.Input = null;
                step.Output = null;
                step.Snapshot = null;
                step.FailurePolicy = null;
                step.Validation = null;
                var plan = TestDataBuilder.ExecutionPlan(step);

                ExecutionPlanFileService.Save(path, plan);
                var loaded = ExecutionPlanFileService.Load(path);

                Assert.Equal("Test Plan", loaded.Name);
                Assert.NotNull(loaded.Steps[0].Input);
                Assert.NotNull(loaded.Steps[0].Output);
                Assert.NotNull(loaded.Steps[0].Snapshot);
                Assert.NotNull(loaded.Steps[0].FailurePolicy);
                Assert.NotNull(loaded.Steps[0].Validation);
            }
        }

        [Fact]
        public void ValidatePlan_FlagsMissingOperationAndExportOutput()
        {
            var step = TestDataBuilder.ExecutionPlanStep("export", null);
            step.Output.PathTemplate = null;
            var plan = TestDataBuilder.ExecutionPlan(step);

            ExecutionPlanFileService.ValidatePlan(plan);

            Assert.Equal("Error", step.Validation.Status);
            Assert.Contains(step.Validation.Messages, m => m.Message == "Step has no operation.");
        }

        [Fact]
        public void ValidatePlan_FlagsImportMissingFileAsWarning()
        {
            var step = TestDataBuilder.ExecutionPlanStep("import", "ImportFromJson");
            step.Input.Path = @"C:\definitely-missing\input.json";
            var plan = TestDataBuilder.ExecutionPlan(step);

            ExecutionPlanFileService.ValidatePlan(plan);

            Assert.Equal("Warning", step.Validation.Status);
            Assert.Contains(step.Validation.Messages, m => m.Message.StartsWith("Import input file does not exist:"));
        }

        [Fact]
        public void ValidatePlan_FlagsInvalidLinkedStepOrder()
        {
            var import = TestDataBuilder.ExecutionPlanStep("import", "ImportFromJson");
            import.Input.Mode = "FromStepOutput";
            import.Input.SourceStepId = "export";
            var export = TestDataBuilder.ExecutionPlanStep("export", "ExportToJson");
            export.Output.PathTemplate = "accounts.json";
            var plan = TestDataBuilder.ExecutionPlan(import, export);

            ExecutionPlanFileService.ValidatePlan(plan);

            Assert.Equal("Error", import.Validation.Status);
            Assert.Contains(import.Validation.Messages, m => m.Message == "Linked import must run after its export step.");
        }

        [Fact]
        public void ValidatePlan_FlagsLinkedFileTypeMismatch()
        {
            var export = TestDataBuilder.ExecutionPlanStep("export", "ExportToExcel");
            export.Output.PathTemplate = "accounts.xlsx";
            var import = TestDataBuilder.ExecutionPlanStep("import", "ImportFromJson");
            import.Input.Mode = "FromStepOutput";
            import.Input.SourceStepId = export.Id;
            var plan = TestDataBuilder.ExecutionPlan(export, import);

            ExecutionPlanFileService.ValidatePlan(plan);

            Assert.Equal("Error", import.Validation.Status);
            Assert.Contains(import.Validation.Messages, m => m.Message == "Linked source step file type does not match the import operation.");
        }

        [Fact]
        public void ValidatePlan_FlagsDuplicateStepIds()
        {
            var first = TestDataBuilder.ExecutionPlanStep("same", "ExportToJson");
            first.Output.PathTemplate = "first.json";
            var second = TestDataBuilder.ExecutionPlanStep("same", "ExportToJson");
            second.Output.PathTemplate = "second.json";
            var plan = TestDataBuilder.ExecutionPlan(first, second);

            ExecutionPlanFileService.ValidatePlan(plan);

            Assert.All(plan.Steps, step => Assert.Contains(step.Validation.Messages, m => m.Message == "Step id is duplicated."));
        }
    }
}
