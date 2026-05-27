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
            this.components = new System.ComponentModel.Container();
            System.ComponentModel.ComponentResourceManager resources = new System.ComponentModel.ComponentResourceManager(typeof(DataMigrationControl));
            this.tsMain = new System.Windows.Forms.ToolStrip();
            this.tsSeparator1 = new System.Windows.Forms.ToolStripSeparator();
            this.tsbPreview = new System.Windows.Forms.ToolStripButton();
            this.tsmiExport = new System.Windows.Forms.ToolStripMenuItem();
            this.tsmiExportData = new System.Windows.Forms.ToolStripMenuItem();
            this.tsmiExportToExcel = new System.Windows.Forms.ToolStripMenuItem();
            this.tsSeparator2 = new System.Windows.Forms.ToolStripSeparator();
            this.tsmiEnvironments = new System.Windows.Forms.ToolStripMenuItem();
            this.tsmiReloadTables = new System.Windows.Forms.ToolStripMenuItem();
            this.tsSeparatorEnv = new System.Windows.Forms.ToolStripSeparator();
            this.tsmiConnectTarget = new System.Windows.Forms.ToolStripMenuItem();
            this.tsmiExecutionPlan = new System.Windows.Forms.ToolStripMenuItem();
            this.tsmiPlanNew = new System.Windows.Forms.ToolStripMenuItem();
            this.tsmiPlanLoad = new System.Windows.Forms.ToolStripMenuItem();
            this.tsmiPlanSave = new System.Windows.Forms.ToolStripMenuItem();
            this.tsmiPlanSeparator1 = new System.Windows.Forms.ToolStripSeparator();
            this.tsmiPlanReview = new System.Windows.Forms.ToolStripMenuItem();
            this.tsmiPlanValidate = new System.Windows.Forms.ToolStripMenuItem();
            this.tsmiPlanExecute = new System.Windows.Forms.ToolStripMenuItem();
            this.tsmiPlanSeparator2 = new System.Windows.Forms.ToolStripSeparator();
            this.tsmiPlanClose = new System.Windows.Forms.ToolStripMenuItem();
            this.tsbShowInstructions = new System.Windows.Forms.ToolStripButton();
            this.tsbAbort = new System.Windows.Forms.ToolStripButton();
            this.pnlMain = new System.Windows.Forms.TableLayoutPanel();
            this.pnlSettings = new System.Windows.Forms.TableLayoutPanel();
            this.gbOpSettings = new System.Windows.Forms.GroupBox();
            this.cbCreate = new System.Windows.Forms.CheckBox();
            this.cbUpdate = new System.Windows.Forms.CheckBox();
            this.cbDelete = new System.Windows.Forms.CheckBox();
            this.lblBatchCount = new System.Windows.Forms.Label();
            this.nudBatchCount = new System.Windows.Forms.NumericUpDown();
            this.gbViewSettings = new System.Windows.Forms.GroupBox();
            this.cbHideInvalid = new System.Windows.Forms.CheckBox();
            this.pnlBody = new System.Windows.Forms.TableLayoutPanel();
            this.gbTables = new System.Windows.Forms.GroupBox();
            this.lblTableFilter = new System.Windows.Forms.Label();
            this.txtTableFilter = new System.Windows.Forms.TextBox();
            this.lvTables = new System.Windows.Forms.ListView();
            this.chTblLogicalName = ((System.Windows.Forms.ColumnHeader)(new System.Windows.Forms.ColumnHeader()));
            this.chTblDisplayName = ((System.Windows.Forms.ColumnHeader)(new System.Windows.Forms.ColumnHeader()));
            this.gbAttributes = new System.Windows.Forms.GroupBox();
            this.cbSelectAll = new System.Windows.Forms.CheckBox();
            this.lvAttributes = new System.Windows.Forms.ListView();
            this.chAttrLogicalName = ((System.Windows.Forms.ColumnHeader)(new System.Windows.Forms.ColumnHeader()));
            this.chAttrDisplayName = ((System.Windows.Forms.ColumnHeader)(new System.Windows.Forms.ColumnHeader()));
            this.chAttrType = ((System.Windows.Forms.ColumnHeader)(new System.Windows.Forms.ColumnHeader()));
            this.chAttrDescription = ((System.Windows.Forms.ColumnHeader)(new System.Windows.Forms.ColumnHeader()));
            this.gbFilters = new System.Windows.Forms.GroupBox();
            this.lblFetchDescription = new System.Windows.Forms.Label();
            this.btnFetchXmlBuilder = new System.Windows.Forms.Button();
            this.btnSql4Cds = new System.Windows.Forms.Button();
            this.rtbFilter = new System.Windows.Forms.RichTextBox();
            this.toolTip = new System.Windows.Forms.ToolTip(this.components);
            this.tsMain.SuspendLayout();
            this.pnlMain.SuspendLayout();
            this.pnlSettings.SuspendLayout();
            this.gbOpSettings.SuspendLayout();
            ((System.ComponentModel.ISupportInitialize)(this.nudBatchCount)).BeginInit();
            this.gbViewSettings.SuspendLayout();
            this.pnlBody.SuspendLayout();
            this.gbTables.SuspendLayout();
            this.gbAttributes.SuspendLayout();
            this.gbFilters.SuspendLayout();
            this.SuspendLayout();
            // 
            // tsMain
            // 
            this.tsMain.AutoSize = false;
            this.tsMain.ImageScalingSize = new System.Drawing.Size(20, 20);
            this.tsMain.Items.AddRange(new System.Windows.Forms.ToolStripItem[] {
            this.tsmiEnvironments,
            this.tsSeparator1,
            this.tsbPreview,
            this.tsmiExport,
            this.tsSeparator2,
            this.tsbShowInstructions,
            this.tsbAbort});
            this.tsMain.Location = new System.Drawing.Point(0, 0);
            this.tsMain.Name = "tsMain";
            this.tsMain.Size = new System.Drawing.Size(1610, 25);
            this.tsMain.TabIndex = 90;
            this.tsMain.Text = "toolStrip1";
            // tsSeparator1
            // 
            this.tsSeparator1.Name = "tsSeparator1";
            this.tsSeparator1.Size = new System.Drawing.Size(6, 25);
            // 
            // tsbPreview
            // 
            this.tsbPreview.Enabled = false;
            this.tsbPreview.Image = global::Dataverse.XrmTools.DataMigrationTool.Properties.Resources.preview;
            this.tsbPreview.ImageTransparentColor = System.Drawing.Color.Magenta;
            this.tsbPreview.Name = "tsbPreview";
            this.tsbPreview.Size = new System.Drawing.Size(72, 22);
            this.tsbPreview.Text = "Preview";
            this.tsbPreview.Click += new System.EventHandler(this.tsbPreview_Click);
            // 
            // tsmiExport
            // 
            this.tsmiExport.DropDownItems.AddRange(new System.Windows.Forms.ToolStripItem[] {
            this.tsmiExportData,
            this.tsmiExportToExcel});
            this.tsmiExport.Enabled = false;
            this.tsmiExport.Image = global::Dataverse.XrmTools.DataMigrationTool.Properties.Resources.export;
            this.tsmiExport.Name = "tsmiExport";
            this.tsmiExport.Size = new System.Drawing.Size(72, 25);
            this.tsmiExport.Text = "Export";
            // 
            // tsmiExportData
            // 
            this.tsmiExportData.Enabled = false;
            this.tsmiExportData.Name = "tsmiExportData";
            this.tsmiExportData.Size = new System.Drawing.Size(197, 22);
            this.tsmiExportData.Text = "To JSON";
            this.tsmiExportData.Click += new System.EventHandler(this.tsmiExportData_Click);
            //
            // tsmiExportToExcel
            //
            this.tsmiExportToExcel.Enabled = false;
            this.tsmiExportToExcel.Name = "tsmiExportToExcel";
            this.tsmiExportToExcel.Text = "To Excel";
            this.tsmiExportToExcel.Click += new System.EventHandler(this.tsmiExportToExcel_Click);
            //
            // tsSeparator2
            //
            this.tsSeparator2.Name = "tsSeparator2";
            this.tsSeparator2.Size = new System.Drawing.Size(6, 25);
            //
            // tsmiEnvironments
            //
            this.tsmiEnvironments.DropDownItems.AddRange(new System.Windows.Forms.ToolStripItem[] {
            this.tsmiReloadTables,
            this.tsSeparatorEnv,
            this.tsmiConnectTarget});
            this.tsmiEnvironments.Name = "tsmiEnvironments";
            this.tsmiEnvironments.Size = new System.Drawing.Size(107, 25);
            this.tsmiEnvironments.Text = "Environments";
            //
            // tsmiReloadTables
            //
            this.tsmiReloadTables.Name = "tsmiReloadTables";
            this.tsmiReloadTables.Size = new System.Drawing.Size(210, 22);
            this.tsmiReloadTables.Text = "Reload Tables";
            this.tsmiReloadTables.Click += new System.EventHandler(this.tsbRefreshTables_Click);
            //
            // tsSeparatorEnv
            //
            this.tsSeparatorEnv.Name = "tsSeparatorEnv";
            this.tsSeparatorEnv.Size = new System.Drawing.Size(207, 6);
            //
            // tsmiConnectTarget
            //
            this.tsmiConnectTarget.Name = "tsmiConnectTarget";
            this.tsmiConnectTarget.Size = new System.Drawing.Size(210, 22);
            this.tsmiConnectTarget.Text = "Connect Target";
            this.tsmiConnectTarget.Click += new System.EventHandler(this.tsmiConnectTarget_Click);
            //
            // tsmiExecutionPlan
            //
            this.tsmiExecutionPlan.DropDownItems.AddRange(new System.Windows.Forms.ToolStripItem[] {
            this.tsmiPlanNew,
            this.tsmiPlanLoad,
            this.tsmiPlanSave,
            this.tsmiPlanSeparator1,
            this.tsmiPlanReview,
            this.tsmiPlanClose});
            this.tsmiExecutionPlan.Name = "tsmiExecutionPlan";
            this.tsmiExecutionPlan.Size = new System.Drawing.Size(104, 25);
            this.tsmiExecutionPlan.Text = "Execution Plan";
            //
            // tsmiPlanNew
            //
            this.tsmiPlanNew.Name = "tsmiPlanNew";
            this.tsmiPlanNew.Size = new System.Drawing.Size(152, 22);
            this.tsmiPlanNew.Text = "New...";
            this.tsmiPlanNew.Click += new System.EventHandler(this.tsmiPlanNew_Click);
            //
            // tsmiPlanLoad
            //
            this.tsmiPlanLoad.Name = "tsmiPlanLoad";
            this.tsmiPlanLoad.Size = new System.Drawing.Size(152, 22);
            this.tsmiPlanLoad.Text = "Load...";
            this.tsmiPlanLoad.Click += new System.EventHandler(this.tsmiPlanLoad_Click);
            //
            // tsmiPlanSave
            //
            this.tsmiPlanSave.Name = "tsmiPlanSave";
            this.tsmiPlanSave.Size = new System.Drawing.Size(152, 22);
            this.tsmiPlanSave.Text = "Save";
            this.tsmiPlanSave.Click += new System.EventHandler(this.tsmiPlanSave_Click);
            //
            // tsmiPlanSeparator1
            //
            this.tsmiPlanSeparator1.Name = "tsmiPlanSeparator1";
            this.tsmiPlanSeparator1.Size = new System.Drawing.Size(149, 6);
            //
            // tsmiPlanReview
            //
            this.tsmiPlanReview.Name = "tsmiPlanReview";
            this.tsmiPlanReview.Size = new System.Drawing.Size(152, 22);
            this.tsmiPlanReview.Text = "Review...";
            this.tsmiPlanReview.Click += new System.EventHandler(this.tsmiPlanReview_Click);
            //
            // tsmiPlanValidate
            //
            this.tsmiPlanValidate.Name = "tsmiPlanValidate";
            this.tsmiPlanValidate.Size = new System.Drawing.Size(152, 22);
            this.tsmiPlanValidate.Text = "Validate";
            this.tsmiPlanValidate.Click += new System.EventHandler(this.tsmiPlanValidate_Click);
            //
            // tsmiPlanExecute
            //
            this.tsmiPlanExecute.Enabled = false;
            this.tsmiPlanExecute.Name = "tsmiPlanExecute";
            this.tsmiPlanExecute.Size = new System.Drawing.Size(152, 22);
            this.tsmiPlanExecute.Text = "Execute";
            this.tsmiPlanExecute.Click += new System.EventHandler(this.tsmiPlanExecute_Click);
            //
            // tsmiPlanSeparator2
            //
            this.tsmiPlanSeparator2.Name = "tsmiPlanSeparator2";
            this.tsmiPlanSeparator2.Size = new System.Drawing.Size(149, 6);
            //
            // tsmiPlanClose
            //
            this.tsmiPlanClose.Name = "tsmiPlanClose";
            this.tsmiPlanClose.Size = new System.Drawing.Size(152, 22);
            this.tsmiPlanClose.Text = "Close plan";
            this.tsmiPlanClose.Click += new System.EventHandler(this.tsmiPlanClose_Click);
            //
            // tsbShowInstructions
            // 
            this.tsbShowInstructions.DisplayStyle = System.Windows.Forms.ToolStripItemDisplayStyle.Text;
            this.tsbShowInstructions.ImageTransparentColor = System.Drawing.Color.Magenta;
            this.tsbShowInstructions.Name = "tsbShowInstructions";
            this.tsbShowInstructions.Size = new System.Drawing.Size(75, 22);
            this.tsbShowInstructions.Text = "Instructions";
            this.tsbShowInstructions.Click += new System.EventHandler(this.tsbShowInstructions_Click);
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
            // pnlMain
            // 
            this.pnlMain.ColumnCount = 1;
            this.pnlMain.ColumnStyles.Add(new System.Windows.Forms.ColumnStyle(System.Windows.Forms.SizeType.Percent, 100F));
            this.pnlMain.Controls.Add(this.pnlBody, 0, 0);
            this.pnlMain.Dock = System.Windows.Forms.DockStyle.Fill;
            this.pnlMain.Location = new System.Drawing.Point(0, 25);
            this.pnlMain.Margin = new System.Windows.Forms.Padding(2, 2, 2, 2);
            this.pnlMain.Name = "pnlMain";
            this.pnlMain.RowCount = 1;
            this.pnlMain.RowStyles.Add(new System.Windows.Forms.RowStyle(System.Windows.Forms.SizeType.Percent, 50F));
            this.pnlMain.Size = new System.Drawing.Size(1610, 763);
            this.pnlMain.TabIndex = 91;
            // 
            // pnlSettings
            // 
            this.pnlSettings.ColumnCount = 1;
            this.pnlSettings.ColumnStyles.Add(new System.Windows.Forms.ColumnStyle(System.Windows.Forms.SizeType.Percent, 100F));
            this.pnlSettings.Controls.Add(this.gbOpSettings, 0, 1);
            this.pnlSettings.Controls.Add(this.gbViewSettings, 0, 2);
            this.pnlSettings.Dock = System.Windows.Forms.DockStyle.Fill;
            this.pnlSettings.Location = new System.Drawing.Point(2, 2);
            this.pnlSettings.Margin = new System.Windows.Forms.Padding(2, 2, 2, 2);
            this.pnlSettings.Name = "pnlSettings";
            this.pnlSettings.RowCount = 3;
            this.pnlSettings.RowStyles.Add(new System.Windows.Forms.RowStyle());
            this.pnlSettings.RowStyles.Add(new System.Windows.Forms.RowStyle());
            this.pnlSettings.RowStyles.Add(new System.Windows.Forms.RowStyle());
            this.pnlSettings.Size = new System.Drawing.Size(237, 759);
            this.pnlSettings.TabIndex = 0;
            // 
            // gbOpSettings
            // 
            this.gbOpSettings.Anchor = ((System.Windows.Forms.AnchorStyles)(((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Left) 
            | System.Windows.Forms.AnchorStyles.Right)));
            this.gbOpSettings.Controls.Add(this.cbCreate);
            this.gbOpSettings.Controls.Add(this.cbUpdate);
            this.gbOpSettings.Controls.Add(this.cbDelete);
            this.gbOpSettings.Controls.Add(this.lblBatchCount);
            this.gbOpSettings.Controls.Add(this.nudBatchCount);
            this.gbOpSettings.Enabled = false;
            this.gbOpSettings.Location = new System.Drawing.Point(2, 326);
            this.gbOpSettings.Margin = new System.Windows.Forms.Padding(2, 2, 2, 2);
            this.gbOpSettings.Name = "gbOpSettings";
            this.gbOpSettings.Padding = new System.Windows.Forms.Padding(2, 2, 2, 2);
            this.gbOpSettings.Size = new System.Drawing.Size(233, 137);
            this.gbOpSettings.TabIndex = 2;
            this.gbOpSettings.TabStop = false;
            this.gbOpSettings.Text = "Operation Settings";
            // 
            // cbCreate
            // 
            this.cbCreate.AutoSize = true;
            this.cbCreate.Checked = true;
            this.cbCreate.CheckState = System.Windows.Forms.CheckState.Checked;
            this.cbCreate.Location = new System.Drawing.Point(5, 21);
            this.cbCreate.Name = "cbCreate";
            this.cbCreate.Size = new System.Drawing.Size(57, 17);
            this.cbCreate.TabIndex = 0;
            this.cbCreate.Text = "Create";
            this.cbCreate.UseVisualStyleBackColor = true;
            // 
            // cbUpdate
            // 
            this.cbUpdate.AutoSize = true;
            this.cbUpdate.Checked = true;
            this.cbUpdate.CheckState = System.Windows.Forms.CheckState.Checked;
            this.cbUpdate.Location = new System.Drawing.Point(5, 45);
            this.cbUpdate.Name = "cbUpdate";
            this.cbUpdate.Size = new System.Drawing.Size(61, 17);
            this.cbUpdate.TabIndex = 1;
            this.cbUpdate.Text = "Update";
            this.cbUpdate.UseVisualStyleBackColor = true;
            // 
            // cbDelete
            // 
            this.cbDelete.AutoSize = true;
            this.cbDelete.Enabled = false;
            this.cbDelete.Location = new System.Drawing.Point(5, 68);
            this.cbDelete.Name = "cbDelete";
            this.cbDelete.Size = new System.Drawing.Size(69, 17);
            this.cbDelete.TabIndex = 2;
            this.cbDelete.Text = "Delete (!)";
            this.cbDelete.UseVisualStyleBackColor = true;
            // 
            // lblBatchCount
            // 
            this.lblBatchCount.AutoSize = true;
            this.lblBatchCount.Location = new System.Drawing.Point(4, 98);
            this.lblBatchCount.Margin = new System.Windows.Forms.Padding(2, 0, 2, 0);
            this.lblBatchCount.Name = "lblBatchCount";
            this.lblBatchCount.Size = new System.Drawing.Size(59, 13);
            this.lblBatchCount.TabIndex = 3;
            this.lblBatchCount.Text = "Batch size:";
            // 
            // nudBatchCount
            // 
            this.nudBatchCount.Increment = new decimal(new int[] {
            100,
            0,
            0,
            0});
            this.nudBatchCount.Location = new System.Drawing.Point(74, 97);
            this.nudBatchCount.Margin = new System.Windows.Forms.Padding(2, 2, 2, 2);
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
            this.nudBatchCount.Size = new System.Drawing.Size(75, 20);
            this.nudBatchCount.TabIndex = 4;
            this.nudBatchCount.Value = new decimal(new int[] {
            250,
            0,
            0,
            0});
            // 
            // gbViewSettings
            // 
            this.gbViewSettings.Anchor = ((System.Windows.Forms.AnchorStyles)(((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Left) 
            | System.Windows.Forms.AnchorStyles.Right)));
            this.gbViewSettings.Controls.Add(this.cbHideInvalid);
            this.gbViewSettings.Location = new System.Drawing.Point(2, 467);
            this.gbViewSettings.Margin = new System.Windows.Forms.Padding(2, 2, 2, 2);
            this.gbViewSettings.Name = "gbViewSettings";
            this.gbViewSettings.Padding = new System.Windows.Forms.Padding(2, 2, 2, 2);
            this.gbViewSettings.Size = new System.Drawing.Size(233, 55);
            this.gbViewSettings.TabIndex = 0;
            this.gbViewSettings.TabStop = false;
            this.gbViewSettings.Text = "View Settings";
            // 
            // cbHideInvalid
            // 
            this.cbHideInvalid.AutoSize = true;
            this.cbHideInvalid.Checked = true;
            this.cbHideInvalid.CheckState = System.Windows.Forms.CheckState.Checked;
            this.cbHideInvalid.Location = new System.Drawing.Point(5, 21);
            this.cbHideInvalid.Name = "cbHideInvalid";
            this.cbHideInvalid.Size = new System.Drawing.Size(129, 17);
            this.cbHideInvalid.TabIndex = 0;
            this.cbHideInvalid.Text = "Hide Invalid Attributes";
            this.cbHideInvalid.UseVisualStyleBackColor = true;
            this.cbHideInvalid.CheckedChanged += new System.EventHandler(this.cbHideInvalid_CheckedChanged);
            // 
            // pnlBody
            // 
            this.pnlBody.ColumnCount = 1;
            this.pnlBody.ColumnStyles.Add(new System.Windows.Forms.ColumnStyle(System.Windows.Forms.SizeType.Percent, 100F));
            this.pnlBody.Controls.Add(this.gbTables, 0, 0);
            this.pnlBody.Controls.Add(this.gbAttributes, 0, 1);
            this.pnlBody.Controls.Add(this.gbFilters, 0, 2);
            this.pnlBody.Dock = System.Windows.Forms.DockStyle.Fill;
            this.pnlBody.Location = new System.Drawing.Point(2, 2);
            this.pnlBody.Margin = new System.Windows.Forms.Padding(2, 2, 2, 2);
            this.pnlBody.Name = "pnlBody";
            this.pnlBody.RowCount = 3;
            this.pnlBody.RowStyles.Add(new System.Windows.Forms.RowStyle(System.Windows.Forms.SizeType.Percent, 38F));
            this.pnlBody.RowStyles.Add(new System.Windows.Forms.RowStyle(System.Windows.Forms.SizeType.Percent, 40F));
            this.pnlBody.RowStyles.Add(new System.Windows.Forms.RowStyle(System.Windows.Forms.SizeType.Percent, 22F));
            this.pnlBody.Size = new System.Drawing.Size(1606, 759);
            this.pnlBody.TabIndex = 1;
            // 
            // gbTables
            // 
            this.gbTables.Controls.Add(this.lblTableFilter);
            this.gbTables.Controls.Add(this.txtTableFilter);
            this.gbTables.Controls.Add(this.lvTables);
            this.gbTables.Dock = System.Windows.Forms.DockStyle.Fill;
            this.gbTables.Enabled = false;
            this.gbTables.Location = new System.Drawing.Point(2, 66);
            this.gbTables.Margin = new System.Windows.Forms.Padding(2, 2, 2, 2);
            this.gbTables.Name = "gbTables";
            this.gbTables.Padding = new System.Windows.Forms.Padding(2, 2, 2, 2);
            this.gbTables.Size = new System.Drawing.Size(1602, 260);
            this.gbTables.TabIndex = 0;
            this.gbTables.TabStop = false;
            this.gbTables.Text = "Tables";
            // 
            // lblTableFilter
            // 
            this.lblTableFilter.AutoSize = true;
            this.lblTableFilter.Location = new System.Drawing.Point(5, 20);
            this.lblTableFilter.Name = "lblTableFilter";
            this.lblTableFilter.Size = new System.Drawing.Size(32, 13);
            this.lblTableFilter.TabIndex = 0;
            this.lblTableFilter.Text = "Filter:";
            // 
            // txtTableFilter
            // 
            this.txtTableFilter.Anchor = ((System.Windows.Forms.AnchorStyles)(((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Left) 
            | System.Windows.Forms.AnchorStyles.Right)));
            this.txtTableFilter.Location = new System.Drawing.Point(48, 17);
            this.txtTableFilter.Name = "txtTableFilter";
            this.txtTableFilter.Size = new System.Drawing.Size(1550, 20);
            this.txtTableFilter.TabIndex = 1;
            this.txtTableFilter.TextChanged += new System.EventHandler(this.txtTableFilter_TextChanged);
            // 
            // lvTables
            // 
            this.lvTables.Anchor = ((System.Windows.Forms.AnchorStyles)((((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Bottom) 
            | System.Windows.Forms.AnchorStyles.Left) 
            | System.Windows.Forms.AnchorStyles.Right)));
            this.lvTables.Columns.AddRange(new System.Windows.Forms.ColumnHeader[] {
            this.chTblLogicalName,
            this.chTblDisplayName});
            this.lvTables.FullRowSelect = true;
            this.lvTables.HideSelection = false;
            this.lvTables.Location = new System.Drawing.Point(7, 44);
            this.lvTables.MultiSelect = false;
            this.lvTables.Name = "lvTables";
            this.lvTables.Size = new System.Drawing.Size(1591, 211);
            this.lvTables.Sorting = System.Windows.Forms.SortOrder.Ascending;
            this.lvTables.TabIndex = 2;
            this.lvTables.UseCompatibleStateImageBehavior = false;
            this.lvTables.View = System.Windows.Forms.View.Details;
            this.lvTables.ColumnClick += new System.Windows.Forms.ColumnClickEventHandler(this.listView_ColumnClick);
            this.lvTables.SelectedIndexChanged += new System.EventHandler(this.lvTables_SelectedIndexChanged);
            this.lvTables.Resize += new System.EventHandler(this.lvTables_Resize);
            // 
            // chTblLogicalName
            // 
            this.chTblLogicalName.Text = "Logical Name";
            this.chTblLogicalName.Width = 200;
            // 
            // chTblDisplayName
            // 
            this.chTblDisplayName.Text = "Display Name";
            this.chTblDisplayName.Width = 200;
            // 
            // gbAttributes
            // 
            this.gbAttributes.Controls.Add(this.cbSelectAll);
            this.gbAttributes.Controls.Add(this.lvAttributes);
            this.gbAttributes.Dock = System.Windows.Forms.DockStyle.Fill;
            this.gbAttributes.Location = new System.Drawing.Point(2, 330);
            this.gbAttributes.Margin = new System.Windows.Forms.Padding(2, 2, 2, 2);
            this.gbAttributes.Name = "gbAttributes";
            this.gbAttributes.Padding = new System.Windows.Forms.Padding(2, 2, 2, 2);
            this.gbAttributes.Size = new System.Drawing.Size(1602, 276);
            this.gbAttributes.TabIndex = 1;
            this.gbAttributes.TabStop = false;
            this.gbAttributes.Text = "Attributes";
            // 
            // cbSelectAll
            // 
            this.cbSelectAll.AutoSize = true;
            this.cbSelectAll.Checked = true;
            this.cbSelectAll.CheckState = System.Windows.Forms.CheckState.Checked;
            this.cbSelectAll.Location = new System.Drawing.Point(6, 21);
            this.cbSelectAll.Name = "cbSelectAll";
            this.cbSelectAll.Size = new System.Drawing.Size(117, 17);
            this.cbSelectAll.TabIndex = 0;
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
            this.chAttrLogicalName,
            this.chAttrDisplayName,
            this.chAttrType,
            this.chAttrDescription});
            this.lvAttributes.FullRowSelect = true;
            this.lvAttributes.HideSelection = false;
            this.lvAttributes.Location = new System.Drawing.Point(6, 44);
            this.lvAttributes.Name = "lvAttributes";
            this.lvAttributes.Size = new System.Drawing.Size(1592, 227);
            this.lvAttributes.Sorting = System.Windows.Forms.SortOrder.Ascending;
            this.lvAttributes.TabIndex = 3;
            this.lvAttributes.UseCompatibleStateImageBehavior = false;
            this.lvAttributes.View = System.Windows.Forms.View.Details;
            this.lvAttributes.ColumnClick += new System.Windows.Forms.ColumnClickEventHandler(this.listView_ColumnClick);
            this.lvAttributes.ItemChecked += new System.Windows.Forms.ItemCheckedEventHandler(this.lvAttributes_ItemChecked);
            this.lvAttributes.Resize += new System.EventHandler(this.lvAttributes_Resize);
            // 
            // chAttrLogicalName
            // 
            this.chAttrLogicalName.Text = "Logical Name";
            this.chAttrLogicalName.Width = 200;
            // 
            // chAttrDisplayName
            // 
            this.chAttrDisplayName.Text = "Display Name";
            this.chAttrDisplayName.Width = 200;
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
            // gbFilters
            // 
            this.gbFilters.Controls.Add(this.lblFetchDescription);
            this.gbFilters.Controls.Add(this.btnFetchXmlBuilder);
            this.gbFilters.Controls.Add(this.btnSql4Cds);
            this.gbFilters.Controls.Add(this.rtbFilter);
            this.gbFilters.Dock = System.Windows.Forms.DockStyle.Fill;
            this.gbFilters.Location = new System.Drawing.Point(2, 610);
            this.gbFilters.Margin = new System.Windows.Forms.Padding(2, 2, 2, 2);
            this.gbFilters.Name = "gbFilters";
            this.gbFilters.Padding = new System.Windows.Forms.Padding(2, 2, 2, 2);
            this.gbFilters.Size = new System.Drawing.Size(1602, 147);
            this.gbFilters.TabIndex = 2;
            this.gbFilters.TabStop = false;
            this.gbFilters.Text = "Filters";
            // 
            // lblFetchDescription
            // 
            this.lblFetchDescription.AutoSize = true;
            this.lblFetchDescription.Location = new System.Drawing.Point(6, 19);
            this.lblFetchDescription.Name = "lblFetchDescription";
            this.lblFetchDescription.Size = new System.Drawing.Size(232, 13);
            this.lblFetchDescription.TabIndex = 0;
            this.lblFetchDescription.Text = "Records will be filtered using query defined here";
            // 
            // btnFetchXmlBuilder
            // 
            this.btnFetchXmlBuilder.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Right)));
            this.btnFetchXmlBuilder.FlatAppearance.BorderSize = 0;
            this.btnFetchXmlBuilder.FlatStyle = System.Windows.Forms.FlatStyle.Popup;
            this.btnFetchXmlBuilder.Image = global::Dataverse.XrmTools.DataMigrationTool.Properties.Resources.fetchXmlBuilder20;
            this.btnFetchXmlBuilder.ImageAlign = System.Drawing.ContentAlignment.MiddleLeft;
            this.btnFetchXmlBuilder.Location = new System.Drawing.Point(1402, 11);
            this.btnFetchXmlBuilder.Name = "btnFetchXmlBuilder";
            this.btnFetchXmlBuilder.Size = new System.Drawing.Size(194, 28);
            this.btnFetchXmlBuilder.TabIndex = 1;
            this.btnFetchXmlBuilder.Text = "Edit in FetchXML Builder";
            this.btnFetchXmlBuilder.UseVisualStyleBackColor = true;
            this.btnFetchXmlBuilder.Click += new System.EventHandler(this.btnFetchXmlBuilder_Click);
            // 
            // btnSql4Cds
            // 
            this.btnSql4Cds.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Right)));
            this.btnSql4Cds.FlatAppearance.BorderSize = 0;
            this.btnSql4Cds.FlatStyle = System.Windows.Forms.FlatStyle.Popup;
            this.btnSql4Cds.Image = global::Dataverse.XrmTools.DataMigrationTool.Properties.Resources.sql4cds20;
            this.btnSql4Cds.ImageAlign = System.Drawing.ContentAlignment.MiddleLeft;
            this.btnSql4Cds.Location = new System.Drawing.Point(1201, 11);
            this.btnSql4Cds.Name = "btnSql4Cds";
            this.btnSql4Cds.Size = new System.Drawing.Size(194, 28);
            this.btnSql4Cds.TabIndex = 2;
            this.btnSql4Cds.Text = "Edit in SQL 4 CDS";
            this.btnSql4Cds.UseVisualStyleBackColor = true;
            this.btnSql4Cds.Click += new System.EventHandler(this.btnSql4Cds_Click);
            // 
            // rtbFilter
            // 
            this.rtbFilter.Anchor = ((System.Windows.Forms.AnchorStyles)((((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Bottom) 
            | System.Windows.Forms.AnchorStyles.Left) 
            | System.Windows.Forms.AnchorStyles.Right)));
            this.rtbFilter.Location = new System.Drawing.Point(6, 42);
            this.rtbFilter.Margin = new System.Windows.Forms.Padding(20, 20, 20, 20);
            this.rtbFilter.Name = "rtbFilter";
            this.rtbFilter.Size = new System.Drawing.Size(1589, 96);
            this.rtbFilter.TabIndex = 2;
            this.rtbFilter.Text = "";
            this.rtbFilter.TextChanged += new System.EventHandler(this.rtbFilter_TextChanged);
            // 
            // DataMigrationControl
            // 
            this.AutoScaleDimensions = new System.Drawing.SizeF(6F, 13F);
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.BackColor = System.Drawing.SystemColors.Window;
            this.Controls.Add(this.pnlMain);
            this.Controls.Add(this.tsMain);
            this.MinimumSize = new System.Drawing.Size(450, 325);
            this.Name = "DataMigrationControl";
            this.Size = new System.Drawing.Size(1610, 788);
            this.Load += new System.EventHandler(this.DataMigrationControl_Load);
            this.Resize += new System.EventHandler(this.DataMigrationControl_Resize);
            this.tsMain.ResumeLayout(false);
            this.tsMain.PerformLayout();
            this.pnlMain.ResumeLayout(false);
            this.pnlSettings.ResumeLayout(false);
            this.gbOpSettings.ResumeLayout(false);
            this.gbOpSettings.PerformLayout();
            ((System.ComponentModel.ISupportInitialize)(this.nudBatchCount)).EndInit();
            this.gbViewSettings.ResumeLayout(false);
            this.gbViewSettings.PerformLayout();
            this.pnlBody.ResumeLayout(false);
            this.gbTables.ResumeLayout(false);
            this.gbTables.PerformLayout();
            this.gbAttributes.ResumeLayout(false);
            this.gbAttributes.PerformLayout();
            this.gbFilters.ResumeLayout(false);
            this.gbFilters.PerformLayout();
            this.ResumeLayout(false);

        }
        #endregion

        // Main Tool Strip
        private System.Windows.Forms.ToolStrip tsMain;
        private System.Windows.Forms.ToolStripSeparator tsSeparator1;
        private System.Windows.Forms.ToolStripButton tsbPreview;
        private System.Windows.Forms.ToolStripMenuItem tsmiExport;
        private System.Windows.Forms.ToolStripMenuItem tsmiExportData;
        private System.Windows.Forms.ToolStripMenuItem tsmiExportToExcel;
        private System.Windows.Forms.ToolStripSeparator tsSeparator2;
        private System.Windows.Forms.ToolStripMenuItem tsmiEnvironments;
        private System.Windows.Forms.ToolStripMenuItem tsmiReloadTables;
        private System.Windows.Forms.ToolStripSeparator tsSeparatorEnv;
        private System.Windows.Forms.ToolStripMenuItem tsmiConnectTarget;
        private System.Windows.Forms.ToolStripMenuItem tsmiExecutionPlan;
        private System.Windows.Forms.ToolStripMenuItem tsmiPlanNew;
        private System.Windows.Forms.ToolStripMenuItem tsmiPlanLoad;
        private System.Windows.Forms.ToolStripMenuItem tsmiPlanSave;
        private System.Windows.Forms.ToolStripSeparator tsmiPlanSeparator1;
        private System.Windows.Forms.ToolStripMenuItem tsmiPlanReview;
        private System.Windows.Forms.ToolStripMenuItem tsmiPlanValidate;
        private System.Windows.Forms.ToolStripMenuItem tsmiPlanExecute;
        private System.Windows.Forms.ToolStripSeparator tsmiPlanSeparator2;
        private System.Windows.Forms.ToolStripMenuItem tsmiPlanClose;
        private System.Windows.Forms.ToolStripButton tsbShowInstructions;
        private System.Windows.Forms.ToolStripButton tsbAbort;

        // Main panel
        private System.Windows.Forms.TableLayoutPanel pnlMain;

        // Settings
        private System.Windows.Forms.TableLayoutPanel pnlSettings;

        // Connection status strip
        private System.Windows.Forms.ToolTip toolTip;

        // View settings group
        private System.Windows.Forms.GroupBox gbViewSettings;
        private System.Windows.Forms.CheckBox cbHideInvalid;

        // Operation settings group
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
        private System.Windows.Forms.ColumnHeader chTblLogicalName;
        private System.Windows.Forms.ColumnHeader chTblDisplayName;

        // Attributes Group
        private System.Windows.Forms.GroupBox gbAttributes;
        private System.Windows.Forms.CheckBox cbSelectAll;
        private System.Windows.Forms.ListView lvAttributes;
        private System.Windows.Forms.ColumnHeader chAttrLogicalName;
        private System.Windows.Forms.ColumnHeader chAttrDisplayName;
        private System.Windows.Forms.ColumnHeader chAttrType;
        private System.Windows.Forms.ColumnHeader chAttrDescription;

        // Filters Group
        private System.Windows.Forms.GroupBox gbFilters;
        private System.Windows.Forms.Label lblFetchDescription;
        private System.Windows.Forms.Button btnFetchXmlBuilder;
        private System.Windows.Forms.Button btnSql4Cds;
        private System.Windows.Forms.RichTextBox rtbFilter;
    }
}
