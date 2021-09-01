// System
using System;
using System.Collections.Generic;

namespace Dataverse.XrmTools.DataMigrationTool.AppSettings
{
    public class Instance
    {
        public Guid Id { get; set; }
        public string Name { get; set; }
        public List<Mapping> Mappings { get; set; }
        public List<string> DefaultDeselected
        {
            get
            {
                return new List<string>
                {
                    "importsequencenumber",
                    "utcconversiontimezonecode",
                    "timezoneruleversionnumber",
                    "overriddencreatedon"
                };
            }
        }
    }
}
