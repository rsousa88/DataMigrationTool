// System
using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Windows.Forms;

// XrmToolBox
using XrmToolBox.Extensibility.Args;

// DataMigrationTool
using Dataverse.XrmTools.DataMigrationTool.Forms;
using Dataverse.XrmTools.DataMigrationTool.Logic;
using Dataverse.XrmTools.DataMigrationTool.Models;

namespace Dataverse.XrmTools.DataMigrationTool
{
    public partial class DataMigrationControl
    {
        #region Snapshots Fields

        private SplitContainer _rightSplitContainer;
        private ListView _inlineSnapList;
        private DataGridView _inlineDataGrid;
        private System.Windows.Forms.Label _inlineDataPageLabel;
        private Button _inlineDataPrevButton;
        private Button _inlineDataNextButton;
        private DmtSnapshot _inlineSelectedSnapshot;
        private int _inlineDataCurrentPage;
        private int _inlineDataTotalRows;
        private const int InlinePageSize = 500;
        private bool _fittingSnapshotColumns;
        private ToolStripButton _snapMoveUpBtn;
        private ToolStripButton _snapMoveDownBtn;

        #endregion

        #region Snapshots Initialization

        internal GroupBox InitializeSnapshotPanel()
        {
            var group = new GroupBox
            {
                Text = "Snapshots",
                Dock = DockStyle.Fill,
                Padding = new Padding(4)
            };

            var outerLayout = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                RowCount = 2,
                ColumnCount = 1,
                Margin = Padding.Empty,
                Padding = Padding.Empty
            };
            outerLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 28));
            outerLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));

            var headerStrip = new ToolStrip
            {
                Dock = DockStyle.Fill,
                GripStyle = ToolStripGripStyle.Hidden,
                RenderMode = ToolStripRenderMode.System,
                Padding = new Padding(0),
                Stretch = true
            };
            var pullBtn = new ToolStripButton("Pull") { DisplayStyle = ToolStripItemDisplayStyle.Text, AutoSize = true };
            pullBtn.Click += (s, e) => PullToProject();
            var loadBtn = new ToolStripButton("Load File") { DisplayStyle = ToolStripItemDisplayStyle.Text, AutoSize = true };
            loadBtn.Click += (s, e) => LoadFileToProject();
            var exportBtn = new ToolStripButton("Export") { DisplayStyle = ToolStripItemDisplayStyle.Text, AutoSize = true, ToolTipText = "Export selected snapshot" };
            exportBtn.Click += (s, e) => ExportInlineSnapshot();
            _snapMoveUpBtn = new ToolStripButton("↑") { DisplayStyle = ToolStripItemDisplayStyle.Text, AutoSize = true, Enabled = false, ToolTipText = "Move snapshot up" };
            _snapMoveUpBtn.Click += (s, e) => MoveInlineSnapshot(-1);
            _snapMoveDownBtn = new ToolStripButton("↓") { DisplayStyle = ToolStripItemDisplayStyle.Text, AutoSize = true, Enabled = false, ToolTipText = "Move snapshot down" };
            _snapMoveDownBtn.Click += (s, e) => MoveInlineSnapshot(1);
            headerStrip.Items.Add(pullBtn);
            headerStrip.Items.Add(loadBtn);
            headerStrip.Items.Add(exportBtn);
            headerStrip.Items.Add(new ToolStripSeparator());
            headerStrip.Items.Add(_snapMoveUpBtn);
            headerStrip.Items.Add(_snapMoveDownBtn);
            outerLayout.Controls.Add(headerStrip, 0, 0);

            var snapSplit = new SplitContainer
            {
                Dock = DockStyle.Fill,
                Orientation = Orientation.Vertical,
                SplitterWidth = 5,
                Panel1MinSize = 1,
                Panel2MinSize = 1,
                FixedPanel = FixedPanel.None
            };

            bool snapSplitterSet = false;
            snapSplit.Resize += (s, e) =>
            {
                if (snapSplitterSet || snapSplit.Width <= 0) return;
                snapSplitterSet = true;
                var target = Math.Max(1, (int)(snapSplit.Width * 0.28));
                try { snapSplit.SplitterDistance = target; } catch { }
            };

            _inlineSnapList = new ListView
            {
                Dock = DockStyle.Fill,
                View = View.Details,
                FullRowSelect = true,
                GridLines = true,
                MultiSelect = false,
                HeaderStyle = ColumnHeaderStyle.Nonclickable
            };
            _inlineSnapList.Columns.Add("#", 32, HorizontalAlignment.Right);
            _inlineSnapList.Columns.Add("Name", 110);
            _inlineSnapList.Columns.Add("Table", 90);
            _inlineSnapList.Columns.Add("Rows", 48, HorizontalAlignment.Right);
            _inlineSnapList.Columns.Add("Src", 36);
            _inlineSnapList.Resize += (s, e) => FitSnapshotListColumns();
            _inlineSnapList.SelectedIndexChanged += InlineSnapList_SelectionChanged;
            _inlineSnapList.MouseDown += (s, e) =>
            {
                if (e.Button == MouseButtons.Right)
                {
                    var hit = _inlineSnapList.GetItemAt(e.X, e.Y);
                    if (hit != null) { hit.Selected = true; hit.Focused = true; }
                }
            };
            _inlineSnapList.ContextMenuStrip = BuildInlineSnapshotContextMenu();
            snapSplit.Panel1.Controls.Add(_inlineSnapList);

            var rightLayout = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                RowCount = 2,
                ColumnCount = 1,
                Margin = Padding.Empty,
                Padding = Padding.Empty
            };
            rightLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
            rightLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 32));

            _inlineDataGrid = new DataGridView
            {
                Dock = DockStyle.Fill,
                ReadOnly = true,
                AllowUserToAddRows = false,
                AllowUserToDeleteRows = false,
                AllowUserToOrderColumns = true,
                RowHeadersVisible = false,
                AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.None,
                SelectionMode = DataGridViewSelectionMode.FullRowSelect,
                ClipboardCopyMode = DataGridViewClipboardCopyMode.EnableWithoutHeaderText,
                Font = new Font("Consolas", 8.5f)
            };
            rightLayout.Controls.Add(_inlineDataGrid, 0, 0);

            var pagBar = new FlowLayoutPanel
            {
                Dock = DockStyle.Fill,
                FlowDirection = FlowDirection.LeftToRight,
                Padding = new Padding(4, 3, 0, 0),
                WrapContents = false
            };
            _inlineDataPrevButton = new Button { Text = "< Prev", Width = 64, Height = 24, Enabled = false };
            _inlineDataPrevButton.Click += (s, e) => LoadInlineDataPage(_inlineDataCurrentPage - 1);
            _inlineDataNextButton = new Button { Text = "Next >", Width = 64, Height = 24, Enabled = false };
            _inlineDataNextButton.Click += (s, e) => LoadInlineDataPage(_inlineDataCurrentPage + 1);
            _inlineDataPageLabel = new System.Windows.Forms.Label
            {
                AutoSize = true,
                TextAlign = ContentAlignment.MiddleLeft,
                Padding = new Padding(8, 4, 0, 0)
            };
            pagBar.Controls.AddRange(new Control[] { _inlineDataPrevButton, _inlineDataNextButton, _inlineDataPageLabel });
            rightLayout.Controls.Add(pagBar, 0, 1);

            snapSplit.Panel2.Controls.Add(rightLayout);
            outerLayout.Controls.Add(snapSplit, 0, 1);
            group.Controls.Add(outerLayout);
            return group;
        }

        private ContextMenuStrip BuildInlineSnapshotContextMenu()
        {
            var menu = new ContextMenuStrip();

            var addToPlan = new ToolStripMenuItem("Add to Plan");
            addToPlan.Click += (s, e) => AddInlineSnapshotToPlan();
            menu.Items.Add(addToPlan);

            var export = new ToolStripMenuItem("Export Snapshot");
            export.Click += (s, e) => ExportInlineSnapshot();
            menu.Items.Add(export);

            menu.Items.Add(new ToolStripSeparator());

            var rename = new ToolStripMenuItem("Rename");
            rename.Click += (s, e) => RenameInlineSnapshot();
            menu.Items.Add(rename);

            var delete = new ToolStripMenuItem("Delete");
            delete.Click += (s, e) => DeleteInlineSnapshot();
            menu.Items.Add(delete);

            menu.Items.Add(new ToolStripSeparator());

            var viewFull = new ToolStripMenuItem("View Full Screen");
            viewFull.Click += (s, e) => ShowSnapshotViewer();
            menu.Items.Add(viewFull);

            menu.Opening += (s, e) =>
            {
                e.Cancel = GetInlineContextSnapshot() == null;
            };

            return menu;
        }

        #endregion

        #region Snapshots Operations

        private void FitSnapshotListColumns()
        {
            if (_fittingSnapshotColumns) return;
            if (_inlineSnapList == null || _inlineSnapList.Columns.Count == 0) return;
            _fittingSnapshotColumns = true;
            try
            {
                int count = _inlineSnapList.Columns.Count;
                for (int i = 0; i < count; i++)
                {
                    _inlineSnapList.AutoResizeColumn(i, ColumnHeaderAutoResizeStyle.ColumnContent);
                    int contentWidth = _inlineSnapList.Columns[i].Width;
                    _inlineSnapList.AutoResizeColumn(i, ColumnHeaderAutoResizeStyle.HeaderSize);
                    if (contentWidth > _inlineSnapList.Columns[i].Width)
                        _inlineSnapList.Columns[i].Width = contentWidth;
                }
                int usedByOthers = 0;
                for (int i = 0; i < count - 1; i++)
                    usedByOthers += _inlineSnapList.Columns[i].Width;
                int available = _inlineSnapList.ClientSize.Width - usedByOthers;
                if (available > _inlineSnapList.Columns[count - 1].Width)
                    _inlineSnapList.Columns[count - 1].Width = available;
            }
            finally
            {
                _fittingSnapshotColumns = false;
            }
        }

        internal void RefreshInlineSnapshotList()
        {
            if (_inlineSnapList == null) return;

            _inlineSnapList.Items.Clear();
            ClearInlineDataGrid();

            if (_project?.Service == null) return;

            var snapshots = _project.Service.GetSnapshots();
            foreach (var s in snapshots)
            {
                var item = new ListViewItem(s.SortOrder.ToString());
                item.SubItems.Add(s.Name);
                item.SubItems.Add(s.TableLogicalName);
                item.SubItems.Add(s.RowCount.ToString("N0"));
                item.SubItems.Add(s.Source ?? "");
                item.Tag = s;
                _inlineSnapList.Items.Add(item);
            }

            FitSnapshotListColumns();
            UpdateSnapshotMoveButtons();
        }

        private DmtSnapshot GetInlineContextSnapshot()
        {
            return _inlineSnapList?.SelectedItems.Count > 0
                ? _inlineSnapList.SelectedItems[0].Tag as DmtSnapshot
                : null;
        }

        private void AddInlineSnapshotToPlan()
        {
            var snap = GetInlineContextSnapshot();
            if (snap == null) return;
            AddSnapshotToPlan(snap);
        }

        private void ExportInlineSnapshot()
        {
            var snap = GetInlineContextSnapshot();
            if (snap == null || _project?.Service == null) return;

            using (var dialog = new SaveFileDialog
            {
                Title = "Export Snapshot",
                FileName = $"{snap.Name}.json",
                Filter = "JSON files (*.json)|*.json|Excel files (*.xlsx)|*.xlsx",
                AddExtension = true,
                OverwritePrompt = true
            })
            {
                if (dialog.ShowDialog(ParentForm) != DialogResult.OK) return;

                var path = dialog.FileName;
                ManageWorkingState(true, $"Exporting snapshot '{snap.Name}'...");
                WorkAsync(new XrmToolBox.Extensibility.WorkAsyncInfo
                {
                    Work = (worker, args) =>
                    {
                        if (string.Equals(Path.GetExtension(path), ".xlsx", StringComparison.OrdinalIgnoreCase))
                            SqliteFileAdapter.ExportToExcel(_project.Service, snap.Name, path);
                        else
                            SqliteFileAdapter.ExportToJson(_project.Service, snap.Name, path);
                    },
                    PostWorkCallBack = args =>
                    {
                        ManageWorkingState(false);
                        if (args.Error != null)
                        {
                            MessageBox.Show(this, $"Export failed: {args.Error.Message}", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                            return;
                        }

                        SendMessageToStatusBar?.Invoke(this, new StatusBarMessageEventArgs($"Snapshot exported: {Path.GetFileName(path)}"));
                    },
                    ProgressChanged = ReportWorkProgress
                });
            }
        }

        private void RenameInlineSnapshot()
        {
            var snap = GetInlineContextSnapshot();
            if (snap == null || _project?.Service == null) return;

            using (var dlg = new SnapshotNameDialog(snap.Name, "Rename"))
            {
                if (dlg.ShowDialog(ParentForm) != DialogResult.OK) return;
                var newName = dlg.SnapshotName;
                if (string.Equals(newName, snap.Name, StringComparison.OrdinalIgnoreCase)) return;

                try
                {
                    snap.Name = newName;
                    snap.UpdatedOn = DateTime.UtcNow;
                    _project.Service.SaveSnapshot(snap);
                    RefreshInlineSnapshotList();
                    SendMessageToStatusBar?.Invoke(this, new StatusBarMessageEventArgs($"Snapshot renamed to '{newName}'"));
                }
                catch (Exception ex)
                {
                    MessageBox.Show(this, $"Rename failed: {ex.Message}", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
            }
        }

        private void DeleteInlineSnapshot()
        {
            var snap = GetInlineContextSnapshot();
            if (snap == null || _project?.Service == null) return;

            var result = MessageBox.Show(this,
                $"Delete snapshot '{snap.Name}' ({snap.RowCount:N0} rows)? This cannot be undone.",
                "Confirm Delete", MessageBoxButtons.YesNo, MessageBoxIcon.Warning, MessageBoxDefaultButton.Button2);
            if (result != DialogResult.Yes) return;

            try
            {
                _project.Service.DeleteSnapshot(snap.Name);
                RefreshInlineSnapshotList();
                SendMessageToStatusBar?.Invoke(this, new StatusBarMessageEventArgs($"Snapshot '{snap.Name}' deleted"));
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, $"Delete failed: {ex.Message}", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void UpdateSnapshotMoveButtons()
        {
            if (_snapMoveUpBtn == null || _snapMoveDownBtn == null) return;
            var count = _inlineSnapList?.Items.Count ?? 0;
            if (count <= 1 || _inlineSnapList.SelectedItems.Count == 0)
            {
                _snapMoveUpBtn.Enabled = false;
                _snapMoveDownBtn.Enabled = false;
                return;
            }
            var idx = _inlineSnapList.SelectedItems[0].Index;
            _snapMoveUpBtn.Enabled = idx > 0;
            _snapMoveDownBtn.Enabled = idx < count - 1;
        }

        private void MoveInlineSnapshot(int direction)
        {
            if (_project?.Service == null || _inlineSnapList.SelectedItems.Count == 0) return;

            var snapshots = _project.Service.GetSnapshots().ToList();
            var selected = _inlineSnapList.SelectedItems[0].Tag as DmtSnapshot;
            if (selected == null) return;

            var idx = snapshots.FindIndex(s => s.Id == selected.Id);
            if (idx < 0) return;

            var swapIdx = idx + direction;
            if (swapIdx < 0 || swapIdx >= snapshots.Count) return;

            var a = snapshots[idx];
            var b = snapshots[swapIdx];
            var tempOrder = a.SortOrder;
            a.SortOrder = b.SortOrder;
            b.SortOrder = tempOrder;

            try
            {
                _project.Service.SaveSnapshot(a);
                _project.Service.SaveSnapshot(b);
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, $"Move failed: {ex.Message}", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }

            RefreshInlineSnapshotList();

            foreach (ListViewItem item in _inlineSnapList.Items)
            {
                if ((item.Tag as DmtSnapshot)?.Id == selected.Id)
                {
                    item.Selected = true;
                    item.EnsureVisible();
                    break;
                }
            }
        }

        private void InlineSnapList_SelectionChanged(object sender, EventArgs e)
        {
            UpdateSnapshotMoveButtons();
            if (_inlineSnapList.SelectedItems.Count == 0) { ClearInlineDataGrid(); return; }
            var snap = _inlineSnapList.SelectedItems[0].Tag as DmtSnapshot;
            if (snap == null) return;

            _inlineSelectedSnapshot = snap;
            _inlineDataCurrentPage = 0;
            try { _inlineDataTotalRows = _project.Service.CountSnapshotRows(snap.TableSuffix); }
            catch { _inlineDataTotalRows = snap.RowCount; }

            BuildInlineGridColumns(snap);
            LoadInlineDataPage(0);
        }

        private void BuildInlineGridColumns(DmtSnapshot snap)
        {
            _inlineDataGrid.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.None;
            _inlineDataGrid.Columns.Clear();
            AddInlineGridColumn("_source_id", "Source ID", 230);
            AddInlineGridColumn("_is_new", "New?", 42);
            foreach (var col in snap.ColumnConfig)
            {
                var header = string.IsNullOrEmpty(col.DisplayName) ? col.LogicalName : col.DisplayName;
                AddInlineGridColumn(col.LogicalName, header, 110);
            }
        }

        private void AddInlineGridColumn(string name, string header, int width)
        {
            _inlineDataGrid.Columns.Add(new DataGridViewTextBoxColumn
            {
                Name = name,
                HeaderText = header,
                Width = width,
                SortMode = DataGridViewColumnSortMode.NotSortable
            });
        }

        private void LoadInlineDataPage(int page)
        {
            if (_inlineSelectedSnapshot == null || _project?.Service == null) return;
            var snap = _inlineSelectedSnapshot;
            var offset = page * InlinePageSize;

            var optCache = new Dictionary<string, Dictionary<int, string>>();
            foreach (var col in snap.ColumnConfig.Where(c => IsInlineOptionSetType(c.Type) || IsInlineMultiOptionSetType(c.Type)))
            {
                try { optCache[col.LogicalName] = _project.Service.GetOptionSetValues(snap.TableLogicalName, col.LogicalName); }
                catch { optCache[col.LogicalName] = new Dictionary<int, string>(); }
            }

            List<Dictionary<string, object>> rows;
            try
            {
                rows = _project.Service.ReadSnapshotRecords(snap.TableSuffix, snap.ColumnConfig, offset, InlinePageSize).ToList();
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, $"Failed to read snapshot: {ex.Message}", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }

            _inlineDataGrid.SuspendLayout();
            _inlineDataGrid.Rows.Clear();
            foreach (var row in rows)
            {
                var cells = new List<object>();
                cells.Add(row.TryGetValue("_source_id", out var sid) ? sid?.ToString() ?? "" : "");
                cells.Add(row.TryGetValue("_is_new", out var isn) && isn is bool b && b ? "Yes" : "");
                foreach (var col in snap.ColumnConfig)
                {
                    row.TryGetValue(col.LogicalName, out var val);
                    cells.Add(FormatInlineValue(val, col, optCache));
                }
                _inlineDataGrid.Rows.Add(cells.ToArray());
            }
            _inlineDataGrid.ResumeLayout();
            if (_inlineDataGrid.Rows.Count > 0)
            {
                _inlineDataGrid.AutoResizeColumns(DataGridViewAutoSizeColumnsMode.DisplayedCells);
                var colCount = _inlineDataGrid.Columns.Count;
                if (colCount > 0)
                {
                    int usedByOthers = 0;
                    for (int i = 0; i < colCount - 1; i++)
                        usedByOthers += _inlineDataGrid.Columns[i].Width;
                    int available = _inlineDataGrid.ClientSize.Width - usedByOthers;
                    if (available > _inlineDataGrid.Columns[colCount - 1].Width)
                        _inlineDataGrid.Columns[colCount - 1].Width = available;
                }
            }

            _inlineDataCurrentPage = page;
            var totalPages = (int)Math.Ceiling(_inlineDataTotalRows / (double)InlinePageSize);
            _inlineDataPageLabel.Text = $"  Page {page + 1} of {Math.Max(1, totalPages)}  ({_inlineDataTotalRows:N0} rows)";
            _inlineDataPrevButton.Enabled = page > 0;
            _inlineDataNextButton.Enabled = (page + 1) * InlinePageSize < _inlineDataTotalRows;
        }

        private void ClearInlineDataGrid()
        {
            if (_inlineDataGrid == null) return;
            _inlineDataGrid.Columns.Clear();
            _inlineDataGrid.Rows.Clear();
            if (_inlineDataPageLabel != null) _inlineDataPageLabel.Text = "";
            if (_inlineDataPrevButton != null) _inlineDataPrevButton.Enabled = false;
            if (_inlineDataNextButton != null) _inlineDataNextButton.Enabled = false;
            _inlineSelectedSnapshot = null;
        }

        private static string FormatInlineValue(object val, DataTableColumnConfig col, Dictionary<string, Dictionary<int, string>> optCache)
        {
            if (val == null) return "";

            if (IsInlineOptionSetType(col.Type) && val is long optVal)
            {
                if (optCache.TryGetValue(col.LogicalName, out var labels) && labels.TryGetValue((int)optVal, out var label))
                    return $"{optVal} - {label}";
                return optVal.ToString();
            }

            if (IsInlineMultiOptionSetType(col.Type) && val is string multiVal)
            {
                if (!optCache.TryGetValue(col.LogicalName, out var labels)) return multiVal;
                return string.Join(", ", multiVal.Split(new[] { '|' }, StringSplitOptions.RemoveEmptyEntries)
                    .Select(p => int.TryParse(p, out var v)
                        ? (labels.TryGetValue(v, out var l) ? $"{v} - {l}" : p)
                        : p));
            }

            return val.ToString();
        }

        private static bool IsInlineOptionSetType(string type) =>
            type == "Picklist" || type == "State" || type == "Status" || type == "Boolean" || type == "OptionSet";

        private static bool IsInlineMultiOptionSetType(string type) =>
            type == "MultiSelectPicklist" || type == "MultiOptionSet";

        #endregion
    }
}
