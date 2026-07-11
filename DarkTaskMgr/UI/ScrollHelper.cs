using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace DarkTaskMgr.UI
{
    /// <summary>
    /// Slows and smooths mouse-wheel scrolling on a list. The default wheel step scrolls several rows
    /// at once, which felt too fast, so this scrolls a fixed, smaller number of pixels per notch
    /// against the list's inner ScrollViewer. It is attached from the shared list style, so every
    /// table gets it without per-view code, and it relies on the list using pixel scroll units.
    /// </summary>
    public static class ScrollHelper
    {
        private const double PixelsPerNotch = 46;

        public static readonly DependencyProperty SmoothProperty =
            DependencyProperty.RegisterAttached("Smooth", typeof(bool), typeof(ScrollHelper),
                new PropertyMetadata(false, OnSmoothChanged));

        public static bool GetSmooth(DependencyObject o) { return (bool)o.GetValue(SmoothProperty); }
        public static void SetSmooth(DependencyObject o, bool v) { o.SetValue(SmoothProperty, v); }

        private static void OnSmoothChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            var el = d as UIElement;
            if (el == null) return;
            if ((bool)e.NewValue) el.PreviewMouseWheel += OnWheel;
            else el.PreviewMouseWheel -= OnWheel;
        }

        private static void OnWheel(object sender, MouseWheelEventArgs e)
        {
            var sv = FindScrollViewer(sender as DependencyObject);
            if (sv == null) return;
            double target = sv.VerticalOffset - (e.Delta / 120.0) * PixelsPerNotch;
            if (target < 0) target = 0;
            if (target > sv.ScrollableHeight) target = sv.ScrollableHeight;
            sv.ScrollToVerticalOffset(target);
            e.Handled = true;
        }

        private static ScrollViewer FindScrollViewer(DependencyObject root)
        {
            if (root == null) return null;
            if (root is ScrollViewer sv) return sv;
            int n = VisualTreeHelper.GetChildrenCount(root);
            for (int i = 0; i < n; i++)
            {
                var found = FindScrollViewer(VisualTreeHelper.GetChild(root, i));
                if (found != null) return found;
            }
            return null;
        }
    }
}
