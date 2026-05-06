using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Windows.Forms;
using Dataverse.XrmTools.DataMigrationTool.Enums;
using Dataverse.XrmTools.DataMigrationTool.Helpers;
using Dataverse.XrmTools.DataMigrationTool.Models;

namespace Dataverse.XrmTools.DataMigrationTool.Forms
{
    public class ExcelImportPreviewDialog : Form
    {
        private readonly ExcelImportPreview _preview;
        private readonly ListView _list;
        private readonly CheckBox _cbCreate;
        private readonly CheckBox _cbUpdate;
        private readonly ComboBox _cboMatchMode;
        private readonly ComboBox _cboAlternateKey;
        private readonly Button _btnConfigureMatchKey;
        private readonly Label _lblCustomMatchKey;
        private readonly NumericUpDown _nudBatchSize;
        private readonly CheckBox _cbApplyMappings;
        private List<string> _customMatchFields = new List<string>();
        private int? _sortColumn;

        public UiSettings Settings { get; private set; }
        public ExcelImportMatchKeySelection SelectedMatchKey { get; private set; }
        public bool RefreshPreviewRequested { get; private set; }

        public ExcelImportPreviewDialog(ExcelImportPreview preview)
        {
            _preview = preview;
            Settings = CloneSettings(preview.Settings);
            SelectedMatchKey = GetPreviewSelection(preview);
            _customMatchFields = SelectedMatchKey.Fields.ToList();

            Text = "Import from Excel";
            ClientSize = new Size(1540, 874);
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            ShowIcon = false;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.CenterScreen;
            BackColor = SystemColors.Window;

            var header = BuildHeader();
            var body = new TableLayoutPanel
            {
                BackColor = SystemColors.Window,
                Dock = DockStyle.Fill,
                ColumnCount = 2,
                RowCount = 1
            };
            body.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 72F));
            body.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 28F));

            var resultsGroup = new GroupBox { Text = "Preview", Dock = DockStyle.Fill, Padding = new Padding(4) };
            _list = BuildList();
            resultsGroup.Controls.Add(_list);

            var sidePanel = BuildSidePanel();
            body.Controls.Add(resultsGroup, 0, 0);
            body.Controls.Add(sidePanel, 1, 0);

            var footer = new Panel { BackColor = Color.White, Dock = DockStyle.Bottom, Height = 64 };
            var btnImport = new Button { Text = "Import", Width = 100, Height = 28, Anchor = AnchorStyles.Right | AnchorStyles.Bottom };
            var btnRefresh = new Button { Text = "Refresh Preview", Width = 120, Height = 28, Anchor = AnchorStyles.Right | AnchorStyles.Bottom };
            var btnCancel = new Button { Text = "Cancel", Width = 100, Height = 28, Anchor = AnchorStyles.Right | AnchorStyles.Bottom };
            btnImport.Location = new Point(ClientSize.Width - 116, 21);
            btnRefresh.Location = new Point(ClientSize.Width - 244, 21);
            btnCancel.Location = new Point(ClientSize.Width - 352, 21);
            btnImport.Click += (s, e) => CloseWith(DialogResult.OK);
            btnRefresh.Click += (s, e) => CloseWith(DialogResult.Retry);
            btnCancel.Click += (s, e) => CloseWith(DialogResult.Cancel);
            footer.Controls.Add(btnImport);
            footer.Controls.Add(btnRefresh);
            footer.Controls.Add(btnCancel);
            footer.Resize += (s, e) =>
            {
                btnImport.Left = footer.Width - 116;
                btnRefresh.Left = footer.Width - 244;
                btnCancel.Left = footer.Width - 352;
            };

            Controls.Add(body);
            Controls.Add(footer);
            Controls.Add(header);

            _cbCreate = FindControl<CheckBox>("cbCreate");
            _cbUpdate = FindControl<CheckBox>("cbUpdate");
            _cboMatchMode = FindControl<ComboBox>("cboMatchMode");
            _cboAlternateKey = FindControl<ComboBox>("cboAlternateKey");
            _btnConfigureMatchKey = FindControl<Button>("btnConfigureMatchKey");
            _lblCustomMatchKey = FindControl<Label>("lblCustomMatchKey");
            _nudBatchSize = FindControl<NumericUpDown>("nudBatchSize");
            _cbApplyMappings = FindControl<CheckBox>("cbApplyMappings");
            _btnConfigureMatchKey.Click += (s, e) => ConfigureCustomMatchKey();
            _cboMatchMode.SelectedIndexChanged += (s, e) => UpdateMatchKeyControls();
            UpdateMatchKeyControls();

            Load += (s, e) => LoadItems();
            Resize += (s, e) => ResizeColumns();
        }

        private Control BuildHeader()
        {
            var header = new Panel { BackColor = Color.White, Dock = DockStyle.Top, Height = 71 };
            header.Controls.Add(new Label
            {
                Text = "Import from Excel",
                AutoSize = true,
                Font = new Font("Segoe UI", 14F),
                Location = new Point(4, 0)
            });
            header.Controls.Add(new Label
            {
                Text = $"Preview { _preview.TableLogicalName } import into {_preview.TargetName}",
                AutoSize = true,
                Font = new Font("Segoe UI", 8.25F),
                Location = new Point(7, 42)
            });
            return header;
        }

        private Control BuildSidePanel()
        {
            var panel = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 3, ColumnCount = 1, Padding = new Padding(4) };
            panel.RowStyles.Add(new RowStyle(SizeType.Absolute, 170F));
            panel.RowStyles.Add(new RowStyle(SizeType.Absolute, 315F));
            panel.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));

            var summary = new GroupBox { Text = "Summary", Dock = DockStyle.Fill };
            summary.Controls.Add(BuildSummaryContent());

            var settings = new GroupBox { Text = "Import settings", Dock = DockStyle.Fill };
            settings.Controls.Add(BuildSettingsContent());

            var warnings = new GroupBox { Text = "Warnings", Dock = DockStyle.Fill };
            warnings.Controls.Add(BuildWarningsBox());

            panel.Controls.Add(summary, 0, 0);
            panel.Controls.Add(settings, 0, 1);
            panel.Controls.Add(warnings, 0, 2);
            return panel;
        }

        private Control BuildSummaryContent()
        {
            var panel = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 6, Padding = new Padding(8, 4, 8, 4) };
            panel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 92F));
            panel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            AddSummaryRow(panel, "Rows", _preview.TotalRows.ToString(), 0);
            AddSummaryRow(panel, "Create", _preview.CreateCount.ToString(), 1);
            AddSummaryRow(panel, "Update", _preview.UpdateCount.ToString(), 2);
            AddSummaryRow(panel, "Skipped", _preview.SkippedCount.ToString(), 3);
            AddSummaryRow(panel, "Mappings", _preview.MappingCount.ToString(), 4);
            AddSummaryRow(panel, "Match key", string.IsNullOrWhiteSpace(_preview.MatchKey) ? "record GUID" : _preview.MatchKey, 5);
            return panel;
        }

        private void AddSummaryRow(TableLayoutPanel panel, string label, string value, int row)
        {
            panel.RowStyles.Add(new RowStyle(SizeType.Absolute, 22F));
            panel.Controls.Add(new Label { Text = label + ":", Dock = DockStyle.Fill, AutoEllipsis = true }, 0, row);
            panel.Controls.Add(new Label { Text = value, Dock = DockStyle.Fill, AutoEllipsis = true, TextAlign = ContentAlignment.TopLeft, Font = new Font(Font, FontStyle.Bold) }, 1, row);
        }

        private Control BuildSettingsContent()
        {
            var panel = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 8, Padding = new Padding(10, 6, 10, 6) };
            panel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 92F));
            panel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            for (var i = 0; i < 7; i++) panel.RowStyles.Add(new RowStyle(SizeType.Absolute, 31F));
            panel.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));

            panel.Controls.Add(new Label { Text = "Operations:", Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft }, 0, 0);
            var ops = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.LeftToRight, Margin = Padding.Empty, WrapContents = false };
            ops.Controls.Add(new CheckBox { Name = "cbCreate", Text = "Create", AutoSize = true, Checked = (Settings.Action & Enums.Action.Create) == Enums.Action.Create });
            ops.Controls.Add(new CheckBox { Name = "cbUpdate", Text = "Update", AutoSize = true, Checked = (Settings.Action & Enums.Action.Update) == Enums.Action.Update });
            panel.Controls.Add(ops, 1, 0);

            panel.Controls.Add(new Label { Text = "Match by:", Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft }, 0, 1);
            var mode = new ComboBox { Name = "cboMatchMode", Dock = DockStyle.Fill, DropDownStyle = ComboBoxStyle.DropDownList };
            mode.Items.Add(new MatchKeyModeItem("Guid", "Record GUID"));
            mode.Items.Add(new MatchKeyModeItem("AlternateKey", "Alternate key"));
            mode.Items.Add(new MatchKeyModeItem("Custom", "Custom columns"));
            SelectMode(mode, SelectedMatchKey.Mode);
            panel.Controls.Add(mode, 1, 1);

            panel.Controls.Add(new Label { Text = "Alt key:", Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft }, 0, 2);
            var altKey = new ComboBox { Name = "cboAlternateKey", Dock = DockStyle.Fill, DropDownStyle = ComboBoxStyle.DropDownList };
            foreach (var key in _preview.AvailableAlternateKeys)
                altKey.Items.Add(MatchKeyItem.AlternateKey(key.Name, key.Fields));
            SelectAlternateKey(altKey, SelectedMatchKey);
            if (altKey.SelectedIndex < 0 && altKey.Items.Count > 0) altKey.SelectedIndex = 0;
            panel.Controls.Add(altKey, 1, 2);

            panel.Controls.Add(new Label { Text = "Custom:", Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft }, 0, 3);
            var custom = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1, Margin = Padding.Empty };
            custom.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 82F));
            custom.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            custom.Controls.Add(new Button { Name = "btnConfigureMatchKey", Text = "Select...", Dock = DockStyle.Fill, Margin = new Padding(0, 2, 6, 2) }, 0, 0);
            custom.Controls.Add(new Label { Name = "lblCustomMatchKey", Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft, AutoEllipsis = true }, 1, 0);
            panel.Controls.Add(custom, 1, 3);

            panel.Controls.Add(new Label { Text = "Batch size:", Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft }, 0, 4);
            panel.Controls.Add(new NumericUpDown { Name = "nudBatchSize", Dock = DockStyle.Left, Width = 86, Minimum = 1, Maximum = 5000, Value = Math.Max(1, Math.Min(5000, Settings.BatchSize)) }, 1, 4);

            panel.Controls.Add(new Label { Text = "Mappings:", Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft }, 0, 5);
            panel.Controls.Add(new CheckBox { Name = "cbApplyMappings", Text = "Apply mappings during import", AutoSize = true, Checked = Settings.ApplyMappingsOn == Operation.Import }, 1, 5);

            var note = new Label
            {
                Text = "Change the match key or settings, then refresh the preview before importing.",
                Dock = DockStyle.Fill,
                ForeColor = SystemColors.GrayText
            };
            panel.Controls.Add(note, 0, 6);
            panel.SetColumnSpan(note, 2);
            return panel;
        }

        private Control BuildWarningsBox()
        {
            var text = new TextBox
            {
                Dock = DockStyle.Fill,
                Multiline = true,
                ReadOnly = true,
                ScrollBars = ScrollBars.Vertical,
                Text = _preview.ImportErrors.Any()
                    ? string.Join(Environment.NewLine, _preview.ImportErrors.Take(30))
                    : "No warnings."
            };
            return text;
        }

        private ListView BuildList()
        {
            var list = new ListView
            {
                Dock = DockStyle.Fill,
                View = View.Details,
                FullRowSelect = true,
                HideSelection = false,
                MultiSelect = true,
                Sorting = SortOrder.None
            };
            list.ColumnClick += List_ColumnClick;
            list.KeyUp += List_KeyUp;
            list.Columns.Add("Row");
            list.Columns.Add("Action");
            list.Columns.Add("Record ID");
            list.Columns.Add("Match Value");
            list.Columns.Add("Record Name");
            list.Columns.Add("Description");
            list.Columns.Add("Warnings");
            return list;
        }

        private void LoadItems()
        {
            _list.Items.Clear();
            foreach (var item in _preview.Items)
            {
                var listItem = new ListViewItem(new[]
                {
                    item.RowNumber > 0 ? item.RowNumber.ToString() : string.Empty,
                    item.Action,
                    item.RecordId,
                    item.MatchValue,
                    item.Name,
                    item.Description,
                    item.Warnings
                });
                if (!string.IsNullOrWhiteSpace(item.Warnings))
                {
                    listItem.BackColor = Color.FromArgb(255, 250, 230);
                    listItem.ForeColor = Color.DarkGoldenrod;
                }
                _list.Items.Add(listItem);
            }
            ResizeColumns();
        }

        private void ResizeColumns()
        {
            if (_list == null) return;
            var width = Math.Max(_list.ClientSize.Width, 700);
            _list.Columns[0].Width = (int)(width * 0.06);
            _list.Columns[1].Width = (int)(width * 0.09);
            _list.Columns[2].Width = (int)(width * 0.20);
            _list.Columns[3].Width = (int)(width * 0.16);
            _list.Columns[4].Width = (int)(width * 0.17);
            _list.Columns[5].Width = (int)(width * 0.18);
            _list.Columns[6].Width = (int)(width * 0.13);
        }

        private void CloseWith(DialogResult result)
        {
            Settings = ReadSettings();
            SelectedMatchKey = ReadMatchKeySelection();
            RefreshPreviewRequested = result == DialogResult.Retry;
            DialogResult = result;
            Close();
        }

        private void List_ColumnClick(object sender, ColumnClickEventArgs e)
        {
            var list = sender as ListView;
            if (list == null) return;

            if (!_sortColumn.HasValue || _sortColumn.Value != e.Column)
            {
                _sortColumn = e.Column;
                list.Sorting = SortOrder.Ascending;
            }
            else
            {
                list.Sorting = list.Sorting == SortOrder.Ascending ? SortOrder.Descending : SortOrder.Ascending;
            }

            list.ListViewItemSorter = new ListViewComparer(e.Column, list.Sorting);
            list.Sort();
        }

        private void List_KeyUp(object sender, KeyEventArgs e)
        {
            if (sender != _list) return;
            if (e.Control && e.KeyCode == Keys.C)
                CopySelectedValuesToClipboard();
        }

        private void CopySelectedValuesToClipboard()
        {
            if (_list.SelectedItems.Count == 0) return;

            var builder = new StringBuilder();
            foreach (ColumnHeader column in _list.Columns)
                builder.Append($"{column.Text};");
            builder.AppendLine();

            foreach (ListViewItem item in _list.SelectedItems)
            {
                foreach (ListViewItem.ListViewSubItem sub in item.SubItems)
                    builder.Append($"{sub.Text};");
                builder.AppendLine();
            }

            Clipboard.SetText(builder.ToString());
        }

        private void ConfigureCustomMatchKey()
        {
            var current = ReadMatchKeySelection().Fields;
            using (var dlg = new MatchColumnsDialog(_preview.AvailableMatchKeys, current))
            {
                if (dlg.ShowDialog(this) != DialogResult.OK) return;
                _customMatchFields = dlg.SelectedColumns;
                SelectMode(_cboMatchMode, "Custom");
                UpdateMatchKeyControls();
            }
        }

        private void UpdateMatchKeyControls()
        {
            if (_btnConfigureMatchKey == null || _cboMatchMode == null) return;
            var mode = (_cboMatchMode.SelectedItem as MatchKeyModeItem)?.Mode ?? "Guid";
            _cboAlternateKey.Enabled = mode == "AlternateKey" && _cboAlternateKey.Items.Count > 0;
            _btnConfigureMatchKey.Enabled = mode == "Custom" && _preview.AvailableMatchKeys.Any();
            _lblCustomMatchKey.Text = mode == "Custom" ? GetCustomMatchKeyText(_customMatchFields) : string.Empty;
        }

        private ExcelImportMatchKeySelection ReadMatchKeySelection()
        {
            var mode = (_cboMatchMode.SelectedItem as MatchKeyModeItem)?.Mode ?? "Guid";
            if (mode == "AlternateKey" && _cboAlternateKey.SelectedItem is MatchKeyItem selected)
            {
                return new ExcelImportMatchKeySelection
                {
                    Mode = selected.Mode,
                    Fields = selected.Fields.ToList(),
                    AlternateKeyName = selected.AlternateKeyName
                };
            }

            if (mode == "Custom")
                return new ExcelImportMatchKeySelection { Mode = "Custom", Fields = _customMatchFields.ToList() };

            return new ExcelImportMatchKeySelection { Mode = "Guid" };
        }

        private void SelectMode(ComboBox combo, string mode)
        {
            mode = string.IsNullOrWhiteSpace(mode) ? "Guid" : mode;
            for (var i = 0; i < combo.Items.Count; i++)
            {
                if ((combo.Items[i] as MatchKeyModeItem)?.Mode == mode)
                {
                    combo.SelectedIndex = i;
                    return;
                }
            }
            combo.SelectedIndex = 0;
        }

        private void SelectAlternateKey(ComboBox combo, ExcelImportMatchKeySelection selection)
        {
            for (var i = 0; i < combo.Items.Count; i++)
            {
                if ((combo.Items[i] as MatchKeyItem)?.Matches(selection) == true)
                {
                    combo.SelectedIndex = i;
                    return;
                }
            }
        }

        private ExcelImportMatchKeySelection GetPreviewSelection(ExcelImportPreview preview)
        {
            return new ExcelImportMatchKeySelection
            {
                Mode = string.IsNullOrWhiteSpace(preview.MatchKeyMode) ? (preview.MatchKeys.Any() ? "Custom" : "Guid") : preview.MatchKeyMode,
                Fields = preview.MatchKeys?.ToList() ?? new List<string>(),
                AlternateKeyName = preview.MatchAlternateKeyName
            };
        }

        private string GetCustomMatchKeyText(List<string> fields)
        {
            return fields?.Any() == true
                ? $"custom: {string.Join(", ", fields)}"
                : "custom columns...";
        }

        private UiSettings ReadSettings()
        {
            var action = Enums.Action.None;
            if (_cbCreate.Checked) action |= Enums.Action.Create;
            if (_cbUpdate.Checked) action |= Enums.Action.Update;

            return new UiSettings
            {
                Action = action,
                BatchSize = (int)_nudBatchSize.Value,
                MapUsers = false,
                MapTeams = false,
                MapBu = Settings.MapBu,
                ApplyMappingsOn = _cbApplyMappings.Checked ? Operation.Import : Operation.Export,
                HideInvalidAttributes = Settings.HideInvalidAttributes
            };
        }

        private UiSettings CloneSettings(UiSettings settings)
        {
            settings = settings ?? new UiSettings();
            return new UiSettings
            {
                Action = settings.Action,
                BatchSize = settings.BatchSize <= 0 ? 250 : settings.BatchSize,
                MapUsers = false,
                MapTeams = false,
                MapBu = settings.MapBu,
                ApplyMappingsOn = settings.ApplyMappingsOn,
                HideInvalidAttributes = settings.HideInvalidAttributes
            };
        }

        private T FindControl<T>(string name) where T : Control
        {
            return Controls.Find(name, true).OfType<T>().FirstOrDefault();
        }

        private class MatchKeyItem
        {
            public string Mode { get; }
            public List<string> Fields { get; }
            public string AlternateKeyName { get; }
            private readonly string _display;

            private MatchKeyItem(string mode, List<string> fields, string alternateKeyName, string display)
            {
                Mode = mode;
                Fields = fields ?? new List<string>();
                AlternateKeyName = alternateKeyName;
                _display = display;
            }

            public static MatchKeyItem Guid() => new MatchKeyItem("Guid", new List<string>(), null, "record GUID");

            public static MatchKeyItem AlternateKey(string name, List<string> fields)
            {
                return new MatchKeyItem("AlternateKey", fields, name, $"alternate key: {name} ({string.Join(", ", fields)})");
            }

            public static MatchKeyItem Custom(List<string> fields, string display) => new MatchKeyItem("Custom", fields, null, display);

            public bool Matches(ExcelImportMatchKeySelection selection)
            {
                if (selection == null) return Mode == "Guid";
                if (!string.Equals(Mode, selection.Mode, StringComparison.OrdinalIgnoreCase)) return false;
                if (Mode == "AlternateKey" && !string.Equals(AlternateKeyName, selection.AlternateKeyName, StringComparison.OrdinalIgnoreCase)) return false;
                return Fields.SequenceEqual(selection.Fields ?? new List<string>(), StringComparer.OrdinalIgnoreCase);
            }

            public override string ToString() => _display;
        }

        private class MatchKeyModeItem
        {
            public string Mode { get; }
            private readonly string _display;

            public MatchKeyModeItem(string mode, string display)
            {
                Mode = mode;
                _display = display;
            }

            public override string ToString() => _display;
        }

        private class MatchColumnsDialog : Form
        {
            private readonly CheckedListBox _columns;
            public List<string> SelectedColumns { get; private set; } = new List<string>();

            public MatchColumnsDialog(List<string> availableColumns, List<string> selectedColumns)
            {
                Text = "Select matching columns";
                ClientSize = new Size(420, 430);
                FormBorderStyle = FormBorderStyle.FixedDialog;
                MaximizeBox = false;
                MinimizeBox = false;
                ShowIcon = false;
                ShowInTaskbar = false;
                StartPosition = FormStartPosition.CenterParent;

                _columns = new CheckedListBox
                {
                    Dock = DockStyle.Fill,
                    CheckOnClick = true
                };

                var selected = new HashSet<string>(selectedColumns ?? new List<string>(), StringComparer.OrdinalIgnoreCase);
                foreach (var column in availableColumns.OrderBy(c => c))
                    _columns.Items.Add(column, selected.Contains(column));

                var footer = new Panel { Dock = DockStyle.Bottom, Height = 52 };
                var ok = new Button { Text = "OK", Width = 88, Height = 28, Left = 224, Top = 12 };
                var cancel = new Button { Text = "Cancel", Width = 88, Height = 28, Left = 318, Top = 12 };
                ok.Click += (s, e) =>
                {
                    SelectedColumns = _columns.CheckedItems.Cast<string>().ToList();
                    DialogResult = DialogResult.OK;
                    Close();
                };
                cancel.Click += (s, e) =>
                {
                    DialogResult = DialogResult.Cancel;
                    Close();
                };
                footer.Controls.Add(ok);
                footer.Controls.Add(cancel);
                Controls.Add(_columns);
                Controls.Add(footer);
            }
        }
    }
}
