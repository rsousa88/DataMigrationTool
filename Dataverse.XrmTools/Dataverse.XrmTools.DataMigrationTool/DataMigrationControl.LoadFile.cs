// System
using System;
using System.IO;
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
        #region LoadFile Fields

        private ToolStripMenuItem _tsmiLoadFile;

        #endregion

        #region LoadFile Initialization

        private void InitializeLoadFilePanel()
        {
            _tsmiLoadFile = new ToolStripMenuItem("Import from File");
            _tsmiLoadFile.Click += (s, e) => LoadFileToProject();
            _tsmiData.DropDownItems.Add(_tsmiLoadFile);
        }

        #endregion

        #region LoadFile Operations

        private void LoadFileToProject()
        {
            if (_project?.Service == null)
            {
                MessageBox.Show(this, "Open or create a project first.", "No Project", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            if (_sourceClient == null || _project.IsSourceMismatch)
            {
                MessageBox.Show(this, "Connect the project's source environment before loading files to a project.", "Source Required", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            string filePath;
            using (var ofd = new OpenFileDialog
            {
                Title = "Select JSON or Excel file",
                Filter = "Supported files (*.json;*.xlsx)|*.json;*.xlsx|JSON files (*.json)|*.json|Excel files (*.xlsx)|*.xlsx",
                CheckFileExists = true
            })
            {
                if (ofd.ShowDialog(ParentForm) != DialogResult.OK) return;
                filePath = ofd.FileName;
            }

            var ext = Path.GetExtension(filePath).ToLowerInvariant();
            var defaultName = Path.GetFileNameWithoutExtension(filePath);
            var existingSnapshots = _project.Service.GetSnapshots();

            using (var dlg = new SnapshotNameDialog(defaultName, "Load", existingSnapshots))
            {
                if (dlg.ShowDialog(ParentForm) != DialogResult.OK) return;

                var snapshotName = dlg.SnapshotName;
                var sourceEnvId = _project.SourceEnvironment?.Id ?? string.Empty;
                var config = _currentTableConfig;

                ManageWorkingState(true, $"Loading {Path.GetFileName(filePath)}...");

                WorkAsync(new WorkAsyncInfo
                {
                    Work = (worker, args) =>
                    {
                        if (ext == ".json")
                            args.Result = SqliteFileAdapter.LoadFromJson(_project.Service, filePath, snapshotName, sourceEnvId, config, _sourceClient, worker, NormalizeProjectFilePath(filePath));
                        else
                            args.Result = SqliteFileAdapter.LoadFromExcel(_project.Service, filePath, snapshotName, sourceEnvId, config, _sourceClient, worker, NormalizeProjectFilePath(filePath));
                    },
                    PostWorkCallBack = args =>
                    {
                        ManageWorkingState(false);
                        if (args.Error != null)
                        {
                            _logger.Log(LogLevel.ERROR, args.Error.Message);
                            MessageBox.Show(this, $"Load failed: {args.Error.Message}", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                            return;
                        }

                        var snapshot = args.Result as DmtSnapshot;
                        RefreshInlineSnapshotList();
                        SendMessageToStatusBar?.Invoke(this, new StatusBarMessageEventArgs(
                            $"Loaded {snapshot?.RowCount ?? 0} records → snapshot '{snapshot?.Name}'"));
                    },
                    ProgressChanged = ReportWorkProgress
                });
            }
        }

        private string NormalizeProjectFilePath(string path)
        {
            return ExecutionPlanService.NormalizePlanPathForStorage(path, _project?.FilePath);
        }

        #endregion
    }
}
