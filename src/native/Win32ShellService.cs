using System;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;
using System.Text;
using System.Windows.Interop;
using System.Windows.Media.Imaging;

namespace DrawerNative
{
    public class ResolvedShortcutInfo
    {
        public string SourcePath;
        public string TargetPath;
        public string Arguments;
        public string WorkingDirectory;
        public string IconPath;
        public int IconIndex;
        public string Url;
    }

    public static class Win32ShellService
    {
        public const int GWL_STYLE = -16;
        public const int GWL_EXSTYLE = -20;

        public const long WS_EX_TOPMOST = 0x00000008L;
        public const long WS_EX_TRANSPARENT = 0x00000020L;
        public const long WS_EX_TOOLWINDOW = 0x00000080L;
        public const long WS_EX_APPWINDOW = 0x00040000L;
        public const long WS_EX_LAYERED = 0x00080000L;
        public const long WS_EX_NOACTIVATE = 0x08000000L;

        public static readonly IntPtr HWND_TOP = new IntPtr(0);
        public static readonly IntPtr HWND_BOTTOM = new IntPtr(1);
        public static readonly IntPtr HWND_TOPMOST = new IntPtr(-1);
        public static readonly IntPtr HWND_NOTOPMOST = new IntPtr(-2);

        public const uint GW_HWNDPREV = 3;

        public const uint SWP_NOSIZE = 0x0001;
        public const uint SWP_NOMOVE = 0x0002;
        public const uint SWP_NOZORDER = 0x0004;
        public const uint SWP_NOACTIVATE = 0x0010;
        public const uint SWP_SHOWWINDOW = 0x0040;
        public const uint SWP_NOSENDCHANGING = 0x0400;

        public const int SW_SHOWNORMAL = 1;
        public const int SW_SHOWNOACTIVATE = 4;
        public const int SW_RESTORE = 9;

        private const int DWMWA_CLOAKED = 14;

        private const int SHCNE_CREATE = 0x00000002;
        private const int SHCNE_UPDATEDIR = 0x00001000;
        private const uint SHCNF_PATHW = 0x0005;
        private const uint SHCNF_FLUSH = 0x1000;

        private const uint CMF_NORMAL = 0x00000000;
        private const uint CMF_EXPLORE = 0x00000004;

        private const uint MF_STRING = 0x00000000;
        private const uint MF_SEPARATOR = 0x00000800;
        private const uint MF_BYPOSITION = 0x00000400;

        private const uint TPM_RETURNCMD = 0x0100;
        private const uint TPM_RIGHTBUTTON = 0x0002;
        private const uint TPM_LEFTALIGN = 0x0000;

        private const int WM_INITMENUPOPUP = 0x0117;
        private const int WM_DRAWITEM = 0x002B;
        private const int WM_MEASUREITEM = 0x002C;
        private const int WM_MENUCHAR = 0x0120;
        public const int WM_HOTKEY = 0x0312;

        public const uint MOD_ALT = 0x0001;
        public const uint MOD_CONTROL = 0x0002;
        public const uint MOD_SHIFT = 0x0004;
        public const uint VK_D = 0x44;

