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
@"DATA MIGRATION TOOL — QUICK GUIDE

Project-based workflow (recommended)
A project file (.dmtproj) stores all snapshots, ID mappings, and run history in one portable file.

1. Connect to your source environment using the Connect button.
2. Use Project > New... to create a project file, or Project > Open... to resume one.
3. Select a table on the left and load its attributes.
4. Load data into the project:
     Import > Pull from Dataverse  — fetch records from the connected source environment.
     Import > Import from File  — import an existing JSON or Excel file.
5. Connect to a target environment using Additional Connections in XrmToolBox.
6. Use Project > Push Snapshot to Target to push a snapshot to the selected target.
     Source→target GUID mappings are saved automatically for subsequent runs.
7. Use Project > View Run History to review past push results and errors.

Execution plan workflow (advanced)
For multi-step, multi-target migrations across environments, use the Execution Plan panel on the right.

1. Select a table and configure its attributes and filter.
2. Use Export and Import from the toolbar menus to add steps to the plan.
3. Review steps, set targets per step, validate, then execute.
4. Plans are saved in the open project and reloaded from the same .dmtproj file.
5. Individual steps can be previewed, reconfigured, or executed in isolation.

Tips
- Settings files (.dmt.json) capture per-table attribute selections, filters, and import options.
- Snapshots in a project store a frozen copy of the data with its column type metadata.
- ID mappings persist across runs: records created in a target are remembered so subsequent pushes update them instead of creating duplicates."
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
