// Microsoft
using Microsoft.Xrm.Sdk.Query;

// DataMigrationTool
using Dataverse.XrmTools.DataMigrationTool.Tests.TestSupport;

// 3rd Party
using Xunit;

namespace Dataverse.XrmTools.DataMigrationTool.Tests.TestSupport
{
    public class FakeOrganizationServiceTests
    {
        [Fact]
        public void CreateAndRetrieve_RoundTripsEntity()
        {
            var service = new FakeOrganizationService();
            var entity = TestDataBuilder.Entity();

            var id = service.Create(entity);
            var retrieved = service.Retrieve(entity.LogicalName, id, new ColumnSet(true));

            Assert.Equal(id, retrieved.Id);
            Assert.Single(service.CreatedEntities);
        }
    }
}
