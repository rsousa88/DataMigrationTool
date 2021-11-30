// System
using System;
using System.IO;
using System.Xml;
using System.Data;
using System.Linq;
using System.Drawing;
using System.Windows.Forms;
using System.Threading.Tasks;
using System.Collections.Generic;
using System.Collections.Specialized;

// Microsoft
using Microsoft.Xrm.Sdk;
using Microsoft.Crm.Sdk.Messages;
using Microsoft.Xrm.Tooling.Connector;

// 3rd Party
using McTools.Xrm.Connection;
using XrmToolBox.Extensibility;
using XrmToolBox.Extensibility.Args;
using XrmToolBox.Extensibility.Interfaces;

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
        // settings
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

        // flags
        private bool _ready = false;
        private bool _working;
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
        }

        public void DataMigrationControl_Load(object sender, EventArgs e)
        {
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
                LogInfo($"Updating connection: {actionName}...");
                base.UpdateConnection(newService, detail, actionName, parameter);
                var client = detail.ServiceClient;

                if (!actionName.Equals("AdditionalOrganization"))
                {
                    UpdateLegacyInstance(detail.ConnectionId.Value, client);

                    var instance = _settings.Instances.FirstOrDefault(inst => inst.UniqueName.Equals(client.ConnectedOrgUniqueName));
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

                    _sourceClient = client;
                    _sourceInstance = instance;

                    // load source instance mappings
                    var srcMappings = _sourceInstance.Mappings.Where(map => map.SourceInstanceName.Equals(_sourceInstance.FriendlyName));
                    _mappings = new List<Mapping>(srcMappings);
                    ClearAutoMappings();

                    // load sorts
                    _sorts = _settings.Sorts;

                    // save settings file
                    SettingsHelper.SetSettings(_settings);

                    // render UI components
                    LogInfo($"Rendering UI components...");
                    RenderConnectionLabel(ConnectionType.Source, instance.FriendlyName);
                    ReRenderComponents(true);

                    // load tables when source connection changes
                    LoadTables();
                }
            }
            catch (Exception ex)
            {
                ManageWorkingState(false);
                LogError(ex.Message);
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
                    LogInfo($"Source OrgId: {_sourceClient.ConnectedOrgId}");
                    LogInfo($"Source OrgUniqueName: {_sourceClient.ConnectedOrgUniqueName}");
                    LogInfo($"Source OrgFriendlyName: {_sourceClient.ConnectedOrgFriendlyName}");
                    LogInfo($"Source EnvId: {_sourceClient.EnvironmentId}");

                    if (client == null) { throw new Exception("Target connection is invalid"); }
                    LogInfo($"Target OrgId: {client.ConnectedOrgId}");
                    LogInfo($"Target OrgUniqueName: {client.ConnectedOrgUniqueName}");
                    LogInfo($"Target OrgFriendlyName: {client.ConnectedOrgFriendlyName}");
                    LogInfo($"Target EnvId: {client.EnvironmentId}");

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

                    // load ui settings
                    LoadUiSettings();
                    GenerateMappings();

                    SettingsHelper.SetSettings(_settings);

                    ReRenderComponents(true);
                    RenderConnectionLabel(ConnectionType.Target, instance.FriendlyName);

                    _ready = true;
                    SendMessageToStatusBar?.Invoke(this, new StatusBarMessageEventArgs("Target Connection ready"));
                }
            }
            catch (Exception ex)
            {
                ManageWorkingState(false);
                LogError(ex.Message);
                MessageBox.Show(this, $"An error occurred: {ex.Message}", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void UpdateLegacyInstance(Guid legacyId, CrmServiceClient client)
        {
            var legacy = _settings.Instances.FirstOrDefault(inst => inst.Updated.Equals(false) && inst.Id.Equals(legacyId));

            // update instance
            if (legacy != null)
            {
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
                if (message.SourcePlugin.Equals("FetchXML Builder") && message.TargetArgument is string fetchXml && !string.IsNullOrWhiteSpace(fetchXml))
                {
                    var filters = ExtractFilterNode(fetchXml);

                    var tableData = GetSelectedTableItemData(false);
                    if (tableData == null)
                    {
                        SendMessageToStatusBar?.Invoke(this, new StatusBarMessageEventArgs("Error setting filters from FetchXML Builder"));
                        return;
                    }

                    // save settings
                    tableData.Settings.Filter = filters;
                    SettingsHelper.SetSettings(_settings);

                    // set text box
                    rtbFilter.Text = filters;
                }
            }
            catch (Exception ex)
            {
                ManageWorkingState(false);
                LogError(ex.Message);
                MessageBox.Show(this, $"An error occurred: {ex.Message}", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }
        #endregion Interface Methods

        #region Private Main Methods
        private void WhoAmI()
        {
            Service.Execute(new WhoAmIRequest());
        }

        private void LoadTables()
        {
            LogInfo($"Loading tables...");

            gbAttributes.Enabled = false;
            gbFilters.Enabled = false;

            if (Service == null)
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
                    var repo = new CrmRepo(Service, worker);
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
            LogInfo($"Rendering tables list view...");

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
            LogInfo($"Loading attributes...");

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
                Message = $"Loading {tableData.Table.DisplayName} attributes...",
                Work = (worker, args) =>
                {
                    // filter valid attributes
                    args.Result = tableData.Metadata.Attributes
                        .Where(att => att.IsValidForRead != null && att.IsValidForRead.Value)
                        .Where(att => att.IsValidForCreate != null && att.IsValidForCreate.Value)
                        .Where(att => att.DisplayName != null && att.DisplayName.UserLocalizedLabel != null && !string.IsNullOrEmpty(att.DisplayName.UserLocalizedLabel.Label))
                        .Select(att => new Models.Attribute
                        {
                            Type = att.AttributeTypeName.Value.EndsWith("Type") ? att.AttributeTypeName.Value.Substring(0, att.AttributeTypeName.Value.LastIndexOf("Type")) : att.AttributeTypeName.Value,
                            LogicalName = att.LogicalName,
                            DisplayName = att.DisplayName.UserLocalizedLabel.Label,
                            Updatable = att.IsValidForUpdate.Value
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

                        SendMessageToStatusBar?.Invoke(this, new StatusBarMessageEventArgs("Attributes load complete"));
                    }
                }
            });
        }

        private void LoadAttributesList(TableData tableData)
        {
            LogInfo($"Rendering attributes list view...");

            var deselected = tableData.Settings.DeselectedAttributes;
            if (deselected == null)
            {
                deselected = new List<string>();
                deselected.AddRange(_sourceInstance.DefaultDeselected);

                // save settings
                tableData.Settings.DeselectedAttributes = deselected;
                SettingsHelper.SetSettings(_settings);
            }

            foreach (var att in tableData.Table.AllAttributes)
            {
                var item = att.ToListViewItem();
                if (!att.Updatable)
                {
                    item.ForeColor = Color.Gray;
                    item.SubItems.Add("Read-only attribute");
                }

                item.Checked = !deselected.Any(dsl => dsl.Equals(att.LogicalName));

                lvAttributes.Items.Add(item);
            }

            ReRenderComponents(true);

            ManageWorkingState(false);
        }

        private void LoadFilters(TableData tableData)
        {
            LogInfo($"Loading table filters...");

            rtbFilter.Text = string.Empty;

            tableData = tableData != null ? tableData : GetSelectedTableItemData(false);
            if (tableData == null || tableData.Settings == null)
            {
                SendMessageToStatusBar?.Invoke(this, new StatusBarMessageEventArgs("Error loading filters"));
                return;
            }

            rtbFilter.Text = tableData.Settings.Filter;

            gbFilters.Enabled = true;
        }

        private void PreviewData()
        {
            LogInfo($"Previewing operation...");

            var tableData = GetSelectedTableItemData(attributeRequired: true);
            if(tableData == null) {
                SendMessageToStatusBar?.Invoke(this, new StatusBarMessageEventArgs("Preview operation aborted"));
                return;
            }

            if (cbDelete.Checked)
            {
                if (string.IsNullOrWhiteSpace(tableData.Settings.Filter))
                {
                    throw new Exception("Delete operation whithout any filter applied is not supported. You must add a filter in order to perform a Delete operation");
                }
                else
                {
                    var msg = $"WARNING: Delete operation\nAll '{tableData.Table.LogicalName}' records on target instance that doesn't match the filter will be deleted!\n\nContinue?";
                    var result = MessageBox.Show(msg, "Warning", MessageBoxButtons.YesNo, MessageBoxIcon.Warning);
                    if (result.Equals(DialogResult.No)) { return; }
                }
            }

            ManageWorkingState(true);

            var uiSettings = ReadSettings(Enums.Action.Preview);

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

                    var logic = new DataLogic(worker, Service, _targetClient);
                    var result = Task.Run(() => logic.Preview(data, uiSettings));

                    evt.Result = result.Result;
                },
                PostWorkCallBack = evt =>
                {
                    if (evt.Result != null)
                    {
                        // show preview form
                        var result = evt.Result as OperationResult;

                        var prvwDialog = new Results(result.Items, _settings);
                        prvwDialog.ShowDialog(ParentForm);

                        SettingsHelper.SetSettings(_settings);
                    }

                    ManageWorkingState(false);
                },
                ProgressChanged = evt =>
                {
                    SetWorkingMessage(evt.UserState.ToString());
                    SendMessageToStatusBar?.Invoke(this, new StatusBarMessageEventArgs(evt.ProgressPercentage * 100, evt.UserState.ToString()));
                }
            });
        }

        private void ExportSettings(string dirPath)
        {
            LogInfo($"Exporting table settings...");

            if (string.IsNullOrEmpty(dirPath)) { return; }

            var tableData = GetSelectedTableItemData(false);
            if (tableData == null)
            {
                SendMessageToStatusBar?.Invoke(this, new StatusBarMessageEventArgs("Error exporting table settings"));
                return;
            }

            // save serialized json with settings
            var filename = $"{dirPath}\\{tableData.Table.LogicalName}.settings.json";

            var json = tableData.Settings.SerializeObject<TableSettings>();
            File.WriteAllText(filename, json);
        }

        private void Export(string dirPath)
        {
            LogInfo($"Export data operation...");

            if (string.IsNullOrEmpty(dirPath)) { return; }

            ManageWorkingState(true);

            var tableData = GetSelectedTableItemData(false, true);
            if (tableData == null)
            {
                SendMessageToStatusBar?.Invoke(this, new StatusBarMessageEventArgs("Export operation aborted"));
                return;
            }

            var filePath = $"{dirPath}/{tableData.Table.LogicalName}.json";

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

                    var logic = new DataLogic(worker, Service, _targetClient);
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
                }
            });
        }

        private void LoadSettings()
        {
            LogInfo($"Loading table settings...");

            // get file path
            var dialog = new OpenFileDialog
            {
                Title = "Select settings file...",
                Filter = "Json files (*.json)|*.settings.json",
                FilterIndex = 2,
                RestoreDirectory = true
            };

            var path = GetFileDialogPath(Operation.Import, dialog);
            if(string.IsNullOrEmpty(path)) { return; }

            ManageWorkingState(true);

            var json = File.ReadAllText(path);
            var loadedSettings = json.DeserializeObject<TableSettings>();

            // get table data by logical name
            var tableData = GetTableDataByLogicalName(loadedSettings.LogicalName, false);
            if (tableData == null)
            {
                SendMessageToStatusBar?.Invoke(this, new StatusBarMessageEventArgs("Import operation aborted"));
                return;
            }

            // re-set settings from json file
            tableData.Settings.Filter = loadedSettings.Filter;
            tableData.Settings.DeselectedAttributes = loadedSettings.DeselectedAttributes;

            ManageWorkingState(false);
            SetSelectedTableItem(tableData);
            SettingsHelper.SetSettings(_settings);

            SendMessageToStatusBar?.Invoke(this, new StatusBarMessageEventArgs($"Successfully imported settings for table '{tableData.Table.DisplayName}'"));
        }

        private void Import(string path = null)
        {
            LogInfo($"Import operation...");

            // get file path
            var dialog = new OpenFileDialog
            {
                Title = "Select data file...",
                Filter = "Json files (*.json)|*.json",
                FilterIndex = 2,
                RestoreDirectory = true
            };

            path = path is null ? GetFileDialogPath(Operation.Import, dialog) : path;
            if (string.IsNullOrEmpty(path)) { return; }

            ManageWorkingState(true);

            var json = File.ReadAllText(path);
            var importData = json.DeserializeObject<RecordCollection>();
            ImportFileDataChecks(importData);

            var tableData = GetTableDataByLogicalName(importData.LogicalName);
            if (tableData == null)
            {
                SendMessageToStatusBar?.Invoke(this, new StatusBarMessageEventArgs("Import operation aborted"));
                return;
            }

            // get ui settings
            var uiSettings = ReadSettings(Enums.Action.None);

            WorkAsync(new WorkAsyncInfo
            {
                Message = "Importing records...",
                AsyncArgument = tableData,
                IsCancelable = true,
                Work = (worker, evt) =>
                {
                    var data = evt.Argument as TableData;

                    var logic = new DataLogic(worker, Service, _targetClient);

                    var result = Task.Run(() => logic.Import(data, importData, uiSettings, _mappings));

                    evt.Result = result.Result;
                },
                PostWorkCallBack = evt =>
                {
                    if (evt.Result != null)
                    {
                        // show results form
                        var result = evt.Result as OperationResult;

                        var resDialog = new Results(result.Items, _settings);
                        resDialog.ShowDialog(ParentForm);

                        SettingsHelper.SetSettings(_settings);
                    }

                    ManageWorkingState(false);
                    SendMessageToStatusBar?.Invoke(this, new StatusBarMessageEventArgs("Import complete"));
                }
            });
        }

        private string GetExportDirectoryPath()
        {
            var path = string.Empty;

            using (var fbd = new FolderBrowserDialog())
            {
                fbd.Description = "Select export directory";

                if (fbd.ShowDialog(this) == DialogResult.OK)
                {
                    path = fbd.SelectedPath;
                }
            }

            return path;
        }

        private string GetFileDialogPath(Operation action, FileDialog dialog)
        {
            var path = string.Empty;

            if (action.Equals(Operation.Export))
            {
                using (var sfd = dialog as SaveFileDialog)
                {
                    if (sfd.ShowDialog() == DialogResult.OK)
                    {
                        path = sfd.FileName;
                    }
                }
            }
            else if (action.Equals(Operation.Import))
            {
                using (var ofd = dialog as OpenFileDialog)
                {
                    if (ofd.ShowDialog(this) == DialogResult.OK)
                    {
                        path = ofd.FileName;
                    }
                }
            }

            return path;
        }

        private void LoadUiSettings()
        {
            var uiSettings = _settings.UiSettings;
            if (uiSettings != null)
            {
                cbMapUsers.Checked = uiSettings.MapUsers;
                cbMapTeams.Checked = uiSettings.MapTeams;
                cbMapBu.Checked = uiSettings.MapBu;
                rbMapOnExport.Checked = uiSettings.ApplyMappingsOn.Equals(Operation.Export);
                rbMapOnImport.Checked = uiSettings.ApplyMappingsOn.Equals(Operation.Import);
                cbCreate.Checked = (uiSettings.Action & Enums.Action.Create) == Enums.Action.Create;
                cbUpdate.Checked = (uiSettings.Action & Enums.Action.Update) == Enums.Action.Update;
                cbDelete.Checked = (uiSettings.Action & Enums.Action.Delete) == Enums.Action.Delete;
                nudBatchCount.Value = uiSettings.BatchSize;
            }
        }

        private void ClearAutoMappings()
        {
            // clear previously generated auto mappings
            _mappings.RemoveAll(map => map.State.Equals(MappingState.Auto));
            _sourceInstance.Mappings = _mappings;
            SettingsHelper.SetSettings(_settings);
            SendMessageToStatusBar?.Invoke(this, new StatusBarMessageEventArgs("Cleared previously generated Automatic Mappings"));
        }

        private void GenerateMappings()
        {
            LogInfo($"Generating automatic mappings...");

            ManageWorkingState(true);

            var uiSettings = ReadSettings(Enums.Action.None);

            WorkAsync(new WorkAsyncInfo
            {
                Message = "Generating automatic mappings...",
                IsCancelable = true,
                Work = (worker, evt) =>
                {
                    ClearAutoMappings();

                    var mappingsLogic = new MappingsLogic(Service, _targetClient);

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
                    SendMessageToStatusBar?.Invoke(this, new StatusBarMessageEventArgs("Automatic Mappings generated successfully"));
                }
            });
        }

        private TableData GetSelectedTableItemData(bool targetRequired = true, bool attributeRequired = false)
        {
            LogInfo($"Parsing table data...");

            if (Service == null || (targetRequired && _targetClient == null))
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
            if (tableItem == null || string.IsNullOrEmpty(tableItem.DisplayName) || string.IsNullOrEmpty(tableItem.LogicalName) || !_tables.Any(tbl => tbl.LogicalName.Equals(tableItem.LogicalName)))
            {
                throw new Exception("Invalid Table: Please reload tables and try again");
            }

            var table = _tables.FirstOrDefault(tbl => tbl.LogicalName.Equals(tableItem.LogicalName));

            var repo = new CrmRepo(Service);
            var metadata = repo.GetTableMetadata(table.LogicalName);

            var tableSettings = _settings.GetTableSettings(_tables, table.LogicalName);
            if (tableSettings == null)
            {
                throw new Exception("Invalid Table: Please reload tables and try again");
            }

            return new TableData { Table = table, Settings = tableSettings, Metadata = metadata };
        }

        private TableData GetTableDataByLogicalName(string logicalName, bool targetRequired = true)
        {
            LogInfo($"Parsing table data...");

            if (Service == null || (targetRequired && _targetClient == null))
            {
                throw new Exception("You must select both a source and a target organization");
            }
            if (targetRequired && !(cbCreate.Checked || cbUpdate.Checked || cbDelete.Checked))
            {
                throw new Exception("You must select at least one setting for transporting the data");
            }

            var table = _tables.FirstOrDefault(tbl => tbl.LogicalName.Equals(logicalName));

            var repo = new CrmRepo(Service);
            var metadata = repo.GetTableMetadata(table.LogicalName);

            var tableSettings = _settings.GetTableSettings(_tables, table.LogicalName);
            if (tableSettings == null)
            {
                throw new Exception("Invalid Table: Please reload tables and try again");
            }

            return new TableData { Table = table, Settings = tableSettings, Metadata = metadata };
        }

        private void SetSelectedTableItem(TableData tableData)
        {
            LogInfo($"Loading table data...");

            var tableItems = lvTables.Items.Cast<ListViewItem>();
            var tableItem = tableItems.FirstOrDefault(lvi => lvi.SubItems[1].Text.Equals(tableData.Table.LogicalName));
            if (tableItem == null)
            {
                throw new Exception("Invalid Table: Please reload tables and try again");
            }

            tableItem.Focused = true;
            tableItem.EnsureVisible();
            tableItem.Selected = true;
            lvTables.Select();
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
        #endregion Private Main Methods

        #region Private Helper Methods
        private void ManageWorkingState(bool working)
        {
            pnlMain.Enabled = !working;

            _working = working;
            Cursor = working ? Cursors.WaitCursor : Cursors.Default;
            tsbAbort.Text = "Abort";
            tsbAbort.Visible = working;
        }

        private void RenderConnectionLabel(ConnectionType serviceType, string name)
        {
            var label = serviceType.Equals(ConnectionType.Source) ? lblSourceValue : lblTargetValue;
            label.Text = name;
            label.ForeColor = Color.MediumSeaGreen;
        }

        private void RenderMappingsButton()
        {
            btnMappings.Font = _sourceInstance.Mappings.Any() ? new Font(btnMappings.Font.Name, btnMappings.Font.Size, FontStyle.Bold) : new Font(btnMappings.Font.Name, btnMappings.Font.Size, FontStyle.Regular);
        }

        private void ReRenderComponents(bool enable)
        {
            var sourceReady = _sourceClient != null && _sourceClient.IsReady;
            var targetReady = _targetClient != null && _targetClient.IsReady;
            var tableSelected = lvTables.SelectedItems.Count > 0;

            if (sourceReady) // source connection is available
            {
                btnSelectTarget.Enabled = enable;

                if (targetReady) // source and target connection is available
                {
                    tsmiImportData.Enabled = enable;
                    gbMappingSettings.Enabled = enable;
                    gbOpSettings.Enabled = enable;
                    gbTables.Enabled = enable;
                    RenderMappingsButton();

                    if (!string.IsNullOrEmpty(_settings.LastDataFile)) // source and target connection is available and a a file was already exported since tool loading
                    {
                        tsmiImportLastFile.Enabled = true;
                    }

                    if (tableSelected) // source and target connection is available and a table is selected
                    {
                        tsbPreview.Enabled = enable;
                    }
                }

                if(tableSelected) // source connection is available and table is selected
                {
                    gbAttributes.Enabled = true;
                    tsmiExport.Enabled = enable;
                    tsmiExportData.Enabled = enable;
                    tsmiExportSettings.Enabled = enable;
                    tsmiExportWithSettings.Enabled = enable;
                }
            }
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
                BatchSize= nudBatchCount.Value.ToInt().Value,
                MapUsers = cbMapUsers.Checked,
                MapTeams = cbMapTeams.Checked,
                MapBu = cbMapBu.Checked,
                ApplyMappingsOn = rbMapOnExport.Checked ? Operation.Export : Operation.Import
            };

            _settings.UiSettings = uiSettings;
            SettingsHelper.SetSettings(_settings);

            return uiSettings;
        }

        private string ExtractFilterNode(string fetchXml)
        {
            var doc = new XmlDocument();

            var filters = string.Empty;

            if(!string.IsNullOrWhiteSpace(fetchXml))
            {
                // load xml
                doc.LoadXml(fetchXml);

                var filterNodes = doc.SelectNodes("/fetch/entity/filter");
                if (filterNodes.Count > 0)
                {
                    filters = filterNodes[0].OuterXml;
                }
            }

            return filters.FormatXml();
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
                // load document from filters xml
                var filtersDoc = new XmlDocument();
                filtersDoc.LoadXml(filters);

                // get node
                var filtersNode = filtersDoc.FirstChild;

                // need to import node to newDoc document before append
                var importNode = newDoc.ImportNode(filtersNode, true);

                // append to new document
                entity.AppendChild(importNode);
            }

            return newDoc.OuterXml;
        }
        #endregion Private Helper Methods

        #region Form events
        private void DataMigrationControl_Resize(object sender, EventArgs e)
        {
            // re-render main panel
            var firstColumn = pnlMain.ColumnStyles[0];
            firstColumn.SizeType = SizeType.Absolute;
            firstColumn.Width = 200;

            // re-render settings panel
            var settingsRows = pnlSettings.RowStyles;
            settingsRows[0].SizeType = SizeType.Absolute;
            settingsRows[0].Height = 115;
            settingsRows[1].SizeType = SizeType.Absolute;
            settingsRows[1].Height = 185;
            settingsRows[2].SizeType = SizeType.Absolute;
            settingsRows[2].Height = 129;

            // center buttons
            //btnSelectTarget.Parent = gbEnvironments;
            btnSelectTarget.Left = (btnSelectTarget.Parent.Width - btnSelectTarget.Width) / 2;
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
                LogError(ex.Message);
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
                if (lvTables.SelectedItems.Count > 0)
                {
                    LoadAttributes();
                    LoadFilters(null);
                }
            }
            catch (Exception ex)
            {
                ManageWorkingState(false);
                LogError(ex.Message);
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
                LogError(ex.Message);
                MessageBox.Show(ex.Message, "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void btnSelectTarget_Click(object sender, EventArgs e)
        {
            try
            {
                AddAdditionalOrganization();
            }
            catch (Exception ex)
            {
                ManageWorkingState(false);
                LogError(ex.Message);
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
                LogError(ex.Message);
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
                LogError(ex.Message);
                MessageBox.Show(ex.Message, "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void tsmiExportData_Click(object sender, EventArgs e)
        {
            try
            {
                var dirPath = GetExportDirectoryPath();
                Export(dirPath);
            }
            catch (Exception ex)
            {
                ManageWorkingState(false);
                LogError(ex.Message);
                MessageBox.Show(ex.Message, "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void tsmiExportSettings_Click(object sender, EventArgs e)
        {
            try
            {
                var dirPath = GetExportDirectoryPath();
                ExportSettings(dirPath);
            }
            catch (Exception ex)
            {
                ManageWorkingState(false);
                LogError(ex.Message);
                MessageBox.Show(ex.Message, "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void tsmiExportWithSettings_Click(object sender, EventArgs e)
        {
            try
            {
                var dirPath = GetExportDirectoryPath();
                ExportSettings(dirPath);
                Export(dirPath);
            }
            catch (Exception ex)
            {
                ManageWorkingState(false);
                LogError(ex.Message);
                MessageBox.Show(ex.Message, "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
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
                LogError(ex.Message);
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
                LogError(ex.Message);
                MessageBox.Show(ex.Message, "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void tsmiImportLastFile_Click(object sender, EventArgs e)
        {
            try
            {
                Import(_settings.LastDataFile);
            }
            catch (Exception ex)
            {
                ManageWorkingState(false);
                LogError(ex.Message);
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
                LogError(ex.Message);
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
                if (allAttributes) { tableData.Settings.DeselectedAttributes.Clear(); }
                else { tableData.Settings.DeselectedAttributes = tableData.Table.AllAttributes.Select(attr => attr.LogicalName).ToList(); }
            }
            catch (Exception ex)
            {
                ManageWorkingState(false);
                LogError(ex.Message);
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
                    var deselected = lvAttributes.Items.Cast<ListViewItem>().ToList().Where(lvi => !lvi.Checked).Select(lvi => lvi.SubItems[1].Text);

                    tableData.Settings.DeselectedAttributes.Clear();
                    tableData.Settings.DeselectedAttributes.AddRange(deselected);
                }
            }
            catch (Exception ex)
            {
                ManageWorkingState(false);
                LogError(ex.Message);
                MessageBox.Show(ex.Message, "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void btnMappings_Click(object sender, EventArgs e)
        {
            if(Service == null) { return; }

            try
            {
                var mappingsDlg = new Mappings(Service, _sourceInstance, _targetInstance, _tables, _settings);
                mappingsDlg.ShowDialog(ParentForm);

                if (mappingsDlg.Updated)
                {
                    RenderMappingsButton();

                    SettingsHelper.SetSettings(_settings);
                    SendMessageToStatusBar?.Invoke(this, new StatusBarMessageEventArgs("Succesfully updated Organization Mappings"));
                }
            }
            catch (Exception ex)
            {
                ManageWorkingState(false);
                LogError(ex.Message);
                MessageBox.Show(ex.Message, "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void btnFetchXmlBuilder_Click(object sender, EventArgs e)
        {
            try
            {
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
                LogError(ex.Message);
                MessageBox.Show(ex.Message, "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void rtbFilter_TextChanged(object sender, EventArgs e)
        {
            try
            {
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
            }
            catch (Exception ex)
            {
                ManageWorkingState(false);
                LogError(ex.Message);
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
        #endregion Form events
    }
}