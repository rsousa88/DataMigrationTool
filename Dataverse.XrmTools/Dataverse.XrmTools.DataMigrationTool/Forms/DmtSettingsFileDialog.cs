// System
using System;
using System.IO;
using System.Drawing;
using System.Windows.Forms;

// DataMigrationTool
using Dataverse.XrmTools.DataMigrationTool.Models;
using Dataverse.XrmTools.DataMigrationTool.Logic;
using Dataverse.XrmTools.DataMigrationTool.AppSettings;

namespace Dataverse.XrmTools.DataMigrationTool.Forms
{
    public enum DmtFileChoice { NewFile, ExistingFile, WithoutFile, Cancel }

    public class DmtSettingsFileDialog : Form
    {
        // inputs
        private readonly string _tableLogicalName;
        private readonly string _tableDisplayName;
        private readonly string _orgUniqueName;
        private readonly string _orgFriendlyName;
        private readonly TableSettings _existingAppDataSettings;
        private readonly Table _table;

        // outputs
        public DmtFileChoice Choice { get; private set; } = DmtFileChoice.Cancel;
        public string FilePath { get; private set; }
        public DmtSettings LoadedSettings { get; private set; }

        public DmtSettingsFileDialog(Table table, string orgUniqueName, string orgFriendlyName,
            TableSettings existingAppDataSettings)
        {
            _table = table;
            _tableLogicalName = table.LogicalName;
            _tableDisplayName = table.DisplayName;
            _orgUniqueName = orgUniqueName;
            _orgFriendlyName = orgFriendlyName;
            _existingAppDataSettings = existingAppDataSettings;

            BuildUi();
        }

        private void BuildUi()
        {
            Text = "Settings File";
            FormBorderStyle = FormBorderStyle.FixedDialog;
            StartPosition = FormStartPosition.CenterParent;
            MinimizeBox = false;
            MaximizeBox = false;
            Size = new Size(400, 210);
            BackColor = SystemColors.Window;

            var pnl = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                Padding = new Padding(16, 12, 16, 12),
                RowCount = 3,
                ColumnCount = 1,
                AutoSize = false
            };
            pnl.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            pnl.RowStyles.Add(new RowStyle(SizeType.Absolute, 8));
            pnl.RowStyles.Add(new RowStyle(SizeType.AutoSize));

            var heading = new Label
            {
                Text = $"Table: {_tableDisplayName} ({_tableLogicalName})",
                Font = new Font("Segoe UI", 9f, FontStyle.Bold),
                AutoSize = true,
                Margin = new Padding(0, 0, 0, 4)
            };
            pnl.Controls.Add(heading, 0, 0);

            var btnPanel = new FlowLayoutPanel
            {
                FlowDirection = FlowDirection.TopDown,
                AutoSize = true,
                WrapContents = false,
                Dock = DockStyle.Fill
            };

            var btnNew = MakeButton("Save new file...", "Create a new .dmt.json file for this table (pre-fills from existing settings)");
            var btnLoad = MakeButton("Load existing file...", "Open an existing .dmt.json settings file");
            var btnWithout = MakeButton("Continue without file", "Work without a settings file; changes will not be saved");
            btnWithout.Font = new Font("Segoe UI", 8.25f, FontStyle.Regular);
            btnWithout.ForeColor = Color.Gray;

            btnNew.Click += (s, e) => HandleNew();
            btnLoad.Click += (s, e) => HandleLoad();
            btnWithout.Click += (s, e) => { Choice = DmtFileChoice.WithoutFile; DialogResult = DialogResult.OK; };

            btnPanel.Controls.Add(btnNew);
            btnPanel.Controls.Add(btnLoad);
            btnPanel.Controls.Add(btnWithout);

            pnl.Controls.Add(btnPanel, 0, 2);
            Controls.Add(pnl);

            AcceptButton = null;
            CancelButton = null;
            KeyPreview = true;
            KeyDown += (s, e) => { if (e.KeyCode == Keys.Escape) { Choice = DmtFileChoice.Cancel; Close(); } };
        }

        private Button MakeButton(string text, string tooltip)
        {
            var btn = new Button
            {
                Text = text,
                Width = 340,
                Height = 32,
                FlatStyle = FlatStyle.Flat,
                TextAlign = ContentAlignment.MiddleLeft,
                Font = new Font("Segoe UI", 8.25f),
                Margin = new Padding(0, 0, 0, 4),
                Padding = new Padding(6, 0, 0, 0)
            };
            btn.FlatAppearance.BorderColor = Color.LightGray;
            var tip = new ToolTip();
            tip.SetToolTip(btn, tooltip);
            return btn;
        }

        private void HandleNew()
        {
            using (var dlg = new SaveFileDialog
            {
                Title = "Create settings file",
                Filter = "DMT Settings (*.dmt.json)|*.dmt.json",
                DefaultExt = "dmt.json",
                FileName = _tableLogicalName
            })
            {
                if (dlg.ShowDialog(this) != DialogResult.OK) return;

                var settings = DmtFileService.CreateNew(
                    _tableLogicalName, _tableDisplayName,
                    _table.IdAttribute, _table.NameAttribute,
                    _orgUniqueName, _orgFriendlyName,
                    _existingAppDataSettings);

                DmtFileService.Save(dlg.FileName, settings);

                Choice = DmtFileChoice.NewFile;
                FilePath = dlg.FileName;
                LoadedSettings = settings;
                DialogResult = DialogResult.OK;
            }
        }

        private void HandleLoad()
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
                        MessageBox.Show(this, $"Could not read settings file:\n{ex.Message}",
                            "Invalid File", MessageBoxButtons.OK, MessageBoxIcon.Error);
                        continue;
                    }

                    // table validation
                    if (!DmtFileService.ValidateTable(settings, _tableLogicalName))
                    {
                        MessageBox.Show(this,
                            $"This file is for table '{settings.Table?.LogicalName}', but you have '{_tableLogicalName}' selected.\n\nPlease select the correct table first, or choose a different file.",
                            "Wrong Table", MessageBoxButtons.OK, MessageBoxIcon.Error);
                        continue;
                    }

                    // environment validation
                    var (matches, warning) = DmtFileService.ValidateEnvironment(settings, _orgUniqueName, _orgFriendlyName);
                    if (!matches)
                    {
                        var result = MessageBox.Show(this,
                            $"{warning}\n\nContinue anyway?",
                            "Environment Mismatch", MessageBoxButtons.YesNo, MessageBoxIcon.Warning);
                        if (result != DialogResult.Yes) continue;
                    }

                    Choice = DmtFileChoice.ExistingFile;
                    FilePath = dlg.FileName;
                    LoadedSettings = settings;
                    DialogResult = DialogResult.OK;
                    return;
                }
            }
        }
    }
}
