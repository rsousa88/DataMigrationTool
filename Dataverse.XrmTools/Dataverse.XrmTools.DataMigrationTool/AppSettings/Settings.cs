// System
using System;
using System.Linq;
using System.Collections.Generic;

namespace Dataverse.XrmTools.DataMigrationTool.AppSettings
{
    public class Settings
    {
        public List<Instance> Instances { get; set; }
        public List<TableSettings> TableSettings { get; set; }
        public List<Sort> Sorts { get; set; }
        public string LastDataFile { get; set; }

        public Instance this[Guid orgId]
        {
            get
            {
                if (Instances == null)
                {
                    Instances = new List<Instance>();
                }

                if (!Instances.Any(org => org.Id.Equals(orgId)))
                {
                    Instances.Add(new Instance { Id = orgId, Mappings = new List<Mapping>() });
                }

                return Instances.Where(org => org.Id.Equals(orgId)).FirstOrDefault();
            }
        }

        public TableSettings this[string logicalName]
        {
            get
            {
                if (TableSettings == null)
                {
                    TableSettings = new List<TableSettings>();
                }

                if (!TableSettings.Any(tbs => tbs.LogicalName.Equals(logicalName)))
                {
                    TableSettings.Add(new TableSettings { LogicalName = logicalName });
                }

                return TableSettings.FirstOrDefault(tbs => tbs.LogicalName.Equals(logicalName));
            }
        }
    }
}
