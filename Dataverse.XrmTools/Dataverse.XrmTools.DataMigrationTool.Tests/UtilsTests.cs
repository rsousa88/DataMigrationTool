// System
using System;
using System.Collections.Generic;
using System.Linq;

// Microsoft
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Metadata;

// DataMigrationTool
using Dataverse.XrmTools.DataMigrationTool.Enums;
using Dataverse.XrmTools.DataMigrationTool.Helpers;
using Dataverse.XrmTools.DataMigrationTool.Models;
using Dataverse.XrmTools.DataMigrationTool.Tests.TestSupport;
using DmtAttributeType = Dataverse.XrmTools.DataMigrationTool.Enums.AttributeType;
using DmtRecord = Dataverse.XrmTools.DataMigrationTool.Models.Record;

// 3rd Party
using Xunit;

namespace Dataverse.XrmTools.DataMigrationTool.Tests
{
    public class UtilsTests
    {
        [Fact]
        public void SerializeAndDeserializeObject_RoundTripsJson()
        {
            var config = TestDataBuilder.ExcelExportConfig();

            var json = config.SerializeObject();
            var clone = json.DeserializeObject<ExcelExportConfig>();

            Assert.Equal(config.Table.LogicalName, clone.Table.LogicalName);
            Assert.Equal(config.Columns.Count, clone.Columns.Count);
        }

        [Fact]
        public void ToEnum_ParsesEnumValue()
        {
            Assert.Equal(MappingState.Existing, "Existing".ToEnum<MappingState>());
        }

        [Fact]
        public void MatchFilter_MatchesDisplayOrLogicalName()
        {
            var table = TestDataBuilder.Table("account", "Account");

            Assert.True(table.MatchFilter("acc"));
            Assert.True(table.MatchFilter("Account"));
            Assert.False(table.MatchFilter("contact"));
            Assert.True(table.MatchFilter(null));
        }

        [Fact]
        public void FormatXml_ReturnsIndentedXml()
        {
            var xml = "<fetch><entity name=\"account\" /></fetch>";

            var formatted = xml.FormatXml();

            Assert.Contains(Environment.NewLine, formatted);
            Assert.Contains("entity", formatted);
        }

        [Fact]
        public void ToEntityCollection_GeneratesMissingPrimaryIdsAndWritesThemBack()
        {
            var record = new DmtRecord
            {
                SourceRowNumber = 1,
                Attributes = new List<RecordAttribute>
                {
                    new RecordAttribute { Key = "name", Type = DmtAttributeType.Standard, Value = "Contoso" }
                }
            };
            var collection = new RecordCollection
            {
                LogicalName = "account",
                PrimaryIdAttribute = "accountid",
                Records = new List<DmtRecord> { record },
                Count = 1
            };

            var entities = collection.ToEntityCollection(new AttributeMetadata[]
            {
                new UniqueIdentifierAttributeMetadata { LogicalName = "accountid" },
                new StringAttributeMetadata { LogicalName = "name" }
            });

            var idAttribute = record.Attributes.Single(a => a.Key == "accountid");
            Assert.Single(entities.Entities);
            Assert.True(idAttribute.Value is Guid);
            Assert.Equal(entities.Entities[0].Id, (Guid)idAttribute.Value);
        }

        [Fact]
        public void MapAttributes_ConvertsStandardIntegerValueUsingMetadata()
        {
            var attributes = new List<RecordAttribute>
            {
                new RecordAttribute { Key = "numberofemployees", Type = DmtAttributeType.Standard, Value = "42" }
            };

            var mapped = attributes.MapAttributes(new AttributeMetadata[]
            {
                new IntegerAttributeMetadata { LogicalName = "numberofemployees" }
            });

            Assert.Equal(42, mapped["numberofemployees"]);
        }
    }
}
