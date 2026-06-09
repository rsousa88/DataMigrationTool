// System
using System;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;

// DataMigrationTool
using Dataverse.XrmTools.DataMigrationTool.Models;

namespace Dataverse.XrmTools.DataMigrationTool.Forms
{
    public class ExecutionPlanResultsDialog : Form
    {
        private readonly ExecutionPlanRunLog _runLog;
        private ListView _steps;
        private TextBox _details;
        private Button _copyDetails;

        public ExecutionPlanResultsDialog(ExecutionPlanRunLog runLog)
        {
            _runLog = runLog ?? new ExecutionPlanRunLog();
            InitializeComponent();
            Render();
        }

        private void InitializeComponent()
        {
            Text = "Execution Plan Results";
            StartPosition = FormStartPosition.CenterParent;
            Size = new Size(980, 600);
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MinimizeBox = false;
            MaximizeBox = false;
            ShowIcon = false;

            _steps = new ListView
            {
                Dock = DockStyle.Fill,
                FullRowSelect = true,
                GridLines = true,
                HideSelection = false,
                View = View.Details
            };
            _steps.Columns.Add("#", 45);
            _steps.Columns.Add("Status", 90);
            _steps.Columns.Add("Operation", 120);
            _steps.Columns.Add("Step", 210);
            _steps.Columns.Add("Records", 90);
            _steps.Columns.Add("Failed", 90);
            _steps.Columns.Add("Path", 260);
            _steps.SelectedIndexChanged += (_, __) => RenderDetails();
            _steps.DoubleClick += (_, __) => CopyDetailsToClipboard();

            _details = new TextBox
            {
                Dock = DockStyle.Fill,
                Multiline = true,
                ReadOnly = true,
                ScrollBars = ScrollBars.Vertical,
                BackColor = SystemColors.Window
            };

            var listGroup = new GroupBox
            {
                Dock = DockStyle.Fill,
                Text = "Steps",
                Padding = new Padding(8)
            };
            listGroup.Controls.Add(_steps);

            var detailsGroup = new GroupBox
            {
                Dock = DockStyle.Fill,
                Text = "Selected step details",
                Padding = new Padding(8)
            };
            detailsGroup.Controls.Add(_details);

            var layout = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 1,
                RowCount = 2,
                Padding = new Padding(8, 8, 8, 0)
            };
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 68F));
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 32F));
            layout.Controls.Add(listGroup, 0, 0);
            layout.Controls.Add(detailsGroup, 0, 1);

            var buttons = new FlowLayoutPanel
            {
                Dock = DockStyle.Bottom,
                Height = 44,
                FlowDirection = FlowDirection.RightToLeft,
                Padding = new Padding(8)
            };
            var close = new Button { Text = "Close", DialogResult = DialogResult.OK, Width = 90 };
            _copyDetails = new Button { Text = "Copy details", Width = 105 };
            _copyDetails.Click += (_, __) => CopyDetailsToClipboard();
            buttons.Controls.Add(close);
            buttons.Controls.Add(_copyDetails);

            Controls.Add(layout);
            Controls.Add(buttons);
            AcceptButton = close;
            CancelButton = close;
        }

        private void Render()
        {
            _steps.Items.Clear();
            foreach (var step in _runLog.Steps.OrderBy(s => s.Index))
            {
                var failed = step.TotalRecords > 0
                    ? $"{step.FailedRecords} ({step.FailedPercent:0.##}%)"
                    : step.FailedRecords.ToString();
                var item = new ListViewItem(step.Index.ToString("00"));
                item.SubItems.Add(step.Status ?? string.Empty);
                item.SubItems.Add(step.Operation ?? string.Empty);
                item.SubItems.Add(step.Name ?? string.Empty);
                item.SubItems.Add(step.TotalRecords.ToString());
                item.SubItems.Add(failed);
                item.SubItems.Add(step.Path ?? string.Empty);
                item.Tag = step;
                if (IsProblemStep(step))
                    item.ForeColor = Color.DarkRed;
                _steps.Items.Add(item);
            }

            if (_steps.Items.Count > 0)
                _steps.Items[0].Selected = true;
            else
                RenderDetails();
        }

        private void RenderDetails()
        {
            var step = _steps.SelectedItems.Count > 0 ? _steps.SelectedItems[0].Tag as ExecutionPlanRunStepLog : null;
            if (step == null)
            {
                _details.Text = "No steps were executed.";
                _copyDetails.Enabled = false;
                return;
            }

            var lines = new[]
            {
                $"Step: {step.Index:00} - {step.Name}",
                $"Status: {step.Status}",
                string.IsNullOrWhiteSpace(step.Operation) ? null : $"Operation: {step.Operation}",
                step.TargetEnvironment == null ? null : $"Target: {step.TargetEnvironment.FriendlyName ?? step.TargetEnvironment.UniqueName}",
                $"Records: {step.TotalRecords}",
                $"Failed: {step.FailedRecords}" + (step.TotalRecords > 0 ? $" ({step.FailedPercent:0.##}%)" : string.Empty),
                step.Summary,
                string.IsNullOrWhiteSpace(step.Error) ? null : $"Error: {step.Error}",
                string.IsNullOrWhiteSpace(step.Path) ? null : $"Path: {step.Path}"
            }.Where(line => !string.IsNullOrWhiteSpace(line)).ToList();

            var errors = (step.ErrorDetails ?? new System.Collections.Generic.List<string>())
                .Where(error => !string.IsNullOrWhiteSpace(error))
                .ToList();
            if (errors.Any())
            {
                lines.Add(string.Empty);
                lines.Add("Error details:");
                for (var i = 0; i < errors.Count; i++)
                    lines.Add($"{i + 1}. {errors[i]}");
            }
            else if (step.FailedRecords > 0)
            {
                lines.Add(string.Empty);
                lines.Add("No row-level error details were captured for this step.");
            }

            _details.Text = string.Join(Environment.NewLine, lines);
            _copyDetails.Enabled = !string.IsNullOrWhiteSpace(_details.Text);
        }

        private bool IsProblemStep(ExecutionPlanRunStepLog step)
        {
            return step != null
                && (step.FailedRecords > 0
                    || !string.IsNullOrWhiteSpace(step.Error)
                    || string.Equals(step.Status, "Failed", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(step.Status, "Warning", StringComparison.OrdinalIgnoreCase));
        }

        private void CopyDetailsToClipboard()
        {
            if (string.IsNullOrWhiteSpace(_details?.Text)) return;
            Clipboard.SetText(_details.Text);
        }
    }
}
