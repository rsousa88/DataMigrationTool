// System
using System;
using System.Collections.Generic;

// DataMigrationTool
using Dataverse.XrmTools.DataMigrationTool.AppSettings;

namespace Dataverse.XrmTools.DataMigrationTool.Models
{
    public class ExecutionPlan
    {
        public string Version { get; set; } = "1.0";
        public string Name { get; set; }
        public DateTime CreatedOn { get; set; } = DateTime.UtcNow;
        public DateTime UpdatedOn { get; set; } = DateTime.UtcNow;
        public DmtEnvironmentInfo SourceEnvironment { get; set; }
        public DmtEnvironmentInfo TargetEnvironment { get; set; }
        public List<DmtEnvironmentInfo> TargetEnvironments { get; set; } = new List<DmtEnvironmentInfo>();
        public ExecutionPlanDefaults Defaults { get; set; } = new ExecutionPlanDefaults();
        public List<ExecutionPlanStep> Steps { get; set; } = new List<ExecutionPlanStep>();
    }

    public class ExecutionPlanDefaults
    {
        public bool StopOnFatalError { get; set; } = true;
        public int MaxFailedRecords { get; set; } = 10;
        public decimal MaxFailedPercent { get; set; } = 20m;
        public bool WarningsOnlyContinue { get; set; } = true;
    }

    public class ExecutionPlanStep
    {
        public string Id { get; set; } = Guid.NewGuid().ToString("D");
        public string Name { get; set; }
        public bool Enabled { get; set; } = true;
        public string Operation { get; set; }
        public DmtTableInfo Table { get; set; }
        public DmtEnvironmentInfo TargetEnvironment { get; set; }
        public ExecutionPlanStepInput Input { get; set; } = new ExecutionPlanStepInput();
        public ExecutionPlanStepOutput Output { get; set; } = new ExecutionPlanStepOutput();
        public ExecutionPlanSettingsProvenance SettingsProvenance { get; set; }
        public ExecutionPlanStepSnapshot Snapshot { get; set; } = new ExecutionPlanStepSnapshot();
        public ExecutionPlanFailurePolicy FailurePolicy { get; set; } = new ExecutionPlanFailurePolicy();
        public ExecutionPlanValidation Validation { get; set; } = new ExecutionPlanValidation();
    }

    public class ExecutionPlanStepInput
    {
        public string Mode { get; set; } = "File";      // "File" | "FromStepOutput" | "Snapshot"
        public string Path { get; set; }
        public string SourceStepId { get; set; }
        public string SnapshotName { get; set; }        // used when Mode = "Snapshot"
    }

    public class ExecutionPlanStepOutput
    {
        public string PathTemplate { get; set; }
        public string ResolvedPath { get; set; }
    }

    public class ExecutionPlanSettingsProvenance
    {
        public string SettingsFilePath { get; set; }
        public string SettingsFileHashAtCapture { get; set; }
        public DateTime CapturedAt { get; set; } = DateTime.UtcNow;
    }

    public class ExecutionPlanStepSnapshot
    {
        public List<string> SelectedAttributes { get; set; } = new List<string>();
        public string Filter { get; set; }
        public List<Mapping> Mappings { get; set; } = new List<Mapping>();
        public ExcelExportConfig ExcelConfig { get; set; }
        public RecordCollection RecordCollection { get; set; }
        public ExcelImportMatchKeySelection ImportMatchKeySelection { get; set; }
        public UiSettings ImportSettings { get; set; }
        public UiSettings ExportSettings { get; set; }
        public List<PushLookupMatchKey> LookupMatchKeys { get; set; }
    }

    public class PushLookupMatchKey
    {
        public string LogicalName { get; set; }
        public string Mode { get; set; }         // "Guid" | "Custom"
        public List<string> Fields { get; set; } = new List<string>();
    }

    public class ExecutionPlanFailurePolicy
    {
        public bool? StopOnFatalError { get; set; }
        public int? MaxFailedRecords { get; set; }
        public decimal? MaxFailedPercent { get; set; }
        public bool? WarningsOnlyContinue { get; set; }
    }

    public class ExecutionPlanValidation
    {
        public string Status { get; set; } = "Unknown";
        public List<ExecutionPlanValidationMessage> Messages { get; set; } = new List<ExecutionPlanValidationMessage>();
        public ExecutionPlanPreviewSummary Preview { get; set; }
        public DateTime? ValidatedAt { get; set; }
    }

    public class ExecutionPlanValidationMessage
    {
        public string Severity { get; set; }
        public string Message { get; set; }
    }

    public class ExecutionPlanPreviewSummary
    {
        public int Rows { get; set; }
        public int Creates { get; set; }
        public int Updates { get; set; }
        public int Skips { get; set; }
        public int Warnings { get; set; }
        public int Errors { get; set; }
        public string Source { get; set; }
        public bool IsEstimated { get; set; }
        public bool IsStale { get; set; }
    }

    public class ExecutionPlanRunLog
    {
        public string PlanName { get; set; }
        public string PlanFilePath { get; set; }
        public DateTime StartedOn { get; set; }
        public DateTime CompletedOn { get; set; }
        public DmtEnvironmentInfo SourceEnvironment { get; set; }
        public DmtEnvironmentInfo TargetEnvironment { get; set; }
        public List<DmtEnvironmentInfo> TargetEnvironments { get; set; } = new List<DmtEnvironmentInfo>();
        public List<ExecutionPlanRunStepLog> Steps { get; set; } = new List<ExecutionPlanRunStepLog>();
    }

    public class ExecutionPlanRunStepLog
    {
        public int Index { get; set; }
        public string StepId { get; set; }
        public string Name { get; set; }
        public string Operation { get; set; }
        public DmtEnvironmentInfo TargetEnvironment { get; set; }
        public string Status { get; set; }
        public string Path { get; set; }
        public int TotalRecords { get; set; }
        public int FailedRecords { get; set; }
        public decimal FailedPercent { get; set; }
        public string Summary { get; set; }
        public string Error { get; set; }
        public List<string> ErrorDetails { get; set; } = new List<string>();
    }

    public class ExecutionPlanStepExecutionResult
    {
        public string Summary { get; set; }
        public int TotalRecords { get; set; }
        public int FailedRecords { get; set; }
        public decimal FailedPercent { get; set; }
        public bool HasFailures { get; set; }
        public bool ShouldStopPlan { get; set; }
        public List<string> ErrorDetails { get; set; } = new List<string>();
    }
}
