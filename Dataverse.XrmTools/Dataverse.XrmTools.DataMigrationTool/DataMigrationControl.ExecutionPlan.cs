// System
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Windows.Forms;

// Microsoft
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;

// XrmToolBox
using XrmToolBox.Extensibility;
using XrmToolBox.Extensibility.Args;

// DataMigrationTool
using Dataverse.XrmTools.DataMigrationTool.AppSettings;
using Dataverse.XrmTools.DataMigrationTool.Enums;
using Dataverse.XrmTools.DataMigrationTool.Forms;
using Dataverse.XrmTools.DataMigrationTool.Helpers;
using Dataverse.XrmTools.DataMigrationTool.Logic;
using Dataverse.XrmTools.DataMigrationTool.Models;

namespace Dataverse.XrmTools.DataMigrationTool
{
    public partial class DataMigrationControl
    {
        #region Execution Plan Methods
        private bool EnsureExecutionPlanLoaded()
        {
            if (_executionPlan != null && !string.IsNullOrWhiteSpace(_executionPlanFilePath)) return true;

            using (var dlg = new ExecutionPlanFileDialog())
            {
                if (dlg.ShowDialog(ParentForm) != DialogResult.OK || string.IsNullOrWhiteSpace(dlg.FilePath))
                    return false;

                if (dlg.Choice == ExecutionPlanFileChoice.NewFile)
                {
                    CreateExecutionPlan(dlg.FilePath);
                    return true;
                }

                if (dlg.Choice == ExecutionPlanFileChoice.ExistingFile)
                {
                    LoadExecutionPlan(dlg.FilePath);
                    return true;
                }
            }

            return false;
        }

        private void CreateExecutionPlan(string filePath = null)
        {
            if (string.IsNullOrWhiteSpace(filePath))
            {
                using (var dlg = new SaveFileDialog
                {
                    Title = "Create execution plan",
                    Filter = "DMT Execution Plan (*.dmtplan.json)|*.dmtplan.json",
                    DefaultExt = "dmtplan.json",
                    FileName = GetDefaultSaveFileName(".dmtplan.json", "migration-plan")
                })
                {
                    if (dlg.ShowDialog(ParentForm) != DialogResult.OK) return;
                    filePath = dlg.FileName;
                }
            }

            _executionPlan = ExecutionPlanFileService.CreateNew(
                filePath,
                _sourceClient?.ConnectedOrgUniqueName,
                _sourceClient?.ConnectedOrgFriendlyName,
                _targetClient?.ConnectedOrgUniqueName,
                _targetClient?.ConnectedOrgFriendlyName);
            UpdateExecutionPlanTargetEnvironments();
            _executionPlanFilePath = filePath;
            _executionPlanValidatedForExecution = false;
            AutoSaveExecutionPlan();
            RenderExecutionPlanMenu();
            SendMessageToStatusBar?.Invoke(this, new StatusBarMessageEventArgs($"Execution plan created: {Path.GetFileName(filePath)}"));
        }

        private void LoadExecutionPlan(string filePath = null)
        {
            if (string.IsNullOrWhiteSpace(filePath))
            {
                using (var dlg = new OpenFileDialog
                {
                    Title = "Load execution plan",
                    Filter = "DMT Execution Plan (*.dmtplan.json)|*.dmtplan.json"
                })
                {
                    if (dlg.ShowDialog(ParentForm) != DialogResult.OK) return;
                    filePath = dlg.FileName;
                }
            }

            _executionPlan = ExecutionPlanFileService.Load(filePath);
            _executionPlanFilePath = filePath;
            UpdateExecutionPlanTargetEnvironments();
            ExecutionPlanFileService.ValidatePlan(_executionPlan);
            _executionPlanValidatedForExecution = false;
            RenderExecutionPlanMenu();
            SendMessageToStatusBar?.Invoke(this, new StatusBarMessageEventArgs($"Execution plan loaded: {Path.GetFileName(filePath)}"));
        }

        private void SaveExecutionPlan()
        {
            if (_executionPlan == null || string.IsNullOrWhiteSpace(_executionPlanFilePath))
            {
                CreateExecutionPlan();
                return;
            }

            ExecutionPlanFileService.Save(_executionPlanFilePath, _executionPlan);
            RenderExecutionPlanMenu();
            SendMessageToStatusBar?.Invoke(this, new StatusBarMessageEventArgs($"Execution plan saved: {Path.GetFileName(_executionPlanFilePath)}"));
        }

        private void SaveExecutionPlanAs()
        {
            if (_executionPlan == null)
            {
                CreateExecutionPlan();
                return;
            }

            using (var dlg = new SaveFileDialog
            {
                Title = "Save execution plan as",
                Filter = "DMT Execution Plan (*.dmtplan.json)|*.dmtplan.json",
                DefaultExt = "dmtplan.json",
                FileName = !string.IsNullOrWhiteSpace(_executionPlanFilePath)
                    ? Path.GetFileName(_executionPlanFilePath)
                    : GetDefaultSaveFileName(".dmtplan.json", "migration-plan")
            })
            {
                if (!string.IsNullOrWhiteSpace(_executionPlanFilePath))
                    dlg.InitialDirectory = Path.GetDirectoryName(_executionPlanFilePath);
                if (dlg.ShowDialog(ParentForm) != DialogResult.OK) return;

                _executionPlanFilePath = dlg.FileName;
                ExecutionPlanFileService.Save(_executionPlanFilePath, _executionPlan);
                RenderExecutionPlanMenu();
                SendMessageToStatusBar?.Invoke(this, new StatusBarMessageEventArgs($"Execution plan saved as: {Path.GetFileName(_executionPlanFilePath)}"));
            }
        }

        private void AutoSaveExecutionPlan(bool showStatus = false)
        {
            if (_executionPlan == null || string.IsNullOrWhiteSpace(_executionPlanFilePath)) return;

            ExecutionPlanFileService.Save(_executionPlanFilePath, _executionPlan);
            RenderExecutionPlanMenu();
            if (showStatus)
                SendMessageToStatusBar?.Invoke(this, new StatusBarMessageEventArgs($"Execution plan saved: {Path.GetFileName(_executionPlanFilePath)}"));
        }

        private void ReviewExecutionPlan()
        {
            if (!EnsureExecutionPlanLoaded()) return;

            using (var dlg = new ExecutionPlanDialog(_executionPlan))
            {
                dlg.ShowDialog(ParentForm);
                if (dlg.PlanChanged)
                {
                    _executionPlanValidatedForExecution = false;
                    AutoSaveExecutionPlan(true);
                }
            }
        }

