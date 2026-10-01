using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Autodesk.Revit.DB;

namespace SlabRebar
{
    /// <summary>Viga detectada cerca de la losa: eje en planta (ya centrado en el solido), ancho y solido.</summary>
    public sealed class BeamInfo
    {
        public Element Beam;
        public string Name = "";
        /// <summary>Extremos del eje en planta (coordenadas del modelo, z irrelevante).</summary>
        public XYZ P, Q;
        /// <summary>Ancho de la viga (pies), medido en el solido perpendicularmente a su eje.</summary>
        public double Width;
        public Solid Solid;
    }

    /// <summary>
    /// La losa vista en un sistema local concreto: u = direccion de las viguetas o de las
    /// barras principales, v = perpendicular (Z x u), origen en la esquina minima del
    /// contorno a la cota de la cara inferior. Incluye el contorno en coordenadas locales y
    /// las vigas de apoyo que cruzan la losa casi perpendiculares a u.
    /// </summary>
    public sealed class SlabFrame
    {
        public SlabOutline Slab;
        public XYZ Origin, DirU, DirV;
        public Outline2D Outline;
        public List<Support> Supports = new List<Support>();
        /// <summary>Vigas detectadas pero no usadas como apoyo (paralelas a las barras u oblicuas).</summary>
        public List<string> IgnoredBeams = new List<string>();
        public string Mode;
        public double AngleDeg;
        public bool WithBeams;

        /// <summary>Solidos contra los que se comprueban las barras en este marco.</summary>
        public List<Solid> CheckSolids => Slab.CheckSolids(WithBeams);

        public double ZBottom => Slab.ZBottom;
        public double ZTop => Slab.ZTop;
        public double Thickness => Slab.Thickness;
        public double Width => Outline.Width;
        public double Depth => Outline.Depth;

        public XYZ World(double u, double v, double z) =>
            new XYZ(Origin.X + DirU.X * u + DirV.X * v, Origin.Y + DirU.Y * u + DirV.Y * v, z);

        public Pt Local(XYZ p)
        {
            XYZ d = p - Origin;
            return new Pt(d.X * DirU.X + d.Y * DirU.Y, d.X * DirV.X + d.Y * DirV.Y);
        }

        /// <summary>Coordenada a lo largo del eje de una barra (u si va a lo largo de u, v si no).</summary>
        public double Along(XYZ p, bool alongU)
        {
            Pt l = Local(p);
            return alongU ? l.U : l.V;
        }

        public string LocalMm(XYZ p)
        {
            Pt l = Local(p);
            return "u=" + SlabOutline.ToMm(l.U) + " v=" + SlabOutline.ToMm(l.V) + " z=" + SlabOutline.ToMm(p.Z - ZBottom);
        }

        /// <summary>Texto de la direccion elegida, con el angulo de u respecto al eje X del proyecto.</summary>
        public string DirectionName
        {
            get
            {
                double deg = Math.Round(Math.Atan2(DirU.Y, DirU.X) * 180 / Math.PI);
                string name;
                switch (Mode)
                {
                    case "long": name = "lado largo"; break;
                    case "x": name = "X del proyecto"; break;
                    case "y": name = "Y del proyecto"; break;
                    case "angle": name = "angulo"; break;
                    default: name = "lado corto"; break;
                }
                return name + " (u a " + deg.ToString(CultureInfo.InvariantCulture) + " grados)";
            }
        }

        public string Describe()
        {
            string d = "u " + (Width * 0.3048).ToString("0.00", CultureInfo.InvariantCulture) + " x v " +
                       (Depth * 0.3048).ToString("0.00", CultureInfo.InvariantCulture) + " m, direccion " + DirectionName +
                       ", " + Supports.Count + (Supports.Count == 1 ? " apoyo" : " apoyos");
            return d;
        }
    }

