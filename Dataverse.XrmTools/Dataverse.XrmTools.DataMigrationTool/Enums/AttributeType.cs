// System
using System;

namespace Dataverse.XrmTools.DataMigrationTool.Enums
{
    [Flags]
    public enum AttributeType
    {
        Identifier = 1,
        EntityReference = 2,
        OptionSet = 4,
        MultiOptionSet = 8,
        Standard = 16
    }
}
