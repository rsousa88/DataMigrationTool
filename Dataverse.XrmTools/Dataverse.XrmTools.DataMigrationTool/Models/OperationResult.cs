// System
using System.Windows.Forms;
using System.Collections.Generic;
using System;

namespace Dataverse.XrmTools.DataMigrationTool.Models
{
    public class OperationResult
    {
        public IEnumerable<ListViewItem> Items { get; set; }
        public Dictionary<Guid, Guid> SuccessfulIdMap { get; set; } = new Dictionary<Guid, Guid>();
    }
}
