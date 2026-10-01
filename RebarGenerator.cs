using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Structure;

namespace SlabRebar
{
    /// <summary>Un conjunto (elemento Rebar) creado para una losa.</summary>
    public sealed class CreatedSet
    {
        public ElementId Id;
        public string Name;
        /// <summary>Radio nominal de la barra (pies).</summary>
        public double Radius;
        public bool AlongU;
        /// <summary>Tramo de la barra que queda dentro del contorno (coordenada a lo largo de su eje, pies): solo eso se comprueba.</summary>
        public double InA, InB;
    }

    /// <summary>Resultado del armado de un elemento.</summary>
    public sealed class BuildResult
    {
        public List<CreatedSet> Created = new List<CreatedSet>();
        /// <summary>Barras que quedarian fuera del hormigon. Si hay alguna, el elemento entero se deshace.</summary>
        public List<string> Rejected = new List<string>();
        /// <summary>Conjuntos que Revit no pudo crear.</summary>
        public List<string> Failed = new List<string>();
        public List<string> Warnings = new List<string>();
        public int Bars;
        public Dictionary<BarLayer, int> ByLayer = new Dictionary<BarLayer, int>();
        public string Summary => Bars + " barras en " + Created.Count + " conjuntos (" +
                                 string.Join(", ", ByLayer.OrderBy(k => (int)k.Key).Select(k => Layers.Name(k.Key) + " " + k.Value)) + ")";
        public bool Safe => Rejected.Count == 0;
    }

    public static class RebarGenerator
    {
        private static double Mm(double mm) => SlabOutline.Mm(mm);
        private static double ToMm(double ft) => SlabOutline.ToMm(ft);
        private const double MinSeg = 0.003;   // ~1 mm en pies
        /// <summary>Longitud de barra que se tolera fuera del solido al comprobar (pies, ~1 mm).</summary>
        private const double InsideTol = 0.0033;

        private sealed class Ctx
        {
            public Document Doc;
            public HostAnalysis Item;
            public SlabFrame F;
            public AppConfig Cfg;
            public BuildResult Result;
            public SlabPlan Plan;
            public double Tol;
            /// <summary>Tipo de barra y gancho de cada capa.</summary>
            public Dictionary<BarLayer, RebarBarType> BarTypes = new Dictionary<BarLayer, RebarBarType>();
            public Dictionary<BarLayer, ElementId> Hooks = new Dictionary<BarLayer, ElementId>();
            /// <summary>Orientacion de los ganchos de cada capa (se invierte sola si el gancho dobla hacia el lado equivocado).</summary>
            public Dictionary<BarLayer, bool> HookLeft = new Dictionary<BarLayer, bool>();
            public HashSet<BarLayer> HookChecked = new HashSet<BarLayer>();
        }

        // =================================================================
        // Armado de una losa
        // =================================================================
        public static BuildResult Build(Document doc, HostAnalysis item, AppConfig cfg)
        {
            SlabFrame f = item.Frame(cfg);
            var c = new Ctx { Doc = doc, Item = item, F = f, Cfg = cfg, Result = new BuildResult(), Tol = Mm(cfg.ToleranceMm) };
            SlabKind kind = item.KindFor(cfg);

            // tipos de barra y ganchos de las capas que se van a colocar
            var d = new PlanDiameters();
            foreach ((BarLayer layer, LayerCfg lc) in LayersFor(kind, cfg))
            {
                RebarBarType bt = FindBarType(doc, lc.BarTypeName, Layers.Name(layer));
                c.BarTypes[layer] = bt;
                c.Hooks[layer] = FindHookType(doc, lc.HookTypeName);
                c.HookLeft[layer] = true;
                SetDiameter(d, layer, bt.BarNominalDiameter);
            }

            c.Plan = SlabPlan.Build(f.Outline, f.Supports, f.Thickness, kind, cfg, d);
            if (c.Plan.Error != null) throw new InvalidOperationException(c.Plan.Error);
            c.Result.Warnings.AddRange(c.Plan.Warnings);
            if (c.Plan.Skipped > 0) c.Result.Warnings.Add(c.Plan.Skipped + " tramo(s) demasiado cortos omitidos");
            if (f.IgnoredBeams.Count > 0) c.Result.Warnings.Add("vigas no usadas como apoyo: " + string.Join(", ", f.IgnoredBeams));

            int n = 0;
            foreach (BarGroup g in c.Plan.Groups)
            {
                n++;
                if (!Place(c, g, n)) break;
            }
            return c.Result;
        }

