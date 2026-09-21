using System;
using System.Collections.Generic;
using System.Drawing;
using GerberParserTool;

namespace GerberParserSmartV4._0
{
    /// <summary>
    /// 形状查询：**视口裁剪** + **命中测试**。
    ///
    /// 【为什么需要它】原实现里有两个与渲染后端无关的性能问题：
    ///
    ///   1) 每帧把全部图形都交给绘制层。铜层有 15,359 个图形，而 GDI+ 每次
    ///      FillEllipse/DrawEllipse 约 2–5 µs —— 全额绘制约 30–80 ms/帧。
    ///      放大到屏幕上只看得见几百个时，仍然在为那 15,359 个发光栅化调用。
    ///      裁剪后只画可见的那些（放大时通常几百个 → 1–3 ms）。
    ///      ★ 成本上限从"全图图形数"改成"可见图形数"，这是手感的分水岭。
    ///
    ///   2) 每次鼠标点击都重建 15,359 个 SelectedCircle，且在循环内对已选集合做
    ///      O(M) 线性查找（15,359 × 3,852 ≈ 2,960 万次比较/次点击）。
    ///      改为"一次性缓存 + 单次 O(N) 扫描"（15,359 次比较，约 0.1 ms）。
    ///
    /// 可见性一律用**外接矩形相交**判定，不做精确几何 —— 绘制阶段 GDI+ 自己还会裁剪，
    /// 这里只需把明显不可见的剔除；包围盒松一点只是多画几个，不会出错。
    /// </summary>
    internal static class ShapeQuery
    {
        /// <summary>
        /// 取形状的外接半宽 / 半高（世界单位）。
        /// 旋转椭圆故意取 max(半轴) 做保守外接 —— 旋转任意角度都落在盒内，
        /// 代价只是多画一点，换来的是这里零三角函数、可安全地每帧调用。
        /// </summary>
        public static void GetHalfExtents(MainForm.SelectedCircle s, out double halfW, out double halfH)
        {
            switch (s.Shape)
            {
                case ApertureShape.Circle:
                    halfW = halfH = s.Diameter * 0.5;
                    break;

                case ApertureShape.Rectangle:
                    halfW = s.Width * 0.5;
                    halfH = s.Height * 0.5;
                    break;

                case ApertureShape.Oval:
                    halfW = halfH = Math.Max(s.Width, s.Height) * 0.5;
                    break;

                default:
                    halfW = halfH = 0;
                    break;
            }

            if (halfW < 0) halfW = 0;
            if (halfH < 0) halfH = 0;
        }

        /// <summary>图形是否与给定世界矩形相交（做视口裁剪用）。</summary>
        public static bool IsVisible(MainForm.SelectedCircle s, RectangleF view)
        {
            double halfW, halfH;
            GetHalfExtents(s, out halfW, out halfH);

            return (s.X + halfW) >= view.Left && (s.X - halfW) <= view.Right &&
                   (s.Y + halfH) >= view.Top && (s.Y - halfH) <= view.Bottom;
        }

        /// <summary>
        /// 收集与可见矩形相交的图形。返回收集到的数量。
        /// 复杂度 O(N)（N = 全图图形数），15,359 个时约 0.1–0.3 ms —— 相比省下的
        /// 绘制开销，这个扫描成本可以忽略。
        /// </summary>
        public static int CollectVisible(IList<MainForm.SelectedCircle> shapes,
                                         RectangleF view,
                                         List<MainForm.SelectedCircle> result)
        {
            result.Clear();
            if (shapes == null) return 0;

            for (int i = 0; i < shapes.Count; i++)
            {
                MainForm.SelectedCircle s = shapes[i];
                if (s == null) continue;
                if (IsVisible(s, view)) result.Add(s);
            }
            return result.Count;
        }

