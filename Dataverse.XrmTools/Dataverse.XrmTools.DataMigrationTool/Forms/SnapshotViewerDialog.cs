// System
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;

// DataMigrationTool
using Dataverse.XrmTools.DataMigrationTool.Logic;
using Dataverse.XrmTools.DataMigrationTool.Models;

namespace Dataverse.XrmTools.DataMigrationTool.Forms
{
    public class SnapshotViewerDialog : Form
    {
        private readonly SqliteProjectService _project;
        private DmtSnapshot _currentSnapshot;
        private int _currentPage;
        private int _totalRows;
        private const int PageSize = 500;

        // colLogicalName -> (relatedTableSuffix, nameColumn) — built per selected snapshot
        private Dictionary<string, (string TableSuffix, string NameColumn)> _lookupInfo =
            new Dictionary<string, (string, string)>(StringComparer.OrdinalIgnoreCase);
        // colLogicalName -> (sourceId -> displayName) — rebuilt per page
        private Dictionary<string, Dictionary<string, string>> _lookupCache =
            new Dictionary<string, Dictionary<string, string>>(StringComparer.OrdinalIgnoreCase);

        private ListView _snapList;
        private DataGridView _grid;
        private System.Windows.Forms.Label _lblPage;
        private Button _btnPrev;
        private Button _btnNext;

