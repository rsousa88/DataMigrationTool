// System
using System.IO;
using System.Linq;
using System.Collections.Generic;

// Newtonsoft
using Newtonsoft.Json;

// DataMigrationTool
using Dataverse.XrmTools.DataMigrationTool.Models;
using Dataverse.XrmTools.DataMigrationTool.AppSettings;

namespace Dataverse.XrmTools.DataMigrationTool.Logic
{
    public static class DmtFileService
    {
        private static readonly JsonSerializerSettings _jsonSettings = new JsonSerializerSettings
        {
            Formatting = Formatting.Indented,
            NullValueHandling = NullValueHandling.Ignore
        };

        public static DmtSettings Load(string filePath)
        {
            var json = File.ReadAllText(filePath);
            return JsonConvert.DeserializeObject<DmtSettings>(json, _jsonSettings)
                ?? throw new System.Exception("Settings file is empty or invalid.");
        }

        public static void Save(string filePath, DmtSettings settings)
        {
            var json = JsonConvert.SerializeObject(settings, _jsonSettings);
            File.WriteAllText(filePath, json);
        }

        public static DmtSettings CreateNew(string tableLogicalName, string tableDisplayName,
            string primaryIdAttribute, string primaryNameAttribute,
            string orgUniqueName, string orgFriendlyName,
            TableSettings existingAppDataSettings = null)
        {
            var settings = new DmtSettings
            {
                Environment = new DmtEnvironmentInfo
                {
                    UniqueName = orgUniqueName,
                    FriendlyName = orgFriendlyName
                },
                Table = new DmtTableInfo
                {
                    LogicalName = tableLogicalName,
                    DisplayName = tableDisplayName,
                    PrimaryIdAttribute = primaryIdAttribute,
                    PrimaryNameAttribute = primaryNameAttribute
                }
            };

            // transparent migration from AppData
            if (existingAppDataSettings != null)
            {
                if (existingAppDataSettings.DeselectedAttributes != null)
                    settings.DeselectedAttributes = new List<string>(existingAppDataSettings.DeselectedAttributes);
                if (!string.IsNullOrWhiteSpace(existingAppDataSettings.Filter))
                    settings.Filter = existingAppDataSettings.Filter;
                if (existingAppDataSettings.ExcelConfig != null)
                    settings.ExcelConfig = existingAppDataSettings.ExcelConfig;
            }

            return settings;
        }

        /// <summary>
        /// Returns (true, null) on match; (false, warningMessage) on mismatch.
        /// </summary>
        public static (bool matches, string warning) ValidateEnvironment(
            DmtSettings settings, string currentUniqueName, string currentFriendlyName)
        {
            if (settings?.Environment == null) return (true, null);

            if (settings.Environment.UniqueName == currentUniqueName) return (true, null);

            var warning = $"This file was created for '{settings.Environment.FriendlyName}' ({settings.Environment.UniqueName}).\n" +
                          $"You are connected to '{currentFriendlyName}' ({currentUniqueName}).\n\n" +
                          "Proceeding may cause lookup resolution errors.";

            return (false, warning);
        }

        /// <summary>
        /// Returns true if the file's table matches the given logicalName.
        /// </summary>
        public static bool ValidateTable(DmtSettings settings, string tableLogicalName)
        {
            return settings?.Table?.LogicalName?.Equals(tableLogicalName,
                System.StringComparison.OrdinalIgnoreCase) == true;
        }
    }
}
