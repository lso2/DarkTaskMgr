using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;

namespace DarkTaskMgr.UI
{
    /// <summary>
    /// Maps a 0..1 intensity to the amber cell shade that gives the Processes and Details tables their
    /// heat-map look, where a busier cell is a stronger amber over the row. Zero is fully transparent
    /// so idle rows read clean, which is why the alpha scales from nothing rather than a floor.
    /// </summary>
    public sealed class HeatConverter : IValueConverter
    {
        private static readonly Color Amber = Color.FromRgb(0xE0, 0xA7, 0x3A);

        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            double f = ToDouble(value);
            if (f <= 0.001) return Brushes.Transparent;
            if (f > 1) f = 1;
            byte a = (byte)(f * 150);
            var b = new SolidColorBrush(Color.FromArgb(a, Amber.R, Amber.G, Amber.B));
            b.Freeze();
            return b;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotSupportedException();
        }

        private static double ToDouble(object v)
        {
            try { return v == null ? 0 : System.Convert.ToDouble(v, CultureInfo.InvariantCulture); }
            catch { return 0; }
        }
    }

    /// <summary>Indents a tree row by its depth, so child processes sit under their application.</summary>
    public sealed class LevelToMarginConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            int level = 0;
            try { level = System.Convert.ToInt32(value); } catch { }
            return new Thickness(level * 17, 0, 0, 0);
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotSupportedException();
        }
    }
}
