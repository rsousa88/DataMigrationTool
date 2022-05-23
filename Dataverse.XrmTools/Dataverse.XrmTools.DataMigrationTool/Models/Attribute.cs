namespace Dataverse.XrmTools.DataMigrationTool.Models
{
    public class Attribute
    {
        public string Type { get; set; }
        public string LogicalName { get; set; }
        public string DisplayName { get; set; }
        public bool ValidOnCreate { get; set; }
        public bool ValidOnUpdate { get; set; }
    }
}
