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
        private Instance _sourceInstance;
        private Instance _targetInstance;
        private IEnumerable<Table> _tables;
        private List<Mapping> _backup;

        public Mappings(IOrganizationService service, Instance sourceInstance, Instance targetInstance, IEnumerable<Table> tables, Settings settings)
        {
            Updated = false;

            _service = service;
            _settings = settings;
            _sourceInstance = sourceInstance;
            _targetInstance = targetInstance;
            _tables = tables;
            _backup = new List<Mapping>(_sourceInstance.Mappings);

            InitializeComponent();
        }

        private void MappingListsLoad(object sender, EventArgs e)
        {
            lvAttributeMappings.Items.Clear();
            var attrMapItems = GetMappings(MappingType.Attribute);
            lvAttributeMappings.Items.AddRange(attrMapItems.ToArray());

            lvValueMappings.Items.Clear();
            var valMapItems = GetMappings(MappingType.Value);
            lvValueMappings.Items.AddRange(valMapItems.ToArray());
        }

        private IEnumerable<ListViewItem> GetMappings(MappingType type, bool hideAuto = false)
        {
            if(type.Equals(MappingType.Attribute))
            {
                return _sourceInstance.Mappings
                    .Where(map => map.Type.Equals(MappingType.Attribute))
                    .Select(map => map.ToListViewItem(new Tuple<string, object>("mappingtype", MappingType.Attribute)));
            }
            else if (type.Equals(MappingType.Value))
            {
                var filter = new Func<Mapping, bool>(
                    map => map.Type.Equals(MappingType.Value)
                    && map.TargetInstanceName.Equals(_targetInstance.FriendlyName)
                );

                if (hideAuto)
                {
                    filter = new Func<Mapping, bool>(
                        map => map.Type.Equals(MappingType.Value)
                        && map.TargetInstanceName.Equals(_targetInstance.FriendlyName)
                        && !map.State.Equals(MappingState.Auto)
                    );
                }
                
                return _sourceInstance.Mappings
                    .Where(filter)
                    .Select(map => map.ToListViewItem(new Tuple<string, object>("mappingtype", MappingType.Value)));
            }
            else
            {
                throw new Exception($"Invalid Mapping Type {type}");
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
                    .Select(map => _sourceInstance.Mappings.FirstOrDefault(insMap => insMap.Type.Equals(mappingType) && insMap.TableLogicalName.Equals(map.TableLogicalName)));

                if (selectedMappings.Any(map => !map.State.Equals(MappingState.Delete) && !map.State.Equals(MappingState.Auto)))
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
                    .Select(map => _sourceInstance.Mappings
                        .FirstOrDefault(insMap =>
                            insMap.Type.Equals(mappingType) &&
                            insMap.TableLogicalName.Equals(map.TableLogicalName) &&
                            insMap.SourceId.Equals(map.SourceId) &&
                            insMap.TargetId.Equals(map.TargetId) &&
                            !insMap.State.Equals(MappingState.Delete)
                            )).ToList();

            // mark selected mappings to be deleted
            var intersect = _sourceInstance.Mappings.Intersect(toDelete);
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
                    .Select(map => _sourceInstance.Mappings
                        .FirstOrDefault(insMap =>
                            insMap.Type.Equals(mappingType) &&
                            insMap.TableLogicalName.Equals(map.TableLogicalName) &&
                            insMap.AttributeLogicalName.Equals(map.AttributeLogicalName) &&
                            insMap.State.Equals(MappingState.Delete)
                            )).ToList();

            // mark selected mappings to be undone
            var intersect = _sourceInstance.Mappings.Intersect(toUndo);
            foreach (var undo in intersect)
            {
                undo.State = MappingState.Undo;
            }

            MappingListsLoad(null, null);
        }

        private void btnNewAttribute_Click(object sender, EventArgs e)
        {
            var newAttrMappingDlg = new AttributeMapping(_service, _sourceInstance, _tables);
            newAttrMappingDlg.ShowDialog(ParentForm);

            MappingListsLoad(null, null);
        }

        private void btnNewValue_Click(object sender, EventArgs e)
        {
            var newValMappingDlg = new ValueMapping(_sourceInstance, _targetInstance, _tables);
            newValMappingDlg.ShowDialog(ParentForm);

            MappingListsLoad(null, null);
        }

        private void btnClose_Click(object sender, EventArgs e)
        {
            try
            {
                // update mappings to existing
                foreach (var map in _sourceInstance.Mappings.Where(map => map.State == MappingState.New || map.State == MappingState.Undo))
                {
                    map.State = MappingState.Existing;
                }

                // remove mappings to be deleted
                var toDelete = _sourceInstance.Mappings.Where(map => map.State == MappingState.Delete).ToList();
                foreach (var map in toDelete)
                {
                    _sourceInstance.Mappings.Remove(map);
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
            _sourceInstance.Mappings = _backup;
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

        private void cbHideAutoMappings_CheckedChanged(object sender, EventArgs e)
        {
            lvValueMappings.Items.Clear();
            var valMapItems = GetMappings(MappingType.Value, cbHideAutoMappings.Checked);

            lvValueMappings.Items.AddRange(valMapItems.ToArray());
        }

        private void lvAttributeMappings_Resize(object sender, EventArgs e)
        {
            // re-render list view columns
            var maxWidth = lvAttributeMappings.Width >= 300 ? lvAttributeMappings.Width : 300;
            chAMapType.Width = (int)Math.Floor(maxWidth * 0.09);
            chAMapTableDisplay.Width = (int)Math.Floor(maxWidth * 0.25);
            chAMapTableLogical.Width = (int)Math.Floor(maxWidth * 0.15);
            chAMapAttributeDisplay.Width = (int)Math.Floor(maxWidth * 0.25);
            chAMapAttributeLogical.Width = (int)Math.Floor(maxWidth * 0.15);
            chAMapState.Width = (int)Math.Floor(maxWidth * 0.09);

            lvAttributeMappings.Scrollable = true;
        }

        private void lvValueMappings_Resize(object sender, EventArgs e)
        {
            // re-render list view columns
            var maxWidth = lvValueMappings.Width >= 300 ? lvValueMappings.Width : 300;
            chVMapType.Width = (int)Math.Floor(maxWidth * 0.09);
            chVMapTableDisplay.Width = (int)Math.Floor(maxWidth * 0.15);
            chVMapTableLogical.Width = (int)Math.Floor(maxWidth * 0.15);
            chVMapSourceId.Width = (int)Math.Floor(maxWidth * 0.19);
            chVMapTargetId.Width = (int)Math.Floor(maxWidth * 0.19);
            chVMapTargetInstance.Width = (int)Math.Floor(maxWidth * 0.12);
            chVMapState.Width = (int)Math.Floor(maxWidth * 0.09);

            lvValueMappings.Scrollable = true;
        }
    }
}
