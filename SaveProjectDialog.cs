using System;
using System.Drawing;
using System.IO;
using System.Windows.Forms;

namespace GerberParserSmartV4._0
{
    /// <summary>
    /// 「保存工程」对话框 —— 一次问清**工程名**与**保存位置**。
    ///
    /// 为什么要有它：工程名过去是**推**出来的 —— <see cref="MainForm"/> 的 GetProjectName()
    /// 在"新建尚未归属"时退化成"第一个 Gerber 文件**所在目录**的名字"，而保存那一步只让选
    /// 上级目录、工程名由程序拼上去，用户没有任何输入点。于是"素材目录叫 1516601-00-C"
    /// 就等于"工程只能叫 1516601-00-C"，想叫别的名字没有门路。
    ///
    /// 本对话框把工程名从"唯一答案"降级成"默认值"：预填推测值，但可改。
    /// 🔴 **落点规则一个字没改** —— 有归属仍是就地保存；本对话框只在
    /// 「新建工程后首次保存」与「另存到」这两处出现。
    ///
    /// 底部实时显示**完整落点**，让"会存到哪"一眼可见 —— 这是交接文档定下的原则：
    /// 落点必须让用户看得见，不能藏在一个他看不见也记不住的全局设置里。
    /// </summary>
    internal sealed class SaveProjectDialog : Form
    {
        private readonly TextBox _txtName = new TextBox();
        private readonly TextBox _txtDir = new TextBox();
        private readonly Label _lblPreview = new Label();
        private readonly Label _lblTip = new Label();
        private readonly Button _btnBrowse = new Button();
        private readonly Button _btnOk = new Button();
        private readonly Button _btnCancel = new Button();

        /// <summary>用户确认的工程名（= 目标目录名）。</summary>
        public string ProjectName { get; private set; }

        /// <summary>用户确认的上级目录。目标工程目录 = 本值 + 工程名。</summary>
        public string TargetParentDir { get; private set; }

        /// <param name="title">窗口标题</param>
        /// <param name="okText">确定按钮文案（「保存」/「另存到」）</param>
        /// <param name="initialName">工程名初值（推测值，可改）</param>
        /// <param name="initialParentDir">上级目录初值</param>
        public SaveProjectDialog(string title, string okText, string initialName, string initialParentDir)
        {
            ProjectName = string.Empty;
            TargetParentDir = string.Empty;

            // 用系统对话框字体 —— 看起来不像一个自绘的野窗体（要在创建子控件之前设，子控件才会继承）
            Font = SystemFonts.MessageBoxFont;
            Text = title;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            StartPosition = FormStartPosition.CenterParent;
            MinimizeBox = false;
            MaximizeBox = false;
            ShowInTaskbar = false;
            ClientSize = new Size(560, 218);

            Label lblName = MakeLabel("工程名：", 20, 24, 72);
            Label lblDir = MakeLabel("保存位置：", 20, 62, 72);
            Label lblPreviewCaption = MakeLabel("将保存到：", 20, 112, 72);

            _txtName.Location = new Point(96, 21);
            _txtName.Size = new Size(444, 23);
            _txtName.Text = initialName ?? string.Empty;

            _txtDir.Location = new Point(96, 59);
            _txtDir.Size = new Size(344, 23);
            _txtDir.ReadOnly = true;
            _txtDir.BackColor = SystemColors.Control;
            _txtDir.Text = initialParentDir ?? string.Empty;

            _btnBrowse.Text = "浏览...";
            _btnBrowse.Location = new Point(448, 58);
            _btnBrowse.Size = new Size(92, 25);
            _btnBrowse.Click += OnBrowseClick;

            Panel sep = new Panel();
            sep.BackColor = SystemColors.ControlDark;
            sep.Location = new Point(20, 100);
            sep.Size = new Size(520, 1);

            _lblPreview.AutoSize = false;
            _lblPreview.Location = new Point(96, 112);
            _lblPreview.Size = new Size(444, 20);
            _lblPreview.TextAlign = ContentAlignment.MiddleLeft;
            _lblPreview.AutoEllipsis = true;

            _lblTip.AutoSize = false;
            _lblTip.Location = new Point(96, 136);
            _lblTip.Size = new Size(444, 20);
            _lblTip.TextAlign = ContentAlignment.MiddleLeft;
            _lblTip.ForeColor = Color.FromArgb(175, 95, 0);   // 琥珀色：提示而已，不是错误红

            _btnCancel.Text = "取消";
            _btnCancel.Location = new Point(380, 172);
            _btnCancel.Size = new Size(78, 28);
            _btnCancel.DialogResult = DialogResult.Cancel;

            _btnOk.Text = okText;
            _btnOk.Location = new Point(462, 172);
            _btnOk.Size = new Size(78, 28);
            _btnOk.Click += OnOkClick;

            Controls.Add(lblName);
            Controls.Add(_txtName);
            Controls.Add(lblDir);
            Controls.Add(_txtDir);
            Controls.Add(_btnBrowse);
            Controls.Add(sep);
            Controls.Add(lblPreviewCaption);
            Controls.Add(_lblPreview);
            Controls.Add(_lblTip);
            Controls.Add(_btnCancel);
            Controls.Add(_btnOk);

            AcceptButton = _btnOk;
            CancelButton = _btnCancel;

            _txtName.TextChanged += OnNameChanged;
            UpdatePreview();

            // 光标直接停在名字末尾，方便接着改（而不是让人先去点一下那个文本框）
            Shown += OnShown;
        }