        /// <summary>Capas que lleva la losa con esta configuracion y su configuracion.</summary>
        public static List<(BarLayer, LayerCfg)> LayersFor(SlabKind kind, AppConfig cfg)
        {
            var list = new List<(BarLayer, LayerCfg)>();
            if (kind == SlabKind.Aligerada)
            {
                list.Add((BarLayer.JoistBottom, cfg.Aligerada.Bottom));
                if (!cfg.Aligerada.Top.None) list.Add((BarLayer.JoistTop, cfg.Aligerada.Top));
                if (cfg.Aligerada.Temperature.Enabled) list.Add((BarLayer.Temperature, cfg.Aligerada.Temperature));
            }
            else
            {
                list.Add((BarLayer.BottomMain, cfg.Maciza.BottomMain));
                if (cfg.Maciza.BottomSecondary.Enabled) list.Add((BarLayer.BottomSecondary, cfg.Maciza.BottomSecondary));
                if (!cfg.Maciza.TopMain.None) list.Add((BarLayer.TopMain, cfg.Maciza.TopMain));
                if (cfg.Maciza.TopSecondary.Enabled) list.Add((BarLayer.TopSecondary, cfg.Maciza.TopSecondary));
            }
            return list;
        }

        public static void SetDiameter(PlanDiameters d, BarLayer layer, double ft)
        {
            switch (layer)
            {
                case BarLayer.JoistBottom: d.JoistBottom = ft; break;
                case BarLayer.JoistTop: d.JoistTop = ft; break;
                case BarLayer.Temperature: d.Temperature = ft; break;
                case BarLayer.BottomMain: d.BottomMain = ft; break;
                case BarLayer.BottomSecondary: d.BottomSecondary = ft; break;
                case BarLayer.TopMain: d.TopMain = ft; break;
                default: d.TopSecondary = ft; break;
            }
        }

        /// <summary>Armado de la losa con esta configuracion y estos diametros (lo mismo que dibuja la ventana).</summary>
        public static SlabPlan PlanFor(HostAnalysis item, AppConfig cfg, PlanDiameters d)
        {
            SlabFrame f = item.Frame(cfg);
            return SlabPlan.Build(f.Outline, f.Supports, f.Thickness, item.KindFor(cfg), cfg, d);
        }

        // =================================================================
        // Colocacion con red de seguridad
        // =================================================================

