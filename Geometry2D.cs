using System;
using System.Collections.Generic;
using System.Linq;

namespace SlabRebar
{
    /// <summary>Punto 2D en coordenadas locales de la losa (u, v), en pies.</summary>
    public struct Pt
    {
        public double U, V;
        public Pt(double u, double v) { U = u; V = v; }
        public double DistanceTo(Pt o) => Math.Sqrt((U - o.U) * (U - o.U) + (V - o.V) * (V - o.V));
        public override string ToString() => "(" + U + ", " + V + ")";
    }

    /// <summary>
    /// Tramo interior de una recta que cruza la losa: de A a B (coordenada a lo largo de la
    /// recta, pies). HoleA / HoleB dicen si ese extremo lo marca el borde de un hueco (true)
    /// o el borde exterior de la losa (false): en el exterior la barra puede prolongarse
    /// hacia la viga; en un hueco siempre para al recubrimiento.
    /// </summary>
    public struct Span
    {
        public double A, B;
        public bool HoleA, HoleB;
        public Span(double a, double b, bool holeA, bool holeB) { A = a; B = b; HoleA = holeA; HoleB = holeB; }
        public double Length => B - A;
        public override string ToString() => "[" + A + (HoleA ? "h" : "") + ", " + B + (HoleB ? "h" : "") + "]";
    }

    /// <summary>
    /// Contorno de la losa en coordenadas locales: anillo exterior (antihorario) y huecos.
    /// Sabe recortar una recta (u = cte o v = cte) contra el contorno devolviendo los tramos
    /// interiores, que son las barras. Geometria pura, sin Revit.
    /// </summary>
    public sealed class Outline2D
    {
        public List<Pt> Outer;
        public List<List<Pt>> Holes;
        public double UMin, UMax, VMin, VMax;
        public double Width => UMax - UMin;
        public double Depth => VMax - VMin;

        private Outline2D _transposed;

        public Outline2D(List<Pt> outer, IEnumerable<List<Pt>> holes, double tol)
        {
            Outer = Geometry2D.Simplify(outer, tol);
            if (Geometry2D.SignedArea(Outer) < 0) Outer.Reverse();
            Holes = new List<List<Pt>>();
            if (holes != null)
                foreach (List<Pt> h in holes)
                {
                    List<Pt> s = Geometry2D.Simplify(h, tol);
                    if (s.Count >= 3 && Math.Abs(Geometry2D.SignedArea(s)) > tol * tol) Holes.Add(s);
                }
            UMin = Outer.Min(p => p.U); UMax = Outer.Max(p => p.U);
            VMin = Outer.Min(p => p.V); VMax = Outer.Max(p => p.V);
        }

        /// <summary>Area neta (exterior menos huecos).</summary>
        public double Area => Math.Abs(Geometry2D.SignedArea(Outer)) - Holes.Sum(h => Math.Abs(Geometry2D.SignedArea(h)));

        public IEnumerable<List<Pt>> Rings()
        {
            yield return Outer;
            foreach (List<Pt> h in Holes) yield return h;
        }

        /// <summary>Copia desplazada (du, dv).</summary>
        public Outline2D Translated(double du, double dv, double tol) =>
            new Outline2D(Outer.Select(p => new Pt(p.U + du, p.V + dv)).ToList(),
                          Holes.Select(h => h.Select(p => new Pt(p.U + du, p.V + dv)).ToList()), tol);

        /// <summary>El mismo contorno con u y v intercambiados (para cortar con rectas u = cte).</summary>
        public Outline2D Transposed(double tol)
        {
            if (_transposed == null)
                _transposed = new Outline2D(Outer.Select(p => new Pt(p.V, p.U)).ToList(),
                                            Holes.Select(h => h.Select(p => new Pt(p.V, p.U)).ToList()), tol);
            return _transposed;
        }

        /// <summary>True si el punto esta dentro de la losa (fuera de los huecos), regla par-impar.</summary>
        public bool Contains(Pt p)
        {
            bool inside = false;
            foreach (List<Pt> ring in Rings())
                if (Geometry2D.PointInRing(ring, p)) inside = !inside;
            return inside;
        }

        /// <summary>
        /// Tramos interiores de la recta v = coord (alongU) o u = coord (!alongU), tales que
        /// toda la franja de semiancho "halfStrip" alrededor de la recta queda dentro del
        /// hormigon: asi la barra guarda ese recubrimiento con los bordes paralelos a ella.
        /// Los extremos de cada tramo llevan la marca de hueco o exterior.
        /// </summary>
        public List<Span> Cut(bool alongU, double coord, double halfStrip, double tol)
        {
            Outline2D o = alongU ? this : Transposed(tol);
            double eps = Math.Max(0.5 * tol, 1e-7);
            // la franja se encoge "eps" para que una barra colocada exactamente al recubrimiento
            // (su franja toca el borde) no quede fuera por el filo del borde
            double strip = halfStrip - eps;
            var offsets = new List<double> { -eps, eps };
            if (strip > eps) { offsets.Insert(0, -strip); offsets.Add(strip); }
            List<Span> result = null;
            foreach (double d in offsets)
            {
                List<Span> raw = Geometry2D.RawCut(o, coord + d);
                result = result == null ? raw : Geometry2D.Intersect(result, raw, tol);
                if (result.Count == 0) break;
            }
            return result ?? new List<Span>();
        }
    }

