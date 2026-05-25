// System
using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows.Forms;

// XrmToolBox
using XrmToolBox.Extensibility;
using XrmToolBox.Extensibility.Args;

// DataMigrationTool
using Dataverse.XrmTools.DataMigrationTool.Enums;
using Dataverse.XrmTools.DataMigrationTool.Logic;
using Dataverse.XrmTools.DataMigrationTool.Models;
using DmtAction = Dataverse.XrmTools.DataMigrationTool.Enums.Action;

namespace Dataverse.XrmTools.DataMigrationTool
{
    public partial class DataMigrationControl
    {
        #region Push Fields

        private ToolStripMenuItem _tsmiPush;

        #endregion

        #region Push Initialization

        private void InitializePushPanel()
        {
            _tsmiPush = new ToolStripMenuItem("Push Snapshot to Target");
            _tsmiPush.Click += (s, e) => PushSnapshot();
            _tsmiProject.DropDownItems.Add(_tsmiPush);
        }

        #endregion

        #region Push Operations

        private void PushSnapshot()
        {
            if (_project?.Service == null)
            {
                MessageBox.Show(this, "Open or create a project first.", "No Project", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            if (_project.IsSourceMismatch)
            {
                MessageBox.Show(this, "Source mismatch — push is blocked until you connect the project's original source environment.", "Source Mismatch", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            if (_project.TargetClients == null || !_project.TargetClients.Any())
            {
                MessageBox.Show(this, "Connect to a target environment first (use 'Additional Connection' in XrmToolBox).", "No Target", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            var snapshots = _project.Service.GetSnapshots();
            if (snapshots == null || !snapshots.Any())
            {
                MessageBox.Show(this, "No snapshots in this project. Pull data or load a file first.", "No Snapshots", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            // Pick snapshot
            string snapshotName;
            using (var dlg = new Forms.PickItemDialog("Select Snapshot to Push",
                snapshots.Select(s => s.Name).OrderBy(n => n).ToList()))
            {
                if (dlg.ShowDialog(ParentForm) != DialogResult.OK) return;
                snapshotName = dlg.SelectedItem;
            }

            // Pick target environment
            string targetEnvId;
            using (var dlg = new Forms.PickItemDialog("Select Target Environment",
                _project.TargetClients.Keys.OrderBy(k => k).ToList()))
            {
                if (dlg.ShowDialog(ParentForm) != DialogResult.OK) return;
                targetEnvId = dlg.SelectedItem;
            }

            if (!_project.TargetClients.TryGetValue(targetEnvId, out var targetClient))
            {
                MessageBox.Show(this, $"Target environment '{targetEnvId}' is not connected.", "Not Connected", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            var sourceEnvId = _project.SourceEnvironment?.Id ?? string.Empty;
            var settings = new UiSettings { Action = DmtAction.Create | DmtAction.Update };

            ManageWorkingState(true, $"Pushing '{snapshotName}' to {targetEnvId}...");

            WorkAsync(new WorkAsyncInfo
            {
                Work = (worker, args) =>
                {
                    args.Result = SqliteDataLogic.Push(
                        _project.Service,
                        snapshotName,
                        sourceEnvId,
                        targetEnvId,
                        targetClient,
                        settings,
                        worker);
                },
                PostWorkCallBack = args =>
                {
                    ManageWorkingState(false);
                    if (args.Error != null)
                    {
                        _logger.Log(LogLevel.ERROR, args.Error.Message);
                        MessageBox.Show(this, $"Push failed: {args.Error.Message}", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                        return;
                    }

                    var result = args.Result as SqliteDataLogic.PushResult;
                    var msg = $"Push complete: {result?.Created ?? 0} created, {result?.Updated ?? 0} updated, {result?.Deleted ?? 0} deleted.";
                    if (result?.Errors?.Any() == true)
                        msg += $" {result.Errors.Count} error(s).";

                    SendMessageToStatusBar?.Invoke(this, new StatusBarMessageEventArgs(msg));

                    if (result?.Errors?.Any() == true)
                        MessageBox.Show(this,
                            $"{msg}\n\nFirst error: {result.Errors[0]}",
                            "Push Completed With Errors",
                            MessageBoxButtons.OK, MessageBoxIcon.Warning);
                },
                ProgressChanged = ReportWorkProgress
            });
        }

        #endregion
    }
}
