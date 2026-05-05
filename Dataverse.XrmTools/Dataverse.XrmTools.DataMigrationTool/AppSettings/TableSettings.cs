// System
using System.Collections.Generic;

// DataMigrationTool
using Dataverse.XrmTools.DataMigrationTool.Models;

namespace Dataverse.XrmTools.DataMigrationTool.AppSettings
{
    public class TableSettings
    {
        public string LogicalName { get; set; }
        public string DisplayName { get; set; }
        public bool IsCustomizable { get; set; }
        public List<string> DeselectedAttributes { get; set; }
        public string Filter { get; set; }
        public ExcelExportConfig ExcelConfig { get; set; }
    }
}
