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
            this.label1 = new System.Windows.Forms.Label();
            this.btnSelectColor = new System.Windows.Forms.Button();
            this.pnlColorPreview = new System.Windows.Forms.Panel();
            this.label2 = new System.Windows.Forms.Label();
            this.label3 = new System.Windows.Forms.Label();
            this.pnlColorPreviewH2 = new System.Windows.Forms.Panel();
            this.pnlColorPreviewH3 = new System.Windows.Forms.Panel();
            this.btnSelectColorH2 = new System.Windows.Forms.Button();
            this.btnSelectColorH3 = new System.Windows.Forms.Button();
            this.label4 = new System.Windows.Forms.Label();
            this.txtDefaultRootPath = new System.Windows.Forms.TextBox();
            this.btnBrowseRootPath = new System.Windows.Forms.Button();
            this.SuspendLayout();
            // 
            // label4 —— 「默认工作路径」（用户存放各个工程目录的共同父目录）
            // 放在最上面一行：现有三行配色（y=64/104/144）与「确定」的坐标因此**一动没动**。
            // 
            this.label4.AutoSize = true;
            this.label4.Font = new System.Drawing.Font("宋体", 12F);
            this.label4.Location = new System.Drawing.Point(9, 24);
            this.label4.Name = "label4";
            this.label4.RightToLeft = System.Windows.Forms.RightToLeft.No;
            this.label4.Size = new System.Drawing.Size(103, 16);
            this.label4.TabIndex = 50;
            this.label4.Text = "默认工作路径";
            // 
            // txtDefaultRootPath
            // 
            this.txtDefaultRootPath.Location = new System.Drawing.Point(128, 21);
            this.txtDefaultRootPath.Name = "txtDefaultRootPath";
            this.txtDefaultRootPath.Size = new System.Drawing.Size(420, 23);
            this.txtDefaultRootPath.TabIndex = 51;
            // 
            // btnBrowseRootPath
            // 
            this.btnBrowseRootPath.Font = new System.Drawing.Font("微软雅黑", 10F);
            this.btnBrowseRootPath.Location = new System.Drawing.Point(556, 18);
            this.btnBrowseRootPath.Name = "btnBrowseRootPath";
            this.btnBrowseRootPath.Size = new System.Drawing.Size(80, 27);
            this.btnBrowseRootPath.TabIndex = 52;
            this.btnBrowseRootPath.Text = "浏览...";
            this.btnBrowseRootPath.UseVisualStyleBackColor = true;
            this.btnBrowseRootPath.Click += new System.EventHandler(this.btnBrowseRootPath_Click);
            // 
            // btnOk
            // 
            this.btnOk.Font = new System.Drawing.Font("微软雅黑", 12F);
            this.btnOk.Location = new System.Drawing.Point(399, 300);
            this.btnOk.Name = "btnOk";
            this.btnOk.Size = new System.Drawing.Size(103, 50);
            this.btnOk.TabIndex = 37;
            this.btnOk.Text = "确定";
            this.btnOk.UseVisualStyleBackColor = true;
            this.btnOk.Click += new System.EventHandler(this.btnOk_Click);
            // 
            // label1
            // 
            this.label1.AutoSize = true;
            this.label1.Font = new System.Drawing.Font("宋体", 12F);
            this.label1.Location = new System.Drawing.Point(9, 64);
            this.label1.Name = "label1";
            this.label1.RightToLeft = System.Windows.Forms.RightToLeft.No;
            this.label1.Size = new System.Drawing.Size(71, 16);
            this.label1.TabIndex = 41;
            this.label1.Text = "h1 颜色";
            // 
            // btnSelectColor
            // 
            this.btnSelectColor.Font = new System.Drawing.Font("微软雅黑", 10F);
            this.btnSelectColor.Location = new System.Drawing.Point(150, 60);
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
            this.pnlColorPreview.Location = new System.Drawing.Point(99, 60);
            this.pnlColorPreview.Name = "pnlColorPreview";
            this.pnlColorPreview.Size = new System.Drawing.Size(33, 33);
            this.pnlColorPreview.TabIndex = 42;
            // 
            // label2
            // 
            this.label2.AutoSize = true;
            this.label2.Font = new System.Drawing.Font("宋体", 12F);
            this.label2.Location = new System.Drawing.Point(9, 104);
            this.label2.Name = "label2";
            this.label2.RightToLeft = System.Windows.Forms.RightToLeft.No;
            this.label2.Size = new System.Drawing.Size(71, 16);
            this.label2.TabIndex = 43;
            this.label2.Text = "h2 颜色";
            // 
            // pnlColorPreviewH2
            // 
            this.pnlColorPreviewH2.BackColor = System.Drawing.Color.Yellow;
            this.pnlColorPreviewH2.BorderStyle = System.Windows.Forms.BorderStyle.FixedSingle;
            this.pnlColorPreviewH2.Location = new System.Drawing.Point(99, 100);
            this.pnlColorPreviewH2.Name = "pnlColorPreviewH2";
            this.pnlColorPreviewH2.Size = new System.Drawing.Size(33, 33);
            this.pnlColorPreviewH2.TabIndex = 44;
            // 
            // btnSelectColorH2
            // 
            this.btnSelectColorH2.Font = new System.Drawing.Font("微软雅黑", 10F);
            this.btnSelectColorH2.Location = new System.Drawing.Point(150, 100);
            this.btnSelectColorH2.Name = "btnSelectColorH2";
            this.btnSelectColorH2.Size = new System.Drawing.Size(100, 33);
            this.btnSelectColorH2.TabIndex = 45;
            this.btnSelectColorH2.Text = "选择颜色";
            this.btnSelectColorH2.UseVisualStyleBackColor = true;
            this.btnSelectColorH2.Click += new System.EventHandler(this.btnSelectColorH2_Click);
            // 
            // label3
            // 
            this.label3.AutoSize = true;
            this.label3.Font = new System.Drawing.Font("宋体", 12F);
            this.label3.Location = new System.Drawing.Point(9, 144);
            this.label3.Name = "label3";
            this.label3.RightToLeft = System.Windows.Forms.RightToLeft.No;
            this.label3.Size = new System.Drawing.Size(71, 16);
            this.label3.TabIndex = 46;
            this.label3.Text = "h3 颜色";
            // 
            // pnlColorPreviewH3
            // 
            this.pnlColorPreviewH3.BackColor = System.Drawing.Color.FromArgb(((int)(((byte)(0)))), ((int)(((byte)(255)))), ((int)(((byte)(0)))));
            this.pnlColorPreviewH3.BorderStyle = System.Windows.Forms.BorderStyle.FixedSingle;
            this.pnlColorPreviewH3.Location = new System.Drawing.Point(99, 140);
            this.pnlColorPreviewH3.Name = "pnlColorPreviewH3";
            this.pnlColorPreviewH3.Size = new System.Drawing.Size(33, 33);
            this.pnlColorPreviewH3.TabIndex = 47;
            // 
            // btnSelectColorH3
            // 
            this.btnSelectColorH3.Font = new System.Drawing.Font("微软雅黑", 10F);
            this.btnSelectColorH3.Location = new System.Drawing.Point(150, 140);
            this.btnSelectColorH3.Name = "btnSelectColorH3";
            this.btnSelectColorH3.Size = new System.Drawing.Size(100, 33);
            this.btnSelectColorH3.TabIndex = 48;
            this.btnSelectColorH3.Text = "选择颜色";
            this.btnSelectColorH3.UseVisualStyleBackColor = true;
            this.btnSelectColorH3.Click += new System.EventHandler(this.btnSelectColorH3_Click);
            // 
            // ParamsForm
            // 
            this.AutoScaleDimensions = new System.Drawing.SizeF(6F, 12F);
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.ClientSize = new System.Drawing.Size(874, 388);
            this.Controls.Add(this.label4);
            this.Controls.Add(this.txtDefaultRootPath);
            this.Controls.Add(this.btnBrowseRootPath);
            this.Controls.Add(this.label1);
            this.Controls.Add(this.btnOk);
            this.Controls.Add(this.pnlColorPreview);
            this.Controls.Add(this.btnSelectColor);
            this.Controls.Add(this.label2);
            this.Controls.Add(this.pnlColorPreviewH2);
            this.Controls.Add(this.btnSelectColorH2);
            this.Controls.Add(this.label3);
            this.Controls.Add(this.pnlColorPreviewH3);
            this.Controls.Add(this.btnSelectColorH3);
            this.Name = "ParamsForm";
            this.Text = "参数设置";
            this.Load += new System.EventHandler(this.ParamsForm_Load);
            this.ResumeLayout(false);
            this.PerformLayout();

        }


        #endregion

        private System.Windows.Forms.Button btnOk;
        private System.Windows.Forms.Label label1;
        private System.Windows.Forms.Button btnSelectColor;
        private System.Windows.Forms.Panel pnlColorPreview;
        private System.Windows.Forms.Label label2;
        private System.Windows.Forms.Label label3;
        private System.Windows.Forms.Panel pnlColorPreviewH2;
        private System.Windows.Forms.Panel pnlColorPreviewH3;
        private System.Windows.Forms.Button btnSelectColorH2;
        private System.Windows.Forms.Button btnSelectColorH3;
        private System.Windows.Forms.Label label4;
        private System.Windows.Forms.TextBox txtDefaultRootPath;
        private System.Windows.Forms.Button btnBrowseRootPath;
    }
}
