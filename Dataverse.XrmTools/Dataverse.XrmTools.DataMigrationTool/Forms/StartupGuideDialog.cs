// System
using System;
using System.Drawing;
using System.Windows.Forms;

namespace Dataverse.XrmTools.DataMigrationTool.Forms
{
    public class StartupGuideDialog : Form
    {
        private readonly CheckBox _hideOnStartup;

        public bool HideOnStartup => _hideOnStartup.Checked;

        public StartupGuideDialog()
        {
            Text = "Data Migration Tool guide";
            StartPosition = FormStartPosition.CenterParent;
            ShowIcon = false;
            ShowInTaskbar = false;
            MinimizeBox = false;
            MaximizeBox = false;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            ClientSize = new Size(760, 560);

            var layout = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 1,
                RowCount = 4,
                Padding = new Padding(18)
            };
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 50));
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 34));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 44));

            var title = new Label
            {
                Text = "Quick guide",
                Dock = DockStyle.Fill,
                Font = new Font(Font.FontFamily, 16F, FontStyle.Bold),
                TextAlign = ContentAlignment.MiddleLeft
            };

            var body = new RichTextBox
            {
                Dock = DockStyle.Fill,
                BorderStyle = BorderStyle.None,
                BackColor = SystemColors.Window,
                ReadOnly = true,
                DetectUrls = false,
                Text =
@"DATA MIGRATION TOOL - QUICK GUIDE

Core workflow
1. Connect to source.
2. Create or open a .dmtproj project.
3. Connect one or more targets.
4. Pull from Dataverse or import JSON/Excel into snapshots.
5. Review or edit snapshot data.
6. Add snapshots to a plan and push to target.

Where things live
- Top bar: global project, connection, environment, and help actions.
- Left strip: table setup, filters, preview, and export.
- Snapshots strip: pull, import, refresh, export, Rowcraft, and Add to Plan.
- Plan panel: validate, preview, execute, clone, move, and reconfigure steps.

Rowcraft editing
- Select a snapshot and choose Rowcraft > Open in Rowcraft.
- Edit rows in Rowcraft without uploading the .dmtproj file.
- Return to DMT and choose Rowcraft > Apply Rowcraft Changes.
- The RC column shows pending Rowcraft edits.

Useful notes
- Snapshots keep data, column metadata, and refresh source together.
- ID mappings are saved so later pushes update existing target rows.
- Environment tags keep multi-target plans readable."
            };

            _hideOnStartup = new CheckBox
            {
                Text = "Don't show this guide on startup",
                Dock = DockStyle.Fill,
                AutoSize = true
            };

            var buttons = new FlowLayoutPanel
            {
                Dock = DockStyle.Fill,
                FlowDirection = FlowDirection.RightToLeft,
                Padding = new Padding(0, 8, 0, 0)
            };
            var ok = new Button { Text = "Got it", Width = 96, Height = 28, DialogResult = DialogResult.OK };
            buttons.Controls.Add(ok);

            layout.Controls.Add(title, 0, 0);
            layout.Controls.Add(body, 0, 1);
            layout.Controls.Add(_hideOnStartup, 0, 2);
            layout.Controls.Add(buttons, 0, 3);
            Controls.Add(layout);

            AcceptButton = ok;
        }
    }
}
