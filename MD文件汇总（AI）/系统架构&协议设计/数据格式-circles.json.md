# 数据格式 —— circles.json

> **适用范围**：模板目录里的 `circles.json`（本程序唯一的输出文件，也是与下游的**契约**）。
> **以代码为准**：读端 `LoadJsonFormat` / `ReadJsonLayer` / `ReadJsonHeader`，写端 `SaveTemplateToPath`（`MainForm.cs`）。
> **最后核对日期**：2026-09-21（v2.5：坐标从 `Positions[]` 提到记录上的 `X` / `Y`）
> **AI 整理，需人工复核。**

## 一、完整结构（v2.5）

```json
{
  "TemplateInfo": {
    "TemplateName": "q02578f044a00",
    "SourceFile": "top.o",
    "Layers": [
      { "FileName": "bot.o", "IsVisible": true,  "ColorIndex": 0 },
      { "FileName": "drl.o", "IsVisible": true,  "ColorIndex": 1 },
      { "FileName": "top.o", "IsVisible": true,  "ColorIndex": 5 }
    ],
    "TotalCount": 3852,
    "SelectedCount": 3852,
    "CircleCount": 3852,
    "RectangleCount": 0,
    "OvalCount": 0,
    "BoxLeft": 0, "BoxTop": 0, "BoxRight": 218, "BoxBottom": 130,
    "SaveTime": "2026-09-21 09:32:09",
    "Version": "2.5",
    "MirrorState": { "IsMirroredX": false, "IsMirroredY": false }
  },
  "Shapes": {
    "Circles": [
      { "Id": "D10", "Layer": "top.o", "Header": "h1", "SizeX": 0.6, "SizeY": 0.6,
        "X": 179.226, "Y": 28.949 }
    ],
    "Rectangles": [
      { "Id": "R5", "Layer": "smb.o", "Header": "h2", "Type": 4, "SizeX": 1.2, "SizeY": 0.8,
        "X": 100.0, "Y": 50.0 }
    ],
    "Ovals": [
      { "Id": "O2", "Layer": "smt.o", "Header": "h3", "Type": 2, "SizeX": 1.6, "SizeY": 0.9, "Rotation": 30.0,
        "X": 120.0, "Y": 60.0 }
    ]
  }
}
```

## 二、字段说明

| 字段 | 说明 |
|---|---|
| `TemplateInfo.TemplateName` | 工程名（= 模板目录名） |
| `TemplateInfo.SourceFile` | **第一个图层的文件名**。兼容只认单底图的旧读取方，多图层下没有完整语义 |
| `TemplateInfo.Layers[]` | **图层清单**：`FileName` / `IsVisible` / `ColorIndex`。**数组顺序 = 图层绘制顺序**（末位 = 压在最上面） |
| `TemplateInfo.TotalCount` / `SelectedCount` | 点数（当前两者相同；`SelectedCount` 统计 `IsSelected` 为真的个数） |
| `TemplateInfo.CircleCount` / `RectangleCount` / `OvalCount` | 按形状分组计数 |
| `TemplateInfo.BoxLeft/Top/Right/Bottom` | 内容包围盒（`Math.Round(…,4)`） |
| `TemplateInfo.Version` | 写端标记的版本号；**读端不校验**（老文件照样能开） |
| `TemplateInfo.MirrorState` | 保存时的翻转状态 |
| `Shapes.Circles[].Id` | 光圈 ID（如 `D10`）。**只在单个 Gerber 文件内有意义** |
| `Shapes.*[].Layer` | **该图形所属的图层文件名** —— 多图层选点靠它区分 |
| `Shapes.*[].Header` | **该点由哪个插针头插**：`"h1"` / `"h2"` / `"h3"`（v2.4 新增）。缺字段 / 空串 / 陌生值 → 读成 `h1`。与 `Layer` **正交**：同一图层上的点可以分属三个头 |
| `Shapes.*[].SizeX` / `SizeY` | 圆的直径 / 矩形的宽高 / 椭圆的长短轴；`Math.Round(…,4)` |
| `Shapes.Ovals[].Rotation` | 椭圆旋转角（度，顺时针为正）；`Math.Round(…,2)` |
| `Shapes.Rectangles/Ovals[].Type` | `(int)ApertureShape` 的转换值。⚠️**待核实**：具体枚举值以 `GerberParserTool.ApertureShape` 为准（圆的记录里刻意不写 `Type`） |
| `Shapes.*[].X` / `.Y` | 点位坐标（`Math.Round(…,4)`）。**v2.5 起直接挂在记录上** —— 原来包在 `Positions` 数组里（每条只放一个元素），那层数组是纯粹的历史包袱，还逼着下游解析多写一层循环。<br>🔴 **读端不兼容旧格式**（用户 2026-09-21 裁定）：打开 v2.0~2.4 的老工程，坐标会**全部读成 0** |

