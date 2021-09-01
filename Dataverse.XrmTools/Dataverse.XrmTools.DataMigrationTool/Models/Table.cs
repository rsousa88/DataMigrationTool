// System
using System.Collections.Generic;

namespace Dataverse.XrmTools.DataMigrationTool.Models
{
    public class Table
    {
        public string LogicalName { get; set; }
        public string DisplayName { get; set; }
        public string IdAttribute { get; set; }
        public string NameAttribute { get; set; }
        public bool IsCustomizable { get; set; }
        public List<Attribute> AllAttributes { get; set; }
    }
}
