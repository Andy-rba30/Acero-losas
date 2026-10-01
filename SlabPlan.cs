using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace SlabRebar
{
    public enum SlabKind { Aligerada, Maciza }

    /// <summary>Capas de barras que puede llevar una losa.</summary>
    public enum BarLayer
    {
        /// <summary>Barra inferior de cada vigueta (aligerada).</summary>
        JoistBottom,
        /// <summary>Bastones o barra superior corrida de cada vigueta (aligerada).</summary>
        JoistTop,
        /// <summary>Acero de temperatura en la losa superior, perpendicular a las viguetas (aligerada).</summary>
        Temperature,
        /// <summary>Malla inferior, direccion principal (maciza).</summary>
        BottomMain,
        /// <summary>Malla inferior, direccion secundaria: temperatura / reparticion (maciza).</summary>
        BottomSecondary,
        /// <summary>Malla superior, direccion principal: bastones o corrida (maciza).</summary>
        TopMain,
        /// <summary>Malla superior, direccion secundaria, corrida (maciza).</summary>
        TopSecondary
    }

    public static class Layers
    {
        public static string Name(BarLayer l)
        {
            switch (l)
            {
                case BarLayer.JoistBottom: return "inferior de vigueta";
                case BarLayer.JoistTop: return "baston";
                case BarLayer.Temperature: return "temperatura";
                case BarLayer.BottomMain: return "inferior principal";
                case BarLayer.BottomSecondary: return "inferior secundaria";
                case BarLayer.TopMain: return "superior principal";
                default: return "superior secundaria";
            }
        }

        /// <summary>Nombre corto para el comodin {capa} de la Particion.</summary>
        public static string Short(BarLayer l)
        {
            switch (l)
            {
                case BarLayer.JoistBottom: case BarLayer.BottomMain: return "inferior";
                case BarLayer.JoistTop: return "baston";
                case BarLayer.Temperature: return "temperatura";
                case BarLayer.BottomSecondary: return "inferior-sec";
                case BarLayer.TopMain: return "superior";
                default: return "superior-sec";
            }
        }

        public static bool IsTop(BarLayer l) => l == BarLayer.JoistTop || l == BarLayer.Temperature || l == BarLayer.TopMain || l == BarLayer.TopSecondary;
    }

    /// <summary>Diametros (pies) de cada capa, ya resueltos a partir de los tipos de barra.</summary>
    public sealed class PlanDiameters
    {
        public double JoistBottom, JoistTop, Temperature, BottomMain, BottomSecondary, TopMain, TopSecondary;

        public double Of(BarLayer l)
        {
            switch (l)
            {
                case BarLayer.JoistBottom: return JoistBottom;
                case BarLayer.JoistTop: return JoistTop;
                case BarLayer.Temperature: return Temperature;
                case BarLayer.BottomMain: return BottomMain;
                case BarLayer.BottomSecondary: return BottomSecondary;
                case BarLayer.TopMain: return TopMain;
                default: return TopSecondary;
            }
        }
    }

    /// <summary>
    /// Una barra recta planificada, en coordenadas locales de la losa (pies). Va a lo largo
    /// de u (AlongU, en la recta v = Coord) o de v (en la recta u = Coord), de Start a End,
    /// a la cota Z medida desde la cara inferior de la losa. [InA, InB] es el tramo que queda
    /// dentro del contorno (lo unico que se comprueba contra el hormigon); lo que sobresale
    /// es la prolongacion hacia la viga.
    /// </summary>
    public sealed class PlannedBar
    {
        public BarLayer Layer;
        public bool AlongU;
        public double Coord, Z, Start, End, InA, InB, D;
        public bool HookStart, HookEnd;
        public double Length => End - Start;
        public bool ExtendsStart => Start < InA - 1e-9;
        public bool ExtendsEnd => End > InB + 1e-9;

        /// <summary>Misma barra (longitud, prolongaciones, ganchos, cota) en otra posicion: candidata al mismo conjunto.</summary>
        public bool SameAs(PlannedBar o, double tol) =>
            Layer == o.Layer && AlongU == o.AlongU && Math.Abs(Z - o.Z) <= tol && Math.Abs(D - o.D) <= 1e-9 &&
            Math.Abs(Start - o.Start) <= tol && Math.Abs(End - o.End) <= tol &&
            Math.Abs(InA - o.InA) <= tol && Math.Abs(InB - o.InB) <= tol &&
            HookStart == o.HookStart && HookEnd == o.HookEnd;
    }

    /// <summary>Barras iguales y equiespaciadas: un conjunto (array) de Revit.</summary>
    public sealed class BarGroup
    {
        public List<PlannedBar> Bars = new List<PlannedBar>();
        public PlannedBar First => Bars[0];
        public int Count => Bars.Count;
        /// <summary>Separacion entre barras del conjunto (pies); 0 si es una sola.</summary>
        public double Spacing;
        public BarLayer Layer => First.Layer;
        public bool AlongU => First.AlongU;
    }

    /// <summary>Vigueta de la losa aligerada: eje (coordenada v) y ancho.</summary>
    public sealed class Joist
    {
        public double Axis, Width;
        public double V1 => Axis - 0.5 * Width;
        public double V2 => Axis + 0.5 * Width;
    }

    /// <summary>
    /// Armado completo de una losa en coordenadas locales: viguetas, barras por capa y
    /// conjuntos. Pura (sin Revit): la misma clase la usan la ventana (para dibujar) y el
    /// generador (para crear las barras), asi lo que se ve es lo que se arma.
    /// </summary>
    public sealed class SlabPlan
    {
        public const double MmPerFt = 304.8;
        public static double Mm(double mm) => mm / MmPerFt;
        public static double ToMm(double ft) => Math.Round(ft * MmPerFt);

        public SlabKind Kind;
        public Outline2D Outline;
        public List<Support> Supports = new List<Support>();
        public double Thickness;
        public double CoverBottom, CoverTop, CoverEdge;
        /// <summary>Solo aligerada: losa superior y viguetas.</summary>
        public double TopSlab, JoistWidth, JoistSpacing;
        public List<Joist> Joists = new List<Joist>();

        public List<PlannedBar> Bars = new List<PlannedBar>();
        public List<BarGroup> Groups = new List<BarGroup>();
        /// <summary>Cota (desde la cara inferior) de cada capa colocada, para el esquema de la seccion.</summary>
        public Dictionary<BarLayer, double> LayerZ = new Dictionary<BarLayer, double>();
        public List<string> Warnings = new List<string>();
        public string Error;
        /// <summary>Tramos demasiado cortos que se han omitido.</summary>
        public int Skipped;

        private double _tol, _minLen;

        public int CountOf(BarLayer l) => Bars.Count(b => b.Layer == l);
        public int GroupsOf(BarLayer l) => Groups.Count(g => g.Layer == l);
        public IEnumerable<BarLayer> UsedLayers => Bars.Select(b => b.Layer).Distinct().OrderBy(l => (int)l);

        private static string Num(double v) => v.ToString("0.#", CultureInfo.InvariantCulture);

        /// <summary>Resumen corto: "aligerada: 9 viguetas; 43 barras en 7 conjuntos".</summary>
        public string Describe()
        {
            if (Error != null) return "SIN ARMAR: " + Error;
            string head = Kind == SlabKind.Aligerada
                ? "aligerada: " + Joists.Count + " viguetas de " + ToMm(JoistWidth) + " @" + ToMm(JoistSpacing)
                : "maciza";
            return head + "; " + Bars.Count + " barras en " + Groups.Count + " conjuntos" +
                   (Skipped > 0 ? " (" + Skipped + " tramos cortos omitidos)" : "");
        }

        /// <summary>Desglose por capa: "inferior de vigueta 9 (Ø12.7), baston 18 (Ø12.7), temperatura 16 (Ø6.4)".</summary>
        public string DescribeLayers()
        {
            var parts = new List<string>();
            foreach (BarLayer l in UsedLayers)
            {
                PlannedBar b = Bars.First(x => x.Layer == l);
                parts.Add(Layers.Name(l) + " " + CountOf(l) + " (Ø" + Num(b.D * MmPerFt) + ")");
            }
            return string.Join(", ", parts);
        }

        // =================================================================
        // Construccion
        // =================================================================

        /// <param name="outline">Contorno en coordenadas locales (u = direccion de las barras principales).</param>
        /// <param name="supports">Vigas de apoyo en coordenadas locales (pueden faltar).</param>
        /// <param name="thickness">Espesor de la losa (pies).</param>
        /// <param name="d">Diametros de cada capa (pies).</param>
        public static SlabPlan Build(Outline2D outline, List<Support> supports, double thickness, SlabKind kind, AppConfig cfg, PlanDiameters d)
        {
            var p = new SlabPlan
            {
                Kind = kind, Outline = outline, Supports = supports ?? new List<Support>(), Thickness = thickness,
                CoverBottom = Mm(cfg.CoverBottomMm), CoverTop = Mm(cfg.CoverTopMm), CoverEdge = Mm(cfg.CoverEdgeMm),
                _tol = Mm(cfg.ToleranceMm), _minLen = Mm(cfg.MinBarLengthMm)
            };
            try
            {
                if (thickness <= p.CoverBottom + p.CoverTop + Mm(10))
                {
                    p.Error = "el espesor (" + ToMm(thickness) + " mm) no deja sitio entre los recubrimientos";
                    return p;
                }
                if (kind == SlabKind.Aligerada) p.BuildAligerada(cfg, d);
                else p.BuildMaciza(cfg, d);
                if (p.Error != null) return p;

                // cotas dentro de la losa
                foreach (PlannedBar b in p.Bars)
                    if (b.Z - 0.5 * b.D < -p._tol || b.Z + 0.5 * b.D > thickness + p._tol)
                    {
                        p.Error = "la capa " + Layers.Name(b.Layer) + " queda fuera del espesor de la losa (cota " + ToMm(b.Z) + " mm)";
                        return p;
                    }
                if (p.Bars.Count == 0) { p.Error = "no se obtiene ninguna barra con esta configuracion"; return p; }
                p.Group();
            }
            catch (Exception ex)
            {
                p.Error = ex.Message;
            }
            return p;
        }

        // -----------------------------------------------------------------
        // Losa aligerada
        // -----------------------------------------------------------------
        private void BuildAligerada(AppConfig cfg, PlanDiameters d)
        {
            AligeradaCfg a = cfg.Aligerada;
            JoistWidth = Mm(a.JoistWidthMm);
            JoistSpacing = Mm(a.JoistSpacingMm);
            TopSlab = Mm(a.TopSlabMm);
            if (TopSlab >= Thickness - _tol) { Error = "la losa superior (" + ToMm(TopSlab) + " mm) no es menor que el espesor (" + ToMm(Thickness) + " mm)"; return; }

            foreach (double axis in Geometry2D.JoistAxes(Outline.VMin, Outline.VMax, JoistWidth, JoistSpacing, Mm(a.FirstJoistOffsetMm), _tol))
                Joists.Add(new Joist { Axis = axis, Width = JoistWidth });
            if (Joists.Count == 0) { Error = "no cabe ninguna vigueta de " + ToMm(JoistWidth) + " mm en " + ToMm(Outline.Depth) + " mm de ancho"; return; }

            // --- barra inferior de cada vigueta ---
            double db = d.JoistBottom;
            double zb = CoverBottom + 0.5 * db;
            LayerZ[BarLayer.JoistBottom] = zb;
            double off = 0;
            if (a.Bottom.Count >= 2)
            {
                off = 0.5 * JoistWidth - CoverEdge - 0.5 * db;
                double clear = 2 * off - db;
                if (off <= 0.5 * db) { off = 0.5 * db + Mm(1); clear = 2 * off - db; }
                if (clear < Math.Max(db, Mm(25)) - 1e-9)
                    Warnings.Add("las 2 barras inferiores de la vigueta quedan a " + ToMm(clear) + " mm libres (ancho " + ToMm(JoistWidth) + ", recubrimiento " + ToMm(CoverEdge) + ")");
            }
            foreach (Joist j in Joists)
            {
                double[] lines = a.Bottom.Count >= 2 ? new[] { j.Axis - off, j.Axis + off } : new[] { j.Axis };
                foreach (double v in lines)
                    foreach (Span s in Outline.Cut(true, v, CoverEdge + 0.5 * db, _tol))
                        Add(MakeBar(BarLayer.JoistBottom, true, v, zb, db, s, Mm(a.Bottom.ExtensionMm), !string.IsNullOrEmpty(a.Bottom.HookTypeName)));
            }

            // --- bastones / barra superior corrida de cada vigueta ---
            double dt = 0;
            if (!a.Top.None)
            {
                dt = d.JoistTop;
                double zt = Thickness - CoverTop - 0.5 * dt;
                LayerZ[BarLayer.JoistTop] = zt;
                foreach (Joist j in Joists)
                    foreach (Span s in Outline.Cut(true, j.Axis, CoverEdge + 0.5 * dt, _tol))
                        TopBars(BarLayer.JoistTop, true, j.Axis, zt, dt, s, a.Top);
            }

            // --- acero de temperatura (perpendicular, en la losa superior) ---
            if (a.Temperature.Enabled)
            {
                double dte = d.Temperature;
                double z = a.Temperature.DepthMm > 0 ? Thickness - Mm(a.Temperature.DepthMm) : Thickness - CoverTop - dt - 0.5 * dte;
                LayerZ[BarLayer.Temperature] = z;
                if (z - 0.5 * dte < Thickness - TopSlab - _tol)
                    Warnings.Add("el acero de temperatura queda por debajo de la losa superior (" + ToMm(Thickness - z) + " mm desde arriba, losa superior " + ToMm(TopSlab) + " mm)");
                if (z + 0.5 * dte > Thickness - CoverTop + _tol)
                    Warnings.Add("el acero de temperatura no respeta el recubrimiento superior");
                double from = Outline.UMin + CoverEdge + 0.5 * dte, to = Outline.UMax - CoverEdge - 0.5 * dte;
                foreach (double u in Geometry2D.Positions(from, to, Mm(a.Temperature.SpacingMm), _tol))
                    foreach (Span s in Outline.Cut(false, u, CoverEdge + 0.5 * dte, _tol))
                        Add(MakeBar(BarLayer.Temperature, false, u, z, dte, s, Mm(a.Temperature.ExtensionMm), !string.IsNullOrEmpty(a.Temperature.HookTypeName)));
            }
        }

        // -----------------------------------------------------------------
        // Losa maciza
        // -----------------------------------------------------------------
        private void BuildMaciza(AppConfig cfg, PlanDiameters d)
        {
            MacizaCfg m = cfg.Maciza;

            // --- inferior principal (a lo largo de u, repartida en v) ---
            double d1 = d.BottomMain;
            double z1 = CoverBottom + 0.5 * d1;
            LayerZ[BarLayer.BottomMain] = z1;
            Mesh(BarLayer.BottomMain, true, z1, d1, m.BottomMain);

            // --- inferior secundaria (temperatura / reparticion), encima de la principal ---
            if (m.BottomSecondary.Enabled)
            {
                double d2 = d.BottomSecondary;
                double z2 = CoverBottom + d1 + 0.5 * d2;
                LayerZ[BarLayer.BottomSecondary] = z2;
                Mesh(BarLayer.BottomSecondary, false, z2, d2, m.BottomSecondary);
            }

            // --- superior principal: bastones o corrida ---
            double dt1 = 0;
            if (!m.TopMain.None)
            {
                dt1 = d.TopMain;
                double zt1 = Thickness - CoverTop - 0.5 * dt1;
                LayerZ[BarLayer.TopMain] = zt1;
                double from = Outline.VMin + CoverEdge + 0.5 * dt1, to = Outline.VMax - CoverEdge - 0.5 * dt1;
                foreach (double v in Geometry2D.Positions(from, to, Mm(m.TopMain.SpacingMm), _tol))
                    foreach (Span s in Outline.Cut(true, v, CoverEdge + 0.5 * dt1, _tol))
                        TopBars(BarLayer.TopMain, true, v, zt1, dt1, s, m.TopMain);
            }

            // --- superior secundaria, corrida, debajo de la principal ---
            if (m.TopSecondary.Enabled)
            {
                double dt2 = d.TopSecondary;
                double zt2 = Thickness - CoverTop - dt1 - 0.5 * dt2;
                LayerZ[BarLayer.TopSecondary] = zt2;
                Mesh(BarLayer.TopSecondary, false, zt2, dt2, m.TopSecondary);
            }
        }

        /// <summary>Capa de barras corridas equiespaciadas: a lo largo de u (repartidas en v) o de v (repartidas en u).</summary>
        private void Mesh(BarLayer layer, bool alongU, double z, double d, LayerCfg cfg)
        {
            double from = (alongU ? Outline.VMin : Outline.UMin) + CoverEdge + 0.5 * d;
            double to = (alongU ? Outline.VMax : Outline.UMax) - CoverEdge - 0.5 * d;
            if (to <= from) { Warnings.Add("no cabe la capa " + Layers.Name(layer)); return; }
            bool hook = !string.IsNullOrEmpty(cfg.HookTypeName);
            foreach (double c in Geometry2D.Positions(from, to, Mm(cfg.SpacingMm), _tol))
                foreach (Span s in Outline.Cut(alongU, c, CoverEdge + 0.5 * d, _tol))
                    Add(MakeBar(layer, alongU, c, z, d, s, Mm(cfg.ExtensionMm), hook));
        }

        // -----------------------------------------------------------------
        // Barras
        // -----------------------------------------------------------------
        private void Add(PlannedBar b)
        {
            if (b != null) Bars.Add(b);
        }

        /// <summary>Extremo de barra en el borde de un tramo: al recubrimiento dentro de la losa, o prolongado "ext" hacia fuera si es borde exterior.</summary>
        private double StartOf(Span s, double ext) => s.HoleA || ext <= 0 ? s.A + CoverEdge : s.A - ext;
        private double EndOf(Span s, double ext) => s.HoleB || ext <= 0 ? s.B - CoverEdge : s.B + ext;

        /// <summary>Barra corrida en todo el tramo (null si queda demasiado corta).</summary>
        private PlannedBar MakeBar(BarLayer layer, bool alongU, double coord, double z, double d, Span s, double ext, bool hook) =>
            MakeBar(layer, alongU, coord, z, d, s, StartOf(s, ext), EndOf(s, ext), hook);

        private PlannedBar MakeBar(BarLayer layer, bool alongU, double coord, double z, double d, Span s, double start, double end, bool hook)
        {
            if (end - start < Math.Max(_minLen, _tol)) { Skipped++; return null; }
            return new PlannedBar
            {
                Layer = layer, AlongU = alongU, Coord = coord, Z = z, D = d, Start = start, End = end,
                InA = Math.Max(start, s.A), InB = Math.Min(end, s.B),
                HookStart = hook && !s.HoleA && start <= s.A + _tol,
                HookEnd = hook && !s.HoleB && end >= s.B - _tol
            };
        }

        /// <summary>
        /// Barras superiores de un tramo: corrida, o bastones en los apoyos. Los apoyos son las
        /// vigas que cruzan la recta de la barra: las que tocan un extremo del tramo son apoyos
        /// extremos (el baston sale de su cara hacia dentro), las interiores llevan baston a
        /// los dos lados. Sin vigas detectadas, los extremos exteriores del tramo son los
        /// apoyos. Los bastones que se solapan se unen.
        /// </summary>
        private void TopBars(BarLayer layer, bool alongU, double coord, double z, double d, Span s, TopCfg cfg)
        {
            double ext = Mm(cfg.ExtensionMm);
            bool hook = !string.IsNullOrEmpty(cfg.HookTypeName);
            if (cfg.Continuous) { Add(MakeBar(layer, alongU, coord, z, d, s, ext, hook)); return; }

            double lo = StartOf(s, ext), hi = EndOf(s, ext);
            if (hi - lo <= _tol) { Skipped++; return; }

            List<Support> crossing = Supports.Where(x => x.Crosses(coord, _tol)).OrderBy(x => x.CU).ToList();
            Support endA = crossing.FirstOrDefault(x => x.U1 - _tol <= s.A && s.A <= x.U2 + _tol);
            Support endB = crossing.LastOrDefault(x => x.U1 - _tol <= s.B && s.B <= x.U2 + _tol);
            double faceA = endA != null ? Math.Min(Math.Max(endA.U2, s.A), s.B) : s.A;
            double faceB = endB != null ? Math.Max(Math.Min(endB.U1, s.B), s.A) : s.B;
            List<Support> interior = crossing.Where(x => x != endA && x != endB && x.U1 > faceA + _tol && x.U2 < faceB - _tol).ToList();

            // caras de apoyo en orden: faceA | U1 U2 | U1 U2 | ... | faceB
            var faces = new List<double> { faceA };
            foreach (Support x in interior) { faces.Add(x.U1); faces.Add(x.U2); }
            faces.Add(faceB);
            double Clear(int i) => Math.Max(0, faces[2 * i + 1] - faces[2 * i]);   // luz libre i (0 = la primera)
            double fixedLen = Mm(cfg.FixedLengthMm);
            double Len(double frac, double clear) => fixedLen > 0 ? fixedLen : frac * clear;

            var parts = new List<(double a, double b)>();
            if (!s.HoleA) parts.Add((lo, faceA + Len(cfg.EndFraction, Clear(0))));
            for (int i = 0; i < interior.Count; i++)
                parts.Add((interior[i].U1 - Len(cfg.InteriorFraction, Clear(i)), interior[i].U2 + Len(cfg.InteriorFraction, Clear(i + 1))));
            if (!s.HoleB) parts.Add((faceB - Len(cfg.EndFraction, Clear(interior.Count)), hi));

            var clamped = parts.Select(x => (Math.Max(lo, x.a), Math.Min(hi, x.b)));
            foreach ((double a, double b) in Geometry2D.Merge(clamped, 0))
                Add(MakeBar(layer, alongU, coord, z, d, s, a, b, hook));
        }

        // -----------------------------------------------------------------
        // Conjuntos (arrays)
        // -----------------------------------------------------------------

        /// <summary>
        /// Agrupa las barras iguales (misma capa, longitud, prolongaciones, ganchos y cota)
        /// equiespaciadas en conjuntos, como si se modelaran a mano con un array: una fila de
        /// barras inferiores de un pano rectangular es un solo conjunto. Si las barras iguales
        /// alternan dos pasos (las dos barras de cada vigueta), se prueban conjuntos
        /// entrelazados y se elige el reparto con menos conjuntos.
        /// </summary>
        private void Group()
        {
            Groups.Clear();
            // 1. racimos de barras iguales
            var clusters = new List<List<PlannedBar>>();
            foreach (PlannedBar b in Bars.OrderBy(b => (int)b.Layer).ThenBy(b => b.AlongU ? 0 : 1).ThenBy(b => b.Coord).ThenBy(b => b.Start))
            {
                List<PlannedBar> c = clusters.FirstOrDefault(x => x[0].SameAs(b, _tol));
                if (c == null) { c = new List<PlannedBar>(); clusters.Add(c); }
                c.Add(b);
            }
            // 2. dentro de cada racimo, conjuntos por paso constante (probando el entrelazado 1, 2 y 3)
            foreach (List<PlannedBar> c in clusters)
            {
                List<PlannedBar> sorted = c.OrderBy(b => b.Coord).ToList();
                List<BarGroup> best = null;
                for (int k = 1; k <= 3; k++)
                {
                    if (sorted.Count < 2 * k && k > 1) break;
                    var variant = new List<BarGroup>();
                    for (int r = 0; r < k; r++)
                        variant.AddRange(Progressions(sorted.Where((b, i) => i % k == r).ToList()));
                    if (best == null || variant.Count < best.Count) best = variant;
                }
                Groups.AddRange(best.OrderBy(g => g.First.Coord));
            }
        }

        /// <summary>Conjuntos de paso constante a partir de barras iguales ordenadas por coordenada (voraz).</summary>
        private List<BarGroup> Progressions(List<PlannedBar> sorted)
        {
            var result = new List<BarGroup>();
            BarGroup g = null;
            foreach (PlannedBar b in sorted)
            {
                if (g != null)
                {
                    double step = b.Coord - g.Bars[g.Count - 1].Coord;
                    if (g.Count == 1 && step > _tol) { g.Spacing = step; g.Bars.Add(b); continue; }
                    if (g.Count >= 2 && Math.Abs(step - g.Spacing) <= _tol) { g.Bars.Add(b); continue; }
                }
                g = new BarGroup();
                g.Bars.Add(b);
                result.Add(g);
            }
            return result;
        }
    }
}
