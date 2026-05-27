// System
using System;
using System.Collections.Generic;
using System.Linq;

// DataMigrationTool
using Dataverse.XrmTools.DataMigrationTool.Enums;
using Dataverse.XrmTools.DataMigrationTool.Logic;
using Dataverse.XrmTools.DataMigrationTool.Models;

// Microsoft
using Microsoft.Xrm.Sdk.Metadata;

// 3rd Party
using Xunit;
using DmtRecord = Dataverse.XrmTools.DataMigrationTool.Models.Record;
using DmtAction = Dataverse.XrmTools.DataMigrationTool.Enums.Action;

namespace Dataverse.XrmTools.DataMigrationTool.Tests
{
    public class ImportPreviewServiceTests
    {
        [Fact]
        public void GroupImportWarningsByRow_GroupsWarningsByParsedRowNumber()
        {
            var warnings = new[]
            {
                "Row 3, field 'name': match key is blank.",
                "Row 3, field 'accountnumber': match key is blank.",
                "General warning without row",
                "Row 7: supplied GUID already exists in this file."
            };

            var result = ImportPreviewService.GroupImportWarningsByRow(warnings);

            Assert.Equal(2, result.Count);
            Assert.Equal(2, result[3].Count);
            Assert.Single(result[7]);
        }

        [Fact]
        public void BuildPreview_CalculatesCreateUpdateSkipCountsAndRows()
        {
            var existingId = Guid.NewGuid();
            var createId = Guid.NewGuid();
            var skippedReadError = "Row 9, column 'accountid': duplicate record GUID.";
            var collection = CreateCollection(
                CreateRecord(2, existingId, "Existing", primaryIdWasBlank: false),
                CreateRecord(3, createId, "New supplied id", primaryIdWasBlank: false));
            collection.ImportErrors = new List<string> { skippedReadError };

            var preview = ImportPreviewService.BuildPreview(new ExcelImportPreviewRequest
            {
                TableData = CreateTableData(),
                Collection = collection,
                Config = CreateExcelConfig(),
                Settings = new UiSettings { Action = DmtAction.Create | DmtAction.Update },
                FilePath = "accounts.xlsx",
                TargetName = "DEV",
                ExistingTargetIdsProvider = ids => new HashSet<Guid> { existingId }
            });

            Assert.Equal("Excel", preview.SourceType);
            Assert.Equal("Excel export metadata", preview.SettingsSource);
            Assert.Equal("DEV", preview.TargetName);
            Assert.Equal(1, preview.CreateCount);
            Assert.Equal(1, preview.UpdateCount);
            Assert.Equal(1, preview.SkippedCount);
            Assert.Equal(3, preview.TotalRows);
            Assert.Equal(new[] { 2, 3, 9 }, preview.Items.Select(i => i.RowNumber));
            Assert.Equal("Update", preview.Items[0].Action);
            Assert.Equal("Create", preview.Items[1].Action);
            Assert.Equal("Skip", preview.Items[2].Action);
            Assert.Contains("supplied record GUID was not found in target", preview.Items[1].Warnings);
            Assert.Equal(skippedReadError, preview.Items[2].Warnings);
        }

        [Fact]
        public void BuildPreview_RespectsDisabledCreateAndUpdateSettings()
        {
            var existingId = Guid.NewGuid();
            var createId = Guid.NewGuid();
            var collection = CreateCollection(
                CreateRecord(1, existingId, "Existing"),
                CreateRecord(2, createId, "New"));

            var preview = ImportPreviewService.BuildPreview(new ExcelImportPreviewRequest
            {
                TableData = CreateTableData(),
                Collection = collection,
                Settings = new UiSettings { Action = DmtAction.Create },
                ExistingTargetIdsProvider = ids => new HashSet<Guid> { existingId }
            });

            Assert.Equal(1, preview.CreateCount);
            Assert.Equal(0, preview.UpdateCount);
            Assert.Equal(1, preview.SkippedCount);
            Assert.Equal("Skip", preview.Items[0].Action);
            Assert.Contains("Update is not enabled", preview.Items[0].Description);
            Assert.Equal("Create", preview.Items[1].Action);
        }

        [Fact]
        public void BuildPreview_WithoutTargetProviderTreatsRowsAsCreateCandidates()
        {
            var id = Guid.NewGuid();
            var collection = CreateCollection(CreateRecord(1, id, "No target"));

            var preview = ImportPreviewService.BuildPreview(new ExcelImportPreviewRequest
            {
                TableData = CreateTableData(),
                Collection = collection,
                Settings = new UiSettings { Action = DmtAction.Create | DmtAction.Update }
            });

            Assert.Equal(1, preview.CreateCount);
            Assert.Equal(0, preview.UpdateCount);
            Assert.Equal(0, preview.SkippedCount);
            Assert.Equal("Create", preview.Items.Single().Action);
            Assert.Contains("Target record not found", preview.Items.Single().Description);
        }

