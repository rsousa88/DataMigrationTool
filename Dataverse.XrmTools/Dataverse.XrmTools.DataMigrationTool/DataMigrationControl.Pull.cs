// System
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
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
        #region Pull Fields

        private ToolStripMenuItem _tsmiPull;

        #endregion

        #region Pull Initialization

        private void InitializePullPanel()
        {
            _tsmiPull = new ToolStripMenuItem("Pull to Project")
            {
                Image = Properties.Resources.database,
                ImageScaling = ToolStripItemImageScaling.None
            };
            _tsmiPull.Click += (s, e) => PullToProject();
            _tsmiProject.DropDownItems.Add(_tsmiPull);
        }

        #endregion

        #region Pull Operations

        private void PullToProject()
        {
            if (_project?.Service == null)
            {
                MessageBox.Show(this, "Open or create a project first.", "No Project", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            if (_project.IsSourceMismatch)
            {
                MessageBox.Show(this, "Source mismatch — pull is blocked until you connect the project's original source environment.", "Source Mismatch", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            if (_sourceClient == null)
            {
                MessageBox.Show(this, "Connect to a source environment first.", "No Connection", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            if (_currentTableConfig == null || string.IsNullOrEmpty(_currentTableLogicalName))
            {
                MessageBox.Show(this, "Select a table and load its attributes first.", "No Table", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            var defaultName = $"{_currentTableLogicalName}-{DateTime.UtcNow:yyyyMMdd}";
            using (var dlg = new SnapshotNameDialog(defaultName))
            {
                if (dlg.ShowDialog(ParentForm) != DialogResult.OK) return;

                var snapshotName = dlg.SnapshotName;
                var tableLogicalName = _currentTableLogicalName;
                var config = _currentTableConfig;
                var table = _tables?.FirstOrDefault(t => string.Equals(t.LogicalName, tableLogicalName, StringComparison.OrdinalIgnoreCase));
                if (table == null) return;

                var sourceEnvId = _sourceClient.EnvironmentId ?? _sourceClient.ConnectedOrgId.ToString();

                ManageWorkingState(true, $"Pulling {tableLogicalName}...");

                WorkAsync(new WorkAsyncInfo
                {
                    Work = (worker, args) =>
                    {
                        var repo = new Repositories.CrmRepo(_sourceClient);
                        var metadata = repo.GetTableMetadata(tableLogicalName);

                        args.Result = SqliteDataLogic.Pull(
                            _project.Service,
                            _sourceClient,
                            tableLogicalName,
                            table.IdAttribute,
                            sourceEnvId,
                            config,
                            snapshotName,
                            metadata.Attributes,
                            worker);
                    },
                    PostWorkCallBack = args =>
                    {
                        ManageWorkingState(false);
                        if (args.Error != null)
                        {
                            _logger.Log(LogLevel.ERROR, args.Error.Message);
                            MessageBox.Show(this, $"Pull failed: {args.Error.Message}", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                            return;
                        }

                        var snapshot = args.Result as DmtSnapshot;
                        SendMessageToStatusBar?.Invoke(this, new StatusBarMessageEventArgs(
                            $"Pulled {snapshot?.RowCount ?? 0} {tableLogicalName} records → snapshot '{snapshot?.Name}'"));
                    },
                    ProgressChanged = ReportWorkProgress
                });
            }
        }

        #endregion
    }
}
