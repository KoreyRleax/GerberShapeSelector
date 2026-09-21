using System;
using System.Drawing;
using System.IO;
using System.Windows.Forms;

namespace GerberParserSmartV4._0
{
    /// <summary>
    /// 「新建工程 —— 输入工程名」对话框。
    ///
    /// 新建工程的顺序是**先问名字、再选文件**（用户 2026-09-21 指定），
    /// 而且选完文件、图层载入完就**自动落盘**，不再需要用户回到主界面点一次「保存」。
    /// 既然载入完就要存，名字必须在那之前拿到 —— 这就是本对话框存在的理由。
    ///
    /// 这里**只问名字，不问位置**：位置优先取「默认工作路径 \ 工程名」，
    /// 只有没设默认路径、或那儿已经有个同名目录时，才退回去让用户选一次
    /// （见 MainForm.AutoSaveNewProject）。底部那行提示就是用来讲清楚这件事的。
    /// </summary>
    internal sealed class ProjectNameDialog : Form
    {
        private readonly TextBox _txtName = new TextBox();
        private readonly Label _lblPreview = new Label();
        private readonly Label _lblTip = new Label();
        private readonly Button _btnOk = new Button();
        private readonly Button _btnCancel = new Button();

        private readonly string _rootPath;

        /// <summary>用户确认的工程名。</summary>
        public string ProjectName { get; private set; }

        /// <param name="title">窗口标题</param>
        /// <param name="okText">确定按钮文案（如「下一步」）</param>
        /// <param name="initialName">工程名初值（可空）</param>
        /// <param name="rootPath">默认工作路径（用于预览"将创建在哪"；可空）</param>
        public ProjectNameDialog(string title, string okText, string initialName, string rootPath)
        {
            ProjectName = string.Empty;
            _rootPath = rootPath ?? string.Empty;

            Font = SystemFonts.MessageBoxFont;
            Text = title;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            StartPosition = FormStartPosition.CenterParent;
            MinimizeBox = false;
            MaximizeBox = false;
            ShowInTaskbar = false;
            ClientSize = new Size(520, 190);

            Label lblName = MakeLabel("工程名：", 20, 30, 72);
            Label lblPreviewCaption = MakeLabel("将创建在：", 20, 76, 72);

            _txtName.Location = new Point(96, 27);
            _txtName.Size = new Size(400, 23);
            _txtName.Text = initialName ?? string.Empty;

            _lblPreview.AutoSize = false;
            _lblPreview.Location = new Point(96, 76);
            _lblPreview.Size = new Size(400, 20);
            _lblPreview.TextAlign = ContentAlignment.MiddleLeft;
            _lblPreview.AutoEllipsis = true;

            _lblTip.AutoSize = false;
            _lblTip.Location = new Point(96, 100);
            _lblTip.Size = new Size(400, 20);
            _lblTip.TextAlign = ContentAlignment.MiddleLeft;
            _lblTip.ForeColor = Color.FromArgb(175, 95, 0);

            _btnCancel.Text = "取消";
            _btnCancel.Location = new Point(340, 138);
            _btnCancel.Size = new Size(78, 28);
            _btnCancel.DialogResult = DialogResult.Cancel;

            _btnOk.Text = okText;
            _btnOk.Location = new Point(424, 138);
            _btnOk.Size = new Size(78, 28);
            _btnOk.Click += OnOkClick;

            Controls.Add(lblName);
            Controls.Add(_txtName);
            Controls.Add(lblPreviewCaption);
            Controls.Add(_lblPreview);
            Controls.Add(_lblTip);
            Controls.Add(_btnCancel);
            Controls.Add(_btnOk);

            AcceptButton = _btnOk;
            CancelButton = _btnCancel;

            _txtName.TextChanged += OnNameChanged;
            UpdatePreview();

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

        /// <summary>
        /// 实时刷"将创建在哪"与确定按钮的可用性。
        ///
        /// 这里**不阻止**"目标目录已存在" —— 那是下一步的事（AutoSaveNewProject 会退回去
        /// 让用户选位置）。本对话框只保证名字本身合法。
        /// </summary>
        private void UpdatePreview()
        {
            string name = _txtName.Text.Trim();

            bool hasRoot = !string.IsNullOrEmpty(_rootPath) && Directory.Exists(_rootPath);
            _lblPreview.Text = (name.Length > 0 && hasRoot)
                ? Path.Combine(_rootPath, name)
                : (hasRoot ? "（请输入工程名）" : "（未设置默认工作路径）");
            _lblPreview.ForeColor = (name.Length > 0 && hasRoot)
                ? SystemColors.ControlText
                : SystemColors.GrayText;

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
                tip = "工程名不能以点或空格结尾";
                ok = false;
            }
            else if (!hasRoot)
            {
                // 不是错误，只是提前告诉用户"下一步还得选一次位置"
                tip = "尚未设置「默认工作路径」，选完文件后会问你存到哪（可在「参数设置」里设定，之后就不用再问）";
            }
            else if (Directory.Exists(Path.Combine(_rootPath, name)))
            {
                tip = "该目录已存在，选完文件后会让你重新选一个位置（不会直接覆盖）";
            }

            _lblTip.Text = tip;
            _btnOk.Enabled = ok;
        }

        private void OnOkClick(object sender, EventArgs e)
        {
            ProjectName = _txtName.Text.Trim();
            DialogResult = DialogResult.OK;
        }
    }
}
