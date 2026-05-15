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
        private CrmServiceClient _executionTargetClientOverride;

        // main objects
        private Instance _sourceInstance;
        private Instance _targetInstance;
        private Instance _executionTargetInstanceOverride;
        private Dictionary<string, CrmServiceClient> _targetClients = new Dictionary<string, CrmServiceClient>(StringComparer.OrdinalIgnoreCase);
        private Dictionary<string, Instance> _targetInstances = new Dictionary<string, Instance>(StringComparer.OrdinalIgnoreCase);
        private IEnumerable<Table> _tables;
        private List<Mapping> _mappings;
        private IEnumerable<Sort> _sorts;
        private DmtSettings _dmtSettings;
        private string _dmtFilePath;
        private ExecutionPlan _executionPlan;
        private string _executionPlanFilePath;
        private TableSettings _currentTableSettings;
        private string _currentTableLogicalName;
        private string _previousTableLogicalName;
        private Timer _dmtAutoSaveTimer;
        private Timer _workingTipsTimer;
        private WorkingDialog _workingDialog;
        private string _currentWorkingMessage;
        private int _workingTipIndex = -1;

        // flags
        private bool _ready = false;
        private bool _working;
        private int _activeExcelImportOperationId;
        private bool _importPreviewDialogOpen;
        private bool _suppressTableSelectionChanged;
        private bool _suppressSettingsEvents;
        private bool _startupFilesDialogShown;
        private bool _startupGuideShown;

        private const int ExcelImportLargeRowWarning = 1000;
        private const int ExcelImportVeryLargeRowWarning = 5000;
        private const int ExcelImportHugeRowWarning = 20000;
        private static readonly string[] WorkingTips =
        {
            "Build a plan first, then validate it before execution. Even one-off operations follow the same flow.",
            "You can connect multiple target environments and run steps across them in a single execution plan.",
            "Each plan step has its own target. Select a step to quickly change its target in the plan grid.",
            "Linked imports can reuse the output from an earlier export step, which keeps file paths consistent.",
            "Validation checks disconnected targets, linked-step order, missing files, duplicate outputs, and preview counts where possible.",
            "Use variables such as {date}, {datetime}, {table}, {source}, {target}, and {planName} in plan file paths.",
            "Excel import write-back only fills generated GUIDs for workbook rows that did not already contain a main GUID.",
            "Saved .dmtplan.json files are snapshots, so plan steps are protected from later settings-file changes.",
            "Changing a step target refreshes that step's mapping snapshot and requires validation again.",
            "For chained plans, keep export steps before their linked imports; the plan review blocks invalid moves."
        };
        private GroupBox _executionPlanGroup;
        private SplitContainer _executionPlanSplitContainer;
        private ListView _executionPlanSteps;
        private readonly List<ComboBox> _executionPlanRowTargetEditors = new List<ComboBox>();
        private TextBox _executionPlanMessages;
        private System.Windows.Forms.Label _executionPlanSummary;
        private Button _executionPlanExecuteButton;
        private bool _suppressExecutionPlanStepChecked;
        private bool _suppressExecutionPlanInlineTargetChanged;
        private bool _executionPlanValidatedForExecution;
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
            tsmiExecutionPlan.Image = CreateExecutionPlanIcon();
            MoveImportSettingsIntoDialogs();
            InitializeDmtAutoSave();
            InitializeWorkingTips();
            RenderDmtFileMenu();

            _logger = new Logger();
            _logger.OnLog += Log;
        }

        public void DataMigrationControl_Load(object sender, EventArgs e)
        {
            _logger.Log(LogLevel.INFO, "Data Migration tool initialized");
            BeginInvoke(new System.Action(() =>
            {
                InitializeExecutionPlanPanel();
                RenderExecutionPlanMenu();
                ExecuteMethod(WhoAmI);
            }));
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
                    _startupFilesDialogShown = false;
                    _startupGuideShown = false;
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

                    RegisterTargetConnection(client, instance, makeDefault: true);

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

            ManageWorkingState(true, "Loading tables...");

            WorkAsync(new WorkAsyncInfo
            {
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
            BeginInvoke(new System.Action(() =>
            {
                ShowStartupGuide();
                ShowStartupFilesDialog();
            }));
        }

        private void RegisterTargetConnection(CrmServiceClient client, Instance instance, bool makeDefault)
        {
            if (client == null || string.IsNullOrWhiteSpace(client.ConnectedOrgUniqueName)) return;

            _targetClients[client.ConnectedOrgUniqueName] = client;
            if (instance != null)
                _targetInstances[client.ConnectedOrgUniqueName] = instance;

            if (makeDefault || _targetClient == null)
            {
                _targetClient = client;
                _targetInstance = instance;
            }

            UpdateExecutionPlanTargetEnvironments();
        }

        private CrmServiceClient ActiveTargetClient => _executionTargetClientOverride ?? _targetClient;
        private Instance ActiveTargetInstance => _executionTargetInstanceOverride ?? _targetInstance;

        private List<DmtEnvironmentInfo> GetLoadedTargetEnvironments()
        {
            return _targetClients.Values
                .Where(client => client != null && !string.IsNullOrWhiteSpace(client.ConnectedOrgUniqueName))
                .Select(client => new DmtEnvironmentInfo
                {
                    UniqueName = client.ConnectedOrgUniqueName,
                    FriendlyName = client.ConnectedOrgFriendlyName
                })
                .GroupBy(env => env.UniqueName, StringComparer.OrdinalIgnoreCase)
                .Select(group => group.First())
                .ToList();
        }

        private void UpdateExecutionPlanTargetEnvironments()
        {
            if (_executionPlan == null) return;

            _executionPlan.TargetEnvironments = _executionPlan.TargetEnvironments ?? new List<DmtEnvironmentInfo>();
            foreach (var env in GetLoadedTargetEnvironments())
            {
                if (!_executionPlan.TargetEnvironments.Any(existing => string.Equals(existing.UniqueName, env.UniqueName, StringComparison.OrdinalIgnoreCase)))
                    _executionPlan.TargetEnvironments.Add(env);
            }

            if (_executionPlan.TargetEnvironment == null && _targetClient != null)
            {
                _executionPlan.TargetEnvironment = new DmtEnvironmentInfo
                {
                    UniqueName = _targetClient.ConnectedOrgUniqueName,
                    FriendlyName = _targetClient.ConnectedOrgFriendlyName
                };
            }
            RenderExecutionPlanRowTargetEditors();
        }

        private bool TrySetExecutionTargetOverride(ExecutionPlanStep step, out string error)
        {
            error = null;
            _executionTargetClientOverride = null;
            _executionTargetInstanceOverride = null;

            var uniqueName = step?.TargetEnvironment?.UniqueName;
            if (string.IsNullOrWhiteSpace(uniqueName))
            {
                if (_targetClient == null)
                {
                    error = "No target connection is available.";
                    return false;
                }
                return true;
            }

            if (!_targetClients.TryGetValue(uniqueName, out var client) || client == null)
            {
                error = $"Target environment is not connected: {step.TargetEnvironment.FriendlyName ?? uniqueName}";
                return false;
            }

            _executionTargetClientOverride = client;
            _targetInstances.TryGetValue(uniqueName, out _executionTargetInstanceOverride);
            return true;
        }

        private void ClearExecutionTargetOverride()
        {
            _executionTargetClientOverride = null;
            _executionTargetInstanceOverride = null;
        }

        private bool SetExecutionTargetOverride(DmtEnvironmentInfo targetEnvironment)
        {
            _executionTargetClientOverride = null;
            _executionTargetInstanceOverride = null;
            if (targetEnvironment == null || string.IsNullOrWhiteSpace(targetEnvironment.UniqueName))
                return _targetClient != null;

            if (!_targetClients.TryGetValue(targetEnvironment.UniqueName, out var client) || client == null)
                return false;

            _executionTargetClientOverride = client;
            _targetInstances.TryGetValue(targetEnvironment.UniqueName, out _executionTargetInstanceOverride);
            return true;
        }

        private bool SelectDefaultTargetConnection(string uniqueName)
        {
            if (string.IsNullOrWhiteSpace(uniqueName)) return false;
            if (!_targetClients.TryGetValue(uniqueName, out var client) || client == null) return false;

            _targetClient = client;
            _targetInstances.TryGetValue(uniqueName, out _targetInstance);

            if (_sourceInstance?.Mappings != null)
            {
                _mappings = _sourceInstance.Mappings
                    .Where(map => _targetInstance == null || string.Equals(map.TargetInstanceName, _targetInstance.FriendlyName, StringComparison.OrdinalIgnoreCase))
                    .ToList();
                RenderMappingsButton();
            }

            UpdateExecutionPlanTargetEnvironments();
            return true;
        }

        private void ShowStartupGuide(bool force = false)
        {
            if (!force && (_startupGuideShown || _settings?.HideStartupGuide == true)) return;

            _startupGuideShown = true;
            using (var dlg = new StartupGuideDialog())
            {
                dlg.ShowDialog(ParentForm);
                if (dlg.HideOnStartup)
                {
                    _settings.HideStartupGuide = true;
                    SettingsHelper.SetSettings(_settings);
                }
            }
        }

        private void ShowStartupFilesDialog()
        {
            if (_startupFilesDialogShown || _tables == null || !_tables.Any()) return;
            if (_dmtSettings != null || !string.IsNullOrWhiteSpace(_dmtFilePath)) return;

            _startupFilesDialogShown = true;
            using (var dlg = new DmtSettingsFileDialog(
                _tables,
                _sourceClient?.ConnectedOrgUniqueName,
                _sourceClient?.ConnectedOrgFriendlyName,
                table => _settings.GetTableSettings(_tables, table.LogicalName)))
            {
                if (dlg.ShowDialog(ParentForm) != DialogResult.OK) return;

                if (dlg.Choice == DmtFileChoice.NewFile || dlg.Choice == DmtFileChoice.ExistingFile)
                {
                    ApplyDmtFileAndSelectTable(dlg.FilePath, dlg.LoadedSettings);
                }

                if (dlg.PlanChoice == ExecutionPlanFileChoice.NewFile)
                    CreateExecutionPlan(dlg.PlanFilePath);
                else if (dlg.PlanChoice == ExecutionPlanFileChoice.ExistingFile)
                    LoadExecutionPlan(dlg.PlanFilePath);
            }
        }

        private void LoadAttributes()
        {
            _logger.Log(LogLevel.INFO, $"Loading attributes...");

            if (_working) { return; }

            lvAttributes.Items.Clear();
            cbSelectAll.Checked = false;

            var tableData = GetSelectedTableItemData(false);
            if (tableData == null)
            {
                SendMessageToStatusBar?.Invoke(this, new StatusBarMessageEventArgs("Error loading attributes"));
                return;
            }

            ManageWorkingState(true, $"Loading {tableData.Table.LogicalName} attributes...");

            WorkAsync(new WorkAsyncInfo
            {
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

            ManageWorkingState(true, "Previewing operation...");

            var uiSettings = GetDefaultImportSettings(Enums.Action.Preview);

            tableData.SelectedAttributes = lvAttributes.CheckedItems
                .Cast<ListViewItem>()
                .Select(lvi => lvi.ToObject(new Models.Attribute()) as Models.Attribute)
                .Select(attr => tableData.Table.AllAttributes.FirstOrDefault(tblAttr => tblAttr.LogicalName.Equals(attr.LogicalName)))
                .ToList();

            WorkAsync(new WorkAsyncInfo
            {
                AsyncArgument = tableData,
                IsCancelable = true,
                Work = (worker, evt) =>
                {
                    var data = evt.Argument as TableData;

                    var logic = new DataLogic(worker, _sourceClient, ActiveTargetClient);
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

            var uiSettings = ReadSettings(Enums.Action.None);

            tableData.SelectedAttributes = lvAttributes.CheckedItems
                .Cast<ListViewItem>()
                .Select(lvi => lvi.ToObject(new Models.Attribute()) as Models.Attribute)
                .Select(attr => tableData.Table.AllAttributes.FirstOrDefault(tblAttr => tblAttr.LogicalName.Equals(attr.LogicalName)))
                .ToList();

            var filePath = this.SelectFile("Json files (*.json)|*.json", save: true,
                defaultFileName: GetDefaultSaveFileName(".json", tableData.Table.LogicalName));
            if (string.IsNullOrEmpty(filePath)) { return; }

            AddExportStepToExecutionPlan("ExportToJson", tableData, uiSettings, null, filePath);
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
            Forms.ExcelExportConfigDialog dlg = null;
            try
            {
                ManageWorkingState(true, "Preparing Excel export wizard...");
                Application.DoEvents();
                dlg = new Forms.ExcelExportConfigDialog(selectedAttrMeta, tableData.Metadata, repo, tableData.Settings.ExcelConfig);
            }
            finally
            {
                ManageWorkingState(false);
            }

            using (dlg)
            {
                var dialogResult = dlg.ShowDialog(ParentForm);
                if (dialogResult != System.Windows.Forms.DialogResult.OK && dialogResult != System.Windows.Forms.DialogResult.Yes) return;
                var config = dlg.Config;
                var uiSettings = ReadSettings(Enums.Action.None);
                config.ImportSettings = BuildExcelImportSettings(uiSettings, config);

                // persist the config so next open pre-populates automatically
                tableData.Settings.ExcelConfig = config;
                _currentTableLogicalName = tableData.Table.LogicalName;
                _currentTableSettings = tableData.Settings;
                SettingsHelper.SetSettings(_settings);
                AutoSaveDmtSettings();

                var filePath = this.SelectFile("Excel files (*.xlsx)|*.xlsx", save: true,
                    defaultFileName: GetDefaultSaveFileName(".xlsx", tableData.Table.LogicalName));
                if (string.IsNullOrEmpty(filePath)) return;

                AddExportStepToExecutionPlan("ExportToExcel", tableData, uiSettings, config, filePath);
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

            if (path == null)
            {
                var source = SelectImportSource("Import from Excel", "Excel files (*.xlsx)|*.xlsx", "ExportToExcel");
                if (source == null || source.Choice == ImportSourceChoice.Cancel) return;
                if (source.Choice == ImportSourceChoice.LinkedPlanStep)
                {
                    var linkedTarget = SelectOperationTargetEnvironment("Import Excel target");
                    if (linkedTarget == null && GetLoadedTargetEnvironments().Any()) return;
                    AddLinkedImportStepToExecutionPlan(source.SelectedStep, "ImportFromExcel", linkedTarget);
                    return;
                }
                path = source.FilePath;
            }
            if (string.IsNullOrEmpty(path)) return;

            if (!TrySelectImportPreviewTarget("Import Excel target", out var targetEnvironment)) return;

            var preflightLogic = new Logic.ExcelLogic();
            var rowCount = preflightLogic.GetImportRowCount(path, out ExcelExportConfig preflightConfig);
            if (!ValidateActiveSettingsTable(preflightConfig?.Table?.LogicalName, "Excel file")) return;
            if (!ConfirmLargeExcelImport(rowCount)) return;

            var operationId = BeginExcelImportOperation();
            var defaultImportSettings = BuildExcelImportSettings(GetDefaultImportSettings(Enums.Action.None), preflightConfig);
            ManageWorkingState(true, "Reading Excel file...");

            WorkAsync(new WorkAsyncInfo
            {
                AsyncArgument = new { path, operationId, defaultImportSettings },
                IsCancelable = true,
                Work = (worker, evt) =>
                {
                    try
                    {
                        dynamic args = evt.Argument;
                        string filePath = args.path;
                        var workbookDefaultImportSettings = args.defaultImportSettings as ExcelImportSettings;
                        worker.ReportProgress(0, "Excel import: reading workbook metadata...");
                        ThrowIfCancelled(worker);
                        var excelLogic = new Logic.ExcelLogic();
                        var target = targetEnvironment != null && _targetClients.TryGetValue(targetEnvironment.UniqueName, out CrmServiceClient selectedTarget)
                            ? selectedTarget
                            : ActiveTargetClient;

                        var collection = excelLogic.ImportFromExcel(
                            filePath,
                            out ExcelExportConfig config,
                            target,
                            worker,
                            importConfig =>
                            {
                                ThrowIfCancelled(worker);
                                EnsureExcelImportSettings(importConfig, workbookDefaultImportSettings);
                                ValidateActiveSettingsTable(importConfig?.Table?.LogicalName, "Excel file", promptUser: false);
                            });
                        ThrowIfCancelled(worker);
                        worker.ReportProgress(0, $"Excel import: read {collection?.Count ?? 0} records with {collection?.ImportErrors?.Count ?? 0} warning(s).");

                        evt.Result = new ExcelImportSession { FilePath = filePath, SourceType = "Excel", Config = config, Collection = collection, OperationId = args.operationId, TargetEnvironment = targetEnvironment };
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
                    var hasImportErrors = collection?.ImportErrors?.Any() == true;
                    if (collection == null || (collection.Count == 0 && !hasImportErrors))
                    {
                        var message = "No records found in the Excel file.";
                        MessageBox.Show(message, "Info", MessageBoxButtons.OK, MessageBoxIcon.Information);
                        return;
                    }

                    var tableData = GetTableDataByLogicalName(collection.LogicalName, false);
                    if (tableData == null)
                    {
                        MessageBox.Show($"Table '{collection.LogicalName}' not found. Please load tables first.", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                        return;
                    }

                    SelectTableForImportedFile(tableData);
                    var previewSettings = GetDefaultImportSettings(session.Config, Enums.Action.None);
                    StartExcelImportPreview(session, tableData, previewSettings);
                },
                ProgressChanged = ReportWorkProgress
            });
        }

        private void StartExcelImportPreview(ExcelImportSession session, TableData tableData, UiSettings uiSettings)
        {
            if (session != null && session.OperationId > 0 && !IsCurrentExcelImportOperation(session.OperationId)) return;
            ManageWorkingState(true, "Preparing import preview...");
            WorkAsync(new WorkAsyncInfo
            {
                AsyncArgument = new { session, tableData, uiSettings },
                IsCancelable = true,
                Work = (worker, evt) =>
                {
                    try
                    {
                        dynamic args = evt.Argument;
                        ThrowIfCancelled(worker);
                        worker.ReportProgress(0, "Import preview: comparing source rows with target records...");
                        var previewSession = args.session as ExcelImportSession;
                        var previousClientOverride = _executionTargetClientOverride;
                        var previousInstanceOverride = _executionTargetInstanceOverride;
                        try
                        {
                            SetExecutionTargetOverride(previewSession?.TargetEnvironment);
                            evt.Result = BuildExcelImportPreview(args.tableData, previewSession.Collection, previewSession.Config, args.uiSettings, previewSession.FilePath);
                        }
                        finally
                        {
                            _executionTargetClientOverride = previousClientOverride;
                            _executionTargetInstanceOverride = previousInstanceOverride;
                        }
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
                                ReloadExcelImportSession(session.FilePath, dlg.SelectedMatchKey, tableData, dlg.Settings, session.TargetEnvironment);
                            return;
                        }
                        if (result == DialogResult.OK || result == DialogResult.Yes || dlg.AddToPlanRequested)
                        {
                            SaveDmtImportSettings(dlg.Settings, dlg.SelectedMatchKey);
                            AddImportStepToExecutionPlan(session, tableData, dlg.Settings, dlg.SelectedMatchKey, preview);
                            return;
                        }
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

        private void ReloadExcelImportSession(string filePath, ExcelImportMatchKeySelection matchKey, TableData tableData, UiSettings uiSettings, DmtEnvironmentInfo targetEnvironment)
        {
            var operationId = BeginExcelImportOperation();
            ManageWorkingState(true, "Reading Excel file...");
            WorkAsync(new WorkAsyncInfo
            {
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
                        var reloadDefaultImportSettings = BuildExcelImportSettings(uiSettings, null);
                        worker.ReportProgress(0, "Excel import: re-reading workbook metadata...");
                        ThrowIfCancelled(worker);
                        var excelLogic = new Logic.ExcelLogic();
                        var target = targetEnvironment != null && _targetClients.TryGetValue(targetEnvironment.UniqueName, out CrmServiceClient selectedTarget)
                            ? selectedTarget
                            : ActiveTargetClient;
                        var collection = excelLogic.ImportFromExcel(
                            reloadFilePath,
                            out ExcelExportConfig config,
                            target,
                            worker,
                            importConfig =>
                            {
                                ThrowIfCancelled(worker);
                                EnsureExcelImportSettings(importConfig, reloadDefaultImportSettings);
                                ValidateActiveSettingsTable(importConfig?.Table?.LogicalName, "Excel file", promptUser: false);
                                worker.ReportProgress(0, "Excel import: applying selected match key...");
                                ApplyImportMatchKeySelection(importConfig, reloadMatchKey);
                                ThrowIfCancelled(worker);
                                worker.ReportProgress(0, $"Excel import: resolving rows using {importConfig.MatchKeyMode} match key...");
                            });
                        ThrowIfCancelled(worker);
                        worker.ReportProgress(0, $"Excel import: read {collection?.Count ?? 0} records with {collection?.ImportErrors?.Count ?? 0} warning(s).");
                        evt.Result = new ExcelImportSession { FilePath = reloadFilePath, SourceType = "Excel", Config = config, Collection = collection, OperationId = reloadOperationId, TargetEnvironment = targetEnvironment };
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

        private List<Mapping> BuildMappingsForImport(UiSettings uiSettings)
        {
            uiSettings = uiSettings ?? GetDefaultImportSettings(Enums.Action.None);
            var mappings = ActiveTargetInstance == null || _sourceInstance?.Mappings == null
                ? new List<Mapping>(_mappings ?? new List<Mapping>())
                : _sourceInstance.Mappings
                    .Where(map => string.Equals(map.TargetInstanceName, ActiveTargetInstance.FriendlyName, StringComparison.OrdinalIgnoreCase))
                    .ToList();
            if (ActiveTargetClient == null || ActiveTargetInstance == null || _sourceInstance == null)
                return mappings;

            if (uiSettings.MapUsers || uiSettings.MapTeams || uiSettings.MapBu)
            {
                var mappingsLogic = new MappingsLogic(_sourceClient, ActiveTargetClient);
                if (uiSettings.MapUsers)
                    mappings.AddRange(mappingsLogic.GetUserMappings(_sourceInstance.FriendlyName, ActiveTargetInstance.FriendlyName));
                if (uiSettings.MapTeams)
                    mappings.AddRange(mappingsLogic.GetTeamMappings(_sourceInstance.FriendlyName, ActiveTargetInstance.FriendlyName));
                if (uiSettings.MapBu)
                {
                    var buMapping = mappingsLogic.GetBusinessUnitMapping(_sourceInstance.FriendlyName, ActiveTargetInstance.FriendlyName);
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

        private HashSet<Guid> GetSuccessfulResultIds(IEnumerable<ListViewItem> resultItems)
        {
            return new HashSet<Guid>(GetSuccessfulResultIdMap(resultItems).Keys);
        }

        private Dictionary<Guid, Guid> GetSuccessfulResultIdMap(IEnumerable<ListViewItem> resultItems)
        {
            var ids = new Dictionary<Guid, Guid>();
            foreach (var item in resultItems ?? Enumerable.Empty<ListViewItem>())
            {
                if (item.SubItems.Count < 4) continue;

                var action = item.SubItems[0].Text;
                var description = item.SubItems[3].Text;
                if (!(action.Equals("Create", StringComparison.OrdinalIgnoreCase) || action.Equals("Update", StringComparison.OrdinalIgnoreCase)))
                    continue;
                if (description.StartsWith("ERROR:", StringComparison.OrdinalIgnoreCase))
                    continue;

                if (Guid.TryParse(item.SubItems[1].Text, out var id))
                    ids[id] = id;
            }

            return ids;
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
                SettingsSource = config?.ImportSettings != null ? "Excel export metadata" : "Current settings",
                TableLogicalName = collection.LogicalName,
                TargetName = ActiveTargetInstance?.FriendlyName ?? string.Empty,
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
                if (config != null && action == Enums.Action.Create && sourceRecord != null && !sourceRecord.PrimaryIdWasBlank)
                {
                    var suppliedGuidWarning = $"Row {rowNumber}, column '{collection.PrimaryIdAttribute}': supplied record GUID was not found in target. Create will use this GUID; clear it for a Dataverse-generated ID and workbook writeback.";
                    if (!warningsByRow.ContainsKey(rowNumber))
                        warningsByRow[rowNumber] = new List<string>();
                    if (!warningsByRow[rowNumber].Contains(suppliedGuidWarning))
                    {
                        warningsByRow[rowNumber].Add(suppliedGuidWarning);
                        preview.ImportErrors.Add(suppliedGuidWarning);
                    }
                    rowWarnings = string.Join(" | ", warningsByRow[rowNumber]);
                }
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

            if (config.ImportSettings != null)
            {
                config.ImportSettings.MatchKeyMode = config.MatchKeyMode;
                config.ImportSettings.MatchKeyFields = fields;
                config.ImportSettings.MatchAlternateKeyName = config.MatchAlternateKeyName;
            }
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

            var targetRepo = new CrmRepo(ActiveTargetClient);
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
            if (!ids.Any() || ActiveTargetClient == null) return existing;

            var repo = new CrmRepo(ActiveTargetClient);

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
            public DmtEnvironmentInfo TargetEnvironment { get; set; }
        }

        private class ExecutionPlanStepExecutionResult
        {
            public string Summary { get; set; }
            public int TotalRecords { get; set; }
            public int FailedRecords { get; set; }
            public decimal FailedPercent { get; set; }
            public bool HasFailures { get; set; }
            public bool ShouldStopPlan { get; set; }
        }

        private void Import(string path = null)
        {
            _logger.Log(LogLevel.INFO, $"Import operation...");
            AutoSaveDmtSettings();

            // get file path
            if (path == null)
            {
                var source = SelectImportSource("Import from JSON", "Json files (*.json)|*.json", "ExportToJson");
                if (source == null || source.Choice == ImportSourceChoice.Cancel) return;
                if (source.Choice == ImportSourceChoice.LinkedPlanStep)
                {
                    var linkedTarget = SelectOperationTargetEnvironment("Import JSON target");
                    if (linkedTarget == null && GetLoadedTargetEnvironments().Any()) return;
                    AddLinkedImportStepToExecutionPlan(source.SelectedStep, "ImportFromJson", linkedTarget);
                    return;
                }
                path = source.FilePath;
            }
            if (string.IsNullOrEmpty(path)) { return; }

            if (!TrySelectImportPreviewTarget("Import JSON target", out var targetEnvironment)) return;

            var json = File.ReadAllText(path);
            var importData = json.DeserializeObject<RecordCollection>();
            ImportFileDataChecks(importData);
            if (!ValidateActiveSettingsTable(importData.LogicalName, "JSON file")) return;

            var tableData = GetTableDataByLogicalName(importData.LogicalName, false);
            if (tableData == null)
            {
                SendMessageToStatusBar?.Invoke(this, new StatusBarMessageEventArgs("Import operation aborted"));
                return;
            }

            SelectTableForImportedFile(tableData);
            var session = new ExcelImportSession { FilePath = path, SourceType = "JSON", Collection = importData, TargetEnvironment = targetEnvironment };
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

            ManageWorkingState(true, "Generating automatic mappings...");

            var uiSettings = ReadSettings(Enums.Action.None);

            WorkAsync(new WorkAsyncInfo
            {
                IsCancelable = true,
                Work = (worker, evt) =>
                {
                    ClearMappings();

                    var mappingsLogic = new MappingsLogic(_sourceClient, ActiveTargetClient);

                    if (uiSettings.MapUsers)
                    {
                        var usrMappings = mappingsLogic.GetUserMappings(_sourceInstance.FriendlyName, ActiveTargetInstance.FriendlyName);
                        if (usrMappings.Any()) { _mappings.AddRange(usrMappings); }
                    }
                    if (uiSettings.MapTeams)
                    {
                        var teamMappings = mappingsLogic.GetTeamMappings(_sourceInstance.FriendlyName, ActiveTargetInstance.FriendlyName);
                        if (teamMappings.Any()) { _mappings.AddRange(teamMappings); }
                    }
                    if (uiSettings.MapBu)
                    {
                        var buMapping = mappingsLogic.GetBusinessUnitMapping(_sourceInstance.FriendlyName, ActiveTargetInstance.FriendlyName);
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

            if (_sourceClient == null || (targetRequired && ActiveTargetClient == null))
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

            if (_sourceClient == null || (targetRequired && ActiveTargetClient == null))
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

        private void SelectTableForImportedFile(TableData tableData)
        {
            if (tableData?.Table == null) return;

            _suppressTableSelectionChanged = true;
            try
            {
                SetSelectedTableItem(tableData);
            }
            finally
            {
                _suppressTableSelectionChanged = false;
            }

            _currentTableLogicalName = tableData.Table.LogicalName;
            _currentTableSettings = tableData.Settings;
            _previousTableLogicalName = tableData.Table.LogicalName;

            lvAttributes.Items.Clear();
            cbSelectAll.Checked = false;
            tableData.Table.AllAttributes = tableData.Metadata.Attributes
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

            LoadAttributesList(tableData);
            LoadFilters(tableData);
            RenderDmtFileMenu();
            SendMessageToStatusBar?.Invoke(this, new StatusBarMessageEventArgs($"Selected table from import file: {tableData.Table.LogicalName}"));
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
            AutoSaveDmtSettings();
            LoadDmtFileAndSelectTable();
        }

        private void LoadDmtFileAndSelectTable()
        {
            var filePath = this.SelectFile("DMT Settings (*.dmt.json)|*.dmt.json");
            if (string.IsNullOrWhiteSpace(filePath)) return;

            var settings = DmtFileService.Load(filePath);
            ApplyDmtFileAndSelectTable(filePath, settings);
        }

        private void ApplyDmtFileAndSelectTable(string filePath, DmtSettings settings)
        {
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

        private bool ValidateActiveSettingsTable(string sourceTableLogicalName, string sourceDescription, bool promptUser = true)
        {
            if (_dmtSettings?.Table == null || string.IsNullOrWhiteSpace(_dmtSettings.Table.LogicalName)) return true;
            if (string.IsNullOrWhiteSpace(sourceTableLogicalName)) return true;

            if (!sourceTableLogicalName.Equals(_dmtSettings.Table.LogicalName, StringComparison.OrdinalIgnoreCase))
            {
                var settingsName = string.IsNullOrWhiteSpace(_dmtFilePath)
                    ? "active settings"
                    : Path.GetFileName(_dmtFilePath);
                var message = $"The selected {sourceDescription} is for table '{sourceTableLogicalName}', but {settingsName} is for '{_dmtSettings.Table.LogicalName}'.{Environment.NewLine}{Environment.NewLine}"
                    + "Continue using the table/configuration from the selected import file?";

                if (!promptUser)
                {
                    _logger?.Log(LogLevel.WARN, message);
                    return true;
                }

                return MessageBox.Show(
                    message,
                    "Settings Table Mismatch",
                    MessageBoxButtons.YesNo,
                    MessageBoxIcon.Warning) == DialogResult.Yes;
            }

            return true;
        }

        private bool EnsureExecutionPlanLoaded()
        {
            if (_executionPlan != null && !string.IsNullOrWhiteSpace(_executionPlanFilePath)) return true;

            using (var dlg = new ExecutionPlanFileDialog())
            {
                if (dlg.ShowDialog(ParentForm) != DialogResult.OK || string.IsNullOrWhiteSpace(dlg.FilePath))
                    return false;

                if (dlg.Choice == ExecutionPlanFileChoice.NewFile)
                {
                    CreateExecutionPlan(dlg.FilePath);
                    return true;
                }

                if (dlg.Choice == ExecutionPlanFileChoice.ExistingFile)
                {
                    LoadExecutionPlan(dlg.FilePath);
                    return true;
                }
            }

            return false;
        }

        private void CreateExecutionPlan(string filePath = null)
        {
            if (string.IsNullOrWhiteSpace(filePath))
            {
                using (var dlg = new SaveFileDialog
                {
                    Title = "Create execution plan",
                    Filter = "DMT Execution Plan (*.dmtplan.json)|*.dmtplan.json",
                    DefaultExt = "dmtplan.json",
                    FileName = GetDefaultSaveFileName(".dmtplan.json", "migration-plan")
                })
                {
                    if (dlg.ShowDialog(ParentForm) != DialogResult.OK) return;
                    filePath = dlg.FileName;
                }
            }

            _executionPlan = ExecutionPlanFileService.CreateNew(
                filePath,
                _sourceClient?.ConnectedOrgUniqueName,
                _sourceClient?.ConnectedOrgFriendlyName,
                _targetClient?.ConnectedOrgUniqueName,
                _targetClient?.ConnectedOrgFriendlyName);
            UpdateExecutionPlanTargetEnvironments();
            _executionPlanFilePath = filePath;
            _executionPlanValidatedForExecution = false;
            AutoSaveExecutionPlan();
            RenderExecutionPlanMenu();
            SendMessageToStatusBar?.Invoke(this, new StatusBarMessageEventArgs($"Execution plan created: {Path.GetFileName(filePath)}"));
        }

        private void LoadExecutionPlan(string filePath = null)
        {
            if (string.IsNullOrWhiteSpace(filePath))
            {
                using (var dlg = new OpenFileDialog
                {
                    Title = "Load execution plan",
                    Filter = "DMT Execution Plan (*.dmtplan.json)|*.dmtplan.json"
                })
                {
                    if (dlg.ShowDialog(ParentForm) != DialogResult.OK) return;
                    filePath = dlg.FileName;
                }
            }

            _executionPlan = ExecutionPlanFileService.Load(filePath);
            _executionPlanFilePath = filePath;
            UpdateExecutionPlanTargetEnvironments();
            ExecutionPlanFileService.ValidatePlan(_executionPlan);
            _executionPlanValidatedForExecution = false;
            RenderExecutionPlanMenu();
            SendMessageToStatusBar?.Invoke(this, new StatusBarMessageEventArgs($"Execution plan loaded: {Path.GetFileName(filePath)}"));
        }

        private void SaveExecutionPlan()
        {
            if (_executionPlan == null || string.IsNullOrWhiteSpace(_executionPlanFilePath))
            {
                CreateExecutionPlan();
                return;
            }

            ExecutionPlanFileService.Save(_executionPlanFilePath, _executionPlan);
            RenderExecutionPlanMenu();
            SendMessageToStatusBar?.Invoke(this, new StatusBarMessageEventArgs($"Execution plan saved: {Path.GetFileName(_executionPlanFilePath)}"));
        }

        private void AutoSaveExecutionPlan(bool showStatus = false)
        {
            if (_executionPlan == null || string.IsNullOrWhiteSpace(_executionPlanFilePath)) return;

            ExecutionPlanFileService.Save(_executionPlanFilePath, _executionPlan);
            RenderExecutionPlanMenu();
            if (showStatus)
                SendMessageToStatusBar?.Invoke(this, new StatusBarMessageEventArgs($"Execution plan saved: {Path.GetFileName(_executionPlanFilePath)}"));
        }

        private void ReviewExecutionPlan()
        {
            if (!EnsureExecutionPlanLoaded()) return;

            using (var dlg = new ExecutionPlanDialog(_executionPlan))
            {
                dlg.ShowDialog(ParentForm);
                if (dlg.PlanChanged)
                {
                    _executionPlanValidatedForExecution = false;
                    AutoSaveExecutionPlan(true);
                }
            }
        }

        private void InitializeExecutionPlanPanel()
        {
            if (_executionPlanGroup != null) return;

            pnlMain.SuspendLayout();
            pnlMain.ColumnStyles.Clear();
            pnlMain.ColumnCount = 1;
            pnlMain.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            pnlMain.Controls.Remove(pnlBody);

            _executionPlanSplitContainer = new SplitContainer
            {
                Dock = DockStyle.Fill,
                Orientation = Orientation.Vertical,
                SplitterWidth = 6,
                FixedPanel = FixedPanel.None,
                Panel1MinSize = 1,
                Panel2MinSize = 1
            };
            _executionPlanSplitContainer.SplitterMoved += (sender, args) => EnforceExecutionPlanPanelWidth();
            _executionPlanSplitContainer.SizeChanged += (sender, args) => EnforceExecutionPlanPanelWidth();
            _executionPlanSplitContainer.Panel1.Controls.Add(pnlBody);
            pnlMain.Controls.Add(_executionPlanSplitContainer, 0, 0);

            _executionPlanGroup = new GroupBox
            {
                Text = "Execution Plan",
                Dock = DockStyle.Fill,
                Padding = new Padding(6)
            };

            var headerLayout = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 2,
                RowCount = 1,
                Margin = new Padding(0)
            };
            headerLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            headerLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 150));

            var layout = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                RowCount = 4,
                ColumnCount = 1
            };
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 26));
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 68F));
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 32F));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 74));

            _executionPlanSummary = new System.Windows.Forms.Label
            {
                Dock = DockStyle.Fill,
                TextAlign = ContentAlignment.MiddleLeft,
                AutoEllipsis = true
            };

            var planMenuStrip = new ToolStrip
            {
                Dock = DockStyle.Fill,
                GripStyle = ToolStripGripStyle.Hidden,
                RenderMode = ToolStripRenderMode.System,
                Padding = new Padding(0),
                Stretch = true
            };
            if (tsmiExecutionPlan.Owner != null)
                tsmiExecutionPlan.Owner.Items.Remove(tsmiExecutionPlan);
            planMenuStrip.Items.Add(tsmiExecutionPlan);
            headerLayout.Controls.Add(_executionPlanSummary, 0, 0);
            headerLayout.Controls.Add(planMenuStrip, 1, 0);

            _executionPlanSteps = new ListView
            {
                Dock = DockStyle.Fill,
                CheckBoxes = true,
                FullRowSelect = true,
                HideSelection = false,
                MultiSelect = false,
                View = View.Details
            };
            _executionPlanSteps.Columns.Add("#", 34);
            _executionPlanSteps.Columns.Add("Status", 76);
            _executionPlanSteps.Columns.Add("Environment", 110);
            _executionPlanSteps.Columns.Add("Step", 190);
            _executionPlanSteps.Columns.Add("Input/Output", 180);
            _executionPlanSteps.ItemChecked += ExecutionPlanStepChecked;
            _executionPlanSteps.SelectedIndexChanged += (sender, args) =>
            {
                RenderExecutionPlanRowTargetEditors();
                RenderExecutionPlanMessages();
            };
            _executionPlanSteps.MouseClick += (sender, args) => RenderExecutionPlanRowTargetEditors();
            _executionPlanSteps.MouseWheel += (sender, args) => BeginInvoke(new System.Action(RenderExecutionPlanRowTargetEditors));
            _executionPlanSteps.KeyDown += (sender, args) => BeginInvoke(new System.Action(RenderExecutionPlanRowTargetEditors));
            _executionPlanSteps.Resize += (sender, args) =>
            {
                ResizeExecutionPlanColumns();
                RenderExecutionPlanRowTargetEditors();
            };

            _executionPlanMessages = new TextBox
            {
                Dock = DockStyle.Fill,
                Multiline = true,
                ReadOnly = true,
                ScrollBars = ScrollBars.Vertical
            };

            var buttons = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 4,
                RowCount = 2
            };
            for (var i = 0; i < 4; i++)
                buttons.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 25F));
            buttons.RowStyles.Add(new RowStyle(SizeType.Percent, 50F));
            buttons.RowStyles.Add(new RowStyle(SizeType.Percent, 50F));

            AddPlanPanelButton(buttons, "New", 0, 0, (s, e) => CreateExecutionPlan());
            AddPlanPanelButton(buttons, "Load", 1, 0, (s, e) => LoadExecutionPlan());
            AddPlanPanelButton(buttons, "Save", 2, 0, (s, e) => SaveExecutionPlan());
            AddPlanPanelButton(buttons, "Validate", 3, 0, (s, e) => ValidateExecutionPlan());
            AddPlanPanelButton(buttons, "Up", 0, 1, (s, e) => MoveSelectedExecutionPlanStep(-1));
            AddPlanPanelButton(buttons, "Down", 1, 1, (s, e) => MoveSelectedExecutionPlanStep(1));
            AddPlanPanelButton(buttons, "Remove", 2, 1, (s, e) => RemoveSelectedExecutionPlanStep());
            _executionPlanExecuteButton = AddPlanPanelButton(buttons, "Execute", 3, 1, (s, e) => ExecuteExecutionPlan());

            layout.Controls.Add(headerLayout, 0, 0);
            layout.Controls.Add(_executionPlanSteps, 0, 1);
            layout.Controls.Add(_executionPlanMessages, 0, 2);
            layout.Controls.Add(buttons, 0, 3);
            _executionPlanGroup.Controls.Add(layout);

            _executionPlanSplitContainer.Panel2.Controls.Add(_executionPlanGroup);
            pnlMain.ResumeLayout();
            System.Action applySplitter = () =>
            {
                if (_executionPlanSplitContainer == null || _executionPlanSplitContainer.Width <= 0) return;
                SetExecutionPlanPanelWidthRatio(0.30m);
                ResizeExecutionPlanColumns();
            };
            if (IsHandleCreated)
                BeginInvoke(applySplitter);
            else
                HandleCreated += (s, e) => BeginInvoke(applySplitter);
            RenderExecutionPlanPanel();
        }

        private void SetExecutionPlanPanelWidthRatio(decimal ratio)
        {
            if (_executionPlanSplitContainer == null || _executionPlanSplitContainer.Width <= 0) return;

            ratio = Math.Max(0.25m, Math.Min(0.50m, ratio));
            var total = _executionPlanSplitContainer.Width - _executionPlanSplitContainer.SplitterWidth;
            if (total <= 0) return;

            var desiredPlanWidth = (int)Math.Round(total * ratio);
            _executionPlanSplitContainer.SplitterDistance = Math.Max(1, total - desiredPlanWidth);
        }

        private void EnforceExecutionPlanPanelWidth()
        {
            if (_executionPlanSplitContainer == null || _executionPlanSplitContainer.Width <= 0) return;

            var total = _executionPlanSplitContainer.Width - _executionPlanSplitContainer.SplitterWidth;
            if (total <= 0) return;

            var planWidth = _executionPlanSplitContainer.Panel2.Width;
            var min = (int)Math.Round(total * 0.25m);
            var max = (int)Math.Round(total * 0.50m);
            if (planWidth < min)
                _executionPlanSplitContainer.SplitterDistance = total - min;
            else if (planWidth > max)
                _executionPlanSplitContainer.SplitterDistance = total - max;

            ResizeExecutionPlanColumns();
        }

        private void ResizeExecutionPlanColumns()
        {
            if (_executionPlanSteps == null || _executionPlanSteps.Columns.Count < 5) return;

            var width = Math.Max(360, _executionPlanSteps.ClientSize.Width - SystemInformation.VerticalScrollBarWidth - 4);
            _executionPlanSteps.Columns[0].Width = 38;
            _executionPlanSteps.Columns[1].Width = 80;
            _executionPlanSteps.Columns[2].Width = Math.Max(180, (int)(width * 0.25));
            _executionPlanSteps.Columns[3].Width = Math.Max(120, (int)(width * 0.20));
            _executionPlanSteps.Columns[4].Width = Math.Max(90, width - _executionPlanSteps.Columns[0].Width - _executionPlanSteps.Columns[1].Width - _executionPlanSteps.Columns[2].Width - _executionPlanSteps.Columns[3].Width);
        }

        private Button AddPlanPanelButton(TableLayoutPanel panel, string text, int column, int row, EventHandler click)
        {
            var button = new Button
            {
                Text = text,
                Dock = DockStyle.Fill,
                Margin = new Padding(2)
            };
            button.Click += click;
            panel.Controls.Add(button, column, row);
            return button;
        }

        private sealed class ExecutionPlanTargetOption
        {
            public string UniqueName { get; set; }
            public string FriendlyName { get; set; }
            public string DisplayName { get; set; }
            public override string ToString()
            {
                if (!string.IsNullOrWhiteSpace(DisplayName)) return DisplayName;
                return string.IsNullOrWhiteSpace(FriendlyName) ? UniqueName : FriendlyName;
            }
        }

        private ExecutionPlanStep GetSelectedExecutionPlanStep()
        {
            return _executionPlanSteps?.SelectedItems.Count > 0
                ? _executionPlanSteps.SelectedItems[0].Tag as ExecutionPlanStep
                : null;
        }

        private void RefreshExecutionPlanStepMappingsForTarget(ExecutionPlanStep step)
        {
            if (step?.Snapshot == null) return;

            var settings = (step.Operation ?? string.Empty).StartsWith("Import", StringComparison.OrdinalIgnoreCase)
                ? step.Snapshot.ImportSettings
                : step.Snapshot.ExportSettings;
            if (settings == null) return;

            var previousClientOverride = _executionTargetClientOverride;
            var previousInstanceOverride = _executionTargetInstanceOverride;
            try
            {
                if (TrySetExecutionTargetOverride(step, out _))
                    step.Snapshot.Mappings = CloneMappings(BuildMappingsForImport(settings));
            }
            finally
            {
                _executionTargetClientOverride = previousClientOverride;
                _executionTargetInstanceOverride = previousInstanceOverride;
            }
        }

        private List<Mapping> BuildMappingsForStepTarget(ExecutionPlanStep step, UiSettings settings)
        {
            var previousClientOverride = _executionTargetClientOverride;
            var previousInstanceOverride = _executionTargetInstanceOverride;
            try
            {
                SetExecutionTargetOverride(step?.TargetEnvironment);
                return BuildMappingsForImport(settings);
            }
            finally
            {
                _executionTargetClientOverride = previousClientOverride;
                _executionTargetInstanceOverride = previousInstanceOverride;
            }
        }

        private void RenderExecutionPlanRowTargetEditors()
        {
            if (_executionPlanSteps == null) return;

            ClearExecutionPlanRowTargetEditors();
            var targets = GetLoadedTargetEnvironments();

            _suppressExecutionPlanInlineTargetChanged = true;
            foreach (ListViewItem item in _executionPlanSteps.Items)
            {
                if (!(item.Tag is ExecutionPlanStep step)) continue;
                if (item.SubItems.Count <= 2) continue;

                var bounds = item.SubItems[2].Bounds;
                if (bounds.Width <= 0 || bounds.Height <= 0) continue;
                if (bounds.Bottom < 0 || bounds.Top > _executionPlanSteps.ClientSize.Height) continue;

                var isExport = IsExportStep(step);
                var editor = new ComboBox
                {
                    DropDownStyle = ComboBoxStyle.DropDownList,
                    Bounds = new Rectangle(bounds.Left + 1, bounds.Top, Math.Max(80, bounds.Width - 2), Math.Max(22, bounds.Height + 2)),
                    Tag = step,
                    Enabled = !isExport
                };

                if (isExport)
                {
                    editor.Items.Add(new ExecutionPlanTargetOption
                    {
                        DisplayName = GetStepEnvironmentDisplay(step)
                    });
                    editor.SelectedIndex = 0;
                }
                else
                {
                    editor.Items.Add(new ExecutionPlanTargetOption
                    {
                        UniqueName = string.Empty,
                        FriendlyName = string.Empty,
                        DisplayName = targets.Any() ? "Set Environment..." : "Connect target first..."
                    });
                    foreach (var env in targets)
                    {
                        editor.Items.Add(new ExecutionPlanTargetOption
                        {
                            UniqueName = env.UniqueName,
                            FriendlyName = env.FriendlyName,
                            DisplayName = string.IsNullOrWhiteSpace(env.FriendlyName) ? env.UniqueName : env.FriendlyName
                        });
                    }

                    var selectedUniqueName = step.TargetEnvironment?.UniqueName;
                    for (var i = 0; i < editor.Items.Count; i++)
                    {
                        var option = editor.Items[i] as ExecutionPlanTargetOption;
                        if (string.Equals(option?.UniqueName, selectedUniqueName, StringComparison.OrdinalIgnoreCase))
                        {
                            editor.SelectedIndex = i;
                            break;
                        }
                    }
                    if (editor.SelectedIndex < 0)
                        editor.SelectedIndex = 0;
                }

                editor.SelectedIndexChanged += ExecutionPlanRowTargetEditorChanged;
                _executionPlanSteps.Controls.Add(editor);
                editor.BringToFront();
                _executionPlanRowTargetEditors.Add(editor);
            }
            _suppressExecutionPlanInlineTargetChanged = false;
        }

        private void ClearExecutionPlanRowTargetEditors()
        {
            foreach (var editor in _executionPlanRowTargetEditors.ToList())
            {
                editor.SelectedIndexChanged -= ExecutionPlanRowTargetEditorChanged;
                _executionPlanSteps?.Controls.Remove(editor);
                editor.Dispose();
            }
            _executionPlanRowTargetEditors.Clear();
        }

        private void ExecutionPlanRowTargetEditorChanged(object sender, EventArgs e)
        {
            if (_suppressExecutionPlanInlineTargetChanged) return;
            var editor = sender as ComboBox;
            var step = editor?.Tag as ExecutionPlanStep;
            var option = editor?.SelectedItem as ExecutionPlanTargetOption;
            if (step == null || option == null) return;
            if (string.IsNullOrWhiteSpace(option.UniqueName))
            {
                step.TargetEnvironment = null;
                RefreshExecutionPlanStepMappingsForTarget(step);
                _executionPlanValidatedForExecution = false;
                ExecutionPlanFileService.ValidatePlan(_executionPlan);
                if (!string.IsNullOrWhiteSpace(_executionPlanFilePath))
                {
                    ExecutionPlanFileService.Save(_executionPlanFilePath, _executionPlan);
                    SendMessageToStatusBar?.Invoke(this, new StatusBarMessageEventArgs($"Execution plan saved: {Path.GetFileName(_executionPlanFilePath)}"));
                }
                BeginInvoke(new System.Action(RenderExecutionPlanMenu));
                return;
            }

            step.TargetEnvironment = new DmtEnvironmentInfo
            {
                UniqueName = option.UniqueName,
                FriendlyName = option.FriendlyName
            };
            RefreshExecutionPlanStepMappingsForTarget(step);
            _executionPlanValidatedForExecution = false;
            ExecutionPlanFileService.ValidatePlan(_executionPlan);
            if (!string.IsNullOrWhiteSpace(_executionPlanFilePath))
            {
                ExecutionPlanFileService.Save(_executionPlanFilePath, _executionPlan);
                SendMessageToStatusBar?.Invoke(this, new StatusBarMessageEventArgs($"Execution plan saved: {Path.GetFileName(_executionPlanFilePath)}"));
            }
            BeginInvoke(new System.Action(RenderExecutionPlanMenu));
        }

        private void RenderExecutionPlanPanel()
        {
            if (_executionPlanSteps == null) return;

            ClearExecutionPlanRowTargetEditors();
            var selectedId = _executionPlanSteps.SelectedItems.Count > 0
                ? (_executionPlanSteps.SelectedItems[0].Tag as ExecutionPlanStep)?.Id
                : null;

            _executionPlanSteps.BeginUpdate();
            _executionPlanSteps.SuspendLayout();
            try
            {
                _suppressExecutionPlanStepChecked = true;
                _executionPlanSteps.Items.Clear();
                if (_executionPlan?.Steps != null)
                {
                    for (var i = 0; i < _executionPlan.Steps.Count; i++)
                    {
                        var step = _executionPlan.Steps[i];
                        var item = new ListViewItem((i + 1).ToString("00"))
                        {
                            Checked = step.Enabled,
                            Tag = step
                        };
                        item.SubItems.Add(step.Validation?.Status ?? "Unknown");
                        item.SubItems.Add(GetStepEnvironmentDisplay(step));
                        item.SubItems.Add(step.Name ?? GetOperationDisplayName(step.Operation));
                        item.SubItems.Add(GetExecutionPlanStepInputOutputText(step));
                        if (string.Equals(step.Validation?.Status, "Error", StringComparison.OrdinalIgnoreCase))
                            item.ForeColor = Color.DarkRed;
                        else if (string.Equals(step.Validation?.Status, "Warning", StringComparison.OrdinalIgnoreCase))
                            item.ForeColor = Color.DarkGoldenrod;
                        _executionPlanSteps.Items.Add(item);
                        if (!string.IsNullOrWhiteSpace(selectedId) && string.Equals(step.Id, selectedId, StringComparison.OrdinalIgnoreCase))
                            item.Selected = true;
                    }
                }
            }
            finally
            {
                _suppressExecutionPlanStepChecked = false;
                _executionPlanSteps.ResumeLayout();
                _executionPlanSteps.EndUpdate();
            }

            var hasPlan = _executionPlan != null && !string.IsNullOrWhiteSpace(_executionPlanFilePath);
            var errors = _executionPlan?.Steps?.Count(s => string.Equals(s.Validation?.Status, "Error", StringComparison.OrdinalIgnoreCase)) ?? 0;
            var warnings = _executionPlan?.Steps?.Count(s => string.Equals(s.Validation?.Status, "Warning", StringComparison.OrdinalIgnoreCase)) ?? 0;
            _executionPlanGroup.Text = hasPlan ? $"Execution Plan - {GetExecutionPlanDisplayName(_executionPlanFilePath)}" : "Execution Plan";
            _executionPlanSummary.Text = hasPlan
                ? $"{_executionPlan.Steps.Count} step(s), {errors} error(s), {warnings} warning(s)"
                : "No active plan";
            if (_executionPlanExecuteButton != null)
                _executionPlanExecuteButton.Enabled = CanExecuteValidatedExecutionPlan();
            RenderExecutionPlanRowTargetEditors();
            RenderExecutionPlanMessages();
        }

        private bool CanExecuteValidatedExecutionPlan()
        {
            return _executionPlanValidatedForExecution
                && _executionPlan?.Steps?.Any(s => s.Enabled) == true
                && !_executionPlan.Steps.Any(s => s.Enabled && string.Equals(s.Validation?.Status, "Error", StringComparison.OrdinalIgnoreCase));
        }

        private void InvalidateExecutionPlanValidation()
        {
            _executionPlanValidatedForExecution = false;
            RenderExecutionPlanMenu();
        }

        private string GetExecutionPlanStepInputOutputText(ExecutionPlanStep step)
        {
            if (string.Equals(step.Input?.Mode, "FromStepOutput", StringComparison.OrdinalIgnoreCase))
                return $"From Step {GetExecutionPlanStepNumber(step.Input.SourceStepId)}";
            if (!string.IsNullOrWhiteSpace(step.Output?.PathTemplate))
                return step.Output.PathTemplate;
            return step.Input?.Path ?? string.Empty;
        }

        private bool IsExportStep(ExecutionPlanStep step)
        {
            return (step?.Operation ?? string.Empty).StartsWith("Export", StringComparison.OrdinalIgnoreCase);
        }

        private string GetStepEnvironmentDisplay(ExecutionPlanStep step)
        {
            if (IsExportStep(step))
            {
                var source = _executionPlan?.SourceEnvironment;
                var sourceName = source?.FriendlyName ?? source?.UniqueName ?? _sourceClient?.ConnectedOrgFriendlyName ?? _sourceClient?.ConnectedOrgUniqueName;
                return string.IsNullOrWhiteSpace(sourceName) ? string.Empty : $"Source: {sourceName}";
            }

            return GetStepTargetDisplay(step);
        }

        private string GetStepTargetDisplay(ExecutionPlanStep step)
        {
            var env = step?.TargetEnvironment;
            if (env == null || string.IsNullOrWhiteSpace(env.UniqueName))
                return _targetClient?.ConnectedOrgFriendlyName ?? _targetClient?.ConnectedOrgUniqueName ?? string.Empty;
            return env.FriendlyName ?? env.UniqueName;
        }

        private int GetExecutionPlanStepNumber(string stepId)
        {
            var index = _executionPlan?.Steps?.FindIndex(s => string.Equals(s.Id, stepId, StringComparison.OrdinalIgnoreCase)) ?? -1;
            return index >= 0 ? index + 1 : 0;
        }

        private void RenderExecutionPlanMessages()
        {
            if (_executionPlanMessages == null) return;
            var step = _executionPlanSteps.SelectedItems.Count > 0 ? _executionPlanSteps.SelectedItems[0].Tag as ExecutionPlanStep : null;
            if (step == null)
            {
                _executionPlanMessages.Text = "Select a step to view details.";
                return;
            }

            var preview = step.Validation?.Preview;
            var lines = new List<string>
            {
                $"{step.Operation} - {step.Table?.LogicalName}",
                $"Environment: {GetStepEnvironmentDisplay(step)}",
                GetExecutionPlanStepInputOutputText(step)
            };
            if (preview != null)
                lines.Add($"Preview: {preview.Creates} create, {preview.Updates} update, {preview.Skips} skip, {preview.Warnings} warning(s)");

            var messages = step.Validation?.Messages ?? new List<ExecutionPlanValidationMessage>();
            lines.AddRange(messages.Select(m => $"{m.Severity}: {m.Message}"));
            _executionPlanMessages.Text = string.Join(Environment.NewLine, lines.Where(line => !string.IsNullOrWhiteSpace(line)));
        }

        private void ExecutionPlanStepChecked(object sender, ItemCheckedEventArgs e)
        {
            if (_suppressExecutionPlanStepChecked) return;
            if (!(e.Item.Tag is ExecutionPlanStep step)) return;

            step.Enabled = e.Item.Checked;
            ExecutionPlanFileService.ValidatePlan(_executionPlan);
            _executionPlanValidatedForExecution = false;
            AutoSaveExecutionPlan(true);
        }

        private void MoveSelectedExecutionPlanStep(int direction)
        {
            if (_executionPlan?.Steps == null || _executionPlanSteps.SelectedItems.Count == 0) return;
            var index = _executionPlanSteps.SelectedItems[0].Index;
            var newIndex = index + direction;
            if (newIndex < 0 || newIndex >= _executionPlan.Steps.Count) return;

            var step = _executionPlan.Steps[index];
            if (!CanMoveExecutionPlanStep(step, newIndex)) return;

            _executionPlan.Steps.RemoveAt(index);
            _executionPlan.Steps.Insert(newIndex, step);
            ExecutionPlanFileService.ValidatePlan(_executionPlan);
            _executionPlanValidatedForExecution = false;
            AutoSaveExecutionPlan(true);
            if (newIndex < _executionPlanSteps.Items.Count)
                _executionPlanSteps.Items[newIndex].Selected = true;
        }

        private bool CanMoveExecutionPlanStep(ExecutionPlanStep step, int newIndex)
        {
            if (string.Equals(step.Input?.Mode, "FromStepOutput", StringComparison.OrdinalIgnoreCase)
                && !string.IsNullOrWhiteSpace(step.Input.SourceStepId))
            {
                var sourceIndex = _executionPlan.Steps.FindIndex(s => string.Equals(s.Id, step.Input.SourceStepId, StringComparison.OrdinalIgnoreCase));
                if (sourceIndex >= 0 && newIndex <= sourceIndex)
                {
                    MessageBox.Show("This import is linked to an earlier export and must stay after it.", "Execution Plan", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    return false;
                }
            }

            var firstDependentIndex = _executionPlan.Steps
                .Select((candidate, index) => new { candidate, index })
                .Where(x => string.Equals(x.candidate.Input?.Mode, "FromStepOutput", StringComparison.OrdinalIgnoreCase)
                    && string.Equals(x.candidate.Input?.SourceStepId, step.Id, StringComparison.OrdinalIgnoreCase))
                .Select(x => x.index)
                .DefaultIfEmpty(int.MaxValue)
                .Min();
            if (newIndex >= firstDependentIndex)
            {
                MessageBox.Show("This export has linked import steps and must stay before them.", "Execution Plan", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return false;
            }

            return true;
        }

        private void RemoveSelectedExecutionPlanStep()
        {
            if (_executionPlan?.Steps == null || _executionPlanSteps.SelectedItems.Count == 0) return;
            var index = _executionPlanSteps.SelectedItems[0].Index;
            var step = _executionPlan.Steps[index];
            var dependents = _executionPlan.Steps
                .Where(s => string.Equals(s.Input?.Mode, "FromStepOutput", StringComparison.OrdinalIgnoreCase)
                    && string.Equals(s.Input?.SourceStepId, step.Id, StringComparison.OrdinalIgnoreCase))
                .ToList();
            var message = dependents.Any()
                ? "Remove this step and unlink dependent import step(s)?"
                : "Remove selected step from this plan?";
            if (MessageBox.Show(message, "Execution Plan", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;

            foreach (var dependent in dependents)
            {
                dependent.Input.Mode = "File";
                dependent.Input.SourceStepId = null;
            }
            _executionPlan.Steps.RemoveAt(index);
            ExecutionPlanFileService.ValidatePlan(_executionPlan);
            _executionPlanValidatedForExecution = false;
            AutoSaveExecutionPlan(true);
        }

        private void ValidateExecutionPlan()
        {
            if (!EnsureExecutionPlanLoaded()) return;
            UpdateExecutionPlanTargetEnvironments();

            ManageWorkingState(true, "Validating execution plan...");
            WorkAsync(new WorkAsyncInfo
            {
                AsyncArgument = _executionPlan,
                IsCancelable = false,
                Work = (worker, evt) =>
                {
                    var plan = evt.Argument as ExecutionPlan;
                    ValidateExecutionPlanInternal(plan, worker, true);
                    evt.Result = plan;
                },
                PostWorkCallBack = evt =>
                {
                    ManageWorkingState(false);
                    if (evt.Error != null)
                    {
                        _executionPlanValidatedForExecution = false;
                        RenderExecutionPlanMenu();
                        _logger.Log(LogLevel.ERROR, evt.Error.ToString());
                        MessageBox.Show(evt.Error.Message, "Execution Plan", MessageBoxButtons.OK, MessageBoxIcon.Error);
                        return;
                    }

                    AutoSaveExecutionPlan(true);

                    var errors = _executionPlan.Steps.Count(s => string.Equals(s.Validation?.Status, "Error", StringComparison.OrdinalIgnoreCase));
                    var warnings = _executionPlan.Steps.Count(s => string.Equals(s.Validation?.Status, "Warning", StringComparison.OrdinalIgnoreCase));
                    _executionPlanValidatedForExecution = !_executionPlan.Steps.Any(s => s.Enabled && string.Equals(s.Validation?.Status, "Error", StringComparison.OrdinalIgnoreCase));
                    RenderExecutionPlanMenu();
                    MessageBox.Show(
                        $"Validation complete.{Environment.NewLine}{Environment.NewLine}Steps: {_executionPlan.Steps.Count}{Environment.NewLine}Errors: {errors}{Environment.NewLine}Warnings: {warnings}",
                        "Execution Plan",
                        MessageBoxButtons.OK,
                        errors > 0 ? MessageBoxIcon.Error : warnings > 0 ? MessageBoxIcon.Warning : MessageBoxIcon.Information);
                    ReRenderComponents(true);
                },
                ProgressChanged = ReportWorkProgress
            });
        }

        private void ExecuteExecutionPlan()
        {
            if (!EnsureExecutionPlanLoaded()) return;

            if (!CanExecuteValidatedExecutionPlan())
            {
                MessageBox.Show("Validate the execution plan successfully before executing it.", "Execution Plan", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            ConfirmAndStartExecutionPlanRun();
        }

        private void ConfirmAndStartExecutionPlanRun()
        {
            var errors = _executionPlan.Steps.Count(s => s.Enabled && string.Equals(s.Validation?.Status, "Error", StringComparison.OrdinalIgnoreCase));
            if (errors > 0)
            {
                MessageBox.Show("Execution plan has validation errors. Review and fix the plan before executing.", "Execution Plan", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }

            var warnings = _executionPlan.Steps.Count(s => s.Enabled && string.Equals(s.Validation?.Status, "Warning", StringComparison.OrdinalIgnoreCase));
            if (warnings > 0)
            {
                var proceed = MessageBox.Show(
                    $"Execution plan has {warnings} warning(s). Continue anyway?",
                    "Execution Plan",
                    MessageBoxButtons.YesNo,
                    MessageBoxIcon.Warning);
                if (proceed != DialogResult.Yes) return;
            }

            StartExecutionPlanRun();
        }

        private void StartExecutionPlanRun()
        {
            ManageWorkingState(true, "Executing plan...");
            WorkAsync(new WorkAsyncInfo
            {
                AsyncArgument = _executionPlan,
                IsCancelable = false,
                Work = (worker, evt) =>
                {
                    var plan = evt.Argument as ExecutionPlan;
                    var runLog = new ExecutionPlanRunLog
                    {
                        PlanName = plan?.Name,
                        PlanFilePath = _executionPlanFilePath,
                        StartedOn = DateTime.UtcNow,
                        SourceEnvironment = new DmtEnvironmentInfo
                        {
                            UniqueName = _sourceClient?.ConnectedOrgUniqueName,
                            FriendlyName = _sourceClient?.ConnectedOrgFriendlyName
                        },
                        TargetEnvironment = new DmtEnvironmentInfo
                        {
                            UniqueName = ActiveTargetClient?.ConnectedOrgUniqueName,
                            FriendlyName = ActiveTargetClient?.ConnectedOrgFriendlyName
                        },
                        TargetEnvironments = GetLoadedTargetEnvironments()
                    };
                    var stepIndex = 0;
                    var failedStepIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                    foreach (var step in plan.Steps.Where(s => s.Enabled))
                    {
                        stepIndex++;
                        worker.ReportProgress(0, $"Execution plan: running step {stepIndex} - {step.Name}...");
                        var stepLog = new ExecutionPlanRunStepLog
                        {
                            Index = stepIndex,
                            StepId = step.Id,
                            Name = step.Name,
                            Operation = step.Operation,
                            TargetEnvironment = step.TargetEnvironment,
                            Path = ResolveExecutionStepPath(step, stepIndex)
                        };
                        try
                        {
                            if (IsBlockedByFailedDependency(step, failedStepIds))
                            {
                                stepLog.Status = "Skipped";
                                stepLog.Summary = $"{step.Name}: skipped because a linked dependency failed.";
                                failedStepIds.Add(step.Id);
                                runLog.Steps.Add(stepLog);
                                continue;
                            }

                            if (!TrySetExecutionTargetOverride(step, out var targetError))
                                throw new Exception(targetError);
                            var result = ExecuteExecutionPlanStep(step, stepIndex, worker);
                            stepLog.Summary = result.Summary;
                            stepLog.TotalRecords = result.TotalRecords;
                            stepLog.FailedRecords = result.FailedRecords;
                            stepLog.FailedPercent = result.FailedPercent;
                            stepLog.Status = result.ShouldStopPlan ? "Failed" : result.HasFailures ? "Warning" : "Success";

                            if (result.ShouldStopPlan)
                            {
                                failedStepIds.Add(step.Id);
                                runLog.Steps.Add(stepLog);
                                break;
                            }
                        }
                        catch (Exception ex)
                        {
                            stepLog.Status = "Failed";
                            stepLog.Error = ex.Message;
                            stepLog.Summary = $"{step.Name}: failed - {ex.Message}";
                            failedStepIds.Add(step.Id);
                            runLog.Steps.Add(stepLog);
                            if (GetStepStopOnFatalError(plan, step))
                                break;
                            continue;
                        }
                        finally
                        {
                            ClearExecutionTargetOverride();
                        }
                        runLog.Steps.Add(stepLog);
                    }

                    runLog.CompletedOn = DateTime.UtcNow;
                    evt.Result = runLog;
                },
                PostWorkCallBack = evt =>
                {
                    ManageWorkingState(false);
                    if (evt.Error != null)
                    {
                        _logger.Log(LogLevel.ERROR, evt.Error.ToString());
                        MessageBox.Show(evt.Error.Message, "Execution Plan", MessageBoxButtons.OK, MessageBoxIcon.Error);
                        return;
                    }

                    var runLog = evt.Result as ExecutionPlanRunLog;
                    if (runLog != null)
                    {
                        SaveExecutionPlanRunLog(runLog);
                        using (var dlg = new ExecutionPlanResultsDialog(runLog))
                            dlg.ShowDialog(ParentForm);
                    }
                    SendMessageToStatusBar?.Invoke(this, new StatusBarMessageEventArgs("Execution plan complete"));
                    ReRenderComponents(true);
                },
                ProgressChanged = ReportWorkProgress
            });
        }

        private ExecutionPlanStepExecutionResult ExecuteExecutionPlanStep(ExecutionPlanStep step, int stepIndex, BackgroundWorker worker)
        {
            var tableData = BuildTableDataForExecutionStep(step);
            var path = ResolveExecutionStepPath(step, stepIndex);

            switch (step.Operation)
            {
                case "ExportToJson":
                {
                    EnsureOutputDirectory(path);
                    var logic = new DataLogic(worker, _sourceClient, ActiveTargetClient);
                    logic.Export(tableData, step.Snapshot.ExportSettings ?? GetDefaultImportSettings(Enums.Action.None), path, step.Snapshot.Mappings, false);
                    _settings.LastDataFile = path;
                    return new ExecutionPlanStepExecutionResult { Summary = $"{step.Name}: exported JSON to {path}" };
                }
                case "ExportToExcel":
                {
                    EnsureOutputDirectory(path);
                    var logic = new DataLogic(worker, _sourceClient, ActiveTargetClient);
                    var sourceCollection = logic.GetSourceEntities(tableData, step.Snapshot.ExportSettings ?? GetDefaultImportSettings(Enums.Action.None));
                    var excelLogic = new Logic.ExcelLogic();
                    excelLogic.Export(step.Snapshot.ExcelConfig, sourceCollection, path, _sourceClient);
                    _settings.LastDataFile = path;
                    var count = sourceCollection?.Count() ?? 0;
                    return new ExecutionPlanStepExecutionResult { Summary = $"{step.Name}: exported Excel ({count} record(s)) to {path}", TotalRecords = count };
                }
                case "ImportFromJson":
                {
                    var json = File.ReadAllText(path);
                    var collection = json.DeserializeObject<RecordCollection>();
                    ImportFileDataChecks(collection);
                    var logic = new DataLogic(worker, _sourceClient, ActiveTargetClient);
                    var result = logic.Import(tableData, collection, step.Snapshot.ImportSettings ?? GetDefaultImportSettings(Enums.Action.None), step.Snapshot.Mappings, false);
                    return BuildExecutionStepResult(step, result, "imported JSON");
                }
                case "ImportFromExcel":
                {
                    var excelLogic = new Logic.ExcelLogic();
                    var collection = excelLogic.ImportFromExcel(
                        path,
                        out ExcelExportConfig config,
                        ActiveTargetClient,
                        worker,
                        importConfig =>
                        {
                            if (step.Snapshot.ExcelConfig != null)
                            {
                                importConfig.MatchKey = step.Snapshot.ExcelConfig.MatchKey;
                                importConfig.MatchKeyMode = step.Snapshot.ExcelConfig.MatchKeyMode;
                                importConfig.MatchKeys = step.Snapshot.ExcelConfig.MatchKeys;
                                importConfig.MatchAlternateKeyName = step.Snapshot.ExcelConfig.MatchAlternateKeyName;
                                importConfig.ImportSettings = step.Snapshot.ExcelConfig.ImportSettings;
                            }
                            EnsureExcelImportSettings(importConfig, BuildExcelImportSettings(step.Snapshot.ImportSettings, importConfig));
                        });
                    var logic = new DataLogic(worker, _sourceClient, ActiveTargetClient);
                    var result = logic.Import(tableData, collection, step.Snapshot.ImportSettings ?? GetDefaultImportSettings(config, Enums.Action.None), step.Snapshot.Mappings, false);
                    if (result != null && config != null)
                    {
                        var importedIds = result.SuccessfulIdMap ?? GetSuccessfulResultIdMap(result.Items);
                        if (importedIds.Any())
                            excelLogic.UpdateImportedGuids(path, config, collection, importedIds, worker);
                    }
                    return BuildExecutionStepResult(step, result, "imported Excel");
                }
                default:
                    throw new Exception($"Unsupported execution plan operation: {step.Operation}");
            }
        }

        private void ValidateExecutionPlanInternal(ExecutionPlan plan, BackgroundWorker worker, bool includePreviewCounts)
        {
            if (plan == null) return;

            ExecutionPlanFileService.ValidatePlan(plan);
            AddEnvironmentValidationMessages(plan);
            AddDuplicateOutputPathValidationMessages(plan);

            if (includePreviewCounts)
            {
                var stepIndex = 0;
                foreach (var step in plan.Steps.Where(s => s.Enabled))
                {
                    stepIndex++;
                    worker?.ReportProgress(0, $"Execution plan: validating step {stepIndex} - {step.Name}...");
                    try
                    {
                        if (!TrySetExecutionTargetOverride(step, out var targetError))
                            AddExecutionPlanValidationMessage(step, "Error", targetError);
                        else
                        {
                            ValidateExecutionPlanStepSnapshot(step);
                            if ((step.Operation ?? string.Empty).StartsWith("Import", StringComparison.OrdinalIgnoreCase))
                                RefreshExecutionPlanImportPreview(step, stepIndex, worker);
                        }
                        RefreshExecutionPlanStepStatus(step);
                    }
                    finally
                    {
                        ClearExecutionTargetOverride();
                    }
                }
            }
            else
            {
                foreach (var step in plan.Steps.Where(s => s.Enabled))
                    RefreshExecutionPlanStepStatus(step);
            }
        }

        private void AddEnvironmentValidationMessages(ExecutionPlan plan)
        {
            var firstEnabledStep = plan.Steps.FirstOrDefault(s => s.Enabled);
            if (firstEnabledStep == null) return;

            if (EnvironmentChanged(plan.SourceEnvironment, _sourceClient?.ConnectedOrgUniqueName))
                AddExecutionPlanValidationMessage(firstEnabledStep, "Warning", "Current source environment differs from the environment captured in the plan.");

            foreach (var step in plan.Steps.Where(s => s.Enabled && s.TargetEnvironment != null && !string.IsNullOrWhiteSpace(s.TargetEnvironment.UniqueName)))
            {
                if (!_targetClients.ContainsKey(step.TargetEnvironment.UniqueName))
                    AddExecutionPlanValidationMessage(step, "Error", $"Target environment is not connected: {step.TargetEnvironment.FriendlyName ?? step.TargetEnvironment.UniqueName}");
            }
        }

        private bool EnvironmentChanged(DmtEnvironmentInfo captured, string currentUniqueName)
        {
            if (captured == null || string.IsNullOrWhiteSpace(captured.UniqueName) || string.IsNullOrWhiteSpace(currentUniqueName)) return false;
            return !captured.UniqueName.Equals(currentUniqueName, StringComparison.OrdinalIgnoreCase);
        }

        private void AddDuplicateOutputPathValidationMessages(ExecutionPlan plan)
        {
            var exports = plan.Steps
                .Select((step, index) => new { step, index })
                .Where(x => x.step.Enabled && (x.step.Operation ?? string.Empty).StartsWith("Export", StringComparison.OrdinalIgnoreCase))
                .Select(x => new { x.step, path = ResolvePlanPath(x.step.Output?.PathTemplate, x.step, x.index + 1) })
                .Where(x => !string.IsNullOrWhiteSpace(x.path))
                .GroupBy(x => x.path, StringComparer.OrdinalIgnoreCase)
                .Where(g => g.Count() > 1);

            foreach (var group in exports)
            {
                foreach (var item in group)
                    AddExecutionPlanValidationMessage(item.step, "Warning", $"Another enabled export resolves to the same output path: {group.Key}");
            }
        }

        private void ValidateExecutionPlanStepSnapshot(ExecutionPlanStep step)
        {
            try
            {
                var tableData = BuildTableDataForExecutionStep(step);
                var selected = step.Snapshot?.SelectedAttributes ?? new List<string>();
                var missingAttributes = selected
                    .Where(name => !tableData.Table.AllAttributes.Any(attr => attr.LogicalName.Equals(name, StringComparison.OrdinalIgnoreCase)))
                    .ToList();
                foreach (var name in missingAttributes)
                    AddExecutionPlanValidationMessage(step, "Error", $"Captured attribute no longer exists on '{step.Table.LogicalName}': {name}");

                var mappingAttributes = (step.Snapshot?.Mappings ?? new List<Mapping>())
                    .Select(m => m.AttributeLogicalName)
                    .Where(name => !string.IsNullOrWhiteSpace(name))
                    .Distinct(StringComparer.OrdinalIgnoreCase);
                foreach (var name in mappingAttributes.Where(name => !tableData.Table.AllAttributes.Any(attr => attr.LogicalName.Equals(name, StringComparison.OrdinalIgnoreCase))))
                    AddExecutionPlanValidationMessage(step, "Warning", $"Captured mapping references an attribute that is not in the current table metadata: {name}");
            }
            catch (Exception ex)
            {
                AddExecutionPlanValidationMessage(step, "Error", $"Table validation failed: {ex.Message}");
            }
        }

        private void RefreshExecutionPlanImportPreview(ExecutionPlanStep step, int stepIndex, BackgroundWorker worker)
        {
            try
            {
                var path = ResolveExecutionStepPath(step, stepIndex);
                if (!string.IsNullOrWhiteSpace(path) && File.Exists(path))
                {
                    var preview = BuildExecutionPlanImportPreview(step, path, worker);
                    step.Validation.Preview = ToExecutionPlanPreviewSummary(preview, "Validation preview", false, false);
                    AddExecutionPlanPreviewMessages(step, preview);
                    return;
                }

                if (string.Equals(step.Input?.Mode, "FromStepOutput", StringComparison.OrdinalIgnoreCase))
                {
                    var estimate = EstimateLinkedImportPreview(step, worker);
                    if (estimate != null)
                    {
                        step.Validation.Preview = estimate;
                        AddExecutionPlanValidationMessage(step, "Info", "Linked export output does not exist yet; preview count is estimated from the export step.");
                        return;
                    }
                }

                if (!string.IsNullOrWhiteSpace(path))
                    AddExecutionPlanValidationMessage(step, "Warning", $"Import preview could not be refreshed because the input file does not exist: {path}");
            }
            catch (Exception ex)
            {
                AddExecutionPlanValidationMessage(step, "Error", $"Import preview failed: {ex.Message}");
                if (step.Validation.Preview != null)
                    step.Validation.Preview.IsStale = true;
            }
        }

        private ExcelImportPreview BuildExecutionPlanImportPreview(ExecutionPlanStep step, string path, BackgroundWorker worker)
        {
            var tableData = BuildTableDataForExecutionStep(step);
            if (string.Equals(step.Operation, "ImportFromExcel", StringComparison.OrdinalIgnoreCase))
            {
                var excelLogic = new Logic.ExcelLogic();
                var collection = excelLogic.ImportFromExcel(
                    path,
                    out ExcelExportConfig config,
                    ActiveTargetClient,
                    worker,
                    importConfig =>
                    {
                        if (step.Snapshot.ExcelConfig != null)
                        {
                            importConfig.MatchKey = step.Snapshot.ExcelConfig.MatchKey;
                            importConfig.MatchKeyMode = step.Snapshot.ExcelConfig.MatchKeyMode;
                            importConfig.MatchKeys = step.Snapshot.ExcelConfig.MatchKeys;
                            importConfig.MatchAlternateKeyName = step.Snapshot.ExcelConfig.MatchAlternateKeyName;
                            importConfig.ImportSettings = step.Snapshot.ExcelConfig.ImportSettings;
                        }
                        EnsureExcelImportSettings(importConfig, BuildExcelImportSettings(step.Snapshot.ImportSettings, importConfig));
                    });
                return BuildExcelImportPreview(tableData, collection, config, step.Snapshot.ImportSettings ?? GetDefaultImportSettings(config, Enums.Action.None), path);
            }

            var json = File.ReadAllText(path);
            var collectionFromJson = json.DeserializeObject<RecordCollection>();
            ImportFileDataChecks(collectionFromJson);
            return BuildExcelImportPreview(tableData, collectionFromJson, null, step.Snapshot.ImportSettings ?? GetDefaultImportSettings(Enums.Action.None), path);
        }

        private ExecutionPlanPreviewSummary EstimateLinkedImportPreview(ExecutionPlanStep importStep, BackgroundWorker worker)
        {
            var sourceStep = _executionPlan?.Steps.FirstOrDefault(s => string.Equals(s.Id, importStep.Input?.SourceStepId, StringComparison.OrdinalIgnoreCase));
            if (sourceStep == null || !(sourceStep.Operation ?? string.Empty).StartsWith("Export", StringComparison.OrdinalIgnoreCase)) return null;

            var tableData = BuildTableDataForExecutionStep(sourceStep);
            var previousClientOverride = _executionTargetClientOverride;
            var previousInstanceOverride = _executionTargetInstanceOverride;
            try
            {
                if (!TrySetExecutionTargetOverride(sourceStep, out _))
                    return null;
                var logic = new DataLogic(worker, _sourceClient, ActiveTargetClient);
                var rows = logic.GetSourceEntities(tableData, sourceStep.Snapshot.ExportSettings ?? GetDefaultImportSettings(Enums.Action.None)).Count();
                return new ExecutionPlanPreviewSummary
                {
                    Rows = rows,
                    Source = "Estimated from linked export",
                    IsEstimated = true,
                    IsStale = false
                };
            }
            finally
            {
                _executionTargetClientOverride = previousClientOverride;
                _executionTargetInstanceOverride = previousInstanceOverride;
            }
        }

        private ExecutionPlanPreviewSummary ToExecutionPlanPreviewSummary(ExcelImportPreview preview, string source, bool estimated, bool stale)
        {
            if (preview == null) return null;

            return new ExecutionPlanPreviewSummary
            {
                Rows = preview.TotalRows,
                Creates = preview.CreateCount,
                Updates = preview.UpdateCount,
                Skips = preview.SkippedCount,
                Warnings = preview.ImportErrors?.Count ?? 0,
                Errors = preview.Items?.Count(i => string.Equals(i.Action, "Skip", StringComparison.OrdinalIgnoreCase) && !string.IsNullOrWhiteSpace(i.Warnings)) ?? 0,
                Source = source,
                IsEstimated = estimated,
                IsStale = stale
            };
        }

        private void AddExecutionPlanPreviewMessages(ExecutionPlanStep step, ExcelImportPreview preview)
        {
            if (preview?.ImportErrors?.Any() != true) return;

            foreach (var warning in preview.ImportErrors.Take(5))
                AddExecutionPlanValidationMessage(step, "Warning", warning);
            if (preview.ImportErrors.Count > 5)
                AddExecutionPlanValidationMessage(step, "Warning", $"{preview.ImportErrors.Count - 5} additional import warning(s) are available in the preview.");
        }

        private ExecutionPlanStepExecutionResult BuildExecutionStepResult(ExecutionPlanStep step, OperationResult operationResult, string action)
        {
            var items = operationResult?.Items?.ToList() ?? new List<ListViewItem>();
            var failed = CountFailedExecutionItems(items);
            var total = items.Count;
            var percent = total > 0 ? decimal.Round((decimal)failed / total * 100m, 2) : 0m;
            var thresholdHit = failed > 0 && (failed >= GetStepMaxFailedRecords(_executionPlan, step) || percent >= GetStepMaxFailedPercent(_executionPlan, step));
            return new ExecutionPlanStepExecutionResult
            {
                TotalRecords = total,
                FailedRecords = failed,
                FailedPercent = percent,
                HasFailures = failed > 0,
                ShouldStopPlan = thresholdHit,
                Summary = thresholdHit
                    ? $"{step.Name}: {action} with {failed}/{total} failed record(s); failure threshold reached, plan stopped."
                    : $"{step.Name}: {action} ({total} result row(s), {failed} failed)"
            };
        }

        private int CountFailedExecutionItems(IEnumerable<ListViewItem> items)
        {
            return (items ?? Enumerable.Empty<ListViewItem>()).Count(item =>
            {
                var description = item.SubItems.Count > 0 ? item.SubItems[item.SubItems.Count - 1].Text : string.Empty;
                return description.StartsWith("ERROR:", StringComparison.OrdinalIgnoreCase)
                    || description.IndexOf("ERROR:", StringComparison.OrdinalIgnoreCase) >= 0;
            });
        }

        private bool IsBlockedByFailedDependency(ExecutionPlanStep step, HashSet<string> failedStepIds)
        {
            return string.Equals(step.Input?.Mode, "FromStepOutput", StringComparison.OrdinalIgnoreCase)
                && !string.IsNullOrWhiteSpace(step.Input.SourceStepId)
                && failedStepIds.Contains(step.Input.SourceStepId);
        }

        private bool GetStepStopOnFatalError(ExecutionPlan plan, ExecutionPlanStep step)
        {
            return step.FailurePolicy?.StopOnFatalError ?? plan?.Defaults?.StopOnFatalError ?? true;
        }

        private int GetStepMaxFailedRecords(ExecutionPlan plan, ExecutionPlanStep step)
        {
            return step.FailurePolicy?.MaxFailedRecords ?? plan?.Defaults?.MaxFailedRecords ?? 10;
        }

        private decimal GetStepMaxFailedPercent(ExecutionPlan plan, ExecutionPlanStep step)
        {
            return step.FailurePolicy?.MaxFailedPercent ?? plan?.Defaults?.MaxFailedPercent ?? 20m;
        }

        private void AddExecutionPlanValidationMessage(ExecutionPlanStep step, string severity, string message)
        {
            step.Validation = step.Validation ?? new ExecutionPlanValidation();
            step.Validation.Messages = step.Validation.Messages ?? new List<ExecutionPlanValidationMessage>();
            step.Validation.Messages.Add(new ExecutionPlanValidationMessage { Severity = severity, Message = message });
            RefreshExecutionPlanStepStatus(step);
        }

        private void RefreshExecutionPlanStepStatus(ExecutionPlanStep step)
        {
            step.Validation = step.Validation ?? new ExecutionPlanValidation();
            step.Validation.ValidatedAt = DateTime.UtcNow;
            step.Validation.Status = step.Validation.Messages.Any(m => string.Equals(m.Severity, "Error", StringComparison.OrdinalIgnoreCase))
                ? "Error"
                : step.Validation.Messages.Any(m => string.Equals(m.Severity, "Warning", StringComparison.OrdinalIgnoreCase))
                    ? "Warning"
                    : "Ready";
        }

        private void SaveExecutionPlanRunLog(ExecutionPlanRunLog runLog)
        {
            if (runLog == null || string.IsNullOrWhiteSpace(_executionPlanFilePath)) return;

            var directory = Path.GetDirectoryName(_executionPlanFilePath);
            if (string.IsNullOrWhiteSpace(directory)) directory = Environment.CurrentDirectory;
            var fileName = $"{SanitizePathToken(_executionPlan?.Name ?? "execution-plan")}.{DateTime.Now:yyyy-MM-dd_HHmm}.run.json";
            var path = Path.Combine(directory, fileName);
            ExecutionPlanFileService.SaveRunLog(path, runLog);
        }

        private string ResolveExecutionStepPath(ExecutionPlanStep step, int stepIndex)
        {
            if (string.Equals(step.Input?.Mode, "FromStepOutput", StringComparison.OrdinalIgnoreCase))
            {
                var sourceIndex = _executionPlan.Steps.FindIndex(s => string.Equals(s.Id, step.Input.SourceStepId, StringComparison.OrdinalIgnoreCase));
                if (sourceIndex < 0) throw new Exception($"Linked source step was not found for '{step.Name}'.");

                var sourceStep = _executionPlan.Steps[sourceIndex];
                return ResolvePlanPath(sourceStep.Output?.PathTemplate, sourceStep, sourceIndex + 1);
            }

            return ResolvePlanPath(!string.IsNullOrWhiteSpace(step.Output?.PathTemplate) ? step.Output.PathTemplate : step.Input?.Path, step, stepIndex);
        }

        private TableData BuildTableDataForExecutionStep(ExecutionPlanStep step)
        {
            var tableData = GetTableDataByLogicalName(step.Table.LogicalName, false);
            tableData.Settings = tableData.Settings ?? new TableSettings();
            tableData.Settings.Filter = step.Snapshot.Filter;
            tableData.SelectedAttributes = (step.Snapshot.SelectedAttributes ?? new List<string>())
                .Select(name => tableData.Table.AllAttributes.FirstOrDefault(a => string.Equals(a.LogicalName, name, StringComparison.OrdinalIgnoreCase)))
                .Where(attr => attr != null)
                .ToList();
            if (!tableData.SelectedAttributes.Any())
                tableData.SelectedAttributes = tableData.Table.AllAttributes.ToList();
            return tableData;
        }

        private string ResolvePlanPath(string template, ExecutionPlanStep step, int stepIndex)
        {
            if (string.IsNullOrWhiteSpace(template)) return template;

            var now = DateTime.Now;
            var targetName = step?.TargetEnvironment?.FriendlyName
                ?? step?.TargetEnvironment?.UniqueName
                ?? ActiveTargetClient?.ConnectedOrgFriendlyName
                ?? ActiveTargetClient?.ConnectedOrgUniqueName;
            return template
                .Replace("{date}", now.ToString("yyyy-MM-dd"))
                .Replace("{datetime}", now.ToString("yyyy-MM-dd_HHmm"))
                .Replace("{table}", SanitizePathToken(step.Table?.LogicalName))
                .Replace("{stepIndex}", stepIndex.ToString("00"))
                .Replace("{stepName}", SanitizePathToken(step.Name))
                .Replace("{source}", SanitizePathToken(_sourceClient?.ConnectedOrgFriendlyName ?? _sourceClient?.ConnectedOrgUniqueName))
                .Replace("{target}", SanitizePathToken(targetName))
                .Replace("{planName}", SanitizePathToken(_executionPlan?.Name));
        }

        private string SanitizePathToken(string value)
        {
            if (string.IsNullOrWhiteSpace(value)) return string.Empty;
            var invalid = Path.GetInvalidFileNameChars();
            return new string(value.Select(ch => invalid.Contains(ch) ? '_' : ch).ToArray());
        }

        private void EnsureOutputDirectory(string path)
        {
            var directory = Path.GetDirectoryName(path);
            if (!string.IsNullOrWhiteSpace(directory) && !Directory.Exists(directory))
                Directory.CreateDirectory(directory);
        }

        private void CloseExecutionPlan()
        {
            AutoSaveExecutionPlan();
            _executionPlan = null;
            _executionPlanFilePath = null;
            _executionPlanValidatedForExecution = false;
            RenderExecutionPlanMenu();
            SendMessageToStatusBar?.Invoke(this, new StatusBarMessageEventArgs("Execution plan closed"));
        }

        private void AddExportStepToExecutionPlan(string operation, TableData tableData, UiSettings uiSettings, ExcelExportConfig excelConfig, string outputPath)
        {
            if (!EnsureExecutionPlanLoaded()) return;

            var step = CreateBaseExecutionPlanStep(operation, tableData);
            step.TargetEnvironment = null;
            step.Name = $"{GetOperationDisplayName(operation)} {tableData.Table.DisplayName}";
            step.Output.PathTemplate = outputPath;
            step.Snapshot.ExportSettings = uiSettings;
            step.Snapshot.ExcelConfig = excelConfig;
            step.Snapshot.SelectedAttributes = (tableData.SelectedAttributes ?? Enumerable.Empty<Models.Attribute>())
                .Select(a => a.LogicalName)
                .Where(name => !string.IsNullOrWhiteSpace(name))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
            step.Snapshot.Filter = tableData.Settings?.Filter;
            step.Snapshot.Mappings = CloneMappings(BuildMappingsForStepTarget(step, uiSettings));

            _executionPlan.Steps.Add(step);
            ExecutionPlanFileService.ValidatePlan(_executionPlan);
            _executionPlanValidatedForExecution = false;
            AutoSaveExecutionPlan(true);
            SendMessageToStatusBar?.Invoke(this, new StatusBarMessageEventArgs($"Added '{step.Name}' to execution plan"));
        }

        private void AddImportStepToExecutionPlan(ExcelImportSession session, TableData tableData, UiSettings uiSettings, ExcelImportMatchKeySelection matchKey, ExcelImportPreview preview)
        {
            if (!EnsureExecutionPlanLoaded()) return;

            var operation = string.Equals(session.SourceType, "JSON", StringComparison.OrdinalIgnoreCase)
                ? "ImportFromJson"
                : "ImportFromExcel";
            var step = CreateBaseExecutionPlanStep(operation, tableData);
            if (session.TargetEnvironment != null)
                step.TargetEnvironment = session.TargetEnvironment;
            step.Name = $"{GetOperationDisplayName(operation)} {tableData.Table.DisplayName}";
            step.Input.Path = session.FilePath;
            ApplyAutomaticStepLink(step, session.FilePath);
            step.Snapshot.ImportSettings = uiSettings;
            step.Snapshot.ExcelConfig = session.Config;
            step.Snapshot.Mappings = CloneMappings(BuildMappingsForStepTarget(step, uiSettings));
            step.Validation.Preview = preview == null ? null : new ExecutionPlanPreviewSummary
            {
                Rows = preview.TotalRows,
                Creates = preview.CreateCount,
                Updates = preview.UpdateCount,
                Skips = preview.SkippedCount,
                Warnings = preview.ImportErrors?.Count ?? 0,
                Errors = preview.Items?.Count(i => string.Equals(i.Action, "Skip", StringComparison.OrdinalIgnoreCase) && !string.IsNullOrWhiteSpace(i.Warnings)) ?? 0,
                Source = "Captured preview",
                IsEstimated = false,
                IsStale = false
            };

            if (matchKey != null && step.Snapshot.ExcelConfig != null)
                ApplyImportMatchKeySelection(step.Snapshot.ExcelConfig, matchKey);

            _executionPlan.Steps.Add(step);
            ExecutionPlanFileService.ValidatePlan(_executionPlan);
            _executionPlanValidatedForExecution = false;
            AutoSaveExecutionPlan(true);
            SendMessageToStatusBar?.Invoke(this, new StatusBarMessageEventArgs($"Added '{step.Name}' to execution plan"));
        }

        private ImportSourceDialog SelectImportSource(string title, string fileFilter, string linkedExportOperation)
        {
            var linkedSteps = GetCompatiblePlanExportSteps(linkedExportOperation);
            using (var dlg = new ImportSourceDialog(title, fileFilter, linkedSteps))
            {
                return dlg.ShowDialog(ParentForm) == DialogResult.OK ? dlg : null;
            }
        }

        private DmtEnvironmentInfo SelectOperationTargetEnvironment(string title)
        {
            var targets = GetLoadedTargetEnvironments();
            if (!targets.Any()) return null;
            if (targets.Count == 1) return targets[0];

            using (var dialog = new Form
            {
                Text = title,
                StartPosition = FormStartPosition.CenterParent,
                ShowIcon = false,
                ShowInTaskbar = false,
                FormBorderStyle = FormBorderStyle.FixedDialog,
                MaximizeBox = false,
                MinimizeBox = false,
                ClientSize = new Size(460, 132)
            })
            {
                var label = new System.Windows.Forms.Label
                {
                    Text = "Select the target environment for this operation.",
                    Left = 12,
                    Top = 14,
                    Width = 430,
                    Height = 22
                };
                var combo = new ComboBox
                {
                    Left = 12,
                    Top = 42,
                    Width = 430,
                    DropDownStyle = ComboBoxStyle.DropDownList
                };
                foreach (var target in targets)
                {
                    combo.Items.Add(new ExecutionPlanTargetOption
                    {
                        UniqueName = target.UniqueName,
                        FriendlyName = target.FriendlyName,
                        DisplayName = string.IsNullOrWhiteSpace(target.FriendlyName) ? target.UniqueName : target.FriendlyName
                    });
                }

                var defaultUniqueName = _targetClient?.ConnectedOrgUniqueName;
                for (var i = 0; i < combo.Items.Count; i++)
                {
                    var option = combo.Items[i] as ExecutionPlanTargetOption;
                    if (string.Equals(option?.UniqueName, defaultUniqueName, StringComparison.OrdinalIgnoreCase))
                    {
                        combo.SelectedIndex = i;
                        break;
                    }
                }
                if (combo.SelectedIndex < 0) combo.SelectedIndex = 0;

                var add = new Button { Text = "Add to Plan", Width = 100, Height = 28, Left = 246, Top = 88, DialogResult = DialogResult.OK };
                var cancel = new Button { Text = "Cancel", Width = 84, Height = 28, Left = 358, Top = 88, DialogResult = DialogResult.Cancel };
                dialog.Controls.Add(label);
                dialog.Controls.Add(combo);
                dialog.Controls.Add(add);
                dialog.Controls.Add(cancel);
                dialog.AcceptButton = add;
                dialog.CancelButton = cancel;

                if (dialog.ShowDialog(ParentForm) != DialogResult.OK) return null;

                var selected = combo.SelectedItem as ExecutionPlanTargetOption;
                return selected == null ? null : new DmtEnvironmentInfo
                {
                    UniqueName = selected.UniqueName,
                    FriendlyName = selected.FriendlyName
                };
            }
        }

        private bool TrySelectImportPreviewTarget(string title, out DmtEnvironmentInfo targetEnvironment)
        {
            targetEnvironment = null;
            if (!GetLoadedTargetEnvironments().Any())
            {
                MessageBox.Show(
                    "Connect a target environment before configuring an import from a file.\n\n" +
                    "The import preview needs a target to determine which rows will be created or updated and to resolve match keys/lookups correctly.",
                    "Target Required",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Warning);
                SendMessageToStatusBar?.Invoke(this, new StatusBarMessageEventArgs("Import configuration requires a target environment"));
                return false;
            }

            targetEnvironment = SelectOperationTargetEnvironment(title);
            return targetEnvironment != null;
        }

        private List<ExecutionPlanStep> GetCompatiblePlanExportSteps(string exportOperation)
        {
            return (_executionPlan?.Steps ?? new List<ExecutionPlanStep>())
                .Where(step => step.Enabled
                    && string.Equals(step.Operation, exportOperation, StringComparison.OrdinalIgnoreCase)
                    && !string.IsNullOrWhiteSpace(step.Output?.PathTemplate)
                    && !string.IsNullOrWhiteSpace(step.Table?.LogicalName))
                .ToList();
        }

        private void AddLinkedImportStepToExecutionPlan(ExecutionPlanStep sourceStep, string importOperation, DmtEnvironmentInfo targetEnvironment)
        {
            if (sourceStep == null) return;
            if (!EnsureExecutionPlanLoaded()) return;

            var tableData = BuildTableDataForExecutionStep(sourceStep);
            var uiSettings = ReadSettings(Enums.Action.None);
            var step = CreateBaseExecutionPlanStep(importOperation, tableData);
            if (targetEnvironment != null)
                step.TargetEnvironment = targetEnvironment;
            step.Name = $"{GetOperationDisplayName(importOperation)} {tableData.Table.DisplayName}";
            step.Input.Mode = "FromStepOutput";
            step.Input.SourceStepId = sourceStep.Id;
            step.Input.Path = null;
            step.Snapshot.ImportSettings = uiSettings;
            step.Snapshot.ExcelConfig = CloneExcelConfig(sourceStep.Snapshot?.ExcelConfig);
            step.Snapshot.SelectedAttributes = sourceStep.Snapshot?.SelectedAttributes?.ToList() ?? new List<string>();
            step.Snapshot.Filter = sourceStep.Snapshot?.Filter;
            step.Snapshot.Mappings = CloneMappings(BuildMappingsForStepTarget(step, uiSettings));
            step.Validation.Preview = new ExecutionPlanPreviewSummary
            {
                Rows = sourceStep.Validation?.Preview?.Rows ?? 0,
                Source = "Linked export output",
                IsEstimated = true,
                IsStale = true
            };

            var sourceIndex = _executionPlan.Steps.FindIndex(s => string.Equals(s.Id, sourceStep.Id, StringComparison.OrdinalIgnoreCase));
            if (sourceIndex >= 0)
                _executionPlan.Steps.Insert(sourceIndex + 1, step);
            else
                _executionPlan.Steps.Add(step);
            ExecutionPlanFileService.ValidatePlan(_executionPlan);
            _executionPlanValidatedForExecution = false;
            AutoSaveExecutionPlan(true);
            SendMessageToStatusBar?.Invoke(this, new StatusBarMessageEventArgs($"Added linked '{step.Name}' to execution plan"));
        }

        private ExcelExportConfig CloneExcelConfig(ExcelExportConfig config)
        {
            if (config == null) return null;
            return config.SerializeObject().DeserializeObject<ExcelExportConfig>();
        }

        private ExecutionPlanStep CreateBaseExecutionPlanStep(string operation, TableData tableData)
        {
            UpdateExecutionPlanTargetEnvironments();
            return new ExecutionPlanStep
            {
                Operation = operation,
                Table = new DmtTableInfo
                {
                    LogicalName = tableData.Table.LogicalName,
                    DisplayName = tableData.Table.DisplayName,
                    PrimaryIdAttribute = tableData.Table.IdAttribute,
                    PrimaryNameAttribute = tableData.Table.NameAttribute
                },
                TargetEnvironment = ActiveTargetClient == null ? null : new DmtEnvironmentInfo
                {
                    UniqueName = ActiveTargetClient.ConnectedOrgUniqueName,
                    FriendlyName = ActiveTargetClient.ConnectedOrgFriendlyName
                },
                SettingsProvenance = new ExecutionPlanSettingsProvenance
                {
                    SettingsFilePath = _dmtFilePath,
                    CapturedAt = DateTime.UtcNow
                }
            };
        }

        private void ApplyAutomaticStepLink(ExecutionPlanStep importStep, string inputPath)
        {
            if (_executionPlan?.Steps == null || string.IsNullOrWhiteSpace(inputPath)) return;

            var expectedExportOperation = string.Equals(importStep.Operation, "ImportFromExcel", StringComparison.OrdinalIgnoreCase)
                ? "ExportToExcel"
                : string.Equals(importStep.Operation, "ImportFromJson", StringComparison.OrdinalIgnoreCase)
                    ? "ExportToJson"
                    : null;
            if (expectedExportOperation == null) return;

            var sourceStep = _executionPlan.Steps.LastOrDefault(step =>
                string.Equals(step.Operation, expectedExportOperation, StringComparison.OrdinalIgnoreCase)
                && string.Equals(step.Table?.LogicalName, importStep.Table?.LogicalName, StringComparison.OrdinalIgnoreCase)
                && PathsEqual(step.Output?.PathTemplate, inputPath));

            if (sourceStep == null) return;

            importStep.Input.Mode = "FromStepOutput";
            importStep.Input.SourceStepId = sourceStep.Id;
            importStep.Input.Path = null;
        }

        private bool PathsEqual(string left, string right)
        {
            if (string.IsNullOrWhiteSpace(left) || string.IsNullOrWhiteSpace(right)) return false;

            try
            {
                return string.Equals(Path.GetFullPath(left), Path.GetFullPath(right), StringComparison.OrdinalIgnoreCase);
            }
            catch
            {
                return string.Equals(left, right, StringComparison.OrdinalIgnoreCase);
            }
        }

        private string GetOperationDisplayName(string operation)
        {
            switch (operation)
            {
                case "ExportToJson": return "Export JSON";
                case "ExportToExcel": return "Export Excel";
                case "ImportFromJson": return "Import JSON";
                case "ImportFromExcel": return "Import Excel";
                default: return operation;
            }
        }

        private List<Mapping> CloneMappings(IEnumerable<Mapping> mappings)
        {
            return (mappings ?? Enumerable.Empty<Mapping>())
                .Select(m => new Mapping
                {
                    Type = m.Type,
                    TableDisplayName = m.TableDisplayName,
                    TableLogicalName = m.TableLogicalName,
                    AttributeDisplayName = m.AttributeDisplayName,
                    AttributeLogicalName = m.AttributeLogicalName,
                    SourceInstanceName = m.SourceInstanceName,
                    SourceId = m.SourceId,
                    TargetId = m.TargetId,
                    TargetInstanceName = m.TargetInstanceName,
                    State = m.State
                })
                .ToList();
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

            _currentWorkingMessage = message;
            UpdateWorkingDialog(message, GetCurrentWorkingTip());
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

        private void ManageWorkingState(bool working, string message = null)
        {
            pnlMain.Enabled = !working;

            _working = working;
            if (working && !string.IsNullOrWhiteSpace(message))
                _currentWorkingMessage = message;
            Cursor = working ? Cursors.WaitCursor : Cursors.Default;
            tsbAbort.Text = "Abort";
            tsbAbort.Visible = working;
            if (working)
            {
                ShowWorkingDialog(_currentWorkingMessage);
                StartWorkingTips();
            }
            else
            {
                StopWorkingTips();
                CloseWorkingDialog();
            }
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

        private void InitializeWorkingTips()
        {
            _workingTipsTimer = new Timer { Interval = 8000 };
            _workingTipsTimer.Tick += (sender, args) => RotateWorkingTip();
        }

        private void StartWorkingTips()
        {
            if (_workingTipsTimer == null) return;

            _currentWorkingMessage = string.IsNullOrWhiteSpace(_currentWorkingMessage) ? "Working..." : _currentWorkingMessage;
            _workingTipIndex = GetNextWorkingTipIndex();
            UpdateWorkingDialog(_currentWorkingMessage, GetCurrentWorkingTip());
            _workingTipsTimer.Stop();
            _workingTipsTimer.Start();
        }

        private void StopWorkingTips()
        {
            _workingTipsTimer?.Stop();
            _currentWorkingMessage = null;
            _workingTipIndex = -1;
        }

        private void RotateWorkingTip()
        {
            if (!_working || string.IsNullOrWhiteSpace(_currentWorkingMessage)) return;

            _workingTipIndex = GetNextWorkingTipIndex();
            UpdateWorkingDialog(_currentWorkingMessage, GetCurrentWorkingTip());
        }

        private void ShowWorkingDialog(string message)
        {
            if (ParentForm == null || ParentForm.IsDisposed) return;

            if (_workingDialog == null || _workingDialog.IsDisposed)
            {
                _workingDialog = new WorkingDialog();
                _workingDialog.AbortRequested += WorkingDialogAbortRequested;
                _workingDialog.UpdateContent(message, GetCurrentWorkingTip());
                _workingDialog.SetAbortEnabled(true);
                _workingDialog.Show(GetWorkingDialogOwner());
            }
            else
            {
                _workingDialog.UpdateContent(message, GetCurrentWorkingTip());
                _workingDialog.SetAbortEnabled(true);
                if (!_workingDialog.Visible)
                    _workingDialog.Show(GetWorkingDialogOwner());
            }

            _workingDialog.CenterOverOwner();
            _workingDialog.BringToFront();
            _workingDialog.Activate();
        }

        private Form GetWorkingDialogOwner()
        {
            var active = Form.ActiveForm;
            if (active != null && !active.IsDisposed && active != _workingDialog)
                return active;
            return ParentForm;
        }

        private void WorkingDialogAbortRequested(object sender, EventArgs e)
        {
            AbortCurrentOperation();
        }

        private void UpdateWorkingDialog(string message, string tip)
        {
            if (_workingDialog == null || _workingDialog.IsDisposed) return;
            _workingDialog.UpdateContent(message, tip);
        }

        private void CloseWorkingDialog()
        {
            if (_workingDialog == null) return;

            try
            {
                _workingDialog.AbortRequested -= WorkingDialogAbortRequested;
                if (!_workingDialog.IsDisposed)
                    _workingDialog.Close();
            }
            finally
            {
                _workingDialog = null;
            }
        }

        private void AbortCurrentOperation()
        {
            CancelWorker();
            tsbAbort.Text = "Aborting operation...";
            _workingDialog?.SetAbortEnabled(false);
        }

        private int GetNextWorkingTipIndex()
        {
            if (WorkingTips.Length == 0) return -1;
            if (WorkingTips.Length == 1) return 0;
            return (_workingTipIndex + 1) % WorkingTips.Length;
        }

        private string GetCurrentWorkingTip()
        {
            if (_workingTipIndex < 0 || _workingTipIndex >= WorkingTips.Length)
                return string.Empty;

            return WorkingTips[_workingTipIndex];
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

        private void RenderExecutionPlanMenu()
        {
            if (tsmiExecutionPlan == null) return;

            var hasPlan = _executionPlan != null && !string.IsNullOrWhiteSpace(_executionPlanFilePath);
            tsmiExecutionPlan.Text = hasPlan ? GetExecutionPlanDisplayName(_executionPlanFilePath) : "Execution Plan";
            tsmiPlanSave.Enabled = hasPlan;
            tsmiPlanReview.Visible = false;
            tsmiPlanValidate.Enabled = hasPlan;
            tsmiPlanExecute.Enabled = hasPlan && CanExecuteValidatedExecutionPlan();
            tsmiPlanClose.Enabled = hasPlan;
            RenderExecutionPlanPanel();
        }

        private string GetDmtFileDisplayName(string filePath)
        {
            var fileName = Path.GetFileName(filePath);
            return fileName != null && fileName.EndsWith(".dmt.json", StringComparison.OrdinalIgnoreCase)
                ? fileName.Substring(0, fileName.Length - ".dmt.json".Length)
                : Path.GetFileNameWithoutExtension(filePath);
        }

        private string GetExecutionPlanDisplayName(string filePath)
        {
            var fileName = Path.GetFileName(filePath);
            return fileName != null && fileName.EndsWith(".dmtplan.json", StringComparison.OrdinalIgnoreCase)
                ? fileName.Substring(0, fileName.Length - ".dmtplan.json".Length)
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

        private static Image CreateExecutionPlanIcon(int size = 20)
        {
            var bmp = new Bitmap(size, size);
            using (var g = Graphics.FromImage(bmp))
            {
                g.Clear(Color.Transparent);
                g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
                using (var pen = new Pen(Color.FromArgb(70, 70, 70), 1.4f))
                using (var brush = new SolidBrush(Color.FromArgb(80, 140, 210)))
                {
                    for (var i = 0; i < 3; i++)
                    {
                        var y = 4 + (i * 5);
                        g.FillEllipse(brush, 2, y - 1, 3, 3);
                        g.DrawLine(pen, 7, y, 17, y);
                    }
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
            tsmiImportSettings.Text = "Legacy settings file";
            tsmiImport.DropDownItems.Clear();
            tsmiImport.DropDownItems.AddRange(new ToolStripItem[]
            {
                tsmiImportData,
                tsmiImportFromExcel,
                tsmiImportSettings
            });
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
                tsmiImportData.Enabled = enable;
                tsmiImportFromExcel.Enabled = enable;

                if (targetReady) // source and target connection is available
                {
                    gbMappingSettings.Enabled = false;
                    gbOpSettings.Enabled = false;
                    
                    RenderMappingsButton();
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
            RenderExecutionPlanMenu();
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

        private UiSettings GetDefaultImportSettings(ExcelExportConfig config, Enums.Action initial)
        {
            var settings = GetDefaultImportSettings(initial);
            var excelSettings = config?.ImportSettings;
            if (excelSettings == null) return settings;

            settings.Action = initial;
            if ((excelSettings.Action & Enums.Action.Create) == Enums.Action.Create)
                settings.Action |= Enums.Action.Create;
            if ((excelSettings.Action & Enums.Action.Update) == Enums.Action.Update)
                settings.Action |= Enums.Action.Update;
            if ((settings.Action & (Enums.Action.Create | Enums.Action.Update)) == 0)
                settings.Action |= Enums.Action.Create | Enums.Action.Update;

            settings.BatchSize = excelSettings.BatchSize > 0 ? Math.Min(excelSettings.BatchSize, 25) : settings.BatchSize;
            settings.MapBu = excelSettings.MapBusinessUnit;
            settings.ApplyMappingsOn = excelSettings.ApplyMappings ? Operation.Import : Operation.Export;
            return settings;
        }

        private ExcelImportSettings BuildExcelImportSettings(UiSettings uiSettings, ExcelExportConfig config)
        {
            uiSettings = uiSettings ?? GetDefaultImportSettings(Enums.Action.None);
            var matchKeys = config?.MatchKeys?.Any() == true
                ? config.MatchKeys.Where(k => !string.IsNullOrWhiteSpace(k)).Distinct(StringComparer.OrdinalIgnoreCase).ToList()
                : (string.IsNullOrWhiteSpace(config?.MatchKey) ? new List<string>() : new List<string> { config.MatchKey });

            return new ExcelImportSettings
            {
                Action = uiSettings.Action,
                BatchSize = uiSettings.BatchSize > 0 ? Math.Min(uiSettings.BatchSize, 25) : 25,
                ApplyMappings = uiSettings.ApplyMappingsOn == Operation.Import,
                MapBusinessUnit = uiSettings.MapBu,
                MatchKeyMode = matchKeys.Any()
                    ? (string.IsNullOrWhiteSpace(config?.MatchKeyMode) ? "Custom" : config.MatchKeyMode)
                    : "Guid",
                MatchKeyFields = matchKeys,
                MatchAlternateKeyName = config?.MatchAlternateKeyName
            };
        }

        private void EnsureExcelImportSettings(ExcelExportConfig config, ExcelImportSettings defaults)
        {
            if (config == null || config.ImportSettings != null) return;

            config.Version = string.IsNullOrWhiteSpace(config.Version) || config.Version == "1.0" ? "1.1" : config.Version;
            config.ImportSettings = defaults ?? BuildExcelImportSettings(GetDefaultImportSettings(Enums.Action.None), config);
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
                lvTables.BeginUpdate();
                lvTables.Items.Clear();
                LoadTablesList();
            }
            catch (Exception ex)
            {
                _logger.Log(LogLevel.ERROR, ex.Message);
                MessageBox.Show(ex.Message, "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            finally
            {
                lvTables.EndUpdate();
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

        private void tsmiPlanNew_Click(object sender, EventArgs e)
        {
            try
            {
                CreateExecutionPlan();
            }
            catch (Exception ex)
            {
                ManageWorkingState(false);
                _logger.Log(LogLevel.ERROR, ex.Message);
                MessageBox.Show(ex.Message, "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void tsmiPlanLoad_Click(object sender, EventArgs e)
        {
            try
            {
                LoadExecutionPlan();
            }
            catch (Exception ex)
            {
                ManageWorkingState(false);
                _logger.Log(LogLevel.ERROR, ex.Message);
                MessageBox.Show(ex.Message, "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void tsmiPlanSave_Click(object sender, EventArgs e)
        {
            try
            {
                SaveExecutionPlan();
            }
            catch (Exception ex)
            {
                ManageWorkingState(false);
                _logger.Log(LogLevel.ERROR, ex.Message);
                MessageBox.Show(ex.Message, "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void tsmiPlanReview_Click(object sender, EventArgs e)
        {
            try
            {
                ReviewExecutionPlan();
            }
            catch (Exception ex)
            {
                ManageWorkingState(false);
                _logger.Log(LogLevel.ERROR, ex.Message);
                MessageBox.Show(ex.Message, "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void tsmiPlanValidate_Click(object sender, EventArgs e)
        {
            try
            {
                ValidateExecutionPlan();
            }
            catch (Exception ex)
            {
                ManageWorkingState(false);
                _logger.Log(LogLevel.ERROR, ex.Message);
                MessageBox.Show(ex.Message, "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void tsmiPlanExecute_Click(object sender, EventArgs e)
        {
            try
            {
                ExecuteExecutionPlan();
            }
            catch (Exception ex)
            {
                ManageWorkingState(false);
                _logger.Log(LogLevel.ERROR, ex.Message);
                MessageBox.Show(ex.Message, "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void tsmiPlanClose_Click(object sender, EventArgs e)
        {
            try
            {
                CloseExecutionPlan();
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
                AbortCurrentOperation();
            }
            catch (Exception ex)
            {
                ManageWorkingState(false);
                _logger.Log(LogLevel.ERROR, ex.Message);
                MessageBox.Show(ex.Message, "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void tsbShowInstructions_Click(object sender, EventArgs e)
        {
            try
            {
                ShowStartupGuide(force: true);
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
