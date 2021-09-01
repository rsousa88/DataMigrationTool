// System
using System.Collections.Generic;

namespace Dataverse.XrmTools.DataMigrationTool.AppSettings
{
    public class TableSettings
    {
        public string LogicalName { get; set; }
        public string DisplayName { get; set; }
        public bool IsCustomizable { get; set; }
        public List<string> DeselectedAttributes { get; set; }
        public string Filter { get; set; }
    }
}
