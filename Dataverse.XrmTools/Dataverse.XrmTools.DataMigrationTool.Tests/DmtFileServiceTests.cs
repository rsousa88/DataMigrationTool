// System
using System.Collections.Generic;

// DataMigrationTool
using Dataverse.XrmTools.DataMigrationTool.AppSettings;
using Dataverse.XrmTools.DataMigrationTool.Logic;
using Dataverse.XrmTools.DataMigrationTool.Models;
using Dataverse.XrmTools.DataMigrationTool.Tests.TestSupport;

// 3rd Party
using Xunit;

namespace Dataverse.XrmTools.DataMigrationTool.Tests
{
    public class DmtFileServiceTests
    {
        [Fact]
        public void CreateNew_CapturesEnvironmentTableAndMigratesExistingAppDataSettings()
        {
            var existing = new TableSettings
            {
                DeselectedAttributes = new List<string> { "name" },
                Filter = "<fetch />",
                ExcelConfig = TestDataBuilder.ExcelExportConfig()
            };

            var settings = DmtFileService.CreateNew(
                "account",
                "Account",
                "accountid",
                "name",
                "ORG",
                "Org",
                existing);

            Assert.Equal("ORG", settings.Environment.UniqueName);
            Assert.Equal("account", settings.Table.LogicalName);
            Assert.Equal("accountid", settings.Table.PrimaryIdAttribute);
            Assert.Equal(existing.DeselectedAttributes, settings.DeselectedAttributes);
            Assert.Equal(existing.Filter, settings.Filter);
            Assert.Same(existing.ExcelConfig, settings.ExcelConfig);
        }

        [Fact]
        public void ValidateEnvironment_MatchesUniqueNameCaseInsensitively()
        {
            var settings = new DmtSettings
            {
                Environment = new DmtEnvironmentInfo { UniqueName = "ORG", FriendlyName = "Org" }
            };

            var result = DmtFileService.ValidateEnvironment(settings, "org", "Org");

            Assert.True(result.matches);
            Assert.Null(result.warning);
        }

        [Fact]
        public void ValidateEnvironment_ReturnsWarningForDifferentEnvironment()
        {
            var settings = new DmtSettings
            {
                Environment = new DmtEnvironmentInfo { UniqueName = "SOURCE", FriendlyName = "Source" }
            };

            var result = DmtFileService.ValidateEnvironment(settings, "TARGET", "Target");

            Assert.False(result.matches);
            Assert.Contains("Source", result.warning);
            Assert.Contains("Target", result.warning);
        }

        [Fact]
        public void ValidateTable_MatchesLogicalNameCaseInsensitively()
        {
            var settings = new DmtSettings
            {
                Table = new DmtTableInfo { LogicalName = "account" }
            };

            Assert.True(DmtFileService.ValidateTable(settings, "ACCOUNT"));
            Assert.False(DmtFileService.ValidateTable(settings, "contact"));
        }

        [Fact]
        public void SaveAndLoad_RoundTripsSettingsFile()
        {
            using (var scope = new TemporaryFileScope())
            {
                var path = scope.GetPath("account.dmt.json");
                var settings = DmtFileService.CreateNew("account", "Account", "accountid", "name", "org", "Org");

                DmtFileService.Save(path, settings);
                var loaded = DmtFileService.Load(path);

                Assert.Equal("account", loaded.Table.LogicalName);
                Assert.Equal("org", loaded.Environment.UniqueName);
            }
        }
    }
}