        public SnapshotViewerDialog(SqliteProjectService project)
        {
            _project = project;

            Text = "Project Snapshots";
            Size = new Size(1400, 820);
            StartPosition = FormStartPosition.CenterParent;
            ShowIcon = false;
            ShowInTaskbar = false;
            MinimizeBox = false;
            MaximizeBox = true;

            var split = new SplitContainer { Dock = DockStyle.Fill };
            Load += (s, e) =>
            {
                split.Panel1MinSize = 200;
                split.Panel2MinSize = 300;
                try { split.SplitterDistance = Math.Min(290, split.Width - 300 - 4); }
                catch { }
            };

            // Left: snapshot list
            _snapList = new ListView
            {
                Dock = DockStyle.Fill,
                View = View.Details,
                FullRowSelect = true,
                GridLines = true,
                MultiSelect = false,
                HeaderStyle = ColumnHeaderStyle.Nonclickable
            };
            _snapList.Columns.Add("#", 32, HorizontalAlignment.Right);
            _snapList.Columns.Add("Name", 110);
            _snapList.Columns.Add("Table", 90);
            _snapList.Columns.Add("Rows", 45, HorizontalAlignment.Right);
            _snapList.Columns.Add("Source", 40);
            _snapList.Columns.Add("Date", 75);
            _snapList.SelectedIndexChanged += SnapList_SelectedIndexChanged;
            split.Panel1.Controls.Add(_snapList);

            // Right: grid + pagination bar
            var rightLayout = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                RowCount = 2,
                ColumnCount = 1,
                Margin = Padding.Empty,
                Padding = Padding.Empty
            };
            rightLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            rightLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 32));

            _grid = new DataGridView
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
            rightLayout.Controls.Add(_grid, 0, 0);

            var pagBar = new FlowLayoutPanel
            {
                Dock = DockStyle.Fill,
                FlowDirection = FlowDirection.LeftToRight,
                Padding = new Padding(4, 3, 0, 0),
                WrapContents = false
            };
            _btnPrev = new Button { Text = "< Prev", Width = 64, Height = 24, Enabled = false };
            _btnPrev.Click += (s, e) => LoadPage(_currentPage - 1);
            _btnNext = new Button { Text = "Next >", Width = 64, Height = 24, Enabled = false };
            _btnNext.Click += (s, e) => LoadPage(_currentPage + 1);
            _lblPage = new System.Windows.Forms.Label
            {
                AutoSize = true,
                TextAlign = ContentAlignment.MiddleLeft,
                Padding = new Padding(8, 4, 0, 0)
            };
            pagBar.Controls.AddRange(new Control[] { _btnPrev, _btnNext, _lblPage });
            rightLayout.Controls.Add(pagBar, 0, 1);

            split.Panel2.Controls.Add(rightLayout);

            var closeBtn = new Button { Text = "Close", Width = 80, Height = 26, DialogResult = DialogResult.OK };
            var btnPanel = new FlowLayoutPanel
            {
                Dock = DockStyle.Bottom,
                FlowDirection = FlowDirection.RightToLeft,
                Height = 36,
                Padding = new Padding(4)
            };
            btnPanel.Controls.Add(closeBtn);

            Controls.Add(split);
            Controls.Add(btnPanel);
            AcceptButton = closeBtn;

            PopulateSnapshotList();
        }

        private void PopulateSnapshotList()
        {
            _snapList.Items.Clear();
            var snapshots = _project.GetSnapshots(); // already ordered by sort_order
            foreach (var s in snapshots)
            {
                var item = new ListViewItem(s.SortOrder.ToString());
                item.SubItems.Add(s.Name);
                item.SubItems.Add(s.TableLogicalName);
                item.SubItems.Add(s.RowCount.ToString("N0"));
                item.SubItems.Add(s.Source ?? "");
                item.SubItems.Add(s.UpdatedOn.ToLocalTime().ToString("yyyy-MM-dd"));
                item.Tag = s;
                _snapList.Items.Add(item);
            }
            if (_snapList.Items.Count > 0)
                _snapList.Items[0].Selected = true;
        }

        private void SnapList_SelectedIndexChanged(object sender, EventArgs e)
        {
            if (_snapList.SelectedItems.Count == 0) { ClearGrid(); return; }
            var snap = _snapList.SelectedItems[0].Tag as DmtSnapshot;
            if (snap == null) return;

            _currentSnapshot = snap;
            _currentPage = 0;
            try { _totalRows = _project.CountSnapshotRows(snap.TableSuffix); }
            catch { _totalRows = snap.RowCount; }

            BuildGridColumns(snap);
            LoadPage(0);
        }

        private void BuildGridColumns(DmtSnapshot snap)
        {
            _grid.Columns.Clear();
            _lookupInfo.Clear();
            _lookupCache.Clear();

            AddGridColumn("_source_id", "Source ID", 230);
            AddGridColumn("_is_new", "New?", 42);

            var allSnapshots = _project.GetSnapshots();
            foreach (var col in snap.ColumnConfig)
            {
                var header = string.IsNullOrEmpty(col.DisplayName) ? col.LogicalName : col.DisplayName;
                var width = !string.IsNullOrEmpty(col.RelatedTable) ? 160 : 110;
                AddGridColumn(col.LogicalName, header, width);

                if (!string.IsNullOrEmpty(col.RelatedTable))
                {
                    var relatedSnap = allSnapshots
                        .Where(s => string.Equals(s.TableLogicalName, col.RelatedTable, StringComparison.OrdinalIgnoreCase))
                        .OrderByDescending(s => s.UpdatedOn)
                        .FirstOrDefault();
                    if (relatedSnap != null)
                    {
                        var nameCol = PickNameColumn(relatedSnap);
                        if (nameCol != null)
                            _lookupInfo[col.LogicalName] = (relatedSnap.TableSuffix, nameCol);
                    }
                }
            }
        }

        private static string PickNameColumn(DmtSnapshot snap)
        {
            var preferred = new[] { "name", "fullname", "title", "subject" };
            foreach (var pref in preferred)
            {
                if (snap.ColumnConfig.Any(c => string.Equals(c.LogicalName, pref, StringComparison.OrdinalIgnoreCase)))
                    return pref;
            }
            return snap.ColumnConfig.FirstOrDefault(c =>
                string.Equals(c.Type, "String", StringComparison.OrdinalIgnoreCase) &&
                !c.LogicalName.EndsWith("id", StringComparison.OrdinalIgnoreCase))?.LogicalName;
        }

        private void AddGridColumn(string name, string header, int width)
        {
            _grid.Columns.Add(new DataGridViewTextBoxColumn
            {
                Name = name,
                HeaderText = header,
                Width = width,
                SortMode = DataGridViewColumnSortMode.NotSortable
            });
        }

        private void LoadPage(int page)
        {
            if (_currentSnapshot == null) return;
            var snap = _currentSnapshot;
            var offset = page * PageSize;

            // Cache option set labels per column
            var optCache = new Dictionary<string, Dictionary<int, string>>();
            foreach (var col in snap.ColumnConfig.Where(c => IsOptionSetType(c.Type) || IsMultiOptionSetType(c.Type)))
            {
                try { optCache[col.LogicalName] = _project.GetOptionSetValues(snap.TableLogicalName, col.LogicalName); }
                catch { optCache[col.LogicalName] = new Dictionary<int, string>(); }
            }

            List<Dictionary<string, object>> rows;
            try { rows = _project.ReadSnapshotRecords(snap.TableSuffix, snap.ColumnConfig, offset, PageSize).ToList(); }
            catch (Exception ex) { MessageBox.Show(this, $"Failed to read snapshot: {ex.Message}", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error); return; }

            // Build lookup name cache for this page
            _lookupCache.Clear();
            foreach (var entry in _lookupInfo)
            {
                var guids = rows
                    .Select(r => r.TryGetValue(entry.Key, out var v) ? v?.ToString() : null)
                    .Where(g => !string.IsNullOrEmpty(g) && IsGuid(g))
                    .Distinct()
                    .ToList();
                if (guids.Count > 0)
                    _lookupCache[entry.Key] = _project.ReadSnapshotDisplayNames(entry.Value.TableSuffix, entry.Value.NameColumn, guids);
            }

            _grid.SuspendLayout();
            _grid.Rows.Clear();
            foreach (var row in rows)
            {
                var cells = new List<object>();
                cells.Add(row.TryGetValue("_source_id", out var sid) ? sid?.ToString() ?? "" : "");
                cells.Add(row.TryGetValue("_is_new", out var isn) && isn is bool b && b ? "Yes" : "");
                foreach (var col in snap.ColumnConfig)
                {
                    row.TryGetValue(col.LogicalName, out var val);
                    cells.Add(FormatValue(val, col, optCache, _lookupCache));
                }
                _grid.Rows.Add(cells.ToArray());
            }
            _grid.ResumeLayout();

            _currentPage = page;
            var totalPages = (int)Math.Ceiling(_totalRows / (double)PageSize);
            _lblPage.Text = $"  Page {page + 1} of {Math.Max(1, totalPages)}  ({_totalRows:N0} rows total)";
            _btnPrev.Enabled = page > 0;
            _btnNext.Enabled = (page + 1) * PageSize < _totalRows;
        }

        private static string FormatValue(
            object val,
            DataTableColumnConfig col,
            Dictionary<string, Dictionary<int, string>> optCache,
            Dictionary<string, Dictionary<string, string>> lookupCache)
        {
            if (val == null) return "";

            if (IsOptionSetType(col.Type) && val is long optVal)
            {
                if (optCache.TryGetValue(col.LogicalName, out var labels) && labels.TryGetValue((int)optVal, out var label))
                    return $"{optVal} - {label}";
                return optVal.ToString();
            }

            if (IsMultiOptionSetType(col.Type) && val is string multiVal)
            {
                if (!optCache.TryGetValue(col.LogicalName, out var labels)) return multiVal;
                return string.Join(", ", multiVal.Split(new[] { '|' }, StringSplitOptions.RemoveEmptyEntries)
                    .Select(p => int.TryParse(p, out var v)
                        ? (labels.TryGetValue(v, out var l) ? $"{v} - {l}" : p)
                        : p));
            }

            if (!string.IsNullOrEmpty(col.RelatedTable) && val is string guidStr && IsGuid(guidStr))
            {
                if (lookupCache != null
                    && lookupCache.TryGetValue(col.LogicalName, out var names)
                    && names.TryGetValue(guidStr, out var name)
                    && !string.IsNullOrEmpty(name))
                    return name;
                return guidStr;
            }

            return val.ToString();
        }

        private static bool IsGuid(string value) =>
            !string.IsNullOrEmpty(value) && Guid.TryParse(value, out _);

        private static bool IsOptionSetType(string type) =>
            type == "Picklist" || type == "State" || type == "Status" || type == "Boolean" || type == "OptionSet";

        private static bool IsMultiOptionSetType(string type) =>
            type == "MultiSelectPicklist" || type == "MultiOptionSet";

        private void ClearGrid()
        {
            _grid.Columns.Clear();
            _grid.Rows.Clear();
            _lblPage.Text = "";
            _btnPrev.Enabled = false;
            _btnNext.Enabled = false;
        }
    }
}
