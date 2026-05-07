// System
using System;
using System.IO;
using System.Xml;
using System.Data;
using System.Linq;
using System.Text;
using System.Drawing;
using System.Windows.Forms;
using System.Threading.Tasks;
using System.ComponentModel;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Text.RegularExpressions;

// Microsoft
using Microsoft.Xrm.Sdk;
using Microsoft.Crm.Sdk.Messages;
using Microsoft.Xrm.Sdk.Query;
using Microsoft.Xrm.Tooling.Connector;

// 3rd Party
using McTools.Xrm.Connection;
using XrmToolBox.Extensibility;
using XrmToolBox.Extensibility.Args;
using XrmToolBox.Extensibility.Interfaces;
using Newtonsoft.Json.Linq;

// DataMigrationTool
using Dataverse.XrmTools.DataMigrationTool.Enums;
using Dataverse.XrmTools.DataMigrationTool.Logic;
using Dataverse.XrmTools.DataMigrationTool.Forms;
using Dataverse.XrmTools.DataMigrationTool.Models;
using Dataverse.XrmTools.DataMigrationTool.Helpers;
using Dataverse.XrmTools.DataMigrationTool.AppSettings;
using Dataverse.XrmTools.DataMigrationTool.Repositories;

namespace Dataverse.XrmTools.DataMigrationTool
{
    public partial class DataMigrationControl : MultipleConnectionsPluginControlBase, IStatusBarMessenger, IMessageBusHost
    {
        #region Variables
        // general
        private Logger _logger;
        private Settings _settings;

        // service
        private CrmServiceClient _sourceClient;
        private CrmServiceClient _targetClient;

        // main objects
        private Instance _sourceInstance;
        private Instance _targetInstance;
        private IEnumerable<Table> _tables;
        private List<Mapping> _mappings;
        private IEnumerable<Sort> _sorts;
        private DmtSettings _dmtSettings;
        private string _dmtFilePath;
        private TableSettings _currentTableSettings;
        private string _currentTableLogicalName;
        private string _previousTableLogicalName;
        private Timer _dmtAutoSaveTimer;

        // flags
        private bool _ready = false;
        private bool _working;
        private int _activeExcelImportOperationId;
        private bool _importPreviewDialogOpen;
        private bool _suppressTableSelectionChanged;
        private bool _suppressSettingsEvents;

        private const int ExcelImportLargeRowWarning = 1000;
        private const int ExcelImportVeryLargeRowWarning = 5000;
        private const int ExcelImportHugeRowWarning = 20000;
        #endregion Variables

        #region Handlers
        public event EventHandler<StatusBarMessageEventArgs> SendMessageToStatusBar;
        public event EventHandler<MessageBusEventArgs> OnOutgoingMessage;
        #endregion Variables

        public DataMigrationControl()
        {
            LogInfo("----- Starting Data Migration Tool -----");

            LogInfo("Loading Settings...");
            SettingsHelper.GetSettings(out _settings);

            LogInfo("Initializing components...");
            InitializeComponent();
            tsmiEnvironments.Image = CreateEnvironmentsIcon();
            tsmiDmtFile.Image = CreateSettingsFileIcon();
            MoveImportSettingsIntoDialogs();
            InitializeDmtAutoSave();
            RenderDmtFileMenu();

            _logger = new Logger();
            _logger.OnLog += Log;
        }

        public void DataMigrationControl_Load(object sender, EventArgs e)
        {
            _logger.Log(LogLevel.INFO, "Data Migration tool initialized");
            ExecuteMethod(WhoAmI);
        }

