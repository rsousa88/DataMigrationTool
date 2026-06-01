// System
using System;
using System.Collections.Generic;
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

            List<DmtEnvironmentInfo> selectedEnvs;
            if (targets.Count == 1)
            {
                var t = targets[0];
                selectedEnvs = new List<DmtEnvironmentInfo>
                {
                    new DmtEnvironmentInfo { UniqueName = t.UniqueName, FriendlyName = t.FriendlyName, Tag = t.Tag }
                };
            }
            else
            {
                var targetNames = targets.Select(t => string.IsNullOrWhiteSpace(t.FriendlyName) ? t.UniqueName : t.FriendlyName).ToList();
                using (var dlg = new PickItemDialog("Select Target Environments", targetNames, multiSelect: true))
                {
                    if (dlg.ShowDialog(ParentForm) != DialogResult.OK) return;
                    selectedEnvs = dlg.SelectedItems
                        .Select(name => targets.FirstOrDefault(t =>
                            string.Equals(t.FriendlyName, name, StringComparison.OrdinalIgnoreCase) ||
                            string.Equals(t.UniqueName, name, StringComparison.OrdinalIgnoreCase)))
                        .Where(t => t != null)
                        .Select(t => new DmtEnvironmentInfo { UniqueName = t.UniqueName, FriendlyName = t.FriendlyName, Tag = t.Tag })
                        .ToList();
                }
                if (!selectedEnvs.Any()) return;
            }

            if (!EnsureExecutionPlanLoaded()) return;

            // Build template step using the first selected environment
            var (savedTableConfig, _, _, _) = _project?.Service?.GetTableConfig(snapshot.TableLogicalName) ?? (null, null, null, null);
            var templateStep = new ExecutionPlanStep
            {
                Operation = "PushFromSnapshot",
                Name = ExecutionPlanService.BuildPushSnapshotStepName(snapshot, selectedEnvs[0]),
                Enabled = true,
                Table = new DmtTableInfo { LogicalName = snapshot.TableLogicalName },
                TargetEnvironment = selectedEnvs[0],
                Input = new ExecutionPlanStepInput { Mode = "Snapshot", SnapshotName = snapshot.Name },
                Snapshot = new ExecutionPlanStepSnapshot
                {
                    ImportSettings = new UiSettings { Action = DmtAction.Create | DmtAction.Update },
                    PushMatchKeyMode = savedTableConfig?.PushMatchKeyMode,
                    PushMatchKeyFields = savedTableConfig?.PushMatchKeyFields != null
                        ? new List<string>(savedTableConfig.PushMatchKeyFields)
                        : new List<string>(),
                    PushMatchAlternateKeyName = savedTableConfig?.PushMatchAlternateKeyName,
                    LookupMatchKeys = savedTableConfig?.PushLookupMatchKeys?.Any() == true
                        ? savedTableConfig.PushLookupMatchKeys.Select(k => new PushLookupMatchKey
                        {
                            LogicalName = k.LogicalName,
                            Mode = k.Mode,
                            AlternateKeyName = k.AlternateKeyName,
                            Fields = k.Fields != null ? new List<string>(k.Fields) : new List<string>()
                        }).ToList()
                        : null
                }
            };

            // One config dialog for shared push settings; clone per additional environment
            OpenPushStepConfigDialog(templateStep, snapshot, configuredStep =>
            {
                for (int i = 0; i < selectedEnvs.Count; i++)
                {
                    var step = i == 0 ? configuredStep : ClonePushStepForEnvironment(configuredStep, snapshot, selectedEnvs[i]);
                    _executionPlan.Steps.Add(step);
                }
                ExecutionPlanService.ValidatePlan(_executionPlan);
                _executionPlanValidatedForExecution = false;
                AutoSaveExecutionPlan(true);
                RenderExecutionPlanPanel();
                var label = selectedEnvs.Count == 1
                    ? $"Added '{configuredStep.Name}' to execution plan"
                    : $"Added {selectedEnvs.Count} push steps for '{snapshot.Name}' to execution plan";
                SendMessageToStatusBar?.Invoke(this, new StatusBarMessageEventArgs(label));
            }, "Add to Plan");
        }

        private ExecutionPlanStep ClonePushStepForEnvironment(ExecutionPlanStep source, DmtSnapshot snapshot, DmtEnvironmentInfo env)
        {
            var src = source.Snapshot;
            return new ExecutionPlanStep
            {
                Operation = source.Operation,
                Name = ExecutionPlanService.BuildPushSnapshotStepName(snapshot, env),
                Enabled = source.Enabled,
                Table = source.Table,
                TargetEnvironment = env,
                Input = new ExecutionPlanStepInput { Mode = source.Input?.Mode, SnapshotName = source.Input?.SnapshotName },
                Snapshot = src == null ? new ExecutionPlanStepSnapshot() : new ExecutionPlanStepSnapshot
                {
                    ImportSettings = src.ImportSettings,
                    PushMatchKeyMode = src.PushMatchKeyMode,
                    PushMatchKeyFields = src.PushMatchKeyFields != null ? new List<string>(src.PushMatchKeyFields) : new List<string>(),
                    PushMatchAlternateKeyName = src.PushMatchAlternateKeyName,
                    SelectedColumns = src.SelectedColumns != null ? new List<string>(src.SelectedColumns) : null,
                    LookupMatchKeys = src.LookupMatchKeys?.Select(k => new PushLookupMatchKey
                    {
                        LogicalName = k.LogicalName,
                        Mode = k.Mode,
                        AlternateKeyName = k.AlternateKeyName,
                        Fields = k.Fields != null ? new List<string>(k.Fields) : new List<string>()
                    }).ToList()
                }
            };
        }

        #endregion
    }
}
