// System
using System;
using System.Linq;
using System.Windows.Forms;
using System.Collections.Generic;

// Microsoft
using Microsoft.Xrm.Sdk;

// DataMigrationTool
using Dataverse.XrmTools.DataMigrationTool.Enums;
using Dataverse.XrmTools.DataMigrationTool.AppSettings;
using Dataverse.XrmTools.DataMigrationTool.Models;
using Dataverse.XrmTools.DataMigrationTool.Helpers;

namespace Dataverse.XrmTools.DataMigrationTool.Forms
{
    public partial class Mappings : Form
    {
        public bool Updated { get; set; }

        private IOrganizationService _service;
        private Settings _settings;
        private Instance _instance;
        private IEnumerable<Table> _tables;
        private List<Mapping> _backup;

        public Mappings(IOrganizationService service, Instance instance, IEnumerable<Table> tables, Settings settings)
        {
            Updated = false;

            _service = service;
            _settings = settings;
            _instance = instance;
            _tables = tables;
            _backup = new List<Mapping>(_instance.Mappings);

            InitializeComponent();
        }

        private void MappingListsLoad(object sender, EventArgs e)
        {
            lvAttributeMappings.Items.Clear();
            lvValueMappings.Items.Clear();

            var attrMapItems = _instance.Mappings
                .Where(map => map.Type.Equals(MappingType.Attribute))
                .Select(map => map.ToListViewItem(new Tuple<string, object>("mappingtype", MappingType.Attribute)));

            lvAttributeMappings.Items.AddRange(attrMapItems.ToArray());

            // re-render list view columns
            lvAttributeMappings.AutoResizeColumns(ColumnHeaderAutoResizeStyle.ColumnContent);

            // ensure minimum width
            if (chAMapType.Width < 90) { chAMapType.Width = 90; }
            if (chAMapTableDisplay.Width < 280) { chAMapTableDisplay.Width = 280; }
            if (chAMapTableLogical.Width < 230) { chAMapTableLogical.Width = 230; }
            if (chAMapAttributeDisplay.Width < 280) { chAMapAttributeDisplay.Width = 280; }
            if (chAMapAttributeLogical.Width < 230) { chAMapAttributeLogical.Width = 230; }
            if (chAMapState.Width < 150) { chAMapState.Width = 150; }

            var valMapItems = _instance.Mappings
                .Where(map => map.Type.Equals(MappingType.Value))
                .Select(map => map.ToListViewItem(new Tuple<string, object>("mappingtype", MappingType.Value)));

            lvValueMappings.Items.AddRange(valMapItems.ToArray());

            // re-render list view columns
            lvValueMappings.AutoResizeColumns(ColumnHeaderAutoResizeStyle.ColumnContent);

            // ensure minimum width
            if (chVMapType.Width < 90) { chVMapType.Width = 90; }
            if (chVMapTableDisplay.Width < 160) { chVMapTableDisplay.Width = 160; }
            if (chVMapTableLogical.Width < 140) { chVMapTableLogical.Width = 140; }
            if (chVMapAttributeDisplay.Width < 160) { chVMapAttributeDisplay.Width = 160; }
            if (chVMapAttributeLogical.Width < 140) { chVMapAttributeLogical.Width = 140; }
            if (chVMapSourceId.Width < 225) { chVMapSourceId.Width = 225; }
            if (chVMapTargetId.Width < 225) { chVMapSourceId.Width = 225; }
            if (chVMapState.Width < 125) { chVMapState.Width = 125; }
        }

        private void btnNewAttribute_Click(object sender, EventArgs e)
        {
            var newAttrMappingDlg = new AttributeMapping(_service, _instance, _tables);
            newAttrMappingDlg.ShowDialog(ParentForm);

            MappingListsLoad(null, null);
        }

        private void btnNewValue_Click(object sender, EventArgs e)
        {
            var newValMappingDlg = new ValueMapping(_service, _instance, _tables);
            newValMappingDlg.ShowDialog(ParentForm);

            MappingListsLoad(null, null);
        }

