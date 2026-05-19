using System.Collections.Generic;
using System;

namespace Dataverse.XrmTools.DataMigrationTool.Models
{
    public class ExcelImportPreviewRequest
    {
        public TableData TableData { get; set; }
        public RecordCollection Collection { get; set; }
        public ExcelExportConfig Config { get; set; }
        public UiSettings Settings { get; set; }
        public string FilePath { get; set; }
        public string TargetName { get; set; }
        public int MappingCount { get; set; }
        public Func<IEnumerable<Guid>, ISet<Guid>> ExistingTargetIdsProvider { get; set; }
    }

    public class ExcelImportPreview
    {
        public string FilePath { get; set; }
        public string SourceType { get; set; }
        public string SettingsSource { get; set; }
        public string TableLogicalName { get; set; }
        public string TargetName { get; set; }
        public string MatchKey { get; set; }
        public string MatchKeyMode { get; set; }
        public List<string> MatchKeys { get; set; } = new List<string>();
        public string MatchAlternateKeyName { get; set; }
        public int TotalRows { get; set; }
        public int CreateCount { get; set; }
        public int UpdateCount { get; set; }
        public int SkippedCount { get; set; }
        public int MappingCount { get; set; }
        public UiSettings Settings { get; set; }
        public List<string> AvailableMatchKeys { get; set; } = new List<string>();
        public List<ExcelImportAlternateKeyOption> AvailableAlternateKeys { get; set; } = new List<ExcelImportAlternateKeyOption>();
        public List<string> ValueColumns { get; set; } = new List<string>();
        public List<string> ImportErrors { get; set; } = new List<string>();
        public List<ExcelImportPreviewItem> Items { get; set; } = new List<ExcelImportPreviewItem>();
    }

    public class ExcelImportAlternateKeyOption
    {
        public string Name { get; set; }
        public List<string> Fields { get; set; } = new List<string>();
    }

    public class ExcelImportMatchKeySelection
    {
        public string Mode { get; set; }
        public List<string> Fields { get; set; } = new List<string>();
        public string AlternateKeyName { get; set; }
    }

    public class ExcelImportPreviewItem
    {
        public int RowNumber { get; set; }
        public string Action { get; set; }
        public string RecordId { get; set; }
        public string MatchValue { get; set; }
        public string Name { get; set; }
        public string Description { get; set; }
        public string Warnings { get; set; }
        public Dictionary<string, string> Values { get; set; } = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
    }
}
