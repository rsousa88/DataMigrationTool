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
        private ToolStripButton _executionPlanSaveButton;
        private ToolStripButton _executionPlanSaveAsButton;
        private ToolStripButton _executionPlanValidateButton;
        private ToolStripButton _executionPlanExecuteButton;
        private ToolStripButton _executionPlanPreviewStepButton;
        private ToolStripButton _executionPlanConfigureStepButton;
        private ToolStripButton _executionPlanExecuteStepButton;
        private ToolStripButton _executionPlanCloneStepButton;
        private ToolStripButton _executionPlanMoveStepUpButton;
        private ToolStripButton _executionPlanMoveStepDownButton;
        private ToolStripButton _executionPlanRemoveStepButton;
        private ContextMenuStrip _executionPlanStepContextMenu;
        private bool _suppressExecutionPlanStepChecked;
        private bool _suppressExecutionPlanInlineTargetChanged;
        private bool _executionPlanRowTargetRenderQueued;
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
            tsmiExecutionPlan.Image = CreateExecutionPlanIcon();
            MoveImportSettingsIntoDialogs();
            InitializeDmtAutoSave();
            InitializeWorkingTips();

            _logger = new Logger();
            _logger.OnLog += Log;
        }

        public void DataMigrationControl_Load(object sender, EventArgs e)
        {
            _logger.Log(LogLevel.INFO, "Data Migration tool initialized");
            InitializeTableConfigAutoSave();
            BeginInvoke(new System.Action(() =>
            {
                InitializeProjectPanel();
                InitializePullPanel();
                InitializeLoadFilePanel();
                InitializePushPanel();
                InitializeHistoryPanel();
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

                    BindProjectSource(client);
                    RenderProjectBanner();
                    RenderProjectName();

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
                    RegisterProjectTarget(client);

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
                ShowStartupProjectDialog();
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

        private DmtEnvironmentInfo GetActiveTargetEnvironmentInfo()
        {
            return ActiveTargetClient == null
                ? null
                : new DmtEnvironmentInfo
                {
                    UniqueName = ActiveTargetClient.ConnectedOrgUniqueName,
                    FriendlyName = ActiveTargetClient.ConnectedOrgFriendlyName
                };
        }

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

            if (!ExecutionPlanService.TryValidateTargetConnection(step, _targetClients.Keys, _targetClient != null, out error))
                return false;

            var uniqueName = step?.TargetEnvironment?.UniqueName;
            if (string.IsNullOrWhiteSpace(uniqueName))
            {
                return true;
            }

            _targetClients.TryGetValue(uniqueName, out var client);

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

        private void ShowStartupProjectDialog()
        {
            if (_startupFilesDialogShown || _tables == null || !_tables.Any()) return;
            if (_project != null) return;

            _startupFilesDialogShown = true;

            using (var dlg = new Form())
            {
                dlg.Text = "Open a project";
                dlg.FormBorderStyle = FormBorderStyle.FixedDialog;
                dlg.StartPosition = FormStartPosition.CenterParent;
                dlg.ClientSize = new System.Drawing.Size(340, 100);
                dlg.ShowIcon = false;
                dlg.ShowInTaskbar = false;
                dlg.MinimizeBox = false;
                dlg.MaximizeBox = false;

                var lbl = new System.Windows.Forms.Label
                {
                    Text = "Open or create a project file (.dmtproj) to get started.",
                    AutoSize = false,
                    Dock = DockStyle.Top,
                    Height = 36,
                    TextAlign = System.Drawing.ContentAlignment.MiddleCenter,
                    Padding = new Padding(8, 6, 8, 0)
                };

                var btnNew = new Button { Text = "New Project", Width = 100, Height = 26, DialogResult = DialogResult.Yes };
                var btnOpen = new Button { Text = "Open Project", Width = 100, Height = 26, DialogResult = DialogResult.OK };
                var btnSkip = new Button { Text = "Skip", Width = 60, Height = 26, DialogResult = DialogResult.Cancel };

                var panel = new FlowLayoutPanel
                {
                    Dock = DockStyle.Fill,
                    FlowDirection = FlowDirection.LeftToRight,
                    Padding = new Padding(16, 4, 0, 0),
                    WrapContents = false
                };
                panel.Controls.AddRange(new Control[] { btnNew, btnOpen, btnSkip });

                dlg.Controls.Add(panel);
                dlg.Controls.Add(lbl);
                dlg.CancelButton = btnSkip;

                var result = dlg.ShowDialog(ParentForm);
                if (result == DialogResult.Yes)
                    NewProject();
                else if (result == DialogResult.OK)
                    OpenProject();
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
                        AfterAttributesLoaded(tableData);

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
                    evt.Result = logic.Preview(data, uiSettings, false);
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

                        var previewColumns = tableData.SelectedAttributes
                            .Select(attr => attr.LogicalName)
                            .Where(name => !string.IsNullOrWhiteSpace(name))
                            .ToList();
                        var prvwDialog = new Results(result.Items, _settings, extraColumns: previewColumns);
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

            StartExcelImportPreflight(path, targetEnvironment);
        }

        private void StartExcelImportPreflight(string path, DmtEnvironmentInfo targetEnvironment)
        {
            ManageWorkingState(true, "Reading Excel metadata...");
            WorkAsync(new WorkAsyncInfo
            {
                AsyncArgument = path,
                IsCancelable = true,
                Work = (worker, evt) =>
                {
                    try
                    {
                        var filePath = evt.Argument as string;
                        worker.ReportProgress(0, "Excel import: reading workbook metadata...");
                        ThrowIfCancelled(worker);
                        var preflightLogic = new Logic.ExcelLogic();
                        var rowCount = preflightLogic.GetImportRowCount(filePath, out ExcelExportConfig preflightConfig);
                        ThrowIfCancelled(worker);
                        evt.Result = new ExcelImportPreflightResult { FilePath = filePath, RowCount = rowCount, Config = preflightConfig };
                    }
                    catch (OperationCanceledException)
                    {
                        evt.Cancel = true;
                    }
                },
                PostWorkCallBack = evt =>
                {
                    ManageWorkingState(false);
                    if (evt.Cancelled)
                    {
                        SendMessageToStatusBar?.Invoke(this, new StatusBarMessageEventArgs("Excel import read cancelled"));
                        return;
                    }
                    if (evt.Error != null)
                    {
                        _logger.Log(LogLevel.ERROR, evt.Error.ToString());
                        MessageBox.Show(evt.Error.Message, "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                        return;
                    }

                    var preflight = evt.Result as ExcelImportPreflightResult;
                    if (preflight == null) return;
                    if (!ValidateActiveSettingsTable(preflight.Config?.Table?.LogicalName, "Excel file")) return;
                    if (!ConfirmLargeExcelImport(preflight.RowCount)) return;

            StartExcelImportRead(preflight.FilePath, targetEnvironment, preflight.Config);
                },
                ProgressChanged = ReportWorkProgress
            });
        }

        private void StartExcelImportRead(string path, DmtEnvironmentInfo targetEnvironment, ExcelExportConfig preflightConfig)
        {
            var operationId = BeginExcelImportOperation();
            var defaultImportSettings = BuildExcelImportSettings(GetDefaultImportSettings(Enums.Action.None), preflightConfig);
            ManageWorkingState(true, "Reading Excel file...");

            WorkAsync(new WorkAsyncInfo
            {
                AsyncArgument = new { path, operationId, defaultImportSettings, targetEnvironment },
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
                        var requiredLookupTables = GetPlanLookupTablesRequiredByImportConfig(preflightConfig);
                        var resolver = BuildPlanLookupContextForPriorSteps(targetEnvironment, null, worker, true, target, requiredLookupTables);

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
                            },
                            resolver);
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
                    using (var dlg = new ExcelImportPreviewDialog(
                        preview,
                        "Add to Plan",
                        () => ConfigureMappingsForExecutionTarget(session?.TargetEnvironment)))
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
            var warning = ImportWorkflowService.GetLargeExcelImportWarning(rowCount);
            if (!warning.ShouldConfirm) return true;

            var icon = warning.Level == LargeImportWarningLevel.Warning
                ? MessageBoxIcon.Warning
                : MessageBoxIcon.Information;

            return MessageBox.Show(warning.Message, "Large Excel Import", MessageBoxButtons.YesNo, icon) == DialogResult.Yes;
        }

        private void ReloadExcelImportSession(string filePath, ExcelImportMatchKeySelection matchKey, TableData tableData, UiSettings uiSettings, DmtEnvironmentInfo targetEnvironment)
        {
            var operationId = BeginExcelImportOperation();
            ManageWorkingState(true, "Reading Excel file...");
            WorkAsync(new WorkAsyncInfo
            {
                AsyncArgument = new { filePath, matchKey, operationId, targetEnvironment },
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
                        var requiredLookupTables = GetPlanLookupTablesRequiredByImportConfig(excelLogic.ReadMetadata(reloadFilePath));
                        var resolver = BuildPlanLookupContextForPriorSteps(targetEnvironment, null, worker, true, target, requiredLookupTables);
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
                            },
                            resolver);
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
                EnsureTableVisible(tableData.Table.LogicalName);
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
            EnsureTableDataAttributes(tableData);

            LoadAttributesList(tableData);
            LoadFilters(tableData);
            SendMessageToStatusBar?.Invoke(this, new StatusBarMessageEventArgs($"Selected table from import file: {tableData.Table.LogicalName}"));
        }

        private bool ConfigureMappingsForExecutionTarget(DmtEnvironmentInfo targetEnvironment)
        {
            if (_sourceClient == null || _sourceInstance == null)
                return false;

            var previousClientOverride = _executionTargetClientOverride;
            var previousInstanceOverride = _executionTargetInstanceOverride;
            try
            {
                SetExecutionTargetOverride(targetEnvironment);
                var targetInstance = ActiveTargetInstance;
                if (targetInstance == null)
                {
                    MessageBox.Show("Connect or select the target environment before configuring mappings.", "Mappings", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    return false;
                }

                var mappingsDlg = new Mappings(_sourceClient, _sourceInstance, targetInstance, _tables, _settings);
                mappingsDlg.ShowDialog(ParentForm);

                if (!mappingsDlg.Updated) return false;

                _mappings = _sourceInstance.Mappings?
                    .Where(map => string.Equals(map.TargetInstanceName, targetInstance.FriendlyName, StringComparison.OrdinalIgnoreCase))
                    .ToList() ?? new List<Mapping>();
                RenderMappingsButton();
                SettingsHelper.SetSettings(_settings);
                AutoSaveDmtSettings();
                SendMessageToStatusBar?.Invoke(this, new StatusBarMessageEventArgs("Successfully updated Organization Mappings"));
                return true;
            }
            finally
            {
                _executionTargetClientOverride = previousClientOverride;
                _executionTargetInstanceOverride = previousInstanceOverride;
            }
        }

        private void EnsureTableDataAttributes(TableData tableData)
        {
            if (tableData?.Table == null)
                throw new Exception("Table metadata could not be resolved.");

            if (tableData.Table.AllAttributes?.Any() == true)
                return;

            var metadataAttributes = tableData.Metadata?.Attributes;
            if (metadataAttributes == null)
                throw new Exception($"Metadata attributes could not be loaded for table '{tableData.Table.LogicalName}'.");

            tableData.Table.AllAttributes = metadataAttributes
                .Where(att => att != null && att.IsValidForRead != null && att.IsValidForRead.Value)
                .Select(att =>
                {
                    var typeName = att.AttributeTypeName?.Value ?? string.Empty;
                    return new Models.Attribute
                    {
                        Type = typeName.EndsWith("Type", StringComparison.Ordinal)
                            ? typeName.Substring(0, typeName.LastIndexOf("Type", StringComparison.Ordinal))
                            : typeName,
                        LogicalName = att.LogicalName,
                        DisplayName = att.DisplayName?.UserLocalizedLabel != null ? att.DisplayName.UserLocalizedLabel.Label : string.Empty,
                        ValidOnCreate = att.IsValidForCreate == true,
                        ValidOnUpdate = att.IsValidForUpdate == true
                    };
                })
                .ToList();
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
                _currentTableConfig = null;

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

        private void ApplyDmtFileAndSelectTable(string filePath, DmtSettings settings, bool promptEnvironmentMismatch = true, bool showStatus = true)
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
                if (promptEnvironmentMismatch)
                {
                    var result = MessageBox.Show(
                        $"{environmentValidation.warning}\n\nContinue anyway?",
                        "Environment Mismatch",
                        MessageBoxButtons.YesNo,
                        MessageBoxIcon.Warning);
                    if (result != DialogResult.Yes) return;
                }
                else
                {
                    _logger?.Log(LogLevel.WARN, environmentValidation.warning);
                }
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
            if (showStatus)
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

        private void RenderExecutionPlanMenu()
        {
            RenderExecutionPlanMenu(true);
        }

        private void RenderExecutionPlanMenu(bool renderPanel)
        {
            if (tsmiExecutionPlan == null) return;

            var hasPlan = _executionPlan != null && !string.IsNullOrWhiteSpace(_executionPlanFilePath);
            tsmiExecutionPlan.Text = hasPlan ? GetExecutionPlanDisplayName(_executionPlanFilePath) : "Execution Plan";
            tsmiPlanSave.Enabled = hasPlan;
            tsmiPlanReview.Visible = false;
            tsmiPlanValidate.Enabled = hasPlan;
            tsmiPlanExecute.Enabled = hasPlan && CanExecuteValidatedExecutionPlan();
            tsmiPlanClose.Enabled = hasPlan;
            if (renderPanel)
                RenderExecutionPlanPanel();
            else
                RenderExecutionPlanActionState();
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

        private void RenderMappingsButton()
        {
            btnMappings.Font = (_mappings?.Any() == true) ? new Font(btnMappings.Font.Name, btnMappings.Font.Size, FontStyle.Bold) : new Font(btnMappings.Font.Name, btnMappings.Font.Size, FontStyle.Regular);
        }

        private void MoveImportSettingsIntoDialogs()
        {
            tsmiExportData.Text = "To JSON";
            tsmiImportData.Text = "From JSON";
            tsmiImport.DropDownItems.Clear();
            tsmiImport.DropDownItems.AddRange(new ToolStripItem[]
            {
                tsmiImportData,
                tsmiImportFromExcel
            });
            tsmiExportSettings.Visible = false;
            tsmiExportSettings.Enabled = false;
            tsmiExportWithSettings.Visible = false;
            tsmiExportWithSettings.Enabled = false;
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

            RenderExecutionPlanMenu(false);
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
                ScheduleTableConfigSave();
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
                    ScheduleTableConfigSave();
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
                ScheduleTableConfigSave();
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
