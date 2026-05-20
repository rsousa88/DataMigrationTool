// System
using System;
using System.Collections.Generic;
using System.Linq;

// 3rd Party
using ClosedXML.Excel;
using Xunit;

// Microsoft
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Messages;
using Microsoft.Xrm.Sdk.Query;

// DataMigrationTool
using Dataverse.XrmTools.DataMigrationTool.Enums;
using Dataverse.XrmTools.DataMigrationTool.Logic;
using Dataverse.XrmTools.DataMigrationTool.Models;
using Dataverse.XrmTools.DataMigrationTool.Tests.TestSupport;
using Action = Dataverse.XrmTools.DataMigrationTool.Enums.Action;
using DmtRecord = Dataverse.XrmTools.DataMigrationTool.Models.Record;

namespace Dataverse.XrmTools.DataMigrationTool.Tests
{
    public class ExcelLogicTests
    {
        [Fact]
        public void ExportCreatesDataAndMetadataSheets()
        {
            using (var files = new TemporaryFileScope())
            {
                var path = files.GetExcelPath();
                var config = CreateAccountConfig();
                var accountId = Guid.NewGuid();
                var records = new[]
                {
                    new Entity("account", accountId)
                    {
                        ["accountid"] = accountId,
                        ["name"] = "Contoso",
                        ["accountnumber"] = "A-001"
                    }
                };

                new ExcelLogic().Export(config, records, path);

                using (var workbook = new XLWorkbook(path))
                {
                    Assert.True(workbook.Worksheets.Contains("Data"));
                    Assert.True(workbook.Worksheets.Contains("_dmt"));
                    Assert.Equal(XLWorksheetVisibility.Hidden, workbook.Worksheet("_dmt").Visibility);
                    Assert.Equal("accountid", workbook.Worksheet("Data").Cell(1, 1).GetString());
                    Assert.Equal(accountId.ToString("D"), workbook.Worksheet("Data").Cell(2, 1).GetString());
                }
            }
        }

        [Fact]
        public void MetadataRoundTripPreservesTableColumnsMatchKeyAndImportSettings()
        {
            using (var files = new TemporaryFileScope())
            {
                var path = files.GetExcelPath();
                var config = CreateAccountConfig();
                config.MatchKeyMode = "AlternateKey";
                config.MatchAlternateKeyName = "ak_accountnumber";
                config.MatchKeys = new List<string> { "accountnumber" };
                config.ImportSettings = new ExcelImportSettings
                {
                    Action = Action.Create | Action.Update,
                    ApplyMappings = true,
                    BatchSize = 50,
                    MatchKeyMode = "AlternateKey",
                    MatchAlternateKeyName = "ak_accountnumber",
                    MatchKeyFields = new List<string> { "accountnumber" }
                };

                new ExcelLogic().Export(config, Enumerable.Empty<Entity>(), path);

                var loaded = new ExcelLogic().ReadMetadata(path);

                Assert.Equal("account", loaded.Table.LogicalName);
                Assert.Equal("accountid", loaded.Table.PrimaryIdAttribute);
                Assert.Equal(new[] { "accountid", "name", "accountnumber" }, loaded.Columns.Select(c => c.LogicalName));
                Assert.Equal("AlternateKey", loaded.MatchKeyMode);
                Assert.Equal("ak_accountnumber", loaded.MatchAlternateKeyName);
                Assert.Equal(new[] { "accountnumber" }, loaded.MatchKeys);
                Assert.Equal(Action.Create | Action.Update, loaded.ImportSettings.Action);
                Assert.True(loaded.ImportSettings.ApplyMappings);
                Assert.Equal(50, loaded.ImportSettings.BatchSize);
            }
        }

        [Fact]
        public void GetImportRowCountHandlesHintRowAndPlainDataRows()
        {
            using (var files = new TemporaryFileScope())
            {
                var logic = new ExcelLogic();
                var config = CreateAccountConfig();
                var plainPath = files.GetExcelPath("plain.xlsx");
                var hintedPath = files.GetExcelPath("hinted.xlsx");

                logic.Export(config, Enumerable.Empty<Entity>(), plainPath);
                using (var workbook = new XLWorkbook(plainPath))
                {
                    var sheet = workbook.Worksheet("Data");
                    sheet.Cell(2, 1).Value = Guid.NewGuid().ToString("D");
                    sheet.Cell(2, 2).Value = "Plain";
                    workbook.Save();
                }

                logic.Export(config, Enumerable.Empty<Entity>(), hintedPath);
                using (var workbook = new XLWorkbook(hintedPath))
                {
                    var sheet = workbook.Worksheet("Data");
                    sheet.Cell(2, 1).Value = "Guid";
                    sheet.Cell(2, 2).Value = "String";
                    sheet.Cell(2, 3).Value = "String";
                    sheet.Cell(3, 1).Value = Guid.NewGuid().ToString("D");
                    sheet.Cell(3, 2).Value = "Hinted";
                    workbook.Save();
                }

                Assert.Equal(1, logic.GetImportRowCount(plainPath, out _));
                Assert.Equal(1, logic.GetImportRowCount(hintedPath, out _));
            }
        }

