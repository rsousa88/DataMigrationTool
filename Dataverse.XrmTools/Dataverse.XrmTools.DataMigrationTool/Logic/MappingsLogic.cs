// System
using System;
using System.Linq;
using System.Collections.Generic;

// Microsoft
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;

// DataMigrationTool
using Dataverse.XrmTools.DataMigrationTool.Enums;
using Dataverse.XrmTools.DataMigrationTool.AppSettings;
using Dataverse.XrmTools.DataMigrationTool.Repositories;

namespace Dataverse.XrmTools.DataMigrationTool.Logic
{
    public class MappingsLogic
    {
        #region Global Variables
        private readonly IOrganizationService  _sourceSvc;
        private readonly IOrganizationService  _targetSvc;
        #endregion Global Variables

        #region Constructors
        public MappingsLogic(IOrganizationService sourceSvc, IOrganizationService targetSvc)
        {
            _sourceSvc = sourceSvc;
            _targetSvc = targetSvc;
        }
        #endregion Constructors

        public IEnumerable<Mapping> GetUserMappings()
        {
            var sourceUsers = GetAllUsers(new CrmRepo(_sourceSvc));
            var targetUsers = GetTargetUsers(new CrmRepo(_targetSvc), sourceUsers);

            var mappings = targetUsers
                .Select(tu => new Mapping
                {
                    Type = MappingType.Value,
                    TableLogicalName = "systemuser",
                    TableDisplayName = "System User",
                    AttributeLogicalName = "systemuserid",
                    AttributeDisplayName = "System User Id",
                    SourceId = sourceUsers
                        .Where(su => su.Attributes["domainname"].ToString().Equals(tu.GetAttributeValue<string>("domainname")))
                        .Select(su => su.GetAttributeValue<Guid>("systemuserid"))
                        .FirstOrDefault(),
                    TargetId = tu.GetAttributeValue<Guid>("systemuserid"),
                    State = MappingState.Auto
                });

            return mappings;
        }

        public IEnumerable<Mapping> GetTeamMappings()
        {
            var sourceTeams = GetAllTeams(new CrmRepo(_sourceSvc));
            var targetTeams = GetTargetTeams(new CrmRepo(_targetSvc), sourceTeams);

            var mappings = targetTeams
                .Select(tt => new Mapping
                {
                    Type = MappingType.Value,
                    TableLogicalName = "team",
                    TableDisplayName = "Team",
                    AttributeLogicalName = "teamid",
                    AttributeDisplayName = "Team Id",
                    SourceId = sourceTeams
                        .Where(st => st.Attributes["name"].ToString().Equals(tt.GetAttributeValue<string>("name")))
                        .Select(st => st.GetAttributeValue<Guid>("teamid"))
                        .FirstOrDefault(),
                    TargetId = tt.GetAttributeValue<Guid>("teamid"),
                    State = MappingState.Auto
                });

            return mappings;
        }

        public Mapping GetBusinessUnitMapping()
        {
            var sourceBu = GetRootBusinessUnit(new CrmRepo(_sourceSvc));
            var targetBu = GetRootBusinessUnit(new CrmRepo(_targetSvc));

            if (sourceBu == null || targetBu == null) { return null; }

            return new Mapping
            {
                Type = MappingType.Value,
                TableLogicalName = "businessunit",
                TableDisplayName = "Business Unit",
                AttributeLogicalName = "businessunitid",
                AttributeDisplayName = "Business Unit Id",
                SourceId = sourceBu.GetAttributeValue<Guid>("businessunitid"),
                TargetId = targetBu.GetAttributeValue<Guid>("businessunitid"),
                State = MappingState.Auto
            };
        }

        private IEnumerable<Entity> GetAllUsers(CrmRepo repo)
        {
            var query = new QueryExpression("systemuser")
            {
                ColumnSet = new ColumnSet("systemuserid", "domainname"),
                PageInfo = new PagingInfo() { Count = 5000, PageNumber = 1 }
            };

            return repo.GetRecords(query);
        }
        private IEnumerable<Entity> GetTargetUsers(CrmRepo repo, IEnumerable<Entity> sourceUsers)
        {
            var domains = sourceUsers.Select(su => su.Attributes["domainname"].ToString());
            return repo.GetUsersInDomainList(domains.ToArray(), new string[] { "systemuserid", "domainname" });
        }

        private IEnumerable<Entity> GetAllTeams(CrmRepo repo)
        {
            var query = new QueryExpression("team")
            {
                ColumnSet = new ColumnSet("teamid", "name"),
                PageInfo = new PagingInfo() { Count = 5000, PageNumber = 1 }
            };

            return repo.GetRecords(query);
        }
        private IEnumerable<Entity> GetTargetTeams(CrmRepo repo, IEnumerable<Entity> sourceTeams)
        {
            var names = sourceTeams.Select(team => team.Attributes["name"].ToString());
            return repo.GetTeamsInNameList(names.ToArray(), new string[] { "teamid", "name" });
        }

        private Entity GetRootBusinessUnit(CrmRepo repo)
        {
            var filter = new FilterExpression(LogicalOperator.And)
            {
                Conditions =
                    {
                        new ConditionExpression("parentbusinessunitid", ConditionOperator.Null)
                    }
            };

            var query = new QueryExpression("businessunit")
            {
                ColumnSet = new ColumnSet("businessunitid"),
                Criteria = filter,
                PageInfo = new PagingInfo() { Count = 5000, PageNumber = 1 }
            };

            return repo.GetRecords(query).FirstOrDefault();
        }
    }
}
