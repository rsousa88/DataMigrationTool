// DataMigrationTool
using Dataverse.XrmTools.DataMigrationTool.Enums;

namespace Dataverse.XrmTools.DataMigrationTool.Models
{
    public class UiSettings
    {
        public Action Action { get; set; }
        public int BatchSize { get; set; }
        public bool HideInvalidAttributes { get; set; }
    }
}
