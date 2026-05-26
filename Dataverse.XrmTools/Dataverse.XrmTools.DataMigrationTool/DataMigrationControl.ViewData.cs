// System
using System.Windows.Forms;

// XrmToolBox
using XrmToolBox.Extensibility.Args;

// DataMigrationTool
using Dataverse.XrmTools.DataMigrationTool.Forms;

namespace Dataverse.XrmTools.DataMigrationTool
{
    public partial class DataMigrationControl
    {
        #region ViewData Fields

        private ToolStripMenuItem _tsmiViewData;

        #endregion

        #region ViewData Initialization

        private void InitializeViewDataPanel()
        {
            _tsmiViewData = new ToolStripMenuItem("View Snapshots...");
            _tsmiViewData.Click += (s, e) => ShowSnapshotViewer();
            _tsmiData.DropDownItems.Add(_tsmiViewData);
        }

        #endregion

        #region ViewData Operations

        private void ShowSnapshotViewer()
        {
            if (_project?.Service == null)
            {
                MessageBox.Show(this, "Open or create a project first.", "No Project", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            if (!_project.Service.HasAnySnapshot())
            {
                MessageBox.Show(this, "No snapshots in this project yet. Pull data or load a file first.", "No Snapshots", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            using (var dlg = new SnapshotViewerDialog(_project.Service))
                dlg.ShowDialog(ParentForm);
        }

        #endregion
    }
}
