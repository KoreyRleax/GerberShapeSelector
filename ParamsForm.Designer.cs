using System.Drawing;

namespace GerberParserSmartV4._0
{
    partial class ParamsForm
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
            this.btnOk = new System.Windows.Forms.Button();
            this.label8 = new System.Windows.Forms.Label();
            this.txtSavePath = new System.Windows.Forms.TextBox();
            this.btnBrowse = new System.Windows.Forms.Button();
            this.label1 = new System.Windows.Forms.Label();
            this.btnSelectColor = new System.Windows.Forms.Button();
            this.pnlColorPreview = new System.Windows.Forms.Panel();
            this.SuspendLayout();
            // 
            // btnOk
            // 
            this.btnOk.Font = new System.Drawing.Font("微软雅黑", 12F);
            this.btnOk.Location = new System.Drawing.Point(399, 340);
            this.btnOk.Name = "btnOk";
            this.btnOk.Size = new System.Drawing.Size(103, 50);
            this.btnOk.TabIndex = 37;
            this.btnOk.Text = "确定";
            this.btnOk.UseVisualStyleBackColor = true;
            this.btnOk.Click += new System.EventHandler(this.btnOk_Click);
            // 
            // label8
            // 
            this.label8.AutoSize = true;
            this.label8.Font = new System.Drawing.Font("宋体", 12F);
            this.label8.Location = new System.Drawing.Point(9, 65);
            this.label8.Name = "label8";
            this.label8.RightToLeft = System.Windows.Forms.RightToLeft.No;
            this.label8.Size = new System.Drawing.Size(135, 16);
            this.label8.TabIndex = 38;
            this.label8.Text = "模板文件保存路径";
            // 
            // txtSavePath
            // 
            this.txtSavePath.BorderStyle = System.Windows.Forms.BorderStyle.None;
            this.txtSavePath.Cursor = System.Windows.Forms.Cursors.IBeam;
            this.txtSavePath.Font = new System.Drawing.Font("微软雅黑", 10F);
            this.txtSavePath.Location = new System.Drawing.Point(150, 61);
            this.txtSavePath.Multiline = true;
            this.txtSavePath.Name = "txtSavePath";
            this.txtSavePath.Size = new System.Drawing.Size(598, 23);
            this.txtSavePath.TabIndex = 39;
            // 
            // btnBrowse
            // 
            this.btnBrowse.Font = new System.Drawing.Font("微软雅黑", 10F);
            this.btnBrowse.Location = new System.Drawing.Point(763, 56);
            this.btnBrowse.Name = "btnBrowse";
            this.btnBrowse.Size = new System.Drawing.Size(68, 33);
            this.btnBrowse.TabIndex = 40;
            this.btnBrowse.Text = "浏览";
            this.btnBrowse.UseVisualStyleBackColor = true;
            this.btnBrowse.Click += new System.EventHandler(this.btnBrowse_Click);
            // 
            // label1
            // 
            this.label1.AutoSize = true;
            this.label1.Font = new System.Drawing.Font("宋体", 12F);
            this.label1.Location = new System.Drawing.Point(9, 104);
            this.label1.Name = "label1";
            this.label1.RightToLeft = System.Windows.Forms.RightToLeft.No;
            this.label1.Size = new System.Drawing.Size(71, 16);
            this.label1.TabIndex = 41;
            this.label1.Text = "选中颜色";
            // 
            // btnSelectColor
            // 
            this.btnSelectColor.Font = new System.Drawing.Font("微软雅黑", 10F);
            this.btnSelectColor.Location = new System.Drawing.Point(150, 100);
            this.btnSelectColor.Name = "btnSelectColor";
            this.btnSelectColor.Size = new System.Drawing.Size(100, 33);
            this.btnSelectColor.TabIndex = 42;
            this.btnSelectColor.Text = "选择颜色";
            this.btnSelectColor.UseVisualStyleBackColor = true;
            this.btnSelectColor.Click += new System.EventHandler(this.btnSelectColor_Click);
            // 
            // pnlColorPreview
            // 
            this.pnlColorPreview.BackColor = System.Drawing.Color.White;
            this.pnlColorPreview.BorderStyle = System.Windows.Forms.BorderStyle.FixedSingle;
            this.pnlColorPreview.Location = new System.Drawing.Point(99, 100);
            this.pnlColorPreview.Name = "pnlColorPreview";
            this.pnlColorPreview.Size = new System.Drawing.Size(33, 33);
            this.pnlColorPreview.TabIndex = 42;
            // 
            // ParamsForm
            // 
            this.AutoScaleDimensions = new System.Drawing.SizeF(6F, 12F);
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.ClientSize = new System.Drawing.Size(874, 428);
            this.Controls.Add(this.label1);
            this.Controls.Add(this.btnBrowse);
            this.Controls.Add(this.txtSavePath);
            this.Controls.Add(this.label8);
            this.Controls.Add(this.btnOk);
            this.Controls.Add(this.pnlColorPreview);
            this.Controls.Add(this.btnSelectColor);
            this.Name = "ParamsForm";
            this.Text = "参数设置";
            this.Load += new System.EventHandler(this.ParamsForm_Load);
            this.ResumeLayout(false);
            this.PerformLayout();

        }


        #endregion

        private System.Windows.Forms.Button btnOk;
        private System.Windows.Forms.Button btnBrowse;
        private System.Windows.Forms.Label label8;
        private System.Windows.Forms.TextBox txtSavePath;
        private System.Windows.Forms.Label label1;
        private System.Windows.Forms.Button btnSelectColor;
        private System.Windows.Forms.Panel pnlColorPreview;
    }
}