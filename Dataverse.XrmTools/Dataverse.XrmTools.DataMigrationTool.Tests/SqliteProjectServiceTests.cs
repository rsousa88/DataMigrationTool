// System
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

// xunit
using Xunit;

// DataMigrationTool
using Dataverse.XrmTools.DataMigrationTool.Logic;
using Dataverse.XrmTools.DataMigrationTool.Models;
using Dataverse.XrmTools.DataMigrationTool.Tests.TestSupport;

namespace Dataverse.XrmTools.DataMigrationTool.Tests
{
    public class SqliteProjectServiceTests : IDisposable
    {
        private readonly SqliteProjectService _svc;

        public SqliteProjectServiceTests()
        {
            _svc = SqliteProjectService.CreateInMemory("Test Project");
        }

        public void Dispose() => _svc?.Dispose();

        // ─── Project metadata ──────────────────────────────────────────────────

        [Fact]
        public void ProjectName_ReturnsConfiguredName()
        {
            Assert.Equal("Test Project", _svc.ProjectName);
        }

        [Fact]
        public void SetProjectValue_GetProjectValue_RoundTrip()
        {
            _svc.SetProjectValue("custom_key", "custom_value");
            Assert.Equal("custom_value", _svc.GetProjectValue("custom_key"));
        }

        [Fact]
        public void SetProjectValue_Overwrites_ExistingKey()
        {
            _svc.SetProjectValue("key", "v1");
            _svc.SetProjectValue("key", "v2");
            Assert.Equal("v2", _svc.GetProjectValue("key"));
        }

        // ─── Environments ──────────────────────────────────────────────────────

        [Fact]
        public void SaveEnvironment_Source_RoundTrip()
        {
            var env = SourceEnv("env-001", "org1", "Org One", "https://org1.crm.dynamics.com");
            _svc.SaveEnvironment(env);

            var result = _svc.GetSourceEnvironment();
            Assert.NotNull(result);
            Assert.Equal("env-001", result.Id);
            Assert.Equal("org1", result.UniqueName);
            Assert.Equal("Org One", result.FriendlyName);
            Assert.Null(result.Tag);
            Assert.Equal("https://org1.crm.dynamics.com", result.Url);
            Assert.Equal("source", result.Role);
        }

        [Fact]
        public void SaveEnvironment_Target_RoundTripsTag()
        {
            var env = TargetEnv("t1", "dev", "Development");
            env.Tag = "DEV";

            _svc.SaveEnvironment(env);

            var result = Assert.Single(_svc.GetEnvironments("target"));
            Assert.Equal("DEV", result.Tag);
        }

        [Fact]
        public void SaveEnvironment_Source_AllowsTagUpdate_WhenSnapshotsExist()
        {
            _svc.SaveEnvironment(SourceEnv("env-001", "org1", "Org One"));
            CreateSnapshotWithData("snap1", "account", "env-001");

            var env = SourceEnv("env-001", "org1", "Org One");
            env.Tag = "SRC";
            _svc.SaveEnvironment(env);

            Assert.Equal("SRC", _svc.GetSourceEnvironment().Tag);
        }

        [Fact]
        public void SaveEnvironment_Source_Replaces_PriorSource()
        {
            _svc.SaveEnvironment(SourceEnv("env-001", "org1", "Org One"));
            _svc.SaveEnvironment(SourceEnv("env-002", "org2", "Org Two"));

            var sources = _svc.GetEnvironments("source");
            Assert.Single(sources);
            Assert.Equal("env-002", sources[0].Id);
        }

        [Fact]
        public void SaveEnvironment_Source_Blocked_WhenSnapshotsExist()
        {
            _svc.SaveEnvironment(SourceEnv("env-001", "org1", "Org One"));
            CreateSnapshotWithData("snap1", "account", "env-001");

            Assert.Throws<InvalidOperationException>(() =>
                _svc.SaveEnvironment(SourceEnv("env-002", "org2", "Org Two")));
        }

        [Fact]
        public void SaveEnvironment_Target_AllowsMultiple()
        {
            _svc.SaveEnvironment(TargetEnv("t1", "dev", "DEV"));
            _svc.SaveEnvironment(TargetEnv("t2", "test", "TEST"));
            _svc.SaveEnvironment(TargetEnv("t3", "prod", "PROD"));

            var targets = _svc.GetEnvironments("target");
            Assert.Equal(3, targets.Count);
        }

        [Fact]
        public void GetEnvironments_NoRole_ReturnsAll()
        {
            _svc.SaveEnvironment(SourceEnv("s1", "src", "Source"));
            _svc.SaveEnvironment(TargetEnv("t1", "dev", "DEV"));

            Assert.Equal(2, _svc.GetEnvironments().Count);
        }

