// System
using System;
using System.Collections.Generic;
using System.Linq;

// Microsoft
using Microsoft.Xrm.Sdk;

// DataMigrationTool
using Dataverse.XrmTools.DataMigrationTool.Models;

namespace Dataverse.XrmTools.DataMigrationTool.Logic
{
    public enum PlanLookupResolutionStatus
    {
        NotFound,
        Found,
        Ambiguous
    }

    public class PlanLookupResolution
    {
        public PlanLookupResolutionStatus Status { get; set; }
        public Guid? Id { get; set; }
        public string Message { get; set; }

        public static PlanLookupResolution NotFound()
        {
            return new PlanLookupResolution { Status = PlanLookupResolutionStatus.NotFound };
        }

        public static PlanLookupResolution Found(Guid id)
        {
            return new PlanLookupResolution { Status = PlanLookupResolutionStatus.Found, Id = id };
        }

        public static PlanLookupResolution Ambiguous(string message)
        {
            return new PlanLookupResolution { Status = PlanLookupResolutionStatus.Ambiguous, Message = message };
        }
    }

    public interface IPlanLookupResolver
    {
        PlanLookupResolution ResolveByAlternateKey(string logicalName, IDictionary<string, object> keyValues);
    }

    public class PlanLookupContext : IPlanLookupResolver
    {
        private readonly Dictionary<string, List<PlanLookupRecord>> _recordsByTable =
            new Dictionary<string, List<PlanLookupRecord>>(StringComparer.OrdinalIgnoreCase);

        public int RecordCount => _recordsByTable.Values.Sum(records => records.Count);

        public void AddRecordCollection(RecordCollection collection, IDictionary<Guid, Guid> importedIdMap = null)
        {
            if (collection?.Records == null || string.IsNullOrWhiteSpace(collection.LogicalName))
                return;

            if (!_recordsByTable.TryGetValue(collection.LogicalName, out var records))
            {
                records = new List<PlanLookupRecord>();
                _recordsByTable[collection.LogicalName] = records;
            }

            foreach (var record in collection.Records)
            {
                if (!RecordCollectionService.TryGetRecordId(record, collection.PrimaryIdAttribute, out var requestId))
                    continue;

                var actualId = importedIdMap != null && importedIdMap.TryGetValue(requestId, out var mappedId)
                    ? mappedId
                    : requestId;

                records.Add(new PlanLookupRecord
                {
                    Id = actualId,
                    Attributes = (record.Attributes ?? new List<RecordAttribute>())
                        .GroupBy(attr => attr.Key ?? string.Empty, StringComparer.OrdinalIgnoreCase)
                        .ToDictionary(g => g.Key, g => NormalizeValue(g.Last().Value), StringComparer.OrdinalIgnoreCase)
                });
            }
        }

        public PlanLookupResolution ResolveByAlternateKey(string logicalName, IDictionary<string, object> keyValues)
        {
            if (string.IsNullOrWhiteSpace(logicalName) || keyValues == null || !keyValues.Any())
                return PlanLookupResolution.NotFound();

            if (!_recordsByTable.TryGetValue(logicalName, out var records) || !records.Any())
                return PlanLookupResolution.NotFound();

            var normalizedKeys = keyValues.ToDictionary(
                kvp => kvp.Key,
                kvp => NormalizeValue(kvp.Value),
                StringComparer.OrdinalIgnoreCase);

            if (normalizedKeys.Values.All(string.IsNullOrWhiteSpace))
                return PlanLookupResolution.NotFound();

            var matches = records
                .Where(record => Matches(record, normalizedKeys))
                .Select(record => record.Id)
                .Distinct()
                .ToList();

            if (matches.Count == 0)
                return PlanLookupResolution.NotFound();
            if (matches.Count == 1)
                return PlanLookupResolution.Found(matches[0]);

            return PlanLookupResolution.Ambiguous($"Plan lookup for '{logicalName}' matched {matches.Count} records for keys: {FormatKeyValues(keyValues)}");
        }

        private static bool Matches(PlanLookupRecord record, Dictionary<string, string> normalizedKeys)
        {
            foreach (var key in normalizedKeys)
            {
                if (!record.Attributes.TryGetValue(key.Key, out var value))
                    return false;
                if (!string.Equals(value, key.Value, StringComparison.OrdinalIgnoreCase))
                    return false;
            }

            return true;
        }

        private static string NormalizeValue(object value)
        {
            if (value == null)
                return string.Empty;
            if (value is EntityReference reference)
                return reference.Id.ToString("D");
            if (value is OptionSetValue option)
                return option.Value.ToString(System.Globalization.CultureInfo.InvariantCulture);
            if (value is Money money)
                return money.Value.ToString(System.Globalization.CultureInfo.InvariantCulture);
            if (value is Guid guid)
                return guid.ToString("D");
            if (value is DateTime date)
                return date.ToString("O", System.Globalization.CultureInfo.InvariantCulture);
            if (value is IFormattable formattable)
                return formattable.ToString(null, System.Globalization.CultureInfo.InvariantCulture);

            return value.ToString();
        }

        private static string FormatKeyValues(IDictionary<string, object> keyValues)
        {
            return string.Join(", ", keyValues.Select(kvp => $"{kvp.Key}={NormalizeValue(kvp.Value)}"));
        }

        private class PlanLookupRecord
        {
            public Guid Id { get; set; }
            public Dictionary<string, string> Attributes { get; set; }
        }
    }
}
