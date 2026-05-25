// System
using System;
using System.Drawing;
using System.Windows.Forms;

namespace Dataverse.XrmTools.DataMigrationTool.Forms
{
    public class NewProjectDialog : Form
    {
        private TextBox _nameBox;
        private TextBox _pathBox;

        public string ProjectName => _nameBox.Text.Trim();
        public string FilePath => _pathBox.Text.Trim();

        public NewProjectDialog()
        {
            Text = "New Project";
            ClientSize = new Size(480, 130);
            FormBorderStyle = FormBorderStyle.FixedDialog;
            StartPosition = FormStartPosition.CenterParent;
            ShowIcon = false;
            ShowInTaskbar = false;
            MaximizeBox = false;
            MinimizeBox = false;

            var layout = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                RowCount = 3,
                ColumnCount = 3,
                Padding = new Padding(10)
            };
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 80));
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 80));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 30));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 30));
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));

            layout.Controls.Add(new Label { Text = "Name:", TextAlign = ContentAlignment.MiddleLeft, Dock = DockStyle.Fill }, 0, 0);
            _nameBox = new TextBox { Dock = DockStyle.Fill };
            layout.Controls.Add(_nameBox, 1, 0);
            layout.SetColumnSpan(_nameBox, 2);

            layout.Controls.Add(new Label { Text = "File:", TextAlign = ContentAlignment.MiddleLeft, Dock = DockStyle.Fill }, 0, 1);
            _pathBox = new TextBox { Dock = DockStyle.Fill, ReadOnly = true };
            layout.Controls.Add(_pathBox, 1, 1);
            var browseBtn = new Button { Text = "Browse...", Dock = DockStyle.Fill };
            browseBtn.Click += Browse_Click;
            layout.Controls.Add(browseBtn, 2, 1);

            var btnPanel = new FlowLayoutPanel
            {
                Dock = DockStyle.Fill,
                FlowDirection = FlowDirection.RightToLeft,
                Padding = new Padding(0, 4, 0, 0)
            };
            var cancelBtn = new Button { Text = "Cancel", DialogResult = DialogResult.Cancel, Width = 75 };
            var createBtn = new Button { Text = "Create", Width = 75 };
            createBtn.Click += Create_Click;
            btnPanel.Controls.AddRange(new Control[] { cancelBtn, createBtn });
            layout.Controls.Add(btnPanel, 0, 2);
            layout.SetColumnSpan(btnPanel, 3);

            Controls.Add(layout);
            AcceptButton = createBtn;
            CancelButton = cancelBtn;
        }

        private void Browse_Click(object sender, EventArgs e)
        {
            using (var dlg = new SaveFileDialog
            {
                Title = "Create Project File",
                Filter = "DMT Project (*.dmtproj)|*.dmtproj",
                DefaultExt = "dmtproj",
                FileName = string.IsNullOrWhiteSpace(_nameBox.Text) ? "project" : _nameBox.Text.Trim()
            })
            {
                if (dlg.ShowDialog(this) == DialogResult.OK)
                    _pathBox.Text = dlg.FileName;
            }
        }

        private void Create_Click(object sender, EventArgs e)
        {
            if (string.IsNullOrWhiteSpace(_nameBox.Text))
            {
                MessageBox.Show(this, "Project name is required.", "Validation", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                _nameBox.Focus();
                return;
            }
            if (string.IsNullOrWhiteSpace(_pathBox.Text))
            {
                MessageBox.Show(this, "Please select a file path.", "Validation", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            DialogResult = DialogResult.OK;
        }
    }
}
