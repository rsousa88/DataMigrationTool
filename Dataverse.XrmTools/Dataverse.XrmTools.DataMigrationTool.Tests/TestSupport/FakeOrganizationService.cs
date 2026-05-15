// System
using System;
using System.Collections.Generic;

// Microsoft
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;

namespace Dataverse.XrmTools.DataMigrationTool.Tests.TestSupport
{
    public class FakeOrganizationService : IOrganizationService
    {
        private readonly Dictionary<string, Entity> _entities = new Dictionary<string, Entity>(StringComparer.OrdinalIgnoreCase);

        public List<Entity> CreatedEntities { get; } = new List<Entity>();
        public List<Entity> UpdatedEntities { get; } = new List<Entity>();
        public List<Tuple<string, Guid>> DeletedEntities { get; } = new List<Tuple<string, Guid>>();
        public List<OrganizationRequest> ExecutedRequests { get; } = new List<OrganizationRequest>();
        public Func<QueryBase, EntityCollection> RetrieveMultipleHandler { get; set; }
        public Func<OrganizationRequest, OrganizationResponse> ExecuteHandler { get; set; }

        public void Add(Entity entity)
        {
            if (entity == null) throw new ArgumentNullException(nameof(entity));
            if (entity.Id == Guid.Empty) entity.Id = Guid.NewGuid();
            _entities[Key(entity.LogicalName, entity.Id)] = entity;
        }

        public Guid Create(Entity entity)
        {
            if (entity == null) throw new ArgumentNullException(nameof(entity));
            if (entity.Id == Guid.Empty) entity.Id = Guid.NewGuid();
            CreatedEntities.Add(entity);
            Add(entity);
            return entity.Id;
        }

        public Entity Retrieve(string entityName, Guid id, ColumnSet columnSet)
        {
            return _entities.TryGetValue(Key(entityName, id), out var entity) ? entity : null;
        }

        public void Update(Entity entity)
        {
            if (entity == null) throw new ArgumentNullException(nameof(entity));
            UpdatedEntities.Add(entity);
            Add(entity);
        }

        public void Delete(string entityName, Guid id)
        {
            DeletedEntities.Add(Tuple.Create(entityName, id));
            _entities.Remove(Key(entityName, id));
        }

        public EntityCollection RetrieveMultiple(QueryBase query)
        {
            return RetrieveMultipleHandler != null
                ? RetrieveMultipleHandler(query)
                : new EntityCollection();
        }

        public void Associate(string entityName, Guid entityId, Relationship relationship, EntityReferenceCollection relatedEntities)
        {
        }

        public void Disassociate(string entityName, Guid entityId, Relationship relationship, EntityReferenceCollection relatedEntities)
        {
        }

        public OrganizationResponse Execute(OrganizationRequest request)
        {
            ExecutedRequests.Add(request);
            return ExecuteHandler != null ? ExecuteHandler(request) : new OrganizationResponse();
        }

        private static string Key(string logicalName, Guid id)
        {
            return $"{logicalName}:{id:D}";
        }
    }
}
