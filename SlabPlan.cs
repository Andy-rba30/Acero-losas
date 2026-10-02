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

    /// <summary>
    /// Medidas del gancho de una capa (pies), tal y como las define Revit en el tipo de barra
    /// (Editar tipo > Longitudes de gancho): el plugin no puede cambiarlas, solo leerlas.
    /// </summary>
    public sealed class HookDims
    {
        /// <summary>Longitud total del gancho, de fuera a fuera ("Longitud de gancho" del tipo de barra). 0 = desconocida.</summary>
        public double Length;
        /// <summary>Diametro interior de doblado del gancho. 0 = desconocido (se estima 6d).</summary>
        public double Bend;

        /// <summary>
        /// Lo que el gancho invade en horizontal mas alla del final del tramo recto, hasta su
        /// cara exterior: el radio de doblado del eje (bend/2 + d/2) mas medio diametro.
        /// </summary>
        public double Reach(double d) => (Bend > 0 ? 0.5 * Bend : 3 * d) + d;

        public static HookDims Default(double d) => new HookDims { Length = 0, Bend = 6 * d };

        /// <summary>Clave "tipo de barra + tipo de gancho" para las tablas que pasan de Revit a la ventana.</summary>
        public static string Key(string barType, string hookType) => (barType ?? "") + "\n" + (hookType ?? "");
    }

    /// <summary>Diametros (pies) de cada capa, ya resueltos a partir de los tipos de barra, y medidas de su gancho si lo lleva.</summary>
    public sealed class PlanDiameters
    {
        public double JoistBottom, JoistTop, Temperature, BottomMain, BottomSecondary, TopMain, TopSecondary;
        /// <summary>Medidas del gancho de cada capa con gancho (las que faltan se estiman).</summary>
        public Dictionary<BarLayer, HookDims> Hooks = new Dictionary<BarLayer, HookDims>();

        public HookDims HookOf(BarLayer l) => Hooks.TryGetValue(l, out HookDims h) ? h : null;

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
        /// <summary>Longitud total del gancho (pies) segun el tipo de barra; 0 si no lleva o no se conoce.</summary>
        public double HookLength;
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
        private PlanDiameters _d;

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
                _tol = Mm(cfg.ToleranceMm), _minLen = Mm(cfg.MinBarLengthMm), _d = d
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
            double zb = FitHook(BarLayer.JoistBottom, CoverBottom + 0.5 * db, db, Hooked(a.Bottom), MinCover(db) + 0.5 * db);
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
                        Add(MakeBar(BarLayer.JoistBottom, true, v, zb, db, s, Mm(a.Bottom.ExtensionMm), Hooked(a.Bottom)));
            }

            // --- acero de temperatura (perpendicular, en la losa superior): es la capa mas alta, al
            // recubrimiento superior o a la profundidad indicada; los bastones van justo debajo ---
            double dte = 0, zte = 0;
            if (a.Temperature.Enabled)
            {
                dte = d.Temperature;
                zte = a.Temperature.DepthMm > 0 ? Thickness - Mm(a.Temperature.DepthMm) : Thickness - CoverTop - 0.5 * dte;
            }

            // --- bastones / barra superior corrida de cada vigueta (bajo la temperatura, si la hay) ---
            if (!a.Top.None)
            {
                double dt = d.JoistTop;
                double top = a.Temperature.Enabled ? Math.Min(Thickness - CoverTop, zte - 0.5 * dte) : Thickness - CoverTop;
                double zt = top - 0.5 * dt;
                // con gancho que no quepa: bajo la temperatura no puede subir; sin ella, hasta el recubrimiento minimo
                zt = FitHook(BarLayer.JoistTop, zt, dt, Hooked(a.Top), a.Temperature.Enabled ? zt : Thickness - MinCover(dt) - 0.5 * dt);
                LayerZ[BarLayer.JoistTop] = zt;
                foreach (Joist j in Joists)
                    foreach (Span s in Outline.Cut(true, j.Axis, CoverEdge + 0.5 * dt, _tol))
                        TopBars(BarLayer.JoistTop, true, j.Axis, zt, dt, s, a.Top);
            }

            if (a.Temperature.Enabled)
            {
                if (zte - 0.5 * dte < Thickness - TopSlab - _tol)
                    Warnings.Add("el acero de temperatura queda por debajo de la losa superior (" + ToMm(Thickness - zte) + " mm desde arriba, losa superior " + ToMm(TopSlab) + " mm)");
                if (zte + 0.5 * dte > Thickness - CoverTop + _tol)
                    Warnings.Add("el acero de temperatura no respeta el recubrimiento superior");
                double z = FitHook(BarLayer.Temperature, zte, dte, Hooked(a.Temperature), Thickness - MinCover(dte) - 0.5 * dte);
                LayerZ[BarLayer.Temperature] = z;
                double from = Outline.UMin + CoverEdge + 0.5 * dte, to = Outline.UMax - CoverEdge - 0.5 * dte;
                foreach (double u in Geometry2D.Positions(from, to, Mm(a.Temperature.SpacingMm), _tol))
                    foreach (Span s in Outline.Cut(false, u, CoverEdge + 0.5 * dte, _tol))
                        Add(MakeBar(BarLayer.Temperature, false, u, z, dte, s, Mm(a.Temperature.ExtensionMm), Hooked(a.Temperature)));
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
            double z1 = FitHook(BarLayer.BottomMain, CoverBottom + 0.5 * d1, d1, Hooked(m.BottomMain), MinCover(d1) + 0.5 * d1);
            LayerZ[BarLayer.BottomMain] = z1;
            Mesh(BarLayer.BottomMain, true, z1, d1, m.BottomMain);

            // --- inferior secundaria (temperatura / reparticion), encima de la principal ---
            if (m.BottomSecondary.Enabled)
            {
                double d2 = d.BottomSecondary;
                double z2 = z1 + 0.5 * d1 + 0.5 * d2;
                z2 = FitHook(BarLayer.BottomSecondary, z2, d2, Hooked(m.BottomSecondary), z2);   // descansa sobre la principal: no baja
                LayerZ[BarLayer.BottomSecondary] = z2;
                Mesh(BarLayer.BottomSecondary, false, z2, d2, m.BottomSecondary);
            }

            // --- superior principal: bastones o corrida ---
            double dt1 = 0, zt1 = 0;
            if (!m.TopMain.None)
            {
                dt1 = d.TopMain;
                zt1 = FitHook(BarLayer.TopMain, Thickness - CoverTop - 0.5 * dt1, dt1, Hooked(m.TopMain), Thickness - MinCover(dt1) - 0.5 * dt1);
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
                double zt2 = dt1 > 0 ? zt1 - 0.5 * dt1 - 0.5 * dt2 : Thickness - CoverTop - 0.5 * dt2;
                zt2 = FitHook(BarLayer.TopSecondary, zt2, dt2, Hooked(m.TopSecondary), dt1 > 0 ? zt2 : Thickness - MinCover(dt2) - 0.5 * dt2);   // bajo la principal no sube
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

        /// <summary>Medidas del gancho de una capa: las leidas del tipo de barra o, si faltan, una estimacion (doblado 6d).</summary>
        private HookDims HookOf(BarLayer layer, double d) => _d?.HookOf(layer) ?? HookDims.Default(d);

        private static bool Hooked(LayerCfg cfg) => !string.IsNullOrEmpty(cfg.HookTypeName);

        /// <summary>Recubrimiento minimo que se le deja a una barra al acercarla a su cara para que le quepa el gancho: un diametro, y no menos de 10 mm.</summary>
        private static double MinCover(double d) => Math.Max(d, Mm(10));

        /// <summary>
        /// Cota de una capa con gancho. El gancho dobla hacia la cara opuesta (arriba en las
        /// capas inferiores, abajo en las superiores) y mide lo que diga el tipo de barra: si no
        /// cabe entre la barra y el recubrimiento opuesto, la barra se acerca a su propia cara lo
        /// que falte, como mucho hasta "limit" (la cota mas proxima a esa cara que se admite), y
        /// se avisa. Si ni asi cabe, el gancho invade el recubrimiento opuesto y solo se avisa (el
        /// plugin no puede acortarlo: es un dato del tipo de barra de Revit); solo si llegara a
        /// sobresalir del hormigon lo rechazara la comprobacion al armar.
        /// </summary>
        private double FitHook(BarLayer layer, double z, double d, bool hook, double limit)
        {
            HookDims h = hook ? _d?.HookOf(layer) : null;
            if (h == null || h.Length <= 0) return z;
            bool up = !Layers.IsTop(layer);
            // de la cara de la barra opuesta al gancho hasta el recubrimiento hacia el que dobla
            double room = up ? Thickness - CoverTop - (z - 0.5 * d) : (z + 0.5 * d) - CoverBottom;
            if (h.Length <= room + _tol) return z;
            double need = h.Length - room;
            double can = Math.Max(0, up ? z - limit : limit - z);
            double shift = Math.Min(need, can);
            double z2 = up ? z - shift : z + shift;
            double left = need - shift;
            string opp = up ? "superior" : "inferior", own = up ? "inferior" : "superior";
            string msg = "el gancho de la capa " + Layers.Name(layer) + " mide " + ToMm(h.Length) + " mm y solo caben " + ToMm(room) + " mm hasta el recubrimiento " + opp;
            if (shift > _tol)
                msg += ": la barra se " + (up ? "baja " : "sube ") + ToMm(shift) + " mm para que quepa (recubrimiento " + own + " de " +
                       ToMm(up ? z2 - 0.5 * d : Thickness - z2 - 0.5 * d) + " mm)";
            if (left > _tol)
            {
                double oppCover = up ? CoverTop : CoverBottom;
                msg += (shift > _tol ? "; aun asi" : ":") + " el gancho invade " + ToMm(Math.Min(left, oppCover)) + " mm el recubrimiento " + opp +
                       (left > oppCover + _tol ? " y sobresale " + ToMm(left - oppCover) + " mm del hormigon (al armar se rechazara)" : " (solo aviso)") +
                       ". Para evitarlo reduce la longitud de gancho del tipo de barra (Editar tipo > Longitudes de gancho) o elige un gancho mas corto";
            }
            Warnings.Add(msg);
            return z2;
        }

        /// <summary>
        /// Extremo del tramo recto de la barra en el borde de un tramo: en un hueco, al
        /// recubrimiento; en el borde exterior, prolongado "ext" hacia la viga si se da
        /// prolongacion y si no al recubrimiento. Si ese extremo lleva gancho y para dentro de
        /// la losa, el tramo recto acaba antes (el radio de doblado mas un diametro), de modo
        /// que la cara exterior del gancho, y no el final de la recta, es la que guarda el
        /// recubrimiento lateral. Con prolongacion el gancho queda dentro de la viga y no se
        /// retrasa nada.
        /// </summary>
        private double StartOf(Span s, double ext, BarLayer layer, double d, bool hook)
        {
            if (s.HoleA) return s.A + CoverEdge;
            if (ext > 0) return s.A - ext;
            return s.A + CoverEdge + (hook ? HookOf(layer, d).Reach(d) : 0);
        }

        private double EndOf(Span s, double ext, BarLayer layer, double d, bool hook)
        {
            if (s.HoleB) return s.B - CoverEdge;
            if (ext > 0) return s.B + ext;
            return s.B - CoverEdge - (hook ? HookOf(layer, d).Reach(d) : 0);
        }

        /// <summary>Barra corrida en todo el tramo (null si queda demasiado corta).</summary>
        private PlannedBar MakeBar(BarLayer layer, bool alongU, double coord, double z, double d, Span s, double ext, bool hook) =>
            MakeBar(layer, alongU, coord, z, d, s, StartOf(s, ext, layer, d, hook), EndOf(s, ext, layer, d, hook), ext, hook);

        /// <summary>
        /// Barra de start a end dentro del tramo s. Lleva gancho en cada extremo exterior (no de
        /// hueco) al que llega, tanto si para dentro de la losa como si se prolonga hacia la viga.
        /// </summary>
        private PlannedBar MakeBar(BarLayer layer, bool alongU, double coord, double z, double d, Span s, double start, double end, double ext, bool hook)
        {
            if (end - start < Math.Max(_minLen, _tol)) { Skipped++; return null; }
            var b = new PlannedBar
            {
                Layer = layer, AlongU = alongU, Coord = coord, Z = z, D = d, Start = start, End = end,
                InA = Math.Max(start, s.A), InB = Math.Min(end, s.B),
                HookStart = hook && !s.HoleA && start <= StartOf(s, ext, layer, d, true) + _tol,
                HookEnd = hook && !s.HoleB && end >= EndOf(s, ext, layer, d, true) - _tol
            };
            if (b.HookStart || b.HookEnd) b.HookLength = HookOf(layer, d).Length;
            return b;
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

            double lo = StartOf(s, ext, layer, d, hook), hi = EndOf(s, ext, layer, d, hook);
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
                Add(MakeBar(layer, alongU, coord, z, d, s, a, b, ext, hook));
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
