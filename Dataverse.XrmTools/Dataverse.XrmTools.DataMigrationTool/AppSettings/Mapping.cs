// System
using System;

// DataMigrationTool
using Dataverse.XrmTools.DataMigrationTool.Enums;

namespace Dataverse.XrmTools.DataMigrationTool.AppSettings
{
    public class Mapping
    {
        public MappingType Type { get; set; }
        public string TableLogicalName{ get; set; }
        public string TableDisplayName{ get; set; }
        public string AttributeLogicalName { get; set; }
        public string AttributeDisplayName { get; set; }
        public string SourceInstanceName { get; set; }
        public Guid SourceId { get; set; }
        public string TargetInstanceName { get; set; }
        public Guid TargetId { get; set; }
        public MappingState State { get; set; }
    }
}
