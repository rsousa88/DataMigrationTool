// DataMigrationTool
using Dataverse.XrmTools.DataMigrationTool.Enums;

namespace Dataverse.XrmTools.DataMigrationTool.Models
{
    public class UiSettings
    {
        public bool MapUsers { get; set; }
        public bool MapTeams { get; set; }
        public bool MapBu { get; set; }
        public Action Action { get; set; }
        public int BatchSize { get; set; }
        public Operation ApplyMappingsOn { get; set; }
    }
}
