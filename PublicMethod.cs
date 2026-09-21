using GerberParserTool;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;
using static GerberParserSmartV4._0.MainForm;

namespace GerberParserSmartV4._0
{
    public static class PublicMethod
    {
        // 注：原 SetHalconViewport(HSmartWindowControl) 已删除 ——
        // 视口现在由 Korey.SmartWindow 的 KViewport 自持（唯一数据源），
        // 而且这个方法全工程没有任何调用点（死代码）。

        public static List<Aperture> CircleConvertToApertures(List<SelectedCircle> circles)
        {
            var apertures = new List<Aperture>();
            if (circles == null) return apertures;

            var aperture = new Aperture("Template", 0);
            foreach (var circle in circles)
            {
                aperture.AddPosition(circle.X, circle.Y);
                // 如果直径不同，创建新的光圈
                if (aperture.Diameter == 0)
                {
                    aperture.Diameter = circle.Diameter;
                }
                else if (aperture.Diameter != circle.Diameter)
                {
                    apertures.Add(aperture);
                    aperture = new Aperture("Template", circle.Diameter);
                    aperture.AddPosition(circle.X, circle.Y);
                }
            }
            if (aperture.Position.Count > 0)
            {
                apertures.Add(aperture);
            }
            return apertures;
        }

        public static void LogCircles(List<SelectedCircle> circles, string title)
        {
            KLog.Info($"=== {title} ===");
            KLog.Info($"圆圈数量: {circles.Count}");

            if (circles.Count > 0)
            {
                KLog.Info("圆圈坐标信息:");
                KLog.Info("X坐标\t\tY坐标\t\t直径");
                KLog.Info("----------------------------------------");
                foreach (var circle in circles)
                {
                    KLog.Info($"{circle.X:F4}\t\t{circle.Y:F4}\t\t{circle.Diameter:F4}");
                }
            }
            else
            {
                KLog.Info("没有圆圈数据");
            }
            KLog.Info("========================================");
        }
        public static void LogTemplateInfo(string folderName, List<SelectedCircle> circles, string operation)
        {
            KLog.Info($"=== 模板{operation}信息 ===");
            KLog.Info($"模板文件夹: {folderName}");
            KLog.Info($"{operation}圆圈数量: {circles.Count}");
            KLog.Info($"选中圆圈数量: {circles.Count(c => c.IsSelected)}");
            KLog.Info($"操作时间: {DateTime.Now}");
            KLog.Info("========================================");
        }

    }
}