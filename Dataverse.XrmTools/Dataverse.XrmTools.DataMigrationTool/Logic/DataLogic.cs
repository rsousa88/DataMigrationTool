// System
using System;
using System.IO;
using System.Xml;
using System.Linq;
using System.Windows.Forms;
using System.ComponentModel;
using System.Collections.Generic;

// Microsoft
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;

// DataMigrationTool
using Dataverse.XrmTools.DataMigrationTool.Enums;
using Dataverse.XrmTools.DataMigrationTool.Models;
using Dataverse.XrmTools.DataMigrationTool.Helpers;
using Dataverse.XrmTools.DataMigrationTool.AppSettings;
using Dataverse.XrmTools.DataMigrationTool.Repositories;

// 3rd Party
using Newtonsoft.Json.Converters;

namespace Dataverse.XrmTools.DataMigrationTool.Logic
{
    public class DataLogic
    {
        #region Variables
        private BackgroundWorker _worker;

        private readonly IOrganizationService _sourceSvc;
        private readonly IOrganizationService _targetSvc;

        private EntityCollection _sourceCollection;
        private EntityCollection _mappedCollection;
        private EntityCollection _targetCollection;

        private List<ListViewItem> _resultsData = new List<ListViewItem>();
        private Dictionary<Guid, Guid> _successfulIdMap = new Dictionary<Guid, Guid>();
        #endregion Variables

        #region Constructors
        public DataLogic(BackgroundWorker worker, IOrganizationService sourceSvc, IOrganizationService targetSvc)
        {
            _worker = worker;
            _sourceSvc = sourceSvc;
            _targetSvc = targetSvc;
        }
        #endregion Constructors

        #region Public Methods
        public OperationResult Preview(TableData tableData, UiSettings uiSettings, bool targetReady)
        {
            ReportStatus($"Preview: retrieving source {tableData.Table.LogicalName} records...");
            RetrieveSourceData(tableData, uiSettings.BatchSize);
            if (_worker.CancellationPending) return null;

            if(targetReady)
            {
                ReportStatus($"Preview: checking matching target {tableData.Table.LogicalName} records...");
                RetrieveTargetData(tableData.Table.LogicalName, tableData.Table.IdAttribute, uiSettings.BatchSize);
                if (_worker.CancellationPending) return null;
            }

            ReportStatus("Preview: comparing source and target records...");
            ExecuteTargetOperations(uiSettings, tableData.Table, true, targetReady);
            if (_worker.CancellationPending) return null;

            return new OperationResult
            {
                Items = _resultsData
            };
        }

        public IEnumerable<Entity> GetSourceEntities(TableData tableData, UiSettings uiSettings)
        {
            ReportStatus($"Retrieving source {tableData.Table.LogicalName} records...");
            RetrieveSourceData(tableData, uiSettings.BatchSize);
            return _sourceCollection?.Entities ?? Enumerable.Empty<Entity>();
        }

        public bool Export(TableData tableData, UiSettings uiSettings, string filePath, List<Mapping> mappings, bool confirm = true)
        {
            ReportStatus($"Export: retrieving source {tableData.Table.LogicalName} records...");
            RetrieveSourceData(tableData, uiSettings.BatchSize);

            if(uiSettings.ApplyMappingsOn.Equals(Operation.Export))
            {
                ReportStatus("Export: applying mappings...");
                var mappingsLogic = new MappingsLogic(_sourceSvc, _targetSvc);
                _mappedCollection = mappingsLogic.ExecuteMappingsOnExport(_sourceCollection.Entities.ToList(), mappings, tableData.Table);

                ReportStatus($"Export: writing {_mappedCollection.Entities.Count} records to JSON...");
                return SaveJsonFile(_mappedCollection, tableData, filePath, confirm);
            }
            else
            {
                ReportStatus($"Export: writing {_sourceCollection.Entities.Count} records to JSON...");
                return SaveJsonFile(_sourceCollection, tableData, filePath, confirm);
            }
        }

