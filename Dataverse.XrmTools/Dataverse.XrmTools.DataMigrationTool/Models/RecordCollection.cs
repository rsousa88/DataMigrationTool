// System
using System.Linq;
using System.Collections.Generic;
using System.Runtime.Serialization;

// Microsoft
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Metadata;

namespace Dataverse.XrmTools.DataMigrationTool.Models
{
    [DataContract]
    public class RecordCollection
    {
        [DataMember]
        public string LogicalName { get; set; }
        [DataMember]
        public string PrimaryIdAttribute { get; set; }
        [DataMember]
        public IEnumerable<Record> Records { get; set; }
        [DataMember]
        public int Count { get; set; }

        public RecordCollection(EntityCollection entityCollection, EntityMetadata metadata)
        {
            var records = entityCollection.Entities.Select(ent => new Record
            {
                Attributes = ent.Attributes
            });

            LogicalName = entityCollection.EntityName;
            PrimaryIdAttribute = metadata.PrimaryIdAttribute;
            Records = records;
            Count = records.Count();
        }
    }
}
