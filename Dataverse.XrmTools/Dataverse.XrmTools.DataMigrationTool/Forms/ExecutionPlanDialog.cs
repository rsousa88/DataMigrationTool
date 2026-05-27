// System
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;

// DataMigrationTool
using Dataverse.XrmTools.DataMigrationTool.Models;
using Dataverse.XrmTools.DataMigrationTool.Logic;
using DmtAction = Dataverse.XrmTools.DataMigrationTool.Enums.Action;

namespace Dataverse.XrmTools.DataMigrationTool.Forms
{
    public class ExecutionPlanDialog : Form
    {
        private readonly ExecutionPlan _plan;
        private readonly ListView _steps;
        private readonly TextBox _messages;
        private readonly Label _summary;

        public bool PlanChanged { get; private set; }

        public ExecutionPlanDialog(ExecutionPlan plan)
        {
            _plan = plan ?? new ExecutionPlan();

            Text = "Execution Plan";
            ClientSize = new Size(1100, 650);
            MinimumSize = new Size(900, 520);
            StartPosition = FormStartPosition.CenterParent;
            ShowIcon = false;
            ShowInTaskbar = false;

            var outer = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                RowCount = 3,
                ColumnCount = 1,
                Padding = new Padding(10)
            };
            outer.RowStyles.Add(new RowStyle(SizeType.Absolute, 28F));
            outer.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
            outer.RowStyles.Add(new RowStyle(SizeType.Absolute, 46F));

            _summary = new Label { Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft, AutoEllipsis = true };
            outer.Controls.Add(_summary, 0, 0);