        public OperationResult Import(TableData tableData, RecordCollection collection, UiSettings uiSettings, List<Mapping> mappings, bool confirm = true)
        {
            ReportStatus($"Import: converting {collection.Count} {collection.LogicalName} records...");
            _sourceCollection = collection.ToEntityCollection(tableData.Metadata.Attributes);
            ReportStatus($"Import: checking existing target {tableData.Table.LogicalName} records...");
            RetrieveTargetData(tableData.Table.LogicalName, tableData.Table.IdAttribute, uiSettings.BatchSize);

            var matchKey = string.IsNullOrWhiteSpace(collection.ImportMatchKey)
                ? "record GUID"
                : collection.ImportMatchKey;
            var msg = $"You are about to import {_sourceCollection.Entities.Count} {tableData.Table.LogicalName} records using '{matchKey}' as the matching key. Continue?";
            if (!confirm || MessageBox.Show(msg, "Info", MessageBoxButtons.YesNo, MessageBoxIcon.Question).Equals(DialogResult.Yes))
            {
                ReportStatus("Import: applying selected operations...");
                ExecuteTargetOperations(uiSettings, tableData.Table, false, true, mappings);

                return new OperationResult
                {
                    Items = _resultsData,
                    SuccessfulIdMap = _successfulIdMap
                };
            }

            return null;
        }
        #endregion Public Methods

        #region Private Methods
        private void RetrieveSourceData(TableData tableData, int batchSize)
        {
            // parse column set
            var columns = tableData.SelectedAttributes.Select(a => a.LogicalName).ToList();
            if (!columns.Contains(tableData.Table.IdAttribute)) { columns.Add(tableData.Table.IdAttribute); } // id attribute is required
            if (tableData.Table.NameAttribute != null && !columns.Contains(tableData.Table.NameAttribute)) { columns.Add(tableData.Table.NameAttribute); } // name attribute is required

            // build source fetch xml query
            var fetch = ParseFetchQuery(tableData.Table.LogicalName, columns, tableData.Settings.Filter);

            // retrieve source records
            var sourceRepo = new CrmRepo(_sourceSvc, _worker);
            _sourceCollection = sourceRepo.GetCollectionByFetchXml(fetch, batchSize);
            ReportStatus($"Retrieved {_sourceCollection?.Entities.Count ?? 0} source {tableData.Table.LogicalName} records.");
        }

        private void RetrieveTargetData(string logicalName, string idAttribute, int batchSize)
        {
            // parse target query
            var targetIds = _sourceCollection.Entities.Select(rec => rec.Id);
            var filter = new FilterExpression(LogicalOperator.And)
            {
                Conditions =
                    {
                        new ConditionExpression(idAttribute, ConditionOperator.In, targetIds.ToArray())
                    }
            };

            var query = new QueryExpression(logicalName)
            {
                ColumnSet = new ColumnSet(idAttribute),
                Criteria = filter
            };

            // retrieve target records
            var targetRepo = new CrmRepo(_targetSvc, _worker);
            _targetCollection = targetRepo.GetCollectionByExpression(query, batchSize);
            ReportStatus($"Found {_targetCollection?.Entities.Count ?? 0} matching target {logicalName} records.");
        }

