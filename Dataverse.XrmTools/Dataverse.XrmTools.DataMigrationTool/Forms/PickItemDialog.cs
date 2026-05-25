// System
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;

namespace Dataverse.XrmTools.DataMigrationTool.Forms
{
    public class PickItemDialog : Form
    {
        private ListBox _list;

        public string SelectedItem => _list.SelectedItem?.ToString();

        public PickItemDialog(string prompt, IList<string> items)
        {
            Text = prompt;
            ClientSize = new Size(440, 280);
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

            _list = new ListBox { Dock = DockStyle.Fill, SelectionMode = SelectionMode.One };
            foreach (var item in items)
                _list.Items.Add(item);
            if (_list.Items.Count > 0) _list.SelectedIndex = 0;
            _list.DoubleClick += (s, e) => { if (SelectedItem != null) Ok(); };
            layout.Controls.Add(_list, 0, 0);

            var btnPanel = new FlowLayoutPanel
            {
                Dock = DockStyle.Fill,
                FlowDirection = FlowDirection.RightToLeft,
                Padding = new Padding(0, 4, 0, 0)
            };
            var cancelBtn = new Button { Text = "Cancel", DialogResult = DialogResult.Cancel, Width = 75 };
            var okBtn = new Button { Text = "OK", Width = 75 };
            okBtn.Click += (s, e) => Ok();
            btnPanel.Controls.AddRange(new Control[] { cancelBtn, okBtn });
            layout.Controls.Add(btnPanel, 0, 1);

            Controls.Add(layout);
            AcceptButton = okBtn;
            CancelButton = cancelBtn;
        }

        private void Ok()
        {
            if (SelectedItem == null)
            {
                MessageBox.Show(this, "Select an item first.", "Selection Required", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            DialogResult = DialogResult.OK;
        }
    }
}
