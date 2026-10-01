using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;

namespace SlabRebar
{
    /// <summary>Colores de los esquemas, compartidos por la planta, la seccion y la leyenda.</summary>
    public static class PlanColors
    {
        public static readonly Brush Concrete = Freeze(new SolidColorBrush(Color.FromRgb(0xE6, 0xE6, 0xE6)));
        public static readonly Brush Bottom = Freeze(new SolidColorBrush(Color.FromRgb(0x8B, 0x2E, 0x2E)));       // inferior principal / de vigueta
        public static readonly Brush Top = Freeze(new SolidColorBrush(Color.FromRgb(0xD9, 0x6C, 0x2A)));          // bastones / superior principal
        public static readonly Brush Temperature = Freeze(new SolidColorBrush(Color.FromRgb(0x7A, 0x3E, 0x9D)));  // temperatura / inferior secundaria
        public static readonly Brush TopSecondary = Freeze(new SolidColorBrush(Color.FromRgb(0x3B, 0x6F, 0xB6))); // superior secundaria
        public static readonly Brush Joist = Freeze(new SolidColorBrush(Color.FromArgb(0x38, 0x1F, 0x7A, 0x7A))); // franja de vigueta
        public static readonly Brush JoistEdge = Freeze(new SolidColorBrush(Color.FromRgb(0x1F, 0x7A, 0x7A)));
        public static readonly Brush Support = Freeze(new SolidColorBrush(Color.FromArgb(0x55, 0x80, 0x80, 0x80))); // viga de apoyo
        public static readonly Brush SupportEdge = Freeze(new SolidColorBrush(Color.FromRgb(0x60, 0x60, 0x60)));
        public static readonly Brush Brick = Freeze(new SolidColorBrush(Color.FromRgb(0xE8, 0xC9, 0xA0)));        // ladrillo de techo
        public static readonly Brush BrickEdge = Freeze(new SolidColorBrush(Color.FromRgb(0x9A, 0x5A, 0x1A)));

        private static Brush Freeze(Brush b) { b.Freeze(); return b; }

        public static Brush Of(BarLayer l)
        {
            switch (l)
            {
                case BarLayer.JoistBottom: case BarLayer.BottomMain: return Bottom;
                case BarLayer.JoistTop: case BarLayer.TopMain: return Top;
                case BarLayer.Temperature: case BarLayer.BottomSecondary: return Temperature;
                default: return TopSecondary;
            }
        }
    }

    /// <summary>
    /// Esquema en planta de la losa con su armado: hormigon con sus huecos, vigas de apoyo,
    /// franjas de las viguetas y cada barra con su color de capa (las prolongaciones hacia
    /// las vigas a trazos, los ganchos con una marca). Zoom con la rueda (centrado en el
    /// cursor), desplazamiento arrastrando y doble clic para volver a encajar. Toda la
    /// geometria sale de SlabPlan, la misma clase que usa el generador.
    /// </summary>
    public sealed class PlanPreview : Canvas
    {
        private SlabFrame _f;
        private SlabPlan _plan;
        private string _message = "Sin elemento armable";

        private double _zoom = 1;
        private Vector _pan;
        private double _x0, _y0;
        private Point _dragStart;
        private Vector _panStart;
        private bool _dragging;

        private const double FtToMm = 304.8;

        public PlanPreview()
        {
            Background = Brushes.White;
            ClipToBounds = true;
            SizeChanged += (s, e) => Redraw();
            MouseWheel += OnWheel;
            MouseLeftButtonDown += OnDown;
            MouseMove += OnMove;
            MouseLeftButtonUp += OnUp;
            MouseLeave += (s, e) => { _dragging = false; ReleaseMouseCapture(); };
            Cursor = Cursors.Hand;
        }

        public void Show(SlabFrame f, SlabPlan plan)
        {
            bool changed = !ReferenceEquals(_f, f);
            _f = f; _plan = plan;
            if (changed) ResetView(); else Redraw();
        }

        public void Clear(string message)
        {
            _f = null; _plan = null; _message = message;
            Redraw();
        }

        public void ResetView()
        {
            _zoom = 1; _pan = new Vector(0, 0);
            Redraw();
        }

        // --- zoom y desplazamiento ---
        private void OnWheel(object sender, MouseWheelEventArgs e)
        {
            if (_f == null) return;
            double factor = e.Delta > 0 ? 1.25 : 1 / 1.25;
            double newZoom = Math.Max(1, Math.Min(60, _zoom * factor));
            factor = newZoom / _zoom;
            Point m = e.GetPosition(this);
            _pan = new Vector(m.X - _x0 - (m.X - _pan.X - _x0) * factor, m.Y - _y0 - (m.Y - _pan.Y - _y0) * factor);
            _zoom = newZoom;
            if (_zoom <= 1.0001) _pan = new Vector(0, 0);
            Redraw();
            e.Handled = true;
        }

