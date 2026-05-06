// System
using System.Collections.Generic;

// DataMigrationTool
using Dataverse.XrmTools.DataMigrationTool.AppSettings;

namespace Dataverse.XrmTools.DataMigrationTool.Models
{
    public class DmtSettings
    {
        public string Version { get; set; } = "1.0";
        public DmtEnvironmentInfo Environment { get; set; }
        public DmtTableInfo Table { get; set; }
        public List<string> DeselectedAttributes { get; set; } = new List<string>();
        public string Filter { get; set; }
        public List<Mapping> Mappings { get; set; } = new List<Mapping>();
        public ExcelExportConfig ExcelConfig { get; set; }
        public DmtImportSettings ImportSettings { get; set; } = new DmtImportSettings();
    }

    public class DmtEnvironmentInfo
    {
        public string UniqueName { get; set; }
        public string FriendlyName { get; set; }
    }

    public class DmtTableInfo
    {
        public string LogicalName { get; set; }
        public string DisplayName { get; set; }
        public string PrimaryIdAttribute { get; set; }
        public string PrimaryNameAttribute { get; set; }
    }

    public class DmtImportSettings
    {
        public string MatchKeyMode { get; set; } = "Guid";
        public List<string> MatchKeyFields { get; set; } = new List<string>();
        public string MatchAlternateKeyName { get; set; }
        public int BatchSize { get; set; } = 25;
    }
}