## 三、是多图层的吗？—— 是

**每条记录都带自己的 `Layer`**，所以：

- **不同图层**上的点各占一条记录，靠 `Layer` 区分；
- **同一图层**上不同坐标的点也各占一条；
- 因此 `Shapes.Circles` 里出现**重复坐标**是可能的 —— 那多半是不同图层的两个点落在同一坐标。
  ⚠ **但它在保存时会被拦下**（2026-09-21 用户裁定"不允许"）→ 见 §四之二。

（来源：保存端 `Shapes.Circles = circles.Select(c => new { Id = c.ID, Layer = c.Layer ?? "", … })`）

## 四、去重口径（图形的唯一键）

```
ShapeKey = 图层 + 坐标(4 位小数) + 形状 + 尺寸
```

四个维度缺一都会出事：

| 少了 | 后果 |
|---|---|
| 图层 | `GBL` 与 `GBS` 在同一坐标的图形互相顶替 |
| 坐标 | 显然（统一 `Math.Round(…,4)`，与写端同精度 → 比对即精确相等，不需要容差） |
| 形状 | 同坐标的圆与矩形分不开 |
| **尺寸** | 同层同坐标的**焊盘与它的外形框**互相顶替（2026-09-21 修掉的就是这条） |

**同一个键 = 界面上同一个点**，只会被选中 / 保存一次。
实现细节与踩坑见 `MD文件汇总（AI）/问题&解决方案/重复坐标的图形互相顶替.md`。

### 四之二、导出端的**第二条口径**：按坐标判重（2026-09-21）

上面那个四维键回答的是"模型内谁是谁"。但**交付给插针机的落点集合另有口径** ——
下游按**坐标**定位打针（**不读 `Layer`**），同一个坐标出现多条记录 = 重复插针。

于是保存前多了一道**检出 + 弹窗**（注意：**不是去重，点不会被自动删**）：

```
落盘坐标 = ( Math.Round(X, 4), Math.Round(Y, 4) )     ← 与写端同一次舍入，比对即精确相等
同一个落盘坐标上有 > 1 条记录 → 冲突
```

- 用户裁定（2026-09-21）：**这种情况不被允许**。处理方式是弹窗列出冲突（图层 / 形状尺寸 / 头类型），
  由用户选「返回修改」（一个文件都不写）或「仍然保存」。
- **为什么不用自动去重**：`GBL` / `GBS` 常在同坐标放不同尺寸的图形，插针机要打的通常是其中某一条，
  程序猜不准 —— 猜反就是打错针位。
- 🔴 **两套口径别混**：四维键**含图层**，拿它判重会把"跨图层同坐标"当成两个正常点而漏判。
- 已知边界：`Math.Round` 默认**银行家舍入**，两个只差 `1e-5` 的邻居可能落到不同的第 4 位小数上 →
  不判为重复（但**落盘时也是这么舍入的**，所以它们在文件里本来就是两个坐标）。