    /// <summary>
    /// Losa deducida de la geometria real del elemento (Floor): se exige una unica cara
    /// superior plana y horizontal; de ella salen el contorno exterior y los huecos; el
    /// espesor es la distancia a la cara inferior. Tambien localiza las vigas (armazon
    /// estructural) a la cota de la losa, que sirven de apoyo a los bastones. Todo en pies.
    /// Los ejes locales dependen de la direccion elegida y se calculan con Frame().
    /// </summary>
    public sealed class SlabOutline
    {
        public const double MmPerFt = 304.8;
        public static double Mm(double mm) => mm / MmPerFt;
        public static double ToMm(double ft) => Math.Round(ft * MmPerFt);

        public Element Host;
        public Solid Solid;
        /// <summary>Solidos de las vigas detectadas (su hormigon cuenta como valido si se usan como apoyos).</summary>
        public List<Solid> BeamSolids = new List<Solid>();

        /// <summary>Solidos contra los que se comprueban las barras: la losa y, si se usan las vigas, tambien ellas.</summary>
        public List<Solid> CheckSolids(bool withBeams)
        {
            var list = new List<Solid> { Solid };
            if (withBeams) list.AddRange(BeamSolids);
            return list;
        }
        public double ZTop, ZBottom;
        public double Thickness => ZTop - ZBottom;
        /// <summary>Anillos del contorno en coordenadas del modelo (x, y): el primero es el exterior, el resto huecos.</summary>
        public List<List<Pt>> WorldRings = new List<List<Pt>>();
        public List<BeamInfo> Beams = new List<BeamInfo>();
        public string Note = "";
        public double TypeThickness;

        public static string LastError;

        private readonly Dictionary<string, SlabFrame> _frames = new Dictionary<string, SlabFrame>();
        private double _tol;

        public int Holes => Math.Max(0, WorldRings.Count - 1);

        public string Describe()
        {
            return "espesor " + ToMm(Thickness) + " mm" + (Holes > 0 ? ", " + Holes + (Holes == 1 ? " hueco" : " huecos") : "") +
                   (Beams.Count > 0 ? ", " + Beams.Count + (Beams.Count == 1 ? " viga cerca" : " vigas cerca") : "") + Note;
        }

