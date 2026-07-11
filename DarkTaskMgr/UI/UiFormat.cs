using System;
using System.Globalization;
using System.Windows.Media;

namespace DarkTaskMgr.UI
{
    /// <summary>Shared number formatting and a few colors the code-drawn graph needs. Formats match
    /// the Windows Task Manager columns: memory in megabytes, transfer rates in MB/s, links in Mbps.</summary>
    internal static class UiFormat
    {
        private static readonly CultureInfo Ci = CultureInfo.CurrentCulture;

        public static string Mb(long bytes)
        {
            double mb = bytes / (1024.0 * 1024.0);
            return mb.ToString("#,0.0", Ci) + " MB";
        }

        public static string DiskRate(double bytesPerSec)
        {
            double mbps = bytesPerSec / (1024.0 * 1024.0);
            if (mbps < 0.05) return "0 MB/s";
            return mbps.ToString("#,0.0", Ci) + " MB/s";
        }

        public static string NetRate(double bytesPerSec)
        {
            double mbps = bytesPerSec * 8.0 / 1_000_000.0;
            if (mbps < 0.05) return "0 Mbps";
            return mbps.ToString("#,0.0", Ci) + " Mbps";
        }

        public static string Percent1(double pct)
        {
            if (pct < 0) pct = 0;
            return pct.ToString("0.0", Ci) + "%";
        }

        public static string PercentInt(double pct)
        {
            if (pct < 0) pct = 0;
            return Math.Round(pct).ToString("0", Ci) + "%";
        }

        public static string Gb(ulong bytes)
        {
            double gb = bytes / (1024.0 * 1024.0 * 1024.0);
            return gb.ToString("0.0", Ci) + " GB";
        }

        public static string Ghz(double ghz)
        {
            return ghz.ToString("0.00", Ci) + " GHz";
        }

        public static string Rate(double bytesPerSec)
        {
            double kb = bytesPerSec / 1024.0;
            if (kb < 1) return "0 Kbps";
            if (kb < 1024) return kb.ToString("#,0", Ci) + " KB/s";
            return (kb / 1024.0).ToString("#,0.0", Ci) + " MB/s";
        }

        public static string Uptime(double seconds)
        {
            var t = TimeSpan.FromSeconds(seconds);
            return string.Format("{0:00}:{1:00}:{2:00}:{3:00}", (int)t.TotalDays, t.Hours, t.Minutes, t.Seconds);
        }

        // ---- Colors used by the code-drawn PerfGraph ----
        public static readonly Color Grid = Color.FromRgb(0x2C, 0x31, 0x39);
        public static readonly Color TextSecondary = Color.FromRgb(0x9A, 0xA0, 0xAA);

        public static Color WithA(Color c, byte a) { return Color.FromArgb(a, c.R, c.G, c.B); }

        public static Brush Frozen(Color c)
        {
            var b = new SolidColorBrush(c);
            b.Freeze();
            return b;
        }
    }
}
