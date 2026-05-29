using System;
using System.Linq;

using Dataverse.XrmTools.DataMigrationTool.Models;

namespace Dataverse.XrmTools.DataMigrationTool.Logic
{
    public static class EnvironmentTagHelper
    {
        public const string DefaultTag = "TGT";

        public static string NormalizeTag(string tag)
        {
            if (string.IsNullOrWhiteSpace(tag)) return null;

            var normalized = new string(tag
                .Where(char.IsLetterOrDigit)
                .Take(5)
                .ToArray());

            return string.IsNullOrWhiteSpace(normalized)
                ? null
                : normalized.ToUpperInvariant();
        }

        public static string GetTag(DmtEnvironmentInfo environment)
        {
            return NormalizeTag(environment?.Tag)
                ?? DeriveTag(environment?.FriendlyName, environment?.UniqueName);
        }

        public static string GetTag(DmtProjectEnvironment environment)
        {
            return NormalizeTag(environment?.Tag)
                ?? DeriveTag(environment?.FriendlyName, environment?.UniqueName);
        }

        public static string DeriveTag(string friendlyName, string uniqueName)
        {
            var name = !string.IsNullOrWhiteSpace(friendlyName) ? friendlyName : uniqueName;
            if (string.IsNullOrWhiteSpace(name)) return DefaultTag;

            var derived = new string(name
                .Where(char.IsLetterOrDigit)
                .Take(3)
                .ToArray());

            return string.IsNullOrWhiteSpace(derived)
                ? DefaultTag
                : derived.ToUpperInvariant();
        }

        public static bool IsValidTag(string tag)
        {
            if (string.IsNullOrWhiteSpace(tag)) return true;

            var normalized = NormalizeTag(tag);
            return string.Equals(normalized, tag.Trim(), StringComparison.OrdinalIgnoreCase)
                && normalized.Length >= 2
                && normalized.Length <= 5;
        }
    }
}
