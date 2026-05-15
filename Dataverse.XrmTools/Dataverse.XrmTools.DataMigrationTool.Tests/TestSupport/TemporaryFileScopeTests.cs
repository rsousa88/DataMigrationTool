// System
using System.IO;

// DataMigrationTool
using Dataverse.XrmTools.DataMigrationTool.Tests.TestSupport;

// 3rd Party
using Xunit;

namespace Dataverse.XrmTools.DataMigrationTool.Tests.TestSupport
{
    public class TemporaryFileScopeTests
    {
        [Fact]
        public void WriteJson_CreatesFileInTemporaryDirectory()
        {
            string directory;
            string path;
            using (var scope = new TemporaryFileScope())
            {
                directory = scope.DirectoryPath;
                path = scope.WriteJson("plan.json", TestDataBuilder.ExecutionPlan());

                Assert.True(File.Exists(path));
                Assert.StartsWith(directory, path);
            }

            Assert.False(Directory.Exists(directory));
        }

        [Fact]
        public void GetExcelPath_ReturnsXlsxPathWithoutCreatingFile()
        {
            using (var scope = new TemporaryFileScope())
            {
                var path = scope.GetExcelPath();

                Assert.EndsWith(".xlsx", path);
                Assert.False(File.Exists(path));
            }
        }
    }
}
