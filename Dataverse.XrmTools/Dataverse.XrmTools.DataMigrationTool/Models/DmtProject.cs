// System
using System;
using System.Collections.Generic;

// Microsoft
using Microsoft.Xrm.Sdk;

// DataMigrationTool
using Dataverse.XrmTools.DataMigrationTool.AppSettings;
using Dataverse.XrmTools.DataMigrationTool.Logic;
using Dataverse.XrmTools.DataMigrationTool.Models;

namespace Dataverse.XrmTools.DataMigrationTool.Models
{
    // Holds the live state of the open project: service, environments, connected clients.
    public class ProjectContext
    {
        public string FilePath { get; set; }
        public string ProjectName { get; set; }
        public SqliteProjectService Service { get; set; }
        public DmtProjectEnvironment SourceEnvironment { get; set; }

        // True when the connected source org does not match the project's locked source.
        public bool IsSourceMismatch { get; set; }

        // envId → live client; populated as target orgs connect.
        public Dictionary<string, IOrganizationService> TargetClients { get; } =
            new Dictionary<string, IOrganizationService>(StringComparer.OrdinalIgnoreCase);
    }


    public class DmtProjectEnvironment
    {
        public string Id { get; set; }
        public string UniqueName { get; set; }
        public string FriendlyName { get; set; }
        public string Url { get; set; }
        public string Role { get; set; }  // "source" | "target"
    }

    // Per-table configuration stored in _table_configs.config_json
    public class DataTableConfig
    {
        public string Filter { get; set; }
        public List<string> SelectedAttributes { get; set; } = new List<string>();
        public int BatchSize { get; set; } = 25;

        // Full attribute type map from Dataverse metadata — refreshed by LoadAttributes()
        public List<DataTableColumnConfig> AllColumns { get; set; } = new List<DataTableColumnConfig>();

        // Load match key — used at LoadFile to match rows against source records
        public string LoadMatchKeyMode { get; set; } = "Guid";
        public List<string> LoadMatchKeyFields { get; set; } = new List<string>();
        public string LoadMatchAlternateKeyName { get; set; }

        // Push match key — null = inherit from _snapshots.load_match_key_mode at push time
        public string PushMatchKeyMode { get; set; }
        public List<string> PushMatchKeyFields { get; set; } = new List<string>();
        public string PushMatchAlternateKeyName { get; set; }

        public ExcelExportConfig ExcelConfig { get; set; }
    }

    // Per-column config stored in _snapshots.column_config_json and DataTableConfig.AllColumns
    public class DataTableColumnConfig
    {
        public string LogicalName { get; set; }
        public string DisplayName { get; set; }

        // Dataverse AttributeTypeCode name: "String", "Integer", "Lookup", "Picklist", etc.
        public string Type { get; set; }

        // SQLite column declaration type — derived from Type at LoadAttributes/LoadFile time
        public string SqliteType { get; set; }  // "INTEGER" | "REAL" | "TEXT"

        // Lookup columns
        public string RelatedTable { get; set; }
        public string Resolution { get; set; }  // "Guid" | "AlternateKey" | "Custom" | "Name"
        public List<string> AlternateKeyFields { get; set; }

        // OptionSet / MultiSelectPicklist
        public bool IsMultiSelect { get; set; }
    }

    // Row in _snapshots
    public class DmtSnapshot
    {
        public string Id { get; set; } = Guid.NewGuid().ToString("D");
        public string Name { get; set; }
        public string TableSuffix { get; set; }          // sanitized name used for data_{suffix} SQLite table
        public string TableLogicalName { get; set; }
        public string SourceEnvId { get; set; }
        public DateTime CreatedOn { get; set; } = DateTime.UtcNow;
        public DateTime UpdatedOn { get; set; } = DateTime.UtcNow;
        public int RowCount { get; set; }
        public string Source { get; set; }               // "Pull" | "Excel" | "JSON"
        public string LoadMatchKeyMode { get; set; }
        public List<string> LoadMatchKeyFields { get; set; } = new List<string>();
        public int SortOrder { get; set; }               // display order (#) — assigned once at creation

