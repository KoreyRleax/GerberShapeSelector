using GerberParserTool;
using Korey.SmartWindow.WinForms;
using Newtonsoft.Json;
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
    public partial class MainForm : Form
    {
        // Gerber光圈数据（**所有图层的聚合**，供包围盒计算与保存使用；绘制走 _layers）
        private List<Aperture> _apertures = new List<Aperture>();

        // 已载入的图层，顺序 = 加载顺序 = 绘制顺序（后加载的在上层）。
        // 这是多图层模型里的权威数据，_apertures 由它聚合而来。
        private List<LayerInfo> _layers = new List<LayerInfo>();

        private GerberParser _parser = new GerberParser();

        // 全量图形缓存：解析 Gerber / 加载模板后构建**一次**。
        // 原 GetAllCirclesFromApertures 是每次鼠标点击都重建这 15,359 个对象，
        // 并且每建一个就要在已选集合里线性查一遍（15,359 × 3,852 ≈ 2,960 万次比较/次点击）。
        private List<SelectedCircle> _allShapes = new List<SelectedCircle>();

        // 内容包围盒（世界坐标）缓存：镜像变换与 FitToWindow 都要用。
        // 原实现是在绘制循环里为每个图形各调一次 ViewportHelper.CalculateBoundingBox，
        // 退化成 O(N²) —— 15,359 × 15,359 ≈ 2.36 亿次迭代/帧，这就是"一点翻转就卡死"的根因。
        private RectangleF _contentBounds = RectangleF.Empty;

        // 选点相关
        // 注：原来的 _isSelectingMode（"是否处于选点模式"）已删除 —— 左键现在任何时候都能选点，
        //     不再需要"先进入模式"。
        private List<SelectedCircle> _selectedCircles = new List<SelectedCircle>();

        // 点击语义：true = 单选（切换一个点），false = 多选（切换"同类的一批点"）。
        // 原来由右侧「选点操作」面板的两个 RadioButton 承载；那两个按钮已随该分组一起移除，
        // 改由画布悬浮工具栏的两个 toggle 控制，状态就留在这一处。
        private bool _isSingleClickMode = true;
        private bool _isTemplateMode = false;

        // 「显示底图」开关。原来的真值存在右侧面板的两个 RadioButton 上（面板已删除），
        // 现在由本字段承载；画布悬浮工具栏的「底图」toggle、菜单「视图 → 显示底图」都指向它，
        // 三处入口统一走 SetShowBaseLayer()，不再各自赋值。
        private bool _showBaseLayer = true;
        // 区域选取相关
        private bool _isRegionSelectingMode = false;
        // 「框选限定」范围（原始坐标系，未翻转）：最近一次框选留下的矩形。
        //
        // 它**只影响一件事** —— 多选模式下"同类扩散"的搜索范围（见 HandleMultipleSelect）。
        // **不**参与点击命中测试：框外一直点得中，旧设计"框外点了没反应"的问题不会回来。
        // 画面上的红虚线框（DrawSelectedRect）就是它的指示灯：框在 = 限定生效，
        // 框不在 = 作用于全板；两者严格同步（框外点击会自动清空本字段，框随之消失）。
        private RectangleF _actualSelectedRect = RectangleF.Empty;//实际坐标系下的选取矩形

        // 框选撤销栈。只收录"被框选改变了选中状态"的图形及其**变化前**状态，
        // 一次框选无论命中多少点都只压入一条快照，所以内存开销与框选次数成正比、与命中数成一次线性。
        // 逐点选点不入栈（否则每点一下就压一条，既无意义又爆栈）。
        private sealed class SelectionSnapshot
        {
            public SelectedCircle Shape;
            public bool WasSelected;
        }
        private readonly List<List<SelectionSnapshot>> _regionUndoStack = new List<List<SelectionSnapshot>>();
        private const int RegionUndoDepth = 20;

        // 缩放 / 平移 / 可见范围由 kWindowControl1.Viewport 统一维护（唯一数据源）。
        // 不再需要 _halconScale / _halconOffsetX / _halconOffsetY 那套手工状态 ——
        // 原先"几何按一套变换烘焙、视口按另一套变换平移"的双状态不同步问题从结构上消失。
        //关联模板文件路径
        private string _templateGerberFilePath = string.Empty;
        //当前模板文件夹保存路径
        private string _currentTemplatePath = string.Empty;
        //首次加载标记
        private bool _firstLoadTag = false;
        //选中颜色
        private string _selectColor = Properties.Settings.Default.SelectedCircleColor; // 默认白色
        // 翻转状态标记
        private bool _isMirroredX = false;
        private bool _isMirroredY = false;

        [Serializable]
        public class SelectedCircle
        {
            public string ID {  get; set; }
            public double X { get; set; }
            public double Y { get; set; }
            public double Diameter { get; set; }
            public double Width { get; set; }     // 用于矩形
            public double Height { get; set; }    // 用于矩形
            public double Rotation { get; set; }  // 用于椭圆旋转角度
            public ApertureShape Shape { get; set; } // 形状类型
            public bool IsSelected { get; set; }

            // ── 多图层支持 ──
            /// <summary>
            /// 该图形所属的图层文件名（如 "1516601-00-C_01.GBL"）。空字符串 = 单图层 / 未知。
            ///
            /// 用**文件名**而不是图层序号做图层身份：图层顺序会随导入顺序变化，
            /// 而文件名在一个工程目录内是稳定的。
            /// 它同时参与图形的唯一键 —— 见 ShapeKey()。
            ///
            /// 用属性初始化器而**不是**写在构造函数里：本类有 4 个构造函数，
            /// 且三个带参构造都没有 `: this()` 链到无参构造，写在无参构造里必然漏。
            /// </summary>
            public string Layer { get; set; } = string.Empty;

            // Parameterless ctor required for JSON deserialization
            public SelectedCircle()
            {
                Shape = ApertureShape.Circle; // 默认形状为圆形
                Rotation = 0;
                ID = string.Empty;
            }

            // 圆形构造函数
            public SelectedCircle(double x, double y, double diameter)
            {
                X = x;
                Y = y;
                Diameter = diameter;
                Shape = ApertureShape.Circle;
                IsSelected = false;
                Rotation = 0;
                ID = string.Empty;
            }

            // 矩形构造函数
            public SelectedCircle(double x, double y, double width, double height)
            {
                X = x;
                Y = y;
                Width = width;
                Height = height;
                Shape = ApertureShape.Rectangle;
                IsSelected = false;
                Rotation = 0;
                ID = string.Empty;
            }
            // 椭圆构造函数
            public SelectedCircle(double x, double y, double width, double height, double rotation)
            {
                X = x;
                Y = y;
                Width = width;
                Height = height;
                Rotation = rotation;
                Shape = ApertureShape.Oval;
                IsSelected = false;
                ID = string.Empty;
            }
        }

        /// <summary>
        /// 一个图层 = 一个 Gerber / 钻孔文件。
        ///
        /// 多图层模型里它是**一等公民**：
        ///   · 图形靠 SelectedCircle.Layer 回指到本类的 FileName（用文件名而不是序号，
        ///     因为图层顺序会随加载顺序变化）；
        ///   · 颜色按它在 _layers 里的**序号**分配 —— 同一图层内所有图形同色，不同图层不同色。
        /// </summary>
        private sealed class LayerInfo
        {
            /// <summary>图层文件全路径。</summary>
            public string FilePath = string.Empty;

            /// <summary>显示名（含扩展名）。同时充当图形的 Layer 标识 —— 同一工程目录内文件名唯一。</summary>
            public string FileName = string.Empty;

            /// <summary>
            /// 是否显示。**只影响绘制**（该图层的图形与已选点都不画），
            /// 不改变 _selectedCircles，也不影响导出结果 —— 见 README 规则 R1。
            /// </summary>
            public bool IsVisible = true;

            /// <summary>该图层解析出的光圈（每个光圈含它在该层上的全部位置）。</summary>
            public List<Aperture> Apertures = new List<Aperture>();
        }

        /// <summary>
        /// 图层容器里的一行：左边文件名，右边一个复选框。
        ///
        /// 为什么自绘而不用 CheckBox / Button：WinForms 里这两个都**不支持透明背景**
        /// （CheckBox 同样没声明 SupportsTransparentBackColor），直接放在黑画布上会是一块不透明的方块。
        /// 做法完全照搬控件库的 OverlayToolbarButton：Control 自绘 + SupportsTransparentBackColor，
        /// OnPaintBackground 只调 base 让父控件（半透明容器 → 画布）先画，本层不叠加任何底色。
        /// </summary>
        private sealed class LayerRowControl : Control
        {
            private const int BoxSize = 12;      // 复选框边长（像素）

            private bool _hover;
            private bool _checked;

            /// <summary>这一行对应 _layers 里的序号。跟着图层走，不跟着行号走。</summary>
            public int LayerIndex = -1;

            /// <summary>勾选变化：(图层序号, 是否可见)。由宿主转发给 SetLayerVisible。</summary>
            public event Action<int, bool> VisibilityChanged;

            public LayerRowControl(string fileName, bool isVisible, int layerIndex)
            {
                SetStyle(ControlStyles.UserPaint
                       | ControlStyles.AllPaintingInWmPaint
                       | ControlStyles.OptimizedDoubleBuffer
                       | ControlStyles.SupportsTransparentBackColor
                       | ControlStyles.ResizeRedraw, true);

                BackColor = Color.Transparent;
                ForeColor = KWindowOptions.ToolbarButtonForeColor;
                Cursor = Cursors.Hand;
                TabStop = false;

                LayerIndex = layerIndex;
                _checked = isVisible;
                Text = fileName;

                Height = KWindowOptions.ToolbarButtonHeight;
                Width = MeasureRowWidth(fileName);
            }

            public bool Checked
            {
                get { return _checked; }
                set
                {
                    if (_checked == value) return;
                    _checked = value;
                    Invalidate();
                    if (VisibilityChanged != null) VisibilityChanged(LayerIndex, _checked);
                }
            }

            /// <summary>行宽 = 左右留白 + 文字 + 与复选框的间隔 + 复选框。</summary>
            private static int MeasureRowWidth(string text)
            {
                Size t = TextRenderer.MeasureText(string.IsNullOrEmpty(text) ? "M" : text,
                                                  SystemFonts.DefaultFont,
                                                  new Size(int.MaxValue, int.MaxValue),
                                                  TextFormatFlags.NoPadding);
                return KWindowOptions.ToolbarButtonPaddingX * 2 + t.Width + 8 + BoxSize;
            }

            protected override void OnMouseEnter(EventArgs e) { _hover = true; Invalidate(); base.OnMouseEnter(e); }

            protected override void OnMouseLeave(EventArgs e) { _hover = false; Invalidate(); base.OnMouseLeave(e); }

            protected override void OnClick(EventArgs e)
            {
                Checked = !Checked;      // 触发 VisibilityChanged
                base.OnClick(e);
            }

            protected override void OnPaint(PaintEventArgs e)
            {
                if (_hover)
                {
                    Color hover = KWindowOptions.ToolbarButtonHoverColor;
                    if (hover.A > 0)
                    {
                        using (var brush = new SolidBrush(hover)) e.Graphics.FillRectangle(brush, ClientRectangle);
                    }
                }

                Color fore = Enabled ? ForeColor : Color.FromArgb(120, ForeColor);

                // 文字（左），绘制与测量统一用 SystemFonts.DefaultFont，避免测量的宽度和实际画出来的对不上
                var textRect = new Rectangle(
                    KWindowOptions.ToolbarButtonPaddingX,
                    0,
                    Width - BoxSize - KWindowOptions.ToolbarButtonPaddingX * 2 - 8,
                    Height);
                TextRenderer.DrawText(e.Graphics, Text, SystemFonts.DefaultFont, textRect, fore,
                                      TextFormatFlags.Left | TextFormatFlags.VerticalCenter
                                    | TextFormatFlags.NoPadding | TextFormatFlags.EndEllipsis);

                // 复选框（右）
                int boxX = Width - BoxSize - KWindowOptions.ToolbarButtonPaddingX;
                int boxY = (Height - BoxSize) / 2;
                var box = new Rectangle(boxX, boxY, BoxSize - 1, BoxSize - 1);

                using (var pen = new Pen(fore, 1f))
                {
                    e.Graphics.DrawRectangle(pen, box);
                }

                if (_checked)
                {
                    // 勾：两笔折线
                    using (var pen = new Pen(fore, 1.6f))
                    {
                        e.Graphics.DrawLines(pen, new[]
                        {
                            new Point(boxX + 2, boxY + BoxSize / 2 - 1),
                            new Point(boxX + BoxSize / 2 - 1, boxY + BoxSize - 4),
                            new Point(boxX + BoxSize - 3, boxY + 2)
                        });
                    }
                }
            }
        }

        // Data container for JSON serialization
        private class MirrorState
        {
            public bool IsMirroredX { get; set; }
            public bool IsMirroredY { get; set; }
        }

        private class TemplateData
        {
            public string TemplateName { get; set; }
            public List<SelectedCircle> Circles { get; set; }
            public string SaveTime { get; set; }
            public int TotalCount { get; set; }
            public int SelectedCount { get; set; }
            public string Version { get; set; }
            public MirrorState MirrorState { get; set; }

            // 新增字段用于统计
            public int CircleCount { get; set; }
            public int RectangleCount { get; set; }
            public int OvalCount { get; set; }
            public string SourceFile { get; set; }
        }


        public MainForm()
        {
            InitializeComponent();

            // 绘制入口：控件每次重绘都会回调，宿主在这里用**世界坐标**画全部图元。
            // ⚠ 回调里不要直接改 UI（状态栏文字等）：会触发重绘 → 递归。
            //   状态栏刷新统一走 kWindowControl1.ViewChanged。
            kWindowControl1.PaintContent += OnPaintContent;

            // 交互：滚轮缩放（以鼠标为锚点）、右键拖拽平移、中键重置视图 —— 全部内建。
            // 左键控件不做任何内建动作，只把**世界坐标**抛出来，行为完全由宿主决定。
            kWindowControl1.KMouseDown += KWindowControl_KMouseDown;
            kWindowControl1.ViewChanged += OnViewChanged;

            // 框选：非阻塞替代原来的 HOperatorSet.DrawRectangle1
            kWindowControl1.Rectangle1Selected += KWindowControl_Rectangle1Selected;
            kWindowControl1.Rectangle1Cancelled += KWindowControl_Rectangle1Cancelled;

            // 右键"未拖动"时弹上下文菜单（拖动则是平移，控件用 3px 位移阈值区分）
            kWindowControl1.ContextMenuRequested += KWindowControl_ContextMenuRequested;

            this.StartPosition = FormStartPosition.Manual;

            UpdateScaleLabel();

            // 加载保存的路径
            LoadSavedPath();
        }

        private void OnViewChanged(object sender, EventArgs e)
        {
            // 视口变化（缩放 / 平移 / 适配）→ 刷新状态栏。
            // 放在这里而不是绘制回调里，是为了避免"绘制 → 改 UI → 重绘"的递归。
            UpdateScaleLabel();
        }

        private void MainForm_Load(object sender, EventArgs e)
        {
            // 悬浮工具栏与图层容器都是运行时 new 出来再挂上去的（按钮按行按需生成，设计器无法序列化），
            // 所以必须在这里、控件创建之后构建。
            BuildOverlayToolbar();
            BuildLayerPanel();

            // 「打开工程」按钮的文件夹图标同样是运行时现画的（不引入外部资源文件）
            btnOpenProject.Image = CreateFolderIcon(16);

            UpdateProjectLabel();   // 信息条先填上"未打开工程"

            // 启动记忆：自动打开上次的工程
            RestoreLastProject();
        }

        #region 启动记忆（上次打开的工程）

        /// <summary>
        /// 记住"上次打开的工程"。只存最后一条 —— 目的是"重启接着干"，不是做历史列表。
        /// kind 取 "gerber" / "template"，path 分别是 Gerber 文件全路径 / 模板文件夹全路径。
        /// </summary>
        private void RememberLastProject(string kind, string path)
        {
            try
            {
                Properties.Settings.Default.LastProjectKind = kind;
                Properties.Settings.Default.LastProjectPath = path;
                Properties.Settings.Default.Save();
                KLog.Info($"记住上次工程：{kind} = {path}");
            }
            catch (Exception ex)
            {
                // 记不住不影响当前这次操作，不该因为写配置失败打断用户
                KLog.Error("保存上次工程信息失败", ex);
            }
        }

        /// <summary>
        /// 启动时自动恢复上次打开的工程。
        ///
        /// 路径已失效时**直接清掉记录**并说明一句 —— 否则每次开机都要对着一个不存在的路径
        /// 失败一次，用户还得自己去找原因。
        /// </summary>
        private void RestoreLastProject()
        {
            string kind = Properties.Settings.Default.LastProjectKind;
            string path = Properties.Settings.Default.LastProjectPath;

            if (string.IsNullOrEmpty(kind) || string.IsNullOrEmpty(path)) return;

            try
            {
                if (kind == "template")
                {
                    if (!Directory.Exists(path)) { ForgetLastProject(path, "工程目录已不存在"); return; }

                    KLog.Info($"启动恢复：打开上次的工程 {path}");
                    LoadProjectFromFolder(path);            // 目录级：载入全部图层 + circles.json
                }
                else if (kind == "gerber")
                {
                    if (!File.Exists(path)) { ForgetLastProject(path, "Gerber 文件已不存在"); return; }

                    KLog.Info($"启动恢复：载入上次的 Gerber {path}");
                    LoadProjectFromFiles(new[] { path });   // 单文件也走多图层路径，得到一个单图层工程
                }
            }
            catch (Exception ex)
            {
                KLog.Error("恢复上次工程失败", ex);
                toolStripStatusLabel1.Text = $"恢复上次工程失败：{ex.Message}";
            }
        }

        /// <summary>清掉失效的"上次工程"记录，避免每次启动都尝试同一个不存在的路径。</summary>
        private void ForgetLastProject(string path, string reason)
        {
            KLog.Info($"启动恢复跳过（{reason}）：{path}");
            Properties.Settings.Default.LastProjectKind = string.Empty;
            Properties.Settings.Default.LastProjectPath = string.Empty;
            Properties.Settings.Default.Save();
            toolStripStatusLabel1.Text = $"上次的工程未自动打开：{reason}";
        }

        #endregion

        #region 画布悬浮工具栏

        private OverlayToolbar _overlayToolbar;
        private OverlayToolbarButton _btnSingleMode;
        private OverlayToolbarButton _btnMultiMode;
        private OverlayToolbarButton _btnShowBaseLayer;

        // 画布右上角的图层容器（纵列表：每行 = 文件名 + 复选框）。
        // 它**不是**用 AttachOverlayToolbar 挂的 —— 那个 API 是单浮层，会把上面这条工具栏顶掉。
        // 详见 BuildLayerPanel() 的注释。
        private OverlayToolbar _layerPanel;

        // 防递归：toggle 与「底图显示」的 RadioButton 共享同一份状态，互相赋值会触发对方的
        // CheckedChanged。不加这个标志会形成"点 A 设 B、B 的回调又把 A 设回来"的回环。
        private bool _syncingClickMode = false;
        private bool _syncingBaseLayer = false;

        /// <summary>
        /// 构建贴在画布上沿的悬浮工具栏。
        ///
        /// 取舍：这里只放**高频、且与"看图 / 选点"直接相关**的动作 —— 手不用离开画布。
        /// 低频与配置类的（打开文件、保存、参数设置、翻转…）留在右键菜单与右侧面板。
        /// AttachOverlayToolbar 会自动同步 ViewPadding 做"视口让位"：工具栏只压留白、不压图。
        /// </summary>
        private void BuildOverlayToolbar()
        {
            _overlayToolbar = new OverlayToolbar();

            // ① 点击语义：单选 / 多选（互斥的两个 toggle）
            _btnSingleMode = _overlayToolbar.AddToggleButton("单选", true, (s, e) => SetClickMode(true));
            _btnMultiMode = _overlayToolbar.AddToggleButton("多选", false, (s, e) => SetClickMode(false));

            _overlayToolbar.AddSeparator();

            // ② 框选与限定
            _overlayToolbar.AddButton("框选区域 (Q)", (s, e) => StartRegionSelectingMode());
            _overlayToolbar.AddButton("解除限定", (s, e) => ReleaseRegionScope());

            _overlayToolbar.AddSeparator();

            // ③ 清空与底图
            _overlayToolbar.AddButton("清空选点", (s, e) => ClearAllSelectedPoints());
            _btnShowBaseLayer = _overlayToolbar.AddToggleButton("底图", _showBaseLayer, (s, e) =>
            {
                // 三处入口（本 toggle / 菜单项 / 右键菜单）统一走 SetShowBaseLayer，不再各自赋值
                SetShowBaseLayer(_btnShowBaseLayer.Checked);
            });

            _overlayToolbar.AddSeparator();
            _overlayToolbar.AddLabel("F1 操作说明");

            kWindowControl1.AttachOverlayToolbar(_overlayToolbar, ToolbarEdge.Top);
        }

        // ───────── 画布右上角：图层容器 ─────────

        /// <summary>
        /// 构建画布右上角的图层容器。
        ///
        /// ⚠ 为什么不用 AttachOverlayToolbar：那个 API 是**单浮层**的 —— 它内部第一句就是
        /// DetachOverlayToolbar()，且只用单个 _toolbar 字段保存，挂第二个会把画布顶部已有的
        /// 工具栏顶掉。所以这里直接 Controls.Add 成子控件、手动定位。
        /// 代价是没有"视口让位"（容器会压住右上角的图）与"滚轮转发"——
        /// 前者影响很小（容器不宽，且要看某处时本来就要看清全图），后者把鼠标移开即可。
        ///
        /// 外壳复用 OverlayToolbar，是为了直接得到它那套半透明底 + 淡边，
        /// 以及正确的"透明链"实现（它已经 SetStyle(SupportsTransparentBackColor)）。
        /// 只需把 FlowDirection 从横向改成纵向，横条就变成列表容器。
        /// </summary>
        private void BuildLayerPanel()
        {
            _layerPanel = new OverlayToolbar();
            _layerPanel.FlowDirection = FlowDirection.TopDown;   // 横条 → 纵列表
            _layerPanel.WrapContents = false;
            _layerPanel.AutoSize = true;
            _layerPanel.AutoSizeMode = AutoSizeMode.GrowAndShrink;
            _layerPanel.Visible = false;                         // 没有工程时不显示

            kWindowControl1.Controls.Add(_layerPanel);
            kWindowControl1.Resize += (s, e) => RepositionLayerPanel();

            RefreshLayerPanel();
        }

        /// <summary>按当前 _layers 重建容器内容。工程载入后调用。</summary>
        private void RefreshLayerPanel()
        {
            if (_layerPanel == null || _layerPanel.IsDisposed) return;

            _layerPanel.SuspendLayout();
            try
            {
                // 先 Dispose 旧行：只 Controls.Clear() 的话，控件对象不会释放（每换一次工程漏一批）
                for (int i = _layerPanel.Controls.Count - 1; i >= 0; i--)
                {
                    Control old = _layerPanel.Controls[i];
                    _layerPanel.Controls.RemoveAt(i);
                    old.Dispose();
                }

                if (_layers.Count == 0)
                {
                    _layerPanel.Visible = false;
                    return;
                }

                for (int i = 0; i < _layers.Count; i++)
                {
                    LayerInfo layer = _layers[i];
                    var row = new LayerRowControl(layer.FileName, layer.IsVisible, i);
                    row.VisibilityChanged += OnLayerRowVisibilityChanged;
                    _layerPanel.Controls.Add(row);
                }

                _layerPanel.Visible = true;
            }
            finally
            {
                _layerPanel.ResumeLayout(true);
            }

            RepositionLayerPanel();
        }

        /// <summary>把容器贴到画布右上角。画布尺寸变化时也会重算。</summary>
        private void RepositionLayerPanel()
        {
            if (_layerPanel == null || _layerPanel.IsDisposed) return;

            int x = kWindowControl1.ClientSize.Width - _layerPanel.Width - 10;
            int y = 46;   // 让开画布顶部那条悬浮工具栏（它在 margin=10 处，高约 34px）

            _layerPanel.Location = new Point(Math.Max(0, x), Math.Max(0, y));
            _layerPanel.BringToFront();
        }

        /// <summary>容器里某一行被点击：切换该图层的显示。</summary>
        private void OnLayerRowVisibilityChanged(int layerIndex, bool visible)
        {
            if (layerIndex < 0 || layerIndex >= _layers.Count) return;

            _layers[layerIndex].IsVisible = visible;
            KLog.Info($"图层显隐：{_layers[layerIndex].FileName} = {visible}");

            // 规则 R1：可见性只影响绘制 —— 不碰 _selectedCircles，也不碰 _apertures（包围盒因此不会跳）
            RefreshKWindow();
        }

        /// <summary>
        /// 切换"点击语义"（单选 / 多选）。两个 toggle 互斥，真正状态存在 _isSingleClickMode。
        /// 整个方法用 _syncingClickMode 包住：给 toggle 赋 Checked 会触发它自己的 CheckedChanged，
        /// 不挡的话会回环。
        /// </summary>
        private void SetClickMode(bool single)
        {
            if (_syncingClickMode) return;
            _syncingClickMode = true;
            try
            {
                _isSingleClickMode = single;
                if (_btnSingleMode != null) _btnSingleMode.Checked = single;
                if (_btnMultiMode != null) _btnMultiMode.Checked = !single;
            }
            finally { _syncingClickMode = false; }

            UpdateSelectionStatusText();
        }

        /// <summary>解除「框选限定」。工具栏按钮 / Esc / 空格 / 右键菜单共用这一个入口。</summary>
        private void ReleaseRegionScope()
        {
            ClearSelectedRect();
            UpdateSelectionStatusText("已解除框选限定");
        }

        #endregion

        #region 画布右键菜单

        private ContextMenuStrip _canvasMenu;

        /// <summary>
        /// 右键菜单。挂在控件的 ContextMenuRequested 上 —— 那个事件只在**右键未拖动**时抛，
        /// 拖动则是平移（控件用 KWindowOptions.PanDragThreshold = 3px 区分），两者不冲突。
        ///
        /// 菜单按**上下文**生成：光标下有没有图形、当前有没有框选限定，决定出现哪些项。
        /// 它最大的价值是"免去先切单选 / 多选" —— 「选中同类全部」在这里直接可用。
        /// </summary>
        private void KWindowControl_ContextMenuRequested(double x, double y)
        {
            try
            {
                // 每次重建（项随上下文变）。先 Dispose 上一个，避免反复右键累积 GDI 资源。
                _canvasMenu?.Dispose();
                _canvasMenu = new ContextMenuStrip();

                // ---------- 针对光标下那个图形的操作 ----------
                double actualX, actualY;
                ReverseMirrorTransform(x, y, out actualX, out actualY);
                double tolerance = Math.Max(kWindowControl1.Viewport.ToWorldLength(4), 1e-9);
                SelectedCircle hit = ShapeQuery.HitTest(_allShapes, actualX, actualY, tolerance);

                if (hit != null)
                {
                    string act = hit.IsSelected ? "取消" : "选中";
                    _canvasMenu.Items.Add($"{act}此点", null, (s, e) => ApplyMenuPointAction(hit, false));
                    _canvasMenu.Items.Add($"{act}同类全部", null, (s, e) => ApplyMenuPointAction(hit, true));
                }
                else
                {
                    _canvasMenu.Items.Add(new ToolStripMenuItem("（此处没有图形）") { Enabled = false });
                }

                _canvasMenu.Items.Add(new ToolStripSeparator());

                // ---------- 框选限定 ----------
                if (_actualSelectedRect.IsEmpty)
                {
                    _canvasMenu.Items.Add("框选区域 (Q)", null, (s, e) => StartRegionSelectingMode());
                }
                else
                {
                    int scopeCount = GetCirclesInSelectedRegion(_allShapes, _actualSelectedRect).Count;
                    _canvasMenu.Items.Add($"解除框选限定（范围内 {scopeCount} 个点）",
                        null, (s, e) => ReleaseRegionScope());
                }

                _canvasMenu.Items.Add(new ToolStripSeparator());

                // ---------- 视图 ----------
                _canvasMenu.Items.Add("重置视图", null, (s, e) =>
                {
                    ResetMirrorState();
                    ResetViewToCenter();
                    UpdateScaleLabel();
                });
                _canvasMenu.Items.Add("水平翻转", null, (s, e) => btnMirrorX_Click(s, e));
                _canvasMenu.Items.Add("垂直翻转", null, (s, e) => btnMirrorY_Click(s, e));

                var showBase = new ToolStripMenuItem("显示底图")
                {
                    CheckOnClick = true,
                    Checked = _showBaseLayer
                };
                showBase.CheckedChanged += (s, e) => SetShowBaseLayer(showBase.Checked);
                _canvasMenu.Items.Add(showBase);

                _canvasMenu.Items.Add(new ToolStripSeparator());

                var clearItem = new ToolStripMenuItem("清空选点…") { Enabled = _selectedCircles.Count > 0 };
                clearItem.Click += (s, e) => ClearAllSelectedPoints();
                _canvasMenu.Items.Add(clearItem);

                _canvasMenu.Show(kWindowControl1, kWindowControl1.PointToClient(Cursor.Position));
            }
            catch (Exception ex)
            {
                KLog.Error("弹出右键菜单失败", ex);
            }
        }

        /// <summary>
        /// 右键菜单的两条快捷选择：只作用于这一个点 / 作用于全部同类点。
        ///
        /// 它们**不受当前点击语义影响** —— 这正是放进右键菜单的意义：
        /// 不用先去工具栏切「单选 / 多选」，随手右键就能做这两种切换。
        /// </summary>
        private void ApplyMenuPointAction(SelectedCircle hit, bool sameType)
        {
            if (!sameType)
            {
                HandleSingleSelect(hit, _allShapes);
                RefreshKWindow();
                UpdateSelectionStatusText("右键：已切换此点");
                return;
            }

            // 同类操作沿用与左键完全相同的规则：落在框外就先解除限定，再按全板扩散
            bool released = ReleaseScopeIfOutside(hit);
            string note = HandleMultipleSelect(hit, _allShapes);

            RefreshKWindow();
            UpdateSelectionStatusText(released ? note + " · 已解除框选限定（点在框外）" : note);
        }

        #endregion

        #region 绘制（Korey.SmartWindow · 世界坐标）

        /// <summary>
        /// 光圈配色表（按光圈索引 `ai % Length` 循环取用）。
        ///
        /// ⚠ 画布背景是**纯黑**（KWindowControl.WindowBackColor = Color.Black），
        ///   所以这里剔除了在黑底上对比度不足的颜色。下面是对纯黑背景的对比度实测
        ///   （WCAG 2.x 相对亮度，背景 L=0）：
        ///
        ///     blue             (0, 0, 255)     2.44:1   ← 已剔除
        ///     dark olive green (85, 107, 47)   3.53:1   ← 已剔除
        ///     dim gray         (105, 105, 105) 3.83:1   ← 已剔除
        ///     slate blue       (106, 90, 205)  3.96:1   ← 已剔除
        ///     ── 以下为保留项里最低的三个 ──
        ///     medium slate blue(123, 104, 238) 5.06:1
        ///     red              (255, 0, 0)     5.25:1
        ///     gray             (128, 128, 128) 5.32:1
        ///
        ///   阈值取 **4.5:1**（而非非文本图形常用的 3:1）：Gerber 光圈是**描边**绘制的
        ///   细轮廓（SetDraw("margin")），比填充图形更容易看不清。
        ///
        /// 剔除后色数从 19 降到 15，`ai % 15` 的循环变短、相邻光圈撞色概率上升，
        /// 因此补入控件库支持的另两个高亮色：
        ///     violet (238,130,238)  约 12:1
        ///     gold   (255,215,0)    约 17:1
        /// 摆放位置是为了**隔开邻近色相**：violet 插在 gray 与 light gray 之间（两者都是灰、
        /// 原本相邻），gold 插在 medium slate blue 与 coral 之间（一冷一暖，差异大）。
        /// </summary>
        private static readonly string[] ApertureColors = {
            "red", "green", "gray", "violet", "light gray", "cyan", "magenta", "yellow",
            "medium slate blue", "gold", "coral", "spring green", "orange red", "orange",
            "pink", "cadet blue", "white"
        };

        /// <summary>
        /// 绘制入口（替代原来的 hWindowControl1_Paint）。
        /// 这里全部用**世界坐标**调用 —— 原来每个图形都要算一遍的
        /// windowX = x * scale + offsetX 已经整体删除，缩放平移由控件负责。
        /// </summary>
        private void OnPaintContent(KWindow ctx)
        {
            try
            {
                // ① 视口裁剪：先取出当前可见的世界区域，只画落在里面的图形。
                //    这是手感的分水岭 —— 成本上限从"全图 15,359 个图形"变成"可见图形数"，
                //    放大后可见图形骤减，帧耗时就跟着骤降（原实现无论看得见几个都全量处理）。
                RectangleF view = kWindowControl1.GetVisibleWorldBounds();
                // 稍微外扩几像素，避免贴近边缘的图形描边被切掉
                double pad = kWindowControl1.Viewport.ToWorldLength(2);
                if (pad > 0) view.Inflate((float)pad, (float)pad);

                if (_isTemplateMode)
                {
                    DrawTemplateMode(ctx, view);
                }
                else
                {
                    DrawGerberMode(ctx, view);
                }
            }
            catch (Exception ex)
            {
                // 绘制中抛异常绝不能让窗口变黑：记日志，本帧剩余图元跳过
                KLog.Error("绘制内容时出错", ex);
            }
        }

        // 模板模式绘制
        private void DrawTemplateMode(KWindow ctx, RectangleF view)
        {
            // 绘制顺序与 DrawGerberMode **严格对齐**：底图 → 选点 → 限定框（先画的在下层）。
            // 原来这里把底图放在最后画，等于把 Gerber 光圈的描边叠在已选点上层；而 Gerber 模式
            // 是反过来的。同一个工程"保存前 / 保存后"（保存会把绘制切到本模式）观感不一致，
            // 根子就在这个顺序差上 —— 绘制内容不该因为"存了一次盘"而变样。

            // ① 底图（最底层）
            if (_showBaseLayer)
            {
                DrawGerberApertures(ctx, view);
            }

            // ② 全部模板点
            //
            // ⚠ 这里**不再**调用 DrawSelectedCircles()：
            // DrawAllTemplateCircles 已经把全部模板点（含已选中的）按同一颜色画过一遍，
            // 而 DrawSelectedCircles 用的又是同一个 _selectColor + SetDraw("fill")，像素完全一致
            // —— 原来那一下是纯重复劳动（模板点 3,852 个时，每帧白画两遍）。
            // （Gerber 模式下仍需它：那个模式不画全部模板点，只画已选中的。）
            if (_selectedCircles.Count > 0)
            {
                DrawAllTemplateCircles(ctx, view);
            }

            // ③ 框选限定框（最上层）：语义已从"我刚才框过的区域"变成"当前批量操作被限定在这一片"，
            // 因此必须保留显示 —— 用户看不见限定，就会以为框外的同类点也会被一起改。
            // 内部已判空。
            DrawSelectedRect(ctx);
        }

        // Gerber模式绘制
        private void DrawGerberMode(KWindow ctx, RectangleF view)
        {
            // ⚠ 「显示底图」开关在这里也必须生效。
            //
            // 原来本方法**无条件**画底图，于是那个 toggle 在 Gerber 模式下是个死按钮：
            // 按下去画面毫无变化（只有按钮自己的高亮变了），状态却已经翻转成 false。
            // 等用户点了「保存」——保存会把绘制切到模板模式，而模板模式是读这个开关的——
            // 那个早先按下却没生效的 false 才突然起作用。用户看到的就是"保存后 Gerber 底图消失"。
            // 同一个开关在两套绘制路径下只能有一个口径：不显示就是不显示。
            if (_showBaseLayer)
            {
                DrawGerberApertures(ctx, view);
            }

            // 原来的条件是 `_isSelectingMode && ...`，去掉了模式门禁后改为"有已选点就画"。
            if (_selectedCircles.Any(c => c.IsSelected))
            {
                DrawSelectedCircles(ctx, view);
            }

            // 框选限定框：它同时是"当前批量操作被限定在这一片"的指示灯，必须画出来 ——
            // 看不见限定，用户就会以为框外的同类点也会跟着被改。
            // 内部已判空，无需外层判断。
            // 放在最后画 = 落在最上层，不会被底图的光圈轮廓压住。
            DrawSelectedRect(ctx);
        }
        /// <summary>按"变换后的中心 + 外接半宽半高"做视口裁剪判定（镜像会改变位置，所以要用变换后的中心）。</summary>
        private static bool IsVisibleAt(SelectedCircle shape, double cx, double cy, RectangleF view)
        {
            double halfW, halfH;
            ShapeQuery.GetHalfExtents(shape, out halfW, out halfH);

            return (cx + halfW) >= view.Left && (cx - halfW) <= view.Right &&
                   (cy + halfH) >= view.Top && (cy - halfH) <= view.Bottom;
        }

        /// <summary>
        /// 按形状类型把图形画到控件上（位置为**世界坐标**）。
        /// 参数顺序与 Halcon 的 GenXxx(Row, Column, ...) 一致：第一个传 Y、第二个传 X。
        /// </summary>
        private static void DispShape(KWindow ctx, SelectedCircle shape, double cx, double cy)
        {
            switch (shape.Shape)
            {
                case ApertureShape.Circle:
                    ctx.DispCircle(cy, cx, shape.Diameter / 2.0);
                    break;

                case ApertureShape.Rectangle:
                    {
                        double hw = shape.Width / 2.0;
                        double hh = shape.Height / 2.0;
                        ctx.DispRectangle1(cy - hh, cx - hw, cy + hh, cx + hw);
                    }
                    break;

                case ApertureShape.Oval:
                    // Rotation 是角度、顺时针为正，与 GDI+ 的 RotateTransform 同向，直接转弧度
                    ctx.DispEllipse2(cy, cx, shape.Rotation * Math.PI / 180.0,
                                     shape.Width / 2.0, shape.Height / 2.0);
                    break;
            }
        }

        // 绘制所有模板点（镜像 + 视口裁剪）
        private void DrawAllTemplateCircles(KWindow ctx, RectangleF view)
        {
            if (_selectedCircles.Count == 0) return;

            ctx.SetDraw("fill");
            ctx.SetColor(_selectColor);

            for (int i = 0; i < _selectedCircles.Count; i++)
            {
                SelectedCircle shape = _selectedCircles[i];

                double cx, cy;
                ApplyMirrorTransform(shape.X, shape.Y, out cx, out cy);

                if (!IsVisibleAt(shape, cx, cy, view)) continue;

                // 世界坐标直接交给控件。几何不预先栅格化，所以放大多少倍都是重新光栅化，
                // 边缘始终是精确的圆 —— 这正是"缓存 region + 位图缩放"（会发糊）的反面。
                DispShape(ctx, shape, cx, cy);
            }
        }

        // 绘制已选中的形状（Gerber 模式下用它显示"已选点"）
        private void DrawSelectedCircles(KWindow ctx, RectangleF view)
        {
            if (_selectedCircles.Count == 0) return;

            ctx.SetDraw("fill");
            ctx.SetColor(_selectColor);

            // 原地遍历 + IsSelected 判断，不再先 Where(...).ToList() 分配一份新列表
            for (int i = 0; i < _selectedCircles.Count; i++)
            {
                SelectedCircle shape = _selectedCircles[i];
                if (!shape.IsSelected) continue;

                double cx, cy;
                ApplyMirrorTransform(shape.X, shape.Y, out cx, out cy);

                if (!IsVisibleAt(shape, cx, cy, view)) continue;

                DispShape(ctx, shape, cx, cy);
            }
        }
        /// <summary>
        /// Gerber 光圈绘制：**按图层**着色 + 按图层可见性过滤 + 视口裁剪。
        ///
        /// 与原实现的两处关键差异：
        ///   ① 颜色从"按光圈序号 ai 取色"改为**按图层序号取色** —— 同一图层内所有图形同色，
        ///      不同图层不同色。多图层叠加时这样才能分清哪条线属于哪一层；
        ///      原来的画法会让同一层里的不同光圈五颜六色，层与层反而看不出区别。
        ///   ② 跳过 IsVisible == false 的图层 —— 图层容器的复选框就是控制这里的。
        /// </summary>
        private void DrawGerberApertures(KWindow ctx, RectangleF view)
        {
            if (_layers.Count == 0) return;

            ctx.SetDraw("margin");

            for (int li = 0; li < _layers.Count; li++)
            {
                LayerInfo layer = _layers[li];
                if (!layer.IsVisible) continue;              // 规则 R1：可见性只影响绘制

                List<Aperture> apertures = layer.Apertures;
                if (apertures == null || apertures.Count == 0) continue;

                // 整层一个颜色（不是整层里每个光圈一个颜色）
                ctx.SetColor(GetLayerColor(li));

                for (int ai = 0; ai < apertures.Count; ai++)
                {
                    Aperture aperture = apertures[ai];
                    if (aperture.Position.Count == 0) continue;

                    // 该光圈尺寸对应的外接半宽/半高只算一次，循环里只做四次比较
                    double halfW, halfH;
                    ApertureHalfExtents(aperture, out halfW, out halfH);

                    for (int pi = 0; pi < aperture.Position.Count; pi++)
                    {
                        var position = aperture.Position[pi];

                        double cx, cy;
                        ApplyMirrorTransform(position.Item1, position.Item2, out cx, out cy);

                        // 视口裁剪：屏幕外的图形连绘制调用都不发出去
                        if ((cx + halfW) < view.Left || (cx - halfW) > view.Right ||
                            (cy + halfH) < view.Top || (cy - halfH) > view.Bottom)
                        {
                            continue;
                        }

                        if (aperture.Shape == ApertureShape.Circle)
                        {
                            ctx.DispCircle(cy, cx, aperture.Diameter / 2.0);
                        }
                        else if (aperture.Shape == ApertureShape.Rectangle)
                        {
                            double hw = aperture.Width / 2.0;
                            double hh = aperture.Height / 2.0;
                            ctx.DispRectangle1(cy - hh, cx - hw, cy + hh, cx + hw);
                        }
                        else if (aperture.Shape == ApertureShape.Oval)
                        {
                            ctx.DispEllipse2(cy, cx, aperture.Rotation * Math.PI / 180.0,
                                             aperture.Width / 2.0, aperture.Height / 2.0);
                        }
                    }
                }
            }
        }

        /// <summary>
        /// 图层颜色：按图层在 _layers 里的序号取色。
        ///
        /// 色表 ApertureColors 已按"对纯黑背景对比度 ≥ 4.5:1"筛过（见其定义处的实测数据），
        /// 共 17 项 —— 超过 17 个图层才会开始循环撞色。
        /// </summary>
        private string GetLayerColor(int layerIndex)
        {
            if (layerIndex < 0) layerIndex = 0;
            return ApertureColors[layerIndex % ApertureColors.Length];
        }

        /// <summary>光圈形状的外接半宽 / 半高（世界单位）。旋转椭圆取保守值，零三角函数。</summary>
        private static void ApertureHalfExtents(Aperture aperture, out double halfW, out double halfH)
        {
            switch (aperture.Shape)
            {
                case ApertureShape.Circle:
                    halfW = halfH = aperture.Diameter * 0.5;
                    break;

                case ApertureShape.Rectangle:
                    halfW = aperture.Width * 0.5;
                    halfH = aperture.Height * 0.5;
                    break;

                case ApertureShape.Oval:
                    halfW = halfH = Math.Max(aperture.Width, aperture.Height) * 0.5;
                    break;

                default:
                    halfW = halfH = 0;
                    break;
            }

            if (halfW < 0) halfW = 0;
            if (halfH < 0) halfH = 0;
        }

        // 绘制「框选限定」框。参数已是世界坐标，不再需要 offset / scale 换算。
        //
        // 这个框是**限定范围的指示灯**：框在 = 同类扩散只在框内；框不在 = 作用于全板。
        // 所以必须在画面上看得见 —— 否则用户点了框外的点，却发现框内的点被改掉
        // （或反之），完全无从判断发生了什么。
        // 线型用**实线**（曾短暂改成虚线以便与拉框橡皮筋区分，用户反馈更倾向实线，已改回）。
        private void DrawSelectedRect(KWindow ctx)
        {
            if (_actualSelectedRect.IsEmpty) return;

            ctx.SetDraw("margin");
            ctx.SetColor("red");
            ctx.SetLineWidth(1);

            // 对矩形应用翻转变换
            RectangleF r = ApplyMirrorTransformToRect(_actualSelectedRect);

            ctx.DispRectangle1(r.Top, r.Left, r.Bottom, r.Right);
        }
      
        #endregion

        #region 所有按钮控件事件
        private void btnOpenFile_Click(object sender, EventArgs e)
        {
            using (OpenFileDialog openFileDialog = new OpenFileDialog())
            {
                openFileDialog.Title = "选择文件";
                openFileDialog.InitialDirectory = GetGerberBrowseStartPath();
                openFileDialog.Filter = "所有文件 (*.*)|*.*";
                openFileDialog.Multiselect = false;

                if (openFileDialog.ShowDialog() == DialogResult.OK)
                {
                    LoadGerberFromPath(openFileDialog.FileName);
                }
            }
        }

        /// <summary>
        /// 打开指定路径的 Gerber。原逻辑整段内联在 btnOpenFile_Click 里，于是"启动记忆"没法复用
        /// （总不能为了恢复上次工程去伪造一个 OpenFileDialog）—— 这里抽出来，两处共用。
        /// </summary>
        private void LoadGerberFromPath(string path)
        {
            Program.FileName = path;
            _isTemplateMode = false;
            _firstLoadTag = false;                  // 重置加载标记
            ClearAllSelection();                    // 含 _allShapes 的 IsSelected 与框选撤销栈
            _actualSelectedRect = RectangleF.Empty; // 清空框选限定
            _currentTemplatePath = string.Empty;
            ResetMirrorState();                     // 重置翻转状态
            UpdateScaleLabel();
            ExtractPosition();

            RememberLastProject("gerber", path);
        }

        /// <summary>
        /// 打开 Gerber 对话框的起始目录：优先上次打开过的位置（原实现这里硬编码了他本机的一个
        /// 绝对路径，换台机器就失效），退回那个旧目录，最后退到桌面。
        /// </summary>
        private string GetGerberBrowseStartPath()
        {
            string last = Properties.Settings.Default.LastProjectPath;
            if (Properties.Settings.Default.LastProjectKind == "gerber" && !string.IsNullOrEmpty(last))
            {
                string dir = Path.GetDirectoryName(last);
                if (!string.IsNullOrEmpty(dir) && Directory.Exists(dir)) return dir;
            }

            // 没有"上次打开过的位置"可参照时，退到桌面。
            //
            // 原来这里还夹了一条原开发者本机的硬编码旧目录：
            //   const string legacy = @"C:\Users\<用户名>\Desktop\<工程>\Gerber";   // 原样，已删除
            // 两宗罪，和 Program.FileName 那个默认值一模一样：
            //   ① 换台机器必然指向一个不存在的路径（Directory.Exists 判掉，等于白写）；
            //   ② 这个字符串会进版本库，把本机用户名公开出去。
            // 已删除 —— 桌面是个中性的、在**任何**机器上都有意义的兜底。
            return Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
        }

        private void btnRestView_Click(object sender, EventArgs e)
        {
            ResetMirrorState();  // 重置翻转状态
            ResetViewToCenter();
            UpdateScaleLabel();
            toolStripStatusLabel1.Text = "已重置视图和翻转状态";
        }

        // 注：原「开始选点」按钮 btnSelectCircles 已删除。
        // 它先后承担过"进入选点模式"和"弹操作说明"两个职责 —— 前者随 _isSelectingMode 一起取消了，
        // 后者（ShowSelectionHelp）改挂在 F1 上，不再占按钮位。

        private void btnSaveTemple_Click(object sender, EventArgs e)
        {
            SaveTemplateFile();

        }
        private void btnSaveAs_Click(object sender, EventArgs e)
        {
            SaveTemplateAs();
        }
        private void btnLoadTemple_Click(object sender, EventArgs e)
        {

            LoadTemplateFile();// LoadTemplateFile方法内部会设置_loadTag = true
        }

        /// <summary>
        /// 统一设置「显示底图」并同步其余入口。
        ///
        /// 状态真值在字段 _showBaseLayer 上（原来挂在右侧面板的两个 RadioButton 上，面板已删除）。
        /// 三个入口 —— 画布悬浮工具栏的「底图」toggle、菜单「视图 → 显示底图」、画布右键菜单的
        /// 「显示底图」—— 全部调本方法，不再各自赋值。
        ///
        /// 为什么要 _syncingBaseLayer：给 toggle / 菜单项赋 Checked 会触发它们自己的
        /// CheckedChanged / Click，而它们又会回调本方法，形成回环。这个标志把回环打断。
        /// </summary>
        private void SetShowBaseLayer(bool visible)
        {
            if (_syncingBaseLayer) return;

            _syncingBaseLayer = true;
            try
            {
                _showBaseLayer = visible;
                if (_btnShowBaseLayer != null) _btnShowBaseLayer.Checked = visible;
                if (mnuShowBaseLayer != null) mnuShowBaseLayer.Checked = visible;
            }
            finally { _syncingBaseLayer = false; }

            // 记一笔。这个开关曾经是"点了没反应、过一会儿又突然生效"的头号嫌疑：
            // Gerber 模式的绘制路径不读它（已修，见 DrawGerberMode），于是按下的 false 当场不生效，
            // 直到绘制切到模板模式才突然起作用。留日志是为了下次再有"底图莫名不见"能一眼看出
            // 它是不是被按过 —— 这地方以前一点记录都没有。
            KLog.Info($"底图显示 = {visible}（当前绘制模式：{(_isTemplateMode ? "模板" : "Gerber")}）");

            // 状态栏回执：开关必须要"按一下就有反应"。
            // 以前它只在模板模式下真的改画面，在 Gerber 模式下按了等于没按（连状态栏都不动），
            // 用户会以为按钮坏了、或者根本没意识到自己按过 —— 这正是它能悄悄停在 false 的原因。
            toolStripStatusLabel1.Text = visible ? "已显示底图" : "已隐藏底图（选点不受影响）";

            // 原来这次重绘是由 RadioButton 的 CheckedChanged 间接触发的，现在显式调一次
            RefreshKWindow();
        }

        /// <summary>
        /// 清空所有已选点（二次确认）。
        /// 原为 btnClearSelectedAll_Click —— 那个按钮已随「选点操作」面板移除，
        /// 现在由工具栏「清空选点」和右键菜单调用，所以改成无参方法。
        /// </summary>
        private void ClearAllSelectedPoints()
        {
            var result = MessageBox.Show(
                "确定要清除所有选中的图形吗？",
                "确认清除",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Question,
                MessageBoxDefaultButton.Button2  // 默认选择"否"，防止误操作
            );

            if (result == DialogResult.Yes)
            {
                ClearAllSelection();
                UpdateSelectionCountLabel();
                RefreshKWindow();
                toolStripStatusLabel1.Text = "已清除所有选点";
            }
        }


        private void btnParamSetting_Click(object sender, EventArgs e)
        {
            ParamsForm paramsForm = new ParamsForm();
            //设置当前保存路径到参数窗体
            string currentPath = GetCurrentSavePath();
            paramsForm.SetSavePath(currentPath);

            // 传递当前颜色设置
            paramsForm.SetSelectColor(_selectColor);
            if (paramsForm.ShowDialog() == DialogResult.OK)
            {
                //1.从界面获取文本框内容，更新保存路径
                string newPath = paramsForm.GetSavePath();
                //2.从界面获取颜色设置
                _selectColor = paramsForm.GetSelectColor();
                Properties.Settings.Default.SelectedCircleColor = _selectColor;
                Properties.Settings.Default.Save(); // 保存到配置文件

                UpdateSavePath(newPath);
                //更新当前模板文件夹路径
                if (!string.IsNullOrEmpty(newPath) && Directory.Exists(newPath))
                {
                    _currentTemplatePath = newPath;
                    KLog.Info($"参数设置后更新当前模板文件夹路径: {_currentTemplatePath}");
                }
                RefreshKWindow();
            }
        }

        // ───────── 菜单事件 ─────────
        // 菜单项大多直接复用原有的按钮 Click 处理方法（保存 / 另存到 / 翻转 / 重置视图 / 参数设置），
        // 这里只补那些原来没有对应"按钮方法"的入口。
        // 统一用 (object, EventArgs) 签名，为的是在 Designer.cs 里能用标准 EventHandler 绑定 ——
        // 用 lambda 虽然更短，但设计器重新生成 Designer.cs 时会被丢掉。

        /// <summary>
        /// 文件 → 新建工程：多选 Gerber 图层文件。
        ///
        /// 用 OpenFileDialog 的多选而不是"选目录"，是刻意的：一个工程目录里常常混着丝印、说明、
        /// 尺寸表等非图层文件，让用户自己挑出要的图层比"扫描整个目录再替他猜"更可控。
        /// 挑错的会在解析器的严格模式下判失败、被跳过并汇总提示。
        /// </summary>
        private void OnNewProjectClick(object sender, EventArgs e)
        {
            using (var dialog = new OpenFileDialog())
            {
                dialog.Title = "新建工程 —— 选择 Gerber 图层文件（可多选）";
                dialog.InitialDirectory = GetGerberBrowseStartPath();
                // 刻意不设扩展名白名单：GBL / GBS / G1 / .o 这类太杂，白名单只会把人挡在外面
                dialog.Filter = "所有文件 (*.*)|*.*";
                dialog.Multiselect = true;

                if (dialog.ShowDialog() != DialogResult.OK) return;

                if (LoadProjectFromFiles(dialog.FileNames))
                {
                    RememberLastProject("gerber", dialog.FileNames[0]);
                }
            }
        }

        /// <summary>
        /// 文件 → 打开工程：选一个工程目录（含 Gerber 图层 + 可选 circles.json）。
        ///
        /// 用目录选择器而不是让用户去点目录里的某个文件 —— 工程的粒度本来就是"一个目录"，
        /// 旧版那种"选 json 再取父目录"只是把这个概念藏起来而已。
        /// </summary>
        private void OnOpenProjectClick(object sender, EventArgs e)
        {
            // 用系统原生的"选择文件夹"对话框（IFileOpenDialog + FOS_PICKFOLDERS），
            // 而不是 FolderBrowserDialog —— 后者是 Vista 前的老式树形选择器，
            // 没有地址栏、不能粘贴路径、不能搜索。详见 FolderPicker.cs 的说明。
            string last = Properties.Settings.Default.LastProjectPath;
            if (string.IsNullOrEmpty(last) || !Directory.Exists(last)) last = null;

            string folder = FolderPicker.PickFolder(
                "打开工程 —— 选择工程目录（含 Gerber 图层，可选 circles.json）", last);

            if (string.IsNullOrEmpty(folder)) return;

            LoadProjectFromFolder(folder);
        }

        /// <summary>
        /// 文件 → 删除工程：关闭当前工程，并**连同磁盘上的工程目录一起删除**。
        ///
        /// 这是本程序唯一的破坏性操作，所以约束比别处严：
        ///   ① 先把**将删除的全部文件**列出来给用户看，不让他盲点；
        ///   ② 二次确认，且默认按钮是"否" —— 免得一路回车就删了；
        ///   ③ 删除走**回收站**（不做永久删除），误删还能捞回来；
        ///   ④ 目录里既没有 circles.json、也没有任何图层文件时**拒绝执行**，
        ///      防止把用户随手选中的普通目录清空；
        ///   ⑤ 工程还没落盘（新建后没保存过）时退化为"只关闭工作区"。
        /// </summary>
        private void OnDeleteProjectClick(object sender, EventArgs e)
        {
            if (_layers.Count == 0 && string.IsNullOrEmpty(Program.FileName))
            {
                toolStripStatusLabel1.Text = "当前没有打开的工程";
                return;
            }

            string folder = _currentTemplatePath;

            // 工程还没落盘 → 没有可删的目录，退化为"只关闭工作区"
            if (string.IsNullOrEmpty(folder) || !Directory.Exists(folder))
            {
                if (!ConfirmCloseProjectOnly()) return;
                CloseProjectAndClearMemory();
                toolStripStatusLabel1.Text = "已关闭当前工程（尚未保存到磁盘，没有删除任何文件）";
                return;
            }

            // ④ 安全阀：目录看起来不像工程目录就拒绝
            bool hasCirclesJson = File.Exists(Path.Combine(folder, "circles.json"));
            List<string> layerFiles = ScanLayerFiles(folder);
            if (!hasCirclesJson && layerFiles.Count == 0)
            {
                MessageBox.Show(
                    "这个目录里既没有 circles.json，也没有任何 Gerber / 钻孔文件，\r\n" +
                    "看起来不是一个工程目录，已取消删除。\r\n\r\n" + folder,
                    "已取消", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            // ① 列出将删除的全部文件
            string[] allFiles = Directory.GetFiles(folder);
            var list = new StringBuilder();
            int shown = Math.Min(allFiles.Length, 20);
            for (int i = 0; i < shown; i++) list.AppendLine("    " + Path.GetFileName(allFiles[i]));
            if (allFiles.Length > shown) list.AppendLine($"    …… 另有 {allFiles.Length - shown} 个文件");

            // ② 二次确认，默认按钮 = 否
            var confirm = MessageBox.Show(
                "即将删除整个工程目录：\r\n\r\n" +
                folder + "\r\n\r\n" +
                $"目录内共 {allFiles.Length} 个文件：\r\n" + list + "\r\n" +
                "文件会被移入回收站（可以从回收站还原）。\r\n\r\n" +
                "确定要删除这个工程吗？",
                "删除工程 —— 请确认",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Warning,
                MessageBoxDefaultButton.Button2);

            if (confirm != DialogResult.Yes)
            {
                toolStripStatusLabel1.Text = "已取消删除工程";
                return;
            }

            // ③ 走回收站（永不永久删除）
            try
            {
                Microsoft.VisualBasic.FileIO.FileSystem.DeleteDirectory(
                    folder,
                    Microsoft.VisualBasic.FileIO.UIOption.OnlyErrorDialogs,
                    Microsoft.VisualBasic.FileIO.RecycleOption.SendToRecycleBin);

                KLog.Info($"删除工程：{folder}（已移入回收站）");
            }
            catch (Exception ex)
            {
                KLog.Error($"删除工程失败：{folder}", ex);
                MessageBox.Show(
                    $"删除工程目录失败：\r\n\r\n{folder}\r\n\r\n{ex.Message}",
                    "删除失败", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }

            CloseProjectAndClearMemory();
            toolStripStatusLabel1.Text = $"工程已删除（移入回收站）：{folder}";
        }

        /// <summary>二次确认"只关闭工作区"（工程尚未落盘时走这条，不涉及任何删除）。</summary>
        private bool ConfirmCloseProjectOnly()
        {
            var confirm = MessageBox.Show(
                "确定要关闭当前工程吗？\r\n\r\n" +
                "这个工程还没有保存到磁盘，所以只会清空工作区，不删除任何文件。\r\n" +
                "尚未保存的选点修改会丢失。",
                "关闭工程", MessageBoxButtons.YesNo, MessageBoxIcon.Question,
                MessageBoxDefaultButton.Button2);
            return confirm == DialogResult.Yes;
        }

        /// <summary>
        /// 清空工作区 + 清掉启动记忆。
        ///
        /// 启动记忆必须一起清 —— 否则下次开程序会自动把刚删掉（或刚关闭）的工程又拉回来，
        /// 用户会以为"删了没生效"。
        /// </summary>
        private void CloseProjectAndClearMemory()
        {
            ResetProjectState();

            Properties.Settings.Default.LastProjectKind = string.Empty;
            Properties.Settings.Default.LastProjectPath = string.Empty;
            Properties.Settings.Default.Save();

            UpdateScaleLabel();
            RefreshKWindow();
        }

        /// <summary>文件 → 退出。</summary>
        private void OnExitClick(object sender, EventArgs e)
        {
            Close();
        }

        /// <summary>编辑 → 框选区域（与工具栏按钮、Q 键同一入口）。</summary>
        private void OnRegionSelectClick(object sender, EventArgs e)
        {
            StartRegionSelectingMode();
        }

        /// <summary>编辑 → 解除框选限定（与工具栏、Esc、空格同一入口）。</summary>
        private void OnReleaseScopeClick(object sender, EventArgs e)
        {
            ReleaseRegionScope();
        }

        /// <summary>编辑 → 清空选点（与工具栏、右键菜单同一入口，含二次确认）。</summary>
        private void OnClearSelectionClick(object sender, EventArgs e)
        {
            ClearAllSelectedPoints();
        }

        /// <summary>视图 → 显示底图（CheckOnClick 自带勾选切换）。转发给统一入口。</summary>
        private void OnShowBaseLayerChanged(object sender, EventArgs e)
        {
            SetShowBaseLayer(mnuShowBaseLayer.Checked);
        }

        /// <summary>帮助 → 操作说明（与 F1 同一入口）。</summary>
        private void OnHelpClick(object sender, EventArgs e)
        {
            ShowSelectionHelp();
        }

        /// <summary>帮助 → 关于。</summary>
        private void OnAboutClick(object sender, EventArgs e)
        {
            MessageBox.Show(
                "Gerber 选点工具 V4.0\r\n\r\n" +
                "一个工程 = 一个目录 = 若干 Gerber 图层 +（可选）circles.json\r\n" +
                "画布控件：Korey.SmartWindow",
                "关于",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);
        }
        #endregion

        #region 辅助方法
        //核心方法： 提取坐标
        private void ExtractPosition()
        {
            if (string.IsNullOrEmpty(Program.FileName))
            {
                MessageBox.Show("请先选择一个文件！");
                return;
            }

            try
            {
                _parser.ParseFile(Program.FileName);
                _parser.PrintSummary();
                _apertures = _parser.GetApertureList();
                _isTemplateMode = false;
                _firstLoadTag = false; // 重置加载标记
                ClearAllSelection(); // 清空已选点集合（含 _allShapes 的 IsSelected 与框选撤销栈）
                _actualSelectedRect = RectangleF.Empty; // 清空区域
                _currentTemplatePath = string.Empty;

                // 重建图形缓存与内容包围盒（视图适配、镜像变换、命中测试都依赖它们）
                BuildAllShapes();
                UpdateContentBounds();

                ResetViewToCenter();
                // 提取坐标后更新选点按钮状态
            }
            catch (Exception ex)
            {
                MessageBox.Show($"解析出错: {ex.Message}");
            }
        }
        /// <summary>
        /// 重置视图：让内容完整居中显示（替代原来的 ResetHalconViewToCenter）。
        /// 原来这里手工算 scale / offset 写进 _halconScale / _halconOffsetX/Y；
        /// 现在交给控件的 KViewport.FitToWindow —— 做的事完全一样，
        /// 区别是它同时成为**唯一**的变换数据源（原来那套手工值后来会和视口脱节）。
        /// </summary>
        private void ResetViewToCenter()
        {
            if (_apertures == null || _apertures.Count == 0)
            {
                KLog.Warn("重置视图被跳过：当前没有 Gerber 数据");
                return;
            }

            try
            {
                UpdateContentBounds();

                if (_contentBounds.Width <= 0 || _contentBounds.Height <= 0)
                {
                    KLog.Warn("重置视图被跳过：内容包围盒非法");
                    return;
                }

                kWindowControl1.FitToWindow(_contentBounds);

                UpdateScaleLabel();
                RefreshKWindow();

                KLog.Info($"视图已重置 | 内容范围 {_contentBounds.Width:F0} x {_contentBounds.Height:F0} | 缩放 {kWindowControl1.Scale:F4}");
            }
            catch (Exception ex)
            {
                KLog.Error("重置视图时出错", ex);
                MessageBox.Show($"重置视图时出错: {ex.Message}");
            }
        }

        /// <summary>
        /// 刷新内容包围盒缓存。
        /// 镜像变换与 FitToWindow 都要用它，而原来是在绘制循环里为**每个图形**各算一次
        /// （ViewportHelper.CalculateBoundingBox 会遍历全部光圈）→ 退化成 O(N²)。
        /// </summary>
        private void UpdateContentBounds()
        {
            try
            {
                if (_apertures == null || _apertures.Count == 0)
                {
                    _contentBounds = RectangleF.Empty;
                    return;
                }

                var box = ViewportHelper.CalculateBoundingBox(_apertures);

                // Min/Max 兜一层，避免 Top/Bottom 方向相反时得到负宽高
                _contentBounds = RectangleF.FromLTRB(
                    (float)Math.Min(box.Left, box.Right),
                    (float)Math.Min(box.Top, box.Bottom),
                    (float)Math.Max(box.Left, box.Right),
                    (float)Math.Max(box.Top, box.Bottom));
            }
            catch (Exception ex)
            {
                KLog.Error("计算内容包围盒失败", ex);
                _contentBounds = RectangleF.Empty;
            }
        }

        private void UpdateScaleLabel()
        {
            // 缩放与可见范围一律从控件的 Viewport 取（唯一数据源）
            RectangleF visible = kWindowControl1.GetVisibleWorldBounds();

            // 原实现把这块信息写在窗体左上角的 label1 —— 9pt 小字、离操作处很远，实际没人看。
            // 现在拆成状态栏右侧的固定位：左侧消息区（Spring=true 占满剩余宽度）留给瞬时消息，
            // 这三个标签紧贴右边，互不挤占。
            lblView.Text = $"缩放 {kWindowControl1.Scale:F3}× | 可见 {visible.Width:F0}×{visible.Height:F0}";

            UpdateProjectLabel();
        }

        /// <summary>
        /// 刷新状态栏的"当前工程"信息。
        ///
        /// 原 label1 显示的是 `[Gerber] 文件: xxx` / `[模板] xxx` —— 这种"模式 + 文件"的说法
        /// 本身就是本次架构改造要消灭的东西（只有"工程"，没有"模板模式"）。
        /// 现在只报工程名；是不是"带选点的工程"，由图层与选点数据自己体现。
        /// </summary>
        private void UpdateProjectLabel()
        {
            string projectName = GetProjectName();
            lblLayerInfo.Text = string.IsNullOrEmpty(projectName) ? "未打开工程" : projectName;

            // 顶部信息条显示的是**根目录路径**（而不是工程名）——
            // 名字只能说明"是哪个工程"，路径才能让用户一眼判断"存到哪、从哪打开来的"，
            // 这正是旧版"保存到哪说不清"那个老问题的正面解法。
            if (!string.IsNullOrEmpty(_currentTemplatePath))
            {
                txtProjectPath.Text = _currentTemplatePath;
            }
            else if (_layers.Count > 0)
            {
                txtProjectPath.Text = "（尚未保存到磁盘 —— 点「保存」选定工程目录）";
            }
            else
            {
                txtProjectPath.Text = string.Empty;
            }
        }

        /// <summary>
        /// 现画一个文件夹图标给「打开工程」按钮用。
        ///
        /// 为什么现画：引入 .png/.ico 要改 csproj 的 EmbeddedResource、还要处理相对路径与缺失兜底，
        /// 而这里只需要一个能认出来的轮廓 —— GDI+ 几笔就够了，零外部依赖。
        /// </summary>
        private static Bitmap CreateFolderIcon(int size)
        {
            var bmp = new Bitmap(size, size);
            using (var g = Graphics.FromImage(bmp))
            {
                g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
                g.Clear(Color.Transparent);

                float w = size, h = size;
                Color fill = Color.FromArgb(255, 214, 138);   // 浅黄
                Color line = Color.FromArgb(205, 145, 45);    // 深黄描边

                using (var body = new SolidBrush(fill))
                using (var pen = new Pen(line, 1f))
                {
                    // 上沿的"标签"
                    var tab = new RectangleF(w * 0.06f, h * 0.20f, w * 0.38f, h * 0.16f);
                    g.FillRectangle(body, tab);
                    g.DrawRectangle(pen, tab.X, tab.Y, tab.Width, tab.Height);

                    // 主体
                    var main = new RectangleF(w * 0.06f, h * 0.30f, w * 0.88f, h * 0.48f);
                    g.FillRectangle(body, main);
                    g.DrawRectangle(pen, main.X, main.Y, main.Width, main.Height);
                }
            }
            return bmp;
        }

        /// <summary>
        /// 当前工程名 = 工程目录名；没有打开的工程时退化到 Gerber 文件名，再没有就返回空串。
        /// 取代原来的 txtOutputFile 文本框（那个"目标文件夹名"输入框随右侧面板一起删除）——
        /// 新模型下工程名就是目录名，不需要用户再手输一遍。
        /// </summary>
        private string GetProjectName()
        {
            if (!string.IsNullOrEmpty(_currentTemplatePath))
            {
                return Path.GetFileName(_currentTemplatePath.TrimEnd(
                    Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
            }

            // 新建工程尚未归属：用第一个图层**所在目录**的名字，而不是文件名 ——
            // 文件名带扩展名（1516601-00-C_01.GBL），拿它当工程目录名不合适。
            if (_layers.Count > 0 && !string.IsNullOrEmpty(_layers[0].FilePath))
            {
                string dir = Path.GetDirectoryName(_layers[0].FilePath);
                if (!string.IsNullOrEmpty(dir))
                {
                    string name = Path.GetFileName(dir.TrimEnd(
                        Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
                    if (!string.IsNullOrEmpty(name)) return name;
                }
            }

            if (!string.IsNullOrEmpty(Program.FileName))
            {
                return Path.GetFileName(Program.FileName);
            }

            return string.Empty;
        }

        private void RefreshKWindow()
        {
            try
            {
                if (kWindowControl1.IsHandleCreated &&
                    kWindowControl1.Visible &&
                    kWindowControl1.Width > 0 &&
                    kWindowControl1.Height > 0)
                {
                    // 不再需要"刷新两次"：那是原来 ClearWindow + 每帧重建几何
                    // 与 WM_PAINT 交错导致的时序问题；GDI+ 自绘路径没有这个问题。
                    kWindowControl1.Refresh();
                }
            }
            catch (Exception ex)
            {
                KLog.Error("刷新画布失败", ex);
            }
        }

        #endregion

        #region 鼠标交互（缩放 / 平移 / 重置视图 由控件内建）

        // 原来这里的三段手工实现已整体删除，现在由 KWindowControl 内建承担：
        //   · 滚轮缩放（以鼠标为锚点）—— 原来手工 `_halconScale *= 1.1` + offset 补偿，
        //     与 KViewport.ZoomAt 的数学完全等价（O' = Anchor - (Anchor - O) * (S'/S)）
        //   · 拖拽平移 —— 内建（默认右键拖拽；中键改为"重置视图"）
        //   · 中键重置视图 —— 内建 PerformResetView（按 ContentBounds 适配居中）

        /// <summary>
        /// 左键按下：选点。
        ///
        /// 传入的 x / y 已经是**世界坐标** —— 控件内部用同一个 KViewport 做完了屏幕→世界的反算，
        /// 所以不存在"拖动/缩放之后坐标反算与视口脱节"的问题（手工维护两套变换时踩过这个坑）。
        /// 右键（平移）与中键（重置视图）是控件内建行为，这里不处理。
        /// </summary>
        private void KWindowControl_KMouseDown(double x, double y, MouseButtons button)
        {
            if (_isRegionSelectingMode) return;   // 框选进行中不处理选点（防御性；控件本身也不会派发）
            // 注：原来的 `if (!_isSelectingMode) return;` 已删除 —— 左键任何时候都能选点。
            //     那个门禁是"点了没反应，以为软件坏了"的根源。
            if (button != MouseButtons.Left) return;   // 右键 / 中键由控件内建（平移 / 重置视图）

            try
            {
                // 反转翻转变换，回到原始（未翻转）坐标系
                double actualX, actualY;
                ReverseMirrorTransform(x, y, out actualX, out actualY);

                // 命中范围：**始终是全量形状缓存**，框选矩形不参与命中测试。
                //
                // 原实现是 `if (!_actualSelectedRect.IsEmpty) candidates = GetCirclesInSelectedRegion(...)`，
                // 有两个后果：
                //   ① 框外点击静默失效（HitTest 返回 null 就直接 return，没有任何提示）——
                //      用户看到的是"点了没反应"，这正是"要选别处必须先清区域"的根源；
                //   ② candidates 被原样传进 HandleMultipleSelect，于是"同 ID 全板全选"
                //      被悄悄降级成"框内同 ID 全选"（区域本是点击过滤器，却意外成了同类扩散的边界）。
                // 现在：① 彻底去掉（框外一直点得中）；
                //       ② 改由 HandleMultipleSelect **显式**处理，框内 / 框外点击行为不同，
                //          且框的显示状态与之严格同步 —— 不再是暗中生效的副作用。
                List<SelectedCircle> candidates = _allShapes;

                // 点击容差：至少 4 个屏幕像素 —— 缩得很小时小图形也能点得中
                double tolerance = Math.Max(kWindowControl1.Viewport.ToWorldLength(4), 1e-9);

                SelectedCircle clicked = ShapeQuery.HitTest(candidates, actualX, actualY, tolerance);
                if (clicked == null) return;

                // 「框外点击 = 解除框选限定」，单选 / 多选一视同仁（规则只有这一条，见方法注释）
                bool scopeReleased = ReleaseScopeIfOutside(clicked);

                // 多选会返回一句"本次范围"说明（框选范围内 / 全板），交给状态栏显示；
                // 单选没有范围概念，保持 null，状态栏就用默认的"点击方式：单选"。
                string note = null;

                if (_isSingleClickMode)
                {
                    HandleSingleSelect(clicked, candidates);
                }
                else
                {
                    note = HandleMultipleSelect(clicked, candidates);
                }

                if (scopeReleased)
                {
                    note = string.IsNullOrEmpty(note)
                        ? "已解除框选限定（点在框外）"
                        : note + " · 已解除框选限定（点在框外）";
                }

                RefreshKWindow();
                UpdateSelectionStatusText(note);
            }
            catch (Exception ex)
            {
                KLog.Error("选点过程中出错", ex);
            }
        }

        /// <summary>
        /// 反转镜像：把显示坐标换回原始坐标。
        /// 中心点取自 _contentBounds 缓存 —— 原来每次调用都要跑一遍
        /// ViewportHelper.CalculateBoundingBox(_apertures)，而它在绘制循环里被调用了每个图形一次，
        /// 翻转开启时就是 O(N²)（约 2.36 亿次迭代/帧）。
        /// </summary>
        private void ReverseMirrorTransform(double x, double y, out double originalX, out double originalY)
        {
            originalX = x;
            originalY = y;

            if (!_isMirroredX && !_isMirroredY) return;

            double centerX = _contentBounds.X + _contentBounds.Width / 2.0;
            double centerY = _contentBounds.Y + _contentBounds.Height / 2.0;

            if (_isMirroredX) originalX = 2 * centerX - x;
            if (_isMirroredY) originalY = 2 * centerY - y;
        }

        #endregion

        #region 选点功能
        // 单选模式处理
        private void HandleSingleSelect(SelectedCircle clickedShape, List<SelectedCircle> allShapes)
        {
            // 切换选择状态
            clickedShape.IsSelected = !clickedShape.IsSelected;

            // 更新选中列表：点击的形状被选中 且 已选形状集合中不存在该形状
            if (clickedShape.IsSelected && !IsSingleShapeInShapes(clickedShape, _selectedCircles))
            {
                _selectedCircles.Add(clickedShape);
            }
            else if (!clickedShape.IsSelected)//再次点击取消选中
            {
                _selectedCircles.Remove(clickedShape);
            }
        }
        /// <summary>
        /// 若本次操作落在框选限定之外，就解除限定。
        ///
        /// 规则只有一条、所有入口共用：单选 / 多选 / 右键菜单**一视同仁**。
        /// （早先只让多选解除、单选不解除，等于给用户加了一条例外规则 ——
        ///   同一个动作在不同点击方式下结果不同，用户只会当成 bug。）
        /// 判定用"命中图形与框是否相交"，与 HandleMultipleSelect 圈定范围的口径完全一致。
        /// </summary>
        /// <returns>本次是否解除了限定（调用方据此在状态栏补一句说明）</returns>
        private bool ReleaseScopeIfOutside(SelectedCircle shape)
        {
            if (_actualSelectedRect.IsEmpty) return false;
            if (ShapeQuery.IsVisible(shape, _actualSelectedRect)) return false;

            _actualSelectedRect = RectangleF.Empty;
            KLog.Info("点击落在框外：解除框选限定，框消失，本次操作作用于全板");
            return true;
        }

        /// <summary>
        /// 多选处理：切换"同类的一批点"。
        ///
        /// "同类"的搜索范围受**框选限定**约束：
        ///   · 框还在（_actualSelectedRect 非空）→ 只在框内扩散同类；
        ///   · 框不在 → 全板扩散。
        /// 框何时消失（＝限定何时解除）统一由 KWindowControl_KMouseDown 决定：
        /// **点到框外就解除**，单选 / 多选一致。
        ///
        /// 注意点击命中本身**不受**限定影响（同上），所以不存在旧设计那种"框外点不中"的问题。
        ///
        /// 返回一句范围说明，供状态栏显示 —— 限定有没有生效必须让用户看得见。
        /// </summary>
        private string HandleMultipleSelect(SelectedCircle clickedShape, List<SelectedCircle> allShapes)
        {
            // 圈定本次"同类扩散"的搜索范围。
            // ⚠ "框外点击解除限定"已经在 KMouseDown 里统一做掉了，
            //   所以这里只要 _actualSelectedRect 非空，就说明本次点击落在框内。
            IEnumerable<SelectedCircle> searchSource = allShapes;
            bool narrowed = false;

            if (!_actualSelectedRect.IsEmpty)
            {
                RectangleF scopeRect = _actualSelectedRect;
                searchSource = allShapes.Where(c => ShapeQuery.IsVisible(c, scopeRect));
                narrowed = true;
            }

            // 获取所有同类型的形状（圆形比较直径，矩形比较宽度和高度）
            var sameIdShapes = searchSource.Where(c => c.ID == clickedShape.ID).ToList();

            // 判断当前点击的形状是否已被选中
            bool isCurrentlySelected = clickedShape.IsSelected;

            if (!isCurrentlySelected)
            {
                // 如果当前形状未选中，则选中所有同类形状
                foreach (var shape in sameIdShapes)
                {
                    shape.IsSelected = true;
                    if (!IsSingleShapeInShapes(shape, _selectedCircles))
                    {
                        _selectedCircles.Add(shape);
                    }
                }
            }
            else
            {
                // 如果当前形状已选中，则取消选中所有同类形状
                foreach (var shape in sameIdShapes)
                {
                    shape.IsSelected = false;
                    _selectedCircles.Remove(shape);
                }
            }

            string where = narrowed ? "框选范围内" : "全板";
            KLog.Info($"多选：切换 {sameIdShapes.Count} 个同类点（范围：{where}）");
            return $"多选切换 {sameIdShapes.Count} 个（{where}）";
        }
        //判断当前一个形状是否存在于形状集合中
        public bool IsSingleShapeInShapes(SelectedCircle currentShape, List<SelectedCircle> shapes)
        {
            foreach (var shape in shapes)
            {
                if (shape.X == currentShape.X &&
                    shape.Y == currentShape.Y &&
                    shape.Shape == currentShape.Shape)
                {
                    // 对于圆形，比较直径
                    if (currentShape.Shape == ApertureShape.Circle && shape.Diameter == currentShape.Diameter)
                    {
                        return true;
                    }
                    // 对于矩形，比较宽度和高度
                    else if (currentShape.Shape == ApertureShape.Rectangle &&
                             shape.Width == currentShape.Width &&
                             shape.Height == currentShape.Height)
                    {
                        return true;
                    }// 对于椭圆，比较宽度、高度和旋转角度
                    else if (currentShape.Shape == ApertureShape.Oval &&
                             shape.Width == currentShape.Width &&
                             shape.Height == currentShape.Height &&
                             shape.Rotation == currentShape.Rotation)
                    {
                        return true;
                    }
                }
            }
            return false;
        }
        //判断当前一个圆圈是否存在于圆圈集合中
        //public bool IsSingleCircleInCircles(SelectedCircle currentCircle, List<SelectedCircle> circles)
        //{
        //    foreach (var circle in circles)
        //    {
        //        if (circle.X == currentCircle.X && circle.Y == currentCircle.Y)
        //        {
        //            return true;
        //        }
        //    }
        //    return false;
        //}

        /// <summary>
        /// 构建全量图形缓存（**只在解析 Gerber / 加载模板后调用一次**）。
        ///
        /// 原来的 GetAllCirclesFromApertures() 是**每次鼠标点击**都跑一遍，而且每建一个图形
        /// 就要在 _selectedCircles 里做一次线性查找（FirstOrDefault）：
        ///   15,359 个位置 × 平均 1,926 次比较 ≈ 2,960 万次带闭包的比较 / 次点击 —— 越用越卡。
        /// 现在改成：一次构建 + 字典 O(1) 复用查找。
        /// </summary>
        /// <summary>
        /// 图形的**唯一键**：图层 + 坐标 + 形状。
        ///
        /// 多图层下必须带图层维度。GBL 与 GBS 经常在同一坐标放尺寸不同的图形，
        /// 只用 (X, Y, Shape) 会把它们认成同一个 —— 后果是命中测试只认其中一个、
        /// 框选去重会丢掉另一个、图层显隐也会算错。
        ///
        /// 键的使用点（原注释已强调"必须一致"）：
        ///   ① BuildAllShapes 的 selectedIndex（建索引 + 查索引）
        ///   ② ApplyRegionSelection 的 selectedKeys（建集合 + 查集合）
        /// 统一走本方法，避免以后再加维度时又漏掉一处。
        /// </summary>
        private static ValueTuple<string, double, double, int> ShapeKey(string layer, double x, double y, ApertureShape shape)
        {
            return ValueTuple.Create(layer ?? string.Empty, x, y, (int)shape);
        }

        /// <summary>
        /// 图形的**退化键**：只用坐标 + 形状，不带图层。
        ///
        /// 存在的唯一理由：circles.json 在 v2.2 以前**没有存 Layer 字段**，从那种文件恢复出来的
        /// 选点，Layer 全是空串，与 Aperture.Layer（图层文件名）永远对不上。只靠 ShapeKey 的后果
        /// 是"选点对象与图形缓存脱节"——画得出来、点一下就重复、再点取消不掉。
        /// 用本键把老数据兜住，命中时顺手把真实图层回填进选点（见 BuildAllShapes）。
        ///
        /// 坐标取 Math.Round(…, 4)：保存端就是这么写的（SaveTemplateToPath 里的 Math.Round），
        /// 两边做同一次舍入，比对即精确相等，不需要引入容差。
        /// </summary>
        private static ValueTuple<double, double, int> ShapeKeyLoose(double x, double y, ApertureShape shape)
        {
            return ValueTuple.Create(Math.Round(x, 4), Math.Round(y, 4), (int)shape);
        }

        private void BuildAllShapes()
        {
            var shapes = new List<SelectedCircle>();

            if (_apertures == null || _apertures.Count == 0)
            {
                _allShapes = shapes;
                return;
            }

            // 索引的目的只有一个：**复用 _selectedCircles 里那个对象实例**，而不是新建一个。
            // 引用必须相同，HandleSingleSelect 的 List.Remove(clicked) 才删得掉 ——
            // 否则表现为"点一下能选中、再点一下取消不掉"，而且同一位置会被画两遍。
            //
            // 两级索引：
            //   ① exactIndex —— 图层 + 坐标 + 形状（键由 ShapeKey 生成）
            //   ② looseIndex —— 坐标 + 形状（不带图层，见 ShapeKeyLoose）
            //
            // 为什么需要 ②：circles.json 在 v2.2 以前**没存 Layer 字段**，恢复出来的选点 Layer
            // 全是空串，跟 Aperture.Layer 永远对不上。原来只有级别 ①，于是老工程一打开，
            // 选点全是"幽灵对象"。现在命中 ② 时顺手把真实图层**回填**进选点，
            // 下次保存（v2.2 起会写 Layer）就走精确匹配。
            var exactIndex = new Dictionary<ValueTuple<string, double, double, int>, SelectedCircle>();
            var looseIndex = new Dictionary<ValueTuple<double, double, int>, SelectedCircle>();

            for (int i = 0; i < _selectedCircles.Count; i++)
            {
                SelectedCircle c = _selectedCircles[i];
                if (c == null) continue;

                exactIndex[ShapeKey(c.Layer, c.X, c.Y, c.Shape)] = c;
                looseIndex[ShapeKeyLoose(c.X, c.Y, c.Shape)] = c;
            }

            int reused = 0;      // 复用了已有选点对象的图形数
            int backfilled = 0;  // 顺手补上图层身份的选点数

            for (int ai = 0; ai < _apertures.Count; ai++)
            {
                Aperture aperture = _apertures[ai];
                string layer = aperture.Layer ?? string.Empty;

                for (int pi = 0; pi < aperture.Position.Count; pi++)
                {
                    var position = aperture.Position[pi];

                    SelectedCircle existing;
                    bool hit = exactIndex.TryGetValue(
                        ShapeKey(layer, position.Item1, position.Item2, aperture.Shape),
                        out existing);

                    if (!hit)
                    {
                        hit = looseIndex.TryGetValue(
                            ShapeKeyLoose(position.Item1, position.Item2, aperture.Shape),
                            out existing);

                        if (hit && existing != null && string.IsNullOrEmpty(existing.Layer))
                        {
                            existing.Layer = layer;   // 回填图层身份（老 JSON 里没有这个字段）
                            backfilled++;
                        }
                    }

                    if (hit && existing != null)
                    {
                        // 已选状态与集合成员保持一致：_selectedCircles 里就是"已选"的定义，
                        // 这里显式置真，免得出现"在集合里但 IsSelected 为假"的脱节。
                        existing.IsSelected = true;
                        shapes.Add(existing);
                        reused++;
                        continue;
                    }

                    SelectedCircle newShape = null;

                    if (aperture.Shape == ApertureShape.Circle)
                    {
                        newShape = new SelectedCircle(position.Item1, position.Item2, aperture.Diameter);
                    }
                    else if (aperture.Shape == ApertureShape.Rectangle)
                    {
                        newShape = new SelectedCircle(position.Item1, position.Item2, aperture.Width, aperture.Height);
                    }
                    else if (aperture.Shape == ApertureShape.Oval)
                    {
                        newShape = new SelectedCircle(position.Item1, position.Item2, aperture.Width, aperture.Height, aperture.Rotation);
                    }

                    if (newShape != null)
                    {
                        newShape.ID = aperture.ApertureId;
                        newShape.Layer = layer;
                        shapes.Add(newShape);
                    }
                }
            }

            _allShapes = shapes;
            KLog.Info($"图形缓存构建完成：{shapes.Count} 个图形（其中已选 {_selectedCircles.Count} 个，" +
                      $"复用选点对象 {reused} 个，回填图层 {backfilled} 个）");
        }

        // 注：原 GetShapesByPos() 已删除 —— 命中测试改由 ShapeQuery.HitTest 承担。
        // 除了消除每次点击的 O(N) 全表扫描，它还修了两个判定问题：
        //   · 旋转椭圆的命中原来忽略了 Rotation（原注释也承认"实际应该考虑旋转角度"）；
        //   · 返回"离光标最近"的图形，而不是列表里第一个碰巧命中的。

        /// <summary>
        /// 显示操作说明。原来这个方法叫 StartSelectingMode()，会把 _isSelectingMode 置真 ——
        /// 那是"进入选点模式"的入口（每次进入还弹一次 MessageBox，本身就是负担）。
        /// 现在左键随时可选点，没有"模式"可进，所以只剩说明这一个职责。
        /// </summary>
        private void ShowSelectionHelp()
        {
            string tip =
                "左键点图形：\n" +
                "    单选 — 切换这一个点；\n" +
                "    多选 — 切换「同类的一批点」（按光圈 ID 判定同类型）。\n\n" +
                "框选（按 Q，或点「框选区域」）：一次选中区域内全部图形，Ctrl+Z 撤销。\n\n" +
                "框选后会留下一个红色虚线框 = 「框选限定」：\n" +
                "    此时多选只在框内扩散同类点，框外的同类点不受影响；\n" +
                "    点框外会自动解除限定（框随之消失），恢复全板操作；\n" +
                "    也可以按 Esc 或点「清除区域」手动解除。\n\n" +
                "中键重置视图；右键拖拽平移；滚轮以光标为锚点缩放。\n" +
                "左键任何时候都能选点，不需要先进入任何模式。";

            MessageBox.Show(tip, "操作说明", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }

        /// <summary>
        /// 刷新状态栏：当前点击语义 + 已选数量 + 框选限定状态。
        ///
        /// <paramref name="prefix"/> 为空时显示"点击方式：单选 / 多选"；否则用调用方给的动作描述
        /// （例如"框选新增 47 个"），让批量操作的结果一眼可见。
        /// 限定状态在这里**持续显示** —— 框虽然画在画布上，但它是否正在生效要有文字兜底。
        /// 文案用"图形"而非"圆圈"：本项目实际会加载圆形 / 矩形 / 椭圆三类。
        /// </summary>
        private void UpdateSelectionStatusText(string prefix = null)
        {
            string head = string.IsNullOrEmpty(prefix)
                ? $"点击方式：{(_isSingleClickMode ? "单选" : "多选")}"
                : prefix;

            string scope = _actualSelectedRect.IsEmpty
                ? string.Empty
                : " · 框选限定生效中（Esc 或「清除区域」解除）";

            toolStripStatusLabel1.Text = $"{head} - 已选择 {_selectedCircles.Count} 个图形{scope}";

            // 状态栏右侧的固定信息位：把"已选数量"从瞬时消息里独立出来，
            // 否则它会被下一条消息（"已重置视图"之类）冲掉。
            UpdateSelectionCountLabel();
        }

        /// <summary>刷新状态栏右侧的"已选 N 个"信息位。选点集合有任何变化都要调。</summary>
        private void UpdateSelectionCountLabel()
        {
            lblSelInfo.Text = _selectedCircles.Count > 0
                ? $"已选 {_selectedCircles.Count} 个"
                : string.Empty;
        }
        #endregion

        #region  区域选点 

        /// <summary>
        /// 进入框选模式。
        /// 替代原来在 BeginInvoke 里跑的**阻塞式** HOperatorSet.DrawRectangle1 —— 那个算子
        /// 内部自建消息泵等用户画框，期间整个窗口（含所有按钮）都是无响应的；
        /// 现在用控件的非阻塞模式：左键拉框、松开即完成，右键或 Esc 取消。
        /// </summary>
        private void StartRegionSelectingMode()
        {
            _isRegionSelectingMode = true;
            _actualSelectedRect = RectangleF.Empty; // 先清掉上一次的框选限定
            RefreshKWindow();

            // 原来这里会禁用「框选区域」按钮防重入；按钮已移除，改由 _isRegionSelectingMode 自己挡
            kWindowControl1.BeginDrawRectangle1();
        }

        /// <summary>框选完成：参数已是**世界坐标**矩形（左上 + 宽高），不需要再做 offset / scale 反算。</summary>
        private void KWindowControl_Rectangle1Selected(RectangleF worldRect)
        {
            try
            {
                RectangleF rect = worldRect;

                // 有翻转时把矩形也翻回原始坐标系（复用同一份缓存中心）
                if (_isMirroredX || _isMirroredY)
                {
                    double x1 = rect.Left, x2 = rect.Right;
                    double y1 = rect.Top, y2 = rect.Bottom;

                    double centerX = _contentBounds.X + _contentBounds.Width / 2.0;
                    double centerY = _contentBounds.Y + _contentBounds.Height / 2.0;

                    if (_isMirroredX) { x1 = 2 * centerX - x1; x2 = 2 * centerX - x2; }
                    if (_isMirroredY) { y1 = 2 * centerY - y1; y2 = 2 * centerY - y2; }

                    rect = RectangleF.FromLTRB(
                        (float)Math.Min(x1, x2), (float)Math.Min(y1, y2),
                        (float)Math.Max(x1, x2), (float)Math.Max(y1, y2));
                }

                _actualSelectedRect = rect;
                KLog.Info($"框选完成：世界坐标 [{rect.Left:F1}, {rect.Top:F1}] - [{rect.Right:F1}, {rect.Bottom:F1}]");

                // 框选即选择：把框内图形一次性全选（累积语义，只增不减；Ctrl+Z 可撤销）。
                // 本方法只改状态、不刷新画面 —— 末尾那一次 RefreshKWindow() 统一重绘，
                // 避免一次框选刷两帧。
                ApplyRegionSelection(rect);
            }
            catch (Exception ex)
            {
                KLog.Error("处理框选结果时出错", ex);
            }
            finally
            {
                _isRegionSelectingMode = false;
            }

            RefreshKWindow();
        }

        /// <summary>框选被取消（右键 / Esc）：解除"框选进行中"标志。</summary>
        private void KWindowControl_Rectangle1Cancelled()
        {
            _isRegionSelectingMode = false;
            RefreshKWindow();
        }

        /// <summary>
        /// 收集落在 <paramref name="rect"/> 内的图形。
        ///
        /// 用途已变更：原先是"点击可达范围的过滤器"（被 KWindowControl_KMouseDown 调用），
        /// 现在是"框选即选择"的范围来源（被 ApplyRegionSelection 调用）。
        /// 判定口径仍是 ShapeQuery.IsVisible —— 图形外接矩形与矩形**相交**，
        /// 与视口裁剪同一口径。注意这意味着"框边擦到的图形"也会被计入，
        /// 所以 ApplyRegionSelection 会把命中数报给用户，让口径的松紧可见。
        /// </summary>
        private List<SelectedCircle> GetCirclesInSelectedRegion(List<SelectedCircle> allCircles, RectangleF rect)
        {
            var result = new List<SelectedCircle>();
            if (allCircles == null) return result;

            for (int i = 0; i < allCircles.Count; i++)
            {
                SelectedCircle s = allCircles[i];
                if (s != null && ShapeQuery.IsVisible(s, rect)) result.Add(s);
            }
            return result;
        }

        /// <summary>
        /// 框选即选择：把框内图形一次性全部选中。
        ///
        /// 语义是**累积**（只增不减）：只把框内"尚未选中"的图形加进来，不会取消已有的选择。
        /// 这样分几次框不同片区就能逐步累积选中结果，而不会因为第二次框选把第一次的结果清掉 ——
        /// "先清空再重选"恰恰是本功能要消灭的负担。
        /// 需要取消某片时走：单选模式下逐个点掉，或「清除所有选点」整体重来。
        ///
        /// 复杂度 O(n + m + k)：n = 全量图形数，m = 已选数，k = 命中数。
        /// 去重用的是**一次性构建**的 HashSet（键与 BuildAllShapes 的 selectedIndex 一致：
        /// (X, Y, Shape)），而不是逐点调用 IsSingleShapeInShapes —— 后者是线性扫描，
        /// 逐点调用会退化成 O(k·n)：3,852 点的板全选时约 1,480 万次多字段比较，
        /// 实际表现是几十到两百毫秒的卡顿；上万点就是秒级。现在是一次 O(n) 扫描 + O(1) 插入。
        /// </summary>
        private void ApplyRegionSelection(RectangleF rect)
        {
            if (_allShapes == null || _allShapes.Count == 0)
            {
                toolStripStatusLabel1.Text = "框选：尚未加载图形（请先打开 Gerber 文件或加载模板）";
                return;
            }

            List<SelectedCircle> inRegion = GetCirclesInSelectedRegion(_allShapes, rect);

            // 已选索引：同一把键（含图层），保证"已在 _selectedCircles 里"能被 O(1) 判出
            var selectedKeys = new HashSet<ValueTuple<string, double, double, int>>();
            for (int i = 0; i < _selectedCircles.Count; i++)
            {
                SelectedCircle c = _selectedCircles[i];
                selectedKeys.Add(ShapeKey(c.Layer, c.X, c.Y, c.Shape));
            }

            var snapshot = new List<SelectionSnapshot>();
            var added = new List<SelectedCircle>();

            for (int i = 0; i < inRegion.Count; i++)
            {
                SelectedCircle s = inRegion[i];
                if (s.IsSelected) continue;   // 本来就是选中状态：既不重复加，也不进撤销快照

                var key = ShapeKey(s.Layer, s.X, s.Y, s.Shape);
                if (selectedKeys.Add(key))
                {
                    added.Add(s);
                }
                // 键已存在（Gerber 里同位置同形状的重复绘制）时不再入列表，
                // 但仍要把 IsSelected 置真 —— 否则"视觉已选"与"列表成员"会脱节。

                snapshot.Add(new SelectionSnapshot { Shape = s, WasSelected = false });
                s.IsSelected = true;
            }

            if (snapshot.Count == 0)
            {
                // 两种情况要分开说：框里本来就没图形 vs 框里的图形都已选中（累积语义下的常态）。
                toolStripStatusLabel1.Text = inRegion.Count == 0
                    ? "框选区域为空：该范围内没有图形"
                    : $"框选完成：区域内 {inRegion.Count} 个图形都已选中，无新增";
                return;
            }

            _selectedCircles.AddRange(added);
            _regionUndoStack.Add(snapshot);
            if (_regionUndoStack.Count > RegionUndoDepth) _regionUndoStack.RemoveAt(0);

            KLog.Info($"框选选择：命中 {inRegion.Count} 个，本次新增 {snapshot.Count} 个，" +
                      $"已选合计 {_selectedCircles.Count} 个（撤销栈 {_regionUndoStack.Count} 步）");

            UpdateSelectionStatusText($"框选新增 {snapshot.Count} 个");
        }

        /// <summary>
        /// Ctrl+Z：撤销上一次框选造成的选择变化。
        /// 只回滚"被那一次框选改动过"的图形，因此不会影响框选之后逐点选点得到的结果。
        /// </summary>
        private void UndoLastRegionSelection()
        {
            if (_regionUndoStack.Count == 0)
            {
                toolStripStatusLabel1.Text = "没有可撤销的框选操作";
                return;
            }

            int last = _regionUndoStack.Count - 1;
            List<SelectionSnapshot> snapshot = _regionUndoStack[last];
            _regionUndoStack.RemoveAt(last);

            for (int i = 0; i < snapshot.Count; i++)
            {
                SelectionSnapshot snap = snapshot[i];
                SelectedCircle s = snap.Shape;
                if (s.IsSelected == snap.WasSelected) continue;   // 之后被别的操作改回去了，不重复处理

                s.IsSelected = snap.WasSelected;
                if (snap.WasSelected)
                {
                    if (!IsSingleShapeInShapes(s, _selectedCircles)) _selectedCircles.Add(s);
                }
                else
                {
                    _selectedCircles.Remove(s);
                }
            }

            RefreshKWindow();
            UpdateSelectionStatusText($"已撤销上次框选（回滚 {snapshot.Count} 个，剩余 {_regionUndoStack.Count} 步）");
            KLog.Info($"撤销框选：回滚 {snapshot.Count} 个图形，撤销栈剩余 {_regionUndoStack.Count} 步");
        }

        /// <summary>清空撤销栈。切换数据源（重新解析 Gerber / 加载模板 / 清空选点）时必须调用：
        /// 旧快照持有的是上一批图形对象，若留着被 Ctrl+Z 触发，会把早已不在
        /// _allShapes 里的"幽灵点"塞回 _selectedCircles。</summary>
        private void ClearRegionUndoStack()
        {
            if (_regionUndoStack.Count > 0)
            {
                KLog.Info($"清空框选撤销栈（{_regionUndoStack.Count} 步）");
                _regionUndoStack.Clear();
            }
        }

        /// <summary>
        /// 清空选择状态。**必须同时清 _allShapes 上的 IsSelected**，两处不同步就会出现
        /// "视觉已清空、状态仍为已选"的脱节：原来的「清空选点」处理只做
        /// _selectedCircles.Clear()，残留的 IsSelected=true 会让后续框选
        /// 认为这些点"已经选中"而直接跳过，表现为"清空之后再框选，一个都没反应"。
        /// </summary>
        private void ClearAllSelection()
        {
            for (int i = 0; i < _allShapes.Count; i++)
            {
                if (_allShapes[i] != null) _allShapes[i].IsSelected = false;
            }

            _selectedCircles.Clear();
            ClearRegionUndoStack();   // 选择已被整体清空，旧快照再撤销只会"复活"已删的点
        }

        /// <summary>
        /// 解除「框选限定」：清掉矩形并重绘（框随之消失），多选回到全板范围。
        /// 入口：Esc、空格、「清除区域」按钮。
        /// </summary>
        private void ClearSelectedRect()
        {
            _actualSelectedRect = RectangleF.Empty;
            RefreshKWindow();
        }

        //键盘快捷键
        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            // 注：原来这里有一段"焦点在文件名文本框上时不处理快捷键"的检查 ——
            // 那个文本框（txtOutputFile）已随右侧面板删除，检查随之取消。

            // Ctrl+Z：撤销上一次框选。逐点选点不入栈，所以这里只回滚"框选造成的批量变化"，
            // 不会把用户后来一个个点出来的结果一起抹掉。
            if (keyData == (Keys.Control | Keys.Z) && _regionUndoStack.Count > 0)
            {
                UndoLastRegionSelection();
                return true;
            }

            switch (keyData)
            {
                case Keys.F1:
                    // 原「开始选点」按钮的位置 —— 那个按钮已删除，操作说明改挂在这里
                    ShowSelectionHelp();
                    return true;

                case Keys.Escape:
                    // Esc：取消进行中的框选；否则解除「框选限定」（框消失 = 回到全板操作）。
                    // 原来是"退出选点模式"，模式已取消，这个键改承担解除限定的职责。
                    if (kWindowControl1.IsDrawingRectangle1)
                    {
                        kWindowControl1.CancelDrawRectangle1();
                        return true;
                    }
                    if (!_actualSelectedRect.IsEmpty)
                    {
                        ClearSelectedRect();
                        UpdateSelectionStatusText("已解除框选限定");
                        return true;
                    }
                    break;

                case Keys.Q:
                    // 框选（工具栏也有同名按钮）。原来靠"按钮是否 Enabled"防重入，
                    // 按钮移除后改看 _isRegionSelectingMode 这个标志本身。
                    if (!_isRegionSelectingMode)
                    {
                        StartRegionSelectingMode();
                        return true;
                    }
                    break;

                case Keys.Space:
                    // 空格 = 解除框选限定（与工具栏「解除限定」同一件事）
                    ClearSelectedRect();
                    UpdateSelectionStatusText("已解除框选限定");
                    return true;
            }

            // 其他按键交给基类处理
            return base.ProcessCmdKey(ref msg, keyData);
        }

        #endregion

        #region 保存和加载模板文件

        // ───────── 多图层：辅助方法 ─────────

        /// <summary>
        /// 清空当前工程的全部状态，回到"没有打开任何工程"。
        ///
        /// 换工程（新建 / 打开 / 关闭）之前必须走这里。否则上一批图层、选点、工程归属、翻转状态
        /// 会以"幽灵状态"残留 —— 历史上就踩过 IsSelected 残留导致框选一个都不响应的坑，
        /// 所以清空一律走 ClearAllSelection()，不要直接 Clear() 集合。
        /// </summary>
        private void ResetProjectState()
        {
            ClearAllSelection();                     // 复位 _allShapes.IsSelected + 清 _selectedCircles + 清撤销栈
            _layers = new List<LayerInfo>();
            _apertures = new List<Aperture>();
            _allShapes = new List<SelectedCircle>();
            _contentBounds = RectangleF.Empty;
            _actualSelectedRect = RectangleF.Empty;
            _currentTemplatePath = string.Empty;
            _templateGerberFilePath = string.Empty;
            _isTemplateMode = false;
            _firstLoadTag = false;
            Program.FileName = string.Empty;
            ResetMirrorState();

            RefreshLayerPanel();   // 容器跟着清空（_layerPanel 尚未构建时，本方法内部会直接返回）
        }

        /// <summary>
        /// 由 _layers 重算派生的 _apertures（所有图层光圈的扁平聚合）。
        ///
        /// 刻意**不做可见性过滤**：包围盒与视图适配都用它，若隐藏一个图层就让包围盒变小，
        /// 视图会跟着跳，用户会以为图被挪了。可见性只在绘制的最后一刻起作用。
        /// </summary>
        private void RebuildAperturesFromLayers()
        {
            var all = new List<Aperture>();
            for (int i = 0; i < _layers.Count; i++)
            {
                List<Aperture> layerApertures = _layers[i].Apertures;
                if (layerApertures == null) continue;
                all.AddRange(layerApertures);
            }
            _apertures = all;
        }

        /// <summary>
        /// 工程载入的最后一步：重建图形缓存、包围盒，适配视图，刷新状态显示。
        ///
        /// 载入图层后、恢复选点后都要调一次 —— BuildAllShapes 负责把 _selectedCircles 与
        /// _allShapes 的引用关系重新接上（它用已选点建索引复用对象实例，所以必须先有选点）。
        /// 幂等，重复调用无害。
        /// </summary>
        private void FinishProjectLoad()
        {
            BuildAllShapes();
            UpdateContentBounds();
            ResetViewToCenter();
            RefreshLayerPanel();          // 图层容器跟着工程刷新（此时 _layers 与 IsVisible 都已就绪）
            UpdateScaleLabel();
            UpdateSelectionCountLabel();  // 打开工程会恢复选点，这个计数位也要跟着动
            RefreshKWindow();
        }

        /// <summary>
        /// 扫描工程目录里的候选图层文件。
        ///
        /// **不按扩展名白名单挑选** —— GBL / GBS / G1 / .o 这类扩展名太杂，白名单必然漏。
        /// 做法是排除掉"确定不是图层"的几类，其余全部交给导入器判定：
        /// 解析器严格模式下认不出的会被挡下来并跳过，不会有垃圾图形混进画布。
        /// </summary>
        private static List<string> ScanLayerFiles(string folder)
        {
            var result = new List<string>();
            string[] files = Directory.GetFiles(folder);

            for (int i = 0; i < files.Length; i++)
            {
                string ext = Path.GetExtension(files[i]).ToLowerInvariant();

                if (ext == ".json") continue;                                  // circles.json 等选点数据
                if (ext == ".dat") continue;                                   // 尺寸表等附属数据
                if (ext == ".txt" || ext == ".log" || ext == ".md") continue;  // 说明 / 日志

                result.Add(files[i]);
            }

            result.Sort(StringComparer.OrdinalIgnoreCase);   // 稳定顺序：图层序号决定颜色，顺序不能随文件系统变
            return result;
        }

        /// <summary>
        /// 汇总提示被跳过的文件。**一次性提示**，不逐个弹框 ——
        /// 一个工程目录里混着十几个非 Gerber 文件是常态，逐个弹会把人逼疯。
        /// </summary>
        private static void ReportSkippedFiles(IList<string> skipped)
        {
            if (skipped == null || skipped.Count == 0) return;

            const int maxShown = 12;
            var text = new StringBuilder();
            text.AppendLine($"{skipped.Count} 个文件无法解析，已跳过：");
            text.AppendLine();

            int shown = Math.Min(skipped.Count, maxShown);
            for (int i = 0; i < shown; i++) text.AppendLine("  · " + skipped[i]);
            if (skipped.Count > shown) text.AppendLine($"  … 另有 {skipped.Count - shown} 个");

            MessageBox.Show(text.ToString(), "已跳过部分文件",
                MessageBoxButtons.OK, MessageBoxIcon.Information);
        }

        // ───────── 多图层：工程载入 ─────────

        /// <summary>
        /// 新建工程：把一批选中的 Gerber / 钻孔文件作为图层载入。
        ///
        /// 关键行为：
        ///   · **严格模式**解析（ParseFile(path, true)）—— 认不出格式的文件直接判失败，
        ///     而不是像旧代码那样硬按 Gerber 解析、把垃圾图形塞进画布；
        ///   · 解析失败、或解析出来一个图形都没有的文件 → **跳过**，不进 _layers，
        ///     只记进 skipped 名单，最后汇总提示一次；
        ///   · 全部被跳过时明确告知用户，不留一个空画布让人对着发愣；
        ///   · 载入后所有图层默认可见（IsVisible = true），颜色按图层序号分配。
        ///
        /// 返回是否至少载入了一个图层。
        /// </summary>
        private bool LoadProjectFromFiles(IList<string> filePaths)
        {
            if (filePaths == null || filePaths.Count == 0) return false;

            ResetProjectState();

            var layers = new List<LayerInfo>();
            var skipped = new List<string>();
            var parser = new GerberParser();

            for (int i = 0; i < filePaths.Count; i++)
            {
                string path = filePaths[i];
                string displayName = Path.GetFileName(path);

                try
                {
                    parser.LayerName = displayName;   // GetApertureList 会把它回填到每个 Aperture.Layer
                    ParseResult result = parser.ParseFile(path, true);
                    List<Aperture> apertures = parser.GetApertureList();

                    if (!result.Success || apertures == null || apertures.Count == 0)
                    {
                        skipped.Add(displayName);
                        KLog.Info($"新建工程：跳过 {displayName} —— {result.Message}");
                        continue;
                    }

                    int positionCount = 0;
                    for (int k = 0; k < apertures.Count; k++) positionCount += apertures[k].Position.Count;
                    if (positionCount == 0)
                    {
                        skipped.Add(displayName);
                        KLog.Info($"新建工程：跳过 {displayName} —— 解析后没有任何图形");
                        continue;
                    }

                    layers.Add(new LayerInfo
                    {
                        FilePath = path,
                        FileName = displayName,
                        IsVisible = true,
                        Apertures = apertures
                    });

                    KLog.Info($"新建工程：载入图层 {displayName}（{apertures.Count} 个光圈 / {positionCount} 个图形）");
                }
                catch (Exception ex)
                {
                    skipped.Add(displayName);
                    KLog.Error($"新建工程：解析 {path} 时异常", ex);
                }
            }

            if (layers.Count == 0)
            {
                MessageBox.Show(
                    $"所选 {filePaths.Count} 个文件都无法解析，没有可显示的图层。\r\n\r\n" +
                    "本工具支持 Gerber（含 %FSLA / %ADD / G04 / %MO）与钻孔文件（M48 / T0x）。",
                    "无法载入", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return false;
            }

            _layers = layers;
            RebuildAperturesFromLayers();

            // 新建工程时还没有"工程目录"（保存时会问用户存到哪），归属留空；
            // Program.FileName 保留第一个图层路径，供仍按单文件工作的旧逻辑退化使用。
            Program.FileName = layers[0].FilePath;
            _currentTemplatePath = string.Empty;
            _isTemplateMode = false;

            FinishProjectLoad();

            ReportSkippedFiles(skipped);
            toolStripStatusLabel1.Text = $"已载入 {layers.Count} 个图层" +
                (skipped.Count > 0 ? $"，跳过 {skipped.Count} 个无法解析的文件" : "");

            return true;
        }

        /// <summary>
        /// 打开工程：把目录里的全部 Gerber / 钻孔文件作为图层载入，并恢复 circles.json 里的选点。
        ///
        /// 与"新建工程"的区别只有两点：① 图层来自目录扫描而非用户多选；② 会读 circles.json。
        /// 载入成功后工程归属（RootPath）定为这个目录 —— 「保存」就地写回它。
        /// </summary>
        private bool LoadProjectFromFolder(string folder)
        {
            if (string.IsNullOrEmpty(folder) || !Directory.Exists(folder)) return false;

            List<string> files = ScanLayerFiles(folder);
            if (files.Count == 0)
            {
                MessageBox.Show(
                    $"这个目录里没有找到可解析的 Gerber / 钻孔文件：\r\n\r\n{folder}",
                    "无法载入", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return false;
            }

            if (!LoadProjectFromFiles(files)) return false;

            // 归属：RootPath = 本目录
            _currentTemplatePath = folder;

            // 选点文件**只认 circles.json**。
            //
            // 原来是"找不着 circles.json 就退而求其次，拿目录里第一个 *.json 当候选"。这条退路很坑：
            // Gerber 素材目录里常混着 FlyingProbeTesting.json 之类别的 json，它会被当候选读进来，
            // 因为没有 Shapes 字段而**静默跳过** —— 用户看到的是"打开工程后选点没了"，
            // 却没有任何提示，完全无从判断是文件不在、还是程序没读。
            // 认错文件比找不到文件更难查，所以这条退路去掉。
            // （Win32 文件系统不区分大小写，Circles.json / CIRCLES.JSON 一样能被 File.Exists 认出。）
            string jsonPath = Path.Combine(folder, "circles.json");
            bool restoredSelection = false;

            if (File.Exists(jsonPath))
            {
                try
                {
                    dynamic jsonData = JsonConvert.DeserializeObject(File.ReadAllText(jsonPath));
                    if (jsonData != null && jsonData.Shapes != null)
                    {
                        LoadJsonFormat(jsonData, folder);
                        restoredSelection = true;
                        KLog.Info($"打开工程：从 circles.json 恢复 {_selectedCircles.Count} 个选点");

                        // 恢复各图层的勾选状态（v2.1 的 TemplateInfo.Layers）
                        ApplyLayerVisibility(jsonData);
                    }
                    else
                    {
                        // 文件在、内容却不是选点数据 —— 也要说清楚，别静默
                        KLog.Info($"打开工程：{jsonPath} 里没有 Shapes 节点，本次选点为空");
                    }
                }
                catch (Exception ex)
                {
                    // 选点读不出来不该让整个工程打不开 —— 图层已经载好了，只把这一件事说清楚
                    KLog.Error($"打开工程：解析 {jsonPath} 失败", ex);
                    MessageBox.Show(
                        $"图层已载入，但选点文件读取失败，本次选点为空：\r\n\r\n" +
                        $"circles.json\r\n{ex.Message}",
                        "选点未恢复", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                }
            }
            else
            {
                // 明确记一笔：不是"没恢复"，是这个目录里根本没有选点文件
                KLog.Info($"打开工程：{folder} 里没有选点文件 circles.json，本次选点为空");
            }

            // 选点刚被填进 _selectedCircles，必须再走一次收尾把 _allShapes 的引用关系接上
            FinishProjectLoad();

            _templateGerberFilePath = _layers.Count > 0 ? _layers[0].FilePath : string.Empty;
            _isTemplateMode = restoredSelection;
            _firstLoadTag = restoredSelection;

            RememberLastProject("template", folder);

            toolStripStatusLabel1.Text = restoredSelection
                ? $"工程已打开：{folder}（{_layers.Count} 个图层，{_selectedCircles.Count} 个选点）"
                : $"工程已打开：{folder}（{_layers.Count} 个图层 —— 目录内没有 circles.json，选点为空）";

            return true;
        }

        /// <summary>
        /// 按 circles.json 里记录的图层清单恢复各层的显示 / 隐藏。
        ///
        /// 只有 v2.1 及以后的文件才有 TemplateInfo.Layers；旧文件（v2.0）没有这一段，
        /// 此时保持"全部可见"的默认值 —— 不能让老工程因为缺个字段就打开成一片空白。
        ///
        /// 读取走 dynamic，**字段缺失会抛 RuntimeBinderException**（不是给默认值），
        /// 所以整段用 try 包住，任何异常都退回"全部可见"。
        /// </summary>
        private void ApplyLayerVisibility(dynamic jsonData)
        {
            try
            {
                dynamic layers = jsonData.TemplateInfo.Layers;
                if (layers == null) return;

                int applied = 0;
                for (int i = 0; i < _layers.Count; i++)
                {
                    string name = _layers[i].FileName;
                    _layers[i].IsVisible = true;          // 默认：找不到对应记录就保持可见

                    foreach (var item in layers)
                    {
                        string fileName = (string)item.FileName;
                        if (string.Equals(fileName, name, StringComparison.OrdinalIgnoreCase))
                        {
                            _layers[i].IsVisible = (bool)item.IsVisible;
                            applied++;
                            break;
                        }
                    }
                }

                KLog.Info($"打开工程：已恢复 {applied} 个图层的勾选状态");
            }
            catch (Exception ex)
            {
                // 字段缺失 / 类型不符都不该影响工程打开 —— 直接退回"全部可见"
                KLog.Info($"打开工程：未恢复图层勾选状态（{ex.Message}），保持全部可见");
            }
        }

        private void SaveTemplateFile()
        {
            // 工程名现在就是目录名（原 txtOutputFile 输入框随右侧面板删除）。
            // 没有打开的工程时无从决定存到哪里 —— 先让用户新建或打开一个工程。
            string folderName = GetProjectName();
            if (string.IsNullOrEmpty(folderName))
            {
                MessageBox.Show("当前没有打开的工程。\r\n请先用「文件 → 新建工程」或「文件 → 打开工程」载入一个工程。",
                    "提示", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            try
            {
                string targetFolder;

                // 保存落点只看一件事：**当前有没有一个确实存在的工程目录**。
                //
                // 原来的条件是 `_isTemplateMode && !string.IsNullOrEmpty(_currentTemplatePath)`，
                // 而 `_isTemplateMode` 在「打开工程」时被赋成 restoredSelection —— 也就是
                // "该目录里读没读到 circles.json"。后果很具体：
                //   打开一个**还没存过选点**的目录（原始 Gerber 素材目录正是这种），
                //   编辑完点「保存」，程序不写回那个目录，而是跑到参数设置的模板路径下**另建一份**。
                // 用户以为存回了打开的工程，下次再打开那里，选点当然是空的 —— 数据从此分叉。
                // "有归属就地保存"是交接文档定下的规则，判断依据不该绕道 _isTemplateMode。
                bool hasProjectFolder = !string.IsNullOrEmpty(_currentTemplatePath)
                                        && Directory.Exists(_currentTemplatePath);

                if (hasProjectFolder)
                {
                    // 有归属 → 就地保存回它所在目录（不管它在桌面还是设置路径里）。
                    // 想换地方请走「另存到」，这是本程序唯一的"保存"语义。
                    targetFolder = _currentTemplatePath;
                    KLog.Info($"就地保存到当前工程目录: {targetFolder}");
                }
                else
                {
                    // 还没有归属（刚从 Gerber 模式新建、从没保存过）→ 落到参数设置里的模板路径
                    targetFolder = Path.Combine(GetBaseTemplatePath(), folderName);
                    KLog.Info($"在设置路径创建新模板: {targetFolder}");
                }

                // 调用公共保存逻辑
                SaveTemplateToPathWithStateUpdate(targetFolder, folderName);

                // 注：原来这里还有一句 ClearSelectedRect()（保存后把框清掉）。
                // 框的语义已经变成「当前批量操作的限定范围」的指示灯，保存模板与它无关，
                // 顺手清掉会**静默改变之后多选的作用范围**，所以删掉这行。
                //
                // 注：原来上面还有一个"文本框名称与当前模板文件夹名不同 → 另建新模板 + 二次确认"
                // 的分支。它是**死代码**：folderName 来自 GetProjectName()，而在
                // _currentTemplatePath 非空时它返回的就是那个目录名，两者恒等，永远走"同名覆盖"。
                // 它建立在"用名字相同与否来猜存到哪"这个隐式条件上（交接文档第五节第 7 条批评的
                // 正是这点），随本次改动一并删除。
            }
            catch (Exception ex)
            {
                MessageBox.Show($"保存模板失败: {ex.Message}", "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        // 带状态更新的保存逻辑（供SaveTemplateFile使用）
        private void SaveTemplateToPathWithStateUpdate(string targetFolder, string folderName)
        {
            // 先执行基础保存逻辑
            bool isSaveSuccess=SaveTemplateToPath(targetFolder, folderName);
            if (isSaveSuccess)
            {
                _currentTemplatePath = targetFolder;
                _isTemplateMode = true;

                // 「上次工程」必须跟着**实际落点**走。
                // 原来只有「打开工程」才调 RememberLastProject，于是"打开的是 A、存的是 B"这种情形
                // （新建工程后保存到设置路径，紧接着又打开过素材目录，就是日志里那次）下，
                // 下次开机自动恢复的是 A，而 A 里没有刚存的选点 —— 用户看到的就是"选点不见了"。
                RememberLastProject("template", targetFolder);

                KLog.Info($"保存后更新当前模板文件夹路径: {_currentTemplatePath}");

                // 更新界面显示
                // 缩放信息统一从控件的 Viewport 取（唯一数据源）
                UpdateScaleLabel();   // 原为写 label1（已删）：现同时刷新状态栏的工程名与缩放
                toolStripStatusLabel1.Text = $"模板已保存: {targetFolder}";

                // 重置视图并刷新显示
                ResetViewToCenter();
            }
        }

        private void SaveTemplateAs()
        {
            string folderName = GetProjectName();
            if (string.IsNullOrEmpty(folderName))
            {
                MessageBox.Show("当前没有打开的工程。\r\n请先用「文件 → 新建工程」或「文件 → 打开工程」载入一个工程。",
                    "提示", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            // 与「打开工程」用同一个现代文件夹选择器（系统原生 IFileOpenDialog），
            // 让整个程序里的"选目录"体验保持一致 —— 不用老式 FolderBrowserDialog。
            string initialPath = !string.IsNullOrEmpty(_currentTemplatePath) ?
                               Path.GetDirectoryName(_currentTemplatePath) :
                               GetBaseTemplatePath();

            string selectedPath = FolderPicker.PickFolder(
                $"另存工程 '{folderName}' —— 选择要保存到的上级目录", initialPath);

            if (!string.IsNullOrEmpty(selectedPath))
            {
                string targetFolder = Path.Combine(selectedPath, folderName);

                    // 调用公共保存逻辑
                    bool isSaveSuccess = SaveTemplateToPath(targetFolder, folderName);

                    // ⚠ 另存成功后必须把"当前模板归属"指到新位置。
                    //   原实现直接调用**不更新状态**的 SaveTemplateToPath，后果是：另存到新目录之后
                    //   再点"保存"，内容仍写回**旧**目录 —— 用户以为在新位置编辑，实际两处分叉。
                    //   （SaveTemplateFile 走的是会更新状态的 SaveTemplateToPathWithStateUpdate，
                    //     两个入口行为不一致，这里补齐。）
                    if (isSaveSuccess)
                    {
                        _currentTemplatePath = targetFolder;
                        _isTemplateMode = true;
                        RememberLastProject("template", targetFolder);   // 同上：开机恢复要落到真存过的那份
                        KLog.Info($"另存为后更新当前模板文件夹路径: {_currentTemplatePath}");

                        UpdateScaleLabel();   // 原为写 label1（已删）：现同时刷新状态栏的工程名与缩放
                        toolStripStatusLabel1.Text = $"另存为完成，当前模板: {targetFolder}";
                    }
                }
        }


        // 提取公共保存逻辑
        /// <summary>
        /// 原子写文件：先写同目录临时文件，再替换目标。
        /// 避免产生"写了一半"的模板文件 —— 原实现在 File.Delete 与 File.WriteAllText 之间
        /// 若崩溃 / 断电 / 磁盘满，模板就只剩 Gerber 而 JSON 已丢，且原数据不可恢复。
        /// </summary>
        private static void WriteFileAtomic(string path, string content)
        {
            string tmp = path + ".tmp";
            File.WriteAllText(tmp, content, new UTF8Encoding(false));

            if (File.Exists(path))
            {
                // File.Replace 是文件系统级的原子替换；第三参数 null = 不保留备份
                File.Replace(tmp, path, null);
            }
            else
            {
                File.Move(tmp, path);
            }
        }

        private Boolean SaveTemplateToPath(string targetFolder, string folderName)
        {
            try
            {
                // 检查文件夹是否已存在
                bool folderExists = Directory.Exists(targetFolder);

                // 准备 JSON 文件路径（避免重复声明）
                string jsonFileName = "circles.json";
                string jsonFilePath = Path.Combine(targetFolder, jsonFileName);

                if (folderExists)
                {
                    // 把**完整落点**写进文案：原来只显示文件夹名，用户根本看不出这次覆盖的是
                    // 桌面那份、还是参数设置路径下那份 —— 这正是"保存到哪说不清"的主要来源。
                    var result = MessageBox.Show(
                        $"模板已存在，是否覆盖？\r\n\r\n{targetFolder}",
                        "确认覆盖", MessageBoxButtons.YesNo, MessageBoxIcon.Question);
                    if (result != DialogResult.Yes)
                    {
                        return false;
                    }
                    // 注意：这里**不再**预先 File.Delete(jsonFilePath)。
                    // 原来"先删、最后才写"之间存在危险窗口：中间一旦崩溃/断电/磁盘满，
                    // 模板就只剩 Gerber 而 JSON 已丢失，原数据不可恢复。
                    // 覆盖现在由写入端的原子替换完成（见 WriteFileAtomic）。
                }
                else
                {
                    Directory.CreateDirectory(targetFolder);
                }

                // 主图层文件路径（仅用于兼容旧格式的 SourceFile 字段）
                string gerberFilePath = _layers.Count > 0
                    ? _layers[0].FilePath
                    : (_firstLoadTag ? _templateGerberFilePath : Program.FileName);

                // 把**每一个图层**都复制到工程目录。
                // 多图层下这一步是必须的：工程目录不自带全部图层，换台机器打开就只剩一个底图。
                for (int li = 0; li < _layers.Count; li++)
                {
                    LayerInfo layer = _layers[li];
                    if (string.IsNullOrEmpty(layer.FilePath) || !File.Exists(layer.FilePath)) continue;

                    string targetLayerPath = Path.Combine(targetFolder, layer.FileName);

                    // 就地保存时 targetLayerPath 就是 layer.FilePath 本身，跳过即可（否则等于自己覆盖自己）
                    if (File.Exists(targetLayerPath))
                    {
                        KLog.Info($"图层文件已存在，跳过复制: {layer.FileName}");
                        continue;
                    }

                    File.Copy(layer.FilePath, targetLayerPath, false);
                    KLog.Info($"已保存图层文件: {layer.FileName}");
                }

                // 兜底：_layers 为空但仍有单文件路径（旧路径遗留），按原来的单底图方式复制一份
                if (_layers.Count == 0 && !string.IsNullOrEmpty(gerberFilePath) && File.Exists(gerberFilePath))
                {
                    string gerberFileName = Path.GetFileName(gerberFilePath);
                    string targetGerberPath = Path.Combine(targetFolder, gerberFileName);

                    if (!File.Exists(targetGerberPath))
                    {
                        File.Copy(gerberFilePath, targetGerberPath, false);
                        KLog.Info($"已保存Gerber文件: {gerberFileName}");
                    }
                    else
                    {
                        KLog.Info($"Gerber文件已存在，跳过复制: {gerberFileName}");
                    }
                }

                // 按形状类型分组
                var circles = _selectedCircles.Where(s => s.Shape == ApertureShape.Circle).ToList();
                var rectangles = _selectedCircles.Where(s => s.Shape == ApertureShape.Rectangle).ToList();
                var ovals = _selectedCircles.Where(s => s.Shape == ApertureShape.Oval).ToList();
                var box = ViewportHelper.CalculateBoundingBox(_apertures);
                var boxLeft = Math.Round(box.Left, 4);
                var boxTop = Math.Round(box.Top, 4);
                var boxRight = Math.Round(box.Right, 4);
                var boxBottom = Math.Round(box.Bottom, 4);
                // 创建新的JSON数据模型
                var exportData = new
                {
                    TemplateInfo = new
                    {
                        TemplateName = folderName,
                        // SourceFile 保留写第一个图层 —— 兼容只认单底图的旧读取方
                        SourceFile = _layers.Count > 0
                            ? _layers[0].FileName
                            : (Path.GetFileName(gerberFilePath) ?? "Unknown"),
                        // 图层清单（v2.1 新增，可选）：打开工程时据此重建图层容器与各层的勾选状态
                        Layers = _layers.Select(l => new { l.FileName, l.IsVisible }).ToList(),
                        TotalCount = _selectedCircles.Count,
                        SelectedCount = _selectedCircles.Count(c => c.IsSelected),
                        CircleCount = circles.Count,
                        RectangleCount = rectangles.Count,
                        OvalCount = ovals.Count,
                        BoxLeft = boxLeft,
                        BoxTop = boxTop,
                        BoxRight = boxRight,
                        BoxBottom = boxBottom,
                        SaveTime = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"),
                        // 2.1：TemplateInfo 带 Layers（图层清单 + 各层勾选状态）
                        // 2.2：每个图形带 Layer（图层文件名）。没有它，打开工程时选点对象与
                        //      _allShapes 里的图形对不上号 —— 表现为"点一下重复、再点取消不掉"。
                        Version = "2.2",
                        MirrorState = new
                        {
                            IsMirroredX = _isMirroredX,
                            IsMirroredY = _isMirroredY
                        }
                    },
                    Shapes = new
                    {
                        Circles = circles.Select(c => new
                        {
                            Id = c.ID,
                            Layer = c.Layer ?? string.Empty,
                            SizeX = Math.Round(c.Diameter, 4),
                            SizeY = Math.Round(c.Diameter, 4),
                            Positions = new[] { new { X = Math.Round(c.X, 4), Y = Math.Round(c.Y, 4) } }
                        }).ToList(),

                        Rectangles = rectangles.Select(r => new
                        {
                            Id = r.ID,
                            Layer = r.Layer ?? string.Empty,
                            Type = (int)ApertureShape.Rectangle,
                            SizeX = Math.Round(r.Width, 4),
                            SizeY = Math.Round(r.Height, 4),
                            Positions = new[] { new { X = Math.Round(r.X, 4), Y = Math.Round(r.Y, 4) } }
                        }).ToList(),

                        Ovals = ovals.Select(o => new
                        {
                            Id = o.ID,
                            Layer = o.Layer ?? string.Empty,
                            Type = (int)ApertureShape.Oval,
                            SizeX = Math.Round(o.Width, 4),
                            SizeY = Math.Round(o.Height, 4),
                            Rotation = Math.Round(o.Rotation, 2),
                            Positions = new[] { new { X = Math.Round(o.X, 4), Y = Math.Round(o.Y, 4) } }
                        }).ToList()
                    },

                };

                // 序列化为JSON，然后**原子替换**写入：
                // 直接 WriteAllText 在写入途中崩溃会留下半截文件，这里先写临时文件再替换。
                string json = JsonConvert.SerializeObject(exportData, Newtonsoft.Json.Formatting.Indented);
                WriteFileAtomic(jsonFilePath, json);
                // 关键区别：只要不修改基础目录，就不更新_currentTemplatePath

                // 输出保存的模板信息到控制台
                KLog.Info($"=== 模板保存信息 ===");
                KLog.Info($"目标文件夹: {targetFolder}");
                KLog.Info($"Gerber文件: {Path.GetFileName(gerberFilePath)}");
                KLog.Info($"保存时间: {DateTime.Now}");
                KLog.Info($"保存的圆圈数量: {_selectedCircles.Count}");
                KLog.Info($"当前模板文件夹路径: {_currentTemplatePath}");
                KLog.Info("========================================");

                // 注意：不更新以下状态，保持原有工作状态
                // - _currentTemplatePath
                // - _isTemplateMode
                // - _templateSource

                MessageBox.Show($"模板已保存到：{targetFolder}",
                    "保存成功", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return true;
            }
            catch (Exception ex)
            {
                MessageBox.Show($"另存为失败: {ex.Message}", "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return false; 
            }
        }

        private void LoadTemplateFile()
        {
            using (OpenFileDialog openFileDialog = new OpenFileDialog())
            {
                openFileDialog.Title = "选择模板圆圈文件";
                openFileDialog.InitialDirectory = GetBaseTemplatePath();
                openFileDialog.Filter = "圆圈数据文件 (*.json)|*.json|所有文件 (*.*)|*.*";
                openFileDialog.Multiselect = false;

                if (openFileDialog.ShowDialog() == DialogResult.OK)
                {
                    // 模板的身份是一个**文件夹**（circles.json + 底图），所以取选中文件的所在目录
                    LoadTemplateFromFolder(Path.GetDirectoryName(openFileDialog.FileName));
                }
            }
        }

        /// <summary>
        /// 从模板**文件夹**加载（而不是从用户选中的那个 json 文件）。
        ///
        /// 之所以按"文件夹"作为入口：① 模板的身份本来就是"一个文件夹"（circles.json + 底图）；
        /// ② "启动记忆"拿到的是记下来的文件夹路径，走这里就能直接加载，不必伪造一个对话框。
        /// 用户在对话框里选中某个 json 时，取其所在目录传入即可 —— 行为与原来完全一致。
        /// 返回是否加载成功，供启动恢复判断。
        /// </summary>
        private bool LoadTemplateFromFolder(string selectedFolder)
        {
            try
            {
                if (string.IsNullOrEmpty(selectedFolder) || !Directory.Exists(selectedFolder))
                    throw new DirectoryNotFoundException($"模板文件夹不存在：{selectedFolder}");

                string folderName = Path.GetFileName(selectedFolder);

                ResetMirrorState();
                ClearRegionUndoStack();   // 马上要换一批图形对象，旧快照必须作废

                // 设置当前模板文件夹路径
                _currentTemplatePath = selectedFolder;
                KLog.Info($"设置当前模板文件夹路径: {_currentTemplatePath}");

                // 确定要读取的 JSON：优先 circles.json；用户给模板 json 起了别的名字时也认
                string jsonFilePath = Path.Combine(selectedFolder, "circles.json");
                if (!File.Exists(jsonFilePath))
                {
                    string[] jsonCandidates = Directory.GetFiles(selectedFolder, "*.json");
                    if (jsonCandidates.Length == 0)
                        throw new FileNotFoundException("该文件夹里既没有 circles.json，也没有其它 .json 文件");

                    jsonFilePath = jsonCandidates[0];
                    KLog.Info($"未找到 circles.json，改用: {Path.GetFileName(jsonFilePath)}");
                }

                // 读取JSON文件
                string jsonContent = File.ReadAllText(jsonFilePath);
                dynamic jsonData = JsonConvert.DeserializeObject(jsonContent);

                if (jsonData == null)
                    throw new Exception("模板文件内容无效");

                // 检查必需的字段
                if (jsonData.TemplateInfo == null || jsonData.Shapes == null)
                    throw new Exception("JSON格式不正确，缺少TemplateInfo或Shapes字段");

                // 加载新格式数据
                LoadJsonFormat(jsonData, selectedFolder);


                // 查找同文件夹中的Gerber文件
                var candidateFiles = new List<string>();

                candidateFiles.AddRange(Directory.GetFiles(selectedFolder, "*.gbr"));
                if (candidateFiles.Count == 0)
                    candidateFiles.AddRange(Directory.GetFiles(selectedFolder, "*.ger"));

                if (candidateFiles.Count == 0)//排除circles.json和后缀为.dat的文件，剩下的就是Gerber底图
                {
                    candidateFiles.AddRange(Directory.GetFiles(selectedFolder, "*.*")
                        .Where(f => !f.EndsWith(".dat", StringComparison.OrdinalIgnoreCase) && !f.EndsWith("circles.json", StringComparison.OrdinalIgnoreCase)));
                }

                string gerberFilePath = string.Empty;
                if (candidateFiles.Count > 0)
                {
                    // 如果存在多个Gerber底图文件，那么就选文件名和文件夹名相同的那个
                    var match = candidateFiles.FirstOrDefault(f =>
                        Path.GetFileNameWithoutExtension(f).IndexOf(folderName, StringComparison.OrdinalIgnoreCase) >= 0);

                    if (!string.IsNullOrEmpty(match))
                    {
                        gerberFilePath = match;
                    }
                    else
                    {
                        // 若无匹配项，则选择最近修改的文件（常为最新的Gerber）
                        gerberFilePath = candidateFiles.OrderByDescending(f => File.GetLastWriteTime(f)).First();
                    }

                    string gerberFileName = Path.GetFileName(gerberFilePath);

                    _parser.ParseFile(gerberFilePath);
                    _apertures = _parser.GetApertureList();
                    KLog.Info($"已加载关联的Gerber文件: {gerberFileName}");
                }
                else
                {
                    _apertures = new List<Aperture>();
                    KLog.Info("未找到关联的Gerber文件，仅加载模板圆圈");
                }


                _templateGerberFilePath = gerberFilePath;

                // 输出加载的模板信息到控制台
                KLog.Info($"=== 模板加载信息 ===");
                KLog.Info($"模板文件夹: {folderName}");
                KLog.Info($"文件夹路径: {selectedFolder}");
                KLog.Info($"当前模板路径: {_currentTemplatePath}");
                KLog.Info($"Gerber文件: {Path.GetFileName(gerberFilePath) ?? "无"}");
                KLog.Info($"加载时间: {DateTime.Now}");
                KLog.Info($"加载的圆圈数量: {_selectedCircles.Count}");
                KLog.Info("========================================");

                // 更新界面状态
                _isTemplateMode = true;
                _actualSelectedRect = RectangleF.Empty;   // 换了一批图形，旧的框选限定随之作废
                _firstLoadTag = true;
                UpdateProjectLabel();

                // 显示完整路径而非仅文件夹名：加载后"当前在编辑哪个模板"必须一眼可见，
                // 否则用户无法判断接下来点"保存"会写回哪里。
                toolStripStatusLabel1.Text = $"模板已加载: {selectedFolder}";


                // 重建图形缓存与内容包围盒
                BuildAllShapes();
                UpdateContentBounds();

                // 重置视图并刷新显示
                ResetViewToCenter();

                KLog.Info($"加载完成后，当前模板文件夹路径: {_currentTemplatePath}");

                RememberLastProject("template", selectedFolder);
                return true;
            }
            catch (Exception ex)
            {
                MessageBox.Show($"加载模板失败: {ex.Message}", "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return false;
            }
        }

        // 新格式的加载方法
        private void LoadJsonFormat(dynamic jsonData, string selectedFolder)
        {
            try
            {
                // 清空现有数据
                _selectedCircles.Clear();

                // 加载TemplateInfo
                var templateInfo = jsonData.TemplateInfo;
                string folderName = templateInfo.TemplateName?.ToString() ?? Path.GetFileName(selectedFolder);

                // 加载翻转状态
                if (templateInfo.MirrorState != null)
                {
                    _isMirroredX = templateInfo.MirrorState.IsMirroredX ?? false;
                    _isMirroredY = templateInfo.MirrorState.IsMirroredY ?? false;
                }

                // 加载形状数据
                var shapes = jsonData.Shapes;

                // 加载圆形
                if (shapes.Circles != null)
                {
                    foreach (var circle in shapes.Circles)
                    {
                        var newCircle = new SelectedCircle(
                            (double)circle.Positions[0].X,
                            (double)circle.Positions[0].Y,
                            (double)circle.SizeX
                        )
                        {
                            ID = circle.Id?.ToString() ?? "Unknown",
                            Layer = ReadJsonLayer((object)circle),   // v2.2 起才有；老文件读空，靠 BuildAllShapes 回填
                            IsSelected = true
                        };
                        _selectedCircles.Add(newCircle);
                    }
                }

                // 加载矩形
                if (shapes.Rectangles != null)
                {
                    foreach (var rectangle in shapes.Rectangles)
                    {
                        var newRect = new SelectedCircle(
                            (double)rectangle.Positions[0].X,
                            (double)rectangle.Positions[0].Y,
                            (double)rectangle.SizeX,
                            (double)rectangle.SizeY
                        )
                        {
                            ID = rectangle.Id?.ToString() ?? "Unknown",
                            Layer = ReadJsonLayer((object)rectangle),   // v2.2 起才有；老文件读空，靠 BuildAllShapes 回填
                            IsSelected = true
                        };
                        _selectedCircles.Add(newRect);
                    }
                }

                // 加载椭圆
                if (shapes.Ovals != null)
                {
                    foreach (var oval in shapes.Ovals)
                    {
                        var newOval = new SelectedCircle(
                            (double)oval.Positions[0].X,
                            (double)oval.Positions[0].Y,
                            (double)oval.SizeX,
                            (double)oval.SizeY,
                            (double)(oval.Rotation ?? 0)
                        )
                        {
                            ID = oval.Id?.ToString() ?? "Unknown",
                            Layer = ReadJsonLayer((object)oval),   // v2.2 起才有；老文件读空，靠 BuildAllShapes 回填
                            IsSelected = true
                        };
                        _selectedCircles.Add(newOval);
                    }
                }

                // 更新界面
                UpdateProjectLabel();

                KLog.Info($"加载模板: {folderName}");
                KLog.Info($"  - 圆形: {shapes.Circles?.Count ?? 0} 个");
                KLog.Info($"  - 矩形: {shapes.Rectangles?.Count ?? 0} 个");
                KLog.Info($"  - 椭圆: {shapes.Ovals?.Count ?? 0} 个");
            }
            catch (Exception ex)
            {
                throw new Exception($"加载模板失败: {ex.Message}");
            }
        }

        /// <summary>
        /// 读选点条目上的 Layer 字段（v2.2 起才有）。
        ///
        /// 必须容错：读取走 dynamic，**字段不存在会抛 RuntimeBinderException**（不是返回 null），
        /// 而 v2.2 以前的 circles.json 里根本没有 Layer —— 一旦抛出去，外层那个 catch 会把整个
        /// 工程判成"选点文件读取失败"，选点全丢，只因为缺一个可有可无的图层字段。
        ///
        /// 实现走 JObject：JsonConvert.DeserializeObject(string) 的产物运行时就是 JObject，
        /// 它的索引器**不区分大小写、字段缺失返回 null**，比 try/catch 干净，也不会误吞别的异常。
        /// 类型不符时返回空串，交由 BuildAllShapes 的宽松键回填。
        /// </summary>
        /// 参数取 object（调用点写 `ReadJsonLayer((object)circle)`）而不是 dynamic：
        /// 实参的**编译期类型**因此不是 dynamic，调用是**静态绑定**的，不会走运行时绑定器
        /// 去访问本类的 private 成员 —— 那条路虽然合法，但没必要为省一次强转去依赖它。
        private static string ReadJsonLayer(object shapeItem)
        {
            var node = shapeItem as Newtonsoft.Json.Linq.JObject;
            if (node == null) return string.Empty;

            var token = node["Layer"];
            return token == null || token.Type == Newtonsoft.Json.Linq.JTokenType.Null
                ? string.Empty
                : token.ToString();
        }

        // 获取基础模板目录
        private string GetBaseTemplatePath()
        {
            // 优先使用参数设置的路径
            if (!string.IsNullOrEmpty(Properties.Settings.Default.DefaultSavePath) &&
                Directory.Exists(Properties.Settings.Default.DefaultSavePath))
            {
                return Properties.Settings.Default.DefaultSavePath;
            }

            // 否则使用默认模板目录
            string defaultPath = Path.Combine(Application.StartupPath, "template");
            if (!Directory.Exists(defaultPath))
            {
                Directory.CreateDirectory(defaultPath);
            }
            return defaultPath;
        }
        #endregion

        #region 模板文件路径管理方法
        // 获取当前保存路径
        private string GetCurrentSavePath()
        {
            // 优先返回当前工程目录
            if (!string.IsNullOrEmpty(_currentTemplatePath) && Directory.Exists(_currentTemplatePath))
            {
                return _currentTemplatePath;
            }

            // 其次：参数设置里存过的默认保存路径。
            // LoadSavedPath 不再把它写进 _currentTemplatePath，所以这里得自己兜住 ——
            // 否则参数设置窗体永远显示不到用户设过的那个路径。
            if (!string.IsNullOrEmpty(Properties.Settings.Default.DefaultSavePath) &&
                Directory.Exists(Properties.Settings.Default.DefaultSavePath))
            {
                return Properties.Settings.Default.DefaultSavePath;
            }

            // 最后退回默认模板目录
            string defaultPath = Path.Combine(Application.StartupPath, "template");
            return defaultPath;
        }

        // 更新保存路径设置
        private void UpdateSavePath(string newPath)
        {
            if (string.IsNullOrEmpty(newPath) || !Directory.Exists(newPath))
            {
                MessageBox.Show("路径不存在或无效，将使用默认路径", "提示",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            // 保存到应用程序设置中
            Properties.Settings.Default.DefaultSavePath = newPath;
            Properties.Settings.Default.Save();

            MessageBox.Show($"保存路径已更新为: {newPath}", "提示",
                MessageBoxButtons.OK, MessageBoxIcon.Information);
        }

        // 在程序启动时加载保存的路径
        //
        // ⚠ 这里**不再**写 _currentTemplatePath。
        // 那个字段的语义是"当前打开的工程目录"，而 DefaultSavePath 只是"新建工程时的默认落点"，
        // 两者不是一回事。原来把它塞进去的副作用很具体：启动后**没有打开任何工程**时点「保存」，
        // GetProjectName() 会拿 DefaultSavePath 的目录名（通常是 "template"）当工程名，
        // 于是在 ...\template\template 下又套一层。
        // 参数设置窗体要显示"当前保存路径"，走 GetCurrentSavePath() 自己的回退即可。
        private void LoadSavedPath()
        {
            string saved = Properties.Settings.Default.DefaultSavePath;
            if (!string.IsNullOrEmpty(saved) && Directory.Exists(saved))
            {
                KLog.Info($"默认模板保存路径: {saved}");
            }
        }

        #endregion

        #region 翻转功能

        private void btnMirrorX_Click(object sender, EventArgs e)
        {
            MirrorHorizontal();
        }

        private void btnMirrorY_Click(object sender, EventArgs e)
        {
            MirrorVertical();
        }

        // 水平翻转 - 只改变显示，不改变实际坐标
        private void MirrorHorizontal()
        {
            try
            {
                _isMirroredX = !_isMirroredX;

                // 刷新显示（绘制时会自动应用翻转）
                RefreshKWindow();

                string status = _isMirroredX ? "已启用" : "已禁用";
                toolStripStatusLabel1.Text = $"水平翻转 {status}";

                KLog.Info($"水平翻转: {status}");
            }
            catch (Exception ex)
            {
                MessageBox.Show($"水平翻转失败: {ex.Message}", "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        // 垂直翻转 - 只改变显示，不改变实际坐标
        private void MirrorVertical()
        {
            try
            {
                _isMirroredY = !_isMirroredY;

                // 刷新显示（绘制时会自动应用翻转）
                RefreshKWindow();

                string status = _isMirroredY ? "已启用" : "已禁用";
                toolStripStatusLabel1.Text = $"垂直翻转 {status}";

                KLog.Info($"垂直翻转: {status}");
            }
            catch (Exception ex)
            {
                MessageBox.Show($"垂直翻转失败: {ex.Message}", "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        // 重置翻转状态
        private void ResetMirrorState()
        {
            _isMirroredX = false;
            _isMirroredY = false;
        }

        /// <summary>
        /// 镜像变换（绘制时调用）：关于内容包围盒中心做点对称。
        /// 中心点走 _contentBounds 缓存；未翻转时原样返回（out 参数，零分配）。
        ///
        /// 原来这个方法返回 Tuple，且内部**每次调用**都跑一遍
        /// ViewportHelper.CalculateBoundingBox(_apertures) —— 而它在绘制循环里被每个图形各调一次，
        /// 翻转开启时就是 O(N²)：15,359 × 15,359 ≈ 2.36 亿次迭代/帧。这就是"一点翻转就卡死"。
        /// </summary>
        private void ApplyMirrorTransform(double x, double y, out double transformedX, out double transformedY)
        {
            transformedX = x;
            transformedY = y;

            if (!_isMirroredX && !_isMirroredY) return;

            double centerX = _contentBounds.X + _contentBounds.Width / 2.0;
            double centerY = _contentBounds.Y + _contentBounds.Height / 2.0;

            if (_isMirroredX) transformedX = 2 * centerX - x;
            if (_isMirroredY) transformedY = 2 * centerY - y;
        }

        // 矩形翻转变换（中心同样取自缓存）
        private RectangleF ApplyMirrorTransformToRect(RectangleF rect)
        {
            if (!_isMirroredX && !_isMirroredY) return rect;

            double centerX = _contentBounds.X + _contentBounds.Width / 2.0;
            double centerY = _contentBounds.Y + _contentBounds.Height / 2.0;

            float x = rect.X;
            float y = rect.Y;

            if (_isMirroredX) x = (float)(2 * centerX - (rect.X + rect.Width));
            if (_isMirroredY) y = (float)(2 * centerY - (rect.Y + rect.Height));

            return new RectangleF(x, y, rect.Width, rect.Height);
        }

        #endregion

    }
}