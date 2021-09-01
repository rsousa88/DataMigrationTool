// System
using System.Collections.Generic;

// Microsoft
using Microsoft.Xrm.Sdk.Metadata;

// DataMigrationTool
using Dataverse.XrmTools.DataMigrationTool.AppSettings;

namespace Dataverse.XrmTools.DataMigrationTool.Models
{
    public class TableData
    {
        public Table Table { get; set; }
        public TableSettings Settings { get; set; }
        public EntityMetadata Metadata { get; set; }
        public List<Attribute> SelectedAttributes { get; set; }
    }
}
