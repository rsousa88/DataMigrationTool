// DataMigrationTool
using Dataverse.XrmTools.DataMigrationTool.Tests.TestSupport;

// 3rd Party
using Xunit;

namespace Dataverse.XrmTools.DataMigrationTool.Tests.TestSupport
{
    public class TestDataBuilderTests
    {
        [Fact]
        public void TableData_CreatesMatchingTableAndMetadata()
        {
            var tableData = TestDataBuilder.TableData();

            Assert.Equal("account", tableData.Table.LogicalName);
            Assert.Equal("account", tableData.Metadata.LogicalName);
            Assert.NotEmpty(tableData.SelectedAttributes);
        }

        [Fact]
        public void ExecutionPlan_CanSeedSteps()
        {
            var step = TestDataBuilder.ExecutionPlanStep();

            var plan = TestDataBuilder.ExecutionPlan(step);

            Assert.Single(plan.Steps);
            Assert.Equal("Test Plan", plan.Name);
        }
    }
}