    /// <summary>Franja de apoyo (viga) en coordenadas locales: ocupa [U1, U2] en u y se extiende de V1 a V2 en v.</summary>
    public sealed class Support
    {
        public double U1, U2, V1, V2;
        public string Name = "";
        public double Width => U2 - U1;
        public double CU => 0.5 * (U1 + U2);
        /// <summary>True si la viga cruza la recta v = coord.</summary>
        public bool Crosses(double coord, double tol) => coord >= V1 - tol && coord <= V2 + tol;
        public override string ToString() => Name + " u=[" + U1 + ", " + U2 + "] v=[" + V1 + ", " + V2 + "]";
    }

    /// <summary>Geometria pura (sin Revit) de poligonos y repartos. Se puede probar fuera de Revit.</summary>
    public static class Geometry2D
    {
        /// <summary>Area con signo (positiva si el poligono va en sentido antihorario).</summary>
        public static double SignedArea(IList<Pt> poly)
        {
            double a = 0;
            for (int i = 0; i < poly.Count; i++)
            {
                Pt p = poly[i], q = poly[(i + 1) % poly.Count];
                a += p.U * q.V - q.U * p.V;
            }
            return 0.5 * a;
        }

        /// <summary>Centroide del poligono (formula del area); el centro de la caja si el area es nula.</summary>
        public static Pt Centroid(IList<Pt> poly)
        {
            double a = SignedArea(poly);
            if (Math.Abs(a) < 1e-12)
                return new Pt(0.5 * (poly.Min(p => p.U) + poly.Max(p => p.U)), 0.5 * (poly.Min(p => p.V) + poly.Max(p => p.V)));
            double cu = 0, cv = 0;
            for (int i = 0; i < poly.Count; i++)
            {
                Pt p = poly[i], q = poly[(i + 1) % poly.Count];
                double w = p.U * q.V - q.U * p.V;
                cu += (p.U + q.U) * w;
                cv += (p.V + q.V) * w;
            }
            return new Pt(cu / (6 * a), cv / (6 * a));
        }

        /// <summary>Quita vertices repetidos y colineales (tolerancia en pies).</summary>
        public static List<Pt> Simplify(IList<Pt> poly, double tol)
        {
            var pts = new List<Pt>();
            foreach (Pt p in poly)
                if (pts.Count == 0 || pts[pts.Count - 1].DistanceTo(p) > tol) pts.Add(p);
            while (pts.Count > 1 && pts[0].DistanceTo(pts[pts.Count - 1]) <= tol) pts.RemoveAt(pts.Count - 1);
            bool changed = true;
            while (changed && pts.Count > 3)
            {
                changed = false;
                for (int i = 0; i < pts.Count; i++)
                {
                    Pt a = pts[(i + pts.Count - 1) % pts.Count], b = pts[i], c = pts[(i + 1) % pts.Count];
                    double la = a.DistanceTo(b), lc = b.DistanceTo(c);
                    if (la < 1e-12 || lc < 1e-12) { pts.RemoveAt(i); changed = true; break; }
                    // distancia de b a la recta ac
                    double du = c.U - a.U, dv = c.V - a.V, len = Math.Sqrt(du * du + dv * dv);
                    if (len < 1e-12) continue;
                    double dist = Math.Abs(du * (b.V - a.V) - dv * (b.U - a.U)) / len;
                    // colineal y entre a y c (no un pico que vuelve)
                    double t = ((b.U - a.U) * du + (b.V - a.V) * dv) / (len * len);
                    if (dist <= tol && t > -1e-9 && t < 1 + 1e-9) { pts.RemoveAt(i); changed = true; break; }
                }
            }
            return pts;
        }

        /// <summary>Direccion unitaria del borde mas largo del anillo (du, dv), con du >= 0 (o dv > 0 si du = 0).</summary>
        public static Pt LongestEdgeDirection(IList<Pt> ring)
        {
            double best = -1; Pt dir = new Pt(1, 0);
            for (int i = 0; i < ring.Count; i++)
            {
                Pt a = ring[i], b = ring[(i + 1) % ring.Count];
                double du = b.U - a.U, dv = b.V - a.V, len = Math.Sqrt(du * du + dv * dv);
                if (len > best && len > 1e-12)
                {
                    best = len;
                    dir = new Pt(du / len, dv / len);
                }
            }
            if (dir.U < -1e-12 || (Math.Abs(dir.U) <= 1e-12 && dir.V < 0)) dir = new Pt(-dir.U, -dir.V);
            return dir;
        }

