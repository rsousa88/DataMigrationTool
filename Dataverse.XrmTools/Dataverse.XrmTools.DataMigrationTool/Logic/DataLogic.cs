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
using Dataverse.XrmTools.DataMigrationTool.Models;
using Dataverse.XrmTools.DataMigrationTool.Helpers;
using Dataverse.XrmTools.DataMigrationTool.AppSettings;
using Dataverse.XrmTools.DataMigrationTool.Repositories;
using Dataverse.XrmTools.DataMigrationTool.Enums;

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
        public OperationResult Preview(TableData tableData, UiSettings uiSettings)
        {
            RetrieveSourceData(tableData, uiSettings.BatchSize);
            if (_worker.CancellationPending) return null;

            RetrieveTargetData(tableData.Table.LogicalName, tableData.Table.IdAttribute, uiSettings.BatchSize);
            if (_worker.CancellationPending) return null;

            ExecuteTargetOperations(uiSettings, tableData.Table, true);
            if (_worker.CancellationPending) return null;

            return new OperationResult
            {
                Items = _resultsData
            };
        }

        public bool Export(TableData tableData, UiSettings uiSettings, string filePath, List<Mapping> mappings)
        {
            RetrieveSourceData(tableData, uiSettings.BatchSize);

            if(uiSettings.ApplyMappingsOn.Equals(Operation.Export))
            {
                var mappingsLogic = new MappingsLogic(_sourceSvc, _targetSvc);
                _mappedCollection = mappingsLogic.ExecuteMappingsOnExport(_sourceCollection.Entities.ToList(), mappings, tableData.Table);
            }
            
            return SaveJsonFile(tableData, filePath);
        }

        public OperationResult Import(TableData tableData, RecordCollection collection, UiSettings uiSettings, List<Mapping> mappings)
        {
            _sourceCollection = collection.ToEntityCollection();
            RetrieveTargetData(tableData.Table.LogicalName, tableData.Table.IdAttribute, uiSettings.BatchSize);

            var msg = $"You are about to import {_sourceCollection.Entities.Count} {tableData.Table.DisplayName} records. Continue?";
            var result = MessageBox.Show(msg, "Info", MessageBoxButtons.YesNo, MessageBoxIcon.Question);
            if (result.Equals(DialogResult.Yes))
            {
                ExecuteTargetOperations(uiSettings, tableData.Table, false, mappings);

                return new OperationResult
                {
                    Items = _resultsData
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
            if (!columns.Contains(tableData.Table.NameAttribute)) { columns.Add(tableData.Table.NameAttribute); } // name attribute is required

            // build source fetch xml query
            var fetch = ParseFetchQuery(tableData.Table.LogicalName, columns, tableData.Settings.Filter);

            // retrieve source records
            var sourceRepo = new CrmRepo(_sourceSvc, _worker);
            _sourceCollection = sourceRepo.GetCollectionByFetchXml(fetch, batchSize);
        }

        private void RetrieveTargetData(string logicalName, string idAttribute, int batchSize)
        {
            // parse target query
            var query = new QueryExpression(logicalName) { ColumnSet = new ColumnSet(idAttribute) };

            // retrieve target records
            var targetRepo = new CrmRepo(_targetSvc, _worker);
            _targetCollection = targetRepo.GetCollectionByExpression(query, batchSize);
        }

        private void ExecuteTargetOperations(UiSettings uiSettings, Table table, bool isPreview, List<Mapping> mappings = null)
        {
            if (_sourceCollection == null || _targetCollection == null) { return; }

            var migrationItems = GetDiffRecords(uiSettings.Action);

            // preview
            if (isPreview)
            {
                var prvItems = migrationItems.Select(mig => mig.Record
                    .ToListViewItem(new Tuple<string, object>("table", new Dictionary<string, string>()
                    {
                        { "attributename", table.NameAttribute },
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

            // execute
            var diffCount = items.Count();

            var done = 0;
            var lvItems = new List<ListViewItem>();
            var maxBatch = (int)Math.Ceiling((decimal)(diffCount) / uiSettings.BatchSize);
            for (int i = 0; i < maxBatch; i++)
            {
                if (_worker.CancellationPending) return;

                var batchNum = i + 1;

                var batchRows = items.Skip(done).Take(uiSettings.BatchSize);

                var responses = ExecuteOperation(uiSettings.Action, batchRows);

                var join = items.Join(responses, mig => mig.Record.Id, res => res.Id, (mig, res) => new
                {
                    Action = mig.Action,
                    Entity = mig.Record,
                    Description = res.Success ? "Ok" : $"ERROR: {res.Message}"
                });

                lvItems.AddRange(join.Select(anon => anon.Entity
                    .ToListViewItem(new Tuple<string, object>("table", new Dictionary<string, string>()
                    {
                        { "attributename", table.NameAttribute },
                        { "action", anon.Action.ToString() },
                        { "description", anon.Description }
                    }))));

                // increment done counter
                done += batchRows.Count();
            }

            // set results
            _resultsData.AddRange(lvItems);
        }

        private IEnumerable<MigrationItem> GetDiffRecords(Enums.Action action)
        {
            var description = (action & Enums.Action.Preview) == Enums.Action.Preview ? Enums.Action.Preview.ToString() : string.Empty;

            var diffs = new List<MigrationItem>();
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

            return diffs;
        }

        private IEnumerable<CrmBulkResponse> ExecuteOperation(Enums.Action mode, IEnumerable<MigrationItem> migrationItems)
        {
            var repo = new CrmRepo(_targetSvc, _worker);

            var responses = new List<CrmBulkResponse>();
            if ((mode & Enums.Action.Create) == Enums.Action.Create)
            {
                var records = migrationItems.Where(mig => (mig.Action & Enums.Action.Create) == Enums.Action.Create).Select(mig => mig.Record);
                responses.AddRange(repo.CreateRecords(records));
            }
            if ((mode & Enums.Action.Update) == Enums.Action.Update)
            {
                var records = migrationItems.Where(mig => (mig.Action & Enums.Action.Update) == Enums.Action.Update).Select(mig => mig.Record);
                responses.AddRange(repo.UpdateRecords(records));
            }
            if ((mode & Enums.Action.Delete) == Enums.Action.Delete)
            {
                var records = migrationItems.Where(mig => (mig.Action & Enums.Action.Delete) == Enums.Action.Delete).Select(mig => mig.Record);
                responses.AddRange(repo.DeleteRecords(records));
            }

            return responses;
        }

        private bool SaveJsonFile(TableData tableData, string path)
        {
            var success = false;

            // export -> serialize source records to json and save file
            _mappedCollection.EntityName = tableData.Table.LogicalName;
            var msg = $"You are about to export {_mappedCollection.Entities.Count} {tableData.Table.DisplayName} records. Continue?";
            var result = MessageBox.Show(msg, "Info", MessageBoxButtons.YesNo, MessageBoxIcon.Question);
            if (result.Equals(DialogResult.Yes))
            {
                var collection = new RecordCollection(_mappedCollection, tableData.Metadata);
                var json = collection.SerializeObject<RecordCollection>();
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

            return doc.InnerXml;
        }
        #endregion Private Methods
    }
}