        [Fact]
        public void BlankPrimaryGuidRowsGetGeneratedIdsAndAreMarkedForWriteback()
        {
            using (var files = new TemporaryFileScope())
            {
                var path = files.GetExcelPath();
                var config = CreateAccountConfig();
                new ExcelLogic().Export(config, Enumerable.Empty<Entity>(), path);
                SetDataRows(path, new[]
                {
                    new Dictionary<string, object>
                    {
                        ["name"] = "New account",
                        ["accountnumber"] = "A-001"
                    }
                });

                var collection = new ExcelLogic().ImportFromExcel(path, config, null);
                var record = Assert.Single(collection.Records);
                var attributes = record.Attributes.ToList();

                Assert.True(record.PrimaryIdWasBlank);
                var generatedId = Assert.IsType<Guid>(attributes.Single(a => a.Key == "accountid").Value);
                Assert.NotEqual(Guid.Empty, generatedId);
            }
        }

        [Fact]
        public void DuplicateSuppliedPrimaryGuidRowsAreSkippedWithRowErrors()
        {
            using (var files = new TemporaryFileScope())
            {
                var path = files.GetExcelPath();
                var config = CreateAccountConfig();
                var duplicateId = Guid.NewGuid();
                new ExcelLogic().Export(config, Enumerable.Empty<Entity>(), path);
                SetDataRows(path, new[]
                {
                    new Dictionary<string, object>
                    {
                        ["accountid"] = duplicateId.ToString("D"),
                        ["name"] = "Duplicate one"
                    },
                    new Dictionary<string, object>
                    {
                        ["accountid"] = duplicateId.ToString("D"),
                        ["name"] = "Duplicate two"
                    }
                });

                var collection = new ExcelLogic().ImportFromExcel(path, config, null);

                Assert.Empty(collection.Records);
                Assert.Equal(2, collection.ImportErrors.Count);
                Assert.All(collection.ImportErrors, error => Assert.Contains("duplicate record GUID", error));
            }
        }

        [Fact]
        public void SuppliedUniqueGuidRowsArePreservedAndMarkedAsUserSupplied()
        {
            using (var files = new TemporaryFileScope())
            {
                var path = files.GetExcelPath();
                var config = CreateAccountConfig();
                var suppliedId = Guid.NewGuid();
                new ExcelLogic().Export(config, Enumerable.Empty<Entity>(), path);
                SetDataRows(path, new[]
                {
                    new Dictionary<string, object>
                    {
                        ["accountid"] = suppliedId.ToString("D"),
                        ["name"] = "Manual id"
                    }
                });

                var collection = new ExcelLogic().ImportFromExcel(path, config, null);
                var record = Assert.Single(collection.Records);
                var accountId = record.Attributes.Single(a => a.Key == "accountid").Value;

                Assert.False(record.PrimaryIdWasBlank);
                Assert.Equal(suppliedId, accountId);
                Assert.Empty(collection.ImportErrors);
            }
        }

