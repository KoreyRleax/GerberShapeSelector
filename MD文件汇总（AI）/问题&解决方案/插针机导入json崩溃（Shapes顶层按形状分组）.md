# 插针机导入 json 崩溃（Shapes 顶层从"按头分组"改成"按形状分组"）

> **症状**：`D:\MyWork\Account\1_BEST\RBLWorkMin\Rayble\RivetPin2019` 导入本项目导出的 `circles.json`，
> 崩溃/报错定位在 `json_value.cpp`（jsoncpp 库文件，不是机台自己的代码）。
> **结论**：不是 jsoncpp 的 bug，是**两份 json 的 `Shapes` 顶层结构不一样**，而读取端按老结构硬取，
> 触发了 jsoncpp 的前置断言。
> **最后核对日期**：2026-09-21（写端代码 + jsoncpp 源码 + 老项目写端，三处都核对过）
> **状态**：✅ **已修**（2026-09-21）—— 改插针机对齐本项目，**不做 V5 兼容**（V5 后续废弃）。落地代码见 3.3。

---

## 一、两份 json 到底差在哪

### 1.1 老项目（稳定版 V5）导出 —— 顶层按**插针头类型**分组

（来源：`D:\MyWork\Gerber\选点软件\稳定版V5\GerberParserV5.0\MainForm.cs:1636-1713`）

```json
"Shapes": {
  "h1": { "Circles": [ { "Id":"D10", "SizeX":0.6, "SizeY":0.6,
                         "Positions":[ { "X":179.226, "Y":28.949 } ] } ],
          "Rectangles": [], "Ovals": [] },
  "h2": { "Circles": [], "Rectangles": [], "Ovals": [] },
  "h3": { "Circles": [], "Rectangles": [], "Ovals": [] }
}
```

头类型 **是结构本身**（`Shapes` 的键）。

### 1.2 本项目（高清版 V4）导出 —— 顶层按**形状**分组，头类型下沉成记录字段

（来源：`MainForm.cs:4415-4449`，v2.4）

```json
"Shapes": {
  "Circles":    [ { "Id":"D10", "Layer":"top.o", "Header":"h1", "SizeX":0.6, "SizeY":0.6,
                    "Positions":[ { "X":179.226, "Y":28.949 } ] } ],
  "Rectangles": [ { "Id":"R5",  "Layer":"smb.o", "Header":"h2", "Type":4, ... } ],
  "Ovals":      [ { "Id":"O2",  "Layer":"smt.o", "Header":"h3", "Type":2, ... } ]
}
```

头类型 **是数据**（每条记录的 `Header` 字段），多出来的 `Layer` 也是记录字段。

### 1.3 为什么必须这么改（不是任性）

`Layer`（来自哪个 Gerber 文件）与 `Header`（用哪个插针头插）是**正交**的两个维度
（见 `MD文件汇总（AI）/任务&需求/R-20260921-03-选点划分h1h2h3与合并.md`）。
按头分组的结构里**没有 `Layer` 的位置** —— 除非再嵌一层，或者把 `Layer` 塞进记录里。
一旦 `Layer` 也得塞进记录，那"头类型做键、图层做字段"就是**两个维度不对称**：
形状、图层、头类型里只有头类型特殊，将来再加维度还是得再改结构。
扁平化（形状做顶层键，图层/头类型都是记录字段）是能同时容纳三个维度的写法。

---

## 二、根因（崩溃的精确位置）

读取端：`D:\MyWork\Account\1_BEST\RBLWorkMin\Rayble\Common\CADImportDlg.cpp:427-518`

```cpp
// :435  拿 Shapes 的所有键 —— 期望是 ["h1","h2","h3"]
Json::Value::Members headerTypes = shapesByHeader.getMemberNames();

for (const auto& headerType : headerTypes) {
    const Json::Value& headerGroup = shapesByHeader[headerType];
    // :442  ← 崩在这里
    if (headerGroup.isMember("Circles")) { … }
}
```

喂进 V4 的 json 之后：

