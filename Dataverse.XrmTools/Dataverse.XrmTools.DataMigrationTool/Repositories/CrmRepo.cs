// System
using System;
using System.Linq;
using System.ComponentModel;
using System.Collections.Generic;

// Microsoft
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
using Microsoft.Xrm.Sdk.Metadata;
using Microsoft.Xrm.Sdk.Messages;
using Microsoft.Crm.Sdk.Messages;

// DataMigrationTool
using Dataverse.XrmTools.DataMigrationTool.Models;
using Dataverse.XrmTools.DataMigrationTool.RepoInterfaces;

namespace Dataverse.XrmTools.DataMigrationTool.Repositories
{
    public class CrmRepo : ICrmRepo
    {
        #region Private Fields
        private readonly IOrganizationService _service;
        private readonly BackgroundWorker _worker;
        #endregion Private Fields

        #region Constructors
        /// <summary>
        /// Creates an instance of the CRM Repository using the specified CRM service.
        /// </summary>
        /// <param name="crmContext">Instantiated crmContext object</param>
        public CrmRepo(IOrganizationService service, BackgroundWorker worker = null)
        {
            _service = service;
            _worker = worker;
        }
        #endregion Constructors

        #region Interface Methods
        public IEnumerable<EntityMetadata> GetOrgTables()
        {
            try
            {
                var request = new RetrieveAllEntitiesRequest
                {
                    RetrieveAsIfPublished = true,
                    EntityFilters = EntityFilters.Entity
                };

                var response = _service.Execute(request) as RetrieveAllEntitiesResponse;
                return response.EntityMetadata.AsEnumerable();
            }
            catch (Exception)
            {
                throw;
            }
        }

        public EntityMetadata GetTableMetadata(string logicalName)
        {
            try
            {
                var request = new RetrieveEntityRequest
                {
                    LogicalName = logicalName,
                    EntityFilters = EntityFilters.Attributes
                };

                var response = _service.Execute(request) as RetrieveEntityResponse;
                return response.EntityMetadata;
            }
            catch (Exception)
            {
                throw;
            }
        }

        public IEnumerable<Entity> GetRecords(QueryExpression query, int batchSize = 250)
        {
            // page info settings
            query.PageInfo.PageNumber = 1;
            query.PageInfo.Count = batchSize;

            RetrieveMultipleResponse response;

            var records = new List<Entity>();
            do
            {
                if (_worker != null && _worker.CancellationPending) return null;

                response = _service.Execute(new RetrieveMultipleRequest() { Query = query }) as RetrieveMultipleResponse;
                if (response == null || response.EntityCollection == null) { break; }

                records.AddRange(response.EntityCollection.Entities);
                _worker?.ReportProgress(0, $"Retrieved {records.Count} record(s)...");
                query.PageInfo.PageNumber++;
                query.PageInfo.PagingCookie = response.EntityCollection.PagingCookie;
            }
            while (response.EntityCollection.MoreRecords);

            return records.AsEnumerable();
        }

        public IEnumerable<Entity> GetUsersInDomainList(string[] domainList, string[] columns)
        {
            var filter = new FilterExpression(LogicalOperator.And)
            {
                Conditions =
                    {
                        new ConditionExpression("domainname", ConditionOperator.In, domainList)
                    }
            };

            var query = new QueryExpression("systemuser")
            {
                ColumnSet = new ColumnSet((from c in columns select c.ToLower()).ToArray()),
                Criteria = filter,
                PageInfo = new PagingInfo() { Count = 5000, PageNumber = 1 }
            };

            return GetRecords(query);
        }

        public IEnumerable<Entity> GetTeamsInNameList(string[] nameList, string[] columns)
        {
            var filter = new FilterExpression(LogicalOperator.And)
            {
                Conditions =
                    {
                        new ConditionExpression("name", ConditionOperator.In, nameList)
                    }
            };

            var query = new QueryExpression("team")
            {
                ColumnSet = new ColumnSet((from c in columns select c.ToLower()).ToArray()),
                Criteria = filter,
                PageInfo = new PagingInfo() { Count = 5000, PageNumber = 1 }
            };

            return GetRecords(query);
        }

        public EntityCollection GetCollectionByExpression(QueryExpression query, int batchSize = 250)
        {
            var records = GetRecords(query, batchSize).ToList();
            return new EntityCollection(records);
        }

        public EntityCollection GetCollectionByFetchXml(string fetchXml, int batchSize = 250)
        {
            var query = ConvertFetchXml(fetchXml);

            return GetCollectionByExpression(query, batchSize);
        }

        public IEnumerable<CrmBulkResponse> CreateRecords(IEnumerable<Entity> records)
        {
            try
            {
                var requests = records.Select(rec => new CreateRequest
                {
                    RequestId = rec.Id,
                    Target = rec
                });

                return ExecuteMultiple(requests);
            }
            catch
            {
                throw;
            }
        }

        public IEnumerable<CrmBulkResponse> UpdateRecords(IEnumerable<Entity> records)
        {
            try
            {
                var requests = records.Select(rec => new UpdateRequest
                {
                    RequestId = rec.Id,
                    Target = rec
                });

                return ExecuteMultiple(requests);
            }
            catch (Exception)
            {
                throw;
            }
        }

