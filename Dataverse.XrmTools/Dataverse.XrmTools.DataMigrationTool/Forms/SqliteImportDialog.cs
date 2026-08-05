// System
using System;
using System.Drawing;
using System.Windows.Forms;

// DataMigrationTool
using Dataverse.XrmTools.DataMigrationTool.Logic;

namespace Dataverse.XrmTools.DataMigrationTool.Forms
{
    // Two-step wizard: pick a SQLite database file, then pick one table from it to import as a snapshot.
    public class SqliteImportDialog : Form
    {
        private Panel _step1Panel;
        private Panel _step2Panel;
        private TextBox _filePathBox;
        private Label _step1StatusLabel;
        private ListView _tableList;
        private Button _backBtn;
        private Button _nextBtn;
        private Button _importBtn;
        private Button _cancelBtn;

        public string DatabaseFilePath { get; private set; }
        public string SelectedTable { get; private set; }

        public SqliteImportDialog()
        {
            Text = "Import from SQLite Database";
            FormBorderStyle = FormBorderStyle.FixedDialog;
            StartPosition = FormStartPosition.CenterParent;
            ShowIcon = false;
            ShowInTaskbar = false;
            MaximizeBox = false;
            MinimizeBox = false;
            ClientSize = new Size(480, 380);

            var outer = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                RowCount = 2,
                ColumnCount = 1,
                Padding = new Padding(10)
            };
            outer.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
            outer.RowStyles.Add(new RowStyle(SizeType.Absolute, 36));

            BuildStep1Panel();
            BuildStep2Panel();
            outer.Controls.Add(_step1Panel, 0, 0);
            outer.Controls.Add(_step2Panel, 0, 0);

            var btnPanel = new FlowLayoutPanel
            {
                Dock = DockStyle.Fill,
                FlowDirection = FlowDirection.RightToLeft,
                Padding = new Padding(0, 4, 0, 0)
            };
            _cancelBtn = new Button { Text = "Cancel", DialogResult = DialogResult.Cancel, Width = 75 };
            _importBtn = new Button { Text = "Import", Width = 75, Enabled = false };
            _importBtn.Click += (s, e) => Import();
            _nextBtn = new Button { Text = "Next >", Width = 75, Enabled = false };
            _nextBtn.Click += (s, e) => GoToStep2();
            _backBtn = new Button { Text = "< Back", Width = 75 };
            _backBtn.Click += (s, e) => GoToStep1();
            btnPanel.Controls.AddRange(new Control[] { _cancelBtn, _importBtn, _nextBtn, _backBtn });
            outer.Controls.Add(btnPanel, 0, 1);

            Controls.Add(outer);
            CancelButton = _cancelBtn;

            ShowStep1();
        }

