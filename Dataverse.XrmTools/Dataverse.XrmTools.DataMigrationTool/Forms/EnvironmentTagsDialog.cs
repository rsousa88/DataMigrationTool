using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;

using Dataverse.XrmTools.DataMigrationTool.Logic;
using Dataverse.XrmTools.DataMigrationTool.Models;

namespace Dataverse.XrmTools.DataMigrationTool.Forms
{
    public class EnvironmentTagsDialog : Form
    {
        private readonly DataGridView _grid;
        private readonly List<DmtProjectEnvironment> _environments;

        public IReadOnlyList<DmtProjectEnvironment> Environments => _environments;

        public EnvironmentTagsDialog(IEnumerable<DmtProjectEnvironment> environments)
        {
            _environments = (environments ?? Enumerable.Empty<DmtProjectEnvironment>())
                .Select(CloneEnvironment)
                .OrderBy(e => string.Equals(e.Role, "source", StringComparison.OrdinalIgnoreCase) ? 0 : 1)
                .ThenBy(e => e.FriendlyName ?? e.UniqueName)
                .ToList();

            Text = "Environment Tags";
            ClientSize = new Size(760, 360);
            MinimumSize = new Size(620, 300);
            StartPosition = FormStartPosition.CenterParent;
            ShowIcon = false;
            ShowInTaskbar = false;

            var layout = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                RowCount = 2,
                ColumnCount = 1,
                Padding = new Padding(10)
            };
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 42));

            _grid = new DataGridView
            {
                Dock = DockStyle.Fill,
                AllowUserToAddRows = false,
                AllowUserToDeleteRows = false,
                AutoGenerateColumns = false,
                MultiSelect = false,
                SelectionMode = DataGridViewSelectionMode.FullRowSelect,
                RowHeadersVisible = false
            };
            _grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Role", DataPropertyName = "Role", ReadOnly = true, Width = 80 });
            _grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Tag", DataPropertyName = "Tag", Width = 70 });
            _grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Friendly Name", DataPropertyName = "FriendlyName", ReadOnly = true, Width = 210 });
            _grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Unique Name", DataPropertyName = "UniqueName", ReadOnly = true, Width = 180 });
            _grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Id", DataPropertyName = "Id", ReadOnly = true, AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill });
            _grid.DataSource = _environments;
            layout.Controls.Add(_grid, 0, 0);

            var buttons = new FlowLayoutPanel
            {
                Dock = DockStyle.Fill,
                FlowDirection = FlowDirection.RightToLeft,
                Padding = new Padding(0, 7, 0, 0)
            };
            var cancel = new Button { Text = "Cancel", DialogResult = DialogResult.Cancel, Width = 86, Height = 28 };
            var save = new Button { Text = "Save", Width = 86, Height = 28 };
            save.Click += (s, e) => Save();
            buttons.Controls.Add(cancel);
            buttons.Controls.Add(save);
            layout.Controls.Add(buttons, 0, 1);

            Controls.Add(layout);
            AcceptButton = save;
            CancelButton = cancel;
        }

        private void Save()
        {
            _grid.EndEdit();
            foreach (var env in _environments)
            {
                env.Tag = EnvironmentTagHelper.NormalizeTag(env.Tag);
                if (!EnvironmentTagHelper.IsValidTag(env.Tag))
                {
                    MessageBox.Show(this, "Environment tags must be 2 to 5 letters or numbers.", "Invalid Tag", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }
            }

            DialogResult = DialogResult.OK;
        }

        private static DmtProjectEnvironment CloneEnvironment(DmtProjectEnvironment environment)
        {
            return new DmtProjectEnvironment
            {
                Id = environment.Id,
                UniqueName = environment.UniqueName,
                FriendlyName = environment.FriendlyName,
                Tag = EnvironmentTagHelper.NormalizeTag(environment.Tag)
                    ?? EnvironmentTagHelper.DeriveTag(environment.FriendlyName, environment.UniqueName),
                Url = environment.Url,
                Role = environment.Role
            };
        }
    }
}
