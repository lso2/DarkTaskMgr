using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Media;

namespace DarkTaskMgr.UI
{
    /// <summary>
    /// A single-series area graph drawn directly for speed and for the exact Task Manager look: a fine
    /// grid, a translucent fill under the line, and new samples scrolling in from the right. It is a
    /// FrameworkElement rather than a Control because it has no interactive parts and redraws once a
    /// second, so a plain OnRender with frozen pens is the cheapest way to keep it smooth.
    /// </summary>
    public sealed class PerfGraph : FrameworkElement
    {
        private float[] _values;
        private double _maxY = 100;
        private Color _color = Color.FromRgb(0x4C, 0xA0, 0xFF);
        private Pen _linePen;
        private Brush _fillBrush;

        private static readonly Pen GridPen = MakePen(UiFormat.WithA(UiFormat.Grid, 140), 1);
        private static readonly Pen BorderPen = MakePen(UiFormat.WithA(UiFormat.Grid, 210), 1);

        public void SetData(float[] values, Color color, double maxY)
        {
            _values = values;
            _maxY = maxY <= 0 ? 100 : maxY;
            if (color != _color || _linePen == null)
            {
                _color = color;
                _linePen = MakePen(color, 1.6);
                var f = new SolidColorBrush(UiFormat.WithA(color, 56));
                f.Freeze();
                _fillBrush = f;
            }
            InvalidateVisual();
        }

        protected override void OnRender(DrawingContext dc)
        {
            double w = ActualWidth, h = ActualHeight;
            if (w <= 4 || h <= 4) return;

            var area = new Rect(0, 0, w, h);
            dc.DrawRectangle(null, BorderPen, area);

            // Fine grid, 10 columns by 10 rows, the way the Performance tab draws it.
            for (int i = 1; i < 10; i++)
            {
                double x = w * i / 10.0;
                dc.DrawLine(GridPen, new Point(x, 0), new Point(x, h));
                double y = h * i / 10.0;
                dc.DrawLine(GridPen, new Point(0, y), new Point(w, y));
            }

            if (_values == null || _values.Length < 2 || _linePen == null) return;

            int n = _values.Length;
            double step = w / (n - 1);

            var line = new StreamGeometry();
            using (var ctx = line.Open())
            {
                ctx.BeginFigure(Map(0, n, _values[0], w, h, step), false, false);
                var pts = new List<Point>(n - 1);
                for (int i = 1; i < n; i++) pts.Add(Map(i, n, _values[i], w, h, step));
                ctx.PolyLineTo(pts, true, false);
            }
            line.Freeze();

            var fill = new StreamGeometry();
            using (var ctx = fill.Open())
            {
                ctx.BeginFigure(new Point(0, h), true, true);
                ctx.LineTo(Map(0, n, _values[0], w, h, step), true, false);
                var pts = new List<Point>(n);
                for (int i = 1; i < n; i++) pts.Add(Map(i, n, _values[i], w, h, step));
                pts.Add(new Point(w, h));
                ctx.PolyLineTo(pts, true, false);
            }
            fill.Freeze();

            dc.DrawGeometry(_fillBrush, null, fill);
            dc.DrawGeometry(null, _linePen, line);
        }

        private Point Map(int i, int n, float v, double w, double h, double step)
        {
            double x = i * step;
            double frac = v / _maxY;
            if (frac < 0) frac = 0; if (frac > 1) frac = 1;
            double y = h - frac * h;
            return new Point(x, y);
        }

        private static Pen MakePen(Color c, double thickness)
        {
            var b = new SolidColorBrush(c); b.Freeze();
            var p = new Pen(b, thickness) { LineJoin = PenLineJoin.Round };
            p.Freeze();
            return p;
        }
    }
}
