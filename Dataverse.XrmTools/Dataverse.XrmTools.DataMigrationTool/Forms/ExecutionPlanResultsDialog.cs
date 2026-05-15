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
            MinimizeBox = false;
            MaximizeBox = true;

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

            _details = new TextBox
            {
                Dock = DockStyle.Bottom,
                Height = 140,
                Multiline = true,
                ReadOnly = true,
                ScrollBars = ScrollBars.Vertical
            };

            var buttons = new FlowLayoutPanel
            {
                Dock = DockStyle.Bottom,
                Height = 44,
                FlowDirection = FlowDirection.RightToLeft,
                Padding = new Padding(8)
            };
            var close = new Button { Text = "Close", DialogResult = DialogResult.OK, Width = 90 };
            buttons.Controls.Add(close);

            Controls.Add(_steps);
            Controls.Add(_details);
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
                return;
            }

            _details.Text = string.Join(Environment.NewLine, new[]
            {
                step.Summary,
                string.IsNullOrWhiteSpace(step.Error) ? null : $"Error: {step.Error}",
                string.IsNullOrWhiteSpace(step.Path) ? null : $"Path: {step.Path}"
            }.Where(line => !string.IsNullOrWhiteSpace(line)));
        }
    }
}
