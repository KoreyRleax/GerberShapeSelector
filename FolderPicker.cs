// FolderPicker.cs
// ---------------------------------------------------------------------------
// 现代风格的"选择文件夹"对话框。
//
// 【为什么不用 FolderBrowserDialog】
//   .NET Framework 4.8 的 FolderBrowserDialog 是 Windows 的老式树形选择器（SHBrowseForFolder）：
//   窗口小、没有地址栏、不能粘贴路径、不能搜索、左侧没有导航栏 —— 用户明确反馈难用。
//   Vista 之后系统提供了 IFileOpenDialog + FOS_PICKFOLDERS，也就是资源管理器里
//   "选择文件夹"那个现代对话框。它在 .NET Core 3.0+ 由 FolderBrowserDialog.AutoUpgradeEnabled
//   自动启用，但 **net48 没有这个属性**，只能自己调 COM。
//
// 【实现要点】
//   用 CLSID_FileOpenDialog 建对象后取 IFileDialog 接口就够 ——
//   不必声明 IFileOpenDialog：FOS_PICKFOLDERS 是 IFileDialog::SetOptions 的选项，
//   而 IFileOpenDialog 追加的 GetResults / GetSelectedItems 只有多选场景才用得上。
//
//   ⚠ 接口方法的**声明顺序必须与原生 vtable 严格一致**（下面逐条照抄 shobjidl_core.h 的顺序）：
//     COM 是靠槽位调用的，顺序错一个就是把参数喂给了别的方法 —— 表现为访问冲突（进程直接挂），
//     try/catch 拦不住。所以这段不要增、删、调序，哪怕觉得某个方法用不到。
// ---------------------------------------------------------------------------

