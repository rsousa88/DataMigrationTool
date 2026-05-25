// System
using System;
using System.Drawing;
using System.IO;
using System.Windows.Forms;

// Microsoft
using Microsoft.Xrm.Tooling.Connector;

// XrmToolBox
using XrmToolBox.Extensibility.Args;

// DataMigrationTool
using Dataverse.XrmTools.DataMigrationTool.Enums;
using Dataverse.XrmTools.DataMigrationTool.Forms;
using Dataverse.XrmTools.DataMigrationTool.Logic;
using Dataverse.XrmTools.DataMigrationTool.Models;

namespace Dataverse.XrmTools.DataMigrationTool
{
    public partial class DataMigrationControl
    {
        #region Project Fields
        private ProjectContext _project;
        private ToolStripMenuItem _tsmiProject;
        private ToolStripLabel _tsmiProjectName;
        private Panel _projectMismatchBanner;
        #endregion

        #region Project Initialization

        private void InitializeProjectPanel()
        {
            _tsmiProject = new ToolStripMenuItem("Project")
            {
                Image = Properties.Resources.directory20_colorful,
                ImageScaling = ToolStripItemImageScaling.None
            };

            var newItem = new ToolStripMenuItem("New...") { Image = Properties.Resources.save16_colorful, ImageScaling = ToolStripItemImageScaling.None };
            newItem.Click += (s, e) => NewProject();
            var openItem = new ToolStripMenuItem("Open...") { Image = Properties.Resources.load16_colorful, ImageScaling = ToolStripItemImageScaling.None };
            openItem.Click += (s, e) => OpenProject();
            var closeItem = new ToolStripMenuItem("Close");
            closeItem.Click += (s, e) => CloseProject();

            _tsmiProject.DropDownItems.Add(newItem);
            _tsmiProject.DropDownItems.Add(openItem);
            _tsmiProject.DropDownItems.Add(new ToolStripSeparator());
            _tsmiProject.DropDownItems.Add(closeItem);
            _tsmiProject.DropDownItems.Add(new ToolStripSeparator());
            _tsmiProject.DropDownOpening += (s, e) => closeItem.Enabled = _project != null;

            _tsmiProjectName = new ToolStripLabel("No project open")
            {
                ForeColor = SystemColors.GrayText,
                Margin = new Padding(6, 0, 0, 0)
            };

            // Insert after tsSeparator2 (keeps Environments group together)
            int insertIdx = tsMain.Items.IndexOf(tsSeparator2);
            if (insertIdx < 0) insertIdx = tsMain.Items.Count - 2;
            tsMain.Items.Insert(insertIdx, _tsmiProject);
            tsMain.Items.Insert(insertIdx + 1, _tsmiProjectName);
            tsMain.Items.Insert(insertIdx + 2, new ToolStripSeparator());

            // Mismatch banner — sits between tsMain (Top) and pnlMain (Fill)
            _projectMismatchBanner = new Panel
            {
                Dock = DockStyle.Top,
                Height = 26,
                BackColor = Color.FromArgb(255, 243, 205),
                Visible = false
            };
            var warnLabel = new Label
            {
                Text = "⚠  Connected source does not match the project’s recorded source — pull operations are restricted.",
                Dock = DockStyle.Fill,
                TextAlign = ContentAlignment.MiddleLeft,
                ForeColor = Color.FromArgb(130, 90, 0),
                Font = new Font(SystemFonts.DefaultFont, FontStyle.Bold),
                Padding = new Padding(6, 0, 0, 0)
            };
            _projectMismatchBanner.Controls.Add(warnLabel);
            Controls.Add(_projectMismatchBanner);
            // Position banner between pnlMain (Fill, index 0) and tsMain (Top, index 1).
            // WinForms stacks Dock.Top controls highest-index-first; banner at index 1 renders below tsMain.
            Controls.SetChildIndex(_projectMismatchBanner, Controls.IndexOf(pnlMain) + 1);
        }

        #endregion

        #region Project Operations