        #region Interface Methods
        /// <summary>
        /// This event occurs when the connection has been updated in XrmToolBox
        /// </summary>
        public override void UpdateConnection(IOrganizationService newService, ConnectionDetail detail, string actionName, object parameter)
        {
            try
            {
                _logger.Log(LogLevel.INFO, $"Updating connection...");
                base.UpdateConnection(newService, detail, actionName, parameter);
                _logger.Log(LogLevel.INFO, $"Connection successfully updated...");

                var client = detail.ServiceClient;

                if (!actionName.Equals("AdditionalOrganization"))
                {
                    _logger.Log(LogLevel.INFO, $"Checking for legacy instances...");
                    UpdateLegacyInstance(detail.ConnectionId.Value, client);

                    _logger.Log(LogLevel.INFO, $"Checking settings for known instances...");
                    var instance = _settings.Instances.FirstOrDefault(inst => inst.UniqueName.Equals(client.ConnectedOrgUniqueName));
                    if (instance is null)
                    {
                        _logger.Log(LogLevel.INFO, $"New instance '{client.ConnectedOrgUniqueName}': Adding to settings...");
                        instance = new Instance
                        {
                            Id = client.ConnectedOrgId,
                            UniqueName = client.ConnectedOrgUniqueName,
                            FriendlyName = client.ConnectedOrgFriendlyName,
                            Mappings = new List<Mapping>(),
                            Updated = true
                        };

                        _settings.Instances.Add(instance);
                    }
                    else
                    {
                        _logger.Log(LogLevel.INFO, $"Found known instance '{instance.UniqueName}'");
                    }

                    _sourceClient = client;
                    _sourceInstance = instance;

                    // load source instance mappings
                    _logger.Log(LogLevel.INFO, $"Loading mappings...");
                    try
                    {
                        var srcMappings = _sourceInstance.Mappings.Where(map => map.SourceInstanceName.Equals(_sourceInstance.FriendlyName));
                        _mappings = new List<Mapping>(srcMappings);

                        _logger.Log(LogLevel.INFO, $"Clearing mappings...");
                        ClearMappings();
                    }
                    catch
                    {
                        _logger.Log(LogLevel.INFO, $"Corrupt mappings detected: Resetting mappings...");
                        ClearMappings(true);
                    }

                    // load sorts
                    _logger.Log(LogLevel.INFO, $"Loading inital settings...");
                    LoadUiSettings();

                    // save settings file
                    SettingsHelper.SetSettings(_settings);

                    // render UI components
                    _logger.Log(LogLevel.INFO, $"Rendering UI components...");
                    ReRenderComponents(true);

                    // load tables when source connection changes
                    LoadTables();
                }
            }
            catch (Exception ex)
            {
                ManageWorkingState(false);
                _logger.Log(LogLevel.ERROR, ex.Message);
                MessageBox.Show(this, $"An error occurred: {ex.Message}", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        protected override void ConnectionDetailsUpdated(NotifyCollectionChangedEventArgs args)
        {
            try
            {
                if (args.Action.Equals(NotifyCollectionChangedAction.Add))
                {
                    var detail = (ConnectionDetail)args.NewItems[0];
                    var client = detail.ServiceClient;

                    UpdateLegacyInstance(detail.ConnectionId.Value, client);
                    if (_settings.SettingsVersion.Equals(0))
                    {
                        UpdateLegacyMappings(client);
                    }

                    if (_sourceClient == null) { throw new Exception("Source connection is invalid"); }
                    _logger.Log(LogLevel.INFO, $"Source OrgId: {_sourceClient.ConnectedOrgId}");
                    _logger.Log(LogLevel.INFO, $"Source OrgUniqueName: {_sourceClient.ConnectedOrgUniqueName}");
                    _logger.Log(LogLevel.INFO, $"Source OrgFriendlyName: {_sourceClient.ConnectedOrgFriendlyName}");
                    _logger.Log(LogLevel.INFO, $"Source EnvId: {_sourceClient.EnvironmentId}");

                    if (client == null) { throw new Exception("Target connection is invalid"); }
                    _logger.Log(LogLevel.INFO, $"Target OrgId: {client.ConnectedOrgId}");
                    _logger.Log(LogLevel.INFO, $"Target OrgUniqueName: {client.ConnectedOrgUniqueName}");
                    _logger.Log(LogLevel.INFO, $"Target OrgFriendlyName: {client.ConnectedOrgFriendlyName}");
                    _logger.Log(LogLevel.INFO, $"Target EnvId: {client.EnvironmentId}");

                    if (_sourceClient.ConnectedOrgUniqueName.Equals(client.ConnectedOrgUniqueName))
                    {
                        throw new Exception("Source and Target connections must refer to different Dataverse instances");
                    }

                    var instance = _settings.Instances.FirstOrDefault(inst => !string.IsNullOrEmpty(inst.UniqueName) && inst.UniqueName.Equals(client.ConnectedOrgUniqueName));
                    if (instance == null)
                    {
                        instance = new Instance
                        {
                            Id = client.ConnectedOrgId,
                            UniqueName = client.ConnectedOrgUniqueName,
                            FriendlyName = client.ConnectedOrgFriendlyName,
                            Mappings = new List<Mapping>(),
                            Updated = true
                    };

                        _settings.Instances.Add(instance);
                    }

                    _targetClient = client;
                    _targetInstance = instance;

                    // filter mappings by target instance
                    var tgtMappings = _mappings.Where(map => map.TargetInstanceName.Equals(_targetInstance.FriendlyName));
                    _mappings = new List<Mapping>(tgtMappings);

                    // load settings + mappings
                    LoadUiSettings();
                    GenerateMappings();

                    SettingsHelper.SetSettings(_settings);

                    ReRenderComponents(true);
                    _ready = true;
                    SendMessageToStatusBar?.Invoke(this, new StatusBarMessageEventArgs("Target Connection ready"));
                }
            }
            catch (Exception ex)
            {
                ManageWorkingState(false);
                _logger.Log(LogLevel.ERROR, ex.Message);
                MessageBox.Show(this, $"An error occurred: {ex.Message}", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void UpdateLegacyInstance(Guid legacyId, CrmServiceClient client)
        {
            var legacy = _settings.Instances.FirstOrDefault(inst => inst.Updated.Equals(false) && inst.Id.Equals(legacyId));

            // update instance
            if (legacy != null)
            {
                _logger.Log(LogLevel.INFO, $"Found legacy instance: Updating...");

                legacy.Id = client.ConnectedOrgId;
                legacy.FriendlyName = client.ConnectedOrgFriendlyName;
                legacy.UniqueName = client.ConnectedOrgUniqueName;
                legacy.Updated = true;
            }

            SettingsHelper.SetSettings(_settings);
        }

        private void UpdateLegacyMappings(CrmServiceClient targetClient)
        {
            var mappings = _settings.Instances.SelectMany(inst => inst.Mappings.Where(map => map.Type.Equals(Enums.MappingType.Value)));

            // update mappings
            foreach (var map in mappings)
            {
                map.SourceInstanceName = _sourceClient.ConnectedOrgFriendlyName;
                map.TargetInstanceName = targetClient.ConnectedOrgFriendlyName;
            }

            _settings.SettingsVersion = 1;
            SettingsHelper.SetSettings(_settings);
        }

        public void OnIncomingMessage(MessageBusEventArgs message)
        {
            try
            {
                if (message == null) { return; }

                var sourcePlugin = message.SourcePlugin;
                var isSupportedPlugin = sourcePlugin.Equals("FetchXML Builder") || sourcePlugin.Equals("SQL 4 CDS");
                if (!isSupportedPlugin) { return; }

                if (!(message.TargetArgument is string incomingFetch) || string.IsNullOrWhiteSpace(incomingFetch))
                {
                    SendMessageToStatusBar?.Invoke(this, new StatusBarMessageEventArgs($"{sourcePlugin} returned no query"));
                    return;
                }

                try
                {
                    var filters = ExtractFilterNode(incomingFetch);

                    var tableData = GetSelectedTableItemData(false);
                    if (tableData == null)
                    {
                        SendMessageToStatusBar?.Invoke(this, new StatusBarMessageEventArgs($"Error setting filters from {sourcePlugin}"));
                        return;
                    }

                    tableData.Settings.Filter = filters;
                    SettingsHelper.SetSettings(_settings);
                    rtbFilter.Text = filters;
                }
                catch (Exception ex)
                {
                    _logger.Log(LogLevel.ERROR, ex.Message);
                    MessageBox.Show(this, $"Error applying query from {sourcePlugin}: {ex.Message}", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
            }
            catch (Exception ex)
            {
                _logger.Log(LogLevel.ERROR, ex.Message);
                MessageBox.Show(this, $"An error occurred: {ex.Message}", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }
        #endregion Interface Methods

        #region Private Main Methods
        private void WhoAmI()
        {
            _sourceClient.Execute(new WhoAmIRequest());
        }

        private void LoadTables()
        {
            _logger.Log(LogLevel.INFO, $"Loading tables...");

            gbAttributes.Enabled = false;
            gbFilters.Enabled = false;

            if (_sourceClient == null)
            {
                ExecuteMethod(WhoAmI);
                return;
            }

            if (_working) { return; }

            lvTables.Items.Clear();
            lvAttributes.Items.Clear();

            ManageWorkingState(true);

            WorkAsync(new WorkAsyncInfo
            {
                Message = "Loading tables...",
                Work = (worker, args) =>
                {
                    var repo = new CrmRepo(_sourceClient, worker);
                    var tableMetadata = repo.GetOrgTables();

                    args.Result = tableMetadata.Select(tbl => new Table
                    {
                        LogicalName = tbl.LogicalName,
                        DisplayName = tbl.DisplayName.UserLocalizedLabel != null ? tbl.DisplayName.UserLocalizedLabel.Label : string.Empty,
                        IdAttribute = tbl.PrimaryIdAttribute,
                        NameAttribute = tbl.PrimaryNameAttribute,
                        IsCustomizable = tbl.IsCustomizable.Value
                    }).ToList();
                },
                PostWorkCallBack = args =>
                {
                    if (args.Error != null)
                    {
                        throw new Exception(args.Error.Message);
                    }
                    else
                    {
                        // load tables list
                        _tables = args.Result as IEnumerable<Table>;
                        LoadTablesList();

                        SendMessageToStatusBar?.Invoke(this, new StatusBarMessageEventArgs("Tables load complete"));
                    }
                }
            });
        }

        private void LoadTablesList()
        {
            _logger.Log(LogLevel.INFO, $"Rendering tables list view...");

            var textFilter = txtTableFilter.Text;
            var filtered = _tables.Where(tbl => string.IsNullOrWhiteSpace(textFilter) || tbl.MatchFilter(textFilter));

            foreach (var tbl in filtered)
            {
                var item = tbl.ToListViewItem();
                if (!tbl.IsCustomizable)
                {
                    item.ForeColor = Color.Gray;
                    item.SubItems.Add("Not customizable");
                }

                lvTables.Items.Add(item);
            }

            ManageWorkingState(false);
        }

        private void LoadAttributes()
        {
            _logger.Log(LogLevel.INFO, $"Loading attributes...");

            if (_working) { return; }

            lvAttributes.Items.Clear();
            cbSelectAll.Checked = false;

            ManageWorkingState(true);

            var tableData = GetSelectedTableItemData(false);
            if (tableData == null)
            {
                SendMessageToStatusBar?.Invoke(this, new StatusBarMessageEventArgs("Error loading attributes"));
                return;
            }

            WorkAsync(new WorkAsyncInfo
            {
                Message = $"Loading {tableData.Table.LogicalName} attributes...",
                Work = (worker, args) =>
                {
                    // filter valid attributes
                    args.Result = tableData.Metadata.Attributes
                        .Where(att => att.IsValidForRead != null && att.IsValidForRead.Value)
                        .Select(att => new Models.Attribute
                        {
                            Type = att.AttributeTypeName.Value.EndsWith("Type") ? att.AttributeTypeName.Value.Substring(0, att.AttributeTypeName.Value.LastIndexOf("Type")) : att.AttributeTypeName.Value,
                            LogicalName = att.LogicalName,
                            DisplayName = att.DisplayName.UserLocalizedLabel != null ? att.DisplayName.UserLocalizedLabel.Label : string.Empty,
                            ValidOnCreate = att.IsValidForCreate.Value,
                            ValidOnUpdate = att.IsValidForUpdate.Value
                        })
                        .ToList();
                },
                PostWorkCallBack = args =>
                {
                    if (args.Error != null)
                    {
                        ReRenderComponents(false);

                        gbFilters.Enabled = false;
                        throw new Exception(args.Error.Message);
                    }
                    else
                    {
                        // save settings
                        tableData.Settings.DisplayName = tableData.Table.DisplayName;
                        tableData.Settings.IsCustomizable = tableData.Table.IsCustomizable;
                        SettingsHelper.SetSettings(_settings);

                        // load attributes
                        tableData.Table.AllAttributes = args.Result as List<Models.Attribute>;
                        LoadAttributesList(tableData);
                        LoadFilters(tableData);
                        AutoSaveDmtSettings(false);

                        SendMessageToStatusBar?.Invoke(this, new StatusBarMessageEventArgs("Attributes load complete"));
                    }
                }
            });
        }

        private void LoadAttributesList(TableData tableData)
        {
            _logger.Log(LogLevel.INFO, $"Rendering attributes list view...");

            var deselected = tableData.Settings.DeselectedAttributes;
            if (deselected == null)
            {
                deselected = new List<string>();
                deselected.AddRange(_sourceInstance.DefaultDeselected);

                // save settings
                tableData.Settings.DeselectedAttributes = deselected;
                SettingsHelper.SetSettings(_settings);
                ScheduleDmtAutoSave();
            }
            else
            {
                // migrate legacy entries saved as display names to logical names
                var migrated = false;
                for (var i = 0; i < deselected.Count; i++)
                {
                    if (!tableData.Table.AllAttributes.Any(a => a.LogicalName.Equals(deselected[i])))
                    {
                        var match = tableData.Table.AllAttributes.FirstOrDefault(a => a.DisplayName.Equals(deselected[i]));
                        if (match != null)
                        {
                            deselected[i] = match.LogicalName;
                            migrated = true;
                        }
                    }
                }
                if (migrated)
                {
                    SettingsHelper.SetSettings(_settings);
                    ScheduleDmtAutoSave();
                }
            }

            foreach (var att in tableData.Table.AllAttributes)
            {
                var item = att.ToListViewItem();
                item.Checked = !deselected.Any(dsl => dsl.Equals(att.LogicalName)) && (att.ValidOnCreate || att.ValidOnUpdate);

                CheckInvalidAttribute(att, item);
            }

            ReRenderComponents(true);

            ManageWorkingState(false);
        }

        private void CheckInvalidAttribute(Models.Attribute att, ListViewItem item)
        {
            if (!att.ValidOnCreate || !att.ValidOnUpdate)
            {
                item.ForeColor = Color.Gray;

                if (!att.ValidOnCreate && !att.ValidOnUpdate)
                {
                    item.SubItems.Add("Invalid on create and update");
                    if (!cbHideInvalid.Checked) { lvAttributes.Items.Add(item); }
                }
                else if (!att.ValidOnCreate)
                {
                    item.SubItems.Add("Invalid on create");
                    lvAttributes.Items.Add(item);
                }
                else if (!att.ValidOnUpdate)
                {
                    item.SubItems.Add("Invalid on update");
                    lvAttributes.Items.Add(item);
                }
            }
            else
            {
                lvAttributes.Items.Add(item);
            }
        }

        private void LoadFilters(TableData tableData)
        {
            _logger.Log(LogLevel.INFO, $"Loading table filters...");

            tableData = tableData != null ? tableData : GetSelectedTableItemData(false);
            if (tableData == null || tableData.Settings == null)
            {
                SendMessageToStatusBar?.Invoke(this, new StatusBarMessageEventArgs("Error loading filters"));
                return;
            }

            _suppressSettingsEvents = true;
            try
            {
                rtbFilter.Text = tableData.Settings.Filter ?? string.Empty;
            }
            finally
            {
                _suppressSettingsEvents = false;
            }

            gbFilters.Enabled = true;
        }

        private void PreviewData()
        {
            _logger.Log(LogLevel.INFO, $"Previewing operation...");
            AutoSaveDmtSettings();

            var tableData = GetSelectedTableItemData(targetRequired: false, attributeRequired: true);
            if(tableData == null) {
                SendMessageToStatusBar?.Invoke(this, new StatusBarMessageEventArgs("Preview operation aborted"));
                return;
            }

            ManageWorkingState(true);

            var uiSettings = GetDefaultImportSettings(Enums.Action.Preview);

            tableData.SelectedAttributes = lvAttributes.CheckedItems
                .Cast<ListViewItem>()
                .Select(lvi => lvi.ToObject(new Models.Attribute()) as Models.Attribute)
                .Select(attr => tableData.Table.AllAttributes.FirstOrDefault(tblAttr => tblAttr.LogicalName.Equals(attr.LogicalName)))
                .ToList();

            WorkAsync(new WorkAsyncInfo
            {
                Message = "Previewing operation...",
                AsyncArgument = tableData,
                IsCancelable = true,
                Work = (worker, evt) =>
                {
                    var data = evt.Argument as TableData;

                    var logic = new DataLogic(worker, _sourceClient, _targetClient);
                    var result = Task.Run(() => logic.Preview(data, uiSettings, false));

                    evt.Result = result.Result;
                },
                PostWorkCallBack = evt =>
                {
                    ManageWorkingState(false);

                    if (evt.Error != null)
                    {
                        _logger.Log(LogLevel.ERROR, evt.Error.ToString());
                        MessageBox.Show(GetPreviewErrorMessage(evt.Error), "Preview error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                        SendMessageToStatusBar?.Invoke(this, new StatusBarMessageEventArgs("Preview failed"));
                        return;
                    }

                    if (evt.Cancelled)
                    {
                        SendMessageToStatusBar?.Invoke(this, new StatusBarMessageEventArgs("Preview cancelled"));
                        return;
                    }

                    if (evt.Result != null)
                    {
                        // show preview form
                        var result = evt.Result as OperationResult;

                        var prvwDialog = new Results(result.Items, _settings);
                        prvwDialog.ShowDialog(ParentForm);

                        SettingsHelper.SetSettings(_settings);
                    }
                },
                ProgressChanged = ReportWorkProgress
            });
        }

        private void ExportSettings()
        {
            _logger.Log(LogLevel.INFO, $"Exporting table settings...");
            AutoSaveDmtSettings();

            var tableData = GetSelectedTableItemData(false);
            if (tableData == null)
            {
                SendMessageToStatusBar?.Invoke(this, new StatusBarMessageEventArgs("Error exporting table settings"));
                return;
            }

            var filePath = this.SelectFile("Settings files (*.settings.json)|*.settings.json|Json files (*.json)|*.json", save: true,
                defaultFileName: GetDefaultSaveFileName(".settings.json", tableData.Table.LogicalName));
            if (string.IsNullOrEmpty(filePath)) { return; }

            var json = tableData.Settings.SerializeObject();
            File.WriteAllText(filePath, json);
        }

        private void Export()
        {
            _logger.Log(LogLevel.INFO, $"Export data operation...");
            AutoSaveDmtSettings();

            var tableData = GetSelectedTableItemData(false, true);
            if (tableData == null)
            {
                SendMessageToStatusBar?.Invoke(this, new StatusBarMessageEventArgs("Export operation aborted"));
                return;
            }

            var filePath = this.SelectFile("Json files (*.json)|*.json", save: true,
                defaultFileName: GetDefaultSaveFileName(".json", tableData.Table.LogicalName));
            if (string.IsNullOrEmpty(filePath)) { return; }

            ManageWorkingState(true);

            var uiSettings = ReadSettings(Enums.Action.None);

            tableData.SelectedAttributes = lvAttributes.CheckedItems
                .Cast<ListViewItem>()
                .Select(lvi => lvi.ToObject(new Models.Attribute()) as Models.Attribute)
                .Select(attr => tableData.Table.AllAttributes.FirstOrDefault(tblAttr => tblAttr.LogicalName.Equals(attr.LogicalName)))
                .ToList();

            WorkAsync(new WorkAsyncInfo
            {
                Message = "Exporting records...",
                AsyncArgument = tableData,
                IsCancelable = true,
                Work = (worker, evt) =>
                {
                    var data = evt.Argument as TableData;

                    var logic = new DataLogic(worker, _sourceClient, _targetClient);
                    var success = logic.Export(data, uiSettings, filePath, _mappings);
                    if (success)
                    {
                        _settings.LastDataFile = filePath;
                        MessageBox.Show("Records successfully exported", "Info", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    }
                },
                PostWorkCallBack = evt =>
                {
                    ManageWorkingState(false);
                    ReRenderComponents(true);
                    SendMessageToStatusBar?.Invoke(this, new StatusBarMessageEventArgs("Export complete"));
                },
                ProgressChanged = ReportWorkProgress
            });
        }

        private void LoadSettings()
        {
            _logger.Log(LogLevel.INFO, "Importing legacy table settings...");

            var path = this.SelectFile("Legacy table settings (*.settings.json)|*.settings.json|Json files (*.json)|*.json");
            if (string.IsNullOrEmpty(path)) { return; }

            var json = File.ReadAllText(path);
            var loadedSettings = json.DeserializeObject<TableSettings>();
            if (loadedSettings == null || string.IsNullOrWhiteSpace(loadedSettings.LogicalName))
            {
                throw new Exception("Invalid legacy settings file: missing table logical name.");
            }

            var selectedTable = GetSelectedTableOrDefault();
            if (selectedTable != null && !loadedSettings.LogicalName.Equals(selectedTable.LogicalName, StringComparison.OrdinalIgnoreCase))
            {
                throw new Exception($"This settings file is for table '{loadedSettings.LogicalName}', but the selected table is '{selectedTable.LogicalName}'.");
            }
            if (_dmtSettings?.Table != null && !loadedSettings.LogicalName.Equals(_dmtSettings.Table.LogicalName, StringComparison.OrdinalIgnoreCase))
            {
                throw new Exception($"This settings file is for table '{loadedSettings.LogicalName}', but the active settings file is for '{_dmtSettings.Table.LogicalName}'.");
            }

            var tableData = GetTableDataByLogicalName(loadedSettings.LogicalName, false);
            if (tableData == null)
            {
                SendMessageToStatusBar?.Invoke(this, new StatusBarMessageEventArgs("Import operation aborted"));
                return;
            }

            if (selectedTable == null)
            {
                _suppressTableSelectionChanged = true;
                try
                {
                    SetSelectedTableItem(tableData);
                }
                finally
                {
                    _suppressTableSelectionChanged = false;
                }
            }

            tableData.Settings.Filter = loadedSettings.Filter;
            tableData.Settings.DeselectedAttributes = loadedSettings.DeselectedAttributes ?? new List<string>();
            tableData.Settings.ExcelConfig = loadedSettings.ExcelConfig;
            _currentTableLogicalName = tableData.Table.LogicalName;
            _currentTableSettings = tableData.Settings;

            _suppressSettingsEvents = true;
            try
            {
                rtbFilter.Text = loadedSettings.Filter ?? string.Empty;
            }
            finally
            {
                _suppressSettingsEvents = false;
            }

            SettingsHelper.SetSettings(_settings);

            if (_dmtSettings == null || string.IsNullOrWhiteSpace(_dmtFilePath))
            {
                var filePath = this.SelectFile("DMT Settings (*.dmt.json)|*.dmt.json", save: true, defaultFileName: GetDefaultSaveFileName(".dmt.json", tableData.Table.LogicalName));
                if (string.IsNullOrWhiteSpace(filePath))
                {
                    SendMessageToStatusBar?.Invoke(this, new StatusBarMessageEventArgs("Legacy settings imported into the current session only"));
                    return;
                }

                _dmtFilePath = filePath;
                _dmtSettings = DmtFileService.CreateNew(
                    tableData.Table.LogicalName,
                    tableData.Table.DisplayName,
                    tableData.Table.IdAttribute,
                    tableData.Table.NameAttribute,
                    _sourceClient?.ConnectedOrgUniqueName,
                    _sourceClient?.ConnectedOrgFriendlyName);
            }

            CaptureDmtSettingsFromUi();
            AutoSaveDmtSettings();
            LoadAttributes();
            LoadFilters(tableData);

            var message = string.IsNullOrWhiteSpace(_dmtFilePath)
                ? $"Imported legacy table settings for '{tableData.Table.LogicalName}' into the current session"
                : $"Merged legacy table settings into '{Path.GetFileName(_dmtFilePath)}'";
            SendMessageToStatusBar?.Invoke(this, new StatusBarMessageEventArgs(message));
        }

        private void ExportToExcel()
        {
            _logger.Log(LogLevel.INFO, "Export to Excel operation...");
            AutoSaveDmtSettings();

            var tableData = GetSelectedTableItemData(false, true);
            if (tableData == null)
            {
                SendMessageToStatusBar?.Invoke(this, new StatusBarMessageEventArgs("Export to Excel aborted"));
                return;
            }

            tableData.SelectedAttributes = lvAttributes.CheckedItems
                .Cast<ListViewItem>()
                .Select(lvi => lvi.ToObject(new Models.Attribute()) as Models.Attribute)
                .Select(attr => tableData.Table.AllAttributes.FirstOrDefault(a => a.LogicalName == attr.LogicalName))
                .Where(a => a != null)
                .ToList();

            var selectedAttrMeta = tableData.SelectedAttributes
                .Select(a => tableData.Metadata.Attributes.FirstOrDefault(m => m.LogicalName == a.LogicalName))
                .Where(m => m != null)
                .ToList();

            var repo = new CrmRepo(_sourceClient);
            using (var dlg = new Forms.ExcelExportConfigDialog(selectedAttrMeta, tableData.Metadata, repo, tableData.Settings.ExcelConfig))
            {
                if (dlg.ShowDialog(ParentForm) != System.Windows.Forms.DialogResult.OK) return;
                var config = dlg.Config;

                // persist the config so next open pre-populates automatically
                tableData.Settings.ExcelConfig = config;
                SettingsHelper.SetSettings(_settings);
                AutoSaveDmtSettings();

                var filePath = this.SelectFile("Excel files (*.xlsx)|*.xlsx", save: true,
                    defaultFileName: GetDefaultSaveFileName(".xlsx", tableData.Table.LogicalName));
                if (string.IsNullOrEmpty(filePath)) return;

                ManageWorkingState(true);

                var uiSettings = ReadSettings(Enums.Action.None);
                tableData.SelectedAttributes = lvAttributes.CheckedItems
                    .Cast<ListViewItem>()
                    .Select(lvi => lvi.ToObject(new Models.Attribute()) as Models.Attribute)
                    .Select(attr => tableData.Table.AllAttributes.FirstOrDefault(a => a.LogicalName == attr.LogicalName))
                    .ToList();

                WorkAsync(new WorkAsyncInfo
                {
                    Message = "Exporting to Excel...",
                    AsyncArgument = new { config, tableData, uiSettings, filePath },
                    IsCancelable = false,
                    Work = (worker, evt) =>
                    {
                        dynamic args = evt.Argument;
                        ExcelExportConfig cfg = args.config;
                        TableData td = args.tableData;
                        UiSettings ui = args.uiSettings;
                        string path = args.filePath;

                        worker.ReportProgress(0, $"Excel export: retrieving source {td.Table.LogicalName} records...");
                        var logic = new DataLogic(worker, _sourceClient, _targetClient);
                        var sourceCollection = logic.GetSourceEntities(td, ui);

                        worker.ReportProgress(0, $"Excel export: writing {sourceCollection.Count()} records to workbook...");
                        var excelLogic = new Logic.ExcelLogic();
                        excelLogic.Export(cfg, sourceCollection, path, _sourceClient);
                        worker.ReportProgress(0, $"Excel export: saved workbook to {path}");
                    },
                    PostWorkCallBack = evt =>
                    {
                        ManageWorkingState(false);
                        if (evt.Error != null)
                        {
                            _logger.Log(LogLevel.ERROR, evt.Error.ToString());
                            MessageBox.Show(evt.Error.Message, "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                        }
                        else
                        {
                            _settings.LastDataFile = filePath;
                            SettingsHelper.SetSettings(_settings);
                            MessageBox.Show("Records successfully exported to Excel", "Info", MessageBoxButtons.OK, MessageBoxIcon.Information);
                            SendMessageToStatusBar?.Invoke(this, new StatusBarMessageEventArgs("Excel export complete"));
                        }
                        ReRenderComponents(true);
                    },
                    ProgressChanged = ReportWorkProgress
                });
            }
        }

        private void ImportFromExcel(string path = null)
        {
            _logger.Log(LogLevel.INFO, "Import from Excel operation...");
            AutoSaveDmtSettings();

            if (_working)
            {
                SendMessageToStatusBar?.Invoke(this, new StatusBarMessageEventArgs("Another operation is already running"));
                return;
            }

            if (_targetClient == null)
            {
                MessageBox.Show("A target connection is required for Excel import.", "Info", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            path = path ?? this.SelectFile("Excel files (*.xlsx)|*.xlsx");
            if (string.IsNullOrEmpty(path)) return;

            var preflightLogic = new Logic.ExcelLogic();
            var rowCount = preflightLogic.GetImportRowCount(path, out ExcelExportConfig preflightConfig);
            ValidateActiveSettingsTable(preflightConfig?.Table?.LogicalName, "Excel file");
            if (!ConfirmLargeExcelImport(rowCount)) return;

            var operationId = BeginExcelImportOperation();
            ManageWorkingState(true);

            WorkAsync(new WorkAsyncInfo
            {
                Message = "Reading Excel file...",
                AsyncArgument = new { path, operationId },
                IsCancelable = true,
                Work = (worker, evt) =>
                {
                    try
                    {
                        dynamic args = evt.Argument;
                        string filePath = args.path;
                        worker.ReportProgress(0, "Excel import: reading workbook metadata...");
                        ThrowIfCancelled(worker);
                        var excelLogic = new Logic.ExcelLogic();

                        var collection = excelLogic.ImportFromExcel(
                            filePath,
                            out ExcelExportConfig config,
                            _targetClient,
                            worker,
                            importConfig =>
                            {
                                ThrowIfCancelled(worker);
                                ValidateActiveSettingsTable(importConfig?.Table?.LogicalName, "Excel file");
                            });
                        ThrowIfCancelled(worker);
                        worker.ReportProgress(0, $"Excel import: read {collection?.Count ?? 0} records with {collection?.ImportErrors?.Count ?? 0} warning(s).");

                        evt.Result = new ExcelImportSession { FilePath = filePath, SourceType = "Excel", Config = config, Collection = collection, OperationId = args.operationId };
                    }
                    catch (OperationCanceledException)
                    {
                        evt.Cancel = true;
                    }
                },
                PostWorkCallBack = evt =>
                {
                    ManageWorkingState(false);
                    if (ShouldIgnoreExcelImportCallback(evt, operationId, "Excel import read cancelled")) return;

                    if (evt.Error != null)
                    {
                        _logger.Log(LogLevel.ERROR, evt.Error.ToString());
                        MessageBox.Show(evt.Error.Message, "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                        return;
                    }

                    var session = evt.Result as ExcelImportSession;
                    var collection = session?.Collection;
                    if (collection == null || collection.Count == 0)
                    {
                        var message = "No records found in the Excel file.";
                        if (collection?.ImportErrors?.Any() == true)
                        {
                            var errors = string.Join(Environment.NewLine, collection.ImportErrors.Take(10));
                            var suffix = collection.ImportErrors.Count > 10 ? $"{Environment.NewLine}..." : string.Empty;
                            message = $"{message}{Environment.NewLine}{Environment.NewLine}Import errors:{Environment.NewLine}{errors}{suffix}";
                        }
                        MessageBox.Show(message, "Info", MessageBoxButtons.OK, MessageBoxIcon.Information);
                        return;
                    }

                    var tableData = GetTableDataByLogicalName(collection.LogicalName, false);
                    if (tableData == null)
                    {
                        MessageBox.Show($"Table '{collection.LogicalName}' not found. Please load tables first.", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                        return;
                    }

                    var previewSettings = GetDefaultImportSettings(Enums.Action.None);
                    StartExcelImportPreview(session, tableData, previewSettings);
                },
                ProgressChanged = ReportWorkProgress
            });
        }

        private void StartExcelImportPreview(ExcelImportSession session, TableData tableData, UiSettings uiSettings)
        {
            if (session != null && session.OperationId > 0 && !IsCurrentExcelImportOperation(session.OperationId)) return;
            ManageWorkingState(true);
            WorkAsync(new WorkAsyncInfo
            {
                Message = "Preparing import preview...",
                AsyncArgument = new { session, tableData, uiSettings },
                IsCancelable = true,
                Work = (worker, evt) =>
                {
                    try
                    {
                        dynamic args = evt.Argument;
                        ThrowIfCancelled(worker);
                        worker.ReportProgress(0, "Import preview: comparing source rows with target records...");
                        evt.Result = BuildExcelImportPreview(args.tableData, args.session.Collection, args.session.Config, args.uiSettings, args.session.FilePath);
                        ThrowIfCancelled(worker);
                    }
                    catch (OperationCanceledException)
                    {
                        evt.Cancel = true;
                    }
                },
                PostWorkCallBack = evt =>
                {
                    ManageWorkingState(false);
                    if ((session?.OperationId ?? 0) > 0 && ShouldIgnoreExcelImportCallback(evt, session.OperationId, "Import preview cancelled")) return;
                    if (evt.Error != null)
                    {
                        _logger.Log(LogLevel.ERROR, evt.Error.ToString());
                        MessageBox.Show(evt.Error.Message, "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                        return;
                    }

                    var preview = evt.Result as ExcelImportPreview;
                    if (preview == null) return;

                    if (_importPreviewDialogOpen) return;
                    _importPreviewDialogOpen = true;
                    using (var dlg = new ExcelImportPreviewDialog(preview))
                    {
                        var result = DialogResult.Cancel;
                        try
                        {
                            result = dlg.ShowDialog(ParentForm);
                        }
                        finally
                        {
                            _importPreviewDialogOpen = false;
                        }
                        if (result == DialogResult.Retry)
                        {
                            SaveDmtImportSettings(dlg.Settings, dlg.SelectedMatchKey);
                            if (session.Config == null || !dlg.MatchKeyChanged)
                            {
                                if (session.Config == null) ApplyJsonImportMatchKeySelection(session.Collection, tableData, dlg.SelectedMatchKey);
                                StartExcelImportPreview(session, tableData, dlg.Settings);
                            }
                            else
                                ReloadExcelImportSession(session.FilePath, dlg.SelectedMatchKey, tableData, dlg.Settings);
                            return;
                        }
                        if (result != DialogResult.OK) return;

                        SaveDmtImportSettings(dlg.Settings, dlg.SelectedMatchKey);
                        StartExcelImport(session.Collection, tableData, dlg.Settings);
                    }
                },
                ProgressChanged = ReportWorkProgress
            });
        }

        private bool ConfirmLargeExcelImport(int rowCount)
        {
            if (rowCount < ExcelImportLargeRowWarning) return true;

            string message;
            MessageBoxIcon icon;
            if (rowCount >= ExcelImportHugeRowWarning)
            {
                icon = MessageBoxIcon.Warning;
                message = $"This Excel file contains {rowCount:N0} data rows.{Environment.NewLine}{Environment.NewLine}"
                    + "This is a very large import and can take a long time to preview and import. Consider splitting the file or using JSON import for this volume."
                    + $"{Environment.NewLine}{Environment.NewLine}Continue?";
            }
            else if (rowCount >= ExcelImportVeryLargeRowWarning)
            {
                icon = MessageBoxIcon.Warning;
                message = $"This Excel file contains {rowCount:N0} data rows.{Environment.NewLine}{Environment.NewLine}"
                    + "Preview and import may take a while, especially when lookup or match-key resolution is enabled. A smaller batch size is recommended for tables with plugins."
                    + $"{Environment.NewLine}{Environment.NewLine}Continue?";
            }
            else
            {
                icon = MessageBoxIcon.Information;
                message = $"This Excel file contains {rowCount:N0} data rows.{Environment.NewLine}{Environment.NewLine}"
                    + "Preview and import may take some time."
                    + $"{Environment.NewLine}{Environment.NewLine}Continue?";
            }

            return MessageBox.Show(message, "Large Excel Import", MessageBoxButtons.YesNo, icon) == DialogResult.Yes;
        }

        private void ReloadExcelImportSession(string filePath, ExcelImportMatchKeySelection matchKey, TableData tableData, UiSettings uiSettings)
        {
            var operationId = BeginExcelImportOperation();
            ManageWorkingState(true);
            WorkAsync(new WorkAsyncInfo
            {
                Message = "Reading Excel file...",
                AsyncArgument = new { filePath, matchKey, operationId },
                IsCancelable = true,
                Work = (worker, evt) =>
                {
                    try
                    {
                        dynamic args = evt.Argument;
                        string reloadFilePath = args.filePath;
                        var reloadOperationId = (int)args.operationId;
                        var reloadMatchKey = args.matchKey as ExcelImportMatchKeySelection;
                        worker.ReportProgress(0, "Excel import: re-reading workbook metadata...");
                        ThrowIfCancelled(worker);
                        var excelLogic = new Logic.ExcelLogic();
                        var collection = excelLogic.ImportFromExcel(
                            reloadFilePath,
                            out ExcelExportConfig config,
                            _targetClient,
                            worker,
                            importConfig =>
                            {
                                ThrowIfCancelled(worker);
                                ValidateActiveSettingsTable(importConfig?.Table?.LogicalName, "Excel file");
                                worker.ReportProgress(0, "Excel import: applying selected match key...");
                                ApplyImportMatchKeySelection(importConfig, reloadMatchKey);
                                ThrowIfCancelled(worker);
                                worker.ReportProgress(0, $"Excel import: resolving rows using {importConfig.MatchKeyMode} match key...");
                            });
                        ThrowIfCancelled(worker);
                        worker.ReportProgress(0, $"Excel import: read {collection?.Count ?? 0} records with {collection?.ImportErrors?.Count ?? 0} warning(s).");
                        evt.Result = new ExcelImportSession { FilePath = reloadFilePath, SourceType = "Excel", Config = config, Collection = collection, OperationId = reloadOperationId };
                    }
                    catch (OperationCanceledException)
                    {
                        evt.Cancel = true;
                    }
                },
                PostWorkCallBack = evt =>
                {
                    ManageWorkingState(false);
                    if (ShouldIgnoreExcelImportCallback(evt, operationId, "Excel import read cancelled")) return;
                    if (evt.Error != null)
                    {
                        _logger.Log(LogLevel.ERROR, evt.Error.ToString());
                        MessageBox.Show(evt.Error.Message, "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                        return;
                    }

                    var session = evt.Result as ExcelImportSession;
                    if (session != null) StartExcelImportPreview(session, tableData, uiSettings);
                },
                ProgressChanged = ReportWorkProgress
            });
        }

        private void StartExcelImport(RecordCollection collection, TableData tableData, UiSettings uiSettings)
        {
            ManageWorkingState(true);
            WorkAsync(new WorkAsyncInfo
            {
                Message = "Importing records...",
                AsyncArgument = new { collection, tableData, uiSettings },
                IsCancelable = false,
                Work = (worker, evt) =>
                {
                    dynamic args = evt.Argument;
                    var logic = new DataLogic(worker, _sourceClient, _targetClient);
                    var mappings = BuildMappingsForImport(args.uiSettings);
                    evt.Result = logic.Import(args.tableData, args.collection, args.uiSettings, mappings, false);
                },
                PostWorkCallBack = evt =>
                {
                    ManageWorkingState(false);
                    if (evt.Error != null)
                    {
                        _logger.Log(LogLevel.ERROR, evt.Error.ToString());
                        MessageBox.Show(evt.Error.Message, "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                        return;
                    }

                    var result = evt.Result as Models.OperationResult;
                    if (result != null)
                    {
                        var resDialog = new Forms.Results(result.Items, _settings, allowRetryFailed: true);
                        var dialogResult = resDialog.ShowDialog(ParentForm);
                        SettingsHelper.SetSettings(_settings);

                        if (dialogResult == DialogResult.Retry && resDialog.FailedRecordIds.Any())
                        {
                            var retryCollection = FilterRecordCollection(collection, resDialog.FailedRecordIds);
                            if (retryCollection.Count > 0)
                            {
                                SendMessageToStatusBar?.Invoke(this, new StatusBarMessageEventArgs($"Retrying {retryCollection.Count} failed record(s)..."));
                                StartExcelImport(retryCollection, tableData, uiSettings);
                                return;
                            }
                        }
                    }

                    SendMessageToStatusBar?.Invoke(this, new StatusBarMessageEventArgs("Excel import complete"));
                    ReRenderComponents(true);
                },
                ProgressChanged = ReportWorkProgress
            });
        }

        private List<Mapping> BuildMappingsForImport(UiSettings uiSettings)
        {
            var mappings = new List<Mapping>(_mappings ?? new List<Mapping>());
            if (uiSettings.MapUsers || uiSettings.MapTeams || uiSettings.MapBu)
            {
                var mappingsLogic = new MappingsLogic(_sourceClient, _targetClient);
                if (uiSettings.MapUsers)
                    mappings.AddRange(mappingsLogic.GetUserMappings(_sourceInstance.FriendlyName, _targetInstance.FriendlyName));
                if (uiSettings.MapTeams)
                    mappings.AddRange(mappingsLogic.GetTeamMappings(_sourceInstance.FriendlyName, _targetInstance.FriendlyName));
                if (uiSettings.MapBu)
                {
                    var buMapping = mappingsLogic.GetBusinessUnitMapping(_sourceInstance.FriendlyName, _targetInstance.FriendlyName);
                    if (buMapping != null) mappings.Add(buMapping);
                }
            }
            return mappings;
        }

        private RecordCollection FilterRecordCollection(RecordCollection collection, IEnumerable<Guid> ids)
        {
            var idSet = new HashSet<Guid>(ids);
            var records = (collection.Records ?? Enumerable.Empty<Record>())
                .Where(record => TryGetRecordId(record, collection.PrimaryIdAttribute, out var id) && idSet.Contains(id))
                .ToList();

            return new RecordCollection
            {
                LogicalName = collection.LogicalName,
                PrimaryIdAttribute = collection.PrimaryIdAttribute,
                Records = records,
                Count = records.Count,
                ImportErrors = collection.ImportErrors,
                ImportMatchKey = collection.ImportMatchKey,
                ImportMatchKeys = collection.ImportMatchKeys,
                ImportMatchKeyMode = collection.ImportMatchKeyMode
            };
        }

        private bool TryGetRecordId(Record record, string primaryIdAttribute, out Guid id)
        {
            id = Guid.Empty;
            var attr = record?.Attributes?.FirstOrDefault(a => a.Key.Equals(primaryIdAttribute, StringComparison.OrdinalIgnoreCase));
            if (attr?.Value == null) return false;
            if (attr.Value is Guid guid)
            {
                id = guid;
                return true;
            }
            return Guid.TryParse(attr.Value.ToString(), out id);
        }

        private int BeginExcelImportOperation()
        {
            _activeExcelImportOperationId++;
            _importPreviewDialogOpen = false;
            return _activeExcelImportOperationId;
        }

        private bool IsCurrentExcelImportOperation(int operationId)
        {
            return operationId > 0 && operationId == _activeExcelImportOperationId;
        }

        private bool ShouldIgnoreExcelImportCallback(RunWorkerCompletedEventArgs evt, int operationId, string cancelledMessage)
        {
            if (!IsCurrentExcelImportOperation(operationId)) return true;
            if (!evt.Cancelled) return false;

            _activeExcelImportOperationId++;
            _logger.Log(LogLevel.INFO, cancelledMessage);
            SendMessageToStatusBar?.Invoke(this, new StatusBarMessageEventArgs(cancelledMessage));
            return true;
        }

        private void ThrowIfCancelled(BackgroundWorker worker)
        {
            if (worker != null && worker.CancellationPending)
                throw new OperationCanceledException();
        }

        private ExcelImportPreview BuildExcelImportPreview(TableData tableData, RecordCollection collection, ExcelExportConfig config, UiSettings uiSettings, string filePath)
        {
            var sourceCollection = collection.ToEntityCollection(tableData.Metadata.Attributes);
            var sourceRecords = collection.Records?.ToList() ?? new List<Record>();
            var warningsByRow = GroupImportWarningsByRow(collection.ImportErrors);
            var previewRows = new HashSet<int>();
            var targetIds = GetExistingTargetIds(tableData.Table.LogicalName, tableData.Table.IdAttribute, sourceCollection.Entities.Select(e => e.Id), uiSettings.BatchSize);
            var nameAttribute = tableData.Table.NameAttribute;
            var availableMatchKeys = GetAvailableImportMatchKeys(collection, config);

            var preview = new ExcelImportPreview
            {
                FilePath = filePath,
                SourceType = config == null ? "JSON" : "Excel",
                TableLogicalName = collection.LogicalName,
                TargetName = _targetInstance?.FriendlyName ?? string.Empty,
                MatchKey = collection.ImportMatchKey,
                MatchKeyMode = collection.ImportMatchKeyMode,
                MatchKeys = collection.ImportMatchKeys ?? new List<string>(),
                MatchAlternateKeyName = config?.MatchAlternateKeyName,
                TotalRows = collection.Count,
                ImportErrors = collection.ImportErrors ?? new List<string>(),
                Settings = uiSettings,
                MappingCount = BuildMappingsForImport(uiSettings).Count,
                AvailableMatchKeys = availableMatchKeys,
                AvailableAlternateKeys = GetAvailableImportAlternateKeys(tableData, availableMatchKeys)
            };

            var entityIndex = 0;
            foreach (var entity in sourceCollection.Entities)
            {
                var sourceRecord = entityIndex < sourceRecords.Count ? sourceRecords[entityIndex] : null;
                var rowNumber = sourceRecord != null && sourceRecord.SourceRowNumber > 0 ? sourceRecord.SourceRowNumber : entityIndex + 1;
                previewRows.Add(rowNumber);
                var rowWarnings = warningsByRow.TryGetValue(rowNumber, out var warnings)
                    ? string.Join(" | ", warnings)
                    : string.Empty;
                var exists = targetIds.Contains(entity.Id);
                var action = exists ? Enums.Action.Update : Enums.Action.Create;
                var enabled = (uiSettings.Action & action) == action;
                var actionText = enabled ? action.ToString() : "Skip";
                var description = enabled
                    ? (exists ? "Target record found" : "Target record not found")
                    : $"{action} is not enabled in operation settings";
                if (!string.IsNullOrWhiteSpace(rowWarnings))
                    description = $"{description}; Warning: {rowWarnings}";
                var name = !string.IsNullOrWhiteSpace(nameAttribute) && entity.Attributes.Contains(nameAttribute)
                    ? entity[nameAttribute]?.ToString() ?? string.Empty
                    : string.Empty;

                if (actionText == "Create") preview.CreateCount++;
                else if (actionText == "Update") preview.UpdateCount++;
                else preview.SkippedCount++;

                preview.Items.Add(new ExcelImportPreviewItem
                {
                    RowNumber = rowNumber,
                    Action = actionText,
                    RecordId = entity.Id.ToString("D"),
                    MatchValue = GetPreviewMatchValue(entity, collection.ImportMatchKeys),
                    Name = name,
                    Description = description,
                    Warnings = rowWarnings
                });
                entityIndex++;
            }

            foreach (var rowWarning in warningsByRow.Where(w => !previewRows.Contains(w.Key)).OrderBy(w => w.Key))
            {
                preview.SkippedCount++;
                preview.Items.Add(new ExcelImportPreviewItem
                {
                    RowNumber = rowWarning.Key,
                    Action = "Skip",
                    RecordId = string.Empty,
                    MatchValue = string.Empty,
                    Name = string.Empty,
                    Description = "Skipped while reading source file",
                    Warnings = string.Join(" | ", rowWarning.Value)
                });
            }

            preview.Items = preview.Items.OrderBy(i => i.RowNumber).ToList();
            preview.TotalRows = preview.Items.Count;
            return preview;
        }

        private Dictionary<int, List<string>> GroupImportWarningsByRow(IEnumerable<string> importErrors)
        {
            var result = new Dictionary<int, List<string>>();
            if (importErrors == null) return result;

            foreach (var error in importErrors.Where(e => !string.IsNullOrWhiteSpace(e)))
            {
                var rowNumber = GetWarningRowNumber(error);
                if (!rowNumber.HasValue) continue;

                if (!result.ContainsKey(rowNumber.Value))
                    result[rowNumber.Value] = new List<string>();

                result[rowNumber.Value].Add(error);
            }

            return result;
        }

        private int? GetWarningRowNumber(string warning)
        {
            var match = Regex.Match(warning ?? string.Empty, @"^Row\s+(?<row>\d+)", RegexOptions.IgnoreCase);
            if (!match.Success) return null;
            return int.TryParse(match.Groups["row"].Value, out var row) ? row : (int?)null;
        }

        private string GetPreviewMatchValue(Entity entity, List<string> matchKeys)
        {
            if (matchKeys == null || !matchKeys.Any()) return entity.Id.ToString("D");
            return string.Join(", ", matchKeys.Select(key =>
            {
                var value = entity.Attributes.Contains(key) && entity[key] != null ? FormatPreviewMatchValue(entity[key]) : string.Empty;
                return $"{key}={value}";
            }));
        }

        private string FormatPreviewMatchValue(object value)
        {
            if (value == null) return string.Empty;
            if (value is OptionSetValue option) return option.Value.ToString();
            if (value is OptionSetValueCollection options) return string.Join(", ", options.Select(o => o.Value));
            if (value is Money money) return money.Value.ToString();
            if (value is EntityReference reference) return reference.Id.ToString("D");
            return value.ToString();
        }

        private void ApplyImportMatchKeySelection(ExcelExportConfig config, ExcelImportMatchKeySelection selection)
        {
            if (config == null || selection == null) return;

            var fields = selection.Fields?
                .Where(f => !string.IsNullOrWhiteSpace(f))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList() ?? new List<string>();

            config.MatchKeyMode = fields.Any() ? selection.Mode : "Guid";
            config.MatchKeys = fields;
            config.MatchKey = fields.Count == 1 ? fields[0] : null;
            config.MatchAlternateKeyName = string.Equals(config.MatchKeyMode, "AlternateKey", StringComparison.OrdinalIgnoreCase)
                ? selection.AlternateKeyName
                : null;
        }

        private void ApplyJsonImportMatchKeySelection(RecordCollection collection, TableData tableData, ExcelImportMatchKeySelection selection)
        {
            if (collection == null || tableData == null || selection == null) return;

            var fields = selection.Fields?
                .Where(f => !string.IsNullOrWhiteSpace(f))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList() ?? new List<string>();
            var mode = string.IsNullOrWhiteSpace(selection.Mode) || !fields.Any()
                ? "Guid"
                : selection.Mode;

            collection.ImportMatchKeyMode = mode;
            collection.ImportMatchKeys = string.Equals(mode, "Guid", StringComparison.OrdinalIgnoreCase)
                ? new List<string>()
                : fields;
            collection.ImportMatchKey = GetImportMatchKeyDisplay(mode, fields, selection.AlternateKeyName);

            if (string.Equals(mode, "Guid", StringComparison.OrdinalIgnoreCase)) return;

            var targetRepo = new CrmRepo(_targetClient);
            var records = collection.Records?.ToList() ?? new List<Record>();
            var warnings = collection.ImportErrors ?? new List<string>();
            var rowIndex = 1;

            foreach (var record in records)
            {
                var attributes = record.Attributes?.ToList() ?? new List<RecordAttribute>();
                var rowNumber = record.SourceRowNumber > 0 ? record.SourceRowNumber : rowIndex;
                var keyValues = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
                var hasBlankKey = false;

                foreach (var field in fields)
                {
                    var attr = attributes.FirstOrDefault(a => a.Key.Equals(field, StringComparison.OrdinalIgnoreCase));
                    if (attr == null || IsImportValueBlank(attr.Value))
                    {
                        warnings.Add($"Row {rowNumber}, field '{field}': match key is blank.");
                        hasBlankKey = true;
                        continue;
                    }

                    keyValues[field] = ToImportQueryValue(attr.Value);
                }

                if (!hasBlankKey && keyValues.Any())
                {
                    var target = targetRepo.FindByFieldValues(tableData.Table.LogicalName, keyValues);
                    if (target != null)
                        SetRecordPrimaryId(collection.PrimaryIdAttribute, target.Id, attributes);
                    else
                        EnsureRecordPrimaryId(collection.PrimaryIdAttribute, attributes);
                }

                record.Attributes = attributes;
                rowIndex++;
            }

            collection.Records = records;
            collection.Count = records.Count;
            collection.ImportErrors = warnings;
        }

        private string GetImportMatchKeyDisplay(string mode, List<string> fields, string alternateKeyName)
        {
            if (string.Equals(mode, "Guid", StringComparison.OrdinalIgnoreCase)) return null;
            if (string.Equals(mode, "AlternateKey", StringComparison.OrdinalIgnoreCase) && !string.IsNullOrWhiteSpace(alternateKeyName))
                return $"{alternateKeyName} ({string.Join(", ", fields)})";
            return fields?.Any() == true ? string.Join(", ", fields) : null;
        }

        private bool IsImportValueBlank(object value)
        {
            if (value == null) return true;
            if (value is JValue jValue) return jValue.Value == null || string.IsNullOrWhiteSpace(jValue.Value.ToString());
            return string.IsNullOrWhiteSpace(value.ToString());
        }

        private object ToImportQueryValue(object value)
        {
            if (value == null) return null;
            if (value is OptionSetValue option) return option.Value;
            if (value is Money money) return money.Value;
            if (value is EntityReference reference) return reference.Id;
            if (value is JValue jValue) return jValue.Value;
            if (value is JObject obj)
            {
                if (obj.ContainsKey("Value")) return ToImportQueryValue(obj["Value"]);
                if (obj.ContainsKey("Id") && Guid.TryParse(obj["Id"]?.ToString(), out var id)) return id;
            }
            return value;
        }

        private void EnsureRecordPrimaryId(string primaryIdAttribute, List<RecordAttribute> attributes)
        {
            var existing = attributes.FirstOrDefault(a => a.Key.Equals(primaryIdAttribute, StringComparison.OrdinalIgnoreCase));
            if (existing != null && !IsImportValueBlank(existing.Value)) return;

            SetRecordPrimaryId(primaryIdAttribute, Guid.NewGuid(), attributes);
        }

        private void SetRecordPrimaryId(string primaryIdAttribute, Guid id, List<RecordAttribute> attributes)
        {
            var existing = attributes.FirstOrDefault(a => a.Key.Equals(primaryIdAttribute, StringComparison.OrdinalIgnoreCase));
            if (existing == null)
            {
                attributes.Add(new RecordAttribute { Key = primaryIdAttribute, Type = Enums.AttributeType.Identifier, Value = id });
                return;
            }

            existing.Type = Enums.AttributeType.Identifier;
            existing.Value = id;
        }

        private void SaveDmtImportSettings(UiSettings uiSettings, ExcelImportMatchKeySelection selection)
        {
            if (_dmtSettings == null) return;

            _dmtSettings.ImportSettings = new DmtImportSettings
            {
                BatchSize = uiSettings != null && uiSettings.BatchSize > 0 ? Math.Min(uiSettings.BatchSize, 25) : 25,
                MatchKeyMode = selection?.Mode ?? "Guid",
                MatchKeyFields = selection?.Fields?.ToList() ?? new List<string>(),
                MatchAlternateKeyName = selection?.AlternateKeyName
            };

            AutoSaveDmtSettings();
        }

        private List<string> GetAvailableImportMatchKeys(RecordCollection collection, ExcelExportConfig config)
        {
            if (config?.Columns != null)
            {
                return config.Columns
                    .Where(c => c.Type != "Lookup" && c.Type != "LookupKeyField" && c.LogicalName != config.Table.PrimaryIdAttribute)
                    .Select(c => c.LogicalName)
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .OrderBy(c => c)
                    .ToList();
            }

            return (collection?.Records ?? Enumerable.Empty<Record>())
                .SelectMany(record => record.Attributes ?? Enumerable.Empty<RecordAttribute>())
                .Select(attr => attr.Key)
                .Where(key => !string.IsNullOrWhiteSpace(key) && !key.Equals(collection.PrimaryIdAttribute, StringComparison.OrdinalIgnoreCase))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(key => key)
                .ToList();
        }

        private List<ExcelImportAlternateKeyOption> GetAvailableImportAlternateKeys(TableData tableData, IEnumerable<string> availableFields)
        {
            var availableColumns = new HashSet<string>(availableFields ?? Enumerable.Empty<string>(), StringComparer.OrdinalIgnoreCase);

            return tableData.Metadata.Keys?
                .Where(key => key.KeyAttributes?.Any() == true)
                .Select(key => new ExcelImportAlternateKeyOption
                {
                    Name = key.LogicalName,
                    Fields = key.KeyAttributes.ToList()
                })
                .Where(key => key.Fields.All(availableColumns.Contains))
                .OrderBy(key => key.Name)
                .ToList() ?? new List<ExcelImportAlternateKeyOption>();
        }

        private HashSet<Guid> GetExistingTargetIds(string logicalName, string idAttribute, IEnumerable<Guid> sourceIds, int batchSize)
        {
            var existing = new HashSet<Guid>();
            var ids = sourceIds.Distinct().ToList();
            var repo = new CrmRepo(_targetClient);

            foreach (var batch in ids.Select((id, index) => new { id, index }).GroupBy(x => x.index / Math.Max(batchSize, 1)).Select(g => g.Select(x => x.id).ToArray()))
            {
                if (!batch.Any()) continue;

                var query = new QueryExpression(logicalName)
                {
                    ColumnSet = new ColumnSet(idAttribute),
                    Criteria = new FilterExpression(LogicalOperator.And)
                };
                query.Criteria.Conditions.Add(new ConditionExpression(idAttribute, ConditionOperator.In, batch.Cast<object>().ToArray()));

                var targetCollection = repo.GetCollectionByExpression(query, Math.Max(batchSize, 1));
                foreach (var entity in targetCollection.Entities)
                    existing.Add(entity.Id);
            }

            return existing;
        }

        private class ExcelImportSession
        {
            public string FilePath { get; set; }
            public string SourceType { get; set; }
            public ExcelExportConfig Config { get; set; }
            public RecordCollection Collection { get; set; }
            public int OperationId { get; set; }
        }

        private void Import(string path = null)
        {
            _logger.Log(LogLevel.INFO, $"Import operation...");
            AutoSaveDmtSettings();

            // get file path
            path = path is null ? this.SelectFile("Json files (*.json)|*.json") : path;
            if (string.IsNullOrEmpty(path)) { return; }

            var json = File.ReadAllText(path);
            var importData = json.DeserializeObject<RecordCollection>();
            ImportFileDataChecks(importData);
            ValidateActiveSettingsTable(importData.LogicalName, "JSON file");

            var tableData = GetTableDataByLogicalName(importData.LogicalName);
            if (tableData == null)
            {
                SendMessageToStatusBar?.Invoke(this, new StatusBarMessageEventArgs("Import operation aborted"));
                return;
            }

            var session = new ExcelImportSession { FilePath = path, SourceType = "JSON", Collection = importData };
            StartExcelImportPreview(session, tableData, GetDefaultImportSettings(Enums.Action.None));
        }

        private void LoadUiSettings()
        {
            var uiSettings = _settings.UiSettings;
            if (uiSettings != null)
            {
                if(_ready)
                {
                    cbMapUsers.Checked = uiSettings.MapUsers;
                    cbMapTeams.Checked = uiSettings.MapTeams;
                    cbMapBu.Checked = uiSettings.MapBu;
                    rbMapOnExport.Checked = uiSettings.ApplyMappingsOn.Equals(Operation.Export);
                    rbMapOnImport.Checked = uiSettings.ApplyMappingsOn.Equals(Operation.Import);
                    cbCreate.Checked = (uiSettings.Action & Enums.Action.Create) == Enums.Action.Create;
                    cbUpdate.Checked = (uiSettings.Action & Enums.Action.Update) == Enums.Action.Update;
                    cbDelete.Checked = (uiSettings.Action & Enums.Action.Delete) == Enums.Action.Delete;
                }

                _sorts = _settings.Sorts;

                nudBatchCount.Value = uiSettings.BatchSize;
                cbHideInvalid.Checked = uiSettings.HideInvalidAttributes;
            }
        }

        private void ClearMappings(bool fullReset = false)
        {
            if(fullReset)
            {
                // reset all mappings
                _mappings = new List<Mapping>();
                _sourceInstance.Mappings = _mappings;
                SettingsHelper.SetSettings(_settings);
                SendMessageToStatusBar?.Invoke(this, new StatusBarMessageEventArgs("All Mappings were reset"));
            }
            else
            {
                // clear previously generated auto mappings
                _mappings.RemoveAll(map => map.State.Equals(MappingState.Auto));
                _sourceInstance.Mappings = _mappings;
                SettingsHelper.SetSettings(_settings);
                SendMessageToStatusBar?.Invoke(this, new StatusBarMessageEventArgs("Cleared previously generated Automatic Mappings"));
            }
        }

        private void GenerateMappings()
        {
            _logger.Log(LogLevel.INFO, $"Generating automatic mappings...");

            ManageWorkingState(true);

            var uiSettings = ReadSettings(Enums.Action.None);

            WorkAsync(new WorkAsyncInfo
            {
                Message = "Generating automatic mappings...",
                IsCancelable = true,
                Work = (worker, evt) =>
                {
                    ClearMappings();

                    var mappingsLogic = new MappingsLogic(_sourceClient, _targetClient);

                    if (uiSettings.MapUsers)
                    {
                        var usrMappings = mappingsLogic.GetUserMappings(_sourceInstance.FriendlyName, _targetInstance.FriendlyName);
                        if (usrMappings.Any()) { _mappings.AddRange(usrMappings); }
                    }
                    if (uiSettings.MapTeams)
                    {
                        var teamMappings = mappingsLogic.GetTeamMappings(_sourceInstance.FriendlyName, _targetInstance.FriendlyName);
                        if (teamMappings.Any()) { _mappings.AddRange(teamMappings); }
                    }
                    if (uiSettings.MapBu)
                    {
                        var buMapping = mappingsLogic.GetBusinessUnitMapping(_sourceInstance.FriendlyName, _targetInstance.FriendlyName);
                        if (buMapping != null) { _mappings.Add(buMapping); }
                    }
                },
                PostWorkCallBack = evt =>
                {
                    ManageWorkingState(false);

                    SettingsHelper.SetSettings(_settings);
                    AutoSaveDmtSettings();
                    SendMessageToStatusBar?.Invoke(this, new StatusBarMessageEventArgs("Automatic Mappings generated successfully"));
                }
            });
        }

        private TableData GetSelectedTableItemData(bool targetRequired = true, bool attributeRequired = false)
        {
            _logger.Log(LogLevel.INFO, $"Parsing table data...");

            if (_sourceClient == null || (targetRequired && _targetClient == null))
            {
                throw new Exception("You must select both a source and a target organization");
            }
            if (targetRequired && !(cbCreate.Checked || cbUpdate.Checked || cbDelete.Checked))
            {
                throw new Exception("You must select at least one setting for transporting the data");
            }
            if (lvTables.SelectedItems.Count == 0)
            {
                throw new Exception("You must select a table first");
            }
            if (attributeRequired && lvAttributes.CheckedItems.Count == 0)
            {
                throw new Exception("At least one attribute must be selected");
            }

            var tableItem = lvTables.SelectedItems[0].ToObject(new Table()) as Table;
            if (tableItem == null || string.IsNullOrEmpty(tableItem.LogicalName) || !_tables.Any(tbl => tbl.LogicalName.Equals(tableItem.LogicalName)))
            {
                throw new Exception("Invalid Table: Please reload tables and try again");
            }

            var table = _tables.FirstOrDefault(tbl => tbl.LogicalName.Equals(tableItem.LogicalName));

            var repo = new CrmRepo(_sourceClient);
            var metadata = repo.GetTableMetadata(table.LogicalName);

            var tableSettings = _currentTableSettings != null
                && string.Equals(_currentTableLogicalName, table.LogicalName, StringComparison.OrdinalIgnoreCase)
                ? _currentTableSettings
                : _settings.GetTableSettings(_tables, table.LogicalName);
            if (tableSettings == null)
            {
                throw new Exception("Invalid Table: Please reload tables and try again");
            }

            return new TableData { Table = table, Settings = tableSettings, Metadata = metadata };
        }

        private TableData GetTableDataByLogicalName(string logicalName, bool targetRequired = true)
        {
            _logger.Log(LogLevel.INFO, $"Parsing table data...");

            if (_sourceClient == null || (targetRequired && _targetClient == null))
            {
                throw new Exception("You must select both a source and a target organization");
            }
            if (targetRequired && !(cbCreate.Checked || cbUpdate.Checked || cbDelete.Checked))
            {
                throw new Exception("You must select at least one setting for transporting the data");
            }

            var table = _tables.FirstOrDefault(tbl => tbl.LogicalName.Equals(logicalName));

            var repo = new CrmRepo(_sourceClient);
            var metadata = repo.GetTableMetadata(table.LogicalName);

            var tableSettings = _currentTableSettings != null
                && string.Equals(_currentTableLogicalName, table.LogicalName, StringComparison.OrdinalIgnoreCase)
                ? _currentTableSettings
                : _settings.GetTableSettings(_tables, table.LogicalName);
            if (tableSettings == null)
            {
                throw new Exception("Invalid Table: Please reload tables and try again");
            }

            return new TableData { Table = table, Settings = tableSettings, Metadata = metadata };
        }

        private void SetSelectedTableItem(TableData tableData)
        {
            _logger.Log(LogLevel.INFO, $"Loading table data...");

            var tableItems = lvTables.Items.Cast<ListViewItem>();
            var tableItem = tableItems.FirstOrDefault(lvi => lvi.SubItems[0].Text.Equals(tableData.Table.LogicalName));
            if (tableItem == null)
            {
                throw new Exception("Invalid Table: Please reload tables and try again");
            }

            tableItem.Focused = true;
            tableItem.EnsureVisible();
            tableItem.Selected = true;
            lvTables.Select();
        }

        private bool PrepareSelectedTableSettings()
        {
            if (_suppressTableSelectionChanged) return false;

            var table = GetSelectedTableOrDefault();
            if (table == null) return false;

            if (string.Equals(_currentTableLogicalName, table.LogicalName, StringComparison.OrdinalIgnoreCase)
                && _currentTableSettings != null)
            {
                return true;
            }

            AutoSaveDmtSettings();

            var previous = _previousTableLogicalName;
            var existingSettings = _settings.GetTableSettings(_tables, table.LogicalName);
            using (var dlg = new DmtSettingsFileDialog(
                table,
                _sourceClient?.ConnectedOrgUniqueName,
                _sourceClient?.ConnectedOrgFriendlyName,
                existingSettings))
            {
                var result = dlg.ShowDialog(ParentForm);
                if (result != DialogResult.OK || dlg.Choice == DmtFileChoice.Cancel)
                {
                    RestorePreviousTableSelection(previous);
                    return false;
                }

                _dmtFilePath = null;
                _dmtSettings = null;
                _currentTableLogicalName = table.LogicalName;

                if (dlg.Choice == DmtFileChoice.WithoutFile)
                {
                    _currentTableSettings = CreateSoftTableSettings(table);
                }
                else
                {
                    _dmtFilePath = dlg.FilePath;
                    ApplyDmtSettingsToCurrentTable(dlg.LoadedSettings);
                }

                _previousTableLogicalName = table.LogicalName;
                RenderDmtFileMenu();
                return true;
            }
        }

        private void RestorePreviousTableSelection(string logicalName)
        {
            _suppressTableSelectionChanged = true;
            try
            {
                foreach (ListViewItem item in lvTables.SelectedItems)
                    item.Selected = false;

                if (!string.IsNullOrWhiteSpace(logicalName))
                {
                    var previous = lvTables.Items
                        .Cast<ListViewItem>()
                        .FirstOrDefault(item => item.SubItems[0].Text.Equals(logicalName, StringComparison.OrdinalIgnoreCase));
                    if (previous != null)
                    {
                        previous.Selected = true;
                        previous.Focused = true;
                        previous.EnsureVisible();
                    }
                }
            }
            finally
            {
                _suppressTableSelectionChanged = false;
            }
        }

        private Table EnsureSelectedTableForDmtFile()
        {
            var table = !string.IsNullOrWhiteSpace(_currentTableLogicalName) && _tables != null
                ? _tables.FirstOrDefault(tbl => tbl.LogicalName.Equals(_currentTableLogicalName, StringComparison.OrdinalIgnoreCase))
                : GetSelectedTableOrDefault();
            if (table == null)
                throw new Exception("You must select a table first");

            if (_currentTableSettings == null || !string.Equals(_currentTableLogicalName, table.LogicalName, StringComparison.OrdinalIgnoreCase))
            {
                _currentTableLogicalName = table.LogicalName;
                _currentTableSettings = CreateSoftTableSettings(table);
            }

            return table;
        }

        private void CreateDmtFileForSelectedTable()
        {
            var table = EnsureSelectedTableForDmtFile();
            var filePath = this.SelectFile("DMT Settings (*.dmt.json)|*.dmt.json", save: true, defaultFileName: GetDefaultSaveFileName(".dmt.json", table.LogicalName));
            if (string.IsNullOrWhiteSpace(filePath)) return;

            var existingSettings = _settings.GetTableSettings(_tables, table.LogicalName);
            _dmtSettings = DmtFileService.CreateNew(
                table.LogicalName,
                table.DisplayName,
                table.IdAttribute,
                table.NameAttribute,
                _sourceClient?.ConnectedOrgUniqueName,
                _sourceClient?.ConnectedOrgFriendlyName,
                existingSettings);
            _dmtFilePath = filePath;
            _currentTableLogicalName = table.LogicalName;
            _currentTableSettings = CreateTableSettingsFromDmt(_dmtSettings);
            CaptureDmtSettingsFromUi();
            DmtFileService.Save(_dmtFilePath, _dmtSettings);
            RenderDmtFileMenu();
            SendMessageToStatusBar?.Invoke(this, new StatusBarMessageEventArgs($"Settings file saved: {Path.GetFileName(_dmtFilePath)}"));
        }

        private void LoadDmtFileForSelectedTable()
        {
            if (GetSelectedTableOrDefault() == null && string.IsNullOrWhiteSpace(_currentTableLogicalName))
            {
                LoadDmtFileAndSelectTable();
                return;
            }

            var table = EnsureSelectedTableForDmtFile();
            var existingSettings = _settings.GetTableSettings(_tables, table.LogicalName);
            using (var dlg = new DmtSettingsFileDialog(
                table,
                _sourceClient?.ConnectedOrgUniqueName,
                _sourceClient?.ConnectedOrgFriendlyName,
                existingSettings))
            {
                if (dlg.ShowDialog(ParentForm) != DialogResult.OK || dlg.Choice == DmtFileChoice.Cancel) return;

                if (dlg.Choice == DmtFileChoice.WithoutFile)
                {
                    CloseDmtFileSession(false);
                    return;
                }

                _dmtFilePath = dlg.FilePath;
                ApplyDmtSettingsToCurrentTable(dlg.LoadedSettings);
                LoadAttributes();
                LoadFilters(null);
                SendMessageToStatusBar?.Invoke(this, new StatusBarMessageEventArgs($"Settings file loaded: {Path.GetFileName(_dmtFilePath)}"));
            }
        }

        private void LoadDmtFileAndSelectTable()
        {
            var filePath = this.SelectFile("DMT Settings (*.dmt.json)|*.dmt.json");
            if (string.IsNullOrWhiteSpace(filePath)) return;

            var settings = DmtFileService.Load(filePath);
            var logicalName = settings?.Table?.LogicalName;
            if (string.IsNullOrWhiteSpace(logicalName))
                throw new Exception("Invalid settings file: missing table logical name.");

            var tableData = GetTableDataByLogicalName(logicalName, false);
            if (tableData == null)
                throw new Exception($"Settings file is for table '{logicalName}', but that table was not found. Reload tables and try again.");

            var environmentValidation = DmtFileService.ValidateEnvironment(
                settings,
                _sourceClient?.ConnectedOrgUniqueName,
                _sourceClient?.ConnectedOrgFriendlyName);
            if (!environmentValidation.matches)
            {
                var result = MessageBox.Show(
                    $"{environmentValidation.warning}\n\nContinue anyway?",
                    "Environment Mismatch",
                    MessageBoxButtons.YesNo,
                    MessageBoxIcon.Warning);
                if (result != DialogResult.Yes) return;
            }

            _suppressTableSelectionChanged = true;
            try
            {
                SetSelectedTableItem(tableData);
            }
            finally
            {
                _suppressTableSelectionChanged = false;
            }

            _dmtFilePath = filePath;
            ApplyDmtSettingsToCurrentTable(settings);
            _previousTableLogicalName = logicalName;
            LoadAttributes();
            LoadFilters(null);
            SendMessageToStatusBar?.Invoke(this, new StatusBarMessageEventArgs($"Settings file loaded: {Path.GetFileName(_dmtFilePath)}"));
        }

        private void SaveDmtFileAs()
        {
            var table = EnsureSelectedTableForDmtFile();
            var filePath = this.SelectFile("DMT Settings (*.dmt.json)|*.dmt.json", save: true, defaultFileName: GetDefaultSaveFileName(".dmt.json", table.LogicalName));
            if (string.IsNullOrWhiteSpace(filePath)) return;

            if (_dmtSettings == null)
            {
                _dmtSettings = DmtFileService.CreateNew(
                    table.LogicalName,
                    table.DisplayName,
                    table.IdAttribute,
                    table.NameAttribute,
                    _sourceClient?.ConnectedOrgUniqueName,
                    _sourceClient?.ConnectedOrgFriendlyName);
            }

            _dmtFilePath = filePath;
            CaptureDmtSettingsFromUi();
            DmtFileService.Save(_dmtFilePath, _dmtSettings);
            RenderDmtFileMenu();
            SendMessageToStatusBar?.Invoke(this, new StatusBarMessageEventArgs($"Settings file saved: {Path.GetFileName(_dmtFilePath)}"));
        }

        private void CloseDmtFileSession(bool saveFirst = true)
        {
            if (saveFirst) AutoSaveDmtSettings();

            var table = GetSelectedTableOrDefault();
            _dmtSettings = null;
            _dmtFilePath = null;
            _currentTableLogicalName = table?.LogicalName;
            _currentTableSettings = table != null ? CreateSoftTableSettingsFromUi(table) : null;
            RenderDmtFileMenu();
            SendMessageToStatusBar?.Invoke(this, new StatusBarMessageEventArgs("Settings file closed"));
        }

        private void ImportFileDataChecks(RecordCollection collection)
        {
            if (collection == null)
            {
                throw new Exception($"Invalid import file: Invalid structure");
            }
            if (string.IsNullOrEmpty(collection.LogicalName))
            {
                throw new Exception($"Invalid import file: Invalid table logical name");
            }
            if (collection.Count == 0 || !collection.Records.Any())
            {
                throw new Exception($"Invalid import file: No records");
            }
        }

        private void ValidateActiveSettingsTable(string sourceTableLogicalName, string sourceDescription)
        {
            if (_dmtSettings?.Table == null || string.IsNullOrWhiteSpace(_dmtSettings.Table.LogicalName)) return;
            if (string.IsNullOrWhiteSpace(sourceTableLogicalName)) return;

            if (!sourceTableLogicalName.Equals(_dmtSettings.Table.LogicalName, StringComparison.OrdinalIgnoreCase))
            {
                var settingsName = string.IsNullOrWhiteSpace(_dmtFilePath)
                    ? "active settings"
                    : Path.GetFileName(_dmtFilePath);
                throw new Exception($"The selected {sourceDescription} is for table '{sourceTableLogicalName}', but {settingsName} is for '{_dmtSettings.Table.LogicalName}'. Load the correct settings file before importing.");
            }
        }
        #endregion Private Main Methods

        #region Private Helper Methods
        private void Log(object sender, LoggerEventArgs args)
        {
            switch (args.Level)
            {
                case LogLevel.DEBUG:
                case LogLevel.INFO:
                    LogInfo(args.Message);
                    break;
                case LogLevel.WARN:
                    LogWarning(args.Message);
                    break;
                case LogLevel.ERROR:
                    LogError(args.Message);
                    break;
                default:
                    break;
            }
        }

        private void ReportWorkProgress(ProgressChangedEventArgs evt)
        {
            if (evt?.UserState == null) return;

            var message = evt.UserState.ToString();
            if (string.IsNullOrWhiteSpace(message)) return;

            SetWorkingMessage(message);
            _logger.Log(LogLevel.INFO, message);
            SendMessageToStatusBar?.Invoke(this, new StatusBarMessageEventArgs(evt.ProgressPercentage, message));
        }

        private string GetPreviewErrorMessage(Exception error)
        {
            var message = error.Message;
            var aliasMatch = Regex.Match(message, @"Link entity with name or alias (?<alias>.+?) is not found", RegexOptions.IgnoreCase);
            if (aliasMatch.Success)
            {
                var alias = aliasMatch.Groups["alias"].Value;
                return $"Preview failed because the filter references link-entity alias '{alias}', but the generated FetchXML does not contain a matching <link-entity alias=\"{alias}\"> node.\r\n\r\nInclude the related <link-entity> in the filter, or remove entityname=\"{alias}\" if those conditions should run on the selected table.";
            }

            return $"Preview failed: {message}";
        }

        private void ManageWorkingState(bool working)
        {
            pnlMain.Enabled = !working;

            _working = working;
            Cursor = working ? Cursors.WaitCursor : Cursors.Default;
            tsbAbort.Text = "Abort";
            tsbAbort.Visible = working;
        }

        private void InitializeDmtAutoSave()
        {
            _dmtAutoSaveTimer = new Timer { Interval = 1500 };
            _dmtAutoSaveTimer.Tick += (sender, args) =>
            {
                _dmtAutoSaveTimer.Stop();
                AutoSaveDmtSettings();
            };
        }

        private void ScheduleDmtAutoSave()
        {
            if (_dmtSettings == null || string.IsNullOrWhiteSpace(_dmtFilePath)) return;

            _dmtAutoSaveTimer.Stop();
            _dmtAutoSaveTimer.Start();
        }

        private void AutoSaveDmtSettings(bool showStatus = true)
        {
            if (_dmtSettings == null || string.IsNullOrWhiteSpace(_dmtFilePath)) return;

            CaptureDmtSettingsFromUi();
            DmtFileService.Save(_dmtFilePath, _dmtSettings);
            RenderDmtFileMenu();

            var fileName = Path.GetFileName(_dmtFilePath);
            _logger?.Log(LogLevel.INFO, $"Auto-saved settings file: {fileName}");
            if (showStatus)
                SendMessageToStatusBar?.Invoke(this, new StatusBarMessageEventArgs($"Auto-saved: {fileName}"));
        }

        private void CaptureDmtSettingsFromUi()
        {
            if (_dmtSettings == null) return;

            var table = !string.IsNullOrWhiteSpace(_currentTableLogicalName) && _tables != null
                ? _tables.FirstOrDefault(tbl => tbl.LogicalName.Equals(_currentTableLogicalName, StringComparison.OrdinalIgnoreCase))
                : GetSelectedTableOrDefault();
            if (table != null)
            {
                _dmtSettings.Table = new DmtTableInfo
                {
                    LogicalName = table.LogicalName,
                    DisplayName = table.DisplayName,
                    PrimaryIdAttribute = table.IdAttribute,
                    PrimaryNameAttribute = table.NameAttribute
                };
            }

            if (_sourceClient != null)
            {
                _dmtSettings.Environment = new DmtEnvironmentInfo
                {
                    UniqueName = _sourceClient.ConnectedOrgUniqueName,
                    FriendlyName = _sourceClient.ConnectedOrgFriendlyName
                };
            }

            if (lvAttributes.Items.Count > 0)
            {
                _dmtSettings.DeselectedAttributes = lvAttributes.Items
                    .Cast<ListViewItem>()
                    .Where(item => !item.Checked)
                    .Select(item => item.SubItems[0].Text)
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToList();
            }
            else if (_currentTableSettings?.DeselectedAttributes != null)
            {
                _dmtSettings.DeselectedAttributes = new List<string>(_currentTableSettings.DeselectedAttributes);
            }

            _dmtSettings.Filter = rtbFilter.Text;
            _dmtSettings.Mappings = new List<Mapping>(_mappings ?? new List<Mapping>());
            if (_currentTableSettings?.ExcelConfig != null)
                _dmtSettings.ExcelConfig = _currentTableSettings.ExcelConfig;

            _dmtSettings.ImportSettings = BuildDmtImportSettingsFromUi(_dmtSettings.ImportSettings);
        }

        private DmtImportSettings BuildDmtImportSettingsFromUi(DmtImportSettings existing)
        {
            existing = existing ?? new DmtImportSettings();
            var saved = _settings?.UiSettings;
            existing.BatchSize = saved?.BatchSize > 0 ? Math.Min(saved.BatchSize, 25) : Math.Max(1, existing.BatchSize);
            return existing;
        }

        private void ApplyDmtSettingsToCurrentTable(DmtSettings settings)
        {
            _dmtSettings = settings;
            _currentTableLogicalName = settings?.Table?.LogicalName;
            _currentTableSettings = CreateTableSettingsFromDmt(settings);
            _mappings = settings?.Mappings != null ? new List<Mapping>(settings.Mappings) : new List<Mapping>();
            if (_sourceInstance != null)
                _sourceInstance.Mappings = new List<Mapping>(_mappings);
            RenderMappingsButton();
            RenderDmtFileMenu();
        }

        private TableSettings CreateTableSettingsFromDmt(DmtSettings settings)
        {
            return new TableSettings
            {
                LogicalName = settings?.Table?.LogicalName,
                DisplayName = settings?.Table?.DisplayName,
                DeselectedAttributes = settings?.DeselectedAttributes != null ? new List<string>(settings.DeselectedAttributes) : null,
                Filter = settings?.Filter,
                ExcelConfig = settings?.ExcelConfig
            };
        }

        private TableSettings CreateSoftTableSettings(Table table)
        {
            return new TableSettings
            {
                LogicalName = table.LogicalName,
                DisplayName = table.DisplayName,
                IsCustomizable = table.IsCustomizable
            };
        }

        private TableSettings CreateSoftTableSettingsFromUi(Table table)
        {
            var settings = CreateSoftTableSettings(table);
            settings.Filter = rtbFilter.Text;
            settings.ExcelConfig = _currentTableSettings?.ExcelConfig;

            if (lvAttributes.Items.Count > 0)
            {
                settings.DeselectedAttributes = lvAttributes.Items
                    .Cast<ListViewItem>()
                    .Where(item => !item.Checked)
                    .Select(item => item.SubItems[0].Text)
                    .ToList();
            }

            return settings;
        }

        private Table GetSelectedTableOrDefault()
        {
            if (lvTables.SelectedItems.Count == 0 || _tables == null) return null;

            var tableItem = lvTables.SelectedItems[0].ToObject(new Table()) as Table;
            if (tableItem == null || string.IsNullOrWhiteSpace(tableItem.LogicalName)) return null;

            return _tables.FirstOrDefault(tbl => tbl.LogicalName.Equals(tableItem.LogicalName));
        }

        private void RenderDmtFileMenu()
        {
            if (tsmiDmtFile == null) return;

            var hasFile = !string.IsNullOrWhiteSpace(_dmtFilePath);
            tsmiDmtFile.Text = hasFile ? GetDmtFileDisplayName(_dmtFilePath) : "Settings File";
            tsmiDmtSaveAs.Enabled = lvTables.SelectedItems.Count > 0;
            tsmiDmtClose.Enabled = hasFile || _currentTableSettings != null;
        }

        private string GetDmtFileDisplayName(string filePath)
        {
            var fileName = Path.GetFileName(filePath);
            return fileName != null && fileName.EndsWith(".dmt.json", StringComparison.OrdinalIgnoreCase)
                ? fileName.Substring(0, fileName.Length - ".dmt.json".Length)
                : Path.GetFileNameWithoutExtension(filePath);
        }

        private string GetDefaultSaveFileName(string extension, string fallbackName)
        {
            var baseName = !string.IsNullOrWhiteSpace(_dmtFilePath)
                ? GetDmtFileDisplayName(_dmtFilePath)
                : fallbackName;

            if (string.IsNullOrWhiteSpace(baseName)) baseName = "data-migration";
            extension = extension.StartsWith(".") ? extension : $".{extension}";

            var invalidChars = Path.GetInvalidFileNameChars();
            baseName = new string(baseName.Select(ch => invalidChars.Contains(ch) ? '_' : ch).ToArray());
            return $"{baseName}{extension}";
        }

        private static Image CreateEnvironmentsIcon(int size = 20)
        {
            var bmp = new Bitmap(size, size);
            using (var g = Graphics.FromImage(bmp))
            {
                g.Clear(Color.Transparent);
                g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
                using (var pen = new Pen(Color.FromArgb(80, 80, 80), 1.5f))
                {
                    // Right-pointing arrow (top)
                    g.DrawLine(pen, 2, 7, 15, 7);
                    g.DrawLine(pen, 12, 4, 15, 7);
                    g.DrawLine(pen, 12, 10, 15, 7);
                    // Left-pointing arrow (bottom)
                    g.DrawLine(pen, 5, 13, 18, 13);
                    g.DrawLine(pen, 5, 13, 8, 10);
                    g.DrawLine(pen, 5, 13, 8, 16);
                }
            }
            return bmp;
        }

        private static Image CreateSettingsFileIcon(int size = 20)
        {
            var bmp = new Bitmap(size, size);
            using (var g = Graphics.FromImage(bmp))
            {
                g.Clear(Color.Transparent);
                g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
                using (var pen = new Pen(Color.FromArgb(80, 80, 80), 1.4f))
                using (var brush = new SolidBrush(Color.FromArgb(245, 245, 245)))
                {
                    var rect = new RectangleF(4, 2, 12, 16);
                    g.FillRectangle(brush, rect);
                    g.DrawRectangle(pen, 4, 2, 12, 16);
                    g.DrawLine(pen, 7, 7, 13, 7);
                    g.DrawLine(pen, 7, 10, 13, 10);
                    g.DrawLine(pen, 7, 13, 11, 13);
                }
            }
            return bmp;
        }

        private void RenderMappingsButton()
        {
            btnMappings.Font = (_mappings?.Any() == true) ? new Font(btnMappings.Font.Name, btnMappings.Font.Size, FontStyle.Bold) : new Font(btnMappings.Font.Name, btnMappings.Font.Size, FontStyle.Regular);
        }

        private void MoveImportSettingsIntoDialogs()
        {
            tsmiExportData.Text = "To JSON";
            tsmiImportData.Text = "From JSON";
            tsmiImportSettings.Text = "Import legacy table settings...";
            tsmiExportSettings.Visible = false;
            tsmiExportSettings.Enabled = false;
            tsmiExportWithSettings.Visible = false;
            tsmiExportWithSettings.Enabled = false;
            tsmiDmtFile.Enabled = false;
            tsmiReloadTables.Enabled = false;
            gbViewSettings.Controls.Remove(cbHideInvalid);
            cbHideInvalid.Location = new Point(cbSelectAll.Right + 18, cbSelectAll.Top);
            gbAttributes.Controls.Add(cbHideInvalid);
            cbHideInvalid.BringToFront();
            gbMappingSettings.Visible = false;
            gbOpSettings.Visible = false;
            gbViewSettings.Visible = false;
            gbViewSettings.Location = gbMappingSettings.Location;
        }

        private void ReRenderComponents(bool enable)
        {
            var sourceReady = _sourceClient != null && _sourceClient.IsReady;
            var targetReady = _targetClient != null && _targetClient.IsReady;
            var tableSelected = lvTables.SelectedItems.Count > 0;

            if (sourceReady) // source connection is available
            {
                tsmiConnectTarget.Enabled = enable;
                tsmiSwitchConnections.Enabled = enable && targetReady;
                tsmiReloadTables.Enabled = enable;
                tsmiDmtFile.Enabled = enable;
                gbTables.Enabled = enable;

                if (targetReady) // source and target connection is available
                {
                    tsmiImportData.Enabled = enable;
                    tsmiImportFromExcel.Enabled = enable;
                    gbMappingSettings.Enabled = false;
                    gbOpSettings.Enabled = false;
                    
                    RenderMappingsButton();

                    if (!string.IsNullOrEmpty(_settings.LastDataFile)) // source and target connection is available and a a file was already exported since tool loading
                    {
                        tsmiImportLastFile.Enabled = true;
                    }
                }

                if(tableSelected) // source connection is available and table is selected
                {
                    gbAttributes.Enabled = true;
                    tsbPreview.Enabled = enable;
                    tsmiExport.Enabled = enable;
                    tsmiExportData.Enabled = enable;
                    tsmiExportSettings.Enabled = false;
                    tsmiExportSettings.Visible = false;
                    tsmiExportWithSettings.Enabled = false;
                    tsmiExportWithSettings.Visible = false;
                    tsmiExportToExcel.Enabled = enable;
                    gbFilters.Enabled = enable;
                }
            }

            RenderDmtFileMenu();
        }

        public UiSettings ReadSettings(Enums.Action initial)
        {
            var mode = initial;
            if (cbCreate.Checked) mode |= Enums.Action.Create;
            if (cbUpdate.Checked) mode |= Enums.Action.Update;
            if (cbDelete.Checked) mode |= Enums.Action.Delete;

            var uiSettings = new UiSettings
            {
                Action = mode,
                BatchSize = nudBatchCount.Value.ToInt().Value,
                MapUsers = cbMapUsers.Checked,
                MapTeams = cbMapTeams.Checked,
                MapBu = cbMapBu.Checked,
                ApplyMappingsOn = rbMapOnExport.Checked ? Operation.Export : Operation.Import,
                HideInvalidAttributes = cbHideInvalid.Checked
            };

            _settings.UiSettings = uiSettings;
            SettingsHelper.SetSettings(_settings);
            if (_dmtSettings?.ImportSettings != null)
            {
                _dmtSettings.ImportSettings.BatchSize = Math.Min(uiSettings.BatchSize, 25);
            }

            return uiSettings;
        }

        private UiSettings GetDefaultImportSettings(Enums.Action initial)
        {
            var saved = _settings.UiSettings ?? new UiSettings();
            var action = initial;
            if ((saved.Action & Enums.Action.Create) == Enums.Action.Create || saved.Action == Enums.Action.None)
                action |= Enums.Action.Create;
            if ((saved.Action & Enums.Action.Update) == Enums.Action.Update || saved.Action == Enums.Action.None)
                action |= Enums.Action.Update;

            var dmtImport = _dmtSettings?.ImportSettings;
            var batchSize = dmtImport?.BatchSize > 0
                ? Math.Min(dmtImport.BatchSize, 25)
                : (saved.BatchSize > 0 ? Math.Min(saved.BatchSize, 25) : 25);

            return new UiSettings
            {
                Action = action,
                BatchSize = batchSize,
                MapUsers = saved.MapUsers,
                MapTeams = saved.MapTeams,
                MapBu = saved.MapBu,
                ApplyMappingsOn = saved.ApplyMappingsOn,
                HideInvalidAttributes = saved.HideInvalidAttributes
            };
        }

        private string ExtractFilterNode(string fetchXml)
        {
            if (string.IsNullOrWhiteSpace(fetchXml)) return string.Empty;

            var doc = new XmlDocument();
            doc.LoadXml(fetchXml);

            var sb = new StringBuilder();

            // extract link-entity nodes (supports filtering by related table)
            foreach (XmlNode node in doc.SelectNodes("/fetch/entity/link-entity"))
                sb.Append(node.OuterXml);

            // extract filter node
            var filterNode = doc.SelectSingleNode("/fetch/entity/filter");
            if (filterNode != null)
                sb.Append(filterNode.OuterXml);

            var fragment = sb.ToString();
            if (string.IsNullOrEmpty(fragment)) return string.Empty;

            // wrap in a root to allow indented formatting of a multi-element fragment
            var wrapDoc = new XmlDocument();
            wrapDoc.LoadXml($"<root>{fragment}</root>");

            using (var ms = new MemoryStream())
            using (var writer = new XmlTextWriter(ms, Encoding.Unicode) { Formatting = System.Xml.Formatting.Indented })
            {
                foreach (XmlNode child in wrapDoc.DocumentElement.ChildNodes)
                    child.WriteTo(writer);

                writer.Flush();
                ms.Flush();
                ms.Position = 0;

                using (var reader = new StreamReader(ms))
                    return reader.ReadToEnd().Trim();
            }
        }

        private string ParseFetchQuery(string filters)
        {
            var tableData = GetSelectedTableItemData(false);
            if (tableData == null)
            {
                throw new Exception("Error parsing query to FetchXML Builder");
            }

            // create new document
            var newDoc = new XmlDocument();

            // fetch node (root)
            var root = newDoc.CreateElement("fetch");
            newDoc.AppendChild(root);

            // entity node
            var entity = newDoc.CreateElement("entity");

            // entity node attributes
            var entityNameAttr = newDoc.CreateAttribute("name");
            entityNameAttr.Value = tableData.Table.LogicalName;
            entity.Attributes.Append(entityNameAttr);

            // append to root
            root.AppendChild(entity);

            if(!string.IsNullOrWhiteSpace(filters))
            {
                var fragment = newDoc.CreateDocumentFragment();
                fragment.InnerXml = filters;
                entity.AppendChild(fragment);
            }

            return newDoc.OuterXml;
        }
        #endregion Private Helper Methods

        #region Form events
        private void DataMigrationControl_Resize(object sender, EventArgs e)
        {
            pnlMain.ColumnStyles[0].SizeType = SizeType.Percent;
            pnlMain.ColumnStyles[0].Width = 100;

            btnMappings.Left = (btnMappings.Parent.Width - btnMappings.Width) / 2;
        }

        private void lvTables_Resize(object sender, EventArgs e)
        {
            // re-render list view columns
            var maxWidth = lvTables.Width >= 300 ? lvTables.Width : 300;
            chTblDisplayName.Width = (int)Math.Floor(maxWidth * 0.49);
            chTblLogicalName.Width = (int)Math.Floor(maxWidth * 0.49);
        }

        private void lvAttributes_Resize(object sender, EventArgs e)
        {
            // re-render list view columns
            var maxWidth = lvAttributes.Width >= 500 ? lvAttributes.Width : 500;
            chAttrDisplayName.Width = (int)Math.Floor(maxWidth * 0.25);
            chAttrLogicalName.Width = (int)Math.Floor(maxWidth * 0.25);
            chAttrType.Width = (int)Math.Floor(maxWidth * 0.19);
            chAttrDescription.Width = (int)Math.Floor(maxWidth * 0.29);
        }

        private async void txtTableFilter_TextChanged(object sender, EventArgs e)
        {
            async Task<bool> UserKeepsTyping()
            {
                var txt = txtTableFilter.Text;
                await Task.Delay(500);

                return txt != txtTableFilter.Text;
            }

            if (await UserKeepsTyping()) return;

            // user is done typing -> execute logic
            try
            {
                lvTables.Items.Clear();
                ManageWorkingState(true);

                LoadTablesList();
            }
            catch (Exception ex)
            {
                ManageWorkingState(false);
                _logger.Log(LogLevel.ERROR, ex.Message);
                MessageBox.Show(ex.Message, "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            finally
            {
                ManageWorkingState(false);
                txtTableFilter.Focus();
            }
        }

        private void lvTables_SelectedIndexChanged(object sender, EventArgs e)
        {
            try
            {
                if (_suppressTableSelectionChanged) return;
                if (lvTables.SelectedItems.Count > 0)
                {
                    if (!PrepareSelectedTableSettings()) return;
                    LoadAttributes();
                    LoadFilters(null);
                }
            }
            catch (Exception ex)
            {
                ManageWorkingState(false);
                _logger.Log(LogLevel.ERROR, ex.Message);
                MessageBox.Show(ex.Message, "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void listView_ColumnClick(object sender, ColumnClickEventArgs e)
        {
            try
            {
                (sender as ListView).Sort(_settings, e.Column);
            }
            catch (Exception ex)
            {
                ManageWorkingState(false);
                _logger.Log(LogLevel.ERROR, ex.Message);
                MessageBox.Show(ex.Message, "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void tsmiConnectTarget_Click(object sender, EventArgs e)
        {
            try
            {
                AddAdditionalOrganization();
            }
            catch (Exception ex)
            {
                ManageWorkingState(false);
                _logger.Log(LogLevel.ERROR, ex.Message);
                MessageBox.Show(ex.Message, "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void tsmiSwitchConnections_Click(object sender, EventArgs e)
        {
            try
            {
                // swap clients
                var tempClient = _sourceClient;
                _sourceClient = _targetClient;
                _targetClient = tempClient;

                // swap instances
                var tempInstance = _sourceInstance;
                _sourceInstance = _targetInstance;
                _targetInstance = tempInstance;

                // reload mappings from new source instance
                var srcMappings = _sourceInstance.Mappings.Where(map => map.SourceInstanceName.Equals(_sourceInstance.FriendlyName));
                _mappings = new List<Mapping>(srcMappings);

                // swap XrmToolBox framework connection details (drives the bold top-left/top-right labels)
                var sourceDetail = ConnectionDetail;
                var targetDetail = AdditionalConnectionDetails.FirstOrDefault();
                if (sourceDetail != null && targetDetail != null)
                {
                    ConnectionDetail = targetDetail;
                    AdditionalConnectionDetails[0] = sourceDetail; // Replace (not Add) — won't trigger ConnectionDetailsUpdated
                }

                // clear stale data and reload from new source
                lvTables.Items.Clear();
                lvAttributes.Items.Clear();
                rtbFilter.Text = string.Empty;
                _dmtSettings = null;
                _dmtFilePath = null;
                _currentTableSettings = null;
                _currentTableLogicalName = null;
                _previousTableLogicalName = null;
                ReRenderComponents(true);

                LoadTables();
            }
            catch (Exception ex)
            {
                ManageWorkingState(false);
                _logger.Log(LogLevel.ERROR, ex.Message);
                MessageBox.Show(ex.Message, "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void tsbRefreshTables_Click(object sender, EventArgs e)
        {
            try
            {
                LoadTables();
            }
            catch (Exception ex)
            {
                ManageWorkingState(false);
                _logger.Log(LogLevel.ERROR, ex.Message);
                MessageBox.Show(ex.Message, "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void tsmiDmtNew_Click(object sender, EventArgs e)
        {
            try
            {
                CreateDmtFileForSelectedTable();
            }
            catch (Exception ex)
            {
                ManageWorkingState(false);
                _logger.Log(LogLevel.ERROR, ex.Message);
                MessageBox.Show(ex.Message, "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void tsmiDmtLoad_Click(object sender, EventArgs e)
        {
            try
            {
                LoadDmtFileForSelectedTable();
            }
            catch (Exception ex)
            {
                ManageWorkingState(false);
                _logger.Log(LogLevel.ERROR, ex.Message);
                MessageBox.Show(ex.Message, "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void tsmiDmtSaveAs_Click(object sender, EventArgs e)
        {
            try
            {
                SaveDmtFileAs();
            }
            catch (Exception ex)
            {
                ManageWorkingState(false);
                _logger.Log(LogLevel.ERROR, ex.Message);
                MessageBox.Show(ex.Message, "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void tsmiDmtClose_Click(object sender, EventArgs e)
        {
            try
            {
                CloseDmtFileSession();
            }
            catch (Exception ex)
            {
                ManageWorkingState(false);
                _logger.Log(LogLevel.ERROR, ex.Message);
                MessageBox.Show(ex.Message, "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void tsbPreview_Click(object sender, EventArgs e)
        {
            try
            {
                PreviewData();
            }
            catch (Exception ex)
            {
                ManageWorkingState(false);
                _logger.Log(LogLevel.ERROR, ex.Message);
                MessageBox.Show(ex.Message, "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void tsmiExportData_Click(object sender, EventArgs e)
        {
            try
            {
                SettingsHelper.SetSettings(_settings);

                Export();
            }
            catch (Exception ex)
            {
                ManageWorkingState(false);
                _logger.Log(LogLevel.ERROR, ex.Message);
                MessageBox.Show(ex.Message, "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void tsmiExportSettings_Click(object sender, EventArgs e)
        {
            try
            {
                SettingsHelper.SetSettings(_settings);

                ExportSettings();
            }
            catch (Exception ex)
            {
                ManageWorkingState(false);
                _logger.Log(LogLevel.ERROR, ex.Message);
                MessageBox.Show(ex.Message, "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void tsmiExportWithSettings_Click(object sender, EventArgs e)
        {
            tsmiExportWithSettings.Visible = false;
            tsmiExportWithSettings.Enabled = false;
        }

        private void tsmiImportData_Click(object sender, EventArgs e)
        {
            try
            {
                Import();
            }
            catch (Exception ex)
            {
                ManageWorkingState(false);
                _logger.Log(LogLevel.ERROR, ex.Message);
                MessageBox.Show(ex.Message, "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void tsmiImportSettings_Click(object sender, EventArgs e)
        {
            try
            {
                LoadSettings();
            }
            catch (Exception ex)
            {
                ManageWorkingState(false);
                _logger.Log(LogLevel.ERROR, ex.Message);
                MessageBox.Show(ex.Message, "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void tsmiImportLastFile_Click(object sender, EventArgs e)
        {
            try
            {
                ImportLastExportedFile();
            }
            catch (Exception ex)
            {
                ManageWorkingState(false);
                _logger.Log(LogLevel.ERROR, ex.Message);
                MessageBox.Show(ex.Message, "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void ImportLastExportedFile()
        {
            var path = _settings.LastDataFile;
            if (string.IsNullOrWhiteSpace(path))
            {
                MessageBox.Show("No exported file is available for import.", "Info", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            if (!File.Exists(path))
            {
                MessageBox.Show($"The last exported file was not found:{Environment.NewLine}{path}", "Info", MessageBoxButtons.OK, MessageBoxIcon.Information);
                _settings.LastDataFile = string.Empty;
                SettingsHelper.SetSettings(_settings);
                ReRenderComponents(true);
                return;
            }

            var extension = Path.GetExtension(path);
            if (extension.Equals(".xlsx", StringComparison.OrdinalIgnoreCase))
            {
                ImportFromExcel(path);
                return;
            }

            if (extension.Equals(".json", StringComparison.OrdinalIgnoreCase))
            {
                Import(path);
                return;
            }

            MessageBox.Show($"Unsupported last exported file type: {extension}", "Info", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }

        private void tsmiExportToExcel_Click(object sender, EventArgs e)
        {
            try
            {
                ExportToExcel();
            }
            catch (Exception ex)
            {
                ManageWorkingState(false);
                _logger.Log(LogLevel.ERROR, ex.Message);
                MessageBox.Show(ex.Message, "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void tsmiImportFromExcel_Click(object sender, EventArgs e)
        {
            try
            {
                ImportFromExcel();
            }
            catch (Exception ex)
            {
                ManageWorkingState(false);
                _logger.Log(LogLevel.ERROR, ex.Message);
                MessageBox.Show(ex.Message, "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void tsbAbort_Click(object sender, EventArgs e)
        {
            try
            {
                CancelWorker();
                tsbAbort.Text = "Aborting operation...";
            }
            catch (Exception ex)
            {
                ManageWorkingState(false);
                _logger.Log(LogLevel.ERROR, ex.Message);
                MessageBox.Show(ex.Message, "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void cbAllAttributes_CheckedChanged(object sender, EventArgs e)
        {
            try
            {
                if (lvAttributes.Items.Count == 0) { return; }

                var allAttributes = (sender as CheckBox).Checked;

                var tableData = GetSelectedTableItemData(false);
                if (tableData == null)
                {
                    SendMessageToStatusBar?.Invoke(this, new StatusBarMessageEventArgs("Error saving selected attributes"));
                    return;
                }

                // check/uncheck all attributes
                lvAttributes.Items.Cast<ListViewItem>().ToList().ForEach(item => item.Checked = allAttributes);

                // save settings
                if (tableData.Settings.DeselectedAttributes == null)
                    tableData.Settings.DeselectedAttributes = new List<string>();
                if (allAttributes) { tableData.Settings.DeselectedAttributes.Clear(); }
                else { tableData.Settings.DeselectedAttributes = tableData.Table.AllAttributes.Select(attr => attr.LogicalName).ToList(); }
                ScheduleDmtAutoSave();
            }
            catch (Exception ex)
            {
                ManageWorkingState(false);
                _logger.Log(LogLevel.ERROR, ex.Message);
                MessageBox.Show(ex.Message, "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void lvAttributes_ItemChecked(object sender, ItemCheckedEventArgs e)
        {
            try
            {
                var listView = sender as ListView;
                if (listView.FocusedItem != null && lvTables.SelectedItems.Count > 0)
                {
                    var tableData = GetSelectedTableItemData(false);
                    if (tableData == null)
                    {
                        SendMessageToStatusBar?.Invoke(this, new StatusBarMessageEventArgs("Error saving selected attributes"));
                        return;
                    }

                    // save deselected attributes to settings
                    var deselected = lvAttributes.Items.Cast<ListViewItem>().ToList().Where(lvi => !lvi.Checked).Select(lvi => lvi.SubItems[0].Text);

                    if (tableData.Settings.DeselectedAttributes == null)
                        tableData.Settings.DeselectedAttributes = new List<string>();
                    tableData.Settings.DeselectedAttributes.Clear();
                    tableData.Settings.DeselectedAttributes.AddRange(deselected);
                    ScheduleDmtAutoSave();
                }
            }
            catch (Exception ex)
            {
                ManageWorkingState(false);
                _logger.Log(LogLevel.ERROR, ex.Message);
                MessageBox.Show(ex.Message, "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void btnMappings_Click(object sender, EventArgs e)
        {
            if (_sourceClient == null) { return; }

            try
            {
                var mappingsDlg = new Mappings(_sourceClient, _sourceInstance, _targetInstance, _tables, _settings);
                mappingsDlg.ShowDialog(ParentForm);

                if (mappingsDlg.Updated)
                {
                    _mappings = _sourceInstance.Mappings?
                        .Where(map => _targetInstance == null || string.Equals(map.TargetInstanceName, _targetInstance.FriendlyName))
                        .ToList() ?? new List<Mapping>();
                    RenderMappingsButton();

                    SettingsHelper.SetSettings(_settings);
                    AutoSaveDmtSettings();
                    SendMessageToStatusBar?.Invoke(this, new StatusBarMessageEventArgs("Succesfully updated Organization Mappings"));
                }
            }
            catch (Exception ex)
            {
                ManageWorkingState(false);
                _logger.Log(LogLevel.ERROR, ex.Message);
                MessageBox.Show(ex.Message, "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void btnFetchXmlBuilder_Click(object sender, EventArgs e)
        {
            try
            {
                if (OnOutgoingMessage == null) { throw new Exception("FetchXML Builder is not open"); }

                var filters = rtbFilter.Text;
                var fetch = ParseFetchQuery(filters);

                OnOutgoingMessage(this, new MessageBusEventArgs("FetchXML Builder")
                {
                    TargetArgument = fetch
                });
            }
            catch (Exception ex)
            {
                ManageWorkingState(false);
                _logger.Log(LogLevel.ERROR, ex.Message);
                MessageBox.Show(ex.Message, "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void btnSql4Cds_Click(object sender, EventArgs e)
        {
            try
            {
                if (OnOutgoingMessage == null) { throw new Exception("SQL 4 CDS is not open"); }

                var filters = rtbFilter.Text;
                var fetch = ParseFetchQuery(filters);

                OnOutgoingMessage(this, new MessageBusEventArgs("SQL 4 CDS")
                {
                    TargetArgument = fetch
                });

                MessageBox.Show(
                    "Your query has been sent to SQL 4 CDS and converted to SQL.\n\n" +
                    "SQL 4 CDS does not automatically send the query back. To apply your changes:\n\n" +
                    "  1. Edit your query in SQL 4 CDS\n" +
                    "  2. Use SQL 4 CDS to convert back to FetchXML\n" +
                    "  3. Copy the <filter> and <link-entity> nodes from the FetchXML\n" +
                    "  4. Paste them into the filter area in Data Migration Tool",
                    "SQL 4 CDS", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            catch (Exception ex)
            {
                ManageWorkingState(false);
                _logger.Log(LogLevel.ERROR, ex.Message);
                MessageBox.Show(ex.Message, "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void rtbFilter_TextChanged(object sender, EventArgs e)
        {
            try
            {
                if (_suppressSettingsEvents) return;

                var filters = rtbFilter.Text;

                var tableData = GetSelectedTableItemData(false);
                if (tableData == null)
                {
                    SendMessageToStatusBar?.Invoke(this, new StatusBarMessageEventArgs("Error saving filters"));
                    return;
                }

                // save settings
                tableData.Settings.Filter = filters;
                SettingsHelper.SetSettings(_settings);
                ScheduleDmtAutoSave();
            }
            catch (Exception ex)
            {
                ManageWorkingState(false);
                _logger.Log(LogLevel.ERROR, ex.Message);
                MessageBox.Show(ex.Message, "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void cbMapOption_CheckedChanged(object sender, EventArgs e)
        {
            if(_ready)
            {
                GenerateMappings();
            }
        }

        private void cbHideInvalid_CheckedChanged(object sender, EventArgs e)
        {
            _settings.UiSettings.HideInvalidAttributes = cbHideInvalid.Checked;
            SettingsHelper.SetSettings(_settings);

            if(lvAttributes.Enabled && lvAttributes.Items.Count > 0)
            {
                LoadAttributes();
            }
        }
        #endregion Form events
    }
}
