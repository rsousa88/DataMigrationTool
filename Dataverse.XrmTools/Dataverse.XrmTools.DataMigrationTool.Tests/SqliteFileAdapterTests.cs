// System
using System;
using System.Collections.Generic;
using System.Linq;

// ClosedXML
using ClosedXML.Excel;

// Microsoft
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Messages;

// Newtonsoft
using Newtonsoft.Json;

// xunit
using Xunit;

// DataMigrationTool
using Dataverse.XrmTools.DataMigrationTool.Enums;
using Dataverse.XrmTools.DataMigrationTool.Logic;
using Dataverse.XrmTools.DataMigrationTool.Models;
using Dataverse.XrmTools.DataMigrationTool.Tests.TestSupport;
using DmtRecord = Dataverse.XrmTools.DataMigrationTool.Models.Record;

namespace Dataverse.XrmTools.DataMigrationTool.Tests
{
    public class SqliteFileAdapterTests : IDisposable
    {
        private readonly SqliteProjectService _svc;
        private readonly TemporaryFileScope _tmp;

        public SqliteFileAdapterTests()
        {
            _svc = SqliteProjectService.CreateInMemory("AdapterTests");
            _tmp = new TemporaryFileScope();
        }

        public void Dispose()
        {
            _svc?.Dispose();
            _tmp?.Dispose();
        }

        // ─── JSON ──────────────────────────────────────────────────────────────

        [Fact]
        public void LoadFromJson_CreatesSnapshot_WithCorrectRowCount()
        {
            var collection = TestDataBuilder.RecordCollection("account", "accountid");
            var jsonPath = _tmp.WriteJson("accounts.json", collection);

            var snapshot = SqliteFileAdapter.LoadFromJson(_svc, jsonPath, "accounts-snap", "env1", null, null);

            Assert.NotNull(snapshot);
            Assert.Equal(1, snapshot.RowCount);
            Assert.Equal("account", snapshot.TableLogicalName);
            Assert.Equal("JSON", snapshot.Source);
        }

        [Fact]
        public void LoadFromJson_SetsSnapshotName()
        {
            var collection = TestDataBuilder.RecordCollection();
            var jsonPath = _tmp.WriteJson("test.json", collection);

            var snapshot = SqliteFileAdapter.LoadFromJson(_svc, jsonPath, "my-snapshot", "env1", null, null);

            Assert.Equal("my-snapshot", snapshot.Name);
        }

        [Fact]
        public void LoadFromJson_RowsAreReadable_FromProject()
        {
            var collection = TestDataBuilder.RecordCollection("account", "accountid");
            var jsonPath = _tmp.WriteJson("test.json", collection);

            var snapshot = SqliteFileAdapter.LoadFromJson(_svc, jsonPath, "snap1", "env1", null, null);
            var rows = _svc.ReadSnapshotRecords(snapshot.TableSuffix, snapshot.ColumnConfig).ToList();

            Assert.Single(rows);
            Assert.Equal("Contoso", rows[0]["name"]?.ToString());
        }

        [Fact]
        public void LoadFromJson_Sets_SourceId_FromPrimaryIdAttribute()
        {
            var recordId = Guid.NewGuid();
            var collection = new RecordCollection
            {
                LogicalName = "account",
                PrimaryIdAttribute = "accountid",
                Count = 1,
                Records = new List<DmtRecord>
                {
                    new DmtRecord
                    {
                        SourceRowNumber = 1,
                        Attributes = new List<RecordAttribute>
                        {
                            new RecordAttribute { Key = "accountid", Type = AttributeType.Identifier, Value = recordId },
                            new RecordAttribute { Key = "name", Type = AttributeType.Standard, Value = "Test Co" }
                        }
                    }
                }
            };
            var jsonPath = _tmp.WriteJson("accts.json", collection);

            var snapshot = SqliteFileAdapter.LoadFromJson(_svc, jsonPath, "snap2", "env1", null, null);
            var rows = _svc.ReadSnapshotRecords(snapshot.TableSuffix, snapshot.ColumnConfig).ToList();

            Assert.Single(rows);
            Assert.Equal(recordId.ToString(), rows[0]["_source_id"]?.ToString());
        }

