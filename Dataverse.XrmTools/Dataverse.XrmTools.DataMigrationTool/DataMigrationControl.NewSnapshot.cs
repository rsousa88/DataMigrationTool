// System
using System.Windows.Forms;

// XrmToolBox
using XrmToolBox.Extensibility;
using XrmToolBox.Extensibility.Args;

// DataMigrationTool
using Dataverse.XrmTools.DataMigrationTool.Enums;
using Dataverse.XrmTools.DataMigrationTool.Forms;
using Dataverse.XrmTools.DataMigrationTool.Logic;
using Dataverse.XrmTools.DataMigrationTool.Models;

namespace Dataverse.XrmTools.DataMigrationTool
{
    public partial class DataMigrationControl
    {
        #region NewSnapshot Operations

        private void NewManualSnapshot()
        {
            if (_project?.Service == null)
            {
                MessageBox.Show(this, "Open or create a project first.", "No Project", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            var existingSnapshots = _project.Service.GetSnapshots();
            using (var dlg = new ManualSnapshotDialog(existingSnapshots))
            {
                if (dlg.ShowDialog(ParentForm) != DialogResult.OK) return;

                var snapshotName = dlg.SnapshotName;
                var columns = dlg.Columns;
                var rows = dlg.Rows;
                var sourceEnvId = _project.SourceEnvironment?.Id ?? string.Empty;

                ManageWorkingState(true, $"Creating snapshot '{snapshotName}'...");

                WorkAsync(new WorkAsyncInfo
                {
                    Work = (worker, args) =>
                    {
                        args.Result = SqliteFileAdapter.CreateManualSnapshot(_project.Service, snapshotName, columns, rows, sourceEnvId, worker);
                    },
                    PostWorkCallBack = args =>
                    {
                        ManageWorkingState(false);
                        if (args.Error != null)
                        {
                            _logger.Log(LogLevel.ERROR, args.Error.Message);
                            MessageBox.Show(this, $"Create failed: {args.Error.Message}", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                            return;
                        }

                        var snapshot = args.Result as DmtSnapshot;
                        RefreshInlineSnapshotList();
                        SendMessageToStatusBar?.Invoke(this, new StatusBarMessageEventArgs(
                            $"Created snapshot '{snapshot?.Name}' with {snapshot?.RowCount ?? 0} rows"));
                    },
                    ProgressChanged = ReportWorkProgress
                });
            }
        }

        #endregion
    }
}