        /// <summary>
        /// Crea un conjunto (array de barras iguales). Antes comprueba que cada posicion del
        /// array queda dentro del hormigon (solo el tramo dentro del contorno; la prolongacion
        /// hacia la viga no se comprueba). Si la capa lleva gancho, tras crear la primera barra
        /// lee su geometria real y, si el gancho dobla hacia el lado equivocado (abajo en una
        /// capa inferior, arriba en una superior), la borra, invierte la orientacion y la
        /// vuelve a crear. False si algo se rechazo.
        /// </summary>
        private static bool Place(Ctx c, BarGroup g, int index)
        {
            PlannedBar b = g.First;
            SlabFrame f = c.F;
            RebarBarType bt = c.BarTypes[b.Layer];
            ElementId hook = c.Hooks.TryGetValue(b.Layer, out ElementId h) ? h : ElementId.InvalidElementId;
            double r = bt.BarNominalDiameter * 0.5;
            double z = f.ZBottom + b.Z;
            XYZ normal = b.AlongU ? f.DirV : f.DirU;
            XYZ p0 = b.AlongU ? f.World(b.Start, b.Coord, z) : f.World(b.Coord, b.Start, z);
            XYZ p1 = b.AlongU ? f.World(b.End, b.Coord, z) : f.World(b.Coord, b.End, z);
            var curves = new List<Curve>();
            AddLine(curves, p0, p1);
            if (curves.Count == 0) { c.Result.Rejected.Add(NameOf(b, g, index) + ": sin longitud"); return false; }

            bool array = g.Count >= 2 && g.Spacing > MinSeg;
            string name = NameOf(b, g, index);

            // --- RED DE SEGURIDAD (1): geometria planificada, antes de crear nada ---
            for (int k = 0; k < (array ? g.Count : 1); k++)
            {
                IList<Curve> moved = curves;
                if (k > 0)
                {
                    Transform t = Transform.CreateTranslation(normal * (k * g.Spacing));
                    moved = curves.Select(cv => cv.CreateTransformed(t)).ToList();
                }
                if (!BarInside(f, f.CheckSolids, moved, r, b.AlongU, b.InA, b.InB, out string why))
                {
                    c.Result.Rejected.Add(name + (k > 0 ? " (posicion " + (k + 1) + " del array)" : "") + ": " + why);
                    return false;
                }
            }

            bool hasHook = hook != ElementId.InvalidElementId && (b.HookStart || b.HookEnd);
            bool checkHook = hasHook && !c.HookChecked.Contains(b.Layer);
            bool wantUp = !Layers.IsTop(b.Layer);
            for (int attempt = 0; attempt < 2; attempt++)
            {
                bool left = c.HookLeft[b.Layer];
                Rebar rb = Create(c.Doc, c.Item.Host, bt, normal, curves,
                                  b.HookStart ? hook : ElementId.InvalidElementId, b.HookEnd ? hook : ElementId.InvalidElementId, left, out string err);
                if (rb == null) { c.Result.Failed.Add(name + ": Revit no pudo crear la barra (" + err + ")"); return true; }

                if (checkHook)
                {
                    // RED DE SEGURIDAD (1b): el gancho solo existe en la geometria real
                    c.Doc.Regenerate();
                    int dir = HookDirection(rb, z, r, c.Tol);
                    if (dir != 0 && (dir > 0) != wantUp)
                    {
                        c.Doc.Delete(rb.Id);
                        if (attempt == 0)
                        {
                            c.HookLeft[b.Layer] = !left;
                            c.Result.Warnings.Add(Layers.Name(b.Layer) + ": los ganchos doblaban hacia " + (dir > 0 ? "arriba" : "abajo") + ", se ha invertido su orientacion");
                            continue;
                        }
                        c.Result.Rejected.Add(name + ": los ganchos doblan hacia el lado equivocado con las dos orientaciones");
                        return false;
                    }
                    c.HookChecked.Add(b.Layer);
                }

                if (array) rb.GetShapeDrivenAccessor().SetLayoutAsFixedNumber(g.Count, (g.Count - 1) * g.Spacing, true, true, true);
                else rb.GetShapeDrivenAccessor().SetLayoutAsSingle();

                Finish(c.Doc, rb, c.Item.Partition(c.Cfg, SetName(b), Layers.Short(b.Layer)));
                c.Result.Created.Add(new CreatedSet { Id = rb.Id, Name = name, Radius = r, AlongU = b.AlongU, InA = b.InA, InB = b.InB });
                c.Result.Bars += g.Count;
                c.Result.ByLayer[b.Layer] = (c.Result.ByLayer.TryGetValue(b.Layer, out int prev) ? prev : 0) + g.Count;
                return true;
            }
            return false;
        }

        private static string SetName(PlannedBar b) => Layers.Name(b.Layer) + (b.AlongU ? "" : " (perpendicular)");

