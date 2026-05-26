// System
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;

// DataMigrationTool
using Dataverse.XrmTools.DataMigrationTool.Models;

namespace Dataverse.XrmTools.DataMigrationTool.Forms
{
    public class SnapshotNameDialog : Form
    {
        private TextBox _nameBox;
        private ListView _existingList;
        private bool _suppressListSelection;

        public string SnapshotName => _nameBox.Text.Trim();

        public SnapshotNameDialog(string defaultName = "", string actionLabel = "Pull",
            IReadOnlyList<DmtSnapshot> existingSnapshots = null)
        {
            var hasExisting = existingSnapshots != null && existingSnapshots.Count > 0;

            Text = "Name Snapshot";
            FormBorderStyle = FormBorderStyle.FixedDialog;
            StartPosition = FormStartPosition.CenterParent;
            ShowIcon = false;
            ShowInTaskbar = false;
            MaximizeBox = false;
            MinimizeBox = false;

            if (hasExisting)
                ClientSize = new Size(500, 310);
            else
                ClientSize = new Size(420, 90);

            var layout = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                RowCount = hasExisting ? 4 : 2,
                ColumnCount = 2,
                Padding = new Padding(10)
            };
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 60));
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 30));

            if (hasExisting)
            {
                layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 20));
                layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
                layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 36));
            }
            else
            {
                layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
            }

            layout.Controls.Add(new Label { Text = "Name:", TextAlign = ContentAlignment.MiddleLeft, Dock = DockStyle.Fill }, 0, 0);
            _nameBox = new TextBox { Dock = DockStyle.Fill, Text = defaultName };
            _nameBox.TextChanged += (s, e) =>
            {
                if (_suppressListSelection || _existingList == null) return;
                _suppressListSelection = true;
                _existingList.SelectedItems.Clear();
                _suppressListSelection = false;
            };
            layout.Controls.Add(_nameBox, 1, 0);

            if (hasExisting)
            {
                var lbl = new Label
                {
                    Text = "Select existing to overwrite:",
                    AutoSize = false,
                    Dock = DockStyle.Fill,
                    TextAlign = ContentAlignment.BottomLeft
                };
                layout.Controls.Add(lbl, 0, 1);
                layout.SetColumnSpan(lbl, 2);

                _existingList = new ListView
                {
                    Dock = DockStyle.Fill,
                    View = View.Details,
                    FullRowSelect = true,
                    MultiSelect = false,
                    GridLines = true,
                    HeaderStyle = ColumnHeaderStyle.Nonclickable
                };
                _existingList.Columns.Add("#", 34, HorizontalAlignment.Right);
                _existingList.Columns.Add("Name", 160);
                _existingList.Columns.Add("Table", 110);
                _existingList.Columns.Add("Rows", 50, HorizontalAlignment.Right);
                _existingList.Columns.Add("Source", 50);

                foreach (var s in existingSnapshots)
                {
                    var item = new ListViewItem(s.SortOrder.ToString());
                    item.SubItems.Add(s.Name);
                    item.SubItems.Add(s.TableLogicalName);
                    item.SubItems.Add(s.RowCount.ToString("N0"));
                    item.SubItems.Add(s.Source ?? "");
                    item.Tag = s;
                    _existingList.Items.Add(item);
                }

                _existingList.SelectedIndexChanged += (s, e) =>
                {
                    if (_suppressListSelection || _existingList.SelectedItems.Count == 0) return;
                    _suppressListSelection = true;
                    _nameBox.Text = (_existingList.SelectedItems[0].Tag as DmtSnapshot)?.Name ?? _nameBox.Text;
                    _suppressListSelection = false;
                };
                _existingList.DoubleClick += (s, e) => { if (_existingList.SelectedItems.Count > 0) Ok(); };

                layout.Controls.Add(_existingList, 0, 2);
                layout.SetColumnSpan(_existingList, 2);

                var btnPanel = new FlowLayoutPanel
                {
                    Dock = DockStyle.Fill,
                    FlowDirection = FlowDirection.RightToLeft,
                    Padding = new Padding(0, 4, 0, 0)
                };
                var cancelBtn = new Button { Text = "Cancel", DialogResult = DialogResult.Cancel, Width = 75 };
                var okBtn = new Button { Text = actionLabel, Width = 75 };
                okBtn.Click += (s, e) => Ok();
                btnPanel.Controls.AddRange(new Control[] { cancelBtn, okBtn });
                layout.Controls.Add(btnPanel, 0, 3);
                layout.SetColumnSpan(btnPanel, 2);

                Controls.Add(layout);
                AcceptButton = okBtn;
                CancelButton = cancelBtn;
            }
            else
            {
                var btnPanel = new FlowLayoutPanel
                {
                    Dock = DockStyle.Fill,
                    FlowDirection = FlowDirection.RightToLeft,
                    Padding = new Padding(0, 4, 0, 0)
                };
                var cancelBtn = new Button { Text = "Cancel", DialogResult = DialogResult.Cancel, Width = 75 };
                var okBtn = new Button { Text = actionLabel, Width = 75 };
                okBtn.Click += (s, e) => Ok();
                btnPanel.Controls.AddRange(new Control[] { cancelBtn, okBtn });
                layout.Controls.Add(btnPanel, 0, 1);
                layout.SetColumnSpan(btnPanel, 2);

                Controls.Add(layout);
                AcceptButton = okBtn;
                CancelButton = cancelBtn;
            }

            Shown += (s, e) =>
            {
                _nameBox.SelectAll();
                _nameBox.Focus();
            };
        }

        private void Ok()
        {
            if (string.IsNullOrWhiteSpace(_nameBox.Text))
            {
                MessageBox.Show(this, "Snapshot name is required.", "Validation", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                _nameBox.Focus();
                return;
            }
            DialogResult = DialogResult.OK;
        }
    }
}
