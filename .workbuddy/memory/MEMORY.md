# GerberParserSmartV4.0 —— 项目长期记忆

工程：`D:\MyWork\Gerber\选点软件\高清版V4\GerberParserV4.0`（sln：`GerberParserSmartV4.0.sln`）

## 一、项目方向（决定架构取舍，优先读）
- **即将加入多图层功能**：一个文件夹可能不止一个 Gerber 文件，像 GerberView 那样叠加显示。
  `MainForm.Designer.cs` 的 `groupBox4`「图层」空面板就是为此预留。
- 推论：**不要把「打开Gerber文件」与「加载」（模板）两个入口焊死**。多图层会让两者的输入粒度分叉
  ——「打开模板」输入是一个目录，「打开 Gerber」输入是一批图层文件。合并成一个按钮后要同时接受
  单文件 / 多选 / 目录，判别逻辑从"猜类型"升级为"猜类型 + 猜粒度"。
- 多图层的第一道坎是**数据模型**不是 UI：`Aperture` 来自外部 `GerberParserTool.dll`，没有"所属图层"
  字段，多份 `GetApertureList()` 累加后图层身份就丢了。

## 二、环境与工程约定
- 目标框架 **.NET Framework 4.8**（WinExe）；只支持 Framework，不能走 .NET Core 路线
- 源码是 **UTF-8 带 BOM + CRLF** —— 批量改文件必须保持，改成 LF 会让 diff 爆炸
- 沙盒**跑不了 MSBuild**（被判定为受限二进制）→ 改动只能做静态自检，编译与实机验证由上位机工程师本地做
- ⚠ `bin\x64\Debug\Json\` 是**真实使用者的模板数据，不可再生**；清理 bin 前必须确认它安全
- `GerberParserTool.dll` 从 `bin\Debug\`（本工程自身输出目录）引用 —— 清空 bin 后再编译会失败
- `Program.FileName` 是静态全局状态，默认值硬编码了原开发者本机绝对路径（换台机器失效）
- 日志：`KLog` 写 exe 目录下 `logs\yyyy-MM-dd.log`（原来 Console.WriteLine 在 WinExe 下会全丢）

## 三、关键设计决策（改前先读注释；交接文档第五节有完整理由）
- 选点**不设门禁**：左键任何时候都能选点；单选 / 多选只是"点击语义"，不是开关
- **框选 = 选择；框选限定 = 作用域**：限定只影响"多选时同类扩散的范围"，不拦截点击命中
- **框外点击一律解除限定**（单选 / 多选 / 右键共用 `ReleaseScopeIfOutside`），规则只能有一条
- `SelectedCircle.IsSelected` 与 `_selectedCircles` 是两份状态，必须同步；清空一律走 `ClearAllSelection()`
- 画布背景**纯黑**，光圈配色按"对纯黑对比度 ≥ 4.5:1"筛过；颜色转换要按 **Halcon 语义**对齐
  （`"green"` = (0,255,0)，不是 GDI+ 的 Color.Green）
- 「保存模板」**就地保存**（有归属就存回原目录），落点必须让用户看得见（显示完整路径）
- 文件写入用 `WriteFileAtomic`（tmp + File.Replace），别退回 `WriteAllText`

## 四、文档索引
- `GerberParserV4.0_交接文档.html` —— 面向接手开发者（代码地图、数据流、设计决策、雷区）
- `GerberParserV4.0_功能说明.html` —— 面向使用者（功能清单、快捷键、数据格式）
- `Halcon替换为Korey_SmartWindow迁移说明.html` —— 去 Halcon 化改动记录
- `D:\MyWork\Common\Korey.SmartWindow.API.md` —— 自研控件库 API 契约（改画布代码前必读）
