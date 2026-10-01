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
    /// <summary>
    /// Esquema de la seccion transversal de la losa (plano v-z, perpendicular a las viguetas
    /// o a las barras principales) cortada a media luz: el hormigon con su espesor, los
    /// ladrillos y las viguetas de la aligerada con la losa superior, cada barra que cruza el
    /// corte como un circulo a su diametro (las que no cruzan, como los bastones de los
    /// apoyos, en hueco) y el acero perpendicular (temperatura, secundaria) como una raya a
    /// su cota. Como la losa es muy ancha respecto a su espesor, al encajar se muestran unas
    /// pocas viguetas centradas y se puede ampliar, reducir y desplazar. Rueda: zoom;
    /// arrastrar: mover; doble clic: encajar.
    /// </summary>
    public sealed class SectionPreview : Canvas
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

        public SectionPreview()
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
            bool changed = !ReferenceEquals(_f, f) || (_plan != null && plan != null && _plan.Kind != plan.Kind);
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
            double newZoom = Math.Max(0.05, Math.Min(60, _zoom * factor));
            factor = newZoom / _zoom;
            Point m = e.GetPosition(this);
            _pan = new Vector(m.X - _x0 - (m.X - _pan.X - _x0) * factor, m.Y - _y0 - (m.Y - _pan.Y - _y0) * factor);
            _zoom = newZoom;
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
            double t = _plan.Thickness;
            // ancho visible al encajar: unas tres viguetas (aligerada) o 1.5 m (maciza), centrado en la losa
            double visible = Math.Min(o.Depth, _plan.Kind == SlabKind.Aligerada && _plan.Joists.Count > 0
                ? Math.Min(o.Depth, 3 * _plan.JoistSpacing + _plan.JoistWidth) : Math.Min(o.Depth, 1500 / FtToMm));
            double margin = 40;
            double k = Math.Min((W - 2 * margin) / Math.Max(visible, 1e-6), (H - 2 * margin) / Math.Max(t, 1e-6)) * _zoom;
            double vc = 0.5 * (o.VMin + o.VMax);
            // origen sin zoom: el centro de la losa en el centro del lienzo, cara inferior abajo
            _x0 = 0.5 * W - vc * k / _zoom;
            _y0 = 0.5 * (H + t * k / _zoom);
            double x0 = _x0 + _pan.X, y0 = _y0 + _pan.Y;
            Func<double, double> X = v => x0 + v * k;
            Func<double, double> Y = z => y0 - z * k;
            double uCut = 0.5 * (o.UMin + o.UMax);

            // hormigon
            var slab = new Rectangle { Width = Math.Max(1, o.Depth * k), Height = Math.Max(1, t * k), Fill = PlanColors.Concrete, Stroke = Brushes.DimGray, StrokeThickness = 1.2 };
            SetLeft(slab, X(o.VMin)); SetTop(slab, Y(t));
            Children.Add(slab);

            // ladrillos de techo entre viguetas (aligerada)
            if (_plan.Kind == SlabKind.Aligerada && _plan.Joists.Count > 0 && _plan.Error == null)
            {
                double hb = t - _plan.TopSlab;
                var gaps = new List<(double a, double b)>();
                double prev = o.VMin;
                foreach (Joist j in _plan.Joists)
                {
                    if (j.V1 - prev > Mm1(20)) gaps.Add((prev, j.V1));
                    prev = j.V2;
                }
                if (o.VMax - prev > Mm1(20)) gaps.Add((prev, o.VMax));
                foreach ((double a, double b) in gaps)
                {
                    var brick = new Rectangle
                    {
                        Width = Math.Max(1, (b - a) * k), Height = Math.Max(1, hb * k), Fill = PlanColors.Brick, Stroke = PlanColors.BrickEdge, StrokeThickness = 0.8,
                        ToolTip = "Ladrillo de techo: " + Mm(b - a) + " x " + Mm(hb) + " mm"
                    };
                    SetLeft(brick, X(a)); SetTop(brick, Y(hb));
                    Children.Add(brick);
                    // alveolos del ladrillo
                    if ((b - a) * k > 30 && hb * k > 14)
                    {
                        int cells = Math.Max(1, Math.Min(4, (int)((b - a) * k / 24)));
                        double cw = (b - a) / cells;
                        for (int c = 0; c < cells; c++)
                        {
                            var hole = new Rectangle
                            {
                                Width = Math.Max(1, cw * k * 0.6), Height = Math.Max(1, hb * k * 0.6), Stroke = PlanColors.BrickEdge, StrokeThickness = 0.6, Fill = null
                            };
                            SetLeft(hole, X(a + cw * (c + 0.2))); SetTop(hole, Y(hb * 0.8));
                            Children.Add(hole);
                        }
                    }
                }
                // etiquetas de vigueta
                if (_plan.JoistSpacing * k >= 40)
                {
                    int i = 0;
                    foreach (Joist j in _plan.Joists)
                        Text("V" + (++i), X(j.Axis) - 8, Y(0) + 3, PlanColors.JoistEdge, 9);
                }
                // linea de la losa superior
                Children.Add(new Line { X1 = X(o.VMin), Y1 = Y(hb), X2 = X(o.VMax), Y2 = Y(hb), Stroke = Brushes.DimGray, StrokeThickness = 0.6, StrokeDashArray = new DoubleCollection { 2, 2 } });
            }

            // cotas
            Text("h = " + Mm(t) + " mm" + (_plan.Kind == SlabKind.Aligerada
                     ? ", losa sup. " + Mm(_plan.TopSlab) + " mm, vigueta " + Mm(_plan.JoistWidth) + " @" + Mm(_plan.JoistSpacing) : ""),
                 8, 6, Brushes.DimGray, 10);
            Text("corte a u = " + (uCut * 0.3048).ToString("0.00", CultureInfo.InvariantCulture) + " m (media luz); v de 0 a " + Mm(o.Depth) + " mm", 8, H - 18, Brushes.DimGray, 10);

            if (_plan.Error != null) { Text(_plan.Error, 10, 24, Brushes.Firebrick, 12); return; }

            // acero perpendicular (a lo largo de v): la barra mas cercana al corte de cada capa, como una raya a su cota
            foreach (var layer in _plan.Bars.Where(b => !b.AlongU).GroupBy(b => b.Layer))
            {
                PlannedBar b = layer.OrderBy(x => Math.Abs(x.Coord - uCut)).First();
                Brush brush = PlanColors.Of(b.Layer);
                double th = Math.Max(1.2, b.D * k);
                var ln = new Line
                {
                    X1 = X(b.Start), Y1 = Y(b.Z), X2 = X(b.End), Y2 = Y(b.Z), Stroke = brush, StrokeThickness = th,
                    ToolTip = Layers.Name(b.Layer) + " Ø" + (b.D * FtToMm).ToString("0.#", CultureInfo.InvariantCulture) + " mm a " + Mm(b.Z) + " mm desde abajo (la mas cercana al corte, u=" + Mm(b.Coord) + ")"
                };
                Children.Add(ln);
                Text(Layers.Name(b.Layer) + " Ø" + (b.D * FtToMm).ToString("0.#", CultureInfo.InvariantCulture), X(o.VMin) + 4, Y(b.Z) - 14, brush, 9);
            }

            // barras a lo largo de u: circulos (llenos si cruzan el corte, huecos si no, p. ej. bastones de los apoyos)
            foreach (PlannedBar b in _plan.Bars.Where(b => b.AlongU))
            {
                bool crosses = b.InA <= uCut && b.InB >= uCut;
                double rr = Math.Max(2.2, 0.5 * b.D * k);
                Brush brush = PlanColors.Of(b.Layer);
                var e = new Ellipse
                {
                    Width = 2 * rr, Height = 2 * rr,
                    Fill = crosses ? brush : Brushes.White, Stroke = crosses ? Brushes.Black : brush, StrokeThickness = crosses ? 0.6 : 1.2,
                    ToolTip = Layers.Name(b.Layer) + " Ø" + (b.D * FtToMm).ToString("0.#", CultureInfo.InvariantCulture) + " mm en v=" + Mm(b.Coord) + ", cota " + Mm(b.Z) +
                              " mm" + (crosses ? "" : " (no cruza el corte: baston de apoyo, de u=" + Mm(b.Start) + " a " + Mm(b.End) + ")")
                };
                if (!crosses) e.StrokeDashArray = new DoubleCollection { 2, 1.5 };
                SetLeft(e, X(b.Coord) - rr); SetTop(e, Y(b.Z) - rr);
                Children.Add(e);
            }
        }

        private static double Mm1(double mm) => mm / FtToMm;

        private TextBlock Text(string s, double x, double y, Brush brush, double size)
        {
            var t = new TextBlock { Text = s, Foreground = brush, FontSize = size, TextWrapping = TextWrapping.NoWrap };
            SetLeft(t, x); SetTop(t, y);
            Children.Add(t);
            return t;
        }
    }
}
