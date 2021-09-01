
namespace Dataverse.XrmTools.DataMigrationTool.Forms
{
    partial class ValueMapping
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
            this.pnlHeader = new System.Windows.Forms.Panel();
            this.lblTitle = new System.Windows.Forms.Label();
            this.pnlFooter = new System.Windows.Forms.Panel();
            this.btnCancel = new System.Windows.Forms.Button();
            this.btnClose = new System.Windows.Forms.Button();
            this.pnlMain = new System.Windows.Forms.Panel();
            this.cbAtributes = new System.Windows.Forms.ComboBox();
            this.lblAttribute = new System.Windows.Forms.Label();
            this.cbTables = new System.Windows.Forms.ComboBox();
            this.lblTable = new System.Windows.Forms.Label();
            this.txt_SourceId = new System.Windows.Forms.TextBox();
            this.txt_TargetId = new System.Windows.Forms.TextBox();
            this.lbl_TargetId = new System.Windows.Forms.Label();
            this.lbl_SourceId = new System.Windows.Forms.Label();
            this.pnlHeader.SuspendLayout();
            this.pnlFooter.SuspendLayout();
            this.pnlMain.SuspendLayout();
            this.SuspendLayout();
            // 
            // pnlHeader
            // 
            this.pnlHeader.BackColor = System.Drawing.Color.White;
            this.pnlHeader.Controls.Add(this.lblTitle);
            this.pnlHeader.Dock = System.Windows.Forms.DockStyle.Top;
            this.pnlHeader.Location = new System.Drawing.Point(0, 0);
            this.pnlHeader.Name = "pnlHeader";
            this.pnlHeader.Size = new System.Drawing.Size(365, 45);
            this.pnlHeader.TabIndex = 1;
            // 
            // lblTitle
            // 
            this.lblTitle.AutoSize = true;
            this.lblTitle.Font = new System.Drawing.Font("Segoe UI", 14F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, ((byte)(0)));
            this.lblTitle.Location = new System.Drawing.Point(3, 7);
            this.lblTitle.Name = "lblTitle";
            this.lblTitle.Size = new System.Drawing.Size(210, 25);
            this.lblTitle.TabIndex = 1;
            this.lblTitle.Text = "New Attribute Mapping";
            // 
            // pnlFooter
            // 
            this.pnlFooter.BackColor = System.Drawing.Color.White;
            this.pnlFooter.Controls.Add(this.btnCancel);
            this.pnlFooter.Controls.Add(this.btnClose);
            this.pnlFooter.Dock = System.Windows.Forms.DockStyle.Bottom;
            this.pnlFooter.Location = new System.Drawing.Point(0, 190);
            this.pnlFooter.Name = "pnlFooter";
            this.pnlFooter.Size = new System.Drawing.Size(365, 46);
            this.pnlFooter.TabIndex = 4;
            // 
            // btnCancel
            // 
            this.btnCancel.Location = new System.Drawing.Point(10, 11);
            this.btnCancel.Name = "btnCancel";
            this.btnCancel.Size = new System.Drawing.Size(75, 23);
            this.btnCancel.TabIndex = 6;
            this.btnCancel.Text = "Cancel";
            this.btnCancel.UseVisualStyleBackColor = true;
            this.btnCancel.Click += new System.EventHandler(this.btnCancel_Click);
            // 
            // btnClose
            // 
            this.btnClose.Location = new System.Drawing.Point(278, 11);
            this.btnClose.Name = "btnClose";
            this.btnClose.Size = new System.Drawing.Size(75, 23);
            this.btnClose.TabIndex = 5;
            this.btnClose.Text = "OK";
            this.btnClose.UseVisualStyleBackColor = true;
            this.btnClose.Click += new System.EventHandler(this.btnClose_Click);
            // 
            // pnlMain
            // 
            this.pnlMain.BackColor = System.Drawing.Color.White;
            this.pnlMain.Controls.Add(this.lbl_SourceId);
            this.pnlMain.Controls.Add(this.lbl_TargetId);
            this.pnlMain.Controls.Add(this.txt_TargetId);
            this.pnlMain.Controls.Add(this.txt_SourceId);
            this.pnlMain.Controls.Add(this.cbAtributes);
            this.pnlMain.Controls.Add(this.lblAttribute);
            this.pnlMain.Controls.Add(this.cbTables);
            this.pnlMain.Controls.Add(this.lblTable);
            this.pnlMain.Dock = System.Windows.Forms.DockStyle.Fill;
            this.pnlMain.Location = new System.Drawing.Point(0, 45);
            this.pnlMain.Name = "pnlMain";
            this.pnlMain.Size = new System.Drawing.Size(365, 145);
            this.pnlMain.TabIndex = 6;
            // 
            // cbAtributes
            // 
            this.cbAtributes.Enabled = false;
            this.cbAtributes.FormattingEnabled = true;
            this.cbAtributes.Location = new System.Drawing.Point(73, 43);
            this.cbAtributes.Name = "cbAtributes";
            this.cbAtributes.Size = new System.Drawing.Size(280, 21);
            this.cbAtributes.TabIndex = 3;
            // 
            // lblAttribute
            // 
            this.lblAttribute.AutoSize = true;
            this.lblAttribute.Location = new System.Drawing.Point(12, 46);
            this.lblAttribute.Name = "lblAttribute";
            this.lblAttribute.Size = new System.Drawing.Size(46, 13);
            this.lblAttribute.TabIndex = 2;
            this.lblAttribute.Text = "Attribute";
            // 
            // cbTables
            // 
            this.cbTables.Enabled = false;
            this.cbTables.FormattingEnabled = true;
            this.cbTables.Location = new System.Drawing.Point(73, 7);
            this.cbTables.Name = "cbTables";
            this.cbTables.Size = new System.Drawing.Size(280, 21);
            this.cbTables.TabIndex = 1;
            this.cbTables.SelectedIndexChanged += new System.EventHandler(this.cbTables_SelectedIndexChanged);
            // 
            // lblTable
            // 
            this.lblTable.AutoSize = true;
            this.lblTable.Location = new System.Drawing.Point(12, 10);
            this.lblTable.Name = "lblTable";
            this.lblTable.Size = new System.Drawing.Size(34, 13);
            this.lblTable.TabIndex = 0;
            this.lblTable.Text = "Table";
            // 
            // txt_SourceId
            // 
            this.txt_SourceId.Location = new System.Drawing.Point(73, 79);
            this.txt_SourceId.Name = "txt_SourceId";
            this.txt_SourceId.Size = new System.Drawing.Size(280, 20);
            this.txt_SourceId.TabIndex = 4;
            // 
            // txt_TargetId
            // 
            this.txt_TargetId.Location = new System.Drawing.Point(73, 115);
            this.txt_TargetId.Name = "txt_TargetId";
            this.txt_TargetId.Size = new System.Drawing.Size(280, 20);
            this.txt_TargetId.TabIndex = 5;
            // 
            // lbl_TargetId
            // 
            this.lbl_TargetId.AutoSize = true;
            this.lbl_TargetId.Location = new System.Drawing.Point(12, 118);
            this.lbl_TargetId.Name = "lbl_TargetId";
            this.lbl_TargetId.Size = new System.Drawing.Size(52, 13);
            this.lbl_TargetId.TabIndex = 6;
            this.lbl_TargetId.Text = "Target ID";
            // 
            // lbl_SourceId
            // 
            this.lbl_SourceId.AutoSize = true;
            this.lbl_SourceId.Location = new System.Drawing.Point(12, 82);
            this.lbl_SourceId.Name = "lbl_SourceId";
            this.lbl_SourceId.Size = new System.Drawing.Size(55, 13);
            this.lbl_SourceId.TabIndex = 7;
            this.lbl_SourceId.Text = "Source ID";
            // 
            // ValueMapping
            // 
            this.AutoScaleDimensions = new System.Drawing.SizeF(6F, 13F);
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.ClientSize = new System.Drawing.Size(365, 236);
            this.Controls.Add(this.pnlMain);
            this.Controls.Add(this.pnlFooter);
            this.Controls.Add(this.pnlHeader);
            this.Margin = new System.Windows.Forms.Padding(2, 2, 2, 2);
            this.MaximizeBox = false;
            this.MinimizeBox = false;
            this.Name = "ValueMapping";
            this.ShowIcon = false;
            this.ShowInTaskbar = false;
            this.StartPosition = System.Windows.Forms.FormStartPosition.CenterScreen;
            this.Text = "ValueMapping";
            this.TopMost = true;
            this.Load += new System.EventHandler(this.LoadTables);
            this.pnlHeader.ResumeLayout(false);
            this.pnlHeader.PerformLayout();
            this.pnlFooter.ResumeLayout(false);
            this.pnlMain.ResumeLayout(false);
            this.pnlMain.PerformLayout();
            this.ResumeLayout(false);

        }

        #endregion

        private System.Windows.Forms.Panel pnlHeader;
        private System.Windows.Forms.Label lblTitle;
        private System.Windows.Forms.Panel pnlFooter;
        private System.Windows.Forms.Button btnClose;
        private System.Windows.Forms.Panel pnlMain;
        private System.Windows.Forms.Label lblTable;
        private System.Windows.Forms.ComboBox cbTables;
        private System.Windows.Forms.ComboBox cbAtributes;
        private System.Windows.Forms.Label lblAttribute;
        private System.Windows.Forms.Button btnCancel;
        private System.Windows.Forms.TextBox txt_SourceId;
        private System.Windows.Forms.Label lbl_SourceId;
        private System.Windows.Forms.Label lbl_TargetId;
        private System.Windows.Forms.TextBox txt_TargetId;
    }
}