        [Fact]
        public void BuildPreview_LabelsJsonAndCurrentSettingsWhenExcelConfigIsMissing()
        {
            var collection = CreateCollection(CreateRecord(1, Guid.NewGuid(), "JSON row"));

            var preview = ImportPreviewService.BuildPreview(new ExcelImportPreviewRequest
            {
                TableData = CreateTableData(),
                Collection = collection,
                Config = null,
                Settings = new UiSettings { Action = DmtAction.Create },
                FilePath = "accounts.json"
            });

            Assert.Equal("JSON", preview.SourceType);
            Assert.Equal("Current settings", preview.SettingsSource);
            Assert.Equal("accounts.json", preview.FilePath);
        }

        [Fact]
        public void BuildPreview_IncludesSourceValueColumnsAndRowValues()
        {
            var id = Guid.NewGuid();
            var collection = CreateCollection(CreateRecord(4, id, "Visible row"));

            var preview = ImportPreviewService.BuildPreview(new ExcelImportPreviewRequest
            {
                TableData = CreateTableData(),
                Collection = collection,
                Config = CreateExcelConfig(),
                Settings = new UiSettings { Action = DmtAction.Create },
                ExistingTargetIdsProvider = ids => new HashSet<Guid>()
            });

            Assert.Contains("name", preview.ValueColumns);
            Assert.Contains("accountnumber", preview.ValueColumns);
            var item = Assert.Single(preview.Items);
            Assert.Equal("Visible row", item.Values["name"]);
            Assert.Equal("A-004", item.Values["accountnumber"]);
        }

        [Fact]
        public void BuildPreview_AddsSuppliedGuidWarningOnlyForExcelCreatesWithSuppliedGuid()
        {
            var existingId = Guid.NewGuid();
            var suppliedCreateId = Guid.NewGuid();
            var blankCreateId = Guid.NewGuid();
            var collection = CreateCollection(
                CreateRecord(1, existingId, "Existing", primaryIdWasBlank: false),
                CreateRecord(2, suppliedCreateId, "Supplied create", primaryIdWasBlank: false),
                CreateRecord(3, blankCreateId, "Blank create", primaryIdWasBlank: true));

            var preview = ImportPreviewService.BuildPreview(new ExcelImportPreviewRequest
            {
                TableData = CreateTableData(),
                Collection = collection,
                Config = CreateExcelConfig(),
                Settings = new UiSettings { Action = DmtAction.Create | DmtAction.Update },
                ExistingTargetIdsProvider = ids => new HashSet<Guid> { existingId }
            });

            Assert.DoesNotContain("supplied record GUID", preview.Items[0].Warnings ?? string.Empty);
            Assert.Contains("supplied record GUID", preview.Items[1].Warnings);
            Assert.DoesNotContain("supplied record GUID", preview.Items[2].Warnings ?? string.Empty);
            Assert.Single(preview.ImportErrors.Where(e => e.Contains("supplied record GUID")));
        }

        [Fact]
        public void AddSuppliedGuidCreateWarning_AddsPreviewWarningOnlyOnce()
        {
            var preview = new ExcelImportPreview();
            var warningsByRow = new Dictionary<int, List<string>>();

            var added = ImportPreviewService.AddSuppliedGuidCreateWarning(preview, warningsByRow, 5, "accountid");
            var addedAgain = ImportPreviewService.AddSuppliedGuidCreateWarning(preview, warningsByRow, 5, "accountid");

            Assert.True(added);
            Assert.False(addedAgain);
            var warning = Assert.Single(warningsByRow[5]);
            Assert.Equal(warning, Assert.Single(preview.ImportErrors));
            Assert.Contains("supplied record GUID was not found in target", warning);
            Assert.Contains("workbook writeback", warning);
        }

        [Theory]
        [InlineData("Guid", null)]
        [InlineData("Columns", "name, accountnumber")]
        [InlineData("AlternateKey", "account_alt_key (name, accountnumber)")]
        public void GetImportMatchKeyDisplay_FormatsSupportedModes(string mode, string expected)
        {
            var fields = new List<string> { "name", "accountnumber" };

            var result = ImportPreviewService.GetImportMatchKeyDisplay(mode, fields, "account_alt_key");

            Assert.Equal(expected, result);
        }

        [Fact]
        public void EnsureRecordPrimaryId_AddsGeneratedIdentifierWhenMissing()
        {
            var attributes = new List<RecordAttribute>
            {
                new RecordAttribute { Key = "name", Type = AttributeType.Standard, Value = "Contoso" }
            };

            ImportPreviewService.EnsureRecordPrimaryId("accountid", attributes);

            var idAttribute = attributes.Single(a => a.Key == "accountid");
            Assert.Equal(AttributeType.Identifier, idAttribute.Type);
            Assert.True(idAttribute.Value is Guid);
            Assert.NotEqual(Guid.Empty, (Guid)idAttribute.Value);
        }

