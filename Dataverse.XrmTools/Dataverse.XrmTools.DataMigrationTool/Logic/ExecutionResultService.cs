// System
using System;
using System.Collections.Generic;
using System.Linq;

namespace Dataverse.XrmTools.DataMigrationTool.Logic
{
    public class ExecutionResultRow
    {
        public string Action { get; set; }
        public string Id { get; set; }
        public string Description { get; set; }
    }

    public static class ExecutionResultService
    {
        public static Dictionary<Guid, Guid> GetSuccessfulIdMap(IEnumerable<ExecutionResultRow> rows)
        {
            var ids = new Dictionary<Guid, Guid>();
            foreach (var row in rows ?? Enumerable.Empty<ExecutionResultRow>())
            {
                if (!IsSuccessfulWriteAction(row?.Action, row?.Description))
                    continue;

                if (Guid.TryParse(row.Id, out var id))
                    ids[id] = id;
            }

            return ids;
        }

        public static bool IsSuccessfulWriteAction(string action, string description)
        {
            if (!(string.Equals(action, "Create", StringComparison.OrdinalIgnoreCase)
                || string.Equals(action, "Update", StringComparison.OrdinalIgnoreCase)))
                return false;

            return string.IsNullOrWhiteSpace(description)
                || !description.StartsWith("ERROR:", StringComparison.OrdinalIgnoreCase);
        }
    }
}