        [Fact]
        public void LoadFromJson_Overwrites_ExistingSnapshot_OnSecondLoad()
        {
            var col1 = TestDataBuilder.RecordCollection();
            var col2 = new RecordCollection
            {
                LogicalName = "account",
                PrimaryIdAttribute = "accountid",
                Count = 2,
                Records = new List<DmtRecord>
                {
                    new DmtRecord { SourceRowNumber = 1, Attributes = new List<RecordAttribute> { new RecordAttribute { Key = "name", Type = AttributeType.Standard, Value = "A" } } },
                    new DmtRecord { SourceRowNumber = 2, Attributes = new List<RecordAttribute> { new RecordAttribute { Key = "name", Type = AttributeType.Standard, Value = "B" } } }
                }
            };
            var path1 = _tmp.WriteJson("c1.json", col1);
            var path2 = _tmp.WriteJson("c2.json", col2);

            SqliteFileAdapter.LoadFromJson(_svc, path1, "same-snap", "env1", null, null);
            var snap2 = SqliteFileAdapter.LoadFromJson(_svc, path2, "same-snap", "env1", null, null);

            Assert.Equal(2, snap2.RowCount);
            var rows = _svc.ReadSnapshotRecords(snap2.TableSuffix, snap2.ColumnConfig).ToList();
            Assert.Equal(2, rows.Count);
        }

        [Fact]
        public void LoadFromJson_ThrowsOnInvalidFile()
        {
            var path = _tmp.WriteText("bad.json", "not-json-at-all");
            Assert.ThrowsAny<Exception>(() =>
                SqliteFileAdapter.LoadFromJson(_svc, path, "snap", "env1", null, null));
        }

        [Fact]
        public void LoadFromJson_UsesConfigColumns_WhenProvided()
        {
            var collection = TestDataBuilder.RecordCollection("account", "accountid");
            var jsonPath = _tmp.WriteJson("test.json", collection);
            var config = new DataTableConfig
            {
                AllColumns = new List<DataTableColumnConfig>
                {
                    new DataTableColumnConfig { LogicalName = "accountid", DisplayName = "Account ID", Type = "Identifier", SqliteType = "TEXT" },
                    new DataTableColumnConfig { LogicalName = "name", DisplayName = "Name", Type = "Standard", SqliteType = "TEXT" }
                },
                SelectedAttributes = new List<string> { "accountid", "name" }
            };

            var snapshot = SqliteFileAdapter.LoadFromJson(_svc, jsonPath, "snap-config", "env1", config, null);

            Assert.Equal(2, snapshot.ColumnConfig.Count);
            Assert.Contains(snapshot.ColumnConfig, c => c.LogicalName == "accountid");
            Assert.Contains(snapshot.ColumnConfig, c => c.LogicalName == "name");
        }

        [Fact]
        public void LoadFromJson_CustomMatchKey_Resolves_SourceId_FromSource()
        {
            var sourceId = Guid.NewGuid();
            var collection = RecordCollectionWithAccountNumber("A-100", id: null);
            var jsonPath = _tmp.WriteJson("match.json", collection);
            var source = new FakeOrganizationService
            {
                ExecuteHandler = request =>
                {
                    if (request is RetrieveMultipleRequest)
                    {
                        return new RetrieveMultipleResponse
                        {
                            Results =
                            {
                                ["EntityCollection"] = new EntityCollection(new[]
                                {
                                    new Entity("account") { Id = sourceId }
                                }.ToList())
                            }
                        };
                    }
                    return new OrganizationResponse();
                }
            };

            var snapshot = SqliteFileAdapter.LoadFromJson(_svc, jsonPath, "match-snap", "env1", MatchKeyConfig(), source, null);
            var rows = _svc.ReadSnapshotRecords(snapshot.TableSuffix, snapshot.ColumnConfig).ToList();

            Assert.Single(rows);
            Assert.Equal(sourceId.ToString("D"), rows[0]["_source_id"]?.ToString());
            Assert.False((bool)rows[0]["_is_new"]);
        }

