// System
using System;
using System.Collections.Generic;
using System.Linq;

// Microsoft
using Microsoft.Crm.Sdk.Messages;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Messages;
using Microsoft.Xrm.Sdk.Query;

// xunit
using Xunit;

// DataMigrationTool
using Dataverse.XrmTools.DataMigrationTool.AppSettings;
using Dataverse.XrmTools.DataMigrationTool.Logic;
using Dataverse.XrmTools.DataMigrationTool.Models;
using Dataverse.XrmTools.DataMigrationTool.Tests.TestSupport;
using DmtAction = Dataverse.XrmTools.DataMigrationTool.Enums.Action;

namespace Dataverse.XrmTools.DataMigrationTool.Tests
{
    public class SqliteDataLogicTests : IDisposable
    {
        private readonly SqliteProjectService _svc;

        public SqliteDataLogicTests()
        {
            _svc = SqliteProjectService.CreateInMemory("DataLogicTests");
        }

        public void Dispose() => _svc?.Dispose();

        [Fact]
        public void Pull_Writes_SourceId_FromDataverseEntityId()
        {
            var sourceId = Guid.NewGuid();
            var source = new FakeOrganizationService
            {
                ExecuteHandler = request => request is FetchXmlToQueryExpressionRequest
                    ? new FetchXmlToQueryExpressionResponse
                    {
                        Results =
                        {
                            ["Query"] = new QueryExpression("account")
                        }
                    }
                    : request is RetrieveMultipleRequest
                        ? new RetrieveMultipleResponse
                        {
                            Results =
                            {
                                ["EntityCollection"] = new EntityCollection(new[]
                                {
                                    new Entity("account")
                                    {
                                        Id = sourceId,
                                        ["accountid"] = sourceId,
                                        ["name"] = "Pulled Account"
                                    }
                                }.ToList())
                            }
                        }
                    : new OrganizationResponse(),
            };
            var config = new DataTableConfig
            {
                BatchSize = 25,
                SelectedAttributes = new List<string> { "name" },
                AllColumns = new List<DataTableColumnConfig>
                {
                    new DataTableColumnConfig { LogicalName = "accountid", Type = "Uniqueidentifier", SqliteType = "TEXT" },
                    new DataTableColumnConfig { LogicalName = "name", Type = "String", SqliteType = "TEXT" }
                }
            };

            var snapshot = SqliteDataLogic.Pull(_svc, source, "account", "accountid", "src-env", config, "pull-snap", null, null);
            var rows = _svc.ReadSnapshotRecords(snapshot.TableSuffix, snapshot.ColumnConfig).ToList();

            Assert.Single(rows);
            Assert.Equal(sourceId.ToString("D"), rows[0]["_source_id"]?.ToString());
        }

        [Fact]
        public void PreviewPush_Treats_BoolIsNew_AsCreate()
        {
            var columns = new List<DataTableColumnConfig>
            {
                new DataTableColumnConfig { LogicalName = "name", Type = "String", SqliteType = "TEXT" }
            };
            var snapshot = new DmtSnapshot
            {
                Name = "new-rows",
                TableSuffix = "new_rows",
                TableLogicalName = "account",
                SourceEnvId = "src-env",
                Source = "JSON",
                ColumnConfig = columns
            };
            _svc.SaveSnapshot(snapshot);
            _svc.CreateSnapshotTable(snapshot.TableSuffix, columns);
            _svc.WriteSnapshotRecords(snapshot.TableSuffix, new[]
            {
                new Dictionary<string, object>
                {
                    ["_source_id"] = Guid.NewGuid().ToString("D"),
                    ["_is_new"] = true,
                    ["name"] = "New Account"
                }
            }, columns);

            var preview = SqliteDataLogic.PreviewPush(
                _svc,
                snapshot.Name,
                "src-env",
                "target-env",
                new UiSettings { Action = DmtAction.Create });

            Assert.Equal(1, preview.CreateCount);
            Assert.Equal("Create", preview.Items.Single().Operation);
        }

        [Fact]
        public void PreviewPush_CustomSingleIntegerMatchKey_UsesTargetLookupForUpdates()
        {
            var sourceId = Guid.NewGuid();
            var targetId = Guid.NewGuid();
            var columns = new List<DataTableColumnConfig>
            {
                new DataTableColumnConfig { LogicalName = "accountnumber", Type = "Integer", SqliteType = "INTEGER" },
                new DataTableColumnConfig { LogicalName = "name", Type = "String", SqliteType = "TEXT" }
            };
            var snapshot = new DmtSnapshot
            {
                Name = "existing-rows",
                TableSuffix = "existing_rows",
                TableLogicalName = "account",
                SourceEnvId = "src-env",
                Source = "JSON",
                ColumnConfig = columns
            };
            QueryExpression capturedQuery = null;
            var target = new FakeOrganizationService
            {
                ExecuteHandler = request =>
                {
                    if (request is RetrieveMultipleRequest retrieve)
                    {
                        capturedQuery = retrieve.Query as QueryExpression;
                        return new RetrieveMultipleResponse
                        {
                            Results =
                            {
                                ["EntityCollection"] = new EntityCollection(new[]
                                {
                                    new Entity("account")
                                    {
                                        Id = targetId,
                                        ["accountnumber"] = 1001
                                    }
                                }.ToList())
                            }
                        };
                    }

                    return new OrganizationResponse();
                }
            };

            _svc.SaveSnapshot(snapshot);
            _svc.CreateSnapshotTable(snapshot.TableSuffix, columns);
            _svc.WriteSnapshotRecords(snapshot.TableSuffix, new[]
            {
                new Dictionary<string, object>
                {
                    ["_source_id"] = sourceId.ToString("D"),
                    ["_is_new"] = false,
                    ["accountnumber"] = 1001L,
                    ["name"] = "Existing Account"
                }
            }, columns);

            var preview = SqliteDataLogic.PreviewPush(
                _svc,
                snapshot.Name,
                "src-env",
                "target-env",
                new UiSettings { Action = DmtAction.Create | DmtAction.Update },
                new ExcelImportMatchKeySelection { Mode = "Custom", Fields = new List<string> { "accountnumber" } },
                targetClient: target,
                queryTargetForMatchKeys: true);

            Assert.Equal(0, preview.CreateCount);
            Assert.Equal(1, preview.UpdateCount);
            Assert.Equal("Update", preview.Items.Single().Operation);
            Assert.NotNull(capturedQuery);
            Assert.Contains("accountnumber", capturedQuery.ColumnSet.Columns);
            Assert.Contains(capturedQuery.Criteria.Conditions, c =>
                c.AttributeName == "accountnumber"
                && c.Operator == ConditionOperator.In
                && c.Values.Cast<object>().Any(v => v is int i && i == 1001));
        }
    }
}