        [Fact]
        public void WritebackOnlyUpdatesRowsWherePrimaryGuidWasBlankAndAddsLookupGuids()
        {
            using (var files = new TemporaryFileScope())
            {
                var path = files.GetExcelPath();
                var config = CreateAccountConfig(includeOwnerLookup: true);
                var existingId = Guid.NewGuid();
                var requestNewId = Guid.NewGuid();
                var createdId = Guid.NewGuid();
                var ownerId = Guid.NewGuid();
                new ExcelLogic().Export(config, Enumerable.Empty<Entity>(), path);
                RemoveColumn(path, "ownerid");
                SetDataRows(path, new[]
                {
                    new Dictionary<string, object>
                    {
                        ["name"] = "New account"
                    },
                    new Dictionary<string, object>
                    {
                        ["accountid"] = existingId.ToString("D"),
                        ["name"] = "Existing account"
                    }
                });
                var collection = new RecordCollection
                {
                    LogicalName = "account",
                    PrimaryIdAttribute = "accountid",
                    Records = new[]
                    {
                        CreateRecord(2, requestNewId, ownerId),
                        CreateRecord(3, existingId, Guid.NewGuid())
                    },
                    Count = 2
                };

                var updatedCells = new ExcelLogic().UpdateImportedGuids(
                    path,
                    config,
                    collection,
                    new Dictionary<Guid, Guid> { [requestNewId] = createdId });

                Assert.Equal(2, updatedCells);
                using (var workbook = new XLWorkbook(path))
                {
                    var sheet = workbook.Worksheet("Data");
                    var ownerColumn = FindColumn(sheet, "ownerid");
                    Assert.Equal(createdId.ToString("D"), sheet.Cell(2, 1).GetString());
                    Assert.Equal(ownerId.ToString("D"), sheet.Cell(2, ownerColumn).GetString());
                    Assert.Equal(existingId.ToString("D"), sheet.Cell(3, 1).GetString());
                    Assert.True(string.IsNullOrWhiteSpace(sheet.Cell(3, ownerColumn).GetString()));
                }
            }
        }

        [Fact]
        public void OptionSetLabelAndValueCellsAreParsed()
        {
            using (var files = new TemporaryFileScope())
            {
                var labelPath = files.GetExcelPath("label.xlsx");
                var valuePath = files.GetExcelPath("value.xlsx");
                var labelConfig = CreateAccountConfig();
                labelConfig.Columns.Add(StatusColumn("Label"));
                var valueConfig = CreateAccountConfig();
                valueConfig.Columns.Add(StatusColumn("Value"));
                new ExcelLogic().Export(labelConfig, Enumerable.Empty<Entity>(), labelPath);
                new ExcelLogic().Export(valueConfig, Enumerable.Empty<Entity>(), valuePath);
                SetDataRows(labelPath, new[]
                {
                    new Dictionary<string, object>
                    {
                        ["name"] = "Label status",
                        ["statuscode"] = "Active"
                    }
                });
                SetDataRows(valuePath, new[]
                {
                    new Dictionary<string, object>
                    {
                        ["name"] = "Value status",
                        ["statuscode"] = "2"
                    }
                });

                var labelRecord = new ExcelLogic().ImportFromExcel(labelPath, labelConfig, null).Records.Single();
                var valueRecord = new ExcelLogic().ImportFromExcel(valuePath, valueConfig, null).Records.Single();

                Assert.Equal(1, ((OptionSetValue)labelRecord.Attributes.Single(a => a.Key == "statuscode").Value).Value);
                Assert.Equal(2, ((OptionSetValue)valueRecord.Attributes.Single(a => a.Key == "statuscode").Value).Value);
            }
        }

        [Fact]
        public void CustomMatchKeyUsesTargetServiceToPreserveExistingRecordId()
        {
            using (var files = new TemporaryFileScope())
            {
                var path = files.GetExcelPath();
                var config = CreateAccountConfig();
                var existingId = Guid.NewGuid();
                config.MatchKeyMode = "Custom";
                config.MatchKeys = new List<string> { "accountnumber" };
                new ExcelLogic().Export(config, Enumerable.Empty<Entity>(), path);
                SetDataRows(path, new[]
                {
                    new Dictionary<string, object>
                    {
                        ["name"] = "Existing by key",
                        ["accountnumber"] = "A-001"
                    }
                });
                var targetService = new FakeOrganizationService
                {
                    ExecuteHandler = request =>
                    {
                        var query = ((RetrieveMultipleRequest)request).Query as QueryExpression;
                        Assert.Equal("account", query.EntityName);
                        var condition = query.Criteria.Conditions.Single();
                        Assert.Equal("accountnumber", condition.AttributeName);
                        Assert.Equal("A-001", condition.Values.Single());
                        return RetrieveMultipleResponse(new EntityCollection(new List<Entity>
                        {
                            new Entity("account", existingId)
                        }));
                    }
                };

                var collection = new ExcelLogic().ImportFromExcel(path, config, targetService);
                var record = Assert.Single(collection.Records);

                Assert.Equal(existingId, record.Attributes.Single(a => a.Key == "accountid").Value);
                Assert.Equal("accountnumber", collection.ImportMatchKey);
                Assert.Equal(new[] { "accountnumber" }, collection.ImportMatchKeys);
                Assert.Equal("Custom", collection.ImportMatchKeyMode);
            }
        }