        private void NewProject()
        {
            using (var dlg = new NewProjectDialog())
            {
                if (dlg.ShowDialog(ParentForm) != DialogResult.OK) return;

                try
                {
                    CloseProject(silent: true);

                    var svc = new SqliteProjectService();
                    svc.CreateProject(dlg.FilePath, dlg.ProjectName);

                    _project = new ProjectContext
                    {
                        FilePath = dlg.FilePath,
                        ProjectName = dlg.ProjectName,
                        Service = svc
                    };

                    BindProjectSource(_sourceClient);
                    RenderProjectBanner();
                    RenderProjectName();

                    SendMessageToStatusBar?.Invoke(this, new StatusBarMessageEventArgs($"Project created: {dlg.ProjectName}"));
                }
                catch (Exception ex)
                {
                    _logger.Log(LogLevel.ERROR, ex.Message);
                    MessageBox.Show(this, $"Failed to create project: {ex.Message}", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
            }
        }

        private void OpenProject(string filePath = null)
        {
            if (string.IsNullOrWhiteSpace(filePath))
            {
                using (var dlg = new OpenFileDialog
                {
                    Title = "Open Project",
                    Filter = "DMT Project (*.dmtproj)|*.dmtproj"
                })
                {
                    if (dlg.ShowDialog(ParentForm) != DialogResult.OK) return;
                    filePath = dlg.FileName;
                }
            }

            try
            {
                CloseProject(silent: true);

                var svc = new SqliteProjectService();
                svc.OpenProject(filePath);

                _project = new ProjectContext
                {
                    FilePath = filePath,
                    ProjectName = svc.ProjectName ?? Path.GetFileNameWithoutExtension(filePath),
                    Service = svc
                };

                BindProjectSource(_sourceClient);
                RenderProjectBanner();
                RenderProjectName();

                SendMessageToStatusBar?.Invoke(this, new StatusBarMessageEventArgs($"Project opened: {_project.ProjectName}"));
            }
            catch (Exception ex)
            {
                _logger.Log(LogLevel.ERROR, ex.Message);
                MessageBox.Show(this, $"Failed to open project: {ex.Message}", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void CloseProject(bool silent = false)
        {
            if (_project == null) return;

            var name = _project.ProjectName;
            try { _project.Service?.Dispose(); } catch { }
            _project = null;

            if (_projectMismatchBanner != null)
                _projectMismatchBanner.Visible = false;
            RenderProjectName();

            if (!silent)
                SendMessageToStatusBar?.Invoke(this, new StatusBarMessageEventArgs($"Project closed: {name}"));
        }

        private void BindProjectSource(CrmServiceClient client)
        {
            if (_project == null || client == null) return;

            var envId = client.EnvironmentId ?? client.ConnectedOrgId.ToString();
            var existing = _project.Service.GetSourceEnvironment();

            if (existing == null)
            {
                var env = new DmtProjectEnvironment
                {
                    Id = envId,
                    UniqueName = client.ConnectedOrgUniqueName,
                    FriendlyName = client.ConnectedOrgFriendlyName,
                    Url = client.CrmConnectOrgUriActual?.ToString() ?? string.Empty,
                    Role = "source"
                };
                _project.Service.SaveEnvironment(env);
                _project.SourceEnvironment = env;
                _project.IsSourceMismatch = false;
            }
            else
            {
                _project.SourceEnvironment = existing;
                _project.IsSourceMismatch = !string.Equals(existing.Id, envId, StringComparison.OrdinalIgnoreCase);
            }
        }

        private void RegisterProjectTarget(CrmServiceClient client)
        {
            if (_project == null || client == null) return;

            var envId = client.EnvironmentId ?? client.ConnectedOrgId.ToString();
            if (!_project.TargetClients.ContainsKey(envId))
                _project.TargetClients[envId] = client;
        }

        private void RenderProjectBanner()
        {
            if (_projectMismatchBanner == null) return;
            _projectMismatchBanner.Visible = _project != null && _project.IsSourceMismatch;
        }

        private void RenderProjectName()
        {
            if (_tsmiProjectName == null) return;
            if (_project == null)
            {
                _tsmiProjectName.Text = "No project open";
                _tsmiProjectName.ForeColor = SystemColors.GrayText;
            }
            else
            {
                _tsmiProjectName.Text = _project.ProjectName;
                _tsmiProjectName.ForeColor = _project.IsSourceMismatch ? Color.OrangeRed : SystemColors.ControlText;
            }
        }

        #endregion
    }
}
