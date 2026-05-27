using System.Collections.Generic;

// DataMigrationTool
using Dataverse.XrmTools.DataMigrationTool.Enums;

namespace Dataverse.XrmTools.DataMigrationTool.Models
{
    public class ExcelExportConfig
    {
        public string Version { get; set; } = "1.1";
        public string MatchKey { get; set; }
        public string MatchKeyMode { get; set; }
        public List<string> MatchKeys { get; set; } = new List<string>();
        public string MatchAlternateKeyName { get; set; }
        public ExcelImportSettings ImportSettings { get; set; }
        public ExcelTableConfig Table { get; set; }
        public List<ExcelColumnConfig> Columns { get; set; } = new List<ExcelColumnConfig>();
    }

    public class ExcelImportSettings
    {
        public Action Action { get; set; }
        public int BatchSize { get; set; } = 25;
        public string MatchKeyMode { get; set; } = "Guid";
        public List<string> MatchKeyFields { get; set; } = new List<string>();
        public string MatchAlternateKeyName { get; set; }
    }

    public class ExcelTableConfig
    {
        public string LogicalName { get; set; }
        public string PrimaryIdAttribute { get; set; }
        public string PrimaryNameAttribute { get; set; }
    }

    public class ExcelColumnConfig
    {
        public string LogicalName { get; set; }
        public string DisplayName { get; set; }
        public bool Hidden { get; set; }
        public string HintOverride { get; set; }

        // String, Integer, BigInt, Decimal, Double, Money, Boolean, DateTime, Guid,
        // Lookup, LookupKeyField, OptionSet, MultiOptionSet
        public string Type { get; set; }

        // Lookup only
        public string RelatedTable { get; set; }
        public string Resolution { get; set; }        // "Guid" | "AlternateKey"
        public List<string> AlternateKeyFields { get; set; }

        // LookupKeyField only
        public string OwnerAttribute { get; set; }
        public string KeyFieldType { get; set; }

        // OptionSet / MultiOptionSet only
        public string ExportMode { get; set; }        // "Value" | "Label"
        public List<OptionConfig> Options { get; set; }
    }

    public class OptionConfig
    {
        public int Value { get; set; }
        public string Label { get; set; }
    }
}
