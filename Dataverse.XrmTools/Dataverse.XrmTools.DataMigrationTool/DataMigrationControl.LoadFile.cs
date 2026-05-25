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
            _tsmiLoadFile = new ToolStripMenuItem("Load File to Project")
            {
                Image = Properties.Resources.import,
                ImageScaling = ToolStripItemImageScaling.None
            };
            _tsmiLoadFile.Click += (s, e) => LoadFileToProject();
            _tsmiProject.DropDownItems.Add(_tsmiLoadFile);
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

            using (var dlg = new SnapshotNameDialog(defaultName, "Load"))
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
                            args.Result = SqliteFileAdapter.LoadFromJson(_project.Service, filePath, snapshotName, sourceEnvId, config, worker);
                        else
                            args.Result = SqliteFileAdapter.LoadFromExcel(_project.Service, filePath, snapshotName, sourceEnvId, config, worker);
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
                        SendMessageToStatusBar?.Invoke(this, new StatusBarMessageEventArgs(
                            $"Loaded {snapshot?.RowCount ?? 0} records → snapshot '{snapshot?.Name}'"));
                    },
                    ProgressChanged = ReportWorkProgress
                });
            }
        }

        #endregion
    }
}
