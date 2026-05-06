using System;
using System.Data;
using System.Linq;
using System.Windows.Forms;
using System.Collections.Generic;
using System.Text;
using static System.Windows.Forms.ListViewItem;
using Dataverse.XrmTools.DataMigrationTool.Helpers;
using Dataverse.XrmTools.DataMigrationTool.AppSettings;

namespace Dataverse.XrmTools.DataMigrationTool.Forms
{
    public partial class Results : Form
    {
        private Settings _settings;
        private List<ListViewItem> _recordItems;
        private CheckBox _cbShowAllRows;
        private Label _lblSumSuccess;
        private Label _lblSumSuccessValue;
        private Label _lblSumFailed;
        private Label _lblSumFailedValue;
        private readonly bool _allowRetryFailed;
        private readonly bool _showExecutionControls;
        private Button _btnRetryFailed;
        private Panel _pnlShowAllRows;
        public List<Guid> FailedRecordIds { get; private set; } = new List<Guid>();

        public Results(IEnumerable<ListViewItem> recordItems, Settings settings, bool allowRetryFailed = false)
        {
            _settings = settings;
            _recordItems = AddRowNumbers(recordItems ?? Enumerable.Empty<ListViewItem>()).ToList();
            _allowRetryFailed = allowRetryFailed;
            _showExecutionControls = allowRetryFailed;
            InitializeComponent();
            ConfigureResultsUi();
        }

        private void LoadRecords(object sender, EventArgs e)
        {
            SetSummary();
            LoadFilteredItems();
        }

        private void SetSummary()
        {
            var createCount = _recordItems.Count(prv => GetAction(prv).Equals("Create"));
            var updateCount = _recordItems.Count(prv => GetAction(prv).Equals("Update"));
            var deleteCount = _recordItems.Count(prv => GetAction(prv).Equals("Delete"));
            var previewCount = _recordItems.Count(prv => GetAction(prv).Equals("Preview"));
            var failedCount = _recordItems.Count(IsFailed);
            var successCount = _recordItems.Count - failedCount;

            lblSumCreateValue.Text = createCount.ToString();
            lblSumUpdateValue.Text = updateCount.ToString();
            lblSumDeleteValue.Text = deleteCount.ToString();
            lblSumTotalValue.Text = (createCount + updateCount + deleteCount + previewCount).ToString();
            if (_showExecutionControls)
            {
                _lblSumSuccessValue.Text = successCount.ToString();
                _lblSumFailedValue.Text = failedCount.ToString();
            }

            gbResults.Text = _showExecutionControls && failedCount > 0 && !_cbShowAllRows.Checked
                ? $"Results - failed rows ({failedCount})"
                : $"Results - all rows ({_recordItems.Count})";
        }

        private void listView_ColumnClick(object sender, ColumnClickEventArgs e)
        {
            (sender as ListView).Sort(_settings, e.Column);
        }

        private void lvItems_KeyUp(object sender, KeyEventArgs e)
        {
            if (sender != lvItems) return;

            if (e.Control && e.KeyCode == Keys.C)
            {
                CopySelectedValuesToClipboard();
            }
        }

        private void Results_Resize(object sender, EventArgs e)
        {
            // re-render list view columns
            var maxWidth = lvItems.Width >= 500 ? lvItems.Width : 500;
            lvItems.Columns[0].Width = (int)Math.Floor(maxWidth * 0.05);
            chResAction.Width = (int)Math.Floor(maxWidth * 0.08);
            chResRecordId.Width = (int)Math.Floor(maxWidth * 0.24);
            chResRecordName.Width = (int)Math.Floor(maxWidth * 0.27);
            chResDescription.Width = (int)Math.Floor(maxWidth * 0.35);
            LayoutSummaryLabels();
        }

        private void btnClose_Click(object sender, EventArgs e)
        {
            Close();
        }

        private void btnRetryFailed_Click(object sender, EventArgs e)
        {
            FailedRecordIds = _recordItems
                .Where(IsFailed)
                .Select(GetRecordId)
                .Where(id => id.HasValue)
                .Select(id => id.Value)
                .Distinct()
                .ToList();

            if (!FailedRecordIds.Any()) return;

            DialogResult = DialogResult.Retry;
            Close();
        }

        private void CopySelectedValuesToClipboard()
        {
            var builder = new StringBuilder();

            // add columns
            foreach (ColumnHeader column in lvItems.Columns)
            {
                builder.Append($"{column.Text};");
            }

            builder.AppendLine();

            // add rows
            foreach (ListViewItem item in lvItems.SelectedItems)
            {
                foreach (ListViewSubItem sub in item.SubItems)
                {
                    builder.Append($"{sub.Text};");
                }

                builder.AppendLine();
            }

            // set clipboard
            Clipboard.SetText(builder.ToString());
        }