        [Fact]
        public void GetSourceEnvironment_ReturnsNull_WhenNoneSet()
        {
            Assert.Null(_svc.GetSourceEnvironment());
        }

        // ─── Table configs ─────────────────────────────────────────────────────

        [Fact]
        public void SaveTableConfig_GetTableConfig_RoundTrip()
        {
            var config = new DataTableConfig
            {
                Filter = "<filter><condition attribute='statecode' operator='eq' value='0'/></filter>",
                SelectedAttributes = new List<string> { "name", "accountnumber" },
                BatchSize = 50,
                LoadMatchKeyMode = "Guid",
                AllColumns = new List<DataTableColumnConfig>
                {
                    new DataTableColumnConfig { LogicalName = "name", Type = "String", SqliteType = "TEXT" },
                    new DataTableColumnConfig { LogicalName = "revenue", Type = "Money", SqliteType = "REAL" }
                }
            };

            _svc.SaveTableConfig("account", "Account", "accountid", "name", config);

            var (result, dn, pid, pn) = _svc.GetTableConfig("account");
            Assert.NotNull(result);
            Assert.Equal("Account", dn);
            Assert.Equal("accountid", pid);
            Assert.Equal("name", pn);
            Assert.Equal("<filter><condition attribute='statecode' operator='eq' value='0'/></filter>", result.Filter);
            Assert.Equal(50, result.BatchSize);
            Assert.Equal(2, result.AllColumns.Count);
            Assert.Equal("REAL", result.AllColumns[1].SqliteType);
        }

        [Fact]
        public void GetTableConfig_ReturnsNull_WhenNotFound()
        {
            var (result, _, _, _) = _svc.GetTableConfig("nonexistent");
            Assert.Null(result);
        }

        [Fact]
        public void GetTableConfigLogicalNames_ReturnsAll()
        {
            _svc.SaveTableConfig("account", "Account", "accountid", "name", new DataTableConfig());
            _svc.SaveTableConfig("contact", "Contact", "contactid", "fullname", new DataTableConfig());

            var names = _svc.GetTableConfigLogicalNames();
            Assert.Contains("account", names);
            Assert.Contains("contact", names);
        }

        // ─── Snapshot name sanitization ────────────────────────────────────────

        [Fact]
        public void RowcraftEditSession_StagesWithoutChangingSnapshot()
        {
            var snapshot = CreateSnapshotWithRows();
            var firstRow = _svc.ReadSnapshotRecords(snapshot.TableSuffix, snapshot.ColumnConfig).First();
            var rowId = (long)firstRow["_row_id"];

            var session = _svc.StartRowcraftEditSession(snapshot.Name);
            _svc.StageRowcraftUpdate(session.Id, rowId, new Dictionary<string, object> { ["name"] = "Edited" });
            _svc.StageRowcraftCreate(session.Id, new Dictionary<string, object> { ["name"] = "Created", ["count"] = 3 });

            var rows = _svc.ReadSnapshotRecords(snapshot.TableSuffix, snapshot.ColumnConfig).ToList();
            Assert.Equal(2, rows.Count);
            Assert.Equal("A", rows[0]["name"]);

            var summary = _svc.GetRowcraftChangeSummary(session.Id);
            Assert.Equal(1, summary.Creates);
            Assert.Equal(1, summary.Updates);
            Assert.Equal(0, summary.Deletes);
        }

        [Fact]
        public void ApplyRowcraftEditSession_AppliesCreateUpdateDelete()
        {
            var snapshot = CreateSnapshotWithRows();
            var rowsBefore = _svc.ReadSnapshotRecords(snapshot.TableSuffix, snapshot.ColumnConfig).ToList();
            var updateRowId = (long)rowsBefore[0]["_row_id"];
            var deleteRowId = (long)rowsBefore[1]["_row_id"];

            var session = _svc.StartRowcraftEditSession(snapshot.Name);
            _svc.StageRowcraftUpdate(session.Id, updateRowId, new Dictionary<string, object> { ["name"] = "Edited", ["count"] = 10 });
            _svc.StageRowcraftDelete(session.Id, deleteRowId);
            _svc.StageRowcraftCreate(session.Id, new Dictionary<string, object> { ["name"] = "Created", ["count"] = 3 });

            var result = _svc.ApplyRowcraftEditSession(session.Id);

            Assert.Equal(1, result.Created);
            Assert.Equal(1, result.Updated);
            Assert.Equal(1, result.Deleted);
            Assert.Equal(2, result.RowCount);

            var rowsAfter = _svc.ReadSnapshotRecords(snapshot.TableSuffix, snapshot.ColumnConfig).ToList();
            Assert.Equal(2, rowsAfter.Count);
            Assert.Contains(rowsAfter, r => (string)r["name"] == "Edited" && (long)r["count"] == 10L);
            Assert.Contains(rowsAfter, r => (string)r["name"] == "Created" && (bool)r["_is_new"]);
            Assert.DoesNotContain(rowsAfter, r => (string)r["name"] == "B");
        }