        /// <summary>Punto dentro de un anillo simple (regla par-impar, sin tolerancia).</summary>
        public static bool PointInRing(IList<Pt> ring, Pt p)
        {
            bool inside = false;
            for (int i = 0, j = ring.Count - 1; i < ring.Count; j = i++)
            {
                Pt a = ring[i], b = ring[j];
                if ((a.V > p.V) != (b.V > p.V))
                {
                    double u = a.U + (p.V - a.V) / (b.V - a.V) * (b.U - a.U);
                    if (p.U < u) inside = !inside;
                }
            }
            return inside;
        }

        /// <summary>
        /// Cruces de la recta v = coord con todos los anillos del contorno, emparejados en
        /// tramos interiores (regla par-impar: exterior y huecos a la vez). Sin franja.
        /// </summary>
        public static List<Span> RawCut(Outline2D o, double coord)
        {
            var xs = new List<(double u, bool hole)>();
            bool holeRing = false;
            foreach (List<Pt> ring in o.Rings())
            {
                for (int i = 0; i < ring.Count; i++)
                {
                    Pt a = ring[i], b = ring[(i + 1) % ring.Count];
                    // regla semiabierta: cada vertice cuenta en un unico lado
                    if ((a.V <= coord) != (b.V <= coord))
                    {
                        double t = (coord - a.V) / (b.V - a.V);
                        xs.Add((a.U + t * (b.U - a.U), holeRing));
                    }
                }
                holeRing = true;
            }
            xs.Sort((p, q) => p.u.CompareTo(q.u));
            var spans = new List<Span>();
            for (int i = 0; i + 1 < xs.Count; i += 2)
                spans.Add(new Span(xs[i].u, xs[i + 1].u, xs[i].hole, xs[i + 1].hole));
            return spans;
        }

        /// <summary>Interseccion de dos listas de tramos (cada una ordenada y sin solapes).</summary>
        public static List<Span> Intersect(List<Span> a, List<Span> b, double tol)
        {
            var result = new List<Span>();
            foreach (Span x in a)
                foreach (Span y in b)
                {
                    double s = Math.Max(x.A, y.A), e = Math.Min(x.B, y.B);
                    if (e - s <= tol) continue;
                    bool hs = x.A >= y.A - 1e-12 ? x.HoleA : y.HoleA;
                    bool he = x.B <= y.B + 1e-12 ? x.HoleB : y.HoleB;
                    // si los dos extremos coinciden, basta que uno sea de hueco para tratarlo como hueco
                    if (Math.Abs(x.A - y.A) <= tol) hs = x.HoleA || y.HoleA;
                    if (Math.Abs(x.B - y.B) <= tol) he = x.HoleB || y.HoleB;
                    result.Add(new Span(s, e, hs, he));
                }
            return result.OrderBy(sp => sp.A).ToList();
        }

        /// <summary>
        /// Posiciones equiespaciadas entre "from" y "to" (ambas incluidas) con separacion no
        /// mayor que "maxSpacing": n = ceil(L / s) huecos iguales. Una sola en el centro si no
        /// cabe mas de una.
        /// </summary>
        public static List<double> Positions(double from, double to, double maxSpacing, double tol)
        {
            var list = new List<double>();
            double len = to - from;
            if (len <= tol) { list.Add(0.5 * (from + to)); return list; }
            if (maxSpacing <= tol) maxSpacing = len;
            int n = Math.Max(1, (int)Math.Ceiling(len / maxSpacing - 1e-9));
            double step = len / n;
            for (int k = 0; k <= n; k++) list.Add(from + k * step);
            return list;
        }

        /// <summary>
        /// Ejes de las viguetas entre vMin y vMax: separacion fija "spacing", vigueta de ancho
        /// "width" entera dentro de la losa. Con "firstOffset" mayor que 0 la primera va a esa
        /// distancia del borde; si no, el reparto se centra.
        /// </summary>
        public static List<double> JoistAxes(double vMin, double vMax, double width, double spacing, double firstOffset, double tol)
        {
            var list = new List<double>();
            double lo = vMin + 0.5 * width - tol, hi = vMax - 0.5 * width + tol;
            if (hi < lo || spacing <= tol) return list;
            double first;
            if (firstOffset > tol)
                first = vMin + firstOffset;
            else
            {
                int n = (int)Math.Floor((hi - lo) / spacing + 1e-9) + 1;
                double span = (n - 1) * spacing;
                first = 0.5 * (vMin + vMax) - 0.5 * span;
            }
            for (double v = first; v <= hi + 1e-9; v += spacing)
                if (v >= lo - 1e-9) list.Add(v);
            return list;
        }

        /// <summary>Une tramos [a, b] que se solapan o se tocan (hueco menor o igual que "gap").</summary>
        public static List<(double a, double b)> Merge(IEnumerable<(double a, double b)> parts, double gap)
        {
            var list = parts.Where(p => p.b > p.a).OrderBy(p => p.a).ToList();
            var result = new List<(double a, double b)>();
            foreach ((double a, double b) p in list)
            {
                if (result.Count > 0 && p.a <= result[result.Count - 1].b + gap)
                {
                    (double a, double b) last = result[result.Count - 1];
                    result[result.Count - 1] = (last.a, Math.Max(last.b, p.b));
                }
                else result.Add(p);
            }
            return result;
        }
    }
}
