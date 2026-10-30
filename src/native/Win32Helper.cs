using System;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;

namespace DrawerWin32
{
    class Program
    {
        private const int GWL_STYLE = -16;
        private const int GWL_EXSTYLE = -20;
        private const int GWLP_HWNDPARENT = -8;

        private const long WS_POPUP = 0x80000000L;
        private const long WS_VISIBLE = 0x10000000L;
        private const long WS_CAPTION = 0x00C00000L;
        private const long WS_THICKFRAME = 0x00040000L;
        private const long WS_MINIMIZEBOX = 0x00020000L;
        private const long WS_MAXIMIZEBOX = 0x00010000L;
        private const long WS_SYSMENU = 0x00080000L;
        private const long WS_BORDER = 0x00800000L;
        private const long WS_DLGFRAME = 0x00400000L;

        private const long WS_EX_TOPMOST = 0x00000008L;
        private const long WS_EX_TRANSPARENT = 0x00000020L;
        private const long WS_EX_TOOLWINDOW = 0x00000080L;
        private const long WS_EX_APPWINDOW = 0x00040000L;
        private const long WS_EX_LAYERED = 0x00080000L;
        private const long WS_EX_NOACTIVATE = 0x08000000L;

        private static readonly IntPtr HWND_TOP = new IntPtr(0);
        private static readonly IntPtr HWND_BOTTOM = new IntPtr(1);

        private const uint GW_HWNDPREV = 3;

        private const uint SWP_NOSIZE = 0x0001;
        private const uint SWP_NOMOVE = 0x0002;
        private const uint SWP_NOZORDER = 0x0004;
        private const uint SWP_NOACTIVATE = 0x0010;
        private const uint SWP_FRAMECHANGED = 0x0020;
        private const uint SWP_SHOWWINDOW = 0x0040;
        private const uint SWP_NOSENDCHANGING = 0x0400;

        private const int SW_SHOWNOACTIVATE = 4;
        private const int SW_RESTORE = 9;

        private const int DWMWA_CLOAKED = 14;

        private const int SHCNE_CREATE = 0x00000002;
        private const int SHCNE_UPDATEDIR = 0x00001000;
        private const uint SHCNF_PATHW = 0x0005;
        private const uint SHCNF_FLUSH = 0x1000;

