# GerberParserSmartV4.0 —— 项目长期记忆

工程：`D:\MyWork\Gerber\选点软件\高清版V4\GerberParserV4.0`
**本文件只做「红线 + 索引」**。详细理由在代码注释、`MD文件汇总（AI）/`、`../GerberParserV4.0_交接文档.html`。

## 一、方向
- **多图层**（2026-09-19）：`_layers` 权威，顺序 = 加载顺序 = 绘制顺序（末位最上层）。图层容器是画布右上角**运行时创建**的控件（`groupBox4` 是空面板）。身份 = **文件名**（`LayerInfo.Id`）、配色 = `ColorIndex`，**都不能用当前序号**（置顶会改序号）
- 两条语义：**命中按图层叠放优先**（上层该处无图形才穿透）；**隐藏 = 不画 + 不选 + 数据保留**

## 二、环境红线
- .NET Framework 4.8 / WinExe；源码 **UTF-8 BOM + CRLF**（`core.autocrlf=true`）。⚠ Edit 工具**有时整文件重写成 LF** → 改完必跑 `git ls-files --eol <file>` 验 `w/crlf`，不对用 node 归一；含中文路径的文本改写一律用 node
- 沙盒**能编译自验**：`dotnet build GerberParserSmartV4.0.csproj -c Debug -p:Platform=x64 -p:OutputPath=<仓库外>`。被拦的：`MSBuild.exe`、`Add-Type`、`Reflection.Assembly.LoadFrom`
- 临时文件写 `C:\Users\59538\.workbuddy\tmp\`；⚠ **不可碰** `bin\x64\Debug\Json\`（真实模板数据，不可再生）
- 日志 `KLog` → exe 目录 `logs\yyyy-MM-dd.log`
- 🔴 **`GerberParserTool` 源码在 `D:\MyWork\GerberParserTool\`**（net48 类库，**不在 git 下 → 改前先备份**）。流程：改源码 → `dotnet build -c Debug` → 把 `bin\Debug\net48\GerberParserTool.dll` 拷进本工程 `lib\`（csproj 里 `HintPath=lib\GerberParserTool.dll`）。⚠ `D:\CSharpWorkBase\ClassLib\GerberParserTool\` 是 2026-09-18 旧副本，**别从它编译**。⚠ 它**往 Console 打解析日志**：2026-09-21 已删 38 行 dump + 清空 `PrintSummary()` 方法体（签名保留，MainForm 仍在调）

## 三、设计决策红线（改前必读代码注释）
- 选点**不设门禁**，单选/多选只是点击语义；**框选 = 选择，框选限定 = 作用域**；框外点击一律解除限定（`ReleaseScopeIfOutside`）
- `SelectedCircle.IsSelected` 与 `_selectedCircles` 两份状态必须同步；清空走 `ClearAllSelection()`
- 画布纯黑；颜色按 Halcon 语义（`"green"`=(0,255,0)，非 GDI+ 的 Color.Green）
- 「保存模板」**就地保存**。落点只有两个来源：有归属 → 就地写回；无归属 → 弹目录选择器。初值走 `GetProjectParentBrowseDir()`（= 当前工程目录的**父**目录，返回值会再拼工程名）。绝不用别处路径顶替 `_currentTemplatePath`。写文件用 `WriteFileAtomic`
- 🔴 **图层状态拷进图形对象**：`SelectedCircle` 带 `LayerId/LayerDepth/LayerHidden`（运行期，不进 json），`RefreshLayerState()` 在载入/勾选/置顶三处刷新。**别用图层名查表** —— 对不上会静默退化成纯距离排序（症状："只能选到大圆"）
- 命中判定 `ShapeQuery.HitTest(…, layerDepth)`，图层知识由 `MainForm.DepthForHitTest` 提供（几何库无状态）
- 🔴 **图形唯一键 = 图层 + 坐标(4位小数) + 形状 + 尺寸**（`ShapeKey`，`GetSize()` 折成三元组）。缺一都错位认领；`IsSingleShapeInShapes` 口径必须一致
- 🔴 **底图 LOD（2026-09-21）**：`DrawGerberApertures` 按**屏幕尺寸**分级 —— `<1px` 不画 / `1~5px` 画实心方块 / `≥5px` 精确描边（判据是屏幕尺寸，自动等价"越放大越精细"）。**只降级底图**，选点永远精确。🔴 **两趟画（精确/简化）必须在图层循环内部** —— 挪到外面会让上层简化图形盖住下层精确图形。实测 20188 图元 **56ms/帧 → 16.5ms/帧**。⚠ 已实测否掉的路：跳过亚像素图元（PCB 上无收益）、合并成一条 GraphicsPath（反而慢 3.5 倍）
- **缩放上限走 `App.config` 的 `ViewMaxScale`（默认 500）**，`ApplyViewScaleLimits()` 在 `MainForm_Load` 读 + 容错。⚠ `KViewport.Scale` 语义是"世界单位→屏幕像素"（1.0=1:1），**不是倍数**
- 🔴 **悬浮浮层两条坑，缺一条就"浮层下方内容滞后/闪"**（2026-09-21）：① 视口变化时浮层压根没被重绘 —— 控件内部 `OnViewportChanged` 用**无参** `Invalidate()`（不失效子控件）→ 本工程已在 `OnViewChanged`（`MainForm.cs:774`）补 `Invalidate(true)`；② 浮层没双缓冲（半透明要先让父控件补画底下内容、再叠半透明底）→ 一律用 `DoubleBufferedOverlayToolbar` 子类。🔴 **别改共享库 `Korey.SmartWindow`**（多工程引用会外溢）。⚠ `RefreshKWindow()` 已从 `Refresh()` 改成 `Invalidate(true)`；**滚轮不走它**
- 🔴 **去重口径两套别混**：① 四维键管"选点对象 ↔ Gerber 图形"对应，模型内跨图层同坐标仍各占一条；② 保存前按坐标判重 —— `ConfirmNoDuplicatePositions()`（`SaveTemplateToPath` 开头），**同落盘坐标 >1 条即弹窗阻断**（返回修改 / 仍然保存）。用户裁定"不同图层同坐标算两个点"**不被允许**（插针机按坐标定位 → 重复插针）。**不做自动去重**（留哪条是业务判断）
- 🔴 **不在任何 Gerber 图形上的选点必须收编进 `_allShapes`**（`AppendOrphanSelections()`）—— 否则"画得出、存得下、点不着"
- **合并** toggle 与单选/多选互斥；候选独立于选中状态（gold 轮廓）。多选扩散**限定同图层**
- 🔴 **`HeaderType`（h1/h2/h3）与 `Layer` 正交**：挂在图形上，工具栏三个互斥 toggle 决定归属，**切类型不改已选点**。h1 → `SelectedCircleColor`（**老名字，默认白，老工程观感不变**）/ h2 → `HeaderH2Color` / h3 → `HeaderH3Color`；写入点 = 单选/多选扩散/框选（Ctrl+Z 连类型回滚）；`ParamsForm` **只管配色**
- 🔴 **图层容器列几何只有一份**（`LayerRowLayout`）；「置顶」列宽 = `max(24, 文字宽+6)` 量出来的。首行 = 表头 `文件名｜置顶｜【全选】`，只有方框可点（三态）
- 🔴 **批量显隐必须重建容器**（`SetAllLayersVisible`→`RefreshLayerPanel`）；**单行切换只刷新**（重建会清悬停态）。两者都只改 `LayerInfo.IsVisible`
- **「默认工作路径」存的是相对路径**（默认 `Broad`），解析在 `GetDefaultProjectRoot()`。🔴 **绝不参与落点决策**，只做各类目录对话框的初值。⚠ 通用原则：**默认值里不放绝对路径**
- **circles.json 天然多图层**，`Version` 现为 **2.5**（2.1 Layers / 2.2 图形 Layer / 2.3 ColorIndex / 2.4 Header / **2.5 `Positions[]` → 记录上的 `X`/`Y`**）。🔴 **读端不兼容旧格式**（用户裁定）：v2.0~2.4 老工程打开后坐标**全读成 0**（属预期，⚠ 别对它点保存，不可逆）；但字段缺失仍容错（`Layer`→空串 / `Header`→`h1`），读端不校验 Version
- 别用 try/catch 探测字段在不在（`RuntimeBinderException` 很贵，实测 6 图层 150ms）→ 用 `JToken` 索引器

## 四、解析库 GerberParserTool（2026-09-21 第二轮已修，细节见 `.workbuddy/memory/2026-09-21.md` 第二十二轮）
`ParseResult` 新增 `FilledAreas`（G36/G37 轮廓）/ `Strokes`（D01 描边）/ `DetectedFormat`（诊断）。
已修：① 单位 `%MO*%` 换算（坐标 + 光圈尺寸）② FS 通用解析（原为 4 个硬编码字符串）③ `%AM` 宏光圈 ④ region ⑤ D01 描边 ⑥ `Reset()` 状态泄漏 ⑦ Excellon 格式声明。全库曝光点 78632→84271，81 文件验证 PASS=20/FAIL=0。
🔴 **上位机尚未消费 region/stroke** → 丝印（GTO/GBO）、板框（GKO/GD1/GG1/GM2）在画布上**仍空白**。接手要点：
1. `TryParseLayerFile` 把 `result.FilledAreas`/`.Strokes` 存进 `LayerInfo`
2. 绘制按图层颜色（region → `FillPolygon`，stroke → 带笔宽 `DrawLine`）
3. 🔴 **必须做 LOD**：全库 144.7 万顶点 / 18.7 万条线，不分级整板视图会卡
4. 包围盒可选换 `ViewportHelper.CalculateBoundingBox(ParseResult)`（含 region+stroke）。⚠ GD1 描边范围 646×428mm（板本体 76×101mm），用新重载会把视图拉大
- ⚠ 用 inch 图纸选过的点，落盘坐标是 1/25.4 的值 → 插针机会打错位置（旧 bug 已修，改前先查 `circles.json`）

## 五、文档体系（产出文档前必读 `MD文件汇总（AI）/README.md`）
四类：`待办`（只维护 `待办清单.md` 一张表，完成/作废**保留标注**）/ `任务&需求`（`R-YYYYMMDD-NN-标题.md`，**必写验收目标 + 范围含"不包含什么"**）/ `问题&解决方案`（以问题点命名无日期前缀，**必含根因 + 解决方案 + 为什么否掉别的做法**）/ `系统架构&协议设计`（标 `（来源：文件:行号）`+ 最后核对日期，⚠️待核实禁止臆造）。引用文档**从仓库根写全**；新增后回 `MD文件汇总（AI）/README.md` 登记；写完跑 `powershell -ExecutionPolicy Bypass -File "MD文件汇总（AI）\检查文档归置.ps1"`。
给 AI 的过程记录写 `.workbuddy/memory/`。跨工具约定写在**仓库根 `README.md`**（用户 2026-09-21 指定：**不建 CLAUDE.md**）。
既有资料（不迁入、只在 README 登记）：`../GerberParserV4.0_交接文档.html`、`../GerberParserV4.0_功能说明.html`、`../Halcon替换为Korey_SmartWindow迁移说明.html`、`D:\MyWork\Common\Korey.SmartWindow.API.md`（改画布前必读）

## 六、下游插针机 RivetPin2019
`D:\MyWork\Account\1_BEST\RBLWorkMin\Rayble\RivetPin2019`（**非 git 仓库**），代码在 `Rayble\Common\`，解析入口 `Common\CADImportDlg.cpp`（**UTF-8 BOM + CRLF**，与 Common 里多数 GBK 文件不同）。交接文档 `RBLWorkMin\PROJECT_CONTEXT.md`（铁律：只改 RS 系列）。
- **唯一消费点 = `CADImportDlg`**（全目录 grep 确认）。读 `TemplateInfo.Box*`/`TotalCount`/`HeaderColors`(读出未用) 与 `Shapes.*[].Positions[].X/.Y` + `Id/SizeX/SizeY/Rotation`；**不读** `Layer`/`Layers`/`ColorIndex`/`Version`
- 🔴 **`Shapes` 顶层键含义相反**：插针机按**老项目 V5**写 → 顶层是 `h1/h2/h3`（头类型是**键**，下面再分 Circles/Rectangles/Ovals）；本项目 v2.1+ 顶层是 `Circles/Rectangles/Ovals`（头类型在记录 `Header` 里）。喂本项目的 json → `getMemberNames()` 拿到形状名 → `headerGroup` 是**数组** → `isMember()` 前置断言失败 → `Json::LogicError` → **无 catch → 崩**（报错指向 `json_value.cpp`）
- **本项目 v2.0 起就是按形状分组，插针机从来没成功读过一次** —— 不是"最近改坏的"，是两条线第一次对接才暴露
- ✅ **结论（2026-09-21 已实施，用户裁定）：改插针机对齐本项目，且不做 V5 兼容**（V5 后续废弃）。已改 `CADImportDlg.cpp`（`ParseShapeArray` 按 `Circles/Rectangles/Ovals` 直取 + 头类型读记录 `Header`；`ParseJSONContent` 拆薄壳兜异常；各处判类型）+ `.h` 加 `ParseJSONContentImpl` 声明。⚠ **插针机不在本机工程、未编译** → 待产线编译验证。全文：`MD文件汇总（AI）/问题&解决方案/插针机导入json崩溃（Shapes顶层按形状分组）.md`
- ⚠ 另两条：① 插针机 `isMember`/`operator[]`/`asCString` 前**一律不判类型**，且 `ParseJSONContent` **无 try/catch** → 任何畸形 json 都崩产线（真 bug，与格式无关）；② 本项目写端**不做显隐过滤** → 隐藏图层的点照样导出（待办 T42）
- jsoncpp 源码：`RBLWorkMin\CombineMySource\Include\Json\*` + `Src\Json\*.cpp`。⚠ `D:\MyWork\Gerber\v1\`、`v2\` 下的同名 `json_value.cpp` **不是**它用的那份