            var body = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 1, ColumnCount = 2 };
            body.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 68F));
            body.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 32F));

            var stepGroup = new GroupBox { Text = "Steps", Dock = DockStyle.Fill, Padding = new Padding(5) };
            _steps = BuildStepList();
            stepGroup.Controls.Add(_steps);

            var msgGroup = new GroupBox { Text = "Validation", Dock = DockStyle.Fill, Padding = new Padding(5) };
            _messages = new TextBox
            {
                Dock = DockStyle.Fill,
                Multiline = true,
                ReadOnly = true,
                ScrollBars = ScrollBars.Vertical
            };
            msgGroup.Controls.Add(_messages);

            body.Controls.Add(stepGroup, 0, 0);
            body.Controls.Add(msgGroup, 1, 0);
            outer.Controls.Add(body, 0, 1);

            var buttons = new FlowLayoutPanel
            {
                Dock = DockStyle.Fill,
                FlowDirection = FlowDirection.RightToLeft,
                Padding = new Padding(0, 8, 0, 0)
            };
            var btnClose = new Button { Text = "Close", Width = 90, Height = 30, DialogResult = DialogResult.OK };
            var btnValidate = new Button { Text = "Validate", Width = 90, Height = 30 };
            var btnUp = new Button { Text = "Move Up", Width = 90, Height = 30 };
            var btnDown = new Button { Text = "Move Down", Width = 90, Height = 30 };
            var btnRemove = new Button { Text = "Remove", Width = 90, Height = 30 };
            var btnEdit = new Button { Text = "Edit", Width = 90, Height = 30 };
            var btnDuplicate = new Button { Text = "Duplicate", Width = 90, Height = 30 };

            btnValidate.Click += (s, e) => ValidatePlan();
            btnUp.Click += (s, e) => MoveSelected(-1);
            btnDown.Click += (s, e) => MoveSelected(1);
            btnRemove.Click += (s, e) => RemoveSelected();
            btnEdit.Click += (s, e) => EditSelected();
            btnDuplicate.Click += (s, e) => DuplicateSelected();
            buttons.Controls.Add(btnClose);
            buttons.Controls.Add(btnValidate);
            buttons.Controls.Add(btnDown);
            buttons.Controls.Add(btnUp);
            buttons.Controls.Add(btnRemove);
            buttons.Controls.Add(btnDuplicate);
            buttons.Controls.Add(btnEdit);
            outer.Controls.Add(buttons, 0, 2);

            Controls.Add(outer);
            AcceptButton = btnClose;

            ValidatePlan();
        }

        private ListView BuildStepList()
        {
            var list = new ListView
            {
                Dock = DockStyle.Fill,
                View = View.Details,
                FullRowSelect = true,
                HideSelection = false,
                MultiSelect = false,
                CheckBoxes = true
            };
            list.Columns.Add("#", 42);
            list.Columns.Add("Status", 80);
            list.Columns.Add("Operation", 120);
            list.Columns.Add("Table", 130);
            list.Columns.Add("Input/Output", 260);
            list.Columns.Add("Preview", 120);
            list.Columns.Add("Messages", 220);
            list.ItemChecked += StepChecked;
            list.SelectedIndexChanged += (s, e) => RenderMessages();
            return list;
        }

        private void ValidatePlan()
        {
            ExecutionPlanService.ValidatePlan(_plan);
            Render();
            PlanChanged = true;
        }

        private void Render()
        {
            _steps.ItemChecked -= StepChecked;
            _steps.Items.Clear();
            for (var i = 0; i < _plan.Steps.Count; i++)
            {
                var step = _plan.Steps[i];
                var messages = step.Validation?.Messages ?? new System.Collections.Generic.List<ExecutionPlanValidationMessage>();
                var preview = step.Validation?.Preview;
                var previewText = preview == null
                    ? string.Empty
                    : $"{preview.Creates}C/{preview.Updates}U/{preview.Skips}S";
                var path = !string.IsNullOrWhiteSpace(step.Output?.PathTemplate)
                    ? step.Output.PathTemplate
                    : step.Input?.Mode == "FromStepOutput"
                        ? $"Step {GetStepNumber(step.Input.SourceStepId)} output"
                        : step.Input?.Path ?? string.Empty;
                var item = new ListViewItem(new[]
                {
                    (i + 1).ToString(),
                    step.Validation?.Status ?? "Unknown",
                    step.Operation ?? string.Empty,
                    step.Table?.LogicalName ?? string.Empty,
                    path,
                    previewText,
                    messages.Any() ? string.Join("; ", messages.Select(m => $"{m.Severity}: {m.Message}").Take(2)) : string.Empty
                })
                {
                    Checked = step.Enabled,
                    Tag = step
                };
                if (string.Equals(step.Validation?.Status, "Error", StringComparison.OrdinalIgnoreCase))
                    item.ForeColor = Color.DarkRed;
                else if (string.Equals(step.Validation?.Status, "Warning", StringComparison.OrdinalIgnoreCase))
                    item.ForeColor = Color.DarkGoldenrod;
                _steps.Items.Add(item);
            }
            _steps.ItemChecked += StepChecked;

            var errorCount = _plan.Steps.Count(s => string.Equals(s.Validation?.Status, "Error", StringComparison.OrdinalIgnoreCase));
            var warningCount = _plan.Steps.Count(s => string.Equals(s.Validation?.Status, "Warning", StringComparison.OrdinalIgnoreCase));
            _summary.Text = $"{_plan.Name ?? "Execution Plan"} - {_plan.Steps.Count} step(s), {errorCount} error(s), {warningCount} warning(s)";
            RenderMessages();
        }

        private int GetStepNumber(string stepId)
        {
            var index = _plan.Steps.FindIndex(s => string.Equals(s.Id, stepId, StringComparison.OrdinalIgnoreCase));
            return index >= 0 ? index + 1 : 0;
        }

        private void RenderMessages()
        {
            var selected = _steps.SelectedItems.Count > 0 ? _steps.SelectedItems[0].Tag as ExecutionPlanStep : null;
            if (selected == null)
            {
                _messages.Text = "Select a step to view validation messages.";
                return;
            }

            var messages = selected.Validation?.Messages ?? new System.Collections.Generic.List<ExecutionPlanValidationMessage>();
            _messages.Text = messages.Any()
                ? string.Join(Environment.NewLine, messages.Select(m => $"{m.Severity}: {m.Message}"))
                : "No validation messages.";
        }

        private void StepChecked(object sender, ItemCheckedEventArgs e)
        {
            if (!(e.Item.Tag is ExecutionPlanStep step)) return;
            step.Enabled = e.Item.Checked;
            PlanChanged = true;
        }

        private void MoveSelected(int direction)
        {
            if (_steps.SelectedItems.Count == 0) return;
            var item = _steps.SelectedItems[0];
            var index = item.Index;
            var newIndex = index + direction;
            if (newIndex < 0 || newIndex >= _plan.Steps.Count) return;

            var step = _plan.Steps[index];
            if (!CanMoveStep(step, index, newIndex))
                return;

            _plan.Steps.RemoveAt(index);
            _plan.Steps.Insert(newIndex, step);
            ValidatePlan();
            _steps.Items[newIndex].Selected = true;
        }

        private bool CanMoveStep(ExecutionPlanStep step, int index, int newIndex)
        {
            if (step?.Input != null
                && string.Equals(step.Input.Mode, "FromStepOutput", StringComparison.OrdinalIgnoreCase)
                && !string.IsNullOrWhiteSpace(step.Input.SourceStepId))
            {
                var sourceIndex = _plan.Steps.FindIndex(s => string.Equals(s.Id, step.Input.SourceStepId, StringComparison.OrdinalIgnoreCase));
                if (sourceIndex >= 0 && newIndex <= sourceIndex)
                {
                    MessageBox.Show(this, "This import is linked to an earlier export and must stay after it.", "Execution Plan", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    return false;
                }
            }

            var dependentIndexes = _plan.Steps
                .Select((candidate, candidateIndex) => new { candidate, candidateIndex })
                .Where(x => string.Equals(x.candidate.Input?.Mode, "FromStepOutput", StringComparison.OrdinalIgnoreCase)
                    && string.Equals(x.candidate.Input?.SourceStepId, step.Id, StringComparison.OrdinalIgnoreCase))
                .Select(x => x.candidateIndex)
                .ToList();

            if (dependentIndexes.Any() && newIndex >= dependentIndexes.Min())
            {
                MessageBox.Show(this, "This export has linked import steps and must stay before them.", "Execution Plan", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return false;
            }

            return true;
        }

        private void RemoveSelected()
        {
            if (_steps.SelectedItems.Count == 0) return;
            var item = _steps.SelectedItems[0];
            if (MessageBox.Show(this, "Remove selected step from this plan?", "Execution Plan", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes)
                return;

            _plan.Steps.RemoveAt(item.Index);
            ValidatePlan();
        }

        private void EditSelected()
        {
            if (_steps.SelectedItems.Count == 0) return;
            var step = _steps.SelectedItems[0].Tag as ExecutionPlanStep;
            if (step == null) return;

            using (var dlg = new ExecutionPlanStepEditDialog(step))
            {
                if (dlg.ShowDialog(this) != DialogResult.OK) return;
            }

            ValidatePlan();
            PlanChanged = true;
        }

        private void DuplicateSelected()
        {
            if (_steps.SelectedItems.Count == 0) return;
            var index = _steps.SelectedItems[0].Index;
            var step = _plan.Steps[index];
            var clone = ExecutionPlanService.CloneStepForEnvironment(step);
            _plan.Steps.Insert(index + 1, clone);
            ValidatePlan();
            _steps.Items[index + 1].Selected = true;
            PlanChanged = true;
        }
    }

    internal class ExecutionPlanStepEditDialog : Form
    {
        private readonly ExecutionPlanStep _step;
        private readonly TextBox _name;
        private readonly CheckBox _enabled;
        private readonly TextBox _path;
        private readonly NumericUpDown _maxFailedRecords;
        private readonly NumericUpDown _maxFailedPercent;
        private readonly CheckBox _stopOnFatalError;

        public ExecutionPlanStepEditDialog(ExecutionPlanStep step)
        {
            _step = step;
            Text = "Edit Plan Step";
            StartPosition = FormStartPosition.CenterParent;
            ShowIcon = false;
            ShowInTaskbar = false;
            ClientSize = new Size(520, 260);
            MinimumSize = new Size(480, 240);

            var layout = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 2,
                RowCount = 7,
                Padding = new Padding(10)
            };
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 140));
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            for (var i = 0; i < 6; i++)
                layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 30));
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

            _name = new TextBox { Dock = DockStyle.Fill, Text = step.Name ?? string.Empty };
            _enabled = new CheckBox { Dock = DockStyle.Left, Checked = step.Enabled };
            var isExport = (step.Operation ?? string.Empty).StartsWith("Export", StringComparison.OrdinalIgnoreCase);
            var pathLabel = isExport ? "Output template" : "Input path";
            _path = new TextBox
            {
                Dock = DockStyle.Fill,
                Text = isExport ? step.Output?.PathTemplate ?? string.Empty : step.Input?.Path ?? string.Empty,
                Enabled = isExport || !string.Equals(step.Input?.Mode, "FromStepOutput", StringComparison.OrdinalIgnoreCase)
            };
            _stopOnFatalError = new CheckBox { Dock = DockStyle.Left, Checked = step.FailurePolicy?.StopOnFatalError ?? true, ThreeState = false };
            _maxFailedRecords = new NumericUpDown { Dock = DockStyle.Left, Minimum = 0, Maximum = 1000000, Width = 100, Value = step.FailurePolicy?.MaxFailedRecords ?? 10 };
            _maxFailedPercent = new NumericUpDown { Dock = DockStyle.Left, Minimum = 0, Maximum = 100, DecimalPlaces = 2, Width = 100, Value = step.FailurePolicy?.MaxFailedPercent ?? 20m };

            AddRow(layout, 0, "Name", _name);
            AddRow(layout, 1, "Enabled", _enabled);
            AddRow(layout, 2, pathLabel, _path);
            AddRow(layout, 3, "Stop on fatal error", _stopOnFatalError);
            AddRow(layout, 4, "Max failed records", _maxFailedRecords);
            AddRow(layout, 5, "Max failed percent", _maxFailedPercent);

            var buttons = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.RightToLeft };
            var ok = new Button { Text = "OK", Width = 90, DialogResult = DialogResult.OK };
            var cancel = new Button { Text = "Cancel", Width = 90, DialogResult = DialogResult.Cancel };
            ok.Click += (s, e) => Apply(isExport);
            buttons.Controls.Add(ok);
            buttons.Controls.Add(cancel);
            layout.Controls.Add(buttons, 1, 6);

            Controls.Add(layout);
            AcceptButton = ok;
            CancelButton = cancel;
        }

        private static void AddRow(TableLayoutPanel layout, int row, string label, Control control)
        {
            layout.Controls.Add(new Label { Text = label, Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft }, 0, row);
            layout.Controls.Add(control, 1, row);
        }

        private void Apply(bool isExport)
        {
            _step.Name = _name.Text.Trim();
            _step.Enabled = _enabled.Checked;
            _step.FailurePolicy = _step.FailurePolicy ?? new ExecutionPlanFailurePolicy();
            _step.FailurePolicy.StopOnFatalError = _stopOnFatalError.Checked;
            _step.FailurePolicy.MaxFailedRecords = (int)_maxFailedRecords.Value;
            _step.FailurePolicy.MaxFailedPercent = _maxFailedPercent.Value;

            if (isExport)
            {
                _step.Output = _step.Output ?? new ExecutionPlanStepOutput();
                _step.Output.PathTemplate = _path.Text.Trim();
            }
            else if (_path.Enabled)
            {
                _step.Input = _step.Input ?? new ExecutionPlanStepInput();
                _step.Input.Path = _path.Text.Trim();
            }
        }
    }

    internal class PushStepConfigDialog : Form
    {
        // ── Data passed in from caller ────────────────────────────────────────────
        public sealed class LookupRelatedTableInfo
        {
            public List<ExcelImportAlternateKeyOption> AltKeys { get; set; } = new List<ExcelImportAlternateKeyOption>();
            public List<DataTableColumnConfig> SnapshotColumns { get; set; } = new List<DataTableColumnConfig>();
        }

        private sealed class LookupRowData
        {
            public DataTableColumnConfig Column { get; set; }
            public PushLookupMatchKey MatchKey { get; set; }
        }

        private sealed class MatchModeItem
        {
            public string Mode { get; }
            public string Display { get; }
            public MatchModeItem(string mode, string display) { Mode = mode; Display = display; }
            public override string ToString() => Display;
        }

        internal sealed class AltKeyItem
        {
            public ExcelImportAlternateKeyOption Option { get; }
            public AltKeyItem(ExcelImportAlternateKeyOption opt) { Option = opt; }
            public override string ToString()
            {
                var name = string.IsNullOrEmpty(Option.DisplayName) ? Option.Name : Option.DisplayName;
                return Option.Fields?.Any() == true
                    ? $"{name} ({string.Join(", ", Option.Fields)})"
                    : name;
            }
        }

        // ── Fields ───────────────────────────────────────────────────────────────
        private readonly ExecutionPlanStep _step;
        private readonly DmtSnapshot _snapshot;
        private readonly IList<ExcelImportAlternateKeyOption> _alternateKeys;
        private readonly SqliteDataLogic.PushPreview _preview;
        private readonly SqliteProjectService _project;
        private readonly string _sourceEnvId;
        private readonly string _targetEnvId;
        private readonly ISet<string> _allPlanTableNames;
        private readonly Microsoft.Xrm.Sdk.IOrganizationService _targetClient;
        private readonly Dictionary<string, LookupRelatedTableInfo> _relatedTableData;

        private TextBox _name;
        private CheckBox _create, _update;
        private ComboBox _cboMatchMode;
        private ComboBox _cboAltKey;
        private Label _lblAltKey, _lblCustomFields;
        private ListView _mainColList;
        private Button _btnCustomFields;
        private DataGridView _lookupGrid;
        private CheckBox _stopOnFatalError;
        private NumericUpDown _batchSize, _maxFailedRecords, _maxFailedPercent;
        private Label _lblPreviewCreate, _lblPreviewUpdate, _lblPreviewWarn, _lblPreviewError;
        private Label _lblRefreshing;
        private Button _btnDetails;
        private Button _btnAccept;
        private GroupBox _settingsGroup;
        private SplitContainer _leftSplit;

        private List<string> _customFields = new List<string>();
        private readonly HashSet<string> _deselectedColumns = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private bool _suppressColumnListCheck;
        private bool _refreshingPreview;
        private bool _pendingPreviewRefresh;
        private bool _initializing;
        private SqliteDataLogic.PushPreview _currentPreview;

        public PushStepConfigDialog(ExecutionPlanStep step, DmtSnapshot snapshot,
            IList<ExcelImportAlternateKeyOption> alternateKeys,
            SqliteDataLogic.PushPreview preview,
            string acceptButtonText = "Add to Plan",
            SqliteProjectService project = null,
            string sourceEnvId = null,
            string targetEnvId = null,
            ISet<string> allPlanTableNames = null,
            Microsoft.Xrm.Sdk.IOrganizationService targetClient = null,
            Dictionary<string, LookupRelatedTableInfo> relatedTableData = null)
        {
            _step = step;
            _snapshot = snapshot;
            _alternateKeys = alternateKeys ?? new List<ExcelImportAlternateKeyOption>();
            _preview = preview;
            _currentPreview = preview;
            _project = project;
            _sourceEnvId = sourceEnvId ?? string.Empty;
            _targetEnvId = targetEnvId ?? string.Empty;
            _allPlanTableNames = allPlanTableNames;
            _targetClient = targetClient;
            _relatedTableData = relatedTableData ?? new Dictionary<string, LookupRelatedTableInfo>(StringComparer.OrdinalIgnoreCase);

            Text = "Configure Push Step";
            FormBorderStyle = FormBorderStyle.FixedDialog;
            StartPosition = FormStartPosition.CenterParent;
            ShowIcon = false;
            ShowInTaskbar = false;
            MaximizeBox = false;
            MinimizeBox = false;
            ClientSize = new Size(1100, 680);
            BackColor = SystemColors.Window;

            BuildLayout(acceptButtonText);
            InitializeValues();
        }

        private void BuildLayout(string acceptButtonText)
        {
            var outer = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                RowCount = 3,
                ColumnCount = 1,
                Margin = Padding.Empty,
                Padding = Padding.Empty
            };
            outer.RowStyles.Add(new RowStyle(SizeType.Absolute, 42));
            outer.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            outer.RowStyles.Add(new RowStyle(SizeType.Absolute, 46));

            // Header
            var header = new Panel { Dock = DockStyle.Fill, BackColor = Color.FromArgb(240, 240, 240), Padding = new Padding(10, 0, 10, 0) };
            var headerLabel = new Label { Dock = DockStyle.Fill, Font = new Font(Font.FontFamily, 9f), TextAlign = ContentAlignment.MiddleLeft };
            headerLabel.Text = _snapshot != null
                ? $"Snapshot: {_snapshot.Name}   |   Table: {_snapshot.TableLogicalName}   |   Rows: {_snapshot.RowCount:N0}   |   Columns: {_snapshot.ColumnConfig?.Count ?? 0}"
                : "Configure push step settings";
            header.Controls.Add(headerLabel);
            outer.Controls.Add(header, 0, 0);

            // Body: left split | right settings+preview
            var body = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                RowCount = 1,
                ColumnCount = 2,
                Margin = Padding.Empty,
                Padding = new Padding(8, 6, 8, 0)
            };
            body.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 55));
            body.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 45));
            outer.Controls.Add(body, 0, 1);

            // Left: vertical SplitContainer — top=main record columns, bottom=lookup match keys
            _leftSplit = new SplitContainer
            {
                Dock = DockStyle.Fill,
                Orientation = Orientation.Horizontal,
                SplitterWidth = 5,
                Panel1MinSize = 80,
                Panel2MinSize = 60
            };
            var leftSplit = _leftSplit;
            bool leftSplitterSet = false;
            leftSplit.Resize += (s, e) =>
            {
                if (leftSplitterSet || leftSplit.Height <= 0) return;
                leftSplitterSet = true;
                try { leftSplit.SplitterDistance = (int)(leftSplit.Height * 0.52); } catch { }
            };

            // Top-left: columns (checked = include in push payload)
            var mainColGroup = new GroupBox { Text = "Columns", Dock = DockStyle.Fill, Padding = new Padding(4) };
            _mainColList = new ListView
            {
                Dock = DockStyle.Fill,
                View = View.Details,
                CheckBoxes = true,
                FullRowSelect = true,
                GridLines = true,
                MultiSelect = false,
                HeaderStyle = ColumnHeaderStyle.Nonclickable,
                Font = new Font("Consolas", 8.25f)
            };
            _mainColList.Columns.Add("Column", -2);
            _mainColList.Columns.Add("Type", 72);
            _mainColList.ItemCheck += ColList_ItemCheck;
            mainColGroup.Controls.Add(_mainColList);
            leftSplit.Panel1.Controls.Add(mainColGroup);

            // Bottom-left: lookup match keys DataGridView
            var lookupGroup = new GroupBox { Text = "Lookup Columns — Match Key", Dock = DockStyle.Fill, Padding = new Padding(4) };
            _lookupGrid = new DataGridView
            {
                Dock = DockStyle.Fill,
                ReadOnly = false,
                AllowUserToAddRows = false,
                AllowUserToDeleteRows = false,
                RowHeadersVisible = false,
                AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill,
                SelectionMode = DataGridViewSelectionMode.FullRowSelect,
                Font = new Font("Consolas", 8.25f),
                ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.DisableResizing,
                ColumnHeadersHeight = 24
            };
            var modeCol = new DataGridViewComboBoxColumn
            {
                Name = "Mode",
                HeaderText = "Mode",
                FillWeight = 28
            };
            modeCol.Items.AddRange("Use Source GUID", "Alternate Key", "Custom Columns", "Skip Field");
            _lookupGrid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Column", HeaderText = "Lookup Column", ReadOnly = true, FillWeight = 30 });
            _lookupGrid.Columns.Add(new DataGridViewTextBoxColumn { Name = "RelatedTable", HeaderText = "Related Table", ReadOnly = true, FillWeight = 22 });
            _lookupGrid.Columns.Add(modeCol);
            _lookupGrid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Config", HeaderText = "Key / Fields", ReadOnly = true, FillWeight = 20 });
            _lookupGrid.EditingControlShowing += (s, e) =>
            {
                if (_lookupGrid.CurrentCell?.OwningColumn?.Name == "Mode" && e.Control is ComboBox cbo)
                    cbo.DropDownStyle = ComboBoxStyle.DropDownList;
            };
            _lookupGrid.CurrentCellDirtyStateChanged += (s, e) =>
            {
                if (_lookupGrid.IsCurrentCellDirty && _lookupGrid.CurrentCell?.OwningColumn?.Name == "Mode")
                    _lookupGrid.CommitEdit(DataGridViewDataErrorContexts.Commit);
            };
            _lookupGrid.DataError += (s, e) => e.ThrowException = false;
            lookupGroup.Controls.Add(_lookupGrid);
            leftSplit.Panel2.Controls.Add(lookupGroup);
            body.Controls.Add(leftSplit, 0, 0);

            // Right: settings + preview
            var rightLayout = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                RowCount = 2,
                ColumnCount = 1,
                Margin = Padding.Empty,
                Padding = new Padding(4, 0, 0, 0)
            };
            rightLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 65));
            rightLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 35));

            _settingsGroup = new GroupBox { Text = "Settings", Dock = DockStyle.Fill, Padding = new Padding(4) };
            _settingsGroup.Controls.Add(BuildSettingsContent());
            rightLayout.Controls.Add(_settingsGroup, 0, 0);

            var previewGroup = new GroupBox { Text = "Push Preview", Dock = DockStyle.Fill, Padding = new Padding(8, 4, 8, 4) };
            previewGroup.Controls.Add(BuildPreviewContent());
            rightLayout.Controls.Add(previewGroup, 0, 1);

            body.Controls.Add(rightLayout, 1, 0);

            // Footer
            var footer = new FlowLayoutPanel
            {
                Dock = DockStyle.Fill,
                FlowDirection = FlowDirection.RightToLeft,
                BackColor = Color.FromArgb(240, 240, 240),
                Padding = new Padding(4, 7, 4, 0),
                WrapContents = false
            };
            var btnCancel = new Button { Text = "Cancel", Width = 88, Height = 28, DialogResult = DialogResult.Cancel };
            _btnAccept = new Button { Text = acceptButtonText, Width = 110, Height = 28, DialogResult = DialogResult.OK };
            _btnAccept.Click += (s, e) => Apply();
            footer.Controls.Add(btnCancel);
            footer.Controls.Add(_btnAccept);
            outer.Controls.Add(footer, 0, 2);

            Controls.Add(outer);
            AcceptButton = _btnAccept;
            CancelButton = btnCancel;
        }

        private Panel BuildSettingsContent()
        {
            var settings = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 2,
                RowCount = 10,
                Padding = new Padding(4, 2, 4, 2)
            };
            settings.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 110));
            settings.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            for (int i = 0; i < 10; i++)
                settings.RowStyles.Add(new RowStyle(SizeType.Absolute, 28));

            _name = new TextBox { Dock = DockStyle.Fill };
            AddSR(settings, 0, "Name:", _name);

            var actionsPanel = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.LeftToRight, WrapContents = false, Padding = Padding.Empty };
            _create = new CheckBox { Text = "Create", AutoSize = true, Padding = new Padding(0, 3, 6, 0) };
            _update = new CheckBox { Text = "Update", AutoSize = true, Padding = new Padding(0, 3, 6, 0) };
            actionsPanel.Controls.AddRange(new Control[] { _create, _update });
            AddSR(settings, 1, "Operations:", actionsPanel);

            _cboMatchMode = new ComboBox { Dock = DockStyle.Fill, DropDownStyle = ComboBoxStyle.DropDownList };
            _cboMatchMode.Items.Add(new MatchModeItem("Guid", "Record GUID"));
            _cboMatchMode.Items.Add(new MatchModeItem("AlternateKey", "Alternate key"));
            _cboMatchMode.Items.Add(new MatchModeItem("Custom", "Custom columns"));
            _cboMatchMode.SelectedIndexChanged += (s, e) => { UpdateMatchModeUI(); RefreshPreview(); };
            AddSR(settings, 2, "Match by:", _cboMatchMode);

            var matchKeyLabelPanel = new Panel { Dock = DockStyle.Fill };
            _lblAltKey = new Label { Text = "Alternate key:", Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft };
            _lblCustomFields = new Label { Text = "Custom columns:", Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft };
            matchKeyLabelPanel.Controls.Add(_lblAltKey);
            matchKeyLabelPanel.Controls.Add(_lblCustomFields);

            var matchKeyControlPanel = new Panel { Dock = DockStyle.Fill };
            _cboAltKey = new ComboBox { Dock = DockStyle.Fill, DropDownStyle = ComboBoxStyle.DropDownList };
            foreach (var k in _alternateKeys)
                _cboAltKey.Items.Add(new AltKeyItem(k));
            _cboAltKey.SelectedIndexChanged += (s, e) => { UpdateMatchModeUI(); RefreshPreview(); };

            _btnCustomFields = new Button
            {
                Dock = DockStyle.Left,
                Width = 170,
                Height = 24,
                Text = "Select columns..."
            };
            _btnCustomFields.Click += (s, e) => OpenCustomFieldsDialog();
            matchKeyControlPanel.Controls.Add(_cboAltKey);
            matchKeyControlPanel.Controls.Add(_btnCustomFields);
            settings.Controls.Add(matchKeyLabelPanel, 0, 3);
            settings.Controls.Add(matchKeyControlPanel, 1, 3);

            _batchSize = new NumericUpDown { Minimum = 1, Maximum = 1000, Width = 90, Dock = DockStyle.Left, Value = 50 };
            AddSR(settings, 4, "Batch size:", _batchSize);

            var sepLabel = new Label { Text = "── Failure Policy ─────────────────", Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft, ForeColor = Color.Gray };
            settings.Controls.Add(new Label(), 0, 5);
            settings.Controls.Add(sepLabel, 1, 5);

            _stopOnFatalError = new CheckBox { Dock = DockStyle.Left, Checked = true };
            AddSR(settings, 6, "Stop on fatal:", _stopOnFatalError);

            _maxFailedRecords = new NumericUpDown { Minimum = 0, Maximum = 1000000, Width = 90, Dock = DockStyle.Left, Value = 10 };
            AddSR(settings, 7, "Max failed rows:", _maxFailedRecords);

            _maxFailedPercent = new NumericUpDown { Minimum = 0, Maximum = 100, DecimalPlaces = 2, Width = 90, Dock = DockStyle.Left, Value = 20m };
            settings.Controls.Add(new Label { Text = "Max failed %:", AutoSize = true, Anchor = AnchorStyles.Left | AnchorStyles.Top, Margin = new Padding(3, 7, 3, 0) }, 0, 8);
            settings.Controls.Add(_maxFailedPercent, 1, 8);

            var panel = new Panel { Dock = DockStyle.Fill };
            panel.Controls.Add(settings);
            return panel;
        }

        private Panel BuildPreviewContent()
        {
            // 5 columns: Create | Update | thin separator | Warnings | Errors
            var layout = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 5,
                RowCount = 3,
                Padding = Padding.Empty
            };
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 24));
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 24));
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 10));
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 26));
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 26));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 20));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 34));
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

            layout.Controls.Add(new Label { Text = "Create", Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleCenter, ForeColor = Color.DarkGreen }, 0, 0);
            layout.Controls.Add(new Label { Text = "Update", Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleCenter, ForeColor = Color.DodgerBlue }, 1, 0);

            // Visual separator panel
            var sep = new Panel { Dock = DockStyle.Fill, BackColor = Color.FromArgb(180, 180, 180), Width = 1, Margin = new Padding(4, 2, 4, 2) };
            layout.Controls.Add(sep, 2, 0);
            layout.SetRowSpan(sep, 2);

            layout.Controls.Add(new Label { Text = "Warnings", Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleCenter, ForeColor = Color.DarkOrange }, 3, 0);
            layout.Controls.Add(new Label { Text = "Errors", Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleCenter, ForeColor = Color.DarkRed }, 4, 0);

            _lblPreviewCreate = new Label { Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleCenter, Font = new Font(Font.FontFamily, 14f, FontStyle.Bold), ForeColor = Color.DarkGreen };
            _lblPreviewUpdate = new Label { Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleCenter, Font = new Font(Font.FontFamily, 14f, FontStyle.Bold), ForeColor = Color.DodgerBlue };
            _lblPreviewWarn = new Label { Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleCenter, Font = new Font(Font.FontFamily, 14f, FontStyle.Bold), ForeColor = Color.DarkOrange };
            _lblPreviewError = new Label { Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleCenter, Font = new Font(Font.FontFamily, 14f, FontStyle.Bold), ForeColor = Color.DarkRed };

            UpdatePreviewLabels(_preview);

            layout.Controls.Add(_lblPreviewCreate, 0, 1);
            layout.Controls.Add(_lblPreviewUpdate, 1, 1);
            layout.Controls.Add(_lblPreviewWarn, 3, 1);
            layout.Controls.Add(_lblPreviewError, 4, 1);

            var footerPanel = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.LeftToRight, Padding = new Padding(4, 2, 0, 0), WrapContents = false };
            _btnDetails = new Button { Text = "View Details...", Width = 120, Height = 24, Enabled = _currentPreview != null };
            _btnDetails.Click += (s, e) =>
            {
                if (_currentPreview == null) return;
                using (var dlg = new PushPreviewDetailsDialog(_currentPreview))
                    dlg.ShowDialog(this);
            };
            _lblRefreshing = new Label
            {
                AutoSize = true,
                Text = "Refreshing preview...",
                ForeColor = Color.DimGray,
                TextAlign = ContentAlignment.MiddleLeft,
                Visible = false,
                Padding = new Padding(8, 4, 0, 0)
            };
            footerPanel.Controls.Add(_btnDetails);
            footerPanel.Controls.Add(_lblRefreshing);

            layout.Controls.Add(footerPanel, 0, 2);
            layout.SetColumnSpan(footerPanel, 5);

            var panel = new Panel { Dock = DockStyle.Fill };
            panel.Controls.Add(layout);
            return panel;
        }

        private void UpdatePreviewLabels(SqliteDataLogic.PushPreview p)
        {
            if (p != null)
            {
                _lblPreviewCreate.Text = FormatPreviewCount(p, p.CreateCount);
                _lblPreviewUpdate.Text = FormatPreviewCount(p, p.UpdateCount);
                _lblPreviewWarn.Text = FormatPreviewCount(p, p.WarningCount);
                _lblPreviewError.Text = FormatPreviewCount(p, p.ErrorCount);
            }
            else
            {
                if (_lblPreviewCreate != null) _lblPreviewCreate.Text = "—";
                if (_lblPreviewUpdate != null) _lblPreviewUpdate.Text = "—";
                if (_lblPreviewWarn != null) _lblPreviewWarn.Text = "—";
                if (_lblPreviewError != null) _lblPreviewError.Text = "—";
            }
        }

        private static string FormatPreviewCount(SqliteDataLogic.PushPreview preview, int count)
        {
            return preview != null && preview.HasMoreRows && count >= preview.AnalyzedRows
                ? $"{count:N0}+"
                : count.ToString("N0");
        }

        private void RefreshPreview()
        {
            if (_initializing || _project == null || _snapshot == null) return;
            if (_refreshingPreview)
            {
                _pendingPreviewRefresh = true;
                return;
            }
            _refreshingPreview = true;
            _pendingPreviewRefresh = false;

            // Block settings and Accept; keep Cancel and View Details accessible
            if (_settingsGroup != null) _settingsGroup.Enabled = false;
            if (_leftSplit != null) _leftSplit.Enabled = false;
            if (_btnAccept != null) _btnAccept.Enabled = false;
            if (_lblRefreshing != null) _lblRefreshing.Visible = true;
            if (_lblPreviewCreate != null) _lblPreviewCreate.Text = "...";
            if (_lblPreviewUpdate != null) _lblPreviewUpdate.Text = "...";
            if (_lblPreviewWarn != null) _lblPreviewWarn.Text = "...";
            if (_lblPreviewError != null) _lblPreviewError.Text = "...";

            var action = (_create?.Checked == true ? DmtAction.Create : 0)
                       | (_update?.Checked == true ? DmtAction.Update : 0);
            var settings = new UiSettings { Action = action };
            var matchKey = BuildCurrentMatchKeySelectionFromUI();
            var lookupKeys = BuildCurrentLookupMatchKeysFromUI();
            var allPlanTableNames = _allPlanTableNames;
            var targetClient = _targetClient;
            var project = _project;
            var snapshotName = _snapshot.Name;
            var sourceEnvId = _sourceEnvId;
            var targetEnvId = _targetEnvId;

            System.Threading.Tasks.Task.Run(() =>
                SqliteDataLogic.PreviewPush(project, snapshotName, sourceEnvId, targetEnvId,
                    settings, matchKey, lookupKeys, allPlanTableNames, targetClient,
                    SqliteDataLogic.DefaultPushPreviewLimit, true))
            .ContinueWith(task => BeginInvoke(new Action(() =>
            {
                _refreshingPreview = false;
                if (_settingsGroup != null) _settingsGroup.Enabled = true;
                if (_leftSplit != null) _leftSplit.Enabled = true;
                if (_btnAccept != null) _btnAccept.Enabled = true;
                if (_lblRefreshing != null) _lblRefreshing.Visible = false;

                // Re-apply mode-based enabled state (e.g., mainColList only enabled for Custom)
                UpdateMatchModeUI();

                if (_pendingPreviewRefresh)
                {
                    RefreshPreview();
                    return;
                }

                if (task.IsFaulted)
                {
                    MessageBox.Show(this, $"Preview refresh failed: {task.Exception?.InnerException?.Message ?? "Unknown error"}",
                        "Refresh Error", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    UpdatePreviewLabels(null);
                    return;
                }
                var refreshed = task.Result;
                _currentPreview = refreshed;
                UpdatePreviewLabels(refreshed);
                if (_btnDetails != null) _btnDetails.Enabled = refreshed != null;
            })));
        }

        private ExcelImportMatchKeySelection BuildCurrentMatchKeySelectionFromUI()
        {
            var selected = _cboMatchMode?.SelectedItem as MatchModeItem;
            var mode = selected?.Mode ?? "Guid";
            if (string.Equals(mode, "AlternateKey", StringComparison.OrdinalIgnoreCase)
                && _cboAltKey?.SelectedItem is AltKeyItem altKey)
            {
                return new ExcelImportMatchKeySelection
                {
                    Mode = "AlternateKey",
                    AlternateKeyName = altKey.Option.Name,
                    Fields = new List<string>(altKey.Option.Fields)
                };
            }
            if (string.Equals(mode, "Custom", StringComparison.OrdinalIgnoreCase))
                return new ExcelImportMatchKeySelection { Mode = "Custom", Fields = new List<string>(_customFields) };
            return new ExcelImportMatchKeySelection { Mode = "Guid" };
        }

        private List<PushLookupMatchKey> BuildCurrentLookupMatchKeysFromUI()
        {
            var keys = new List<PushLookupMatchKey>();
            if (_lookupGrid == null) return null;
            foreach (DataGridViewRow row in _lookupGrid.Rows)
            {
                var data = row.Tag as LookupRowData;
                if (data == null) continue;
                keys.Add(data.MatchKey);
            }
            return keys.Any() ? keys : null;
        }

        private static string LookupModeToDisplay(string mode)
        {
            switch (mode?.ToLowerInvariant())
            {
                case "skip":      return "Skip Field";
                case "alternatekey": return "Alternate Key";
                case "custom":    return "Custom Columns";
                default:          return "Use Source GUID";
            }
        }

        private static string BuildLookupConfigDisplay(PushLookupMatchKey key)
        {
            if (key == null) return "";
            var mode = key.Mode?.ToLowerInvariant();
            if (mode == "alternatekey" && !string.IsNullOrEmpty(key.AlternateKeyName))
                return key.AlternateKeyName;
            if ((mode == "alternatekey" || mode == "custom") && key.Fields?.Any() == true)
                return string.Join(", ", key.Fields);
            return "";
        }

        private static void AddSR(TableLayoutPanel layout, int row, string label, Control control)
        {
            layout.Controls.Add(new Label { Text = label, Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft }, 0, row);
            layout.Controls.Add(control, 1, row);
        }

        private void InitializeValues()
        {
            _initializing = true;
            try
            {
                var currentSettings = _step.Snapshot?.ImportSettings;
                var currentAction = currentSettings?.Action ?? (DmtAction.Create | DmtAction.Update);
                var currentMatchKey = BuildCurrentPushMatchKeySelection(_step.Snapshot);
                var currentLookupKeys = _step.Snapshot?.LookupMatchKeys ?? new List<PushLookupMatchKey>();

                _name.Text = _step.Name ?? string.Empty;
                _create.Checked = (currentAction & DmtAction.Create) != 0;
                _update.Checked = (currentAction & DmtAction.Update) != 0;

                _batchSize.Value = Math.Max(1, Math.Min(1000, currentSettings?.BatchSize > 0 ? currentSettings.BatchSize : 50));
                _stopOnFatalError.Checked = _step.FailurePolicy?.StopOnFatalError ?? true;
                _maxFailedRecords.Value = Math.Max(0, _step.FailurePolicy?.MaxFailedRecords ?? 10);
                _maxFailedPercent.Value = Math.Max(0, Math.Min(100, _step.FailurePolicy?.MaxFailedPercent ?? 20m));

                var columns = _snapshot?.ColumnConfig;
                if (columns != null)
                {
                    // Compute deselected columns from SelectedColumns (null = all included)
                    var savedSelected = _step.Snapshot?.SelectedColumns;
                    if (savedSelected != null && savedSelected.Any())
                    {
                        foreach (var col in columns.Where(c => !c.LogicalName.StartsWith("_", StringComparison.OrdinalIgnoreCase)))
                            if (!savedSelected.Contains(col.LogicalName, StringComparer.OrdinalIgnoreCase))
                                _deselectedColumns.Add(col.LogicalName);
                    }

                    _suppressColumnListCheck = true;
                    foreach (var col in columns.Where(c => !c.LogicalName.StartsWith("_", StringComparison.OrdinalIgnoreCase)))
                    {
                        var colName = string.IsNullOrEmpty(col.DisplayName) ? col.LogicalName : $"{col.DisplayName} ({col.LogicalName})";
                        var item = new ListViewItem(colName) { Tag = col };
                        item.SubItems.Add(col.Type ?? "");
                        _mainColList.Items.Add(item);
                    }
                    _suppressColumnListCheck = false;

                    // Populate lookup grid
                    foreach (var col in columns.Where(c => c.Type == "Lookup" || c.Type == "Owner" || c.Type == "Customer"))
                    {
                        var existingKey = currentLookupKeys.FirstOrDefault(k =>
                            string.Equals(k.LogicalName, col.LogicalName, StringComparison.OrdinalIgnoreCase));
                        var matchKey = existingKey ?? new PushLookupMatchKey { LogicalName = col.LogicalName, Mode = "Guid", Fields = new List<string>() };
                        var modeDisplay = LookupModeToDisplay(matchKey.Mode);
                        var configDisplay = BuildLookupConfigDisplay(matchKey);
                        var colDisplay = string.IsNullOrEmpty(col.DisplayName) ? col.LogicalName : $"{col.DisplayName} ({col.LogicalName})";
                        var rowIdx = _lookupGrid.Rows.Add(colDisplay, col.RelatedTable ?? "", modeDisplay, configDisplay);
                        _lookupGrid.Rows[rowIdx].Tag = new LookupRowData { Column = col, MatchKey = matchKey };
                    }
                }

                if (_lookupGrid.Rows.Count == 0)
                    _lookupGrid.Rows.Add("(no lookup columns in snapshot)", "", "Use Source GUID", "");

                _lookupGrid.CellEndEdit += LookupGrid_CellEndEdit;

                var mode = currentMatchKey?.Mode ?? "Guid";
                _customFields = string.Equals(mode, "Custom", StringComparison.OrdinalIgnoreCase) && currentMatchKey?.Fields != null
                    ? new List<string>(currentMatchKey.Fields)
                    : new List<string>();

                MatchModeItem modeToSelect = null;
                foreach (MatchModeItem m in _cboMatchMode.Items)
                    if (string.Equals(m.Mode, mode, StringComparison.OrdinalIgnoreCase)) { modeToSelect = m; break; }
                _cboMatchMode.SelectedItem = modeToSelect ?? _cboMatchMode.Items[0];

                if (string.Equals(mode, "AlternateKey", StringComparison.OrdinalIgnoreCase) && !string.IsNullOrEmpty(currentMatchKey?.AlternateKeyName))
                {
                    foreach (AltKeyItem a in _cboAltKey.Items)
                        if (string.Equals(a.Option.Name, currentMatchKey.AlternateKeyName, StringComparison.OrdinalIgnoreCase))
                        { _cboAltKey.SelectedItem = a; break; }
                }

                UpdateMatchModeUI();

                _create.CheckedChanged += (s, e) => RefreshPreview();
                _update.CheckedChanged += (s, e) => RefreshPreview();
            }
            finally
            {
                _initializing = false;
            }
        }

        private void UpdateMatchModeUI()
        {
            var selected = _cboMatchMode.SelectedItem as MatchModeItem;
            var mode = selected?.Mode ?? "Guid";
            var isAlt = string.Equals(mode, "AlternateKey", StringComparison.OrdinalIgnoreCase);
            var isCustom = string.Equals(mode, "Custom", StringComparison.OrdinalIgnoreCase);

            _lblAltKey.Visible = isAlt;
            _cboAltKey.Visible = isAlt;
            _lblCustomFields.Visible = isCustom;
            _btnCustomFields.Visible = isCustom;
            UpdateCustomFieldsButtonText();

            _suppressColumnListCheck = true;
            foreach (ListViewItem item in _mainColList.Items)
            {
                var col = item.Tag as DataTableColumnConfig;
                if (col == null) continue;

                item.Checked = !_deselectedColumns.Contains(col.LogicalName);
                if (isAlt && _cboAltKey.SelectedItem is AltKeyItem altKey)
                {
                    item.ForeColor = altKey.Option.Fields.Contains(col.LogicalName, StringComparer.OrdinalIgnoreCase)
                        ? Color.DodgerBlue
                        : SystemColors.WindowText;
                }
                else if (isCustom && _customFields.Contains(col.LogicalName, StringComparer.OrdinalIgnoreCase))
                {
                    item.ForeColor = Color.DodgerBlue;
                }
                else
                {
                    item.ForeColor = _deselectedColumns.Contains(col.LogicalName)
                        ? Color.Gray
                        : SystemColors.WindowText;
                }
            }
            _suppressColumnListCheck = false;
            _mainColList.Enabled = true;
        }

        private void UpdateCustomFieldsButtonText()
        {
            if (_btnCustomFields == null) return;
            var count = _customFields?.Count ?? 0;
            _btnCustomFields.Text = count == 0
                ? "Select columns..."
                : count == 1
                    ? "1 column selected..."
                    : $"{count} columns selected...";
        }

        private void OpenCustomFieldsDialog()
        {
            var columns = _snapshot?.ColumnConfig?
                .Where(c => !c.LogicalName.StartsWith("_", StringComparison.OrdinalIgnoreCase))
                .ToList() ?? new List<DataTableColumnConfig>();
            using (var dlg = new LookupCustomFieldsPickerDialog("Select Custom Match Key Columns", columns, _customFields))
            {
                if (dlg.ShowDialog(this) != DialogResult.OK) return;
                _customFields = dlg.SelectedFields ?? new List<string>();
                UpdateCustomFieldsButtonText();
                UpdateMatchModeUI();
                RefreshPreview();
            }
        }

        private void ColList_ItemCheck(object sender, ItemCheckEventArgs e)
        {
            if (_suppressColumnListCheck) return;
            var item = _mainColList.Items[e.Index];
            var col = item.Tag as DataTableColumnConfig;
            if (col == null) return;

            if (e.NewValue == CheckState.Checked)
                _deselectedColumns.Remove(col.LogicalName);
            else
                _deselectedColumns.Add(col.LogicalName);

            RefreshPreview();
        }

        private void LookupGrid_CellEndEdit(object sender, DataGridViewCellEventArgs e)
        {
            if (e.ColumnIndex < 0 || _lookupGrid.Columns[e.ColumnIndex].Name != "Mode") return;
            var row = _lookupGrid.Rows[e.RowIndex];
            var data = row.Tag as LookupRowData;
            if (data == null) return;

            var modeVal = row.Cells["Mode"].Value?.ToString() ?? "Use Source GUID";

            if (string.Equals(modeVal, "Alternate Key", StringComparison.OrdinalIgnoreCase))
            {
                _relatedTableData.TryGetValue(data.Column.RelatedTable ?? "", out var tableInfo);
                var altKeys = tableInfo?.AltKeys ?? new List<ExcelImportAlternateKeyOption>();
                if (!altKeys.Any())
                {
                    MessageBox.Show(this,
                        $"No alternate keys found for '{data.Column.RelatedTable}' in the target environment" +
                        (tableInfo?.SnapshotColumns?.Any() == true ? "" : " (no snapshot available for this table either)") + ".",
                        "Alternate Key", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    row.Cells["Mode"].Value = LookupModeToDisplay(data.MatchKey.Mode);
                    return;
                }
                using (var dlg = new LookupAltKeyPickerDialog($"Alternate Key for '{data.Column.RelatedTable}'", altKeys, data.MatchKey.AlternateKeyName))
                {
                    if (dlg.ShowDialog(this) == DialogResult.OK)
                    {
                        data.MatchKey.Mode = "AlternateKey";
                        data.MatchKey.AlternateKeyName = dlg.SelectedKeyName;
                        data.MatchKey.Fields = dlg.SelectedFields;
                        row.Cells["Config"].Value = BuildLookupConfigDisplay(data.MatchKey);
                    }
                    else
                    {
                        row.Cells["Mode"].Value = LookupModeToDisplay(data.MatchKey.Mode);
                        return;
                    }
                }
            }
            else if (string.Equals(modeVal, "Custom Columns", StringComparison.OrdinalIgnoreCase))
            {
                _relatedTableData.TryGetValue(data.Column.RelatedTable ?? "", out var tableInfo);
                var cols = tableInfo?.SnapshotColumns ?? new List<DataTableColumnConfig>();
                if (!cols.Any())
                {
                    MessageBox.Show(this,
                        $"No snapshot available for '{data.Column.RelatedTable}'. Add a snapshot for that table to the project first.",
                        "Custom Columns", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    row.Cells["Mode"].Value = LookupModeToDisplay(data.MatchKey.Mode);
                    return;
                }
                using (var dlg = new LookupCustomFieldsPickerDialog($"Custom match columns for '{data.Column.RelatedTable}'", cols,
                    string.Equals(data.MatchKey.Mode, "Custom", StringComparison.OrdinalIgnoreCase) ? data.MatchKey.Fields : null))
                {
                    if (dlg.ShowDialog(this) == DialogResult.OK && dlg.SelectedFields.Any())
                    {
                        data.MatchKey.Mode = "Custom";
                        data.MatchKey.AlternateKeyName = null;
                        data.MatchKey.Fields = dlg.SelectedFields;
                        row.Cells["Config"].Value = BuildLookupConfigDisplay(data.MatchKey);
                    }
                    else
                    {
                        row.Cells["Mode"].Value = LookupModeToDisplay(data.MatchKey.Mode);
                        return;
                    }
                }
            }
            else if (string.Equals(modeVal, "Skip Field", StringComparison.OrdinalIgnoreCase))
            {
                data.MatchKey.Mode = "Skip";
                data.MatchKey.AlternateKeyName = null;
                data.MatchKey.Fields = new List<string>();
                row.Cells["Config"].Value = "";
            }
            else // Use Source GUID
            {
                data.MatchKey.Mode = "Guid";
                data.MatchKey.AlternateKeyName = null;
                data.MatchKey.Fields = new List<string>();
                row.Cells["Config"].Value = "";
            }

            RefreshPreview();
        }

        private void Apply()
        {
            _step.Name = _name.Text.Trim();

            var action = DmtAction.None;
            if (_create.Checked) action |= DmtAction.Create;
            if (_update.Checked) action |= DmtAction.Update;

            _step.Snapshot = _step.Snapshot ?? new ExecutionPlanStepSnapshot();
            _step.Snapshot.ImportSettings = _step.Snapshot.ImportSettings ?? new UiSettings();
            _step.Snapshot.ImportSettings.Action = action;
            _step.Snapshot.ImportSettings.BatchSize = (int)_batchSize.Value;

            var selected = _cboMatchMode.SelectedItem as MatchModeItem;
            var mode = selected?.Mode ?? "Guid";

            if (string.Equals(mode, "AlternateKey", StringComparison.OrdinalIgnoreCase) && _cboAltKey.SelectedItem is AltKeyItem altKey)
            {
                ApplyPushMatchKeySelection(new ExcelImportMatchKeySelection
                {
                    Mode = "AlternateKey",
                    AlternateKeyName = altKey.Option.Name,
                    Fields = new List<string>(altKey.Option.Fields)
                });
            }
            else if (string.Equals(mode, "Custom", StringComparison.OrdinalIgnoreCase))
            {
                ApplyPushMatchKeySelection(new ExcelImportMatchKeySelection
                {
                    Mode = "Custom",
                    Fields = new List<string>(_customFields)
                });
            }
            else
            {
                ApplyPushMatchKeySelection(new ExcelImportMatchKeySelection { Mode = "Guid" });
            }

            // Save column selection (null = all included)
            if (_deselectedColumns.Any())
            {
                var allCols = _snapshot?.ColumnConfig?
                    .Where(c => !c.LogicalName.StartsWith("_", StringComparison.OrdinalIgnoreCase))
                    .Select(c => c.LogicalName)
                    .ToList() ?? new List<string>();
                _step.Snapshot.SelectedColumns = allCols
                    .Where(c => !_deselectedColumns.Contains(c))
                    .ToList();
            }
            else
            {
                _step.Snapshot.SelectedColumns = null;
            }

            // Collect per-lookup match key settings from the grid (via LookupRowData)
            var lookupKeys = new List<PushLookupMatchKey>();
            foreach (DataGridViewRow row in _lookupGrid.Rows)
            {
                var data = row.Tag as LookupRowData;
                if (data == null) continue;
                lookupKeys.Add(data.MatchKey);
            }
            _step.Snapshot.LookupMatchKeys = lookupKeys.Any() ? lookupKeys : null;

            _step.FailurePolicy = _step.FailurePolicy ?? new ExecutionPlanFailurePolicy();
            _step.FailurePolicy.StopOnFatalError = _stopOnFatalError.Checked;
            _step.FailurePolicy.MaxFailedRecords = (int)_maxFailedRecords.Value;
            _step.FailurePolicy.MaxFailedPercent = _maxFailedPercent.Value;

            if (_currentPreview != null)
            {
                _step.Validation = _step.Validation ?? new ExecutionPlanValidation();
                _step.Validation.Preview = new ExecutionPlanPreviewSummary
                {
                    Rows = _currentPreview.TotalRows,
                    Creates = _currentPreview.CreateCount,
                    Updates = _currentPreview.UpdateCount,
                    Skips = _currentPreview.Items?.Count(i => string.Equals(i.Operation, "Skip", StringComparison.OrdinalIgnoreCase)) ?? 0,
                    Warnings = _currentPreview.WarningCount,
                    Errors = _currentPreview.ErrorCount,
                    Source = "Configuration preview",
                    IsEstimated = _currentPreview.HasMoreRows,
                    IsStale = false
                };
            }
        }

        private static ExcelImportMatchKeySelection BuildCurrentPushMatchKeySelection(ExecutionPlanStepSnapshot snapshot)
        {
            if (snapshot == null) return null;
            if (!string.IsNullOrWhiteSpace(snapshot.PushMatchKeyMode))
            {
                return new ExcelImportMatchKeySelection
                {
                    Mode = snapshot.PushMatchKeyMode,
                    Fields = snapshot.PushMatchKeyFields != null ? new List<string>(snapshot.PushMatchKeyFields) : new List<string>(),
                    AlternateKeyName = snapshot.PushMatchAlternateKeyName
                };
            }
            return snapshot.ImportMatchKeySelection;
        }

        private void ApplyPushMatchKeySelection(ExcelImportMatchKeySelection selection)
        {
            _step.Snapshot.ImportMatchKeySelection = selection;
            _step.Snapshot.PushMatchKeyMode = selection?.Mode;
            _step.Snapshot.PushMatchKeyFields = selection?.Fields != null ? new List<string>(selection.Fields) : new List<string>();
            _step.Snapshot.PushMatchAlternateKeyName = selection?.AlternateKeyName;
        }
    }

    internal class LookupAltKeyPickerDialog : Form
    {
        public string SelectedKeyName { get; private set; }
        public List<string> SelectedFields { get; private set; }
        private readonly ComboBox _cbo;

        public LookupAltKeyPickerDialog(string title, IList<ExcelImportAlternateKeyOption> altKeys, string currentKeyName)
        {
            Text = title;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            StartPosition = FormStartPosition.CenterParent;
            ShowIcon = false; ShowInTaskbar = false;
            MaximizeBox = false; MinimizeBox = false;
            ClientSize = new Size(420, 100);

            var layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 2, Padding = new Padding(10, 8, 10, 4) };
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 110));
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 32));
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

            _cbo = new ComboBox { Dock = DockStyle.Fill, DropDownStyle = ComboBoxStyle.DropDownList };
            foreach (var k in altKeys)
                _cbo.Items.Add(new PushStepConfigDialog.AltKeyItem(k));
            if (!string.IsNullOrEmpty(currentKeyName))
                foreach (PushStepConfigDialog.AltKeyItem item in _cbo.Items)
                    if (string.Equals(item.Option.Name, currentKeyName, StringComparison.OrdinalIgnoreCase))
                    { _cbo.SelectedItem = item; break; }
            if (_cbo.SelectedIndex < 0 && _cbo.Items.Count > 0)
                _cbo.SelectedIndex = 0;

            layout.Controls.Add(new Label { Text = "Alternate key:", Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft }, 0, 0);
            layout.Controls.Add(_cbo, 1, 0);

            var buttons = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.RightToLeft, Padding = new Padding(0, 2, 0, 0) };
            var ok = new Button { Text = "OK", Width = 80, Height = 26, DialogResult = DialogResult.OK };
            var cancel = new Button { Text = "Cancel", Width = 80, Height = 26, DialogResult = DialogResult.Cancel };
            ok.Click += (s, e) =>
            {
                if (_cbo.SelectedItem is PushStepConfigDialog.AltKeyItem a)
                {
                    SelectedKeyName = a.Option.Name;
                    SelectedFields = new List<string>(a.Option.Fields);
                }
            };
            buttons.Controls.Add(ok);
            buttons.Controls.Add(cancel);
            layout.Controls.Add(buttons, 1, 1);

            Controls.Add(layout);
            AcceptButton = ok;
            CancelButton = cancel;
        }
    }

    internal class LookupCustomFieldsPickerDialog : Form
    {
        private sealed class ColItem
        {
            public string LogicalName { get; }
            public ColItem(string logicalName, string display) { LogicalName = logicalName; _display = display; }
            private readonly string _display;
            public override string ToString() => _display;
        }

        public List<string> SelectedFields { get; private set; } = new List<string>();
        private readonly CheckedListBox _list;

        public LookupCustomFieldsPickerDialog(string title, IList<DataTableColumnConfig> columns, IList<string> currentFields)
        {
            Text = title;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            StartPosition = FormStartPosition.CenterParent;
            ShowIcon = false; ShowInTaskbar = false;
            MaximizeBox = false; MinimizeBox = false;
            ClientSize = new Size(360, 356);

            var outer = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 2, ColumnCount = 1, Padding = new Padding(10, 8, 10, 8) };
            outer.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            outer.RowStyles.Add(new RowStyle(SizeType.Absolute, 44));

            _list = new CheckedListBox { Dock = DockStyle.Fill, CheckOnClick = true };
            foreach (var col in columns)
            {
                var display = string.IsNullOrEmpty(col.DisplayName) ? col.LogicalName : $"{col.DisplayName} ({col.LogicalName})";
                var item = new ColItem(col.LogicalName, display);
                var idx = _list.Items.Add(item);
                if (currentFields != null && currentFields.Contains(col.LogicalName, StringComparer.OrdinalIgnoreCase))
                    _list.SetItemChecked(idx, true);
            }
            outer.Controls.Add(_list, 0, 0);

            var buttons = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.RightToLeft, Padding = new Padding(0, 6, 0, 0) };
            var ok = new Button { Text = "OK", Width = 80, Height = 28, DialogResult = DialogResult.OK };
            var cancel = new Button { Text = "Cancel", Width = 80, Height = 28, DialogResult = DialogResult.Cancel };
            ok.Click += (s, e) =>
            {
                SelectedFields = new List<string>();
                for (int i = 0; i < _list.Items.Count; i++)
                    if (_list.GetItemChecked(i) && _list.Items[i] is ColItem ci)
                        SelectedFields.Add(ci.LogicalName);
            };
            buttons.Controls.Add(ok);
            buttons.Controls.Add(cancel);
            outer.Controls.Add(buttons, 0, 1);

            Controls.Add(outer);
            AcceptButton = ok;
            CancelButton = cancel;
        }
    }

    internal class PushPreviewDetailsDialog : Form
    {
        public PushPreviewDetailsDialog(SqliteDataLogic.PushPreview preview)
        {
            Text = "Push Preview — Record Details";
            FormBorderStyle = FormBorderStyle.Sizable;
            StartPosition = FormStartPosition.CenterParent;
            ShowIcon = false;
            ShowInTaskbar = false;
            ClientSize = new Size(900, 600);
            MinimumSize = new Size(600, 400);

            var grid = new DataGridView
            {
                Dock = DockStyle.Fill,
                ReadOnly = true,
                AllowUserToAddRows = false,
                AllowUserToDeleteRows = false,
                RowHeadersVisible = false,
                AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.None,
                SelectionMode = DataGridViewSelectionMode.FullRowSelect,
                ClipboardCopyMode = DataGridViewClipboardCopyMode.EnableWithoutHeaderText,
                Font = new Font("Consolas", 8.5f)
            };
            grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Row", HeaderText = "#", Width = 50 });
            grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "SourceId", HeaderText = "Source ID", Width = 260 });
            grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Operation", HeaderText = "Operation", Width = 80 });
            grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Warnings", HeaderText = "Warnings", Width = 240, AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill });
            grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Errors", HeaderText = "Errors", Width = 240, AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill });

            if (preview?.Items != null)
            {
                grid.SuspendLayout();
                foreach (var item in preview.Items)
                {
                    var rowIdx = grid.Rows.Add(item.RowNumber.ToString(), item.SourceId ?? "", item.Operation, item.Warnings ?? "", item.Errors ?? "");
                    if (!string.IsNullOrEmpty(item.Errors))
                        grid.Rows[rowIdx].DefaultCellStyle.ForeColor = Color.DarkRed;
                    else if (!string.IsNullOrEmpty(item.Warnings))
                        grid.Rows[rowIdx].DefaultCellStyle.ForeColor = Color.DarkOrange;
                    else if (item.Operation == "Create")
                        grid.Rows[rowIdx].DefaultCellStyle.ForeColor = Color.DarkGreen;
                    else if (item.Operation == "Update")
                        grid.Rows[rowIdx].DefaultCellStyle.ForeColor = Color.DodgerBlue;
                }
                grid.ResumeLayout();
            }

            var footer = new Panel { Dock = DockStyle.Bottom, Height = 40 };
            var btnClose = new Button { Text = "Close", Width = 80, Height = 28, DialogResult = DialogResult.OK };
            btnClose.Location = new Point(ClientSize.Width - 94, 6);
            footer.Resize += (s, e) => btnClose.Left = footer.Width - 94;
            footer.Controls.Add(btnClose);

            Controls.Add(grid);
            Controls.Add(footer);
            AcceptButton = btnClose;
        }
    }
}
