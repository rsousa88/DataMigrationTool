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

        [Fact]
        public void PreviewPush_CustomLookupWithoutRelatedSnapshot_AllowsExistingTargetGuid()
        {
            var sourceId = Guid.NewGuid();
            var teamId = Guid.NewGuid();
            var columns = new List<DataTableColumnConfig>
            {
                new DataTableColumnConfig { LogicalName = "ownerid", Type = "Owner", SqliteType = "TEXT", RelatedTable = "team" },
                new DataTableColumnConfig { LogicalName = "name", Type = "String", SqliteType = "TEXT" }
            };
            var snapshot = new DmtSnapshot
            {
                Name = "lookup-existing-target",
                TableSuffix = "lookup_existing_target",
                TableLogicalName = "account",
                SourceEnvId = "src-env",
                Source = "JSON",
                ColumnConfig = columns
            };
            var target = new FakeOrganizationService();
            target.Add(new Entity("team") { Id = teamId });

            _svc.SaveSnapshot(snapshot);
            _svc.CreateSnapshotTable(snapshot.TableSuffix, columns);
            _svc.WriteSnapshotRecords(snapshot.TableSuffix, new[]
            {
                new Dictionary<string, object>
                {
                    ["_source_id"] = sourceId.ToString("D"),
                    ["_is_new"] = true,
                    ["ownerid"] = teamId.ToString("D"),
                    ["name"] = "Account With Existing Owner"
                }
            }, columns);

            var preview = SqliteDataLogic.PreviewPush(
                _svc,
                snapshot.Name,
                "src-env",
                "target-env",
                new UiSettings { Action = DmtAction.Create },
                lookupMatchKeys: new List<PushLookupMatchKey>
                {
                    new PushLookupMatchKey
                    {
                        LogicalName = "ownerid",
                        Mode = "Custom",
                        Fields = new List<string> { "name" }
                    }
                },
                targetClient: target);

            var item = preview.Items.Single();
            Assert.Equal("Create", item.Operation);
            Assert.Equal(string.Empty, item.Warnings);
            Assert.Equal(0, preview.WarningCount);
        }

        [Fact]
        public void Push_EmptySelectedColumns_ExcludesAllNonPrimaryAttributes()
        {
            var sourceId = Guid.NewGuid();
            var createdId = Guid.NewGuid();
            var columns = new List<DataTableColumnConfig>
            {
                new DataTableColumnConfig { LogicalName = "accountid", Type = "Uniqueidentifier", SqliteType = "TEXT" },
                new DataTableColumnConfig { LogicalName = "name", Type = "String", SqliteType = "TEXT" }
            };
            var snapshot = new DmtSnapshot
            {
                Name = "empty-selected-columns",
                TableSuffix = "empty_selected_columns",
                TableLogicalName = "account",
                SourceEnvId = "src-env",
                Source = "JSON",
                PrimaryIdAttribute = "accountid",
                ColumnConfig = columns
            };
            ExecuteMultipleRequest capturedBatch = null;
            var target = new FakeOrganizationService
            {
                ExecuteHandler = request =>
                {
                    if (request is ExecuteMultipleRequest batch)
                    {
                        capturedBatch = batch;
                        var response = new ExecuteMultipleResponse();
                        response.Results["Responses"] = new ExecuteMultipleResponseItemCollection
                        {
                            new ExecuteMultipleResponseItem
                            {
                                RequestIndex = 0,
                                Response = new CreateResponse { Results = { ["id"] = createdId } }
                            }
                        };
                        return response;
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
                    ["_is_new"] = true,
                    ["accountid"] = sourceId.ToString("D"),
                    ["name"] = "Excluded Name"
                }
            }, columns);

            var result = SqliteDataLogic.Push(
                _svc,
                snapshot.Name,
                "src-env",
                "target-env",
                target,
                new UiSettings { Action = DmtAction.Create },
                null,                  // worker
                null,                  // matchKeyOverride
                null,                  // lookupMatchKeys
                new List<string>(),    // selectedColumns (empty = push nothing but primary id)
                null);                 // sourceClient

            Assert.Equal(1, result.Created);
            Assert.Empty(result.Errors);
            Assert.NotNull(capturedBatch);
            var createRequest = Assert.IsType<CreateRequest>(capturedBatch.Requests.Single());
            Assert.Equal(sourceId, createRequest.Target.Id);
            Assert.Empty(createRequest.Target.Attributes);
        }

        [Fact]
        public void Push_LookupNameCompanionColumn_IsExcludedFromPayload()
        {
            var sourceId = Guid.NewGuid();
            var createdId = Guid.NewGuid();
            var columns = new List<DataTableColumnConfig>
            {
                new DataTableColumnConfig { LogicalName = "peers_roleid", Type = "Uniqueidentifier", SqliteType = "TEXT" },
                new DataTableColumnConfig { LogicalName = "peers_name", Type = "String", SqliteType = "TEXT" },
                new DataTableColumnConfig { LogicalName = "peers_parentgrouptypeid", Type = "Lookup", SqliteType = "TEXT", RelatedTable = "peers_grouptype" },
                new DataTableColumnConfig { LogicalName = "peers_parentgrouptypeidname", Type = "String", SqliteType = "TEXT" }
            };
            var snapshot = new DmtSnapshot
            {
                Name = "lookup-name-companion",
                TableSuffix = "lookup_name_companion",
                TableLogicalName = "peers_role",
                SourceEnvId = "src-env",
                Source = "JSON",
                PrimaryIdAttribute = "peers_roleid",
                ColumnConfig = columns
            };
            ExecuteMultipleRequest capturedBatch = null;
            var target = new FakeOrganizationService
            {
                ExecuteHandler = request =>
                {
                    if (request is ExecuteMultipleRequest batch)
                    {
                        capturedBatch = batch;
                        var response = new ExecuteMultipleResponse();
                        response.Results["Responses"] = new ExecuteMultipleResponseItemCollection
                        {
                            new ExecuteMultipleResponseItem
                            {
                                RequestIndex = 0,
                                Response = new CreateResponse { Results = { ["id"] = createdId } }
                            }
                        };
                        return response;
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
                    ["_is_new"] = true,
                    ["peers_roleid"] = sourceId.ToString("D"),
                    ["peers_name"] = "Role A",
                    ["peers_parentgrouptypeid"] = Guid.NewGuid().ToString("D"),
                    ["peers_parentgrouptypeidname"] = "Some Group Type"
                }
            }, columns);

            var result = SqliteDataLogic.Push(
                _svc,
                snapshot.Name,
                "src-env",
                "target-env",
                target,
                new UiSettings { Action = DmtAction.Create },
                null,   // worker
                null,   // matchKeyOverride
                null,   // lookupMatchKeys
                null,   // selectedColumns
                null);  // sourceClient

            Assert.Equal(1, result.Created);
            Assert.Empty(result.Errors);
            Assert.NotNull(capturedBatch);
            var createRequest = Assert.IsType<CreateRequest>(capturedBatch.Requests.Single());
            Assert.False(createRequest.Target.Attributes.ContainsKey("peers_parentgrouptypeidname"),
                "Lookup name companion column must be excluded from the push payload");
            Assert.True(createRequest.Target.Attributes.ContainsKey("peers_name"),
                "Regular columns must still be pushed");
        }

        [Fact]
        public void Push_ImportedSnapshotWithoutTableConfig_UsesSnapshotPrimaryIdAndCreatesConfig()
        {
            var sourceId = Guid.NewGuid();
            var createdId = Guid.NewGuid();
            var columns = new List<DataTableColumnConfig>
            {
                new DataTableColumnConfig { LogicalName = "accountid", Type = "Uniqueidentifier", SqliteType = "TEXT" },
                new DataTableColumnConfig { LogicalName = "name", Type = "String", SqliteType = "TEXT" }
            };
            var snapshot = new DmtSnapshot
            {
                Name = "imported-accounts",
                TableSuffix = "imported_accounts",
                TableLogicalName = "account",
                SourceEnvId = "src-env",
                Source = "JSON",
                PrimaryIdAttribute = "accountid",
                ColumnConfig = columns
            };
            ExecuteMultipleRequest capturedBatch = null;
            var target = new FakeOrganizationService
            {
                ExecuteHandler = request =>
                {
                    if (request is ExecuteMultipleRequest batch)
                    {
                        capturedBatch = batch;
                        var response = new ExecuteMultipleResponse();
                        response.Results["Responses"] = new ExecuteMultipleResponseItemCollection
                        {
                            new ExecuteMultipleResponseItem
                            {
                                RequestIndex = 0,
                                Response = new CreateResponse { Results = { ["id"] = createdId } }
                            }
                        };
                        return response;
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
                    ["_is_new"] = true,
                    ["accountid"] = sourceId.ToString("D"),
                    ["name"] = "Imported Account"
                }
            }, columns);
            Assert.Null(_svc.GetTableConfig("account").config);

            var result = SqliteDataLogic.Push(
                _svc,
                snapshot.Name,
                "src-env",
                "target-env",
                target,
                new UiSettings { Action = DmtAction.Create },
                null);

            Assert.Equal(1, result.Created);
            Assert.Empty(result.Errors);
            Assert.NotNull(capturedBatch);
            Assert.IsType<CreateRequest>(capturedBatch.Requests.Single());
            Assert.Equal("accountid", _svc.GetTableConfig("account").primaryIdAttr);
        }

        [Fact]
        public void Push_CreateWithAlternateKeyMatch_PreservesSourceSnapshotGuid()
        {
            var sourceId = Guid.NewGuid();
            var columns = new List<DataTableColumnConfig>
            {
                new DataTableColumnConfig { LogicalName = "accountid", Type = "Uniqueidentifier", SqliteType = "TEXT" },
                new DataTableColumnConfig { LogicalName = "accountnumber", Type = "String", SqliteType = "TEXT" },
                new DataTableColumnConfig { LogicalName = "name", Type = "String", SqliteType = "TEXT" }
            };
            var snapshot = new DmtSnapshot
            {
                Name = "alternate-key-create",
                TableSuffix = "alternate_key_create",
                TableLogicalName = "account",
                SourceEnvId = "src-env",
                Source = "Excel",
                PrimaryIdAttribute = "accountid",
                ColumnConfig = columns
            };
            CreateRequest capturedCreate = null;
            var target = new FakeOrganizationService
            {
                ExecuteHandler = request =>
                {
                    if (request is RetrieveMultipleRequest)
                    {
                        return new RetrieveMultipleResponse
                        {
                            Results = { ["EntityCollection"] = new EntityCollection() }
                        };
                    }

                    if (request is ExecuteMultipleRequest batch)
                    {
                        capturedCreate = (CreateRequest)batch.Requests.Single();
                        var response = new ExecuteMultipleResponse();
                        response.Results["Responses"] = new ExecuteMultipleResponseItemCollection
                        {
                            new ExecuteMultipleResponseItem
                            {
                                RequestIndex = 0,
                                Response = new CreateResponse { Results = { ["id"] = sourceId } }
                            }
                        };
                        return response;
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
                    ["_is_new"] = true,
                    ["accountid"] = sourceId.ToString("D"),
                    ["accountnumber"] = "A-100",
                    ["name"] = "Alternate Key Created Account"
                }
            }, columns);

            var result = SqliteDataLogic.Push(
                _svc,
                snapshot.Name,
                "src-env",
                "target-env",
                target,
                new UiSettings { Action = DmtAction.Create | DmtAction.Update },
                null,
                new ExcelImportMatchKeySelection
                {
                    Mode = "AlternateKey",
                    AlternateKeyName = "accountnumber_key",
                    Fields = new List<string> { "accountnumber" }
                });

            Assert.Equal(1, result.Created);
            Assert.Empty(result.Errors);
            Assert.NotNull(capturedCreate);
            Assert.Equal(sourceId, capturedCreate.Target.Id);
            Assert.Equal(sourceId, capturedCreate.RequestId);
        }

        [Fact]
        public void Push_CustomLookupKeyWithoutRelatedSnapshot_ResolvesTargetFromSourceLookupName()
        {
            var accountSourceId = Guid.NewGuid();
            var sourceTeamId = Guid.NewGuid();
            var targetTeamId = Guid.NewGuid();
            var createdId = Guid.NewGuid();
            var columns = new List<DataTableColumnConfig>
            {
                new DataTableColumnConfig { LogicalName = "accountid", Type = "Uniqueidentifier", SqliteType = "TEXT" },
                new DataTableColumnConfig { LogicalName = "ownerid", Type = "Owner", SqliteType = "TEXT", RelatedTable = "team" },
                new DataTableColumnConfig { LogicalName = "name", Type = "String", SqliteType = "TEXT" }
            };
            var snapshot = new DmtSnapshot
            {
                Name = "account-with-team-owner",
                TableSuffix = "account_with_team_owner",
                TableLogicalName = "account",
                SourceEnvId = "src-env",
                Source = "JSON",
                PrimaryIdAttribute = "accountid",
                ColumnConfig = columns
            };
            var source = new FakeOrganizationService();
            source.Add(new Entity("team") { Id = sourceTeamId, ["name"] = "Data Migration Team" });
            Entity createdEntity = null;
            var target = new FakeOrganizationService
            {
                ExecuteHandler = request =>
                {
                    if (request is RetrieveMultipleRequest retrieve)
                    {
                        var query = (QueryExpression)retrieve.Query;
                        if (query.EntityName == "team"
                            && query.Criteria.Conditions.Any(c =>
                                c.AttributeName == "name"
                                && c.Values.Cast<object>().Any(v => v?.ToString() == "Data Migration Team")))
                        {
                            return new RetrieveMultipleResponse
                            {
                                Results =
                                {
                                    ["EntityCollection"] = new EntityCollection(new[]
                                    {
                                        new Entity("team") { Id = targetTeamId }
                                    }.ToList())
                                }
                            };
                        }
                    }

                    if (request is ExecuteMultipleRequest batch)
                    {
                        createdEntity = ((CreateRequest)batch.Requests.Single()).Target;
                        var response = new ExecuteMultipleResponse();
                        response.Results["Responses"] = new ExecuteMultipleResponseItemCollection
                        {
                            new ExecuteMultipleResponseItem
                            {
                                RequestIndex = 0,
                                Response = new CreateResponse { Results = { ["id"] = createdId } }
                            }
                        };
                        return response;
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
                    ["_source_id"] = accountSourceId.ToString("D"),
                    ["_is_new"] = true,
                    ["accountid"] = accountSourceId.ToString("D"),
                    ["ownerid"] = sourceTeamId.ToString("D"),
                    ["name"] = "Owned Account"
                }
            }, columns);

            var result = SqliteDataLogic.Push(
                _svc,
                snapshot.Name,
                "src-env",
                "target-env",
                target,
                new UiSettings { Action = DmtAction.Create },
                null,
                lookupMatchKeys: new List<PushLookupMatchKey>
                {
                    new PushLookupMatchKey
                    {
                        LogicalName = "ownerid",
                        Mode = "Custom",
                        Fields = new List<string> { "name" }
                    }
                },
                sourceClient: source);

            Assert.Equal(1, result.Created);
            Assert.Empty(result.Errors);
            var owner = Assert.IsType<EntityReference>(createdEntity["ownerid"]);
            Assert.Equal("team", owner.LogicalName);
            Assert.Equal(targetTeamId, owner.Id);
        }

        [Fact]
        public void Push_CreateFailures_CountsTotalRowsAndFailedRows()
        {
            var columns = new List<DataTableColumnConfig>
            {
                new DataTableColumnConfig { LogicalName = "accountid", Type = "Uniqueidentifier", SqliteType = "TEXT" },
                new DataTableColumnConfig { LogicalName = "name", Type = "String", SqliteType = "TEXT" }
            };
            var snapshot = new DmtSnapshot
            {
                Name = "mixed-results",
                TableSuffix = "mixed_results",
                TableLogicalName = "account",
                SourceEnvId = "src-env",
                Source = "JSON",
                PrimaryIdAttribute = "accountid",
                ColumnConfig = columns
            };
            var target = new FakeOrganizationService
            {
                ExecuteHandler = request =>
                {
                    if (request is ExecuteMultipleRequest batch)
                    {
                        var response = new ExecuteMultipleResponse();
                        var responses = new ExecuteMultipleResponseItemCollection();
                        for (var i = 0; i < batch.Requests.Count; i++)
                        {
                            responses.Add(new ExecuteMultipleResponseItem
                            {
                                RequestIndex = i,
                                Fault = new OrganizationServiceFault { Message = "Lookup target does not exist" }
                            });
                        }
                        response.Results["Responses"] = responses;
                        return response;
                    }

                    return new OrganizationResponse();
                }
            };

            _svc.SaveSnapshot(snapshot);
            _svc.CreateSnapshotTable(snapshot.TableSuffix, columns);
            _svc.WriteSnapshotRecords(snapshot.TableSuffix, new[]
            {
                new Dictionary<string, object> { ["_source_id"] = Guid.NewGuid().ToString("D"), ["_is_new"] = true, ["accountid"] = Guid.NewGuid().ToString("D"), ["name"] = "Fail 1" },
                new Dictionary<string, object> { ["_source_id"] = Guid.NewGuid().ToString("D"), ["_is_new"] = true, ["accountid"] = Guid.NewGuid().ToString("D"), ["name"] = "Fail 2" },
                new Dictionary<string, object> { ["_source_id"] = Guid.NewGuid().ToString("D"), ["_is_new"] = true, ["accountid"] = Guid.NewGuid().ToString("D"), ["name"] = "Fail 3" },
                new Dictionary<string, object> { ["_source_id"] = null, ["_is_new"] = false, ["accountid"] = Guid.NewGuid().ToString("D"), ["name"] = "Skip 1" },
                new Dictionary<string, object> { ["_source_id"] = null, ["_is_new"] = false, ["accountid"] = Guid.NewGuid().ToString("D"), ["name"] = "Skip 2" }
            }, columns);

            var result = SqliteDataLogic.Push(
                _svc,
                snapshot.Name,
                "src-env",
                "target-env",
                target,
                new UiSettings { Action = DmtAction.Create },
                null);

            Assert.Equal(5, result.TotalRecords);
            Assert.Equal(3, result.Failed);
            Assert.Equal(2, result.Skipped);
            Assert.Equal(3, result.Errors.Count);
        }
    }
}
