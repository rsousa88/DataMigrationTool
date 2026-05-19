// System
using System;
using System.IO;
using System.Xml;
using System.Text;
using System.Linq;
using System.Data;
using System.Windows.Forms;
using System.Collections.Generic;
using System.Globalization;

// Microsoft
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Metadata;

// DataMigrationTool
using Dataverse.XrmTools.DataMigrationTool.Enums;
using Dataverse.XrmTools.DataMigrationTool.Models;
using Dataverse.XrmTools.DataMigrationTool.External;
using Dataverse.XrmTools.DataMigrationTool.AppSettings;

// 3rd Party
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Dataverse.XrmTools.DataMigrationTool.Helpers
{
    public static class Utils
    {
        public delegate bool TryParseHandler<T>(string value, out T result);
        public static T? TryParse<T>(this string value, TryParseHandler<T> handler) where T : struct
        {
            if (string.IsNullOrEmpty(value)) { return null; }

            if (handler(value, out T result)) { return result; }

            return null;
        }

        public static int? ToInt<T>(this T value)
        {
            return value.ToString().TryParse<int>(int.TryParse);
        }

        public static Guid ToGuid<T>(this T value)
        {
            var guid = value.ToString().TryParse<Guid>(Guid.TryParse);
            return guid.HasValue ? guid.Value : Guid.Empty;
        }

        public static ListViewItem ToListViewItem<T>(this T value, Tuple<string, object> parameters = null)
        {
            if (value is Table) {
                var table = value as Table;
                return new ListViewItem(new string[] { table.LogicalName, table.DisplayName });
            }
            if (value is Models.Attribute) {
                var attribute = value as Models.Attribute;
                return new ListViewItem(new string[] { attribute.LogicalName, attribute.DisplayName, attribute.Type }); ;
            }
            if (value is Mapping) {
                var mapping = value as Mapping;
                if (parameters == null || !parameters.Item1.Equals("mappingtype") || !(parameters.Item2 is Enums.MappingType mappingType)) { throw new Exception("Invalid parameters for Mapping type cast"); }

                var columns = new List<string>
                {
                    mapping.Type.ToString(),
                    mapping.TableDisplayName,
                    mapping.TableLogicalName
                };

                if (mappingType.Equals(Enums.MappingType.Attribute))
                {
                    columns.Add(mapping.AttributeDisplayName);
                    columns.Add(mapping.AttributeLogicalName);
                }
                if (mappingType.Equals(Enums.MappingType.Value))
                {
                    columns.Add(mapping.SourceId.ToString());
                    columns.Add(mapping.TargetId.ToString());
                    columns.Add(mapping.TargetInstanceName.ToString());
                }

                columns.Add(mapping.State.ToString());

                return new ListViewItem(columns.ToArray());
            }
            if (value is Entity)
            {
                var entity = value as Entity;
                if (parameters == null || !parameters.Item1.Equals("table") || !(parameters.Item2 is Dictionary<string, string> dictionary)) { throw new Exception("Invalid parameters for Entity type cast"); }

                var attrName = dictionary.FirstOrDefault(kvp => kvp.Key.Equals("attributename")).Value;
                var actionName = dictionary.FirstOrDefault(kvp => kvp.Key.Equals("action")).Value;
                var description = dictionary.FirstOrDefault(kvp => kvp.Key.Equals("description")).Value;
                var columns = (dictionary.FirstOrDefault(kvp => kvp.Key.Equals("columns")).Value ?? string.Empty)
                    .Split(new[] { '|' }, StringSplitOptions.RemoveEmptyEntries)
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToList();

                var values = new List<string>
                {
                    actionName,
                    entity.Id.ToString(),
                    !string.IsNullOrEmpty(attrName) ? entity.GetAttributeValue<string>(attrName) : string.Empty,
                    description
                };
                values.AddRange(columns.Select(column => entity.Attributes.Contains(column) && entity[column] != null ? FormatListViewValue(entity[column]) : string.Empty));

                return new ListViewItem(values.ToArray());
            }
            if (value is ExecuteMultipleResponseItem)
            {
                var response = value as ExecuteMultipleResponseItem;
                if (parameters == null || !parameters.Item1.Equals("table") || !(parameters.Item2 is Table table)) { throw new Exception("Invalid parameters for ExecuteMultipleResponseItem type cast"); }

                return new ListViewItem(new string[] { table.DisplayName, response.Fault.Message });
            }

            return null;
        }

        private static string FormatListViewValue(object value)
        {
            if (value == null) return string.Empty;
            if (value is EntityReference reference) return reference.Id.ToString("D");
            if (value is OptionSetValue option) return option.Value.ToString();
            if (value is OptionSetValueCollection options) return string.Join(", ", options.Select(o => o.Value));
            if (value is Money money) return money.Value.ToString(CultureInfo.InvariantCulture);
            return value.ToString();
        }

        public static object ToObject<T>(this ListViewItem lvItem, T output, Tuple<string, object> parameters = null)
        {
            if (output is Table)
            {
                return new Table
                {
                    LogicalName = lvItem.SubItems[0].Text,
                    DisplayName = lvItem.SubItems[1].Text
                };
            }
            if (output is Models.Attribute)
            {
                return new Models.Attribute
                {
                    LogicalName = lvItem.SubItems[0].Text,
                    DisplayName = lvItem.SubItems[1].Text,
                    Type = lvItem.SubItems[2].Text
                };
            }
            if (output is Mapping)
            {
                if (parameters == null || !parameters.Item1.Equals("mappingtype") || !(parameters.Item2 is Enums.MappingType mappingType)) { throw new Exception("Invalid parameters for Mapping type cast"); }
                var mapping = new Mapping
                {
                    Type = lvItem.SubItems[0].Text.ToEnum<Enums.MappingType>(),
                    TableDisplayName = lvItem.SubItems[1].Text,
                    TableLogicalName = lvItem.SubItems[2].Text
                };

                if(mappingType.Equals(Enums.MappingType.Attribute))
                {
                    mapping.AttributeDisplayName = lvItem.SubItems[3].Text;
                    mapping.AttributeLogicalName = lvItem.SubItems[4].Text;
                    mapping.State = lvItem.SubItems[5].Text.ToEnum<MappingState>();
                }

                if (mappingType.Equals(Enums.MappingType.Value))
                {
                    mapping.SourceId = lvItem.SubItems[3].Text.ToGuid();
                    mapping.TargetId = lvItem.SubItems[4].Text.ToGuid();
                    mapping.TargetInstanceName = lvItem.SubItems[5].Text;
                    mapping.State = lvItem.SubItems[6].Text.ToEnum<MappingState>();
                }

                return mapping;
            }
            if (output is Entity)
            {
                if (parameters == null || !parameters.Item1.Equals("table") || !(parameters.Item2 is Dictionary<string, string> dictionary)) { throw new Exception("Invalid parameters for Entity type cast"); }

                var logicalName = dictionary.FirstOrDefault(kvp => kvp.Key.Equals("logicalname")).Value;

                return new Entity(logicalName, Guid.Parse(lvItem.SubItems[1].Text));
            }

            return null;
        }

        public static T DeserializeObject<T>(this string json)
        {
            return JsonConvert.DeserializeObject<T>(json);
        }

        public static T DeserializeObject<T>(this string json, params JsonConverter[] converters)
        {
            return JsonConvert.DeserializeObject<T>(json, converters);
        }

        public static string SerializeObject(this object value)
        {
            return JsonConvert.SerializeObject(value, Newtonsoft.Json.Formatting.Indented);
        }

        public static string SerializeObject(this object value, params JsonConverter[] converters)
        {
            return JsonConvert.SerializeObject(value, Newtonsoft.Json.Formatting.Indented, converters);
        }

        public static T ToEnum<T>(this string enumString)
        {
            return (T)Enum.Parse(typeof(T), enumString);
        }

        public static IEnumerable<RecordAttribute> MapAttributes(this AttributeCollection collection, EntityMetadata metadata)
        {
            var attributes = collection.Select(kvp => new RecordAttribute
            {
                Type = metadata.Attributes.FirstOrDefault(attr => attr.LogicalName.Equals(kvp.Key)).ToAttributeType(),
                Key = kvp.Key,
                Value = kvp.Value
            });

            return attributes;
        }

        public static AttributeCollection MapAttributes(this IEnumerable<RecordAttribute> attributes, AttributeMetadata[] attributeMetadata)
        {
            var dictionary = attributes.Select(attr =>
            {
                // UniqueIdentifier
                if (attr.Type.Equals(AttributeType.Identifier) && attr.Value is string)
                {
                    var id = Guid.Parse(attr.Value.ToString());

                    return new KeyValuePair<string, object>(attr.Key, id);
                }

                // EntityReference
                if (attr.Type.Equals(AttributeType.EntityReference) && attr.Value is EntityReference)
                {
                    return new KeyValuePair<string, object>(attr.Key, attr.Value);
                }
                if (attr.Type.Equals(AttributeType.EntityReference) && attr.Value is JObject referenceObj && referenceObj.ContainsKey("Id") && referenceObj.ContainsKey("LogicalName"))
                {
                    var logicalName = (referenceObj.GetValue("LogicalName") as JValue).Value.ToString();
                    var id = (referenceObj.GetValue("Id") as JValue).Value.ToString();

                    var reference = new EntityReference(logicalName, Guid.Parse(id));
                    return new KeyValuePair<string, object>(attr.Key, reference);
                }

                // OptionSetValue
                if (attr.Type.Equals(AttributeType.OptionSet) && attr.Value is OptionSetValue)
                {
                    return new KeyValuePair<string, object>(attr.Key, attr.Value);
                }
                if (attr.Type.Equals(AttributeType.OptionSet) && attr.Value is JObject optionObj && optionObj.ContainsKey("Value"))
                {
                    var value = (optionObj.GetValue("Value") as JValue).Value.ToString().ToInt();
                    var optionSet = new OptionSetValue(value.Value);

                    return new KeyValuePair<string, object>(attr.Key, optionSet);
                }

                // OptionSetValueCollection
                if (attr.Type.Equals(AttributeType.MultiOptionSet) && attr.Value is OptionSetValueCollection)
                {
                    return new KeyValuePair<string, object>(attr.Key, attr.Value);
                }
                if (attr.Type.Equals(AttributeType.MultiOptionSet) && attr.Value is JArray)
                {
                    var options = (attr.Value as JArray)
                        .Select(ops => ops as JObject)
                        .Where(ops => ops.ContainsKey("Value"))
                        .Select(ops =>
                        {
                            var value = (ops.GetValue("Value") as JValue).Value.ToString().ToInt();

                            return new OptionSetValue(value.Value);
                        });

                    var optionSet = new OptionSetValueCollection(options.ToList());
                    return new KeyValuePair<string, object>(attr.Key, optionSet);
                }
                
                // Handle number value types
                if (attr.Type.Equals(AttributeType.Standard))
                {
                    var attrValue = attr.Value;
                    var attrMeta = attributeMetadata.FirstOrDefault(item => item.LogicalName == attr.Key);
                    if (attrMeta != null && attrMeta.AttributeType.HasValue)
                    {
                        switch (attrMeta.AttributeType.Value)
                        {
                            case AttributeTypeCode.BigInt:
                                attrValue = attr.Value is JToken bigIntToken
                                    ? bigIntToken.ToObject<long>()
                                    : Convert.ToInt64(attr.Value, CultureInfo.InvariantCulture);
                                break;
                            case AttributeTypeCode.Decimal:
                                attrValue = attr.Value is JToken decimalToken
                                    ? decimalToken.ToObject<decimal>()
                                    : Convert.ToDecimal(attr.Value, CultureInfo.InvariantCulture);
                                break;
                            case AttributeTypeCode.Double:
                                attrValue = attr.Value is JToken doubleToken
                                    ? doubleToken.ToObject<double>()
                                    : Convert.ToDouble(attr.Value, CultureInfo.InvariantCulture);
                                break;
                            case AttributeTypeCode.Integer:
                                attrValue = attr.Value is JToken integerToken
                                    ? integerToken.ToObject<int>()
                                    : Convert.ToInt32(attr.Value, CultureInfo.InvariantCulture);
                                break;
                            case AttributeTypeCode.Money:
                                if (attr.Value is Money)
                                {
                                    attrValue = attr.Value;
                                    break;
                                }
                                var moneyObj = attr.Value as JObject;
                                if (moneyObj != null && moneyObj.ContainsKey("Value"))
                                {
                                    attrValue = new Money(moneyObj.GetValue("Value").ToObject<decimal>());
                                }
                                break;
                        }
                    }
                    return new KeyValuePair<string, object>(attr.Key, attrValue);
                }
                return new KeyValuePair<string, object>(attr.Key, attr.Value);
            });
            var collection = new AttributeCollection();
            collection.AddRange(dictionary);

            return collection;
        }

        public static AttributeType ToAttributeType(this AttributeMetadata metadata)
        {
            switch (metadata.AttributeType)
            {
                case AttributeTypeCode.Uniqueidentifier:
                    return AttributeType.Identifier;

                case AttributeTypeCode.Customer:
                case AttributeTypeCode.Lookup:
                case AttributeTypeCode.Owner:
                    return AttributeType.EntityReference;

                case AttributeTypeCode.Picklist:
                case AttributeTypeCode.State:
                case AttributeTypeCode.Status:
                    return AttributeType.OptionSet;

                case AttributeTypeCode.Virtual:
                    return metadata.AttributeTypeName.Value.Equals("MultiSelectPicklistType") ? AttributeType.MultiOptionSet : AttributeType.Standard;

                default:
                    return AttributeType.Standard;
            }
        }

        public static EntityCollection ToEntityCollection(this RecordCollection collection, AttributeMetadata[] attributeMetadata)
        {
            var entityList = new List<Entity>();
            foreach (var rec in collection.Records)
            {
                var attributes = rec.Attributes.ToList();
                var idAttr = attributes.Where(attr => attr.Key.Equals(collection.PrimaryIdAttribute)).FirstOrDefault();
                Guid id;
                if (idAttr == null)
                {
                    id = Guid.NewGuid();
                    attributes.Add(new RecordAttribute
                    {
                        Key = collection.PrimaryIdAttribute,
                        Type = AttributeType.Identifier,
                        Value = id
                    });
                }
                else if (idAttr.Value == null || string.IsNullOrWhiteSpace(idAttr.Value.ToString()))
                {
                    id = Guid.NewGuid();
                    idAttr.Value = id;
                    idAttr.Type = AttributeType.Identifier;
                }
                else if (!Guid.TryParse(idAttr.Value.ToString(), out id))
                {
                    throw new FormatException($"Record primary id '{collection.PrimaryIdAttribute}' is not a valid GUID: {idAttr.Value}");
                }
                else
                {
                    idAttr.Value = id; // manually convert to Guid
                    idAttr.Type = AttributeType.Identifier;
                }

                var entity = new Entity
                {
                    Id = id,
                    LogicalName = collection.LogicalName,
                    Attributes = attributes.MapAttributes(attributeMetadata)
                }.ToEntity<Entity>();

                rec.Attributes = attributes;
                entityList.Add(entity);
            }

            return new EntityCollection(entityList)
            {
                TotalRecordCount = collection.Count,
                EntityName = collection.LogicalName
            };
        }

        public static bool MatchFilter(this Table table, string filter)
        {
            if (string.IsNullOrWhiteSpace(filter)) { return true; }

            filter = filter.ToLower();

            if (table.DisplayName.ToLower().Contains(filter) || table.LogicalName.ToLower().Contains(filter)) { return true; }
            return false;
        }

        public static void Sort(this ListView listview, Settings settings, int column)
        {
            var savedSort = settings.Sorts.FirstOrDefault(sor => sor.ListViewName.Equals(listview.Name));
            if (savedSort == null)
            {
                savedSort = new Sort { ListViewName = listview.Name };
                settings.Sorts.Add(savedSort);
            }

            if (!savedSort.ColumnIndex.HasValue || !savedSort.ColumnIndex.Value.Equals(column))
            {
                savedSort.ColumnIndex = column;
                listview.Sorting = SortOrder.Ascending;
            }
            else
            {
                listview.Sorting = listview.Sorting.Equals(SortOrder.Ascending) ? SortOrder.Descending : SortOrder.Ascending;
            }

            listview.ListViewItemSorter = new ListViewComparer(column, listview.Sorting);
        }

        public static TableSettings GetTableSettings(this Settings settings, IEnumerable<Table> tables, string logicalName)
        {
            // check for settings object integrity
            if (settings.TableSettings == null)
            {
                settings.TableSettings = new List<TableSettings>();
            }

            // check for a valid table by logical name
            var table = tables.FirstOrDefault(tbl => tbl.LogicalName.Equals(logicalName));
            if(table == null)
            {
                // exception: not a valid table
                return null;
            }

            var tableSettings = settings.TableSettings.FirstOrDefault(tbs => tbs.LogicalName.Equals(logicalName));
            if (tableSettings == null)
            {
                // new table settings
                tableSettings = new TableSettings { LogicalName = logicalName };
                settings.TableSettings.Add(tableSettings);
            }

            SettingsHelper.SetSettings(settings);
            return tableSettings;
        }

        public static string FormatXml(this string xml)
        {
            using (var ms = new MemoryStream())
            {
                using (var writer = new XmlTextWriter(ms, Encoding.Unicode))
                {
                    var doc = new XmlDocument();
                    doc.LoadXml(xml);

                    writer.Formatting = System.Xml.Formatting.Indented;
                    doc.WriteContentTo(writer);

                    writer.Flush();
                    ms.Flush();

                    ms.Position = 0;

                    using (var sReader = new StreamReader(ms))
                    {
                        return sReader.ReadToEnd();
                    }
                }
            }
        }

        public static string SelectDirectory(this IntPtr owner, string initialDir)
        {
            var dialog = new FolderSelectDialog
            {
                InitialDirectory = !string.IsNullOrEmpty(initialDir) ? initialDir : "C:\\",
                Title = "Select directory..."
            };

            var dirPath = string.Empty;
            if (dialog.Show(owner)) { dirPath = dialog.FileName; }
            return dirPath;
        }

        public static string SelectFile(this IWin32Window owner, string filter)
        {
            var filePath = string.Empty;
            using (var dialog = new OpenFileDialog())
            {
                dialog.Title = "Select file...";
                dialog.Filter = filter;
                dialog.FilterIndex = 2;
                dialog.RestoreDirectory = true;

                if (dialog.ShowDialog(owner).Equals(DialogResult.OK)) { filePath = dialog.FileName; }
            }

            return filePath;
        }

        public static string SelectFile(this IWin32Window owner, string filter, bool save, string defaultFileName = "")
        {
            if (!save) return SelectFile(owner, filter);

            var filePath = string.Empty;
            using (var dialog = new SaveFileDialog())
            {
                dialog.Title = "Save file...";
                dialog.Filter = filter;
                dialog.FilterIndex = 1;
                dialog.RestoreDirectory = true;
                dialog.FileName = defaultFileName;

                if (dialog.ShowDialog(owner).Equals(DialogResult.OK)) { filePath = dialog.FileName; }
            }

            return filePath;
        }
    }
}
