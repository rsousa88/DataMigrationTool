// System
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;

namespace Dataverse.XrmTools.DataMigrationTool.Forms
{
    public class PickItemDialog : Form
    {
        private ListBox _list;
        private CheckedListBox _checkedList;
        private readonly bool _multiSelect;

        public string SelectedItem => _list?.SelectedItem?.ToString();
        public IList<string> SelectedItems => _multiSelect
            ? _checkedList.CheckedItems.Cast<string>().ToList()
            : (_list.SelectedItem != null ? new List<string> { _list.SelectedItem.ToString() } : new List<string>());

        public PickItemDialog(string prompt, IList<string> items, bool multiSelect = false)
        {
            _multiSelect = multiSelect;
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

            if (multiSelect)
            {
                _checkedList = new CheckedListBox
                {
                    Dock = DockStyle.Fill,
                    CheckOnClick = true
                };
                foreach (var item in items)
                    _checkedList.Items.Add(item);
                layout.Controls.Add(_checkedList, 0, 0);
            }
            else
            {
                _list = new ListBox { Dock = DockStyle.Fill, SelectionMode = SelectionMode.One };
                foreach (var item in items)
                    _list.Items.Add(item);
                if (_list.Items.Count > 0) _list.SelectedIndex = 0;
                _list.DoubleClick += (s, e) => { if (SelectedItem != null) Ok(); };
                layout.Controls.Add(_list, 0, 0);
            }

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
            var hasSelection = _multiSelect ? _checkedList.CheckedItems.Count > 0 : SelectedItem != null;
            if (!hasSelection)
            {
                MessageBox.Show(this, "Select at least one item.", "Selection Required", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            DialogResult = DialogResult.OK;
        }
    }
}
