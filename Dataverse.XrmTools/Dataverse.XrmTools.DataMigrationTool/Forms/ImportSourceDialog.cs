// System
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;

// DataMigrationTool
using Dataverse.XrmTools.DataMigrationTool.Models;

namespace Dataverse.XrmTools.DataMigrationTool.Forms
{
    public enum ImportSourceChoice
    {
        Cancel,
        File,
        LinkedPlanStep
    }

    public class ImportSourceDialog : Form
    {
        private readonly List<ExecutionPlanStep> _linkedSteps;
        private readonly string _fileFilter;
        private readonly string _title;
        private readonly ListBox _steps;
        private readonly RadioButton _rbFile;
        private readonly RadioButton _rbLinked;

        public ImportSourceChoice Choice { get; private set; } = ImportSourceChoice.Cancel;
        public string FilePath { get; private set; }
        public ExecutionPlanStep SelectedStep { get; private set; }

        public ImportSourceDialog(string title, string fileFilter, IEnumerable<ExecutionPlanStep> linkedSteps)
        {
            _title = title;
            _fileFilter = fileFilter;
            _linkedSteps = (linkedSteps ?? Enumerable.Empty<ExecutionPlanStep>()).ToList();

            Text = title;
            StartPosition = FormStartPosition.CenterParent;
            ShowIcon = false;
            ShowInTaskbar = false;
            ClientSize = new Size(560, 330);
            MinimumSize = new Size(520, 300);

            var layout = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                RowCount = 4,
                ColumnCount = 1,
                Padding = new Padding(10)
            };
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 34));
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 34));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 48));

            _rbFile = new RadioButton
            {
                Text = "Select a file",
                Checked = !_linkedSteps.Any(),
                AutoSize = true,
                Margin = new Padding(0, 4, 0, 4)
            };
            _rbLinked = new RadioButton
            {
                Text = "Use output from an execution plan export step",
                AutoSize = true,
                Margin = new Padding(0, 4, 0, 4),
                Enabled = _linkedSteps.Any(),
                Checked = _linkedSteps.Any()
            };

            var options = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 1, ColumnCount = 2 };
            options.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 120));
            options.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            options.Controls.Add(_rbFile);
            options.Controls.Add(_rbLinked);

            _steps = new ListBox
            {
                Dock = DockStyle.Fill,
                DrawMode = DrawMode.OwnerDrawFixed,
                SelectionMode = SelectionMode.One
            };
            _steps.DrawItem += DrawLinkedStepItem;
            foreach (var step in _linkedSteps)
                _steps.Items.Add(new LinkedStepListItem(step));
            if (_steps.Items.Count > 0) _steps.SelectedIndex = 0;

            _rbFile.CheckedChanged += (s, e) => RenderState();
            _rbLinked.CheckedChanged += (s, e) => RenderState();

            var note = new Label
            {
                Text = _linkedSteps.Any()
                    ? "Linked imports are added directly to the active execution plan."
                    : "No compatible export steps are available in the active execution plan.",
                Dock = DockStyle.Fill,
                TextAlign = ContentAlignment.MiddleLeft
            };

            var buttons = new FlowLayoutPanel
            {
                Dock = DockStyle.Fill,
                FlowDirection = FlowDirection.RightToLeft,
                Padding = new Padding(0, 8, 0, 0)
            };
            var cancel = new Button { Text = "Cancel", Width = 90, DialogResult = DialogResult.Cancel };
            var continueButton = new Button { Text = "Continue", Width = 90 };
            continueButton.Click += Continue;
            buttons.Controls.Add(cancel);
            buttons.Controls.Add(continueButton);

            layout.Controls.Add(options, 0, 0);
            layout.Controls.Add(_steps, 0, 1);
            layout.Controls.Add(note, 0, 2);
            layout.Controls.Add(buttons, 0, 3);
            Controls.Add(layout);

            AcceptButton = continueButton;
            CancelButton = cancel;
            RenderState();
        }

        private void RenderState()
        {
            _steps.Enabled = _rbLinked.Checked && _linkedSteps.Any();
            _steps.BackColor = _steps.Enabled ? SystemColors.Window : SystemColors.Control;
            _steps.ForeColor = SystemColors.WindowText;
        }

        private void DrawLinkedStepItem(object sender, DrawItemEventArgs e)
        {
            e.DrawBackground();
            if (e.Index < 0) return;

            var selected = (e.State & DrawItemState.Selected) == DrawItemState.Selected;
            var textColor = selected && _steps.Enabled
                ? SystemColors.HighlightText
                : SystemColors.WindowText;
            TextRenderer.DrawText(
                e.Graphics,
                _steps.Items[e.Index].ToString(),
                e.Font,
                e.Bounds,
                textColor,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
            e.DrawFocusRectangle();
        }

        private void Continue(object sender, EventArgs e)
        {
            if (_rbLinked.Checked)
            {
                var selected = _steps.SelectedItem as LinkedStepListItem;
                if (selected == null)
                {
                    MessageBox.Show(this, "Select an export step to link.", _title, MessageBoxButtons.OK, MessageBoxIcon.Information);
                    return;
                }

                Choice = ImportSourceChoice.LinkedPlanStep;
                SelectedStep = selected.Step;
                DialogResult = DialogResult.OK;
                Close();
                return;
            }

            using (var dialog = new OpenFileDialog { Filter = _fileFilter, CheckFileExists = true })
            {
                if (dialog.ShowDialog(this) != DialogResult.OK) return;
                Choice = ImportSourceChoice.File;
                FilePath = dialog.FileName;
                DialogResult = DialogResult.OK;
                Close();
            }
        }

        private class LinkedStepListItem
        {
            public ExecutionPlanStep Step { get; }

            public LinkedStepListItem(ExecutionPlanStep step)
            {
                Step = step;
            }

            public override string ToString()
            {
                var path = Step.Output?.PathTemplate ?? "(no output path)";
                return $"{Step.Name} - {Step.Table?.LogicalName} - {path}";
            }
        }
    }
}