        [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")]
        public static extern IntPtr GetWindowLongPtr(IntPtr hWnd, int nIndex);

        [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")]
        public static extern IntPtr SetWindowLongPtr(IntPtr hWnd, int nIndex, IntPtr dwNewLong);

        [DllImport("user32.dll", EntryPoint = "FindWindowW", CharSet = CharSet.Unicode)]
        public static extern IntPtr FindWindow(string lpClassName, string lpWindowName);

        [DllImport("user32.dll", EntryPoint = "FindWindowExW", CharSet = CharSet.Unicode)]
        public static extern IntPtr FindWindowEx(IntPtr hWndParent, IntPtr hWndChildAfter, string lpszClass, string lpszWindow);

        [DllImport("user32.dll")]
        public static extern IntPtr GetWindow(IntPtr hWnd, uint uCmd);

        [DllImport("user32.dll")]
        public static extern bool IsWindow(IntPtr hWnd);

        [DllImport("user32.dll")]
        public static extern bool IsWindowVisible(IntPtr hWnd);

        [DllImport("user32.dll")]
        public static extern bool IsIconic(IntPtr hWnd);

        [DllImport("user32.dll")]
        public static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

        [DllImport("user32.dll", EntryPoint = "GetClassNameW", CharSet = CharSet.Unicode)]
        public static extern int GetClassName(IntPtr hWnd, StringBuilder lpClassName, int nMaxCount);

        [DllImport("user32.dll")]
        public static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);

        [DllImport("user32.dll")]
        public static extern bool GetCursorPos(out POINT lpPoint);

        [DllImport("user32.dll")]
        public static extern bool SetForegroundWindow(IntPtr hWnd);

        [DllImport("user32.dll")]
        public static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

        [DllImport("user32.dll")]
        public static extern bool UnregisterHotKey(IntPtr hWnd, int id);

        [DllImport("user32.dll")]
        public static extern bool SetWindowPos(
            IntPtr hWnd,
            IntPtr hWndInsertAfter,
            int X,
            int Y,
            int cx,
            int cy,
            uint uFlags);

        [DllImport("user32.dll")]
        private static extern IntPtr CreatePopupMenu();

        [DllImport("user32.dll")]
        private static extern bool DestroyMenu(IntPtr hMenu);

        [DllImport("user32.dll", CharSet = CharSet.Unicode, EntryPoint = "InsertMenuW")]
        private static extern bool InsertMenu(IntPtr hMenu, uint uPosition, uint uFlags, UIntPtr uIDNewItem, string lpNewItem);

        [DllImport("user32.dll")]
        private static extern int TrackPopupMenuEx(IntPtr hmenu, uint fuFlags, int x, int y, IntPtr hwnd, IntPtr lptpm);

        [DllImport("dwmapi.dll")]
        private static extern int DwmGetWindowAttribute(IntPtr hwnd, int dwAttribute, out int pvAttribute, int cbAttribute);

        [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
        private static extern void SHChangeNotify(int wEventId, uint uFlags, string dwItem1, string dwItem2);

        [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
        private static extern int SHParseDisplayName(
            [MarshalAs(UnmanagedType.LPWStr)] string pszName,
            IntPtr pbc,
            out IntPtr ppidl,
            uint sfgaoIn,
            out uint psfgaoOut);

        [DllImport("shell32.dll")]
        private static extern int SHBindToParent(
            IntPtr pidl,
            [In] ref Guid riid,
            [MarshalAs(UnmanagedType.Interface)] out IShellFolder ppv,
            out IntPtr ppidlLast);

        [DllImport("shell32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern bool ShellExecuteExW(ref SHELLEXECUTEINFO lpExecInfo);

        [DllImport("ole32.dll")]
        private static extern void CoTaskMemFree(IntPtr pv);

        [DllImport("gdi32.dll")]
        private static extern int GetObject(IntPtr hgdiobj, int cbBuffer, out BITMAP lpvObject);

        [DllImport("gdi32.dll")]
        private static extern bool DeleteObject(IntPtr hObject);

        [StructLayout(LayoutKind.Sequential)]
        public struct POINT
        {
            public int X;
            public int Y;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct RECT
        {
            public int Left;
            public int Top;
            public int Right;
            public int Bottom;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct SIZE
        {
            public int cx;
            public int cy;
            public SIZE(int cx, int cy)
            {
                this.cx = cx;
                this.cy = cy;
            }
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct BITMAP
        {
            public int bmType;
            public int bmWidth;
            public int bmHeight;
            public int bmWidthBytes;
            public ushort bmPlanes;
            public ushort bmBitsPixel;
            public IntPtr bmBits;
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct SHELLEXECUTEINFO
        {
            public int cbSize;
            public uint fMask;
            public IntPtr hwnd;
            [MarshalAs(UnmanagedType.LPWStr)]
            public string lpVerb;
            [MarshalAs(UnmanagedType.LPWStr)]
            public string lpFile;
            [MarshalAs(UnmanagedType.LPWStr)]
            public string lpParameters;
            [MarshalAs(UnmanagedType.LPWStr)]
            public string lpDirectory;
            public int nShow;
            public IntPtr hInstApp;
            public IntPtr lpIDList;
            [MarshalAs(UnmanagedType.LPWStr)]
            public string lpClass;
            public IntPtr hkeyClass;
            public uint dwHotKey;
            public IntPtr hIcon;
            public IntPtr hProcess;
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Ansi)]
        private struct CMINVOKECOMMANDINFO
        {
            public int cbSize;
            public uint fMask;
            public IntPtr hwnd;
            public IntPtr lpVerb;
            [MarshalAs(UnmanagedType.LPStr)]
            public string lpParameters;
            [MarshalAs(UnmanagedType.LPStr)]
            public string lpDirectory;
            public int nShow;
            public uint dwHotKey;
            public IntPtr hIcon;
        }

        [Flags]
        private enum SIIGBF
        {
            SIIGBF_RESIZETOFIT = 0x00,
            SIIGBF_BIGGERSIZEOK = 0x01,
            SIIGBF_MEMORYONLY = 0x02,
            SIIGBF_ICONONLY = 0x04,
            SIIGBF_THUMBNAILONLY = 0x08,
            SIIGBF_INCACHEONLY = 0x10,
        }

        [ComImport]
        [Guid("bcc18b79-ba16-442f-80c4-8a59c30c463b")]
        [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IShellItemImageFactory
        {
            [PreserveSig]
            int GetImage([In, MarshalAs(UnmanagedType.Struct)] SIZE size, [In] SIIGBF flags, out IntPtr phbm);
        }

        [DllImport("shell32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern int SHCreateItemFromParsingName(
            [MarshalAs(UnmanagedType.LPWStr)] string pszPath,
            IntPtr pbc,
            ref Guid riid,
            [MarshalAs(UnmanagedType.Interface)] out IShellItemImageFactory ppv);

        [ComImport]
        [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        [Guid("000214E6-0000-0000-C000-000000000046")]
        private interface IShellFolder
        {
            [PreserveSig]
            int ParseDisplayName(IntPtr hwnd, IntPtr pbc, [MarshalAs(UnmanagedType.LPWStr)] string pszDisplayName, out uint pchEaten, out IntPtr ppidl, ref uint pdwAttributes);
            [PreserveSig]
            int EnumObjects(IntPtr hwnd, uint grfFlags, out IntPtr ppenumIDList);
            [PreserveSig]
            int BindToObject(IntPtr pidl, IntPtr pbc, [In] ref Guid riid, out IntPtr ppv);
            [PreserveSig]
            int BindToStorage(IntPtr pidl, IntPtr pbc, [In] ref Guid riid, out IntPtr ppv);
            [PreserveSig]
            int CompareIDs(IntPtr lParam, IntPtr pidl1, IntPtr pidl2);
            [PreserveSig]
            int CreateViewObject(IntPtr hwndOwner, [In] ref Guid riid, out IntPtr ppv);
            [PreserveSig]
            int GetAttributesOf(uint cidl, [MarshalAs(UnmanagedType.LPArray, SizeParamIndex = 0)] IntPtr[] apidl, ref uint rgfInOut);
            [PreserveSig]
            int GetUIObjectOf(IntPtr hwndOwner, uint cidl, [MarshalAs(UnmanagedType.LPArray, SizeParamIndex = 1)] IntPtr[] apidl, [In] ref Guid riid, IntPtr rgflReserved, out IntPtr ppv);
            [PreserveSig]
            int GetDisplayNameOf(IntPtr pidl, uint uFlags, out IntPtr pName);
            [PreserveSig]
            int SetNameOf(IntPtr hwnd, IntPtr pidl, [MarshalAs(UnmanagedType.LPWStr)] string pszName, uint uFlags, out IntPtr ppidlOut);
        }

        [ComImport]
        [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        [Guid("000214e4-0000-0000-c000-000000000046")]
        private interface IContextMenu
        {
            [PreserveSig]
            int QueryContextMenu(IntPtr hMenu, uint indexMenu, uint idCmdFirst, uint idCmdLast, uint uFlags);
            [PreserveSig]
            int InvokeCommand(ref CMINVOKECOMMANDINFO pici);
            [PreserveSig]
            int GetCommandString(UIntPtr idCmd, uint uType, IntPtr pReserved, StringBuilder pszName, uint cchMax);
        }

        [ComImport]
        [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        [Guid("000214f4-0000-0000-c000-000000000046")]
        private interface IContextMenu2
        {
            [PreserveSig]
            int QueryContextMenu(IntPtr hMenu, uint indexMenu, uint idCmdFirst, uint idCmdLast, uint uFlags);
            [PreserveSig]
            int InvokeCommand(ref CMINVOKECOMMANDINFO pici);
            [PreserveSig]
            int GetCommandString(UIntPtr idCmd, uint uType, IntPtr pReserved, StringBuilder pszName, uint cchMax);
            [PreserveSig]
            int HandleMenuMsg(uint uMsg, IntPtr wParam, IntPtr lParam);
        }

        [ComImport]
        [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        [Guid("bcfce0a0-ec17-11d0-8d10-00a0c90f2719")]
        private interface IContextMenu3
        {
            [PreserveSig]
            int QueryContextMenu(IntPtr hMenu, uint indexMenu, uint idCmdFirst, uint idCmdLast, uint uFlags);
            [PreserveSig]
            int InvokeCommand(ref CMINVOKECOMMANDINFO pici);
            [PreserveSig]
            int GetCommandString(UIntPtr idCmd, uint uType, IntPtr pReserved, StringBuilder pszName, uint cchMax);
            [PreserveSig]
            int HandleMenuMsg(uint uMsg, IntPtr wParam, IntPtr lParam);
            [PreserveSig]
            int HandleMenuMsg2(uint uMsg, IntPtr wParam, IntPtr lParam, out IntPtr plResult);
        }

        [ComImport]
        [Guid("00021401-0000-0000-C000-000000000046")]
        private class ShellLinkCoClass
        {
        }

        [ComImport]
        [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        [Guid("000214F9-0000-0000-C000-000000000046")]
        private interface IShellLinkW
        {
            void GetPath([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszFile, int cchMaxPath, IntPtr pfd, uint fFlags);
            void GetIDList(out IntPtr ppidl);
            void SetIDList(IntPtr pidl);
            void GetDescription([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszName, int cchMaxName);
            void SetDescription([MarshalAs(UnmanagedType.LPWStr)] string pszName);
            void GetWorkingDirectory([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszDir, int cchMaxPath);
            void SetWorkingDirectory([MarshalAs(UnmanagedType.LPWStr)] string pszDir);
            void GetArguments([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszArgs, int cchMaxPath);
            void SetArguments([MarshalAs(UnmanagedType.LPWStr)] string pszArgs);
            void GetHotkey(out short pwHotkey);
            void SetHotkey(short wHotkey);
            void GetShowCmd(out int piShowCmd);
            void SetShowCmd(int iShowCmd);
            void GetIconLocation([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszIconPath, int cchIconPath, out int piIcon);
            void SetIconLocation([MarshalAs(UnmanagedType.LPWStr)] string pszIconPath, int iIcon);
            void SetRelativePath([MarshalAs(UnmanagedType.LPWStr)] string pszPathRel, uint dwReserved);
            void Resolve(IntPtr hwnd, uint fFlags);
            void SetPath([MarshalAs(UnmanagedType.LPWStr)] string pszFile);
        }

        public static void NotifyDesktopFileRestored(string filePath)
        {
            if (string.IsNullOrEmpty(filePath)) return;
            try
            {
                string fullPath = Path.GetFullPath(filePath);
                SHChangeNotify(SHCNE_CREATE, SHCNF_PATHW | SHCNF_FLUSH, fullPath, null);
                string dir = Path.GetDirectoryName(fullPath);
                if (!string.IsNullOrEmpty(dir))
                {
                    SHChangeNotify(SHCNE_UPDATEDIR, SHCNF_PATHW | SHCNF_FLUSH, dir, null);
                }
            }
            catch
            {
                // Ignore
            }
        }

        public static ResolvedShortcutInfo ResolveShortcutDetails(string filePath)
        {
            var info = new ResolvedShortcutInfo
            {
                SourcePath = filePath ?? "",
                TargetPath = "",
                Arguments = "",
                WorkingDirectory = "",
                IconPath = "",
                IconIndex = 0,
                Url = ""
            };

            if (string.IsNullOrEmpty(filePath) || !File.Exists(filePath))
                return info;

            string ext = Path.GetExtension(filePath).ToLowerInvariant();
            if (ext == ".url")
            {
                try
                {
                    foreach (string rawLine in File.ReadAllLines(filePath))
                    {
                        string line = rawLine.Trim();
                        if (string.IsNullOrEmpty(line) || line.StartsWith(";") || line.StartsWith("[")) continue;
                        int eq = line.IndexOf('=');
                        if (eq <= 0) continue;
                        string key = line.Substring(0, eq).Trim().ToLowerInvariant();
                        string val = Environment.ExpandEnvironmentVariables(line.Substring(eq + 1).Trim().Trim('"'));

                        if (key == "url") info.Url = val;
                        else if (key == "iconfile") info.IconPath = val;
                        else if (key == "iconindex")
                        {
                            int idx;
                            if (int.TryParse(val, out idx)) info.IconIndex = idx;
                        }
                    }
                }
                catch
                {
                    // Ignore
                }
                return info;
            }

            if (ext == ".lnk")
            {
                try
                {
                    var link = (IShellLinkW)new ShellLinkCoClass();
                    var persist = (IPersistFile)link;
                    persist.Load(filePath, 0);

                    var sbTarget = new StringBuilder(1024);
                    link.GetPath(sbTarget, sbTarget.Capacity, IntPtr.Zero, 0);
                    info.TargetPath = Environment.ExpandEnvironmentVariables(sbTarget.ToString().Trim());

                    var sbArgs = new StringBuilder(2048);
                    link.GetArguments(sbArgs, sbArgs.Capacity);
                    info.Arguments = Environment.ExpandEnvironmentVariables(sbArgs.ToString().Trim());

                    var sbWorkDir = new StringBuilder(1024);
                    link.GetWorkingDirectory(sbWorkDir, sbWorkDir.Capacity);
                    info.WorkingDirectory = Environment.ExpandEnvironmentVariables(sbWorkDir.ToString().Trim());

                    var sbIcon = new StringBuilder(1024);
                    int iconIdx;
                    link.GetIconLocation(sbIcon, sbIcon.Capacity, out iconIdx);
                    info.IconPath = Environment.ExpandEnvironmentVariables(sbIcon.ToString().Trim());
                    info.IconIndex = iconIdx;
                }
                catch
                {
                    // Ignore COM errors on special shell links
                }
            }

            return info;
        }

        public static bool OpenItemFaithfully(DrawerItem item, out string errorMessage)
        {
            errorMessage = null;
            if (item == null)
            {
                errorMessage = "Item inválido.";
                return false;
            }

            string target = item.targetPath;
            if ((string.IsNullOrEmpty(target) || (!File.Exists(target) && !Directory.Exists(target))) &&
                !string.IsNullOrEmpty(item.originalDesktopPath) &&
                (File.Exists(item.originalDesktopPath) || Directory.Exists(item.originalDesktopPath)))
            {
                target = item.originalDesktopPath;
            }

            if (string.IsNullOrEmpty(target) || (!File.Exists(target) && !Directory.Exists(target)))
            {
                errorMessage = "Arquivo ou atalho não encontrado.";
                return false;
            }

            string ext = Path.GetExtension(target).ToLowerInvariant();
            ResolvedShortcutInfo details = ResolveShortcutDetails(target);

            // 1. Internet / Steam / Epic Shortcuts (.url)
            if (ext == ".url" && !string.IsNullOrEmpty(details.Url))
            {
                try
                {
                    Process.Start(new ProcessStartInfo(details.Url) { UseShellExecute = true });
                    return true;
                }
                catch (Exception ex)
                {
                    errorMessage = ex.Message;
                    return false;
                }
            }

            // 2. Windows Shortcuts (.lnk) — preserve Arguments (e.g. Update.exe --processStart Discord.exe) and WorkingDirectory!
            if (ext == ".lnk")
            {
                string workDir = !string.IsNullOrEmpty(details.WorkingDirectory) && Directory.Exists(details.WorkingDirectory)
                    ? details.WorkingDirectory
                    : (!string.IsNullOrEmpty(details.TargetPath) && File.Exists(details.TargetPath)
                        ? Path.GetDirectoryName(details.TargetPath)
                        : Path.GetDirectoryName(target));

                // First invoke the .lnk directly via ShellExecuteExW so Windows Shell handles UWP/AppUserModelID/Elevation/Arguments natively
                var sei = new SHELLEXECUTEINFO();
                sei.cbSize = Marshal.SizeOf(typeof(SHELLEXECUTEINFO));
                sei.fMask = 0x0000000C; // SEE_MASK_INVOKEIDLIST
                sei.lpVerb = null; // default action
                sei.lpFile = target;
                sei.lpDirectory = workDir;
                sei.nShow = SW_SHOWNORMAL;

                if (ShellExecuteExW(ref sei))
                {
                    return true;
                }

                // Fallback: if the .lnk had an explicit target + arguments (e.g. Squirrel launchers), launch target with arguments
                if (!string.IsNullOrEmpty(details.TargetPath) && File.Exists(details.TargetPath))
                {
                    try
                    {
                        var psi = new ProcessStartInfo
                        {
                            FileName = details.TargetPath,
                            Arguments = details.Arguments ?? "",
                            WorkingDirectory = workDir ?? Path.GetDirectoryName(details.TargetPath),
                            UseShellExecute = true
                        };
                        Process.Start(psi);
                        return true;
                    }
                    catch (Exception ex)
                    {
                        errorMessage = ex.Message;
                        return false;
                    }
                }
            }

            // 3. Standard executable, file, or folder
            try
            {
                string workDir = Directory.Exists(target) ? target : Path.GetDirectoryName(target);
                var sei = new SHELLEXECUTEINFO();
                sei.cbSize = Marshal.SizeOf(typeof(SHELLEXECUTEINFO));
                sei.fMask = 0x0000000C; // SEE_MASK_INVOKEIDLIST
                sei.lpVerb = null;
                sei.lpFile = target;
                sei.lpDirectory = workDir;
                sei.nShow = SW_SHOWNORMAL;

                if (ShellExecuteExW(ref sei))
                {
                    return true;
                }

                Process.Start(new ProcessStartInfo(target)
                {
                    WorkingDirectory = workDir ?? "",
                    UseShellExecute = true
                });
                return true;
            }
            catch (Exception ex)
            {
                errorMessage = ex.Message;
                return false;
            }
        }

        private static IContextMenu2 _activeCtxMenu2;
        private static IContextMenu3 _activeCtxMenu3;

        private static IntPtr ContextMenuWndProcHook(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            if (msg == WM_INITMENUPOPUP || msg == WM_DRAWITEM || msg == WM_MEASUREITEM)
            {
                if (_activeCtxMenu3 != null)
                {
                    IntPtr res;
                    if (_activeCtxMenu3.HandleMenuMsg2((uint)msg, wParam, lParam, out res) == 0)
                    {
                        handled = true;
                        return res;
                    }
                }
                if (_activeCtxMenu2 != null)
                {
                    if (_activeCtxMenu2.HandleMenuMsg((uint)msg, wParam, lParam) == 0)
                    {
                        handled = true;
                        return IntPtr.Zero;
                    }
                }
            }
            else if (msg == WM_MENUCHAR && _activeCtxMenu3 != null)
            {
                IntPtr res;
                if (_activeCtxMenu3.HandleMenuMsg2((uint)msg, wParam, lParam, out res) == 0)
                {
                    handled = true;
                    return res;
                }
            }
            return IntPtr.Zero;
        }

        /// <summary>
        /// Displays the real Windows Shell Context Menu (IContextMenu / IContextMenu3) for filePath,
        /// injecting the Drawer's own quick actions at the top of the native popup menu.
        /// </summary>
        public static bool ShowNativeShellContextMenu(
            IntPtr ownerHwnd,
            string filePath,
            string itemDisplayName,
            int screenX,
            int screenY,
            Action onOpenInDrawer,
            Action onRenameInDrawer,
            Action onRestoreToDesktop)
        {
            if (ownerHwnd == IntPtr.Zero || string.IsNullOrEmpty(filePath) || (!File.Exists(filePath) && !Directory.Exists(filePath)))
                return false;

            IntPtr pidlFull = IntPtr.Zero;
            IntPtr hMenu = IntPtr.Zero;
            IShellFolder parentFolder = null;
            IntPtr pCtxMenu = IntPtr.Zero;
            IContextMenu ctxMenu = null;
            HwndSource hwndSource = HwndSource.FromHwnd(ownerHwnd);
            HwndSourceHook hook = new HwndSourceHook(ContextMenuWndProcHook);

            const uint ID_DRAWER_OPEN = 1;
            const uint ID_DRAWER_RENAME = 2;
            const uint ID_DRAWER_RESTORE = 3;
            const uint ID_SHELL_FIRST = 0x0100;
            const uint ID_SHELL_LAST = 0x7FFF;

            try
            {
                uint sfgao;
                int hr = SHParseDisplayName(Path.GetFullPath(filePath), IntPtr.Zero, out pidlFull, 0, out sfgao);
                if (hr != 0 || pidlFull == IntPtr.Zero) return false;

                Guid iidShellFolder = new Guid("000214E6-0000-0000-C000-000000000046");
                IntPtr pidlChild;
                hr = SHBindToParent(pidlFull, ref iidShellFolder, out parentFolder, out pidlChild);
                if (hr != 0 || parentFolder == null || pidlChild == IntPtr.Zero) return false;

                Guid iidContextMenu = new Guid("000214e4-0000-0000-c000-000000000046");
                IntPtr[] apidl = new IntPtr[] { pidlChild };
                hr = parentFolder.GetUIObjectOf(ownerHwnd, 1, apidl, ref iidContextMenu, IntPtr.Zero, out pCtxMenu);
                if (hr != 0 || pCtxMenu == IntPtr.Zero) return false;

                ctxMenu = (IContextMenu)Marshal.GetObjectForIUnknown(pCtxMenu);
                _activeCtxMenu2 = ctxMenu as IContextMenu2;
                _activeCtxMenu3 = ctxMenu as IContextMenu3;

                hMenu = CreatePopupMenu();
                if (hMenu == IntPtr.Zero) return false;

                // 1. Inject Drawer-specific actions at the top of the native Win32 menu
                InsertMenu(hMenu, 0, MF_BYPOSITION | MF_STRING, new UIntPtr(ID_DRAWER_OPEN),
                    string.Format("Abrir \"{0}\"", itemDisplayName ?? "Atalho"));
                InsertMenu(hMenu, 1, MF_BYPOSITION | MF_STRING, new UIntPtr(ID_DRAWER_RENAME),
                    "Renomear na Gaveta...");
                InsertMenu(hMenu, 2, MF_BYPOSITION | MF_STRING, new UIntPtr(ID_DRAWER_RESTORE),
                    "Devolver para a Área de Trabalho");
                InsertMenu(hMenu, 3, MF_BYPOSITION | MF_SEPARATOR, UIntPtr.Zero, null);

                // 2. Populate the rest of the menu with the REAL Windows Explorer Shell Context Menu
                ctxMenu.QueryContextMenu(hMenu, 4, ID_SHELL_FIRST, ID_SHELL_LAST, CMF_NORMAL | CMF_EXPLORE);

                if (hwndSource != null)
                {
                    hwndSource.AddHook(hook);
                }

                SetForegroundWindow(ownerHwnd);
                int selectedCmd = TrackPopupMenuEx(
                    hMenu,
                    TPM_RETURNCMD | TPM_RIGHTBUTTON | TPM_LEFTALIGN,
                    screenX,
                    screenY,
                    ownerHwnd,
                    IntPtr.Zero);

                if (selectedCmd == (int)ID_DRAWER_OPEN)
                {
                    if (onOpenInDrawer != null) onOpenInDrawer();
                    return true;
                }
                if (selectedCmd == (int)ID_DRAWER_RENAME)
                {
                    if (onRenameInDrawer != null) onRenameInDrawer();
                    return true;
                }
                if (selectedCmd == (int)ID_DRAWER_RESTORE)
                {
                    if (onRestoreToDesktop != null) onRestoreToDesktop();
                    return true;
                }
                if (selectedCmd >= (int)ID_SHELL_FIRST && selectedCmd <= (int)ID_SHELL_LAST)
                {
                    var invoke = new CMINVOKECOMMANDINFO
                    {
                        cbSize = Marshal.SizeOf(typeof(CMINVOKECOMMANDINFO)),
                        fMask = 0,
                        hwnd = ownerHwnd,
                        lpVerb = new IntPtr(selectedCmd - (int)ID_SHELL_FIRST),
                        lpParameters = null,
                        lpDirectory = Path.GetDirectoryName(filePath),
                        nShow = SW_SHOWNORMAL,
                        dwHotKey = 0,
                        hIcon = IntPtr.Zero
                    };
                    ctxMenu.InvokeCommand(ref invoke);
                    return true;
                }
                return true;
            }
            catch
            {
                return false;
            }
            finally
            {
                if (hwndSource != null)
                {
                    try { hwndSource.RemoveHook(hook); } catch { }
                }
                _activeCtxMenu2 = null;
                _activeCtxMenu3 = null;
                if (hMenu != IntPtr.Zero) DestroyMenu(hMenu);
                if (pCtxMenu != IntPtr.Zero) Marshal.Release(pCtxMenu);
                if (pidlFull != IntPtr.Zero) CoTaskMemFree(pidlFull);
            }
        }

        private static Bitmap HBitmapToArgbBitmap(IntPtr hBitmap)
        {
            BITMAP bmpInfo;
            if (GetObject(hBitmap, Marshal.SizeOf(typeof(BITMAP)), out bmpInfo) == 0 ||
                bmpInfo.bmBitsPixel != 32 ||
                bmpInfo.bmBits == IntPtr.Zero)
            {
                return Image.FromHbitmap(hBitmap);
            }

            int width = bmpInfo.bmWidth;
            int height = Math.Abs(bmpInfo.bmHeight);
            var result = new Bitmap(width, height, PixelFormat.Format32bppArgb);

            BitmapData dstData = result.LockBits(
                new Rectangle(0, 0, width, height),
                ImageLockMode.WriteOnly,
                PixelFormat.Format32bppArgb);

            int stride = bmpInfo.bmWidthBytes;
            byte[] rowBuf = new byte[stride];

            for (int y = 0; y < height; y++)
            {
                int srcRow = bmpInfo.bmHeight > 0 ? (height - 1 - y) : y;
                IntPtr srcPtr = new IntPtr(bmpInfo.bmBits.ToInt64() + (long)srcRow * stride);
                Marshal.Copy(srcPtr, rowBuf, 0, stride);
                IntPtr dstPtr = new IntPtr(dstData.Scan0.ToInt64() + (long)y * dstData.Stride);
                Marshal.Copy(rowBuf, 0, dstPtr, Math.Min(stride, dstData.Stride));
            }

            result.UnlockBits(dstData);
            return result;
        }

        private static string BitmapToDataUrl(Bitmap bmp)
        {
            using (var ms = new MemoryStream())
            {
                bmp.Save(ms, ImageFormat.Png);
                return "data:image/png;base64," + Convert.ToBase64String(ms.ToArray());
            }
        }

        public static string ExtractHighResIconDataUrl(string filePath)
        {
            if (string.IsNullOrEmpty(filePath) || (!File.Exists(filePath) && !Directory.Exists(filePath)))
                return "";

            string targetToExtract = filePath;
            string ext = Path.GetExtension(filePath).ToLowerInvariant();

            if (ext == ".url" || ext == ".lnk")
            {
                ResolvedShortcutInfo details = ResolveShortcutDetails(filePath);
                if (!string.IsNullOrEmpty(details.IconPath) && File.Exists(details.IconPath))
                {
                    targetToExtract = details.IconPath;
                }
                else if (!string.IsNullOrEmpty(details.TargetPath) && File.Exists(details.TargetPath))
                {
                    targetToExtract = details.TargetPath;
                }
            }

            string targetExt = Path.GetExtension(targetToExtract).ToLowerInvariant();
            if (targetExt == ".png" && File.Exists(targetToExtract))
            {
                try
                {
                    byte[] bytes = File.ReadAllBytes(targetToExtract);
                    return "data:image/png;base64," + Convert.ToBase64String(bytes);
                }
                catch { }
            }

            if (targetExt == ".ico" && File.Exists(targetToExtract))
            {
                try
                {
                    using (var ico = new Icon(targetToExtract, new Size(256, 256)))
                    using (Bitmap bmp = ico.ToBitmap())
                    {
                        return BitmapToDataUrl(bmp);
                    }
                }
                catch { }
            }

            string[] candidates = (targetToExtract != filePath)
                ? new string[] { targetToExtract, filePath }
                : new string[] { filePath };

            foreach (string candidate in candidates)
            {
                if (string.IsNullOrEmpty(candidate) || (!File.Exists(candidate) && !Directory.Exists(candidate)))
                    continue;

                try
                {
                    Guid iid = new Guid("bcc18b79-ba16-442f-80c4-8a59c30c463b");
                    IShellItemImageFactory factory;
                    int hr = SHCreateItemFromParsingName(Path.GetFullPath(candidate), IntPtr.Zero, ref iid, out factory);
                    if (hr == 0 && factory != null)
                    {
                        IntPtr hBitmap;
                        hr = factory.GetImage(new SIZE(128, 128), SIIGBF.SIIGBF_BIGGERSIZEOK | SIIGBF.SIIGBF_ICONONLY, out hBitmap);
                        if (hr == 0 && hBitmap != IntPtr.Zero)
                        {
                            try
                            {
                                using (Bitmap bmp = HBitmapToArgbBitmap(hBitmap))
                                {
                                    return BitmapToDataUrl(bmp);
                                }
                            }
                            finally
                            {
                                DeleteObject(hBitmap);
                            }
                        }
                    }
                }
                catch { }

                if (File.Exists(candidate))
                {
                    try
                    {
                        using (Icon assoc = Icon.ExtractAssociatedIcon(candidate))
                        {
                            if (assoc != null)
                            {
                                using (Bitmap bmp = assoc.ToBitmap())
                                {
                                    return BitmapToDataUrl(bmp);
                                }
                            }
                        }
                    }
                    catch { }
                }
            }

            return "";
        }

        public static BitmapSource DataUrlToBitmapSource(string dataUrl)
        {
            if (string.IsNullOrEmpty(dataUrl)) return null;
            const string prefix = "data:image/png;base64,";
            string base64 = dataUrl.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
                ? dataUrl.Substring(prefix.Length)
                : dataUrl;

            try
            {
                byte[] bytes = Convert.FromBase64String(base64);
                using (var ms = new MemoryStream(bytes))
                {
                    var bi = new BitmapImage();
                    bi.BeginInit();
                    bi.CacheOption = BitmapCacheOption.OnLoad;
                    bi.StreamSource = ms;
                    bi.EndInit();
                    bi.Freeze();
                    return bi;
                }
            }
            catch
            {
                return null;
            }
        }

        public static void ConfigurePodToolWindowStyle(IntPtr hwnd)
        {
            if (hwnd == IntPtr.Zero || !IsWindow(hwnd)) return;
            long exStyle = GetWindowLongPtr(hwnd, GWL_EXSTYLE).ToInt64() & 0xFFFFFFFFL;
            long desiredEx = ((exStyle & ~WS_EX_APPWINDOW) | WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE) & 0xFFFFFFFFL;
            if (exStyle != desiredEx)
            {
                SetWindowLongPtr(hwnd, GWL_EXSTYLE, new IntPtr(desiredEx));
            }
        }

        private static void FindDesktopHandles(out IntPtr hDesktopHost, out IntPtr hDefView)
        {
            hDesktopHost = IntPtr.Zero;
            hDefView = IntPtr.Zero;

            IntPtr progman = FindWindow("Progman", null);
            if (progman != IntPtr.Zero)
            {
                IntPtr dv = FindWindowEx(progman, IntPtr.Zero, "SHELLDLL_DefView", null);
                if (dv != IntPtr.Zero)
                {
                    hDesktopHost = progman;
                    hDefView = dv;
                    return;
                }
            }

            IntPtr workerW = IntPtr.Zero;
            while ((workerW = FindWindowEx(IntPtr.Zero, workerW, "WorkerW", null)) != IntPtr.Zero)
            {
                IntPtr dv = FindWindowEx(workerW, IntPtr.Zero, "SHELLDLL_DefView", null);
                if (dv != IntPtr.Zero)
                {
                    hDesktopHost = workerW;
                    hDefView = dv;
                    return;
                }
            }

            hDesktopHost = progman;
        }

        private static bool IsRealVisibleNormalWindow(IntPtr candidate, IntPtr selfHwnd, IntPtr hDesktopHost, IntPtr hDefView)
        {
            if (candidate == IntPtr.Zero || candidate == selfHwnd || candidate == hDesktopHost || candidate == hDefView)
                return false;
            if (!IsWindowVisible(candidate) || IsIconic(candidate))
                return false;

            long ex = GetWindowLongPtr(candidate, GWL_EXSTYLE).ToInt64();
            if ((ex & WS_EX_TOPMOST) != 0) return false;
            if ((ex & WS_EX_TOOLWINDOW) != 0 && (ex & WS_EX_NOACTIVATE) != 0) return false;

            int cloaked = 0;
            if (DwmGetWindowAttribute(candidate, DWMWA_CLOAKED, out cloaked, sizeof(int)) == 0 && cloaked != 0)
                return false;

            RECT r;
            if (!GetWindowRect(candidate, out r) || (r.Right - r.Left) <= 2 || (r.Bottom - r.Top) <= 2)
                return false;

            var sb = new StringBuilder(128);
            if (GetClassName(candidate, sb, 128) > 0)
            {
                string cls = sb.ToString();
                if (cls == "Progman" ||
                    cls == "WorkerW" ||
                    cls == "Shell_TrayWnd" ||
                    cls == "Shell_SecondaryTrayWnd" ||
                    cls == "NotifyIconOverflowWindow" ||
                    cls == "DummyDWMListenerWindow")
                {
                    return false;
                }
            }
            return true;
        }

        public static void ApplyPodLayering(IntPtr hwnd, bool stayTopmost)
        {
            if (hwnd == IntPtr.Zero || !IsWindow(hwnd)) return;

            ConfigurePodToolWindowStyle(hwnd);

            if (IsIconic(hwnd))
            {
                ShowWindow(hwnd, SW_RESTORE);
                ShowWindow(hwnd, SW_SHOWNOACTIVATE);
            }

            if (stayTopmost)
            {
                SetWindowPos(
                    hwnd,
                    HWND_TOPMOST,
                    0, 0, 0, 0,
                    SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE | SWP_NOSENDCHANGING | SWP_SHOWWINDOW);
                return;
            }

            // Ensure not topmost first
            long ex = GetWindowLongPtr(hwnd, GWL_EXSTYLE).ToInt64();
            if ((ex & WS_EX_TOPMOST) != 0)
            {
                SetWindowPos(
                    hwnd,
                    HWND_NOTOPMOST,
                    0, 0, 0, 0,
                    SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE | SWP_NOSENDCHANGING);
            }

            IntPtr hDesktopHost, hDefView;
            FindDesktopHandles(out hDesktopHost, out hDefView);

            if (hDesktopHost == IntPtr.Zero)
            {
                SetWindowPos(
                    hwnd,
                    HWND_BOTTOM,
                    0, 0, 0, 0,
                    SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE | SWP_NOSENDCHANGING);
                return;
            }

            IntPtr cur = GetWindow(hDesktopHost, GW_HWNDPREV);
            int guard = 0;
            while (cur != IntPtr.Zero && guard++ < 500)
            {
                if (cur == hwnd) return;
                if (IsRealVisibleNormalWindow(cur, hwnd, hDesktopHost, hDefView))
                {
                    SetWindowPos(
                        hwnd,
                        cur,
                        0, 0, 0, 0,
                        SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE | SWP_NOSENDCHANGING | SWP_SHOWWINDOW);
                    return;
                }
                cur = GetWindow(cur, GW_HWNDPREV);
            }

            SetWindowPos(
                hwnd,
                HWND_TOP,
                0, 0, 0, 0,
                SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE | SWP_NOSENDCHANGING | SWP_SHOWWINDOW);
        }
    }
}