        private void OnDown(object sender, MouseButtonEventArgs e)
        {
            if (_f == null) return;
            if (e.ClickCount == 2) { ResetView(); e.Handled = true; return; }
            _dragging = true; _dragStart = e.GetPosition(this); _panStart = _pan;
            CaptureMouse();
        }

        private void OnMove(object sender, MouseEventArgs e)
        {
            if (!_dragging) return;
            _pan = _panStart + (e.GetPosition(this) - _dragStart);
            Redraw();
        }

        private void OnUp(object sender, MouseButtonEventArgs e)
        {
            _dragging = false;
            ReleaseMouseCapture();
        }

        // --- dibujo ---
        private static string Mm(double ft) => Math.Round(ft * FtToMm).ToString(CultureInfo.InvariantCulture);
        private static string M(double ft) => (ft * 0.3048).ToString("0.00", CultureInfo.InvariantCulture) + " m";

        private void Redraw()
        {
            Children.Clear();
            double W = ActualWidth, H = ActualHeight;
            if (W < 10 || H < 10) return;
            if (_f == null || _plan == null)
            {
                Text(_message, 10, 10, Brushes.Gray, 12);
                return;
            }

            Outline2D o = _f.Outline;
            double margin = 46;
            double k = Math.Min((W - 2 * margin) / Math.Max(o.Width, 1e-6), (H - 2 * margin) / Math.Max(o.Depth, 1e-6)) * _zoom;
            // origen sin zoom (esquina inferior izquierda de la losa); el zoom crece desde ahi y el desplazamiento se suma
            _x0 = 0.5 * (W - o.Width * k / _zoom);
            _y0 = 0.5 * (H + o.Depth * k / _zoom);
            double x0 = _x0 + _pan.X, y0 = _y0 + _pan.Y;
            Func<double, double> X = u => x0 + u * k;
            Func<double, double> Y = v => y0 - v * k;

            // hormigon con huecos (regla par-impar)
            Geometry outlineGeo = OutlineGeometry(o, X, Y);
            Children.Add(new Path { Data = outlineGeo, Fill = PlanColors.Concrete, Stroke = Brushes.DimGray, StrokeThickness = 1.2 });

            // vigas de apoyo (franjas grises, por debajo de la losa pero visibles)
            foreach (Support s in _plan.Supports)
            {
                var r = new Rectangle
                {
                    Width = Math.Max(1, s.Width * k), Height = Math.Max(1, (s.V2 - s.V1) * k),
                    Fill = PlanColors.Support, Stroke = PlanColors.SupportEdge, StrokeThickness = 1, StrokeDashArray = new DoubleCollection { 4, 3 },
                    ToolTip = "Apoyo: " + s.Name + ", ancho " + Mm(s.Width) + " mm, eje en u=" + Mm(s.CU) + " mm"
                };
                SetLeft(r, X(s.U1)); SetTop(r, Y(s.V2));
                Children.Add(r);
                Text(s.Name, X(s.U1) + 2, Math.Max(2, Y(s.V2)) + 2, PlanColors.SupportEdge, 9);
            }

            // viguetas (franjas recortadas al contorno)
            if (_plan.Kind == SlabKind.Aligerada && _plan.Joists.Count > 0)
            {
                var strips = new Canvas { Clip = outlineGeo };
                int i = 0;
                foreach (Joist j in _plan.Joists)
                {
                    i++;
                    var r = new Rectangle
                    {
                        Width = Math.Max(1, o.Width * k), Height = Math.Max(1, j.Width * k), Fill = PlanColors.Joist,
                        ToolTip = "Vigueta " + i + ": eje v=" + Mm(j.Axis) + " mm, ancho " + Mm(j.Width) + " mm"
                    };
                    SetLeft(r, X(0)); SetTop(r, Y(j.V2));
                    strips.Children.Add(r);
                }
                Children.Add(strips);
                if (_plan.JoistSpacing * k >= 16)
                {
                    i = 0;
                    foreach (Joist j in _plan.Joists)
                        Text("V" + (++i), X(0) - 22, Y(j.Axis) - 7, PlanColors.JoistEdge, 9);
                }
            }

            // cotas generales
            Text(M(o.Width) + "  (u)", X(o.Width / 2) - 30, Y(0) + 20, Brushes.DimGray, 11);
            Text(M(o.Depth) + "  (v)", X(o.Width) + 6, Y(o.Depth / 2) - 8, Brushes.DimGray, 11);
            Arrow(X(0), Y(0) + 34, X(0) + 40, Y(0) + 34, Brushes.DimGray);
            Text("u (barras principales / viguetas)", X(0) + 44, Y(0) + 27, Brushes.DimGray, 9);

            if (_plan.Error != null)
            {
                Text(_plan.Error, 10, 10, Brushes.Firebrick, 12);
                return;
            }

            // barras: primero las inferiores, encima las superiores
            foreach (PlannedBar b in _plan.Bars.OrderBy(b => b.Z))
            {
                Brush brush = PlanColors.Of(b.Layer);
                double th = Math.Max(1.1, b.D * k);
                string tip = Layers.Name(b.Layer) + " Ø" + (b.D * FtToMm).ToString("0.#", CultureInfo.InvariantCulture) + " mm, " +
                             (b.AlongU ? "v=" : "u=") + Mm(b.Coord) + " mm, L=" + Mm(b.Length) + " mm (de " + Mm(b.Start) + " a " + Mm(b.End) +
                             "), cota " + Mm(b.Z) + " mm desde abajo" + (b.ExtendsStart || b.ExtendsEnd ? ", con prolongacion" : "") +
                             (b.HookStart || b.HookEnd ? ", con gancho" : "");
                // tramo dentro de la losa (continuo) y prolongaciones (a trazos)
                Segment(b, b.InA, b.InB, X, Y, brush, th, false, tip);
                if (b.ExtendsStart) Segment(b, b.Start, b.InA, X, Y, brush, th, true, tip);
                if (b.ExtendsEnd) Segment(b, b.InB, b.End, X, Y, brush, th, true, tip);
                // marcas de gancho
                double tick = Math.Max(4, 1.5 * th);
                if (b.HookStart) Tick(b, b.Start, X, Y, brush, th, tick);
                if (b.HookEnd) Tick(b, b.End, X, Y, brush, th, tick);
            }

            // resumen
            Text(_plan.Describe() + " | " + _plan.DescribeLayers(), 8, H - 20, Brushes.DimGray, 11);
            if (_plan.Warnings.Count > 0) Text(string.Join(" | ", _plan.Warnings), 8, H - 36, Brushes.Firebrick, 11);
        }

