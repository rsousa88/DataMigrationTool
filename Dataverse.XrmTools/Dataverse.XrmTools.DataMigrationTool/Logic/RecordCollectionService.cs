// System
using System;
using System.Collections.Generic;
using System.Linq;

// DataMigrationTool
using Dataverse.XrmTools.DataMigrationTool.Models;

namespace Dataverse.XrmTools.DataMigrationTool.Logic
{
    public static class RecordCollectionService
    {
        public static RecordCollection FilterByIds(RecordCollection collection, IEnumerable<Guid> ids)
        {
            if (collection == null)
                return null;

            var idSet = new HashSet<Guid>(ids ?? Enumerable.Empty<Guid>());
            var records = (collection.Records ?? Enumerable.Empty<Record>())
                .Where(record => TryGetRecordId(record, collection.PrimaryIdAttribute, out var id) && idSet.Contains(id))
                .ToList();

            return new RecordCollection
            {
                LogicalName = collection.LogicalName,
                PrimaryIdAttribute = collection.PrimaryIdAttribute,
                Records = records,
                Count = records.Count,
                ImportErrors = collection.ImportErrors,
                ImportMatchKey = collection.ImportMatchKey,
                ImportMatchKeys = collection.ImportMatchKeys,
                ImportMatchKeyMode = collection.ImportMatchKeyMode
            };
        }

        public static bool TryGetRecordId(Record record, string primaryIdAttribute, out Guid id)
        {
            id = Guid.Empty;
            if (record == null || string.IsNullOrWhiteSpace(primaryIdAttribute))
                return false;

            var attr = record.Attributes?.FirstOrDefault(a => a.Key.Equals(primaryIdAttribute, StringComparison.OrdinalIgnoreCase));
            if (attr?.Value == null)
                return false;

            if (attr.Value is Guid guid)
            {
                id = guid;
                return true;
            }

            return Guid.TryParse(attr.Value.ToString(), out id);
        }
    }
}