        /// <summary>
        /// 「本次不参与命中」的层序哨兵值。命中测试见到它就跳过该图形。
        ///
        /// 取值刻意用 int.MinValue：真实的层序是从 0 往上走的非负序号（见 HitTest 的 layerDepth 参数），
        /// 用一个**不可能与真实序号相撞**的值当"跳过"，就不必再额外传一个可见性谓词。
        /// 典型用途是"该图形所属图层被隐藏"——看不见的东西不该被点中。
        /// </summary>
        public const int SkipDepth = int.MinValue;

        /// <summary>
        /// 命中测试：返回"最该被点中"的图形。排序键依次是：
        ///   ① 图层叠放（值大者胜）→ ② 命中内部优先于"只是落在容差里" →
        ///   ③ 都在内部时**尺寸小者胜** → ④ 都在容差内时边界近者胜。
        ///
        /// 相比原实现有五点改进：
        ///   · 返回"最近的"而不是"列表里第一个"——重叠图形时选中的是视觉上最贴近光标那个；
        ///   · 支持旋转椭圆的精确命中（原实现忽略了 Rotation，注释里也承认了）；
        ///   · 支持点击容差（<paramref name="toleranceWorld"/>），小半径图形不再需要点得像素级精准；
        ///   · **支持图层叠放优先级**（<paramref name="layerDepth"/>），见下面的注释块；
        ///   · **同层内按尺寸从小往大挑**（第 ③ 档）—— 单图层场景下的唯一防线，见下。
        ///
        /// ⚠ 第 ③ 档不是锦上添花：只有一个图层时 layerDepth 全体相同，①②两档直接失效，
        /// 此时若还按"边界距离"排序，因为圆内部距离 = len−r（半径越大越负），必然大圆通吃 ——
        /// 现象就是"只有一个图层，也总是选中最大的那个圆"。
        ///
        /// 【为什么必须有 layerDepth】多图层叠加时，同一位置常压着多个图层的图形
        /// （GBL 与 GBS 就经常在同一坐标放直径不同的焊盘）。只看"点到边界距离"会**系统性偏向大图形**：
        /// 圆内部的边界距离是 <c>len - r</c>，直径越大这个值越小，于是不管点在大圆的哪个位置，
        /// 大圆都会赢过压在它上面的小图形 —— 用户看到的现象就是"只能选中最大圆圈那一层"。
        ///
        /// 用户看到的是**画在最上层**的那个图形，所以叠放优先级必须由图层顺序决定，
        /// 距离只在**同一图层内部**做次序；最上层在该处没有图形时才落到下一层（穿透），
        /// 不会出现"点了没反应"。
        /// </summary>
        /// <param name="toleranceWorld">容差（世界单位）。调用方用 viewport.ToWorldLength(4) 之类的换算给出。</param>
        /// <param name="layerDepth">
        /// 图层叠放深度：**数值越大越靠上层**（后加载的图层画在上面）。传 null 时退化为纯粹的"最近优先"
        /// （单图层、或不关心叠放时的旧口径）。
        /// 返回 <see cref="SkipDepth"/> 表示该图形本次不参与命中（例如它所属图层被隐藏）。
        /// </param>
        public static MainForm.SelectedCircle HitTest(IList<MainForm.SelectedCircle> shapes,
                                                     double x, double y,
                                                     double toleranceWorld,
                                                     Func<MainForm.SelectedCircle, int> layerDepth = null)
        {
            if (shapes == null) return null;

            MainForm.SelectedCircle best = null;
            int bestDepth = int.MinValue;
            bool bestInside = false;
            double bestSize = double.MaxValue;
            double bestDistance = double.MaxValue;

            for (int i = 0; i < shapes.Count; i++)
            {
                MainForm.SelectedCircle s = shapes[i];
                if (s == null) continue;

                int depth = layerDepth == null ? 0 : layerDepth(s);
                if (depth == SkipDepth) continue;    // 图层被隐藏等：本次不参与命中

                double d = DistanceToBoundary(s, x, y);
                if (d > toleranceWorld) continue;    // 容差外，够不着

                bool inside = d <= 0.0;              // 光标是否**落在图形内部**
                double size = AreaProxy(s);

                bool better;
                if (best == null)
                {
                    better = true;
                }
                else if (depth != bestDepth)
                {
                    // ① 叠放优先：值大者胜（后加载的图层画在上面，用户看到的就是它）
                    better = depth > bestDepth;
                }
                else if (inside != bestInside)
                {
                    // ② 同层内："命中内部"优先于"只是落在容差里"
                    better = inside;
                }
                else if (inside)
                {
                    // ③ 都在内部 → **尺寸小的胜**。
                    //    圆内部的边界距离是 len-r，半径越大越负 → 按距离比必然是大圆通吃；
                    //    而用户点的是那个**具体的**图形，所以这里必须按尺寸从小往大挑。
                    //    ⚠ 单图层时 layerDepth 全体相同，①②两档直接失效，走的就是这一档 ——
                    //      早先这里只按距离排，表现为"只有一个图层，也总是选中最大那个圆"。
                    better = (size < bestSize) || (size == bestSize && d < bestDistance);
                }
                else
                {
                    // ④ 都在容差内但没命中内部 → 边界近者胜（原来的口径）
                    better = d < bestDistance;
                }

                if (better)
                {
                    bestDepth = depth;
                    bestInside = inside;
                    bestSize = size;
                    bestDistance = d;
                    best = s;
                }
            }

            return best;
        }