        [Fact]
        public void LoadFromJson_CustomMatchKey_Marks_Row_New_WhenSourceMissing()
        {
            var collection = RecordCollectionWithAccountNumber("A-404", id: null);
            var jsonPath = _tmp.WriteJson("new.json", collection);
            var source = new FakeOrganizationService
            {
                ExecuteHandler = request => request is RetrieveMultipleRequest
                    ? new RetrieveMultipleResponse
                    {
                        Results =
                        {
                            ["EntityCollection"] = new EntityCollection()
                        }
                    }
                    : new OrganizationResponse()
            };

            var snapshot = SqliteFileAdapter.LoadFromJson(_svc, jsonPath, "new-snap", "env1", MatchKeyConfig(), source, null);
            var rows = _svc.ReadSnapshotRecords(snapshot.TableSuffix, snapshot.ColumnConfig).ToList();

            Assert.Single(rows);
            Assert.True(Guid.TryParse(rows[0]["_source_id"]?.ToString(), out _));
            Assert.True((bool)rows[0]["_is_new"]);
        }

        // ─── Excel ─────────────────────────────────────────────────────────────

        [Fact]
        public void LoadFromExcel_ThrowsWhenMetaSheetMissing()
        {
            var excelPath = _tmp.GetExcelPath("no-meta.xlsx");
            using (var wb = new XLWorkbook())
            {
                wb.AddWorksheet("Data");
                wb.SaveAs(excelPath);
            }

            Assert.Throws<InvalidOperationException>(() =>
                SqliteFileAdapter.LoadFromExcel(_svc, excelPath, "snap", "env1", null, null));
        }

        [Fact]
        public void LoadFromExcel_ThrowsWhenDataSheetMissing()
        {
            var excelPath = _tmp.GetExcelPath("no-data.xlsx");
            var exportConfig = TestDataBuilder.ExcelExportConfig("account", "accountid");

            using (var wb = new XLWorkbook())
            {
                var meta = wb.AddWorksheet("_dmt");
                meta.Cell("A1").SetValue(JsonConvert.SerializeObject(exportConfig));
                wb.SaveAs(excelPath);
            }

            Assert.Throws<InvalidOperationException>(() =>
                SqliteFileAdapter.LoadFromExcel(_svc, excelPath, "snap", "env1", null, null));
        }

        [Fact]
        public void LoadFromExcel_CreatesSnapshot_WithCorrectRowCount()
        {
            var excelPath = BuildTestExcel("accts.xlsx", "account", "accountid",
                new[] { ("accountid", "Account ID"), ("name", "Name") },
                new[] { new[] { Guid.NewGuid().ToString(), "Contoso" } });

            var snapshot = SqliteFileAdapter.LoadFromExcel(_svc, excelPath, "excel-snap", "env1", null, null);

            Assert.NotNull(snapshot);
            Assert.Equal(1, snapshot.RowCount);
            Assert.Equal("account", snapshot.TableLogicalName);
            Assert.Equal("Excel", snapshot.Source);
        }

        [Fact]
        public void LoadFromExcel_SetsSnapshotName()
        {
            var excelPath = BuildTestExcel("test.xlsx", "account", "accountid",
                new[] { ("accountid", "Account ID"), ("name", "Name") },
                new[] { new[] { Guid.NewGuid().ToString(), "Acme" } });

            var snapshot = SqliteFileAdapter.LoadFromExcel(_svc, excelPath, "my-excel-snap", "env1", null, null);

            Assert.Equal("my-excel-snap", snapshot.Name);
        }

        [Fact]
        public void LoadFromExcel_RowsAreReadable_FromProject()
        {
            var id = Guid.NewGuid().ToString();
            var excelPath = BuildTestExcel("r.xlsx", "account", "accountid",
                new[] { ("accountid", "Account ID"), ("name", "Name") },
                new[] { new[] { id, "Fabrikam" } });

            var snapshot = SqliteFileAdapter.LoadFromExcel(_svc, excelPath, "snap-ex", "env1", null, null);
            var rows = _svc.ReadSnapshotRecords(snapshot.TableSuffix, snapshot.ColumnConfig).ToList();

            Assert.Single(rows);
            Assert.Equal("Fabrikam", rows[0]["name"]?.ToString());
        }

        [Fact]
        public void LoadFromExcel_Sets_SourceId_FromPrimaryIdAttribute()
        {
            var id = Guid.NewGuid().ToString();
            var excelPath = BuildTestExcel("sid.xlsx", "account", "accountid",
                new[] { ("accountid", "Account ID"), ("name", "Name") },
                new[] { new[] { id, "TestCo" } });

            var snapshot = SqliteFileAdapter.LoadFromExcel(_svc, excelPath, "snap-sid", "env1", null, null);
            var rows = _svc.ReadSnapshotRecords(snapshot.TableSuffix, snapshot.ColumnConfig).ToList();

            Assert.Single(rows);
            Assert.Equal(id, rows[0]["_source_id"]?.ToString());
        }

