// Microsoft
using System.Collections.Generic;

namespace Dataverse.XrmTools.DataMigrationTool.Models
{
    public class Record
    {
        public int SourceRowNumber { get; set; }
        public bool PrimaryIdWasBlank { get; set; }
        public IEnumerable<RecordAttribute> Attributes { get; set; }
    }
}