        private void btnClose_Click(object sender, EventArgs e)
        {
            try
            {
                // update mappings to existing
                foreach (var map in _instance.Mappings.Where(map => map.State == MappingState.New || map.State == MappingState.Undo))
                {
                    map.State = MappingState.Existing;
                }

                // remove mappings to be deleted
                var toDelete = _instance.Mappings.Where(map => map.State == MappingState.Delete).ToList();
                foreach (var map in toDelete)
                {
                    _instance.Mappings.Remove(map);
                }

                Updated = true;
                Close();
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message, "Warning", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }

        private void btnCancel_Click(object sender, EventArgs e)
        {
            // purge new mappings
            _instance.Mappings = _backup;
            Close();
        }

        private void listView_ColumnClick(object sender, ColumnClickEventArgs e)
        {
            (sender as ListView).Sort(_settings, e.Column);
            Updated = true;
        }

        private void cms_ContextMenu_Opening(object sender, System.ComponentModel.CancelEventArgs e)
        {
            if (lvAttributeMappings.FocusedItem != null && lvAttributeMappings.SelectedItems.Count > 0)
            {
                ControlContextOptions(lvAttributeMappings, MappingType.Attribute);
            }

            if (lvValueMappings.FocusedItem != null && lvValueMappings.SelectedItems.Count > 0)
            {
                ControlContextOptions(lvValueMappings, MappingType.Value);
            }
        }

        private void cmsi_Delete_Click(object sender, EventArgs e)
        {
            if (lvAttributeMappings.FocusedItem != null && lvAttributeMappings.SelectedItems.Count > 0)
            {
                DeleteMappings(lvAttributeMappings, MappingType.Attribute);
            }

            if (lvValueMappings.FocusedItem != null && lvValueMappings.SelectedItems.Count > 0)
            {
                DeleteMappings(lvValueMappings, MappingType.Value);
            }
        }

        private void cmsi_UndoDelete_Click(object sender, EventArgs e)
        {
            if (lvAttributeMappings.FocusedItem != null && lvAttributeMappings.SelectedItems.Count > 0)
            {
                UndoDeleteMappings(lvAttributeMappings, MappingType.Attribute);
            }

            if (lvValueMappings.FocusedItem != null && lvValueMappings.SelectedItems.Count > 0)
            {
                UndoDeleteMappings(lvValueMappings, MappingType.Value);
            }
        }

        private void ControlContextOptions (ListView listView, MappingType mappingType)
        {
            if (listView.FocusedItem != null && listView.SelectedItems.Count > 0)
            {
                cmsi_Delete.Visible = false;
                cmsi_UndoDelete.Visible = false;

                var selectedMappings = listView.SelectedItems
                    .Cast<ListViewItem>()
                    .Select(lvi => lvi.ToObject(new Mapping(), new Tuple<string, object>("mappingtype", mappingType)) as Mapping)
                    .Select(map => _instance.Mappings.FirstOrDefault(insMap => insMap.Type.Equals(mappingType) && insMap.TableLogicalName.Equals(map.TableLogicalName) && insMap.AttributeLogicalName.Equals(map.AttributeLogicalName)));

                if (selectedMappings.Any(map => !map.State.Equals(MappingState.Delete)))
                {
                    cmsi_Delete.Visible = true;
                }

                if (selectedMappings.Any(map => map.State.Equals(MappingState.Delete)))
                {
                    cmsi_UndoDelete.Visible = true;
                }
            }
        }

        private void DeleteMappings(ListView listView, MappingType mappingType)
        {
            var toDelete = listView.SelectedItems
                    .Cast<ListViewItem>()
                    .Select(lvi => lvi.ToObject(new Mapping(), new Tuple<string, object>("mappingtype", mappingType)) as Mapping)
                    .Select(map => _instance.Mappings
                        .FirstOrDefault(insMap =>
                            insMap.Type.Equals(mappingType) &&
                            insMap.TableLogicalName.Equals(map.TableLogicalName) &&
                            insMap.AttributeLogicalName.Equals(map.AttributeLogicalName) &&
                            !insMap.State.Equals(MappingState.Delete)
                            )).ToList();

            // mark selected mappings to be deleted
            var intersect = _instance.Mappings.Intersect(toDelete);
            foreach (var del in intersect)
            {
                del.State = MappingState.Delete;
            }

            MappingListsLoad(null, null);
        }

        private void UndoDeleteMappings(ListView listView, MappingType mappingType)
        {
            var toUndo = listView.SelectedItems
                    .Cast<ListViewItem>()
                    .Select(lvi => lvi.ToObject(new Mapping(), new Tuple<string, object>("mappingtype", mappingType)) as Mapping)
                    .Select(map => _instance.Mappings
                        .FirstOrDefault(insMap =>
                            insMap.Type.Equals(mappingType) &&
                            insMap.TableLogicalName.Equals(map.TableLogicalName) &&
                            insMap.AttributeLogicalName.Equals(map.AttributeLogicalName) &&
                            insMap.State.Equals(MappingState.Delete)
                            )).ToList();

            // mark selected mappings to be undone
            var intersect = _instance.Mappings.Intersect(toUndo);
            foreach (var undo in intersect)
            {
                undo.State = MappingState.Undo;
            }

            MappingListsLoad(null, null);
        }
    }
}