        [Fact]
        public void DiscardRowcraftEditSession_LeavesSnapshotUnchanged()
        {
            var snapshot = CreateSnapshotWithRows();
            var firstRow = _svc.ReadSnapshotRecords(snapshot.TableSuffix, snapshot.ColumnConfig).First();
            var rowId = (long)firstRow["_row_id"];

            var session = _svc.StartRowcraftEditSession(snapshot.Name);
            _svc.StageRowcraftUpdate(session.Id, rowId, new Dictionary<string, object> { ["name"] = "Edited" });
            _svc.DiscardRowcraftEditSession(session.Id);

            Assert.Throws<InvalidOperationException>(() =>
                _svc.StageRowcraftCreate(session.Id, new Dictionary<string, object> { ["name"] = "AfterDiscard" }));

            var rows = _svc.ReadSnapshotRecords(snapshot.TableSuffix, snapshot.ColumnConfig).ToList();
            Assert.Equal("A", rows[0]["name"]);
            Assert.Equal("Discarded", _svc.GetRowcraftEditSession(session.Id).Status);
        }

        [Fact]
        public void StageRowcraftUpdate_BlocksInternalColumns()
        {
            var snapshot = CreateSnapshotWithRows();
            var firstRow = _svc.ReadSnapshotRecords(snapshot.TableSuffix, snapshot.ColumnConfig).First();
            var rowId = (long)firstRow["_row_id"];
            var session = _svc.StartRowcraftEditSession(snapshot.Name);

            Assert.Throws<InvalidOperationException>(() =>
                _svc.StageRowcraftUpdate(session.Id, rowId, new Dictionary<string, object> { ["_source_id"] = "bad" }));
        }

        [Fact]
        public void StageRowcraftCreate_PreservesClientRowId()
        {
            var snapshot = CreateSnapshotWithRows();
            var session = _svc.StartRowcraftEditSession(snapshot.Name);

            var change = _svc.StageRowcraftCreate(
                session.Id,
                new Dictionary<string, object> { ["name"] = "Created", ["count"] = 3 },
                "rowcraft-temp-1");

            Assert.Equal("rowcraft-temp-1", change.ClientRowId);
            Assert.Equal("Created", change.After["name"]);
        }

        [Theory]
        [InlineData("My Accounts", "my_accounts")]
        [InlineData("Account (Active)", "account_active")]
        [InlineData("UPPER CASE", "upper_case")]
        [InlineData("already_clean", "already_clean")]
        [InlineData("  spaces  ", "spaces")]
        [InlineData("", "snapshot")]
        [InlineData("!!!special!!!", "special")]   // strips ! but "special" remains — not empty
        [InlineData("a1_b2", "a1_b2")]
        public void SanitizeSnapshotName_ReturnsExpected(string input, string expected)
        {
            Assert.Equal(expected, SqliteProjectService.SanitizeSnapshotName(input));
        }

        [Fact]
        public void ResolveTableSuffix_NoCollision_ReturnsSanitized()
        {
            var suffix = _svc.ResolveTableSuffix("My Accounts");
            Assert.Equal("my_accounts", suffix);
        }

        [Fact]
        public void ResolveTableSuffix_Collision_AppendsCounter()
        {
            CreateSnapshotWithData("My Accounts", "account", "env-001");

            var suffix = _svc.ResolveTableSuffix("My Accounts");
            Assert.Equal("my_accounts_2", suffix);
        }

        // ─── Snapshots ─────────────────────────────────────────────────────────

        [Fact]
        public void SaveSnapshot_GetSnapshot_RoundTrip()
        {
            var snapshot = MakeSnapshot("snap1", "account", "env-001",
                new List<DataTableColumnConfig>
                {
                    new DataTableColumnConfig { LogicalName = "name", Type = "String", SqliteType = "TEXT" }
                });

            _svc.SaveSnapshot(snapshot);

            var result = _svc.GetSnapshot("snap1");
            Assert.NotNull(result);
            Assert.Equal("snap1", result.Name);
            Assert.Equal("account", result.TableLogicalName);
            Assert.Equal("env-001", result.SourceEnvId);
            Assert.Equal("Pull", result.Source);
            Assert.Single(result.ColumnConfig);
            Assert.Equal("name", result.ColumnConfig[0].LogicalName);
        }

