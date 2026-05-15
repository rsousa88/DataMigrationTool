using System;
using System.IO;
using System.Linq;
using System.Drawing;
using System.Windows.Forms;
using System.Collections.Generic;
using Microsoft.Xrm.Sdk.Metadata;
using Dataverse.XrmTools.DataMigrationTool.Models;
using Dataverse.XrmTools.DataMigrationTool.AppSettings;
using Dataverse.XrmTools.DataMigrationTool.Helpers;
using Dataverse.XrmTools.DataMigrationTool.Repositories;

namespace Dataverse.XrmTools.DataMigrationTool.Forms
{
    public class ExcelExportConfigDialog : Form
    {
        private readonly List<AttributeMetadata> _selectedAttributes;
        private readonly EntityMetadata _metadata;
        private readonly CrmRepo _repo;

        // Per-attribute lookup state
        private readonly Dictionary<string, ComboBox> _targetTableCombos = new Dictionary<string, ComboBox>();
        private readonly Dictionary<string, ComboBox> _altKeyCombos = new Dictionary<string, ComboBox>();
        private readonly Dictionary<string, RadioButton> _altKeyRadios = new Dictionary<string, RadioButton>();
        private readonly Dictionary<string, RadioButton> _customRadios = new Dictionary<string, RadioButton>();
        private readonly Dictionary<string, List<string>> _customSelections = new Dictionary<string, List<string>>();
        private readonly Dictionary<string, Label> _customSelectionLabels = new Dictionary<string, Label>();
        private readonly Dictionary<string, NestedLookupConfig> _nestedLookupConfigs = new Dictionary<string, NestedLookupConfig>();
        private readonly Dictionary<string, ColumnManagerState> _columnStates = new Dictionary<string, ColumnManagerState>();

        // Per-attribute option set state
        private readonly Dictionary<string, RadioButton> _optionLabelRadios = new Dictionary<string, RadioButton>();
        private DataGridView _columnsGrid;
        private ComboBox _matchKeyModeCombo;
        private ComboBox _matchKeyAltCombo;
        private Label _matchKeyCustomLabel;
        private Button _matchKeyCustomButton;
        private Label _stepLabel;
        private Button _btnPrevious;
        private Button _btnNext;
        private Button _btnAddToPlan;
        private TextBox _reviewText;
        private List<string> _matchKeyCustomFields = new List<string>();
        private bool _columnsTabInitialized;

        public ExcelExportConfig Config { get; private set; }
        public bool AddToPlanRequested { get; private set; }

        private ExcelExportConfig _savedConfig;

        public ExcelExportConfigDialog(IEnumerable<AttributeMetadata> selectedAttributes, EntityMetadata metadata, CrmRepo repo, ExcelExportConfig savedConfig = null)
        {
            _selectedAttributes = selectedAttributes.ToList();
            _metadata = metadata;
            _repo = repo;
            _savedConfig = savedConfig;

            BuildLayout();
            PopulateLookupSection();
            PopulateOptionSetSection();

            if (_savedConfig != null) ApplySavedConfig(_savedConfig);
        }

        #region Layout

        private Panel _lookupPanel;
        private Panel _optionSetPanel;
        private TabControl _tabs;

        private void BuildLayout()
        {
            Text = "Excel Export Configuration";
            ClientSize = new Size(980, 740);
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            StartPosition = FormStartPosition.CenterParent;
            ShowIcon = false;
            ShowInTaskbar = false;

            var outer = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                RowCount = 3,
                ColumnCount = 1,
                Padding = new Padding(10)
            };
            outer.RowStyles.Add(new RowStyle(SizeType.Absolute, 34F));
            outer.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
            outer.RowStyles.Add(new RowStyle(SizeType.Absolute, 48F));

            _stepLabel = new Label
            {
                Dock = DockStyle.Fill,
                Font = new Font(Font, FontStyle.Bold),
                TextAlign = ContentAlignment.MiddleLeft
            };

            _tabs = new TabControl
            {
                Dock = DockStyle.Fill,
                Appearance = TabAppearance.FlatButtons,
                SizeMode = TabSizeMode.Fixed,
                ItemSize = new Size(0, 1),
                TabStop = false
            };
            var tabLookups = new TabPage("Lookups");
            var tabOptionSets = new TabPage("Option Sets");
            var tabColumns = new TabPage("Columns");
            var tabReview = new TabPage("Review");
            _tabs.TabPages.Add(tabLookups);
            _tabs.TabPages.Add(tabOptionSets);
            _tabs.TabPages.Add(tabColumns);
            _tabs.TabPages.Add(tabReview);
            _tabs.SelectedIndexChanged += (s, e) =>
            {
                if (_tabs.SelectedTab == tabColumns) RefreshColumnsGrid();
                if (_tabs.SelectedTab == tabReview) RefreshReview();
                UpdateWizardNavigation();
            };

            var grpLookup = new GroupBox { Text = "A — Lookup resolution", Dock = DockStyle.Fill, Padding = new Padding(5) };
            _lookupPanel = new Panel { Dock = DockStyle.Fill, AutoScroll = true };
            grpLookup.Controls.Add(_lookupPanel);
            tabLookups.Controls.Add(grpLookup);

            var grpOptionSet = new GroupBox { Text = "B — Option sets", Dock = DockStyle.Fill, Padding = new Padding(5) };
            _optionSetPanel = new Panel { Dock = DockStyle.Fill, AutoScroll = true };
            grpOptionSet.Controls.Add(_optionSetPanel);
            tabOptionSets.Controls.Add(grpOptionSet);
            tabColumns.Controls.Add(BuildColumnsTab());
            tabReview.Controls.Add(BuildReviewTab());

            var btnRow = new FlowLayoutPanel
            {
                Dock = DockStyle.Fill,
                FlowDirection = FlowDirection.RightToLeft,
                Padding = new Padding(0, 8, 0, 0)
            };
            var btnCancel = new Button { Text = "Cancel", Width = 90, Height = 30, DialogResult = DialogResult.Cancel };
            _btnAddToPlan = new Button { Text = "Add to Plan", Width = 110, Height = 30 };
            _btnAddToPlan.Click += OnExportClick;
            _btnNext = new Button { Text = "Next >", Width = 90, Height = 30 };
            _btnNext.Click += (s, e) => MoveWizardStep(1);
            _btnPrevious = new Button { Text = "< Previous", Width = 90, Height = 30 };
            _btnPrevious.Click += (s, e) => MoveWizardStep(-1);
            var btnLoad = new Button { Text = "Load from file...", Width = 130, Height = 30 };
            btnLoad.Click += OnLoadFromFileClick;
            btnRow.Controls.Add(btnCancel);
            btnRow.Controls.Add(_btnAddToPlan);
            btnRow.Controls.Add(_btnNext);
            btnRow.Controls.Add(_btnPrevious);
            btnRow.Controls.Add(btnLoad);

            outer.Controls.Add(_stepLabel, 0, 0);
            outer.Controls.Add(_tabs, 0, 1);
            outer.Controls.Add(btnRow, 0, 2);

            Controls.Add(outer);
            AcceptButton = _btnNext;
            CancelButton = btnCancel;
            UpdateWizardNavigation();
        }

        private Control BuildColumnsTab()
        {
            var outer = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                RowCount = 2,
                ColumnCount = 1,
                Padding = new Padding(8)
            };
            outer.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
            outer.RowStyles.Add(new RowStyle(SizeType.Absolute, 38F));

