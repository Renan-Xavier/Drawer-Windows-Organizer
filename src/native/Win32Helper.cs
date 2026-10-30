using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;

namespace DrawerWin32
{
    class Program
    {
        private const int GWL_STYLE = -16;
        private const int GWL_EXSTYLE = -20;

        private const long WS_POPUP = 0x80000000L;
        private const long WS_VISIBLE = 0x10000000L;
        private const long WS_CAPTION = 0x00C00000L;
        private const long WS_THICKFRAME = 0x00040000L;
        private const long WS_MINIMIZEBOX = 0x00020000L;
        private const long WS_MAXIMIZEBOX = 0x00010000L;
        private const long WS_SYSMENU = 0x00080000L;
        private const long WS_BORDER = 0x00800000L;
        private const long WS_DLGFRAME = 0x00400000L;

        private const long WS_EX_TOOLWINDOW = 0x00000080L;
        private const long WS_EX_NOACTIVATE = 0x08000000L;
        private const long WS_EX_APPWINDOW = 0x00040000L;

        private static readonly IntPtr HWND_BOTTOM = new IntPtr(1);

        private const uint SWP_NOSIZE = 0x0001;
        private const uint SWP_NOMOVE = 0x0002;
        private const uint SWP_NOACTIVATE = 0x0010;
        private const uint SWP_FRAMECHANGED = 0x0020;
        private const uint SWP_NOSENDCHANGING = 0x0400;

        [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")]
        private static extern IntPtr GetWindowLongPtr(IntPtr hWnd, int nIndex);

        [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")]
        private static extern IntPtr SetWindowLongPtr(IntPtr hWnd, int nIndex, IntPtr dwNewLong);

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

        static void PinToDesktop(IntPtr hwnd)
        {
            long style = GetWindowLongPtr(hwnd, GWL_STYLE).ToInt64();
            style &= ~(WS_CAPTION | WS_THICKFRAME | WS_MINIMIZEBOX | WS_MAXIMIZEBOX | WS_SYSMENU | WS_BORDER | WS_DLGFRAME);
            style |= (WS_POPUP | WS_VISIBLE);
            SetWindowLongPtr(hwnd, GWL_STYLE, new IntPtr(style));

            long exStyle = GetWindowLongPtr(hwnd, GWL_EXSTYLE).ToInt64();
            exStyle &= ~WS_EX_APPWINDOW;
            exStyle |= (WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE);
            SetWindowLongPtr(hwnd, GWL_EXSTYLE, new IntPtr(exStyle));

            SetWindowPos(
                hwnd,
                HWND_BOTTOM,
                0,
                0,
                0,
                0,
                SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE | SWP_FRAMECHANGED | SWP_NOSENDCHANGING
            );
        }

        static void KeepBottom(IntPtr hwnd)
        {
            SetWindowPos(
                hwnd,
                HWND_BOTTOM,
                0,
                0,
                0,
                0,
                SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE | SWP_NOSENDCHANGING
            );
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
                // HBITMAP from IShellItemImageFactory is bottom-up when bmHeight > 0
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

            // 1. If it's a direct .ico file (e.g. resolved from Steam .url or .lnk), load largest frame directly
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

            // 2. Use Windows Shell IShellItemImageFactory (exact icon rendered by Windows Explorer)
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

            // 3. Fallback: Icon.ExtractAssociatedIcon
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
                    PinToDesktop(new IntPtr(val));
                    return 0;
                }
                if (cmd == "keep-bottom")
                {
                    long val = long.Parse(args[1]);
                    KeepBottom(new IntPtr(val));
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