        [Fact]
        public void LoadFromExcel_SkipsHintRow_AndReadsDataFromRow3()
        {
            var id1 = Guid.NewGuid().ToString();
            var id2 = Guid.NewGuid().ToString();
            var excelPath = BuildTestExcel("multi.xlsx", "account", "accountid",
                new[] { ("accountid", "Account ID"), ("name", "Name") },
                new[] { new[] { id1, "Alpha" }, new[] { id2, "Beta" } });

            var snapshot = SqliteFileAdapter.LoadFromExcel(_svc, excelPath, "multi-snap", "env1", null, null);

            Assert.Equal(2, snapshot.RowCount);
        }

        [Fact]
        public void LoadFromExcel_Overwrites_ExistingSnapshot_OnSecondLoad()
        {
            var id = Guid.NewGuid().ToString();
            var path1 = BuildTestExcel("v1.xlsx", "account", "accountid",
                new[] { ("accountid", "Account ID"), ("name", "Name") },
                new[] { new[] { id, "V1" } });
            var path2 = BuildTestExcel("v2.xlsx", "account", "accountid",
                new[] { ("accountid", "Account ID"), ("name", "Name") },
                new[] { new[] { id, "V2-A" }, new[] { Guid.NewGuid().ToString(), "V2-B" } });

            SqliteFileAdapter.LoadFromExcel(_svc, path1, "shared-snap", "env1", null, null);
            var snap2 = SqliteFileAdapter.LoadFromExcel(_svc, path2, "shared-snap", "env1", null, null);

            Assert.Equal(2, snap2.RowCount);
        }

        // ─── Snapshot persistence ──────────────────────────────────────────────

        [Fact]
        public void Snapshot_IsPersisted_InProject_AfterLoad()
        {
            var collection = TestDataBuilder.RecordCollection();
            var jsonPath = _tmp.WriteJson("p.json", collection);

            var snap = SqliteFileAdapter.LoadFromJson(_svc, jsonPath, "persisted-snap", "env1", null, null);

            var retrieved = _svc.GetSnapshot("persisted-snap");
            Assert.NotNull(retrieved);
            Assert.Equal(snap.TableSuffix, retrieved.TableSuffix);
        }

        [Fact]
        public void TableSuffix_IsSanitized()
        {
            var collection = TestDataBuilder.RecordCollection();
            var jsonPath = _tmp.WriteJson("s.json", collection);

            var snap = SqliteFileAdapter.LoadFromJson(_svc, jsonPath, "My Snapshot 2024!", "env1", null, null);

            Assert.Equal("my_snapshot_2024", snap.TableSuffix);
        }

        // ─── Helpers ───────────────────────────────────────────────────────────

        [Fact]
        public void TableSuffix_Collision_AppendsCounter_ForDifferentSnapshotNames()
        {
            var collection = TestDataBuilder.RecordCollection();
            var path1 = _tmp.WriteJson("s1.json", collection);
            var path2 = _tmp.WriteJson("s2.json", collection);

            var first = SqliteFileAdapter.LoadFromJson(_svc, path1, "My Snapshot", "env1", null, null);
            var second = SqliteFileAdapter.LoadFromJson(_svc, path2, "My Snapshot!", "env1", null, null);

            Assert.Equal("my_snapshot", first.TableSuffix);
            Assert.Equal("my_snapshot_2", second.TableSuffix);
        }

        [Fact]
        public void ExportToJson_WritesSnapshotRows_WithoutInternalColumns()
        {
            var collection = TestDataBuilder.RecordCollection("account", "accountid");
            var jsonPath = _tmp.WriteJson("source.json", collection);
            var snapshot = SqliteFileAdapter.LoadFromJson(_svc, jsonPath, "export-json", "env1", null, null);
            var exportPath = _tmp.GetPath("snapshot.json");

            SqliteFileAdapter.ExportToJson(_svc, snapshot.Name, exportPath);

            var exported = JsonConvert.DeserializeObject<RecordCollection>(System.IO.File.ReadAllText(exportPath));
            var attributes = exported.Records.Single().Attributes.Select(a => a.Key).ToList();
            Assert.Equal("account", exported.LogicalName);
            Assert.DoesNotContain("_source_id", attributes);
            Assert.DoesNotContain("_is_new", attributes);
            Assert.Contains("name", attributes);
        }

