using System;
using System.Linq;
using System.Windows.Forms;

using Dataverse.XrmTools.DataMigrationTool.Forms;

namespace Dataverse.XrmTools.DataMigrationTool
{
    public partial class DataMigrationControl
    {
        private void InitializeEnvironmentTagsMenu()
        {
            _tsmiEnvironmentTags = new ToolStripMenuItem("Environment Tags...");
            _tsmiEnvironmentTags.Click += (s, e) => ConfigureEnvironmentTags();
            tsmiEnvironments.DropDownItems.Add(_tsmiEnvironmentTags);
        }

        private void ConfigureEnvironmentTags()
        {
            if (_project?.Service == null)
            {
                MessageBox.Show(this, "Open or create a project first.", "No Project", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            var environments = _project.Service.GetEnvironments();
            if (environments == null || !environments.Any())
            {
                MessageBox.Show(this, "No environments are stored in this project yet.", "No Environments", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            using (var dialog = new EnvironmentTagsDialog(environments))
            {
                if (dialog.ShowDialog(ParentForm) != DialogResult.OK) return;

                foreach (var environment in dialog.Environments)
                    _project.Service.SaveEnvironment(environment);
            }

            UpdateExecutionPlanTargetEnvironments();
            RenderExecutionPlanPanel();
            SendMessageToStatusBar?.Invoke(this, new XrmToolBox.Extensibility.Args.StatusBarMessageEventArgs("Environment tags saved"));
        }
    }
}