        [Fact]
        public void CustomMatchKeyCanResolveFromPlanContext()
        {
            using (var files = new TemporaryFileScope())
            {
                var requestId = Guid.NewGuid();
                var actualId = Guid.NewGuid();
                var context = new PlanLookupContext();
                context.AddRecordCollection(new RecordCollection
                {
                    LogicalName = "account",
                    PrimaryIdAttribute = "accountid",
                    Records = new List<DmtRecord>
                    {
                        new DmtRecord
                        {
                            Attributes = new List<RecordAttribute>
                            {
                                new RecordAttribute { Key = "accountid", Value = requestId },
                                new RecordAttribute { Key = "accountnumber", Value = "A-001" }
                            }
                        }
                    },
                    Count = 1
                }, new Dictionary<Guid, Guid> { [requestId] = actualId });

                var path = files.GetExcelPath();
                var config = CreateAccountConfig();
                config.MatchKeyMode = "Custom";
                config.MatchKeys = new List<string> { "accountnumber" };
                new ExcelLogic().Export(config, Enumerable.Empty<Entity>(), path);
                SetDataRows(path, new[]
                {
                    new Dictionary<string, object>
                    {
                        ["name"] = "Existing by plan key",
                        ["accountnumber"] = "A-001"
                    }
                });

                var collection = new ExcelLogic().ImportFromExcel(path, config, null, null, context);
                var record = Assert.Single(collection.Records);

                Assert.Equal(actualId, record.Attributes.Single(a => a.Key == "accountid").Value);
                Assert.Empty(collection.ImportErrors);
            }
        }

        [Fact]
        public void LookupAlternateKeyCanResolveFromPlanContext()
        {
            using (var files = new TemporaryFileScope())
            {
                var accountRequestId = Guid.NewGuid();
                var accountActualId = Guid.NewGuid();
                var context = new PlanLookupContext();
                context.AddRecordCollection(new RecordCollection
                {
                    LogicalName = "account",
                    PrimaryIdAttribute = "accountid",
                    Records = new List<DmtRecord>
                    {
                        new DmtRecord
                        {
                            Attributes = new List<RecordAttribute>
                            {
                                new RecordAttribute { Key = "accountid", Value = accountRequestId },
                                new RecordAttribute { Key = "accountnumber", Value = "A-001" }
                            }
                        }
                    },
                    Count = 1
                }, new Dictionary<Guid, Guid> { [accountRequestId] = accountActualId });

                var path = files.GetExcelPath();
                var config = CreateContactConfigWithAccountLookup();
                new ExcelLogic().Export(config, Enumerable.Empty<Entity>(), path);
                SetDataRows(path, new[]
                {
                    new Dictionary<string, object>
                    {
                        ["contactid"] = Guid.NewGuid().ToString("D"),
                        ["fullname"] = "Contact",
                        ["parentcustomerid.accountnumber"] = "A-001"
                    }
                });

                var collection = new ExcelLogic().ImportFromExcel(path, config, null, null, context);
                var record = Assert.Single(collection.Records);
                var lookup = Assert.IsType<EntityReference>(record.Attributes.Single(a => a.Key == "parentcustomerid").Value);

                Assert.Equal("account", lookup.LogicalName);
                Assert.Equal(accountActualId, lookup.Id);
                Assert.Empty(collection.ImportErrors);
            }
        }

        private static ExcelExportConfig CreateAccountConfig(bool includeOwnerLookup = false)
        {
            var config = new ExcelExportConfig
            {
                Table = new ExcelTableConfig
                {
                    LogicalName = "account",
                    PrimaryIdAttribute = "accountid",
                    PrimaryNameAttribute = "name"
                },
                ImportSettings = new ExcelImportSettings
                {
                    Action = Action.Create | Action.Update,
                    BatchSize = 25,
                    MatchKeyMode = "Guid"
                },
                Columns = new List<ExcelColumnConfig>
                {
                    new ExcelColumnConfig { LogicalName = "accountid", DisplayName = "Account", Type = "Guid" },
                    new ExcelColumnConfig { LogicalName = "name", DisplayName = "Name", Type = "String" },
                    new ExcelColumnConfig { LogicalName = "accountnumber", DisplayName = "Account Number", Type = "String" }
                }
            };

            if (includeOwnerLookup)
            {
                config.Columns.Add(new ExcelColumnConfig
                {
                    LogicalName = "ownerid",
                    DisplayName = "Owner",
                    Type = "Lookup",
                    RelatedTable = "systemuser",
                    Resolution = "Guid"
                });
            }

            return config;
        }

