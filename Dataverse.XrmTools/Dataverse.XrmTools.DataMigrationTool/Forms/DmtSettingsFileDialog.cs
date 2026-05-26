// System
using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Windows.Forms;

// DataMigrationTool
using Dataverse.XrmTools.DataMigrationTool.AppSettings;
using Dataverse.XrmTools.DataMigrationTool.Logic;
using Dataverse.XrmTools.DataMigrationTool.Models;

namespace Dataverse.XrmTools.DataMigrationTool.Forms
{
    public enum DmtFileChoice { NewFile, ExistingFile, WithoutFile, Cancel }

    public class DmtSettingsFileDialog : Form
    {
        private readonly string _orgUniqueName;
        private readonly string _orgFriendlyName;
        private readonly Func<Table, TableSettings> _existingSettingsProvider;
        private readonly List<Table> _tables;
        private Label _settingsStatus;

        public DmtFileChoice Choice { get; private set; } = DmtFileChoice.Cancel;
        public string FilePath { get; private set; }
        public DmtSettings LoadedSettings { get; private set; }
        public Table SelectedTable { get; private set; }

        public DmtSettingsFileDialog(Table table, string orgUniqueName, string orgFriendlyName, TableSettings existingAppDataSettings)
            : this(new[] { table }, orgUniqueName, orgFriendlyName, _ => existingAppDataSettings, false)
        {
            SelectedTable = table;
        }

        public DmtSettingsFileDialog(IEnumerable<Table> tables, string orgUniqueName, string orgFriendlyName, Func<Table, TableSettings> existingSettingsProvider)
            : this(tables, orgUniqueName, orgFriendlyName, existingSettingsProvider, false)
        {
        }

        private DmtSettingsFileDialog(IEnumerable<Table> tables, string orgUniqueName, string orgFriendlyName, Func<Table, TableSettings> existingSettingsProvider, bool unused = false)
        {
            _tables = (tables ?? Enumerable.Empty<Table>())
                .Where(t => t != null && !string.IsNullOrWhiteSpace(t.LogicalName))
                .OrderBy(t => string.IsNullOrWhiteSpace(t.DisplayName) ? t.LogicalName : t.DisplayName)
                .ToList();
            _orgUniqueName = orgUniqueName;
            _orgFriendlyName = orgFriendlyName;
            _existingSettingsProvider = existingSettingsProvider;
            SelectedTable = _tables.FirstOrDefault();

            BuildUi();
        }

        private void BuildUi()
        {
            Text = "Settings File";
            FormBorderStyle = FormBorderStyle.FixedDialog;
            StartPosition = FormStartPosition.CenterParent;
            MinimizeBox = false;
            MaximizeBox = false;
            ClientSize = new Size(520, 180);
            BackColor = SystemColors.Window;

            var pnl = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                Padding = new Padding(16, 12, 16, 12),
                RowCount = 2,
                ColumnCount = 1
            };
            pnl.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            pnl.RowStyles.Add(new RowStyle(SizeType.Absolute, 48));

            pnl.Controls.Add(BuildSettingsSection(), 0, 0);
            pnl.Controls.Add(BuildFooter(), 0, 1);
            Controls.Add(pnl);

            KeyPreview = true;
            KeyDown += (s, e) =>
            {
                if (e.KeyCode == Keys.Escape)
                {
                    Choice = DmtFileChoice.Cancel;
                    Close();
                }
            };
        }