        [Fact]
        public void ExportToExcel_WritesDataAndMetadataSheets_WithoutInternalColumns()
        {
            var collection = TestDataBuilder.RecordCollection("account", "accountid");
            var jsonPath = _tmp.WriteJson("source-excel.json", collection);
            var snapshot = SqliteFileAdapter.LoadFromJson(_svc, jsonPath, "export-excel", "env1", null, null);
            var exportPath = _tmp.GetExcelPath("snapshot.xlsx");

            SqliteFileAdapter.ExportToExcel(_svc, snapshot.Name, exportPath);

            using (var wb = new XLWorkbook(exportPath))
            {
                Assert.True(wb.Worksheets.Contains("_dmt"));
                Assert.True(wb.Worksheets.Contains("Data"));
                var headers = wb.Worksheet("Data").Row(1).CellsUsed().Select(c => c.GetString()).ToList();
                Assert.DoesNotContain("_source_id", headers);
                Assert.DoesNotContain("_is_new", headers);
                Assert.Contains("name", headers);
            }
        }

        private string BuildTestExcel(
            string fileName,
            string tableLogicalName,
            string primaryIdAttr,
            (string logicalName, string displayName)[] columns,
            string[][] dataRows)
        {
            var path = _tmp.GetExcelPath(fileName);
            var exportConfig = new ExcelExportConfig
            {
                Table = new ExcelTableConfig
                {
                    LogicalName = tableLogicalName,
                    PrimaryIdAttribute = primaryIdAttr,
                    PrimaryNameAttribute = "name"
                },
                Columns = columns.Select(c => new ExcelColumnConfig
                {
                    LogicalName = c.logicalName,
                    DisplayName = c.displayName,
                    Type = "String"
                }).ToList()
            };

            using (var wb = new XLWorkbook())
            {
                var meta = wb.AddWorksheet("_dmt");
                meta.Cell("A1").SetValue(JsonConvert.SerializeObject(exportConfig));
                meta.Visibility = XLWorksheetVisibility.Hidden;

                var data = wb.AddWorksheet("Data");
                for (var i = 0; i < columns.Length; i++)
                    data.Cell(1, i + 1).SetValue(columns[i].logicalName);
                for (var i = 0; i < columns.Length; i++)
                    data.Cell(2, i + 1).SetValue($"({columns[i].displayName})");
                for (var r = 0; r < dataRows.Length; r++)
                    for (var c = 0; c < dataRows[r].Length; c++)
                        data.Cell(r + 3, c + 1).SetValue(dataRows[r][c]);

                wb.SaveAs(path);
            }

            return path;
        }

        private static DataTableConfig MatchKeyConfig()
        {
            return new DataTableConfig
            {
                LoadMatchKeyMode = "Custom",
                LoadMatchKeyFields = new List<string> { "accountnumber" },
                AllColumns = new List<DataTableColumnConfig>
                {
                    new DataTableColumnConfig { LogicalName = "accountid", DisplayName = "Account ID", Type = "Uniqueidentifier", SqliteType = "TEXT" },
                    new DataTableColumnConfig { LogicalName = "accountnumber", DisplayName = "Account Number", Type = "String", SqliteType = "TEXT" },
                    new DataTableColumnConfig { LogicalName = "name", DisplayName = "Name", Type = "String", SqliteType = "TEXT" }
                }
            };
        }

        private static RecordCollection RecordCollectionWithAccountNumber(string accountNumber, Guid? id)
        {
            var attributes = new List<RecordAttribute>
            {
                new RecordAttribute { Key = "accountnumber", Type = AttributeType.Standard, Value = accountNumber },
                new RecordAttribute { Key = "name", Type = AttributeType.Standard, Value = "Match Co" }
            };
            if (id.HasValue)
                attributes.Insert(0, new RecordAttribute { Key = "accountid", Type = AttributeType.Identifier, Value = id.Value });

            return new RecordCollection
            {
                LogicalName = "account",
                PrimaryIdAttribute = "accountid",
                Count = 1,
                Records = new List<DmtRecord>
                {
                    new DmtRecord
                    {
                        SourceRowNumber = 1,
                        Attributes = attributes
                    }
                }
            };
        }
    }
}