        private static Label MakeLabel(string text, int x, int y, int w)
        {
            Label lbl = new Label();
            lbl.AutoSize = false;              // Label 默认 AutoSize=true，会无视 Size
            lbl.Text = text;
            lbl.Location = new Point(x, y);
            lbl.Size = new Size(w, 20);
            lbl.TextAlign = ContentAlignment.MiddleLeft;
            return lbl;
        }

        private void OnShown(object sender, EventArgs e)
        {
            _txtName.Focus();
            _txtName.SelectionStart = _txtName.TextLength;
        }

        private void OnNameChanged(object sender, EventArgs e)
        {
            UpdatePreview();
        }

        /// <summary>目标工程目录（上级目录 + 工程名）；任一为空则返回空串。</summary>
        private string FullTargetPath
        {
            get
            {
                string name = _txtName.Text.Trim();
                string dir = _txtDir.Text.Trim();
                if (name.Length == 0 || dir.Length == 0) return string.Empty;
                return Path.Combine(dir, name);
            }
        }

        private void OnBrowseClick(object sender, EventArgs e)
        {
            string picked = FolderPicker.PickFolder("选择工程保存到的上级目录", _txtDir.Text);
            if (!string.IsNullOrEmpty(picked)) _txtDir.Text = picked;
        }

        /// <summary>
        /// 刷新落点预览、提示语与「确定」的可用性。
        ///
        /// 用 TextChanged 实时刷、而不是等点了「确定」才报错 —— 这几条校验（空名、非法字符、
        /// 目录不存在）都能在输入当场判定，一边输一边看到结果，比"点确定被弹回来"省事。
        /// </summary>
        private void UpdatePreview()
        {
            string name = _txtName.Text.Trim();
            string dir = _txtDir.Text.Trim();
            string full = FullTargetPath;

            _lblPreview.Text = full.Length > 0 ? full : "（请填写工程名并选择保存位置）";
            _lblPreview.ForeColor = full.Length > 0 ? SystemColors.ControlText : SystemColors.GrayText;

            string tip = string.Empty;
            bool ok = true;

            if (name.Length == 0)
            {
                tip = "工程名不能为空";
                ok = false;
            }
            else if (name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
            {
                tip = "工程名不能包含 \\ / : * ? \" < > | 这些字符";
                ok = false;
            }
            else if (name.EndsWith(".", StringComparison.Ordinal) ||
                     name.EndsWith(" ", StringComparison.Ordinal))
            {
                // Windows 会静默去掉结尾的点/空格，导致实际目录名与这里显示的不一致
                tip = "工程名不能以点或空格结尾";
                ok = false;
            }
            else if (dir.Length == 0 || !Directory.Exists(dir))
            {
                tip = "保存位置不存在，请用「浏览」重新选择";
                ok = false;
            }
            else if (Directory.Exists(full))
            {
                // 目录已存在**不阻止**：另存到同一位置正是"覆盖 / 改名"的正常用法。
                // 这里只提示，点「确定」时再确认一次。
                tip = "该目录已存在，保存会覆盖其中的 circles.json 与同名图层文件";
            }

            _lblTip.Text = tip;
            _btnOk.Enabled = ok;
        }

        private void OnOkClick(object sender, EventArgs e)
        {
            string full = FullTargetPath;
            if (full.Length == 0) return;

            if (Directory.Exists(full))
            {
                // 覆盖前确认一次。这个对话框也会被「首次保存」用到，那时目录已存在
                // 往往意味着用户挑错了地方（或者真要覆盖另一个工程）。
                DialogResult r = MessageBox.Show(
                    "目标目录已存在：\r\n\r\n" + full + "\r\n\r\n" +
                    "继续保存会覆盖其中的 circles.json 与同名图层文件。是否继续？",
                    "目录已存在", MessageBoxButtons.YesNo, MessageBoxIcon.Warning,
                    MessageBoxDefaultButton.Button2);
                if (r != DialogResult.Yes) return;
            }

            ProjectName = _txtName.Text.Trim();
            TargetParentDir = _txtDir.Text.Trim();
            DialogResult = DialogResult.OK;
        }
    }
}
