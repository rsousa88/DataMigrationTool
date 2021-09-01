using System;
using System.Windows.Forms;
using Dataverse.XrmTools.DataMigrationTool.Models;
using Dataverse.XrmTools.DataMigrationTool.AppSettings;

namespace Dataverse.XrmTools.DataMigrationTool.Forms
{
    public partial class Filters : Form
    {
        public bool Updated { get; set; }

        private TableSettings _tableSettings;

        public Filters(Table table, TableSettings tableSettings)
        {
            Updated = false;

            _tableSettings = tableSettings;

            InitializeComponent();
        }

        private void LoadFilter(object sender, EventArgs e)
        {
            rtbFilter.Text = _tableSettings.Filter;
        }

        private void btnClose_Click(object sender, EventArgs e)
        {
            var after = rtbFilter.Text;
            if(!after.Equals(_tableSettings.Filter)) // filter has changed
            {
                // save updated filter to settings object
                _tableSettings.Filter = after;

                Updated = true;
            }

            Close();
        }
    }
}
