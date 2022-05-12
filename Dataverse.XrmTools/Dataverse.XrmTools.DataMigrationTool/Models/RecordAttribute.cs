using Dataverse.XrmTools.DataMigrationTool.Enums;

namespace Dataverse.XrmTools.DataMigrationTool.Models
{
    public class RecordAttribute
    {
        public string Key { get; set; }
        public AttributeType Type { get; set; }
        public object Value { get; set; }
    }
}
