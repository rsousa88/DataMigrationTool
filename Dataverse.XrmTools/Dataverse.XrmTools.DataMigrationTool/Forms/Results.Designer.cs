
namespace Dataverse.XrmTools.DataMigrationTool.Forms
{
    partial class Results
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
            this.lblDescription = new System.Windows.Forms.Label();
            this.lblTitle = new System.Windows.Forms.Label();
            this.lvItems = new System.Windows.Forms.ListView();
            this.chResAction = ((System.Windows.Forms.ColumnHeader)(new System.Windows.Forms.ColumnHeader()));
            this.chResRecordId = ((System.Windows.Forms.ColumnHeader)(new System.Windows.Forms.ColumnHeader()));
            this.chResRecordName = ((System.Windows.Forms.ColumnHeader)(new System.Windows.Forms.ColumnHeader()));
            this.chResDescription = ((System.Windows.Forms.ColumnHeader)(new System.Windows.Forms.ColumnHeader()));
            this.pnlFooter = new System.Windows.Forms.Panel();
            this.btnClose = new System.Windows.Forms.Button();
            this.pnlBody = new System.Windows.Forms.TableLayoutPanel();
            this.gbSummary = new System.Windows.Forms.GroupBox();
            this.gbResults = new System.Windows.Forms.GroupBox();
            this.lblSumCreate = new System.Windows.Forms.Label();
            this.lblSumUpdate = new System.Windows.Forms.Label();
            this.lblSumDelete = new System.Windows.Forms.Label();
            this.lblSumTotal = new System.Windows.Forms.Label();
            this.lblSumCreateValue = new System.Windows.Forms.Label();
            this.lblSumUpdateValue = new System.Windows.Forms.Label();
            this.lblSumDeleteValue = new System.Windows.Forms.Label();
            this.lblSumTotalValue = new System.Windows.Forms.Label();
            this.pnlHeader.SuspendLayout();
            this.pnlFooter.SuspendLayout();
            this.pnlBody.SuspendLayout();
            this.gbSummary.SuspendLayout();
            this.gbResults.SuspendLayout();
            this.SuspendLayout();
            // 
            // pnlHeader
            // 
            this.pnlHeader.BackColor = System.Drawing.Color.White;
            this.pnlHeader.Controls.Add(this.lblDescription);
            this.pnlHeader.Controls.Add(this.lblTitle);
            this.pnlHeader.Dock = System.Windows.Forms.DockStyle.Top;
            this.pnlHeader.Location = new System.Drawing.Point(0, 0);
            this.pnlHeader.Margin = new System.Windows.Forms.Padding(4);
            this.pnlHeader.Name = "pnlHeader";
            this.pnlHeader.Size = new System.Drawing.Size(1508, 71);
            this.pnlHeader.TabIndex = 0;
            // 
            // lblDescription
            // 
            this.lblDescription.AutoSize = true;
            this.lblDescription.Font = new System.Drawing.Font("Segoe UI", 8.25F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, ((byte)(0)));
            this.lblDescription.Location = new System.Drawing.Point(7, 42);
            this.lblDescription.Margin = new System.Windows.Forms.Padding(4, 0, 4, 0);
            this.lblDescription.Name = "lblDescription";
            this.lblDescription.Size = new System.Drawing.Size(145, 19);
            this.lblDescription.TabIndex = 2;
            this.lblDescription.Text = "Data migration results";
            // 
            // lblTitle
            // 
            this.lblTitle.AutoSize = true;
            this.lblTitle.Font = new System.Drawing.Font("Segoe UI", 14F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, ((byte)(0)));
            this.lblTitle.Location = new System.Drawing.Point(4, 0);
            this.lblTitle.Margin = new System.Windows.Forms.Padding(4, 0, 4, 0);
            this.lblTitle.Name = "lblTitle";
            this.lblTitle.Size = new System.Drawing.Size(89, 32);
            this.lblTitle.TabIndex = 1;
            this.lblTitle.Text = "Results";
            // 
            // lvItems
            // 
            this.lvItems.Columns.AddRange(new System.Windows.Forms.ColumnHeader[] {
            this.chResAction,
            this.chResRecordId,
            this.chResRecordName,
            this.chResDescription});
            this.lvItems.Dock = System.Windows.Forms.DockStyle.Fill;
            this.lvItems.FullRowSelect = true;
            this.lvItems.HideSelection = false;
            this.lvItems.LabelEdit = true;
            this.lvItems.Location = new System.Drawing.Point(4, 19);
            this.lvItems.Margin = new System.Windows.Forms.Padding(4);
            this.lvItems.Name = "lvItems";
            this.lvItems.Size = new System.Drawing.Size(1341, 708);
            this.lvItems.Sorting = System.Windows.Forms.SortOrder.Ascending;
            this.lvItems.TabIndex = 4;
            this.lvItems.UseCompatibleStateImageBehavior = false;
            this.lvItems.View = System.Windows.Forms.View.Details;
            this.lvItems.ColumnClick += new System.Windows.Forms.ColumnClickEventHandler(this.listView_ColumnClick);
            this.lvItems.KeyUp += new System.Windows.Forms.KeyEventHandler(this.lvItems_KeyUp);
            // 
            // chResAction
            // 
            this.chResAction.Text = "Action";
            this.chResAction.Width = 100;
            // 
            // chResRecordId
            // 
            this.chResRecordId.Text = "Record ID";
            this.chResRecordId.Width = 250;
            // 
            // chResRecordName
            // 
            this.chResRecordName.Text = "Record Name";
            this.chResRecordName.Width = 350;
            // 
            // chResDescription
            // 
            this.chResDescription.Text = "Description";
            this.chResDescription.Width = 600;
            // 
            // pnlFooter
            // 
            this.pnlFooter.BackColor = System.Drawing.Color.White;
            this.pnlFooter.Controls.Add(this.btnClose);
            this.pnlFooter.Dock = System.Windows.Forms.DockStyle.Bottom;
            this.pnlFooter.Location = new System.Drawing.Point(0, 810);
            this.pnlFooter.Margin = new System.Windows.Forms.Padding(4);
            this.pnlFooter.Name = "pnlFooter";
            this.pnlFooter.Size = new System.Drawing.Size(1508, 64);
            this.pnlFooter.TabIndex = 3;
            // 
            // btnClose
            // 
            this.btnClose.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Bottom | System.Windows.Forms.AnchorStyles.Right)));
            this.btnClose.Location = new System.Drawing.Point(1392, 21);
            this.btnClose.Margin = new System.Windows.Forms.Padding(4);
            this.btnClose.Name = "btnClose";
            this.btnClose.Size = new System.Drawing.Size(100, 28);
            this.btnClose.TabIndex = 3;
            this.btnClose.Text = "Close";
            this.btnClose.UseVisualStyleBackColor = true;
            this.btnClose.Click += new System.EventHandler(this.btnClose_Click);
            // 
            // pnlBody
            // 
            this.pnlBody.BackColor = System.Drawing.SystemColors.Window;
            this.pnlBody.ColumnCount = 2;
            this.pnlBody.ColumnStyles.Add(new System.Windows.Forms.ColumnStyle(System.Windows.Forms.SizeType.Percent, 90F));
            this.pnlBody.ColumnStyles.Add(new System.Windows.Forms.ColumnStyle(System.Windows.Forms.SizeType.Percent, 10F));
            this.pnlBody.Controls.Add(this.gbSummary, 1, 0);
            this.pnlBody.Controls.Add(this.gbResults, 0, 0);
            this.pnlBody.Dock = System.Windows.Forms.DockStyle.Fill;
            this.pnlBody.Location = new System.Drawing.Point(0, 71);
            this.pnlBody.Margin = new System.Windows.Forms.Padding(4);
            this.pnlBody.Name = "pnlBody";
            this.pnlBody.RowCount = 1;
            this.pnlBody.RowStyles.Add(new System.Windows.Forms.RowStyle(System.Windows.Forms.SizeType.Percent, 90F));
            this.pnlBody.RowStyles.Add(new System.Windows.Forms.RowStyle(System.Windows.Forms.SizeType.Percent, 10F));
            this.pnlBody.Size = new System.Drawing.Size(1508, 739);
            this.pnlBody.TabIndex = 105;
            // 
            // gbSummary
            // 
            this.gbSummary.BackColor = System.Drawing.SystemColors.Window;
            this.gbSummary.Controls.Add(this.lblSumTotalValue);
            this.gbSummary.Controls.Add(this.lblSumDeleteValue);
            this.gbSummary.Controls.Add(this.lblSumUpdateValue);
            this.gbSummary.Controls.Add(this.lblSumCreateValue);
            this.gbSummary.Controls.Add(this.lblSumTotal);
            this.gbSummary.Controls.Add(this.lblSumDelete);
            this.gbSummary.Controls.Add(this.lblSumUpdate);
            this.gbSummary.Controls.Add(this.lblSumCreate);
            this.gbSummary.Dock = System.Windows.Forms.DockStyle.Fill;
            this.gbSummary.Font = new System.Drawing.Font("Segoe UI", 8.25F);
            this.gbSummary.Location = new System.Drawing.Point(1361, 4);
            this.gbSummary.Margin = new System.Windows.Forms.Padding(4);
            this.gbSummary.Name = "gbSummary";
            this.gbSummary.Padding = new System.Windows.Forms.Padding(4);
            this.gbSummary.Size = new System.Drawing.Size(143, 731);
            this.gbSummary.TabIndex = 92;
            this.gbSummary.TabStop = false;
            this.gbSummary.Text = "Summary";
            // 
            // gbResults
            // 
            this.gbResults.BackColor = System.Drawing.SystemColors.Window;
            this.gbResults.Controls.Add(this.lvItems);
            this.gbResults.Dock = System.Windows.Forms.DockStyle.Fill;
            this.gbResults.Location = new System.Drawing.Point(4, 4);
            this.gbResults.Margin = new System.Windows.Forms.Padding(4);
            this.gbResults.Name = "gbResults";
            this.gbResults.Padding = new System.Windows.Forms.Padding(4);
            this.gbResults.Size = new System.Drawing.Size(1349, 731);
            this.gbResults.TabIndex = 93;
            this.gbResults.TabStop = false;
            this.gbResults.Text = "Results";
            // 
            // lblSumCreate
            // 
            this.lblSumCreate.AutoSize = true;
            this.lblSumCreate.Location = new System.Drawing.Point(7, 23);
            this.lblSumCreate.Name = "lblSumCreate";
            this.lblSumCreate.Size = new System.Drawing.Size(52, 19);
            this.lblSumCreate.TabIndex = 3;
            this.lblSumCreate.Text = "Create:";
            // 
            // lblSumUpdate
            // 
            this.lblSumUpdate.AutoSize = true;
            this.lblSumUpdate.Location = new System.Drawing.Point(7, 42);
            this.lblSumUpdate.Name = "lblSumUpdate";
            this.lblSumUpdate.Size = new System.Drawing.Size(57, 19);
            this.lblSumUpdate.TabIndex = 4;
            this.lblSumUpdate.Text = "Update:";
            // 
            // lblSumDelete
            // 
            this.lblSumDelete.AutoSize = true;
            this.lblSumDelete.Location = new System.Drawing.Point(7, 61);
            this.lblSumDelete.Name = "lblSumDelete";
            this.lblSumDelete.Size = new System.Drawing.Size(51, 19);
            this.lblSumDelete.TabIndex = 5;
            this.lblSumDelete.Text = "Delete:";
            // 
            // lblSumTotal
            // 
            this.lblSumTotal.AutoSize = true;
            this.lblSumTotal.Font = new System.Drawing.Font("Segoe UI", 8.25F, System.Drawing.FontStyle.Bold);
            this.lblSumTotal.Location = new System.Drawing.Point(7, 89);
            this.lblSumTotal.Name = "lblSumTotal";
            this.lblSumTotal.Size = new System.Drawing.Size(46, 19);
            this.lblSumTotal.TabIndex = 6;
            this.lblSumTotal.Text = "Total:";
            // 
            // lblSumCreateValue
            // 
            this.lblSumCreateValue.Anchor = ((System.Windows.Forms.AnchorStyles)(((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Left) 
            | System.Windows.Forms.AnchorStyles.Right)));
            this.lblSumCreateValue.Location = new System.Drawing.Point(84, 23);
            this.lblSumCreateValue.Name = "lblSumCreateValue";
            this.lblSumCreateValue.RightToLeft = System.Windows.Forms.RightToLeft.Yes;
            this.lblSumCreateValue.Size = new System.Drawing.Size(52, 19);
            this.lblSumCreateValue.TabIndex = 7;
            this.lblSumCreateValue.Text = "0";
            // 
            // lblSumUpdateValue
            // 
            this.lblSumUpdateValue.Anchor = ((System.Windows.Forms.AnchorStyles)(((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Left) 
            | System.Windows.Forms.AnchorStyles.Right)));
            this.lblSumUpdateValue.Location = new System.Drawing.Point(84, 42);
            this.lblSumUpdateValue.Name = "lblSumUpdateValue";
            this.lblSumUpdateValue.RightToLeft = System.Windows.Forms.RightToLeft.Yes;
            this.lblSumUpdateValue.Size = new System.Drawing.Size(52, 19);
            this.lblSumUpdateValue.TabIndex = 8;
            this.lblSumUpdateValue.Text = "0";
            // 
            // lblSumDeleteValue
            // 
            this.lblSumDeleteValue.Anchor = ((System.Windows.Forms.AnchorStyles)(((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Left) 
            | System.Windows.Forms.AnchorStyles.Right)));
            this.lblSumDeleteValue.Location = new System.Drawing.Point(84, 61);
            this.lblSumDeleteValue.Name = "lblSumDeleteValue";
            this.lblSumDeleteValue.RightToLeft = System.Windows.Forms.RightToLeft.Yes;
            this.lblSumDeleteValue.Size = new System.Drawing.Size(52, 19);
            this.lblSumDeleteValue.TabIndex = 9;
            this.lblSumDeleteValue.Text = "0";
            // 
            // lblSumTotalValue
            // 
            this.lblSumTotalValue.Anchor = ((System.Windows.Forms.AnchorStyles)(((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Left) 
            | System.Windows.Forms.AnchorStyles.Right)));
            this.lblSumTotalValue.Font = new System.Drawing.Font("Segoe UI", 8.25F, System.Drawing.FontStyle.Bold);
            this.lblSumTotalValue.Location = new System.Drawing.Point(84, 89);
            this.lblSumTotalValue.Name = "lblSumTotalValue";
            this.lblSumTotalValue.RightToLeft = System.Windows.Forms.RightToLeft.Yes;
            this.lblSumTotalValue.Size = new System.Drawing.Size(52, 19);
            this.lblSumTotalValue.TabIndex = 10;
            this.lblSumTotalValue.Text = "0";
            // 
            // Results
            // 
            this.AutoScaleDimensions = new System.Drawing.SizeF(8F, 16F);
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.BackColor = System.Drawing.SystemColors.Window;
            this.ClientSize = new System.Drawing.Size(1508, 874);
            this.Controls.Add(this.pnlBody);
            this.Controls.Add(this.pnlFooter);
            this.Controls.Add(this.pnlHeader);
            this.Margin = new System.Windows.Forms.Padding(4);
            this.MaximizeBox = false;
            this.MinimizeBox = false;
            this.Name = "Results";
            this.ShowIcon = false;
            this.ShowInTaskbar = false;
            this.StartPosition = System.Windows.Forms.FormStartPosition.CenterScreen;
            this.Text = "Results";
            this.Load += new System.EventHandler(this.LoadRecords);
            this.Resize += new System.EventHandler(this.Results_Resize);
            this.pnlHeader.ResumeLayout(false);
            this.pnlHeader.PerformLayout();
            this.pnlFooter.ResumeLayout(false);
            this.pnlBody.ResumeLayout(false);
            this.gbSummary.ResumeLayout(false);
            this.gbSummary.PerformLayout();
            this.gbResults.ResumeLayout(false);
            this.ResumeLayout(false);

        }

        #endregion

        private System.Windows.Forms.Panel pnlHeader;
        private System.Windows.Forms.Label lblTitle;
        private System.Windows.Forms.Label lblDescription;
        private System.Windows.Forms.Panel pnlFooter;
        private System.Windows.Forms.ListView lvItems;
        private System.Windows.Forms.Button btnClose;
        private System.Windows.Forms.ColumnHeader chResAction;
        private System.Windows.Forms.ColumnHeader chResRecordId;
        private System.Windows.Forms.ColumnHeader chResRecordName;
        private System.Windows.Forms.ColumnHeader chResDescription;
        private System.Windows.Forms.TableLayoutPanel pnlBody;
        private System.Windows.Forms.GroupBox gbResults;
        private System.Windows.Forms.GroupBox gbSummary;
        private System.Windows.Forms.Label lblSumTotalValue;
        private System.Windows.Forms.Label lblSumDeleteValue;
        private System.Windows.Forms.Label lblSumUpdateValue;
        private System.Windows.Forms.Label lblSumCreateValue;
        private System.Windows.Forms.Label lblSumTotal;
        private System.Windows.Forms.Label lblSumDelete;
        private System.Windows.Forms.Label lblSumUpdate;
        private System.Windows.Forms.Label lblSumCreate;
    }
}