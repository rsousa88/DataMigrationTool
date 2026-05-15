// System
using System;
using System.Collections.Generic;

// Microsoft
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Metadata;

// DataMigrationTool
using Dataverse.XrmTools.DataMigrationTool.AppSettings;
using Dataverse.XrmTools.DataMigrationTool.Enums;
using Dataverse.XrmTools.DataMigrationTool.Models;
using DmtAttribute = Dataverse.XrmTools.DataMigrationTool.Models.Attribute;

namespace Dataverse.XrmTools.DataMigrationTool.Tests.TestSupport
{
    public static class TestDataBuilder
    {
        public static Table Table(
            string logicalName = "account",
            string displayName = "Account",
            string idAttribute = "accountid",
            string nameAttribute = "name")
        {
            return new Table
            {
                LogicalName = logicalName,
                DisplayName = displayName,
                IdAttribute = idAttribute,
                NameAttribute = nameAttribute,
                IsCustomizable = true,
                AllAttributes = new List<DmtAttribute>
                {
                    Attribute(idAttribute, "Unique Identifier", "Guid"),
                    Attribute(nameAttribute, "Name"),
                    Attribute("accountnumber", "Account Number")
                }
            };
        }

        public static DmtAttribute Attribute(string logicalName, string displayName = null, string type = "String")
        {
            return new DmtAttribute
            {
                LogicalName = logicalName,
                DisplayName = displayName ?? logicalName,
                Type = type,
                ValidOnCreate = true,
                ValidOnUpdate = true
            };
        }

        public static TableData TableData(Table table = null, EntityMetadata metadata = null)
        {
            var resolvedTable = table ?? Table();
            return new TableData
            {
                Table = resolvedTable,
                Metadata = metadata ?? EntityMetadata(resolvedTable.LogicalName, resolvedTable.IdAttribute, resolvedTable.NameAttribute),
                SelectedAttributes = resolvedTable.AllAttributes,
                Settings = new TableSettings()
            };
        }

        public static EntityMetadata EntityMetadata(
            string logicalName = "account",
            string primaryIdAttribute = "accountid",
            string primaryNameAttribute = "name")
        {
            return new EntityMetadata
            {
                LogicalName = logicalName
            };
        }

        public static AttributeMetadata AttributeMetadata(string logicalName = "name", string displayName = "Name")
        {
            return new StringAttributeMetadata
            {
                LogicalName = logicalName,
                DisplayName = new Label(displayName, 1033)
            };
        }

        public static RecordCollection RecordCollection(string logicalName = "account", string primaryIdAttribute = "accountid")
        {
            var recordId = Guid.NewGuid();
            return new RecordCollection
            {
                LogicalName = logicalName,
                PrimaryIdAttribute = primaryIdAttribute,
                Records = new List<Record>
                {
                    new Record
                    {
                        SourceRowNumber = 1,
                        Attributes = new List<RecordAttribute>
                        {
                            new RecordAttribute { Key = primaryIdAttribute, Type = AttributeType.Identifier, Value = recordId },
                            new RecordAttribute { Key = "name", Type = AttributeType.Standard, Value = "Contoso" }
                        }
                    }
                },
                Count = 1
            };
        }

        public static ExcelExportConfig ExcelExportConfig(string logicalName = "account", string primaryIdAttribute = "accountid")
        {
            return new ExcelExportConfig
            {
                Table = new ExcelTableConfig
                {
                    LogicalName = logicalName,
                    PrimaryIdAttribute = primaryIdAttribute,
                    PrimaryNameAttribute = "name"
                },
                Columns = new List<ExcelColumnConfig>
                {
                    new ExcelColumnConfig { LogicalName = primaryIdAttribute, Type = "Guid" },
                    new ExcelColumnConfig { LogicalName = "name", Type = "String" },
                    new ExcelColumnConfig { LogicalName = "accountnumber", Type = "String" }
                }
            };
        }

        public static ExecutionPlan ExecutionPlan(params ExecutionPlanStep[] steps)
        {
            var plan = new ExecutionPlan
            {
                Name = "Test Plan",
                SourceEnvironment = new DmtEnvironmentInfo { UniqueName = "source", FriendlyName = "Source" }
            };
            plan.Steps.AddRange(steps);
            return plan;
        }

        public static ExecutionPlanStep ExecutionPlanStep(
            string id = "step-1",
            string operation = "ExportToJson",
            string table = "account")
        {
            return new ExecutionPlanStep
            {
                Id = id,
                Name = id,
                Operation = operation,
                Table = new DmtTableInfo
                {
                    LogicalName = table,
                    DisplayName = table,
                    PrimaryIdAttribute = $"{table}id",
                    PrimaryNameAttribute = "name"
                }
            };
        }

        public static Entity Entity(string logicalName = "account", Guid? id = null)
        {
            var entity = new Entity(logicalName)
            {
                Id = id ?? Guid.NewGuid()
            };
            entity["name"] = "Contoso";
            return entity;
        }
    }
}