| 步骤 | 实际发生 |
|---|---|
| `getMemberNames()` | 拿到的是 `["Circles","Rectangles","Ovals"]`（**不是** h1/h2/h3） |
| `headerGroup` | 变成**数组**（`[ {Id,…}, {Id,…} ]`），不是对象 |
| `headerGroup.isMember("Circles")` | jsoncpp 的 `isMember` 不判类型，直接调 `find` |
| `Value::find` | `json_value.cpp:1117` 前置断言：`type() == nullValue \|\| type() == objectValue` |
| 断言失败 | `assertions.h:54` → `JSON_FAIL_MESSAGE` → `Json::throwLogicError(…)` **抛异常** |
| 异常没人接 | `ParseJSONContent` 没有 `try/catch` → 未捕获异常 → 程序崩 |

- 抛出点确实在 `json_value.cpp`，所以报错指向它 —— **这是表象，不是 jsoncpp 的问题**。
- `JSON_USE_EXCEPTION` 在 `CombineMySource\Include\Json\config.h:20` 为 `1`，
  所以是**抛异常**（`Json::LogicError`），不是 `abort()`。Debug 下 VS 会停在抛出点。
- 附带：`Circles`/`Rectangles`/`Ovals` 三个键里只有第一个会触发 —— 崩在**第一次循环**。

**一个必须承认的事实**：就算格式对上了，`isMember` 前不判 `isObject()` 也是**真 bug**。
任何一份不符合预期的 json（字段改名、手改坏、别的软件导出）都会让产线程序崩。
这次只是把它暴露出来了。

---

## 三、解决方案（**已实施**：改插针机对齐本项目）

> ### ⚠️ 用户 2026-09-21 裁定 —— 与下面旧稿的差异
> **稳定版 V5 后续将废弃**，因此插针机**不做双格式兼容**，直接只认高清版 V4 的格式。
> 3.2 是**当时的推荐稿（双格式兼容）**，保留下来是为了记录分析过程；
> **实际落地的代码在 3.3**。

### 3.1 判定依据

`Common\` 里**只有 `CADImportDlg` 一个消费点**（全目录 grep `TemplateInfo|Shapes|circles.json`
只命中 `CADImportDlg.cpp/.h` 和 rc 资源），改动面可控。

### 3.2 改动点：`Common\CADImportDlg.cpp`

#### （1）抽一个共用的数组解析函数（放在 `#pragma region Json处理相关函数` 里）

```cpp
// 解析一个形状数组：arr = [ { Id, Layer?, Header?, SizeX, SizeY, Rotation?, Positions:[{X,Y}] } ]
// headerFromRecord = true  → 头类型取记录的 Header 字段（V4 扁平格式，v2.1+）
// headerFromRecord = false → 头类型用参数 headerType（V5 按头分组格式）
static void ParseShapeArray(const Json::Value& arr, ShapeType st,
                            const CString& headerType, bool headerFromRecord,
                            std::vector<ShapeData>& out)
{
    if (!arr.isArray()) return;                       // ← 关键：类型不对直接走人
    for (Json::ArrayIndex i = 0; i < arr.size(); ++i) {
        const Json::Value& item = arr[i];
        if (!item.isObject()) continue;
        const Json::Value& positions = item["Positions"];
        if (!positions.isArray()) continue;

        CString hdr(headerType);
        if (headerFromRecord) {
            const Json::Value& h = item["Header"];     // 缺字段返回 null，不抛异常
            if (h.isString()) {
                const char* p = h.asCString();
                if (p && p[0] != '\0') hdr = CString(p);
            }
        }

        for (Json::ArrayIndex j = 0; j < positions.size(); ++j) {
            const Json::Value& p = positions[j];
            if (!p.isObject()) continue;
            ShapeData s;
            s.shapeType = st;
            s.x = p["X"].asDouble();
            s.y = p["Y"].asDouble();
            s.diameter = s.width = s.height = s.rotation = 0.0;
            if (st == CIRCLE)        s.diameter = item["SizeX"].asDouble();
            else if (st == RECTANGLE){ s.width = item["SizeX"].asDouble(); s.height = item["SizeY"].asDouble(); }
            else                     { s.width = item["SizeX"].asDouble(); s.height = item["SizeY"].asDouble();
                                       s.rotation = item["Rotation"].asDouble(); }
            const Json::Value& idv = item["Id"];
            s.id = idv.isString() ? CString(idv.asCString()) : CString(item["Id"].asString().c_str());
            s.headerType = hdr;
            out.push_back(s);
        }
    }
}
```

