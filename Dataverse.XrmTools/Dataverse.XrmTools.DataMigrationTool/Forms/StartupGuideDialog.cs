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

Project-based workflow (recommended)
A project file (.dmtproj) stores environments, table configs, snapshots, execution plans, ID mappings, and run history in one portable file.

1. Connect to your source environment using the Connect button.
2. Use Project > New... to create a project file, or Project > Open... to resume one.
3. Use Environments > Connect Target to connect one or more target environments.
4. Optionally use Environments > Environment Tags... to set short project labels such as DEV, UAT, or PROD.
5. Use the left strip to Reload Tables, select a table, choose attributes, configure filters, Preview, and Export.
6. Use the Snapshots strip to Pull from Dataverse, Import JSON/Excel files, Refresh snapshots, Export snapshots, and Add to Plan.
7. Add a snapshot to the execution plan to push it to a target. Source-to-target GUID mappings are saved automatically.
8. Use the execution plan History action to review past run results and errors.

Execution plan workflow (advanced)
For multi-step, multi-target migrations across environments, use the Execution Plan panel on the right.

1. Create or load a plan from the plan-level action strip.
2. Add snapshot push steps from the Snapshots strip or add file/export/import steps from the plan actions.
3. Review steps, set targets per step, validate, refresh counts when needed, then execute.
4. Use the step action strip to preview, reconfigure, execute, clone, move, or remove selected steps.
5. Plans are saved in the open project and reloaded from the same .dmtproj file.

Tips
- The top command bar is for global actions. Left-side, snapshot, and plan commands live near the data they affect.
- Snapshots store a copy of the data with its column metadata and original refresh source.
- ID mappings persist across runs: records created in a target are remembered so later pushes update them instead of creating duplicates."
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
