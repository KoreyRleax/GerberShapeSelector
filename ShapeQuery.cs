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
        /// 命中测试：返回离点击位置**最近**、且距离不超过 <paramref name="toleranceWorld"/> 的图形。
        ///
        /// 相比原实现有三点改进：
        ///   · 返回"最近的"而不是"列表里第一个"——重叠图形时选中的是视觉上最贴近光标那个；
        ///   · 支持旋转椭圆的精确命中（原实现忽略了 Rotation，注释里也承认了）；
        ///   · 支持点击容差（<paramref name="toleranceWorld"/>），小半径图形不再需要点得像素级精准。
        /// </summary>
        /// <param name="toleranceWorld">容差（世界单位）。调用方用 viewport.ToWorldLength(4) 之类的换算给出。</param>
        public static MainForm.SelectedCircle HitTest(IList<MainForm.SelectedCircle> shapes,
                                                     double x, double y,
                                                     double toleranceWorld)
        {
            if (shapes == null) return null;

            MainForm.SelectedCircle best = null;
            double bestDistance = double.MaxValue;

            for (int i = 0; i < shapes.Count; i++)
            {
                MainForm.SelectedCircle s = shapes[i];
                if (s == null) continue;

                double d = DistanceToBoundary(s, x, y);
                if (d <= toleranceWorld && d < bestDistance)
                {
                    bestDistance = d;
                    best = s;
                }
            }

            return best;
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
