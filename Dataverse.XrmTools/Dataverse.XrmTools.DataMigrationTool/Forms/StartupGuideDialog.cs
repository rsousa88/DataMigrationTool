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
@"This tool now works through execution plans.

Typical flow
1. Select a source table.
2. Pick the attributes and filters you want.
3. Configure an export or import operation.
4. Add the operation to the execution plan.
5. Review targets, linked steps, warnings, and preview counts.
6. Validate the plan.
7. Execute the validated plan.

Important notions
- Operations are not run immediately from the Export and Import menus. They are added to the execution plan first.
- A plan can contain one step or many steps. Even a single import/export uses the same plan workflow.
- You can connect multiple target environments and perform operations across them in one plan.
- Each plan step has its own target. For example: export accounts once, then import the linked output to DEV, then import the same linked output to TEST.
- Linked import steps depend on their export step and must stay after it.
- Validation checks the plan before execution, including missing files, disconnected target environments, linked-step order, and preview counts where possible.
- Execute is enabled only after validation succeeds.
- Plans can be saved as .dmtplan.json and loaded again later.
- Settings files capture table-specific choices such as selected attributes, filters, Excel configuration, and import options.

Tip
Use the execution plan panel on the right to change step targets, reorder steps, validate, and execute."
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
