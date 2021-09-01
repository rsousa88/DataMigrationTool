// System
using System;

namespace Dataverse.XrmTools.DataMigrationTool.Handlers
{
    public class StatusHandler : EventArgs
    {
        public string Message { get; set; }

        public StatusHandler(string message)
        {
            Message = message;
        }
    }
}