            var body = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 1, ColumnCount = 2 };
            body.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            body.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 42F));

            _columnsGrid = new DataGridView
            {
                Dock = DockStyle.Fill,
                AllowUserToAddRows = false,
                AllowUserToDeleteRows = false,
                AllowUserToResizeRows = false,
                MultiSelect = false,
                RowHeadersVisible = false,
                SelectionMode = DataGridViewSelectionMode.FullRowSelect,
                AutoGenerateColumns = false
            };
            _columnsGrid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Order", HeaderText = "#", Width = 42, ReadOnly = true });
            _columnsGrid.Columns.Add(new DataGridViewCheckBoxColumn { Name = "Include", HeaderText = "Include", Width = 65 });
            _columnsGrid.Columns.Add(new DataGridViewTextBoxColumn { Name = "LogicalName", HeaderText = "Logical name", Width = 260, ReadOnly = true });
            _columnsGrid.Columns.Add(new DataGridViewTextBoxColumn { Name = "DisplayName", HeaderText = "Display name", Width = 260, ReadOnly = true });
            _columnsGrid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Hint", HeaderText = "Hint", AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill });
            foreach (DataGridViewColumn column in _columnsGrid.Columns)
                column.SortMode = DataGridViewColumnSortMode.NotSortable;
            _columnsGrid.CellValueChanged += (s, e) => SaveColumnGridState();
            _columnsGrid.CurrentCellDirtyStateChanged += (s, e) =>
            {
                if (_columnsGrid.IsCurrentCellDirty) _columnsGrid.CommitEdit(DataGridViewDataErrorContexts.Commit);
            };

            var buttons = new FlowLayoutPanel
            {
                Dock = DockStyle.Fill,
                FlowDirection = FlowDirection.TopDown,
                Padding = new Padding(4, 0, 0, 0)
            };
            var btnUp = new Button { Text = "↑", Width = 32, Height = 32 };
            var btnDown = new Button { Text = "↓", Width = 32, Height = 32 };
            btnUp.Click += (s, e) => MoveSelectedColumnGroup(-1);
            btnDown.Click += (s, e) => MoveSelectedColumnGroup(1);
            buttons.Controls.Add(btnUp);
            buttons.Controls.Add(btnDown);

            body.Controls.Add(_columnsGrid, 0, 0);
            body.Controls.Add(buttons, 1, 0);

            var matchRow = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 6,
                RowCount = 1,
                Padding = new Padding(0, 7, 0, 0)
            };
            matchRow.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 128F));
            matchRow.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 125F));
            matchRow.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 130F));
            matchRow.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 210F));
            matchRow.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 92F));
            matchRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            matchRow.Controls.Add(new Label { Text = "Match key for import:", Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft }, 0, 0);
            _matchKeyModeCombo = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Dock = DockStyle.Fill };
            _matchKeyModeCombo.Items.Add(new MatchKeyModeItem("Guid", "Record GUID"));
            _matchKeyModeCombo.Items.Add(new MatchKeyModeItem("AlternateKey", "Alternate key"));
            _matchKeyModeCombo.Items.Add(new MatchKeyModeItem("Custom", "Custom"));
            _matchKeyModeCombo.SelectedIndexChanged += (s, e) => UpdateMatchKeyControls();
            matchRow.Controls.Add(_matchKeyModeCombo, 1, 0);
            matchRow.Controls.Add(new Label { Text = "Alternate key:", Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleRight, Padding = new Padding(0, 0, 6, 0) }, 2, 0);
            _matchKeyAltCombo = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Dock = DockStyle.Fill };
            matchRow.Controls.Add(_matchKeyAltCombo, 3, 0);
            _matchKeyCustomButton = new Button { Text = "Configure", Dock = DockStyle.Fill };
            _matchKeyCustomButton.Click += (s, e) => ConfigureExportCustomMatchKey();
            matchRow.Controls.Add(_matchKeyCustomButton, 4, 0);
            _matchKeyCustomLabel = new Label { Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft, AutoEllipsis = true, Padding = new Padding(6, 0, 0, 0) };
            matchRow.Controls.Add(_matchKeyCustomLabel, 5, 0);

            outer.Controls.Add(body, 0, 0);
            outer.Controls.Add(matchRow, 0, 1);
            return outer;
        }

        private Control BuildReviewTab()
        {
            var outer = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                RowCount = 2,
                ColumnCount = 1,
                Padding = new Padding(8)
            };
            outer.RowStyles.Add(new RowStyle(SizeType.Absolute, 28F));
            outer.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));

            outer.Controls.Add(new Label
            {
                Text = "Review the Excel export setup before adding this operation to the execution plan.",
                Dock = DockStyle.Fill,
                TextAlign = ContentAlignment.MiddleLeft
            }, 0, 0);

            _reviewText = new TextBox
            {
                Dock = DockStyle.Fill,
                Multiline = true,
                ReadOnly = true,
                ScrollBars = ScrollBars.Vertical
            };
            outer.Controls.Add(_reviewText, 0, 1);
            return outer;
        }

        private void MoveWizardStep(int delta)
        {
            var next = Math.Max(0, Math.Min(_tabs.TabPages.Count - 1, _tabs.SelectedIndex + delta));
            if (next == _tabs.SelectedIndex) return;
            _tabs.SelectedIndex = next;
        }

        private void UpdateWizardNavigation()
        {
            if (_tabs == null || _btnPrevious == null || _btnNext == null || _btnAddToPlan == null || _stepLabel == null) return;

            var step = _tabs.SelectedIndex;
            _stepLabel.Text = GetWizardStepTitle(step);
            _btnPrevious.Enabled = step > 0;
            _btnNext.Visible = step < _tabs.TabPages.Count - 1;
            _btnAddToPlan.Visible = step == _tabs.TabPages.Count - 1;
            AcceptButton = _btnNext.Visible ? _btnNext : _btnAddToPlan;
        }

        private string GetWizardStepTitle(int step)
        {
            switch (step)
            {
                case 0: return "Step 1 of 4 - Configure lookup columns";
                case 1: return "Step 2 of 4 - Configure option set columns";
                case 2: return "Step 3 of 4 - Configure columns and import match key";
                case 3: return "Step 4 of 4 - Review";
                default: return "Excel export configuration";
            }
        }

        #endregion

        #region Lookup section

        private const int Col1X = 0;    // Attribute
        private const int Col2X = 195;  // Related table
        private const int Col3X = 355;  // Resolution
        private const int Col4X = 530;  // Detail (alt key / custom)
        private const int RowW = 900;

        private void PopulateLookupSection()
        {
            var lookups = _selectedAttributes
                .Where(a => a.AttributeType == AttributeTypeCode.Lookup
                         || a.AttributeType == AttributeTypeCode.Customer
                         || a.AttributeType == AttributeTypeCode.Owner)
                .ToList();

            if (!lookups.Any())
            {
                _lookupPanel.Controls.Add(EmptyLabel("No lookup attributes in selection."));
                return;
            }

            var header = MakeLookupHeaderRow();
            _lookupPanel.Controls.Add(header);
            var y = header.Height + 2;

            foreach (var attr in lookups)
            {
                var row = BuildLookupRow(attr, y);
                _lookupPanel.Controls.Add(row);
                y += row.Height + 6;
            }
        }

        private Panel MakeLookupHeaderRow()
        {
            var p = new Panel { Location = new Point(0, 0), Width = RowW, Height = 22 };
            void H(string t, int x, int w) => p.Controls.Add(new Label
            {
                Text = t, Location = new Point(x, 3), Width = w, AutoSize = false,
                Font = new Font(Font.FontFamily, Font.Size, FontStyle.Bold)
            });
            H("Attribute", Col1X, 190);
            H("Related table", Col2X, 155);
            H("Resolution", Col3X, 170);
            H("Detail", Col4X, 360);
            return p;
        }

        private Panel BuildLookupRow(AttributeMetadata attr, int y)
        {
            var displayName = attr.DisplayName.UserLocalizedLabel?.Label ?? attr.LogicalName;
            var targets = (attr as LookupAttributeMetadata)?.Targets ?? new string[0];
            var isPolymorphic = targets.Length > 1;
            var rowHeight = 56;

            var row = new Panel { Location = new Point(0, y), Width = RowW, Height = rowHeight };

            // Col 1 — Attribute
            row.Controls.Add(new Label
            {
                Text = $"{displayName}\n({attr.LogicalName})",
                Location = new Point(Col1X, 3),
                Width = 190,
                Height = 38,
                AutoSize = false
            });

            // Col 2 — Related table(s)
            if (isPolymorphic)
            {
                var cbo = new ComboBox
                {
                    Location = new Point(Col2X, 4),
                    Width = 152,
                    DropDownStyle = ComboBoxStyle.DropDownList
                };
                foreach (var t in targets) cbo.Items.Add(t);
                cbo.SelectedIndex = 0;
                cbo.SelectedIndexChanged += (s, e) =>
                {
                    RefreshAltKeys(attr.LogicalName, (string)cbo.SelectedItem, row);
                    RefreshCustomLabel(attr.LogicalName);
                };
                row.Controls.Add(cbo);
                _targetTableCombos[attr.LogicalName] = cbo;
            }
            else
            {
                row.Controls.Add(new Label
                {
                    Text = targets.FirstOrDefault() ?? "(unknown)",
                    Location = new Point(Col2X, 6),
                    Width = 152,
                    AutoSize = false,
                    ForeColor = Color.DimGray
                });
            }

            // Col 3 — Resolution radio buttons (vertical stack)
            var rbGuid = new RadioButton { Text = "GUID", Location = new Point(Col3X, 2), AutoSize = true, Checked = true };
            var rbAlt = new RadioButton { Text = "Alt key", Location = new Point(Col3X, 20), AutoSize = true };
            var rbCustom = new RadioButton { Text = "Custom", Location = new Point(Col3X, 38), AutoSize = true };
            row.Controls.Add(rbGuid);
            row.Controls.Add(rbAlt);
            row.Controls.Add(rbCustom);
            _altKeyRadios[attr.LogicalName] = rbAlt;
            _customRadios[attr.LogicalName] = rbCustom;

            // Col 4 — Alt key combo + fields info label
            var altCombo = new ComboBox
            {
                Location = new Point(Col4X, 4),
                Width = 355,
                DropDownStyle = ComboBoxStyle.DropDownList,
                Enabled = false
            };
            row.Controls.Add(altCombo);
            _altKeyCombos[attr.LogicalName] = altCombo;

            var altInfoLabel = new Label
            {
                Location = new Point(Col4X, 28),
                Width = 230,
                Height = 18,
                AutoSize = false,
                ForeColor = Color.DimGray,
                Font = new Font(Font.FontFamily, Font.Size - 0.5f),
                Visible = false
            };
            row.Controls.Add(altInfoLabel);

            var btnNested = new Button
            {
                Text = "Nested lookups...",
                Location = new Point(Col4X + 235, 24),
                Width = 120,
                Height = 24,
                Visible = false
            };
            row.Controls.Add(btnNested);

            // Col 4 — Custom: warning + select button + selection label
            var warningLabel = new Label
            {
                Text = "⚠ Uniqueness not guaranteed",
                Location = new Point(Col4X, 4),
                Width = 220,
                AutoSize = false,
                ForeColor = Color.DarkOrange,
                Visible = false
            };
            var btnSelect = new Button
            {
                Text = "Select attributes...",
                Location = new Point(Col4X, 22),
                Width = 130,
                Height = 24,
                Visible = false
            };
            var selLabel = new Label
            {
                Text = "(none selected)",
                Location = new Point(Col4X + 135, 26),
                Width = 220,
                AutoSize = false,
                ForeColor = Color.DimGray,
                Visible = false
            };
            row.Controls.Add(warningLabel);
            row.Controls.Add(btnSelect);
            row.Controls.Add(selLabel);
            _customSelectionLabels[attr.LogicalName] = selLabel;
            _customSelections[attr.LogicalName] = new List<string>();

            // Load initial alt keys
            var initialTable = targets.FirstOrDefault() ?? string.Empty;
            LoadAltKeys(attr.LogicalName, initialTable, altCombo, rbAlt);

            // Wire radio button events
            void UpdateAltInfo()
            {
                if (rbAlt.Checked && altCombo.SelectedItem is AltKeyItem item)
                {
                    altInfoLabel.Text = $"Fields: {string.Join(" + ", item.Key.KeyAttributes)}";
                    altInfoLabel.Visible = true;
                    btnNested.Visible = HasNestedLookupFields(attr.LogicalName, item.Key.KeyAttributes.ToList());
                }
                else
                {
                    altInfoLabel.Visible = false;
                    btnNested.Visible = false;
                }
            }

            rbAlt.CheckedChanged += (s, e) =>
            {
                altCombo.Visible = rbAlt.Checked;
                altCombo.Enabled = rbAlt.Checked;
                warningLabel.Visible = false;
                btnSelect.Visible = false;
                selLabel.Visible = false;
                UpdateAltInfo();
            };

            altCombo.SelectedIndexChanged += (s, e) => UpdateAltInfo();

            rbCustom.CheckedChanged += (s, e) =>
            {
                altCombo.Visible = false;
                altInfoLabel.Visible = false;
                warningLabel.Visible = rbCustom.Checked;
                btnSelect.Visible = rbCustom.Checked;
                selLabel.Visible = rbCustom.Checked;
                btnNested.Visible = rbCustom.Checked && HasNestedLookupFields(attr.LogicalName, _customSelections[attr.LogicalName]);
            };

            rbGuid.CheckedChanged += (s, e) =>
            {
                if (rbGuid.Checked)
                {
                    altCombo.Visible = false;
                    altInfoLabel.Visible = false;
                    warningLabel.Visible = false;
                    btnSelect.Visible = false;
                    selLabel.Visible = false;
                    btnNested.Visible = false;
                }
            };

            // Wire select button
            btnSelect.Click += (s, e) =>
            {
                if (OpenPropertySelector(attr.LogicalName, targets, selLabel))
                    btnNested.Visible = rbCustom.Checked && HasNestedLookupFields(attr.LogicalName, _customSelections[attr.LogicalName]);
            };
            btnNested.Click += (s, e) =>
            {
                var tableName = GetSelectedRelatedTable(attr.LogicalName, targets);
                var fields = rbAlt.Checked && altCombo.SelectedItem is AltKeyItem item
                    ? item.Key.KeyAttributes.ToList()
                    : _customSelections[attr.LogicalName];
                OpenNestedLookupConfig(attr.LogicalName, tableName, fields);
            };

            // Make row taller to fit 3 radio buttons
            row.Height = 60;
            return row;
        }

        private void LoadAltKeys(string attrLogicalName, string tableName, ComboBox combo, RadioButton rbAlt)
        {
            combo.Items.Clear();
            if (!string.IsNullOrEmpty(tableName))
            {
                try
                {
                    Cursor = Cursors.WaitCursor;
                    foreach (var key in _repo.GetAlternateKeys(tableName))
                    {
                        var keyName = key.DisplayName?.UserLocalizedLabel?.Label ?? key.LogicalName;
                        combo.Items.Add(new AltKeyItem(key, keyName));
                    }
                }
                catch (Exception ex)
                {
                    combo.Items.Add($"(could not load alternate keys: {ex.Message})");
                }
                finally
                {
                    Cursor = Cursors.Default;
                }
            }

            if (combo.Items.Count > 0)
                combo.SelectedIndex = 0;
            else
                combo.Items.Add("(no alternate keys defined)");

            rbAlt.Enabled = combo.Items.OfType<AltKeyItem>().Any();
            if (!rbAlt.Enabled && rbAlt.Checked)
            {
                // Switch back to GUID if alt keys disappeared
                var rbGuid = rbAlt.Parent?.Controls.OfType<RadioButton>()
                    .FirstOrDefault(r => r.Text == "GUID");
                if (rbGuid != null) rbGuid.Checked = true;
            }
        }

        private void RefreshAltKeys(string attrLogicalName, string newTable, Panel row)
        {
            if (!_altKeyCombos.TryGetValue(attrLogicalName, out var combo)) return;
            if (!_altKeyRadios.TryGetValue(attrLogicalName, out var rbAlt)) return;
            LoadAltKeys(attrLogicalName, newTable, combo, rbAlt);
        }

        private void RefreshCustomLabel(string attrLogicalName)
        {
            if (_customSelections.TryGetValue(attrLogicalName, out var sel))
                sel.Clear();
            if (_customSelectionLabels.TryGetValue(attrLogicalName, out var lbl))
                lbl.Text = "(none selected)";
        }

        private bool OpenPropertySelector(string attrLogicalName, string[] targets, Label selLabel)
        {
            var tableName = GetSelectedRelatedTable(attrLogicalName, targets);

            if (string.IsNullOrEmpty(tableName)) return false;

            List<AttributeMetadata> attrs;
            try
            {
                Cursor = Cursors.WaitCursor;
                var meta = _repo.GetTableMetadata(tableName);
                attrs = meta.Attributes
                    .Where(a => a.IsValidForRead == true && a.AttributeType != AttributeTypeCode.Virtual
                             && a.AttributeType != AttributeTypeCode.ManagedProperty)
                    .OrderBy(a => a.LogicalName)
                    .ToList();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Could not load attributes for '{tableName}': {ex.Message}", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return false;
            }
            finally
            {
                Cursor = Cursors.Default;
            }

            var existing = _customSelections.TryGetValue(attrLogicalName, out var prev) ? prev : new List<string>();

            using (var dlg = new PropertySelectorDialog(tableName, attrs, existing))
            {
                if (dlg.ShowDialog(this) != DialogResult.OK) return false;
                _customSelections[attrLogicalName] = dlg.SelectedProperties;
                selLabel.Text = dlg.SelectedProperties.Any()
                    ? string.Join(", ", dlg.SelectedProperties)
                    : "(none selected)";
                return true;
            }
        }

        private string GetSelectedRelatedTable(string attrLogicalName, string[] targets)
        {
            return _targetTableCombos.TryGetValue(attrLogicalName, out var tblCbo)
                ? (string)tblCbo.SelectedItem
                : targets.FirstOrDefault() ?? string.Empty;
        }

        private bool HasNestedLookupFields(string ownerLogicalName, List<string> fields)
        {
            var targets = _selectedAttributes
                .OfType<LookupAttributeMetadata>()
                .FirstOrDefault(a => a.LogicalName == ownerLogicalName)
                ?.Targets ?? new string[0];
            var tableName = GetSelectedRelatedTable(ownerLogicalName, targets);

            return fields.Any(field => GetAttributeMetadata(tableName, field) is LookupAttributeMetadata);
        }

        private void OpenNestedLookupConfig(string ownerLogicalName, string ownerTable, List<string> fields)
        {
            var lookupFields = fields
                .Select(field => GetAttributeMetadata(ownerTable, field))
                .OfType<LookupAttributeMetadata>()
                .ToList();

            if (!lookupFields.Any()) return;

            using (var dlg = new NestedLookupConfigDialog(ownerLogicalName, lookupFields, _repo, _nestedLookupConfigs))
            {
                if (dlg.ShowDialog(this) != DialogResult.OK) return;
                foreach (var config in dlg.Configs)
                    _nestedLookupConfigs[config.Key] = config.Value;
            }
        }

        #endregion

        #region OptionSet section

        private void PopulateOptionSetSection()
        {
            var optionSets = _selectedAttributes
                .Where(a => a.AttributeType == AttributeTypeCode.Picklist
                         || a.AttributeType == AttributeTypeCode.State
                         || a.AttributeType == AttributeTypeCode.Status
                         || (a.AttributeType == AttributeTypeCode.Virtual
                             && a.AttributeTypeName?.Value == "MultiSelectPicklistType"))
                .ToList();

            if (!optionSets.Any())
            {
                _optionSetPanel.Controls.Add(EmptyLabel("No option set attributes in selection."));
                return;
            }

            var y = 5;
            foreach (var attr in optionSets)
            {
                var row = BuildOptionSetRow(attr, y);
                _optionSetPanel.Controls.Add(row);
                y += row.Height + 4;
            }
        }

        private Panel BuildOptionSetRow(AttributeMetadata attr, int y)
        {
            var displayName = attr.DisplayName.UserLocalizedLabel?.Label ?? attr.LogicalName;
            var options = GetOptions(attr);

            var row = new Panel { Location = new Point(0, y), Width = RowW, Height = 30 };

            row.Controls.Add(new Label
            {
                Text = $"{displayName} ({attr.LogicalName})",
                Location = new Point(0, 6),
                Width = 220,
                AutoSize = false
            });

            // Default: Label
            var rbValue = new RadioButton { Text = "Value", Location = new Point(224, 5), AutoSize = true };
            var rbLabel = new RadioButton { Text = "Label", Location = new Point(284, 5), AutoSize = true, Checked = true };
            rbLabel.Tag = options;
            row.Controls.Add(rbValue);
            row.Controls.Add(rbLabel);
            _optionLabelRadios[attr.LogicalName] = rbLabel;

            var preview = options.Count > 0
                ? string.Join("  |  ", options.Take(5).Select(o => $"{o.Value}={o.Label}")) + (options.Count > 5 ? " …" : "")
                : "(no options)";
            row.Controls.Add(new Label
            {
                Text = preview,
                Location = new Point(344, 6),
                Width = 550,
                AutoSize = false,
                ForeColor = Color.DimGray,
                Font = new Font(Font.FontFamily, Font.Size - 0.5f)
            });

            return row;
        }

        private List<OptionConfig> GetOptions(AttributeMetadata attr)
        {
            OptionMetadataCollection col = null;
            if (attr is PicklistAttributeMetadata pl) col = pl.OptionSet?.Options;
            else if (attr is StateAttributeMetadata st) col = st.OptionSet?.Options;
            else if (attr is StatusAttributeMetadata ss) col = ss.OptionSet?.Options;
            else if (attr is MultiSelectPicklistAttributeMetadata ms) col = ms.OptionSet?.Options;
            return col?.Select(o => new OptionConfig { Value = o.Value ?? 0, Label = o.Label?.UserLocalizedLabel?.Label ?? o.Value.ToString() }).ToList()
                   ?? new List<OptionConfig>();
        }

        #endregion

        #region Build config

        private void OnLoadFromFileClick(object sender, EventArgs e)
        {
            var path = string.Empty;
            using (var dlg = new OpenFileDialog { Title = "Load Excel config from settings file", Filter = "Settings files (*.settings.json)|*.settings.json" })
            {
                if (dlg.ShowDialog(this) != DialogResult.OK) return;
                path = dlg.FileName;
            }

            try
            {
                var json = File.ReadAllText(path);
                var tableSettings = json.DeserializeObject<TableSettings>();

                if (tableSettings?.ExcelConfig == null)
                {
                    MessageBox.Show("This settings file does not contain a saved Excel configuration.", "Info", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    return;
                }

                if (!string.IsNullOrEmpty(tableSettings.LogicalName)
                    && !tableSettings.LogicalName.Equals(_metadata.LogicalName, StringComparison.OrdinalIgnoreCase))
                {
                    var result = MessageBox.Show(
                        $"This config was saved for table '{tableSettings.LogicalName}' but the current table is '{_metadata.LogicalName}'.\nApply anyway?",
                        "Table mismatch", MessageBoxButtons.YesNo, MessageBoxIcon.Warning);
                    if (result != DialogResult.Yes) return;
                }

                ApplySavedConfig(tableSettings.ExcelConfig);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Could not load settings file: {ex.Message}", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void ApplySavedConfig(ExcelExportConfig config)
        {
            if (config?.Columns == null) return;
            _savedConfig = config;

            for (var i = 0; i < config.Columns.Count; i++)
            {
                var col = config.Columns[i];
                _columnStates[col.LogicalName] = new ColumnManagerState
                {
                    Include = !col.Hidden,
                    HintOverride = col.HintOverride,
                    Order = i
                };

                if (col.Type == "LookupKeyField" && col.KeyFieldType == "Lookup")
                {
                    _nestedLookupConfigs[col.LogicalName] = new NestedLookupConfig
                    {
                        Resolution = col.Resolution ?? "Guid",
                        Fields = col.AlternateKeyFields != null ? new List<string>(col.AlternateKeyFields) : new List<string>()
                    };
                }
            }

            foreach (var col in config.Columns)
            {
                // Restore lookup resolution
                if (col.Type == "Lookup")
                {
                    // Related table (polymorphic)
                    if (_targetTableCombos.TryGetValue(col.LogicalName, out var tblCbo)
                        && !string.IsNullOrEmpty(col.RelatedTable))
                    {
                        for (var i = 0; i < tblCbo.Items.Count; i++)
                            if (tblCbo.Items[i]?.ToString() == col.RelatedTable) { tblCbo.SelectedIndex = i; break; }
                    }

                    if (col.Resolution == "AlternateKey"
                        && _altKeyRadios.TryGetValue(col.LogicalName, out var rbAlt) && rbAlt.Enabled)
                    {
                        rbAlt.Checked = true;

                        // Select the matching key in the combo
                        if (_altKeyCombos.TryGetValue(col.LogicalName, out var altCbo) && col.AlternateKeyFields != null)
                        {
                            for (var i = 0; i < altCbo.Items.Count; i++)
                            {
                                if (altCbo.Items[i] is AltKeyItem item
                                    && item.Key.KeyAttributes.SequenceEqual(col.AlternateKeyFields))
                                {
                                    altCbo.SelectedIndex = i;
                                    break;
                                }
                            }
                        }
                    }
                    else if (col.Resolution == "Custom"
                        && _customRadios.TryGetValue(col.LogicalName, out var rbCustom))
                    {
                        rbCustom.Checked = true;
                        if (col.AlternateKeyFields != null)
                        {
                            _customSelections[col.LogicalName] = new List<string>(col.AlternateKeyFields);
                            if (_customSelectionLabels.TryGetValue(col.LogicalName, out var lbl))
                                lbl.Text = string.Join(", ", col.AlternateKeyFields);
                        }
                    }
                }

                // Restore option set export mode
                if ((col.Type == "OptionSet" || col.Type == "MultiOptionSet")
                    && _optionLabelRadios.TryGetValue(col.LogicalName, out var rbLbl))
                {
                    var useLabel = col.ExportMode == "Label";
                    rbLbl.Checked = useLabel;

                    // Find and check the Value radio (sibling control)
                    var valueRb = rbLbl.Parent?.Controls.OfType<RadioButton>()
                        .FirstOrDefault(r => r.Text == "Value");
                    if (valueRb != null) valueRb.Checked = !useLabel;
                }
            }

            if (_columnsTabInitialized) RefreshColumnsGrid();
        }

        private void OnExportClick(object sender, EventArgs e)
        {
            if (_tabs.SelectedIndex != _tabs.TabPages.Count - 1)
            {
                _tabs.SelectedIndex = _tabs.TabPages.Count - 1;
                return;
            }

            SaveColumnGridState();
            Config = BuildConfig();
            AddToPlanRequested = true;
            DialogResult = DialogResult.Yes;
            Close();
        }

        private void RefreshReview()
        {
            if (_reviewText == null) return;

            SaveColumnGridState();
            var config = BuildConfig();
            var lookups = config.Columns.Count(c => c.Type == "Lookup");
            var lookupKeyFields = config.Columns.Count(c => c.Type == "LookupKeyField");
            var optionSets = config.Columns.Count(c => c.Type == "OptionSet" || c.Type == "MultiOptionSet");
            var included = config.Columns.Count(c => !c.Hidden);
            var hidden = config.Columns.Count(c => c.Hidden);
            var matchKey = config.MatchKeys?.Any() == true
                ? $"{config.MatchKeyMode}: {string.Join(", ", config.MatchKeys)}"
                : "Record GUID";

            _reviewText.Text =
                $"Table: {_metadata.LogicalName}{Environment.NewLine}" +
                $"Included columns: {included}{Environment.NewLine}" +
                $"Hidden/helper columns: {hidden}{Environment.NewLine}" +
                $"Lookup columns: {lookups}{Environment.NewLine}" +
                $"Lookup key helper columns: {lookupKeyFields}{Environment.NewLine}" +
                $"Option set columns: {optionSets}{Environment.NewLine}" +
                $"Default import match key: {matchKey}{Environment.NewLine}{Environment.NewLine}" +
                "Columns:" + Environment.NewLine +
                string.Join(Environment.NewLine, config.Columns.Select((c, i) =>
                    $"{i + 1:00}. {(c.Hidden ? "[hidden] " : string.Empty)}{c.LogicalName} ({c.Type})"));
        }

        private ExcelExportConfig BuildConfig()
        {
            var config = BuildBaseConfig();
            ApplyColumnManager(config);
            return config;
        }

        private ExcelExportConfig BuildBaseConfig()
        {
            var config = new ExcelExportConfig
            {
                Table = new ExcelTableConfig
                {
                    LogicalName = _metadata.LogicalName,
                    PrimaryIdAttribute = _metadata.PrimaryIdAttribute,
                    PrimaryNameAttribute = _metadata.PrimaryNameAttribute
                }
            };

            foreach (var attr in _selectedAttributes)
            {
                var type = GetExcelType(attr);
                if (type == null) continue;

                var displayName = attr.DisplayName.UserLocalizedLabel?.Label ?? attr.LogicalName;

                if (attr.AttributeType == AttributeTypeCode.Lookup
                 || attr.AttributeType == AttributeTypeCode.Customer
                 || attr.AttributeType == AttributeTypeCode.Owner)
                {
                    var targets = (attr as LookupAttributeMetadata)?.Targets ?? new string[0];
                    var relatedTable = _targetTableCombos.TryGetValue(attr.LogicalName, out var tblCbo)
                        ? (string)tblCbo.SelectedItem
                        : targets.FirstOrDefault() ?? string.Empty;

                    var rbAlt = _altKeyRadios.TryGetValue(attr.LogicalName, out var ra) ? ra : null;
                    var rbCustom = _customRadios.TryGetValue(attr.LogicalName, out var rc) ? rc : null;
                    var altCombo = _altKeyCombos.TryGetValue(attr.LogicalName, out var ac) ? ac : null;

                    if (rbAlt?.Checked == true && altCombo?.SelectedItem is AltKeyItem keyItem)
                    {
                        var keyFields = keyItem.Key.KeyAttributes.ToList();
                        AddLookupColumns(config, attr.LogicalName, displayName, relatedTable, "AlternateKey", keyFields);
                    }
                    else if (rbCustom?.Checked == true)
                    {
                        var fields = _customSelections.TryGetValue(attr.LogicalName, out var sel) ? sel : new List<string>();
                        AddLookupColumns(config, attr.LogicalName, displayName, relatedTable, "Custom", fields);
                    }
                    else
                    {
                        config.Columns.Add(new ExcelColumnConfig
                        {
                            LogicalName = attr.LogicalName, DisplayName = displayName,
                            Type = "Lookup", RelatedTable = relatedTable, Resolution = "Guid"
                        });
                    }
                }
                else if (attr.AttributeType == AttributeTypeCode.Picklist
                      || attr.AttributeType == AttributeTypeCode.State
                      || attr.AttributeType == AttributeTypeCode.Status
                      || (attr.AttributeType == AttributeTypeCode.Virtual && attr.AttributeTypeName?.Value == "MultiSelectPicklistType"))
                {
                    var rbLbl = _optionLabelRadios.TryGetValue(attr.LogicalName, out var r) ? r : null;
                    var useLabel = rbLbl?.Checked != false;
                    var options = rbLbl?.Tag as List<OptionConfig> ?? new List<OptionConfig>();
                    config.Columns.Add(new ExcelColumnConfig
                    {
                        LogicalName = attr.LogicalName, DisplayName = displayName,
                        Type = type, ExportMode = useLabel ? "Label" : "Value",
                        Options = useLabel ? options : null
                    });
                }
                else
                {
                    config.Columns.Add(new ExcelColumnConfig
                    {
                        LogicalName = attr.LogicalName, DisplayName = displayName, Type = type
                    });
                }
            }

            return config;
        }

        private void RefreshColumnsGrid()
        {
            if (_columnsGrid == null) return;

            SaveColumnGridState();
            var config = BuildBaseConfig();
            var columns = ApplyStoredColumnOrder(config.Columns).ToList();

            _columnsGrid.Rows.Clear();
            foreach (var column in columns)
                AddColumnGridRow(column);

            PopulateMatchKeyCombo(columns);
            _columnsTabInitialized = true;
        }

        private int AddColumnGridRow(ExcelColumnConfig column)
        {
            var state = GetColumnState(column);
            var displayName = column.Type == "LookupKeyField" ? "  " + column.DisplayName : column.DisplayName;
            var rowIndex = _columnsGrid.Rows.Add(
                _columnsGrid.Rows.Count + 1,
                state.Include,
                column.LogicalName,
                displayName,
                state.HintOverride ?? GetDefaultHintText(column));
            var row = _columnsGrid.Rows[rowIndex];
            row.Tag = column;
            if (column.Type == "LookupKeyField") row.DefaultCellStyle.ForeColor = Color.DimGray;
            return rowIndex;
        }

        private IEnumerable<ExcelColumnConfig> ApplyStoredColumnOrder(IEnumerable<ExcelColumnConfig> columns)
        {
            return columns
                .Select((column, index) => new { column, index, state = GetColumnState(column) })
                .OrderBy(x => x.state.Order ?? int.MaxValue)
                .ThenBy(x => x.index)
                .Select(x => x.column);
        }

        private ColumnManagerState GetColumnState(ExcelColumnConfig column)
        {
            if (!_columnStates.TryGetValue(column.LogicalName, out var state))
            {
                state = new ColumnManagerState { Include = !column.Hidden, HintOverride = column.HintOverride };
                _columnStates[column.LogicalName] = state;
            }
            return state;
        }

        private void SaveColumnGridState()
        {
            if (_columnsGrid == null || _columnsGrid.Rows.Count == 0) return;

            for (var i = 0; i < _columnsGrid.Rows.Count; i++)
            {
                var row = _columnsGrid.Rows[i];
                if (!(row.Tag is ExcelColumnConfig column)) continue;

                var state = GetColumnState(column);
                state.Order = i;
                state.Include = Convert.ToBoolean(row.Cells["Include"].Value);
                var hint = row.Cells["Hint"].Value?.ToString();
                state.HintOverride = string.Equals(hint, GetDefaultHintText(column), StringComparison.Ordinal)
                    ? null
                    : hint;
            }
        }

        private void PopulateMatchKeyCombo(List<ExcelColumnConfig> columns)
        {
            var previous = GetCurrentMatchKeySelection();
            var target = _columnsTabInitialized ? previous : GetSavedMatchKeySelection();
            var availableFields = columns.Where(IsMatchKeyCandidate).Select(c => c.LogicalName).ToList();

            _matchKeyAltCombo.Items.Clear();
            foreach (var key in GetImportAlternateKeys(availableFields))
            {
                _matchKeyAltCombo.Items.Add(new MatchKeyItem
                {
                    Mode = "AlternateKey",
                    AlternateKeyName = key.LogicalName,
                    Fields = key.KeyAttributes.ToList(),
                    Display = $"{GetAlternateKeyDisplayName(key)} ({string.Join(", ", key.KeyAttributes)})"
                });
            }

            _matchKeyCustomFields = target.Fields
                .Where(availableFields.Contains)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            SelectMatchKeyMode(target.Mode);
            if (string.Equals(target.Mode, "AlternateKey", StringComparison.OrdinalIgnoreCase))
                SelectAlternateKey(target);
            if (_matchKeyAltCombo.SelectedIndex < 0 && _matchKeyAltCombo.Items.Count > 0)
                _matchKeyAltCombo.SelectedIndex = 0;

            UpdateMatchKeyControls();
        }

        private bool IsMatchKeyCandidate(ExcelColumnConfig column)
        {
            if (column.Type == "Lookup" || column.Type == "LookupKeyField") return false;
            if (string.Equals(column.LogicalName, _metadata.PrimaryIdAttribute, StringComparison.OrdinalIgnoreCase)) return false;
            return GetColumnState(column).Include;
        }

        private void ApplyColumnManager(ExcelExportConfig config)
        {
            if (_columnsTabInitialized)
                SaveColumnGridState();
            else if (_savedConfig?.Columns != null)
                foreach (var saved in _savedConfig.Columns)
                    GetColumnState(saved);

            config.Columns = ApplyStoredColumnOrder(config.Columns)
                .Where(column => GetColumnState(column).Include)
                .Select(column =>
                {
                    var state = GetColumnState(column);
                    column.Hidden = false;
                    column.HintOverride = state.HintOverride;
                    return column;
                })
                .ToList();

            var selection = GetCurrentMatchKeySelection();
            var included = new HashSet<string>(config.Columns.Select(c => c.LogicalName), StringComparer.OrdinalIgnoreCase);
            var fields = selection.Fields.Where(included.Contains).ToList();
            config.MatchKeyMode = fields.Any() ? selection.Mode : "Guid";
            config.MatchKeys = fields;
            config.MatchKey = fields.Count == 1 ? fields[0] : null;
            config.MatchAlternateKeyName = string.Equals(config.MatchKeyMode, "AlternateKey", StringComparison.OrdinalIgnoreCase)
                ? selection.AlternateKeyName
                : null;
        }

        private MatchKeyItem GetCurrentMatchKeySelection()
        {
            var mode = (_matchKeyModeCombo?.SelectedItem as MatchKeyModeItem)?.Mode ?? "Guid";
            if (mode == "AlternateKey" && _matchKeyAltCombo?.SelectedItem is MatchKeyItem alt)
                return alt;
            if (mode == "Custom")
                return new MatchKeyItem { Mode = "Custom", Fields = new List<string>(_matchKeyCustomFields), Display = "Custom" };
            return new MatchKeyItem { Mode = "Guid", Fields = new List<string>(), Display = "Record GUID" };
        }

        private MatchKeyItem GetSavedMatchKeySelection()
        {
            if (_savedConfig?.MatchKeys?.Any() == true)
            {
                return new MatchKeyItem
                {
                    Mode = string.IsNullOrWhiteSpace(_savedConfig.MatchKeyMode) ? "Custom" : _savedConfig.MatchKeyMode,
                    Fields = new List<string>(_savedConfig.MatchKeys),
                    AlternateKeyName = _savedConfig.MatchAlternateKeyName
                };
            }

            if (!string.IsNullOrWhiteSpace(_savedConfig?.MatchKey))
                return new MatchKeyItem { Mode = "Custom", Fields = new List<string> { _savedConfig.MatchKey } };

            return new MatchKeyItem { Mode = "Guid", Fields = new List<string>() };
        }

        private void SelectMatchKeyMode(string mode)
        {
            mode = string.IsNullOrWhiteSpace(mode) ? "Guid" : mode;
            for (var i = 0; i < _matchKeyModeCombo.Items.Count; i++)
            {
                if ((_matchKeyModeCombo.Items[i] as MatchKeyModeItem)?.Mode == mode)
                {
                    _matchKeyModeCombo.SelectedIndex = i;
                    return;
                }
            }
            _matchKeyModeCombo.SelectedIndex = 0;
        }

        private void SelectAlternateKey(MatchKeyItem target)
        {
            for (var i = 0; i < _matchKeyAltCombo.Items.Count; i++)
            {
                if (_matchKeyAltCombo.Items[i] is MatchKeyItem item && item.Matches(target))
                {
                    _matchKeyAltCombo.SelectedIndex = i;
                    return;
                }
            }
        }

        private void UpdateMatchKeyControls()
        {
            if (_matchKeyModeCombo == null) return;
            var mode = (_matchKeyModeCombo.SelectedItem as MatchKeyModeItem)?.Mode ?? "Guid";
            _matchKeyAltCombo.Enabled = mode == "AlternateKey" && _matchKeyAltCombo.Items.Count > 0;
            _matchKeyCustomButton.Enabled = mode == "Custom";
            _matchKeyCustomLabel.Text = mode == "Custom" && _matchKeyCustomFields.Any()
                ? string.Join(", ", _matchKeyCustomFields)
                : string.Empty;
        }

        private void ConfigureExportCustomMatchKey()
        {
            var available = GetCurrentMatchKeyCandidates();
            var attributes = _metadata.Attributes
                .Where(a => available.Contains(a.LogicalName))
                .OrderBy(a => a.DisplayName?.UserLocalizedLabel?.Label ?? a.LogicalName)
                .ToList();
            using (var dlg = new PropertySelectorDialog(_metadata.LogicalName, attributes, _matchKeyCustomFields))
            {
                if (dlg.ShowDialog(this) != DialogResult.OK) return;
                _matchKeyCustomFields = dlg.SelectedProperties;
                UpdateMatchKeyControls();
            }
        }

        private List<string> GetCurrentMatchKeyCandidates()
        {
            SaveColumnGridState();
            return _columnsGrid.Rows.Cast<DataGridViewRow>()
                .Where(row => row.Tag is ExcelColumnConfig column && IsMatchKeyCandidate(column))
                .Select(row => ((ExcelColumnConfig)row.Tag).LogicalName)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(name => name)
                .ToList();
        }

        private List<EntityKeyMetadata> GetImportAlternateKeys(List<string> availableFields)
        {
            var available = new HashSet<string>(availableFields, StringComparer.OrdinalIgnoreCase);
            return _metadata.Keys?
                .Where(k => k.KeyAttributes?.Any() == true && k.KeyAttributes.All(available.Contains))
                .OrderBy(k => GetAlternateKeyDisplayName(k))
                .ToList() ?? new List<EntityKeyMetadata>();
        }

        private string GetAlternateKeyDisplayName(EntityKeyMetadata key)
        {
            return key.DisplayName?.UserLocalizedLabel?.Label ?? key.LogicalName;
        }

        private void MoveSelectedColumnGroup(int direction)
        {
            if (_columnsGrid.SelectedRows.Count == 0) return;
            SaveColumnGridState();

            var rowIndex = _columnsGrid.SelectedRows[0].Index;
            var group = GetColumnGroup(rowIndex);
            if (group.Count == 0) return;

            var target = direction < 0
                ? GetPreviousGroupStart(group[0])
                : GetNextGroupStart(group[group.Count - 1]);
            if (target < 0) return;

            var rows = _columnsGrid.Rows.Cast<DataGridViewRow>().ToList();
            var moving = group.Select(i => rows[i]).ToList();
            foreach (var row in moving) rows.Remove(row);
            var insertAt = direction < 0 ? target : target - moving.Count + 1;
            rows.InsertRange(insertAt, moving);

            _columnsGrid.Rows.Clear();
            foreach (var row in rows)
            {
                var column = row.Tag as ExcelColumnConfig;
                if (column == null) continue;
                AddColumnGridRow(column);
            }
            _columnsGrid.Rows[Math.Max(0, Math.Min(insertAt, _columnsGrid.Rows.Count - 1))].Selected = true;
            SaveColumnGridState();
        }

        private List<int> GetColumnGroup(int rowIndex)
        {
            var column = _columnsGrid.Rows[rowIndex].Tag as ExcelColumnConfig;
            if (column == null) return new List<int>();

            var start = rowIndex;
            if (column.Type == "LookupKeyField")
            {
                for (var i = rowIndex - 1; i >= 0; i--)
                {
                    var candidate = _columnsGrid.Rows[i].Tag as ExcelColumnConfig;
                    if (candidate?.Type == "Lookup" && column.LogicalName.StartsWith(candidate.LogicalName + ".", StringComparison.Ordinal))
                    {
                        start = i;
                        break;
                    }
                }
            }

            var root = _columnsGrid.Rows[start].Tag as ExcelColumnConfig;
            var end = start;
            if (root?.Type == "Lookup")
            {
                for (var i = start + 1; i < _columnsGrid.Rows.Count; i++)
                {
                    var candidate = _columnsGrid.Rows[i].Tag as ExcelColumnConfig;
                    if (candidate?.Type == "LookupKeyField" && candidate.LogicalName.StartsWith(root.LogicalName + ".", StringComparison.Ordinal))
                        end = i;
                    else
                        break;
                }
            }
            return Enumerable.Range(start, end - start + 1).ToList();
        }

        private int GetPreviousGroupStart(int groupStart)
        {
            return groupStart <= 0 ? -1 : GetColumnGroup(groupStart - 1).First();
        }

        private int GetNextGroupStart(int groupEnd)
        {
            return groupEnd >= _columnsGrid.Rows.Count - 1 ? -1 : GetColumnGroup(groupEnd + 1).First();
        }

        private string GetDefaultHintText(ExcelColumnConfig column)
        {
            switch (column.Type)
            {
                case "Lookup":
                    return string.IsNullOrEmpty(column.RelatedTable) ? "Lookup (GUID)" : $"Lookup -> {column.RelatedTable} (GUID)";
                case "LookupKeyField":
                    if (column.KeyFieldType == "OptionSet") return column.ExportMode == "Label" ? "Key choice label" : "Key choice value";
                    if (column.KeyFieldType == "Lookup") return string.IsNullOrEmpty(column.RelatedTable) ? $"Lookup key for {column.OwnerAttribute}" : $"Lookup key -> {column.RelatedTable}";
                    return $"Key for {column.OwnerAttribute}";
                case "OptionSet":
                    return column.ExportMode == "Label" ? "Option Label" : "Option Value (Integer)";
                case "MultiOptionSet":
                    return column.ExportMode == "Label" ? "Labels (comma-separated)" : "Values (comma-separated integers)";
                case "DateTime": return "DateTime (UTC ISO 8601)";
                case "Money": return "Money (Decimal)";
                case "Boolean": return "true / false";
                default: return column.Type;
            }
        }

        private void AddLookupColumns(
            ExcelExportConfig config,
            string logicalName,
            string displayName,
            string relatedTable,
            string resolution,
            List<string> keyFields)
        {
            config.Columns.Add(new ExcelColumnConfig
            {
                LogicalName = logicalName,
                DisplayName = displayName,
                Type = "Lookup",
                RelatedTable = relatedTable,
                Resolution = resolution,
                AlternateKeyFields = keyFields
            });

            foreach (var field in keyFields)
                AddLookupKeyFieldColumns(config, logicalName, displayName, relatedTable, field, new HashSet<string>(), 0);
        }

        private void AddLookupKeyFieldColumns(
            ExcelExportConfig config,
            string ownerLogicalName,
            string ownerDisplayName,
            string ownerTable,
            string fieldLogicalName,
            HashSet<string> visited,
            int depth)
        {
            var attr = GetAttributeMetadata(ownerTable, fieldLogicalName);
            var type = attr != null ? GetExcelType(attr) : "String";
            var displayName = attr?.DisplayName.UserLocalizedLabel?.Label ?? fieldLogicalName;

            var column = new ExcelColumnConfig
            {
                LogicalName = $"{ownerLogicalName}.{fieldLogicalName}",
                DisplayName = $"{ownerDisplayName} ({displayName})",
                Type = "LookupKeyField",
                OwnerAttribute = ownerLogicalName,
                KeyFieldType = type
            };

            if (type == "OptionSet" || type == "MultiOptionSet")
            {
                column.ExportMode = "Label";
                column.Options = GetOptions(attr);
            }
            else if (type == "Lookup")
            {
                var targets = (attr as LookupAttributeMetadata)?.Targets ?? new string[0];
                var nestedTable = targets.FirstOrDefault() ?? string.Empty;
                column.RelatedTable = nestedTable;

                var key = column.LogicalName;
                var nestedConfig = _nestedLookupConfigs.TryGetValue(key, out var cfg) ? cfg : null;
                if (nestedConfig?.Resolution == "Custom" && nestedConfig.Fields.Any())
                {
                    column.Resolution = "Custom";
                    column.AlternateKeyFields = nestedConfig.Fields;
                }
                else
                {
                    column.Resolution = "Guid";
                }
            }

            config.Columns.Add(column);

            if (column.KeyFieldType == "Lookup"
                && column.Resolution == "Custom"
                && column.AlternateKeyFields?.Any() == true)
            {
                foreach (var nestedField in column.AlternateKeyFields)
                    AddLookupKeyFieldColumns(config, column.LogicalName, column.DisplayName, column.RelatedTable, nestedField, new HashSet<string>(visited), depth + 1);
            }
        }

        private AttributeMetadata GetAttributeMetadata(string tableName, string attributeName)
        {
            if (string.IsNullOrWhiteSpace(tableName) || string.IsNullOrWhiteSpace(attributeName)) return null;

            try
            {
                return _repo.GetTableMetadata(tableName)
                    .Attributes
                    .FirstOrDefault(a => a.LogicalName == attributeName);
            }
            catch
            {
                return null;
            }
        }

        private EntityKeyMetadata GetDefaultAlternateKey(string tableName)
        {
            if (string.IsNullOrWhiteSpace(tableName)) return null;

            try
            {
                return _repo.GetAlternateKeys(tableName)
                    .OrderBy(k => k.KeyAttributes?.Length ?? int.MaxValue)
                    .ThenBy(k => k.LogicalName)
                    .FirstOrDefault();
            }
            catch
            {
                return null;
            }
        }

        private string GetExcelType(AttributeMetadata attr)
        {
            switch (attr.AttributeType)
            {
                case AttributeTypeCode.Uniqueidentifier: return "Guid";
                case AttributeTypeCode.Lookup:
                case AttributeTypeCode.Customer:
                case AttributeTypeCode.Owner:           return "Lookup";
                case AttributeTypeCode.Picklist:
                case AttributeTypeCode.State:
                case AttributeTypeCode.Status:          return "OptionSet";
                case AttributeTypeCode.Boolean:         return "Boolean";
                case AttributeTypeCode.DateTime:        return "DateTime";
                case AttributeTypeCode.Integer:         return "Integer";
                case AttributeTypeCode.BigInt:          return "BigInt";
                case AttributeTypeCode.Decimal:         return "Decimal";
                case AttributeTypeCode.Double:          return "Double";
                case AttributeTypeCode.Money:           return "Money";
                case AttributeTypeCode.Virtual:
                    return attr.AttributeTypeName?.Value == "MultiSelectPicklistType" ? "MultiOptionSet" : null;
                case AttributeTypeCode.ManagedProperty: return null;
                default: return "String";
            }
        }

        #endregion

        #region Helpers

        private Label EmptyLabel(string text) => new Label
        {
            Text = text, ForeColor = Color.Gray, AutoSize = true, Location = new Point(5, 8)
        };

        private class AltKeyItem
        {
            public EntityKeyMetadata Key { get; }
            private readonly string _display;
            public AltKeyItem(EntityKeyMetadata key, string display) { Key = key; _display = display; }
            public override string ToString() => _display;
        }

        private class NestedLookupConfig
        {
            public string Resolution { get; set; } = "Guid";
            public List<string> Fields { get; set; } = new List<string>();
        }

        private class ColumnManagerState
        {
            public bool Include { get; set; } = true;
            public string HintOverride { get; set; }
            public int? Order { get; set; }
        }

        private class MatchKeyItem
        {
            public string Mode { get; set; }
            public List<string> Fields { get; set; } = new List<string>();
            public string AlternateKeyName { get; set; }
            public string Display { get; set; }

            public bool Matches(MatchKeyItem other)
            {
                if (other == null) return false;
                if (!string.Equals(Mode, other.Mode, StringComparison.OrdinalIgnoreCase)) return false;
                if (Mode == "AlternateKey" && !string.Equals(AlternateKeyName, other.AlternateKeyName, StringComparison.OrdinalIgnoreCase)) return false;
                return Fields.SequenceEqual(other.Fields ?? new List<string>(), StringComparer.OrdinalIgnoreCase);
            }

            public override string ToString() => Display;
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

        #endregion

        #region Nested lookup dialog

        private class NestedLookupConfigDialog : Form
        {
            private readonly string _ownerLogicalName;
            private readonly List<LookupAttributeMetadata> _lookupFields;
            private readonly CrmRepo _repo;
            private readonly Dictionary<string, NestedLookupConfig> _existing;
            private readonly Dictionary<string, RadioButton> _customRadios = new Dictionary<string, RadioButton>();
            private readonly Dictionary<string, Label> _selectionLabels = new Dictionary<string, Label>();
            private readonly Dictionary<string, List<string>> _selections = new Dictionary<string, List<string>>();

            public Dictionary<string, NestedLookupConfig> Configs { get; private set; } = new Dictionary<string, NestedLookupConfig>();

            public NestedLookupConfigDialog(
                string ownerLogicalName,
                List<LookupAttributeMetadata> lookupFields,
                CrmRepo repo,
                Dictionary<string, NestedLookupConfig> existing)
            {
                _ownerLogicalName = ownerLogicalName;
                _lookupFields = lookupFields;
                _repo = repo;
                _existing = existing;

                BuildLayout();
            }

            private void BuildLayout()
            {
                Text = "Nested Lookup Resolution";
                ClientSize = new Size(720, Math.Max(160, Math.Min(560, 72 + _lookupFields.Count * 58)));
                FormBorderStyle = FormBorderStyle.FixedDialog;
                MaximizeBox = false;
                MinimizeBox = false;
                StartPosition = FormStartPosition.CenterParent;
                ShowIcon = false;
                ShowInTaskbar = false;

                var outer = new TableLayoutPanel
                {
                    Dock = DockStyle.Fill,
                    RowCount = 2,
                    ColumnCount = 1,
                    Padding = new Padding(10)
                };
                outer.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
                outer.RowStyles.Add(new RowStyle(SizeType.Absolute, 44F));

                var panel = new Panel { Dock = DockStyle.Fill, AutoScroll = true };
                var y = 0;
                foreach (var field in _lookupFields)
                {
                    var row = BuildRow(field, y);
                    panel.Controls.Add(row);
                    y += row.Height + 6;
                }

                var btnRow = new FlowLayoutPanel
                {
                    Dock = DockStyle.Fill,
                    FlowDirection = FlowDirection.RightToLeft,
                    Padding = new Padding(0, 8, 0, 0)
                };
                var btnCancel = new Button { Text = "Cancel", Width = 90, Height = 28, DialogResult = DialogResult.Cancel };
                var btnOk = new Button { Text = "OK", Width = 90, Height = 28 };
                btnOk.Click += (s, e) =>
                {
                    BuildConfigs();
                    DialogResult = DialogResult.OK;
                    Close();
                };
                btnRow.Controls.Add(btnCancel);
                btnRow.Controls.Add(btnOk);

                outer.Controls.Add(panel, 0, 0);
                outer.Controls.Add(btnRow, 0, 1);
                Controls.Add(outer);
                AcceptButton = btnOk;
                CancelButton = btnCancel;
            }

            private Panel BuildRow(LookupAttributeMetadata field, int y)
            {
                var key = $"{_ownerLogicalName}.{field.LogicalName}";
                var displayName = field.DisplayName.UserLocalizedLabel?.Label ?? field.LogicalName;
                var targets = field.Targets ?? new string[0];
                var target = targets.FirstOrDefault() ?? string.Empty;
                var existing = _existing.TryGetValue(key, out var cfg) ? cfg : new NestedLookupConfig();
                _selections[key] = new List<string>(existing.Fields ?? new List<string>());

                var row = new Panel { Location = new Point(0, y), Width = 680, Height = 52 };
                row.Controls.Add(new Label
                {
                    Text = $"{displayName} ({field.LogicalName})",
                    Location = new Point(0, 6),
                    Width = 230,
                    AutoSize = false
                });

                row.Controls.Add(new Label
                {
                    Text = target,
                    Location = new Point(235, 6),
                    Width = 120,
                    ForeColor = Color.DimGray,
                    AutoSize = false
                });

                var rbGuid = new RadioButton { Text = "GUID", Location = new Point(360, 4), AutoSize = true, Checked = existing.Resolution != "Custom" };
                var rbCustom = new RadioButton { Text = "Custom", Location = new Point(360, 24), AutoSize = true, Checked = existing.Resolution == "Custom" };
                row.Controls.Add(rbGuid);
                row.Controls.Add(rbCustom);
                _customRadios[key] = rbCustom;

                var btnSelect = new Button
                {
                    Text = "Select attributes...",
                    Location = new Point(445, 22),
                    Width = 130,
                    Height = 24,
                    Enabled = rbCustom.Checked
                };
                var label = new Label
                {
                    Text = _selections[key].Any() ? string.Join(", ", _selections[key]) : "(none selected)",
                    Location = new Point(580, 26),
                    Width = 95,
                    ForeColor = Color.DimGray,
                    AutoSize = false
                };
                row.Controls.Add(btnSelect);
                row.Controls.Add(label);
                _selectionLabels[key] = label;

                rbCustom.CheckedChanged += (s, e) => btnSelect.Enabled = rbCustom.Checked;
                btnSelect.Click += (s, e) => SelectNestedAttributes(key, target);

                return row;
            }

            private void SelectNestedAttributes(string key, string target)
            {
                if (string.IsNullOrWhiteSpace(target)) return;

                List<AttributeMetadata> attrs;
                try
                {
                    Cursor = Cursors.WaitCursor;
                    attrs = _repo.GetTableMetadata(target)
                        .Attributes
                        .Where(a => a.IsValidForRead == true
                                 && a.AttributeType != AttributeTypeCode.Lookup
                                 && a.AttributeType != AttributeTypeCode.Customer
                                 && a.AttributeType != AttributeTypeCode.Owner
                                 && a.AttributeType != AttributeTypeCode.Virtual
                                 && a.AttributeType != AttributeTypeCode.ManagedProperty)
                        .OrderBy(a => a.LogicalName)
                        .ToList();
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Could not load attributes for '{target}': {ex.Message}", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    return;
                }
                finally
                {
                    Cursor = Cursors.Default;
                }

                using (var dlg = new PropertySelectorDialog(target, attrs, _selections[key]))
                {
                    if (dlg.ShowDialog(this) != DialogResult.OK) return;
                    _selections[key] = dlg.SelectedProperties;
                    _selectionLabels[key].Text = _selections[key].Any() ? string.Join(", ", _selections[key]) : "(none selected)";
                }
            }

            private void BuildConfigs()
            {
                foreach (var field in _lookupFields)
                {
                    var key = $"{_ownerLogicalName}.{field.LogicalName}";
                    var useCustom = _customRadios.TryGetValue(key, out var rb) && rb.Checked;
                    Configs[key] = new NestedLookupConfig
                    {
                        Resolution = useCustom ? "Custom" : "Guid",
                        Fields = useCustom ? _selections[key] : new List<string>()
                    };
                }
            }
        }

        #endregion

        #region Property selector dialog

        private class PropertySelectorDialog : Form
        {
            private readonly ListView _list;
            private int _sortColumn;
            public List<string> SelectedProperties { get; private set; } = new List<string>();

            public PropertySelectorDialog(string tableName, List<AttributeMetadata> attributes, List<string> preSelected)
            {
                Text = $"Select attributes — {tableName}";
                ClientSize = new Size(560, 520);
                FormBorderStyle = FormBorderStyle.FixedDialog;
                MaximizeBox = false;
                MinimizeBox = false;
                StartPosition = FormStartPosition.CenterParent;
                ShowIcon = false;
                ShowInTaskbar = false;

                var warning = new Label
                {
                    Text = "⚠  Uniqueness is not guaranteed. Ensure the selected attributes\n" +
                           "cannot produce multiple matching records on import.",
                    Location = new Point(10, 10),
                    Size = new Size(540, 40),
                    ForeColor = Color.DarkOrange
                };

                _list = new ListView
                {
                    Location = new Point(10, 58),
                    Size = new Size(540, 390),
                    CheckBoxes = true,
                    FullRowSelect = true,
                    HideSelection = false,
                    MultiSelect = false,
                    View = View.Details,
                    Sorting = SortOrder.Ascending
                };
                _list.Columns.Add("Logical", 240);
                _list.Columns.Add("Display", 275);
                _list.ColumnClick += List_ColumnClick;

                var selected = new HashSet<string>(preSelected ?? new List<string>(), StringComparer.OrdinalIgnoreCase);
                foreach (var attr in attributes)
                {
                    var display = attr.DisplayName?.UserLocalizedLabel?.Label ?? attr.LogicalName;
                    _list.Items.Add(new ListViewItem(new[] { attr.LogicalName, display })
                    {
                        Checked = selected.Contains(attr.LogicalName),
                        Tag = attr.LogicalName
                    });
                }
                SortByColumn(0, SortOrder.Ascending);

                var btnOk = new Button { Text = "OK", Location = new Point(370, 460), Width = 80, Height = 28, DialogResult = DialogResult.OK };
                var btnCancel = new Button { Text = "Cancel", Location = new Point(460, 460), Width = 80, Height = 28, DialogResult = DialogResult.Cancel };
                btnOk.Click += (s, e) =>
                {
                    SelectedProperties = _list.CheckedItems
                        .Cast<ListViewItem>()
                        .Select(i => i.Tag as string)
                        .Where(name => !string.IsNullOrWhiteSpace(name))
                        .ToList();
                };

                Controls.Add(warning);
                Controls.Add(_list);
                Controls.Add(btnOk);
                Controls.Add(btnCancel);
                AcceptButton = btnOk;
                CancelButton = btnCancel;
            }

            private void List_ColumnClick(object sender, ColumnClickEventArgs e)
            {
                var order = _sortColumn == e.Column && _list.Sorting == SortOrder.Ascending
                    ? SortOrder.Descending
                    : SortOrder.Ascending;
                SortByColumn(e.Column, order);
            }

            private void SortByColumn(int column, SortOrder order)
            {
                _sortColumn = column;
                _list.Sorting = order;
                _list.ListViewItemSorter = new ListViewComparer(column, order);
                _list.Sort();
            }
        }

        #endregion
    }
}
