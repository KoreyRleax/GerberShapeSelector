using GerberParserTool;
using Korey.SmartWindow.WinForms;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
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

        // 图层 Id → LayerInfo。Id 在图层首次进入 RefreshLayerState() 时分配，
        // **置顶重排不会改变它** —— 图形的归属就是靠这个 Id 建立的。
        //
        // 为什么不拿"shape.Layer 与 layer.FileName"做字符串匹配来现查：
        // 那种匹配一旦有一处对不上（老数据、命名差异、以后改命名规则）就会**静默失效**，
        // 表现为"命中优先级退回纯距离排序"——也就是"怎么点都只选到最大的那个圆"，且无报错可查。
        private readonly Dictionary<int, LayerInfo> _layerById = new Dictionary<int, LayerInfo>();

        // 下一个可用的图层 Id（只增不减）。
        private int _nextLayerId = 1;

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

        // ── 选点划分：插针头类型 h1 / h2 / h3 ──
        //
        // 这是"当前用哪个头"的全局状态（对应 V5 右侧面板那个下拉框，本工程搬进了工具栏）。
        // 左键选点 / 多选扩散 / 框选新增，都把当前值写进图形的 SelectedCircle.HeaderType。
        //
        // 为什么用"当前值 + 写进图形"，而不是"按下标分组存三份列表"：
        // 一个点只属于一个头，挂在图形上就只有**一份真相** —— 绘制、保存、导出读的是同一个字段；
        // 分成三份列表就要处处维护"这个点在哪个列表里"，改一次类型得跨列表搬移。
        private string _currentHeaderType = "h1";

        // 防递归：三个 h toggle 共享同一份状态，互相赋值会触发对方的 CheckedChanged（同 _syncingClickMode）
        private bool _syncingHeader = false;

        // ── 合并模式 ──
        // 进入后左键点击不再"选点"，而是把图形选成**合并候选**；候选满两个 → 取中点生成一个新点。
        // 候选是**独立于选中状态**的一份临时清单：合并期间不动 _selectedCircles，
        // 用户随时退出合并模式，已有选点结果一个都不会变（见 HandleMergeClick 的注释）。
        private bool _isMergeMode = false;
        private readonly List<SelectedCircle> _mergeCandidates = new List<SelectedCircle>();

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

            /// <summary>框选会把新选中的点归到"当前头类型"，所以撤销时这个也要一起回滚。</summary>
            public string WasHeaderType;
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
        // 选点颜色（来自「参数设置 → h1 颜色」）。默认白色。
        // ⚠ 2026-09-21 起它只表示 **h1 那一档**的颜色：h2 / h3 各有自己的设置项（见下）。
        //   这样老工程（所有点都是 h1）打开后的观感与改动前完全一致，参数设置里的这一项也仍然有效。
        private string _selectColor = Properties.Settings.Default.SelectedCircleColor; // 默认白色

        // h2 / h3 的画布配色，同样可在「参数设置」里改（三个头各一行，与 V5 一致）。默认黄 / 绿。
        // 取值时机：字段初始化器（启动读一次）→ 参数窗体确定后回写（见 btnParamSetting_Click）。
        private string _headerH2Color = Properties.Settings.Default.HeaderH2Color;
        private string _headerH3Color = Properties.Settings.Default.HeaderH3Color;
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

            /// <summary>
            /// 该点由**哪个插针头**插：`"h1"` / `"h2"` / `"h3"`。空串或未知值一律按 h1 处理
            /// （见 <see cref="NormalizeHeader"/>），所以老 circles.json 打开后行为与之前完全一致。
            ///
            /// ⚠ 它与 `Layer` 是**两个正交的维度**，别混：
            ///   · `Layer`   = 这个图形来自哪个 Gerber 文件（几何归属，参与唯一键、决定命中优先级）；
            ///   · `HeaderType` = 这个点要哪个插针头来插（工艺分配，不参与任何几何判定）。
            /// 同一个图层上的点可以分属三个头，同一个头的点也可以散布在多个图层上。
            ///
            /// 它只影响两件事：① 画布上的颜色（见 <see cref="HeaderColorOf"/>）；② 导出的分组。
            /// </summary>
            public string HeaderType { get; set; } = "h1";

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

            // ── 图层状态的**运行期副本**（不持久化，不写进 circles.json）──
            //
            // 为什么把状态拷到每个图形上，而不是"拿 shape.Layer 去图层表里现查"：
            // 现查要求两个字符串逐字符相等 —— 一旦对不上（老数据、命名差异、以后改了命名规则），
            // 命中优先级与隐藏过滤会**静默失效**：代码看着改了，实际退回"纯按距离排序"，
            // 表现就是"不论怎么点，还是只能选中最大的那个圆"。拷一份就没有这层脆弱匹配。
            //
            // 三个字段由 MainForm.RefreshLayerState() 统一刷新（载入工程 / 勾选显隐 / 图层置顶）。

            /// <summary>所属图层的会话内 Id（见 <see cref="LayerInfo.Id"/>）。0 = 未知归属。</summary>
            public int LayerId { get; set; } = 0;

            /// <summary>
            /// 所属图层在 _layers 里的序号：**数值越大越靠上层**。命中优先级比较的就是它。
            /// -1 = 未知归属（当作最底层，但**仍可被点中**）。
            /// </summary>
            public int LayerDepth { get; set; } = -1;

            /// <summary>所属图层当前是否被隐藏。隐藏的图形不画、不被点中、不进框选与多选扩散。</summary>
            public bool LayerHidden { get; set; } = false;

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
            /// 会话内唯一 Id，首次载入时由 <see cref="RefreshLayerState"/> 分配，**此后不再变化**。
            /// 图形的归属靠它建立：置顶只改变图层序号，不会改变 Id ——
            /// 若拿序号当身份，一次重排之后"图形属于哪层"就全错了。
            /// </summary>
            public int Id = 0;

            /// <summary>
            /// 配色序号：载入时按当时的位置分配，**不随置顶重排而变**。
            /// 否则用户一点「置顶」，那层的颜色就跟着序号跳到另一个颜色，看着像 bug。
            /// -1 = 尚未分配。
            /// </summary>
            public int ColorIndex = -1;

            /// <summary>
            /// 是否显示。三条语义（详见交接文档第五节第 11 条）：
            /// ① **不画**（该图层的底图与已选点都不画）；② **不选**（点不中 / 框选不到 / 多选不扩散）；
            /// ③ **数据保留**（不动 _selectedCircles，重新勾上原样回来）。
            /// </summary>
            public bool IsVisible = true;

            /// <summary>该图层解析出的光圈（每个光圈含它在该层上的全部位置）。</summary>
            public List<Aperture> Apertures = new List<Aperture>();
        }

        /// <summary>
        /// 图层容器里的一行：左边文件名，中间一个「置顶」按钮，右边一个复选框。
        /// 三个区域横向排开，点击按坐标分流（复选框右侧、置顶按钮中右、其余整行左侧）。
        ///
        /// 为什么自绘而不用 CheckBox / Button：WinForms 里这两个都**不支持透明背景**
        /// （CheckBox 同样没声明 SupportsTransparentBackColor），直接放在黑画布上会是一块不透明的方块。
        /// 做法完全照搬控件库的 OverlayToolbarButton：Control 自绘 + SupportsTransparentBackColor，
        /// OnPaintBackground 只调 base 让父控件（半透明容器 → 画布）先画，本层不叠加任何底色。
        /// </summary>
        /// <summary>
        /// 图层容器的**列几何**。容器里有两类行控件（顶部栏 / 图层行），它们的列位置必须**逐像素一致**，
        /// 否则表头与内容会错开一格（"置顶"两个字压在 ↑ 图标左边或右边）。几何只写一处，两边都调这里。
        ///
        /// 三列自右向左定：复选框 → 置顶列 → 文字区（文字区吃掉剩下的全部宽度）。
        ///
        /// ⚠ 「置顶」列的宽度是**量出来的**、不是写死的 24：顶部栏要在这一列里画出「置顶」两个字，
        /// 而原来只为 ↑ 图标留了 24px，`SystemFonts.DefaultFont` 下两个汉字约 22~26px ——
        /// 写死就会让表头文字被裁掉或溢到复选框上，还会连带把下面每行的文字区宽度算计错。
        /// </summary>
        private static class LayerRowLayout
        {
            /// <summary>复选框边长（像素）。</summary>
            public const int BoxSize = 12;

            /// <summary>↑ 图标所需的最小列宽 —— 无论文字多窄都不小于它。</summary>
            private const int TopIconMinWidth = 24;

            private static readonly int _topColumnWidth = MeasureTopColumnWidth();

            /// <summary>「置顶」列宽 = max(图标所需 24, 「置顶」文字宽 + 6)。</summary>
            public static int TopColumnWidth { get { return _topColumnWidth; } }

            private static int MeasureTopColumnWidth()
            {
                return Math.Max(TopIconMinWidth, TextWidth("置顶") + 6);
            }

            /// <summary>量一段文字的像素宽（与绘制同字体、同 flags，避免量出来的和画出来的对不上）。</summary>
            public static int TextWidth(string text)
            {
                if (string.IsNullOrEmpty(text)) return 0;
                Size t = TextRenderer.MeasureText(text, SystemFonts.DefaultFont,
                                                  new Size(int.MaxValue, int.MaxValue),
                                                  TextFormatFlags.NoPadding);
                return t.Width;
            }

            /// <summary>
            /// 行宽 = 左右留白 + 文字 + 间隔 + **置顶列占位** + 间隔 + 复选框。
            /// 置顶列对**所有行**都算进去（最上层那行只是不画 ↑ 图标）—— 否则它短一截，
            /// 复选框就会跟别的行错开一格。各行之间还会再统一取最大值，见 RefreshLayerPanel。
            /// </summary>
            public static int RowWidth(string text)
            {
                int textWidth = TextWidth(string.IsNullOrEmpty(text) ? "M" : text);
                return KWindowOptions.ToolbarButtonPaddingX * 2
                     + textWidth + 8
                     + TopColumnWidth + 8
                     + BoxSize;
            }

            /// <summary>置顶列（贴复选框左侧）。</summary>
            public static Rectangle TopColumn(int width, int height)
            {
                int right = width - BoxSize - KWindowOptions.ToolbarButtonPaddingX - 8;
                return new Rectangle(right - TopColumnWidth, 0, TopColumnWidth, height);
            }

            /// <summary>复选框（贴右）。宽高比边长小 1，是原来就有的写法（给描边留出半个像素）。</summary>
            public static Rectangle CheckBox(int width, int height)
            {
                int boxX = width - BoxSize - KWindowOptions.ToolbarButtonPaddingX;
                return new Rectangle(boxX, (height - BoxSize) / 2, BoxSize - 1, BoxSize - 1);
            }

            /// <summary>文字区宽度（左对齐，吃掉剩下的全部宽度）。</summary>
            public static int TextAreaWidth(int width)
            {
                return width - BoxSize - KWindowOptions.ToolbarButtonPaddingX * 2 - 8 - (TopColumnWidth + 8);
            }
        }

        private sealed class LayerRowControl : Control
        {
            private bool _hover;
            private bool _hoverTopButton;            // 光标是否停在置顶按钮上
            private bool _checked;
            private bool _isTopLayer;                // 已经在最上层：不画置顶按钮（点了也没用）

            /// <summary>这一行对应 _layers 里的序号。跟着图层走，不跟着行号走。</summary>
            public int LayerIndex = -1;

            /// <summary>勾选变化：(图层序号, 是否可见)。由宿主转发给 SetLayerVisible。</summary>
            public event Action<int, bool> VisibilityChanged;

            /// <summary>点了「置顶」：(图层序号)。宿主把它移到最上层、并让容器重排。</summary>
            public event Action<int> TopRequested;

            public LayerRowControl(string fileName, bool isVisible, int layerIndex, bool isTopLayer)
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
                _isTopLayer = isTopLayer;
                Text = fileName;

                Height = KWindowOptions.ToolbarButtonHeight;
                Width = MeasureRowWidth(fileName);   // 先按自身文字宽；RefreshLayerPanel 会统一成同一宽度
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

            /// <summary>行宽（几何在 LayerRowLayout，顶部栏与图层行共用同一份）。</summary>
            public static int MeasureRowWidth(string text)
            {
                return LayerRowLayout.RowWidth(text);
            }

            protected override void OnMouseEnter(EventArgs e) { _hover = true; Invalidate(); base.OnMouseEnter(e); }

            protected override void OnMouseLeave(EventArgs e)
            {
                _hover = false;
                _hoverTopButton = false;
                Invalidate();
                base.OnMouseLeave(e);
            }

            /// <summary>置顶按钮占据的横向区间（贴在复选框左边）。</summary>
            private Rectangle TopButtonRect()
            {
                return LayerRowLayout.TopColumn(Width, Height);
            }

            private bool IsInTopButton(int x, int y)
            {
                if (_isTopLayer) return false;   // 已经在最上层：整个行都是"切换显隐"
                return TopButtonRect().Contains(x, y);
            }

            protected override void OnMouseMove(MouseEventArgs e)
            {
                bool over = IsInTopButton(e.X, e.Y);
                if (over != _hoverTopButton)
                {
                    _hoverTopButton = over;
                    Invalidate();
                }
                base.OnMouseMove(e);
            }

            /// <summary>
            /// 按下即响应（不走 Click）：一行里有两个可点区域，必须按**坐标**分流。
            /// Click 事件在 MouseUp 时触发、拿不到可靠的分区语义，容易把"点置顶"误判成"切换显隐"。
            /// </summary>
            protected override void OnMouseDown(MouseEventArgs e)
            {
                if (e.Button == MouseButtons.Left)
                {
                    if (IsInTopButton(e.X, e.Y))
                    {
                        if (TopRequested != null) TopRequested(LayerIndex);
                    }
                    else
                    {
                        Checked = !Checked;      // 触发 VisibilityChanged
                    }
                }
                base.OnMouseDown(e);
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

                // 置顶按钮（贴复选框左侧）。**已经在最上层就不画**（点了没作用，画出来只会误导），
                // 但它的位置照留 —— 所有行的文字区宽度必须一致，复选框才能纵向对齐。
                if (!_isTopLayer)
                {
                    Rectangle btn = TopButtonRect();
                    if (_hoverTopButton)
                    {
                        Color hover = KWindowOptions.ToolbarButtonHoverColor;
                        if (hover.A > 0)
                        {
                            using (var brush = new SolidBrush(hover)) e.Graphics.FillRectangle(brush, btn);
                        }
                    }

                    // 图标：向上箭头（实心三角头 + 短箭杆）。
                    // 用几何自绘而不是"↑"字符或图标字体 —— 不赌系统里装了哪个字形，
                    // 也不受 DPI 缩放影响（字符在某些字体下会缺字或大小对不上）。
                    float cx = btn.Left + btn.Width / 2f;
                    float cy = btn.Top + btn.Height / 2f;

                    using (var brush = new SolidBrush(fore))
                    using (var pen = new Pen(fore, 1.6f))
                    {
                        // 箭杆
                        e.Graphics.DrawLine(pen, cx, cy - 1f, cx, cy + 5f);

                        // 三角头（顶点朝上）
                        e.Graphics.FillPolygon(brush, new[]
                        {
                            new PointF(cx, cy - 6f),
                            new PointF(cx - 4.5f, cy),
                            new PointF(cx + 4.5f, cy)
                        });
                    }
                }

                // 文字（左），绘制与测量统一用 SystemFonts.DefaultFont，避免测量的宽度和实际画出来的对不上
                var textRect = new Rectangle(
                    KWindowOptions.ToolbarButtonPaddingX,
                    0,
                    LayerRowLayout.TextAreaWidth(Width),
                    Height);
                TextRenderer.DrawText(e.Graphics, Text, SystemFonts.DefaultFont, textRect, fore,
                                      TextFormatFlags.Left | TextFormatFlags.VerticalCenter
                                    | TextFormatFlags.NoPadding | TextFormatFlags.EndEllipsis);

                // 复选框（右）
                Rectangle box = LayerRowLayout.CheckBox(Width, Height);
                int boxX = box.Left;
                int boxY = box.Top;

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
                            new Point(boxX + 2, boxY + LayerRowLayout.BoxSize / 2 - 1),
                            new Point(boxX + LayerRowLayout.BoxSize / 2 - 1, boxY + LayerRowLayout.BoxSize - 4),
                            new Point(boxX + LayerRowLayout.BoxSize - 3, boxY + 2)
                        });
                    }
                }
            }
        }

        /// <summary>
        /// 图层容器的**顶部栏**（表头行）：三段与下面每一行**逐列对齐** ——
        /// 左「文件名」、中「置顶」、右一个方框。
        /// 前两段是**纯文字**（列名，不响应点击），只有右边的方框是可点的：全选 / 取消全选。
        ///
        /// 【为什么只让方框可点，不是整行可点】
        /// 整行可点意味着"想看清表头"的一次误触会把**所有图层一起隐藏**。按规则 R1 数据当然还在、
        /// 重新勾上就全回来，但用户的第一反应会是"我的图层全没了"—— 可点的东西就该长得像可点的东西。
        /// 所以热区只在方框周围（12px 的框再外扩 6px，够手指点又不至于误触）。
        ///
        /// 【方框是三态的】全勾 → 打勾；部分勾 → 一横（半选）；全不勾 → 空。
        /// 半选态**只影响显示**，点击行为始终只有两条：**全勾时点 = 全部取消；其余（含半勾）点 = 全部勾上**。
        /// 这样"想看全部 → 点一下"永远成立，用户不必先数现在勾了几个。
        ///
        /// 自绘的理由与 LayerRowControl 相同：CheckBox 不支持透明背景，放在黑画布上会是一块不透明方块。
        /// </summary>
        private sealed class LayerHeaderControl : Control
        {
            /// <summary>方框外扩的热区半宽（像素）。</summary>
            private const int HitPadding = 6;

            private bool _hoverBox;
            private bool _allChecked;      // 全部图层可见 → 打勾
            private bool _anyChecked;      // 有任一可见 → 半选

            /// <summary>点了方框。参数是**目标状态**（true = 全部显示）。</summary>
            public event Action<bool> SelectAllRequested;

            public LayerHeaderControl(bool allChecked, bool anyChecked)
            {
                SetStyle(ControlStyles.UserPaint
                       | ControlStyles.AllPaintingInWmPaint
                       | ControlStyles.OptimizedDoubleBuffer
                       | ControlStyles.SupportsTransparentBackColor
                       | ControlStyles.ResizeRedraw, true);

                BackColor = Color.Transparent;
                ForeColor = KWindowOptions.ToolbarButtonForeColor;
                // 光标**不**整行设成手型：本行只有右边的方框可点，
                // 整行手型会暗示"点哪里都行"，点上去却没反应 —— 与"可点的东西长得像可点的"相反。
                // 所以改在 OnMouseMove 里按热区切换。
                Cursor = Cursors.Default;
                TabStop = false;

                _allChecked = allChecked;
                _anyChecked = anyChecked;

                Height = KWindowOptions.ToolbarButtonHeight;
                Width = LayerRowLayout.RowWidth("文件名");   // RefreshLayerPanel 会统一成同一宽度
            }

            private Rectangle BoxHitRect()
            {
                return Rectangle.Inflate(LayerRowLayout.CheckBox(Width, Height), HitPadding, HitPadding);
            }

            protected override void OnMouseEnter(EventArgs e) { Invalidate(); base.OnMouseEnter(e); }

            protected override void OnMouseLeave(EventArgs e)
            {
                _hoverBox = false;
                Cursor = Cursors.Default;
                Invalidate();
                base.OnMouseLeave(e);
            }

            protected override void OnMouseMove(MouseEventArgs e)
            {
                bool over = BoxHitRect().Contains(e.X, e.Y);
                Cursor = over ? Cursors.Hand : Cursors.Default;   // 手型只出现在真能点的那块
                if (over != _hoverBox)
                {
                    _hoverBox = over;
                    Invalidate();
                }
                base.OnMouseMove(e);
            }

            /// <summary>
            /// 按下即响应（与图层行一致，不走 Click）：本行的"可点区域"只有右边一小块，
            /// 按坐标判定比 MouseUp 的 Click 语义可靠。
            /// </summary>
            protected override void OnMouseDown(MouseEventArgs e)
            {
                if (e.Button == MouseButtons.Left && BoxHitRect().Contains(e.X, e.Y))
                {
                    // 全勾 → 全部取消；只要不是全勾（含半勾）→ 全部勾上。
                    if (SelectAllRequested != null) SelectAllRequested(!_allChecked);
                }
                base.OnMouseDown(e);
            }

            protected override void OnPaint(PaintEventArgs e)
            {
                // 表头文字比图层名**淡一档**：它只是列名，不该和下面的内容抢注意力。
                Color fore = Color.FromArgb((int)(ForeColor.A * 0.72f), ForeColor);
                if (!Enabled) fore = Color.FromArgb(110, fore);

                // 左：「文件名」（与下面每行的图层名左对齐）
                var textRect = new Rectangle(KWindowOptions.ToolbarButtonPaddingX, 0,
                                             LayerRowLayout.TextAreaWidth(Width), Height);
                TextRenderer.DrawText(e.Graphics, "文件名", SystemFonts.DefaultFont, textRect, fore,
                                      TextFormatFlags.Left | TextFormatFlags.VerticalCenter
                                    | TextFormatFlags.NoPadding | TextFormatFlags.EndEllipsis);

                // 中：「置顶」（占位 = 下面每行的 ↑ 图标那一列，居中）
                Rectangle column = LayerRowLayout.TopColumn(Width, Height);
                TextRenderer.DrawText(e.Graphics, "置顶", SystemFonts.DefaultFont, column, fore,
                                      TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter
                                    | TextFormatFlags.NoPadding | TextFormatFlags.EndEllipsis);

                // 右：全选方框
                Rectangle box = LayerRowLayout.CheckBox(Width, Height);

                if (_hoverBox)
                {
                    Color hover = KWindowOptions.ToolbarButtonHoverColor;
                    if (hover.A > 0)
                    {
                        using (var brush = new SolidBrush(hover))
                        {
                            // 只高亮方框四周一小圈，不铺满整个热区 —— 铺满会像个按钮，与"复选框"的印象冲突
                            e.Graphics.FillRectangle(brush, Rectangle.Inflate(box, 3, 3));
                        }
                    }
                }

                Color boxColor = Enabled ? Color.FromArgb((int)(ForeColor.A * 0.9f), ForeColor) : fore;
                using (var pen = new Pen(boxColor, 1f))
                {
                    e.Graphics.DrawRectangle(pen, box);
                }

                if (_allChecked)
                {
                    // 勾：两笔折线（与图层行完全相同的画法）
                    using (var pen = new Pen(fore, 1.6f))
                    {
                        e.Graphics.DrawLines(pen, new[]
                        {
                            new Point(box.Left + 2, box.Top + LayerRowLayout.BoxSize / 2 - 1),
                            new Point(box.Left + LayerRowLayout.BoxSize / 2 - 1, box.Top + LayerRowLayout.BoxSize - 4),
                            new Point(box.Left + LayerRowLayout.BoxSize - 3, box.Top + 2)
                        });
                    }
                }
                else if (_anyChecked)
                {
                    // 半选：一横。表示"只显示了一部分图层"—— 比画成空框诚实。
                    using (var pen = new Pen(fore, 1.6f))
                    {
                        int y = box.Top + LayerRowLayout.BoxSize / 2;
                        e.Graphics.DrawLine(pen, box.Left + 2, y, box.Left + LayerRowLayout.BoxSize - 3, y);
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

            // 拖放：把 Gerber / 钻孔文件直接拖进窗口。
            // 窗体与画布**都要挂** —— 鼠标落点几乎总在画布上，只设窗体会收不到消息。
            // 两处用同一个处理函数，所以拖到工具栏 / 状态栏 / 路径栏也同样生效。
            this.AllowDrop = true;
            this.DragEnter += OnFileDragEnter;
            this.DragDrop += OnFileDragDrop;
            kWindowControl1.AllowDrop = true;
            kWindowControl1.DragEnter += OnFileDragEnter;
            kWindowControl1.DragDrop += OnFileDragDrop;

            this.StartPosition = FormStartPosition.Manual;

            UpdateScaleLabel();
        }

        private void OnViewChanged(object sender, EventArgs e)
        {
            // 视口变化（缩放 / 平移 / 适配）→ 刷新状态栏。
            // 放在这里而不是绘制回调里，是为了避免"绘制 → 改 UI → 重绘"的递归。
            UpdateScaleLabel();
        }

        private void MainForm_Load(object sender, EventArgs e)
        {
            // 默认工作路径现在**有默认值**（程序目录下的 Broad）—— 启动时保证它存在，
            // 否则"新建工程载入完自动落盘"那一步会因为目录不存在而退化成弹框问位置。
            EnsureDefaultProjectRoot();

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

        // 插针头类型（h1/h2/h3）与合并模式 —— 都是工具栏上的 toggle
        private OverlayToolbarButton _btnHeaderH1;
        private OverlayToolbarButton _btnHeaderH2;
        private OverlayToolbarButton _btnHeaderH3;
        private OverlayToolbarButton _btnMerge;

        // 画布右上角的图层容器（纵列表：每行 = 文件名 + 复选框）。
        // 它**不是**用 AttachOverlayToolbar 挂的 —— 那个 API 是单浮层，会把上面这条工具栏顶掉。
        // 详见 BuildLayerPanel() 的注释。
        private OverlayToolbar _layerPanel;

        // 图层行的悬停提示。置顶按钮现在只有一个箭头图标、没有文字，
        // 第一次用的人猜不出它是干什么的 —— 用 ToolTip 补上说明（整行一条，覆盖两种操作）。
        private ToolTip _layerRowToolTip;

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

            // ② 插针头类型：h1 / h2 / h3（互斥的三个 toggle）——
            //    决定"接下来选中的点归哪个头"，与「单选 / 多选」同构：都是"当前用哪种语义"的开关。
            //    按钮上只写 h1/h2/h3，与状态栏、右键菜单显示的名字一致（颜色见 HeaderColorOf）。
            _btnHeaderH1 = _overlayToolbar.AddToggleButton("h1", true, (s, e) => SetCurrentHeader("h1"));
            _btnHeaderH2 = _overlayToolbar.AddToggleButton("h2", false, (s, e) => SetCurrentHeader("h2"));
            _btnHeaderH3 = _overlayToolbar.AddToggleButton("h3", false, (s, e) => SetCurrentHeader("h3"));

            _overlayToolbar.AddSeparator();

            // ③ 合并（互斥无关，单纯是"进入 / 退出合并模式"的开关）
            _btnMerge = _overlayToolbar.AddToggleButton("合并", false, (s, e) =>
            {
                SetMergeMode(_btnMerge.Checked);
            });

            _overlayToolbar.AddSeparator();

            // ④ 框选与限定
            _overlayToolbar.AddButton("框选区域 (Q)", (s, e) => StartRegionSelectingMode());
            _overlayToolbar.AddButton("解除限定", (s, e) => ReleaseRegionScope());

            _overlayToolbar.AddSeparator();

            // ⑤ 清空与底图
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

            // 行的悬停提示。创建一次即可，行是反复重建的，SetToolTip 会覆盖旧关联。
            _layerRowToolTip = new ToolTip
            {
                InitialDelay = 400,
                ReshowDelay = 200,
                AutoPopDelay = 6000
            };

            kWindowControl1.Controls.Add(_layerPanel);
            kWindowControl1.Resize += (s, e) => RepositionLayerPanel();

            RefreshLayerPanel();
        }

        /// <summary>按当前 _layers 重建容器内容。工程载入后调用。</summary>
        private void RefreshLayerPanel()
        {
            // 先重算图层状态（叠放深度 + 隐藏），且必须在下面那个"没有容器就返回"的早退**之前** ——
            // 状态是命中测试与选点绘制的依据，跟容器在不在没有关系。
            RefreshLayerState();

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

                // 统一行宽（取最长的一行）：行宽若各随自己的文字长度变，同一列的复选框就会左右参差。
                // 顶部栏也要一起算进来 —— 它是首行，比任何一行窄都会让整列错位。
                int maxRowWidth = LayerRowControl.MeasureRowWidth("文件名");
                for (int i = 0; i < _layers.Count; i++)
                {
                    if (_layers[i] == null) continue;
                    maxRowWidth = Math.Max(maxRowWidth, LayerRowControl.MeasureRowWidth(_layers[i].FileName));
                }

                // ① 顶部栏（表头）：文件名 | 置顶 | 【全选】。**必须最先加** ——
                //    TopDown 流式布局里"先加的在上面"，它就是列表的首行。
                // 它的方框是三态的，所以要先把"全可见 / 有可见"这两个统计算出来喂给它。
                bool allVisible = true;
                bool anyVisible = false;
                for (int i = 0; i < _layers.Count; i++)
                {
                    if (_layers[i] == null) continue;
                    if (_layers[i].IsVisible) anyVisible = true;
                    else allVisible = false;
                }

                var header = new LayerHeaderControl(allVisible, anyVisible)
                {
                    Width = maxRowWidth
                };
                header.SelectAllRequested += OnLayerHeaderSelectAllRequested;
                _layerPanel.Controls.Add(header);

                if (_layerRowToolTip != null)
                {
                    _layerRowToolTip.SetToolTip(header,
                        "文件名 / 置顶 是列名，点它们没有作用\r\n" +
                        "点最右边那个方框：显示全部图层 / 隐藏全部图层\r\n" +
                        "（隐藏只是不画、不选，选点数据仍然保留，重新全选就回来）");
                }

                // ⚠ **倒序**填充：列表第一行 = _layers 的最后一项 = 画在**最上层**的那层。
                //    这样容器里的上下顺序与画面上的叠放顺序一致 —— 原来两者正好相反
                //    （首行是最先加载、画在最底下的那个），用户得在脑子里翻一次才算得对，
                //    「置顶」按钮的语义（点了就排到首行 = 压在最上面）也就无从谈起。
                for (int i = _layers.Count - 1; i >= 0; i--)
                {
                    LayerInfo layer = _layers[i];

                    // isTopLayer：已经在最上层的那行不画「置顶」按钮（点了没作用，画出来只会误导）
                    var row = new LayerRowControl(layer.FileName, layer.IsVisible, i, i == _layers.Count - 1)
                    {
                        Width = maxRowWidth
                    };
                    row.VisibilityChanged += OnLayerRowVisibilityChanged;
                    row.TopRequested += OnLayerRowTopRequested;
                    _layerPanel.Controls.Add(row);

                    if (_layerRowToolTip != null)
                    {
                        string tip = layer.FileName + "\r\n点这一行：显示 / 隐藏该图层";
                        if (i != _layers.Count - 1)
                        {
                            tip += "\r\n点 ↑ 图标：移到最上层（压在所有图层之上，并排到列表首行）";
                        }
                        else
                        {
                            tip += "\r\n（已是最上层）";
                        }
                        _layerRowToolTip.SetToolTip(row, tip);
                    }
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

            // 显隐变了必须立刻把状态刷进图形 —— 否则会出现"图层刚隐藏，它的选点还画着、还能点中"的滞后。
            RefreshLayerState();

            // 规则 R1：可见性只影响**呈现与可点性** —— 不碰 _selectedCircles，也不碰 _apertures。
            // 于是：隐藏图层的选点不再绘制、不再能被点中 / 框选，但**数据仍留在集合里**，
            // 重新勾上就原样回来（隐藏是临时的视觉操作，不该顺手删掉用户选的 3852 个点）。
            // 包围盒因此也不会跳 —— 它按 _apertures 算，不受显隐影响。
            RefreshKWindow();
        }

        /// <summary>
        /// 容器**顶部栏的方框**被点：把所有图层一次性设为可见 / 不可见（全选 / 取消全选）。
        ///
        /// 语义与单行切换（OnLayerRowVisibilityChanged）**完全一致**（规则 R1）：只改
        /// `LayerInfo.IsVisible`，**不碰 `_selectedCircles` / `_apertures`** ——
        /// 隐藏期间选点数据原样留着，重新全选就全部回来，包围盒也不会跳。
        ///
        /// 【为什么这里必须重建容器，而单行切换只需刷新】
        /// 每一行自己的复选框是**该行控件的私有状态**（`LayerRowControl._checked`），
        /// 全选改了所有图层，就必须让所有行的方框跟着变 —— 唯一同步手段是重建（RefreshLayerPanel）。
        /// 单行切换时被点的那一行已经是最新的，所以它只调 RefreshLayerState + RefreshKWindow，
        /// 不重建（重建会顺手把鼠标下的悬停态清掉，手感变差）。
        /// </summary>
        private void SetAllLayersVisible(bool visible)
        {
            if (_layers.Count == 0) return;

            for (int i = 0; i < _layers.Count; i++)
            {
                if (_layers[i] != null) _layers[i].IsVisible = visible;
            }

            KLog.Info($"图层显隐（全选）：{_layers.Count} 个图层 = {visible}");
            LogLayerOrder();     // 顺带把层序与显隐打进日志，方便回看这一下到底改了什么

            RefreshLayerPanel(); // 内部先 RefreshLayerState()，再重建顶部栏与全部行
            RefreshKWindow();

            toolStripStatusLabel1.Text = visible
                ? $"已显示全部图层（共 {_layers.Count} 个）"
                : $"已隐藏全部图层（共 {_layers.Count} 个，选点数据保留，点方框即可恢复）";
        }

        /// <summary>容器顶部栏的方框被点。参数 = 目标状态（true 表示"全部显示"）。</summary>
        private void OnLayerHeaderSelectAllRequested(bool visible)
        {
            SetAllLayersVisible(visible);
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

        /// <summary>
        /// 切换"当前插针头类型"（h1 / h2 / h3）。工具栏三个 toggle、右键菜单三项共用这一个入口。
        ///
        /// 它只决定"**接下来**选中的点归哪个头"，**不动已经选好的点** —— 用户切到 h2 是为了接着选 h2 的点，
        /// 不是要把刚才那批 h1 一起改掉。要改已有点的类型：在目标 h 下重新点它一下（选中会写入当前值），
        /// 或者用框选把一片重新框进来（框选同样写当前值，且可以用 Ctrl+Z 撤销 —— 见 ApplyRegionSelection）。
        ///
        /// 防递归的理由与 SetClickMode 完全相同：给 toggle 赋 Checked 会触发它自己的 CheckedChanged。
        /// </summary>
        private void SetCurrentHeader(string header)
        {
            string h = NormalizeHeader(header);

            if (_syncingHeader) return;
            _syncingHeader = true;
            try
            {
                _currentHeaderType = h;
                if (_btnHeaderH1 != null) _btnHeaderH1.Checked = (h == "h1");
                if (_btnHeaderH2 != null) _btnHeaderH2.Checked = (h == "h2");
                if (_btnHeaderH3 != null) _btnHeaderH3.Checked = (h == "h3");
            }
            finally { _syncingHeader = false; }

            KLog.Info($"切换插针头类型：{h}");
            UpdateSelectionStatusText($"已切换到 {h}（接下来选中的点归 {h}）");
        }

        /// <summary>
        /// 进入 / 退出**合并模式**。
        ///
        /// 合并的语义（照 V5，保证两个版本的操作习惯一致）：
        ///   进入后在画布上依次点**两个**图形 → 取两者的**中点**、形状与尺寸沿用**第一个**，
        ///   生成一个新选点（归当前 h 类型）；**原来那两个点保留不动**。
        ///
        /// 为什么候选清单独立于 _selectedCircles：合并是"看一眼再决定"的动作，
        /// 若点一下就顺手把图形选中了，用户一旦退出合并模式还得自己把那两个点取消掉 ——
        /// 而"我只点了一下，选点数量就变了"正是本工程最忌讳的那种惊喜（同 R1 的取舍）。
        /// </summary>
        private void SetMergeMode(bool on)
        {
            _isMergeMode = on;
            _mergeCandidates.Clear();

            if (_btnMerge != null) _btnMerge.Checked = on;

            KLog.Info(on ? "进入合并模式" : "退出合并模式");
            RefreshKWindow();
            UpdateSelectionStatusText(on
                ? "合并模式：请依次点两个图形（取中点合成一个新点，原点保留；再点「合并」退出）"
                : "已退出合并模式");
        }

        /// <summary>
        /// 合并模式下的一次点击：把图形收进**合并候选**；候选满两个就合成一个新点。
        ///
        /// 两种"取消"都支持：① 再点一次已选为候选的那个图形 → 取消它的候选资格；
        /// ② 点工具栏「合并」退出 → 候选清单清空，画面立刻还原，选点数据一点没动。
        /// </summary>
        private void HandleMergeClick(SelectedCircle clicked)
        {
            if (clicked == null) return;

            // 再点同一个 = 取消候选（沿用"点一下选中、再点一下取消"的手感）
            if (_mergeCandidates.Remove(clicked))
            {
                RefreshKWindow();
                UpdateSelectionStatusText($"合并模式：已取消 1 个候选，当前 {_mergeCandidates.Count}/2");
                return;
            }

            _mergeCandidates.Add(clicked);

            if (_mergeCandidates.Count < 2)
            {
                RefreshKWindow();
                UpdateSelectionStatusText($"合并模式：已选 {_mergeCandidates.Count}/2，请再点一个图形");
                return;
            }

            SelectedCircle first = _mergeCandidates[0];
            SelectedCircle second = _mergeCandidates[1];
            _mergeCandidates.Clear();

            SelectedCircle merged = CreateMergedShape(first, second);
            if (merged == null)
            {
                UpdateSelectionStatusText("合并失败：图形已失效");
                return;
            }

            if (!IsSingleShapeInShapes(merged, _selectedCircles))
            {
                _selectedCircles.Add(merged);
            }

            // 新点不在任何 Gerber 光圈上 —— 不进图形缓存就是"幽灵点"（画得出来、存得下去、点不中）。
            AppendOrphanSelections();

            KLog.Info($"合并：({first.X:F4},{first.Y:F4})[{NormalizeHeader(first.HeaderType)}] + " +
                      $"({second.X:F4},{second.Y:F4})[{NormalizeHeader(second.HeaderType)}] → " +
                      $"({merged.X:F4},{merged.Y:F4})[{merged.HeaderType}]，图层 {merged.Layer}");

            RefreshKWindow();
            UpdateSelectionStatusText(
                $"合并完成 → 新点 ({merged.X:F4}, {merged.Y:F4})，归 {NormalizeHeader(merged.HeaderType)}（原点保留）");
        }

        /// <summary>
        /// 由两点生成合并点：位置取**中点**，形状 / 尺寸 / 光圈 ID / 图层归属**沿用第一个**。
        ///
        /// 为什么沿用第一个的全部特征，而不是取两者的平均或并集：
        /// 合并的用途是"两个靠得很近的点其实只要一个插针位"，插的还是同一种焊盘尺寸 ——
        /// 尺寸取平均会让新点跟任何 Gerber 图形都对不上（画出来比原焊盘大一圈，很难看）。
        /// 两个点形状不同时（一个圆一个矩形），沿用第一个同样是最可解释的选择。
        /// </summary>
        private SelectedCircle CreateMergedShape(SelectedCircle first, SelectedCircle second)
        {
            if (first == null || second == null) return null;

            double cx = (first.X + second.X) / 2.0;
            double cy = (first.Y + second.Y) / 2.0;

            SelectedCircle merged;
            switch (first.Shape)
            {
                case ApertureShape.Rectangle:
                    merged = new SelectedCircle(cx, cy, first.Width, first.Height);
                    break;

                case ApertureShape.Oval:
                    merged = new SelectedCircle(cx, cy, first.Width, first.Height, first.Rotation);
                    break;

                default:   // 圆
                    merged = new SelectedCircle(cx, cy, first.Diameter);
                    break;
            }

            merged.ID = first.ID;
            merged.Layer = first.Layer ?? string.Empty;
            merged.LayerId = first.LayerId;
            merged.HeaderType = _currentHeaderType;
            merged.IsSelected = true;
            return merged;
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

                // ---------- 当前模式：点击语义 + 插针头类型 ----------
                //
                // 放在**最前面**：这两项决定"接下来点下去会发生什么"，是**上下文无关**的全局状态；
                // 后面的项都是"针对这一个图形 / 这一片区域"的动作。模式在前、动作在后，层级才清楚。
                // 尤其 h1/h2/h3 必须一眼看见 —— 当前归错了头，事后要一个个点回来。
                var singleItem = new ToolStripMenuItem("单选") { Checked = _isSingleClickMode, CheckOnClick = false };
                var multiItem = new ToolStripMenuItem("多选") { Checked = !_isSingleClickMode, CheckOnClick = false };
                singleItem.Click += (s, e) =>
                {
                    SetClickMode(true);
                    // 菜单此刻还没关，就地同步勾选 —— 否则要关掉再右键一次才看得出变化
                    singleItem.Checked = true;
                    multiItem.Checked = false;
                };
                multiItem.Click += (s, e) =>
                {
                    SetClickMode(false);
                    singleItem.Checked = false;
                    multiItem.Checked = true;
                };
                _canvasMenu.Items.Add(singleItem);
                _canvasMenu.Items.Add(multiItem);

                _canvasMenu.Items.Add(new ToolStripSeparator());

                // 插针头类型：三个互斥项，勾选状态 = 当前归哪个头
                var headerItems = new ToolStripMenuItem[HeaderTypes.Length];
                for (int hi = 0; hi < HeaderTypes.Length; hi++)
                {
                    string headerName = HeaderTypes[hi];
                    var headerItem = new ToolStripMenuItem("切换到 " + headerName)
                    {
                        Checked = (_currentHeaderType == headerName),
                        CheckOnClick = false
                    };
                    headerItem.Click += (s, e) =>
                    {
                        SetCurrentHeader(headerName);
                        for (int k = 0; k < headerItems.Length; k++)
                        {
                            if (headerItems[k] != null) headerItems[k].Checked = (HeaderTypes[k] == headerName);
                        }
                    };
                    headerItems[hi] = headerItem;
                    _canvasMenu.Items.Add(headerItem);
                }

                _canvasMenu.Items.Add(new ToolStripSeparator());

                // ---------- 针对光标下那个图形的操作 ----------
                double actualX, actualY;
                ReverseMirrorTransform(x, y, out actualX, out actualY);
                double tolerance = Math.Max(kWindowControl1.Viewport.ToWorldLength(4), 1e-9);
                // 与左键选点同一口径：图层叠放优先 + 隐藏图层的图形不参与命中
                SelectedCircle hit = ShapeQuery.HitTest(_allShapes, actualX, actualY, tolerance, DepthForHitTest);

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
        /// 插针头类型表：顺序 = 工具栏按钮顺序 = 菜单顺序 = 状态栏顺序。
        /// 三个值只写在这一处，避免"工具栏加了 h4、绘制 / 保存那边忘了同步"。
        /// </summary>
        private static readonly string[] HeaderTypes = { "h1", "h2", "h3" };

        /// <summary>
        /// 把任意来源的头类型折成 h1 / h2 / h3 之一。
        ///
        /// 空值、null、"H2"（大小写不一）、以及以后手改过 json 塞进来的陌生值，**一律落到 h1** ——
        /// 容错方向必须与"默认 h1"一致。若这里改成返回空串，一个拼错的字段就会让那个点的
        /// 配色查表失败（画不出色），用户看到的是"点凭空消失"。
        /// </summary>
        private static string NormalizeHeader(string header)
        {
            if (string.IsNullOrEmpty(header)) return "h1";

            string h = header.Trim().ToLowerInvariant();
            for (int i = 0; i < HeaderTypes.Length; i++)
            {
                if (HeaderTypes[i] == h) return h;
            }
            return "h1";
        }

        /// <summary>
        /// 该图形按哪个颜色画（画布背景是纯黑，色名走控件库 / Halcon 语义）。
        ///
        /// 三个头**各有一个颜色设置**，都能在「参数设置」里改（与 V5 一致）：
        ///   · h1 → `_selectColor`，即设置项 `SelectedCircleColor`（参数界面上写着「h1 颜色」，默认白）。
        ///     为什么 h1 不用别的名字：这个设置项历史上就叫"选中颜色"，改名会让老用户已经设过的值失效。
        ///   · h2 → `_headerH2Color`（`HeaderH2Color`，默认黄）
        ///   · h3 → `_headerH3Color`（`HeaderH3Color`，默认绿）
        ///
        /// 为什么把 h1 默认保持白色、而不是 V5 的红色：老工程与老 `circles.json` 里所有点都是 h1
        /// （字段缺失即默认 h1），照抄 V5 会**一打开满屏白点变红点** —— 那是"升了个版本观感全变"，
        /// 而用户没要求改配色。默认值的语义在这里必须与"什么都不改"等价。
        ///
        /// 空值兜底：设置里读到空串时退回内置默认色，避免老配置文件（没有这两个键）让点画不出色。
        /// </summary>
        private string HeaderColorOf(SelectedCircle shape)
        {
            switch (NormalizeHeader(shape == null ? null : shape.HeaderType))
            {
                case "h2": return string.IsNullOrEmpty(_headerH2Color) ? "yellow" : _headerH2Color;
                case "h3": return string.IsNullOrEmpty(_headerH3Color) ? "green" : _headerH3Color;
                default:   return _selectColor;      // h1
            }
        }

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

            // ③ 合并候选高亮（只有合并模式下、且已点过至少一个时才画）
            DrawMergeCandidates(ctx, view);

            // ④ 框选限定框（最上层）：语义已从"我刚才框过的区域"变成"当前批量操作被限定在这一片"，
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

            // 合并候选高亮（合并模式下才有；画在选点之上、限定框之下）
            DrawMergeCandidates(ctx, view);

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

            // 颜色按头类型**逐点**取（h1/h2/h3 三色），但只在真正变化时才调 SetColor：
            // 控件库的 SetColor 内部要做色名解析，每帧对全部模板点各调一次纯属浪费；
            // 实际数据里同类点通常是成片连续的，一次遍历下来的切换次数远小于点数。
            string currentColor = null;

            for (int i = 0; i < _selectedCircles.Count; i++)
            {
                SelectedCircle shape = _selectedCircles[i];

                // 同 DrawSelectedCircles：图层隐藏时，它上面的模板点一并隐藏
                // （底图那一路已经在 DrawGerberApertures 里过滤过了，两处必须成对）
                if (!IsShapeLayerVisible(shape)) continue;

                double cx, cy;
                ApplyMirrorTransform(shape.X, shape.Y, out cx, out cy);

                if (!IsVisibleAt(shape, cx, cy, view)) continue;

                string color = HeaderColorOf(shape);
                if (color != currentColor)
                {
                    ctx.SetColor(color);
                    currentColor = color;
                }

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

            // 颜色按头类型逐点取（h1/h2/h3），只在变化时才调 SetColor —— 理由同 DrawAllTemplateCircles
            string currentColor = null;

            // 原地遍历 + IsSelected 判断，不再先 Where(...).ToList() 分配一份新列表
            for (int i = 0; i < _selectedCircles.Count; i++)
            {
                SelectedCircle shape = _selectedCircles[i];
                if (!shape.IsSelected) continue;

                // 图层被隐藏 → 它上面的选点跟着一起藏起来。
                // 选点是**独立于底图**画的第二遍（底图在 DrawGerberApertures 里已按显隐过滤），
                // 漏了这一句就会出现"底图没了、选点还悬在原地"——用户看不见底图，却能看到一堆孤立的点，
                // 点它还照样能选中（命中测试的 DepthForHitTest 会拒绝，见那里）。两处必须成对。
                if (!IsShapeLayerVisible(shape)) continue;

                double cx, cy;
                ApplyMirrorTransform(shape.X, shape.Y, out cx, out cy);

                if (!IsVisibleAt(shape, cx, cy, view)) continue;

                string color = HeaderColorOf(shape);
                if (color != currentColor)
                {
                    ctx.SetColor(color);
                    currentColor = color;
                }

                DispShape(ctx, shape, cx, cy);
            }
        }

        /// <summary>
        /// 合并候选的高亮（合并模式下才画）。
        ///
        /// 为什么要在选点之后**再画一遍轮廓**，而不是"把候选点改成某个醒目颜色"：
        /// 候选是**临时的、与选中状态无关**的一份清单（见 _mergeCandidates 的注释），
        /// 改色就得动图形本身的 HeaderType —— 那等于"看一眼候选就把用户的头类型分配改了"。
        /// 描一圈轮廓只影响这一帧的观感，退出合并模式立刻不留痕迹。
        /// </summary>
        private void DrawMergeCandidates(KWindow ctx, RectangleF view)
        {
            if (!_isMergeMode || _mergeCandidates.Count == 0) return;

            ctx.SetDraw("margin");
            ctx.SetColor("gold");

            for (int i = 0; i < _mergeCandidates.Count; i++)
            {
                SelectedCircle shape = _mergeCandidates[i];
                if (shape == null || !IsShapeLayerVisible(shape)) continue;

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

                // 整层一个颜色（不是整层里每个光圈一个颜色）；色号取自 LayerInfo.ColorIndex，
                // 与"当前第几层"解耦 —— 置顶重排不会让颜色跟着跳。
                ctx.SetColor(GetLayerColor(layer));

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
        /// 图层颜色：按图层的**配色序号**取色（<see cref="LayerInfo.ColorIndex"/>，载入时定下，此后不变）。
        ///
        /// 刻意**不按"在 _layers 里的当前序号"**取色：序号会被「置顶」改变 ——
        /// 那样用户一点置顶，那层的颜色就跳到另一个颜色，看着像又冒出一个 bug。
        ///
        /// 色表 ApertureColors 已按"对纯黑背景对比度 ≥ 4.5:1"筛过（见其定义处的实测数据），
        /// 共 17 项 —— 超过 17 个图层才会开始循环撞色。
        /// </summary>
        private string GetLayerColor(LayerInfo layer)
        {
            int index = (layer != null && layer.ColorIndex >= 0) ? layer.ColorIndex : 0;
            return ApertureColors[index % ApertureColors.Length];
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


        /// <summary>
        /// 视图 → 参数设置。**只管配色** —— 保存落点不再由这里决定。
        ///
        /// 原来这里还会把参数窗体里的"模板文件保存路径"回写进 _currentTemplatePath，后果很具体：
        /// 那个设置项是个**全局默认值**，用户进来看一眼颜色、顺手点「确定」，工程归属就被它顶掉了
        /// —— 之后点「保存」写去的是那个默认目录，当前工程里什么都没有，而状态栏照样报"已保存"。
        /// 该设置项已随本轮一并移除，落点只由"当前工程目录 / 用户当场选择"决定，
        /// 任何"顺带改个设置"的动作都不该动它。
        /// </summary>
        private void btnParamSetting_Click(object sender, EventArgs e)
        {
            ParamsForm paramsForm = new ParamsForm();

            // 传递当前设置（默认工作路径 + h1 / h2 / h3 三行颜色）
            paramsForm.SetDefaultRootPath(GetDefaultProjectRoot());   // 显示解析后的有效值（人看得懂）
            paramsForm.SetSelectColor(_selectColor);
            paramsForm.SetHeaderColors(_headerH2Color, _headerH3Color);
            if (paramsForm.ShowDialog() == DialogResult.OK)
            {
                // 从界面获取颜色设置
                _selectColor = paramsForm.GetSelectColor();
                _headerH2Color = paramsForm.GetHeaderH2Color();
                _headerH3Color = paramsForm.GetHeaderH3Color();

                Properties.Settings.Default.SelectedCircleColor = _selectColor;
                Properties.Settings.Default.HeaderH2Color = _headerH2Color;
                Properties.Settings.Default.HeaderH3Color = _headerH3Color;

                // 「默认工作路径」**只存进设置**，绝不去碰 _currentTemplatePath。
                // 🔴 这条边界正是历史上那个缺陷的分水岭：那个路径设置项当年越界去顶替了工程归属，
                //    于是"打开工程 A → 改个颜色 → 点确定 → 再保存"会把内容写去别处（见
                //    MD文件汇总（AI）/问题&解决方案/参数设置确定按钮改写工程归属.md）。
                //    它现在的唯一作用是**各类目录对话框的初值**。
                Properties.Settings.Default.DefaultProjectRootPath = paramsForm.GetDefaultRootPath();

                Properties.Settings.Default.Save(); // 保存到配置文件

                // 配色刚改过 → 立刻按新颜色重绘。少了这一句，要等下一次选点 / 缩放 / 平移
                // 才会看到颜色变化，用户会以为"改了没生效"。
                RefreshKWindow();
            }
        }

        // ───────── 菜单事件 ─────────
        // 菜单项大多直接复用原有的按钮 Click 处理方法（保存 / 另存到 / 翻转 / 重置视图 / 参数设置），
        // 这里只补那些原来没有对应"按钮方法"的入口。
        // 统一用 (object, EventArgs) 签名，为的是在 Designer.cs 里能用标准 EventHandler 绑定 ——
        // 用 lambda 虽然更短，但设计器重新生成 Designer.cs 时会被丢掉。

        /// <summary>
        /// 文件 → 新建工程。顺序是**先问工程名 → 再选文件 → 载入完自动落盘**（用户 2026-09-21 指定）。
        ///
        /// 为什么名字必须问在最前面：新建完成后工程**已经在磁盘上了**，不再需要用户回到主界面
        /// 再点一次「保存」—— 既然载入完就要存，名字就必须在载入之前定下来。
        /// 落点优先取「默认工作路径 \ 工程名」，不可用时才当场问（见 AutoSaveNewProject）。
        ///
        /// 选文件用 OpenFileDialog 的多选而不是"选目录"，是刻意的：一个工程目录里常常混着丝印、
        /// 说明、尺寸表等非图层文件，让用户自己挑出要的图层比"扫描整个目录再替他猜"更可控。
        /// 挑错的会在解析器的严格模式下判失败、被跳过并汇总提示。
        /// </summary>
        private void OnNewProjectClick(object sender, EventArgs e)
        {
            // ① 先定工程名
            string projectName;
            using (ProjectNameDialog nameDlg = new ProjectNameDialog(
                "新建工程 —— 第 1 步：工程名", "下一步", string.Empty,
                Properties.Settings.Default.DefaultProjectRootPath))
            {
                if (nameDlg.ShowDialog(this) != DialogResult.OK) return;
                projectName = nameDlg.ProjectName;
            }

            // ② 再选 Gerber 文件（可多选）
            string[] picked;
            using (OpenFileDialog dialog = new OpenFileDialog())
            {
                dialog.Title = $"新建工程「{projectName}」—— 第 2 步：选择 Gerber 图层文件（可多选）";
                dialog.InitialDirectory = GetGerberBrowseStartPath();
                // 刻意不设扩展名白名单：GBL / GBS / G1 / .o 这类太杂，白名单只会把人挡在外面
                dialog.Filter = "所有文件 (*.*)|*.*";
                dialog.Multiselect = true;

                if (dialog.ShowDialog() != DialogResult.OK) return;
                picked = dialog.FileNames;
            }

            // ③ 载入图层（清空重建；归属留空，紧接着由 AutoSaveNewProject 定下来）
            if (!LoadProjectFromFiles(picked))
            {
                MessageBox.Show(
                    "工程没有创建 —— 所选的 " + picked.Length + " 个文件都没能解析出图形。\r\n\r\n" +
                    "刚才输入的工程名「" + projectName + "」未写入磁盘。",
                    "无法载入", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            RememberLastProject("gerber", picked[0]);

            // ④ 载入完就保存 —— 用户不必再点一次「保存」
            AutoSaveNewProject(projectName);
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
            //
            // 初值优先级（注意：这里要选的是**工程目录本身**，不是"父目录"）：
            //   ① 用户设置的「默认工作路径」= 各工程目录的**共同父目录**。
            //      用户一般把所有工程放在同一个地方，从那儿开始最省事 ——
            //      否则每次都要从"上次那个工程目录"往回退一层再进另一个，白点两下。
            //   ② 上次打开过的工程目录（kind == "template" 时 LastProjectPath 就是它）
            //   ③ 都没有 → 交给系统对话框（默认当前工作目录）
            string startDir = GetDefaultProjectRoot();
            if (string.IsNullOrEmpty(startDir) || !Directory.Exists(startDir))
            {
                string lastPath = Properties.Settings.Default.LastProjectPath;
                startDir = (!string.IsNullOrEmpty(lastPath) && Directory.Exists(lastPath)) ? lastPath : null;
            }

            string folder = FolderPicker.PickFolder(
                "打开工程 —— 选择工程目录（含 Gerber 图层，可选 circles.json）", startDir);

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

                // 命中口径：**先比图层叠放、再比距离**（理由见 ShapeQuery.HitTest 的注释）——
                // 画在上层的图形优先，上层此处没有图形才穿透到下一层。
                // 隐藏图层的图形由 DepthForHitTest 返回 SkipDepth 跳过：看不见的东西点不中。
                SelectedCircle clicked = ShapeQuery.HitTest(candidates, actualX, actualY, tolerance, DepthForHitTest);

                if (clicked == null)
                {
                    if (_isMergeMode) toolStripStatusLabel1.Text = "合并模式：此处没有图形，请点在图形上";
                    return;
                }

                // 诊断：把"这次为什么是它"写进日志（定位选错层时最有用，排查完可整段删除）
                LogHitDiagnostics(actualX, actualY, tolerance, clicked);

                // 合并模式：点击 = 收「合并候选」，**完全不碰选点语义**（也不解除框选限定）。
                // 放在这里、而不是混进下面的单选 / 多选分支，是为了让两者**互斥**：
                // 合并时必须能精确点到"两个图形"，若还按多选扩散"同类一批"，永远凑不出两个点。
                if (_isMergeMode)
                {
                    HandleMergeClick(clicked);
                    return;
                }

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

            // 选中时写入**当前**头类型。取消选中**不回改**类型 ——
            // "取消"只是"这次不要它"，不是"把它改成别的头"；下次再选中会按那时的当前类型重新赋值。
            if (clickedShape.IsSelected)
            {
                clickedShape.HeaderType = _currentHeaderType;
            }

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
        /// "同类" = **同一图层内、光圈 ID 相同**。ID 只在单个 Gerber 文件内有意义 ——
        /// GBL 的 D10 与 GTS 的 D10 是两个完全不同的焊盘，跨图层按 ID 扩散会误伤别的层。
        ///
        /// 除此之外，搜索范围还受**框选限定**约束：
        ///   · 框还在（_actualSelectedRect 非空）→ 只在框内扩散同类；
        ///   · 框不在 → 在该图层内全板扩散。
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
            //
            // 第一道过滤永远是"图层可见"：同 ID 的同类图形可能散布在多个图层上，不过滤就会把
            // **隐藏图层**上的同类点一起选中 —— 用户看不见那些点，却发现选点数量对不上。
            // 第二道才是框选限定（两道是**叠加**关系，所以下面用 searchSource.Where 继续收窄）。
            IEnumerable<SelectedCircle> searchSource = allShapes.Where(IsShapeLayerVisible);
            bool narrowed = false;

            if (!_actualSelectedRect.IsEmpty)
            {
                RectangleF scopeRect = _actualSelectedRect;
                searchSource = searchSource.Where(c => ShapeQuery.IsVisible(c, scopeRect));
                narrowed = true;
            }

            // 获取所有同类型的形状（圆形比较直径，矩形比较宽度和高度），
            // **并且限定在与被点图形同一个图层内** —— 光圈 ID 只在单个 Gerber 文件内有意义，
            // 不限定图层就会把别的层上"碰巧同名"的图形一起改掉。
            // LayerId 相同即同层（未知归属的图形 LayerId = 0，它们之间仍然互相匹配）。
            var sameIdShapes = searchSource
                .Where(c => c.ID == clickedShape.ID && c.LayerId == clickedShape.LayerId)
                .ToList();

            // 判断当前点击的形状是否已被选中
            bool isCurrentlySelected = clickedShape.IsSelected;

            if (!isCurrentlySelected)
            {
                // 如果当前形状未选中，则选中所有同类形状
                foreach (var shape in sameIdShapes)
                {
                    shape.IsSelected = true;
                    shape.HeaderType = _currentHeaderType;   // 扩散出来的点按当前头类型归类（与单选同口径）
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
        /// <summary>
        /// 判断这个图形是否已经在集合里（**按值比较，不是按引用**）。
        ///
        /// 比较维度：坐标 + 形状 + **图层** + 尺寸。每一维都不能少：
        ///   · **图层**（2026-09-21 补）—— 跨图层同坐标同尺寸的图形是两个不同的点。少了这一维，
        ///     后一个会被判成"已经选过"而加不进 _selectedCircles，表现为"选了却保存不进去"；
        ///   · **尺寸** —— 同层同坐标的焊盘与它的外形框（直径不同）是两个点。
        /// 口径必须与 <see cref="ShapeKey"/> 一致，否则会出现"单选选得上、框选选不上"这类自相矛盾的行为。
        /// </summary>
        public bool IsSingleShapeInShapes(SelectedCircle currentShape, List<SelectedCircle> shapes)
        {
            foreach (var shape in shapes)
            {
                if (shape.X == currentShape.X &&
                    shape.Y == currentShape.Y &&
                    shape.Shape == currentShape.Shape &&
                    shape.LayerId == currentShape.LayerId)
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
        /// 图形的"尺寸三元组"：把各形状自己的尺寸字段统一成 (sizeX, sizeY, sizeR)，
        /// 好跟其他维度一起进唯一键。口径与绘制端 / 保存端一致：
        ///   圆   → (直径, 直径, 0)      矩形 → (宽, 高, 0)      椭圆 → (宽, 高, 旋转角)
        /// </summary>
        private static void GetSize(ApertureShape shape, double diameter, double width, double height, double rotation,
                                    out double sizeX, out double sizeY, out double sizeR)
        {
            switch (shape)
            {
                case ApertureShape.Rectangle:
                    sizeX = width; sizeY = height; sizeR = 0;
                    break;

                case ApertureShape.Oval:
                    sizeX = width; sizeY = height; sizeR = rotation;
                    break;

                default:   // 圆
                    sizeX = diameter; sizeY = diameter; sizeR = 0;
                    break;
            }
        }

        /// <summary>
        /// 图形的**唯一键**：图层 + 坐标 + 形状 + **尺寸**。四个维度缺一不可：
        ///
        ///   · **图层** —— GBL 与 GBS 常在同一坐标放尺寸不同的图形，少了它会把两层认成一个；
        ///   · **坐标** —— 统一 Math.Round(…, 4)，与保存端同一精度，比对即精确相等（不需要容差）；
        ///   · **形状** —— 同坐标的圆与矩形必须分得开；
        ///   · **尺寸**（2026-09-21 补上，原先只有前三项）—— 同一图层、同一坐标、同一形状、
        ///     **直径不同**的两个圆（真实 Gerber 里很常见：焊盘与它的外形框都落在中心点上）
        ///     原来会算出**同一个键**。后果不是"少画一个"，而是**错位认领**：
        ///     建索引时键互相覆盖 → 遍历光圈时同一个选点对象被反复复用 → 缓存里同一对象出现多次、
        ///     另一个图形则整个消失。症状是"那个点怎么都点不中"，
        ///     日志里的指纹是**复用数 > 已选点数**（例如"已选 2 个，复用选点对象 8 个"）。
        ///
        /// 键的使用点（原来就强调过"必须一致"）：
        ///   ① BuildAllShapes 的 exactIndex（建索引 + 查索引）
        ///   ② ApplyRegionSelection 的 selectedKeys（建集合 + 查集合）
        /// 统一走本方法，避免以后再加维度时又漏掉一处。
        /// </summary>
        private static ValueTuple<string, double, double, int, double, double, double> ShapeKey(
            string layer, double x, double y, ApertureShape shape, double sizeX, double sizeY, double sizeR)
        {
            return ValueTuple.Create(layer ?? string.Empty,
                                     Math.Round(x, 4), Math.Round(y, 4), (int)shape,
                                     Math.Round(sizeX, 4), Math.Round(sizeY, 4), Math.Round(sizeR, 2));
        }

        /// <summary>图形（图形缓存 / 选点对象）的键。</summary>
        private static ValueTuple<string, double, double, int, double, double, double> ShapeKey(SelectedCircle c)
        {
            double sx, sy, sr;
            GetSize(c.Shape, c.Diameter, c.Width, c.Height, c.Rotation, out sx, out sy, out sr);
            return ShapeKey(c.Layer, c.X, c.Y, c.Shape, sx, sy, sr);
        }

        /// <summary>光圈上某个位置（坐标）的键。</summary>
        private static ValueTuple<string, double, double, int, double, double, double> ShapeKey(Aperture a, double x, double y)
        {
            double sx, sy, sr;
            GetSize(a.Shape, a.Diameter, a.Width, a.Height, a.Rotation, out sx, out sy, out sr);
            return ShapeKey(a.Layer, x, y, a.Shape, sx, sy, sr);
        }

        /// <summary>
        /// 图形的**退化键**：只用坐标 + 形状 + 尺寸，**不带图层**。
        ///
        /// 存在的唯一理由：circles.json 在 v2.2 以前**没有存 Layer 字段**，从那种文件恢复出来的
        /// 选点，Layer 全是空串，与 Aperture.Layer（图层文件名）永远对不上。只靠 ShapeKey 的后果
        /// 是"选点对象与图形缓存脱节"——画得出来、点一下就重复、再点取消不掉。
        /// 用本键把老数据兜住，命中时顺手把真实图层回填进选点（见 BuildAllShapes）。
        ///
        /// 坐标取 Math.Round(…, 4)：保存端就是这么写的（SaveTemplateToPath 里的 Math.Round），
        /// 两边做同一次舍入，比对即精确相等，不需要引入容差。
        /// </summary>
        private static ValueTuple<double, double, int, double, double, double> ShapeKeyLoose(
            double x, double y, ApertureShape shape, double sizeX, double sizeY, double sizeR)
        {
            return ValueTuple.Create(Math.Round(x, 4), Math.Round(y, 4), (int)shape,
                                     Math.Round(sizeX, 4), Math.Round(sizeY, 4), Math.Round(sizeR, 2));
        }

        /// <summary>图形（图形缓存 / 选点对象）的退化键。</summary>
        private static ValueTuple<double, double, int, double, double, double> ShapeKeyLoose(SelectedCircle c)
        {
            double sx, sy, sr;
            GetSize(c.Shape, c.Diameter, c.Width, c.Height, c.Rotation, out sx, out sy, out sr);
            return ShapeKeyLoose(c.X, c.Y, c.Shape, sx, sy, sr);
        }

        /// <summary>光圈上某个位置（坐标）的退化键。</summary>
        private static ValueTuple<double, double, int, double, double, double> ShapeKeyLoose(Aperture a, double x, double y)
        {
            double sx, sy, sr;
            GetSize(a.Shape, a.Diameter, a.Width, a.Height, a.Rotation, out sx, out sy, out sr);
            return ShapeKeyLoose(x, y, a.Shape, sx, sy, sr);
        }

        private void BuildAllShapes()
        {
            var shapes = new List<SelectedCircle>();

            // 图形缓存重建 = 数据源换了（打开另一个工程 / 重新解析 Gerber）。
            // 合并候选持有的是**上一批**图形对象的引用，留着会被 DrawMergeCandidates 画成
            // "幽灵高亮"—— 用户在画面上看到一圈金色轮廓，却已不属于当前工程。
            _mergeCandidates.Clear();

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
            var exactIndex = new Dictionary<ValueTuple<string, double, double, int, double, double, double>, SelectedCircle>();
            var looseIndex = new Dictionary<ValueTuple<double, double, int, double, double, double>, SelectedCircle>();

            for (int i = 0; i < _selectedCircles.Count; i++)
            {
                SelectedCircle c = _selectedCircles[i];
                if (c == null) continue;

                exactIndex[ShapeKey(c)] = c;
                looseIndex[ShapeKeyLoose(c)] = c;
            }

            // 已经复用过的选点对象。**这一层兜底不能省**：万一两个图形算出的键依然相同
            // （尺寸也分不开的极端情况），没有它就会出现"同一个选点对象被塞进 _allShapes 好几次，
            // 而真正该在里面的那个图形整个消失"，且日志里只能看出"复用数 > 已选数"这一条线索。
            // 有了它，每个选点对象最多被复用一次，其余走"新建图形"，缓存里永远是**一对一**。
            var usedSelected = new HashSet<SelectedCircle>();

            // 光圈 → 所属图层：**用对象引用建立映射**，不做任何字符串匹配。
            //
            // 这张表用来给每个图形写 LayerId（图层身份）；之后的"叠放深度 / 是否隐藏"由
            // RefreshLayerState 按 Id 查出来拷进图形。
            // 为什么不用 aperture.Layer（文件名）去 _layers 里找同名项：那种匹配只要有一处对不上
            // 就会**静默失败** —— 所有图形都变成"未知图层"，命中优先级退回纯距离排序，
            // 表现就是"怎么点都只选到最大的那个圆"，而日志里看不出任何异常。
            // _layers[li].Apertures 里的 Aperture 对象就是这一层的，引用相等，骗不了人。
            var ownerOf = new Dictionary<Aperture, LayerInfo>();
            for (int li = 0; li < _layers.Count; li++)
            {
                LayerInfo ly = _layers[li];
                if (ly == null || ly.Apertures == null) continue;

                for (int k = 0; k < ly.Apertures.Count; k++)
                {
                    Aperture ap = ly.Apertures[k];
                    if (ap != null) ownerOf[ap] = ly;
                }
            }

            int reused = 0;      // 复用了已有选点对象的图形数
            int backfilled = 0;  // 顺手补上图层身份的选点数
            int unowned = 0;     // 找不到所属图层的光圈数（正常应为 0）

            for (int ai = 0; ai < _apertures.Count; ai++)
            {
                Aperture aperture = _apertures[ai];

                LayerInfo owner;
                bool owned = ownerOf.TryGetValue(aperture, out owner);
                string layer = owned ? owner.FileName : (aperture.Layer ?? string.Empty);
                if (!owned) unowned++;

                for (int pi = 0; pi < aperture.Position.Count; pi++)
                {
                    var position = aperture.Position[pi];

                    SelectedCircle existing;
                    bool hit = exactIndex.TryGetValue(
                        ShapeKey(aperture, position.Item1, position.Item2), out existing);

                    if (!hit)
                    {
                        hit = looseIndex.TryGetValue(
                            ShapeKeyLoose(aperture, position.Item1, position.Item2), out existing);

                        if (hit && existing != null && string.IsNullOrEmpty(existing.Layer))
                        {
                            existing.Layer = layer;   // 回填图层身份（老 JSON 里没有这个字段）
                            backfilled++;
                        }
                    }

                    if (hit && existing != null && usedSelected.Add(existing))
                    {
                        // 已选状态与集合成员保持一致：_selectedCircles 里就是"已选"的定义，
                        // 这里显式置真，免得出现"在集合里但 IsSelected 为假"的脱节。
                        existing.IsSelected = true;
                        existing.LayerId = owned ? owner.Id : 0;   // 图层归属一并接上（json 里不存这个字段）
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
                        newShape.LayerId = owned ? owner.Id : 0;
                        shapes.Add(newShape);
                    }
                }
            }

            _allShapes = shapes;

            // ③ 收编"不属于任何 Gerber 图形的选点"（合并生成的点、坐标对不上的老选点）——
            //    必须在 _allShapes 赋值之后调用：它读的就是 _allShapes。
            AppendOrphanSelections();

            // 形状建好，立刻把图层状态（叠放深度 + 隐藏）拷进每个图形 ——
            // 不能等 RefreshLayerPanel：命中测试随时可能发生，状态必须先就位。
            RefreshLayerState();

            // 自检：LayerId 为 0 = 这个图形没接上图层身份 → RefreshLayerState 只能把它的 LayerDepth
            // 设成 -1，命中排序的"叠放优先"对它整体失效（表现：置顶了也选不到那一层）。
            // 正常应为 0。非 0 就说明图层 Id 分配晚了（见 EnsureLayerIdentity 注释）或光圈归属失败。
            int noLayerId = 0;
            for (int si = 0; si < shapes.Count; si++)
            {
                if (shapes[si] != null && shapes[si].LayerId <= 0) noLayerId++;
            }

            KLog.Info($"图形缓存构建完成：{shapes.Count} 个图形（其中已选 {_selectedCircles.Count} 个，" +
                      $"复用选点对象 {reused} 个，回填图层 {backfilled} 个）；" +
                      $"图层 {_layers.Count} 个，归属失败 {unowned} 个光圈" +
                      (unowned > 0 ? " ← ⚠ 非 0 表示有光圈不属于任何图层，它们的命中优先级会退化" : "") +
                      (noLayerId > 0
                          ? $" ← ⚠ 有 {noLayerId} 个图形没有图层身份（叠放优先级对它们失效，置顶将不起作用）"
                          : "") +
                      (reused > _selectedCircles.Count
                          ? $" ← ⚠ 复用数({reused})超过已选点数({_selectedCircles.Count})，有图形被重复认领"
                          : ""));

            LogLayerOrder();   // 诊断：把叠放顺序也记一笔，排查"哪层压在上面"最直接
        }

        /// <summary>
        /// 把"**不属于任何 Gerber 图形**的选点"收进图形缓存（_allShapes），让它们能被点中、能被取消。
        ///
        /// 哪些点会走到这里：
        ///   · **合并生成的点** —— 坐标是两点的中点，天然不落在任何光圈位置上；
        ///   · 老 circles.json 里与当前 Gerber 对不上的选点（换过底图、或手工编辑过 json）。
        ///
        /// 【为什么必须收】
        /// 图形缓存是**命中测试、框选、多选扩散的唯一依据**，而选点**绘制**读的是 _selectedCircles。
        /// 一个点只要不在缓存里，就会出现"画得出来、保存得下去，却**怎么点都没反应**" ——
        /// 想取消它只剩"清空全部选点"这一条路。V5 里的合并点正是这个状态，这里顺手补上。
        /// （本工程对"点了没反应"零容忍，见 KWindowControl_KMouseDown 里删掉选点模式门禁那段注释。）
        ///
        /// 【代价与边界】
        ///   · 收进来的点带 `LayerId`（沿用生成它的那个图形），于是它的 LayerDepth / LayerHidden
        ///     与**同层图形完全一致** —— 该层被隐藏时它一起隐藏，不会赖在画面上；
        ///   · 它的 ID 也沿用第一个图形，所以"多选同类扩散"会把它和同层同名光圈视为同类（合理）；
        ///   · 复杂度 O(已选数)，由 HashSet 判重；每次载入工程 / 每次合并各跑一次。
        /// </summary>
        private void AppendOrphanSelections()
        {
            if (_allShapes == null || _selectedCircles.Count == 0) return;

            var inShapes = new HashSet<SelectedCircle>(_allShapes);
            int added = 0;

            for (int i = 0; i < _selectedCircles.Count; i++)
            {
                SelectedCircle c = _selectedCircles[i];
                if (c == null) continue;
                if (!inShapes.Add(c)) continue;      // 复用到位（或重复调用本方法）时直接跳过

                _allShapes.Add(c);
                added++;
            }

            if (added > 0)
            {
                // 新收进来的点也要拿到图层状态，否则它们的命中优先级停在初始值（-1 = 最底层）
                RefreshLayerState();
                KLog.Info($"图形缓存补充：{added} 个选点不属于任何 Gerber 图形（合并点 / 坐标对不上的点），" +
                          $"已纳入可点范围；缓存合计 {_allShapes.Count} 个");
            }
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
                "选点归类（工具栏 h1 / h2 / h3）：\n" +
                "    当前选中哪个头，接下来选中的点就归哪个头；三种头在画面上用不同颜色画。\n" +
                "    已经选好的点不会被切换动作改掉 —— 要改就在目标 h 下重新点它，或用框选重新框进来。\n" +
                "    老工程（没有 h 字段的 circles.json）里的点全部归 h1。\n" +
                "    右键菜单里也有「单选 / 多选」与「切换到 h1 / h2 / h3」，不必回工具栏。\n\n" +
                "合并（工具栏「合并」）：\n" +
                "    进入后依次点两个图形 → 取两者中点生成一个新点，形状与尺寸沿用第一个；\n" +
                "    原来那两个点保留。再点一次已选为候选的图形可取消它；再点「合并」退出。\n\n" +
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
                ? $"点击方式：{(_isSingleClickMode ? "单选" : "多选")} · 当前 {_currentHeaderType}"
                  + (_isMergeMode ? " · 合并模式" : "")
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
        ///
        /// 2026-09-21 增补：本方法**同时过滤掉隐藏图层上的图形**（IsShapeLayerVisible）。
        /// 它有两个调用方 —— ApplyRegionSelection（真正选中）与右键菜单（显示"范围内 N 个点"），
        /// 过滤必须放在这里而不是调用方，两处的口径才不会分叉。
        /// </summary>
        private List<SelectedCircle> GetCirclesInSelectedRegion(List<SelectedCircle> allCircles, RectangleF rect)
        {
            var result = new List<SelectedCircle>();
            if (allCircles == null) return result;

            for (int i = 0; i < allCircles.Count; i++)
            {
                SelectedCircle s = allCircles[i];

                // 隐藏图层的图形不参与框选：用户看不见的东西不该被选中。
                // 少了这一句，框一次就会把隐藏图层上的图形也收进选点，而画面上毫无痕迹 ——
                // 等用户重新勾上那层才发现多出一堆点。
                if (s != null && IsShapeLayerVisible(s) && ShapeQuery.IsVisible(s, rect)) result.Add(s);
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

            // 已选索引：同一把键（含图层与尺寸），保证"已在 _selectedCircles 里"能被 O(1) 判出
            var selectedKeys = new HashSet<ValueTuple<string, double, double, int, double, double, double>>();
            for (int i = 0; i < _selectedCircles.Count; i++)
            {
                SelectedCircle c = _selectedCircles[i];
                selectedKeys.Add(ShapeKey(c));
            }

            var snapshot = new List<SelectionSnapshot>();
            var added = new List<SelectedCircle>();

            for (int i = 0; i < inRegion.Count; i++)
            {
                SelectedCircle s = inRegion[i];
                if (s.IsSelected) continue;   // 本来就是选中状态：既不重复加，也不进撤销快照

                var key = ShapeKey(s);
                if (selectedKeys.Add(key))
                {
                    added.Add(s);
                }
                // 键已存在（Gerber 里同位置同形状的重复绘制）时不再入列表，
                // 但仍要把 IsSelected 置真 —— 否则"视觉已选"与"列表成员"会脱节。

                // 头类型也一起进快照：框选会把新选中的点归到"当前头类型"，撤销时必须一并回滚 ——
                // 否则 Ctrl+Z 之后点的**数量**回到从前、**归属/颜色**却留在新值上，
                // 用户看到的是"撤销了一半"，而且没有任何提示能解释这件事。
                snapshot.Add(new SelectionSnapshot
                {
                    Shape = s,
                    WasSelected = false,
                    WasHeaderType = s.HeaderType
                });
                s.IsSelected = true;
                s.HeaderType = _currentHeaderType;
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
                s.HeaderType = snap.WasHeaderType;   // 与选中状态一起回滚（框选写入过当前头类型）
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
        /// 重算图层状态，并**刷进每一个图形对象**：
        ///   <c>SelectedCircle.LayerDepth</c>  = 该图形所属图层在 _layers 里的序号（越大越靠上层）
        ///   <c>SelectedCircle.LayerHidden</c> = 该图层当前是否被隐藏
        ///
        /// 【为什么把状态拷进图形，而不是"用 shape.Layer 去图层表里现查"】
        /// 现查要求两个字符串逐字符相等。只要有一处对不上（老数据、命名差异、以后改了命名规则），
        /// 命中优先级与隐藏过滤就会**静默失效** —— 代码看着改了，实际退回"纯按距离排序"，
        /// 表现就是"不论怎么点，还是只能选中最大的那个圆"，而且没有任何报错可查。
        /// 归属关系是在 BuildAllShapes 里用**光圈对象引用**建立的，全链路不做字符串匹配。
        ///
        /// 调用点（3 处）：载入 / 切换工程（RefreshLayerPanel 的早退之前）、勾选显隐、图层置顶。
        /// 复杂度 O(图形数 + 图层数)，1.5 万图形约 1 ms。
        /// </summary>
        /// <summary>
        /// 分配图层 Id、重建 Id → LayerInfo 表；顺带把"图层 → 叠放深度"填进 <paramref name="depthOf"/>。
        ///
        /// 🔴 **必须在 BuildAllShapes 之前调用**，这是本方法独立存在的唯一理由。
        /// BuildAllShapes 要把 `owner.Id` 写进每个图形当身份（`LayerId`）；此时 Id 若还没分配
        /// （初值 0），写进去就是 0。紧接着 RefreshLayerState 按 `LayerId > 0` 查归属会**全部**
        /// 落进 else 分支，把每个图形的 `LayerDepth` 都设成 -1 —— **全体深度相同**，
        /// 于是命中排序里"图层叠放优先"那一档**静默失效**：
        /// 置顶哪个图层都没用，命中退回按距离 / 尺寸挑，表现就是"置顶了还是选到别的图层"。
        ///
        /// 这个坑只在"载入后没有第二次 BuildAllShapes"的路径上暴露：
        ///   · 打开**存过选点**的工程 → 恢复选点时会再跑一次 BuildAllShapes，那时 Id 已分配，
        ///     于是侥幸正常（所以它藏了很久）；
        ///   · 新建工程（无选点可恢复）→ 必然中招。
        /// </summary>
        private void EnsureLayerIdentity(Dictionary<LayerInfo, int> depthOf)
        {
            _layerById.Clear();
            if (depthOf != null) depthOf.Clear();

            for (int i = 0; i < _layers.Count; i++)
            {
                LayerInfo layer = _layers[i];
                if (layer == null) continue;

                if (layer.Id <= 0) layer.Id = _nextLayerId++;
                if (layer.ColorIndex < 0) layer.ColorIndex = i;

                _layerById[layer.Id] = layer;
                if (depthOf != null) depthOf[layer] = i;   // i 越大 = 越后画 = 越靠上层
            }
        }

        private void RefreshLayerState()
        {
            // ① 分配 Id（只在首次见到时分配，此后永不变）、建 Id → LayerInfo 表、记录每层的深度
            var depthOf = new Dictionary<LayerInfo, int>(_layers.Count);
            EnsureLayerIdentity(depthOf);

            // ② 把状态拷进图形
            for (int i = 0; i < _allShapes.Count; i++)
            {
                SelectedCircle s = _allShapes[i];
                if (s == null) continue;

                LayerInfo owner;
                int depth;
                if (s.LayerId > 0 && _layerById.TryGetValue(s.LayerId, out owner)
                    && depthOf.TryGetValue(owner, out depth))
                {
                    s.LayerDepth = depth;
                    s.LayerHidden = !owner.IsVisible;
                }
                else
                {
                    s.LayerDepth = -1;      // 未知归属：当作最底层，但**仍可见、仍可被点中**
                    s.LayerHidden = false;
                }
            }
        }

        /// <summary>
        /// 该图形所属图层当前是否可见。
        /// 未知归属（LayerId = 0）按**可见**处理：宁可多画一个，也不能把用户已经选好的点藏起来。
        /// </summary>
        private bool IsShapeLayerVisible(SelectedCircle shape)
        {
            return shape != null && !shape.LayerHidden;
        }

        /// <summary>
        /// 命中测试用的层序委托：**隐藏图层的图形直接跳过**（用户看不见的东西不该被点中），
        /// 其余返回它所属图层的叠放深度。
        /// 左键选点与右键菜单共用这一个委托，保证两处口径永远一致。
        /// </summary>
        private int DepthForHitTest(SelectedCircle shape)
        {
            if (shape == null || shape.LayerHidden) return ShapeQuery.SkipDepth;
            return shape.LayerDepth;
        }

        /// <summary>
        /// 把某个图层移到**最上层**：_layers 的末位 = 最后绘制 = 压在最上面；
        /// 又因为容器是**倒序**显示的，它会同时排到列表**首行**。
        ///
        /// 为什么置顶直接改 _layers 的顺序、而不是记一个"置顶标记"：
        /// 绘制顺序、命中优先级（LayerDepth）、保存进 circles.json 的图层清单，三者都按 _layers 顺序走，
        /// 只记标记就要在这三处各判一次。**顺序即真相**最省事。
        /// 颜色不会跟着跳 —— 配色用的是 LayerInfo.ColorIndex（载入时定下，不随序号变）。
        /// </summary>
        private void MoveLayerToTop(int layerIndex)
        {
            if (layerIndex < 0 || layerIndex >= _layers.Count) return;
            if (layerIndex == _layers.Count - 1) return;        // 已经在最上层，无需动作

            LayerInfo layer = _layers[layerIndex];
            _layers.RemoveAt(layerIndex);
            _layers.Add(layer);

            KLog.Info($"图层置顶：{layer.FileName}（序号 {layerIndex} → {_layers.Count - 1}，现在压在最上层）");
            LogLayerOrder();

            // RefreshLayerPanel 内部第一件事就是 RefreshLayerState()，这里仍显式再调一次：
            // 状态是命中优先级的依据，不该依赖"容器是否已创建"那条分支（方法幂等，代价 O(图形数)）。
            RefreshLayerState();
            RefreshLayerPanel();
            RefreshKWindow();

            toolStripStatusLabel1.Text = $"图层已置顶：{layer.FileName}（压在最上层，并排在列表首行）";
        }

        /// <summary>容器里某一行点了「置顶」。</summary>
        private void OnLayerRowTopRequested(int layerIndex)
        {
            MoveLayerToTop(layerIndex);
        }

        /// <summary>
        /// 把当前图层叠放顺序写进日志（诊断用）：**靠后的层画在上面，命中时优先**。
        /// 日志里按"从最上层到最下层"列出，与画面上看到的顺序一致。
        /// 排查完可整段删除，不影响任何功能。
        /// </summary>
        private void LogLayerOrder()
        {
            var sb = new StringBuilder();
            for (int i = _layers.Count - 1; i >= 0; i--)
            {
                LayerInfo layer = _layers[i];
                if (layer == null) continue;

                if (sb.Length > 0) sb.Append(" > ");
                sb.Append(layer.FileName).Append("#").Append(i);
                if (i == _layers.Count - 1) sb.Append("(最上)");
                if (!layer.IsVisible) sb.Append("(隐)");
            }
            KLog.Info($"图层叠放顺序（从最上层到最下层）：{sb}");
        }

        /// <summary>
        /// 选点命中诊断（**只写日志，不参与任何判定**）。
        ///
        /// 回答的问题是"这次为什么选中了它"：列出点击位置附近的全部图形及其图层深度，
        /// 按深度**降序**排列 —— 按当前规则，命中者应当是列表里的第一个。
        ///   · 命中者不是第一个  → 命中优先级有问题；
        ///   · 所有深度都一样    → 图层归属没接上（BuildAllShapes 的"归属失败"计数也应非 0）；
        ///   · 深度符合预期但用户想选别层 → 那是层序问题，用「置顶」调整。
        /// 排查完可整段删除，不影响功能。
        /// </summary>
        private void LogHitDiagnostics(double x, double y, double tolerance, SelectedCircle chosen)
        {
            var rect = new RectangleF((float)(x - tolerance), (float)(y - tolerance),
                                      (float)(tolerance * 2), (float)(tolerance * 2));

            var nearby = new List<SelectedCircle>();
            for (int i = 0; i < _allShapes.Count; i++)
            {
                SelectedCircle s = _allShapes[i];
                if (s == null || !ShapeQuery.IsVisible(s, rect)) continue;

                nearby.Add(s);
                if (nearby.Count >= 16) break;     // 够诊断就行，别让日志爆炸
            }

            nearby.Sort((a, b) => b.LayerDepth.CompareTo(a.LayerDepth));   // 靠上层的排前面

            var sb = new StringBuilder();
            for (int i = 0; i < nearby.Count; i++)
            {
                SelectedCircle s = nearby[i];
                if (i > 0) sb.Append(" | ");
                sb.Append(s.Layer).Append("#").Append(s.LayerDepth);
                if (s.LayerHidden) sb.Append("(隐)");
            }

            KLog.Info($"选点诊断：点({x:F3},{y:F3})，容差 {tolerance:F4}，附近 {nearby.Count} 个图形" +
                      $"（按深度降序）[{sb}]；→ 命中 {chosen.Layer}#{chosen.LayerDepth}" +
                      $"（图层总数 {_layers.Count}{(_layers.Count == 1 ? " ⚠ 只有一个图层" : "")}）");
        }

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
            // 🔴 先把图层 Id 分好 —— BuildAllShapes 要用它当图形的图层身份（LayerId），
            //    晚这一步就会把 0 写进所有图形，命中排序的"叠放优先"随之静默失效。
            //    详见 EnsureLayerIdentity 的注释。
            EnsureLayerIdentity(null);

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

            for (int i = 0; i < filePaths.Count; i++)
            {
                string path = filePaths[i];
                string displayName = Path.GetFileName(path);

                LayerInfo parsed;
                string reason;
                if (TryParseLayerFile(path, out parsed, out reason))
                {
                    layers.Add(parsed);
                    KLog.Info($"新建工程：载入图层 {displayName}（{reason}）");
                }
                else
                {
                    skipped.Add(displayName);
                    KLog.Info($"新建工程：跳过 {displayName} —— {reason}");
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

        // ───────── 拖放载入（拖文件进窗口 = 追加图层 / 无工程时新建） ─────────

        /// <summary>
        /// 拖入窗口：只接受"文件拖放"。其余（拖一段文字、拖一个 URL）一律拒绝 ——
        /// 不接受的表现是光标变禁止符，比"接受了却什么都不做"清楚得多。
        /// </summary>
        private void OnFileDragEnter(object sender, DragEventArgs e)
        {
            e.Effect = (e.Data != null && e.Data.GetDataPresent(DataFormats.FileDrop))
                ? DragDropEffects.Copy
                : DragDropEffects.None;
        }

        /// <summary>
        /// 拖放落下：按"当前有没有图层"分派。
        ///   没有图层 → 等价于「文件 → 新建工程」（清空重建；归属留空，保存时再问落点）
        ///   已有图层 → **追加图层**（不清状态、不动归属、不重置视图，见 AppendLayersFromFiles）
        ///
        /// 拖进来的可能是文件夹，这里只认文件 —— 免得把"拖一个目录"误解成"打开工程"
        /// （那是「打开工程」入口的语义，两者不该混）。
        /// </summary>
        private void OnFileDragDrop(object sender, DragEventArgs e)
        {
            if (e.Data == null || !e.Data.GetDataPresent(DataFormats.FileDrop)) return;

            string[] dropped = e.Data.GetData(DataFormats.FileDrop) as string[];
            if (dropped == null || dropped.Length == 0) return;

            var files = new List<string>();
            int dirCount = 0;
            for (int i = 0; i < dropped.Length; i++)
            {
                if (Directory.Exists(dropped[i])) dirCount++;
                else if (File.Exists(dropped[i])) files.Add(dropped[i]);
            }

            if (files.Count == 0)
            {
                MessageBox.Show(
                    dirCount > 0
                        ? "拖入的是文件夹。\r\n\r\n要打开一个工程，请用「文件 → 打开工程」选择工程目录。"
                        : "拖入的内容里没有文件。",
                    "无法载入", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            if (_layers.Count == 0)
            {
                // 没有工程 → 与「文件 → 新建工程」同一套：载入完就落盘，不必再点保存。
                //
                // 顺序与菜单入口略有不同：文件已经拖进来了，所以**先载入、成功了再问名字** ——
                // 免得用户输完一个名字，才发现拖进来的全是解析不了的文件。
                if (!LoadProjectFromFiles(files)) return;

                RememberLastProject("gerber", files[0]);

                string suggested = GuessProjectNameFromFile(files[0]);
                using (ProjectNameDialog nameDlg = new ProjectNameDialog(
                    "新建工程 —— 工程名", "创建", suggested,
                    Properties.Settings.Default.DefaultProjectRootPath))
                {
                    if (nameDlg.ShowDialog(this) != DialogResult.OK)
                    {
                        // 取消 = 放弃这次新建。把刚载入的图层一并撤掉 ——
                        // 免得界面上留下一个"没有名字、也没有归属"的半成品，
                        // 用户之后再点「保存」还要被问一次落点，更绕。
                        ResetProjectState();
                        toolStripStatusLabel1.Text = "已取消新建工程";
                        KLog.Info("拖放新建：用户在工程名对话框取消，已撤掉刚载入的图层");
                        return;
                    }

                    AutoSaveNewProject(nameDlg.ProjectName);
                }
            }
            else
            {
                AppendLayersFromFiles(files);
            }
        }

        /// <summary>
        /// 解析一个 Gerber / 钻孔文件成图层。解析失败、或解析出来一个图形都没有 → 返回 false，
        /// 并把原因写进 <paramref name="reason"/>。
        ///
        /// 抽出来是因为「新建工程」与「拖入追加图层」两条路都要它 ——
        /// 复制一份的话，以后改解析口径（严格模式、跳过条件）必然漂移成两套行为。
        /// </summary>
        private static bool TryParseLayerFile(string path, out LayerInfo layer, out string reason)
        {
            layer = null;
            reason = string.Empty;

            string displayName = Path.GetFileName(path);
            try
            {
                GerberParser parser = new GerberParser();
                parser.LayerName = displayName;   // GetApertureList 会把它回填到每个 Aperture.Layer
                ParseResult result = parser.ParseFile(path, true);
                List<Aperture> apertures = parser.GetApertureList();

                if (!result.Success || apertures == null || apertures.Count == 0)
                {
                    reason = result.Message;
                    return false;
                }

                int positionCount = 0;
                for (int k = 0; k < apertures.Count; k++) positionCount += apertures[k].Position.Count;
                if (positionCount == 0)
                {
                    reason = "解析后没有任何图形";
                    return false;
                }

                layer = new LayerInfo
                {
                    FilePath = path,
                    FileName = displayName,
                    IsVisible = true,
                    Apertures = apertures
                };
                reason = $"{apertures.Count} 个光圈 / {positionCount} 个图形";
                return true;
            }
            catch (Exception ex)
            {
                reason = ex.Message;
                return false;
            }
        }

        /// <summary>
        /// 把一批文件**追加**为当前工程的新图层（拖放进来时走这里）。
        ///
        /// 与 <see cref="LoadProjectFromFiles"/> 的区别，条条都是红线：
        ///   · **不调 ResetProjectState()** —— 那会把图层、选点、工程归属一起清掉，
        ///     于是"拖一层进来"直接变成"工程没了"；
        ///   · **不动 _currentTemplatePath / _isTemplateMode** —— 当前工程的归属必须保住，
        ///     否则之后点「保存」会去问落点，而用户以为还在编辑原来那个工程；
        ///   · **不调 ResetViewToCenter()** —— 用户正盯着某处看，追加一层不该把视图甩走
        ///     （包围盒变了，想看全按快捷键就行）；
        ///   · 新图层追加到 _layers **末尾** —— 末位 = 最后绘制 = 压在最上层，
        ///     与"图层顺序 = 加载顺序"这条既有语义一致；
        ///   · 已选点不受影响 —— BuildAllShapes 幂等，且会复用 _selectedCircles 里的对象实例。
        ///
        /// 与已有图层**同名**的文件会先问用户：替换（并清掉该层选点）还是跳过。
        /// </summary>
        private bool AppendLayersFromFiles(IList<string> filePaths)
        {
            if (filePaths == null || filePaths.Count == 0) return false;

            int added = 0;
            int skipped = 0;
            var notes = new List<string>();

            for (int i = 0; i < filePaths.Count; i++)
            {
                string path = filePaths[i];
                string displayName = Path.GetFileName(path);

                LayerInfo parsed;
                string reason;
                if (!TryParseLayerFile(path, out parsed, out reason))
                {
                    skipped++;
                    notes.Add($"{displayName}：{reason}");
                    KLog.Info($"拖入图层：跳过 {displayName} —— {reason}");
                    continue;
                }

                int existIndex = FindLayerIndexByName(displayName);
                if (existIndex >= 0)
                {
                    int selCount = CountSelectionsOnLayer(_layers[existIndex].Id);

                    string msg = $"图层「{displayName}」已经在工程里了。\r\n\r\n" +
                        (selCount > 0
                            ? $"替换后该图层上的 {selCount} 个选点会被清除。\r\n\r\n"
                            : string.Empty) +
                        "要用拖入的这份替换它吗？\r\n（选「否」= 跳过这个文件）";

                    DialogResult r = MessageBox.Show(msg, "图层已存在",
                        MessageBoxButtons.YesNo, MessageBoxIcon.Question, MessageBoxDefaultButton.Button2);
                    if (r != DialogResult.Yes)
                    {
                        skipped++;
                        notes.Add($"{displayName}：与已有图层同名，已跳过");
                        KLog.Info($"拖入图层：{displayName} 与已有图层同名，用户选择跳过");
                        continue;
                    }

                    ReplaceLayer(existIndex, parsed);
                    added++;
                    KLog.Info($"拖入图层：替换 {displayName}（{reason}）");
                    continue;
                }

                parsed.ColorIndex = -1;   // -1 = 未分配，交给容器刷新时按当前序号现分配
                _layers.Add(parsed);
                added++;
                KLog.Info($"拖入图层：新增 {displayName}（{reason}）");
            }

            if (added == 0)
            {
                toolStripStatusLabel1.Text = skipped > 0
                    ? $"拖入的 {skipped} 个文件都没能加进来"
                    : "没有可添加的图层";
                ShowLayerNotes(notes, "没有图层被添加");   // 全失败也要说清原因，否则用户只看到"没反应"
                return false;
            }

            // 收尾：重算派生数据 + 刷新界面。**刻意不调 ResetViewToCenter()** —— 见方法注释。
            EnsureLayerIdentity(null);   // 同理：新图层的 Id 要先分配，BuildAllShapes 才认得出归属
            RebuildAperturesFromLayers();
            BuildAllShapes();
            UpdateContentBounds();
            RefreshLayerPanel();
            UpdateScaleLabel();
            UpdateSelectionCountLabel();
            RefreshKWindow();

            toolStripStatusLabel1.Text = $"已添加 {added} 个图层" +
                (skipped > 0 ? $"，跳过 {skipped} 个" : string.Empty);

            if (notes.Count > 0) ShowLayerNotes(notes, $"已添加 {added} 个图层，另有 {skipped} 个未添加");
            return true;
        }

        private int FindLayerIndexByName(string fileName)
        {
            for (int i = 0; i < _layers.Count; i++)
            {
                if (string.Equals(_layers[i].FileName, fileName, StringComparison.OrdinalIgnoreCase))
                    return i;
            }
            return -1;
        }

        private int CountSelectionsOnLayer(int layerId)
        {
            int n = 0;
            for (int i = 0; i < _selectedCircles.Count; i++)
            {
                if (_selectedCircles[i].LayerId == layerId) n++;
            }
            return n;
        }

        /// <summary>
        /// 用新解析出来的图层**替换** _layers[index]，但保留它原有的 Id / ColorIndex / IsVisible
        /// 与它在列表中的**位置** —— 位置决定叠放次序，不能因为"换个文件"就跳到最上层；
        /// 配色和显隐也要沿用，不能因为换个文件就变色 / 变可见。
        ///
        /// 🔴 必须**清掉该层上的选点**：替换后图形对象整批换新，旧选点指向的是已经不存在的图形，
        ///    留着只会变成"点不中的幽灵点"（_allShapes 里没有它们，_selectedCircles 里却还在）。
        ///    同时要把**框选撤销栈**里的相关快照剔掉 —— 那些快照持有旧对象的引用，
        ///    撤销时会把它们**加回 _selectedCircles**，幽灵点就此复活。
        /// </summary>
        private void ReplaceLayer(int index, LayerInfo parsed)
        {
            LayerInfo old = _layers[index];

            parsed.Id = old.Id;                 // 身份（会话内唯一）沿用
            parsed.ColorIndex = old.ColorIndex; // 配色沿用
            parsed.IsVisible = old.IsVisible;   // 显隐沿用 —— 用户可能特意藏了这一层

            // ① 清掉该层选点。两份状态要同步：IsSelected 与 _selectedCircles
            //    （只 Remove 不改 IsSelected，会留下"看着没选中、其实还选着"的脏状态）
            for (int i = 0; i < _selectedCircles.Count; i++)
            {
                if (_selectedCircles[i].LayerId == old.Id) _selectedCircles[i].IsSelected = false;
            }
            _selectedCircles.RemoveAll(s => s.LayerId == old.Id);

            // ② 剔除撤销栈里引用旧图形的快照，否则撤销会让它们复活
            for (int i = 0; i < _regionUndoStack.Count; i++)
            {
                _regionUndoStack[i].RemoveAll(snap => snap.Shape != null && snap.Shape.LayerId == old.Id);
            }

            _layers[index] = parsed;
        }

        /// <summary>把"跳过 / 失败"的原因列出来给用户看（最多列 15 条，其余折叠计数）。</summary>
        private static void ShowLayerNotes(List<string> notes, string title)
        {
            if (notes == null || notes.Count == 0) return;

            int show = Math.Min(notes.Count, 15);
            string body = string.Join("\r\n", notes.Take(show).ToArray());
            if (notes.Count > show) body += $"\r\n...（共 {notes.Count} 条）";

            MessageBox.Show(body, title, MessageBoxButtons.OK, MessageBoxIcon.Information);
        }

        /// <summary>
        /// 两个路径是否指向同一个文件（规范化成全路径后比较）。
        /// 保存图层时用它区分"就地保存"与"需要复制" —— 见 SaveTemplateToPath 里的说明。
        /// </summary>
        private static bool IsSamePath(string a, string b)
        {
            if (string.IsNullOrEmpty(a) || string.IsNullOrEmpty(b)) return false;
            try
            {
                return string.Equals(Path.GetFullPath(a), Path.GetFullPath(b),
                    StringComparison.OrdinalIgnoreCase);
            }
            catch
            {
                return string.Equals(a, b, StringComparison.OrdinalIgnoreCase);
            }
        }

        /// <summary>
        /// 新建工程载入完成后的**自动保存** —— 用户不必再回主界面点一次「保存」。
        ///
        /// 落点优先取「默认工作路径 \ 工程名」：用户把各工程都放在同一个根目录下，
        /// 新建时就该直接放进去，不必再问一遍。
        /// 只有两种情况退回到"当场问用户"：
        ///   · 没设默认工作路径，或它已经不存在了；
        ///   · 那个目录**已经存在**（多半是重名 —— 覆盖与否必须由用户决定，不能替他做主）。
        ///
        /// 🔴 这与「首次保存」是完全同一套落点语义（有归属就地写回 / 无归属当场问），
        ///    区别只是它发生在"图层载入完成"之后、不需要用户先点一次保存。
        /// </summary>
        /// <returns>true = 工程已落盘；false = 用户取消（工程只存在于内存里）。</returns>
        private bool AutoSaveNewProject(string projectName)
        {
            string root = GetDefaultProjectRoot();
            string targetFolder = null;
            string folderName = projectName;

            if (!string.IsNullOrEmpty(root) && Directory.Exists(root) &&
                !Directory.Exists(Path.Combine(root, projectName)))
            {
                targetFolder = Path.Combine(root, projectName);
                KLog.Info($"新建工程：按默认工作路径自动落盘 {targetFolder}");
            }
            else
            {
                // 没设默认路径 / 该目录已存在 / 路径失效 → 当场问一次
                using (SaveProjectDialog dlg = new SaveProjectDialog(
                    "保存新工程", "保存", projectName, GetProjectParentBrowseDir()))
                {
                    if (dlg.ShowDialog(this) != DialogResult.OK)
                    {
                        toolStripStatusLabel1.Text =
                            $"已载入 {_layers.Count} 个图层，但工程尚未保存到磁盘（点「保存」可再存一次）";
                        KLog.Info("新建工程：用户取消了保存，工程尚未落盘");
                        return false;
                    }

                    folderName = dlg.ProjectName;
                    targetFolder = Path.Combine(dlg.TargetParentDir, folderName);
                }
            }

            SaveTemplateToPathWithStateUpdate(targetFolder, folderName);
            return !string.IsNullOrEmpty(_currentTemplatePath);
        }

        /// <summary>
        /// 从 Gerber 文件路径推一个默认工程名 —— 取它**所在目录**的名字，而不是文件名
        /// （文件名带扩展名，`1516601-00-C_01.GBL` 当工程名不合适）。
        /// 这只是**默认值**，用户可以改。
        /// </summary>
        private static string GuessProjectNameFromFile(string filePath)
        {
            if (string.IsNullOrEmpty(filePath)) return string.Empty;
            string dir = Path.GetDirectoryName(filePath);
            if (string.IsNullOrEmpty(dir)) return string.Empty;
            return Path.GetFileName(dir.TrimEnd(
                Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
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
        /// 按 circles.json 里记录的图层清单，恢复各层的**显示 / 隐藏**与**图层顺序**。
        ///
        /// 只有 v2.1 及以后的文件才有 TemplateInfo.Layers；旧文件（v2.0）没有这一段，
        /// 此时保持"全部可见 + 载入顺序" —— 不能让老工程因为缺个字段就打开成一片空白。
        ///
        /// 【为什么顺序也要恢复】清单数组就是按保存时的 _layers 顺序写的，顺序天然被持久化。
        /// 用户「置顶」过的层序必须下次打开原样还原，否则每次开工程都要重新置顶一遍。
        ///
        /// 读取走 dynamic，**字段缺失会抛 RuntimeBinderException**（不是给默认值），
        /// 所以整段用 try 包住；ColorIndex 是 v2.3 才有的字段，单独再包一层。
        /// </summary>
        private void ApplyLayerVisibility(dynamic jsonData)
        {
            try
            {
                // ⚠ 一律用 JToken 的**索引器**取字段，不要用 dynamic 点属性：
                //    索引器取不到的字段返回 null（好判断），而 dynamic 访问不存在的属性会**抛异常**。
                //    原来这里用 try/catch 兜 v2.3 才有的 "ColorIndex"，代价是打开老工程时
                //    **每个图层抛一次 RuntimeBinderException** —— 异常构造 + DLR 绑定失败都很贵，
                //    实测 6 个图层吃掉 **150 ms**（日志 09:32:09.530 → 09:32:09.680），
                //    而这正好压在"打开工程"的关键路径上。
                JToken root = jsonData as JToken;
                if (root == null) return;

                JToken tinfo = root["TemplateInfo"];
                JArray layers = tinfo == null ? null : tinfo["Layers"] as JArray;
                if (layers == null) return;

                // 文件名 → LayerInfo（只是把 json 里的名字映射回内存对象，映射结果不参与任何判定）
                var byName = new Dictionary<string, LayerInfo>(StringComparer.OrdinalIgnoreCase);
                for (int i = 0; i < _layers.Count; i++)
                {
                    LayerInfo ly = _layers[i];
                    if (ly != null && !string.IsNullOrEmpty(ly.FileName)) byName[ly.FileName] = ly;
                }

                var reordered = new List<LayerInfo>(_layers.Count);
                var taken = new HashSet<LayerInfo>();
                int applied = 0;

                foreach (JToken item in layers)
                {
                    string fileName = (string)item["FileName"];
                    if (string.IsNullOrEmpty(fileName)) continue;

                    LayerInfo layer;
                    if (!byName.TryGetValue(fileName, out layer)) continue;   // 清单里有、目录里没有 → 跳过
                    if (!taken.Add(layer)) continue;                          // 清单里的重复项

                    JToken vis = item["IsVisible"];
                    layer.IsVisible = (vis == null || vis.Type != JTokenType.Boolean) || vis.Value<bool>();

                    // v2.3 起才写 ColorIndex；老文件取到 null 就保持 -1，稍后按当前顺序现分配。
                    JToken ci = item["ColorIndex"];
                    layer.ColorIndex = (ci != null && ci.Type == JTokenType.Integer) ? ci.Value<int>() : -1;

                    reordered.Add(layer);
                    applied++;
                }

                // 清单里没提到的图层（工程目录里新加的文件）：保持相对顺序追加到后面，默认可见
                for (int i = 0; i < _layers.Count; i++)
                {
                    LayerInfo ly = _layers[i];
                    if (ly == null || taken.Contains(ly)) continue;
                    ly.IsVisible = true;
                    reordered.Add(ly);
                }

                if (reordered.Count == _layers.Count) _layers = reordered;

                KLog.Info($"打开工程：已恢复 {applied} 个图层的勾选状态与图层顺序");
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
                    // 还没有归属（刚从 Gerber 模式新建、从没保存过）→ **当场问用户两件事**：
                    // 工程叫什么名字、放到哪个上级目录。问完归属就定了，之后再点「保存」
                    // 一律就地写回，与「打开工程」完全同一条路。
                    //
                    // 为什么两件事要一起问：工程名过去是**推**出来的 —— GetProjectName() 在
                    // 无归属时退化成"第一个 Gerber 文件**所在目录**的名字"，而这里原本只弹一个
                    // 目录选择器、工程名由程序拼上去，用户没有任何输入点。素材目录叫
                    // 1516601-00-C，工程就只能叫 1516601-00-C。现在把它从"唯一答案"降级成"默认值"。
                    //
                    // 仍然守住交接文档那条规则：**没有归属就等于"还没决定这个工程放哪"**，
                    // 所以问一次；问完就定，不存在任何"全局默认落点"替用户做主。
                    using (SaveProjectDialog dlg = new SaveProjectDialog(
                        "保存新工程", "保存", folderName, GetProjectParentBrowseDir()))
                    {
                        if (dlg.ShowDialog(this) != DialogResult.OK)
                        {
                            toolStripStatusLabel1.Text = "已取消保存（工程尚未保存到磁盘）";
                            KLog.Info("新工程首次保存：用户取消了保存对话框");
                            return;
                        }

                        folderName = dlg.ProjectName;   // 用户可能改过名字
                        targetFolder = Path.Combine(dlg.TargetParentDir, folderName);
                    }

                    KLog.Info($"新工程保存到用户选择的目录: {targetFolder}");
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

            // 与「打开工程」同一个现代文件夹选择器（系统原生 IFileOpenDialog），
            // 让整个程序里的"选目录"体验保持一致 —— 不用老式 FolderBrowserDialog。
            // 另存时工程名**可改** —— 这正是"给工程改名"的正规入口：另存到同一父目录 + 改个名。
            using (SaveProjectDialog dlg = new SaveProjectDialog(
                "另存工程", "另存到", folderName, GetProjectParentBrowseDir()))
            {
                if (dlg.ShowDialog(this) != DialogResult.OK) return;

                folderName = dlg.ProjectName;
                string targetFolder = Path.Combine(dlg.TargetParentDir, folderName);

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

        // ── 保存前预检：同坐标重复点（2026-09-21 用户裁定：这种情况不被允许）──

        /// <summary>冲突明细里最多显示的**处数**；超出部分只进日志（几百处弹窗也装不下）。</summary>
        private const int DuplicateDetailMaxGroups = 50;

        /// <summary>
        /// 把选点按**落盘坐标**分组，挑出"同一个坐标上不止一个点"的那些组。
        ///
        /// 判重口径**只有坐标一项**（用户 2026-09-21 裁定）：不分图层、形状、尺寸、插针头 ——
        /// 插针机按坐标定位，同一坐标就是同一个落点，多条记录 = 重复插针。
        ///
        /// ⚠ 这与图形的四维唯一键 <see cref="ShapeKey"/> 是**两件事**，别混：
        ///   · `ShapeKey` = 图层 + 坐标 + 形状 + 尺寸，管的是"选点对象 ↔ Gerber 图形"的一一对应，
        ///     少任何一维都会静默错位认领（见 `MD文件汇总（AI）/问题&解决方案/重复坐标的图形互相顶替.md`），
        ///     那一套**不能动**；
        ///   · 本方法是"交付给插针机的落点集合"的去重口径，只认坐标。
        ///
        /// 坐标取 Math.Round(…, 4)：与保存端写出的精度是**同一次舍入**，
        /// 于是比对即精确相等，不需要引入容差。
        ///
        /// ⚠ 负零（`-0.0`）**不需要**额外归一 —— 这一层想过，被实测否掉了：
        /// `Math.Round(-0.00003, 4)` 确实得到 `-0.0`（位模式 0x8000000000000000，与 `+0.0` 不同），
        /// 但它与 `+0.0` 的 `GetHashCode()` **相同**（都是 0）、`Equals` 为 true，
        /// 作为 `Dictionary` 键**可以互相命中**，不会漏判。
        /// 实测环境：.NET Framework 4.8.9345.0（正是本工程的运行目标）。将来若换运行时，再核这条。
        /// </summary>
        private List<List<SelectedCircle>> FindDuplicatePositionGroups()
        {
            var buckets = new Dictionary<ValueTuple<double, double>, List<SelectedCircle>>();

            for (int i = 0; i < _selectedCircles.Count; i++)
            {
                SelectedCircle c = _selectedCircles[i];
                if (c == null) continue;

                var key = ValueTuple.Create(Math.Round(c.X, 4), Math.Round(c.Y, 4));

                List<SelectedCircle> bucket;
                if (!buckets.TryGetValue(key, out bucket))
                {
                    bucket = new List<SelectedCircle>();
                    buckets[key] = bucket;
                }
                bucket.Add(c);
            }

            var groups = new List<List<SelectedCircle>>();
            foreach (KeyValuePair<ValueTuple<double, double>, List<SelectedCircle>> pair in buckets)
            {
                if (pair.Value.Count <= 1) continue;

                // 组内按图层叠放**从上层到下层**排：第一行就是命中时会选到的那个，便于判断留谁。
                // （叠放深度来自 RefreshLayerState 拷进图形的运行期副本，见 SelectedCircle.LayerDepth。）
                pair.Value.Sort((a, b) => b.LayerDepth.CompareTo(a.LayerDepth));
                groups.Add(pair.Value);
            }

            // 按坐标升序 —— 明细顺序每次保存都一致，便于与日志对照
            groups.Sort((a, b) =>
            {
                double ax = Math.Round(a[0].X, 4);
                double bx = Math.Round(b[0].X, 4);
                if (ax != bx) return ax.CompareTo(bx);
                return Math.Round(a[0].Y, 4).CompareTo(Math.Round(b[0].Y, 4));
            });

            return groups;
        }

        /// <summary>明细里单个点的尺寸描述（口径与保存端写出的字段一致）。</summary>
        private static string DescribeShapeSize(SelectedCircle c)
        {
            switch (c.Shape)
            {
                case ApertureShape.Circle: return $"圆 φ{c.Diameter:F4}";
                case ApertureShape.Rectangle: return $"矩形 {c.Width:F4}×{c.Height:F4}";
                case ApertureShape.Oval: return $"椭圆 {c.Width:F4}×{c.Height:F4} 旋转 {c.Rotation:F2}°";
                default: return $"形状 {(int)c.Shape}";
            }
        }

        /// <summary>
        /// 把冲突组拼成明细文本 —— 弹窗与日志**共用同一份**，保证两边看到的一致。
        ///
        /// 每个点带上四项：所在图层、形状尺寸、插针头，另标两种**容易被忽略**的状态：
        ///   · `⚠ 该图层当前已隐藏` —— 隐藏层的点照样会被写进 json（导出端不做显隐过滤），
        ///     用户常常没想到"看不见的那层也在里面"；
        ///   · `⚠ 当前未选中` —— 保存写的是 `_selectedCircles` 全部，不看 `IsSelected`。
        /// </summary>
        private string BuildDuplicateDetailText(List<List<SelectedCircle>> groups)
        {
            var sb = new StringBuilder();

            int shown = Math.Min(groups.Count, DuplicateDetailMaxGroups);
            for (int g = 0; g < shown; g++)
            {
                List<SelectedCircle> group = groups[g];

                sb.Append('(').Append(group[0].X.ToString("F4"))
                  .Append(", ").Append(group[0].Y.ToString("F4"))
                  .Append(")   共 ").Append(group.Count).AppendLine(" 个点");

                for (int i = 0; i < group.Count; i++)
                {
                    SelectedCircle c = group[i];

                    sb.Append("    ").Append(i + 1).Append(") ")
                      .Append(string.IsNullOrEmpty(c.Layer) ? "（无图层：合并点 / 游离点）" : c.Layer)
                      .Append("  |  ").Append(DescribeShapeSize(c))
                      .Append("  |  ").Append(NormalizeHeader(c.HeaderType));

                    if (c.LayerHidden) sb.Append("  |  ⚠ 该图层当前已隐藏");
                    if (!c.IsSelected) sb.Append("  |  ⚠ 当前未选中");

                    sb.AppendLine();
                }

                sb.AppendLine();
            }

            if (groups.Count > shown)
            {
                sb.Append("…… 共 ").Append(groups.Count).Append(" 处，此处只显示前 ")
                  .Append(shown).AppendLine(" 处（完整明细见程序目录 logs 下当日日志）");
            }

            return sb.ToString();
        }

        /// <summary>
        /// 保存前的最后一道闸：同一个落盘坐标上只允许有一个点。
        ///
        /// 检出冲突 → 弹窗列明细 → 用户选「返回修改」就**中止本次保存（一个文件都不写）**。
        /// 选「仍然保存」则放行，并把这件事写进日志（记下谁在何时放过了多少处）。
        ///
        /// 为什么拦在这里而不是选点处：选点入口有五处（单选 / 多选扩散 / 框选 / 合并 / 游离点收编），
        /// 逐个设防必漏；隐藏图层场景在选点端也判不准；而且**只有这里**能覆盖老工程里已存下的历史重复点。
        /// 详见 <see cref="DuplicatePositionDialog"/> 的类注释。
        /// </summary>
        /// <returns>true = 可以继续保存；false = 必须中止</returns>
        private bool ConfirmNoDuplicatePositions()
        {
            List<List<SelectedCircle>> groups = FindDuplicatePositionGroups();
            if (groups.Count == 0) return true;

            int pointCount = 0;
            for (int i = 0; i < groups.Count; i++) pointCount += groups[i].Count;

            string detail = BuildDuplicateDetailText(groups);

            // 日志先写：万一弹窗阶段出意外，线索也不丢
            KLog.Warn($"保存前预检：发现 {groups.Count} 处同坐标重复点（涉及 {pointCount} 个点）");
            KLog.Warn("同坐标重复点明细：" + Environment.NewLine + detail);

            using (DuplicatePositionDialog dlg = new DuplicatePositionDialog(groups.Count, pointCount, detail))
            {
                if (dlg.ShowDialog(this) != DialogResult.Yes)
                {
                    KLog.Warn("保存已中止：用户选择返回修改（未写入任何文件）");
                    MessageBox.Show(
                        "已取消保存，未写入任何文件。\r\n\r\n" +
                        $"共 {groups.Count} 处同坐标重复点（涉及 {pointCount} 个点），完整明细在程序目录 logs 下的当日日志里。\r\n" +
                        "请先在画布上取消多余的选点，再重新保存。",
                        "保存未执行", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    return false;
                }
            }

            KLog.Warn($"用户确认在 {groups.Count} 处同坐标重复点存在的情况下仍然保存");
            return true;
        }

        private Boolean SaveTemplateToPath(string targetFolder, string folderName)
        {
            try
            {
                // 落盘前先过最后一道闸：同一个坐标不允许出现多个点（插针机会重复插针）。
                // 放在"是否覆盖"确认**之前** —— 先把数据问题解决掉，再谈落点。
                // 返回 false 时一个文件都不写（图层拷贝与 json 全部跳过），工程归属也不更新。
                if (!ConfirmNoDuplicatePositions()) return false;

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

                    // 判据是"**是不是同一个文件**"，而不是"目标是否已存在"。
                    //
                    // 后者会漏掉一种真实情形：拖入新文件**替换**了同名图层之后，layer.FilePath
                    // 指向的是别处的那个文件，而工程目录里原有的旧文件还在 —— 只看存在与否就跳过复制，
                    // 于是"替换"在保存后等于没发生过，下次打开工程看到的还是旧图层。
                    if (IsSamePath(targetLayerPath, layer.FilePath))
                    {
                        KLog.Info($"图层文件就地保存，跳过复制: {layer.FileName}");
                        continue;
                    }

                    File.Copy(layer.FilePath, targetLayerPath, true);   // 覆盖：同名不同源时必须更新
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
                        // 图层清单（v2.1 新增，可选）：打开工程时据此重建图层容器、
                        // 恢复各层勾选状态与**图层顺序**（数组顺序 = 保存时的绘制顺序）。
                        // v2.3 起补 ColorIndex：图层顺序可由用户「置顶」改动，配色不能再用"当前序号"推，
                        // 存下来才能保证同一个工程每次打开的颜色一致。
                        Layers = _layers.Select(l => new { l.FileName, l.IsVisible, l.ColorIndex }).ToList(),
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
                        // 2.3：TemplateInfo.Layers 的元素补 ColorIndex（图层顺序可被「置顶」改动，
                        //      配色不能再用"当前序号"反推）。读取端不校验版本号，老文件照样打开。
                        // 2.4：每个图形补 Header（插针头类型 h1/h2/h3）。老文件缺这个字段 → 读出即 h1，
                        //      所以**老工程打开后的颜色与行为跟改动前完全一致**。
                        // 2.5：坐标从 Positions[] 数组里提出来，直接挂在记录上（X / Y）——
                        //      每条记录本来就只放一个坐标，那层数组是纯粹的历史包袱，
                        //      还逼着读端多写一层循环。
                        //      🔴 用户裁定：**读端也不兼容旧格式**（见 ReadJsonPosition），
                        //      打开 v2.0~2.4 的老工程，坐标会全部读成 0（点堆在原点）。
                        Version = "2.5",
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
                            Header = NormalizeHeader(c.HeaderType),   // v2.4：插针头类型 h1/h2/h3
                            SizeX = Math.Round(c.Diameter, 4),
                            SizeY = Math.Round(c.Diameter, 4),
                            X = Math.Round(c.X, 4),     // v2.5：坐标直接挂在记录上
                            Y = Math.Round(c.Y, 4)
                        }).ToList(),

                        Rectangles = rectangles.Select(r => new
                        {
                            Id = r.ID,
                            Layer = r.Layer ?? string.Empty,
                            Header = NormalizeHeader(r.HeaderType),   // v2.4
                            Type = (int)ApertureShape.Rectangle,
                            SizeX = Math.Round(r.Width, 4),
                            SizeY = Math.Round(r.Height, 4),
                            X = Math.Round(r.X, 4),     // v2.5
                            Y = Math.Round(r.Y, 4)
                        }).ToList(),

                        Ovals = ovals.Select(o => new
                        {
                            Id = o.ID,
                            Layer = o.Layer ?? string.Empty,
                            Header = NormalizeHeader(o.HeaderType),   // v2.4
                            Type = (int)ApertureShape.Oval,
                            SizeX = Math.Round(o.Width, 4),
                            SizeY = Math.Round(o.Height, 4),
                            Rotation = Math.Round(o.Rotation, 2),
                            X = Math.Round(o.X, 4),     // v2.5
                            Y = Math.Round(o.Y, 4)
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
                // 起始位置与「另存到 / 打开工程」同一套口径（当前工程的上级目录 → 图层所在目录的上级
                // → 上次位置 → 桌面）。原来指向"参数设置里的模板路径"，该设置项本轮已移除。
                openFileDialog.InitialDirectory = GetProjectParentBrowseDir();
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
                        double cx, cy;
                        ReadJsonPosition((object)circle, out cx, out cy);   // 只认 v2.5 的 X / Y
                        var newCircle = new SelectedCircle(
                            cx,
                            cy,
                            (double)circle.SizeX
                        )
                        {
                            ID = circle.Id?.ToString() ?? "Unknown",
                            Layer = ReadJsonLayer((object)circle),   // v2.2 起才有；老文件读空，靠 BuildAllShapes 回填
                            HeaderType = ReadJsonHeader((object)circle),   // v2.4 起才有；老文件读空 → h1
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
                        double rx, ry;
                        ReadJsonPosition((object)rectangle, out rx, out ry);
                        var newRect = new SelectedCircle(
                            rx,
                            ry,
                            (double)rectangle.SizeX,
                            (double)rectangle.SizeY
                        )
                        {
                            ID = rectangle.Id?.ToString() ?? "Unknown",
                            Layer = ReadJsonLayer((object)rectangle),   // v2.2 起才有；老文件读空，靠 BuildAllShapes 回填
                            HeaderType = ReadJsonHeader((object)rectangle),   // v2.4 起才有；老文件读空 → h1
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
                        double ox, oy;
                        ReadJsonPosition((object)oval, out ox, out oy);
                        var newOval = new SelectedCircle(
                            ox,
                            oy,
                            (double)oval.SizeX,
                            (double)oval.SizeY,
                            (double)(oval.Rotation ?? 0)
                        )
                        {
                            ID = oval.Id?.ToString() ?? "Unknown",
                            Layer = ReadJsonLayer((object)oval),   // v2.2 起才有；老文件读空，靠 BuildAllShapes 回填
                            HeaderType = ReadJsonHeader((object)oval),   // v2.4 起才有；老文件读空 → h1
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

        /// <summary>
        /// 读选点条目上的 Header 字段（v2.4 起才有：插针头类型 h1 / h2 / h3）。
        ///
        /// 必须容错，理由与 <see cref="ReadJsonLayer"/> **完全一样**：读取走 dynamic，
        /// 字段不存在会抛 `RuntimeBinderException`（不是返回 null），而 v2.4 以前的 circles.json
        /// 里没有 Header —— 一旦抛出去，外层 catch 会把整个工程判成"选点文件读取失败"，
        /// 选点全丢，只因为少了一个"缺了就等于 h1"的字段。
        ///
        /// 取值统一过一遍 <see cref="NormalizeHeader"/>：空串 / 陌生值都落到 h1，
        /// 保证"老文件打开后与改动前完全一致"。
        /// </summary>
        private static string ReadJsonHeader(object shapeItem)
        {
            var node = shapeItem as Newtonsoft.Json.Linq.JObject;
            if (node == null) return "h1";

            var token = node["Header"];
            return NormalizeHeader(token == null || token.Type == Newtonsoft.Json.Linq.JTokenType.Null
                ? null
                : token.ToString());
        }

        /// <summary>
        /// 读选点条目上的坐标（**只认 v2.5 格式**：`X` / `Y` 直接挂在记录上）。
        ///
        /// 必须容错，理由与 <see cref="ReadJsonLayer"/> 完全一样：不能因为缺一个坐标字段
        /// 就把整个工程判成"选点文件读取失败"、选点全丢。这里走 JToken 索引器 ——
        /// 取不到返回 null，不抛异常（用 `dynamic` 点属性会抛 `RuntimeBinderException`）。
        ///
        /// 只认数字类型（写端写的就是 JSON number）；拿不到就保持 0，
        /// 后续 BuildAllShapes 按坐标认领会失败、那个点落到原点，但不会连累整个工程。
        ///
        /// 🔴 用户 2026-09-21 裁定：**不兼容 v2.0~2.4 的 `Positions[]` 旧格式**（旧格式已废弃）。
        /// 打开那种老工程时坐标会全部读成 0（所有点堆在原点）—— 这是**有意为之**，不是 bug。
        /// </summary>
        private static void ReadJsonPosition(object shapeItem, out double x, out double y)
        {
            x = 0.0;
            y = 0.0;

            var node = shapeItem as Newtonsoft.Json.Linq.JObject;
            if (node == null) return;

            TryReadDouble(node["X"], out x);
            TryReadDouble(node["Y"], out y);
        }

        /// <summary>把 JToken 当数字读；不是数字（缺失 / null / 字符串 / 对象…）返回 false。</summary>
        private static bool TryReadDouble(Newtonsoft.Json.Linq.JToken token, out double value)
        {
            value = 0.0;
            if (token == null) return false;
            if (token.Type != Newtonsoft.Json.Linq.JTokenType.Float &&
                token.Type != Newtonsoft.Json.Linq.JTokenType.Integer) return false;

            value = token.Value<double>();
            return true;
        }

        #endregion

        #region 保存落点（选择上级目录）
        /// <summary>
        /// "选择上级目录"类对话框的起始位置 —— 另存到 / 新工程首次保存 / 打开模板共用这一处。
        ///
        /// 为什么不各写各的：这三处的语义都是"**把这个工程目录放到哪个父目录下**"，
        /// 起始位置理应同一个优先级推出来。原来各写各的，必然漂移 ——
        /// 一处看设置项、一处看当前工程目录，最后连"弹出来停在哪"都说不清。
        ///
        /// 优先级：
        ///   ⓪ **用户设置的「默认工作路径」** —— 他把各工程目录都放在同一个地方，
        ///      那就该从那儿出发，而不是每次从上次那个工程目录再重选一遍。
        ///      它是"各工程目录的共同父目录"，语义与下面几级完全一致，所以直接当第一优先。
        ///      ⚠ 空值 / 目录已不存在 → 跳过（用户可能把那个目录移走或删了）。
        ///   ① 当前工程目录的父目录 —— 另存为就是"换个地方放同名的工程目录"，从原地出发最顺；
        ///   ② 有图层但还没归属（新建工程后没保存过）→ 取图层所在目录的**父目录**。
        ///      ⚠ 必须是父目录，不能是图层所在目录本身：对话框的返回值会再拼上工程名
        ///      （= 图层所在目录名），初值若给目录自己，用户直接确认就会得到
        ///      `...\1516601-00-C\1516601-00-C` —— 白套一层。
        ///   ③ 上次打开过的工程目录（kind == "template" 时 LastProjectPath 就是它）；
        ///   ④ 桌面（与 GetGerberBrowseStartPath 的兜底一致，任何机器上都有意义）。
        ///
        /// 每一级都要求父目录**实际存在**才采用 —— 否则系统对话框会自己退化到"我的电脑"，
        /// 那比给桌面更糟：用户丢掉了全部上下文，还得从头点进去。
        /// </summary>
        /// <summary>
        /// 「默认工作路径」的**有效值**。
        ///
        /// 设置项里存的是 **`Broad`** 这样一个**相对路径**（相对程序目录），而不是绝对路径 ——
        /// 绝对路径一旦写进默认值，换台机器就指向不存在的地方（`Program.FileName` 当年正是这个毛病：
        /// 默认值硬编码了开发机路径）。于是解析放在这里：
        ///   · 空       → 程序目录 \ Broad
        ///   · 相对路径 → 程序目录 \ 它
        ///   · 绝对路径 → 原样用；**但若它不存在（换了机器 / 被删了）就回退到程序目录 \ Broad**，
        ///                而不是死抱着一个不存在的路径 —— 那会让后面所有优先级一起落空。
        /// </summary>
        private static string GetDefaultProjectRoot()
        {
            string configured = Properties.Settings.Default.DefaultProjectRootPath;
            string fallback = Path.Combine(Application.StartupPath, "Broad");

            if (string.IsNullOrEmpty(configured)) return fallback;
            if (!Path.IsPathRooted(configured)) return Path.Combine(Application.StartupPath, configured);

            return Directory.Exists(configured) ? configured : fallback;
        }

        /// <summary>
        /// 保证「默认工作路径」这个目录**存在**（不存在就建一个）。
        ///
        /// 为什么需要：它现在**有默认值**（程序目录下的 `Broad`）。用户在参数设置里什么都不改时，
        /// 新建工程就该直接落到那儿 —— 目录若不存在，那一步会退化成"弹框问位置"，默认值等于白设。
        /// 启动时建一次即可（与 `KLog` 建 `logs` 文件夹是同一个套路）。
        /// 失败**不弹框**：建不出来只是少了点便利，不该拦着用户用程序。
        /// </summary>
        private static void EnsureDefaultProjectRoot()
        {
            try
            {
                string root = GetDefaultProjectRoot();
                if (!Directory.Exists(root)) Directory.CreateDirectory(root);
            }
            catch (Exception ex)
            {
                KLog.Info($"创建默认工作路径失败（不影响使用，之后会退回让你选位置）：{ex.Message}");
            }
        }

        private string GetProjectParentBrowseDir()
        {
            // ⓪ 默认工作路径（用户可改；没改过就是程序目录下的 `Broad`）。
            //    **只作对话框初值** —— 它绝不参与落点决策
            //    （落点仍只有两个来源：有归属就地写回 / 无归属当场问，见 SaveTemplateFile）。
            string configured = GetDefaultProjectRoot();
            if (!string.IsNullOrEmpty(configured) && Directory.Exists(configured))
                return configured;

            // ① 当前工程目录的父目录
            string fromCurrent = GetParentDirIfExists(_currentTemplatePath);
            if (!string.IsNullOrEmpty(fromCurrent)) return fromCurrent;

            // ② 第一个图层所在目录的父目录（新建工程尚未归属）
            if (_layers.Count > 0 && !string.IsNullOrEmpty(_layers[0].FilePath))
            {
                string fromLayer = GetParentDirIfExists(Path.GetDirectoryName(_layers[0].FilePath));
                if (!string.IsNullOrEmpty(fromLayer)) return fromLayer;
            }

            // ③ 上次打开过的工程目录。
            //    LastProjectPath 在 kind == "gerber" 时是**文件**、在 kind == "template" 时是**目录**，
            //    但两种都只要"它的上一级"，所以这里不必分开处理。
            if (Properties.Settings.Default.LastProjectKind == "template")
            {
                string fromLast = GetParentDirIfExists(Properties.Settings.Default.LastProjectPath);
                if (!string.IsNullOrEmpty(fromLast)) return fromLast;
            }

            // ④ 桌面
            return Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
        }

        /// <summary>
        /// 取某个目录（或文件）的父目录，且要求它**实际存在**；拿不到就返回 null。
        ///
        /// 传进来的既可能是目录也可能是文件，由调用方决定 —— 本方法只做一件事：
        /// 去掉尾部分隔符后取上一级。路径为空、已经在根上、父目录不存在，三种情况都返回 null，
        /// 让调用方继续往下一条优先级走。
        /// </summary>
        private static string GetParentDirIfExists(string dir)
        {
            if (string.IsNullOrEmpty(dir)) return null;

            string trimmed = dir.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            string parent = Path.GetDirectoryName(trimmed);
            if (string.IsNullOrEmpty(parent)) return null;

            return Directory.Exists(parent) ? parent : null;
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