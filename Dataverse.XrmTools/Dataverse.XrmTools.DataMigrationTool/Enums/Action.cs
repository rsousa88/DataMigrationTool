// System
using System;

namespace Dataverse.XrmTools.DataMigrationTool.Enums
{
    [Flags]
    public enum Action
    {
        None = 1,
        Preview = 2,
        Create = 4,
        Update = 8,
        Delete = 16
    }
}
