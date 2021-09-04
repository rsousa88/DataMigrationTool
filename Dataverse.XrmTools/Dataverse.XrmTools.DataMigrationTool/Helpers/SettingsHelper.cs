// System
using System.Collections.Generic;

// DataMigrationTool
using Dataverse.XrmTools.DataMigrationTool.AppSettings;

// 3rd Party
using XrmToolBox.Extensibility;

namespace Dataverse.XrmTools.DataMigrationTool.Helpers
{
    public class SettingsHelper
    {
        public static void GetSettings(out Settings settings)
        {
            try
            {
                if (!SettingsManager.Instance.TryLoad(typeof(DataMigrationControl), out settings))
                {
                    settings = new Settings { Instances = new List<Instance>(), TableSettings = new List<TableSettings>() };
                    SetSettings(settings);
                }
            }
            catch { throw; }
        }

        public static bool SetSettings(Settings settings)
        {
            try
            {
                SettingsManager.Instance.Save(typeof(DataMigrationControl), settings);
                return true;
            }
            catch { throw; }
        }
    }
}