> 每个 `isArray()` / `isObject()` 都不是多余的：jsoncpp 的 `operator[]` / `isMember` / `asCString`
> 都带前置断言，**不判类型就调用 = 一颗定时炸弹**。`asDouble()` 对 null 返回 `0.0`，安全。

#### （2）`ParseJSONContent` 里按结构分派（替换 `:427-518` 的形状解析部分）

```cpp
const Json::Value& shapes = root["Shapes"];
if (!shapes.isObject()) { AfxMessageBox(_T("JSON格式不正确：Shapes 不是对象")); return FALSE; }

// v2.1+（高清版V4）：Shapes = { Circles / Rectangles / Ovals }，头类型在每条记录的 Header
// v2.0 （稳定版V5）：Shapes = { h1 / h2 / h3 }，每个头下面再分三种形状
const bool flat = shapes.isMember("Circles") || shapes.isMember("Rectangles") || shapes.isMember("Ovals");

if (flat) {
    ParseShapeArray(shapes["Circles"],    CIRCLE,    _T("h1"), true, m_shapes);
    ParseShapeArray(shapes["Rectangles"], RECTANGLE, _T("h1"), true, m_shapes);
    ParseShapeArray(shapes["Ovals"],      OVAL,      _T("h1"), true, m_shapes);
} else {
    for (const auto& ht : shapes.getMemberNames()) {
        const Json::Value& g = shapes[ht];
        if (!g.isObject()) continue;                 // ← 防崩：V4 的 json 走老分支时靠这行救回来
        CString h(ht.c_str());
        ParseShapeArray(g["Circles"],    CIRCLE,    h, false, m_shapes);
        ParseShapeArray(g["Rectangles"], RECTANGLE, h, false, m_shapes);
        ParseShapeArray(g["Ovals"],      OVAL,      h, false, m_shapes);
    }
}
```

#### （3）顺带必修的三个健壮性问题（**不管选哪条路都要做**）

| # | 位置 | 问题 | 修法 |
|---|---|---|---|
| 1 | `ParseJSONContent` 整体 | 无任何 `catch` → 一份坏文件崩产线程序 | 外层包 `try { … } catch (const Json::Exception& e) { 弹框; return FALSE; } catch (...) { … }` |
| 2 | `:401-415` `templateInfo["BoxLeft"]` 等 | 没判 `templateInfo.isObject()` | 加 `isObject()` 判断；`TotalCount` 用 `isNumeric()` 判一下 |
| 3 | `:458` `circle["Id"].asCString()` | `Id` 若是非字符串（比如数字光圈号）会断言 | 先 `isString()`，否则用 `asString()` |

### 3.3 实际落地的改动（2026-09-21）

**只动了两个文件**：`Common\CADImportDlg.cpp`、`Common\CADImportDlg.h`（加一行
`ParseJSONContentImpl` 声明）。R9 / RS / R5 / R6 各分支的**行为一处未改**
（`AddShapesToModel` / `CountShapesByHeaderType` 原样）。

#### （1）形状解析改为按 V4 的顶层键直取（`:551-596`）

```cpp
const Json::Value& shapes = root["Shapes"];
if (!shapes.isObject()) {
	AfxMessageBox(_T("JSON格式不正确：Shapes 不是对象"));
	return FALSE;
}

ParseShapeArray(shapes["Circles"],    CIRCLE,    m_shapes);
ParseShapeArray(shapes["Rectangles"], RECTANGLE, m_shapes);
ParseShapeArray(shapes["Ovals"],      OVAL,      m_shapes);
```

⚠ **旧格式一律不兼容**：
- V5 的 `Shapes = { h1:{…} }` → 三个数组都取不到 → **解析出 0 个点**，不再崩；
- 本项目 v2.0~2.4 用的 `Positions[0]` 坐标 → 读成 0，**点全落在原点**。

#### （2）新增文件级 `ParseShapeArray`（`:372-441`）—— 一个记录一个点

