// System
using System;
using System.Collections.Generic;

namespace Dataverse.XrmTools.DataMigrationTool.Logic
{
    public static class BatchingService
    {
        public static IEnumerable<List<T>> Batch<T>(IEnumerable<T> source, int batchSize)
        {
            if (source == null)
                yield break;

            var size = Math.Max(batchSize, 1);
            var batch = new List<T>(size);

            foreach (var item in source)
            {
                batch.Add(item);
                if (batch.Count < size)
                    continue;

                yield return batch;
                batch = new List<T>(size);
            }

            if (batch.Count > 0)
                yield return batch;
        }
    }
}
