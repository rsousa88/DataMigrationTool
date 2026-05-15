// System
using System;

// DataMigrationTool
using Dataverse.XrmTools.DataMigrationTool.Logic;

// 3rd Party
using Xunit;

namespace Dataverse.XrmTools.DataMigrationTool.Tests
{
    public class ImportWorkflowServiceTests
    {
        [Theory]
        [InlineData(999, LargeImportWarningLevel.None)]
        [InlineData(1000, LargeImportWarningLevel.Information)]
        [InlineData(5000, LargeImportWarningLevel.Warning)]
        [InlineData(20000, LargeImportWarningLevel.Warning)]
        public void GetLargeExcelImportWarning_UsesConfiguredThresholds(int rowCount, LargeImportWarningLevel expectedLevel)
        {
            var warning = ImportWorkflowService.GetLargeExcelImportWarning(rowCount);

            Assert.Equal(expectedLevel, warning.Level);
            Assert.Equal(expectedLevel != LargeImportWarningLevel.None, warning.ShouldConfirm);
        }

        [Fact]
        public void GetLargeExcelImportWarning_IncludesFormattedRowCount()
        {
            var warning = ImportWorkflowService.GetLargeExcelImportWarning(20000);

            Assert.Contains("20,000", warning.Message);
            Assert.Contains("Continue?", warning.Message);
            Assert.Contains(Environment.NewLine, warning.Message);
        }
    }
}