        /// <summary>
        /// 图形尺寸的排序代理值 —— 只要求**单调于面积**，不必是真实面积（省掉 π 与开方）。
        /// 用途：光标同时落在多个图形内部时，挑出最**具体**的那个（最小的那个）。
        /// </summary>
        private static double AreaProxy(MainForm.SelectedCircle s)
        {
            double halfW, halfH;
            GetHalfExtents(s, out halfW, out halfH);
            return halfW * halfH;
        }

        /// <summary>
        /// 点到形状边界的近似距离：内部为负、外部为正。用于命中优先级排序。
        /// </summary>
        private static double DistanceToBoundary(MainForm.SelectedCircle s, double x, double y)
        {
            double dx = x - s.X;
            double dy = y - s.Y;

            switch (s.Shape)
            {
                case ApertureShape.Circle:
                    {
                        double r = s.Diameter * 0.5;
                        double len = Math.Sqrt(dx * dx + dy * dy);
                        return len - r;
                    }

                case ApertureShape.Rectangle:
                    {
                        // 轴对齐矩形的带符号距离：外部取欧氏距离，内部取"最深方向"的负值
                        double hw = s.Width * 0.5;
                        double hh = s.Height * 0.5;
                        double ex = Math.Abs(dx) - hw;
                        double ey = Math.Abs(dy) - hh;

                        if (ex > 0 || ey > 0)
                        {
                            double px = ex > 0 ? ex : 0;
                            double py = ey > 0 ? ey : 0;
                            return Math.Sqrt(px * px + py * py);
                        }
                        return Math.Max(ex, ey);
                    }

                case ApertureShape.Oval:
                    {
                        // 椭圆：把点旋转到椭圆局部坐标系，再用归一化半径近似换算成距离。
                        // Rotation 是角度、顺时针为正，与 GDI+ 的 RotateTransform 同向。
                        double a = s.Width * 0.5;
                        double b = s.Height * 0.5;
                        if (a <= 0 || b <= 0) return double.MaxValue;

                        double phi = s.Rotation * Math.PI / 180.0;
                        double cos = Math.Cos(phi);
                        double sin = Math.Sin(phi);

                        double u = dx * cos + dy * sin;
                        double v = -dx * sin + dy * cos;

                        double k = Math.Sqrt((u / a) * (u / a) + (v / b) * (v / b));
                        return (k - 1.0) * Math.Min(a, b);
                    }

                default:
                    return double.MaxValue;
            }
        }
    }
}