        [Fact]
        public void SaveSnapshot_GetSnapshot_RoundTripsRefreshMetadata()
        {
            var snapshot = MakeSnapshot("snap-refresh", "account", "env-001",
                new List<DataTableColumnConfig>
                {
                    new DataTableColumnConfig { LogicalName = "accountid", Type = "Uniqueidentifier", SqliteType = "TEXT" },
                    new DataTableColumnConfig { LogicalName = "name", Type = "String", SqliteType = "TEXT" }
                });
            snapshot.Source = "JSON";
            snapshot.SourceFilePath = "imports\\accounts.json";
            snapshot.PullFilter = "<filter><condition attribute='statecode' operator='eq' value='0'/></filter>";
            snapshot.PrimaryIdAttribute = "accountid";

            _svc.SaveSnapshot(snapshot);

            var result = _svc.GetSnapshot("snap-refresh");
            Assert.NotNull(result);
            Assert.Equal("imports\\accounts.json", result.SourceFilePath);
            Assert.Equal("<filter><condition attribute='statecode' operator='eq' value='0'/></filter>", result.PullFilter);
            Assert.Equal("accountid", result.PrimaryIdAttribute);
        }

        [Fact]
        public void HasSnapshot_ReturnsTrueWhenExists()
        {
            CreateSnapshotWithData("snap1", "account", "env-001");
            Assert.True(_svc.HasSnapshot("snap1"));
            Assert.False(_svc.HasSnapshot("other"));
        }

        [Fact]
        public void GetSnapshots_ReturnsAll()
        {
            CreateSnapshotWithData("snap1", "account", "env-001");
            CreateSnapshotWithData("snap2", "contact", "env-001");

            Assert.Equal(2, _svc.GetSnapshots().Count);
        }

        [Fact]
        public void DeleteSnapshot_RemovesRowAndDropsDataTable()
        {
            CreateSnapshotWithData("snap1", "account", "env-001");
            Assert.True(_svc.HasSnapshot("snap1"));

            _svc.DeleteSnapshot("snap1");
            Assert.False(_svc.HasSnapshot("snap1"));
        }

        [Fact]
        public void DeleteSnapshot_Throws_WhenNotFound()
        {
            Assert.Throws<InvalidOperationException>(() => _svc.DeleteSnapshot("nonexistent"));
        }

        // ─── Snapshot data table ───────────────────────────────────────────────

        [Fact]
        public void CreateSnapshotTable_AllSqliteTypes_WritesAndReadsCorrectly()
        {
            var columns = new List<DataTableColumnConfig>
            {
                new DataTableColumnConfig { LogicalName = "name",    SqliteType = "TEXT" },
                new DataTableColumnConfig { LogicalName = "count",   SqliteType = "INTEGER" },
                new DataTableColumnConfig { LogicalName = "amount",  SqliteType = "REAL" },
                new DataTableColumnConfig { LogicalName = "flag",    SqliteType = "INTEGER" }
            };

            _svc.CreateSnapshotTable("test_table", columns);

            var rows = new List<Dictionary<string, object>>
            {
                new Dictionary<string, object>
                {
                    ["_source_id"] = Guid.NewGuid().ToString(),
                    ["_is_new"]    = false,
                    ["name"]       = "Contoso",
                    ["count"]      = 42L,
                    ["amount"]     = 99.99,
                    ["flag"]       = true
                }
            };

            _svc.WriteSnapshotRecords("test_table", rows, columns);

            var read = _svc.ReadSnapshotRecords("test_table", columns).ToList();
            Assert.Single(read);
            Assert.Equal("Contoso", read[0]["name"]);
            Assert.Equal(42L, read[0]["count"]);
            Assert.Equal(99.99, (double)read[0]["amount"], 5);
            Assert.Equal(1L, read[0]["flag"]);  // bool true stored as 1
        }

        [Fact]
        public void WriteSnapshotRecords_NullValues_StoredAndReadAsNull()
        {
            var columns = new List<DataTableColumnConfig>
            {
                new DataTableColumnConfig { LogicalName = "name",   SqliteType = "TEXT" },
                new DataTableColumnConfig { LogicalName = "amount", SqliteType = "REAL" }
            };
            _svc.CreateSnapshotTable("nulltest", columns);

            var rows = new List<Dictionary<string, object>>
            {
                new Dictionary<string, object> { ["_source_id"] = null, ["name"] = null, ["amount"] = null }
            };
            _svc.WriteSnapshotRecords("nulltest", rows, columns);

            var read = _svc.ReadSnapshotRecords("nulltest", columns).ToList();
            Assert.Single(read);
            Assert.Null(read[0]["name"]);
            Assert.Null(read[0]["amount"]);
            Assert.Null(read[0]["_source_id"]);
        }