        private Control BuildSettingsSection()
        {
            var group = new GroupBox
            {
                Text = "Settings File",
                Dock = DockStyle.Top,
                Height = 112,
                Padding = new Padding(8)
            };

            var layout = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 2, ColumnCount = 1 };
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 42));
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

            var buttons = BuildButtonRow();
            AddRowButton(buttons, 0, "Create...", "Create a new .dmt.json settings file", HandleNewSettings);
            AddRowButton(buttons, 1, "Load...", "Load an existing .dmt.json settings file", HandleLoadSettings);
            AddRowButton(buttons, 2, "No file", "Continue without a settings file", (s, e) =>
            {
                SelectedTable = GetSelectedTable();
                Choice = DmtFileChoice.WithoutFile;
                FilePath = null;
                LoadedSettings = null;
                _settingsStatus.Text = "No settings file";
            });

            _settingsStatus = new Label
            {
                Text = "No settings file selected",
                Dock = DockStyle.Fill,
                AutoEllipsis = true,
                TextAlign = ContentAlignment.MiddleLeft
            };

            layout.Controls.Add(buttons, 0, 0);
            layout.Controls.Add(_settingsStatus, 0, 1);
            group.Controls.Add(layout);
            return group;
        }

        private Control BuildFooter()
        {
            var footer = new FlowLayoutPanel
            {
                Dock = DockStyle.Fill,
                FlowDirection = FlowDirection.RightToLeft,
                Padding = new Padding(0, 10, 0, 0)
            };
            var btnContinue = new Button { Text = "OK", Width = 90, Height = 28 };
            var btnCancel = new Button { Text = "Cancel", Width = 90, Height = 28, DialogResult = DialogResult.Cancel };
            btnContinue.Click += (s, e) =>
            {
                if (Choice == DmtFileChoice.Cancel)
                {
                    SelectedTable = GetSelectedTable();
                    Choice = DmtFileChoice.WithoutFile;
                }

                DialogResult = DialogResult.OK;
                Close();
            };
            btnCancel.Click += (s, e) =>
            {
                Choice = DmtFileChoice.Cancel;
                Close();
            };
            footer.Controls.Add(btnCancel);
            footer.Controls.Add(btnContinue);
            return footer;
        }

        private TableLayoutPanel BuildButtonRow()
        {
            var panel = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 3,
                RowCount = 1
            };
            panel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 33.333F));
            panel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 33.333F));
            panel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 33.334F));
            panel.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
            return panel;
        }

        private void AddRowButton(TableLayoutPanel panel, int column, string text, string tooltip, EventHandler click)
        {
            panel.Controls.Add(MakeButton(text, tooltip, click), column, 0);
        }

        private Button MakeButton(string text, string tooltip, EventHandler click)
        {
            var btn = new Button
            {
                Text = text,
                Dock = DockStyle.Fill,
                FlatStyle = FlatStyle.Flat,
                TextAlign = ContentAlignment.MiddleCenter,
                Font = new Font("Segoe UI", 8.25f),
                Margin = new Padding(2, 0, 2, 4)
            };
            btn.FlatAppearance.BorderColor = Color.LightGray;
            btn.Click += click;
            new ToolTip().SetToolTip(btn, tooltip);
            return btn;
        }

        private void HandleNewSettings(object sender, EventArgs e)
        {
            var table = GetSelectedTable();
            if (table == null)
            {
                MessageBox.Show(this, "Select a table on the main page before creating a settings file.", "Settings File", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            using (var dlg = new SaveFileDialog
            {
                Title = "Create settings file",
                Filter = "DMT Settings (*.dmt.json)|*.dmt.json",
                DefaultExt = "dmt.json",
                FileName = table.LogicalName
            })
            {
                if (dlg.ShowDialog(this) != DialogResult.OK) return;

                var settings = DmtFileService.CreateNew(
                    table.LogicalName,
                    table.DisplayName,
                    table.IdAttribute,
                    table.NameAttribute,
                    _orgUniqueName,
                    _orgFriendlyName,
                    _existingSettingsProvider?.Invoke(table));

                DmtFileService.Save(dlg.FileName, settings);

                SelectedTable = table;
                Choice = DmtFileChoice.NewFile;
                FilePath = dlg.FileName;
                LoadedSettings = settings;
                _settingsStatus.Text = $"Created: {Path.GetFileName(dlg.FileName)}";
            }
        }

        private void HandleLoadSettings(object sender, EventArgs e)
        {
            while (true)
            {
                using (var dlg = new OpenFileDialog
                {
                    Title = "Load settings file",
                    Filter = "DMT Settings (*.dmt.json)|*.dmt.json"
                })
                {
                    if (dlg.ShowDialog(this) != DialogResult.OK) return;

                    DmtSettings settings;
                    try { settings = DmtFileService.Load(dlg.FileName); }
                    catch (Exception ex)
                    {
                        MessageBox.Show(this, $"Could not read settings file:\n{ex.Message}", "Invalid File", MessageBoxButtons.OK, MessageBoxIcon.Error);
                        continue;
                    }

                    var (matches, warning) = DmtFileService.ValidateEnvironment(settings, _orgUniqueName, _orgFriendlyName);
                    if (!matches)
                    {
                        var result = MessageBox.Show(this, $"{warning}\n\nContinue anyway?", "Environment Mismatch", MessageBoxButtons.YesNo, MessageBoxIcon.Warning);
                        if (result != DialogResult.Yes) continue;
                    }

                    SelectedTable = _tables.FirstOrDefault(t => string.Equals(t.LogicalName, settings?.Table?.LogicalName, StringComparison.OrdinalIgnoreCase));
                    Choice = DmtFileChoice.ExistingFile;
                    FilePath = dlg.FileName;
                    LoadedSettings = settings;
                    _settingsStatus.Text = $"Loaded: {Path.GetFileName(dlg.FileName)}";
                    return;
                }
            }
        }

        private Table GetSelectedTable()
        {
            return SelectedTable ?? _tables.FirstOrDefault();
        }

        private static string GetTableText(Table table)
        {
            if (table == null) return "(none)";
            return string.IsNullOrWhiteSpace(table.DisplayName)
                ? table.LogicalName
                : $"{table.DisplayName} ({table.LogicalName})";
        }

        private class TableListItem
        {
            public Table Table { get; }
            public string Text => GetTableText(Table);

            public TableListItem(Table table)
            {
                Table = table;
            }
        }
    }
}
