// System
using System;
using System.Collections.Generic;

// DataMigrationTool
using Dataverse.XrmTools.DataMigrationTool.Logic;

// 3rd Party
using Xunit;

namespace Dataverse.XrmTools.DataMigrationTool.Tests
{
    public class ExecutionResultServiceTests
    {
        [Fact]
        public void GetSuccessfulIdMap_ReturnsCreateAndUpdateIdsOnly()
        {
            var createdId = Guid.NewGuid();
            var updatedId = Guid.NewGuid();
            var deletedId = Guid.NewGuid();
            var failedId = Guid.NewGuid();

            var ids = ExecutionResultService.GetSuccessfulIdMap(new List<ExecutionResultRow>
            {
                new ExecutionResultRow { Action = "Create", Id = createdId.ToString(), Description = "Created" },
                new ExecutionResultRow { Action = "Update", Id = updatedId.ToString(), Description = "Updated" },
                new ExecutionResultRow { Action = "Delete", Id = deletedId.ToString(), Description = "Deleted" },
                new ExecutionResultRow { Action = "Create", Id = failedId.ToString(), Description = "ERROR: failed" },
                new ExecutionResultRow { Action = "Create", Id = "not-a-guid", Description = "Created" }
            });

            Assert.True(ids.ContainsKey(createdId));
            Assert.True(ids.ContainsKey(updatedId));
            Assert.False(ids.ContainsKey(deletedId));
            Assert.False(ids.ContainsKey(failedId));
            Assert.Equal(2, ids.Count);
        }

        [Theory]
        [InlineData("Create", "Created", true)]
        [InlineData("Update", "", true)]
        [InlineData("create", null, true)]
        [InlineData("Delete", "Deleted", false)]
        [InlineData("Create", "ERROR: duplicate", false)]
        public void IsSuccessfulWriteAction_ClassifiesRows(string action, string description, bool expected)
        {
            Assert.Equal(expected, ExecutionResultService.IsSuccessfulWriteAction(action, description));
        }
    }
}
