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
                return response.EntityMetadata.Where(meta => meta.DisplayName.UserLocalizedLabel != null).AsEnumerable();
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
            catch (Exception)
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
                var multipleReq = new ExecuteMultipleRequest
                {
                    Requests = new OrganizationRequestCollection(),
                    Settings = new ExecuteMultipleSettings { ContinueOnError = true, ReturnResponses = true }
                };

                multipleReq.Requests.AddRange(requests);

                var response = _service.Execute(multipleReq) as ExecuteMultipleResponse;

                return response.Responses
                    .Select(resp => new CrmBulkResponse
                    {
                        Id = multipleReq.Requests[resp.RequestIndex].RequestId.GetValueOrDefault(Guid.Empty),
                        Success = resp.Fault == null,
                        Message = resp.Fault != null ? resp.Fault.Message : string.Empty
                    });
            }
            catch (Exception)
            {
                throw;
            }
        }
        #endregion Private Methods
    }
}