        private static string NameOf(PlannedBar b, BarGroup g, int index)
        {
            string pos = b.AlongU ? "v=" + ToMm(b.Coord) : "u=" + ToMm(b.Coord);
            return "conjunto " + index + " " + Layers.Name(b.Layer) + " " + pos + " mm, L=" + ToMm(b.Length) + " mm" +
                   (g.Count > 1 ? " (" + g.Count + " barras cada " + ToMm(g.Spacing) + " mm)" : "");
        }

        /// <summary>
        /// Hacia donde dobla el gancho de la barra real: +1 arriba, -1 abajo, 0 sin desviacion
        /// vertical apreciable (sin gancho o gancho en el plano horizontal).
        /// </summary>
        private static int HookDirection(Rebar rb, double zBar, double r, double tol)
        {
            try
            {
                IList<Curve> cl = rb.GetCenterlineCurves(false, false, false, MultiplanarOption.IncludeOnlyPlanarCurves, 0);
                double zMin = double.MaxValue, zMax = double.MinValue;
                foreach (Curve cv in cl)
                    foreach (XYZ p in cv.Tessellate()) { zMin = Math.Min(zMin, p.Z); zMax = Math.Max(zMax, p.Z); }
                double limit = Math.Max(2 * r, tol) + 1e-6;
                if (zMax > zBar + limit) return 1;
                if (zMin < zBar - limit) return -1;
            }
            catch { }
            return 0;
        }

        /// <summary>
        /// RED DE SEGURIDAD (2): tras crear y regenerar, se lee la geometria REAL de cada
        /// barra de cada conjunto tal y como la ha colocado Revit (radios de doblado, ganchos
        /// y todas las posiciones del array) y se comprueba contra el hormigon de la losa (y de
        /// las vigas detectadas). Solo se comprueba el tramo dentro del contorno.
        /// </summary>
        public static void VerifyCreated(Document doc, HostAnalysis item, AppConfig cfg, BuildResult res)
        {
            SlabFrame f = item.Frame(cfg);
            foreach (CreatedSet cs in res.Created)
            {
                var rb = doc.GetElement(cs.Id) as Rebar;
                if (rb == null) { res.Rejected.Add(cs.Name + ": el conjunto no existe tras regenerar"); continue; }
                if (!RealInside(f, f.CheckSolids, rb, cs, out string why)) res.Rejected.Add(cs.Name + ": " + why);
            }
        }

        private static bool RealInside(SlabFrame f, List<Solid> solids, Rebar rb, CreatedSet cs, out string why)
        {
            why = null;
            int n;
            try { n = rb.NumberOfBarPositions; }
            catch (Exception ex) { why = "no se pudo leer el conjunto (" + ex.Message + ")"; return false; }
            for (int k = 0; k < n; k++)
            {
                IList<Curve> cl;
                try
                {
                    if (!rb.DoesBarExistAtPosition(k)) continue;
                    cl = rb.GetCenterlineCurves(false, false, false, MultiplanarOption.IncludeOnlyPlanarCurves, k);
                }
                catch (Exception ex) { why = "barra " + (k + 1) + " de " + n + ": no se pudo leer su geometria (" + ex.Message + ")"; return false; }
                if (cl == null || cl.Count == 0) { why = "barra " + (k + 1) + " de " + n + ": sin geometria"; return false; }
                if (!BarInside(f, solids, cl, cs.Radius, cs.AlongU, cs.InA, cs.InB, out string w)) { why = "barra " + (k + 1) + " de " + n + ": " + w; return false; }
            }
            return true;
        }