        private void ExecuteTargetOperations(UiSettings uiSettings, Table table, bool isPreview, bool targetReady, List<Mapping> mappings = null)
        {
            if (_sourceCollection == null || (targetReady && _targetCollection == null)) { return; }

            var migrationItems = GetDiffRecords(uiSettings.Action, targetReady).ToList();
            ReportStatus($"Calculated {migrationItems.Count} record action(s).");

            // preview
            if (isPreview)
            {
                var prvItems = migrationItems.Select(mig => mig.Record
                    .ToListViewItem(new Tuple<string, object>("table", new Dictionary<string, string>()
                    {
                        { "attributename", table.NameAttribute != null ? table.NameAttribute : string.Empty },
                        { "action", mig.Action.ToString() },
                        { "description", Enums.Action.Preview.ToString() }
                    })));

                _resultsData.AddRange(prvItems);
                return;
            }

            if (_worker.CancellationPending) return;

            // apply mappings
            var mappingsLogic = new MappingsLogic(_sourceSvc, _targetSvc);
            var items = uiSettings.ApplyMappingsOn.Equals(Operation.Import) ? mappingsLogic.ExecuteMappingsOnImport(migrationItems, mappings, table) : migrationItems;
            var itemList = items.ToList();

            // execute
            var diffCount = itemList.Count;
            var failed = 0;
            ReportStatus(BuildImportProgressMessage(table.LogicalName, 0, diffCount, failed, $"Preparing batches of {uiSettings.BatchSize} record(s)."), 0);

            var done = 0;
            var lvItems = new List<ListViewItem>();
            var maxBatch = (int)Math.Ceiling((decimal)(diffCount) / uiSettings.BatchSize);
            for (int i = 0; i < maxBatch; i++)
            {
                if (_worker.CancellationPending) return;

                var batchNum = i + 1;

                var batchRows = itemList.Skip(done).Take(uiSettings.BatchSize).ToList();
                var batchStart = done + 1;
                var batchEnd = done + batchRows.Count;
                ReportStatus(
                    BuildImportProgressMessage(table.LogicalName, done, diffCount, failed, $"Processing records {batchStart}-{batchEnd}..."),
                    GetProgressPercentage(done, diffCount));

                var responses = ExecuteOperation(uiSettings.Action, batchRows, done, diffCount, table.LogicalName, failed).ToList();
                failed += responses.Count(res => !res.Success);
                foreach (var response in responses.Where(res => res.Success && res.Id != Guid.Empty))
                    _successfulIdMap[response.Id] = response.ResponseId != Guid.Empty ? response.ResponseId : response.Id;

                var join = batchRows.Join(responses, mig => mig.Record.Id, res => res.Id, (mig, res) => new
                {
                    Action = mig.Action,
                    Entity = mig.Record,
                    Description = res.Success ? "Ok" : $"ERROR: {res.Message}"
                });

                lvItems.AddRange(join.Select(anon => anon.Entity
                    .ToListViewItem(new Tuple<string, object>("table", new Dictionary<string, string>()
                    {
                        { "attributename", table.NameAttribute != null ? table.NameAttribute : string.Empty },
                        { "action", anon.Action.ToString() },
                        { "description", anon.Description }
                    }))));

                // increment done counter
                done += batchRows.Count();
                ReportStatus(BuildImportProgressMessage(table.LogicalName, done, diffCount, failed), GetProgressPercentage(done, diffCount));
            }

            // set results
            _resultsData.AddRange(lvItems);
            ReportStatus(BuildImportProgressMessage(table.LogicalName, done, diffCount, failed, $"Completed {lvItems.Count} record operation result(s)."), 100);
        }

        private void ReportStatus(string message, int progress = 0)
        {
            _worker?.ReportProgress(progress, message);
        }

        private int GetProgressPercentage(int processed, int total)
        {
            if (total <= 0) return 0;
            return Math.Max(0, Math.Min(100, (int)Math.Round((processed * 100m) / total)));
        }

        private string BuildImportProgressMessage(string logicalName, int processed, int total, int failed, string detail = null)
        {
            var percent = GetProgressPercentage(processed, total);
            var message = $"Importing {processed}/{total} {logicalName} records...\r\n{percent}% complete | Errors: {failed}";
            return string.IsNullOrWhiteSpace(detail) ? message : $"{message}\r\n{detail}";
        }

