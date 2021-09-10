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

        private void LoadRecords(object sender, EventArgs e)
        {
            lvItems.Items.Clear();
            lvItems.Items.AddRange(_recordItems.ToArray());

            // Set summary
            SetSummary();
        }

        private void SetSummary()
        {
            var createCount = _recordItems.Where(prv => prv.Text.Equals("Create")).Count();
            var updateCount = _recordItems.Where(prv => prv.Text.Equals("Update")).Count();
            var deleteCount = _recordItems.Where(prv => prv.Text.Equals("Delete")).Count();

            lblSumCreateValue.Text = createCount.ToString();
            lblSumUpdateValue.Text = updateCount.ToString();
            lblSumDeleteValue.Text = deleteCount.ToString();
            lblSumTotalValue.Text = (createCount + updateCount + deleteCount).ToString();
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

        private void Results_Resize(object sender, EventArgs e)
        {
            // re-render list view columns
            var maxWidth = lvItems.Width >= 500 ? lvItems.Width : 500;
            chResAction.Width = (int)Math.Floor(maxWidth * 0.07);
            chResRecordId.Width = (int)Math.Floor(maxWidth * 0.25);
            chResRecordName.Width = (int)Math.Floor(maxWidth * 0.30);
            chResDescription.Width = (int)Math.Floor(maxWidth * 0.37);
        }

        private void btnClose_Click(object sender, EventArgs e)
        {
            Close();
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
