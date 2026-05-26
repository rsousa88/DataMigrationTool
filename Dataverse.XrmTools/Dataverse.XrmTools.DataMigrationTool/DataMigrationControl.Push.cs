// System
using System;
using System.Linq;
using System.Windows.Forms;

// XrmToolBox
using XrmToolBox.Extensibility.Args;

// DataMigrationTool
using Dataverse.XrmTools.DataMigrationTool.Enums;
using Dataverse.XrmTools.DataMigrationTool.Forms;
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
            _tsmiPush = new ToolStripMenuItem("Add Snapshot to Plan");
            _tsmiPush.Click += (s, e) => AddPushSnapshotStepToExecutionPlan();
            _tsmiDeploy.DropDownItems.Add(_tsmiPush);
        }

        #endregion

        #region Push Operations

        private void AddPushSnapshotStepToExecutionPlan()
        {
            if (_project?.Service == null)
            {
                MessageBox.Show(this, "Open or create a project first.", "No Project", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            var snapshots = _project.Service.GetSnapshots();
            if (snapshots == null || !snapshots.Any())
            {
                MessageBox.Show(this, "No snapshots in this project. Pull data or load a file first.", "No Snapshots", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            DmtSnapshot snapshot;
            using (var dlg = new Forms.PickSnapshotDialog("Select Snapshot", snapshots))
            {
                if (dlg.ShowDialog(ParentForm) != DialogResult.OK) return;
                snapshot = dlg.SelectedSnapshot;
            }

            AddSnapshotToPlan(snapshot);
        }

        internal void AddSnapshotToPlan(DmtSnapshot snapshot)
        {
            if (snapshot == null) return;

            var targets = GetLoadedTargetEnvironments();
            if (targets == null || !targets.Any())
            {
                MessageBox.Show(this, "Connect to a target environment first (use 'Additional Connection' in XrmToolBox).", "No Target", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            DmtEnvironmentInfo targetEnv;
            if (targets.Count == 1)
            {
                var t = targets[0];
                targetEnv = new DmtEnvironmentInfo { UniqueName = t.UniqueName, FriendlyName = t.FriendlyName };
            }
            else
            {
                var targetNames = targets.Select(t => string.IsNullOrWhiteSpace(t.FriendlyName) ? t.UniqueName : t.FriendlyName).ToList();
                using (var dlg = new PickItemDialog("Select Target Environment", targetNames))
                {
                    if (dlg.ShowDialog(ParentForm) != DialogResult.OK) return;
                    var chosen = targets.FirstOrDefault(t =>
                        string.Equals(t.FriendlyName, dlg.SelectedItem, StringComparison.OrdinalIgnoreCase) ||
                        string.Equals(t.UniqueName, dlg.SelectedItem, StringComparison.OrdinalIgnoreCase));
                    if (chosen == null) return;
                    targetEnv = new DmtEnvironmentInfo { UniqueName = chosen.UniqueName, FriendlyName = chosen.FriendlyName };
                }
            }

            if (!EnsureExecutionPlanLoaded()) return;

            var step = new ExecutionPlanStep
            {
                Operation = "PushFromSnapshot",
                Name = $"Push {snapshot.TableLogicalName} (#{snapshot.SortOrder} {snapshot.Name})",
                Enabled = true,
                Table = new DmtTableInfo { LogicalName = snapshot.TableLogicalName },
                TargetEnvironment = targetEnv,
                Input = new ExecutionPlanStepInput
                {
                    Mode = "Snapshot",
                    SnapshotName = snapshot.Name
                },
                Snapshot = new ExecutionPlanStepSnapshot
                {
                    ImportSettings = new UiSettings { Action = DmtAction.Create | DmtAction.Update }
                }
            };

            OpenPushStepConfigDialog(step, snapshot, configuredStep =>
            {
                _executionPlan.Steps.Add(configuredStep);
                ExecutionPlanFileService.ValidatePlan(_executionPlan);
                _executionPlanValidatedForExecution = false;
                AutoSaveExecutionPlan(true);
                RenderExecutionPlanPanel();
                SendMessageToStatusBar?.Invoke(this, new StatusBarMessageEventArgs($"Added '{configuredStep.Name}' to execution plan"));
            }, "Configure & Add");
        }

        #endregion
    }
}