        // Frozen at creation — not updated when DataTableConfig.AllColumns is refreshed
        public List<DataTableColumnConfig> ColumnConfig { get; set; } = new List<DataTableColumnConfig>();
    }

    // Row in _plans
    public class DmtPlan
    {
        public string Id { get; set; } = Guid.NewGuid().ToString("D");
        public string Name { get; set; }
        public string Description { get; set; }
        public DateTime CreatedOn { get; set; } = DateTime.UtcNow;
        public DateTime UpdatedOn { get; set; } = DateTime.UtcNow;
        public ExecutionPlanDefaults Defaults { get; set; } = new ExecutionPlanDefaults();
    }

    // Row in _plan_steps
    public class DmtPlanStep
    {
        public string Id { get; set; } = Guid.NewGuid().ToString("D");
        public string PlanId { get; set; }
        public int SortOrder { get; set; }
        public string Name { get; set; }
        public bool Enabled { get; set; } = true;
        public string Operation { get; set; }            // "Pull" | "Push" | "LoadFile" | "ExportFile"
        public string TableLogicalName { get; set; }
        public string SourceEnvId { get; set; }          // Pull only
        public string TargetEnvId { get; set; }          // Push only
        public string SnapshotName { get; set; }
        public string FileType { get; set; }             // "Excel" | "JSON"
        public string FilePath { get; set; }             // relative to .dmtproj
        public DmtPlanStepSnapshot Snapshot { get; set; } = new DmtPlanStepSnapshot();
        public ExecutionPlanFailurePolicy FailurePolicy { get; set; } = new ExecutionPlanFailurePolicy();
        public ExecutionPlanValidation Validation { get; set; } = new ExecutionPlanValidation();
    }

    // Serialised as _plan_steps.snapshot_json — frozen at step creation
    public class DmtPlanStepSnapshot
    {
        public DmtTableInfo Table { get; set; }
        public DmtEnvironmentInfo TargetEnvironment { get; set; }
        public ExecutionPlanStepInput Input { get; set; }
        public ExecutionPlanStepOutput Output { get; set; }
        public ExecutionPlanSettingsProvenance SettingsProvenance { get; set; }
        public List<string> SelectedAttributes { get; set; } = new List<string>();
        public string Filter { get; set; }
        public List<Mapping> Mappings { get; set; } = new List<Mapping>();
        public ExcelExportConfig ExcelConfig { get; set; }
        public RecordCollection RecordCollection { get; set; }
        public ExcelImportMatchKeySelection ImportMatchKeySelection { get; set; }

        // LoadFile steps only
        public string LoadMatchKeyMode { get; set; }
        public List<string> LoadMatchKeyFields { get; set; } = new List<string>();
        public string LoadMatchAlternateKeyName { get; set; }

        // Push steps only — null = inherit from _snapshots.load_match_key_mode
        public string PushMatchKeyMode { get; set; }
        public List<string> PushMatchKeyFields { get; set; } = new List<string>();
        public string PushMatchAlternateKeyName { get; set; }

        public UiSettings ImportSettings { get; set; }
        public UiSettings ExportSettings { get; set; }
        public List<PushLookupMatchKey> LookupMatchKeys { get; set; }
    }

    // Row in _id_mappings
    public class DmtIdMapping
    {
        public string TableLogicalName { get; set; }
        public string SourceEnvId { get; set; }
        public string SourceId { get; set; }
        public string TargetEnvId { get; set; }
        public string TargetId { get; set; }
        public DateTime MappedOn { get; set; } = DateTime.UtcNow;
    }

    // Row in _run_logs
    public class DmtRunLog
    {
        public string Id { get; set; } = Guid.NewGuid().ToString("D");
        public string PlanId { get; set; }
        public string PlanName { get; set; }
        public DateTime StartedOn { get; set; }
        public DateTime? CompletedOn { get; set; }
        public string Status { get; set; }               // "Completed" | "Failed" | "Cancelled"
        public ExecutionPlanRunLog Log { get; set; }
    }
}