        private static ExcelExportConfig CreateContactConfigWithAccountLookup()
        {
            return new ExcelExportConfig
            {
                Table = new ExcelTableConfig
                {
                    LogicalName = "contact",
                    PrimaryIdAttribute = "contactid",
                    PrimaryNameAttribute = "fullname"
                },
                ImportSettings = new ExcelImportSettings
                {
                    Action = Action.Create | Action.Update,
                    BatchSize = 25,
                    MatchKeyMode = "Guid"
                },
                Columns = new List<ExcelColumnConfig>
                {
                    new ExcelColumnConfig { LogicalName = "contactid", DisplayName = "Contact", Type = "Guid" },
                    new ExcelColumnConfig { LogicalName = "fullname", DisplayName = "Full Name", Type = "String" },
                    new ExcelColumnConfig
                    {
                        LogicalName = "parentcustomerid",
                        DisplayName = "Parent Account",
                        Type = "Lookup",
                        RelatedTable = "account",
                        Resolution = "AlternateKey",
                        AlternateKeyFields = new List<string> { "accountnumber" }
                    },
                    new ExcelColumnConfig
                    {
                        LogicalName = "parentcustomerid.accountnumber",
                        DisplayName = "Parent Account Number",
                        Type = "LookupKeyField",
                        OwnerAttribute = "parentcustomerid",
                        KeyFieldType = "String",
                        RelatedTable = "account"
                    }
                }
            };
        }

        private static ExcelColumnConfig StatusColumn(string exportMode)
        {
            return new ExcelColumnConfig
            {
                LogicalName = "statuscode",
                DisplayName = "Status",
                Type = "OptionSet",
                ExportMode = exportMode,
                Options = new List<OptionConfig>
                {
                    new OptionConfig { Value = 1, Label = "Active" },
                    new OptionConfig { Value = 2, Label = "Inactive" }
                }
            };
        }

        private static DmtRecord CreateRecord(int row, Guid accountId, Guid ownerId)
        {
            return new DmtRecord
            {
                SourceRowNumber = row,
                Attributes = new List<RecordAttribute>
                {
                    new RecordAttribute { Key = "accountid", Type = AttributeType.Identifier, Value = accountId },
                    new RecordAttribute { Key = "ownerid", Type = AttributeType.EntityReference, Value = new EntityReference("systemuser", ownerId) }
                }
            };
        }

        private static RetrieveMultipleResponse RetrieveMultipleResponse(EntityCollection collection)
        {
            var response = new RetrieveMultipleResponse();
            response.Results["EntityCollection"] = collection;
            return response;
        }

        private static void SetDataRows(string path, IEnumerable<Dictionary<string, object>> rows)
        {
            using (var workbook = new XLWorkbook(path))
            {
                var sheet = workbook.Worksheet("Data");
                var headerMap = BuildHeaderMap(sheet);
                var rowIndex = 2;
                foreach (var row in rows)
                {
                    foreach (var value in row)
                    {
                        if (!headerMap.TryGetValue(value.Key, out var columnIndex))
                        {
                            columnIndex = (sheet.LastColumnUsed()?.ColumnNumber() ?? 0) + 1;
                            sheet.Cell(1, columnIndex).Value = value.Key;
                            headerMap[value.Key] = columnIndex;
                        }

                        sheet.Cell(rowIndex, columnIndex).Value = value.Value?.ToString();
                    }

                    rowIndex++;
                }

                workbook.Save();
            }
        }

        private static void RemoveColumn(string path, string logicalName)
        {
            using (var workbook = new XLWorkbook(path))
            {
                var sheet = workbook.Worksheet("Data");
                var column = FindColumn(sheet, logicalName);
                if (column > 0)
                    sheet.Column(column).Delete();
                workbook.Save();
            }
        }

        private static Dictionary<string, int> BuildHeaderMap(IXLWorksheet sheet)
        {
            var lastColumn = sheet.LastColumnUsed()?.ColumnNumber() ?? 0;
            var map = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            for (var column = 1; column <= lastColumn; column++)
            {
                var header = sheet.Cell(1, column).GetString();
                if (!string.IsNullOrWhiteSpace(header))
                    map[header] = column;
            }

            return map;
        }

        private static int FindColumn(IXLWorksheet sheet, string logicalName)
        {
            return BuildHeaderMap(sheet).TryGetValue(logicalName, out var column)
                ? column
                : 0;
        }
    }
}
