using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace DarkTaskMgr.UI
{
    /// <summary>
    /// Loads and caches the small icon for a process image, keyed by its path. Icons are pulled with
    /// the shell (SHGetFileInfo), which succeeds for more images than the managed extractor, and every
    /// lookup is guaranteed to return something: a resolved file gives its real icon, and an
    /// unresolved one falls back to the Windows default-application icon rather than a blank square,
    /// so a row is never left without an icon. Every image is frozen for reuse on any thread.
    /// </summary>
    internal static class IconCache
    {
        private static readonly Dictionary<string, ImageSource> Cache =
            new Dictionary<string, ImageSource>(StringComparer.OrdinalIgnoreCase);
        private static ImageSource _generic;

        public static ImageSource ForPath(string path)
        {
            if (string.IsNullOrEmpty(path)) return Generic();
            if (Cache.TryGetValue(path, out var img)) return img;
            img = FromFile(path) ?? Generic();
            Cache[path] = img;
            return img;
        }

        private static ImageSource FromFile(string path)
        {
            if (!File.Exists(path)) return null;
            return ShellIcon(path, false) ?? Managed(path);
        }

        private static ImageSource Managed(string path)
        {
            try
            {
                using (var ico = System.Drawing.Icon.ExtractAssociatedIcon(path))
                {
                    if (ico == null) return null;
                    var src = Imaging.CreateBitmapSourceFromHIcon(ico.Handle, Int32Rect.Empty, BitmapSizeOptions.FromWidthAndHeight(16, 16));
                    src.Freeze();
                    return src;
                }
            }
            catch { return null; }
        }

        // The Windows generic-application icon, obtained by asking the shell for a plain ".exe" type
        // with USEFILEATTRIBUTES so no real file is needed. This is what shows for the few processes
        // that have no image on disk (System, memory compression), so they look intentional.
        private static ImageSource Generic()
        {
            if (_generic != null) return _generic;
            _generic = ShellIcon("application.exe", true) ?? Drawn();
            return _generic;
        }

        private static ImageSource Drawn()
        {
            var geo = new RectangleGeometry(new Rect(2, 2, 12, 12), 3, 3);
            var fill = new SolidColorBrush(Color.FromRgb(0x5A, 0x61, 0x6C));
            var pen = new Pen(new SolidColorBrush(Color.FromRgb(0x7A, 0x82, 0x8F)), 1);
            var img = new DrawingImage(new GeometryDrawing(fill, pen, geo));
            img.Freeze();
            return img;
        }

        private static ImageSource ShellIcon(string path, bool useAttributes)
        {
            var info = new SHFILEINFO();
            uint flags = SHGFI_ICON | SHGFI_SMALLICON;
            if (useAttributes) flags |= SHGFI_USEFILEATTRIBUTES;
            IntPtr res = IntPtr.Zero;
            try
            {
                res = SHGetFileInfo(path, FILE_ATTRIBUTE_NORMAL, ref info, (uint)Marshal.SizeOf(typeof(SHFILEINFO)), flags);
                if (info.hIcon == IntPtr.Zero) return null;
                var src = Imaging.CreateBitmapSourceFromHIcon(info.hIcon, Int32Rect.Empty, BitmapSizeOptions.FromWidthAndHeight(16, 16));
                src.Freeze();
                return src;
            }
            catch { return null; }
            finally { if (info.hIcon != IntPtr.Zero) DestroyIcon(info.hIcon); }
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct SHFILEINFO
        {
            public IntPtr hIcon;
            public int iIcon;
            public uint dwAttributes;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)] public string szDisplayName;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 80)] public string szTypeName;
        }

        private const uint SHGFI_ICON = 0x100;
        private const uint SHGFI_SMALLICON = 0x1;
        private const uint SHGFI_USEFILEATTRIBUTES = 0x10;
        private const uint FILE_ATTRIBUTE_NORMAL = 0x80;

        [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
        private static extern IntPtr SHGetFileInfo(string pszPath, uint dwFileAttributes, ref SHFILEINFO psfi, uint cbFileInfo, uint uFlags);

        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool DestroyIcon(IntPtr hIcon);
    }
}