        [Fact]
        public void SetRecordPrimaryId_UpdatesExistingIdentifierCaseInsensitively()
        {
            var id = Guid.NewGuid();
            var attributes = new List<RecordAttribute>
            {
                new RecordAttribute { Key = "AccountId", Type = AttributeType.Standard, Value = string.Empty }
            };

            ImportPreviewService.SetRecordPrimaryId("accountid", id, attributes);

            Assert.Single(attributes);
            Assert.Equal(AttributeType.Identifier, attributes[0].Type);
            Assert.Equal(id, attributes[0].Value);
        }

        [Fact]
        public void ApplyImportMatchKeySelection_UpdatesExcelConfigAndImportSettings()
        {
            var config = new ExcelExportConfig
            {
                ImportSettings = new ExcelImportSettings()
            };
            var selection = new ExcelImportMatchKeySelection
            {
                Mode = "AlternateKey",
                AlternateKeyName = "account_alt_key",
                Fields = new List<string> { "accountnumber", "accountnumber", " " }
            };

            ImportPreviewService.ApplyImportMatchKeySelection(config, selection);

            Assert.Equal("AlternateKey", config.MatchKeyMode);
            Assert.Single(config.MatchKeys);
            Assert.Equal("account_alt_key", config.MatchAlternateKeyName);
            Assert.Equal(config.MatchKeys, config.ImportSettings.MatchKeyFields);
            Assert.Equal("account_alt_key", config.ImportSettings.MatchAlternateKeyName);
        }

        [Fact]
        public void GetAvailableImportMatchKeys_UsesConfigColumnsWhenAvailable()
        {
            var config = new ExcelExportConfig
            {
                Table = new ExcelTableConfig { PrimaryIdAttribute = "accountid" },
                Columns = new List<ExcelColumnConfig>
                {
                    new ExcelColumnConfig { LogicalName = "accountid", Type = "Guid" },
                    new ExcelColumnConfig { LogicalName = "ownerid", Type = "Lookup" },
                    new ExcelColumnConfig { LogicalName = "accountnumber", Type = "String" },
                    new ExcelColumnConfig { LogicalName = "name", Type = "String" }
                }
            };

            var result = ImportPreviewService.GetAvailableImportMatchKeys(null, config);

            Assert.Equal(new[] { "accountnumber", "name" }, result);
        }

        [Fact]
        public void GetAvailableImportAlternateKeys_ReturnsKeysWhoseFieldsAreAvailable()
        {
            var keys = new[]
            {
                new EntityKeyMetadata { LogicalName = "account_number_key", KeyAttributes = new[] { "accountnumber" } },
                new EntityKeyMetadata { LogicalName = "missing_key", KeyAttributes = new[] { "missingfield" } }
            };

            var result = ImportPreviewService.GetAvailableImportAlternateKeys(keys, new[] { "accountnumber", "name" });

            Assert.Single(result);
            Assert.Equal("account_number_key", result[0].Name);
            Assert.Equal("accountnumber", result[0].Fields[0]);
        }

        private static TableData CreateTableData()
        {
            var table = TestSupport.TestDataBuilder.Table();
            return new TableData
            {
                Table = table,
                Metadata = new EntityMetadata
                {
                    LogicalName = "account"
                }
            };
        }

        private static ExcelExportConfig CreateExcelConfig()
        {
            return new ExcelExportConfig
            {
                ImportSettings = new ExcelImportSettings { Action = DmtAction.Create | DmtAction.Update },
                Table = new ExcelTableConfig
                {
                    LogicalName = "account",
                    PrimaryIdAttribute = "accountid",
                    PrimaryNameAttribute = "name"
                },
                Columns = new List<ExcelColumnConfig>
                {
                    new ExcelColumnConfig { LogicalName = "accountid", Type = "Guid" },
                    new ExcelColumnConfig { LogicalName = "name", Type = "String" },
                    new ExcelColumnConfig { LogicalName = "accountnumber", Type = "String" }
                }
            };
        }

        private static RecordCollection CreateCollection(params DmtRecord[] records)
        {
            return new RecordCollection
            {
                LogicalName = "account",
                PrimaryIdAttribute = "accountid",
                Records = records,
                Count = records.Length
            };
        }

        private static DmtRecord CreateRecord(int rowNumber, Guid id, string name, bool primaryIdWasBlank = false)
        {
            return new DmtRecord
            {
                SourceRowNumber = rowNumber,
                PrimaryIdWasBlank = primaryIdWasBlank,
                Attributes = new List<RecordAttribute>
                {
                    new RecordAttribute { Key = "accountid", Type = AttributeType.Identifier, Value = id },
                    new RecordAttribute { Key = "name", Type = AttributeType.Standard, Value = name },
                    new RecordAttribute { Key = "accountnumber", Type = AttributeType.Standard, Value = $"A-{rowNumber:000}" }
                }
            };
        }
    }
}
