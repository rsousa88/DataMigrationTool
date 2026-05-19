// System
using System;
using System.Collections.Generic;
using System.Linq;

// Microsoft
using Microsoft.Xrm.Sdk;

// DataMigrationTool
using Dataverse.XrmTools.DataMigrationTool.Logic;
using Dataverse.XrmTools.DataMigrationTool.Models;
using DmtRecord = Dataverse.XrmTools.DataMigrationTool.Models.Record;

// 3rd Party
using Xunit;

namespace Dataverse.XrmTools.DataMigrationTool.Tests
{
    public class PlanLookupContextTests
    {
        [Fact]
        public void ResolveByAlternateKey_ReturnsMappedActualIdForPriorImportedRecord()
        {
            var requestId = Guid.NewGuid();
            var actualId = Guid.NewGuid();
            var context = new PlanLookupContext();
            context.AddRecordCollection(CollectionWithRecord(requestId, "A-001"), new Dictionary<Guid, Guid>
            {
                [requestId] = actualId
            });

            var result = context.ResolveByAlternateKey("account", new Dictionary<string, object>
            {
                ["accountnumber"] = "A-001"
            });

            Assert.Equal(PlanLookupResolutionStatus.Found, result.Status);
            Assert.Equal(actualId, result.Id);
        }

        [Fact]
        public void ResolveByAlternateKey_ReturnsAmbiguousForDuplicateKeysWithDifferentIds()
        {
            var context = new PlanLookupContext();
            var collection = CollectionWithRecord(Guid.NewGuid(), "A-001");
            var records = collection.Records.ToList();
            records.Add(CreateRecord(Guid.NewGuid(), "A-001"));
            collection.Records = records;
            collection.Count = 2;
            context.AddRecordCollection(collection);

            var result = context.ResolveByAlternateKey("account", new Dictionary<string, object>
            {
                ["accountnumber"] = "A-001"
            });

            Assert.Equal(PlanLookupResolutionStatus.Ambiguous, result.Status);
            Assert.Contains("matched 2 records", result.Message);
        }

        [Fact]
        public void ResolveByAlternateKey_NormalizesEntityReferenceValues()
        {
            var ownerId = Guid.NewGuid();
            var accountId = Guid.NewGuid();
            var context = new PlanLookupContext();
            var collection = CollectionWithRecord(accountId, "A-001");
            var record = collection.Records.First();
            var attributes = record.Attributes.ToList();
            attributes.Add(new RecordAttribute
            {
                Key = "ownerid",
                Value = new EntityReference("systemuser", ownerId)
            });
            record.Attributes = attributes;
            context.AddRecordCollection(collection);

            var result = context.ResolveByAlternateKey("account", new Dictionary<string, object>
            {
                ["ownerid"] = ownerId
            });

            Assert.Equal(PlanLookupResolutionStatus.Found, result.Status);
            Assert.Equal(accountId, result.Id);
        }

        private static RecordCollection CollectionWithRecord(Guid id, string accountNumber)
        {
            return new RecordCollection
            {
                LogicalName = "account",
                PrimaryIdAttribute = "accountid",
                Records = new List<DmtRecord> { CreateRecord(id, accountNumber) },
                Count = 1
            };
        }

        private static DmtRecord CreateRecord(Guid id, string accountNumber)
        {
            return new DmtRecord
            {
                Attributes = new List<RecordAttribute>
                {
                    new RecordAttribute { Key = "accountid", Value = id },
                    new RecordAttribute { Key = "accountnumber", Value = accountNumber }
                }
            };
        }
    }
}