        [Fact]
        public void WriteSnapshotRecords_UpdatesSnapshotRowCount()
        {
            var columns = new List<DataTableColumnConfig>
            {
                new DataTableColumnConfig { LogicalName = "name", SqliteType = "TEXT" }
            };
            var snapshot = MakeSnapshot("rctest", "account", "env-001", columns);
            _svc.SaveSnapshot(snapshot);
            _svc.CreateSnapshotTable(snapshot.TableSuffix, columns);

            var rows = new List<Dictionary<string, object>>
            {
                new Dictionary<string, object> { ["name"] = "A" },
                new Dictionary<string, object> { ["name"] = "B" },
                new Dictionary<string, object> { ["name"] = "C" }
            };
            _svc.WriteSnapshotRecords(snapshot.TableSuffix, rows, columns);

            var updated = _svc.GetSnapshot("rctest");
            Assert.Equal(3, updated.RowCount);
        }

        [Fact]
        public void ReplaceSnapshotData_RollsBackSnapshotMetadata_WhenTableCreateFails()
        {
            var snapshot = MakeSnapshot("bad-snapshot", "account", "env-001",
                new List<DataTableColumnConfig>
                {
                    new DataTableColumnConfig { LogicalName = "_source_id", SqliteType = "TEXT" }
                });

            Assert.Throws<InvalidOperationException>(() =>
                _svc.ReplaceSnapshotData(snapshot, snapshot.ColumnConfig, Enumerable.Empty<Dictionary<string, object>>()));

            Assert.Null(_svc.GetSnapshot("bad-snapshot"));
        }

        [Fact]
        public void ReadSnapshotRecords_Pagination_ReturnsCorrectWindow()
        {
            var columns = new List<DataTableColumnConfig>
            {
                new DataTableColumnConfig { LogicalName = "name", SqliteType = "TEXT" }
            };
            _svc.CreateSnapshotTable("pagetest", columns);

            var rows = Enumerable.Range(1, 10)
                .Select(i => new Dictionary<string, object> { ["name"] = $"Row{i}" })
                .ToList();
            _svc.WriteSnapshotRecords("pagetest", rows, columns);

            var page1 = _svc.ReadSnapshotRecords("pagetest", columns, offset: 0, limit: 3).ToList();
            var page2 = _svc.ReadSnapshotRecords("pagetest", columns, offset: 3, limit: 3).ToList();

            Assert.Equal(3, page1.Count);
            Assert.Equal(3, page2.Count);
            Assert.Equal("Row1", page1[0]["name"]);
            Assert.Equal("Row4", page2[0]["name"]);
        }

        [Fact]
        public void CountSnapshotRows_ReturnsCorrectCount()
        {
            var columns = new List<DataTableColumnConfig>
            {
                new DataTableColumnConfig { LogicalName = "name", SqliteType = "TEXT" }
            };
            _svc.CreateSnapshotTable("cnttest", columns);

            var rows = Enumerable.Range(1, 5)
                .Select(i => new Dictionary<string, object> { ["name"] = $"Row{i}" })
                .ToList();
            _svc.WriteSnapshotRecords("cnttest", rows, columns);

            Assert.Equal(5, _svc.CountSnapshotRows("cnttest"));
        }

        [Fact]
        public void CreateSnapshotTable_GuardsReservedColumnNames()
        {
            var columns = new List<DataTableColumnConfig>
            {
                new DataTableColumnConfig { LogicalName = "_source_id", SqliteType = "TEXT" }
            };
            Assert.Throws<InvalidOperationException>(() => _svc.CreateSnapshotTable("bad", columns));
        }

        [Fact]
        public void CreateSnapshotTable_DropsAndRecreates_ExistingTable()
        {
            var columns = new List<DataTableColumnConfig>
            {
                new DataTableColumnConfig { LogicalName = "name", SqliteType = "TEXT" }
            };
            _svc.CreateSnapshotTable("recreate", columns);
            _svc.WriteSnapshotRecords("recreate",
                new[] { new Dictionary<string, object> { ["name"] = "old" } }, columns);

            // Recreate wipes data
            _svc.CreateSnapshotTable("recreate", columns);
            Assert.Equal(0, _svc.CountSnapshotRows("recreate"));
        }

        // ─── OptionSet values ──────────────────────────────────────────────────

        [Fact]
        public void SaveOptionSetValues_GetOptionSetValues_RoundTrip()
        {
            var options = new List<OptionConfig>
            {
                new OptionConfig { Value = 1, Label = "Active" },
                new OptionConfig { Value = 2, Label = "Inactive" }
            };
            _svc.SaveOptionSetValues("account", "statecode", options);

            var result = _svc.GetOptionSetValues("account", "statecode");
            Assert.Equal(2, result.Count);
            Assert.Equal("Active", result[1]);
            Assert.Equal("Inactive", result[2]);
        }

