// System
using System;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;

// XrmToolBox
using XrmToolBox.Extensibility;

// DataMigrationTool
using Dataverse.XrmTools.DataMigrationTool.Models;

namespace Dataverse.XrmTools.DataMigrationTool
{
    public partial class DataMigrationControl
    {
        #region History Fields

        private ToolStripMenuItem _tsmiHistory;

        #endregion

        #region History Initialization

        private void InitializeHistoryPanel()
        {
            _tsmiHistory = new ToolStripMenuItem("View Run History")
            {
                Image = Properties.Resources.preview,
                ImageScaling = ToolStripItemImageScaling.None
            };
            _tsmiHistory.Click += (s, e) => ShowRunHistory();
            _tsmiProject.DropDownItems.Add(new ToolStripSeparator());
            _tsmiProject.DropDownItems.Add(_tsmiHistory);
        }

        #endregion

        #region History Operations

        private void ShowRunHistory()
        {
            if (_project?.Service == null)
            {
                MessageBox.Show(this, "Open or create a project first.", "No Project", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            var logs = _project.Service.GetRunLogs();
            if (logs == null || !logs.Any())
            {
                MessageBox.Show(this, "No run history in this project.", "History", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            using (var dlg = new Form())
            {
                dlg.Text = "Run History";
                dlg.Size = new System.Drawing.Size(760, 480);
                dlg.StartPosition = FormStartPosition.CenterParent;
                dlg.ShowIcon = false;
                dlg.ShowInTaskbar = false;

                var split = new SplitContainer
                {
                    Dock = DockStyle.Fill,
                    Orientation = Orientation.Vertical,
                    SplitterDistance = 340,
                    Panel1MinSize = 200,
                    Panel2MinSize = 150
                };

                // Left: log list
                var grid = new ListView
                {
                    Dock = DockStyle.Fill,
                    View = View.Details,
                    FullRowSelect = true,
                    GridLines = true,
                    MultiSelect = false
                };
                grid.Columns.Add("Started", 140);
                grid.Columns.Add("Name", 150);
                grid.Columns.Add("Status", 90);
                grid.Columns.Add("Completed", 140);

                foreach (var log in logs)
                {
                    var item = new ListViewItem(log.StartedOn.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss"));
                    item.SubItems.Add(log.PlanName ?? "(standalone)");
                    item.SubItems.Add(log.Status ?? "");
                    item.SubItems.Add(log.CompletedOn?.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss") ?? "");
                    item.Tag = log;
                    if (log.Status == "Failed")
                        item.ForeColor = Color.Red;
                    else if (log.Status == "CompletedWithErrors")
                        item.ForeColor = Color.DarkOrange;
                    grid.Items.Add(item);
                }
                split.Panel1.Controls.Add(grid);

                // Right: detail
                var detail = new RichTextBox
                {
                    Dock = DockStyle.Fill,
                    ReadOnly = true,
                    Font = new Font("Consolas", 9),
                    BorderStyle = BorderStyle.None,
                    BackColor = SystemColors.Control
                };
                split.Panel2.Controls.Add(detail);

                grid.SelectedIndexChanged += (s, e) =>
                {
                    if (grid.SelectedItems.Count == 0) { detail.Clear(); return; }
                    var log = grid.SelectedItems[0].Tag as DmtRunLog;
                    if (log == null) return;

                    detail.Clear();
                    detail.AppendText($"Plan: {log.PlanName}\r\n");
                    detail.AppendText($"Status: {log.Status}\r\n");
                    detail.AppendText($"Started: {log.StartedOn:O}\r\n");
                    if (log.CompletedOn.HasValue)
                        detail.AppendText($"Completed: {log.CompletedOn:O}\r\n");

                    if (log.Log?.Steps?.Any() == true)
                    {
                        detail.AppendText("\r\nSteps:\r\n");
                        foreach (var step in log.Log.Steps)
                        {
                            detail.AppendText($"\r\n  [{step.Status}] {step.Name}\r\n");
                            detail.AppendText($"    {step.Summary}\r\n");
                            if (step.ErrorDetails?.Any() == true)
                            {
                                detail.AppendText("    Errors:\r\n");
                                foreach (var err in step.ErrorDetails.Take(10))
                                    detail.AppendText($"    - {err}\r\n");
                                if (step.ErrorDetails.Count > 10)
                                    detail.AppendText($"    ... and {step.ErrorDetails.Count - 10} more.\r\n");
                            }
                        }
                    }
                };

                var closeBtn = new Button { Text = "Close", DialogResult = DialogResult.OK, Width = 80 };
                var btnPanel = new FlowLayoutPanel
                {
                    Dock = DockStyle.Bottom,
                    FlowDirection = FlowDirection.RightToLeft,
                    Height = 36,
                    Padding = new Padding(4)
                };
                btnPanel.Controls.Add(closeBtn);

                dlg.Controls.Add(split);
                dlg.Controls.Add(btnPanel);
                dlg.AcceptButton = closeBtn;

                if (grid.Items.Count > 0) grid.Items[0].Selected = true;

                dlg.ShowDialog(ParentForm);
            }
        }

        #endregion
    }
}
