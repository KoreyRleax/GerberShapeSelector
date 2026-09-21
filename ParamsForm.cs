using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace GerberParserSmartV4._0
{
    public partial class ParamsForm : Form
    {
        private string _selectColor = "white";

        // h2 / h3 的画布配色。h1 就是上面的 _selectColor —— 它历史上叫"选中颜色"，
        // 改名会让老用户已经设过的值失效，所以保持原名不动（现在界面标签写着「h1 颜色」）。
        private string _headerH2Color = "";
        private string _headerH3Color = "";

        // 「默认工作路径」= 各工程目录的共同父目录。**只做对话框初值，不参与落点决策。**
        private string _defaultRootPath = "";

        public ParamsForm()
        {
            InitializeComponent();
        }

        /// <summary>
        /// 窗体加载：准备「默认工作路径」与三个颜色行的初始值。
        ///
        /// ⚠ 别把这里的路径与已删除的 DefaultSavePath 混为一谈。那个设置项当时**同时**承担了两个角色
        /// ——①「保存新工程」的落点、② 各类目录对话框的初值 —— 而 ① 是错的：它被
        /// MainForm.btnParamSetting_Click 拿去顶替 _currentTemplatePath，于是"打开工程 A →
        /// 改个颜色 → 点确定 → 再保存"会把内容写去别处（见
        /// MD文件汇总（AI）/问题&解决方案/参数设置确定按钮改写工程归属.md）。
        /// 现在只保留 ②：**它决定对话框停在哪，绝不决定文件写到哪**。
        /// </summary>
        private void ParamsForm_Load(object sender, EventArgs e)
        {
            // 默认工作路径：主窗体传进来的优先（与它当前用的那个保持一致），否则退回设置里持久化的值
            if (string.IsNullOrEmpty(_defaultRootPath))
                _defaultRootPath = Properties.Settings.Default.DefaultProjectRootPath;
            txtDefaultRootPath.Text = _defaultRootPath;

            _selectColor = Properties.Settings.Default.SelectedCircleColor;

            // h2 / h3：主窗体已经通过 SetHeaderColors 传进来的话就用那个值（与画布上正在用的保持一致），
            // 否则退回设置里持久化的值。这个顺序不能反 —— 反了会出现"预览块显示的颜色
            // 和画布上正在画的不一样"。h1 那行没有这个问题：它只有设置这一个来源。
            if (string.IsNullOrEmpty(_headerH2Color)) _headerH2Color = Properties.Settings.Default.HeaderH2Color;
            if (string.IsNullOrEmpty(_headerH3Color)) _headerH3Color = Properties.Settings.Default.HeaderH3Color;

            // 初始化颜色预览（三行都要刷，否则新加的两行只会显示 Designer 里的占位色）
            pnlColorPreview.BackColor = HalconColorToSystemColor(_selectColor);
            pnlColorPreviewH2.BackColor = HalconColorToSystemColor(_headerH2Color);
            pnlColorPreviewH3.BackColor = HalconColorToSystemColor(_headerH3Color);
        }

        public void SetSelectColor(string color)
        {
            _selectColor = color;
        }
        public string GetSelectColor()
        {
            return _selectColor;
        }

        /// <summary>
        /// 传入 h2 / h3 当前的颜色（由主窗体在 ShowDialog 之前调用）。
        /// h1 走 <see cref="SetSelectColor"/>，不用这里。
        /// </summary>
        public void SetHeaderColors(string h2, string h3)
        {
            _headerH2Color = h2;
            _headerH3Color = h3;

            // 窗体已经构造出来时顺带刷预览（ShowDialog 之前 Load 还没跑，所以这句是给
            // "窗体已显示过、再设一次颜色"这种用法兜底；重复刷没有副作用）。
            if (pnlColorPreviewH2 != null) pnlColorPreviewH2.BackColor = HalconColorToSystemColor(_headerH2Color);
            if (pnlColorPreviewH3 != null) pnlColorPreviewH3.BackColor = HalconColorToSystemColor(_headerH3Color);
        }

        public string GetHeaderH2Color()
        {
            return _headerH2Color;
        }

        public string GetHeaderH3Color()
        {
            return _headerH3Color;
        }

        /// <summary>传入「默认工作路径」的当前值（由主窗体在 ShowDialog 之前调用）。</summary>
        public void SetDefaultRootPath(string rootPath)
        {
            _defaultRootPath = rootPath ?? string.Empty;
            if (txtDefaultRootPath != null) txtDefaultRootPath.Text = _defaultRootPath;
        }

        /// <summary>取「默认工作路径」（用户可能刚改过）。主窗体在点确定后写进 Settings。</summary>
        public string GetDefaultRootPath()
        {
            return txtDefaultRootPath != null ? txtDefaultRootPath.Text.Trim() : _defaultRootPath;
        }

        /// <summary>
        /// 「浏览...」：挑一个目录作为默认工作路径。
        /// 用与「打开工程」同一个系统原生目录选择器，整个程序的"选目录"体验保持一致。
        /// </summary>
        private void btnBrowseRootPath_Click(object sender, EventArgs e)
        {
            string picked = FolderPicker.PickFolder(
                "选择默认工作路径 —— 存放各个工程目录的上级目录", txtDefaultRootPath.Text);
            if (!string.IsNullOrEmpty(picked)) txtDefaultRootPath.Text = picked;
        }

        /// <summary>
        /// 「确定」。颜色是"选中即存"的（<see cref="btnSelectColor_Click"/> 与
        /// <see cref="PickHeaderColor"/> 里都当场 Properties.Settings.Default.Save()），
        /// 所以这里**只**处理「默认工作路径」这一项，再把 DialogResult 置成 OK。
        ///
        /// 路径为什么是"点确定才存"、而不是像颜色那样"选中即存"：它是可手输的文本框，
        /// 输到一半的值不该被持久化。校验也放在这里 —— 用「浏览...」选出来的目录必然存在，
        /// 只有手输那条路需要拦。
        /// </summary>
        private void btnOk_Click(object sender, EventArgs e)
        {
            string root = GetDefaultRootPath();
            if (root.Length > 0 && !Directory.Exists(root))
            {
                MessageBox.Show(
                    "默认工作路径不存在：\r\n\r\n" + root + "\r\n\r\n" +
                    "请用「浏览...」选一个已存在的目录；或清空它（表示不设置，那就仍旧从上次的位置开始）。",
                    "路径无效", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                txtDefaultRootPath.Focus();
                return;   // 不关窗，让用户就地改
            }

            this.DialogResult = DialogResult.OK;
            this.Close();
        }

        private void btnSelectColor_Click(object sender, EventArgs e)
        {
            using (ColorDialog colorDialog = new ColorDialog())
            {
                colorDialog.Color = pnlColorPreview.BackColor;
                colorDialog.FullOpen = true;
                colorDialog.AnyColor = true;

                if (colorDialog.ShowDialog() == DialogResult.OK)
                {
                    _selectColor = ColorToHalconColor(colorDialog.Color);
                    pnlColorPreview.BackColor = colorDialog.Color;
                    // 立即保存颜色配置
                    Properties.Settings.Default.SelectedCircleColor = _selectColor;
                    Properties.Settings.Default.Save();
                }
            }
        }

        // ── 插针头类型的颜色（h2 / h3）──
        // 两个头共用一套实现，差别只有"写哪个字段、刷哪个预览块、存哪个设置项"。

        private void btnSelectColorH2_Click(object sender, EventArgs e)
        {
            PickHeaderColor("h2", pnlColorPreviewH2);
        }

        private void btnSelectColorH3_Click(object sender, EventArgs e)
        {
            PickHeaderColor("h3", pnlColorPreviewH3);
        }

        /// <summary>
        /// 给某个插针头挑颜色：弹系统调色板 → 转成控件库 / Halcon 语义的色名 → 刷预览 → **立即存设置**。
        ///
        /// "立即存"是跟随 h1 那行的既有行为（btnSelectColor_Click 也是当场 Save）：
        /// 参数窗体点「取消」不会回滚颜色 —— 两处保持一致，免得同一个窗体里两行的语义还不一样。
        /// </summary>
        private void PickHeaderColor(string header, Panel preview)
        {
            using (ColorDialog colorDialog = new ColorDialog())
            {
                colorDialog.Color = preview.BackColor;
                colorDialog.FullOpen = true;
                colorDialog.AnyColor = true;

                if (colorDialog.ShowDialog() != DialogResult.OK) return;

                string color = ColorToHalconColor(colorDialog.Color);
                preview.BackColor = colorDialog.Color;

                if (header == "h2")
                {
                    _headerH2Color = color;
                    Properties.Settings.Default.HeaderH2Color = color;
                }
                else
                {
                    _headerH3Color = color;
                    Properties.Settings.Default.HeaderH3Color = color;
                }

                Properties.Settings.Default.Save();
            }
        }

        // 将System.Drawing.Color转换为Halcon颜色字符串
        private string ColorToHalconColor(Color color)
        {
            // 优先匹配常用颜色名称（性能更好）
            if (color.R == 255 && color.G == 0 && color.B == 0) return "red";
            if (color.R == 0 && color.G == 255 && color.B == 0) return "green";
            if (color.R == 0 && color.G == 0 && color.B == 255) return "blue";
            if (color.R == 255 && color.G == 255 && color.B == 0) return "yellow";
            if (color.R == 255 && color.G == 0 && color.B == 255) return "magenta";
            if (color.R == 0 && color.G == 255 && color.B == 255) return "cyan";
            if (color.R == 255 && color.G == 255 && color.B == 255) return "white";
            if (color.R == 0 && color.G == 0 && color.B == 0) return "black";
            if (color.R == 128 && color.G == 128 && color.B == 128) return "gray";
            if (color.R == 255 && color.G == 165 && color.B == 0) return "orange";

            // 对于其他颜色，使用RGB值
            return $"#{color.R:X2}{color.G:X2}{color.B:X2}";
        }

        // 将Halcon颜色字符串转换为System.Drawing.Color
        private Color HalconColorToSystemColor(string halconColor)
        {
            switch (halconColor.ToLower())
            {
                case "red": return Color.Red;
                // ⚠ 不能用 Color.Green —— 那是 GDI+ 语义的 (0,128,0)，而 Halcon 调色板的 "green"
                //   是纯绿 (0,255,0)（控件库 KWindow.ParseColor 就按纯绿画）。用错会让预览色块
                //   比画布上的实际颜色明显发暗。ColorToHalconColor 那边匹配的是 (0,255,0)，两头要对齐。
                case "green": return Color.FromArgb(0, 255, 0);
                case "blue": return Color.Blue;
                case "yellow": return Color.Yellow;
                case "magenta": return Color.Magenta;
                case "cyan": return Color.Cyan;
                case "white": return Color.White;
                case "black": return Color.Black;
                case "gray": return Color.Gray;
                case "orange": return Color.Orange;
                default:
                    // 处理RGB格式 #RRGGBB
                    if (halconColor.StartsWith("#") && halconColor.Length == 7)
                    {
                        try
                        {
                            int r = Convert.ToInt32(halconColor.Substring(1, 2), 16);
                            int g = Convert.ToInt32(halconColor.Substring(3, 2), 16);
                            int b = Convert.ToInt32(halconColor.Substring(5, 2), 16);
                            return Color.FromArgb(r, g, b);
                        }
                        catch
                        {
                            return Color.White; // 默认白色
                        }
                    }
                    return Color.White; // 默认白色
            }
        }
    }
}


