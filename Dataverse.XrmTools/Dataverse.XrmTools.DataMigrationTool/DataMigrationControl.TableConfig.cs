// System
using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows.Forms;

// Microsoft
using Microsoft.Xrm.Sdk.Metadata;

// DataMigrationTool
using Dataverse.XrmTools.DataMigrationTool.Logic;
using Dataverse.XrmTools.DataMigrationTool.Models;

namespace Dataverse.XrmTools.DataMigrationTool
{
    public partial class DataMigrationControl
    {
        #region Table Config Fields

        private DataTableConfig _currentTableConfig;
        private Timer _tableConfigSaveTimer;

        #endregion

        #region Table Config Initialization

        private void InitializeTableConfigAutoSave()
        {
            _tableConfigSaveTimer = new Timer { Interval = 500 };
            _tableConfigSaveTimer.Tick += (s, e) =>
            {
                _tableConfigSaveTimer.Stop();
                FlushTableConfigToProject();
            };
        }

        #endregion

        #region Table Config Operations

        private void AfterAttributesLoaded(TableData tableData)
        {
            if (_project?.Service == null || tableData?.Table == null || tableData.Metadata?.Attributes == null)
                return;

            var table = tableData.Table;
            var allColumns = BuildColumnConfigs(tableData.Metadata.Attributes);

            // Load existing or create fresh config
            var (existing, _, _, _) = _project.Service.GetTableConfig(table.LogicalName);
            _currentTableConfig = existing ?? new DataTableConfig();
            _currentTableConfig.AllColumns = allColumns;

            // Capture current UI selection state
            _currentTableConfig.SelectedAttributes = lvAttributes.CheckedItems
                .Cast<ListViewItem>()
                .Select(lvi => lvi.SubItems[0].Text)
                .ToList();

            _currentTableConfig.Filter = rtbFilter.Text;

            _project.Service.SaveTableConfig(
                table.LogicalName,
                table.DisplayName,
                table.IdAttribute,
                table.NameAttribute,
                _currentTableConfig);
        }

        private void ScheduleTableConfigSave()
        {
            if (_project?.Service == null || _currentTableConfig == null) return;
            _tableConfigSaveTimer.Stop();
            _tableConfigSaveTimer.Start();
        }

        private void FlushTableConfigToProject()
        {
            if (_project?.Service == null || _currentTableConfig == null) return;
            if (string.IsNullOrEmpty(_currentTableLogicalName)) return;

            _currentTableConfig.SelectedAttributes = lvAttributes.CheckedItems
                .Cast<ListViewItem>()
                .Select(lvi => lvi.SubItems[0].Text)
                .ToList();

            _currentTableConfig.Filter = rtbFilter.Text;

            var table = _tables?.FirstOrDefault(t => string.Equals(t.LogicalName, _currentTableLogicalName, StringComparison.OrdinalIgnoreCase));
            if (table == null) return;

            try
            {
                _project.Service.SaveTableConfig(
                    table.LogicalName,
                    table.DisplayName,
                    table.IdAttribute,
                    table.NameAttribute,
                    _currentTableConfig);
            }
            catch (Exception ex)
            {
                _logger.Log(Enums.LogLevel.WARN, $"Table config auto-save failed: {ex.Message}");
            }
        }

        private static List<DataTableColumnConfig> BuildColumnConfigs(AttributeMetadata[] attrs)
        {
            if (attrs == null) return new List<DataTableColumnConfig>();

            var result = new List<DataTableColumnConfig>();
            foreach (var att in attrs)
            {
                if (att == null || att.IsValidForRead == null || !att.IsValidForRead.Value) continue;

                var typeCode = GetAttributeTypeCode(att);
                if (SqliteProjectService.IsExcludedAttributeType(typeCode)) continue;

                var cfg = new DataTableColumnConfig
                {
                    LogicalName = att.LogicalName,
                    DisplayName = att.DisplayName?.UserLocalizedLabel?.Label ?? att.LogicalName,
                    Type = typeCode,
                    SqliteType = SqliteProjectService.GetSqliteType(typeCode),
                    IsMultiSelect = att is MultiSelectPicklistAttributeMetadata
                };

                if (att is LookupAttributeMetadata lookup)
                {
                    cfg.RelatedTable = lookup.Targets?.FirstOrDefault();
                    cfg.Resolution = "Guid";
                }

                result.Add(cfg);
            }
            return result;
        }

        private static string GetAttributeTypeCode(AttributeMetadata att)
        {
            var typeName = att.AttributeTypeName?.Value ?? string.Empty;
            return typeName.EndsWith("Type", StringComparison.Ordinal)
                ? typeName.Substring(0, typeName.LastIndexOf("Type", StringComparison.Ordinal))
                : typeName;
        }

        #endregion
    }
}
