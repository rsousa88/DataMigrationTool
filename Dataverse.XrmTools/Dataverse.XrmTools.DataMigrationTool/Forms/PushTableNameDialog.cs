// System
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;

namespace Dataverse.XrmTools.DataMigrationTool.Forms
{
    /// <summary>
    /// Prompts for the Dataverse table logical name before Configure Push Step opens, for snapshots
    /// that don't already know their table (e.g. generic Excel loads with no DMT metadata sheet).
    /// Without this, the push step dialog opens with every setting greyed out and no explanation why.
    /// </summary>
    public class PushTableNameDialog : Form
    {
        private readonly TextBox _tableBox;

        public string TableLogicalName => _tableBox.Text.Trim();

        public PushTableNameDialog(string snapshotName, IEnumerable<string> availableTableLogicalNames = null)
        {
            Text = "Set Dataverse Table";
            FormBorderStyle = FormBorderStyle.FixedDialog;
            StartPosition = FormStartPosition.CenterParent;
            ShowIcon = false;
            ShowInTaskbar = false;
            MaximizeBox = false;
            MinimizeBox = false;
            ClientSize = new Size(440, 140);

            var layout = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                RowCount = 3,
                ColumnCount = 2,
                Padding = new Padding(10)
            };
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 60));
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 50));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 30));
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));

            var infoLabel = new Label
            {
                Text = $"Snapshot \"{snapshotName}\" has no Dataverse table set. Enter the target table's " +
                       "logical name so column mappings and lookups can be configured.",
                Dock = DockStyle.Fill,
                AutoSize = false,
                TextAlign = ContentAlignment.MiddleLeft
            };
            layout.Controls.Add(infoLabel, 0, 0);
            layout.SetColumnSpan(infoLabel, 2);

            layout.Controls.Add(new Label { Text = "Table:", TextAlign = ContentAlignment.MiddleLeft, Dock = DockStyle.Fill }, 0, 1);
            _tableBox = new TextBox { Dock = DockStyle.Fill };
            var names = availableTableLogicalNames?.Where(n => !string.IsNullOrWhiteSpace(n))
                .Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            if (names?.Any() == true)
            {
                _tableBox.AutoCompleteMode = AutoCompleteMode.SuggestAppend;
                _tableBox.AutoCompleteSource = AutoCompleteSource.CustomSource;
                var src = new AutoCompleteStringCollection();
                src.AddRange(names.ToArray());
                _tableBox.AutoCompleteCustomSource = src;
            }
            _tableBox.KeyDown += (s, e) => { if (e.KeyCode == Keys.Return) { e.SuppressKeyPress = true; Ok(); } };
            layout.Controls.Add(_tableBox, 1, 1);

            var btnPanel = new FlowLayoutPanel
            {
                Dock = DockStyle.Fill,
                FlowDirection = FlowDirection.RightToLeft,
                Padding = new Padding(0, 6, 0, 0)
            };
            var cancelBtn = new Button { Text = "Cancel", DialogResult = DialogResult.Cancel, Width = 75 };
            var okBtn = new Button { Text = "OK", Width = 75 };
            okBtn.Click += (s, e) => Ok();
            btnPanel.Controls.AddRange(new Control[] { cancelBtn, okBtn });
            layout.Controls.Add(btnPanel, 0, 2);
            layout.SetColumnSpan(btnPanel, 2);

            Controls.Add(layout);
            AcceptButton = okBtn;
            CancelButton = cancelBtn;

            Shown += (s, e) => _tableBox.Focus();
        }

        private void Ok()
        {
            if (string.IsNullOrWhiteSpace(_tableBox.Text))
            {
                MessageBox.Show(this, "Enter the Dataverse table logical name.", "Validation", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                _tableBox.Focus();
                return;
            }
            DialogResult = DialogResult.OK;
        }
    }
}