        private void BuildStep1Panel()
        {
            _step1Panel = new Panel { Dock = DockStyle.Fill };

            var layout = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                RowCount = 3,
                ColumnCount = 1
            };
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 20));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 30));
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));

            var label = new Label { Text = "Select a SQLite database file (.db, .sqlite, .sqlite3):", AutoSize = true, Dock = DockStyle.Fill };
            layout.Controls.Add(label, 0, 0);

            var filePanel = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 1, ColumnCount = 2 };
            filePanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            filePanel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 90));
            _filePathBox = new TextBox { Dock = DockStyle.Fill, ReadOnly = true };
            var browseBtn = new Button { Text = "Browse...", Dock = DockStyle.Fill };
            browseBtn.Click += (s, e) => BrowseForFile();
            filePanel.Controls.Add(_filePathBox, 0, 0);
            filePanel.Controls.Add(browseBtn, 1, 0);
            layout.Controls.Add(filePanel, 0, 1);

            _step1StatusLabel = new Label { Dock = DockStyle.Fill, AutoSize = false, ForeColor = Color.DimGray, TextAlign = ContentAlignment.TopLeft };
            layout.Controls.Add(_step1StatusLabel, 0, 2);

            _step1Panel.Controls.Add(layout);
        }

        private void BuildStep2Panel()
        {
            _step2Panel = new Panel { Dock = DockStyle.Fill, Visible = false };

            var layout = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                RowCount = 2,
                ColumnCount = 1
            };
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 20));
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));

            var label = new Label { Text = "Select a table to import:", AutoSize = true, Dock = DockStyle.Fill };
            layout.Controls.Add(label, 0, 0);

            _tableList = new ListView
            {
                Dock = DockStyle.Fill,
                View = View.Details,
                FullRowSelect = true,
                MultiSelect = false,
                GridLines = true,
                HeaderStyle = ColumnHeaderStyle.Nonclickable
            };
            _tableList.Columns.Add("Table", 320);
            _tableList.Columns.Add("Rows", 100, HorizontalAlignment.Right);
            _tableList.SelectedIndexChanged += (s, e) => _importBtn.Enabled = _tableList.SelectedItems.Count > 0;
            _tableList.DoubleClick += (s, e) => { if (_tableList.SelectedItems.Count > 0) Import(); };
            layout.Controls.Add(_tableList, 0, 1);

            _step2Panel.Controls.Add(layout);
        }

        private void BrowseForFile()
        {
            using (var ofd = new OpenFileDialog
            {
                Title = "Select SQLite Database",
                Filter = "SQLite database files (*.db;*.sqlite;*.sqlite3)|*.db;*.sqlite;*.sqlite3|All files (*.*)|*.*",
                CheckFileExists = true
            })
            {
                if (ofd.ShowDialog(this) != DialogResult.OK) return;

                _step1StatusLabel.ForeColor = Color.DimGray;
                _step1StatusLabel.Text = "Reading tables...";
                Cursor = Cursors.WaitCursor;
                try
                {
                    var tables = SqliteFileAdapter.GetSqliteTables(ofd.FileName);
                    if (tables.Count == 0)
                    {
                        _step1StatusLabel.ForeColor = Color.Firebrick;
                        _step1StatusLabel.Text = "This database file has no tables.";
                        _nextBtn.Enabled = false;
                        return;
                    }

                    DatabaseFilePath = ofd.FileName;
                    _filePathBox.Text = ofd.FileName;
                    _tableList.Items.Clear();
                    foreach (var t in tables)
                    {
                        var item = new ListViewItem(t.Name);
                        item.SubItems.Add(t.RowCount.ToString("N0"));
                        item.Tag = t.Name;
                        _tableList.Items.Add(item);
                    }

                    _step1StatusLabel.Text = $"Found {tables.Count:N0} table(s).";
                    _nextBtn.Enabled = true;
                }
                catch (Exception ex)
                {
                    _step1StatusLabel.ForeColor = Color.Firebrick;
                    _step1StatusLabel.Text = $"Could not read database: {ex.Message}";
                    _nextBtn.Enabled = false;
                }
                finally
                {
                    Cursor = Cursors.Default;
                }
            }
        }

        private void ShowStep1()
        {
            _step2Panel.Visible = false;
            _step1Panel.Visible = true;
            _backBtn.Visible = false;
            _importBtn.Visible = false;
            _nextBtn.Visible = true;
            AcceptButton = _nextBtn;
        }

        private void GoToStep1() => ShowStep1();

        private void GoToStep2()
        {
            if (string.IsNullOrEmpty(DatabaseFilePath)) return;
            _step1Panel.Visible = false;
            _step2Panel.Visible = true;
            _backBtn.Visible = true;
            _nextBtn.Visible = false;
            _importBtn.Visible = true;
            AcceptButton = _importBtn;
        }

        private void Import()
        {
            if (_tableList.SelectedItems.Count == 0) return;
            SelectedTable = _tableList.SelectedItems[0].Tag as string;
            DialogResult = DialogResult.OK;
        }
    }
}
