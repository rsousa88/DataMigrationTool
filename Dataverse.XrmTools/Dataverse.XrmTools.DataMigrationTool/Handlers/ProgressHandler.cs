// System
using System;

namespace Dataverse.XrmTools.DataMigrationTool.Handlers
{
    public class ProgressHandler : EventArgs
    {
        public int Progress { get; set; }
        public string Message { get; set; }

        public ProgressHandler(int progress, string message)
        {
            Progress = progress;
            Message = message;
        }
    }
}
