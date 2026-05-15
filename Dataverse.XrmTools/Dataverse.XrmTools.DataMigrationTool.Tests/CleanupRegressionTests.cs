// DataMigrationTool
using Dataverse.XrmTools.DataMigrationTool.AppSettings;

// 3rd Party
using Xunit;

namespace Dataverse.XrmTools.DataMigrationTool.Tests
{
    public class CleanupRegressionTests
    {
        [Fact]
        public void Settings_DoesNotExposeObsoleteLastDataFileState()
        {
            Assert.Null(typeof(Settings).GetProperty("LastDataFile"));
        }
    }
}