        /// <summary>
        /// True si el tramo de la barra que queda dentro del contorno (coordenada a lo largo de
        /// su eje entre inA e inB) esta dentro del hormigon. Ademas del eje se comprueban
        /// cuatro fibras extremas (eje desplazado +-r en horizontal perpendicular y en
        /// vertical), asi una barra tangente a una cara o con medio diametro fuera tambien falla.
        /// </summary>
        private static bool BarInside(SlabFrame f, List<Solid> solids, IList<Curve> curves, double r, bool alongU, double inA, double inB, out string why)
        {
            why = null;
            XYZ side = alongU ? f.DirV : f.DirU;
            var shifts = new List<XYZ> { XYZ.Zero, side * r, side * -r, XYZ.BasisZ * r, XYZ.BasisZ * -r };
            IEnumerable<Curve> toCheck = curves.SelectMany(cv => ClipToRange(f, cv, alongU, inA + InsideTol, inB - InsideTol));
            foreach (Curve cv in toCheck)
                foreach (XYZ sh in shifts)
                {
                    Curve probe = sh.IsZeroLength() ? cv : cv.CreateTransformed(Transform.CreateTranslation(sh));
                    if (!CurveInside(solids, probe, out double outside))
                    {
                        why = "queda fuera del hormigon (" + ToMm(outside) + " mm de barra fuera; segmento de " +
                              f.LocalMm(cv.GetEndPoint(0)) + " a " + f.LocalMm(cv.GetEndPoint(1)) + ")";
                        return false;
                    }
                }
            return true;
        }

        /// <summary>Trozos de la curva (como lineas) cuya coordenada a lo largo del eje de la barra esta entre a y b.</summary>
        private static List<Curve> ClipToRange(SlabFrame f, Curve cv, bool alongU, double a, double b)
        {
            var result = new List<Curve>();
            IList<XYZ> pts = cv.Tessellate();
            for (int i = 0; i + 1 < pts.Count; i++)
            {
                XYZ p = pts[i], q = pts[i + 1];
                double sp = f.Along(p, alongU), sq = f.Along(q, alongU);
                if (Math.Max(sp, sq) < a || Math.Min(sp, sq) > b) continue;
                XYZ lo = p, hi = q;
                if (Math.Abs(sq - sp) > 1e-12)
                {
                    double ta = Math.Max(0, Math.Min(1, (a - sp) / (sq - sp)));
                    double tb = Math.Max(0, Math.Min(1, (b - sp) / (sq - sp)));
                    double t0 = Math.Min(ta, tb), t1 = Math.Max(ta, tb);
                    if (sp < a || sp > b) lo = p + (q - p) * (sp < sq ? t0 : t1);
                    if (sq < a || sq > b) hi = p + (q - p) * (sp < sq ? t1 : t0);
                }
                if (lo.DistanceTo(hi) > MinSeg) result.Add(Line.CreateBound(lo, hi));
            }
            return result;
        }

        /// <summary>Longitud de la curva que queda fuera de todos los solidos; no verificable cuenta como fuera.</summary>
        private static bool CurveInside(List<Solid> solids, Curve cv, out double outsideLen)
        {
            outsideLen = cv.Length;
            double inside = 0;
            bool any = false;
            foreach (Solid solid in solids)
            {
                try
                {
                    var opt = new SolidCurveIntersectionOptions { ResultType = SolidCurveIntersectionMode.CurveSegmentsInside };
                    SolidCurveIntersection ix = solid.IntersectWithCurve(cv, opt);
                    if (ix != null)
                        for (int i = 0; i < ix.SegmentCount; i++) inside += ix.GetCurveSegment(i).Length;
                    any = true;
                }
                catch { }
            }
            if (!any) return false;
            outsideLen = Math.Max(0, cv.Length - inside);
            return outsideLen <= InsideTol;
        }

        // =================================================================
        // Utilidades
        // =================================================================
        private static void AddLine(List<Curve> list, XYZ a, XYZ b)
        {
            if (a.DistanceTo(b) > MinSeg) list.Add(Line.CreateBound(a, b));
        }

