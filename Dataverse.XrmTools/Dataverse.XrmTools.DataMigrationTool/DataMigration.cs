using System;
using System.Collections.Generic;
using System.ComponentModel.Composition;
using System.IO;
using System.Linq;
using System.Reflection;
using XrmToolBox.Extensibility;
using XrmToolBox.Extensibility.Interfaces;

namespace Dataverse.XrmTools.DataMigrationTool
{
    // Do not forget to update version number and author (company attribute) in AssemblyInfo.cs class
    // To generate Base64 string for Images below, you can use https://www.base64-image.de/
    [Export(typeof(IXrmToolBoxPlugin)),
        ExportMetadata("Name", "Data Migration Tool"),
        ExportMetadata("Description", "Tool used to migrate reference data between Dataverse instances"),
        // Please specify the base64 content of a 32x32 pixels image
        //ExportMetadata("SmallImageBase64", null),
        ExportMetadata("SmallImageBase64", "iVBORw0KGgoAAAANSUhEUgAAACAAAAAgCAYAAABzenr0AAAAAXNSR0IArs4c6QAAAARnQU1BAACxjwv8YQUAAAAJcEhZcwAADsMAAA7DAcdvqGQAAARjSURBVFhH7ZZ9TJVVHMe/z9t9474BXjBUINpNCGtjsHQqZbIWzrCxtZK5WqG13Gxzs39s0WZj1+Uftf7IPyzLtlZ/uMyZc0uQSubIvIaCg0wTQgUUrsCFy8O997nP0+95gfHyXLxkW//weXZ23s/5ne8553ceLLLI/w1jxCgqf28TRdv0nClTbVNEbc/qyTm0djbXf6gmphuwFywTYFwWCJyw4NlSRYpEkYjGfyYDnlHzMw1wCQHL1kKs9D0KnuWMmv+WnpNBhC7dMDfAnekM1LxfjYLMTChJ1BOo+EHUOfp5A1qa2qcM4LVSA4cgYEveIxgCh9YRo3AWFZlAfpqRMQhHx7WQ7UwnA3XlEooMjpm7iJY0q5HSmWHAJBwt0Zrk+LDTlt9+twt1TUdwtqsNUiwGL+/Arie3gPHHcGG0DWVpq7Dn4VoaL/l2mk6TUICobB4UqlNp7buOF7/Zh6bOIKQxEcpEHPcGBrHvx8P4KtiIcCSC073n0DZyVe+QBFMFVrqBFbNknsQrkBH0vXPiIPru9FNKRlbuEmxbuwYCyX7894u4eu0OPB6GFiIhOHgFJd7HjN5zMVXARor5MEJhGD7assmwxKIfwu5QP4LX2qGIMSg2BpUlRcjOssGRxWHD6gLwcQnh0Qgi4yJGJsaMUc0x3+m4CIQ6gca3jQKg8/oAjjd0aOkbd29BpvusUJAZGREpjEGEtG+EPkaJQYxHMRoRsdy2VOuTDHMDyBHhz2PAPdq/vvPoujmE7tvDqKoo1KqtLK+vXoxDGY+h8adL+Kv/Jv4O9aG5qR18OqOtXo4p2LhsjdYnGeYG0ATYcACJF77HqS8/QceFc9j0tB88pzd/ItcPW4KlyUmFnmGMjo/il/NXcaaxA0N8DAkhAUmUUFe2E1n2DK1PMswNIOK0j4HPfsXR3nVYH/2CdD8FTAxpdW67E3s2vwpekuHnfKjfvAveNC+iNLEYjSJbyMSnG+vwenG11n4+TG+BSv1Hx/Db5Vs4/HEtPJbngB9eAgqeB9Z/oNXv3foWdle/BqtgAcuy2Lm2Bj3hPs35rHAvpRug4FDPGQzEwlp7FTtHp3gWSRVYXerHwf2vICfbC3jygcIaoHS3Uatjt9q0yVXUt6PAuxx5nhxyVlRGDounWA0ZghNl3gKUuGmcWcx8C1yOwJs7KpFhZ8A8iMMnZPIJnWO3MSHH4CYP6U97SCv/7uRFBC93J3kNgYCeSw2H0wXBYkV4KEQe0nCRqTFlQNItSAWO48HxSY9RSkxXYB1FVXpufliOy2cF4WW71U4j0BC0eikeaxHHI2eNJvfjD1LgiJr4VztdUnXgWYblTmd46G02YGR5f8PXte8a2ZRZ0G9P3qod+b7c8iJesJaAYSt5jkMiIWkhPiF2uXyl/dl5T6Xn5pUP9PY0G73mZ0EK5BRvP2RzuN6w2pxGiSnqg1FGEtODcn8WZMCy4u0VVrvrcavdOd+RD5Gs315prk8Y+UUWmQfgH+qzjjlZbnjEAAAAAElFTkSuQmCC"),
        // Please specify the base64 content of a 80x80 pixels image
        //ExportMetadata("BigImageBase64", null),
        ExportMetadata("BigImageBase64", "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"),
        ExportMetadata("BackgroundColor", "Lavender"),
        ExportMetadata("PrimaryFontColor", "Black"),
        ExportMetadata("SecondaryFontColor", "Gray")]
    public class DataMigration : PluginBase
    {
        public override IXrmToolBoxPluginControl GetControl()
        {
            return new DataMigrationControl();
        }

        /// <summary>
        /// Constructor 
        /// </summary>
        public DataMigration()
        {
            // If you have external assemblies that you need to load, uncomment the following to 
            // hook into the event that will fire when an Assembly fails to resolve
            // AppDomain.CurrentDomain.AssemblyResolve += new ResolveEventHandler(AssemblyResolveEventHandler);
        }

        /// <summary>
        /// Event fired by CLR when an assembly reference fails to load
        /// Assumes that related assemblies will be loaded from a subfolder named the same as the Plugin
        /// For example, a folder named Sample.XrmToolBox.MyPlugin 
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="args"></param>
        /// <returns></returns>
        private Assembly AssemblyResolveEventHandler(object sender, ResolveEventArgs args)
        {
            Assembly loadAssembly = null;
            Assembly currAssembly = Assembly.GetExecutingAssembly();

            // base name of the assembly that failed to resolve
            var argName = args.Name.Substring(0, args.Name.IndexOf(","));

            // check to see if the failing assembly is one that we reference.
            List<AssemblyName> refAssemblies = currAssembly.GetReferencedAssemblies().ToList();
            var refAssembly = refAssemblies.Where(a => a.Name == argName).FirstOrDefault();

            // if the current unresolved assembly is referenced by our plugin, attempt to load
            if (refAssembly != null)
            {
                // load from the path to this plugin assembly, not host executable
                string dir = Path.GetDirectoryName(currAssembly.Location).ToLower();
                string folder = Path.GetFileNameWithoutExtension(currAssembly.Location);
                dir = Path.Combine(dir, folder);

                var assmbPath = Path.Combine(dir, $"{argName}.dll");

                if (File.Exists(assmbPath))
                {
                    loadAssembly = Assembly.LoadFrom(assmbPath);
                }
                else
                {
                    throw new FileNotFoundException($"Unable to locate dependency: {assmbPath}");
                }
            }

            return loadAssembly;
        }
    }
}