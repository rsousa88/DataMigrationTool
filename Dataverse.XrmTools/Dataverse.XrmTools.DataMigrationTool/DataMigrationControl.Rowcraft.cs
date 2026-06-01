// System
using System;
using System.Diagnostics;
using System.Linq;
using System.Windows.Forms;

// XrmToolBox
using XrmToolBox.Extensibility.Args;

// DataMigrationTool
using Dataverse.XrmTools.DataMigrationTool.Helpers;
using Dataverse.XrmTools.DataMigrationTool.Logic;
using Dataverse.XrmTools.DataMigrationTool.Models;

namespace Dataverse.XrmTools.DataMigrationTool
{
    public partial class DataMigrationControl
    {
        private const string RowcraftConnectBaseUrl = "https://rowcraft.io/connectors/connect";
        private RowcraftBridgeService _rowcraftBridge;

        private RowcraftBridgeService RowcraftBridge =>
            _rowcraftBridge ?? (_rowcraftBridge = new RowcraftBridgeService(() => _project?.Service));

        private bool ConfirmRowcraftBetaIfNeeded()
        {
            if (_settings.RowcraftBetaAccepted) return true;

            var message =
                "Rowcraft integration is currently in beta.\r\n\r\n" +
                "Your snapshot data is served to Rowcraft through a local authenticated bridge running on your machine. " +
                "No data leaves your device and nothing is stored in any external database or cloud storage.\r\n\r\n" +
                "Do you want to continue?";
            if (MessageBox.Show(this, message, "Rowcraft Beta", MessageBoxButtons.OKCancel, MessageBoxIcon.Information, MessageBoxDefaultButton.Button1) != DialogResult.OK)
                return false;

            _settings.RowcraftBetaAccepted = true;
            SettingsHelper.SetSettings(_settings);
            return true;
        }

        private void OpenInlineSnapshotInRowcraft()
        {
            if (!ConfirmRowcraftBetaIfNeeded()) return;

            var snap = GetInlineContextSnapshot();
            if (snap == null || _project?.Service == null)
            {
                MessageBox.Show(this, "Select a snapshot first.", "Rowcraft", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            try
            {
                var token = RowcraftBridge.StartForRowcraft();
                var bridge = Uri.EscapeDataString(RowcraftBridge.BaseUrl);
                var encodedToken = Uri.EscapeDataString(token);
                var dataset = Uri.EscapeDataString(snap.Name);
                var url = $"{RowcraftConnectBaseUrl}?bridge={bridge}&token={encodedToken}&dataset={dataset}";
                Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
                SendMessageToStatusBar?.Invoke(this, new StatusBarMessageEventArgs($"Rowcraft bridge started for '{snap.Name}'"));
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, $"Could not open Rowcraft: {ex.Message}", "Rowcraft", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void ApplyInlineRowcraftChanges()
        {
            var snap = GetInlineContextSnapshot();
            if (snap == null || _project?.Service == null) return;

            var session = _project.Service.GetPendingRowcraftEditSession(snap.Name);
            if (session == null)
            {
                MessageBox.Show(this, $"No pending Rowcraft changes for '{snap.Name}'.", "Rowcraft", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            var summary = _project.Service.GetRowcraftChangeSummary(session.Id);
            if (summary == null || summary.Total == 0)
            {
                MessageBox.Show(this, $"No pending Rowcraft changes for '{snap.Name}'.", "Rowcraft", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            var message =
                $"Apply pending Rowcraft changes to snapshot '{snap.Name}'?\r\n\r\n" +
                $"Create: {summary.Creates:N0}\r\nUpdate: {summary.Updates:N0}\r\nDelete: {summary.Deletes:N0}\r\n\r\n" +
                "This updates the DMT snapshot and marks dependent plan previews stale.";
            if (MessageBox.Show(this, message, "Apply Rowcraft Changes", MessageBoxButtons.YesNo, MessageBoxIcon.Question, MessageBoxDefaultButton.Button2) != DialogResult.Yes)
                return;

            try
            {
                var result = _project.Service.ApplyRowcraftEditSession(session.Id);
                var stalePlans = _project.Service.MarkPlanPreviewsStaleForSnapshot(snap.Name);
                _executionPlanValidatedForExecution = false;
                RefreshInlineSnapshotList();
                ReselectInlineSnapshot(snap.Name);
                RenderExecutionPlanPanel();
                SendMessageToStatusBar?.Invoke(this, new StatusBarMessageEventArgs(
                    $"Applied Rowcraft changes to '{snap.Name}': +{result.Created:N0}, ~{result.Updated:N0}, -{result.Deleted:N0}; {result.RowCount:N0} row(s). {stalePlans:N0} preview(s) marked stale."));
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, $"Apply failed: {ex.Message}", "Rowcraft", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void DiscardInlineRowcraftChanges()
        {
            var snap = GetInlineContextSnapshot();
            if (snap == null || _project?.Service == null) return;

            var session = _project.Service.GetPendingRowcraftEditSession(snap.Name);
            if (session == null)
            {
                MessageBox.Show(this, $"No pending Rowcraft changes for '{snap.Name}'.", "Rowcraft", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            var summary = _project.Service.GetRowcraftChangeSummary(session.Id);
            var total = summary?.Total ?? 0;
            if (MessageBox.Show(this,
                    $"Discard {total:N0} pending Rowcraft change(s) for '{snap.Name}'?",
                    "Discard Rowcraft Changes",
                    MessageBoxButtons.YesNo,
                    MessageBoxIcon.Warning,
                    MessageBoxDefaultButton.Button2) != DialogResult.Yes)
                return;

            try
            {
                _project.Service.DiscardRowcraftEditSession(session.Id);
                RefreshInlineSnapshotList();
                ReselectInlineSnapshot(snap.Name);
                SendMessageToStatusBar?.Invoke(this, new StatusBarMessageEventArgs($"Discarded Rowcraft changes for '{snap.Name}'"));
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, $"Discard failed: {ex.Message}", "Rowcraft", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void ReselectInlineSnapshot(string snapshotName)
        {
            if (_inlineSnapList == null) return;
            foreach (ListViewItem item in _inlineSnapList.Items)
            {
                if (string.Equals((item.Tag as DmtSnapshot)?.Name, snapshotName, StringComparison.OrdinalIgnoreCase))
                {
                    item.Selected = true;
                    item.Focused = true;
                    item.EnsureVisible();
                    break;
                }
            }
        }

        private void StopRowcraftBridge()
        {
            try { _rowcraftBridge?.Stop(); } catch { }
        }
    }
}