        // ------------------------------------------------------------------
        // Deduccion
        // ------------------------------------------------------------------
        public static SlabOutline Probe(Document doc, Element host, AppConfig cfg)
        {
            LastError = null;
            var s = new SlabOutline { Host = host, _tol = Mm(cfg.ToleranceMm) };
            double tol = s._tol;

            List<Solid> solids = Solids(host);
            if (solids.Count == 0) { LastError = "el elemento no tiene geometria solida"; return null; }
            if (solids.Count > 1 && solids[1].Volume > 0.01 * solids[0].Volume)
            {
                LastError = "el elemento tiene " + solids.Count + " solidos; se esperaba uno solo";
                return null;
            }
            s.Solid = solids[0];

            // --- caras superiores e inferiores horizontales ---
            var ups = new List<PlanarFace>();
            var downs = new List<PlanarFace>();
            foreach (Face f in s.Solid.Faces)
            {
                if (!(f is PlanarFace pf)) continue;
                if (pf.FaceNormal.Z > 0.999) ups.Add(pf);
                else if (pf.FaceNormal.Z < -0.999) downs.Add(pf);
            }
            if (ups.Count == 0) { LastError = "la losa no tiene ninguna cara superior horizontal (losa inclinada o con pendiente); solo se arman losas horizontales"; return null; }
            if (downs.Count == 0) { LastError = "la losa no tiene cara inferior horizontal"; return null; }
            s.ZTop = ups.Max(f => f.Origin.Z);
            List<PlanarFace> topFaces = ups.Where(f => Math.Abs(f.Origin.Z - s.ZTop) <= tol).ToList();
            if (topFaces.Count > 1)
            {
                LastError = "la cara superior esta partida en " + topFaces.Count + " trozos (losa dividida); se esperaba una sola cara";
                return null;
            }
            PlanarFace top = topFaces[0];
            double lowerArea = ups.Where(f => f != top).Sum(f => f.Area);
            if (lowerArea > 0.02 * top.Area)
            {
                LastError = "la losa tiene caras superiores a distintas cotas (escalonada o con rebaje); solo se arman losas de cara superior unica";
                return null;
            }
            s.ZBottom = downs.Min(f => f.Origin.Z);
            if (s.Thickness < Mm(40)) { LastError = "la losa es demasiado delgada (" + ToMm(s.Thickness) + " mm)"; return null; }

            // espesor del tipo, por si el solido esta cortado por otros elementos
            try
            {
                Parameter tp = host.get_Parameter(BuiltInParameter.FLOOR_ATTR_THICKNESS_PARAM);
                if (tp != null && tp.StorageType == StorageType.Double) s.TypeThickness = tp.AsDouble();
            }
            catch { }
            if (s.TypeThickness > 0 && Math.Abs(s.TypeThickness - s.Thickness) > tol)
                s.Note += " (el solido mide " + ToMm(s.Thickness) + " mm y el tipo " + ToMm(s.TypeThickness) + " mm: losa cortada o unida a otros elementos; se usa el solido)";
            if (downs.Count > 1 && downs.Any(f => Math.Abs(f.Origin.Z - s.ZBottom) > tol))
                s.Note += " (cara inferior partida: hay otros elementos que le quitan hormigon por debajo)";

            // --- contorno: anillos de la cara superior ---
            IList<CurveLoop> loops;
            try { loops = top.GetEdgesAsCurveLoops(); }
            catch (Exception ex) { LastError = "no se pudo leer el contorno de la cara superior (" + ex.Message + ")"; return null; }
            var rings = new List<List<Pt>>();
            foreach (CurveLoop loop in loops)
            {
                var pts = new List<Pt>();
                foreach (Curve c in loop)
                {
                    if (c is Line)
                        pts.Add(new Pt(c.GetEndPoint(0).X, c.GetEndPoint(0).Y));
                    else
                    {
                        IList<XYZ> tess = c.Tessellate();
                        for (int i = 0; i + 1 < tess.Count; i++) pts.Add(new Pt(tess[i].X, tess[i].Y));
                        if (tess.Count == 1) pts.Add(new Pt(tess[0].X, tess[0].Y));
                    }
                }
                pts = Geometry2D.Simplify(pts, tol);
                if (pts.Count >= 3 && Math.Abs(Geometry2D.SignedArea(pts)) > tol * tol) rings.Add(pts);
            }
            if (rings.Count == 0) { LastError = "el contorno de la cara superior esta vacio"; return null; }
            rings = rings.OrderByDescending(r => Math.Abs(Geometry2D.SignedArea(r))).ToList();
            if (rings.Count > 1)
            {
                // todos los demas anillos tienen que estar dentro del exterior (huecos)
                foreach (List<Pt> h in rings.Skip(1))
                    if (!Geometry2D.PointInRing(rings[0], Geometry2D.Centroid(h)))
                    {
                        LastError = "la cara superior tiene varios contornos exteriores (losa partida)";
                        return null;
                    }
            }
            s.WorldRings = rings;

            // --- vigas de apoyo (se buscan siempre; la configuracion decide si se usan) ---
            try { s.FindBeams(doc); }
            catch (Exception ex) { s.Note += " (no se pudieron leer las vigas: " + ex.Message + ")"; }
            return s;
        }

