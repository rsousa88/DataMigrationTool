// System
using Microsoft.Xrm.Sdk;

// DataMigrationTool
using Dataverse.XrmTools.DataMigrationTool.Enums;

namespace Dataverse.XrmTools.DataMigrationTool.Models
{
    public class MigrationItem
    {
        public Action Action { get; set; }
        public Entity Record { get; set; }
        public string Description { get; set; }

        public MigrationItem(Action action, Entity entity, string description)
        {
            Action = action;
            Record = entity;
            Description = description;
        }
    }
}