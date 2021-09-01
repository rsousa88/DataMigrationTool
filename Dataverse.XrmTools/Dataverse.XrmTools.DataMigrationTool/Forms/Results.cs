using System;
using System.Data;
using System.Linq;
using System.Windows.Forms;
using System.Collections.Generic;
using System.Text;
using static System.Windows.Forms.ListViewItem;
using Dataverse.XrmTools.DataMigrationTool.Helpers;
using Dataverse.XrmTools.DataMigrationTool.AppSettings;

namespace Dataverse.XrmTools.DataMigrationTool.Forms
{
    public partial class Results : Form
    {
        private Settings _settings;
        private IEnumerable<ListViewItem> _recordItems;

        public Results(IEnumerable<ListViewItem> recordItems, Settings settings)
        {
            _settings = settings;
            _recordItems = recordItems;
            InitializeComponent();
        }

        private void btnClose_Click(object sender, EventArgs e)
        {
            Close();
        }

        private void LoadRecords(object sender, EventArgs e)
        {
            lvItems.Items.Clear();
            lvItems.Items.AddRange(_recordItems.ToArray());

            // re-render list view columns
            lvItems.AutoResizeColumns(ColumnHeaderAutoResizeStyle.ColumnContent);

            // ensure minimum width
            if(chResAction.Width < 100) { chResAction.Width = 100; }
            if(chResRecordId.Width < 250) { chResRecordId.Width = 250; }
            if(chResRecordName.Width < 400) { chResRecordName.Width = 400; }
            if(chResDescription.Width < 750) { chResDescription.Width = 750; }

            // Set summary
            SetSummary();
        }

        private void SetSummary()
        {
            var createCount = _recordItems.Where(prv => prv.Text.Equals("Create")).Count();
            var updateCount = _recordItems.Where(prv => prv.Text.Equals("Update")).Count();
            var deleteCount = _recordItems.Where(prv => prv.Text.Equals("Delete")).Count();

            var message = $"Create: {createCount} | Update: {updateCount} | Delete: {deleteCount} | Total: {createCount + updateCount + deleteCount}";
            lblSummary.Text = message;
        }

        private void listView_ColumnClick(object sender, ColumnClickEventArgs e)
        {
            (sender as ListView).Sort(_settings, e.Column);
        }

        private void lvItems_KeyUp(object sender, KeyEventArgs e)
        {
            if (sender != lvItems) return;

            if (e.Control && e.KeyCode == Keys.C)
            {
                CopySelectedValuesToClipboard();
            }
        }

        private void CopySelectedValuesToClipboard()
        {
            var builder = new StringBuilder();

            // add columns
            foreach (ColumnHeader column in lvItems.Columns)
            {
                builder.Append($"{column.Text};");
            }

            builder.AppendLine();

            // add rows
            foreach (ListViewItem item in lvItems.SelectedItems)
            {
                foreach (ListViewSubItem sub in item.SubItems)
                {
                    builder.Append($"{sub.Text};");
                }

                builder.AppendLine();
            }

            // set clipboard
            Clipboard.SetText(builder.ToString());
        }
    }
}