        /// <summary>
        /// Vigas (armazon estructural con eje recto) cuya caja envolvente toca la de la losa en
        /// planta y cuyo rango vertical toca el de la losa: tanto las que tienen la cara superior
        /// a ras de la losa como las que la sostienen por debajo. El eje se recentra en el solido
        /// y de el sale el ancho.
        /// </summary>
        private void FindBeams(Document doc)
        {
            double tol = _tol;
            BoundingBoxXYZ sb = Host.get_BoundingBox(null);
            if (sb == null) return;
            double grow = Mm(1500);
            var collector = new FilteredElementCollector(doc).OfCategory(BuiltInCategory.OST_StructuralFraming).WhereElementIsNotElementType();
            foreach (Element e in collector)
            {
                if (!(e is FamilyInstance fi)) continue;
                BoundingBoxXYZ bb = e.get_BoundingBox(null);
                if (bb == null) continue;
                if (bb.Max.X < sb.Min.X - grow || bb.Min.X > sb.Max.X + grow || bb.Max.Y < sb.Min.Y - grow || bb.Min.Y > sb.Max.Y + grow) continue;
                if (bb.Max.Z < ZBottom - tol || bb.Min.Z > ZTop + tol) continue;
                if (!(fi.Location is LocationCurve lc) || !(lc.Curve is Line ln)) continue;

                XYZ p = ln.GetEndPoint(0), q = ln.GetEndPoint(1);
                XYZ d = new XYZ(q.X - p.X, q.Y - p.Y, 0);
                if (d.GetLength() < Mm(100)) continue;
                d = d.Normalize();
                XYZ perp = new XYZ(-d.Y, d.X, 0);

                var info = new BeamInfo { Beam = e, Name = e.Name + " [" + e.Id + "]" };
                List<Solid> solids = Solids(e);
                double width = Mm(300);
                double shift = 0;
                if (solids.Count > 0)
                {
                    info.Solid = solids[0];
                    double lo = double.MaxValue, hi = double.MinValue;
                    foreach (Edge ed in info.Solid.Edges)
                        foreach (XYZ v in ed.Tessellate())
                        {
                            double t = v.X * perp.X + v.Y * perp.Y;
                            lo = Math.Min(lo, t); hi = Math.Max(hi, t);
                        }
                    if (hi > lo)
                    {
                        width = hi - lo;
                        shift = 0.5 * (lo + hi) - (p.X * perp.X + p.Y * perp.Y);   // recentrar el eje en el solido
                    }
                }
                info.Width = width;
                info.P = new XYZ(p.X + perp.X * shift, p.Y + perp.Y * shift, 0);
                info.Q = new XYZ(q.X + perp.X * shift, q.Y + perp.Y * shift, 0);
                Beams.Add(info);
                if (info.Solid != null) BeamSolids.Add(info.Solid);
            }
        }

        // ------------------------------------------------------------------
        // Sistema local
        // ------------------------------------------------------------------

