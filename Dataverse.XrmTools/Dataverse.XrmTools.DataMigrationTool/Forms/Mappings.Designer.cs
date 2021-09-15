
namespace Dataverse.XrmTools.DataMigrationTool.Forms
{
    partial class Mappings
    {
        /// <summary>
        /// Required designer variable.
        /// </summary>
        private System.ComponentModel.IContainer components = null;

        /// <summary>
        /// Clean up any resources being used.
        /// </summary>
        /// <param name="disposing">true if managed resources should be disposed; otherwise, false.</param>
        protected override void Dispose(bool disposing)
        {
            if (disposing && (components != null))
            {
                components.Dispose();
            }
            base.Dispose(disposing);
        }

        #region Windows Form Designer generated code

        /// <summary>
        /// Required method for Designer support - do not modify
        /// the contents of this method with the code editor.
        /// </summary>
        private void InitializeComponent()
        {
            this.components = new System.ComponentModel.Container();
            this.pnlHeader1 = new System.Windows.Forms.Panel();
            this.btnNewAttribute = new System.Windows.Forms.Button();
            this.lblADescription = new System.Windows.Forms.Label();
            this.lblATitle = new System.Windows.Forms.Label();
            this.pnlMain1 = new WeifenLuo.WinFormsUI.Docking.DockPanel();
            this.lvAttributeMappings = new System.Windows.Forms.ListView();
            this.chAMapType = ((System.Windows.Forms.ColumnHeader)(new System.Windows.Forms.ColumnHeader()));
            this.chAMapTableDisplay = ((System.Windows.Forms.ColumnHeader)(new System.Windows.Forms.ColumnHeader()));
            this.chAMapTableLogical = ((System.Windows.Forms.ColumnHeader)(new System.Windows.Forms.ColumnHeader()));
            this.chAMapAttributeDisplay = ((System.Windows.Forms.ColumnHeader)(new System.Windows.Forms.ColumnHeader()));
            this.chAMapAttributeLogical = ((System.Windows.Forms.ColumnHeader)(new System.Windows.Forms.ColumnHeader()));
            this.chAMapState = ((System.Windows.Forms.ColumnHeader)(new System.Windows.Forms.ColumnHeader()));
            this.cms_ContextMenu = new System.Windows.Forms.ContextMenuStrip(this.components);
            this.cmsi_Delete = new System.Windows.Forms.ToolStripMenuItem();
            this.cmsi_UndoDelete = new System.Windows.Forms.ToolStripMenuItem();
            this.pnlFooter = new System.Windows.Forms.Panel();
            this.btnCancel = new System.Windows.Forms.Button();
            this.btnClose = new System.Windows.Forms.Button();
            this.pnlHeader2 = new System.Windows.Forms.Panel();
            this.btnNewValue = new System.Windows.Forms.Button();
            this.lblVDescription = new System.Windows.Forms.Label();
            this.lblVTitle = new System.Windows.Forms.Label();
            this.pnlMain2 = new WeifenLuo.WinFormsUI.Docking.DockPanel();
            this.lvValueMappings = new System.Windows.Forms.ListView();
            this.chVMapType = ((System.Windows.Forms.ColumnHeader)(new System.Windows.Forms.ColumnHeader()));
            this.chVMapTableDisplay = ((System.Windows.Forms.ColumnHeader)(new System.Windows.Forms.ColumnHeader()));
            this.chVMapTableLogical = ((System.Windows.Forms.ColumnHeader)(new System.Windows.Forms.ColumnHeader()));
            this.chVMapSourceId = ((System.Windows.Forms.ColumnHeader)(new System.Windows.Forms.ColumnHeader()));
            this.chVMapTargetId = ((System.Windows.Forms.ColumnHeader)(new System.Windows.Forms.ColumnHeader()));
            this.chVMapState = ((System.Windows.Forms.ColumnHeader)(new System.Windows.Forms.ColumnHeader()));
            this.mappingsBindingSource = new System.Windows.Forms.BindingSource(this.components);
            this.pnlHeader1.SuspendLayout();
            this.pnlMain1.SuspendLayout();
            this.cms_ContextMenu.SuspendLayout();
            this.pnlFooter.SuspendLayout();
            this.pnlHeader2.SuspendLayout();
            this.pnlMain2.SuspendLayout();
            ((System.ComponentModel.ISupportInitialize)(this.mappingsBindingSource)).BeginInit();
            this.SuspendLayout();
            // 
            // pnlHeader1
            // 
            this.pnlHeader1.BackColor = System.Drawing.Color.White;
            this.pnlHeader1.Controls.Add(this.btnNewAttribute);
            this.pnlHeader1.Controls.Add(this.lblADescription);
            this.pnlHeader1.Controls.Add(this.lblATitle);
            this.pnlHeader1.Dock = System.Windows.Forms.DockStyle.Top;
            this.pnlHeader1.Location = new System.Drawing.Point(0, 0);
            this.pnlHeader1.Margin = new System.Windows.Forms.Padding(4);
            this.pnlHeader1.Name = "pnlHeader1";
            this.pnlHeader1.Size = new System.Drawing.Size(1696, 71);
            this.pnlHeader1.TabIndex = 0;
            // 
            // btnNewAttribute
            // 
            this.btnNewAttribute.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Right)));
            this.btnNewAttribute.Location = new System.Drawing.Point(1427, 20);
            this.btnNewAttribute.Margin = new System.Windows.Forms.Padding(4);
            this.btnNewAttribute.Name = "btnNewAttribute";
            this.btnNewAttribute.Size = new System.Drawing.Size(253, 28);
            this.btnNewAttribute.TabIndex = 6;
            this.btnNewAttribute.Text = "New Attribute Mapping";
            this.btnNewAttribute.UseVisualStyleBackColor = true;
            this.btnNewAttribute.Click += new System.EventHandler(this.btnNewAttribute_Click);
            // 
            // lblADescription
            // 
            this.lblADescription.AutoSize = true;
            this.lblADescription.Font = new System.Drawing.Font("Segoe UI", 8.25F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, ((byte)(0)));
            this.lblADescription.Location = new System.Drawing.Point(7, 42);
            this.lblADescription.Margin = new System.Windows.Forms.Padding(4, 0, 4, 0);
            this.lblADescription.Name = "lblADescription";
            this.lblADescription.Size = new System.Drawing.Size(713, 19);
            this.lblADescription.TabIndex = 2;
            this.lblADescription.Text = "Match records from source and target instances by a specified attribute (override" +
    " automatic record matching by ID)";
            // 
            // lblATitle
            // 
            this.lblATitle.AutoSize = true;
            this.lblATitle.Font = new System.Drawing.Font("Segoe UI", 14F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, ((byte)(0)));
            this.lblATitle.Location = new System.Drawing.Point(4, 9);
            this.lblATitle.Margin = new System.Windows.Forms.Padding(4, 0, 4, 0);
            this.lblATitle.Name = "lblATitle";
            this.lblATitle.Size = new System.Drawing.Size(222, 32);
            this.lblATitle.TabIndex = 1;
            this.lblATitle.Text = "Attribute Mappings";
            // 
            // pnlMain1
            // 
            this.pnlMain1.BackColor = System.Drawing.Color.White;
            this.pnlMain1.Controls.Add(this.lvAttributeMappings);
            this.pnlMain1.Dock = System.Windows.Forms.DockStyle.Top;
            this.pnlMain1.Location = new System.Drawing.Point(0, 71);
            this.pnlMain1.Margin = new System.Windows.Forms.Padding(4);
            this.pnlMain1.Name = "pnlMain1";
            this.pnlMain1.Size = new System.Drawing.Size(1696, 300);
            this.pnlMain1.TabIndex = 1;
            // 
            // lvAttributeMappings
            // 
            this.lvAttributeMappings.Columns.AddRange(new System.Windows.Forms.ColumnHeader[] {
            this.chAMapType,
            this.chAMapTableDisplay,
            this.chAMapTableLogical,
            this.chAMapAttributeDisplay,
            this.chAMapAttributeLogical,
            this.chAMapState});
            this.lvAttributeMappings.ContextMenuStrip = this.cms_ContextMenu;
            this.lvAttributeMappings.Dock = System.Windows.Forms.DockStyle.Fill;
            this.lvAttributeMappings.FullRowSelect = true;
            this.lvAttributeMappings.HideSelection = false;
            this.lvAttributeMappings.LabelEdit = true;
            this.lvAttributeMappings.Location = new System.Drawing.Point(0, 0);
            this.lvAttributeMappings.Margin = new System.Windows.Forms.Padding(4);
            this.lvAttributeMappings.Name = "lvAttributeMappings";
            this.lvAttributeMappings.Size = new System.Drawing.Size(1696, 300);
            this.lvAttributeMappings.Sorting = System.Windows.Forms.SortOrder.Ascending;
            this.lvAttributeMappings.TabIndex = 5;
            this.lvAttributeMappings.UseCompatibleStateImageBehavior = false;
            this.lvAttributeMappings.View = System.Windows.Forms.View.Details;
            this.lvAttributeMappings.ColumnClick += new System.Windows.Forms.ColumnClickEventHandler(this.listView_ColumnClick);
            this.lvAttributeMappings.Resize += new System.EventHandler(this.lvAttributeMappings_Resize);
            // 
            // chAMapType
            // 
            this.chAMapType.Name = "chAMapType";
            this.chAMapType.Text = "Type";
            this.chAMapType.Width = 103;
            // 
            // chAMapTableDisplay
            // 
            this.chAMapTableDisplay.Name = "chAMapTableDisplay";
            this.chAMapTableDisplay.Text = "Table Display Name";
            this.chAMapTableDisplay.Width = 188;
            // 
            // chAMapTableLogical
            // 
            this.chAMapTableLogical.Name = "chAMapTableLogical";
            this.chAMapTableLogical.Text = "Table Logical Name";
            this.chAMapTableLogical.Width = 184;
            // 
            // chAMapAttributeDisplay
            // 
            this.chAMapAttributeDisplay.Name = "chAMapAttributeDisplay";
            this.chAMapAttributeDisplay.Text = "Attribute Display Name";
            this.chAMapAttributeDisplay.Width = 186;
            // 
            // chAMapAttributeLogical
            // 
            this.chAMapAttributeLogical.Name = "chAMapAttributeLogical";
            this.chAMapAttributeLogical.Text = "Attribute Logical Name";
            this.chAMapAttributeLogical.Width = 182;
            // 
            // chAMapState
            // 
            this.chAMapState.Name = "chAMapState";
            this.chAMapState.Text = "State";
            this.chAMapState.Width = 98;
            // 
            // cms_ContextMenu
            // 
            this.cms_ContextMenu.ImageScalingSize = new System.Drawing.Size(20, 20);
            this.cms_ContextMenu.Items.AddRange(new System.Windows.Forms.ToolStripItem[] {
            this.cmsi_Delete,
            this.cmsi_UndoDelete});
            this.cms_ContextMenu.Name = "cms_ContextMenu";
            this.cms_ContextMenu.Size = new System.Drawing.Size(163, 52);
            this.cms_ContextMenu.Opening += new System.ComponentModel.CancelEventHandler(this.cms_ContextMenu_Opening);
            // 
            // cmsi_Delete
            // 
            this.cmsi_Delete.Name = "cmsi_Delete";
            this.cmsi_Delete.Size = new System.Drawing.Size(162, 24);
            this.cmsi_Delete.Text = "Delete";
            this.cmsi_Delete.Click += new System.EventHandler(this.cmsi_Delete_Click);
            // 
            // cmsi_UndoDelete
            // 
            this.cmsi_UndoDelete.Name = "cmsi_UndoDelete";
            this.cmsi_UndoDelete.Size = new System.Drawing.Size(162, 24);
            this.cmsi_UndoDelete.Text = "Undo Delete";
            this.cmsi_UndoDelete.Click += new System.EventHandler(this.cmsi_UndoDelete_Click);
            // 
            // pnlFooter
            // 
            this.pnlFooter.BackColor = System.Drawing.Color.White;
            this.pnlFooter.Controls.Add(this.btnCancel);
            this.pnlFooter.Controls.Add(this.btnClose);
            this.pnlFooter.Dock = System.Windows.Forms.DockStyle.Bottom;
            this.pnlFooter.Location = new System.Drawing.Point(0, 747);
            this.pnlFooter.Margin = new System.Windows.Forms.Padding(4);
            this.pnlFooter.Name = "pnlFooter";
            this.pnlFooter.Size = new System.Drawing.Size(1696, 64);
            this.pnlFooter.TabIndex = 3;
            // 
            // btnCancel
            // 
            this.btnCancel.Location = new System.Drawing.Point(13, 18);
            this.btnCancel.Margin = new System.Windows.Forms.Padding(5);
            this.btnCancel.Name = "btnCancel";
            this.btnCancel.Size = new System.Drawing.Size(133, 34);
            this.btnCancel.TabIndex = 7;
            this.btnCancel.Text = "Cancel";
            this.btnCancel.UseVisualStyleBackColor = true;
            this.btnCancel.Click += new System.EventHandler(this.btnCancel_Click);
            // 
            // btnClose
            // 
            this.btnClose.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Right)));
            this.btnClose.Location = new System.Drawing.Point(1580, 22);
            this.btnClose.Margin = new System.Windows.Forms.Padding(4);
            this.btnClose.Name = "btnClose";
            this.btnClose.Size = new System.Drawing.Size(100, 28);
            this.btnClose.TabIndex = 5;
            this.btnClose.Text = "OK";
            this.btnClose.UseVisualStyleBackColor = true;
            this.btnClose.Click += new System.EventHandler(this.btnClose_Click);
            // 
            // pnlHeader2
            // 
            this.pnlHeader2.BackColor = System.Drawing.Color.White;
            this.pnlHeader2.Controls.Add(this.btnNewValue);
            this.pnlHeader2.Controls.Add(this.lblVDescription);
            this.pnlHeader2.Controls.Add(this.lblVTitle);
            this.pnlHeader2.Dock = System.Windows.Forms.DockStyle.Top;
            this.pnlHeader2.Location = new System.Drawing.Point(0, 371);
            this.pnlHeader2.Margin = new System.Windows.Forms.Padding(4);
            this.pnlHeader2.Name = "pnlHeader2";
            this.pnlHeader2.Size = new System.Drawing.Size(1696, 71);
            this.pnlHeader2.TabIndex = 5;
            // 
            // btnNewValue
            // 
            this.btnNewValue.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Right)));
            this.btnNewValue.Location = new System.Drawing.Point(1427, 20);
            this.btnNewValue.Margin = new System.Windows.Forms.Padding(4);
            this.btnNewValue.Name = "btnNewValue";
            this.btnNewValue.Size = new System.Drawing.Size(253, 28);
            this.btnNewValue.TabIndex = 7;
            this.btnNewValue.Text = "New Value Mapping";
            this.btnNewValue.UseVisualStyleBackColor = true;
            this.btnNewValue.Click += new System.EventHandler(this.btnNewValue_Click);
            // 
            // lblVDescription
            // 
            this.lblVDescription.AutoSize = true;
            this.lblVDescription.Font = new System.Drawing.Font("Segoe UI", 8.25F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, ((byte)(0)));
            this.lblVDescription.Location = new System.Drawing.Point(7, 42);
            this.lblVDescription.Margin = new System.Windows.Forms.Padding(4, 0, 4, 0);
            this.lblVDescription.Name = "lblVDescription";
            this.lblVDescription.Size = new System.Drawing.Size(674, 19);
            this.lblVDescription.TabIndex = 2;
            this.lblVDescription.Text = "Replace all record references by a specified ID (migrate records even if there ar" +
    "e references with different ID\'s)";
            // 
            // lblVTitle
            // 
            this.lblVTitle.AutoSize = true;
            this.lblVTitle.Font = new System.Drawing.Font("Segoe UI", 14F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, ((byte)(0)));
            this.lblVTitle.Location = new System.Drawing.Point(4, 9);
            this.lblVTitle.Margin = new System.Windows.Forms.Padding(4, 0, 4, 0);
            this.lblVTitle.Name = "lblVTitle";
            this.lblVTitle.Size = new System.Drawing.Size(186, 32);
            this.lblVTitle.TabIndex = 1;
            this.lblVTitle.Text = "Value Mappings";
            // 
            // pnlMain2
            // 
            this.pnlMain2.BackColor = System.Drawing.Color.White;
            this.pnlMain2.Controls.Add(this.lvValueMappings);
            this.pnlMain2.Dock = System.Windows.Forms.DockStyle.Top;
            this.pnlMain2.Location = new System.Drawing.Point(0, 442);
            this.pnlMain2.Margin = new System.Windows.Forms.Padding(4);
            this.pnlMain2.Name = "pnlMain2";
            this.pnlMain2.Size = new System.Drawing.Size(1696, 300);
            this.pnlMain2.TabIndex = 7;
            // 
            // lvValueMappings
            // 
            this.lvValueMappings.Columns.AddRange(new System.Windows.Forms.ColumnHeader[] {
            this.chVMapType,
            this.chVMapTableDisplay,
            this.chVMapTableLogical,
            this.chVMapSourceId,
            this.chVMapTargetId,
            this.chVMapState});
            this.lvValueMappings.ContextMenuStrip = this.cms_ContextMenu;
            this.lvValueMappings.Dock = System.Windows.Forms.DockStyle.Fill;
            this.lvValueMappings.FullRowSelect = true;
            this.lvValueMappings.HideSelection = false;
            this.lvValueMappings.LabelEdit = true;
            this.lvValueMappings.Location = new System.Drawing.Point(0, 0);
            this.lvValueMappings.Margin = new System.Windows.Forms.Padding(4);
            this.lvValueMappings.Name = "lvValueMappings";
            this.lvValueMappings.Size = new System.Drawing.Size(1696, 300);
            this.lvValueMappings.Sorting = System.Windows.Forms.SortOrder.Ascending;
            this.lvValueMappings.TabIndex = 5;
            this.lvValueMappings.UseCompatibleStateImageBehavior = false;
            this.lvValueMappings.View = System.Windows.Forms.View.Details;
            this.lvValueMappings.Resize += new System.EventHandler(this.lvValueMappings_Resize);
            // 
            // chVMapType
            // 
            this.chVMapType.Name = "chVMapType";
            this.chVMapType.Text = "Type";
            this.chVMapType.Width = 79;
            // 
            // chVMapTableDisplay
            // 
            this.chVMapTableDisplay.Name = "chVMapTableDisplay";
            this.chVMapTableDisplay.Text = "Table Display Name";
            this.chVMapTableDisplay.Width = 166;
            // 
            // chVMapTableLogical
            // 
            this.chVMapTableLogical.Name = "chVMapTableLogical";
            this.chVMapTableLogical.Text = "Table Logical Name";
            this.chVMapTableLogical.Width = 173;
            // 
            // chVMapSourceId
            // 
            this.chVMapSourceId.Name = "chVMapSourceId";
            this.chVMapSourceId.Text = "Source ID";
            this.chVMapSourceId.Width = 104;
            // 
            // chVMapTargetId
            // 
            this.chVMapTargetId.Name = "chVMapTargetId";
            this.chVMapTargetId.Text = "Target ID";
            this.chVMapTargetId.Width = 96;
            // 
            // chVMapState
            // 
            this.chVMapState.Name = "chVMapState";
            this.chVMapState.Text = "State";
            this.chVMapState.Width = 83;
            // 
            // mappingsBindingSource
            // 
            this.mappingsBindingSource.DataSource = typeof(Dataverse.XrmTools.DataMigrationTool.Forms.Mappings);
            // 
            // Mappings
            // 
            this.AutoScaleDimensions = new System.Drawing.SizeF(8F, 16F);
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.ClientSize = new System.Drawing.Size(1696, 811);
            this.Controls.Add(this.pnlFooter);
            this.Controls.Add(this.pnlMain2);
            this.Controls.Add(this.pnlHeader2);
            this.Controls.Add(this.pnlMain1);
            this.Controls.Add(this.pnlHeader1);
            this.FormBorderStyle = System.Windows.Forms.FormBorderStyle.Fixed3D;
            this.Margin = new System.Windows.Forms.Padding(4);
            this.MaximizeBox = false;
            this.MinimizeBox = false;
            this.Name = "Mappings";
            this.ShowIcon = false;
            this.ShowInTaskbar = false;
            this.StartPosition = System.Windows.Forms.FormStartPosition.CenterScreen;
            this.Text = "Mappings";
            this.Load += new System.EventHandler(this.MappingListsLoad);
            this.pnlHeader1.ResumeLayout(false);
            this.pnlHeader1.PerformLayout();
            this.pnlMain1.ResumeLayout(false);
            this.cms_ContextMenu.ResumeLayout(false);
            this.pnlFooter.ResumeLayout(false);
            this.pnlHeader2.ResumeLayout(false);
            this.pnlHeader2.PerformLayout();
            this.pnlMain2.ResumeLayout(false);
            ((System.ComponentModel.ISupportInitialize)(this.mappingsBindingSource)).EndInit();
            this.ResumeLayout(false);

        }

        #endregion

        private System.Windows.Forms.Panel pnlHeader1;
        private System.Windows.Forms.Label lblATitle;
        private System.Windows.Forms.Label lblADescription;
        private WeifenLuo.WinFormsUI.Docking.DockPanel pnlMain1;
        private System.Windows.Forms.Panel pnlFooter;
        private System.Windows.Forms.BindingSource mappingsBindingSource;
        private System.Windows.Forms.Button btnClose;
        private System.Windows.Forms.ListView lvAttributeMappings;
        private System.Windows.Forms.ColumnHeader chAMapType;
        private System.Windows.Forms.ColumnHeader chAMapTableDisplay;
        private System.Windows.Forms.ColumnHeader chAMapTableLogical;
        private System.Windows.Forms.ColumnHeader chAMapAttributeDisplay;
        private System.Windows.Forms.ColumnHeader chAMapAttributeLogical;
        private System.Windows.Forms.ColumnHeader chAMapState;
        private System.Windows.Forms.Panel pnlHeader2;
        private System.Windows.Forms.Label lblVDescription;
        private System.Windows.Forms.Label lblVTitle;
        private WeifenLuo.WinFormsUI.Docking.DockPanel pnlMain2;
        private System.Windows.Forms.ListView lvValueMappings;
        private System.Windows.Forms.ColumnHeader chVMapType;
        private System.Windows.Forms.ColumnHeader chVMapTableDisplay;
        private System.Windows.Forms.ColumnHeader chVMapTableLogical;
        private System.Windows.Forms.ColumnHeader chVMapSourceId;
        private System.Windows.Forms.ColumnHeader chVMapTargetId;
        private System.Windows.Forms.ColumnHeader chVMapState;
        private System.Windows.Forms.Button btnNewAttribute;
        private System.Windows.Forms.Button btnNewValue;
        private System.Windows.Forms.Button btnCancel;
        private System.Windows.Forms.ContextMenuStrip cms_ContextMenu;
        private System.Windows.Forms.ToolStripMenuItem cmsi_Delete;
        private System.Windows.Forms.ToolStripMenuItem cmsi_UndoDelete;
    }
}