        private IEnumerable<MigrationItem> GetDiffRecords(Enums.Action action, bool targetReady)
        {
            var description = (action & Enums.Action.Preview) == Enums.Action.Preview ? Enums.Action.Preview.ToString() : string.Empty;

            var diffs = new List<MigrationItem>();
            if(!targetReady)
            {
                var previewIds = _sourceCollection.Entities.Select(ent => ent.Id);
                var previewRecords = previewIds.Select(id => new MigrationItem(Enums.Action.Preview, _sourceCollection.Entities.FirstOrDefault(ent => ent.Id.Equals(id)), description));

                diffs.AddRange(previewRecords);
            }
            else
            {
                if ((action & Enums.Action.Create) == Enums.Action.Create)
                {
                    var createIds = _sourceCollection.Entities.Select(ent => ent.Id).Except(_targetCollection.Entities.Select(ent => ent.Id));
                    var createRecords = createIds.Select(id => new MigrationItem(Enums.Action.Create, _sourceCollection.Entities.FirstOrDefault(ent => ent.Id.Equals(id)), description));

                    diffs.AddRange(createRecords);
                }
                if ((action & Enums.Action.Update) == Enums.Action.Update)
                {
                    var updateIds = _sourceCollection.Entities.Select(ent => ent.Id).Intersect(_targetCollection.Entities.Select(ent => ent.Id));
                    var updateRecords = updateIds.Select(id => new MigrationItem(Enums.Action.Update, _sourceCollection.Entities.FirstOrDefault(ent => ent.Id.Equals(id)), description));

                    diffs.AddRange(updateRecords);
                }
                if ((action & Enums.Action.Delete) == Enums.Action.Delete)
                {
                    var deleteIds = _targetCollection.Entities.Select(ent => ent.Id).Except(_sourceCollection.Entities.Select(ent => ent.Id));
                    var deleteRecords = deleteIds.Select(id => new MigrationItem(Enums.Action.Delete, _targetCollection.Entities.FirstOrDefault(ent => ent.Id.Equals(id)), description));

                    diffs.AddRange(deleteRecords);
                }
            }

            return diffs;
        }

        private IEnumerable<CrmBulkResponse> ExecuteOperation(Enums.Action mode, IEnumerable<MigrationItem> migrationItems, int processedBeforeBatch, int total, string logicalName, int failed)
        {
            var repo = new CrmRepo(_targetSvc, null);

            var responses = new List<CrmBulkResponse>();
            var processedInBatch = 0;
            if ((mode & Enums.Action.Create) == Enums.Action.Create)
            {
                var records = migrationItems.Where(mig => (mig.Action & Enums.Action.Create) == Enums.Action.Create).Select(mig => mig.Record).ToList();
                if (records.Any())
                    ReportStatus(BuildImportProgressMessage(logicalName, processedBeforeBatch + processedInBatch, total, failed, $"Dataverse is processing {records.Count} create request(s)."), GetProgressPercentage(processedBeforeBatch + processedInBatch, total));
                responses.AddRange(repo.CreateRecords(records));
                processedInBatch += records.Count;
            }
            if ((mode & Enums.Action.Update) == Enums.Action.Update)
            {
                var records = migrationItems.Where(mig => (mig.Action & Enums.Action.Update) == Enums.Action.Update).Select(mig => mig.Record).ToList();
                if (records.Any())
                    ReportStatus(BuildImportProgressMessage(logicalName, processedBeforeBatch + processedInBatch, total, failed, $"Dataverse is processing {records.Count} update request(s)."), GetProgressPercentage(processedBeforeBatch + processedInBatch, total));
                responses.AddRange(repo.UpdateRecords(records));
                processedInBatch += records.Count;
            }
            if ((mode & Enums.Action.Delete) == Enums.Action.Delete)
            {
                var records = migrationItems.Where(mig => (mig.Action & Enums.Action.Delete) == Enums.Action.Delete).Select(mig => mig.Record).ToList();
                if (records.Any())
                    ReportStatus(BuildImportProgressMessage(logicalName, processedBeforeBatch + processedInBatch, total, failed, $"Dataverse is processing {records.Count} delete request(s)."), GetProgressPercentage(processedBeforeBatch + processedInBatch, total));
                responses.AddRange(repo.DeleteRecords(records));
                processedInBatch += records.Count;
            }

            return responses;
        }

