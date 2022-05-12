// Microsoft
using System.Collections.Generic;

namespace Dataverse.XrmTools.DataMigrationTool.Models
{
    public class Record
    {
        public IEnumerable<RecordAttribute> Attributes { get; set; }
    }
}
