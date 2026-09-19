using System;
using System.IO;
using System.Text;
using System.Windows.Forms;

namespace GerberParserSmartV4._0
{
    /// <summary>
    /// 极简文件日志。
    ///
    /// 【为什么需要它】本工程 OutputType=WinExe，进程没有控制台，原来遍布全工程的
    /// Console.WriteLine 输出（含解析统计、绘制异常、选点异常）**全部被丢弃**——
    /// 出了问题是零线索。这里把它们落到 exe 同目录的 logs\yyyy-MM-dd.log。
    ///
    /// 设计约定：
    ///   · 日志失败一律静默（不能因为写日志失败而影响主流程），失败后不再重试；
    ///   · 每次写入独立打开/关闭文件，避免进程被强杀时丢缓冲；
    ///   · 线程安全（lock）。
    /// </summary>
    internal static class KLog
    {
        private static readonly object _sync = new object();
        private static string _dir;
        private static bool _disabled;

        private static string Dir
        {
            get
            {
                if (_dir == null)
                {
                    try { _dir = Path.Combine(Application.StartupPath, "logs"); }
                    catch { _dir = "."; }
                }
                return _dir;
            }
        }

        public static void Info(string message) { Write("INFO ", message, null); }
        public static void Warn(string message) { Write("WARN ", message, null); }
        public static void Error(string message) { Write("ERROR", message, null); }
        public static void Error(string message, Exception ex) { Write("ERROR", message, ex); }

        private static void Write(string level, string message, Exception ex)
        {
            if (_disabled) return;

            lock (_sync)
            {
                try
                {
                    if (!Directory.Exists(Dir)) Directory.CreateDirectory(Dir);

                    string file = Path.Combine(Dir, DateTime.Now.ToString("yyyy-MM-dd") + ".log");

                    var sb = new StringBuilder();
                    sb.Append(DateTime.Now.ToString("HH:mm:ss.fff"))
                      .Append(" [").Append(level).Append("] ")
                      .Append(message ?? string.Empty);

                    if (ex != null)
                    {
                        sb.Append(Environment.NewLine)
                          .Append("        ")
                          .Append(ex.GetType().Name)
                          .Append(": ")
                          .Append(ex.Message);

                        if (!string.IsNullOrEmpty(ex.StackTrace))
                        {
                            sb.Append(Environment.NewLine).Append(ex.StackTrace);
                        }
                    }

                    sb.Append(Environment.NewLine);
                    File.AppendAllText(file, sb.ToString(), Encoding.UTF8);
                }
                catch
                {
                    // 日志是辅助能力，失败不影响业务；关掉避免每条日志都抛一次异常
                    _disabled = true;
                }
            }
        }
    }
}
