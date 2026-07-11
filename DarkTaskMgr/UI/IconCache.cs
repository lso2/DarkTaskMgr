using System;
using System.Collections.Generic;
using System.IO;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace DarkTaskMgr.UI
{
    /// <summary>
    /// Loads and caches the small icon for a process image, keyed by its path so the many workers of
    /// one application share a single extraction. Extraction is guarded and falls back to a generic
    /// glyph, because system and protected images cannot be opened by a normal user and would
    /// otherwise leave a blank cell. Every returned image is frozen so it is safe to reuse anywhere.
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
            img = Extract(path) ?? Generic();
            Cache[path] = img;
            return img;
        }

        private static ImageSource Extract(string path)
        {
            try
            {
                if (!File.Exists(path)) return null;
                using (var ico = System.Drawing.Icon.ExtractAssociatedIcon(path))
                {
                    if (ico == null) return null;
                    var src = Imaging.CreateBitmapSourceFromHIcon(
                        ico.Handle, Int32Rect.Empty, BitmapSizeOptions.FromWidthAndHeight(16, 16));
                    src.Freeze();
                    return src;
                }
            }
            catch { return null; }
        }

        // A neutral rounded square, drawn once, for images that cannot be read.
        private static ImageSource Generic()
        {
            if (_generic != null) return _generic;
            var geo = new RectangleGeometry(new Rect(2, 2, 12, 12), 3, 3);
            var fill = new SolidColorBrush(Color.FromRgb(0x5A, 0x61, 0x6C));
            var pen = new Pen(new SolidColorBrush(Color.FromRgb(0x7A, 0x82, 0x8F)), 1);
            var drawing = new GeometryDrawing(fill, pen, geo);
            var img = new DrawingImage(drawing);
            img.Freeze();
            _generic = img;
            return _generic;
        }
    }
}