        private void InitializeExecutionPlanPanel()
        {
            if (_executionPlanGroup != null) return;

            pnlMain.SuspendLayout();
            pnlMain.ColumnStyles.Clear();
            pnlMain.ColumnCount = 1;
            pnlMain.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            pnlMain.Controls.Remove(pnlBody);

            _executionPlanSplitContainer = new SplitContainer
            {
                Dock = DockStyle.Fill,
                Orientation = Orientation.Vertical,
                SplitterWidth = 6,
                FixedPanel = FixedPanel.None,
                Panel1MinSize = 1,
                Panel2MinSize = 1
            };
            _executionPlanSplitContainer.SplitterMoved += (sender, args) => EnforceExecutionPlanPanelWidth();
            _executionPlanSplitContainer.SizeChanged += (sender, args) => EnforceExecutionPlanPanelWidth();
            _executionPlanSplitContainer.Panel1.Controls.Add(pnlBody);
            pnlMain.Controls.Add(_executionPlanSplitContainer, 0, 0);

            _executionPlanGroup = new GroupBox
            {
                Text = "Execution Plan",
                Dock = DockStyle.Fill,
                Padding = new Padding(6)
            };

            var headerLayout = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 2,
                RowCount = 1,
                Margin = new Padding(0)
            };
            headerLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            headerLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 150));

            var layout = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                RowCount = 5,
                ColumnCount = 1
            };
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 26));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 28));
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 68F));
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 32F));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 30));

            _executionPlanSummary = new System.Windows.Forms.Label
            {
                Dock = DockStyle.Fill,
                TextAlign = ContentAlignment.MiddleLeft,
                AutoEllipsis = true
            };

            var planMenuStrip = new ToolStrip
            {
                Dock = DockStyle.Fill,
                GripStyle = ToolStripGripStyle.Hidden,
                RenderMode = ToolStripRenderMode.System,
                Padding = new Padding(0),
                Stretch = true
            };
            if (tsmiExecutionPlan.Owner != null)
                tsmiExecutionPlan.Owner.Items.Remove(tsmiExecutionPlan);
            planMenuStrip.Items.Add(tsmiExecutionPlan);
            headerLayout.Controls.Add(_executionPlanSummary, 0, 0);
            headerLayout.Controls.Add(planMenuStrip, 1, 0);

            var globalActions = CreateExecutionPlanToolStrip();
            AddExecutionPlanToolStripButton(globalActions, "New", (s, e) => CreateExecutionPlan());
            AddExecutionPlanToolStripButton(globalActions, "Load", (s, e) => LoadExecutionPlan());
            _executionPlanSaveButton = AddExecutionPlanToolStripButton(globalActions, "Save", (s, e) => SaveExecutionPlan());
            _executionPlanSaveAsButton = AddExecutionPlanToolStripButton(globalActions, "Save As", (s, e) => SaveExecutionPlanAs());
            _executionPlanValidateButton = AddExecutionPlanToolStripButton(globalActions, "Validate", (s, e) => ValidateExecutionPlan());
            _executionPlanExecuteButton = AddExecutionPlanToolStripButton(globalActions, "Execute Plan", (s, e) => ExecuteExecutionPlan());

            _executionPlanSteps = new ListView
            {
                Dock = DockStyle.Fill,
                CheckBoxes = true,
                FullRowSelect = true,
                HideSelection = false,
                MultiSelect = false,
                View = View.Details
            };
            _executionPlanSteps.Columns.Add("#", 34);
            _executionPlanSteps.Columns.Add("Status", 76);
            _executionPlanSteps.Columns.Add("Environment", 110);
            _executionPlanSteps.Columns.Add("Step", 190);
            _executionPlanSteps.Columns.Add("Input/Output", 180);
            _executionPlanStepContextMenu = BuildExecutionPlanStepContextMenu();
            _executionPlanSteps.ContextMenuStrip = _executionPlanStepContextMenu;
            _executionPlanSteps.ItemChecked += ExecutionPlanStepChecked;
            _executionPlanSteps.SelectedIndexChanged += (sender, args) =>
            {
                SelectExecutionPlanStepContext();
                RenderExecutionPlanRowTargetEditors();
                RenderExecutionPlanMessages();
                RenderExecutionPlanActionState();
            };
            _executionPlanSteps.MouseClick += (sender, args) =>
            {
                if (args.Button == MouseButtons.Right)
                    SelectExecutionPlanStepAt(args.Location);
                RenderExecutionPlanRowTargetEditors();
                RenderExecutionPlanActionState();
            };
            _executionPlanSteps.MouseWheel += (sender, args) => QueueRenderExecutionPlanRowTargetEditors();
            _executionPlanSteps.KeyDown += (sender, args) => QueueRenderExecutionPlanRowTargetEditors();
            _executionPlanSteps.Resize += (sender, args) =>
            {
                ResizeExecutionPlanColumns();
                QueueRenderExecutionPlanRowTargetEditors();
            };

            _executionPlanMessages = new TextBox
            {
                Dock = DockStyle.Fill,
                Multiline = true,
                ReadOnly = true,
                ScrollBars = ScrollBars.Vertical
            };

            var stepActions = CreateExecutionPlanToolStrip();
            _executionPlanPreviewStepButton = AddExecutionPlanToolStripButton(stepActions, "Preview", (s, e) => PreviewSelectedExecutionPlanStep());
            _executionPlanConfigureStepButton = AddExecutionPlanToolStripButton(stepActions, "Configure", (s, e) => ReconfigureSelectedExecutionPlanStep());
            _executionPlanExecuteStepButton = AddExecutionPlanToolStripButton(stepActions, "Execute Step", (s, e) => ExecuteSelectedExecutionPlanStep());
            stepActions.Items.Add(new ToolStripSeparator());
            _executionPlanMoveStepUpButton = AddExecutionPlanToolStripButton(stepActions, "Move Up", (s, e) => MoveSelectedExecutionPlanStep(-1));
            _executionPlanMoveStepDownButton = AddExecutionPlanToolStripButton(stepActions, "Move Down", (s, e) => MoveSelectedExecutionPlanStep(1));
            _executionPlanRemoveStepButton = AddExecutionPlanToolStripButton(stepActions, "Remove", (s, e) => RemoveSelectedExecutionPlanStep());

            layout.Controls.Add(headerLayout, 0, 0);
            layout.Controls.Add(globalActions, 0, 1);
            layout.Controls.Add(_executionPlanSteps, 0, 2);
            layout.Controls.Add(_executionPlanMessages, 0, 3);
            layout.Controls.Add(stepActions, 0, 4);
            _executionPlanGroup.Controls.Add(layout);

            _executionPlanSplitContainer.Panel2.Controls.Add(_executionPlanGroup);
            pnlMain.ResumeLayout();
            System.Action applySplitter = () =>
            {
                if (_executionPlanSplitContainer == null || _executionPlanSplitContainer.Width <= 0) return;
                SetExecutionPlanPanelWidthRatio(0.30m);
                ResizeExecutionPlanColumns();
            };
            if (IsHandleCreated)
                BeginInvoke(applySplitter);
            else
                HandleCreated += (s, e) => BeginInvoke(applySplitter);
            RenderExecutionPlanPanel();
        }

        private void SetExecutionPlanPanelWidthRatio(decimal ratio)
        {
            if (_executionPlanSplitContainer == null || _executionPlanSplitContainer.Width <= 0) return;

            ratio = Math.Max(0.25m, Math.Min(0.50m, ratio));
            var total = _executionPlanSplitContainer.Width - _executionPlanSplitContainer.SplitterWidth;
            if (total <= 0) return;

            var desiredPlanWidth = (int)Math.Round(total * ratio);
            _executionPlanSplitContainer.SplitterDistance = Math.Max(1, total - desiredPlanWidth);
        }

        private void EnforceExecutionPlanPanelWidth()
        {
            if (_executionPlanSplitContainer == null || _executionPlanSplitContainer.Width <= 0) return;

            var total = _executionPlanSplitContainer.Width - _executionPlanSplitContainer.SplitterWidth;
            if (total <= 0) return;

            var planWidth = _executionPlanSplitContainer.Panel2.Width;
            var min = (int)Math.Round(total * 0.25m);
            var max = (int)Math.Round(total * 0.50m);
            if (planWidth < min)
                _executionPlanSplitContainer.SplitterDistance = total - min;
            else if (planWidth > max)
                _executionPlanSplitContainer.SplitterDistance = total - max;

            ResizeExecutionPlanColumns();
        }

        private void ResizeExecutionPlanColumns()
        {
            if (_executionPlanSteps == null || _executionPlanSteps.Columns.Count < 5) return;

            var width = Math.Max(360, _executionPlanSteps.ClientSize.Width - SystemInformation.VerticalScrollBarWidth - 4);
            _executionPlanSteps.Columns[0].Width = 38;
            _executionPlanSteps.Columns[1].Width = 80;
            _executionPlanSteps.Columns[2].Width = Math.Max(180, (int)(width * 0.25));
            _executionPlanSteps.Columns[3].Width = Math.Max(120, (int)(width * 0.20));
            _executionPlanSteps.Columns[4].Width = Math.Max(90, width - _executionPlanSteps.Columns[0].Width - _executionPlanSteps.Columns[1].Width - _executionPlanSteps.Columns[2].Width - _executionPlanSteps.Columns[3].Width);
        }

        private ToolStrip CreateExecutionPlanToolStrip()
        {
            return new ToolStrip
            {
                Dock = DockStyle.Fill,
                GripStyle = ToolStripGripStyle.Hidden,
                RenderMode = ToolStripRenderMode.System,
                Padding = new Padding(0),
                Stretch = true
            };
        }

        private ToolStripButton AddExecutionPlanToolStripButton(ToolStrip strip, string text, EventHandler click)
        {
            var button = new ToolStripButton(text)
            {
                DisplayStyle = ToolStripItemDisplayStyle.Text,
                AutoSize = true
            };
            button.Click += click;
            strip.Items.Add(button);
            return button;
        }

        private ContextMenuStrip BuildExecutionPlanStepContextMenu()
        {
            var menu = new ContextMenuStrip();
            menu.Items.Add("Preview", null, (s, e) => PreviewSelectedExecutionPlanStep());
            menu.Items.Add("Configure", null, (s, e) => ReconfigureSelectedExecutionPlanStep());
            menu.Items.Add("Execute Step", null, (s, e) => ExecuteSelectedExecutionPlanStep());
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add("Move Up", null, (s, e) => MoveSelectedExecutionPlanStep(-1));
            menu.Items.Add("Move Down", null, (s, e) => MoveSelectedExecutionPlanStep(1));
            menu.Items.Add("Remove", null, (s, e) => RemoveSelectedExecutionPlanStep());
            menu.Opening += (s, e) =>
            {
                RenderExecutionPlanActionState();
                e.Cancel = GetSelectedExecutionPlanStep() == null;
            };
            return menu;
        }

        private void SelectExecutionPlanStepAt(Point location)
        {
            if (_executionPlanSteps == null) return;
            var item = _executionPlanSteps.GetItemAt(location.X, location.Y);
            if (item == null) return;

            item.Selected = true;
            item.Focused = true;
        }

        private sealed class ExecutionPlanTargetOption
        {
            public string UniqueName { get; set; }
            public string FriendlyName { get; set; }
            public string DisplayName { get; set; }
            public override string ToString()
            {
                if (!string.IsNullOrWhiteSpace(DisplayName)) return DisplayName;
                return string.IsNullOrWhiteSpace(FriendlyName) ? UniqueName : FriendlyName;
            }
        }

        private ExecutionPlanStep GetSelectedExecutionPlanStep()
        {
            return _executionPlanSteps?.SelectedItems.Count > 0
                ? _executionPlanSteps.SelectedItems[0].Tag as ExecutionPlanStep
                : null;
        }

        private void SelectExecutionPlanStepContext()
        {
            var step = GetSelectedExecutionPlanStep();
            if (step == null || string.IsNullOrWhiteSpace(step.Table?.LogicalName) || _tables == null) return;

            try
            {
                var settingsPath = step.SettingsProvenance?.SettingsFilePath;
                if (!string.IsNullOrWhiteSpace(settingsPath) && File.Exists(settingsPath))
                {
                    var settings = DmtFileService.Load(settingsPath);
                    if (string.Equals(settings?.Table?.LogicalName, step.Table.LogicalName, StringComparison.OrdinalIgnoreCase))
                    {
                        ApplyDmtFileAndSelectTable(settingsPath, settings, false, false);
                        SendMessageToStatusBar?.Invoke(this, new StatusBarMessageEventArgs($"Loaded step settings: {Path.GetFileName(settingsPath)}"));
                        return;
                    }

                    _logger?.Log(
                        LogLevel.WARN,
                        $"Plan step '{step.Name}' references settings file '{settingsPath}', but the file is for table '{settings?.Table?.LogicalName}'. Loading the plan snapshot instead.");
                }

                ApplyExecutionPlanStepSnapshotToMainContext(step);
            }
            catch (Exception ex)
            {
                _logger?.Log(LogLevel.WARN, $"Could not load plan step context: {ex.Message}");
                SendMessageToStatusBar?.Invoke(this, new StatusBarMessageEventArgs($"Could not load step context: {ex.Message}"));
            }
        }

        private void ApplyExecutionPlanStepSnapshotToMainContext(ExecutionPlanStep step)
        {
            var tableData = GetTableDataByLogicalName(step.Table.LogicalName, false);
            EnsureTableDataAttributes(tableData);

            var settings = CreateDmtSettingsFromExecutionPlanStep(step, tableData);

            _suppressTableSelectionChanged = true;
            try
            {
                EnsureTableVisible(tableData.Table.LogicalName);
                SetSelectedTableItem(tableData);
            }
            finally
            {
                _suppressTableSelectionChanged = false;
            }

            _dmtFilePath = null;
            ApplyDmtSettingsToCurrentTable(settings);
            _previousTableLogicalName = tableData.Table.LogicalName;
            LoadAttributes();
            LoadFilters(null);
            SendMessageToStatusBar?.Invoke(this, new StatusBarMessageEventArgs($"Loaded step snapshot for table: {tableData.Table.LogicalName}"));
        }

        private void EnsureTableVisible(string logicalName)
        {
            if (lvTables.Items.Cast<ListViewItem>().Any(item => item.SubItems[0].Text.Equals(logicalName, StringComparison.OrdinalIgnoreCase)))
                return;

            if (!string.IsNullOrWhiteSpace(txtTableFilter.Text))
            {
                txtTableFilter.Text = string.Empty;
                lvTables.Items.Clear();
                LoadTablesList();
            }
        }

        private DmtSettings CreateDmtSettingsFromExecutionPlanStep(ExecutionPlanStep step, TableData tableData)
        {
            var selected = new HashSet<string>(
                step.Snapshot?.SelectedAttributes ?? new List<string>(),
                StringComparer.OrdinalIgnoreCase);

            var deselected = selected.Count > 0 && tableData.Table.AllAttributes != null
                ? tableData.Table.AllAttributes
                    .Where(attr => !selected.Contains(attr.LogicalName))
                    .Select(attr => attr.LogicalName)
                    .ToList()
                : null;

            var importSettings = step.Snapshot?.ImportMatchKeySelection != null
                ? new DmtImportSettings
                {
                    MatchKeyMode = step.Snapshot.ImportMatchKeySelection.Mode,
                    MatchKeyFields = step.Snapshot.ImportMatchKeySelection.Fields != null ? new List<string>(step.Snapshot.ImportMatchKeySelection.Fields) : new List<string>(),
                    MatchAlternateKeyName = step.Snapshot.ImportMatchKeySelection.AlternateKeyName,
                    BatchSize = Math.Min((step.Snapshot.ImportSettings ?? step.Snapshot.ExportSettings)?.BatchSize ?? 25, 25)
                }
                : new DmtImportSettings
                {
                    BatchSize = Math.Min((step.Snapshot?.ImportSettings ?? step.Snapshot?.ExportSettings)?.BatchSize ?? 25, 25)
                };

            return new DmtSettings
            {
                Environment = _sourceClient == null
                    ? null
                    : new DmtEnvironmentInfo
                    {
                        UniqueName = _sourceClient.ConnectedOrgUniqueName,
                        FriendlyName = _sourceClient.ConnectedOrgFriendlyName
                    },
                Table = new DmtTableInfo
                {
                    LogicalName = tableData.Table.LogicalName,
                    DisplayName = tableData.Table.DisplayName,
                    PrimaryIdAttribute = tableData.Table.IdAttribute,
                    PrimaryNameAttribute = tableData.Table.NameAttribute
                },
                DeselectedAttributes = deselected,
                Filter = step.Snapshot?.Filter,
                Mappings = ExecutionPlanService.CloneMappings(step.Snapshot?.Mappings ?? new List<Mapping>()),
                ExcelConfig = ExecutionPlanService.CloneExcelConfig(step.Snapshot?.ExcelConfig),
                ImportSettings = importSettings
            };
        }

        private void RefreshExecutionPlanStepMappingsForTarget(ExecutionPlanStep step)
        {
            if (step?.Snapshot == null) return;

            var settings = (step.Operation ?? string.Empty).StartsWith("Import", StringComparison.OrdinalIgnoreCase)
                ? step.Snapshot.ImportSettings
                : step.Snapshot.ExportSettings;
            if (settings == null) return;

            var previousClientOverride = _executionTargetClientOverride;
            var previousInstanceOverride = _executionTargetInstanceOverride;
            try
            {
                if (TrySetExecutionTargetOverride(step, out _))
                    step.Snapshot.Mappings = ExecutionPlanService.CloneMappings(BuildMappingsForImport(settings));
            }
            finally
            {
                _executionTargetClientOverride = previousClientOverride;
                _executionTargetInstanceOverride = previousInstanceOverride;
            }
        }

        private List<Mapping> BuildMappingsForStepTarget(ExecutionPlanStep step, UiSettings settings)
        {
            var previousClientOverride = _executionTargetClientOverride;
            var previousInstanceOverride = _executionTargetInstanceOverride;
            try
            {
                SetExecutionTargetOverride(step?.TargetEnvironment);
                return BuildMappingsForImport(settings);
            }
            finally
            {
                _executionTargetClientOverride = previousClientOverride;
                _executionTargetInstanceOverride = previousInstanceOverride;
            }
        }

        private void RenderExecutionPlanRowTargetEditors()
        {
            if (_executionPlanSteps == null) return;

            _executionPlanRowTargetRenderQueued = false;
            ClearExecutionPlanRowTargetEditors();
            var targets = GetLoadedTargetEnvironments();

            _suppressExecutionPlanInlineTargetChanged = true;
            foreach (ListViewItem item in _executionPlanSteps.Items)
            {
                if (!(item.Tag is ExecutionPlanStep step)) continue;
                if (item.SubItems.Count <= 2) continue;

                var bounds = item.SubItems[2].Bounds;
                if (bounds.Width <= 0 || bounds.Height <= 0) continue;
                if (bounds.Bottom < 0 || bounds.Top > _executionPlanSteps.ClientSize.Height) continue;

                var isExport = IsExportStep(step);
                var editor = new ComboBox
                {
                    DropDownStyle = ComboBoxStyle.DropDownList,
                    Bounds = new Rectangle(bounds.Left + 1, bounds.Top, Math.Max(80, bounds.Width - 2), Math.Max(22, bounds.Height + 2)),
                    Tag = step,
                    Enabled = !isExport
                };

                if (isExport)
                {
                    editor.Items.Add(new ExecutionPlanTargetOption
                    {
                        DisplayName = GetStepEnvironmentDisplay(step)
                    });
                    editor.SelectedIndex = 0;
                }
                else
                {
                    editor.Items.Add(new ExecutionPlanTargetOption
                    {
                        UniqueName = string.Empty,
                        FriendlyName = string.Empty,
                        DisplayName = targets.Any() ? "Set Environment..." : "Connect target first..."
                    });
                    foreach (var env in targets)
                    {
                        editor.Items.Add(new ExecutionPlanTargetOption
                        {
                            UniqueName = env.UniqueName,
                            FriendlyName = env.FriendlyName,
                            DisplayName = string.IsNullOrWhiteSpace(env.FriendlyName) ? env.UniqueName : env.FriendlyName
                        });
                    }

                    var selectedUniqueName = step.TargetEnvironment?.UniqueName;
                    for (var i = 0; i < editor.Items.Count; i++)
                    {
                        var option = editor.Items[i] as ExecutionPlanTargetOption;
                        if (string.Equals(option?.UniqueName, selectedUniqueName, StringComparison.OrdinalIgnoreCase))
                        {
                            editor.SelectedIndex = i;
                            break;
                        }
                    }
                    if (editor.SelectedIndex < 0)
                        editor.SelectedIndex = 0;
                }

                editor.SelectedIndexChanged += ExecutionPlanRowTargetEditorChanged;
                _executionPlanSteps.Controls.Add(editor);
                editor.BringToFront();
                _executionPlanRowTargetEditors.Add(editor);
            }
            _suppressExecutionPlanInlineTargetChanged = false;
        }

        private void QueueRenderExecutionPlanRowTargetEditors()
        {
            if (_executionPlanRowTargetRenderQueued || _executionPlanSteps == null || _executionPlanSteps.IsDisposed)
                return;

            _executionPlanRowTargetRenderQueued = true;
            BeginInvoke(new System.Action(RenderExecutionPlanRowTargetEditors));
        }

        private void ClearExecutionPlanRowTargetEditors()
        {
            foreach (var editor in _executionPlanRowTargetEditors.ToList())
            {
                editor.SelectedIndexChanged -= ExecutionPlanRowTargetEditorChanged;
                _executionPlanSteps?.Controls.Remove(editor);
                editor.Dispose();
            }
            _executionPlanRowTargetEditors.Clear();
        }

        private void ExecutionPlanRowTargetEditorChanged(object sender, EventArgs e)
        {
            if (_suppressExecutionPlanInlineTargetChanged) return;
            var editor = sender as ComboBox;
            var step = editor?.Tag as ExecutionPlanStep;
            var option = editor?.SelectedItem as ExecutionPlanTargetOption;
            if (step == null || option == null) return;
            if (string.IsNullOrWhiteSpace(option.UniqueName))
            {
                step.TargetEnvironment = null;
                RefreshExecutionPlanStepMappingsForTarget(step);
                _executionPlanValidatedForExecution = false;
                ExecutionPlanFileService.ValidatePlan(_executionPlan);
                if (!string.IsNullOrWhiteSpace(_executionPlanFilePath))
                {
                    ExecutionPlanFileService.Save(_executionPlanFilePath, _executionPlan);
                    SendMessageToStatusBar?.Invoke(this, new StatusBarMessageEventArgs($"Execution plan saved: {Path.GetFileName(_executionPlanFilePath)}"));
                }
                BeginInvoke(new System.Action(RenderExecutionPlanMenu));
                return;
            }

            step.TargetEnvironment = new DmtEnvironmentInfo
            {
                UniqueName = option.UniqueName,
                FriendlyName = option.FriendlyName
            };
            RefreshExecutionPlanStepMappingsForTarget(step);
            _executionPlanValidatedForExecution = false;
            ExecutionPlanFileService.ValidatePlan(_executionPlan);
            if (!string.IsNullOrWhiteSpace(_executionPlanFilePath))
            {
                ExecutionPlanFileService.Save(_executionPlanFilePath, _executionPlan);
                SendMessageToStatusBar?.Invoke(this, new StatusBarMessageEventArgs($"Execution plan saved: {Path.GetFileName(_executionPlanFilePath)}"));
            }
            BeginInvoke(new System.Action(RenderExecutionPlanMenu));
        }

        private void RenderExecutionPlanPanel()
        {
            if (_executionPlanSteps == null) return;

            ClearExecutionPlanRowTargetEditors();
            var selectedId = _executionPlanSteps.SelectedItems.Count > 0
                ? (_executionPlanSteps.SelectedItems[0].Tag as ExecutionPlanStep)?.Id
                : null;

            _executionPlanSteps.BeginUpdate();
            _executionPlanSteps.SuspendLayout();
            try
            {
                _suppressExecutionPlanStepChecked = true;
                _executionPlanSteps.Items.Clear();
                if (_executionPlan?.Steps != null)
                {
                    for (var i = 0; i < _executionPlan.Steps.Count; i++)
                    {
                        var step = _executionPlan.Steps[i];
                        var item = new ListViewItem((i + 1).ToString("00"))
                        {
                            Checked = step.Enabled,
                            Tag = step
                        };
                        item.SubItems.Add(step.Validation?.Status ?? "Unknown");
                        item.SubItems.Add(GetStepEnvironmentDisplay(step));
                        item.SubItems.Add(step.Name ?? GetOperationDisplayName(step.Operation));
                        item.SubItems.Add(GetExecutionPlanStepInputOutputText(step));
                        if (string.Equals(step.Validation?.Status, "Error", StringComparison.OrdinalIgnoreCase))
                            item.ForeColor = Color.DarkRed;
                        else if (string.Equals(step.Validation?.Status, "Warning", StringComparison.OrdinalIgnoreCase))
                            item.ForeColor = Color.DarkGoldenrod;
                        _executionPlanSteps.Items.Add(item);
                        if (!string.IsNullOrWhiteSpace(selectedId) && string.Equals(step.Id, selectedId, StringComparison.OrdinalIgnoreCase))
                            item.Selected = true;
                    }
                }
            }
            finally
            {
                _suppressExecutionPlanStepChecked = false;
                _executionPlanSteps.ResumeLayout();
                _executionPlanSteps.EndUpdate();
            }

            var hasPlan = _executionPlan != null && !string.IsNullOrWhiteSpace(_executionPlanFilePath);
            var errors = _executionPlan?.Steps?.Count(s => string.Equals(s.Validation?.Status, "Error", StringComparison.OrdinalIgnoreCase)) ?? 0;
            var warnings = _executionPlan?.Steps?.Count(s => string.Equals(s.Validation?.Status, "Warning", StringComparison.OrdinalIgnoreCase)) ?? 0;
            _executionPlanGroup.Text = hasPlan ? $"Execution Plan - {GetExecutionPlanDisplayName(_executionPlanFilePath)}" : "Execution Plan";
            _executionPlanSummary.Text = hasPlan
                ? $"{_executionPlan.Steps.Count} step(s), {errors} error(s), {warnings} warning(s)"
                : "No active plan";
            RenderExecutionPlanActionState();
            RenderExecutionPlanRowTargetEditors();
            RenderExecutionPlanMessages();
        }

        private void RenderExecutionPlanActionState()
        {
            var hasPlan = _executionPlan != null && !string.IsNullOrWhiteSpace(_executionPlanFilePath);
            var selectedStep = GetSelectedExecutionPlanStep();
            var selectedIndex = selectedStep == null || _executionPlan?.Steps == null
                ? -1
                : _executionPlan.Steps.FindIndex(s => string.Equals(s.Id, selectedStep.Id, StringComparison.OrdinalIgnoreCase));
            var hasSelectedStep = selectedStep != null && selectedIndex >= 0;
            var canMoveUp = hasSelectedStep && selectedIndex > 0 && ExecutionPlanService.CanMoveStep(_executionPlan, selectedStep, selectedIndex - 1, out _);
            var canMoveDown = hasSelectedStep && selectedIndex < _executionPlan.Steps.Count - 1 && ExecutionPlanService.CanMoveStep(_executionPlan, selectedStep, selectedIndex + 1, out _);
            var canPreviewStep = CanPreviewSelectedExecutionPlanStep(selectedStep);
            var canExecuteStep = CanExecuteSelectedExecutionPlanStep(selectedStep);

            if (_executionPlanSaveButton != null) _executionPlanSaveButton.Enabled = hasPlan;
            if (_executionPlanSaveAsButton != null) _executionPlanSaveAsButton.Enabled = _executionPlan != null;
            if (_executionPlanValidateButton != null) _executionPlanValidateButton.Enabled = hasPlan;
            if (_executionPlanExecuteButton != null) _executionPlanExecuteButton.Enabled = CanExecuteValidatedExecutionPlan();

            if (_executionPlanPreviewStepButton != null) _executionPlanPreviewStepButton.Enabled = canPreviewStep;
            if (_executionPlanConfigureStepButton != null) _executionPlanConfigureStepButton.Enabled = canPreviewStep && (selectedStep.Operation ?? string.Empty).StartsWith("Import", StringComparison.OrdinalIgnoreCase);
            if (_executionPlanExecuteStepButton != null) _executionPlanExecuteStepButton.Enabled = canExecuteStep;
            if (_executionPlanMoveStepUpButton != null) _executionPlanMoveStepUpButton.Enabled = canMoveUp;
            if (_executionPlanMoveStepDownButton != null) _executionPlanMoveStepDownButton.Enabled = canMoveDown;
            if (_executionPlanRemoveStepButton != null) _executionPlanRemoveStepButton.Enabled = hasSelectedStep;

            if (_executionPlanStepContextMenu != null)
            {
                SetContextMenuItemEnabled("Preview", canPreviewStep);
                SetContextMenuItemEnabled("Configure", _executionPlanConfigureStepButton?.Enabled == true);
                SetContextMenuItemEnabled("Execute Step", canExecuteStep);
                SetContextMenuItemEnabled("Move Up", canMoveUp);
                SetContextMenuItemEnabled("Move Down", canMoveDown);
                SetContextMenuItemEnabled("Remove", hasSelectedStep);
            }
        }

        private void SetContextMenuItemEnabled(string text, bool enabled)
        {
            var item = _executionPlanStepContextMenu?.Items
                .Cast<ToolStripItem>()
                .FirstOrDefault(menuItem => string.Equals(menuItem.Text, text, StringComparison.OrdinalIgnoreCase));
            if (item != null)
                item.Enabled = enabled;
        }

        private bool CanExecuteSelectedExecutionPlanStep(ExecutionPlanStep step)
        {
            if (_executionPlan == null || step == null || _working)
                return false;
            if (string.Equals(step.Validation?.Status, "Error", StringComparison.OrdinalIgnoreCase))
                return false;
            if (!ExecutionPlanService.TryValidateTargetConnection(step, _targetClients.Keys, _targetClient != null, out _))
                return false;
            if ((step.Operation ?? string.Empty).StartsWith("Import", StringComparison.OrdinalIgnoreCase))
            {
                var stepIndex = _executionPlan.Steps.FindIndex(s => string.Equals(s.Id, step.Id, StringComparison.OrdinalIgnoreCase)) + 1;
                var path = ResolveExecutionStepPath(step, stepIndex);
                return !string.IsNullOrWhiteSpace(path) && File.Exists(path);
            }

            return true;
        }

        private bool CanPreviewSelectedExecutionPlanStep(ExecutionPlanStep step)
        {
            if (_executionPlan == null || step == null || _working)
                return false;
            if (!ExecutionPlanService.TryValidateTargetConnection(step, _targetClients.Keys, _targetClient != null, out _))
                return false;
            if ((step.Operation ?? string.Empty).StartsWith("Import", StringComparison.OrdinalIgnoreCase))
            {
                var stepIndex = _executionPlan.Steps.FindIndex(s => string.Equals(s.Id, step.Id, StringComparison.OrdinalIgnoreCase)) + 1;
                var path = ResolveExecutionStepPath(step, stepIndex);
                return !string.IsNullOrWhiteSpace(path) && File.Exists(path);
            }

            return true;
        }

        private bool CanExecuteValidatedExecutionPlan()
        {
            return ExecutionPlanService.CanExecuteValidatedPlan(_executionPlan, _executionPlanValidatedForExecution);
        }

        private void InvalidateExecutionPlanValidation()
        {
            _executionPlanValidatedForExecution = false;
            RenderExecutionPlanMenu();
        }

        private string GetExecutionPlanStepInputOutputText(ExecutionPlanStep step)
        {
            return ExecutionPlanService.GetStepInputOutputText(_executionPlan, step);
        }

        private bool IsExportStep(ExecutionPlanStep step)
        {
            return ExecutionPlanService.IsExportStep(step);
        }

        private string GetStepEnvironmentDisplay(ExecutionPlanStep step)
        {
            if (IsExportStep(step))
            {
                var source = _executionPlan?.SourceEnvironment;
                var sourceName = source?.FriendlyName ?? source?.UniqueName ?? _sourceClient?.ConnectedOrgFriendlyName ?? _sourceClient?.ConnectedOrgUniqueName;
                return string.IsNullOrWhiteSpace(sourceName) ? string.Empty : $"Source: {sourceName}";
            }

            return GetStepTargetDisplay(step);
        }

        private string GetStepTargetDisplay(ExecutionPlanStep step)
        {
            var env = step?.TargetEnvironment;
            if (env == null || string.IsNullOrWhiteSpace(env.UniqueName))
                return _targetClient?.ConnectedOrgFriendlyName ?? _targetClient?.ConnectedOrgUniqueName ?? string.Empty;
            return env.FriendlyName ?? env.UniqueName;
        }

        private void RenderExecutionPlanMessages()
        {
            if (_executionPlanMessages == null) return;
            var step = _executionPlanSteps.SelectedItems.Count > 0 ? _executionPlanSteps.SelectedItems[0].Tag as ExecutionPlanStep : null;
            if (step == null)
            {
                _executionPlanMessages.Text = "Select a step to view details.";
                return;
            }

            var preview = step.Validation?.Preview;
            var lines = new List<string>
            {
                $"{step.Operation} - {step.Table?.LogicalName}",
                $"Environment: {GetStepEnvironmentDisplay(step)}",
                GetExecutionPlanStepInputOutputText(step)
            };
            if (preview != null)
                lines.Add($"Preview: {preview.Creates} create, {preview.Updates} update, {preview.Skips} skip, {preview.Warnings} warning(s)");

            var messages = step.Validation?.Messages ?? new List<ExecutionPlanValidationMessage>();
            lines.AddRange(messages.Select(m => $"{m.Severity}: {m.Message}"));
            _executionPlanMessages.Text = string.Join(Environment.NewLine, lines.Where(line => !string.IsNullOrWhiteSpace(line)));
        }

        private void ExecutionPlanStepChecked(object sender, ItemCheckedEventArgs e)
        {
            if (_suppressExecutionPlanStepChecked) return;
            if (!(e.Item.Tag is ExecutionPlanStep step)) return;

            step.Enabled = e.Item.Checked;
            ExecutionPlanFileService.ValidatePlan(_executionPlan);
            _executionPlanValidatedForExecution = false;
            AutoSaveExecutionPlan(true);
        }

        private void MoveSelectedExecutionPlanStep(int direction)
        {
            if (_executionPlan?.Steps == null || _executionPlanSteps.SelectedItems.Count == 0) return;
            var index = _executionPlanSteps.SelectedItems[0].Index;
            var newIndex = index + direction;
            if (newIndex < 0 || newIndex >= _executionPlan.Steps.Count) return;

            var step = _executionPlan.Steps[index];
            if (!CanMoveExecutionPlanStep(step, newIndex)) return;

            _executionPlan.Steps.RemoveAt(index);
            _executionPlan.Steps.Insert(newIndex, step);
            ExecutionPlanFileService.ValidatePlan(_executionPlan);
            _executionPlanValidatedForExecution = false;
            AutoSaveExecutionPlan(true);
            if (newIndex < _executionPlanSteps.Items.Count)
                _executionPlanSteps.Items[newIndex].Selected = true;
        }

        private bool CanMoveExecutionPlanStep(ExecutionPlanStep step, int newIndex)
        {
            var canMove = ExecutionPlanService.CanMoveStep(_executionPlan, step, newIndex, out var reason);
            if (!canMove)
                MessageBox.Show(reason, "Execution Plan", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return canMove;
        }

        private void RemoveSelectedExecutionPlanStep()
        {
            if (_executionPlan?.Steps == null || _executionPlanSteps.SelectedItems.Count == 0) return;
            var index = _executionPlanSteps.SelectedItems[0].Index;
            var step = _executionPlan.Steps[index];
            var dependents = _executionPlan.Steps
                .Where(s => string.Equals(s.Input?.Mode, "FromStepOutput", StringComparison.OrdinalIgnoreCase)
                    && string.Equals(s.Input?.SourceStepId, step.Id, StringComparison.OrdinalIgnoreCase))
                .ToList();
            var message = dependents.Any()
                ? "Remove this step and unlink dependent import step(s)?"
                : "Remove selected step from this plan?";
            if (MessageBox.Show(message, "Execution Plan", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;

            foreach (var dependent in dependents)
            {
                dependent.Input.Mode = "File";
                dependent.Input.SourceStepId = null;
            }
            _executionPlan.Steps.RemoveAt(index);
            ExecutionPlanFileService.ValidatePlan(_executionPlan);
            _executionPlanValidatedForExecution = false;
            AutoSaveExecutionPlan(true);
        }

        private void PreviewSelectedExecutionPlanStep()
        {
            var step = GetSelectedExecutionPlanStep();
            if (step == null)
            {
                MessageBox.Show("Select a step to preview.", "Execution Plan", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            var stepIndex = _executionPlan.Steps.FindIndex(s => string.Equals(s.Id, step.Id, StringComparison.OrdinalIgnoreCase)) + 1;
            ManageWorkingState(true, $"Previewing {step.Name}...");
            WorkAsync(new WorkAsyncInfo
            {
                AsyncArgument = new { step, stepIndex },
                IsCancelable = true,
                Work = (worker, evt) =>
                {
                    dynamic args = evt.Argument;
                    var previewStep = args.step as ExecutionPlanStep;
                    var index = (int)args.stepIndex;
                    if (!TrySetExecutionTargetOverride(previewStep, out var targetError))
                        throw new Exception(targetError);
                    try
                    {
                        if ((previewStep.Operation ?? string.Empty).StartsWith("Import", StringComparison.OrdinalIgnoreCase))
                        {
                            var path = ResolveExecutionStepPath(previewStep, index);
                            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
                                throw new FileNotFoundException("Import input file was not found.", path);
                            evt.Result = BuildExecutionPlanImportPreview(previewStep, path, worker);
                            return;
                        }

                        var tableData = BuildTableDataForExecutionStep(previewStep);
                        var logic = new DataLogic(worker, _sourceClient, ActiveTargetClient);
                        evt.Result = logic.Preview(tableData, previewStep.Snapshot.ExportSettings ?? GetDefaultImportSettings(Enums.Action.Preview), false);
                    }
                    finally
                    {
                        ClearExecutionTargetOverride();
                    }
                },
                PostWorkCallBack = evt =>
                {
                    ManageWorkingState(false);
                    if (evt.Error != null)
                    {
                        _logger.Log(LogLevel.ERROR, evt.Error.ToString());
                        MessageBox.Show(evt.Error.Message, "Step Preview", MessageBoxButtons.OK, MessageBoxIcon.Error);
                        return;
                    }

                    if (evt.Result is ExcelImportPreview importPreview)
                    {
                        using (var dlg = new ExcelImportPreviewDialog(importPreview, "Close", null, true))
                            dlg.ShowDialog(ParentForm);
                        return;
                    }

                    if (evt.Result is OperationResult result)
                    {
                        var tableData = BuildTableDataForExecutionStep(step);
                        var columns = tableData.SelectedAttributes.Select(attr => attr.LogicalName).ToList();
                        using (var dlg = new Results(result.Items, _settings, extraColumns: columns))
                            dlg.ShowDialog(ParentForm);
                    }
                },
                ProgressChanged = ReportWorkProgress
            });
        }

        private void ReconfigureSelectedExecutionPlanStep()
        {
            var step = GetSelectedExecutionPlanStep();
            if (step == null)
            {
                MessageBox.Show("Select a step to configure.", "Execution Plan", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            if (!(step.Operation ?? string.Empty).StartsWith("Import", StringComparison.OrdinalIgnoreCase))
            {
                using (var dlg = new ExecutionPlanStepEditDialog(step))
                {
                    if (dlg.ShowDialog(ParentForm) != DialogResult.OK) return;
                }
                step.Snapshot.Mappings = ExecutionPlanService.CloneMappings(BuildMappingsForStepTarget(step, step.Snapshot.ExportSettings ?? GetDefaultImportSettings(Enums.Action.None)));
                _executionPlanValidatedForExecution = false;
                ExecutionPlanFileService.ValidatePlan(_executionPlan);
                AutoSaveExecutionPlan(true);
                return;
            }

            var stepIndex = _executionPlan.Steps.FindIndex(s => string.Equals(s.Id, step.Id, StringComparison.OrdinalIgnoreCase)) + 1;
            ManageWorkingState(true, $"Loading {step.Name} configuration...");
            WorkAsync(new WorkAsyncInfo
            {
                AsyncArgument = new { step, stepIndex },
                IsCancelable = true,
                Work = (worker, evt) =>
                {
                    dynamic args = evt.Argument;
                    var configureStep = args.step as ExecutionPlanStep;
                    var index = (int)args.stepIndex;
                    if (!TrySetExecutionTargetOverride(configureStep, out var targetError))
                        throw new Exception(targetError);
                    try
                    {
                        var path = ResolveExecutionStepPath(configureStep, index);
                        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
                            throw new FileNotFoundException("Import input file was not found.", path);
                        evt.Result = BuildExecutionPlanImportPreview(configureStep, path, worker);
                    }
                    finally
                    {
                        ClearExecutionTargetOverride();
                    }
                },
                PostWorkCallBack = evt =>
                {
                    ManageWorkingState(false);
                    if (evt.Error != null)
                    {
                        _logger.Log(LogLevel.ERROR, evt.Error.ToString());
                        MessageBox.Show(evt.Error.Message, "Configure Step", MessageBoxButtons.OK, MessageBoxIcon.Error);
                        return;
                    }

                    var preview = evt.Result as ExcelImportPreview;
                    if (preview == null) return;
                    using (var dlg = new ExcelImportPreviewDialog(preview, "Apply", () => ConfigureMappingsForExecutionTarget(step.TargetEnvironment)))
                    {
                        var result = dlg.ShowDialog(ParentForm);
                        if (result == DialogResult.Retry)
                        {
                            ApplyImportStepConfiguration(step, dlg.Settings, dlg.SelectedMatchKey, null);
                            ReconfigureSelectedExecutionPlanStep();
                            return;
                        }

                        if (result != DialogResult.OK && result != DialogResult.Yes) return;
                        ApplyImportStepConfiguration(step, dlg.Settings, dlg.SelectedMatchKey, preview);
                        _executionPlanValidatedForExecution = false;
                        AutoSaveExecutionPlan(true);
                    }
                },
                ProgressChanged = ReportWorkProgress
            });
        }

        private void ApplyImportStepConfiguration(ExecutionPlanStep step, UiSettings settings, ExcelImportMatchKeySelection matchKey, ExcelImportPreview preview)
        {
            if (step?.Snapshot == null) return;
            step.Snapshot.ImportSettings = settings;
            step.Snapshot.ImportMatchKeySelection = ExecutionPlanService.CloneImportMatchKeySelection(matchKey);
            if (step.Snapshot.ExcelConfig != null)
                ApplyImportMatchKeySelection(step.Snapshot.ExcelConfig, matchKey);
            step.Snapshot.Mappings = ExecutionPlanService.CloneMappings(BuildMappingsForStepTarget(step, settings));
            if (preview != null)
                step.Validation.Preview = ExecutionPlanService.ToPreviewSummary(preview, "Captured preview", false, false);
            ExecutionPlanFileService.ValidatePlan(_executionPlan);
            RenderExecutionPlanMenu();
        }

        private void ValidateExecutionPlan()
        {
            if (!EnsureExecutionPlanLoaded()) return;
            UpdateExecutionPlanTargetEnvironments();

            ManageWorkingState(true, "Validating execution plan...");
            WorkAsync(new WorkAsyncInfo
            {
                AsyncArgument = _executionPlan,
                IsCancelable = false,
                Work = (worker, evt) =>
                {
                    var plan = evt.Argument as ExecutionPlan;
                    ValidateExecutionPlanInternal(plan, worker, true);
                    evt.Result = plan;
                },
                PostWorkCallBack = evt =>
                {
                    ManageWorkingState(false);
                    if (evt.Error != null)
                    {
                        _executionPlanValidatedForExecution = false;
                        RenderExecutionPlanMenu();
                        _logger.Log(LogLevel.ERROR, evt.Error.ToString());
                        MessageBox.Show(evt.Error.Message, "Execution Plan", MessageBoxButtons.OK, MessageBoxIcon.Error);
                        return;
                    }

                    AutoSaveExecutionPlan(true);

                    var errors = _executionPlan.Steps.Count(s => string.Equals(s.Validation?.Status, "Error", StringComparison.OrdinalIgnoreCase));
                    var warnings = _executionPlan.Steps.Count(s => string.Equals(s.Validation?.Status, "Warning", StringComparison.OrdinalIgnoreCase));
                    _executionPlanValidatedForExecution = !_executionPlan.Steps.Any(s => s.Enabled && string.Equals(s.Validation?.Status, "Error", StringComparison.OrdinalIgnoreCase));
                    RenderExecutionPlanMenu();
                    MessageBox.Show(
                        $"Validation complete.{Environment.NewLine}{Environment.NewLine}Steps: {_executionPlan.Steps.Count}{Environment.NewLine}Errors: {errors}{Environment.NewLine}Warnings: {warnings}",
                        "Execution Plan",
                        MessageBoxButtons.OK,
                        errors > 0 ? MessageBoxIcon.Error : warnings > 0 ? MessageBoxIcon.Warning : MessageBoxIcon.Information);
                    ReRenderComponents(true);
                },
                ProgressChanged = ReportWorkProgress
            });
        }

        private void ExecuteExecutionPlan()
        {
            if (!EnsureExecutionPlanLoaded()) return;

            if (!CanExecuteValidatedExecutionPlan())
            {
                MessageBox.Show("Validate the execution plan successfully before executing it.", "Execution Plan", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            ConfirmAndStartExecutionPlanRun();
        }

        private void ExecuteSelectedExecutionPlanStep()
        {
            if (!EnsureExecutionPlanLoaded()) return;

            var step = GetSelectedExecutionPlanStep();
            if (step == null)
            {
                MessageBox.Show("Select a step to execute.", "Execution Plan", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            if (!CanExecuteSelectedExecutionPlanStep(step))
            {
                MessageBox.Show("The selected step cannot be executed yet. Check its validation status, target connection, and input/output path.", "Execution Plan", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            if (string.Equals(step.Validation?.Status, "Warning", StringComparison.OrdinalIgnoreCase))
            {
                var proceed = MessageBox.Show(
                    $"Selected step has warning(s). Execute '{step.Name}' anyway?",
                    "Execution Plan",
                    MessageBoxButtons.YesNo,
                    MessageBoxIcon.Warning);
                if (proceed != DialogResult.Yes) return;
            }

            StartExecutionPlanSingleStepRun(step);
        }

        private void ConfirmAndStartExecutionPlanRun()
        {
            var errors = _executionPlan.Steps.Count(s => s.Enabled && string.Equals(s.Validation?.Status, "Error", StringComparison.OrdinalIgnoreCase));
            if (errors > 0)
            {
                MessageBox.Show("Execution plan has validation errors. Review and fix the plan before executing.", "Execution Plan", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }

            var warnings = _executionPlan.Steps.Count(s => s.Enabled && string.Equals(s.Validation?.Status, "Warning", StringComparison.OrdinalIgnoreCase));
            if (warnings > 0)
            {
                var proceed = MessageBox.Show(
                    $"Execution plan has {warnings} warning(s). Continue anyway?",
                    "Execution Plan",
                    MessageBoxButtons.YesNo,
                    MessageBoxIcon.Warning);
                if (proceed != DialogResult.Yes) return;
            }

            StartExecutionPlanRun();
        }

        private void StartExecutionPlanRun()
        {
            ManageWorkingState(true, "Executing plan...");
            WorkAsync(new WorkAsyncInfo
            {
                AsyncArgument = _executionPlan,
                IsCancelable = false,
                Work = (worker, evt) =>
                {
                    var plan = evt.Argument as ExecutionPlan;
                    var runLog = ExecutionPlanService.CreateRunLog(
                        plan,
                        _executionPlanFilePath,
                        new DmtEnvironmentInfo
                        {
                            UniqueName = _sourceClient?.ConnectedOrgUniqueName,
                            FriendlyName = _sourceClient?.ConnectedOrgFriendlyName
                        },
                        new DmtEnvironmentInfo
                        {
                            UniqueName = ActiveTargetClient?.ConnectedOrgUniqueName,
                            FriendlyName = ActiveTargetClient?.ConnectedOrgFriendlyName
                        },
                        GetLoadedTargetEnvironments());
                    var stepIndex = 0;
                    var failedStepIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                    var runtimeLookupContexts = new Dictionary<string, PlanLookupContext>(StringComparer.OrdinalIgnoreCase);
                    foreach (var step in ExecutionPlanService.GetExecutableSteps(plan))
                    {
                        stepIndex++;
                        worker.ReportProgress(0, $"Execution plan: running step {stepIndex} - {step.Name}...");
                        var stepLog = ExecutionPlanService.CreateRunStepLog(plan, step, stepIndex, CreateExecutionPlanPathContext());
                        try
                        {
                            if (ExecutionPlanService.IsBlockedByFailedDependency(step, failedStepIds))
                            {
                                ExecutionPlanService.MarkSkippedDueToFailedDependency(stepLog, step);
                                failedStepIds.Add(step.Id);
                                runLog.Steps.Add(stepLog);
                                continue;
                            }

                            if (!TrySetExecutionTargetOverride(step, out var targetError))
                                throw new Exception(targetError);
                            var result = ExecuteExecutionPlanStep(step, stepIndex, worker, runtimeLookupContexts);
                            ExecutionPlanService.ApplyExecutionResultToLog(stepLog, result);

                            if (result.ShouldStopPlan)
                            {
                                failedStepIds.Add(step.Id);
                                runLog.Steps.Add(stepLog);
                                break;
                            }
                        }
                        catch (Exception ex)
                        {
                            stepLog.Status = "Failed";
                            stepLog.Error = ex.Message;
                            stepLog.ErrorDetails = new List<string> { ex.Message };
                            stepLog.Summary = $"{step.Name}: failed - {ex.Message}";
                            failedStepIds.Add(step.Id);
                            runLog.Steps.Add(stepLog);
                            if (GetStepStopOnFatalError(plan, step))
                                break;
                            continue;
                        }
                        finally
                        {
                            ClearExecutionTargetOverride();
                        }
                        runLog.Steps.Add(stepLog);
                    }

                    runLog.CompletedOn = DateTime.UtcNow;
                    evt.Result = runLog;
                },
                PostWorkCallBack = evt =>
                {
                    ManageWorkingState(false);
                    if (evt.Error != null)
                    {
                        _logger.Log(LogLevel.ERROR, evt.Error.ToString());
                        MessageBox.Show(evt.Error.Message, "Execution Plan", MessageBoxButtons.OK, MessageBoxIcon.Error);
                        return;
                    }

                    var runLog = evt.Result as ExecutionPlanRunLog;
                    if (runLog != null)
                    {
                        SaveExecutionPlanRunLog(runLog);
                        using (var dlg = new ExecutionPlanResultsDialog(runLog))
                            dlg.ShowDialog(ParentForm);
                    }
                    SendMessageToStatusBar?.Invoke(this, new StatusBarMessageEventArgs("Execution plan complete"));
                    ReRenderComponents(true);
                },
                ProgressChanged = ReportWorkProgress
            });
        }

        private void StartExecutionPlanSingleStepRun(ExecutionPlanStep selectedStep)
        {
            ManageWorkingState(true, $"Executing {selectedStep.Name}...");
            WorkAsync(new WorkAsyncInfo
            {
                AsyncArgument = selectedStep,
                IsCancelable = false,
                Work = (worker, evt) =>
                {
                    var step = evt.Argument as ExecutionPlanStep;
                    var stepIndex = _executionPlan.Steps.FindIndex(s => string.Equals(s.Id, step.Id, StringComparison.OrdinalIgnoreCase)) + 1;
                    var runLog = ExecutionPlanService.CreateRunLog(
                        _executionPlan,
                        _executionPlanFilePath,
                        new DmtEnvironmentInfo
                        {
                            UniqueName = _sourceClient?.ConnectedOrgUniqueName,
                            FriendlyName = _sourceClient?.ConnectedOrgFriendlyName
                        },
                        new DmtEnvironmentInfo
                        {
                            UniqueName = ActiveTargetClient?.ConnectedOrgUniqueName,
                            FriendlyName = ActiveTargetClient?.ConnectedOrgFriendlyName
                        },
                        GetLoadedTargetEnvironments());
                    var stepLog = ExecutionPlanService.CreateRunStepLog(_executionPlan, step, stepIndex, CreateExecutionPlanPathContext());
                    var runtimeLookupContexts = BuildSingleStepRuntimeLookupContexts(step, stepIndex, worker);

                    try
                    {
                        worker.ReportProgress(0, $"Execution plan: running selected step {stepIndex} - {step.Name}...");
                        if (!TrySetExecutionTargetOverride(step, out var targetError))
                            throw new Exception(targetError);

                        var result = ExecuteExecutionPlanStep(step, stepIndex, worker, runtimeLookupContexts);
                        ExecutionPlanService.ApplyExecutionResultToLog(stepLog, result);
                    }
                    catch (Exception ex)
                    {
                        stepLog.Status = "Failed";
                        stepLog.Error = ex.Message;
                        stepLog.ErrorDetails = new List<string> { ex.Message };
                        stepLog.Summary = $"{step.Name}: failed - {ex.Message}";
                    }
                    finally
                    {
                        ClearExecutionTargetOverride();
                    }

                    runLog.Steps.Add(stepLog);
                    runLog.CompletedOn = DateTime.UtcNow;
                    evt.Result = runLog;
                },
                PostWorkCallBack = evt =>
                {
                    ManageWorkingState(false);
                    if (evt.Error != null)
                    {
                        _logger.Log(LogLevel.ERROR, evt.Error.ToString());
                        MessageBox.Show(evt.Error.Message, "Execution Plan", MessageBoxButtons.OK, MessageBoxIcon.Error);
                        return;
                    }

                    var runLog = evt.Result as ExecutionPlanRunLog;
                    if (runLog != null)
                    {
                        SaveExecutionPlanRunLog(runLog);
                        using (var dlg = new ExecutionPlanResultsDialog(runLog))
                            dlg.ShowDialog(ParentForm);
                    }
                    SendMessageToStatusBar?.Invoke(this, new StatusBarMessageEventArgs("Execution plan step complete"));
                    ReRenderComponents(true);
                },
                ProgressChanged = ReportWorkProgress
            });
        }

        private Dictionary<string, PlanLookupContext> BuildSingleStepRuntimeLookupContexts(ExecutionPlanStep step, int stepIndex, BackgroundWorker worker)
        {
            var contexts = new Dictionary<string, PlanLookupContext>(StringComparer.OrdinalIgnoreCase);
            if (step == null || !(step.Operation ?? string.Empty).StartsWith("Import", StringComparison.OrdinalIgnoreCase))
                return contexts;

            ISet<string> requiredLookupTables = null;
            if (string.Equals(step.Operation, "ImportFromExcel", StringComparison.OrdinalIgnoreCase))
            {
                var path = ResolveExecutionStepPath(step, stepIndex);
                var excelLogic = new Logic.ExcelLogic();
                var lookupConfig = step.Snapshot?.ExcelConfig ?? (!string.IsNullOrWhiteSpace(path) && File.Exists(path) ? excelLogic.ReadMetadata(path) : null);
                requiredLookupTables = GetPlanLookupTablesRequiredByImportConfig(lookupConfig);
            }

            var target = step.TargetEnvironment != null && _targetClients.TryGetValue(step.TargetEnvironment.UniqueName, out var selectedTarget)
                ? selectedTarget
                : ActiveTargetClient;
            var context = BuildPlanLookupContextForPriorSteps(step.TargetEnvironment, step, worker, true, target, requiredLookupTables);
            contexts[GetExecutionTargetKey(step.TargetEnvironment)] = context;
            return contexts;
        }

        private ExecutionPlanStepExecutionResult ExecuteExecutionPlanStep(
            ExecutionPlanStep step,
            int stepIndex,
            BackgroundWorker worker,
            Dictionary<string, PlanLookupContext> runtimeLookupContexts)
        {
            var tableData = BuildTableDataForExecutionStep(step);
            var path = ResolveExecutionStepPath(step, stepIndex);

            switch (step.Operation)
            {
                case "ExportToJson":
                {
                    EnsureOutputDirectory(path);
                    var logic = new DataLogic(worker, _sourceClient, ActiveTargetClient);
                    logic.Export(tableData, step.Snapshot.ExportSettings ?? GetDefaultImportSettings(Enums.Action.None), path, step.Snapshot.Mappings, false);
                    return new ExecutionPlanStepExecutionResult { Summary = $"{step.Name}: exported JSON to {path}" };
                }
                case "ExportToExcel":
                {
                    EnsureOutputDirectory(path);
                    var logic = new DataLogic(worker, _sourceClient, ActiveTargetClient);
                    var sourceCollection = logic.GetSourceEntities(tableData, step.Snapshot.ExportSettings ?? GetDefaultImportSettings(Enums.Action.None));
                    var excelLogic = new Logic.ExcelLogic();
                    excelLogic.Export(step.Snapshot.ExcelConfig, sourceCollection, path, _sourceClient);
                    var count = sourceCollection?.Count() ?? 0;
                    return new ExecutionPlanStepExecutionResult { Summary = $"{step.Name}: exported Excel ({count} record(s)) to {path}", TotalRecords = count };
                }
                case "ImportFromJson":
                {
                    var json = File.ReadAllText(path);
                    var collection = json.DeserializeObject<RecordCollection>();
                    ImportFileDataChecks(collection);
                    ApplyJsonImportMatchKeySelection(collection, tableData, step.Snapshot?.ImportMatchKeySelection);
                    var logic = new DataLogic(worker, _sourceClient, ActiveTargetClient);
                    var result = logic.Import(tableData, collection, step.Snapshot.ImportSettings ?? GetDefaultImportSettings(Enums.Action.None), step.Snapshot.Mappings, false);
                    AddImportedRecordsToPlanLookupContext(runtimeLookupContexts, step, collection, result?.SuccessfulIdMap ?? GetSuccessfulResultIdMap(result?.Items));
                    return BuildExecutionStepResult(step, result, "imported JSON");
                }
                case "ImportFromExcel":
                {
                    var excelLogic = new Logic.ExcelLogic();
                    var planLookupResolver = GetRuntimePlanLookupContext(runtimeLookupContexts, step);
                    var collection = excelLogic.ImportFromExcel(
                        path,
                        out ExcelExportConfig config,
                        ActiveTargetClient,
                        worker,
                        importConfig =>
                        {
                            if (step.Snapshot.ExcelConfig != null)
                            {
                                importConfig.MatchKey = step.Snapshot.ExcelConfig.MatchKey;
                                importConfig.MatchKeyMode = step.Snapshot.ExcelConfig.MatchKeyMode;
                                importConfig.MatchKeys = step.Snapshot.ExcelConfig.MatchKeys;
                                importConfig.MatchAlternateKeyName = step.Snapshot.ExcelConfig.MatchAlternateKeyName;
                                importConfig.ImportSettings = step.Snapshot.ExcelConfig.ImportSettings;
                            }
                            ApplyImportMatchKeySelection(importConfig, step.Snapshot?.ImportMatchKeySelection);
                            EnsureExcelImportSettings(importConfig, BuildExcelImportSettings(step.Snapshot.ImportSettings, importConfig));
                        },
                        planLookupResolver);
                    var logic = new DataLogic(worker, _sourceClient, ActiveTargetClient);
                    var result = logic.Import(tableData, collection, step.Snapshot.ImportSettings ?? GetDefaultImportSettings(config, Enums.Action.None), step.Snapshot.Mappings, false);
                    if (result != null && config != null)
                    {
                        var importedIds = result.SuccessfulIdMap ?? GetSuccessfulResultIdMap(result.Items);
                        if (importedIds.Any())
                            excelLogic.UpdateImportedGuids(path, config, collection, importedIds, worker);
                        AddImportedRecordsToPlanLookupContext(runtimeLookupContexts, step, collection, importedIds);
                    }
                    return BuildExecutionStepResult(step, result, "imported Excel");
                }
                default:
                    throw new Exception($"Unsupported execution plan operation: {step.Operation}");
            }
        }

        private void ValidateExecutionPlanInternal(ExecutionPlan plan, BackgroundWorker worker, bool includePreviewCounts)
        {
            if (plan == null) return;

            ExecutionPlanFileService.ValidatePlan(plan);
            AddEnvironmentValidationMessages(plan);
            AddDuplicateOutputPathValidationMessages(plan);

            if (includePreviewCounts)
            {
                var stepIndex = 0;
                foreach (var step in plan.Steps.Where(s => s.Enabled))
                {
                    stepIndex++;
                    worker?.ReportProgress(0, $"Execution plan: validating step {stepIndex} - {step.Name}...");
                    try
                    {
                        if (!TrySetExecutionTargetOverride(step, out var targetError))
                            AddExecutionPlanValidationMessage(step, "Error", targetError);
                        else
                        {
                            ValidateExecutionPlanStepSnapshot(step);
                            if ((step.Operation ?? string.Empty).StartsWith("Import", StringComparison.OrdinalIgnoreCase))
                                RefreshExecutionPlanImportPreview(step, stepIndex, worker);
                        }
                        RefreshExecutionPlanStepStatus(step);
                    }
                    finally
                    {
                        ClearExecutionTargetOverride();
                    }
                }
            }
            else
            {
                foreach (var step in plan.Steps.Where(s => s.Enabled))
                    RefreshExecutionPlanStepStatus(step);
            }
        }

        private void AddEnvironmentValidationMessages(ExecutionPlan plan)
        {
            var firstEnabledStep = plan.Steps.FirstOrDefault(s => s.Enabled);
            if (firstEnabledStep == null) return;

            if (EnvironmentChanged(plan.SourceEnvironment, _sourceClient?.ConnectedOrgUniqueName))
                AddExecutionPlanValidationMessage(firstEnabledStep, "Warning", "Current source environment differs from the environment captured in the plan.");

            foreach (var step in plan.Steps.Where(s => s.Enabled && s.TargetEnvironment != null && !string.IsNullOrWhiteSpace(s.TargetEnvironment.UniqueName)))
            {
                if (!_targetClients.ContainsKey(step.TargetEnvironment.UniqueName))
                    AddExecutionPlanValidationMessage(step, "Error", $"Target environment is not connected: {step.TargetEnvironment.FriendlyName ?? step.TargetEnvironment.UniqueName}");
            }
        }

        private bool EnvironmentChanged(DmtEnvironmentInfo captured, string currentUniqueName)
        {
            if (captured == null || string.IsNullOrWhiteSpace(captured.UniqueName) || string.IsNullOrWhiteSpace(currentUniqueName)) return false;
            return !captured.UniqueName.Equals(currentUniqueName, StringComparison.OrdinalIgnoreCase);
        }

        private void AddDuplicateOutputPathValidationMessages(ExecutionPlan plan)
        {
            var exports = plan.Steps
                .Select((step, index) => new { step, index })
                .Where(x => x.step.Enabled && (x.step.Operation ?? string.Empty).StartsWith("Export", StringComparison.OrdinalIgnoreCase))
                .Select(x => new { x.step, path = ResolvePlanPath(x.step.Output?.PathTemplate, x.step, x.index + 1) })
                .Where(x => !string.IsNullOrWhiteSpace(x.path))
                .GroupBy(x => x.path, StringComparer.OrdinalIgnoreCase)
                .Where(g => g.Count() > 1);

            foreach (var group in exports)
            {
                foreach (var item in group)
                    AddExecutionPlanValidationMessage(item.step, "Warning", $"Another enabled export resolves to the same output path: {group.Key}");
            }
        }

        private void ValidateExecutionPlanStepSnapshot(ExecutionPlanStep step)
        {
            try
            {
                var tableData = BuildTableDataForExecutionStep(step);
                var selected = step.Snapshot?.SelectedAttributes ?? new List<string>();
                var allAttributes = tableData.Table.AllAttributes ?? new List<Models.Attribute>();
                var missingAttributes = selected
                    .Where(name => !allAttributes.Any(attr => attr.LogicalName.Equals(name, StringComparison.OrdinalIgnoreCase)))
                    .ToList();
                foreach (var name in missingAttributes)
                    AddExecutionPlanValidationMessage(step, "Error", $"Captured attribute no longer exists on '{step.Table.LogicalName}': {name}");

                var stepTableName = step.Table?.LogicalName;
                var mappingAttributes = (step.Snapshot?.Mappings ?? new List<Mapping>())
                    .Where(mapping => string.IsNullOrWhiteSpace(mapping.TableLogicalName)
                        || string.Equals(mapping.TableLogicalName, stepTableName, StringComparison.OrdinalIgnoreCase))
                    .Select(m => m.AttributeLogicalName)
                    .Where(name => !string.IsNullOrWhiteSpace(name))
                    .Distinct(StringComparer.OrdinalIgnoreCase);
                foreach (var name in mappingAttributes.Where(name => !allAttributes.Any(attr => attr.LogicalName.Equals(name, StringComparison.OrdinalIgnoreCase))))
                    AddExecutionPlanValidationMessage(step, "Warning", $"Captured mapping references an attribute that is not in the current table metadata: {name}");
            }
            catch (Exception ex)
            {
                AddExecutionPlanValidationMessage(step, "Error", $"Table validation failed: {ex.Message}");
            }
        }

        private void RefreshExecutionPlanImportPreview(ExecutionPlanStep step, int stepIndex, BackgroundWorker worker, bool fullPreview = false)
        {
            try
            {
                var path = ResolveExecutionStepPath(step, stepIndex);
                if (!string.IsNullOrWhiteSpace(path) && File.Exists(path))
                {
                    if (!fullPreview && string.Equals(step.Operation, "ImportFromExcel", StringComparison.OrdinalIgnoreCase))
                    {
                        step.Validation.Preview = BuildLightweightExcelImportPreview(step, path, worker);
                        return;
                    }

                    var preview = BuildExecutionPlanImportPreview(step, path, worker);
                    step.Validation.Preview = ExecutionPlanService.ToPreviewSummary(preview, "Validation preview", false, false);
                    AddExecutionPlanPreviewMessages(step, preview);
                    return;
                }

                if (string.Equals(step.Input?.Mode, "FromStepOutput", StringComparison.OrdinalIgnoreCase))
                {
                    var estimate = EstimateLinkedImportPreview(step, worker);
                    if (estimate != null)
                    {
                        step.Validation.Preview = estimate;
                        AddExecutionPlanValidationMessage(step, "Info", "Linked export output does not exist yet; preview count is estimated from the export step.");
                        return;
                    }
                }

                if (!string.IsNullOrWhiteSpace(path))
                    AddExecutionPlanValidationMessage(step, "Warning", $"Import preview could not be refreshed because the input file does not exist: {path}");
            }
            catch (Exception ex)
            {
                AddExecutionPlanValidationMessage(step, "Error", $"Import preview failed: {ex.Message}");
                if (step.Validation.Preview != null)
                    step.Validation.Preview.IsStale = true;
            }
        }

        private ExecutionPlanPreviewSummary BuildLightweightExcelImportPreview(ExecutionPlanStep step, string path, BackgroundWorker worker)
        {
            worker?.ReportProgress(0, $"Execution plan: reading Excel metadata for {step.Name}...");
            var excelLogic = new Logic.ExcelLogic();
            var rows = excelLogic.GetImportRowCount(path, out _);
            return new ExecutionPlanPreviewSummary
            {
                Rows = rows,
                Source = "Estimated from Excel metadata",
                IsEstimated = true,
                IsStale = false
            };
        }

        private ExcelImportPreview BuildExecutionPlanImportPreview(ExecutionPlanStep step, string path, BackgroundWorker worker)
        {
            var tableData = BuildTableDataForExecutionStep(step);
            if (string.Equals(step.Operation, "ImportFromExcel", StringComparison.OrdinalIgnoreCase))
            {
                var excelLogic = new Logic.ExcelLogic();
                var lookupConfig = step.Snapshot?.ExcelConfig ?? excelLogic.ReadMetadata(path);
                var requiredLookupTables = GetPlanLookupTablesRequiredByImportConfig(lookupConfig);
                var planLookupResolver = BuildPlanLookupContextForPriorSteps(step.TargetEnvironment, step, worker, true, null, requiredLookupTables);
                var collection = excelLogic.ImportFromExcel(
                    path,
                    out ExcelExportConfig config,
                    ActiveTargetClient,
                    worker,
                    importConfig =>
                    {
                        if (step.Snapshot.ExcelConfig != null)
                        {
                            importConfig.MatchKey = step.Snapshot.ExcelConfig.MatchKey;
                            importConfig.MatchKeyMode = step.Snapshot.ExcelConfig.MatchKeyMode;
                            importConfig.MatchKeys = step.Snapshot.ExcelConfig.MatchKeys;
                            importConfig.MatchAlternateKeyName = step.Snapshot.ExcelConfig.MatchAlternateKeyName;
                            importConfig.ImportSettings = step.Snapshot.ExcelConfig.ImportSettings;
                        }
                        ApplyImportMatchKeySelection(importConfig, step.Snapshot?.ImportMatchKeySelection);
                        EnsureExcelImportSettings(importConfig, BuildExcelImportSettings(step.Snapshot.ImportSettings, importConfig));
                    },
                    planLookupResolver);
                return BuildExcelImportPreview(tableData, collection, config, step.Snapshot.ImportSettings ?? GetDefaultImportSettings(config, Enums.Action.None), path);
            }

            var json = File.ReadAllText(path);
            var collectionFromJson = json.DeserializeObject<RecordCollection>();
            ImportFileDataChecks(collectionFromJson);
            ApplyJsonImportMatchKeySelection(collectionFromJson, tableData, step.Snapshot?.ImportMatchKeySelection);
            return BuildExcelImportPreview(tableData, collectionFromJson, null, step.Snapshot.ImportSettings ?? GetDefaultImportSettings(Enums.Action.None), path);
        }

        private ExecutionPlanPreviewSummary EstimateLinkedImportPreview(ExecutionPlanStep importStep, BackgroundWorker worker)
        {
            var sourceStep = _executionPlan?.Steps.FirstOrDefault(s => string.Equals(s.Id, importStep.Input?.SourceStepId, StringComparison.OrdinalIgnoreCase));
            if (sourceStep == null || !(sourceStep.Operation ?? string.Empty).StartsWith("Export", StringComparison.OrdinalIgnoreCase)) return null;

            var tableData = BuildTableDataForExecutionStep(sourceStep);
            var previousClientOverride = _executionTargetClientOverride;
            var previousInstanceOverride = _executionTargetInstanceOverride;
            try
            {
                if (!TrySetExecutionTargetOverride(sourceStep, out _))
                    return null;
                var logic = new DataLogic(worker, _sourceClient, ActiveTargetClient);
                var rows = logic.GetSourceEntities(tableData, sourceStep.Snapshot.ExportSettings ?? GetDefaultImportSettings(Enums.Action.None)).Count();
                return new ExecutionPlanPreviewSummary
                {
                    Rows = rows,
                    Source = "Estimated from linked export",
                    IsEstimated = true,
                    IsStale = false
                };
            }
            finally
            {
                _executionTargetClientOverride = previousClientOverride;
                _executionTargetInstanceOverride = previousInstanceOverride;
            }
        }

        private void AddExecutionPlanPreviewMessages(ExecutionPlanStep step, ExcelImportPreview preview)
        {
            if (preview?.ImportErrors?.Any() != true) return;

            foreach (var warning in preview.ImportErrors.Take(5))
                AddExecutionPlanValidationMessage(step, "Warning", warning);
            if (preview.ImportErrors.Count > 5)
                AddExecutionPlanValidationMessage(step, "Warning", $"{preview.ImportErrors.Count - 5} additional import warning(s) are available in the preview.");
        }

        private ExecutionPlanStepExecutionResult BuildExecutionStepResult(ExecutionPlanStep step, OperationResult operationResult, string action)
        {
            var items = operationResult?.Items?.ToList() ?? new List<ListViewItem>();
            return ExecutionPlanService.BuildExecutionStepResult(
                _executionPlan,
                step,
                action,
                items.Select(GetExecutionResultDescription));
        }

        private string GetExecutionResultDescription(ListViewItem item)
        {
            return item?.SubItems?.Count > 0
                ? item.SubItems[item.SubItems.Count - 1].Text
                : string.Empty;
        }

        private bool GetStepStopOnFatalError(ExecutionPlan plan, ExecutionPlanStep step)
        {
            return ExecutionPlanService.GetStepStopOnFatalError(plan, step);
        }

        private void AddExecutionPlanValidationMessage(ExecutionPlanStep step, string severity, string message)
        {
            ExecutionPlanService.AddValidationMessage(step, severity, message);
        }

        private void RefreshExecutionPlanStepStatus(ExecutionPlanStep step)
        {
            ExecutionPlanService.RefreshValidationStatus(step);
        }

        private void SaveExecutionPlanRunLog(ExecutionPlanRunLog runLog)
        {
            if (runLog == null || string.IsNullOrWhiteSpace(_executionPlanFilePath)) return;

            var directory = Path.GetDirectoryName(_executionPlanFilePath);
            if (string.IsNullOrWhiteSpace(directory)) directory = Environment.CurrentDirectory;
            var fileName = $"{ExecutionPlanService.SanitizePathToken(_executionPlan?.Name ?? "execution-plan")}.{DateTime.Now:yyyy-MM-dd_HHmm}.run.json";
            var path = Path.Combine(directory, fileName);
            ExecutionPlanFileService.SaveRunLog(path, runLog);
        }

        private string ResolveExecutionStepPath(ExecutionPlanStep step, int stepIndex)
        {
            return ExecutionPlanService.ResolveExecutionStepPath(_executionPlan, step, stepIndex, CreateExecutionPlanPathContext());
        }

        private TableData BuildTableDataForExecutionStep(ExecutionPlanStep step)
        {
            if (step?.Table == null || string.IsNullOrWhiteSpace(step.Table.LogicalName))
                throw new Exception("Execution plan step has no table.");

            var tableData = GetTableDataByLogicalName(step.Table.LogicalName, false);
            tableData.Settings = tableData.Settings ?? new TableSettings();
            EnsureTableDataAttributes(tableData);

            tableData.Settings.Filter = step.Snapshot?.Filter;
            tableData.SelectedAttributes = (step.Snapshot?.SelectedAttributes ?? new List<string>())
                .Select(name => tableData.Table.AllAttributes.FirstOrDefault(a => string.Equals(a.LogicalName, name, StringComparison.OrdinalIgnoreCase)))
                .Where(attr => attr != null)
                .ToList();
            if (!tableData.SelectedAttributes.Any())
                tableData.SelectedAttributes = tableData.Table.AllAttributes.ToList();
            return tableData;
        }

        private string ResolvePlanPath(string template, ExecutionPlanStep step, int stepIndex)
        {
            return ExecutionPlanService.ResolvePlanPath(template, step, stepIndex, CreateExecutionPlanPathContext());
        }

        private ExecutionPlanPathContext CreateExecutionPlanPathContext()
        {
            return new ExecutionPlanPathContext
            {
                PlanName = _executionPlan?.Name,
                SourceName = _sourceClient?.ConnectedOrgFriendlyName ?? _sourceClient?.ConnectedOrgUniqueName,
                FallbackTargetName = ActiveTargetClient?.ConnectedOrgFriendlyName ?? ActiveTargetClient?.ConnectedOrgUniqueName
            };
        }

        private void EnsureOutputDirectory(string path)
        {
            var directory = Path.GetDirectoryName(path);
            if (!string.IsNullOrWhiteSpace(directory) && !Directory.Exists(directory))
                Directory.CreateDirectory(directory);
        }

        private void CloseExecutionPlan()
        {
            AutoSaveExecutionPlan();
            _executionPlan = null;
            _executionPlanFilePath = null;
            _executionPlanValidatedForExecution = false;
            RenderExecutionPlanMenu();
            SendMessageToStatusBar?.Invoke(this, new StatusBarMessageEventArgs("Execution plan closed"));
        }

        private void AddExportStepToExecutionPlan(string operation, TableData tableData, UiSettings uiSettings, ExcelExportConfig excelConfig, string outputPath)
        {
            if (!EnsureExecutionPlanLoaded()) return;

            var step = CreateBaseExecutionPlanStep(operation, tableData);
            step.TargetEnvironment = null;
            step.Name = $"{GetOperationDisplayName(operation)} {tableData.Table.DisplayName}";
            step.Output.PathTemplate = outputPath;
            step.Snapshot.ExportSettings = uiSettings;
            step.Snapshot.ExcelConfig = excelConfig;
            step.Snapshot.SelectedAttributes = (tableData.SelectedAttributes ?? Enumerable.Empty<Models.Attribute>())
                .Select(a => a.LogicalName)
                .Where(name => !string.IsNullOrWhiteSpace(name))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
            step.Snapshot.Filter = tableData.Settings?.Filter;
            step.Snapshot.Mappings = ExecutionPlanService.CloneMappings(BuildMappingsForStepTarget(step, uiSettings));

            _executionPlan.Steps.Add(step);
            ExecutionPlanFileService.ValidatePlan(_executionPlan);
            _executionPlanValidatedForExecution = false;
            AutoSaveExecutionPlan(true);
            SendMessageToStatusBar?.Invoke(this, new StatusBarMessageEventArgs($"Added '{step.Name}' to execution plan"));
        }

        private void AddImportStepToExecutionPlan(ExcelImportSession session, TableData tableData, UiSettings uiSettings, ExcelImportMatchKeySelection matchKey, ExcelImportPreview preview)
        {
            if (!EnsureExecutionPlanLoaded()) return;

            var operation = string.Equals(session.SourceType, "JSON", StringComparison.OrdinalIgnoreCase)
                ? "ImportFromJson"
                : "ImportFromExcel";
            var step = CreateBaseExecutionPlanStep(operation, tableData);
            if (session.TargetEnvironment != null)
                step.TargetEnvironment = session.TargetEnvironment;
            step.Name = $"{GetOperationDisplayName(operation)} {tableData.Table.DisplayName}";
            step.Input.Path = session.FilePath;
            ApplyAutomaticStepLink(step, session.FilePath);
            step.Snapshot.ImportSettings = uiSettings;
            step.Snapshot.ExcelConfig = session.Config;
            step.Snapshot.RecordCollection = session.Collection;
            step.Snapshot.ImportMatchKeySelection = ExecutionPlanService.CloneImportMatchKeySelection(matchKey);
            step.Snapshot.Mappings = ExecutionPlanService.CloneMappings(BuildMappingsForStepTarget(step, uiSettings));
            step.Validation.Preview = preview == null ? null : new ExecutionPlanPreviewSummary
            {
                Rows = preview.TotalRows,
                Creates = preview.CreateCount,
                Updates = preview.UpdateCount,
                Skips = preview.SkippedCount,
                Warnings = preview.ImportErrors?.Count ?? 0,
                Errors = preview.Items?.Count(i => string.Equals(i.Action, "Skip", StringComparison.OrdinalIgnoreCase) && !string.IsNullOrWhiteSpace(i.Warnings)) ?? 0,
                Source = "Captured preview",
                IsEstimated = false,
                IsStale = false
            };

            if (matchKey != null && step.Snapshot.ExcelConfig != null)
                ApplyImportMatchKeySelection(step.Snapshot.ExcelConfig, matchKey);

            _executionPlan.Steps.Add(step);
            ExecutionPlanFileService.ValidatePlan(_executionPlan);
            _executionPlanValidatedForExecution = false;
            AutoSaveExecutionPlan(true);
            SendMessageToStatusBar?.Invoke(this, new StatusBarMessageEventArgs($"Added '{step.Name}' to execution plan"));
        }

        private ImportSourceDialog SelectImportSource(string title, string fileFilter, string linkedExportOperation)
        {
            var linkedSteps = GetCompatiblePlanExportSteps(linkedExportOperation);
            using (var dlg = new ImportSourceDialog(title, fileFilter, linkedSteps))
            {
                return dlg.ShowDialog(ParentForm) == DialogResult.OK ? dlg : null;
            }
        }

        private DmtEnvironmentInfo SelectOperationTargetEnvironment(string title)
        {
            var targets = GetLoadedTargetEnvironments();
            if (!targets.Any()) return null;
            if (targets.Count == 1) return targets[0];

            using (var dialog = new Form
            {
                Text = title,
                StartPosition = FormStartPosition.CenterParent,
                ShowIcon = false,
                ShowInTaskbar = false,
                FormBorderStyle = FormBorderStyle.FixedDialog,
                MaximizeBox = false,
                MinimizeBox = false,
                ClientSize = new Size(460, 132)
            })
            {
                var label = new System.Windows.Forms.Label
                {
                    Text = "Select the target environment for this operation.",
                    Left = 12,
                    Top = 14,
                    Width = 430,
                    Height = 22
                };
                var combo = new ComboBox
                {
                    Left = 12,
                    Top = 42,
                    Width = 430,
                    DropDownStyle = ComboBoxStyle.DropDownList
                };
                foreach (var target in targets)
                {
                    combo.Items.Add(new ExecutionPlanTargetOption
                    {
                        UniqueName = target.UniqueName,
                        FriendlyName = target.FriendlyName,
                        DisplayName = string.IsNullOrWhiteSpace(target.FriendlyName) ? target.UniqueName : target.FriendlyName
                    });
                }

                var defaultUniqueName = _targetClient?.ConnectedOrgUniqueName;
                for (var i = 0; i < combo.Items.Count; i++)
                {
                    var option = combo.Items[i] as ExecutionPlanTargetOption;
                    if (string.Equals(option?.UniqueName, defaultUniqueName, StringComparison.OrdinalIgnoreCase))
                    {
                        combo.SelectedIndex = i;
                        break;
                    }
                }
                if (combo.SelectedIndex < 0) combo.SelectedIndex = 0;

                var add = new Button { Text = "Add to Plan", Width = 100, Height = 28, Left = 246, Top = 88, DialogResult = DialogResult.OK };
                var cancel = new Button { Text = "Cancel", Width = 84, Height = 28, Left = 358, Top = 88, DialogResult = DialogResult.Cancel };
                dialog.Controls.Add(label);
                dialog.Controls.Add(combo);
                dialog.Controls.Add(add);
                dialog.Controls.Add(cancel);
                dialog.AcceptButton = add;
                dialog.CancelButton = cancel;

                if (dialog.ShowDialog(ParentForm) != DialogResult.OK) return null;

                var selected = combo.SelectedItem as ExecutionPlanTargetOption;
                return selected == null ? null : new DmtEnvironmentInfo
                {
                    UniqueName = selected.UniqueName,
                    FriendlyName = selected.FriendlyName
                };
            }
        }

        private bool TrySelectImportPreviewTarget(string title, out DmtEnvironmentInfo targetEnvironment)
        {
            targetEnvironment = null;
            if (!GetLoadedTargetEnvironments().Any())
            {
                MessageBox.Show(
                    "Connect a target environment before configuring an import from a file.\n\n" +
                    "The import preview needs a target to determine which rows will be created or updated and to resolve match keys/lookups correctly.",
                    "Target Required",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Warning);
                SendMessageToStatusBar?.Invoke(this, new StatusBarMessageEventArgs("Import configuration requires a target environment"));
                return false;
            }

            targetEnvironment = SelectOperationTargetEnvironment(title);
            return targetEnvironment != null;
        }

        private List<ExecutionPlanStep> GetCompatiblePlanExportSteps(string exportOperation)
        {
            return ExecutionPlanService.GetCompatibleExportSteps(_executionPlan, exportOperation);
        }

        private void AddLinkedImportStepToExecutionPlan(ExecutionPlanStep sourceStep, string importOperation, DmtEnvironmentInfo targetEnvironment)
        {
            if (sourceStep == null) return;
            if (!EnsureExecutionPlanLoaded()) return;

            var tableData = BuildTableDataForExecutionStep(sourceStep);
            var uiSettings = ReadSettings(Enums.Action.None);
            var step = CreateBaseExecutionPlanStep(importOperation, tableData);
            if (targetEnvironment != null)
                step.TargetEnvironment = targetEnvironment;
            step.Name = $"{GetOperationDisplayName(importOperation)} {tableData.Table.DisplayName}";
            step.Input.Mode = "FromStepOutput";
            step.Input.SourceStepId = sourceStep.Id;
            step.Input.Path = null;
            step.Snapshot.ImportSettings = uiSettings;
            step.Snapshot.ExcelConfig = ExecutionPlanService.CloneExcelConfig(sourceStep.Snapshot?.ExcelConfig);
            step.Snapshot.ImportMatchKeySelection = ExecutionPlanService.CloneImportMatchKeySelection(sourceStep.Snapshot?.ImportMatchKeySelection);
            step.Snapshot.SelectedAttributes = sourceStep.Snapshot?.SelectedAttributes?.ToList() ?? new List<string>();
            step.Snapshot.Filter = sourceStep.Snapshot?.Filter;
            step.Snapshot.Mappings = ExecutionPlanService.CloneMappings(BuildMappingsForStepTarget(step, uiSettings));
            step.Validation.Preview = new ExecutionPlanPreviewSummary
            {
                Rows = sourceStep.Validation?.Preview?.Rows ?? 0,
                Source = "Linked export output",
                IsEstimated = true,
                IsStale = true
            };

            var sourceIndex = _executionPlan.Steps.FindIndex(s => string.Equals(s.Id, sourceStep.Id, StringComparison.OrdinalIgnoreCase));
            if (sourceIndex >= 0)
                _executionPlan.Steps.Insert(sourceIndex + 1, step);
            else
                _executionPlan.Steps.Add(step);
            ExecutionPlanFileService.ValidatePlan(_executionPlan);
            _executionPlanValidatedForExecution = false;
            AutoSaveExecutionPlan(true);
            SendMessageToStatusBar?.Invoke(this, new StatusBarMessageEventArgs($"Added linked '{step.Name}' to execution plan"));
        }

        private ExecutionPlanStep CreateBaseExecutionPlanStep(string operation, TableData tableData)
        {
            UpdateExecutionPlanTargetEnvironments();
            return ExecutionPlanService.CreateBaseStep(operation, tableData, GetActiveTargetEnvironmentInfo(), _dmtFilePath);
        }

        private void ApplyAutomaticStepLink(ExecutionPlanStep importStep, string inputPath)
        {
            ExecutionPlanService.ApplyAutomaticStepLink(_executionPlan, importStep, inputPath);
        }

        private string GetOperationDisplayName(string operation)
        {
            return ExecutionPlanService.GetOperationDisplayName(operation);
        }

        #endregion Execution Plan Methods
    }
}
