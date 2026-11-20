using System;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;
using System.Text;
using System.Threading;
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
        private const uint MF_CHECKED = 0x00000008;
        private const uint MF_POPUP = 0x00000010;
        private const uint MF_SEPARATOR = 0x00000800;
        private const uint MF_BYPOSITION = 0x00000400;

        private const uint TPM_RETURNCMD = 0x0100;
        private const uint TPM_RIGHTBUTTON = 0x0002;
        private const uint TPM_LEFTALIGN = 0x0000;

        public const int WM_NULL = 0x0000;
        public const int WM_MOUSEACTIVATE = 0x0021;
        public const int MA_NOACTIVATE = 3;
        public const int WM_WINDOWPOSCHANGING = 0x0046;
        public const int WM_COPYGLOBALDATA = 0x0049;
        public const int WM_COPYDATA = 0x004A;
        public const int WM_DROPFILES = 0x0233;
        private const int WM_INITMENUPOPUP = 0x0117;
        private const int WM_DRAWITEM = 0x002B;
        private const int WM_MEASUREITEM = 0x002C;
        private const int WM_MENUCHAR = 0x0120;
        public const int WM_HOTKEY = 0x0312;

        private const uint MSGFLT_ALLOW = 1;

        public const uint MOD_ALT = 0x0001;
        public const uint MOD_CONTROL = 0x0002;
        public const uint MOD_SHIFT = 0x0004;
        public const uint VK_D = 0x44;

        [StructLayout(LayoutKind.Sequential)]
        public struct WINDOWPOS
        {
            public IntPtr hwnd;
            public IntPtr hwndInsertAfter;
            public int x;
            public int y;
            public int cx;
            public int cy;
            public uint flags;
        }

        [DllImport("user32.dll", EntryPoint = "PostMessageW")]
        public static extern bool PostMessage(IntPtr hWnd, uint Msg, IntPtr wParam, IntPtr lParam);

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
        public static extern bool ScreenToClient(IntPtr hWnd, ref POINT lpPoint);

        [DllImport("user32.dll")]
        public static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

        [DllImport("user32.dll", CharSet = CharSet.Unicode, EntryPoint = "SendMessageW")]
        public static extern IntPtr SendMessage(IntPtr hWnd, uint Msg, IntPtr wParam, IntPtr lParam);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern IntPtr OpenProcess(uint dwDesiredAccess, bool bInheritHandle, uint dwProcessId);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern IntPtr VirtualAllocEx(IntPtr hProcess, IntPtr lpAddress, uint dwSize, uint flAllocationType, uint flProtect);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool VirtualFreeEx(IntPtr hProcess, IntPtr lpAddress, uint dwSize, uint dwFreeType);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool WriteProcessMemory(IntPtr hProcess, IntPtr lpBaseAddress, IntPtr lpBuffer, uint nSize, out IntPtr lpNumberOfBytesWritten);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool ReadProcessMemory(IntPtr hProcess, IntPtr lpBaseAddress, IntPtr lpBuffer, uint nSize, out IntPtr lpNumberOfBytesRead);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool CloseHandle(IntPtr hObject);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool ChangeWindowMessageFilterEx(IntPtr hWnd, uint msg, uint action, IntPtr changeFilterStruct);

        [DllImport("shell32.dll")]
        private static extern void DragAcceptFiles(IntPtr hWnd, bool fAccept);

        [DllImport("shell32.dll", CharSet = CharSet.Unicode, EntryPoint = "DragQueryFileW")]
        private static extern uint DragQueryFile(IntPtr hDrop, uint iFile, StringBuilder lpszFile, uint cch);

        [DllImport("shell32.dll")]
        private static extern void DragFinish(IntPtr hDrop);

        [DllImport("advapi32.dll", SetLastError = true)]
        private static extern bool OpenProcessToken(IntPtr ProcessHandle, uint DesiredAccess, out IntPtr TokenHandle);

        [DllImport("advapi32.dll", SetLastError = true)]
        private static extern bool GetTokenInformation(IntPtr TokenHandle, int TokenInformationClass, out int TokenInformation, int TokenInformationLength, out int ReturnLength);

        [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        private static extern bool DuplicateTokenEx(
            IntPtr hExistingToken,
            uint dwDesiredAccess,
            IntPtr lpTokenAttributes,
            int ImpersonationLevel,
            int TokenType,
            out IntPtr phNewToken);

        [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        private static extern bool CreateProcessWithTokenW(
            IntPtr hToken,
            uint dwLogonFlags,
            string lpApplicationName,
            string lpCommandLine,
            uint dwCreationFlags,
            IntPtr lpEnvironment,
            string lpCurrentDirectory,
            ref STARTUPINFO lpStartupInfo,
            out PROCESS_INFORMATION lpProcessInformation);

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct STARTUPINFO
        {
            public int cb;
            public string lpReserved;
            public string lpDesktop;
            public string lpTitle;
            public int dwX;
            public int dwY;
            public int dwXSize;
            public int dwYSize;
            public int dwXCountChars;
            public int dwYCountChars;
            public int dwFillAttribute;
            public int dwFlags;
            public short wShowWindow;
            public short cbReserved2;
            public IntPtr lpReserved2;
            public IntPtr hStdInput;
            public IntPtr hStdOutput;
            public IntPtr hStdError;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct PROCESS_INFORMATION
        {
            public IntPtr hProcess;
            public IntPtr hThread;
            public int dwProcessId;
            public int dwThreadId;
        }

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

        [StructLayout(LayoutKind.Sequential)]
        private struct LVITEM64
        {
            public uint mask;
            public int iItem;
            public int iSubItem;
            public uint state;
            public uint stateMask;
            public IntPtr pszText;
            public int cchTextMax;
            public int iImage;
            public IntPtr lParam;
            public int iIndent;
            public int iGroupId;
            public uint cColumns;
            public IntPtr puColumns;
            public IntPtr piColFmt;
            public int iGroup;
        }

        private const uint LVM_FIRST = 0x1000;
        private const uint LVM_GETITEMCOUNT = LVM_FIRST + 4;
        private const uint LVM_SETITEMPOSITION = LVM_FIRST + 15;
        private const uint LVM_SETITEMPOSITION32 = LVM_FIRST + 49;
        private const uint LVM_GETITEMTEXTW = LVM_FIRST + 115;

        private const uint PROCESS_VM_OPERATION = 0x0008;
        private const uint PROCESS_VM_READ = 0x0010;
        private const uint PROCESS_VM_WRITE = 0x0020;
        private const uint MEM_COMMIT = 0x1000;
        private const uint MEM_RELEASE = 0x8000;
        private const uint PAGE_READWRITE = 0x04;

        private static bool TrySetDesktopListViewIconPosition(string filePath, int screenX, int screenY)
        {
            if (string.IsNullOrEmpty(filePath)) return false;

            IntPtr hDesktopHost, hDefView;
            FindDesktopHandles(out hDesktopHost, out hDefView);
            if (hDefView == IntPtr.Zero) return false;

            IntPtr hListView = FindWindowEx(hDefView, IntPtr.Zero, "SysListView32", null);
            if (hListView == IntPtr.Zero) return false;

            POINT clientPt = new POINT { X = screenX, Y = screenY };
            ScreenToClient(hListView, ref clientPt);
            clientPt.X = Math.Max(4, clientPt.X - 24);
            clientPt.Y = Math.Max(4, clientPt.Y - 24);

            int itemCount = (int)SendMessage(hListView, LVM_GETITEMCOUNT, IntPtr.Zero, IntPtr.Zero);
            if (itemCount <= 0) return false;

            uint pid;
            GetWindowThreadProcessId(hListView, out pid);
            if (pid == 0) return false;

            string targetNameNoExt = Path.GetFileNameWithoutExtension(filePath) ?? "";
            string targetFileName = Path.GetFileName(filePath) ?? "";

            IntPtr hProc = OpenProcess(PROCESS_VM_OPERATION | PROCESS_VM_READ | PROCESS_VM_WRITE, false, pid);
            if (hProc == IntPtr.Zero) return false;

            IntPtr remoteMem = IntPtr.Zero;
            IntPtr localBuf = IntPtr.Zero;
            try
            {
                const uint allocSize = 2048;
                remoteMem = VirtualAllocEx(hProc, IntPtr.Zero, allocSize, MEM_COMMIT, PAGE_READWRITE);
                if (remoteMem == IntPtr.Zero) return false;

                int lvSize = Marshal.SizeOf(typeof(LVITEM64));
                IntPtr remoteTextPtr = new IntPtr(remoteMem.ToInt64() + 128);
                IntPtr remotePtPtr = new IntPtr(remoteMem.ToInt64() + 1200);

                localBuf = Marshal.AllocHGlobal(1024);

                int matchedIndex = -1;
                for (int i = 0; i < itemCount; i++)
                {
                    var lvi = new LVITEM64
                    {
                        mask = 0x0001, // LVIF_TEXT
                        iItem = i,
                        iSubItem = 0,
                        pszText = remoteTextPtr,
                        cchTextMax = 260
                    };

                    Marshal.StructureToPtr(lvi, localBuf, false);
                    IntPtr written;
                    if (!WriteProcessMemory(hProc, remoteMem, localBuf, (uint)lvSize, out written))
                        continue;

                    int charsLen = (int)SendMessage(hListView, LVM_GETITEMTEXTW, new IntPtr(i), remoteMem);
                    if (charsLen <= 0) continue;

                    IntPtr read;
                    if (!ReadProcessMemory(hProc, remoteTextPtr, localBuf, (uint)Math.Min(520, (charsLen + 1) * 2), out read))
                        continue;

                    string itemText = Marshal.PtrToStringUni(localBuf, charsLen);
                    if (string.IsNullOrEmpty(itemText)) continue;

                    if (string.Equals(itemText.Trim(), targetNameNoExt.Trim(), StringComparison.OrdinalIgnoreCase) ||
                        string.Equals(itemText.Trim(), targetFileName.Trim(), StringComparison.OrdinalIgnoreCase))
                    {
                        matchedIndex = i;
                        break;
                    }
                }

                if (matchedIndex < 0) return false;

                // Position via both LVM_SETITEMPOSITION32 and LVM_SETITEMPOSITION
                Marshal.StructureToPtr(clientPt, localBuf, false);
                IntPtr bytesWritten;
                if (WriteProcessMemory(hProc, remotePtPtr, localBuf, (uint)Marshal.SizeOf(typeof(POINT)), out bytesWritten))
                {
                    SendMessage(hListView, LVM_SETITEMPOSITION32, new IntPtr(matchedIndex), remotePtPtr);
                }

                int packedPos = ((clientPt.Y & 0xFFFF) << 16) | (clientPt.X & 0xFFFF);
                SendMessage(hListView, LVM_SETITEMPOSITION, new IntPtr(matchedIndex), new IntPtr(packedPos));
                return true;
            }
            catch
            {
                return false;
            }
            finally
            {
                if (localBuf != IntPtr.Zero) Marshal.FreeHGlobal(localBuf);
                if (remoteMem != IntPtr.Zero) VirtualFreeEx(hProc, remoteMem, 0, MEM_RELEASE);
                CloseHandle(hProc);
            }
        }

        public static void PositionDesktopIconAtScreenPoint(string filePath, int screenX, int screenY)
        {
            if (string.IsNullOrEmpty(filePath)) return;
            NotifyDesktopFileRestored(filePath);

            if (TrySetDesktopListViewIconPosition(filePath, screenX, screenY))
            {
                return;
            }

            // Schedule fast background retries as Windows Explorer processes SHChangeNotify and inserts the desktop icon
            ThreadPool.QueueUserWorkItem(delegate
            {
                int[] delays = new int[] { 70, 180, 380, 700 };
                foreach (int d in delays)
                {
                    Thread.Sleep(d);
                    if (TrySetDesktopListViewIconPosition(filePath, screenX, screenY))
                    {
                        break;
                    }
                }
            });
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

                PrepareWindowForTrackPopupMenu(ownerHwnd);
                int selectedCmd = TrackPopupMenuEx(
                    hMenu,
                    TPM_RETURNCMD | TPM_RIGHTBUTTON | TPM_LEFTALIGN,
                    screenX,
                    screenY,
                    ownerHwnd,
                    IntPtr.Zero);
                FinishWindowAfterTrackPopupMenu(ownerHwnd);

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
                FinishWindowAfterTrackPopupMenu(ownerHwnd);
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

        private static void PrepareWindowForTrackPopupMenu(IntPtr ownerHwnd)
        {
            if (ownerHwnd == IntPtr.Zero || !IsWindow(ownerHwnd)) return;
            // Temporarily clear WS_EX_NOACTIVATE so SetForegroundWindow succeeds and clicking outside dismisses TrackPopupMenuEx (KB135788)
            long exStyle = GetWindowLongPtr(ownerHwnd, GWL_EXSTYLE).ToInt64() & 0xFFFFFFFFL;
            long activeAllowedEx = (exStyle & ~WS_EX_NOACTIVATE) & 0xFFFFFFFFL;
            if (exStyle != activeAllowedEx)
            {
                SetWindowLongPtr(ownerHwnd, GWL_EXSTYLE, new IntPtr(activeAllowedEx));
            }
            SetForegroundWindow(ownerHwnd);
        }

        private static void FinishWindowAfterTrackPopupMenu(IntPtr ownerHwnd)
        {
            if (ownerHwnd == IntPtr.Zero || !IsWindow(ownerHwnd)) return;
            PostMessage(ownerHwnd, (uint)WM_NULL, IntPtr.Zero, IntPtr.Zero);
            ConfigurePodToolWindowStyle(ownerHwnd);
        }

        /// <summary>
        /// Displays a 100% Native Win32 Popup Menu (TrackPopupMenuEx) for the Drawer Pod Handle,
        /// ensuring that clicking anywhere outside the menu (on another app or Desktop) immediately closes it.
        /// </summary>
        public static int ShowNativePodHandleContextMenu(
            IntPtr ownerHwnd,
            int screenX,
            int screenY,
            bool isPinned,
            string currentDockedEdge,
            string currentPreferredDirection)
        {
            if (ownerHwnd == IntPtr.Zero || !IsWindow(ownerHwnd)) return 0;

            IntPtr hMenu = CreatePopupMenu();
            IntPtr hEdgeSub = CreatePopupMenu();
            IntPtr hDirSub = CreatePopupMenu();
            if (hMenu == IntPtr.Zero) return 0;

            string edge = string.IsNullOrEmpty(currentDockedEdge) ? "none" : currentDockedEdge;
            string dir = string.IsNullOrEmpty(currentPreferredDirection) ? "auto" : currentPreferredDirection;

            try
            {
                // Edge docking submenu items (IDs 20..23)
                InsertMenu(hEdgeSub, 0, MF_BYPOSITION | MF_STRING | (edge == "none" ? MF_CHECKED : 0),
                    new UIntPtr(20), "Livre na Área de Trabalho (Atrás das Janelas)");
                InsertMenu(hEdgeSub, 1, MF_BYPOSITION | MF_STRING | (edge == "left" ? MF_CHECKED : 0),
                    new UIntPtr(21), "Aba Magnética na Borda Esquerda");
                InsertMenu(hEdgeSub, 2, MF_BYPOSITION | MF_STRING | (edge == "right" ? MF_CHECKED : 0),
                    new UIntPtr(22), "Aba Magnética na Borda Direita");
                InsertMenu(hEdgeSub, 3, MF_BYPOSITION | MF_STRING | (edge == "top" ? MF_CHECKED : 0),
                    new UIntPtr(23), "Aba Magnética na Borda Superior");

                // Direction submenu items (IDs 30..33)
                InsertMenu(hDirSub, 0, MF_BYPOSITION | MF_STRING | (dir == "auto" ? MF_CHECKED : 0),
                    new UIntPtr(30), "Automático (Inteligente)");
                InsertMenu(hDirSub, 1, MF_BYPOSITION | MF_STRING | (dir == "right" ? MF_CHECKED : 0),
                    new UIntPtr(31), "Abrir para a Direita");
                InsertMenu(hDirSub, 2, MF_BYPOSITION | MF_STRING | (dir == "down" ? MF_CHECKED : 0),
                    new UIntPtr(32), "Abrir para Baixo");
                InsertMenu(hDirSub, 3, MF_BYPOSITION | MF_STRING | (dir == "left" ? MF_CHECKED : 0),
                    new UIntPtr(33), "Abrir para a Esquerda");

                // Main menu items
                uint pos = 0;
                InsertMenu(hMenu, pos++, MF_BYPOSITION | MF_STRING, new UIntPtr(1),
                    isPinned ? "Desafixar e Fechar Gaveta" : "Fixar Gaveta Aberta (Pin)");
                InsertMenu(hMenu, pos++, MF_BYPOSITION | MF_STRING, new UIntPtr(2),
                    "+ Catálogo Rápido de Apps (Desktop & Menu Iniciar)...");
                InsertMenu(hMenu, pos++, MF_BYPOSITION | MF_STRING, new UIntPtr(3),
                    "+ Escolher Arquivo ou Atalho (.lnk / .url / .exe)...");
                InsertMenu(hMenu, pos++, MF_BYPOSITION | MF_STRING, new UIntPtr(4),
                    "Renomear Gaveta...");
                InsertMenu(hMenu, pos++, MF_BYPOSITION | MF_SEPARATOR, UIntPtr.Zero, null);
                InsertMenu(hMenu, pos++, MF_BYPOSITION | MF_POPUP, new UIntPtr((ulong)hEdgeSub.ToInt64()),
                    "Acoplar na Borda Magnética (Sempre Acessível)");
                InsertMenu(hMenu, pos++, MF_BYPOSITION | MF_POPUP, new UIntPtr((ulong)hDirSub.ToInt64()),
                    "Direção de Abertura da Gaveta");
                InsertMenu(hMenu, pos++, MF_BYPOSITION | MF_SEPARATOR, UIntPtr.Zero, null);
                InsertMenu(hMenu, pos++, MF_BYPOSITION | MF_STRING, new UIntPtr(5),
                    "+ Criar Nova Gaveta");
                InsertMenu(hMenu, pos++, MF_BYPOSITION | MF_STRING, new UIntPtr(6),
                    "Configurações do Drawer...");
                InsertMenu(hMenu, pos++, MF_BYPOSITION | MF_SEPARATOR, UIntPtr.Zero, null);
                InsertMenu(hMenu, pos++, MF_BYPOSITION | MF_STRING, new UIntPtr(7),
                    "Excluir Gaveta (Devolver ícones ao Desktop)");

                PrepareWindowForTrackPopupMenu(ownerHwnd);
                int cmd = TrackPopupMenuEx(
                    hMenu,
                    TPM_RETURNCMD | TPM_RIGHTBUTTON | TPM_LEFTALIGN,
                    screenX,
                    screenY,
                    ownerHwnd,
                    IntPtr.Zero);
                return cmd;
            }
            finally
            {
                FinishWindowAfterTrackPopupMenu(ownerHwnd);
                if (hMenu != IntPtr.Zero) DestroyMenu(hMenu);
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

        private static Bitmap TrimTransparentMargins(Bitmap src)
        {
            if (src == null || src.Width <= 16 || src.Height <= 16) return src;
            try
            {
                int w = src.Width;
                int h = src.Height;
                int minX = w, minY = h, maxX = -1, maxY = -1;

                BitmapData data = src.LockBits(
                    new Rectangle(0, 0, w, h),
                    ImageLockMode.ReadOnly,
                    PixelFormat.Format32bppArgb);

                try
                {
                    int stride = data.Stride;
                    byte[] buf = new byte[stride * h];
                    Marshal.Copy(data.Scan0, buf, 0, buf.Length);

                    for (int y = 0; y < h; y++)
                    {
                        int rowOffset = y * stride;
                        for (int x = 0; x < w; x++)
                        {
                            byte alpha = buf[rowOffset + x * 4 + 3];
                            if (alpha > 12)
                            {
                                if (x < minX) minX = x;
                                if (x > maxX) maxX = x;
                                if (y < minY) minY = y;
                                if (y > maxY) maxY = y;
                            }
                        }
                    }
                }
                finally
                {
                    src.UnlockBits(data);
                }

                if (maxX < minX || maxY < minY) return src;

                int contentW = maxX - minX + 1;
                int contentH = maxY - minY + 1;
                int boxSize = Math.Max(contentW, contentH);

                // If content already fills > 90% of the bitmap, no trim needed
                if (boxSize >= Math.Min(w, h) * 0.90) return src;

                int pad = Math.Max(1, boxSize / 24);
                int outSize = boxSize + pad * 2;
                var trimmed = new Bitmap(outSize, outSize, PixelFormat.Format32bppArgb);
                using (Graphics g = Graphics.FromImage(trimmed))
                {
                    g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
                    int dstX = pad + (boxSize - contentW) / 2;
                    int dstY = pad + (boxSize - contentH) / 2;
                    g.DrawImage(
                        src,
                        new Rectangle(dstX, dstY, contentW, contentH),
                        new Rectangle(minX, minY, contentW, contentH),
                        GraphicsUnit.Pixel);
                }
                return trimmed;
            }
            catch
            {
                return src;
            }
        }

        private static string BitmapToDataUrl(Bitmap bmp)
        {
            Bitmap clean = TrimTransparentMargins(bmp);
            try
            {
                using (var ms = new MemoryStream())
                {
                    clean.Save(ms, ImageFormat.Png);
                    return "data:image/png;base64," + Convert.ToBase64String(ms.ToArray());
                }
            }
            finally
            {
                if (!ReferenceEquals(clean, bmp) && clean != null)
                {
                    clean.Dispose();
                }
            }
        }

        public static string TrimDataUrlTransparentMargins(string dataUrl)
        {
            if (string.IsNullOrEmpty(dataUrl)) return dataUrl;
            const string prefix = "data:image/png;base64,";
            string base64 = dataUrl.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
                ? dataUrl.Substring(prefix.Length)
                : dataUrl;

            try
            {
                byte[] bytes = Convert.FromBase64String(base64);
                using (var ms = new MemoryStream(bytes))
                using (var bmp = new Bitmap(ms))
                {
                    return BitmapToDataUrl(bmp);
                }
            }
            catch
            {
                return dataUrl;
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

        public static void EnableNativeFileDropOnWindow(IntPtr hwnd)
        {
            if (hwnd == IntPtr.Zero || !IsWindow(hwnd)) return;
            try
            {
                ChangeWindowMessageFilterEx(hwnd, (uint)WM_DROPFILES, MSGFLT_ALLOW, IntPtr.Zero);
                ChangeWindowMessageFilterEx(hwnd, (uint)WM_COPYDATA, MSGFLT_ALLOW, IntPtr.Zero);
                ChangeWindowMessageFilterEx(hwnd, (uint)WM_COPYGLOBALDATA, MSGFLT_ALLOW, IntPtr.Zero);
                DragAcceptFiles(hwnd, true);
            }
            catch { }
        }

        public static string[] ExtractDroppedFilesFromHDrop(IntPtr hDrop)
        {
            if (hDrop == IntPtr.Zero) return new string[0];
            try
            {
                uint count = DragQueryFile(hDrop, 0xFFFFFFFF, null, 0);
                if (count == 0) return new string[0];
                var list = new System.Collections.Generic.List<string>();
                var sb = new StringBuilder(1024);
                for (uint i = 0; i < count; i++)
                {
                    sb.Clear();
                    if (DragQueryFile(hDrop, i, sb, (uint)sb.Capacity) > 0)
                    {
                        string fp = sb.ToString();
                        if (!string.IsNullOrWhiteSpace(fp))
                        {
                            list.Add(fp);
                        }
                    }
                }
                return list.ToArray();
            }
            catch
            {
                return new string[0];
            }
            finally
            {
                try { DragFinish(hDrop); } catch { }
            }
        }

        public static bool IsProcessElevated(IntPtr processHandle)
        {
            IntPtr hToken = IntPtr.Zero;
            try
            {
                if (!OpenProcessToken(processHandle, 0x0008 /* TOKEN_QUERY */, out hToken))
                    return false;
                int elevation = 0;
                int retLen = 0;
                if (!GetTokenInformation(hToken, 20 /* TokenElevation */, out elevation, sizeof(int), out retLen))
                    return false;
                return elevation != 0;
            }
            catch
            {
                return false;
            }
            finally
            {
                if (hToken != IntPtr.Zero) CloseHandle(hToken);
            }
        }

        public static bool IsCurrentProcessElevated()
        {
            return IsProcessElevated(Process.GetCurrentProcess().Handle);
        }

        /// <summary>
        /// If Drawer.exe was launched from an elevated (Administrator) process while Windows Explorer (Desktop)
        /// runs at Medium Integrity, re-launches Drawer.exe using explorer.exe's Medium-Integrity token so
        /// Windows UIPI never blocks OLE Drag & Drop from the Desktop into Drawer Pods!
        /// </summary>
        public static bool TryRelaunchUnelevatedViaExplorer(string exePath, string arguments)
        {
            if (string.IsNullOrEmpty(exePath) || !File.Exists(exePath)) return false;
            if (!IsCurrentProcessElevated()) return false;

            IntPtr hExplorerProc = IntPtr.Zero;
            IntPtr hExplorerToken = IntPtr.Zero;
            IntPtr hPrimaryToken = IntPtr.Zero;

            try
            {
                Process[] explorers = Process.GetProcessesByName("explorer");
                Process targetExplorer = null;
                foreach (Process p in explorers)
                {
                    try
                    {
                        if (!IsProcessElevated(p.Handle))
                        {
                            targetExplorer = p;
                            break;
                        }
                    }
                    catch { }
                }

                if (targetExplorer == null) return false;

                hExplorerProc = OpenProcess(0x0400 /* PROCESS_QUERY_INFORMATION */, false, (uint)targetExplorer.Id);
                if (hExplorerProc == IntPtr.Zero) return false;

                if (!OpenProcessToken(hExplorerProc, 0x0002 /* TOKEN_DUPLICATE */, out hExplorerToken))
                    return false;

                const uint MAXIMUM_ALLOWED = 0x02000000;
                if (!DuplicateTokenEx(
                    hExplorerToken,
                    MAXIMUM_ALLOWED,
                    IntPtr.Zero,
                    2 /* SecurityImpersonation */,
                    1 /* TokenPrimary */,
                    out hPrimaryToken))
                {
                    return false;
                }

                var si = new STARTUPINFO();
                si.cb = Marshal.SizeOf(typeof(STARTUPINFO));
                PROCESS_INFORMATION pi;

                string cmdLine = string.IsNullOrWhiteSpace(arguments)
                    ? string.Format("\"{0}\"", exePath)
                    : string.Format("\"{0}\" {1}", exePath, arguments);

                bool ok = CreateProcessWithTokenW(
                    hPrimaryToken,
                    0,
                    exePath,
                    cmdLine,
                    0,
                    IntPtr.Zero,
                    Path.GetDirectoryName(exePath),
                    ref si,
                    out pi);

                if (ok)
                {
                    if (pi.hProcess != IntPtr.Zero) CloseHandle(pi.hProcess);
                    if (pi.hThread != IntPtr.Zero) CloseHandle(pi.hThread);
                    return true;
                }
                return false;
            }
            catch
            {
                return false;
            }
            finally
            {
                if (hPrimaryToken != IntPtr.Zero) CloseHandle(hPrimaryToken);
                if (hExplorerToken != IntPtr.Zero) CloseHandle(hExplorerToken);
                if (hExplorerProc != IntPtr.Zero) CloseHandle(hExplorerProc);
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

            // Place at HWND_BOTTOM of ZBID_DEFAULT (always behind all normal Windows programs, always above Desktop Progman)
            SetWindowPos(
                hwnd,
                HWND_BOTTOM,
                0, 0, 0, 0,
                SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE | SWP_NOSENDCHANGING | SWP_SHOWWINDOW);
        }
    }
}
