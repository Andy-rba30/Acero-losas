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
    /// su cota. Va acotada como el detalle tipico de losa aligerada: a la derecha el espesor
    /// total, a la izquierda la losa superior y la altura del ladrillo, debajo el ancho de la
    /// vigueta y del ladrillo, y la profundidad del eje de la temperatura y de la barra
    /// inferior desde las caras (una sola vez, en la vigueta del centro, para no saturar).
    /// Como la losa es muy ancha respecto a su espesor, al encajar se muestran unas pocas
    /// viguetas centradas y se puede ampliar, reducir y desplazar. Rueda: zoom; arrastrar:
    /// mover; doble clic: encajar.
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
        /// <summary>Franja libre a cada lado del dibujo donde van las cotas de espesor (px).</summary>
        private const double Gutter = 50;
        /// <summary>Largo de la marca de referencia a cada lado de la linea de cota y de la flecha (px).</summary>
        private const double Tick = 5, Arrow = 6;
        private static readonly Brush DimBrush = Freeze(new SolidColorBrush(Color.FromRgb(0x3C, 0x3C, 0x3C)));
        private static readonly Brush LabelBack = Freeze(new SolidColorBrush(Color.FromArgb(0xE0, 0xFF, 0xFF, 0xFF)));

        private static Brush Freeze(Brush b) { b.Freeze(); return b; }

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
        private static string Dia(double ft) => "Ø" + (ft * FtToMm).ToString("0.#", CultureInfo.InvariantCulture);

        private void Redraw()
        {
            Children.Clear();
            double W = ActualWidth, H = ActualHeight;
            if (W < 10 || H < 10) return;
            if (_f == null || _plan == null)
            {
                Text(this, _message, 10, 10, Brushes.Gray, 12);
                return;
            }

            Outline2D o = _f.Outline;
            double t = _plan.Thickness;
            bool alig = _plan.Kind == SlabKind.Aligerada && _plan.Joists.Count > 0;
            // ancho visible al encajar: unas tres viguetas (aligerada) o 1.5 m (maciza), centrado en la losa
            double visible = Math.Min(o.Depth, alig ? 3 * _plan.JoistSpacing + _plan.JoistWidth : 1500 / FtToMm);
            // margenes: a los lados la franja de las cotas de espesor; arriba el pie de datos; abajo las
            // etiquetas de vigueta, las cotas de vigueta y ladrillo y el pie del corte
            const double marginX = Gutter + 6, marginTop = 30, marginBottom = 58;
            double k = Math.Min((W - 2 * marginX) / Math.Max(visible, 1e-6), (H - marginTop - marginBottom) / Math.Max(t, 1e-6)) * _zoom;
            double vc = 0.5 * (o.VMin + o.VMax);
            // origen sin zoom: el centro de la losa en el centro de la zona de dibujo, cara inferior abajo
            _x0 = 0.5 * W - vc * k / _zoom;
            _y0 = 0.5 * (H + t * k / _zoom) - 0.5 * (marginBottom - marginTop);
            double x0 = _x0 + _pan.X, y0 = _y0 + _pan.Y;
            Func<double, double> X = v => x0 + v * k;
            Func<double, double> Y = z => y0 - z * k;
            double uCut = 0.5 * (o.UMin + o.UMax);

            // el dibujo de la losa va en un lienzo interior recortado a la zona entre las franjas de cotas
            var geom = new Canvas { Clip = new RectangleGeometry(new Rect(Gutter, 0, Math.Max(0, W - 2 * Gutter), H)) };
            Children.Add(geom);
            UIElementCollection g = geom.Children;

            // hormigon
            var slab = new Rectangle { Width = Math.Max(1, o.Depth * k), Height = Math.Max(1, t * k), Fill = PlanColors.Concrete, Stroke = Brushes.DimGray, StrokeThickness = 1.2 };
            SetLeft(slab, X(o.VMin)); SetTop(slab, Y(t));
            g.Add(slab);

            // vigueta mas cercana al centro de la vista y la siguiente: en ellas van las cotas y las etiquetas
            Joist jc = null, jn = null;
            if (alig)
            {
                double vCenter = (0.5 * W - x0) / k;
                jc = _plan.Joists.OrderBy(j => Math.Abs(j.Axis - vCenter)).First();
                jn = _plan.Joists.Where(j => j.Axis > jc.Axis + 1e-9).OrderBy(j => j.Axis).FirstOrDefault();
                if (jn == null)
                {
                    Joist prev = _plan.Joists.Where(j => j.Axis < jc.Axis - 1e-9).OrderByDescending(j => j.Axis).FirstOrDefault();
                    if (prev != null) { jn = jc; jc = prev; }
                }
            }

            // ladrillos de techo entre viguetas (aligerada)
            if (alig && _plan.Error == null)
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
                    g.Add(brick);
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
                            g.Add(hole);
                        }
                    }
                }
                // etiquetas de vigueta
                if (_plan.JoistSpacing * k >= 40)
                {
                    int i = 0;
                    foreach (Joist j in _plan.Joists)
                        Text(geom, "V" + (++i), X(j.Axis) - 8, Y(0) + 3, PlanColors.JoistEdge, 9);
                }
                // linea de la losa superior
                g.Add(new Line { X1 = X(o.VMin), Y1 = Y(hb), X2 = X(o.VMax), Y2 = Y(hb), Stroke = Brushes.DimGray, StrokeThickness = 0.6, StrokeDashArray = new DoubleCollection { 2, 2 } });
            }

            // pies de datos
            Text(this, "h = " + Mm(t) + " mm" + (_plan.Kind == SlabKind.Aligerada
                     ? ", losa sup. " + Mm(_plan.TopSlab) + " mm, vigueta " + Mm(_plan.JoistWidth) + " @" + Mm(_plan.JoistSpacing) : "") + " (cotas en mm)",
                 8, 6, Brushes.DimGray, 10);
            Text(this, "corte a u = " + (uCut * 0.3048).ToString("0.00", CultureInfo.InvariantCulture) + " m (media luz); v de 0 a " + Mm(o.Depth) + " mm", 8, H - 18, Brushes.DimGray, 10);

            if (_plan.Error != null) { Text(this, _plan.Error, 10, 24, Brushes.Firebrick, 12); return; }

            // acero perpendicular (a lo largo de v): la barra mas cercana al corte de cada capa, como una raya a su cota;
            // su etiqueta junto a la vigueta del centro (o al borde visible de la losa en la maciza)
            double xLabel = jc != null ? X(jc.V2) + 16 : Math.Max(X(o.VMin), Gutter) + 4;
            foreach (var layer in _plan.Bars.Where(b => !b.AlongU).GroupBy(b => b.Layer))
            {
                PlannedBar b = layer.OrderBy(x => Math.Abs(x.Coord - uCut)).First();
                Brush brush = PlanColors.Of(b.Layer);
                double th = Math.Max(1.2, b.D * k);
                var ln = new Line
                {
                    X1 = X(b.Start), Y1 = Y(b.Z), X2 = X(b.End), Y2 = Y(b.Z), Stroke = brush, StrokeThickness = th,
                    ToolTip = Layers.Name(b.Layer) + " " + Dia(b.D) + " mm a " + Mm(b.Z) + " mm desde abajo (la mas cercana al corte, u=" + Mm(b.Coord) + ")"
                };
                g.Add(ln);
                Text(geom, Layers.Name(b.Layer) + " " + Dia(b.D), xLabel, Y(b.Z) - 14, brush, 9);
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
                    ToolTip = Layers.Name(b.Layer) + " " + Dia(b.D) + " mm en v=" + Mm(b.Coord) + ", cota " + Mm(b.Z) +
                              " mm" + (crosses ? "" : " (no cruza el corte: baston de apoyo, de u=" + Mm(b.Start) + " a " + Mm(b.End) + ")")
                };
                if (!crosses) e.StrokeDashArray = new DoubleCollection { 2, 1.5 };
                SetLeft(e, X(b.Coord) - rr); SetTop(e, Y(b.Z) - rr);
                g.Add(e);
            }

            // --- cotas, como en el detalle de la losa aligerada ---
            // Espesores a los lados: fuera de la losa si se ve su borde, si no en la franja libre del lienzo,
            // con la linea de referencia hasta la cara o hasta el corte del dibujo.
            bool leftVisible = X(o.VMin) >= Gutter + 8, rightVisible = X(o.VMax) <= W - Gutter - 8;
            double xl = leftVisible ? X(o.VMin) - 30 : Gutter - 22;
            double xr = rightVisible ? X(o.VMax) + 30 : W - Gutter + 22;
            double refL = leftVisible ? X(o.VMin) : Gutter, refR = rightVisible ? X(o.VMax) : W - Gutter;
            foreach (double z in new[] { 0, t }) DimLine(Math.Min(xr - Tick, refR), Y(z), xr + Tick, Y(z), 0.5);
            VDim(xr, Y(0), Y(t), Mm(t), false);
            if (alig && _plan.TopSlab * k >= 12)
            {
                double hb = t - _plan.TopSlab;
                foreach (double z in new[] { 0, hb, t }) DimLine(xl - Tick, Y(z), Math.Max(xl + Tick, refL), Y(z), 0.5);
                VDim(xl, Y(t), Y(hb), Mm(_plan.TopSlab), true);
                VDim(xl, Y(hb), Y(0), Mm(hb), true);
            }
            // Debajo, en la vigueta del centro: ancho de la vigueta y del ladrillo. Junto a ella, la profundidad del
            // eje de la temperatura desde la cara superior y de la barra inferior desde la cara inferior.
            if (jc != null && _plan.JoistWidth * k >= 18)
            {
                double yd = Y(0) + 20;
                foreach (double v in jn != null ? new[] { jc.V1, jc.V2, jn.V1 } : new[] { jc.V1, jc.V2 })
                    DimLine(X(v), Y(0) + 3, X(v), yd + Tick, 0.5);
                HDim(X(jc.V1), X(jc.V2), yd, Mm(jc.Width));
                if (jn != null && jn.V1 - jc.V2 > Mm1(20)) HDim(X(jc.V2), X(jn.V1), yd, Mm(jn.V1 - jc.V2));
                if (_plan.LayerZ.TryGetValue(BarLayer.Temperature, out double zte))
                    VDim(X(jc.V1) - 12, Y(t), Y(zte), Mm(t - zte), true);
                if (_plan.LayerZ.TryGetValue(BarLayer.JoistBottom, out double zjb))
                    VDim(X(jc.V2) + 12, Y(0), Y(zjb), Mm(zjb), false);
            }
        }

        private static double Mm1(double mm) => mm / FtToMm;

        private static TextBlock Text(Panel target, string s, double x, double y, Brush brush, double size)
        {
            var t = new TextBlock { Text = s, Foreground = brush, FontSize = size, TextWrapping = TextWrapping.NoWrap };
            SetLeft(t, x); SetTop(t, y);
            target.Children.Add(t);
            return t;
        }

        // --- cotas ---
        private void DimLine(double x1, double y1, double x2, double y2, double thickness = 0.8)
        {
            Children.Add(new Line { X1 = x1, Y1 = y1, X2 = x2, Y2 = y2, Stroke = DimBrush, StrokeThickness = thickness });
        }

        /// <summary>Punta de flecha con la punta en (x, y) apuntando en la direccion unitaria (dx, dy).</summary>
        private void ArrowHead(double x, double y, double dx, double dy)
        {
            double nx = -dy * Arrow * 0.32, ny = dx * Arrow * 0.32;
            Children.Add(new Polygon
            {
                Fill = DimBrush,
                Points = new PointCollection
                {
                    new Point(x, y),
                    new Point(x - dx * Arrow + nx, y - dy * Arrow + ny),
                    new Point(x - dx * Arrow - nx, y - dy * Arrow - ny)
                }
            });
        }

        /// <summary>Cota vertical en x entre dos alturas de pantalla; el texto a la izquierda o a la derecha de la linea. Si es muy corta, las flechas van por fuera.</summary>
        private void VDim(double x, double ya, double yb, string label, bool textLeft)
        {
            if (ya > yb) { double tmp = ya; ya = yb; yb = tmp; }
            DimLine(x - Tick, ya, x + Tick, ya);
            DimLine(x - Tick, yb, x + Tick, yb);
            if (yb - ya >= 2 * Arrow + 4)
            {
                DimLine(x, ya, x, yb);
                ArrowHead(x, ya, 0, -1); ArrowHead(x, yb, 0, 1);
            }
            else
            {
                DimLine(x, ya - Arrow - 3, x, yb + Arrow + 3);
                ArrowHead(x, ya, 0, 1); ArrowHead(x, yb, 0, -1);
            }
            Label(label, textLeft ? x - Tick - 2 : x + Tick + 2, 0.5 * (ya + yb), textLeft ? -1 : 1, 0);
        }

        /// <summary>Cota horizontal a la altura y entre dos abscisas de pantalla, con el texto debajo. Si es muy corta, las flechas van por fuera.</summary>
        private void HDim(double xa, double xb, double y, string label)
        {
            if (xa > xb) { double tmp = xa; xa = xb; xb = tmp; }
            DimLine(xa, y - Tick, xa, y + Tick);
            DimLine(xb, y - Tick, xb, y + Tick);
            if (xb - xa >= 2 * Arrow + 4)
            {
                DimLine(xa, y, xb, y);
                ArrowHead(xa, y, -1, 0); ArrowHead(xb, y, 1, 0);
            }
            else
            {
                DimLine(xa - Arrow - 3, y, xb + Arrow + 3, y);
                ArrowHead(xa, y, 1, 0); ArrowHead(xb, y, -1, 0);
            }
            Label(label, 0.5 * (xa + xb), y + 3, 0, 1);
        }

        /// <summary>Texto de cota con fondo claro para que se lea sobre el hormigon y el ladrillo. ax / ay: -1 = termina en el punto, 0 = centrado, 1 = empieza en el punto.</summary>
        private void Label(string s, double x, double y, int ax, int ay)
        {
            var b = new Border
            {
                Background = LabelBack, CornerRadius = new CornerRadius(2), Padding = new Thickness(2, 0, 2, 0),
                Child = new TextBlock { Text = s, Foreground = DimBrush, FontSize = 9 }
            };
            b.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            double w = b.DesiredSize.Width, h = b.DesiredSize.Height;
            SetLeft(b, ax < 0 ? x - w : ax == 0 ? x - 0.5 * w : x);
            SetTop(b, ay < 0 ? y - h : ay == 0 ? y - 0.5 * h : y);
            Children.Add(b);
        }
    }
}
