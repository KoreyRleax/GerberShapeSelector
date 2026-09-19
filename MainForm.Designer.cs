namespace GerberParserSmartV4._0
{
    partial class MainForm
    {
        /// <summary>
        /// 必需的设计器变量。
        /// </summary>
        private System.ComponentModel.IContainer components = null;

        /// <summary>
        /// 清理所有正在使用的资源。
        /// </summary>
        /// <param name="disposing">如果应释放托管资源，为 true；否则为 false。</param>
        protected override void Dispose(bool disposing)
        {
            if (disposing && (components != null))
            {
                components.Dispose();
            }
            base.Dispose(disposing);
        }

        #region Windows 窗体设计器生成的代码

        /// <summary>
        /// 设计器支持所需的方法 - 不要修改
        /// 使用代码编辑器修改此方法的内容。
        /// </summary>
        private void InitializeComponent()
        {
            this.menuStrip1 = new System.Windows.Forms.MenuStrip();
            this.mnuFile = new System.Windows.Forms.ToolStripMenuItem();
            this.mnuNewProject = new System.Windows.Forms.ToolStripMenuItem();
            this.mnuOpenProject = new System.Windows.Forms.ToolStripMenuItem();
            this.mnuDeleteProject = new System.Windows.Forms.ToolStripMenuItem();
            this.mnuFileSep1 = new System.Windows.Forms.ToolStripSeparator();
            this.mnuSave = new System.Windows.Forms.ToolStripMenuItem();
            this.mnuSaveAs = new System.Windows.Forms.ToolStripMenuItem();
            this.mnuFileSep2 = new System.Windows.Forms.ToolStripSeparator();
            this.mnuExit = new System.Windows.Forms.ToolStripMenuItem();
            this.mnuEdit = new System.Windows.Forms.ToolStripMenuItem();
            this.mnuRegionSelect = new System.Windows.Forms.ToolStripMenuItem();
            this.mnuReleaseScope = new System.Windows.Forms.ToolStripMenuItem();
            this.mnuClearSelection = new System.Windows.Forms.ToolStripMenuItem();
            this.mnuEditSep1 = new System.Windows.Forms.ToolStripSeparator();
            this.mnuMirrorX = new System.Windows.Forms.ToolStripMenuItem();
            this.mnuMirrorY = new System.Windows.Forms.ToolStripMenuItem();
            this.mnuView = new System.Windows.Forms.ToolStripMenuItem();
            this.mnuResetView = new System.Windows.Forms.ToolStripMenuItem();
            this.mnuShowBaseLayer = new System.Windows.Forms.ToolStripMenuItem();
            this.mnuViewSep1 = new System.Windows.Forms.ToolStripSeparator();
            this.mnuParams = new System.Windows.Forms.ToolStripMenuItem();
            this.mnuHelp = new System.Windows.Forms.ToolStripMenuItem();
            this.mnuHelpDoc = new System.Windows.Forms.ToolStripMenuItem();
            this.mnuAbout = new System.Windows.Forms.ToolStripMenuItem();
            this.lblSelInfo = new System.Windows.Forms.ToolStripStatusLabel();
            this.lblLayerInfo = new System.Windows.Forms.ToolStripStatusLabel();
            this.lblView = new System.Windows.Forms.ToolStripStatusLabel();
            this.folderBrowserDialog1 = new System.Windows.Forms.FolderBrowserDialog();
            this.panelPathBar = new System.Windows.Forms.Panel();
            this.lblProjectPath = new System.Windows.Forms.Label();
            this.txtProjectPath = new System.Windows.Forms.TextBox();
            this.btnOpenProject = new System.Windows.Forms.Button();
            this.kWindowControl1 = new Korey.SmartWindow.WinForms.KWindowControl();
            this.statusStrip1 = new System.Windows.Forms.StatusStrip();
            this.toolStripStatusLabel1 = new System.Windows.Forms.ToolStripStatusLabel();
            this.openFileDialog1 = new System.Windows.Forms.OpenFileDialog();
            this.saveFileDialog1 = new System.Windows.Forms.SaveFileDialog();
            this.menuStrip1.SuspendLayout();
            this.statusStrip1.SuspendLayout();
            this.SuspendLayout();
            // 
            // menuStrip1
            // 
            this.menuStrip1.Items.AddRange(new System.Windows.Forms.ToolStripItem[] {
            this.mnuFile,
            this.mnuEdit,
            this.mnuView,
            this.mnuHelp});
            this.menuStrip1.Location = new System.Drawing.Point(0, 0);
            this.menuStrip1.Name = "menuStrip1";
            this.menuStrip1.Size = new System.Drawing.Size(1384, 28);
            this.menuStrip1.TabIndex = 0;
            this.menuStrip1.Text = "menuStrip1";
            // 
            // mnuFile
            // 
            this.mnuFile.DropDownItems.AddRange(new System.Windows.Forms.ToolStripItem[] {
            this.mnuNewProject,
            this.mnuOpenProject,
            this.mnuDeleteProject,
            this.mnuFileSep1,
            this.mnuSave,
            this.mnuSaveAs,
            this.mnuFileSep2,
            this.mnuExit});
            this.mnuFile.Name = "mnuFile";
            this.mnuFile.Size = new System.Drawing.Size(68, 24);
            this.mnuFile.Text = "文件(&F)";
            // 
            // mnuNewProject
            // 
            this.mnuNewProject.Name = "mnuNewProject";
            this.mnuNewProject.ShortcutKeys = ((System.Windows.Forms.Keys)((System.Windows.Forms.Keys.Control | System.Windows.Forms.Keys.N)));
            this.mnuNewProject.Size = new System.Drawing.Size(228, 26);
            this.mnuNewProject.Text = "新建工程...";
            this.mnuNewProject.Click += new System.EventHandler(this.OnNewProjectClick);
            // 
            // mnuOpenProject
            // 
            this.mnuOpenProject.Name = "mnuOpenProject";
            this.mnuOpenProject.ShortcutKeys = ((System.Windows.Forms.Keys)((System.Windows.Forms.Keys.Control | System.Windows.Forms.Keys.O)));
            this.mnuOpenProject.Size = new System.Drawing.Size(228, 26);
            this.mnuOpenProject.Text = "打开工程...";
            this.mnuOpenProject.Click += new System.EventHandler(this.OnOpenProjectClick);
            // 
            // mnuDeleteProject
            // 
            this.mnuDeleteProject.Name = "mnuDeleteProject";
            this.mnuDeleteProject.Size = new System.Drawing.Size(228, 26);
            this.mnuDeleteProject.Text = "删除工程...";
            this.mnuDeleteProject.Click += new System.EventHandler(this.OnDeleteProjectClick);
            // 
            // mnuFileSep1
            // 
            this.mnuFileSep1.Name = "mnuFileSep1";
            this.mnuFileSep1.Size = new System.Drawing.Size(225, 6);
            // 
            // mnuSave
            // 
            this.mnuSave.Name = "mnuSave";
            this.mnuSave.ShortcutKeys = ((System.Windows.Forms.Keys)((System.Windows.Forms.Keys.Control | System.Windows.Forms.Keys.S)));
            this.mnuSave.Size = new System.Drawing.Size(228, 26);
            this.mnuSave.Text = "保存";
            this.mnuSave.Click += new System.EventHandler(this.btnSaveTemple_Click);
            // 
            // mnuSaveAs
            // 
            this.mnuSaveAs.Name = "mnuSaveAs";
            this.mnuSaveAs.Size = new System.Drawing.Size(228, 26);
            this.mnuSaveAs.Text = "另存到...";
            this.mnuSaveAs.Click += new System.EventHandler(this.btnSaveAs_Click);
            // 
            // mnuFileSep2
            // 
            this.mnuFileSep2.Name = "mnuFileSep2";
            this.mnuFileSep2.Size = new System.Drawing.Size(225, 6);
            // 
            // mnuExit
            // 
            this.mnuExit.Name = "mnuExit";
            this.mnuExit.Size = new System.Drawing.Size(228, 26);
            this.mnuExit.Text = "退出";
            this.mnuExit.Click += new System.EventHandler(this.OnExitClick);
            // 
            // mnuEdit
            // 
            this.mnuEdit.DropDownItems.AddRange(new System.Windows.Forms.ToolStripItem[] {
            this.mnuRegionSelect,
            this.mnuReleaseScope,
            this.mnuClearSelection,
            this.mnuEditSep1,
            this.mnuMirrorX,
            this.mnuMirrorY});
            this.mnuEdit.Name = "mnuEdit";
            this.mnuEdit.Size = new System.Drawing.Size(68, 24);
            this.mnuEdit.Text = "编辑(&E)";
            // 
            // mnuRegionSelect
            // 
            this.mnuRegionSelect.Name = "mnuRegionSelect";
            this.mnuRegionSelect.Size = new System.Drawing.Size(228, 26);
            this.mnuRegionSelect.Text = "框选区域 (Q)";
            this.mnuRegionSelect.Click += new System.EventHandler(this.OnRegionSelectClick);
            // 
            // mnuReleaseScope
            // 
            this.mnuReleaseScope.Name = "mnuReleaseScope";
            this.mnuReleaseScope.Size = new System.Drawing.Size(228, 26);
            this.mnuReleaseScope.Text = "解除框选限定";
            this.mnuReleaseScope.Click += new System.EventHandler(this.OnReleaseScopeClick);
            // 
            // mnuClearSelection
            // 
            this.mnuClearSelection.Name = "mnuClearSelection";
            this.mnuClearSelection.Size = new System.Drawing.Size(228, 26);
            this.mnuClearSelection.Text = "清空选点...";
            this.mnuClearSelection.Click += new System.EventHandler(this.OnClearSelectionClick);
            // 
            // mnuEditSep1
            // 
            this.mnuEditSep1.Name = "mnuEditSep1";
            this.mnuEditSep1.Size = new System.Drawing.Size(225, 6);
            // 
            // mnuMirrorX
            // 
            this.mnuMirrorX.Name = "mnuMirrorX";
            this.mnuMirrorX.Size = new System.Drawing.Size(228, 26);
            this.mnuMirrorX.Text = "水平翻转";
            this.mnuMirrorX.Click += new System.EventHandler(this.btnMirrorX_Click);
            // 
            // mnuMirrorY
            // 
            this.mnuMirrorY.Name = "mnuMirrorY";
            this.mnuMirrorY.Size = new System.Drawing.Size(228, 26);
            this.mnuMirrorY.Text = "垂直翻转";
            this.mnuMirrorY.Click += new System.EventHandler(this.btnMirrorY_Click);
            // 
            // mnuView
            // 
            this.mnuView.DropDownItems.AddRange(new System.Windows.Forms.ToolStripItem[] {
            this.mnuResetView,
            this.mnuShowBaseLayer,
            this.mnuViewSep1,
            this.mnuParams});
            this.mnuView.Name = "mnuView";
            this.mnuView.Size = new System.Drawing.Size(68, 24);
            this.mnuView.Text = "视图(&V)";
            // 
            // mnuResetView
            // 
            this.mnuResetView.Name = "mnuResetView";
            this.mnuResetView.Size = new System.Drawing.Size(228, 26);
            this.mnuResetView.Text = "重置视图";
            this.mnuResetView.Click += new System.EventHandler(this.btnRestView_Click);
            // 
            // mnuShowBaseLayer
            // 
            this.mnuShowBaseLayer.Checked = true;
            this.mnuShowBaseLayer.CheckOnClick = true;
            this.mnuShowBaseLayer.CheckState = System.Windows.Forms.CheckState.Checked;
            this.mnuShowBaseLayer.Name = "mnuShowBaseLayer";
            this.mnuShowBaseLayer.Size = new System.Drawing.Size(228, 26);
            this.mnuShowBaseLayer.Text = "显示底图";
            this.mnuShowBaseLayer.Click += new System.EventHandler(this.OnShowBaseLayerChanged);
            // 
            // mnuViewSep1
            // 
            this.mnuViewSep1.Name = "mnuViewSep1";
            this.mnuViewSep1.Size = new System.Drawing.Size(225, 6);
            // 
            // mnuParams
            // 
            this.mnuParams.Name = "mnuParams";
            this.mnuParams.Size = new System.Drawing.Size(228, 26);
            this.mnuParams.Text = "参数设置...";
            this.mnuParams.Click += new System.EventHandler(this.btnParamSetting_Click);
            // 
            // mnuHelp
            // 
            this.mnuHelp.DropDownItems.AddRange(new System.Windows.Forms.ToolStripItem[] {
            this.mnuHelpDoc,
            this.mnuAbout});
            this.mnuHelp.Name = "mnuHelp";
            this.mnuHelp.Size = new System.Drawing.Size(68, 24);
            this.mnuHelp.Text = "帮助(&H)";
            // 
            // mnuHelpDoc
            // 
            this.mnuHelpDoc.Name = "mnuHelpDoc";
            this.mnuHelpDoc.Size = new System.Drawing.Size(228, 26);
            this.mnuHelpDoc.Text = "操作说明 (F1)";
            this.mnuHelpDoc.Click += new System.EventHandler(this.OnHelpClick);
            // 
            // mnuAbout
            // 
            this.mnuAbout.Name = "mnuAbout";
            this.mnuAbout.Size = new System.Drawing.Size(228, 26);
            this.mnuAbout.Text = "关于";
            this.mnuAbout.Click += new System.EventHandler(this.OnAboutClick);
            // 
            // panelPathBar
            // 
            // 「当前工程」信息条：贴在菜单下面，显示工程根目录 + 一个「打开工程」按钮。
            // 用绝对定位 + Top|Left|Right 锚定，**不用 Dock** —— 菜单已经是 Dock=Top，
            // 两个 Dock=Top 控件谁先占位取决于 z-order，容易踩坑；这样写不依赖顺序。
            this.panelPathBar.Anchor = ((System.Windows.Forms.AnchorStyles)(((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Left) 
            | System.Windows.Forms.AnchorStyles.Right)));
            this.panelPathBar.BackColor = System.Drawing.SystemColors.Control;
            this.panelPathBar.Controls.Add(this.txtProjectPath);
            this.panelPathBar.Controls.Add(this.lblProjectPath);
            this.panelPathBar.Controls.Add(this.btnOpenProject);
            this.panelPathBar.Location = new System.Drawing.Point(0, 28);
            this.panelPathBar.Name = "panelPathBar";
            this.panelPathBar.Size = new System.Drawing.Size(1384, 32);
            this.panelPathBar.TabIndex = 31;
            // 
            // lblProjectPath
            // 
            this.lblProjectPath.AutoSize = true;
            this.lblProjectPath.Font = new System.Drawing.Font("微软雅黑", 9.75F);
            this.lblProjectPath.Location = new System.Drawing.Point(12, 8);
            this.lblProjectPath.Name = "lblProjectPath";
            this.lblProjectPath.Size = new System.Drawing.Size(68, 17);
            this.lblProjectPath.TabIndex = 0;
            this.lblProjectPath.Text = "工程路径：";
            // 
            // txtProjectPath
            // 
            // 只读：路径是"程序告诉用户"的信息，不是让用户手输的入口 —— 要换工程走「打开工程」。
            this.txtProjectPath.Anchor = ((System.Windows.Forms.AnchorStyles)(((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Left) 
            | System.Windows.Forms.AnchorStyles.Right)));
            this.txtProjectPath.Font = new System.Drawing.Font("微软雅黑", 9.75F);
            this.txtProjectPath.Location = new System.Drawing.Point(88, 5);
            this.txtProjectPath.Name = "txtProjectPath";
            this.txtProjectPath.ReadOnly = true;
            this.txtProjectPath.Size = new System.Drawing.Size(1164, 25);
            this.txtProjectPath.TabIndex = 1;
            // 
            // btnOpenProject
            // 
            // 图标在运行时用 GDI+ 现画（见 MainForm.CreateFolderIcon），不依赖任何外部资源文件
            this.btnOpenProject.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Right)));
            this.btnOpenProject.Font = new System.Drawing.Font("微软雅黑", 9.75F);
            this.btnOpenProject.ImageAlign = System.Drawing.ContentAlignment.MiddleLeft;
            this.btnOpenProject.Location = new System.Drawing.Point(1261, 4);
            this.btnOpenProject.Name = "btnOpenProject";
            this.btnOpenProject.Size = new System.Drawing.Size(112, 25);
            this.btnOpenProject.TabIndex = 2;
            this.btnOpenProject.Text = "打开工程";
            this.btnOpenProject.TextImageRelation = System.Windows.Forms.TextImageRelation.ImageBeforeText;
            this.btnOpenProject.UseVisualStyleBackColor = true;
            this.btnOpenProject.Click += new System.EventHandler(this.OnOpenProjectClick);
            // 
            // kWindowControl1
            // 
            // 四边锚定：画布占满"信息条以下、状态栏以上"的工作区，并随窗口缩放自动跟着变。
            this.kWindowControl1.Anchor = ((System.Windows.Forms.AnchorStyles)((((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Bottom) 
            | System.Windows.Forms.AnchorStyles.Left) 
            | System.Windows.Forms.AnchorStyles.Right)));
            this.kWindowControl1.BackColor = System.Drawing.Color.Black;
            this.kWindowControl1.WindowBackColor = System.Drawing.Color.Black;
            this.kWindowControl1.Location = new System.Drawing.Point(7, 66);
            this.kWindowControl1.Name = "kWindowControl1";
            this.kWindowControl1.Size = new System.Drawing.Size(1370, 886);
            this.kWindowControl1.TabIndex = 27;
            // 
            // statusStrip1
            // 
            this.statusStrip1.Items.AddRange(new System.Windows.Forms.ToolStripItem[] {
            this.toolStripStatusLabel1,
            this.lblSelInfo,
            this.lblLayerInfo,
            this.lblView});
            this.statusStrip1.Location = new System.Drawing.Point(0, 956);
            this.statusStrip1.Name = "statusStrip1";
            this.statusStrip1.Size = new System.Drawing.Size(1384, 25);
            this.statusStrip1.TabIndex = 13;
            this.statusStrip1.Text = "statusStrip1";
            // 
            // toolStripStatusLabel1
            // 
            // Spring = true：让"消息位"占满剩余宽度，把后面的信息位（已选 / 工程 / 缩放）
            // 一律推到最右侧。消息文字左对齐，两者互不挤占。
            this.toolStripStatusLabel1.Font = new System.Drawing.Font("Microsoft YaHei UI", 11F);
            this.toolStripStatusLabel1.Name = "toolStripStatusLabel1";
            this.toolStripStatusLabel1.Size = new System.Drawing.Size(0, 20);
            this.toolStripStatusLabel1.Spring = true;
            this.toolStripStatusLabel1.TextAlign = System.Drawing.ContentAlignment.MiddleLeft;
            // 
            // lblSelInfo
            // 
            this.lblSelInfo.Font = new System.Drawing.Font("Microsoft YaHei UI", 11F);
            this.lblSelInfo.Name = "lblSelInfo";
            this.lblSelInfo.Size = new System.Drawing.Size(0, 20);
            this.lblSelInfo.Text = "";
            // 
            // lblLayerInfo
            // 
            this.lblLayerInfo.Font = new System.Drawing.Font("Microsoft YaHei UI", 11F);
            this.lblLayerInfo.Name = "lblLayerInfo";
            this.lblLayerInfo.Size = new System.Drawing.Size(0, 20);
            this.lblLayerInfo.Text = "";
            // 
            // lblView
            // 
            this.lblView.Font = new System.Drawing.Font("Microsoft YaHei UI", 11F);
            this.lblView.Name = "lblView";
            this.lblView.Size = new System.Drawing.Size(0, 20);
            this.lblView.Text = "";
            // 
            // openFileDialog1
            // 
            this.openFileDialog1.FileName = "openFileDialog1";
            // 
            // MainForm
            // 
            this.AutoScaleDimensions = new System.Drawing.SizeF(96F, 96F);
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Dpi;
            this.BackColor = System.Drawing.SystemColors.AppWorkspace;
            this.ClientSize = new System.Drawing.Size(1384, 981);
            this.Controls.Add(this.kWindowControl1);
            this.Controls.Add(this.panelPathBar);
            this.Controls.Add(this.menuStrip1);
            this.Controls.Add(this.statusStrip1);
            this.ForeColor = System.Drawing.SystemColors.ControlText;
            this.MainMenuStrip = this.menuStrip1;
            this.Name = "MainForm";
            this.Text = "Gerber文件解析器V4.0";
            this.Load += new System.EventHandler(this.MainForm_Load);
            this.menuStrip1.ResumeLayout(false);
            this.menuStrip1.PerformLayout();
            this.statusStrip1.ResumeLayout(false);
            this.statusStrip1.PerformLayout();
            this.ResumeLayout(false);
            this.PerformLayout();

        }

        #endregion
        private System.Windows.Forms.MenuStrip menuStrip1;
        private System.Windows.Forms.ToolStripMenuItem mnuFile;
        private System.Windows.Forms.ToolStripMenuItem mnuNewProject;
        private System.Windows.Forms.ToolStripMenuItem mnuOpenProject;
        private System.Windows.Forms.ToolStripMenuItem mnuDeleteProject;
        private System.Windows.Forms.ToolStripSeparator mnuFileSep1;
        private System.Windows.Forms.ToolStripMenuItem mnuSave;
        private System.Windows.Forms.ToolStripMenuItem mnuSaveAs;
        private System.Windows.Forms.ToolStripSeparator mnuFileSep2;
        private System.Windows.Forms.ToolStripMenuItem mnuExit;
        private System.Windows.Forms.ToolStripMenuItem mnuEdit;
        private System.Windows.Forms.ToolStripMenuItem mnuRegionSelect;
        private System.Windows.Forms.ToolStripMenuItem mnuReleaseScope;
        private System.Windows.Forms.ToolStripMenuItem mnuClearSelection;
        private System.Windows.Forms.ToolStripSeparator mnuEditSep1;
        private System.Windows.Forms.ToolStripMenuItem mnuMirrorX;
        private System.Windows.Forms.ToolStripMenuItem mnuMirrorY;
        private System.Windows.Forms.ToolStripMenuItem mnuView;
        private System.Windows.Forms.ToolStripMenuItem mnuResetView;
        private System.Windows.Forms.ToolStripMenuItem mnuShowBaseLayer;
        private System.Windows.Forms.ToolStripSeparator mnuViewSep1;
        private System.Windows.Forms.ToolStripMenuItem mnuParams;
        private System.Windows.Forms.ToolStripMenuItem mnuHelp;
        private System.Windows.Forms.ToolStripMenuItem mnuHelpDoc;
        private System.Windows.Forms.ToolStripMenuItem mnuAbout;
        private System.Windows.Forms.ToolStripStatusLabel lblSelInfo;
        private System.Windows.Forms.ToolStripStatusLabel lblLayerInfo;
        private System.Windows.Forms.ToolStripStatusLabel lblView;
        private System.Windows.Forms.FolderBrowserDialog folderBrowserDialog1;
        private System.Windows.Forms.Panel panelPathBar;
        private System.Windows.Forms.Label lblProjectPath;
        private System.Windows.Forms.TextBox txtProjectPath;
        private System.Windows.Forms.Button btnOpenProject;
        private System.Windows.Forms.StatusStrip statusStrip1;
        private System.Windows.Forms.ToolStripStatusLabel toolStripStatusLabel1;
        private Korey.SmartWindow.WinForms.KWindowControl kWindowControl1;
        private System.Windows.Forms.OpenFileDialog openFileDialog1;
        private System.Windows.Forms.SaveFileDialog saveFileDialog1;
    }
}
