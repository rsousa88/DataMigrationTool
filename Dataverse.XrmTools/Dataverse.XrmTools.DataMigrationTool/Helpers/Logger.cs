using System;
using Dataverse.XrmTools.DataMigrationTool.Enums;

namespace Dataverse.XrmTools.DataMigrationTool.Helpers
{
    public class Logger
    {
        public event EventHandler<LoggerEventArgs> OnLog;

        internal virtual void Log(LogLevel level, string message)
        {
            var args = new LoggerEventArgs
            {
                Level = level,
                Message = message
            };

            OnLog?.Invoke(this, args);
        }
    }

    public class LoggerEventArgs : EventArgs
    {
        public LogLevel Level { get; set; }
        public string Message { get; set; }
    }
}