        [Fact]
        public void SaveOptionSetValues_Overwrites_PriorValues()
        {
            _svc.SaveOptionSetValues("account", "statecode", new List<OptionConfig>
            {
                new OptionConfig { Value = 1, Label = "OldLabel" }
            });
            _svc.SaveOptionSetValues("account", "statecode", new List<OptionConfig>
            {
                new OptionConfig { Value = 1, Label = "NewLabel" }
            });

            var result = _svc.GetOptionSetValues("account", "statecode");
            Assert.Equal("NewLabel", result[1]);
        }

        [Fact]
        public void ResolveOptionSetLabel_ReturnsValue_WhenFound()
        {
            _svc.SaveOptionSetValues("account", "statecode", new List<OptionConfig>
            {
                new OptionConfig { Value = 1, Label = "Active" }
            });
            Assert.Equal(1, _svc.ResolveOptionSetLabel("account", "statecode", "Active"));
        }

        [Fact]
        public void ResolveOptionSetLabel_ReturnsNull_WhenNotFound()
        {
            Assert.Null(_svc.ResolveOptionSetLabel("account", "statecode", "Unknown"));
        }

        // ─── Plans ─────────────────────────────────────────────────────────────

        [Fact]
        public void SavePlan_GetPlans_RoundTrip()
        {
            var plan = new DmtPlan
            {
                Id = Guid.NewGuid().ToString("D"),
                Name = "Migration Plan",
                Description = "Test plan",
                Defaults = new ExecutionPlanDefaults { MaxFailedRecords = 5 }
            };
            _svc.SavePlan(plan);

            var plans = _svc.GetPlans();
            Assert.Single(plans);
            Assert.Equal("Migration Plan", plans[0].Name);
            Assert.Equal(5, plans[0].Defaults.MaxFailedRecords);
        }

        [Fact]
        public void DeletePlan_RemovesPlanAndSteps()
        {
            var plan = new DmtPlan { Id = "plan-1", Name = "P1" };
            _svc.SavePlan(plan);
            _svc.SavePlanStep(new DmtPlanStep { Id = "step-1", PlanId = "plan-1", Operation = "Pull", SortOrder = 1 });

            _svc.DeletePlan("plan-1");
            Assert.Empty(_svc.GetPlans());
            Assert.Empty(_svc.GetPlanSteps("plan-1"));
        }

        // ─── Plan steps ────────────────────────────────────────────────────────

        [Fact]
        public void ReplacePlan_ReplacesStaleSteps()
        {
            var plan = new DmtPlan { Id = "plan-1", Name = "P1" };
            _svc.SavePlan(plan);
            _svc.SavePlanStep(new DmtPlanStep { Id = "old-step", PlanId = "plan-1", Operation = "Pull", SortOrder = 0 });

            plan.Name = "Updated P1";
            _svc.ReplacePlan(plan, new[]
            {
                new DmtPlanStep { Id = "new-step", PlanId = "plan-1", Operation = "PushFromSnapshot", SortOrder = 0, SnapshotName = "accounts" }
            });

            var plans = _svc.GetPlans();
            var steps = _svc.GetPlanSteps("plan-1");
            Assert.Equal("Updated P1", Assert.Single(plans).Name);
            var step = Assert.Single(steps);
            Assert.Equal("new-step", step.Id);
            Assert.Equal("accounts", step.SnapshotName);
        }

        [Fact]
        public void SavePlanStep_GetPlanSteps_RoundTrip()
        {
            var plan = new DmtPlan { Id = "plan-1", Name = "P1" };
            _svc.SavePlan(plan);

            var step = new DmtPlanStep
            {
                Id              = "step-1",
                PlanId          = "plan-1",
                SortOrder       = 1,
                Name            = "Pull Accounts",
                Operation       = "Pull",
                TableLogicalName = "account",
                SnapshotName    = "account_full",
                Snapshot        = new DmtPlanStepSnapshot
                {
                    LoadMatchKeyMode = "Guid",
                    SelectedAttributes = new List<string> { "name", "accountnumber" }
                }
            };
            _svc.SavePlanStep(step);

            var steps = _svc.GetPlanSteps("plan-1");
            Assert.Single(steps);
            Assert.Equal("Pull Accounts", steps[0].Name);
            Assert.Equal("account_full", steps[0].SnapshotName);
            Assert.Equal("Guid", steps[0].Snapshot.LoadMatchKeyMode);
            Assert.Equal(2, steps[0].Snapshot.SelectedAttributes.Count);
        }

