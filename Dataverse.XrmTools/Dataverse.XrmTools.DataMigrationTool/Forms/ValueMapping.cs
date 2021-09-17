// System
using System;
using System.Linq;
using System.Windows.Forms;
using System.Collections.Generic;

// DataMigrationTool
using Dataverse.XrmTools.DataMigrationTool.Enums;
using Dataverse.XrmTools.DataMigrationTool.Models;
using Dataverse.XrmTools.DataMigrationTool.Helpers;
using Dataverse.XrmTools.DataMigrationTool.AppSettings;

namespace Dataverse.XrmTools.DataMigrationTool.Forms
{
    public partial class ValueMapping : Form
    {
        private Instance _sourceInstance;
        private Instance _targetInstance;
        private IEnumerable<Table> _tables;

        public ValueMapping(Instance sourceInstance, Instance targetInstance, IEnumerable<Table> tables)
        {
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

        private void btnClose_Click(object sender, EventArgs e)
        {
            try
            {
                var tableName = cbTables.SelectedItem as string;
                var table = _tables.FirstOrDefault(tbl => tbl.LogicalName.Equals(tableName));

                var sourceId = txt_SourceId.Text.ToGuid();
                var targetId = txt_TargetId.Text.ToGuid();

                if (table == null) { throw new Exception($"A table is required"); }
                if (sourceId.Equals(Guid.Empty) || targetId.Equals(Guid.Empty)) { throw new Exception($"Invalid Guid"); }

                var mapping = new Mapping
                {
                    Type = MappingType.Value,
                    TableLogicalName = table.LogicalName,
                    TableDisplayName = table.DisplayName,
                    SourceInstanceName = _sourceInstance.FriendlyName,
                    SourceId = sourceId,
                    TargetInstanceName = _targetInstance.FriendlyName,
                    TargetId = targetId,
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
