// System
using System;
using System.Collections.Generic;
using System.Linq;

// DataMigrationTool
using Dataverse.XrmTools.DataMigrationTool.Logic;
using Dataverse.XrmTools.DataMigrationTool.Models;
using DmtRecord = Dataverse.XrmTools.DataMigrationTool.Models.Record;

// 3rd Party
using Xunit;

namespace Dataverse.XrmTools.DataMigrationTool.Tests
{
    public class RecordCollectionServiceTests
    {
        [Fact]
        public void FilterByIds_ReturnsMatchingGuidAndStringIds()
        {
            var firstId = Guid.NewGuid();
            var secondId = Guid.NewGuid();
            var ignoredId = Guid.NewGuid();
            var collection = new RecordCollection
            {
                LogicalName = "account",
                PrimaryIdAttribute = "accountid",
                ImportErrors = new List<string> { "warning" },
                Records = new List<DmtRecord>
                {
                    RecordWithId("accountid", firstId),
                    RecordWithId("ACCOUNTID", secondId.ToString()),
                    RecordWithId("accountid", ignoredId)
                }
            };

            var filtered = RecordCollectionService.FilterByIds(collection, new[] { firstId, secondId });

            Assert.Equal(2, filtered.Count);
            Assert.Equal(collection.ImportErrors, filtered.ImportErrors);
            Assert.All(filtered.Records, record =>
                Assert.True(RecordCollectionService.TryGetRecordId(record, "accountid", out _)));
        }

        [Fact]
        public void TryGetRecordId_ReturnsFalseForMissingOrInvalidIds()
        {
            Assert.False(RecordCollectionService.TryGetRecordId(null, "accountid", out _));
            Assert.False(RecordCollectionService.TryGetRecordId(RecordWithId("accountid", "not-a-guid"), "accountid", out _));
            Assert.False(RecordCollectionService.TryGetRecordId(RecordWithId("name", "Contoso"), "accountid", out _));
        }

        private static DmtRecord RecordWithId(string key, object value)
        {
            return new DmtRecord
            {
                Attributes = new List<RecordAttribute>
                {
                    new RecordAttribute { Key = key, Value = value }
                }
            };
        }
    }
}
