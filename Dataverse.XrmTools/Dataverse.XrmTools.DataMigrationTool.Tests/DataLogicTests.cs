﻿// DataMigrationTool
using Dataverse.XrmTools.DataMigrationTool.AppSettings;
using Dataverse.XrmTools.DataMigrationTool.Enums;
using Dataverse.XrmTools.DataMigrationTool.Logic;
using Dataverse.XrmTools.DataMigrationTool.Models;
using System.Collections.Generic;
using System.ComponentModel;

// 3rd Party
using Xunit;

namespace Dataverse.XrmTools.DataMigrationTool.Tests
{
    public class DataLogicTests
    {
        [Fact]
        public void Preview_Test()
        {
            var uiSettings = new UiSettings
            {
                Action = Action.Preview | Action.Create | Action.Update,
                BatchSize = 250,
                MapUsers = false,
                MapTeams = false,
                MapBu = true,
                ApplyMappingsOn = Operation.Export
            };

            var tableData = new TableData
            {
                Table = new Table
                {
                    LogicalName = "account",
                    DisplayName = "Account",
                    IdAttribute = "accountid",
                    NameAttribute = "name",
                    IsCustomizable = true
                },
                Settings = new TableSettings
                {
                    LogicalName = "account"
                },
                SelectedAttributes = new List<Attribute>
                {
                    new Attribute
                    {
                        LogicalName = "name",
                        DisplayName = "Name",
                        Type = "Text",
                        Updatable = true
                    }
                }
            };

            var logic = new DataLogic(new BackgroundWorker(), )
        }
    }
}
