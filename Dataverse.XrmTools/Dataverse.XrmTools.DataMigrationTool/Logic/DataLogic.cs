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
using Dataverse.XrmTools.DataMigrationTool.Handlers;
using Dataverse.XrmTools.DataMigrationTool.Repositories;

namespace Dataverse.XrmTools.DataMigrationTool.Logic
{
    public class DataLogic
    {
        #region Global Variables
        private BackgroundWorker _worker;

        private readonly IOrganizationService _sourceSvc;
        private readonly IOrganizationService _targetSvc;

        private EntityCollection _sourceCollection;
        private EntityCollection _targetCollection;
        private RecordCollection _recordCollection;

        private List<ListViewItem> _resultsData = new List<ListViewItem>();
        #endregion Global Variables

        #region Constructors
        public DataLogic(BackgroundWorker worker, IOrganizationService sourceSvc, IOrganizationService targetSvc)
        {
            _worker = worker;
            _sourceSvc = sourceSvc;
            _targetSvc = targetSvc;
        }
        #endregion Constructors

        #region Handlers
        public event EventHandler OnStatus;
        private void SetStatus(string message)
        {
            if (OnStatus == null) { return; }
            OnStatus(this, new StatusHandler(message));
        }

        public event EventHandler OnProgress;
        private void SetProgress(int progress, string message)
        {
            if (OnProgress == null) { return; }
            OnProgress(this, new ProgressHandler(progress, message));
        }
        #endregion Handlers

        #region Public Methods
        public OperationResult Preview(TableData tableData, UiSettings uiSettings)
        {
            RetrieveSourceData(tableData, uiSettings.BatchSize);
            if (_worker.CancellationPending) return null;

            RetrieveTargetData(tableData.Table.LogicalName, tableData.Table.IdAttribute, uiSettings.BatchSize);
            if (_worker.CancellationPending) return null;

            PerformDataOperations(uiSettings, tableData.Table, true);
            if (_worker.CancellationPending) return null;

            return new OperationResult
            {
                Items = _resultsData
            };
        }

