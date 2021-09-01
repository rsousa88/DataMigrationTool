// System
using System;

namespace Dataverse.XrmTools.DataMigrationTool.Models
{
    public class CrmBulkResponse
    {
        public Guid Id { get; set; }
        public bool Success { get; set; }
        public string Message { get; set; }
    }
}
