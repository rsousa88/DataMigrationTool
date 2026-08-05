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
        #region LoadSqlite Initialization

        private void InitializeLoadSqlitePanel()
        {
            var tsmiLoadSqlite = new ToolStripMenuItem("Import from SQLite Database...");
            tsmiLoadSqlite.Click += (s, e) => ImportSqliteToProject();
            _tsmiData.DropDownItems.Add(tsmiLoadSqlite);
        }

        #endregion

        #region LoadSqlite Operations

        private void ImportSqliteToProject()
        {
            if (_project?.Service == null)
            {
                MessageBox.Show(this, "Open or create a project first.", "No Project", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            if (_sourceClient == null || _project.IsSourceMismatch)
            {
                MessageBox.Show(this, "Connect the project's source environment before importing to a project.", "Source Required", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            string dbFilePath;
            string tableName;
            using (var dlg = new SqliteImportDialog())
            {
                if (dlg.ShowDialog(ParentForm) != DialogResult.OK) return;
                dbFilePath = dlg.DatabaseFilePath;
                tableName = dlg.SelectedTable;
            }

            var existingSnapshots = _project.Service.GetSnapshots();
            using (var dlg = new SnapshotNameDialog(tableName, "Import", existingSnapshots))
            {
                if (dlg.ShowDialog(ParentForm) != DialogResult.OK) return;

                var snapshotName = dlg.SnapshotName;
                var sourceEnvId = _project.SourceEnvironment?.Id ?? string.Empty;
                var config = _currentTableConfig;

                ManageWorkingState(true, $"Importing table '{tableName}'...");

                WorkAsync(new WorkAsyncInfo
                {
                    Work = (worker, args) =>
                    {
                        args.Result = SqliteFileAdapter.LoadFromSqlite(_project.Service, dbFilePath, tableName, snapshotName,
                            sourceEnvId, config, _sourceClient, worker, NormalizeProjectFilePath(dbFilePath), _currentTableLogicalName);
                    },
                    PostWorkCallBack = args =>
                    {
                        ManageWorkingState(false);
                        if (args.Error != null)
                        {
                            _logger.Log(LogLevel.ERROR, args.Error.Message);
                            MessageBox.Show(this, $"Import failed: {args.Error.Message}", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                            return;
                        }

                        var snapshot = args.Result as DmtSnapshot;
                        RefreshInlineSnapshotList();
                        SendMessageToStatusBar?.Invoke(this, new StatusBarMessageEventArgs(
                            $"Imported {snapshot?.RowCount ?? 0} records → snapshot '{snapshot?.Name}'"));
                    },
                    ProgressChanged = ReportWorkProgress
                });
            }
        }

        #endregion
    }
}