        /// <summary>
        /// La losa en el sistema local de la direccion pedida ("short", "long", "x", "y" o
        /// "angle" con angleDeg), con o sin las vigas como apoyos. Se calcula una vez por combinacion.
        /// </summary>
        public SlabFrame Frame(string mode, double angleDeg, bool withBeams)
        {
            mode = AppConfig.NormalizeDirection(mode);
            string key = (mode == "angle" ? mode + ":" + Math.Round(angleDeg, 3).ToString(CultureInfo.InvariantCulture) : mode) + (withBeams ? "+vigas" : "");
            if (_frames.TryGetValue(key, out SlabFrame cached)) return cached;

            List<Pt> outer = WorldRings[0];
            Pt e = Geometry2D.LongestEdgeDirection(outer);
            Pt perp = new Pt(-e.V, e.U);
            double le = Extent(outer, e), lp = Extent(outer, perp);
            Pt dir;
            switch (mode)
            {
                case "x": dir = new Pt(1, 0); break;
                case "y": dir = new Pt(0, 1); break;
                case "angle":
                    double a = angleDeg * Math.PI / 180;
                    dir = new Pt(Math.Cos(a), Math.Sin(a));
                    break;
                case "long": dir = le >= lp ? e : perp; break;
                default: dir = le <= lp ? e : perp; break;   // short: las barras van en la dimension menor
            }
            var f = new SlabFrame { Slab = this, Mode = mode, AngleDeg = angleDeg, WithBeams = withBeams };
            f.DirU = new XYZ(dir.U, dir.V, 0).Normalize();
            f.DirV = XYZ.BasisZ.CrossProduct(f.DirU).Normalize();

            // contorno en coordenadas locales, con el minimo en (0, 0)
            List<List<Pt>> local = WorldRings.Select(r => r.Select(p => ToLocal(p, f)).ToList()).ToList();
            double umin = local[0].Min(p => p.U), vmin = local[0].Min(p => p.V);
            local = local.Select(r => r.Select(p => new Pt(p.U - umin, p.V - vmin)).ToList()).ToList();
            f.Origin = new XYZ(f.DirU.X * umin + f.DirV.X * vmin, f.DirU.Y * umin + f.DirV.Y * vmin, ZBottom);
            f.Outline = new Outline2D(local[0], local.Skip(1), _tol);

            // vigas casi perpendiculares a u = apoyos (franja [U1, U2] a lo largo de v)
            double cosTol = Math.Cos(3 * Math.PI / 180);
            foreach (BeamInfo b in withBeams ? Beams : new List<BeamInfo>())
            {
                Pt p = f.Local(b.P), q = f.Local(b.Q);
                double du = q.U - p.U, dv = q.V - p.V, len = Math.Sqrt(du * du + dv * dv);
                if (len < 1e-9) continue;
                if (Math.Abs(dv) / len < cosTol)
                {
                    f.IgnoredBeams.Add(b.Name + (Math.Abs(du) / len >= cosTol ? " (paralela a las barras)" : " (oblicua)"));
                    continue;
                }
                double cu = 0.5 * (p.U + q.U);
                f.Supports.Add(new Support
                {
                    U1 = cu - 0.5 * b.Width, U2 = cu + 0.5 * b.Width,
                    V1 = Math.Min(p.V, q.V), V2 = Math.Max(p.V, q.V), Name = b.Name
                });
            }
            f.Supports = f.Supports.OrderBy(x => x.CU).ToList();
            _frames[key] = f;
            return f;
        }

        private static Pt ToLocal(Pt world, SlabFrame f) =>
            new Pt(world.U * f.DirU.X + world.V * f.DirU.Y, world.U * f.DirV.X + world.V * f.DirV.Y);

        private static double Extent(List<Pt> pts, Pt dir)
        {
            double lo = double.MaxValue, hi = double.MinValue;
            foreach (Pt p in pts)
            {
                double t = p.U * dir.U + p.V * dir.V;
                lo = Math.Min(lo, t); hi = Math.Max(hi, t);
            }
            return hi - lo;
        }

        // ------------------------------------------------------------------
        // Solidos
        // ------------------------------------------------------------------
        private static Options GeometryOptions() =>
            new Options { DetailLevel = ViewDetailLevel.Fine, ComputeReferences = false, IncludeNonVisibleObjects = false };

        /// <summary>Todos los solidos con volumen del elemento, de mayor a menor.</summary>
        public static List<Solid> Solids(Element e)
        {
            var list = new List<Solid>();
            GeometryElement ge;
            try { ge = e.get_Geometry(GeometryOptions()); }
            catch { return list; }
            if (ge == null) return list;
            void Scan(IEnumerable<GeometryObject> objs)
            {
                foreach (GeometryObject go in objs)
                {
                    if (go is Solid sol) { if (sol.Volume > 1e-9) list.Add(sol); }
                    else if (go is GeometryInstance gi) Scan(gi.GetInstanceGeometry());
                }
            }
            Scan(ge);
            return list.OrderByDescending(x => x.Volume).ToList();
        }

        internal static string TypeNameOf(Document doc, Element e)
        {
            ElementId tid = e.GetTypeId();
            Element t = (tid != null && tid != ElementId.InvalidElementId) ? doc.GetElement(tid) : null;
            return t?.Name ?? e.Name;
        }
    }
}