        public void Export(TableData tableData, UiSettings uiSettings, string path)
        {
            RetrieveSourceData(tableData, uiSettings.BatchSize);
            ExecuteJson(tableData, path, ImportExportAction.Export);

            MessageBox.Show("Records successfully exported", "Info", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }

        public OperationResult Import(TableData tableData, string path, UiSettings uiSettings)
        {
            ExecuteJson(null, path, ImportExportAction.Import);
            ImportFileDataChecks(tableData);

            RetrieveTargetData(tableData.Table.LogicalName, tableData.Table.IdAttribute, uiSettings.BatchSize);

            var msg = $"You are about to import {_sourceCollection.Entities.Count} {tableData.Table.DisplayName} records. Continue?";
            var result = MessageBox.Show(msg, "Info", MessageBoxButtons.YesNo, MessageBoxIcon.Question);
            if (result.Equals(DialogResult.Yes))
            {
                PerformDataOperations(uiSettings, tableData.Table, false);

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
            SetProgress(0, "Retrieving source records...");
            _sourceCollection = sourceRepo.GetCollectionByFetchXml(fetch, batchSize);

            if (_sourceCollection == null) { SetProgress(100, $"Operation aborted"); return; }
            SetProgress(100, $"Retrieved {_sourceCollection.Entities.Count} records from source");
        }

        private void RetrieveTargetData(string logicalName, string idAttribute, int batchSize)
        {
            // parse target query
            var query = new QueryExpression(logicalName) { ColumnSet = new ColumnSet(idAttribute) };

            // retrieve target records
            var targetRepo = new CrmRepo(_targetSvc, _worker);
            SetProgress(0, "Retrieving target records...");
            _targetCollection = targetRepo.GetCollectionByExpression(query, batchSize);

            if(_targetCollection == null) { SetProgress(100, $"Operation aborted"); return; }
            SetProgress(100, $"Retrieved {_targetCollection.Entities.Count} records from target");
        }

        private void PerformDataOperations(UiSettings uiSettings, Table table, bool isPreview)
        {
            if (_sourceCollection == null || _targetCollection == null) { return; }

            var migrationItems = GetDiffRecords(uiSettings.Action);

            // preview
            if (isPreview)
            {
                var prvItems = migrationItems.Select(mig => mig.Entity
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

            // execute
            var diffCount = migrationItems.Count();
            SetStatus($"Performing {uiSettings.Action} operations for {diffCount} records...");

            var done = 0;
            var items = new List<ListViewItem>();
            var maxBatch = (int)Math.Ceiling((decimal)(diffCount) / uiSettings.BatchSize);
            for (int i = 0; i < maxBatch; i++)
            {
                if (_worker.CancellationPending) return;

                var batchNum = i + 1;
                SetProgress(batchNum / maxBatch, $"Executing batch {batchNum}/{maxBatch}");

                var batchRows = migrationItems.Skip(done).Take(uiSettings.BatchSize);

                var errors = ExecuteOperation(uiSettings.Action, batchRows);

                var join = migrationItems.Join(errors, mig => mig.Entity.Id, err => err.Id, (mig, err) => new
                {
                    Action = mig.Action,
                    Entity = mig.Entity,
                    Description = $"ERROR: {err.Message}"
                });

                items.AddRange(join.Select(anon => anon.Entity
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
            _resultsData.AddRange(items);
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
                var records = migrationItems.Where(mig => (mig.Action & Enums.Action.Create) == Enums.Action.Create).Select(mig => mig.Entity);
                responses.AddRange(repo.CreateRecords(records));
            }
            if ((mode & Enums.Action.Update) == Enums.Action.Update)
            {
                var records = migrationItems.Where(mig => (mig.Action & Enums.Action.Update) == Enums.Action.Update).Select(mig => mig.Entity);
                responses.AddRange(repo.UpdateRecords(records));
            }
            if ((mode & Enums.Action.Delete) == Enums.Action.Delete)
            {
                var records = migrationItems.Where(mig => (mig.Action & Enums.Action.Delete) == Enums.Action.Delete).Select(mig => mig.Entity);
                responses.AddRange(repo.DeleteRecords(records));
            }

            return responses;
        }

        private void ExecuteJson(TableData tableData, string path, ImportExportAction operation)
        {
            // export -> serialize source records to json and save file
            if (operation.Equals(ImportExportAction.Export))
            {
                _sourceCollection.EntityName = tableData.Table.LogicalName;
                var msg = $"You are about to export {_sourceCollection.Entities.Count} {tableData.Table.DisplayName} records. Continue?";
                var result = MessageBox.Show(msg, "Info", MessageBoxButtons.YesNo, MessageBoxIcon.Question);
                if (result.Equals(DialogResult.Yes))
                {
                    var collection = new RecordCollection(_sourceCollection, tableData.Metadata);
                    var json = collection.SerializeObject<RecordCollection>();
                    File.WriteAllText($"{path}/{tableData.Table.LogicalName}.json", json);

                    SetStatus($"File saved");
                }
            }

            // import -> deserialize json and set source records
            if (operation.Equals(ImportExportAction.Import))
            {
                var json = File.ReadAllText(path);

                _recordCollection = json.DeserializeObject<RecordCollection>();
                _sourceCollection = _recordCollection.ToEntityCollection();
            }
        }

        private void ImportFileDataChecks(TableData tableData)
        {
            if (_recordCollection == null)
            {
                throw new Exception($"Invalid import file: Invalid structure");
            }
            if (!tableData.Table.LogicalName.Equals(_recordCollection.LogicalName))
            {
                throw new Exception($"Invalid import file: Unexpected table '{_recordCollection.LogicalName}'");
            }
            if (!tableData.Table.IdAttribute.Equals(_recordCollection.PrimaryIdAttribute))
            {
                throw new Exception($"Invalid import file: Unexpected primary ID attribute '{_recordCollection.PrimaryIdAttribute}'");
            }
            if (_recordCollection.Count == 0 || !_recordCollection.Records.Any())
            {
                throw new Exception($"Invalid import file: No records");
            }
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
