// System
using System;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;

// DataMigrationTool
using Dataverse.XrmTools.DataMigrationTool.Models;
using Dataverse.XrmTools.DataMigrationTool.Logic;

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
            ExecutionPlanFileService.ValidatePlan(_plan);
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
}
