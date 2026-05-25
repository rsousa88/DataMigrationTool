// System
using System;
using System.Drawing;
using System.Windows.Forms;

namespace Dataverse.XrmTools.DataMigrationTool.Forms
{
    public class SnapshotNameDialog : Form
    {
        private TextBox _nameBox;

        public string SnapshotName => _nameBox.Text.Trim();

        public SnapshotNameDialog(string defaultName = "", string actionLabel = "Pull")
        {
            Text = "Name Snapshot";
            ClientSize = new Size(420, 90);
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
                ColumnCount = 2,
                Padding = new Padding(10)
            };
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 60));
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 30));
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));

            layout.Controls.Add(new Label { Text = "Name:", TextAlign = ContentAlignment.MiddleLeft, Dock = DockStyle.Fill }, 0, 0);
            _nameBox = new TextBox { Dock = DockStyle.Fill, Text = defaultName };
            layout.Controls.Add(_nameBox, 1, 0);

            var btnPanel = new FlowLayoutPanel
            {
                Dock = DockStyle.Fill,
                FlowDirection = FlowDirection.RightToLeft,
                Padding = new Padding(0, 4, 0, 0)
            };
            var cancelBtn = new Button { Text = "Cancel", DialogResult = DialogResult.Cancel, Width = 75 };
            var okBtn = new Button { Text = actionLabel, Width = 75 };
            okBtn.Click += Ok_Click;
            btnPanel.Controls.AddRange(new Control[] { cancelBtn, okBtn });
            layout.Controls.Add(btnPanel, 0, 1);
            layout.SetColumnSpan(btnPanel, 2);

            Controls.Add(layout);
            AcceptButton = okBtn;
            CancelButton = cancelBtn;

            Shown += (s, e) =>
            {
                _nameBox.SelectAll();
                _nameBox.Focus();
            };
        }

        private void Ok_Click(object sender, EventArgs e)
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
