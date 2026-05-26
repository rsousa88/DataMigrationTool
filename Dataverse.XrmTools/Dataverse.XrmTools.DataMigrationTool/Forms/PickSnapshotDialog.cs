// System
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;

// DataMigrationTool
using Dataverse.XrmTools.DataMigrationTool.Models;

namespace Dataverse.XrmTools.DataMigrationTool.Forms
{
    public class PickSnapshotDialog : Form
    {
        private ListView _list;

        public DmtSnapshot SelectedSnapshot => _list.SelectedItems.Count > 0
            ? _list.SelectedItems[0].Tag as DmtSnapshot
            : null;

        public PickSnapshotDialog(string prompt, IList<DmtSnapshot> snapshots)
        {
            Text = prompt;
            ClientSize = new Size(540, 300);
            FormBorderStyle = FormBorderStyle.FixedDialog;
            StartPosition = FormStartPosition.CenterParent;
            ShowIcon = false;
            ShowInTaskbar = false;
            MaximizeBox = false;
            MinimizeBox = false;

            var layout = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                RowCount = 2,
                ColumnCount = 1,
                Padding = new Padding(10)
            };
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 40));

            _list = new ListView
            {
                Dock = DockStyle.Fill,
                View = View.Details,
                FullRowSelect = true,
                MultiSelect = false,
                GridLines = true,
                HeaderStyle = ColumnHeaderStyle.Nonclickable
            };
            _list.Columns.Add("#", 34, HorizontalAlignment.Right);
            _list.Columns.Add("Name", 160);
            _list.Columns.Add("Table", 120);
            _list.Columns.Add("Rows", 55, HorizontalAlignment.Right);
            _list.Columns.Add("Source", 50);

            foreach (var s in snapshots)
            {
                var item = new ListViewItem(s.SortOrder.ToString());
                item.SubItems.Add(s.Name);
                item.SubItems.Add(s.TableLogicalName);
                item.SubItems.Add(s.RowCount.ToString("N0"));
                item.SubItems.Add(s.Source ?? "");
                item.Tag = s;
                _list.Items.Add(item);
            }
            if (_list.Items.Count > 0) _list.Items[0].Selected = true;
            _list.DoubleClick += (s, e) => { if (SelectedSnapshot != null) Ok(); };
            layout.Controls.Add(_list, 0, 0);

            var btnPanel = new FlowLayoutPanel
            {
                Dock = DockStyle.Fill,
                FlowDirection = FlowDirection.RightToLeft,
                Padding = new Padding(0, 4, 0, 0)
            };
            var cancelBtn = new Button { Text = "Cancel", DialogResult = DialogResult.Cancel, Width = 75 };
            var okBtn = new Button { Text = "Select", Width = 75 };
            okBtn.Click += (s, e) => Ok();
            btnPanel.Controls.AddRange(new Control[] { cancelBtn, okBtn });
            layout.Controls.Add(btnPanel, 0, 1);

            Controls.Add(layout);
            AcceptButton = okBtn;
            CancelButton = cancelBtn;
        }

        private void Ok()
        {
            if (SelectedSnapshot == null)
            {
                MessageBox.Show(this, "Select a snapshot first.", "Selection Required", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            DialogResult = DialogResult.OK;
        }
    }
}