```cpp
static void ParseShapeArray(const Json::Value& arr, ShapeType st, std::vector<ShapeData>& out)
{
	if (!arr.isArray()) return;

	for (Json::ArrayIndex i = 0; i < arr.size(); ++i) {
		const Json::Value& item = arr[i];
		if (!item.isObject()) continue;

		// 坐标：直接挂在记录上（X / Y）。不兼容 v2.0~2.4 的 Positions[0]
		double px = 0.0, py = 0.0;
		const Json::Value& jx = item["X"];
		const Json::Value& jy = item["Y"];
		if (jx.isNumeric() && jy.isNumeric()) {
			px = jx.asDouble(); py = jy.asDouble();
		}

		// 插针头类型：只认 h1/h2/h3，缺失 / 空串 / 陌生值一律退回 h1（与上游口径一致）
		CString hdr(_T("h1"));
		const Json::Value& h = item["Header"];
		if (h.isString()) {
			const char* p = h.asCString();
			if (p != NULL && p[0] != '\0') {
				CString v(p); v.MakeLower();
				if (v == _T("h1") || v == _T("h2") || v == _T("h3")) hdr = v;
			}
		}

		const double sizeX = item["SizeX"].asDouble();
		const double sizeY = item["SizeY"].asDouble();
		const double rot   = item["Rotation"].asDouble();

		CString id;                                  // 光圈 ID：字符串或数字都取
		const Json::Value& idv = item["Id"];
		if (idv.isString())       id = CString(idv.asCString());
		else if (idv.isNumeric()) id = CString(idv.asString().c_str());

		ShapeData s;
		s.shapeType = st;
		s.x = px; s.y = py;
		s.diameter = s.width = s.height = s.rotation = 0.0;
		if (st == CIRCLE)         { s.diameter = sizeX; }
		else if (st == RECTANGLE) { s.width = sizeX; s.height = sizeY; }
		else                      { s.width = sizeX; s.height = sizeY; s.rotation = rot; }
		s.id = id;
		s.headerType = hdr;
		out.push_back(s);
	}
}
```

> `isArray()` / `isObject()` / `isNumeric()` 一个都不能省 —— jsoncpp 的 `operator[]` /
> `isMember` / `asCString` 都带前置断言，**不判类型就直接抛 `Json::LogicError`**；
> `asDouble()` 对 null 返回 `0.0`，是安全的。
>
> `Positions` 那个兼容 `else` 分支**已经删掉了** —— 用户 2026-09-21 裁定
> "**不需要兼容**"（连选点软件自己的读端 `ReadJsonPosition` 也一并断掉）。
> 现在的 `ParseShapeArray` 只读记录上的 `X` / `Y`。

#### （3）外层兜异常：`ParseJSONContent` 变薄壳

```cpp
BOOL CADImportDlg::ParseJSONContent(const CString& jsonContent)
{
	m_shapes.clear();
	try { return ParseJSONContentImpl(jsonContent); }
	catch (const Json::Exception& e) { ...弹框... }   // jsoncpp 的类型断言
	catch (const std::exception& e)  { ...弹框... }
	catch (...)                      { ...弹框... }
	m_shapes.clear();
	return FALSE;
}
```

原来的函数体整体改名为 `ParseJSONContentImpl`（`CADImportDlg.h` 里加声明）——
这样"兜异常"只有一处，且不必给整个函数体重新缩进。

#### （4）顺带补的类型判断（同 3.2(3) 那张表）

`root.isObject()` / `templateInfo.isObject()` / `HeaderColors.isObject()` /
`TotalCount.isNumeric()`。

### 3.4 编码/行尾（改这个文件必看）