- 全文（含验收目标与备选方案取舍）：`MD文件汇总（AI）/任务&需求/R-20260921-06-保存前同坐标重复点检查.md`。

## 五、版本演进

| 版本 | 变更 | 谁需要知道 |
|---|---|---|
| 2.0 | 最初形态：只有 `Shapes`，图形**没有图层字段** | 打开这种老工程时，选点的 `Layer` 是空串，靠"退化键"（坐标+形状+尺寸）回填真实图层 |
| **2.1** | `TemplateInfo` 增加 `Layers`（图层清单 + 各层勾选状态） | 打开时据此重建图层容器与勾选状态 |
| **2.2** | 每个图形增加 `Layer`（图层文件名） | 没有它，选点对象与图形缓存对不上号 —— 表现为"点一下重复、再点取消不掉" |
| **2.3** | `Layers` 的元素增加 `ColorIndex` | 图层顺序可被用户「置顶」改动，配色不能再用"当前序号"反推；存下来才能保证同一个工程每次打开颜色一致 |
| **2.4** | 每个图形增加 `Header`（插针头类型 h1/h2/h3） | 选点要按插针头分派（V5 的功能移植）。老文件缺这个字段 → 读出即 `h1`，**老工程打开后的颜色与行为跟改动前完全一致** |
| **2.5** | 坐标从 `Positions[]` 数组提到记录上的 `X` / `Y` | 每条记录本来就只放一个坐标，那层数组是历史包袱，还逼着下游多写一层循环。🔴 **读端不兼容旧格式**（用户 2026-09-21 裁定）→ 打开 v2.0~2.4 的老工程坐标会全读成 0；下游插针机的解析同步简化（同样不兼容旧格式） |

**读取端的容错**（重要）：

> ⚠️ **插针机在 2026-09-21 之前从来没成功读过本项目的 json 一次** —— 这不是"某次升级弄坏了"。
> 本项目的 `Shapes` 从 v2.0 起就是**按形状分组**（那时还没有 h1/h2/h3 概念，头类型是 2026-09-21
> 从 V5 移植过来的）；而插针机的 `CADImportDlg`（2026-08-18 从**老项目 V5** 移植）一直按
> **按头分组**解析。两条线是**第一次对接**才暴露的。
> → **2026-09-21 已改插针机对齐本项目**（V5 分组格式不再兼容，V5 后续废弃），见第七节。

- **只认 `circles.json` 这一个文件名**（原来会拿目录里任意 `*.json` 凑数，读不出 `Shapes` 还完全静默 ——
  认错文件比找不到文件更难查）。
- **不校验 `Version`**。字段缺失时：`Layer` 缺失 → 靠退化键回填；
  `Layers` / `ColorIndex` 缺失 → 退回"全部可见 + 按当前顺序分配颜色"；
  `Header` 缺失 → 读成 `h1`（<br>`ReadJsonHeader` 走 `JObject` 索引器，缺字段返回 null 而**不是**抛异常）。
- 🔴 **坐标不做退化**（v2.5）：只认记录上的 `X` / `Y`，没有就按 0 处理 ——
  **v2.0~2.4 的老工程打开后，所有点会堆在原点**（用户 2026-09-21 裁定不兼容旧格式）。
- ⚠️ **`LoadJsonFormat` 目前仍用 `dynamic` 点属性读字段，缺字段会抛异常**
  （外层 catch 会把整个工程判成"选点文件读取失败"）。见 `MD文件汇总（AI）/待办/待办清单.md` T09。

## 六、写端的两个约定

1. **原子替换**：先写同目录 `.tmp`，再 `File.Replace`（`WriteFileAtomic`）。
   原实现在 `File.Delete` 与 `WriteAllText` 之间有危险窗口 —— 崩在中间会让模板只剩 Gerber 而 JSON 已丢。