        private static Geometry OutlineGeometry(Outline2D o, Func<double, double> X, Func<double, double> Y)
        {
            var geo = new PathGeometry { FillRule = FillRule.EvenOdd };
            foreach (List<Pt> ring in o.Rings())
            {
                var fig = new PathFigure { StartPoint = new Point(X(ring[0].U), Y(ring[0].V)), IsClosed = true, IsFilled = true };
                for (int i = 1; i < ring.Count; i++) fig.Segments.Add(new LineSegment(new Point(X(ring[i].U), Y(ring[i].V)), true));
                geo.Figures.Add(fig);
            }
            geo.Freeze();
            return geo;
        }

        private void Segment(PlannedBar b, double a0, double a1, Func<double, double> X, Func<double, double> Y, Brush brush, double th, bool dashed, string tip)
        {
            if (a1 - a0 <= 1e-9) return;
            var ln = new Line
            {
                X1 = b.AlongU ? X(a0) : X(b.Coord), Y1 = b.AlongU ? Y(b.Coord) : Y(a0),
                X2 = b.AlongU ? X(a1) : X(b.Coord), Y2 = b.AlongU ? Y(b.Coord) : Y(a1),
                Stroke = brush, StrokeThickness = th, ToolTip = tip, StrokeStartLineCap = PenLineCap.Flat, StrokeEndLineCap = PenLineCap.Flat
            };
            if (dashed) ln.StrokeDashArray = new DoubleCollection { 3, 2 };
            Children.Add(ln);
        }

        private void Tick(PlannedBar b, double at, Func<double, double> X, Func<double, double> Y, Brush brush, double th, double len)
        {
            double cx = b.AlongU ? X(at) : X(b.Coord), cy = b.AlongU ? Y(b.Coord) : Y(at);
            Children.Add(new Line
            {
                X1 = b.AlongU ? cx : cx - len, Y1 = b.AlongU ? cy - len : cy,
                X2 = b.AlongU ? cx : cx + len, Y2 = b.AlongU ? cy + len : cy,
                Stroke = brush, StrokeThickness = Math.Max(1, th * 0.8)
            });
        }

        private void Arrow(double x1, double y1, double x2, double y2, Brush brush)
        {
            Children.Add(new Line { X1 = x1, Y1 = y1, X2 = x2, Y2 = y2, Stroke = brush, StrokeThickness = 1 });
            Children.Add(new Line { X1 = x2, Y1 = y2, X2 = x2 - 5, Y2 = y2 - 3, Stroke = brush, StrokeThickness = 1 });
            Children.Add(new Line { X1 = x2, Y1 = y2, X2 = x2 - 5, Y2 = y2 + 3, Stroke = brush, StrokeThickness = 1 });
        }

        private TextBlock Text(string s, double x, double y, Brush brush, double size, bool bold = false)
        {
            var t = new TextBlock { Text = s, Foreground = brush, FontSize = size, TextWrapping = TextWrapping.NoWrap };
            if (bold) t.FontWeight = FontWeights.SemiBold;
            SetLeft(t, x); SetTop(t, y);
            Children.Add(t);
            return t;
        }
    }
}