        private bool SaveJsonFile(EntityCollection entityCollection, TableData tableData, string path, bool confirm = true)
        {
            var success = false;

            // export -> serialize source records to json and save file
            entityCollection.EntityName = tableData.Table.LogicalName;
            var msg = $"You are about to export {entityCollection.Entities.Count} {tableData.Table.LogicalName} records. Continue?";
            var result = confirm ? MessageBox.Show(msg, "Info", MessageBoxButtons.YesNo, MessageBoxIcon.Question) : DialogResult.Yes;
            if (result.Equals(DialogResult.Yes))
            {
                var collection = new RecordCollection(entityCollection, tableData.Metadata);
                var json = collection.SerializeObject(new IsoDateTimeConverter());
                File.WriteAllText(path, json);

                success = true;
            }

            return success;
        }

        private string ParseFetchQuery(string logicalName, List<string> columns, string filter)
        {
            var doc = new XmlDocument();

            // fetch node (root)
            var root = doc.CreateElement("fetch");

            // fetch node attributes
            var rootMappingAttr = doc.CreateAttribute("mapping");
            rootMappingAttr.Value = "logical";
            root.Attributes.Append(rootMappingAttr);

            var rootDistinctAttr = doc.CreateAttribute("distinct");
            rootDistinctAttr.Value = "false";
            root.Attributes.Append(rootDistinctAttr);

            // append to doc
            doc.AppendChild(root);

            // entity node
            var entity = doc.CreateElement("entity");

            // entity node attributes
            var entityNameAttr = doc.CreateAttribute("name");
            entityNameAttr.Value = logicalName;
            entity.Attributes.Append(entityNameAttr);

            // append to root
            root.AppendChild(entity);

            // entity node column attributes
            foreach (var col in columns)
            {
                // attribute node
                var attr = doc.CreateElement("attribute");

                // attribute node attributes
                var attributeNameAttr = doc.CreateAttribute("name");
                attributeNameAttr.Value = col;
                attr.Attributes.Append(attributeNameAttr);

                // append to entity
                entity.AppendChild(attr);
            }

            // filter
            if (filter != null)
            {
                filter = filter.Trim();

                var fragment = doc.CreateDocumentFragment();
                fragment.InnerXml = filter;

                entity.AppendChild(fragment);
            }

            ValidateConditionEntityAliases(doc);

            return doc.InnerXml;
        }

        private void ValidateConditionEntityAliases(XmlDocument doc)
        {
            var knownEntityNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var entity = doc.SelectSingleNode("/fetch/entity") as XmlElement;
            var entityName = entity?.GetAttribute("name");
            if (!string.IsNullOrWhiteSpace(entityName)) { knownEntityNames.Add(entityName); }

            foreach (XmlElement linkEntity in doc.GetElementsByTagName("link-entity"))
            {
                var alias = linkEntity.GetAttribute("alias");
                if (!string.IsNullOrWhiteSpace(alias)) { knownEntityNames.Add(alias); }

                var linkEntityName = linkEntity.GetAttribute("name");
                if (!string.IsNullOrWhiteSpace(linkEntityName)) { knownEntityNames.Add(linkEntityName); }
            }

            var missingAliases = doc.GetElementsByTagName("condition")
                .OfType<XmlElement>()
                .Select(condition => condition.GetAttribute("entityname"))
                .Where(entityNameValue => !string.IsNullOrWhiteSpace(entityNameValue) && !knownEntityNames.Contains(entityNameValue))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            if (missingAliases.Any())
            {
                var aliases = string.Join(", ", missingAliases.Select(alias => $"'{alias}'"));
                throw new Exception($"The filter references link-entity alias {aliases}, but no matching <link-entity alias=\"...\"> node was found. Include the related <link-entity> in the filter, or remove the entityname attribute if the condition belongs to the selected table.");
            }
        }
        #endregion Private Methods
    }
}