        [Fact]
        public void DeletePlanStep_RemovesStep()
        {
            var plan = new DmtPlan { Id = "plan-1", Name = "P1" };
            _svc.SavePlan(plan);
            _svc.SavePlanStep(new DmtPlanStep { Id = "step-1", PlanId = "plan-1", Operation = "Pull", SortOrder = 1 });
            _svc.SavePlanStep(new DmtPlanStep { Id = "step-2", PlanId = "plan-1", Operation = "Push", SortOrder = 2 });

            _svc.DeletePlanStep("step-1");
            var steps = _svc.GetPlanSteps("plan-1");
            Assert.Single(steps);
            Assert.Equal("step-2", steps[0].Id);
        }

        // ─── ID mappings ───────────────────────────────────────────────────────

        [Fact]
        public void SaveIdMapping_ResolveTargetId_RoundTrip()
        {
            var srcId = Guid.NewGuid().ToString("D");
            var tgtId = Guid.NewGuid().ToString("D");

            _svc.SaveIdMapping("account", "src-env", srcId, "dev-env", tgtId);

            var resolved = _svc.ResolveTargetId("account", "src-env", srcId, "dev-env");
            Assert.Equal(tgtId, resolved);
        }

        [Fact]
        public void ResolveTargetId_ReturnsNull_WhenNotFound()
        {
            Assert.Null(_svc.ResolveTargetId("account", "src", Guid.NewGuid().ToString(), "tgt"));
        }

        [Fact]
        public void RemoveIdMapping_RemovesEntry()
        {
            var srcId = Guid.NewGuid().ToString("D");
            _svc.SaveIdMapping("account", "src", srcId, "dev", Guid.NewGuid().ToString());
            _svc.RemoveIdMapping("account", "src", srcId, "dev");
            Assert.Null(_svc.ResolveTargetId("account", "src", srcId, "dev"));
        }

        [Fact]
        public void GetAllIdMappings_ReturnsAllForEnvPair()
        {
            var id1 = Guid.NewGuid().ToString("D");
            var id2 = Guid.NewGuid().ToString("D");
            _svc.SaveIdMapping("account", "src", id1, "dev", Guid.NewGuid().ToString());
            _svc.SaveIdMapping("account", "src", id2, "dev", Guid.NewGuid().ToString());
            _svc.SaveIdMapping("contact", "src", id1, "dev", Guid.NewGuid().ToString()); // different table

            var mappings = _svc.GetAllIdMappings("account", "src", "dev");
            Assert.Equal(2, mappings.Count);
        }

        [Fact]
        public void IdMappings_PersistedAfterSnapshotDeleted()
        {
            _svc.SaveEnvironment(SourceEnv("src", "org1", "Org1"));
            CreateSnapshotWithData("snap1", "account", "src");

            var srcId = Guid.NewGuid().ToString("D");
            _svc.SaveIdMapping("account", "src", srcId, "dev", Guid.NewGuid().ToString());

            _svc.DeleteSnapshot("snap1");

            // ID mapping must still exist after snapshot deletion
            Assert.NotNull(_svc.ResolveTargetId("account", "src", srcId, "dev"));
        }

        // ─── Run logs ──────────────────────────────────────────────────────────

        [Fact]
        public void SaveRunLog_GetRunLogs_RoundTrip()
        {
            var log = new DmtRunLog
            {
                Id        = Guid.NewGuid().ToString("D"),
                PlanName  = "Test Plan",
                StartedOn = DateTime.UtcNow,
                Status    = "Completed",
                Log       = new ExecutionPlanRunLog { PlanName = "Test Plan" }
            };
            _svc.SaveRunLog(log);

            var logs = _svc.GetRunLogs();
            Assert.Single(logs);
            Assert.Equal("Completed", logs[0].Status);
            Assert.Equal("Test Plan", logs[0].Log.PlanName);
        }

        // ─── SQLite type mapping ───────────────────────────────────────────────

        [Theory]
        [InlineData("Integer",  "INTEGER")]
        [InlineData("BigInt",   "INTEGER")]
        [InlineData("Boolean",  "INTEGER")]
        [InlineData("OptionSet", "INTEGER")]
        [InlineData("Picklist", "INTEGER")]
        [InlineData("State",    "INTEGER")]
        [InlineData("Status",   "INTEGER")]
        [InlineData("Double",   "REAL")]
        [InlineData("Decimal",  "REAL")]
        [InlineData("Money",    "REAL")]
        [InlineData("String",   "TEXT")]
        [InlineData("Memo",     "TEXT")]
        [InlineData("DateTime", "TEXT")]
        [InlineData("Lookup",   "TEXT")]
        [InlineData("Uniqueidentifier", "TEXT")]
        [InlineData("MultiSelectPicklist", "TEXT")]
        public void GetSqliteType_MapsCorrectly(string attrType, string expected)
        {
            Assert.Equal(expected, SqliteProjectService.GetSqliteType(attrType));
        }