- `CADImportDlg.cpp` 是 **UTF-8 BOM + CRLF**（实测：前 3 字节 `EF BB BF`，578 个 CRLF，**0 个裸 LF**）。
  ⚠ 它跟 `Common\` 里绝大多数文件（GBK 无 BOM，如 `CModelRes.cpp`）**不一样** ——
  按 GBK 读写这个文件会直接乱码。**改完必须复验 BOM 和 CRLF 都还在。**
- 这是**共享文件**（含 `RBL_R9Machine` / `RBL_RSMachine` / `R5` / `R6` 分支）。
  本次只加分支、不改任何已有分支的行为；R9 只收 h1 的逻辑不动。

---

## 四、备选方案与为什么否掉

| 方案 | 做法 | 优点 | 代价 | 结论 |
|---|---|---|---|---|
| **A 改插针机** | 只按 V4 的格式解析（见 3.3） | 上游 schema 可继续演进；只动一个消费点；老代码删得干净 | 要动共享文件；产线要重新编译发版本；**读不了 V5 旧模板** | ✅ **已采纳**（用户裁定 V5 将废弃，故不要双格式兼容） |
| B 改本项目，导出回 V5 分组 | `MainForm.cs` 保存端改成 `Shapes:{h1:{Circles:…}}` | 完全不动产线代码 | `Header` 被提升为结构键 → 与"Layer/Header 正交"的设计打架；`Layer` 只能塞回记录里，两个维度不对称；**V4 自己的读端也要跟着改**；将来再加维度还得再动一次结构 | ❌ |
| C 本项目导出时加"兼容模式"开关 / 导出两份 | 保存时多出一个选项 | 不动产线 | 一个事实存两份 —— 本项目在 `IsSelected` 与 `_selectedCircles` 上已经吃过"两份状态不同步"的亏；且用户每次都得记得选 | ❌ |
| D 中间转换工具 | 写个小工具把 V4 json 转成 V5 json | 两边都不动 | 多一个要维护的环节 + 现场多一步操作；格式再演进就要跟着改两次 | 备选（临时救急可用） |

**判断原则**：**消费方对上游要宽容，上游不该被消费方绑架。**
V4 的扁平格式是 V5 分组格式的**超集**（V5 能表达的"一个点 + 一个头类型"，V4 一条记录就能表达；
反过来 V5 格式装不下 `Layer`）。让超集退回去迁就子集，等于把上游的表达力锁死。

---

## 五、还有两个要你拍板的语义问题（与本次崩溃无关，但会咬人）

1. **隐藏图层的点仍会被导出**。本项目的规则 R1 是"隐藏 = 不画 + 不选 + **数据保留**"，
   写端**不做显隐过滤**（见 `MD文件汇总（AI）/系统架构&协议设计/数据格式-circles.json.md` 与根 `README.md`）。
   → 操作员在选点软件里把某层藏了再保存，插针机**照样会拿到那一层的点**。
   如果现场的期望是"藏起来的层不进模板"，那是**本项目**的改动（保存端加 `IsShapeLayerVisible` 过滤），
   跟格式兼容是两回事。
2. **`TotalCount` 含 h2/h3**。插针机 RS 分支用它做"成功添加 N 个点位"的提示，R9 分支只算 h1
   （`CADImportDlg.cpp:159`）。V4 与 V5 的 `TotalCount` 口径一致（都是全部已选点），无需改。

---

## 六、验收目标（改完插针机后怎么算通过）

1. 用**本项目**（高清版 V4，v2.5）导出一份含 h1/h2/h3 三种头、两种以上图层的 `circles.json`
   → 插针机导入：**不崩**，弹出"解析成功"，圆形/矩形/椭圆数量与选点软件状态栏一致，
   h1/h2/h3 三个计数也对得上。
2. 三种头类型分别导入后，模板里点位的类型（RS：0/2/4；R9：只 h1）与选点软件里标的一致。
3. 手工把 `Shapes` 改成 `[]`（数组）/ 删掉 `TemplateInfo` / 把 `Id` 改成数字
   → 插针机**弹框提示**，不崩。
4. **（不再要求）** 老项目 V5 导出的 json —— V5 已废弃；插针机对它会解析出 0 个点，**不崩即算通过**。
5. **（不兼容，属预期）** 本项目**老工程**（v2.0~2.4 导出的 `circles.json`，坐标在 `Positions[0]` 里）
   用**选点软件**打开时，坐标会全读成 0、点堆在原点 —— 这是**有意为之**（用户裁定不兼容旧格式），
   不是缺陷。⚠ 打开这类老工程后**不要点保存**，否则会把"坐标全 0"写回去，**不可逆**。