        private static Rebar Create(Document doc, Element host, RebarBarType bt, XYZ normal, IList<Curve> curves,
                                    ElementId hookStart, ElementId hookEnd, bool hookLeft, out string err)
        {
            err = null;
            try
            {
                // Revit 2027: ganchos y tratamientos de extremo van agrupados en BarTerminationsData.
                using (BarTerminationsData term = new BarTerminationsData(doc))
                {
                    if (hookStart != null && hookStart != ElementId.InvalidElementId) term.HookTypeIdAtStart = hookStart;
                    if (hookEnd != null && hookEnd != ElementId.InvalidElementId) term.HookTypeIdAtEnd = hookEnd;
                    RebarTerminationOrientation o = hookLeft ? RebarTerminationOrientation.Left : RebarTerminationOrientation.Right;
                    term.TerminationOrientationAtStart = o;
                    term.TerminationOrientationAtEnd = o;
                    return Rebar.CreateFromCurves(doc, RebarStyle.Standard, bt, host, normal.Normalize(), curves, term, true, true);
                }
            }
            catch (Exception ex)
            {
                err = ex.Message;
                return null;
            }
        }

        private static void Finish(Document doc, Rebar r, string partition)
        {
            if (!string.IsNullOrEmpty(partition))
            {
                Parameter p = null;
                try { p = r.get_Parameter(BuiltInParameter.NUMBER_PARTITION_PARAM); } catch { }
                if (p == null) p = r.LookupParameter("Partition") ?? r.LookupParameter("Particion") ?? r.LookupParameter("Partición");
                if (p != null && !p.IsReadOnly) { try { p.Set(partition); } catch { } }
            }
            try { r.SetUnobscuredInView(doc.ActiveView, true); } catch { }
        }

        public static RebarBarType FindBarType(Document doc, string name, string use)
        {
            var all = AllBarTypes(doc);
            if (all.Count == 0)
                throw new InvalidOperationException("El proyecto no tiene ningun tipo de barra (RebarBarType). Carga una familia de armadura primero.");
            string match = MatchName(all.Select(b => b.Name), name);
            if (match == null)
                throw new InvalidOperationException("el tipo de barra de " + use + " \"" + name + "\" no existe en este proyecto; elige uno de los cargados en la ventana");
            return all.First(b => b.Name == match);
        }

        /// <summary>Id del tipo de gancho, o InvalidElementId si el nombre esta vacio. Lanza si el nombre no existe.</summary>
        public static ElementId FindHookType(Document doc, string name)
        {
            if (string.IsNullOrWhiteSpace(name)) return ElementId.InvalidElementId;
            var all = AllHookTypes(doc);
            string match = MatchName(all.Select(h => h.Name), name);
            if (match == null)
                throw new InvalidOperationException("el tipo de gancho \"" + name + "\" no existe en este proyecto; elige uno de los cargados en la ventana o deja el gancho vacio");
            return all.First(h => h.Name == match).Id;
        }

        /// <summary>
        /// Nombre que corresponde a "name": coincidencia exacta, si no parcial (sin distinguir
        /// mayusculas); null si no hay ninguna. Nunca se sustituye por otro: sin coincidencia no se arma.
        /// </summary>
        public static string MatchName(IEnumerable<string> names, string name)
        {
            if (string.IsNullOrWhiteSpace(name)) return null;
            var list = names.ToList();
            string exact = list.FirstOrDefault(n => string.Equals(n, name, StringComparison.OrdinalIgnoreCase));
            if (exact != null) return exact;
            return list.FirstOrDefault(n => n.IndexOf(name, StringComparison.OrdinalIgnoreCase) >= 0);
        }

        public static List<RebarBarType> AllBarTypes(Document doc) =>
            new FilteredElementCollector(doc).OfClass(typeof(RebarBarType)).Cast<RebarBarType>()
                .OrderBy(b => b.Name, StringComparer.OrdinalIgnoreCase).ToList();

        public static List<RebarHookType> AllHookTypes(Document doc) =>
            new FilteredElementCollector(doc).OfClass(typeof(RebarHookType)).Cast<RebarHookType>()
                .OrderBy(h => h.Name, StringComparer.OrdinalIgnoreCase).ToList();
    }
}
