namespace Dataverse.XrmTools.DataMigrationTool
{
    partial class DataMigrationControl
    {
        /// <summary> 
        /// Variable nécessaire au concepteur.
        /// </summary>
        private System.ComponentModel.IContainer components = null;

        /// <summary> 
        /// Nettoyage des ressources utilisées.
        /// </summary>
        /// <param name="disposing">true si les ressources managées doivent être supprimées ; sinon, false.</param>
        protected override void Dispose(bool disposing)
        {
            if (disposing && (components != null))
            {
                components.Dispose();
            }
            base.Dispose(disposing);
        }

        #region Code généré par le Concepteur de composants
        private void InitializeComponent()
        {
            System.ComponentModel.ComponentResourceManager resources = new System.ComponentModel.ComponentResourceManager(typeof(DataMigrationControl));
            this.tsMain = new System.Windows.Forms.ToolStrip();
            this.tsbRefreshTables = new System.Windows.Forms.ToolStripButton();
            this.tsSeparator1 = new System.Windows.Forms.ToolStripSeparator();
            this.tsbPreview = new System.Windows.Forms.ToolStripButton();
            this.tsbExport = new System.Windows.Forms.ToolStripButton();
            this.tsbImport = new System.Windows.Forms.ToolStripButton();
            this.tsbAbort = new System.Windows.Forms.ToolStripButton();
            this.pnlHeader = new System.Windows.Forms.Panel();
            this.gbImportExport = new System.Windows.Forms.GroupBox();
            this.txtExportDirPath = new System.Windows.Forms.TextBox();
            this.lblExportDir = new System.Windows.Forms.Label();
            this.btnSelectExportDir = new System.Windows.Forms.Button();
            this.gbOrgSettings = new System.Windows.Forms.GroupBox();
            this.cbMapUsers = new System.Windows.Forms.CheckBox();
            this.cbMapTeams = new System.Windows.Forms.CheckBox();
            this.btnMappings = new System.Windows.Forms.Button();
            this.cbMapBu = new System.Windows.Forms.CheckBox();
            this.gbOpSettings = new System.Windows.Forms.GroupBox();
            this.nudBatchCount = new System.Windows.Forms.NumericUpDown();
            this.lblBatchCount = new System.Windows.Forms.Label();
            this.cbUpdate = new System.Windows.Forms.CheckBox();
            this.cbDelete = new System.Windows.Forms.CheckBox();
            this.cbCreate = new System.Windows.Forms.CheckBox();
            this.gbEnvironments = new System.Windows.Forms.GroupBox();
            this.lblTarget = new System.Windows.Forms.Label();
            this.lblSourceValue = new System.Windows.Forms.Label();
            this.lblSource = new System.Windows.Forms.Label();
            this.btnSelectTarget = new System.Windows.Forms.Button();
            this.lblTargetValue = new System.Windows.Forms.Label();
            this.pnlBody = new System.Windows.Forms.TableLayoutPanel();
            this.gbAttributes = new System.Windows.Forms.GroupBox();
            this.btnLoadTableSettings = new System.Windows.Forms.Button();
            this.btnSaveTableSettings = new System.Windows.Forms.Button();
            this.btnTableFilters = new System.Windows.Forms.Button();
            this.cbSelectAll = new System.Windows.Forms.CheckBox();
            this.lvAttributes = new System.Windows.Forms.ListView();
            this.chAttrDisplayName = ((System.Windows.Forms.ColumnHeader)(new System.Windows.Forms.ColumnHeader()));
            this.chAttrLogicalName = ((System.Windows.Forms.ColumnHeader)(new System.Windows.Forms.ColumnHeader()));
            this.chAttrType = ((System.Windows.Forms.ColumnHeader)(new System.Windows.Forms.ColumnHeader()));
            this.chAttrDescription = ((System.Windows.Forms.ColumnHeader)(new System.Windows.Forms.ColumnHeader()));
            this.gbTables = new System.Windows.Forms.GroupBox();
            this.txtTableFilter = new System.Windows.Forms.TextBox();
            this.lblTableFilter = new System.Windows.Forms.Label();
            this.lvTables = new System.Windows.Forms.ListView();
            this.chTblDisplayName = ((System.Windows.Forms.ColumnHeader)(new System.Windows.Forms.ColumnHeader()));
            this.chTblLogicalName = ((System.Windows.Forms.ColumnHeader)(new System.Windows.Forms.ColumnHeader()));
            this.chTblDescription = ((System.Windows.Forms.ColumnHeader)(new System.Windows.Forms.ColumnHeader()));
            this.tsMain.SuspendLayout();
            this.pnlHeader.SuspendLayout();
            this.gbImportExport.SuspendLayout();
            this.gbOrgSettings.SuspendLayout();
            this.gbOpSettings.SuspendLayout();
            ((System.ComponentModel.ISupportInitialize)(this.nudBatchCount)).BeginInit();
            this.gbEnvironments.SuspendLayout();
            this.pnlBody.SuspendLayout();
            this.gbAttributes.SuspendLayout();
            this.gbTables.SuspendLayout();
            this.SuspendLayout();
            // 
            // tsMain
            // 
            this.tsMain.AutoSize = false;
            this.tsMain.ImageScalingSize = new System.Drawing.Size(20, 20);
            this.tsMain.Items.AddRange(new System.Windows.Forms.ToolStripItem[] {
            this.tsbRefreshTables,
            this.tsSeparator1,
            this.tsbPreview,
            this.tsbExport,
            this.tsbImport,
            this.tsbAbort});
            this.tsMain.Location = new System.Drawing.Point(0, 0);
            this.tsMain.Name = "tsMain";
            this.tsMain.Size = new System.Drawing.Size(1610, 25);
            this.tsMain.TabIndex = 90;
            this.tsMain.Text = "toolStrip1";
            // 
            // tsbRefreshTables
            // 
            this.tsbRefreshTables.Font = new System.Drawing.Font("Segoe UI", 8.25F);
            this.tsbRefreshTables.Image = global::Dataverse.XrmTools.DataMigrationTool.Properties.Resources.database;
            this.tsbRefreshTables.ImageTransparentColor = System.Drawing.Color.Magenta;
            this.tsbRefreshTables.Name = "tsbRefreshTables";
            this.tsbRefreshTables.Size = new System.Drawing.Size(91, 22);
            this.tsbRefreshTables.Text = "Load Tables";
            this.tsbRefreshTables.Click += new System.EventHandler(this.tsbRefreshTables_Click);
            // 
            // tsSeparator1
            // 
            this.tsSeparator1.Name = "tsSeparator1";
            this.tsSeparator1.Size = new System.Drawing.Size(6, 25);
            // 
            // tsbPreview
            // 
            this.tsbPreview.Image = global::Dataverse.XrmTools.DataMigrationTool.Properties.Resources.preview;
            this.tsbPreview.ImageTransparentColor = System.Drawing.Color.Magenta;
            this.tsbPreview.Name = "tsbPreview";
            this.tsbPreview.Size = new System.Drawing.Size(72, 22);
            this.tsbPreview.Text = "Preview";
            this.tsbPreview.Visible = false;
            this.tsbPreview.Click += new System.EventHandler(this.tsbPreview_Click);
            // 
            // tsbExport
            // 
            this.tsbExport.Image = ((System.Drawing.Image)(resources.GetObject("tsbExport.Image")));
            this.tsbExport.ImageTransparentColor = System.Drawing.Color.Magenta;
            this.tsbExport.Name = "tsbExport";
            this.tsbExport.Size = new System.Drawing.Size(65, 22);
            this.tsbExport.Text = "Export";
            this.tsbExport.Visible = false;
            this.tsbExport.Click += new System.EventHandler(this.tsbExport_Click);
            // 
            // tsbImport
            // 
            this.tsbImport.Image = global::Dataverse.XrmTools.DataMigrationTool.Properties.Resources.import;
            this.tsbImport.ImageTransparentColor = System.Drawing.Color.Magenta;
            this.tsbImport.Name = "tsbImport";
            this.tsbImport.Size = new System.Drawing.Size(67, 22);
            this.tsbImport.Text = "Import";
            this.tsbImport.Visible = false;
            this.tsbImport.Click += new System.EventHandler(this.tsbImport_Click);
            // 
            // tsbAbort
            // 
            this.tsbAbort.DisplayStyle = System.Windows.Forms.ToolStripItemDisplayStyle.Text;
            this.tsbAbort.Image = ((System.Drawing.Image)(resources.GetObject("tsbAbort.Image")));
            this.tsbAbort.ImageTransparentColor = System.Drawing.Color.Magenta;
            this.tsbAbort.Name = "tsbAbort";
            this.tsbAbort.Size = new System.Drawing.Size(41, 22);
            this.tsbAbort.Text = "Abort";
            this.tsbAbort.Visible = false;
            this.tsbAbort.Click += new System.EventHandler(this.tsbAbort_Click);
            // 
            // pnlHeader
            // 
            this.pnlHeader.Controls.Add(this.gbImportExport);
            this.pnlHeader.Controls.Add(this.gbOrgSettings);
            this.pnlHeader.Controls.Add(this.gbOpSettings);
            this.pnlHeader.Controls.Add(this.gbEnvironments);
            this.pnlHeader.Dock = System.Windows.Forms.DockStyle.Top;
            this.pnlHeader.Location = new System.Drawing.Point(0, 25);
            this.pnlHeader.Name = "pnlHeader";
            this.pnlHeader.Size = new System.Drawing.Size(1610, 100);
            this.pnlHeader.TabIndex = 103;
            // 
            // gbImportExport
            // 
            this.gbImportExport.Anchor = ((System.Windows.Forms.AnchorStyles)(((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Left) 
            | System.Windows.Forms.AnchorStyles.Right)));
            this.gbImportExport.Controls.Add(this.txtExportDirPath);
            this.gbImportExport.Controls.Add(this.lblExportDir);
            this.gbImportExport.Controls.Add(this.btnSelectExportDir);
            this.gbImportExport.Enabled = false;
            this.gbImportExport.Location = new System.Drawing.Point(302, 4);
            this.gbImportExport.MinimumSize = new System.Drawing.Size(250, 0);
            this.gbImportExport.Name = "gbImportExport";
            this.gbImportExport.Size = new System.Drawing.Size(732, 94);
            this.gbImportExport.TabIndex = 102;
            this.gbImportExport.TabStop = false;
            this.gbImportExport.Text = "Import/Export";
            // 
            // txtExportDirPath
            // 
            this.txtExportDirPath.Anchor = ((System.Windows.Forms.AnchorStyles)(((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Left) 
            | System.Windows.Forms.AnchorStyles.Right)));
            this.txtExportDirPath.Location = new System.Drawing.Point(97, 21);
            this.txtExportDirPath.Name = "txtExportDirPath";
            this.txtExportDirPath.Size = new System.Drawing.Size(629, 20);
            this.txtExportDirPath.TabIndex = 101;
            this.txtExportDirPath.Validating += new System.ComponentModel.CancelEventHandler(this.txtExportDirPath_Validating);
            // 
            // lblExportDir
            // 
            this.lblExportDir.AutoSize = true;
            this.lblExportDir.Location = new System.Drawing.Point(6, 24);
            this.lblExportDir.Name = "lblExportDir";
            this.lblExportDir.Size = new System.Drawing.Size(82, 13);
            this.lblExportDir.TabIndex = 100;
            this.lblExportDir.Text = "Export Directory";
            // 
            // btnSelectExportDir
            // 
            this.btnSelectExportDir.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Right)));
            this.btnSelectExportDir.FlatAppearance.BorderSize = 0;
            this.btnSelectExportDir.FlatStyle = System.Windows.Forms.FlatStyle.Popup;
            this.btnSelectExportDir.Image = ((System.Drawing.Image)(resources.GetObject("btnSelectExportDir.Image")));
            this.btnSelectExportDir.ImageAlign = System.Drawing.ContentAlignment.BottomLeft;
            this.btnSelectExportDir.Location = new System.Drawing.Point(586, 57);
            this.btnSelectExportDir.Name = "btnSelectExportDir";
            this.btnSelectExportDir.Size = new System.Drawing.Size(140, 28);
            this.btnSelectExportDir.TabIndex = 99;
            this.btnSelectExportDir.Text = "Select Directory";
            this.btnSelectExportDir.UseVisualStyleBackColor = true;
            this.btnSelectExportDir.Click += new System.EventHandler(this.btnSelectExportDir_Click);
            // 
            // gbOrgSettings
            // 
            this.gbOrgSettings.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Right)));
            this.gbOrgSettings.Controls.Add(this.cbMapUsers);
            this.gbOrgSettings.Controls.Add(this.cbMapTeams);
            this.gbOrgSettings.Controls.Add(this.btnMappings);
            this.gbOrgSettings.Controls.Add(this.cbMapBu);
            this.gbOrgSettings.Enabled = false;
            this.gbOrgSettings.Location = new System.Drawing.Point(1040, 4);
            this.gbOrgSettings.Name = "gbOrgSettings";
            this.gbOrgSettings.Size = new System.Drawing.Size(361, 94);
            this.gbOrgSettings.TabIndex = 103;
            this.gbOrgSettings.TabStop = false;
            this.gbOrgSettings.Text = "Organization Settings";
            // 
            // cbMapUsers
            // 
            this.cbMapUsers.AutoSize = true;
            this.cbMapUsers.Location = new System.Drawing.Point(6, 21);
            this.cbMapUsers.Name = "cbMapUsers";
            this.cbMapUsers.Size = new System.Drawing.Size(177, 17);
            this.cbMapUsers.TabIndex = 3;
            this.cbMapUsers.Text = "Map Users by User Name (slow)";
            this.cbMapUsers.UseVisualStyleBackColor = true;
            // 
            // cbMapTeams
            // 
            this.cbMapTeams.AutoSize = true;
            this.cbMapTeams.Location = new System.Drawing.Point(6, 44);
            this.cbMapTeams.Name = "cbMapTeams";
            this.cbMapTeams.Size = new System.Drawing.Size(157, 17);
            this.cbMapTeams.TabIndex = 2;
            this.cbMapTeams.Text = "Map Teams by Name (slow)";
            this.cbMapTeams.UseVisualStyleBackColor = true;
            // 
            // btnMappings
            // 
            this.btnMappings.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Right)));
            this.btnMappings.FlatAppearance.BorderSize = 0;
            this.btnMappings.FlatStyle = System.Windows.Forms.FlatStyle.Popup;
            this.btnMappings.ForeColor = System.Drawing.SystemColors.ControlText;
            this.btnMappings.Image = global::Dataverse.XrmTools.DataMigrationTool.Properties.Resources.mapping20_colorful;
            this.btnMappings.ImageAlign = System.Drawing.ContentAlignment.MiddleLeft;
            this.btnMappings.Location = new System.Drawing.Point(214, 57);
            this.btnMappings.Name = "btnMappings";
            this.btnMappings.Size = new System.Drawing.Size(140, 28);
            this.btnMappings.TabIndex = 102;
            this.btnMappings.Text = "Mappings";
            this.btnMappings.UseVisualStyleBackColor = true;
            this.btnMappings.Click += new System.EventHandler(this.btnMappings_Click);
            // 
            // cbMapBu
            // 
            this.cbMapBu.AutoSize = true;
            this.cbMapBu.Checked = true;
            this.cbMapBu.CheckState = System.Windows.Forms.CheckState.Checked;
            this.cbMapBu.Location = new System.Drawing.Point(6, 67);
            this.cbMapBu.Name = "cbMapBu";
            this.cbMapBu.Size = new System.Drawing.Size(140, 17);
            this.cbMapBu.TabIndex = 0;
            this.cbMapBu.Text = "Map Root Business Unit";
            this.cbMapBu.UseVisualStyleBackColor = true;
            // 
            // gbOpSettings
            // 
            this.gbOpSettings.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Right)));
            this.gbOpSettings.Controls.Add(this.nudBatchCount);
            this.gbOpSettings.Controls.Add(this.lblBatchCount);
            this.gbOpSettings.Controls.Add(this.cbUpdate);
            this.gbOpSettings.Controls.Add(this.cbDelete);
            this.gbOpSettings.Controls.Add(this.cbCreate);
            this.gbOpSettings.Enabled = false;
            this.gbOpSettings.Location = new System.Drawing.Point(1407, 4);
            this.gbOpSettings.Name = "gbOpSettings";
            this.gbOpSettings.Size = new System.Drawing.Size(190, 94);
            this.gbOpSettings.TabIndex = 102;
            this.gbOpSettings.TabStop = false;
            this.gbOpSettings.Text = "Settings";
            // 
            // nudBatchCount
            // 
            this.nudBatchCount.Increment = new decimal(new int[] {
            100,
            0,
            0,
            0});
            this.nudBatchCount.Location = new System.Drawing.Point(106, 66);
            this.nudBatchCount.Margin = new System.Windows.Forms.Padding(2);
            this.nudBatchCount.Maximum = new decimal(new int[] {
            10000,
            0,
            0,
            0});
            this.nudBatchCount.Minimum = new decimal(new int[] {
            100,
            0,
            0,
            0});
            this.nudBatchCount.Name = "nudBatchCount";
            this.nudBatchCount.Size = new System.Drawing.Size(76, 20);
            this.nudBatchCount.TabIndex = 5;
            this.nudBatchCount.Value = new decimal(new int[] {
            250,
            0,
            0,
            0});
            // 
            // lblBatchCount
            // 
            this.lblBatchCount.AutoSize = true;
            this.lblBatchCount.Location = new System.Drawing.Point(5, 67);
            this.lblBatchCount.Margin = new System.Windows.Forms.Padding(2, 0, 2, 0);
            this.lblBatchCount.Name = "lblBatchCount";
            this.lblBatchCount.Size = new System.Drawing.Size(59, 13);
            this.lblBatchCount.TabIndex = 4;
            this.lblBatchCount.Text = "Batch size:";
            // 
            // cbUpdate
            // 
            this.cbUpdate.AutoSize = true;
            this.cbUpdate.Checked = true;
            this.cbUpdate.CheckState = System.Windows.Forms.CheckState.Checked;
            this.cbUpdate.Location = new System.Drawing.Point(66, 21);
            this.cbUpdate.Name = "cbUpdate";
            this.cbUpdate.Size = new System.Drawing.Size(61, 17);
            this.cbUpdate.TabIndex = 2;
            this.cbUpdate.Text = "Update";
            this.cbUpdate.UseVisualStyleBackColor = true;
            // 
            // cbDelete
            // 
            this.cbDelete.AutoSize = true;
            this.cbDelete.Location = new System.Drawing.Point(129, 21);
            this.cbDelete.Name = "cbDelete";
            this.cbDelete.Size = new System.Drawing.Size(57, 17);
            this.cbDelete.TabIndex = 1;
            this.cbDelete.Text = "Delete";
            this.cbDelete.UseVisualStyleBackColor = true;
            // 
            // cbCreate
            // 
            this.cbCreate.AutoSize = true;
            this.cbCreate.Checked = true;
            this.cbCreate.CheckState = System.Windows.Forms.CheckState.Checked;
            this.cbCreate.Location = new System.Drawing.Point(6, 21);
            this.cbCreate.Name = "cbCreate";
            this.cbCreate.Size = new System.Drawing.Size(57, 17);
            this.cbCreate.TabIndex = 0;
            this.cbCreate.Text = "Create";
            this.cbCreate.UseVisualStyleBackColor = true;
            // 
            // gbEnvironments
            // 
            this.gbEnvironments.Controls.Add(this.lblTarget);
            this.gbEnvironments.Controls.Add(this.lblSourceValue);
            this.gbEnvironments.Controls.Add(this.lblSource);
            this.gbEnvironments.Controls.Add(this.btnSelectTarget);
            this.gbEnvironments.Controls.Add(this.lblTargetValue);
            this.gbEnvironments.Location = new System.Drawing.Point(3, 4);
            this.gbEnvironments.Name = "gbEnvironments";
            this.gbEnvironments.Size = new System.Drawing.Size(290, 94);
            this.gbEnvironments.TabIndex = 101;
            this.gbEnvironments.TabStop = false;
            this.gbEnvironments.Text = "Environments";
            // 
            // lblTarget
            // 
            this.lblTarget.AutoSize = true;
            this.lblTarget.Location = new System.Drawing.Point(6, 53);
            this.lblTarget.Name = "lblTarget";
            this.lblTarget.Size = new System.Drawing.Size(38, 13);
            this.lblTarget.TabIndex = 101;
            this.lblTarget.Text = "Target";
            // 
            // lblSourceValue
            // 
            this.lblSourceValue.Anchor = ((System.Windows.Forms.AnchorStyles)(((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Left) 
            | System.Windows.Forms.AnchorStyles.Right)));
            this.lblSourceValue.AutoSize = true;
            this.lblSourceValue.Font = new System.Drawing.Font("Segoe UI", 8.25F);
            this.lblSourceValue.ForeColor = System.Drawing.Color.DarkRed;
            this.lblSourceValue.Location = new System.Drawing.Point(83, 24);
            this.lblSourceValue.Name = "lblSourceValue";
            this.lblSourceValue.Size = new System.Drawing.Size(77, 13);
            this.lblSourceValue.TabIndex = 97;
            this.lblSourceValue.Text = "Disconnected";
            // 
            // lblSource
            // 
            this.lblSource.AutoSize = true;
            this.lblSource.Location = new System.Drawing.Point(6, 24);
            this.lblSource.Name = "lblSource";
            this.lblSource.Size = new System.Drawing.Size(41, 13);
            this.lblSource.TabIndex = 100;
            this.lblSource.Text = "Source";
            // 
            // btnSelectTarget
            // 
            this.btnSelectTarget.Enabled = false;
            this.btnSelectTarget.FlatAppearance.BorderSize = 0;
            this.btnSelectTarget.FlatStyle = System.Windows.Forms.FlatStyle.Flat;
            this.btnSelectTarget.Image = global::Dataverse.XrmTools.DataMigrationTool.Properties.Resources.connect16_colorful;
            this.btnSelectTarget.Location = new System.Drawing.Point(53, 48);
            this.btnSelectTarget.Name = "btnSelectTarget";
            this.btnSelectTarget.Size = new System.Drawing.Size(24, 24);
            this.btnSelectTarget.TabIndex = 99;
            this.btnSelectTarget.UseVisualStyleBackColor = true;
            this.btnSelectTarget.Click += new System.EventHandler(this.btnSelectTarget_Click);
            // 
            // lblTargetValue
            // 
            this.lblTargetValue.Anchor = ((System.Windows.Forms.AnchorStyles)(((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Left) 
            | System.Windows.Forms.AnchorStyles.Right)));
            this.lblTargetValue.AutoSize = true;
            this.lblTargetValue.Font = new System.Drawing.Font("Segoe UI", 8.25F);
            this.lblTargetValue.ForeColor = System.Drawing.Color.DarkRed;
            this.lblTargetValue.Location = new System.Drawing.Point(83, 53);
            this.lblTargetValue.Name = "lblTargetValue";
            this.lblTargetValue.Size = new System.Drawing.Size(77, 13);
            this.lblTargetValue.TabIndex = 98;
            this.lblTargetValue.Text = "Disconnected";
            // 
            // pnlBody
            // 
            this.pnlBody.ColumnCount = 2;
            this.pnlBody.ColumnStyles.Add(new System.Windows.Forms.ColumnStyle(System.Windows.Forms.SizeType.Percent, 45F));
            this.pnlBody.ColumnStyles.Add(new System.Windows.Forms.ColumnStyle(System.Windows.Forms.SizeType.Percent, 55F));
            this.pnlBody.Controls.Add(this.gbAttributes, 1, 0);
            this.pnlBody.Controls.Add(this.gbTables, 0, 0);
            this.pnlBody.Dock = System.Windows.Forms.DockStyle.Fill;
            this.pnlBody.Location = new System.Drawing.Point(0, 125);
            this.pnlBody.Name = "pnlBody";
            this.pnlBody.RowCount = 1;
            this.pnlBody.RowStyles.Add(new System.Windows.Forms.RowStyle(System.Windows.Forms.SizeType.Percent, 50F));
            this.pnlBody.RowStyles.Add(new System.Windows.Forms.RowStyle(System.Windows.Forms.SizeType.Percent, 50F));
            this.pnlBody.Size = new System.Drawing.Size(1610, 663);
            this.pnlBody.TabIndex = 104;
            // 
            // gbAttributes
            // 
            this.gbAttributes.Controls.Add(this.btnLoadTableSettings);
            this.gbAttributes.Controls.Add(this.btnSaveTableSettings);
            this.gbAttributes.Controls.Add(this.btnTableFilters);
            this.gbAttributes.Controls.Add(this.cbSelectAll);
            this.gbAttributes.Controls.Add(this.lvAttributes);
            this.gbAttributes.Dock = System.Windows.Forms.DockStyle.Fill;
            this.gbAttributes.Font = new System.Drawing.Font("Segoe UI", 8.25F);
            this.gbAttributes.Location = new System.Drawing.Point(727, 3);
            this.gbAttributes.Name = "gbAttributes";
            this.gbAttributes.Size = new System.Drawing.Size(880, 657);
            this.gbAttributes.TabIndex = 92;
            this.gbAttributes.TabStop = false;
            this.gbAttributes.Text = "Attributes";
            this.gbAttributes.Visible = false;
            // 
            // btnLoadTableSettings
            // 
            this.btnLoadTableSettings.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Right)));
            this.btnLoadTableSettings.FlatAppearance.BorderSize = 0;
            this.btnLoadTableSettings.FlatStyle = System.Windows.Forms.FlatStyle.Popup;
            this.btnLoadTableSettings.Image = ((System.Drawing.Image)(resources.GetObject("btnLoadTableSettings.Image")));
            this.btnLoadTableSettings.ImageAlign = System.Drawing.ContentAlignment.MiddleLeft;
            this.btnLoadTableSettings.Location = new System.Drawing.Point(734, 12);
            this.btnLoadTableSettings.Name = "btnLoadTableSettings";
            this.btnLoadTableSettings.Size = new System.Drawing.Size(140, 28);
            this.btnLoadTableSettings.TabIndex = 104;
            this.btnLoadTableSettings.Text = "Load Settings";
            this.btnLoadTableSettings.UseVisualStyleBackColor = true;
            this.btnLoadTableSettings.Click += new System.EventHandler(this.btnImportTableSettings_Click);
            // 
            // btnSaveTableSettings
            // 
            this.btnSaveTableSettings.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Right)));
            this.btnSaveTableSettings.FlatAppearance.BorderSize = 0;
            this.btnSaveTableSettings.FlatStyle = System.Windows.Forms.FlatStyle.Popup;
            this.btnSaveTableSettings.Image = global::Dataverse.XrmTools.DataMigrationTool.Properties.Resources.save16_colorful;
            this.btnSaveTableSettings.ImageAlign = System.Drawing.ContentAlignment.MiddleLeft;
            this.btnSaveTableSettings.Location = new System.Drawing.Point(588, 12);
            this.btnSaveTableSettings.Name = "btnSaveTableSettings";
            this.btnSaveTableSettings.Size = new System.Drawing.Size(140, 28);
            this.btnSaveTableSettings.TabIndex = 103;
            this.btnSaveTableSettings.Text = "Save Settings";
            this.btnSaveTableSettings.UseVisualStyleBackColor = true;
            this.btnSaveTableSettings.Click += new System.EventHandler(this.btnExportTableSettings_Click);
            // 
            // btnTableFilters
            // 
            this.btnTableFilters.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Right)));
            this.btnTableFilters.FlatAppearance.BorderSize = 0;
            this.btnTableFilters.FlatStyle = System.Windows.Forms.FlatStyle.Popup;
            this.btnTableFilters.Image = global::Dataverse.XrmTools.DataMigrationTool.Properties.Resources.filters20_colorful;
            this.btnTableFilters.ImageAlign = System.Drawing.ContentAlignment.MiddleLeft;
            this.btnTableFilters.Location = new System.Drawing.Point(442, 12);
            this.btnTableFilters.Name = "btnTableFilters";
            this.btnTableFilters.Size = new System.Drawing.Size(140, 28);
            this.btnTableFilters.TabIndex = 101;
            this.btnTableFilters.Text = "Set Filters";
            this.btnTableFilters.UseVisualStyleBackColor = true;
            this.btnTableFilters.Click += new System.EventHandler(this.btnFilter_Click);
            // 
            // cbSelectAll
            // 
            this.cbSelectAll.AutoSize = true;
            this.cbSelectAll.Checked = true;
            this.cbSelectAll.CheckState = System.Windows.Forms.CheckState.Checked;
            this.cbSelectAll.Location = new System.Drawing.Point(6, 21);
            this.cbSelectAll.Name = "cbSelectAll";
            this.cbSelectAll.Size = new System.Drawing.Size(120, 17);
            this.cbSelectAll.TabIndex = 3;
            this.cbSelectAll.Text = "Select/Unselect All";
            this.cbSelectAll.UseVisualStyleBackColor = true;
            this.cbSelectAll.CheckedChanged += new System.EventHandler(this.cbAllAttributes_CheckedChanged);
            // 
            // lvAttributes
            // 
            this.lvAttributes.Anchor = ((System.Windows.Forms.AnchorStyles)((((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Bottom) 
            | System.Windows.Forms.AnchorStyles.Left) 
            | System.Windows.Forms.AnchorStyles.Right)));
            this.lvAttributes.CheckBoxes = true;
            this.lvAttributes.Columns.AddRange(new System.Windows.Forms.ColumnHeader[] {
            this.chAttrDisplayName,
            this.chAttrLogicalName,
            this.chAttrType,
            this.chAttrDescription});
            this.lvAttributes.FullRowSelect = true;
            this.lvAttributes.HideSelection = false;
            this.lvAttributes.Location = new System.Drawing.Point(6, 44);
            this.lvAttributes.Name = "lvAttributes";
            this.lvAttributes.Size = new System.Drawing.Size(868, 607);
            this.lvAttributes.Sorting = System.Windows.Forms.SortOrder.Ascending;
            this.lvAttributes.TabIndex = 64;
            this.lvAttributes.UseCompatibleStateImageBehavior = false;
            this.lvAttributes.View = System.Windows.Forms.View.Details;
            this.lvAttributes.ColumnClick += new System.Windows.Forms.ColumnClickEventHandler(this.listView_ColumnClick);
            this.lvAttributes.ItemChecked += new System.Windows.Forms.ItemCheckedEventHandler(this.lvAttributes_ItemChecked);
            // 
            // chAttrDisplayName
            // 
            this.chAttrDisplayName.Text = "Display Name";
            this.chAttrDisplayName.Width = 200;
            // 
            // chAttrLogicalName
            // 
            this.chAttrLogicalName.Text = "Logical Name";
            this.chAttrLogicalName.Width = 200;
            // 
            // chAttrType
            // 
            this.chAttrType.Text = "Type";
            this.chAttrType.Width = 160;
            // 
            // chAttrDescription
            // 
            this.chAttrDescription.Text = "Description";
            this.chAttrDescription.Width = 300;
            // 
            // gbTables
            // 
            this.gbTables.Controls.Add(this.txtTableFilter);
            this.gbTables.Controls.Add(this.lblTableFilter);
            this.gbTables.Controls.Add(this.lvTables);
            this.gbTables.Dock = System.Windows.Forms.DockStyle.Fill;
            this.gbTables.Location = new System.Drawing.Point(3, 3);
            this.gbTables.Name = "gbTables";
            this.gbTables.Size = new System.Drawing.Size(718, 657);
            this.gbTables.TabIndex = 93;
            this.gbTables.TabStop = false;
            this.gbTables.Text = "Tables";
            this.gbTables.Visible = false;
            // 
            // txtTableFilter
            // 
            this.txtTableFilter.Anchor = ((System.Windows.Forms.AnchorStyles)(((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Left) 
            | System.Windows.Forms.AnchorStyles.Right)));
            this.txtTableFilter.Location = new System.Drawing.Point(48, 17);
            this.txtTableFilter.Name = "txtTableFilter";
            this.txtTableFilter.Size = new System.Drawing.Size(665, 20);
            this.txtTableFilter.TabIndex = 66;
            this.txtTableFilter.TextChanged += new System.EventHandler(this.txtTableFilter_TextChanged);
            // 
            // lblTableFilter
            // 
            this.lblTableFilter.AutoSize = true;
            this.lblTableFilter.Location = new System.Drawing.Point(6, 22);
            this.lblTableFilter.Name = "lblTableFilter";
            this.lblTableFilter.Size = new System.Drawing.Size(32, 13);
            this.lblTableFilter.TabIndex = 65;
            this.lblTableFilter.Text = "Filter:";
            // 
            // lvTables
            // 
            this.lvTables.Anchor = ((System.Windows.Forms.AnchorStyles)((((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Bottom) 
            | System.Windows.Forms.AnchorStyles.Left) 
            | System.Windows.Forms.AnchorStyles.Right)));
            this.lvTables.Columns.AddRange(new System.Windows.Forms.ColumnHeader[] {
            this.chTblDisplayName,
            this.chTblLogicalName,
            this.chTblDescription});
            this.lvTables.FullRowSelect = true;
            this.lvTables.HideSelection = false;
            this.lvTables.Location = new System.Drawing.Point(7, 44);
            this.lvTables.MultiSelect = false;
            this.lvTables.Name = "lvTables";
            this.lvTables.Size = new System.Drawing.Size(706, 607);
            this.lvTables.Sorting = System.Windows.Forms.SortOrder.Ascending;
            this.lvTables.TabIndex = 64;
            this.lvTables.UseCompatibleStateImageBehavior = false;
            this.lvTables.View = System.Windows.Forms.View.Details;
            this.lvTables.ColumnClick += new System.Windows.Forms.ColumnClickEventHandler(this.listView_ColumnClick);
            this.lvTables.SelectedIndexChanged += new System.EventHandler(this.lvTables_SelectedIndexChanged);
            // 
            // chTblDisplayName
            // 
            this.chTblDisplayName.Text = "Display Name";
            this.chTblDisplayName.Width = 200;
            // 
            // chTblLogicalName
            // 
            this.chTblLogicalName.Text = "Logical Name";
            this.chTblLogicalName.Width = 200;
            // 
            // chTblDescription
            // 
            this.chTblDescription.Text = "Description";
            this.chTblDescription.Width = 300;
            // 
            // DataMigrationControl
            // 
            this.AutoScaleDimensions = new System.Drawing.SizeF(6F, 13F);
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.BackColor = System.Drawing.SystemColors.Window;
            this.Controls.Add(this.pnlBody);
            this.Controls.Add(this.pnlHeader);
            this.Controls.Add(this.tsMain);
            this.Name = "DataMigrationControl";
            this.Size = new System.Drawing.Size(1610, 788);
            this.Load += new System.EventHandler(this.DataMigrationControl_Load);
            this.tsMain.ResumeLayout(false);
            this.tsMain.PerformLayout();
            this.pnlHeader.ResumeLayout(false);
            this.gbImportExport.ResumeLayout(false);
            this.gbImportExport.PerformLayout();
            this.gbOrgSettings.ResumeLayout(false);
            this.gbOrgSettings.PerformLayout();
            this.gbOpSettings.ResumeLayout(false);
            this.gbOpSettings.PerformLayout();
            ((System.ComponentModel.ISupportInitialize)(this.nudBatchCount)).EndInit();
            this.gbEnvironments.ResumeLayout(false);
            this.gbEnvironments.PerformLayout();
            this.pnlBody.ResumeLayout(false);
            this.gbAttributes.ResumeLayout(false);
            this.gbAttributes.PerformLayout();
            this.gbTables.ResumeLayout(false);
            this.gbTables.PerformLayout();
            this.ResumeLayout(false);

        }
        #endregion

        // Main Tool Strip
        private System.Windows.Forms.ToolStrip tsMain;
        private System.Windows.Forms.ToolStripButton tsbRefreshTables;
        private System.Windows.Forms.ToolStripSeparator tsSeparator1;
        private System.Windows.Forms.ToolStripButton tsbPreview;
        private System.Windows.Forms.ToolStripButton tsbExport;
        private System.Windows.Forms.ToolStripButton tsbImport;
        private System.Windows.Forms.ToolStripButton tsbAbort;

        // Header
        private System.Windows.Forms.Panel pnlHeader;

        // Environments Group
        private System.Windows.Forms.GroupBox gbEnvironments;
        private System.Windows.Forms.Label lblSource;
        private System.Windows.Forms.Label lblSourceValue;
        private System.Windows.Forms.Label lblTarget;
        private System.Windows.Forms.Button btnSelectTarget;
        private System.Windows.Forms.Label lblTargetValue;

        // Import/Export Group
        private System.Windows.Forms.GroupBox gbImportExport;
        private System.Windows.Forms.Label lblExportDir;
        private System.Windows.Forms.TextBox txtExportDirPath;
        private System.Windows.Forms.Button btnSelectExportDir;

        // Organization Settings Group
        private System.Windows.Forms.GroupBox gbOrgSettings;
        private System.Windows.Forms.CheckBox cbMapUsers;
        private System.Windows.Forms.CheckBox cbMapTeams;
        private System.Windows.Forms.CheckBox cbMapBu;
        private System.Windows.Forms.Button btnMappings;

        // Operation Settings Group
        private System.Windows.Forms.GroupBox gbOpSettings;
        private System.Windows.Forms.CheckBox cbCreate;
        private System.Windows.Forms.CheckBox cbUpdate;
        private System.Windows.Forms.CheckBox cbDelete;
        private System.Windows.Forms.Label lblBatchCount;
        private System.Windows.Forms.NumericUpDown nudBatchCount;

        // Body
        private System.Windows.Forms.TableLayoutPanel pnlBody;

        // Tables Group
        private System.Windows.Forms.GroupBox gbTables;
        private System.Windows.Forms.Label lblTableFilter;
        private System.Windows.Forms.TextBox txtTableFilter;
        private System.Windows.Forms.ListView lvTables;
        private System.Windows.Forms.ColumnHeader chTblDisplayName;
        private System.Windows.Forms.ColumnHeader chTblLogicalName;
        private System.Windows.Forms.ColumnHeader chTblDescription;

        // Attributes Group
        private System.Windows.Forms.GroupBox gbAttributes;
        private System.Windows.Forms.CheckBox cbSelectAll;
        private System.Windows.Forms.Button btnTableFilters;
        private System.Windows.Forms.Button btnSaveTableSettings;
        private System.Windows.Forms.Button btnLoadTableSettings;
        private System.Windows.Forms.ListView lvAttributes;
        private System.Windows.Forms.ColumnHeader chAttrDisplayName;
        private System.Windows.Forms.ColumnHeader chAttrLogicalName;
        private System.Windows.Forms.ColumnHeader chAttrType;
        private System.Windows.Forms.ColumnHeader chAttrDescription;
    }
}
