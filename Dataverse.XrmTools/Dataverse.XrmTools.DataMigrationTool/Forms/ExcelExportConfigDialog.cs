using System;
using System.Linq;
using System.Drawing;
using System.Windows.Forms;
using System.Collections.Generic;
using Microsoft.Xrm.Sdk.Metadata;
using Dataverse.XrmTools.DataMigrationTool.Models;
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

        // Per-attribute option set state
        private readonly Dictionary<string, RadioButton> _optionLabelRadios = new Dictionary<string, RadioButton>();

        public ExcelExportConfig Config { get; private set; }

        public ExcelExportConfigDialog(IEnumerable<AttributeMetadata> selectedAttributes, EntityMetadata metadata, CrmRepo repo)
        {
            _selectedAttributes = selectedAttributes.ToList();
            _metadata = metadata;
            _repo = repo;

            BuildLayout();
            PopulateLookupSection();
            PopulateOptionSetSection();
        }

        #region Layout

        private Panel _lookupPanel;
        private Panel _optionSetPanel;

        private void BuildLayout()
        {
            Text = "Excel Export Configuration";
            ClientSize = new Size(940, 700);
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
            outer.RowStyles.Add(new RowStyle(SizeType.Percent, 50F));
            outer.RowStyles.Add(new RowStyle(SizeType.Percent, 50F));
            outer.RowStyles.Add(new RowStyle(SizeType.Absolute, 48F));

            var grpLookup = new GroupBox { Text = "A — Lookup resolution", Dock = DockStyle.Fill, Padding = new Padding(5) };
            _lookupPanel = new Panel { Dock = DockStyle.Fill, AutoScroll = true };
            grpLookup.Controls.Add(_lookupPanel);

            var grpOptionSet = new GroupBox { Text = "B — Option sets", Dock = DockStyle.Fill, Padding = new Padding(5) };
            _optionSetPanel = new Panel { Dock = DockStyle.Fill, AutoScroll = true };
            grpOptionSet.Controls.Add(_optionSetPanel);

            var btnRow = new FlowLayoutPanel
            {
                Dock = DockStyle.Fill,
                FlowDirection = FlowDirection.RightToLeft,
                Padding = new Padding(0, 8, 0, 0)
            };
            var btnCancel = new Button { Text = "Cancel", Width = 90, Height = 30, DialogResult = DialogResult.Cancel };
            var btnOk = new Button { Text = "Export", Width = 90, Height = 30 };
            btnOk.Click += OnExportClick;
            btnRow.Controls.Add(btnCancel);
            btnRow.Controls.Add(btnOk);

            outer.Controls.Add(grpLookup, 0, 0);
            outer.Controls.Add(grpOptionSet, 0, 1);
            outer.Controls.Add(btnRow, 0, 2);

            Controls.Add(outer);
            AcceptButton = btnOk;
            CancelButton = btnCancel;
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
                Width = 355,
                Height = 18,
                AutoSize = false,
                ForeColor = Color.DimGray,
                Font = new Font(Font.FontFamily, Font.Size - 0.5f),
                Visible = false
            };
            row.Controls.Add(altInfoLabel);

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
                }
                else
                {
                    altInfoLabel.Visible = false;
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
                }
            };

            // Wire select button
            btnSelect.Click += (s, e) => OpenPropertySelector(attr.LogicalName, targets, selLabel);

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
                    foreach (var key in _repo.GetAlternateKeys(tableName))
                    {
                        var keyName = key.DisplayName?.UserLocalizedLabel?.Label ?? key.LogicalName;
                        combo.Items.Add(new AltKeyItem(key, keyName));
                    }
                }
                catch { }
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

        private void OpenPropertySelector(string attrLogicalName, string[] targets, Label selLabel)
        {
            var tableName = _targetTableCombos.TryGetValue(attrLogicalName, out var tblCbo)
                ? (string)tblCbo.SelectedItem
                : targets.FirstOrDefault() ?? string.Empty;

            if (string.IsNullOrEmpty(tableName)) return;

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
                return;
            }
            finally
            {
                Cursor = Cursors.Default;
            }

            var existing = _customSelections.TryGetValue(attrLogicalName, out var prev) ? prev : new List<string>();

            using (var dlg = new PropertySelectorDialog(tableName, attrs, existing))
            {
                if (dlg.ShowDialog(this) != DialogResult.OK) return;
                _customSelections[attrLogicalName] = dlg.SelectedProperties;
                selLabel.Text = dlg.SelectedProperties.Any()
                    ? string.Join(", ", dlg.SelectedProperties)
                    : "(none selected)";
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

        private void OnExportClick(object sender, EventArgs e)
        {
            Config = BuildConfig();
            DialogResult = DialogResult.OK;
            Close();
        }

        private ExcelExportConfig BuildConfig()
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
                        config.Columns.Add(new ExcelColumnConfig
                        {
                            LogicalName = attr.LogicalName, DisplayName = displayName,
                            Type = "Lookup", RelatedTable = relatedTable,
                            Resolution = "AlternateKey", AlternateKeyFields = keyFields
                        });
                        foreach (var f in keyFields)
                            config.Columns.Add(new ExcelColumnConfig
                            {
                                LogicalName = $"{attr.LogicalName}.{f}",
                                DisplayName = $"{displayName} ({f})",
                                Type = "LookupKeyField", OwnerAttribute = attr.LogicalName
                            });
                    }
                    else if (rbCustom?.Checked == true)
                    {
                        var fields = _customSelections.TryGetValue(attr.LogicalName, out var sel) ? sel : new List<string>();
                        config.Columns.Add(new ExcelColumnConfig
                        {
                            LogicalName = attr.LogicalName, DisplayName = displayName,
                            Type = "Lookup", RelatedTable = relatedTable,
                            Resolution = "Custom", AlternateKeyFields = fields
                        });
                        foreach (var f in fields)
                            config.Columns.Add(new ExcelColumnConfig
                            {
                                LogicalName = $"{attr.LogicalName}.{f}",
                                DisplayName = $"{displayName} ({f})",
                                Type = "LookupKeyField", OwnerAttribute = attr.LogicalName
                            });
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

        #endregion

        #region Property selector dialog

        private class PropertySelectorDialog : Form
        {
            private readonly CheckedListBox _list;
            public List<string> SelectedProperties { get; private set; } = new List<string>();

            public PropertySelectorDialog(string tableName, List<AttributeMetadata> attributes, List<string> preSelected)
            {
                Text = $"Select attributes — {tableName}";
                ClientSize = new Size(420, 520);
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
                    Size = new Size(400, 40),
                    ForeColor = Color.DarkOrange
                };

                _list = new CheckedListBox
                {
                    Location = new Point(10, 58),
                    Size = new Size(400, 390),
                    CheckOnClick = true
                };

                foreach (var attr in attributes)
                {
                    var display = attr.DisplayName.UserLocalizedLabel?.Label ?? attr.LogicalName;
                    var idx = _list.Items.Add($"{display} ({attr.LogicalName})");
                    if (preSelected.Contains(attr.LogicalName))
                        _list.SetItemChecked(idx, true);
                    _list.Items[idx] = new AttrItem(attr.LogicalName, display);
                    if (preSelected.Contains(attr.LogicalName))
                        _list.SetItemChecked(_list.Items.Count - 1, true);
                }

                var btnOk = new Button { Text = "OK", Location = new Point(230, 460), Width = 80, Height = 28, DialogResult = DialogResult.OK };
                var btnCancel = new Button { Text = "Cancel", Location = new Point(320, 460), Width = 80, Height = 28, DialogResult = DialogResult.Cancel };
                btnOk.Click += (s, e) =>
                {
                    SelectedProperties = _list.CheckedItems.Cast<AttrItem>().Select(i => i.LogicalName).ToList();
                };

                Controls.Add(warning);
                Controls.Add(_list);
                Controls.Add(btnOk);
                Controls.Add(btnCancel);
                AcceptButton = btnOk;
                CancelButton = btnCancel;
            }

            protected override void OnLoad(EventArgs e)
            {
                base.OnLoad(e);
                // Re-apply checks after Items are rebuilt
                for (var i = 0; i < _list.Items.Count; i++)
                    if (_list.GetItemChecked(i)) _list.SetItemChecked(i, true);
            }

            private class AttrItem
            {
                public string LogicalName { get; }
                private readonly string _display;
                public AttrItem(string logicalName, string display) { LogicalName = logicalName; _display = display; }
                public override string ToString() => $"{_display} ({LogicalName})";
            }
        }

        #endregion
    }
}