2. **图层文件要一起拷贝**到模板目录（`_layers` 里每一个，已存在则跳过）。
   多图层下这步是必须的：工程目录不自带图层，换台机器打开就只剩一个底图。

## 七、下游：插针机（RivetPin2019 / `Common\CADImportDlg`）

**2026-09-21：已改插针机对齐本项目**（它原来按**老项目 V5** 的分组格式解析，一读就崩）。
根因、改法、四种方案的取舍见
`MD文件汇总（AI）/问题&解决方案/插针机导入json崩溃（Shapes顶层按形状分组）.md`。

- 唯一消费点：`D:\MyWork\Account\1_BEST\RBLWorkMin\Rayble\Common\CADImportDlg.cpp`
  （`Common\` 全目录 grep `TemplateInfo|Shapes|circles.json` 只命中它和 rc 资源）。
- 改动后它**实际读取**的字段：

| 它读的 | 用途 |
|---|---|
| `TemplateInfo.BoxLeft/Top/Right/Bottom` | 与相机定位区域算偏移量（`CalculateOffset`） |
| `TemplateInfo.TotalCount` | 提示"成功添加 N 个点位"（R9 改用 h1 计数） |
| `TemplateInfo.HeaderColors`（可选） | 读出但未使用 |
| `Shapes.Circles/Rectangles/Ovals[].X` / `.Y` | 点位坐标（v2.5 起在记录上；**不做 `Positions[0]` 兼容**） |
| `Shapes.*[].Id`、`SizeX`、`SizeY`、`Rotation` | 存进 `ShapeData`，当前**不参与落点** |
| `Shapes.*[].Header` | 决定 `addPos` 的点类型：RS → h1/h2/h3 映射 0/2/4；R9 → **只收 h1** |

- 它**不读** `Layer`、`Layers`、`ColorIndex`、`Version`、`SelectedCount`、`MirrorState`。
  → 多图层信息对插针机是透明的，加字段不会影响到它。
- 🔴 **旧格式一律不再兼容**（用户 2026-09-21 裁定：V5 后续将废弃 + 旧坐标格式不需要兼容）：
  - 稳定版 V5 的 `Shapes = { h1:{…} }` → 三个数组都取不到 → **解析出 0 个点**（不会再崩）；
  - 本项目 v2.0~2.4 用的 `Positions[0]` 坐标 → 读成 0，**点全落在原点**。
- 🔴 插针机的解析已加**逐元素判类型** + **外层 try/catch**（`ParseJSONContent` 薄壳 +
  `ParseJSONContentImpl`）。这两条与格式无关：jsoncpp 的 `isMember` / `operator[]` / `asCString`
  都带前置断言，**不判类型就直接抛 `Json::LogicError`**，而产线程序不能因一份畸形文件就崩。

### 格式对照（V5 已废弃）

```
老项目 V5（已废弃）             本项目 V4（v2.5，插针机现按此解析）
Shapes                         Shapes
├─ h1                          ├─ Circles[]
│   ├─ Circles[]               │   └─ { Id, Layer, Header:"h1", SizeX, SizeY, X, Y }
│   ├─ Rectangles[]            ├─ Rectangles[]
│   └─ Ovals[]                 │   └─ { Id, Layer, Header:"h2", Type, X, Y }
└─ h2 / h3 …                   └─ Ovals[]
                                   └─ { Id, Layer, Header:"h3", Type, Rotation, X, Y }
```

## 八、待核实清单

- `Shapes.Rectangles[].Type` / `Shapes.Ovals[].Type` 的具体数值（`ApertureShape` 枚举来自
  `GerberParserTool.dll`，本次未核对）。
- ~~`Positions` 为什么是数组而不是单个坐标~~ —— **已解决（v2.5）**：每条本来就只放一个坐标，
  那层数组是历史包袱，已去掉，坐标直接挂在记录上的 `X` / `Y`。读端两种都认，老工程不受影响。
