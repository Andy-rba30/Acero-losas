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
    /// o a las barras principales) cortada a media luz, como el detalle tipico de un plano:
    /// no se dibuja la losa entera (satura el esquema) sino un modulo representativo con
    /// marcas de corte a los lados: en la aligerada dos viguetas contiguas del centro con el
    /// ladrillo entre ellas y medio ladrillo a cada lado, en la maciza un metro. Lleva sus
    /// cotas en mm (espesor, losa superior y altura del ladrillo, anchos de vigueta y
    /// ladrillo, recubrimientos), cada barra que cruza el corte como un circulo a su
    /// diametro (las que no cruzan, como los bastones de los apoyos, en hueco) y el acero
    /// perpendicular (temperatura, secundaria) como una raya a su cota con un rotulo de
    /// directriz (diametro y separacion). Rueda: zoom; arrastrar: mover; doble clic: encajar.
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
        private const double ArrowLen = 8, ArrowHalf = 2.5;
        private static readonly Brush DimBrush = Brushes.Black;

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
        private static double Mm1(double mm) => mm / FtToMm;

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
            List<Joist> joists = _plan.Joists;
            bool alig = _plan.Kind == SlabKind.Aligerada && joists.Count > 0 && _plan.Error == null;

            // modulo que se dibuja (en v): dos viguetas contiguas del centro con el ladrillo entre
            // ellas y medio ladrillo a cada lado; maciza (o sin viguetas): 1 m centrado. Lo que
            // queda fuera se indica con una marca de corte en vez de dibujar toda la losa.
            int i0 = 0, i1 = -1;
            double va, vb;
            if (alig)
            {
                int n = joists.Count;
                i0 = Math.Max(0, n / 2 - 1);
                i1 = Math.Min(n - 1, i0 + 1);
                double prev = i0 > 0 ? joists[i0 - 1].V2 : o.VMin;
                double next = i1 < n - 1 ? joists[i1 + 1].V1 : o.VMax;
                va = 0.5 * (prev + joists[i0].V1);
                vb = 0.5 * (joists[i1].V2 + next);
            }
            else
            {
                double half = 0.5 * Math.Min(o.Depth, Mm1(1000));
                double vc = 0.5 * (o.VMin + o.VMax);
                va = vc - half; vb = vc + half;
            }
            double mw = Math.Max(vb - va, Mm1(50));
            bool cutA = va > o.VMin + Mm1(1), cutB = vb < o.VMax - Mm1(1);

            // margenes (px) para las cotas: cadena vertical a la izquierda (losa superior + ladrillo),
            // espesor y recubrimiento inferior a la derecha, recubrimiento superior y rotulo del acero
            // perpendicular arriba, anchos de vigueta y ladrillo y la linea de informacion abajo
            double ml = alig ? 64 : 24, mr = alig ? 72 : 52, mt = 62, mb = alig ? 58 : 60;
            double areaW = Math.Max(W - ml - mr, 20), areaH = Math.Max(H - mt - mb, 20);
            double k = Math.Min(areaW / mw, areaH / Math.Max(t, 1e-6)) * _zoom;
            // origen sin zoom: el centro del modulo en el centro del area de dibujo, cara inferior abajo
            _x0 = ml + 0.5 * areaW - 0.5 * (va + vb) * k / _zoom;
            _y0 = mt + 0.5 * areaH + 0.5 * t * k / _zoom;
            double x0 = _x0 + _pan.X, y0 = _y0 + _pan.Y;
            Func<double, double> X = v => x0 + v * k;
            Func<double, double> Y = z => y0 - z * k;
            double uCut = 0.5 * (o.UMin + o.UMax);
            double xa = X(va), xb = X(vb), yTop = Y(t), yBot = Y(0);

            // hormigon del modulo
            var slab = new Rectangle { Width = Math.Max(1, xb - xa), Height = Math.Max(1, yBot - yTop), Fill = PlanColors.Concrete };
            SetLeft(slab, xa); SetTop(slab, yTop);
            Children.Add(slab);

            // ladrillos de techo entre viguetas (aligerada), recortados al modulo
            double hb = alig ? t - _plan.TopSlab : 0;
            if (alig)
            {
                var gaps = new List<(double a, double b)>();
                double prev = o.VMin;
                foreach (Joist j in joists)
                {
                    if (j.V1 - prev > Mm1(20)) gaps.Add((prev, j.V1));
                    prev = j.V2;
                }
                if (o.VMax - prev > Mm1(20)) gaps.Add((prev, o.VMax));
                foreach ((double a, double b) in gaps)
                {
                    double ca = Math.Max(a, va), cb = Math.Min(b, vb);
                    if (cb - ca < Mm1(10)) continue;
                    Brick(X(ca), X(cb), Y(hb), yBot, cb - ca, hb, a >= va - 1e-9, b <= vb + 1e-9,
                          "Ladrillo de techo: " + Mm(b - a) + " x " + Mm(hb) + " mm");
                }
                // etiquetas de vigueta, dentro de la vigueta a media altura del ladrillo
                if (_plan.JoistWidth * k >= 22 && hb * k >= 16)
                    for (int i = i0; i <= i1; i++)
                        Text("V" + (i + 1), X(joists[i].Axis) - 7, Y(0.5 * hb) - 7, PlanColors.JoistEdge, 9);
            }

            // caras superior e inferior y, en cada extremo, el borde real de la losa o la marca de corte
            Seg(xa, yTop, xb, yTop, Brushes.DimGray, 1.2);
            Seg(xa, yBot, xb, yBot, Brushes.DimGray, 1.2);
            EdgeOrBreak(xa, yTop, yBot, cutA);
            EdgeOrBreak(xb, yTop, yBot, cutB);

            // linea de informacion
            string info = "corte a u = " + (uCut * 0.3048).ToString("0.00", CultureInfo.InvariantCulture) + " m (media luz)";
            if (alig)
                info += " · viguetas V" + (i0 + 1) + (i1 > i0 ? " y V" + (i1 + 1) : "") + " de " + joists.Count + " (ejes @" + Mm(_plan.JoistSpacing) + " mm); el resto queda fuera del dibujo";
            else
                info += " · tramo de " + Mm(vb - va) + " mm de los " + Mm(o.Depth) + " mm de ancho";
            Text(info, 8, H - 18, Brushes.DimGray, 10);

            if (_plan.Error != null) { Text(_plan.Error, 10, 8, Brushes.Firebrick, 12); return; }

            // cabecera: cada capa que cruza el corte (a lo largo de u) con su diametro y su cota desde la cara inferior
            Text(string.Join(" · ", _plan.Bars.Where(b => b.AlongU).GroupBy(b => b.Layer).OrderBy(g => (int)g.Key)
                                        .Select(g => Layers.Name(g.Key) + " " + Dia(g.First().D) + " a cota " + Mm(g.First().Z) + " mm")),
                 8, 4, Brushes.DimGray, 10);

            // cotas (mm) como en el detalle tipico: espesor a la derecha; en la aligerada, losa superior
            // y altura del ladrillo a la izquierda y anchos de vigueta y ladrillo abajo
            DimV(xb + (alig ? 46 : 28), yTop, yBot, xb, Mm(t), false);
            if (alig)
            {
                double yb = Y(hb);
                DimV(xa - 24, yTop, yb, xa, Mm(_plan.TopSlab), true);
                DimV(xa - 24, yb, yBot, xa, Mm(hb), true);
                double yd = yBot + 24;
                for (int i = i0; i <= i1; i++)
                {
                    Joist j = joists[i];
                    DimH(X(j.V1), X(j.V2), yd, yBot, Mm(j.Width));
                    if (i < i1) DimH(X(j.V2), X(joists[i + 1].V1), yd, yBot, Mm(joists[i + 1].V1 - j.V2));
                }
            }

            // recubrimientos: de la cara superior a la capa mas alta y de la cara inferior a la mas baja
            PlannedBar top = _plan.Bars.OrderByDescending(b => b.Z + 0.5 * b.D).FirstOrDefault();
            PlannedBar bottom = _plan.Bars.OrderBy(b => b.Z - 0.5 * b.D).FirstOrDefault();
            if (top != null && t - top.Z - 0.5 * top.D > Mm1(1))
                DimV(X(va + 0.2 * mw), yTop, Y(top.Z + 0.5 * top.D), double.NaN, Mm(t - top.Z - 0.5 * top.D), true);
            if (bottom != null && bottom.Z - 0.5 * bottom.D > Mm1(1))
                DimV(xb + 16, Y(bottom.Z - 0.5 * bottom.D), yBot, double.NaN, Mm(bottom.Z - 0.5 * bottom.D), false);

            // acero perpendicular (a lo largo de v): la recta mas cercana al corte de cada capa, como una raya
            // a su cota, recortada al modulo. Con varios panos (o huecos) esa recta esta partida en un tramo
            // por pano: se dibujan los que caen en el modulo. Rotulo con directriz: capa, diametro y separacion.
            int upLeaders = 0, downLeaders = 0;
            foreach (var layer in _plan.Bars.Where(b => !b.AlongU).GroupBy(b => b.Layer))
            {
                double coord = layer.OrderBy(x => Math.Abs(x.Coord - uCut)).First().Coord;
                List<PlannedBar> line = layer.Where(x => Math.Abs(x.Coord - coord) < 1e-9).OrderBy(x => x.Start).ToList();
                PlannedBar b = line[0];
                Brush brush = PlanColors.Of(b.Layer);
                double th = Math.Max(1.2, b.D * k);
                double tipV = double.NaN;
                foreach (PlannedBar seg in line)
                {
                    double sa = Math.Max(seg.Start, va), sb = Math.Min(seg.End, vb);
                    if (sb - sa <= 1e-9) continue;
                    var ln = new Line
                    {
                        X1 = X(sa), Y1 = Y(seg.Z), X2 = X(sb), Y2 = Y(seg.Z), Stroke = brush, StrokeThickness = th,
                        ToolTip = Layers.Name(seg.Layer) + " " + Dia(seg.D) + " mm a " + Mm(seg.Z) +
                                  " mm desde abajo (la mas cercana al corte, u=" + Mm(seg.Coord) + "; de v=" + Mm(seg.Start) + " a " + Mm(seg.End) + ")"
                    };
                    Children.Add(ln);
                    if (double.IsNaN(tipV)) tipV = 0.5 * (sa + sb);
                    double want = va + 0.62 * mw;
                    if (want >= sa && want <= sb) tipV = want;
                }
                if (double.IsNaN(tipV)) continue;
                bool up = Layers.IsTop(b.Layer);
                int slot = up ? upLeaders++ : downLeaders++;
                double spacing = _plan.Groups.Where(g => g.Layer == b.Layer && g.Count > 1).Select(g => g.Spacing).DefaultIfEmpty(0).Max();
                string label = "ACERO " + Layers.Name(b.Layer).ToUpperInvariant() + " " + Dia(b.D) +
                               (spacing > 0 ? " @" + (spacing * 0.3048).ToString("0.00", CultureInfo.InvariantCulture) : "");
                Leader(X(tipV) - slot * 0.12 * mw * k, Y(b.Z) + (up ? -0.5 * th : 0.5 * th), up, slot, label, brush, W);
            }

            // barras a lo largo de u: circulos (llenos si cruzan el corte, huecos si no, p. ej. bastones de los apoyos)
            foreach (PlannedBar b in _plan.Bars.Where(b => b.AlongU && b.Coord >= va - Mm1(1) && b.Coord <= vb + Mm1(1)))
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
                Children.Add(e);
            }
        }

        // --- piezas del dibujo ---

        /// <summary>Ladrillo de techo entre x1 y x2 (px) con sus alveolos; sin el borde lateral que corta el modulo.</summary>
        private void Brick(double x1, double x2, double y1, double y2, double wFt, double hFt, bool edgeLeft, bool edgeRight, string tip)
        {
            var r = new Rectangle { Width = Math.Max(1, x2 - x1), Height = Math.Max(1, y2 - y1), Fill = PlanColors.Brick, ToolTip = tip };
            SetLeft(r, x1); SetTop(r, y1);
            Children.Add(r);
            Seg(x1, y1, x2, y1, PlanColors.BrickEdge, 0.8);
            Seg(x1, y2, x2, y2, PlanColors.BrickEdge, 0.8);
            if (edgeLeft) Seg(x1, y1, x1, y2, PlanColors.BrickEdge, 0.8);
            if (edgeRight) Seg(x2, y1, x2, y2, PlanColors.BrickEdge, 0.8);

            // alveolos: celdas de unos 75 mm en una o dos filas, si hay sitio para verlas
            double wpx = x2 - x1, hpx = y2 - y1;
            if (wpx < 16 || hpx < 12) return;
            int cols = Math.Max(1, Math.Min((int)Math.Round(wFt / Mm1(75)), (int)(wpx / 8)));
            int rows = hFt >= Mm1(90) && hpx >= 24 ? 2 : 1;
            double cw = wpx / cols, ch = hpx / rows;
            for (int row = 0; row < rows; row++)
                for (int c = 0; c < cols; c++)
                {
                    var hole = new Rectangle
                    {
                        Width = Math.Max(1, cw * 0.7), Height = Math.Max(1, ch * 0.7), Stroke = PlanColors.BrickEdge, StrokeThickness = 0.6, Fill = null, IsHitTestVisible = false
                    };
                    SetLeft(hole, x1 + cw * (c + 0.15)); SetTop(hole, y1 + ch * (row + 0.15));
                    Children.Add(hole);
                }
        }

        /// <summary>Extremo del modulo: borde real de la losa (linea) o marca de corte (quebrada).</summary>
        private void EdgeOrBreak(double x, double yTop, double yBot, bool cut)
        {
            if (!cut) { Seg(x, yTop, x, yBot, Brushes.DimGray, 1.2); return; }
            double ym = 0.5 * (yTop + yBot);
            var pl = new Polyline { Stroke = Brushes.DimGray, StrokeThickness = 1, Fill = null, IsHitTestVisible = false };
            pl.Points.Add(new Point(x, yTop - 4));
            pl.Points.Add(new Point(x, ym - 7));
            pl.Points.Add(new Point(x - 5, ym - 2));
            pl.Points.Add(new Point(x + 5, ym + 2));
            pl.Points.Add(new Point(x, ym + 7));
            pl.Points.Add(new Point(x, yBot + 4));
            Children.Add(pl);
        }

        /// <summary>Cota horizontal entre x1 y x2 a la altura y, con lineas de referencia desde yFeature.</summary>
        private void DimH(double x1, double x2, double y, double yFeature, string label)
        {
            if (x2 < x1) { double s = x1; x1 = x2; x2 = s; }
            double dir = y >= yFeature ? 1 : -1;
            Seg(x1, yFeature + 3 * dir, x1, y + 4 * dir, DimBrush, 0.6);
            Seg(x2, yFeature + 3 * dir, x2, y + 4 * dir, DimBrush, 0.6);
            bool inside = x2 - x1 >= 2 * ArrowLen + 4;
            double ext = inside ? 0 : 14;
            Seg(x1 - ext, y, x2 + ext, y, DimBrush, 0.6);
            Arrow(x1, y, inside ? -1 : 1, 0);
            Arrow(x2, y, inside ? 1 : -1, 0);
            TextBlock tb = Text(label, 0, 0, DimBrush, 10);
            Size sz = Measure(tb);
            SetLeft(tb, 0.5 * (x1 + x2) - 0.5 * sz.Width); SetTop(tb, y - sz.Height - 1);
        }

        /// <summary>
        /// Cota vertical entre y1 e y2 en la abscisa x, con lineas de referencia desde xFeature
        /// (NaN = sin ellas) y el texto girado a la izquierda o a la derecha de la linea.
        /// En cotas cortas (recubrimientos) las flechas van por fuera y el texto sobre la de arriba.
        /// </summary>
        private void DimV(double x, double y1, double y2, double xFeature, string label, bool textLeft)
        {
            if (y2 < y1) { double s = y1; y1 = y2; y2 = s; }
            if (!double.IsNaN(xFeature))
            {
                double dir = x >= xFeature ? 1 : -1;
                Seg(xFeature + 3 * dir, y1, x + 4 * dir, y1, DimBrush, 0.6);
                Seg(xFeature + 3 * dir, y2, x + 4 * dir, y2, DimBrush, 0.6);
            }
            bool inside = y2 - y1 >= 2 * ArrowLen + 4;
            double ext = inside ? 0 : 14;
            Seg(x, y1 - ext, x, y2 + ext, DimBrush, 0.6);
            Arrow(x, y1, 0, inside ? -1 : 1);
            Arrow(x, y2, 0, inside ? 1 : -1);
            TextBlock tb = Text(label, 0, 0, DimBrush, 10);
            tb.LayoutTransform = new RotateTransform(-90);
            Size sz = Measure(tb);
            SetLeft(tb, textLeft ? x - sz.Width - 1 : x + 1);
            SetTop(tb, inside ? 0.5 * (y1 + y2) - 0.5 * sz.Height : y1 - ext - sz.Height - 1);
        }

        /// <summary>Directriz desde (tipX, tipY) hacia arriba o abajo a la derecha, con el rotulo en su extremo.</summary>
        private void Leader(double tipX, double tipY, bool up, int slot, string label, Brush brush, double W)
        {
            TextBlock tb = Text(label, 0, 0, brush, 10);
            Size sz = Measure(tb);
            double ey = tipY + (up ? -1 : 1) * (26 + 14 * slot);
            double ex = tipX + 30;
            double tx = ex + 4;
            if (tx + sz.Width > W - 4) { tx = Math.Max(4, W - 4 - sz.Width); ex = tx - 4; }
            Seg(tipX, tipY, ex, ey, brush, 0.8);
            Seg(ex, ey, tx + sz.Width, ey, brush, 0.8);
            double dx = tipX - ex, dy = tipY - ey, len = Math.Max(1e-6, Math.Sqrt(dx * dx + dy * dy));
            Arrow(tipX, tipY, dx / len, dy / len, brush);
            SetLeft(tb, tx); SetTop(tb, ey - sz.Height - 1);
        }

        /// <summary>Punta de flecha rellena con la punta en (x, y) apuntando en la direccion unitaria (dx, dy).</summary>
        private void Arrow(double x, double y, double dx, double dy, Brush brush = null)
        {
            var p = new Polygon { Fill = brush ?? DimBrush, IsHitTestVisible = false };
            p.Points.Add(new Point(x, y));
            p.Points.Add(new Point(x - ArrowLen * dx - ArrowHalf * dy, y - ArrowLen * dy + ArrowHalf * dx));
            p.Points.Add(new Point(x - ArrowLen * dx + ArrowHalf * dy, y - ArrowLen * dy - ArrowHalf * dx));
            Children.Add(p);
        }

        private void Seg(double x1, double y1, double x2, double y2, Brush brush, double thickness)
        {
            Children.Add(new Line { X1 = x1, Y1 = y1, X2 = x2, Y2 = y2, Stroke = brush, StrokeThickness = thickness, IsHitTestVisible = false });
        }

        private static Size Measure(TextBlock tb)
        {
            tb.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            return tb.DesiredSize;
        }

        private TextBlock Text(string s, double x, double y, Brush brush, double size)
        {
            var t = new TextBlock { Text = s, Foreground = brush, FontSize = size, TextWrapping = TextWrapping.NoWrap };
            SetLeft(t, x); SetTop(t, y);
            Children.Add(t);
            return t;
        }
    }
}