        private void ConfigureResultsUi()
        {
            lvItems.Columns.Insert(0, new ColumnHeader { Text = "Row", Width = 70 });

            pnlBody.ColumnStyles[0].SizeType = SizeType.Percent;
            pnlBody.ColumnStyles[0].Width = 100F;
            pnlBody.ColumnStyles[1].SizeType = SizeType.Absolute;
            pnlBody.ColumnStyles[1].Width = _showExecutionControls ? 185F : 150F;

            if (_showExecutionControls)
            {
                _pnlShowAllRows = new Panel
                {
                    Dock = DockStyle.Top,
                    Height = 30,
                    Padding = new Padding(7, 3, 0, 0),
                    BackColor = System.Drawing.SystemColors.Window
                };
                _cbShowAllRows = new CheckBox
                {
                    Text = "Show all rows",
                    AutoSize = true,
                    Checked = !_recordItems.Any(IsFailed)
                };
                _cbShowAllRows.CheckedChanged += (s, e) =>
                {
                    LoadFilteredItems();
                    SetSummary();
                };
                _pnlShowAllRows.Controls.Add(_cbShowAllRows);
                gbResults.Controls.Add(_pnlShowAllRows);
                _pnlShowAllRows.BringToFront();

                _lblSumSuccess = new Label { Text = "Success:", AutoSize = true, Location = new System.Drawing.Point(7, 117) };
                _lblSumSuccessValue = new Label { Text = "0", RightToLeft = RightToLeft.Yes, Location = new System.Drawing.Point(96, 117), Size = lblSumTotalValue.Size, Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right };
                _lblSumFailed = new Label { Text = "Failed:", AutoSize = true, Location = new System.Drawing.Point(7, 136) };
                _lblSumFailedValue = new Label { Text = "0", RightToLeft = RightToLeft.Yes, Location = new System.Drawing.Point(96, 136), Size = lblSumTotalValue.Size, Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right };
                gbSummary.Controls.Add(_lblSumSuccess);
                gbSummary.Controls.Add(_lblSumSuccessValue);
                gbSummary.Controls.Add(_lblSumFailed);
                gbSummary.Controls.Add(_lblSumFailedValue);
            }

            if (_allowRetryFailed)
            {
                _btnRetryFailed = new Button
                {
                    Text = "Retry failed",
                    Width = 112,
                    Height = 28,
                    Anchor = AnchorStyles.Right | AnchorStyles.Bottom,
                    Enabled = _recordItems.Any(IsFailed)
                };
                _btnRetryFailed.Location = new System.Drawing.Point(btnClose.Left - _btnRetryFailed.Width - 8, btnClose.Top);
                _btnRetryFailed.Click += btnRetryFailed_Click;
                pnlFooter.Controls.Add(_btnRetryFailed);
                pnlFooter.Resize += (s, e) =>
                {
                    _btnRetryFailed.Left = btnClose.Left - _btnRetryFailed.Width - 8;
                    _btnRetryFailed.Top = btnClose.Top;
                };
            }
        }

        private void LoadFilteredItems()
        {
            lvItems.Items.Clear();
            var items = !_showExecutionControls || _cbShowAllRows.Checked ? _recordItems : _recordItems.Where(IsFailed);
            lvItems.Items.AddRange(items.Select(CloneListViewItem).ToArray());
            Results_Resize(this, EventArgs.Empty);
        }

        private void LayoutSummaryLabels()
        {
            var valueLeft = 96;
            var valueWidth = Math.Max(50, gbSummary.ClientSize.Width - valueLeft - 8);
            foreach (var label in new[] { lblSumCreateValue, lblSumUpdateValue, lblSumDeleteValue, lblSumTotalValue, _lblSumSuccessValue, _lblSumFailedValue }.Where(lbl => lbl != null))
            {
                label.Left = valueLeft;
                label.Width = valueWidth;
            }
        }

        private IEnumerable<ListViewItem> AddRowNumbers(IEnumerable<ListViewItem> items)
        {
            var row = 1;
            foreach (var item in items)
            {
                var values = new List<string> { row.ToString() };
                values.AddRange(item.SubItems.Cast<ListViewSubItem>().Select(sub => sub.Text));
                var clone = new ListViewItem(values.ToArray())
                {
                    BackColor = item.BackColor,
                    ForeColor = item.ForeColor,
                    Tag = item.Tag
                };
                yield return clone;
                row++;
            }
        }

        private ListViewItem CloneListViewItem(ListViewItem item)
        {
            var clone = new ListViewItem(item.SubItems.Cast<ListViewSubItem>().Select(sub => sub.Text).ToArray())
            {
                BackColor = item.BackColor,
                ForeColor = item.ForeColor,
                Tag = item.Tag
            };
            if (IsFailed(clone))
                clone.ForeColor = System.Drawing.Color.DarkRed;
            return clone;
        }

        private bool IsFailed(ListViewItem item)
        {
            var description = item.SubItems.Count > 4 ? item.SubItems[4].Text : string.Empty;
            return description.StartsWith("ERROR:", StringComparison.OrdinalIgnoreCase)
                || description.IndexOf(" error", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private string GetAction(ListViewItem item)
        {
            return item.SubItems.Count > 1 ? item.SubItems[1].Text : string.Empty;
        }

        private Guid? GetRecordId(ListViewItem item)
        {
            if (item.SubItems.Count <= 2) return null;
            return Guid.TryParse(item.SubItems[2].Text, out var id) ? id : (Guid?)null;
        }
    }
}
