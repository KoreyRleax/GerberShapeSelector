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
        private string _savePath = "";
        private string _selectColor = "white";
        public ParamsForm()
        {
            InitializeComponent();
        }

        private void ParamsForm_Load(object sender, EventArgs e)
        {
            // 窗体加载时显示基础模板目录，而不是具体的模板文件夹
            string basePath = GetBaseTemplatePathFromSettings();
            txtSavePath.Text = basePath;
            _savePath = basePath;
            _selectColor = Properties.Settings.Default.SelectedCircleColor;
            // 初始化颜色预览
            pnlColorPreview.BackColor = HalconColorToSystemColor(_selectColor);
        }

        // 设置保存路径（由主窗体调用）
        public void SetSavePath(string path)
        {
            _savePath = path;
            txtSavePath.Text = path;
        }

        // 获取保存路径（由主窗体调用）
        public string GetSavePath()
        {
            return _savePath;
        }

        public void SetSelectColor(string color)
        {
            _selectColor = color;
        }
        public string GetSelectColor()
        {
            return _selectColor;
        }

        private void btnOk_Click(object sender, EventArgs e)
        {
            string inputPath = txtSavePath.Text.Trim();

            if (string.IsNullOrEmpty(inputPath))
            {
                MessageBox.Show("请输入有效的保存路径！", "提示",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            // 检查路径是否存在，如果不存在则创建
            try
            {
                if (!Directory.Exists(inputPath))
                {
                    var result = MessageBox.Show($"路径 '{inputPath}' 不存在，是否创建？",
                        "确认创建", MessageBoxButtons.YesNo, MessageBoxIcon.Question);

                    if (result == DialogResult.Yes)
                    {
                        Directory.CreateDirectory(inputPath);
                        _savePath = inputPath;
                        this.DialogResult = DialogResult.OK;
                        this.Close();
                    }
                    else
                    {
                        return;
                    }
                }
                else
                {
                    _savePath = inputPath;
                    this.DialogResult = DialogResult.OK;
                    this.Close();
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"路径创建失败: {ex.Message}", "错误",
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }
        // 浏览文件夹按钮点击事件
        private void btnBrowse_Click(object sender, EventArgs e)
        {
            using (FolderBrowserDialog folderDialog = new FolderBrowserDialog())
            {
                folderDialog.Description = "选择模板保存路径";
                folderDialog.ShowNewFolderButton = true;

                // 设置初始路径
                if (!string.IsNullOrEmpty(txtSavePath.Text) && Directory.Exists(txtSavePath.Text))
                {
                    folderDialog.SelectedPath = txtSavePath.Text;
                }
                else
                {
                    // 默认设置为桌面
                    folderDialog.SelectedPath = Environment.GetFolderPath(Environment.SpecialFolder.Desktop);
                }

                if (folderDialog.ShowDialog() == DialogResult.OK)
                {
                    string selectedPath = folderDialog.SelectedPath;
                    txtSavePath.Text = selectedPath;
                    _savePath = selectedPath;

                    // 可选：显示确认消息
                    KLog.Info($"已选择路径: {selectedPath}");
                }
            }
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

        // 从设置获取基础模板目录
        private string GetBaseTemplatePathFromSettings()
        {
            if (!string.IsNullOrEmpty(Properties.Settings.Default.DefaultSavePath) &&
                Directory.Exists(Properties.Settings.Default.DefaultSavePath))
            {
                return Properties.Settings.Default.DefaultSavePath;
            }

            // 默认路径
            return Path.Combine(Application.StartupPath, "template");
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