        [Theory]
        [InlineData("Virtual",         true)]
        [InlineData("ManagedProperty", true)]
        [InlineData("CalcRollup",      true)]
        [InlineData("Image",           true)]
        [InlineData("File",            true)]
        [InlineData("String",          false)]
        [InlineData("Lookup",          false)]
        public void IsExcludedAttributeType_ReturnsExpected(string attrType, bool expected)
        {
            Assert.Equal(expected, SqliteProjectService.IsExcludedAttributeType(attrType));
        }

        // ─── CreateProject / OpenProject (file-based) ─────────────────────────

        [Fact]
        public void CreateProject_OpensAndCreatesSchema()
        {
            using var tmp = new TemporaryFileScope();
            var filePath = tmp.GetPath("test.dmtproj");

            using var svc = new SqliteProjectService();
            svc.CreateProject(filePath, "File Project");

            Assert.True(svc.IsOpen);
            Assert.Equal("File Project", svc.ProjectName);
        }

        [Fact]
        public void CreateProject_Throws_WhenFileExists()
        {
            using var tmp = new TemporaryFileScope();
            var filePath = tmp.WriteText("test.dmtproj");  // creates the file

            using var svc = new SqliteProjectService();
            Assert.Throws<InvalidOperationException>(() => svc.CreateProject(filePath, "P"));
        }

        [Fact]
        public void OpenProject_PersistsDataAcrossReopens()
        {
            using var tmp = new TemporaryFileScope();
            var filePath = tmp.GetPath("persistent.dmtproj");

            using (var svc = new SqliteProjectService())
            {
                svc.CreateProject(filePath, "Persistent");
                svc.SetProjectValue("custom", "hello");
            }

            using (var svc2 = new SqliteProjectService())
            {
                svc2.OpenProject(filePath);
                Assert.Equal("hello", svc2.GetProjectValue("custom"));
                Assert.Equal("Persistent", svc2.ProjectName);
            }
        }

        // ─── Helpers ──────────────────────────────────────────────────────────

        private static DmtProjectEnvironment SourceEnv(string id, string unique, string friendly, string url = null) =>
            new DmtProjectEnvironment { Id = id, UniqueName = unique, FriendlyName = friendly, Url = url, Role = "source" };

        private static DmtProjectEnvironment TargetEnv(string id, string unique, string friendly) =>
            new DmtProjectEnvironment { Id = id, UniqueName = unique, FriendlyName = friendly, Role = "target" };

        private static DmtSnapshot MakeSnapshot(string name, string table, string envId,
            List<DataTableColumnConfig> columns = null)
        {
            var suffix = SqliteProjectService.SanitizeSnapshotName(name);
            return new DmtSnapshot
            {
                Id = Guid.NewGuid().ToString("D"),
                Name = name,
                TableSuffix = suffix,
                TableLogicalName = table,
                SourceEnvId = envId,
                Source = "Pull",
                LoadMatchKeyMode = "Guid",
                ColumnConfig = columns ?? new List<DataTableColumnConfig>()
            };
        }

        // Creates a snapshot row AND its data table (with zero rows).
        private void CreateSnapshotWithData(string name, string table, string envId)
        {
            var columns = new List<DataTableColumnConfig>
            {
                new DataTableColumnConfig { LogicalName = "name", SqliteType = "TEXT" }
            };
            var snapshot = MakeSnapshot(name, table, envId, columns);
            _svc.SaveSnapshot(snapshot);
            _svc.CreateSnapshotTable(snapshot.TableSuffix, columns);
        }

        private DmtSnapshot CreateSnapshotWithRows()
        {
            var columns = new List<DataTableColumnConfig>
            {
                new DataTableColumnConfig { LogicalName = "name", Type = "String", SqliteType = "TEXT" },
                new DataTableColumnConfig { LogicalName = "count", Type = "Integer", SqliteType = "INTEGER" }
            };
            var snapshot = MakeSnapshot("rowcraft_snap", "account", "env-001", columns);
            snapshot.Source = "Excel";
            _svc.SaveSnapshot(snapshot);
            _svc.CreateSnapshotTable(snapshot.TableSuffix, columns);
            _svc.WriteSnapshotRecords(snapshot.TableSuffix, new[]
            {
                new Dictionary<string, object> { ["_source_id"] = "src-a", ["name"] = "A", ["count"] = 1 },
                new Dictionary<string, object> { ["_source_id"] = "src-b", ["name"] = "B", ["count"] = 2 }
            }, columns);
            return _svc.GetSnapshot(snapshot.Name);
        }
    }
}
