using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace GerberParserSmartV4._0
{
    internal static class Program
    {
        /// <summary>
        /// 应用程序的主入口点。
        /// </summary>
        // 启动时"当前工程"的 Gerber 全路径（历史遗留的隐式全局状态，MainForm 之外也有读取）。
        //
        // 默认值原来是原开发者本机的一个绝对路径（形如 C:/Users/<用户名>/Desktop/<工程>/Gerber/<文件>.GBS），
        // 两宗罪：① 换台机器必然失效；② 这个字符串会进版本库，等于把本机用户名公开出去。
        //
        // 改成空串后行为反而更干净：没有任何"上次工程"记录时，程序停在"未打开工程"状态，
        // 而不是去开一个别人机器上的文件。注意下游两处都是按 string.IsNullOrEmpty 判这条状态的
        // —— 空串是它们的预期取值，不是"缺值"：
        //   · MainForm.GetProjectName()      → 空则继续往下找别的工程名来源
        //   · MainForm.OnDeleteProjectClick() → 空则报"当前没有打开的工程"
        //   · MainForm.LoadProjectFromFiles() → 载入成功后赋 layers[0].FilePath
        public static string FileName = string.Empty;
        [STAThread]
        static void Main()
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.Run(new MainForm());
        }
    }
}
