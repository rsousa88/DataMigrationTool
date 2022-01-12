// System
using System;
using System.Linq;
using System.Windows.Forms;
using System.Collections.Generic;

// Microsoft
using Microsoft.Xrm.Sdk;

// DataMigrationTool
using Dataverse.XrmTools.DataMigrationTool.Enums;
using Dataverse.XrmTools.DataMigrationTool.Models;
using Dataverse.XrmTools.DataMigrationTool.AppSettings;
using Dataverse.XrmTools.DataMigrationTool.Repositories;

namespace Dataverse.XrmTools.DataMigrationTool.Forms
{
    public partial class AttributeMapping : Form
    {
        private IOrganizationService _service;
        private Instance _sourceInstance;
        private Instance _targetInstance;
        private IEnumerable<Table> _tables;
        private IEnumerable<Models.Attribute> _tempAttrs;

        public AttributeMapping(IOrganizationService service, Instance sourceInstance, Instance targetInstance, IEnumerable<Table> tables)
        {
            _service = service;
            _sourceInstance = sourceInstance;
            _targetInstance = targetInstance;
            _tables = tables;

            InitializeComponent();
        }

        private void LoadTables(object sender, EventArgs e)
        {
            var boxOptions = _tables.Select(tbl => tbl.LogicalName).ToArray();
            cbTables.Items.AddRange(boxOptions);

            cbTables.Enabled = true;
        }

        private void cbTables_SelectedIndexChanged(object sender, EventArgs e)
        {
            // reset attributes
            cbAtributes.SelectedItem = null;
            cbAtributes.Items.Clear();
            cbAtributes.Enabled = false;

            var logicalName = (sender as ComboBox).SelectedItem as string;
            var table = _tables.Where(tbl => tbl.LogicalName.Equals(logicalName)).FirstOrDefault();

            if(table == null) { throw new Exception($"Invalid Table {logicalName}. Please reload tables and try again."); }

            // retrieve table attributes valid for mapping
            var repo = new CrmRepo(_service);
            var attributes = repo.GetTableMetadata(table.LogicalName);

            // filter valid attributes
            var validAttrs = attributes.Attributes
                .Where(att => att.IsValidForRead != null && att.IsValidForRead.Value)
                .Where(att => att.DisplayName != null && att.DisplayName.UserLocalizedLabel != null && !string.IsNullOrEmpty(att.DisplayName.UserLocalizedLabel.Label))
                //.Where(att => att.AttributeType.Equals(AttributeTypeCode.Lookup))
                .OrderBy(att => att.LogicalName)
                .Select(att => new Models.Attribute
                {
                    LogicalName = att.LogicalName,
                    DisplayName = att.DisplayName.UserLocalizedLabel.Label,
                    Type = att.AttributeTypeName.Value.EndsWith("Type") ? att.AttributeTypeName.Value.Substring(0, att.AttributeTypeName.Value.LastIndexOf("Type")) : att.AttributeTypeName.Value
                });

            _tempAttrs = validAttrs;

            var boxOptions = validAttrs.Select(attr => attr.LogicalName).ToArray();
            cbAtributes.Items.AddRange(boxOptions);
            cbAtributes.Enabled = true;
        }

        private void btnClose_Click(object sender, EventArgs e)
        {
            try
            {
                var tableName = cbTables.SelectedItem as string;
                var table = _tables.FirstOrDefault(tbl => tbl.LogicalName.Equals(tableName));

                var attrName = cbAtributes.SelectedItem as string;
                var attribute = _tempAttrs.FirstOrDefault(attr => attr.LogicalName.Equals(attrName));

                if (table == null || attribute == null) { throw new Exception($"Both a table and an attribute are required."); }

                var mapping = new Mapping
                {
                    Type = MappingType.Attribute,
                    TableLogicalName = table.LogicalName,
                    TableDisplayName = table.DisplayName,
                    SourceInstanceName = _sourceInstance.FriendlyName,
                    TargetInstanceName = _targetInstance.FriendlyName,
                    AttributeLogicalName = attribute.LogicalName,
                    AttributeDisplayName = attribute.DisplayName,
                    State = MappingState.New
                };

                _sourceInstance.Mappings.Add(mapping);

                Close();
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message, "Warning", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }

        private void btnCancel_Click(object sender, EventArgs e)
        {
            Close();
        }
    }
}