        public IEnumerable<CrmBulkResponse> DeleteRecords(IEnumerable<Entity> records)
        {
            try
            {
                var requests = records.Select(rec => new DeleteRequest
                {
                    RequestId = rec.Id,
                    Target = new EntityReference(rec.LogicalName, rec.Id)
                });

                return ExecuteMultiple(requests);
            }
            catch (Exception)
            {
                throw;
            }
        }
        public IEnumerable<EntityKeyMetadata> GetAlternateKeys(string logicalName)
        {
            var request = new RetrieveEntityRequest
            {
                LogicalName = logicalName,
                EntityFilters = EntityFilters.All
            };

            var response = _service.Execute(request) as RetrieveEntityResponse;
            return response.EntityMetadata.Keys ?? Enumerable.Empty<EntityKeyMetadata>();
        }

        public Guid? ResolveByAlternateKey(string logicalName, Dictionary<string, object> keyValues)
        {
            ThrowIfCancelled();
            var filter = new FilterExpression(LogicalOperator.And);
            foreach (var kv in keyValues)
            {
                filter.Conditions.Add(kv.Value == null
                    ? new ConditionExpression(kv.Key, ConditionOperator.Null)
                    : new ConditionExpression(kv.Key, ConditionOperator.Equal, kv.Value));
            }

            var query = new QueryExpression(logicalName)
            {
                ColumnSet = new ColumnSet(false),
                Criteria = filter,
                TopCount = 1
            };

            ThrowIfCancelled();
            var response = _service.Execute(new RetrieveMultipleRequest { Query = query }) as RetrieveMultipleResponse;
            ThrowIfCancelled();
            return response?.EntityCollection?.Entities?.FirstOrDefault()?.Id;
        }

        public Entity FindByFieldValue(string logicalName, string field, object value)
        {
            return FindByFieldValues(logicalName, new Dictionary<string, object> { { field, value } });
        }

        public Entity FindByFieldValues(string logicalName, Dictionary<string, object> fieldValues)
        {
            ThrowIfCancelled();
            var filter = new FilterExpression(LogicalOperator.And);
            foreach (var kv in fieldValues)
            {
                filter.Conditions.Add(kv.Value == null
                    ? new ConditionExpression(kv.Key, ConditionOperator.Null)
                    : new ConditionExpression(kv.Key, ConditionOperator.Equal, kv.Value));
            }

            var query = new QueryExpression(logicalName)
            {
                ColumnSet = new ColumnSet(false),
                Criteria = filter,
                TopCount = 2
            };

            ThrowIfCancelled();
            var response = _service.Execute(new RetrieveMultipleRequest { Query = query }) as RetrieveMultipleResponse;
            ThrowIfCancelled();
            var results = response?.EntityCollection?.Entities;
            if (results == null || results.Count == 0) return null;
            if (results.Count > 1) throw new Exception($"Multiple records found for {FormatFieldValues(fieldValues)}");
            return results[0];
        }

        private string FormatFieldValues(Dictionary<string, object> fieldValues)
        {
            return string.Join(", ", fieldValues.Select(kv => $"{kv.Key} = '{kv.Value}'"));
        }

        private void ThrowIfCancelled()
        {
            if (_worker != null && _worker.CancellationPending)
                throw new OperationCanceledException();
        }
        #endregion Interface Methods

        #region Private Methods
        private QueryExpression ConvertFetchXml(string fetchXml)
        {
            try
            {
                var request = new FetchXmlToQueryExpressionRequest
                {
                    FetchXml = fetchXml
                };

                var response = _service.Execute(request) as FetchXmlToQueryExpressionResponse;
                return response.Query;
            }
            catch (Exception)
            {
                throw;
            }
        }

        private IEnumerable<CrmBulkResponse> ExecuteMultiple(IEnumerable<OrganizationRequest> requests)
        {
            try
            {
                var requestList = requests.ToList();
                if (!requestList.Any()) return Enumerable.Empty<CrmBulkResponse>();

                var requestName = requestList.Select(req => req.RequestName).Distinct().Count() == 1
                    ? requestList.First().RequestName
                    : "mixed";
                _worker?.ReportProgress(0, $"Dataverse is processing {requestList.Count} {requestName} request(s)...");

                var multipleReq = new ExecuteMultipleRequest
                {
                    Requests = new OrganizationRequestCollection(),
                    Settings = new ExecuteMultipleSettings { ContinueOnError = true, ReturnResponses = true }
                };

                multipleReq.Requests.AddRange(requestList);

                var response = _service.Execute(multipleReq) as ExecuteMultipleResponse;
                _worker?.ReportProgress(0, $"Dataverse returned {response?.Responses?.Count ?? 0} response(s) for this batch.");

                return response.Responses
                    .Select(resp => new CrmBulkResponse
                    {
                        Id = multipleReq.Requests[resp.RequestIndex].RequestId.GetValueOrDefault(Guid.Empty),
                        ResponseId = GetResponseId(resp),
                        Success = resp.Fault == null,
                        Message = resp.Fault != null ? resp.Fault.Message : string.Empty
                    });
            }
            catch
            {
                throw;
            }
        }

        private Guid GetResponseId(ExecuteMultipleResponseItem response)
        {
            if (response?.Response is CreateResponse createResponse)
                return createResponse.id;

            return Guid.Empty;
        }
        #endregion Private Methods
    }
}
