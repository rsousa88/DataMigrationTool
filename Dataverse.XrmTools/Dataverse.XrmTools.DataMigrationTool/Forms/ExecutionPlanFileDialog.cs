// System
using System;
using System.Drawing;
using System.Windows.Forms;

namespace Dataverse.XrmTools.DataMigrationTool.Forms
{
    public enum ExecutionPlanFileChoice { NewFile, ExistingFile, Cancel }

    public class ExecutionPlanFileDialog : Form
    {
        public ExecutionPlanFileChoice Choice { get; private set; } = ExecutionPlanFileChoice.Cancel;
        public string FilePath { get; private set; }

        public ExecutionPlanFileDialog()
        {
            BuildUi();
        }

        private void BuildUi()
        {
            Text = "Execution Plan";
            FormBorderStyle = FormBorderStyle.FixedDialog;
            StartPosition = FormStartPosition.CenterParent;
            MinimizeBox = false;
            MaximizeBox = false;
            Size = new Size(420, 170);
            BackColor = SystemColors.Window;

            var pnl = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                Padding = new Padding(16, 12, 16, 12),
                RowCount = 2,
                ColumnCount = 1
            };
            pnl.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            pnl.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));

            pnl.Controls.Add(new Label
            {
                Text = "No execution plan is currently loaded.",
                Font = new Font("Segoe UI", 9f, FontStyle.Bold),
                AutoSize = true,
                Margin = new Padding(0, 0, 0, 8)
            }, 0, 0);

            var buttons = new FlowLayoutPanel
            {
                Dock = DockStyle.Fill,
                FlowDirection = FlowDirection.TopDown,
                WrapContents = false
            };

            var btnNew = MakeButton("Save new plan...", "Create a new .dmtplan.json execution plan");
            var btnLoad = MakeButton("Load existing plan...", "Open an existing .dmtplan.json execution plan");
            btnNew.Click += (s, e) => HandleNew();
            btnLoad.Click += (s, e) => HandleLoad();

            buttons.Controls.Add(btnNew);
            buttons.Controls.Add(btnLoad);
            pnl.Controls.Add(buttons, 0, 1);
            Controls.Add(pnl);

            KeyPreview = true;
            KeyDown += (s, e) =>
            {
                if (e.KeyCode == Keys.Escape)
                {
                    Choice = ExecutionPlanFileChoice.Cancel;
                    Close();
                }
            };
        }

        private Button MakeButton(string text, string tooltip)
        {
            var button = new Button
            {
                Text = text,
                Width = 360,
                Height = 32,
                FlatStyle = FlatStyle.Flat,
                TextAlign = ContentAlignment.MiddleLeft,
                Font = new Font("Segoe UI", 8.25f),
                Margin = new Padding(0, 0, 0, 4),
                Padding = new Padding(6, 0, 0, 0)
            };
            button.FlatAppearance.BorderColor = Color.LightGray;
            new ToolTip().SetToolTip(button, tooltip);
            return button;
        }

        private void HandleNew()
        {
            using (var dlg = new SaveFileDialog
            {
                Title = "Create execution plan",
                Filter = "DMT Execution Plan (*.dmtplan.json)|*.dmtplan.json",
                DefaultExt = "dmtplan.json",
                FileName = "migration-plan"
            })
            {
                if (dlg.ShowDialog(this) != DialogResult.OK) return;

                Choice = ExecutionPlanFileChoice.NewFile;
                FilePath = dlg.FileName;
                DialogResult = DialogResult.OK;
            }
        }

        private void HandleLoad()
        {
            using (var dlg = new OpenFileDialog
            {
                Title = "Load execution plan",
                Filter = "DMT Execution Plan (*.dmtplan.json)|*.dmtplan.json"
            })
            {
                if (dlg.ShowDialog(this) != DialogResult.OK) return;

                Choice = ExecutionPlanFileChoice.ExistingFile;
                FilePath = dlg.FileName;
                DialogResult = DialogResult.OK;
            }
        }
    }
}
