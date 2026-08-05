// System
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Text.RegularExpressions;
using System.Windows.Forms;

// DataMigrationTool
using Dataverse.XrmTools.DataMigrationTool.Models;

namespace Dataverse.XrmTools.DataMigrationTool.Forms
{
    // Lets a user build a snapshot from scratch: type into a blank grid, or paste
    // tab/newline-delimited data copied from Excel, a DB browser, or a query result grid.
    // Row 0 of the grid holds the column headers; rows below it are data.
    public class ManualSnapshotDialog : Form
    {
        private const int DefaultColumns = 5;
        private const int DefaultDataRows = 10;

        private TextBox _nameBox;
        private DataGridView _grid;
        private readonly IReadOnlyList<DmtSnapshot> _existingSnapshots;

        public string SnapshotName => _nameBox.Text.Trim();
        public List<DataTableColumnConfig> Columns { get; private set; }
        public List<Dictionary<string, object>> Rows { get; private set; }

        public ManualSnapshotDialog(IReadOnlyList<DmtSnapshot> existingSnapshots = null)
        {
            _existingSnapshots = existingSnapshots;

            Text = "New Snapshot";
            FormBorderStyle = FormBorderStyle.Sizable;
            StartPosition = FormStartPosition.CenterParent;
            ShowIcon = false;
            ShowInTaskbar = false;
            MaximizeBox = true;
            MinimizeBox = false;
            ClientSize = new Size(720, 480);
            MinimumSize = new Size(520, 340);

            var outer = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                RowCount = 4,
                ColumnCount = 1,
                Padding = new Padding(10)
            };
            outer.RowStyles.Add(new RowStyle(SizeType.Absolute, 28));
            outer.RowStyles.Add(new RowStyle(SizeType.Absolute, 28));
            outer.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
            outer.RowStyles.Add(new RowStyle(SizeType.Absolute, 36));

