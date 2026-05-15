// System
using System.Collections.Generic;
using System.Linq;

// DataMigrationTool
using Dataverse.XrmTools.DataMigrationTool.Logic;

// 3rd Party
using Xunit;

namespace Dataverse.XrmTools.DataMigrationTool.Tests
{
    public class BatchingServiceTests
    {
        [Fact]
        public void Batch_SplitsItemsByBatchSize()
        {
            var batches = BatchingService.Batch(new[] { 1, 2, 3, 4, 5 }, 2).ToList();

            Assert.Equal(3, batches.Count);
            Assert.Equal(new List<int> { 1, 2 }, batches[0]);
            Assert.Equal(new List<int> { 3, 4 }, batches[1]);
            Assert.Equal(new List<int> { 5 }, batches[2]);
        }

        [Fact]
        public void Batch_UsesOneWhenBatchSizeIsInvalid()
        {
            var batches = BatchingService.Batch(new[] { 1, 2 }, 0).ToList();

            Assert.Equal(2, batches.Count);
            Assert.All(batches, batch => Assert.Single(batch));
        }

        [Fact]
        public void Batch_ReturnsNoBatchesForNullSource()
        {
            Assert.Empty(BatchingService.Batch<int>(null, 10));
        }
    }
}