        [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")]
        private static extern IntPtr GetWindowLongPtr(IntPtr hWnd, int nIndex);

        [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")]
        private static extern IntPtr SetWindowLongPtr(IntPtr hWnd, int nIndex, IntPtr dwNewLong);

        [DllImport("user32.dll", EntryPoint = "FindWindowW", CharSet = CharSet.Unicode)]
        private static extern IntPtr FindWindow(string lpClassName, string lpWindowName);

        [DllImport("user32.dll", EntryPoint = "FindWindowExW", CharSet = CharSet.Unicode)]
        private static extern IntPtr FindWindowEx(IntPtr hWndParent, IntPtr hWndChildAfter, string lpszClass, string lpszWindow);

        [DllImport("user32.dll")]
        private static extern IntPtr GetWindow(IntPtr hWnd, uint uCmd);

        [DllImport("user32.dll")]
        private static extern bool IsWindow(IntPtr hWnd);

        [DllImport("user32.dll")]
        private static extern bool IsWindowVisible(IntPtr hWnd);

        [DllImport("user32.dll")]
        private static extern bool IsIconic(IntPtr hWnd);

        [DllImport("user32.dll")]
        private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

        [DllImport("user32.dll", EntryPoint = "GetClassNameW", CharSet = CharSet.Unicode)]
        private static extern int GetClassName(IntPtr hWnd, StringBuilder lpClassName, int nMaxCount);

        [DllImport("user32.dll")]
        private static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);

        [DllImport("user32.dll")]
        private static extern bool SetWindowPos(
            IntPtr hWnd,
            IntPtr hWndInsertAfter,
            int X,
            int Y,
            int cx,
            int cy,
            uint uFlags
        );

        [DllImport("dwmapi.dll")]
        private static extern int DwmGetWindowAttribute(IntPtr hwnd, int dwAttribute, out int pvAttribute, int cbAttribute);

        [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
        private static extern void SHChangeNotify(int wEventId, uint uFlags, string dwItem1, string dwItem2);

        [StructLayout(LayoutKind.Sequential)]
        private struct RECT
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
            [MarshalAs(UnmanagedType.Interface)] out IShellItemImageFactory ppv
        );

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

        [DllImport("gdi32.dll")]
        private static extern int GetObject(IntPtr hgdiobj, int cbBuffer, out BITMAP lpvObject);

        [DllImport("gdi32.dll")]
        private static extern bool DeleteObject(IntPtr hObject);

        static void FindDesktopHandles(out IntPtr hDesktopHost, out IntPtr hDefView)
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

        static bool IsRealVisibleNormalWindow(IntPtr candidate, IntPtr selfHwnd, IntPtr hDesktopHost, IntPtr hDefView)
        {
            if (candidate == IntPtr.Zero || candidate == selfHwnd || candidate == hDesktopHost || candidate == hDefView)
            {
                return false;
            }
            if (!IsWindowVisible(candidate) || IsIconic(candidate))
            {
                return false;
            }

            long ex = GetWindowLongPtr(candidate, GWL_EXSTYLE).ToInt64();
            if ((ex & WS_EX_TOPMOST) != 0)
            {
                return false;
            }
            if ((ex & WS_EX_TRANSPARENT) != 0 && (ex & WS_EX_LAYERED) != 0)
            {
                return false;
            }

            int cloaked = 0;
            if (DwmGetWindowAttribute(candidate, DWMWA_CLOAKED, out cloaked, sizeof(int)) == 0 && cloaked != 0)
            {
                return false;
            }

            RECT r;
            if (!GetWindowRect(candidate, out r) || (r.Right - r.Left) <= 2 || (r.Bottom - r.Top) <= 2)
            {
                return false;
            }

            StringBuilder sb = new StringBuilder(128);
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

        static void EnsureOnDesktop(IntPtr hwnd, bool isInitialPin)
        {
            if (hwnd == IntPtr.Zero || !IsWindow(hwnd)) return;

            // Strip any non-client caption/system-menu/border styles so Windows never draws a white title bar with an X button
            long style = GetWindowLongPtr(hwnd, GWL_STYLE).ToInt64() & 0xFFFFFFFFL;
            long desiredStyle = ((style & ~(WS_CAPTION | WS_THICKFRAME | WS_MINIMIZEBOX | WS_MAXIMIZEBOX | WS_SYSMENU | WS_BORDER | WS_DLGFRAME)) | WS_POPUP | WS_VISIBLE) & 0xFFFFFFFFL;
            if (style != desiredStyle)
            {
                SetWindowLongPtr(hwnd, GWL_STYLE, new IntPtr(desiredStyle));
            }

            // If Windows ever minimized the window (e.g. via Show Desktop / Win+M), restore it without stealing focus
            if (IsIconic(hwnd))
            {
                ShowWindow(hwnd, SW_RESTORE);
                ShowWindow(hwnd, SW_SHOWNOACTIVATE);
            }
            else if (!IsWindowVisible(hwnd))
            {
                ShowWindow(hwnd, SW_SHOWNOACTIVATE);
            }

            // Ensure toolwindow + noactivate extended styles
            long exStyle = GetWindowLongPtr(hwnd, GWL_EXSTYLE).ToInt64() & 0xFFFFFFFFL;
            long desiredEx = ((exStyle & ~(WS_EX_APPWINDOW | WS_EX_TOPMOST)) | WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE) & 0xFFFFFFFFL;
            if (exStyle != desiredEx)
            {
                SetWindowLongPtr(hwnd, GWL_EXSTYLE, new IntPtr(desiredEx));
            }

            IntPtr hDesktopHost, hDefView;
            FindDesktopHandles(out hDesktopHost, out hDefView);

            if (hDesktopHost == IntPtr.Zero)
            {
                SetWindowPos(
                    hwnd,
                    HWND_BOTTOM,
                    0, 0, 0, 0,
                    SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE | SWP_NOSENDCHANGING
                );
                return;
            }

            // Walk UP the Z-order from hDesktopHost (Progman or WorkerW hosting SHELLDLL_DefView).
            // Goal: Keep hwnd immediately ABOVE hDesktopHost and BELOW all visible normal windows above hDesktopHost.
            IntPtr cur = GetWindow(hDesktopHost, GW_HWNDPREV);
            int guard = 0;
            while (cur != IntPtr.Zero && guard++ < 500)
            {
                if (cur == hwnd)
                {
                    // Already immediately above the desktop and below all visible normal windows!
                    return;
                }

                if (IsRealVisibleNormalWindow(cur, hwnd, hDesktopHost, hDefView))
                {
                    // Place hwnd immediately below the lowest visible normal window above the desktop
                    SetWindowPos(
                        hwnd,
                        cur,
                        0, 0, 0, 0,
                        SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE | SWP_NOSENDCHANGING | SWP_SHOWWINDOW
                    );
                    return;
                }

                cur = GetWindow(cur, GW_HWNDPREV);
            }

            // No visible normal window is above hDesktopHost (e.g. "Mostrar área de trabalho" / Show Desktop is active!)
            // and hwnd was below hDesktopHost -> lift hwnd to top of non-topmost layer (immediately above hDesktopHost).
            SetWindowPos(
                hwnd,
                HWND_TOP,
                0, 0, 0, 0,
                SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE | SWP_NOSENDCHANGING | SWP_SHOWWINDOW
            );
        }

        static void WatchDesktopLoop(IntPtr hwnd, int parentPid)
        {
            EnsureOnDesktop(hwnd, true);

            Process parentProc = null;
            if (parentPid > 0)
            {
                try
                {
                    parentProc = Process.GetProcessById(parentPid);
                }
                catch
                {
                    return;
                }
            }

            while (true)
            {
                Thread.Sleep(80);
                if (!IsWindow(hwnd))
                {
                    break;
                }
                if (parentProc != null)
                {
                    try
                    {
                        if (parentProc.HasExited) break;
                    }
                    catch
                    {
                        break;
                    }
                }
                EnsureOnDesktop(hwnd, false);
            }
        }

        static void NotifyDesktopFileRestored(string filePath)
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

        static Bitmap HBitmapToArgbBitmap(IntPtr hBitmap)
        {
            BITMAP bmpInfo;
            if (GetObject(hBitmap, Marshal.SizeOf(typeof(BITMAP)), out bmpInfo) == 0)
            {
                return Image.FromHbitmap(hBitmap);
            }

            if (bmpInfo.bmBitsPixel != 32 || bmpInfo.bmBits == IntPtr.Zero)
            {
                return Image.FromHbitmap(hBitmap);
            }

            int width = bmpInfo.bmWidth;
            int height = Math.Abs(bmpInfo.bmHeight);
            Bitmap result = new Bitmap(width, height, PixelFormat.Format32bppArgb);

            BitmapData dstData = result.LockBits(
                new Rectangle(0, 0, width, height),
                ImageLockMode.WriteOnly,
                PixelFormat.Format32bppArgb
            );

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

        static string BitmapToDataUrl(Bitmap bmp)
        {
            using (MemoryStream ms = new MemoryStream())
            {
                bmp.Save(ms, ImageFormat.Png);
                return "data:image/png;base64," + Convert.ToBase64String(ms.ToArray());
            }
        }

        static string ExtractHighResIcon(string targetPath)
        {
            if (string.IsNullOrEmpty(targetPath) || (!File.Exists(targetPath) && !Directory.Exists(targetPath)))
            {
                return "";
            }

            string ext = Path.GetExtension(targetPath).ToLowerInvariant();

            if (ext == ".ico" && File.Exists(targetPath))
            {
                try
                {
                    using (Icon ico = new Icon(targetPath, new Size(256, 256)))
                    using (Bitmap bmp = ico.ToBitmap())
                    {
                        return BitmapToDataUrl(bmp);
                    }
                }
                catch
                {
                    // Fall through to ShellItemImageFactory
                }
            }

            try
            {
                Guid iid = new Guid("bcc18b79-ba16-442f-80c4-8a59c30c463b");
                IShellItemImageFactory factory;
                int hr = SHCreateItemFromParsingName(Path.GetFullPath(targetPath), IntPtr.Zero, ref iid, out factory);
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
            catch
            {
                // Fall through
            }

            if (File.Exists(targetPath))
            {
                try
                {
                    using (Icon assoc = Icon.ExtractAssociatedIcon(targetPath))
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
                catch
                {
                    // Ignore
                }
            }

            return "";
        }

        [STAThread]
        static int Main(string[] args)
        {
            if (args.Length < 2) return 1;
            string cmd = args[0];

            try
            {
                if (cmd == "pin-to-desktop")
                {
                    long val = long.Parse(args[1]);
                    EnsureOnDesktop(new IntPtr(val), true);
                    return 0;
                }
                if (cmd == "keep-bottom")
                {
                    long val = long.Parse(args[1]);
                    EnsureOnDesktop(new IntPtr(val), false);
                    return 0;
                }
                if (cmd == "watch-desktop")
                {
                    long val = long.Parse(args[1]);
                    int parentPid = args.Length >= 3 ? int.Parse(args[2]) : 0;
                    WatchDesktopLoop(new IntPtr(val), parentPid);
                    return 0;
                }
                if (cmd == "notify-desktop-file")
                {
                    NotifyDesktopFileRestored(args[1]);
                    return 0;
                }
                if (cmd == "extract-icon")
                {
                    string dataUrl = ExtractHighResIcon(args[1]);
                    Console.Out.Write(dataUrl);
                    return 0;
                }
            }
            catch
            {
                return 1;
            }
            return 0;
        }
    }
}