            var namePanel = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 1, ColumnCount = 2 };
            namePanel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 90));
            namePanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            namePanel.Controls.Add(new Label { Text = "Name:", TextAlign = ContentAlignment.MiddleLeft, Dock = DockStyle.Fill }, 0, 0);
            _nameBox = new TextBox { Dock = DockStyle.Fill };
            namePanel.Controls.Add(_nameBox, 1, 0);
            outer.Controls.Add(namePanel, 0, 0);

            var toolStrip = new ToolStrip { Dock = DockStyle.Fill, GripStyle = ToolStripGripStyle.Hidden };
            var addRowBtn = new ToolStripButton("+ Row") { DisplayStyle = ToolStripItemDisplayStyle.Text, AutoSize = true };
            addRowBtn.Click += (s, e) => AddRow();
            var addColBtn = new ToolStripButton("+ Column") { DisplayStyle = ToolStripItemDisplayStyle.Text, AutoSize = true };
            addColBtn.Click += (s, e) => AddColumn();
            var pasteBtn = new ToolStripButton("Paste") { DisplayStyle = ToolStripItemDisplayStyle.Text, AutoSize = true, ToolTipText = "Paste tab-delimited data at the selected cell (Ctrl+V also works)" };
            pasteBtn.Click += (s, e) => PasteFromClipboard();
            var hint = new ToolStripLabel("First row = column headers. Paste data copied from Excel, SQL 4 CDS, a DB browser, etc.")
            {
                ForeColor = Color.DimGray,
                Alignment = ToolStripItemAlignment.Right
            };
            toolStrip.Items.Add(addRowBtn);
            toolStrip.Items.Add(addColBtn);
            toolStrip.Items.Add(pasteBtn);
            toolStrip.Items.Add(hint);
            outer.Controls.Add(toolStrip, 0, 1);

            _grid = new DataGridView
            {
                Dock = DockStyle.Fill,
                AllowUserToAddRows = false,
                AllowUserToDeleteRows = false,
                AllowUserToResizeRows = false,
                RowHeadersVisible = true,
                RowHeadersWidth = 32,
                ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.DisableResizing,
                ClipboardCopyMode = DataGridViewClipboardCopyMode.EnableWithoutHeaderText,
                SelectionMode = DataGridViewSelectionMode.CellSelect
            };
            _grid.KeyDown += Grid_KeyDown;
            outer.Controls.Add(_grid, 0, 2);

            var btnPanel = new FlowLayoutPanel
            {
                Dock = DockStyle.Fill,
                FlowDirection = FlowDirection.RightToLeft,
                Padding = new Padding(0, 4, 0, 0)
            };
            var cancelBtn = new Button { Text = "Cancel", DialogResult = DialogResult.Cancel, Width = 75 };
            var createBtn = new Button { Text = "Create", Width = 75 };
            createBtn.Click += (s, e) => Ok();
            btnPanel.Controls.AddRange(new Control[] { cancelBtn, createBtn });
            outer.Controls.Add(btnPanel, 0, 3);

            Controls.Add(outer);
            AcceptButton = null; // Enter is used for grid cell navigation, not dialog accept
            CancelButton = cancelBtn;

            InitializeGrid();

            Shown += (s, e) => { _nameBox.Focus(); };
        }

        private void InitializeGrid()
        {
            for (var c = 0; c < DefaultColumns; c++)
                AddColumn(suppressPlaceholder: false);
            for (var r = 0; r < DefaultDataRows; r++)
                AddRow();
        }

        private void AddColumn(bool suppressPlaceholder = false)
        {
            var index = _grid.Columns.Count;
            var col = new DataGridViewTextBoxColumn
            {
                Name = "col" + index,
                HeaderText = ColumnLetter(index),
                Width = 110,
                SortMode = DataGridViewColumnSortMode.NotSortable
            };
            _grid.Columns.Add(col);

            // Header data row (row 0) may not exist yet on first call.
            if (_grid.Rows.Count == 0)
                AddHeaderRow();

            if (!suppressPlaceholder)
                _grid.Rows[0].Cells[index].Value = $"Column{index + 1}";
        }

        private void AddHeaderRow()
        {
            var rowIndex = _grid.Rows.Add();
            var row = _grid.Rows[rowIndex];
            row.DefaultCellStyle.Font = new Font(_grid.Font, FontStyle.Bold);
            row.DefaultCellStyle.BackColor = Color.FromArgb(235, 235, 235);
            row.HeaderCell.Value = "H";
        }

        private void AddRow()
        {
            var rowIndex = _grid.Rows.Add();
            _grid.Rows[rowIndex].HeaderCell.Value = rowIndex.ToString();
        }

        private static string ColumnLetter(int index)
        {
            var name = "";
            index++;
            while (index > 0)
            {
                var rem = (index - 1) % 26;
                name = (char)('A' + rem) + name;
                index = (index - 1) / 26;
            }
            return name;
        }

        private void Grid_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Control && e.KeyCode == Keys.V)
            {
                PasteFromClipboard();
                e.Handled = true;
                return;
            }

            if (e.KeyCode == Keys.Delete && !_grid.IsCurrentCellInEditMode)
            {
                foreach (DataGridViewCell cell in _grid.SelectedCells)
                    cell.Value = null;
                e.Handled = true;
            }
        }

        private void PasteFromClipboard()
        {
            if (!Clipboard.ContainsText()) return;
            var text = Clipboard.GetText();
            if (string.IsNullOrEmpty(text)) return;

            var lines = text.Replace("\r\n", "\n").Replace("\r", "\n").TrimEnd('\n').Split('\n');
            if (lines.Length == 0) return;

            var parsedRows = lines.Select(l => l.Split('\t')).ToList();
            var maxCols = parsedRows.Max(r => r.Length);

            var anchorRow = _grid.CurrentCell?.RowIndex ?? 0;
            var anchorCol = _grid.CurrentCell?.ColumnIndex ?? 0;

            EnsureGridSize(anchorRow + parsedRows.Count, anchorCol + maxCols);

            for (var r = 0; r < parsedRows.Count; r++)
            {
                for (var c = 0; c < parsedRows[r].Length; c++)
                    _grid.Rows[anchorRow + r].Cells[anchorCol + c].Value = parsedRows[r][c];
            }
        }

        private void EnsureGridSize(int minRows, int minCols)
        {
            while (_grid.Columns.Count < minCols)
                AddColumn(suppressPlaceholder: true);
            while (_grid.Rows.Count < minRows)
                AddRow();
        }

        private void Ok()
        {
            if (string.IsNullOrWhiteSpace(_nameBox.Text))
            {
                MessageBox.Show(this, "Snapshot name is required.", "Validation", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                _nameBox.Focus();
                return;
            }

            var name = _nameBox.Text.Trim();
            if (_existingSnapshots != null && _existingSnapshots.Any(s => string.Equals(s.Name, name, StringComparison.OrdinalIgnoreCase)))
            {
                MessageBox.Show(this, $"A snapshot named '{name}' already exists.", "Name Conflict", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            if (!TryBuildResult(out var columns, out var rows, out var error))
            {
                MessageBox.Show(this, error, "Validation", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            Columns = columns;
            Rows = rows;
            DialogResult = DialogResult.OK;
        }

        // Builds the final column/row set: drops columns with a blank header and no data anywhere,
        // assigns a fallback name to columns with data but no header, and drops fully blank rows.
        private bool TryBuildResult(out List<DataTableColumnConfig> columns, out List<Dictionary<string, object>> rows, out string error)
        {
            columns = null;
            rows = null;
            error = null;

            var colCount = _grid.Columns.Count;
            var headerRow = _grid.Rows[0];
            var dataRowIndexes = Enumerable.Range(1, _grid.Rows.Count - 1).ToList();

            var includedCols = new List<int>();
            for (var c = 0; c < colCount; c++)
            {
                var header = Convert.ToString(headerRow.Cells[c].Value ?? "").Trim();
                var hasData = dataRowIndexes.Any(r => !string.IsNullOrWhiteSpace(Convert.ToString(_grid.Rows[r].Cells[c].Value ?? "")));
                if (!string.IsNullOrWhiteSpace(header) || hasData)
                    includedCols.Add(c);
            }

            if (includedCols.Count == 0)
            {
                error = "Enter at least one column header or value.";
                return false;
            }

            var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var logicalNames = new Dictionary<int, string>();
            foreach (var c in includedCols)
            {
                var header = Convert.ToString(headerRow.Cells[c].Value ?? "").Trim();
                logicalNames[c] = SanitizeColumnLogicalName(header, c, used);
            }

            columns = includedCols.Select(c => new DataTableColumnConfig
            {
                LogicalName = logicalNames[c],
                DisplayName = string.IsNullOrWhiteSpace(Convert.ToString(headerRow.Cells[c].Value ?? ""))
                    ? logicalNames[c]
                    : Convert.ToString(headerRow.Cells[c].Value).Trim(),
                Type = null,
                SqliteType = "TEXT"
            }).ToList();

            rows = new List<Dictionary<string, object>>();
            foreach (var r in dataRowIndexes)
            {
                var row = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
                var hasAnyValue = false;
                foreach (var c in includedCols)
                {
                    var value = Convert.ToString(_grid.Rows[r].Cells[c].Value ?? "");
                    if (string.IsNullOrWhiteSpace(value))
                    {
                        row[logicalNames[c]] = null;
                    }
                    else
                    {
                        row[logicalNames[c]] = value;
                        hasAnyValue = true;
                    }
                }
                if (hasAnyValue) rows.Add(row);
            }

            return true;
        }

        private static string SanitizeColumnLogicalName(string header, int fallbackIndex, HashSet<string> used)
        {
            var trimmed = (header ?? "").Trim().ToLowerInvariant();
            var underscored = trimmed.Replace(' ', '_');
            var clean = Regex.Replace(underscored, "[^a-z0-9_]", "");
            clean = clean.Trim('_');
            if (string.IsNullOrEmpty(clean)) clean = $"column{fallbackIndex + 1}";

            var candidate = clean;
            var n = 2;
            while (!used.Add(candidate))
            {
                candidate = $"{clean}_{n}";
                n++;
            }
            return candidate;
        }
    }
}
