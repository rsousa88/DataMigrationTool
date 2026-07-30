// System
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;

// DataMigrationTool
using Dataverse.XrmTools.DataMigrationTool.Models;
using Dataverse.XrmTools.DataMigrationTool.Logic;
using Dataverse.XrmTools.DataMigrationTool.Repositories;
using DmtAction = Dataverse.XrmTools.DataMigrationTool.Enums.Action;
using IOrganizationService = Microsoft.Xrm.Sdk.IOrganizationService;

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
            public List<DataTableColumnConfig> TargetColumns { get; set; } = new List<DataTableColumnConfig>();
        }

        private sealed class LookupRowData
        {
            public DataTableColumnConfig Column { get; set; }
            public PushLookupMatchKey MatchKey { get; set; }
        }

        private sealed class ColumnMappingRowData
        {
            public DataTableColumnConfig Column { get; set; }
            public PushLookupMatchKey LookupKey { get; set; }
            public bool IsLookup => Column != null
                && (Column.Type == "Lookup" || Column.Type == "Owner" || Column.Type == "Customer");
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

        private sealed class TargetEnvironmentItem
        {
            public DmtEnvironmentInfo Environment { get; }
            public TargetEnvironmentItem(DmtEnvironmentInfo environment) { Environment = environment; }
            public override string ToString()
            {
                return EnvironmentTagHelper.GetTag(Environment);
            }
        }

        // ── Type display helpers ─────────────────────────────────────────────────
        private static readonly string[] ScalarTypeItems = new[]
        {
            "", "String", "Integer", "BigInt", "Decimal", "Double", "Money",
            "Boolean", "DateTime", "Guid",
            "OptionSet (Value)", "OptionSet (Label)",
            "MultiOptionSet (Value)", "MultiOptionSet (Label)",
            "Lookup", "Owner", "Customer"
        };

        private static string TypeToDisplay(DataTableColumnConfig col)
        {
            if (col == null || string.IsNullOrEmpty(col.Type)) return "";
            if (col.Type == "OptionSet" || col.Type == "MultiOptionSet")
                return $"{col.Type} ({(string.Equals(col.ExportMode, "Label", StringComparison.OrdinalIgnoreCase) ? "Label" : "Value")})";
            return col.Type;
        }

        private static (string type, string exportMode) DisplayToType(string display)
        {
            if (string.IsNullOrEmpty(display)) return (null, null);
            if (display == "OptionSet (Value)") return ("OptionSet", "Value");
            if (display == "OptionSet (Label)") return ("OptionSet", "Label");
            if (display == "MultiOptionSet (Value)") return ("MultiOptionSet", "Value");
            if (display == "MultiOptionSet (Label)") return ("MultiOptionSet", "Label");
            return (display, null);
        }

        // ── Fields ───────────────────────────────────────────────────────────────
        private readonly bool _tableLockedFromPrompt;
        private readonly ExecutionPlanStep _step;
        private readonly DmtSnapshot _snapshot;
        private readonly IList<ExcelImportAlternateKeyOption> _alternateKeys;
        private readonly SqliteDataLogic.PushPreview _preview;
        private readonly SqliteProjectService _project;
        private readonly string _sourceEnvId;
        private string _targetEnvId;
        private readonly ISet<string> _allPlanTableNames;
        private readonly IOrganizationService _targetClient;
        private readonly List<DmtEnvironmentInfo> _availableTargets;
        private readonly Dictionary<string, IOrganizationService> _targetClientsByEnvironment;
        private readonly Dictionary<string, LookupRelatedTableInfo> _relatedTableData;

        private Label _headerLabel;
        private TextBox _txtTable;
        private Button _btnLoadTable;
        private List<string> _availableTableLogicalNames;

        private TextBox _name;
        private ComboBox _cboTargetEnvironment;
        private CheckBox _create, _update;
        private ComboBox _cboMatchMode;
        private ComboBox _cboAltKey;
        private Label _lblAltKey, _lblCustomFields;
        private DataGridView _colMappingGrid;
        private Button _btnCustomFields;
        private CheckBox _stopOnFatalError;
        private NumericUpDown _batchSize, _maxFailedRecords, _maxFailedPercent;
        private Label _lblPreviewCreate, _lblPreviewUpdate, _lblPreviewWarn, _lblPreviewError;
        private Label _lblRefreshing;
        private Button _btnDetails;
        private Button _btnAccept;
        private GroupBox _settingsGroup;

        private readonly List<string> _targetTableAttributes;
        private readonly List<Dictionary<string, object>> _sampleRows;
        private readonly Dictionary<string, List<string>> _lookupTargets;
        private List<int> _sampleSubsetIndices = new List<int>();
        private int _sampleSubsetPos = 0;
        private Label _lblSampleNav;
        private Button _btnSamplePrev;
        private Button _btnSampleNext;
        private EventHandler _taLeaveHandler;
        private ComboBox _taCurrentCbo;
        private List<string> _customFields = new List<string>();
        private bool _refreshingPreview;
        private bool _pendingPreviewRefresh;
        private bool _deferredPreviewRefresh;
        private bool _initializing;
        private SqliteDataLogic.PushPreview _currentPreview;
        private Timer _previewRefreshTimer;

        public PushStepConfigDialog(ExecutionPlanStep step, DmtSnapshot snapshot,
            IList<ExcelImportAlternateKeyOption> alternateKeys,
            SqliteDataLogic.PushPreview preview,
            string acceptButtonText = "Add to Plan",
            SqliteProjectService project = null,
            string sourceEnvId = null,
            string targetEnvId = null,
            ISet<string> allPlanTableNames = null,
            IOrganizationService targetClient = null,
            IEnumerable<DmtEnvironmentInfo> availableTargets = null,
            IDictionary<string, IOrganizationService> targetClientsByEnvironment = null,
            Dictionary<string, LookupRelatedTableInfo> relatedTableData = null,
            List<string> targetTableAttributes = null,
            List<Dictionary<string, object>> sampleRows = null,
            Dictionary<string, List<string>> lookupTargets = null,
            List<string> availableTableLogicalNames = null,
            bool tableLockedFromPrompt = false)
        {
            _tableLockedFromPrompt = tableLockedFromPrompt;
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
            _availableTargets = (availableTargets ?? Enumerable.Empty<DmtEnvironmentInfo>())
                .Where(env => env != null && !string.IsNullOrWhiteSpace(env.UniqueName))
                .GroupBy(env => env.UniqueName, StringComparer.OrdinalIgnoreCase)
                .Select(group => group.First())
                .ToList();
            _targetClientsByEnvironment = targetClientsByEnvironment != null
                ? new Dictionary<string, IOrganizationService>(targetClientsByEnvironment, StringComparer.OrdinalIgnoreCase)
                : new Dictionary<string, IOrganizationService>(StringComparer.OrdinalIgnoreCase);
            _relatedTableData = relatedTableData ?? new Dictionary<string, LookupRelatedTableInfo>(StringComparer.OrdinalIgnoreCase);
            _targetTableAttributes = targetTableAttributes ?? new List<string>();
            _sampleRows = sampleRows;
            _lookupTargets = lookupTargets ?? new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
            _availableTableLogicalNames = availableTableLogicalNames ?? new List<string>();

            Text = "Configure Push Step";
            FormBorderStyle = FormBorderStyle.Sizable;
            StartPosition = FormStartPosition.CenterParent;
            ShowIcon = false;
            ShowInTaskbar = false;
            MaximizeBox = false;
            MinimizeBox = false;
            var workArea = Screen.PrimaryScreen.WorkingArea;
            ClientSize = new Size(Math.Max(1100, (int)(workArea.Width * 0.82)), Math.Max(720, (int)(workArea.Height * 0.82)));
            BackColor = SystemColors.Window;

            BuildLayout(acceptButtonText);
            InitializeValues();

            FormClosed += (s, e) =>
            {
                _previewRefreshTimer?.Stop();
                _previewRefreshTimer?.Dispose();
            };
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
            _headerLabel = new Label { Dock = DockStyle.Fill, Font = new Font(Font.FontFamily, 9f), TextAlign = ContentAlignment.MiddleLeft };
            UpdateHeaderLabel();
            header.Controls.Add(_headerLabel);
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

            // Left: unified column mapping grid (include/exclude, attribute remap, lookup match key)
            var leftGroup = new GroupBox { Text = "Column Mappings", Dock = DockStyle.Fill, Padding = new Padding(4) };
            _colMappingGrid = new DataGridView
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
                ColumnHeadersHeight = 24,
                EditMode = DataGridViewEditMode.EditOnEnter
            };
            var includeCol = new DataGridViewCheckBoxColumn
                { Name = "Include", HeaderText = "✓", SortMode = DataGridViewColumnSortMode.NotSortable, Width = 26 };
            includeCol.AutoSizeMode = DataGridViewAutoSizeColumnMode.None;
            _colMappingGrid.Columns.Add(includeCol);
            _colMappingGrid.Columns.Add(new DataGridViewTextBoxColumn
                { Name = "SnapshotColumn", HeaderText = "Snapshot Column", ReadOnly = true, FillWeight = 20, SortMode = DataGridViewColumnSortMode.NotSortable });
            _colMappingGrid.Columns.Add(new DataGridViewComboBoxColumn
                { Name = "TargetAttribute", HeaderText = "Target Attribute", FillWeight = 18, SortMode = DataGridViewColumnSortMode.NotSortable });
            _colMappingGrid.Columns.Add(new DataGridViewTextBoxColumn
                { Name = "Type", HeaderText = "Type", FillWeight = 8, SortMode = DataGridViewColumnSortMode.NotSortable });
            var lookupModeCol = new DataGridViewComboBoxColumn
                { Name = "LookupMode", HeaderText = "Lookup Match", FillWeight = 14, SortMode = DataGridViewColumnSortMode.NotSortable };
            lookupModeCol.Items.AddRange("Use Source GUID", "Alternate Key", "Custom Columns", "Skip Field");
            _colMappingGrid.Columns.Add(lookupModeCol);
            _colMappingGrid.Columns.Add(new DataGridViewTextBoxColumn
                { Name = "RelatedTable", HeaderText = "Related Table", FillWeight = 12, SortMode = DataGridViewColumnSortMode.NotSortable });
            _colMappingGrid.Columns.Add(new DataGridViewTextBoxColumn
                { Name = "Config", HeaderText = "Key / Fields", ReadOnly = true, FillWeight = 16, SortMode = DataGridViewColumnSortMode.NotSortable });
            _colMappingGrid.Columns.Add(new DataGridViewTextBoxColumn
                { Name = "Sample", HeaderText = "Sample", ReadOnly = true, FillWeight = 12, SortMode = DataGridViewColumnSortMode.NotSortable });
            _colMappingGrid.EditingControlShowing += (s, e) =>
            {
                var colName = _colMappingGrid.CurrentCell?.OwningColumn?.Name;
                if (colName == "LookupMode" && e.Control is ComboBox cboLM)
                {
                    cboLM.DropDownStyle = ComboBoxStyle.DropDownList;
                }
                else if (colName == "TargetAttribute" && e.Control is ComboBox cboTA)
                {
                    cboTA.DropDownStyle = ComboBoxStyle.DropDown;
                    cboTA.AutoCompleteMode = AutoCompleteMode.SuggestAppend;
                    cboTA.AutoCompleteSource = AutoCompleteSource.ListItems;
                    if (_taCurrentCbo != null && _taLeaveHandler != null)
                        _taCurrentCbo.Leave -= _taLeaveHandler;
                    _taCurrentCbo = cboTA;
                    _taLeaveHandler = (ls, le) =>
                    {
                        var text = _taCurrentCbo.Text?.Trim();
                        if (!string.IsNullOrEmpty(text) && !_taCurrentCbo.Items.Contains(text))
                            _taCurrentCbo.Items.Add(text);
                    };
                    cboTA.Leave += _taLeaveHandler;
                }
                else if (colName == "RelatedTable" && e.Control is ComboBox cboRT)
                {
                    cboRT.DropDownStyle = ComboBoxStyle.DropDownList;
                }
                else if (colName == "RelatedTable" && e.Control is TextBox txtRT)
                {
                    txtRT.AutoCompleteMode = AutoCompleteMode.SuggestAppend;
                    txtRT.AutoCompleteSource = AutoCompleteSource.CustomSource;
                    var rtSrc = new AutoCompleteStringCollection();
                    if (_availableTableLogicalNames?.Any() == true)
                        rtSrc.AddRange(_availableTableLogicalNames.ToArray());
                    txtRT.AutoCompleteCustomSource = rtSrc;
                }
            };
            _colMappingGrid.CurrentCellDirtyStateChanged += (s, e) =>
            {
                if (!_colMappingGrid.IsCurrentCellDirty) return;
                var colNm = _colMappingGrid.CurrentCell?.OwningColumn?.Name;
                if (colNm == "Include" || colNm == "LookupMode" || colNm == "RelatedTable" || colNm == "Type")
                    _colMappingGrid.CommitEdit(DataGridViewDataErrorContexts.Commit);
            };
            _colMappingGrid.CellValueChanged += (s, e) =>
            {
                if (e.ColumnIndex >= 0 && _colMappingGrid.Columns[e.ColumnIndex].Name == "Include")
                    SchedulePreviewRefresh();
            };
            _colMappingGrid.DataError += (s, e) => e.ThrowException = false;
            _colMappingGrid.CellClick += (s, e) =>
            {
                if (e.RowIndex < 0 || e.ColumnIndex < 0) return;
                var colName = _colMappingGrid.Columns[e.ColumnIndex].Name;
                if (colName == "LookupMode" && _colMappingGrid.Rows[e.RowIndex].Cells["LookupMode"] is DataGridViewComboBoxCell)
                {
                    _colMappingGrid.BeginEdit(true);
                    BeginInvoke(new Action(() =>
                    {
                        if (_colMappingGrid.EditingControl is ComboBox cbo) cbo.DroppedDown = true;
                    }));
                }
                else if (colName == "TargetAttribute" && _colMappingGrid.Rows[e.RowIndex].Cells["TargetAttribute"] is DataGridViewComboBoxCell)
                {
                    _colMappingGrid.BeginEdit(true);
                    BeginInvoke(new Action(() =>
                    {
                        if (_colMappingGrid.EditingControl is ComboBox cbo) cbo.DroppedDown = true;
                    }));
                }
                else if (colName == "RelatedTable" && _colMappingGrid.Rows[e.RowIndex].Cells["RelatedTable"] is DataGridViewComboBoxCell)
                {
                    _colMappingGrid.BeginEdit(true);
                    BeginInvoke(new Action(() =>
                    {
                        if (_colMappingGrid.EditingControl is ComboBox cbo) cbo.DroppedDown = true;
                    }));
                }
                else if (colName == "Type" && _colMappingGrid.Rows[e.RowIndex].Cells["Type"] is DataGridViewComboBoxCell)
                {
                    _colMappingGrid.BeginEdit(true);
                    BeginInvoke(new Action(() =>
                    {
                        if (_colMappingGrid.EditingControl is ComboBox cbo) cbo.DroppedDown = true;
                    }));
                }
            };
            // Sample row navigator bar
            var navPanel = new FlowLayoutPanel
            {
                Dock = DockStyle.Fill,
                FlowDirection = FlowDirection.LeftToRight,
                WrapContents = false,
                Padding = new Padding(0, 3, 0, 0)
            };
            navPanel.Controls.Add(new Label { Text = "Sample row:", AutoSize = true, Padding = new Padding(0, 2, 4, 0) });
            _btnSamplePrev = new Button { Text = "◀", Width = 24, Height = 22, FlatStyle = FlatStyle.Flat, Padding = Padding.Empty, Margin = new Padding(0, 0, 2, 0) };
            _lblSampleNav = new Label { AutoSize = true, Text = "—", TextAlign = ContentAlignment.MiddleLeft, Padding = new Padding(4, 2, 4, 0) };
            _btnSampleNext = new Button { Text = "▶", Width = 24, Height = 22, FlatStyle = FlatStyle.Flat, Padding = Padding.Empty, Margin = Padding.Empty };
            _btnSamplePrev.Click += (s, e) => NavigateSampleRow(-1);
            _btnSampleNext.Click += (s, e) => NavigateSampleRow(+1);
            navPanel.Controls.Add(_btnSamplePrev);
            navPanel.Controls.Add(_lblSampleNav);
            navPanel.Controls.Add(_btnSampleNext);

            var leftContent = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                RowCount = 2,
                ColumnCount = 1,
                Margin = Padding.Empty,
                Padding = Padding.Empty
            };
            leftContent.RowStyles.Add(new RowStyle(SizeType.Absolute, 28));
            leftContent.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            leftContent.Controls.Add(navPanel, 0, 0);
            leftContent.Controls.Add(_colMappingGrid, 0, 1);
            leftGroup.Controls.Add(leftContent);
            body.Controls.Add(leftGroup, 0, 0);

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
                RowCount = 12,
                Padding = new Padding(4, 2, 4, 2)
            };
            settings.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 110));
            settings.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            for (int i = 0; i < 12; i++)
                settings.RowStyles.Add(new RowStyle(SizeType.Absolute, 28));

            // Row 0: Table logical name + Load button
            var tablePanel = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 2,
                RowCount = 1,
                Padding = Padding.Empty,
                Margin = Padding.Empty
            };
            tablePanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            tablePanel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 55));
            _txtTable = new TextBox { Dock = DockStyle.Fill, Margin = new Padding(0, 3, 2, 0) };
            if (_availableTableLogicalNames?.Any() == true)
            {
                _txtTable.AutoCompleteMode = AutoCompleteMode.SuggestAppend;
                _txtTable.AutoCompleteSource = AutoCompleteSource.CustomSource;
                var src = new AutoCompleteStringCollection();
                src.AddRange(_availableTableLogicalNames.ToArray());
                _txtTable.AutoCompleteCustomSource = src;
            }
            _txtTable.KeyDown += (s, e) =>
            {
                if (e.KeyCode == Keys.Return) { e.SuppressKeyPress = true; LoadTableConfiguration(_txtTable.Text.Trim()); }
            };
            _btnLoadTable = new Button { Dock = DockStyle.Fill, Text = "Load", Margin = new Padding(0, 2, 0, 2) };
            _btnLoadTable.Click += (s, e) => LoadTableConfiguration(_txtTable.Text.Trim());
            tablePanel.Controls.Add(_txtTable, 0, 0);
            tablePanel.Controls.Add(_btnLoadTable, 1, 0);
            AddSR(settings, 0, "Table:", tablePanel);

            _name = new TextBox { Dock = DockStyle.Fill };
            AddSR(settings, 1, "Name:", _name);

            _cboTargetEnvironment = new ComboBox { Dock = DockStyle.Fill, DropDownStyle = ComboBoxStyle.DropDownList };
            _cboTargetEnvironment.SelectedIndexChanged += (s, e) =>
            {
                if (_initializing) return;
                ApplySelectedTargetToName();
                RefreshPreview();
            };
            AddSR(settings, 2, "Target:", _cboTargetEnvironment);

            var actionsPanel = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.LeftToRight, WrapContents = false, Padding = Padding.Empty };
            _create = new CheckBox { Text = "Create", AutoSize = true, Padding = new Padding(0, 3, 6, 0) };
            _update = new CheckBox { Text = "Update", AutoSize = true, Padding = new Padding(0, 3, 6, 0) };
            actionsPanel.Controls.AddRange(new Control[] { _create, _update });
            AddSR(settings, 3, "Operations:", actionsPanel);

            _cboMatchMode = new ComboBox { Dock = DockStyle.Fill, DropDownStyle = ComboBoxStyle.DropDownList };
            _cboMatchMode.Items.Add(new MatchModeItem("Guid", "Record GUID"));
            _cboMatchMode.Items.Add(new MatchModeItem("AlternateKey", "Alternate key"));
            _cboMatchMode.Items.Add(new MatchModeItem("Custom", "Custom columns"));
            _cboMatchMode.SelectedIndexChanged += (s, e) => { UpdateMatchModeUI(); RefreshPreview(); };
            AddSR(settings, 4, "Match by:", _cboMatchMode);

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
            settings.Controls.Add(matchKeyLabelPanel, 0, 5);
            settings.Controls.Add(matchKeyControlPanel, 1, 5);

            _batchSize = new NumericUpDown { Minimum = 1, Maximum = 1000, Width = 90, Dock = DockStyle.Left, Value = 50 };
            AddSR(settings, 6, "Batch size:", _batchSize);

            var sepLabel = new Label { Text = "── Failure Policy ─────────────────", Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft, ForeColor = Color.Gray };
            settings.Controls.Add(new Label(), 0, 7);
            settings.Controls.Add(sepLabel, 1, 7);

            _stopOnFatalError = new CheckBox { Dock = DockStyle.Left, Checked = true };
            AddSR(settings, 8, "Stop on fatal:", _stopOnFatalError);

            _maxFailedRecords = new NumericUpDown { Minimum = 0, Maximum = 1000000, Width = 90, Dock = DockStyle.Left, Value = 10 };
            AddSR(settings, 9, "Max failed rows:", _maxFailedRecords);

            _maxFailedPercent = new NumericUpDown { Minimum = 0, Maximum = 100, DecimalPlaces = 2, Width = 90, Dock = DockStyle.Left, Value = 20m };
            settings.Controls.Add(new Label { Text = "Max failed %:", AutoSize = true, Anchor = AnchorStyles.Left | AnchorStyles.Top, Margin = new Padding(3, 7, 3, 0) }, 0, 10);
            settings.Controls.Add(_maxFailedPercent, 1, 10);

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
            if (string.IsNullOrWhiteSpace(_snapshot.TableLogicalName)) return;
            _deferredPreviewRefresh = false;
            if (_refreshingPreview)
            {
                _pendingPreviewRefresh = true;
                return;
            }
            _refreshingPreview = true;
            _pendingPreviewRefresh = false;

            // Block settings and Accept; keep Cancel and View Details accessible
            if (_settingsGroup != null) _settingsGroup.Enabled = false;
            if (_colMappingGrid != null) _colMappingGrid.Enabled = false;
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
            var targetClient = GetSelectedTargetClient();
            var project = _project;
            var snapshotName = _snapshot.Name;
            var sourceEnvId = _sourceEnvId;
            var selectedTarget = GetSelectedTargetEnvironment();
            var targetEnvId = selectedTarget?.UniqueName ?? _targetEnvId;

            System.Threading.Tasks.Task.Run(() =>
                SqliteDataLogic.PreviewPush(project, snapshotName, sourceEnvId, targetEnvId,
                    settings, matchKey, lookupKeys, allPlanTableNames, targetClient,
                    SqliteDataLogic.DefaultPushPreviewLimit, true))
            .ContinueWith(task => BeginInvoke(new Action(() =>
            {
                _refreshingPreview = false;
                if (_settingsGroup != null) _settingsGroup.Enabled = true;
                if (_colMappingGrid != null) _colMappingGrid.Enabled = true;
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
                UpdateSampleSubset();
                if (_btnDetails != null) _btnDetails.Enabled = refreshed != null;
            })));
        }

        private void SchedulePreviewRefresh()
        {
            if (_initializing || _project == null || _snapshot == null) return;
            _deferredPreviewRefresh = true;
            if (_previewRefreshTimer == null)
            {
                _previewRefreshTimer = new Timer { Interval = 900 };
                _previewRefreshTimer.Tick += (s, e) =>
                {
                    _previewRefreshTimer.Stop();
                    RefreshPreview();
                };
            }

            _previewRefreshTimer.Stop();
            _previewRefreshTimer.Start();
        }

        private void UpdateSampleSubset()
        {
            if (_sampleRows == null || !_sampleRows.Any())
            {
                _sampleSubsetIndices = new List<int>();
                UpdateSampleNavLabel();
                UpdateSampleDisplay();
                return;
            }

            var preview = _currentPreview;
            if (preview?.Items?.Any() == true)
            {
                var ops = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                if (_create?.Checked == true) ops.Add("Create");
                if (_update?.Checked == true) ops.Add("Update");

                var subset = preview.Items
                    .Where(item => ops.Contains(item.Operation ?? "") && item.RowNumber >= 1 && item.RowNumber <= _sampleRows.Count)
                    .Select(item => item.RowNumber - 1)
                    .Distinct()
                    .OrderBy(i => i)
                    .ToList();

                _sampleSubsetIndices = subset.Any() ? subset : Enumerable.Range(0, _sampleRows.Count).ToList();
            }
            else
            {
                _sampleSubsetIndices = Enumerable.Range(0, _sampleRows.Count).ToList();
            }

            _sampleSubsetPos = Math.Min(_sampleSubsetPos, Math.Max(0, _sampleSubsetIndices.Count - 1));
            UpdateSampleNavLabel();
            UpdateSampleDisplay();
        }

        private void NavigateSampleRow(int delta)
        {
            if (!_sampleSubsetIndices.Any()) return;
            _sampleSubsetPos = Math.Max(0, Math.Min(_sampleSubsetIndices.Count - 1, _sampleSubsetPos + delta));
            UpdateSampleNavLabel();
            UpdateSampleDisplay();
        }

        private void UpdateSampleNavLabel()
        {
            if (_lblSampleNav == null) return;
            if (!_sampleSubsetIndices.Any())
            {
                _lblSampleNav.Text = "—";
                if (_btnSamplePrev != null) _btnSamplePrev.Enabled = false;
                if (_btnSampleNext != null) _btnSampleNext.Enabled = false;
            }
            else
            {
                _lblSampleNav.Text = $"{_sampleSubsetPos + 1} / {_sampleSubsetIndices.Count}";
                if (_btnSamplePrev != null) _btnSamplePrev.Enabled = _sampleSubsetPos > 0;
                if (_btnSampleNext != null) _btnSampleNext.Enabled = _sampleSubsetPos < _sampleSubsetIndices.Count - 1;
            }
        }

        private void UpdateSampleDisplay()
        {
            if (_colMappingGrid == null) return;
            Dictionary<string, object> currentRow = null;
            if (_sampleSubsetIndices.Any() && _sampleRows != null)
            {
                var rowIdx = _sampleSubsetIndices[_sampleSubsetPos];
                if (rowIdx >= 0 && rowIdx < _sampleRows.Count)
                    currentRow = _sampleRows[rowIdx];
            }

            foreach (DataGridViewRow gridRow in _colMappingGrid.Rows)
            {
                var data = gridRow.Tag as ColumnMappingRowData;
                if (data == null) continue;
                object sampleVal = null;
                currentRow?.TryGetValue(data.Column.LogicalName, out sampleVal);
                gridRow.Cells["Sample"].Value = FormatSampleValue(sampleVal);
            }
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
            if (_colMappingGrid == null) return null;
            foreach (DataGridViewRow row in _colMappingGrid.Rows)
            {
                var data = row.Tag as ColumnMappingRowData;
                if (data?.IsLookup != true || data.LookupKey == null) continue;
                keys.Add(data.LookupKey);
            }
            return keys.Any() ? keys : null;
        }

        private static string FormatSampleValue(object val)
        {
            if (val == null) return "";
            var str = val.ToString();
            return str.Length > 35 ? str.Substring(0, 35) + "…" : str;
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

        private void PopulateTargetEnvironmentList()
        {
            if (_cboTargetEnvironment == null) return;

            var targets = new List<DmtEnvironmentInfo>(_availableTargets);
            var current = _step.TargetEnvironment;
            if (current != null && !string.IsNullOrWhiteSpace(current.UniqueName)
                && !targets.Any(env => string.Equals(env.UniqueName, current.UniqueName, StringComparison.OrdinalIgnoreCase)))
            {
                targets.Add(current);
            }

            _cboTargetEnvironment.Items.Clear();
            foreach (var target in targets)
                _cboTargetEnvironment.Items.Add(new TargetEnvironmentItem(target));

            var selectedUniqueName = !string.IsNullOrWhiteSpace(_step.TargetEnvironment?.UniqueName)
                ? _step.TargetEnvironment.UniqueName
                : _targetEnvId;

            for (var i = 0; i < _cboTargetEnvironment.Items.Count; i++)
            {
                var item = _cboTargetEnvironment.Items[i] as TargetEnvironmentItem;
                if (string.Equals(item?.Environment?.UniqueName, selectedUniqueName, StringComparison.OrdinalIgnoreCase))
                {
                    _cboTargetEnvironment.SelectedIndex = i;
                    break;
                }
            }

            if (_cboTargetEnvironment.SelectedIndex < 0 && _cboTargetEnvironment.Items.Count > 0)
                _cboTargetEnvironment.SelectedIndex = 0;

            _cboTargetEnvironment.Enabled = _cboTargetEnvironment.Items.Count > 1;
            var selected = GetSelectedTargetEnvironment();
            _targetEnvId = selected?.UniqueName ?? _targetEnvId;
            if (selected != null)
            {
                _step.TargetEnvironment = new DmtEnvironmentInfo
                {
                    UniqueName = selected.UniqueName,
                    FriendlyName = selected.FriendlyName,
                    Tag = selected.Tag
                };
            }
        }

        private DmtEnvironmentInfo GetSelectedTargetEnvironment()
        {
            return (_cboTargetEnvironment?.SelectedItem as TargetEnvironmentItem)?.Environment;
        }

        private IOrganizationService GetSelectedTargetClient()
        {
            var selected = GetSelectedTargetEnvironment();
            if (selected != null
                && !string.IsNullOrWhiteSpace(selected.UniqueName)
                && _targetClientsByEnvironment.TryGetValue(selected.UniqueName, out var client)
                && client != null)
            {
                return client;
            }

            return _targetClient;
        }

        private void ApplySelectedTargetToName()
        {
            var selected = GetSelectedTargetEnvironment();
            if (selected == null) return;

            _targetEnvId = selected.UniqueName ?? string.Empty;
            if (_name != null)
                _name.Text = ExecutionPlanService.BuildPushSnapshotStepName(_snapshot, selected);
        }

        private void InitializeValues()
        {
            _initializing = true;
            try
            {
                var tableLogicalName = _step.Table?.LogicalName ?? string.Empty;
                _txtTable.Text = tableLogicalName;
                if (string.IsNullOrWhiteSpace(tableLogicalName))
                    SetTableControlsEnabled(false);
                else if (_tableLockedFromPrompt)
                {
                    // Table was just resolved via the pre-dialog prompt and all metadata below was
                    // fetched for it — retyping it here would silently desync the grid from that data.
                    _txtTable.ReadOnly = true;
                    _btnLoadTable.Enabled = false;
                }

                var currentSettings = _step.Snapshot?.ImportSettings;
                var currentAction = currentSettings?.Action ?? (DmtAction.Create | DmtAction.Update);
                var currentMatchKey = BuildCurrentPushMatchKeySelection(_step.Snapshot);
                var currentLookupKeys = _step.Snapshot?.LookupMatchKeys ?? new List<PushLookupMatchKey>();

                PopulateTargetEnvironmentList();
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
                    var existingMappings = _step.Snapshot?.ColumnMappings;
                    var savedSelected = _step.Snapshot?.SelectedColumns;

                    foreach (var col in columns.Where(c => !c.LogicalName.StartsWith("_", StringComparison.OrdinalIgnoreCase)))
                    {
                        bool include;
                        string targetAttr;
                        PushLookupMatchKey lookupKey = null;
                        var isLookup = col.Type == "Lookup" || col.Type == "Owner" || col.Type == "Customer";

                        if (existingMappings != null)
                        {
                            var m = existingMappings.FirstOrDefault(x =>
                                string.Equals(x.SnapshotColumn, col.LogicalName, StringComparison.OrdinalIgnoreCase));
                            include = m?.Include ?? true;
                            targetAttr = !string.IsNullOrWhiteSpace(m?.DataverseAttribute) ? m.DataverseAttribute : col.LogicalName;
                            if (isLookup)
                                lookupKey = m?.LookupKey
                                    ?? currentLookupKeys.FirstOrDefault(k => string.Equals(k.LogicalName, col.LogicalName, StringComparison.OrdinalIgnoreCase))
                                    ?? new PushLookupMatchKey { LogicalName = col.LogicalName, Mode = "Guid", Fields = new List<string>() };
                        }
                        else
                        {
                            include = savedSelected == null || savedSelected.Contains(col.LogicalName, StringComparer.OrdinalIgnoreCase);
                            targetAttr = col.LogicalName;
                            if (isLookup)
                                lookupKey = currentLookupKeys.FirstOrDefault(k => string.Equals(k.LogicalName, col.LogicalName, StringComparison.OrdinalIgnoreCase))
                                    ?? new PushLookupMatchKey { LogicalName = col.LogicalName, Mode = "Guid", Fields = new List<string>() };
                        }

                        var colDisplay = string.IsNullOrEmpty(col.DisplayName) ? col.LogicalName : $"{col.LogicalName} ({col.DisplayName})";
                        var modeDisplay = isLookup ? LookupModeToDisplay(lookupKey?.Mode ?? "Guid") : "-";
                        var configDisplay = isLookup ? BuildLookupConfigDisplay(lookupKey) : "";

                        // Build row with per-cell types so non-lookup rows get plain text in Lookup Match.
                        // ReadOnly cannot be set before a cell is attached to a grid, so set it after Add().
                        var newRow = new DataGridViewRow();
                        newRow.Cells.Add(new DataGridViewCheckBoxCell());  // Include
                        newRow.Cells.Add(new DataGridViewTextBoxCell());   // SnapshotColumn

                        if (_targetTableAttributes.Any())
                        {
                            var taCbo = new DataGridViewComboBoxCell();
                            foreach (var attr in _targetTableAttributes)
                                taCbo.Items.Add(attr);
                            if (!string.IsNullOrEmpty(targetAttr) &&
                                !_targetTableAttributes.Contains(targetAttr, StringComparer.OrdinalIgnoreCase))
                                taCbo.Items.Add(targetAttr);
                            newRow.Cells.Add(taCbo);                       // TargetAttribute (combobox)
                        }
                        else
                        {
                            newRow.Cells.Add(new DataGridViewTextBoxCell()); // TargetAttribute (plain text)
                        }

                        {
                            var typeCbo = new DataGridViewComboBoxCell();
                            foreach (var t in ScalarTypeItems) typeCbo.Items.Add(t);
                            newRow.Cells.Add(typeCbo);                       // Type (combobox for all rows)
                        }

                        {
                            var lmCbo = new DataGridViewComboBoxCell();
                            lmCbo.Items.AddRange(new object[] { "-", "Use Source GUID", "Alternate Key", "Custom Columns", "Skip Field" });
                            newRow.Cells.Add(lmCbo);                       // LookupMode (combobox for all rows; "-" for non-lookup)
                        }

                        // RelatedTable: combobox for polymorphic lookups (multiple possible targets), plain text otherwise
                        List<string> rtTargets = null;
                        var isPolymorphic = isLookup && _lookupTargets.TryGetValue(col.LogicalName, out rtTargets) && rtTargets?.Count > 1;
                        if (isPolymorphic)
                        {
                            var rtCbo = new DataGridViewComboBoxCell();
                            foreach (var t in rtTargets) rtCbo.Items.Add(t);
                            newRow.Cells.Add(rtCbo);                       // RelatedTable (combobox)
                        }
                        else
                        {
                            newRow.Cells.Add(new DataGridViewTextBoxCell()); // RelatedTable (plain)
                        }

                        newRow.Cells.Add(new DataGridViewTextBoxCell());   // Config
                        newRow.Cells.Add(new DataGridViewTextBoxCell());   // Sample

                        var rowIdx = _colMappingGrid.Rows.Add(newRow);

                        // Set values and ReadOnly AFTER the row is part of the grid
                        var desiredRelTable = isLookup ? (lookupKey?.TargetRelatedTable ?? col.RelatedTable ?? "") : "";
                        // For combobox cells, ensure the value is one of the allowed items
                        var rtCell = _colMappingGrid.Rows[rowIdx].Cells["RelatedTable"];
                        string relatedTableDisplay;
                        if (rtCell is DataGridViewComboBoxCell rtCboCell)
                            relatedTableDisplay = rtCboCell.Items.Contains(desiredRelTable) ? desiredRelTable
                                : (rtCboCell.Items.Count > 0 ? rtCboCell.Items[0].ToString() : "");
                        else
                            relatedTableDisplay = desiredRelTable;

                        _colMappingGrid.Rows[rowIdx].Cells["Include"].Value = include;
                        _colMappingGrid.Rows[rowIdx].Cells["SnapshotColumn"].Value = colDisplay;
                        _colMappingGrid.Rows[rowIdx].Cells["SnapshotColumn"].ReadOnly = true;
                        _colMappingGrid.Rows[rowIdx].Cells["TargetAttribute"].Value = targetAttr;
                        _colMappingGrid.Rows[rowIdx].Cells["Type"].Value = TypeToDisplay(col);
                        if (isLookup) _colMappingGrid.Rows[rowIdx].Cells["Type"].ReadOnly = true;
                        _colMappingGrid.Rows[rowIdx].Cells["LookupMode"].Value = modeDisplay;
                        _colMappingGrid.Rows[rowIdx].Cells["RelatedTable"].Value = relatedTableDisplay;
                        _colMappingGrid.Rows[rowIdx].Cells["Config"].Value = configDisplay;
                        _colMappingGrid.Rows[rowIdx].Cells["Sample"].Value = "";
                        _colMappingGrid.Rows[rowIdx].Cells["Sample"].ReadOnly = true;
                        if (!isLookup)
                        {
                            _colMappingGrid.Rows[rowIdx].Cells["LookupMode"].ReadOnly = true;
                            _colMappingGrid.Rows[rowIdx].Cells["RelatedTable"].ReadOnly = true;
                            _colMappingGrid.Rows[rowIdx].Cells["Config"].ReadOnly = true;
                        }
                        else if (!isPolymorphic && !string.IsNullOrWhiteSpace(relatedTableDisplay))
                        {
                            // Single-target lookup with a known target: RelatedTable is informational only.
                            // When the target is unknown (e.g. a generic Excel/JSON load with no metadata),
                            // leave the cell editable so the user can type the related table logical name —
                            // otherwise Alternate Key / Custom Columns matching has no way to be configured.
                            _colMappingGrid.Rows[rowIdx].Cells["RelatedTable"].ReadOnly = true;
                        }
                        _colMappingGrid.Rows[rowIdx].Tag = new ColumnMappingRowData { Column = col, LookupKey = lookupKey };
                    }
                }

                UpdateSampleSubset();

                _colMappingGrid.CellEndEdit += ColMappingGrid_CellEndEdit;

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

            if (_colMappingGrid == null) return;
            foreach (DataGridViewRow row in _colMappingGrid.Rows)
            {
                var data = row.Tag as ColumnMappingRowData;
                if (data == null) continue;
                var cell = row.Cells["SnapshotColumn"];
                if (isAlt && _cboAltKey.SelectedItem is AltKeyItem altKey
                    && altKey.Option.Fields.Contains(data.Column.LogicalName, StringComparer.OrdinalIgnoreCase))
                    cell.Style.ForeColor = Color.DodgerBlue;
                else if (isCustom && _customFields.Contains(data.Column.LogicalName, StringComparer.OrdinalIgnoreCase))
                    cell.Style.ForeColor = Color.DodgerBlue;
                else
                    cell.Style.ForeColor = SystemColors.WindowText;
            }
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

        private void ColMappingGrid_CellEndEdit(object sender, DataGridViewCellEventArgs e)
        {
            if (e.ColumnIndex < 0) return;
            var colName = _colMappingGrid.Columns[e.ColumnIndex].Name;

            if (colName == "Type")
            {
                var row = _colMappingGrid.Rows[e.RowIndex];
                var data = row.Tag as ColumnMappingRowData;
                if (data?.Column == null) return;

                var display = row.Cells["Type"].Value?.ToString() ?? "";
                var (newType, newExportMode) = DisplayToType(display);

                var wasLookup = data.IsLookup;
                var nowLookup = newType == "Lookup" || newType == "Owner" || newType == "Customer";

                if (!string.Equals(data.Column.Type, newType, StringComparison.OrdinalIgnoreCase)
                    || !string.Equals(data.Column.ExportMode, newExportMode, StringComparison.OrdinalIgnoreCase))
                {
                    data.Column.Type = newType;
                    data.Column.ExportMode = newExportMode;
                    data.Column.SqliteType = SqliteProjectService.GetSqliteType(newType);

                    if (nowLookup && !wasLookup)
                    {
                        // Enable RelatedTable so user can set the related entity
                        row.Cells["RelatedTable"].ReadOnly = false;
                        row.Cells["RelatedTable"].Value = data.Column.RelatedTable ?? "";
                        row.Cells["LookupMode"].ReadOnly = false;
                        row.Cells["LookupMode"].Value = "Use Source GUID";
                        row.Cells["Config"].ReadOnly = false;
                        data.LookupKey = new PushLookupMatchKey
                        {
                            LogicalName = data.Column.LogicalName,
                            Mode = "Guid",
                            Fields = new List<string>()
                        };
                    }
                    else if (!nowLookup && wasLookup)
                    {
                        row.Cells["LookupMode"].Value = "-";
                        row.Cells["LookupMode"].ReadOnly = true;
                        row.Cells["RelatedTable"].Value = "";
                        row.Cells["RelatedTable"].ReadOnly = true;
                        row.Cells["Config"].Value = "";
                        row.Cells["Config"].ReadOnly = true;
                        data.LookupKey = null;
                    }

                    SchedulePreviewRefresh();
                }
                return;
            }

            if (colName != "LookupMode" && colName != "RelatedTable") return;
            var row2 = _colMappingGrid.Rows[e.RowIndex];
            var data2 = row2.Tag as ColumnMappingRowData;
            if (data2?.IsLookup != true) return;

            if (colName == "RelatedTable")
            {
                HandleRelatedTableChanged(row2, data2);
                return;
            }

            var modeVal = row2.Cells["LookupMode"].Value?.ToString() ?? "Use Source GUID";
            if (modeVal == "-") { row2.Cells["LookupMode"].Value = LookupModeToDisplay(data2.LookupKey?.Mode ?? "Guid"); return; }

            var effectiveTable = data2.LookupKey?.TargetRelatedTable ?? data2.Column.RelatedTable ?? "";

            if (string.Equals(modeVal, "Alternate Key", StringComparison.OrdinalIgnoreCase))
            {
                if (string.IsNullOrWhiteSpace(effectiveTable))
                {
                    MessageBox.Show(this, "Set the Related Table before selecting a match method.", "Related Table Required", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    row2.Cells["LookupMode"].Value = LookupModeToDisplay(data2.LookupKey.Mode);
                    return;
                }
                row2.Cells["LookupMode"].Value = LookupModeToDisplay(data2.LookupKey.Mode); // revert while loading
                FetchRelatedTableInfoThenShowAltKeyPicker(effectiveTable, row2, data2);
            }
            else if (string.Equals(modeVal, "Custom Columns", StringComparison.OrdinalIgnoreCase))
            {
                if (string.IsNullOrWhiteSpace(effectiveTable))
                {
                    MessageBox.Show(this, "Set the Related Table before selecting a match method.", "Related Table Required", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    row2.Cells["LookupMode"].Value = LookupModeToDisplay(data2.LookupKey.Mode);
                    return;
                }
                row2.Cells["LookupMode"].Value = LookupModeToDisplay(data2.LookupKey.Mode); // revert while loading
                FetchRelatedTableInfoThenShowCustomPicker(effectiveTable, row2, data2);
            }
            else if (string.Equals(modeVal, "Skip Field", StringComparison.OrdinalIgnoreCase))
            {
                data2.LookupKey.Mode = "Skip";
                data2.LookupKey.AlternateKeyName = null;
                data2.LookupKey.Fields = new List<string>();
                row2.Cells["Config"].Value = "";
                RefreshPreview();
            }
            else // Use Source GUID
            {
                data2.LookupKey.Mode = "Guid";
                data2.LookupKey.AlternateKeyName = null;
                data2.LookupKey.Fields = new List<string>();
                row2.Cells["Config"].Value = "";
                RefreshPreview();
            }
        }

        private void FetchRelatedTableInfoThenShowAltKeyPicker(string tableName, DataGridViewRow row, ColumnMappingRowData data)
        {
            if (_relatedTableData.TryGetValue(tableName, out var cached) && cached.AltKeys != null)
            {
                ShowAltKeyPicker(tableName, cached.AltKeys, row, data);
                return;
            }
            var client = GetSelectedTargetClient();
            if (client == null) { MessageBox.Show(this, "Connect to a target environment first.", "No Connection", MessageBoxButtons.OK, MessageBoxIcon.Warning); return; }
            var project = _project;
            Task.Run(() =>
            {
                var repo = new CrmRepo(client);
                var info = EnsureRelatedTableInfo(tableName, project, repo);
                return info.AltKeys;
            }).ContinueWith(t => BeginInvoke((Action)(() =>
            {
                if (t.IsFaulted) { MessageBox.Show(this, $"Failed to load alternate keys: {t.Exception?.InnerException?.Message}", "Error", MessageBoxButtons.OK, MessageBoxIcon.Warning); return; }
                ShowAltKeyPicker(tableName, t.Result, row, data);
            })), TaskScheduler.Default);
        }

        private void FetchRelatedTableInfoThenShowCustomPicker(string tableName, DataGridViewRow row, ColumnMappingRowData data)
        {
            if (_relatedTableData.TryGetValue(tableName, out var cached)
                && (cached.SnapshotColumns?.Any() == true || cached.TargetColumns?.Any() == true))
            {
                ShowCustomColsPicker(tableName, cached, row, data);
                return;
            }
            var client = GetSelectedTargetClient();
            if (client == null) { MessageBox.Show(this, "Connect to a target environment first.", "No Connection", MessageBoxButtons.OK, MessageBoxIcon.Warning); return; }
            var project = _project;
            Task.Run(() =>
            {
                var repo = new CrmRepo(client);
                return EnsureRelatedTableInfo(tableName, project, repo);
            }).ContinueWith(t => BeginInvoke((Action)(() =>
            {
                if (t.IsFaulted) { MessageBox.Show(this, $"Failed to load table columns: {t.Exception?.InnerException?.Message}", "Error", MessageBoxButtons.OK, MessageBoxIcon.Warning); return; }
                ShowCustomColsPicker(tableName, t.Result, row, data);
            })), TaskScheduler.Default);
        }

        private LookupRelatedTableInfo EnsureRelatedTableInfo(string tableName, SqliteProjectService project, CrmRepo repo)
        {
            if (_relatedTableData.TryGetValue(tableName, out var existing)) return existing;
            var info = new LookupRelatedTableInfo();
            try
            {
                var relSnap = project?.GetSnapshots()?.FirstOrDefault(s =>
                    string.Equals(s.TableLogicalName, tableName, StringComparison.OrdinalIgnoreCase));
                info.SnapshotColumns = relSnap?.ColumnConfig?
                    .Where(c => !c.LogicalName.StartsWith("_")).ToList()
                    ?? new List<DataTableColumnConfig>();
            }
            catch { info.SnapshotColumns = new List<DataTableColumnConfig>(); }
            try
            {
                var rawKeys = repo.GetAlternateKeys(tableName);
                var fieldFilter = info.SnapshotColumns?.Any() == true
                    ? info.SnapshotColumns.Select(c => c.LogicalName)
                    : (IEnumerable<string>)null;
                info.AltKeys = ImportPreviewService.GetAvailableImportAlternateKeys(rawKeys, fieldFilter);
            }
            catch { info.AltKeys = new List<ExcelImportAlternateKeyOption>(); }
            try
            {
                info.TargetColumns = DataMigrationControl.BuildLookupTargetColumnConfigs(
                    repo.GetTableMetadata(tableName)?.Attributes);
            }
            catch { info.TargetColumns = new List<DataTableColumnConfig>(); }
            _relatedTableData[tableName] = info;
            return info;
        }

        private void ShowAltKeyPicker(string tableName, List<ExcelImportAlternateKeyOption> altKeys, DataGridViewRow row, ColumnMappingRowData data)
        {
            if (!altKeys.Any())
            {
                MessageBox.Show(this, $"No alternate keys found for '{tableName}' in the target environment.", "Alternate Key", MessageBoxButtons.OK, MessageBoxIcon.Information);
                row.Cells["LookupMode"].Value = LookupModeToDisplay(data.LookupKey.Mode);
                return;
            }
            using (var dlg = new LookupAltKeyPickerDialog($"Alternate Key for '{tableName}'", altKeys, data.LookupKey.AlternateKeyName))
            {
                if (dlg.ShowDialog(this) == DialogResult.OK)
                {
                    data.LookupKey.Mode = "AlternateKey";
                    data.LookupKey.AlternateKeyName = dlg.SelectedKeyName;
                    data.LookupKey.Fields = dlg.SelectedFields;
                    row.Cells["LookupMode"].Value = LookupModeToDisplay("AlternateKey");
                    row.Cells["Config"].Value = BuildLookupConfigDisplay(data.LookupKey);
                    RefreshPreview();
                }
                else
                {
                    row.Cells["LookupMode"].Value = LookupModeToDisplay(data.LookupKey.Mode);
                }
            }
        }

        private void ShowCustomColsPicker(string tableName, LookupRelatedTableInfo tableInfo, DataGridViewRow row, ColumnMappingRowData data)
        {
            var cols = tableInfo?.SnapshotColumns?.Any() == true
                ? tableInfo.SnapshotColumns
                : tableInfo?.TargetColumns ?? new List<DataTableColumnConfig>();
            if (!cols.Any())
            {
                MessageBox.Show(this, $"No columns available for '{tableName}'. Load that table in the target environment first or add a snapshot for the table to the project.", "Custom Columns", MessageBoxButtons.OK, MessageBoxIcon.Information);
                row.Cells["LookupMode"].Value = LookupModeToDisplay(data.LookupKey.Mode);
                return;
            }
            using (var dlg = new LookupCustomFieldsPickerDialog($"Custom match columns for '{tableName}'", cols,
                string.Equals(data.LookupKey.Mode, "Custom", StringComparison.OrdinalIgnoreCase) ? data.LookupKey.Fields : null))
            {
                if (dlg.ShowDialog(this) == DialogResult.OK && dlg.SelectedFields.Any())
                {
                    data.LookupKey.Mode = "Custom";
                    data.LookupKey.AlternateKeyName = null;
                    data.LookupKey.Fields = dlg.SelectedFields;
                    row.Cells["LookupMode"].Value = LookupModeToDisplay("Custom");
                    row.Cells["Config"].Value = BuildLookupConfigDisplay(data.LookupKey);
                    RefreshPreview();
                }
                else
                {
                    row.Cells["LookupMode"].Value = LookupModeToDisplay(data.LookupKey.Mode);
                }
            }
        }

        private void HandleRelatedTableChanged(DataGridViewRow row, ColumnMappingRowData data)
        {
            if (data.LookupKey == null) return;

            var newTable = row.Cells["RelatedTable"].Value?.ToString()?.Trim() ?? "";
            var previousTable = data.LookupKey.TargetRelatedTable ?? data.Column.RelatedTable ?? "";
            if (string.Equals(newTable, previousTable, StringComparison.OrdinalIgnoreCase)) return;

            var previousMode = data.LookupKey.Mode?.ToLowerInvariant() ?? "guid";

            data.LookupKey.TargetRelatedTable = string.IsNullOrEmpty(newTable)
                || string.Equals(newTable, data.Column.RelatedTable, StringComparison.OrdinalIgnoreCase)
                ? null : newTable;
            data.LookupKey.AlternateKeyName = null;
            data.LookupKey.Fields = new List<string>();

            if (previousMode == "alternatekey" || previousMode == "custom")
            {
                data.LookupKey.Mode = "Guid";
                row.Cells["LookupMode"].Value = "Use Source GUID";
                row.Cells["Config"].Value = "";

                if (!string.IsNullOrEmpty(newTable))
                {
                    if (previousMode == "alternatekey")
                        FetchRelatedTableInfoThenShowAltKeyPicker(newTable, row, data);
                    else
                        FetchRelatedTableInfoThenShowCustomPicker(newTable, row, data);
                    return; // RefreshPreview called by the picker helpers after user confirms
                }
            }
            else
            {
                row.Cells["Config"].Value = "";
            }

            RefreshPreview();
        }

        private void UpdateHeaderLabel()
        {
            if (_headerLabel == null) return;
            var tln = _txtTable?.Text?.Trim() ?? _step?.Table?.LogicalName ?? string.Empty;
            var tableDisplay = string.IsNullOrWhiteSpace(tln) ? "(not set)" : tln;
            _headerLabel.Text = _snapshot != null
                ? $"Snapshot: {_snapshot.Name}   |   Table: {tableDisplay}   |   Rows: {_snapshot.RowCount:N0}   |   Columns: {_snapshot.ColumnConfig?.Count ?? 0}"
                : "Configure push step settings";
        }

        private void SetTableControlsEnabled(bool enabled)
        {
            _create.Enabled = enabled;
            _update.Enabled = enabled;
            _cboMatchMode.Enabled = enabled;
            _cboAltKey.Enabled = enabled;
            _btnCustomFields.Enabled = enabled;
            _batchSize.Enabled = enabled;
            _stopOnFatalError.Enabled = enabled;
            _maxFailedRecords.Enabled = enabled;
            _maxFailedPercent.Enabled = enabled;
            _colMappingGrid.Enabled = enabled;
            if (_btnAccept != null) _btnAccept.Enabled = enabled;
        }

        private void LoadTableConfiguration(string tableName)
        {
            if (string.IsNullOrWhiteSpace(tableName))
            {
                MessageBox.Show(this, "Enter the Dataverse table logical name.", "Table Required", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            var client = GetSelectedTargetClient();
            if (client == null)
            {
                MessageBox.Show(this, "Connect to a target environment before loading table configuration.", "No Connection", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            _btnLoadTable.Enabled = false;
            _txtTable.ReadOnly = true;

            var availableFields = _snapshot?.ColumnConfig?.Select(c => c.LogicalName) ?? Enumerable.Empty<string>();
            Task.Run(() =>
            {
                var repo = new CrmRepo(client);
                var keys = new List<ExcelImportAlternateKeyOption>();
                try
                {
                    var rawKeys = repo.GetAlternateKeys(tableName);
                    keys = ImportPreviewService.GetAvailableImportAlternateKeys(rawKeys, availableFields);
                }
                catch { }
                return keys;
            }).ContinueWith(t =>
            {
                BeginInvoke((Action)(() =>
                {
                    _btnLoadTable.Enabled = true;
                    _txtTable.ReadOnly = false;

                    if (t.IsFaulted) return;

                    _cboAltKey.Items.Clear();
                    foreach (var k in t.Result)
                        _cboAltKey.Items.Add(new AltKeyItem(k));

                    if (_step.Table == null) _step.Table = new DmtTableInfo();
                    _step.Table.LogicalName = tableName;

                    // Persist table name to snapshot immediately so PreviewPush reads it from SQLite correctly
                    if (_snapshot != null && !string.Equals(_snapshot.TableLogicalName, tableName, StringComparison.OrdinalIgnoreCase))
                    {
                        _snapshot.TableLogicalName = tableName;
                        try { _project?.SaveSnapshot(_snapshot); } catch { }
                    }

                    UpdateHeaderLabel();
                    SetTableControlsEnabled(true);
                    UpdateMatchModeUI();
                }));
            }, TaskScheduler.Default);
        }

        private void Apply()
        {
            _colMappingGrid?.EndEdit();
            _step.Table = _step.Table ?? new DmtTableInfo();
            _step.Table.LogicalName = _txtTable?.Text?.Trim() ?? _step.Table.LogicalName;
            var selectedTarget = GetSelectedTargetEnvironment();
            if (selectedTarget != null)
            {
                _step.TargetEnvironment = new DmtEnvironmentInfo
                {
                    UniqueName = selectedTarget.UniqueName,
                    FriendlyName = selectedTarget.FriendlyName,
                    Tag = selectedTarget.Tag
                };
                _targetEnvId = selectedTarget.UniqueName ?? string.Empty;
            }

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

            // Build ColumnMappings from grid; also derive SelectedColumns + LookupMatchKeys for backward compatibility
            var columnMappings = new List<ColumnAttributeMapping>();
            var lookupKeys = new List<PushLookupMatchKey>();
            foreach (DataGridViewRow row in _colMappingGrid.Rows)
            {
                var data = row.Tag as ColumnMappingRowData;
                if (data == null) continue;
                var included = row.Cells["Include"].Value is bool b && b;
                var targetAttr = row.Cells["TargetAttribute"].Value?.ToString()?.Trim() ?? "";
                if (string.Equals(targetAttr, data.Column.LogicalName, StringComparison.OrdinalIgnoreCase))
                    targetAttr = null;

                if (data.IsLookup && data.LookupKey != null)
                {
                    var relatedTableVal = row.Cells["RelatedTable"].Value?.ToString()?.Trim() ?? "";
                    data.LookupKey.TargetRelatedTable = string.IsNullOrEmpty(relatedTableVal)
                        || string.Equals(relatedTableVal, data.Column.RelatedTable, StringComparison.OrdinalIgnoreCase)
                        ? null : relatedTableVal;
                }

                columnMappings.Add(new ColumnAttributeMapping
                {
                    SnapshotColumn = data.Column.LogicalName,
                    DataverseAttribute = string.IsNullOrEmpty(targetAttr) ? null : targetAttr,
                    Include = included,
                    LookupKey = data.LookupKey
                });
                if (data.IsLookup && data.LookupKey != null)
                    lookupKeys.Add(data.LookupKey);
            }
            _step.Snapshot.ColumnMappings = columnMappings.Any() ? columnMappings : null;

            var deselected = columnMappings.Where(m => !m.Include).Select(m => m.SnapshotColumn).ToList();
            if (deselected.Any())
            {
                var allCols = _snapshot?.ColumnConfig?
                    .Where(c => !c.LogicalName.StartsWith("_", StringComparison.OrdinalIgnoreCase))
                    .Select(c => c.LogicalName)
                    .ToList() ?? new List<string>();
                _step.Snapshot.SelectedColumns = allCols
                    .Where(c => !deselected.Contains(c, StringComparer.OrdinalIgnoreCase))
                    .ToList();
            }
            else
            {
                _step.Snapshot.SelectedColumns = null;
            }
            _step.Snapshot.LookupMatchKeys = lookupKeys.Any() ? lookupKeys : null;

            // Persist any column type changes made in the grid
            if (_snapshot != null)
                try { _project?.SaveSnapshot(_snapshot); } catch { }

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
                    IsStale = _deferredPreviewRefresh || (_previewRefreshTimer?.Enabled == true)
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