using System;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace GerberParserSmartV4._0
{
    /// <summary>系统原生的"选择文件夹"对话框；任何异常都退回老式 FolderBrowserDialog，保证功能不缺失。</summary>
    internal static class FolderPicker
    {
        /// <summary>
        /// 弹出"选择文件夹"对话框。用户取消或失败时返回 null。
        /// </summary>
        public static string PickFolder(string title, string initialFolder)
        {
            try
            {
                var dialog = (IFileDialog)new FileOpenDialogRCW();

                // 关键：FOS_PICKFOLDERS 让"打开文件"对话框变成"选择文件夹"。
                // FOS_FORCEFILESYSTEM 保证返回的是真实文件系统路径（而不是 MTP 设备、库这类虚拟项）。
                FOS options;
                dialog.GetOptions(out options);
                dialog.SetOptions(options | FOS.FOS_PICKFOLDERS | FOS.FOS_FORCEFILESYSTEM | FOS.FOS_PATHMUSTEXIST);

                if (!string.IsNullOrEmpty(title)) dialog.SetTitle(title);

                // 起始目录（失败不影响主流程 —— 大不了从系统默认位置开始）
                if (!string.IsNullOrEmpty(initialFolder))
                {
                    Guid iid = typeof(IShellItem).GUID;
                    IShellItem startItem;
                    if (SHCreateItemFromParsingName(initialFolder, IntPtr.Zero, ref iid, out startItem) == 0
                        && startItem != null)
                    {
                        try { dialog.SetFolder(startItem); }
                        finally { Marshal.ReleaseComObject(startItem); }
                    }
                }

                int hr = dialog.Show(IntPtr.Zero);
                if (hr != 0) return null;        // 0x800704C7 = 用户点了取消

                IShellItem result;
                dialog.GetResult(out result);
                if (result == null) return null;

                try
                {
                    string path;
                    result.GetDisplayName(SIGDN.SIGDN_FILESYSPATH, out path);
                    return path;
                }
                finally
                {
                    Marshal.ReleaseComObject(result);
                }
            }
            catch (Exception)
            {
                // COM 不可用（极老的系统 / 被安全软件拦）时退回老式选择器，功能不缺失
                using (var fallback = new FolderBrowserDialog())
                {
                    fallback.Description = title;
                    if (!string.IsNullOrEmpty(initialFolder) && System.IO.Directory.Exists(initialFolder))
                    {
                        fallback.SelectedPath = initialFolder;
                    }
                    return fallback.ShowDialog() == DialogResult.OK ? fallback.SelectedPath : null;
                }
            }
        }

        [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
        private static extern int SHCreateItemFromParsingName(
            [MarshalAs(UnmanagedType.LPWStr)] string pszPath,
            IntPtr pbc,
            ref Guid riid,
            [MarshalAs(UnmanagedType.Interface)] out IShellItem ppv);

        // ── COM 接口（顺序即 vtable 顺序，不要改动） ──

        [ComImport, Guid("42f85136-db7e-439c-85f1-e4075d135fc8"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IFileDialog
        {
            [PreserveSig] int Show(IntPtr parent);

            void SetFileTypes(uint cFileTypes, IntPtr rgFilterSpec);
            void SetFileTypeIndex(uint iFileType);
            void GetFileTypeIndex(out uint piFileType);
            void Advise(IntPtr pfde, out uint pdwCookie);
            void Unadvise(uint dwCookie);
            void SetOptions(FOS fos);
            void GetOptions(out FOS pfos);
            void SetDefaultFolder(IShellItem psi);
            void SetFolder(IShellItem psi);
            void GetFolder(out IShellItem ppsi);
            void GetCurrentSelection(out IShellItem ppsi);
            void SetFileName([MarshalAs(UnmanagedType.LPWStr)] string pszName);
            void GetFileName([MarshalAs(UnmanagedType.LPWStr)] out string pszName);
            void SetTitle([MarshalAs(UnmanagedType.LPWStr)] string pszTitle);
            void SetOkButtonLabel([MarshalAs(UnmanagedType.LPWStr)] string pszText);
            void SetFileNameLabel([MarshalAs(UnmanagedType.LPWStr)] string pszLabel);
            void GetResult(out IShellItem ppsi);
            void AddPlace(IShellItem psi, int fdap);
            void SetDefaultExtension([MarshalAs(UnmanagedType.LPWStr)] string pszDefaultExtension);
            void Close(int hr);
            void SetClientGuid(ref Guid guid);
            void ClearClientData();
            void SetFilter(IntPtr pFilter);
            void GetResults(IntPtr ppenum);
            void GetSelectedItems(IntPtr ppsai);
        }

        [ComImport, Guid("43826d1e-e718-42ee-bc55-a1e261c37bfe"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IShellItem
        {
            void BindToHandler(IntPtr pbc, ref Guid bhid, ref Guid riid, out IntPtr ppv);
            void GetParent(out IShellItem ppsi);
            void GetDisplayName(SIGDN sigdnName, [MarshalAs(UnmanagedType.LPWStr)] out string ppszName);
            void GetAttributes(uint sfgaoMask, out uint psfgaoAttribs);
            void Compare(IShellItem psi, uint hint, out int piOrder);
        }

        /// <summary>CLSID_FileOpenDialog 的 coclass。取 IFileDialog 接口即可用于选文件夹。</summary>
        [ComImport, Guid("DC1C5A9C-E88A-4dde-A5A1-60F82A20AEF7")]
        private class FileOpenDialogRCW
        {
        }

        [Flags]
        private enum FOS : uint
        {
            FOS_OVERWRITEPROMPT = 0x00000002,
            FOS_STRICTFILETYPES = 0x00000004,
            FOS_NOCHANGEDIR = 0x00000008,
            FOS_PICKFOLDERS = 0x00000020,
            FOS_FORCEFILESYSTEM = 0x00000040,
            FOS_ALLNONSTORAGEITEMS = 0x00000080,
            FOS_NOVALIDATE = 0x00000100,
            FOS_ALLOWMULTISELECT = 0x00000200,
            FOS_PATHMUSTEXIST = 0x00000800,
            FOS_FILEMUSTEXIST = 0x00001000,
            FOS_CREATEPROMPT = 0x00002000,
            FOS_SHAREAWARE = 0x00004000,
            FOS_NODEREFERENCELINKS = 0x00100000,
            FOS_DONTADDTORECENT = 0x02000000,
            FOS_FORCESHOWHIDDEN = 0x10000000,
            FOS_DEFAULTNOMINIMODE = 0x20000000
        }

        private enum SIGDN : uint
        {
            SIGDN_NORMALDISPLAY = 0x00000000,
            SIGDN_FILESYSPATH = 0x80058000
        }
    }
}
