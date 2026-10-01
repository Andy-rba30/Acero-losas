using System;
using System.Collections.Generic;
using System.Linq;

namespace SlabRebar.Tests
{
    /// <summary>Pruebas de consola de las clases puras. Imprime OK/FALLO por comprobacion y termina con codigo 1 si algo falla.</summary>
    internal static class Program
    {
        private static int _fail, _ok;
        private const double Ft = 304.8;
        private static double Mm(double mm) => mm / Ft;
        private static double ToMm(double ft) => Math.Round(ft * Ft, 1);

        private static void Check(bool cond, string what)
        {
            if (cond) { _ok++; Console.WriteLine("  OK    " + what); }
            else { _fail++; Console.WriteLine("  FALLO " + what); }
        }

        private static void Near(double a, double bMm, string what, double tolMm = 0.5) =>
            Check(Math.Abs(ToMm(a) - bMm) <= tolMm, what + " = " + ToMm(a) + " mm (esperado " + bMm + ")");

        private static Outline2D Rect(double wMm, double dMm, params List<Pt>[] holes) =>
            new Outline2D(new List<Pt> { new Pt(0, 0), new Pt(Mm(wMm), 0), new Pt(Mm(wMm), Mm(dMm)), new Pt(0, Mm(dMm)) }, holes, Mm(2));

        private static List<Pt> Box(double u1, double v1, double u2, double v2) =>
            new List<Pt> { new Pt(Mm(u1), Mm(v1)), new Pt(Mm(u2), Mm(v1)), new Pt(Mm(u2), Mm(v2)), new Pt(Mm(u1), Mm(v2)) };

        private static PlanDiameters Diam() => new PlanDiameters
        {
            JoistBottom = Mm(12.7), JoistTop = Mm(12.7), Temperature = Mm(6.4),
            BottomMain = Mm(9.5), BottomSecondary = Mm(9.5), TopMain = Mm(9.5), TopSecondary = Mm(9.5)
        };

        private static int Main()
        {
            Console.WriteLine("== Geometry2D ==");
            Geometry();
            Console.WriteLine("== Aligerada rectangular 4 x 8 m ==");
            AligeradaRect();
            Console.WriteLine("== Aligerada con viga interior ==");
            AligeradaBeam();
            Console.WriteLine("== Aligerada con hueco ==");
            AligeradaHole();
            Console.WriteLine("== Varios panos en un mismo suelo ==");
            Panels();
            Console.WriteLine("== Losa en L ==");
            LShape();
            Console.WriteLine("== Maciza 4 x 8 m ==");
            Maciza();
            Console.WriteLine("== Config y particion ==");
            ConfigAndPartition();
            Console.WriteLine();
            Console.WriteLine(_ok + " comprobaciones correctas, " + _fail + " fallos");
            return _fail == 0 ? 0 : 1;
        }

        private static void Geometry()
        {
            var sq = Box(0, 0, 1000, 1000);
            Check(Geometry2D.SignedArea(sq) > 0, "cuadrado antihorario tiene area positiva");
            Near(Geometry2D.Centroid(sq).U, 500, "centroide u");

            List<double> pos = Geometry2D.Positions(0, Mm(1000), Mm(300), Mm(2));
            Check(pos.Count == 5, "Positions 0..1000 @300 -> 5 posiciones (" + pos.Count + ")");
            Near(pos[1], 250, "paso real 250");

            List<double> axes = Geometry2D.JoistAxes(0, Mm(8000), Mm(100), Mm(400), 0, Mm(2));
            Check(axes.Count == 20, "20 viguetas en 8 m (" + axes.Count + ")");
            Near(axes[0], 200, "primera vigueta centrada a 200");
            Near(axes[19], 7800, "ultima vigueta a 7800");
            List<double> axes2 = Geometry2D.JoistAxes(0, Mm(8000), Mm(100), Mm(400), Mm(300), Mm(2));
            Near(axes2[0], 300, "primera vigueta con desfase 300");
            Check(axes2.Count == 20, "20 viguetas con desfase 300 (" + axes2.Count + ")");

            Outline2D r = Rect(4000, 8000);
            List<Span> sp = r.Cut(true, Mm(1000), Mm(30), Mm(2));
            Check(sp.Count == 1 && !sp[0].HoleA && !sp[0].HoleB, "corte de rectangulo: un tramo exterior");
            Near(sp[0].A, 0, "tramo A"); Near(sp[0].B, 4000, "tramo B");
            // franja: a 20 mm del borde con semiancho 30 no hay tramo
            Check(r.Cut(true, Mm(20), Mm(30), Mm(2)).Count == 0, "franja fuera del borde -> sin tramo");

            Outline2D h = Rect(4000, 8000, Box(1000, 3000, 2000, 4000));
            sp = h.Cut(true, Mm(3500), Mm(30), Mm(2));
            Check(sp.Count == 2, "corte por el hueco: dos tramos (" + sp.Count + ")");
            if (sp.Count == 2)
            {
                Near(sp[0].B, 1000, "fin del primer tramo en el hueco"); Check(sp[0].HoleB && !sp[0].HoleA, "marcas de hueco tramo 1");
                Near(sp[1].A, 2000, "inicio del segundo tramo"); Check(sp[1].HoleA && !sp[1].HoleB, "marcas de hueco tramo 2");
            }
            // recta justo en el borde del hueco: la franja la parte
            sp = h.Cut(true, Mm(3000), Mm(30), Mm(2));
            Check(sp.Count == 2, "recta en el borde del hueco con franja: dos tramos (" + sp.Count + ")");
            sp = h.Cut(true, Mm(2950), Mm(30), Mm(2));
            Check(sp.Count == 1, "recta a 50 del hueco con franja 30: un tramo (" + sp.Count + ")");
            // corte en la otra direccion
            sp = h.Cut(false, Mm(1500), Mm(30), Mm(2));
            Check(sp.Count == 2, "corte u=1500 por el hueco: dos tramos (" + sp.Count + ")");
            if (sp.Count == 2) { Near(sp[0].B, 3000, "tramo v hasta 3000"); Near(sp[1].A, 4000, "tramo v desde 4000"); }
            Check(h.Contains(new Pt(Mm(1500), Mm(3500))) == false, "punto en el hueco no esta dentro");
            Check(h.Contains(new Pt(Mm(500), Mm(500))), "punto en la losa esta dentro");

            var merged = Geometry2D.Merge(new[] { (0.0, 1.0), (0.5, 2.0), (3.0, 4.0) }, 0);
            Check(merged.Count == 2 && Math.Abs(merged[0].b - 2.0) < 1e-9, "Merge une solapes");

            Pt dir = Geometry2D.LongestEdgeDirection(Box(0, 0, 3000, 1000));
            Check(Math.Abs(dir.U - 1) < 1e-9, "borde mas largo en u");
        }

        private static AppConfig Cfg()
        {
            var c = new AppConfig();
            c.Normalize();
            c.Aligerada.Bottom.BarTypeName = "1/2"; c.Aligerada.Top.BarTypeName = "1/2"; c.Aligerada.Temperature.BarTypeName = "1/4";
            c.Maciza.BottomMain.BarTypeName = "3/8"; c.Maciza.BottomSecondary.BarTypeName = "3/8";
            c.Maciza.TopMain.BarTypeName = "3/8"; c.Maciza.TopSecondary.BarTypeName = "3/8";
            return c;
        }

        private static void AligeradaRect()
        {
            AppConfig c = Cfg();
            SlabPlan p = SlabPlan.Build(Rect(4000, 8000), new List<Support>(), Mm(200), SlabKind.Aligerada, c, Diam());
            Console.WriteLine("  " + p.Describe() + " | " + p.DescribeLayers());
            Check(p.Error == null, "sin error: " + p.Error);
            Check(p.Joists.Count == 20, "20 viguetas (" + p.Joists.Count + ")");
            Check(p.CountOf(BarLayer.JoistBottom) == 20, "20 barras inferiores (" + p.CountOf(BarLayer.JoistBottom) + ")");
            Check(p.GroupsOf(BarLayer.JoistBottom) == 1, "un solo conjunto inferior (" + p.GroupsOf(BarLayer.JoistBottom) + ")");
            PlannedBar b = p.Bars.First(x => x.Layer == BarLayer.JoistBottom);
            Near(b.Start, 25, "barra inferior empieza al recubrimiento"); Near(b.End, 3975, "y termina al recubrimiento");
            Near(b.Z, 25 + 6.35, "cota inferior");
            Check(p.CountOf(BarLayer.JoistTop) == 40, "40 bastones (2 por vigueta) (" + p.CountOf(BarLayer.JoistTop) + ")");
            Check(p.GroupsOf(BarLayer.JoistTop) == 2, "bastones en 2 conjuntos (" + p.GroupsOf(BarLayer.JoistTop) + ")");
            PlannedBar t = p.Bars.Where(x => x.Layer == BarLayer.JoistTop).OrderBy(x => x.Start).First();
            Near(t.Start, 25, "baston extremo desde el recubrimiento"); Near(t.End, 800, "baston L/5 = 800");
            Near(t.Z, 200 - 25 - 6.4 - 6.35, "cota del baston: justo debajo de la temperatura");
            Check(p.CountOf(BarLayer.Temperature) == 17, "17 barras de temperatura (" + p.CountOf(BarLayer.Temperature) + ")");
            PlannedBar te = p.Bars.First(x => x.Layer == BarLayer.Temperature);
            Check(!te.AlongU, "temperatura perpendicular a las viguetas");
            Near(te.Start, 25, "temperatura empieza al recubrimiento"); Near(te.End, 7975, "temperatura termina al recubrimiento");
            Near(te.Z, 200 - 25 - 3.2, "temperatura al recubrimiento, en la losa superior, encima de los bastones");
            Check(!p.Warnings.Any(w => w.Contains("losa superior")), "la temperatura queda dentro de la losa superior");
            Check(p.Groups.Count == 4, "4 conjuntos en total (" + p.Groups.Count + ")");
            BarGroup g = p.Groups.First(x => x.Layer == BarLayer.JoistBottom);
            Near(g.Spacing, 400, "paso del conjunto inferior");

            // con prolongacion y 2 barras
            c.Aligerada.Bottom.ExtensionMm = 150; c.Aligerada.Bottom.Count = 2; c.Aligerada.Bottom.HookTypeName = "90";
            p = SlabPlan.Build(Rect(4000, 8000), new List<Support>(), Mm(200), SlabKind.Aligerada, c, Diam());
            Check(p.Error == null, "sin error con prolongacion: " + p.Error);
            Check(p.CountOf(BarLayer.JoistBottom) == 40, "2 barras por vigueta -> 40 (" + p.CountOf(BarLayer.JoistBottom) + ")");
            b = p.Bars.First(x => x.Layer == BarLayer.JoistBottom);
            Near(b.Start, -150, "prolongacion inferior"); Near(b.InA, 0, "tramo comprobable desde el borde");
            Check(b.HookStart && b.HookEnd, "ganchos en los dos extremos exteriores");
            Check(b.ExtendsStart && b.ExtendsEnd, "la barra sobresale");
            Check(p.GroupsOf(BarLayer.JoistBottom) == 2, "las 40 barras (2 por vigueta) se entrelazan en 2 conjuntos (" + p.GroupsOf(BarLayer.JoistBottom) + ")");
            foreach (string w in p.Warnings) Console.WriteLine("  aviso: " + w);

            // corrida
            c.Aligerada.Top.Mode = "corrida";
            p = SlabPlan.Build(Rect(4000, 8000), new List<Support>(), Mm(200), SlabKind.Aligerada, c, Diam());
            Check(p.CountOf(BarLayer.JoistTop) == 20, "barra superior corrida: 20 (" + p.CountOf(BarLayer.JoistTop) + ")");
            c.Aligerada.Top.Mode = "ninguna";
            p = SlabPlan.Build(Rect(4000, 8000), new List<Support>(), Mm(200), SlabKind.Aligerada, c, Diam());
            Check(p.CountOf(BarLayer.JoistTop) == 0, "sin barras superiores");
            Near(p.Bars.First(x => x.Layer == BarLayer.Temperature).Z, 200 - 25 - 3.2, "temperatura al recubrimiento sin bastones");

            // baston grueso: la temperatura sigue al recubrimiento y el baston baja por debajo de ella
            c.Aligerada.Top.Mode = "bastones";
            PlanDiameters thick = Diam(); thick.JoistTop = Mm(35.8);
            p = SlabPlan.Build(Rect(4000, 8000), new List<Support>(), Mm(200), SlabKind.Aligerada, c, thick);
            Near(p.Bars.First(x => x.Layer == BarLayer.Temperature).Z, 200 - 25 - 3.2, "temperatura al recubrimiento con baston de 35.8");
            Near(p.Bars.First(x => x.Layer == BarLayer.JoistTop).Z, 200 - 25 - 6.4 - 17.9, "baston de 35.8 colgado bajo la temperatura");
            Check(!p.Warnings.Any(w => w.Contains("losa superior")), "sin aviso de losa superior con baston grueso");

            // profundidad dada por debajo de los bastones: estos vuelven al recubrimiento
            c.Aligerada.Temperature.DepthMm = 45;
            p = SlabPlan.Build(Rect(4000, 8000), new List<Support>(), Mm(200), SlabKind.Aligerada, c, Diam());
            Near(p.Bars.First(x => x.Layer == BarLayer.Temperature).Z, 200 - 45, "temperatura a la profundidad dada");
            Near(p.Bars.First(x => x.Layer == BarLayer.JoistTop).Z, 200 - 25 - 6.35, "baston al recubrimiento con la temperatura por debajo");
            // profundidad dada en la franja del baston: el baston baja
            c.Aligerada.Temperature.DepthMm = 20;
            p = SlabPlan.Build(Rect(4000, 8000), new List<Support>(), Mm(200), SlabKind.Aligerada, c, Diam());
            Near(p.Bars.First(x => x.Layer == BarLayer.Temperature).Z, 200 - 20, "temperatura a 20 del borde superior (como en el detalle)");
            Check(p.Warnings.Any(w => w.Contains("recubrimiento superior")), "aviso: a 20 no respeta el recubrimiento de 25");
            Near(p.Bars.First(x => x.Layer == BarLayer.JoistTop).Z, 200 - 25 - 6.35, "baston al recubrimiento: la temperatura a 20 queda por encima de el");

            // temperatura demasiado profunda avisa
            c.Aligerada.Temperature.DepthMm = 80;
            p = SlabPlan.Build(Rect(4000, 8000), new List<Support>(), Mm(200), SlabKind.Aligerada, c, Diam());
            Check(p.Warnings.Any(w => w.Contains("losa superior")), "aviso de temperatura bajo la losa superior");

            // espesor insuficiente
            p = SlabPlan.Build(Rect(4000, 8000), new List<Support>(), Mm(40), SlabKind.Aligerada, c, Diam());
            Check(p.Error != null, "espesor 40 mm rechazado: " + p.Error);
        }

        private static void AligeradaBeam()
        {
            AppConfig c = Cfg();
            var beam = new Support { U1 = Mm(1850), U2 = Mm(2150), V1 = Mm(-500), V2 = Mm(8500), Name = "V-1" };
            SlabPlan p = SlabPlan.Build(Rect(4000, 8000), new List<Support> { beam }, Mm(200), SlabKind.Aligerada, c, Diam());
            Console.WriteLine("  " + p.Describe() + " | " + p.DescribeLayers());
            Check(p.Error == null, "sin error: " + p.Error);
            Check(p.CountOf(BarLayer.JoistTop) == 60, "3 bastones por vigueta -> 60 (" + p.CountOf(BarLayer.JoistTop) + ")");
            List<PlannedBar> tops = p.Bars.Where(x => x.Layer == BarLayer.JoistTop && Math.Abs(x.Coord - p.Joists[0].Axis) < 1e-9).OrderBy(x => x.Start).ToList();
            Check(tops.Count == 3, "vigueta 1 con 3 bastones");
            if (tops.Count == 3)
            {
                Near(tops[0].End, 370, "baston extremo L/5 de la luz libre 1850");
                Near(tops[1].Start, 1850 - 462.5, "baston interior L/4 a la izquierda");
                Near(tops[1].End, 2150 + 462.5, "baston interior L/4 a la derecha");
                Near(tops[2].Start, 4000 - 370, "baston extremo derecho");
                Check(!tops[1].HookStart && !tops[1].HookEnd, "baston interior sin ganchos");
            }
            Check(p.GroupsOf(BarLayer.JoistTop) == 3, "bastones en 3 conjuntos (" + p.GroupsOf(BarLayer.JoistTop) + ")");

            // viga bajo el borde (apoyo extremo): la cara es U2
            var edge = new Support { U1 = Mm(-100), U2 = Mm(200), V1 = Mm(-500), V2 = Mm(8500), Name = "V-borde" };
            p = SlabPlan.Build(Rect(4000, 8000), new List<Support> { edge }, Mm(200), SlabKind.Aligerada, c, Diam());
            PlannedBar t0 = p.Bars.Where(x => x.Layer == BarLayer.JoistTop).OrderBy(x => x.Start).First();
            Near(t0.End, 200 + 0.2 * 3800, "baston extremo desde la cara interior de la viga de borde");
            // viga fuera del borde (losa entre vigas): la cara es el borde
            var outside = new Support { U1 = Mm(-300), U2 = Mm(0), V1 = Mm(-500), V2 = Mm(8500), Name = "V-ext" };
            p = SlabPlan.Build(Rect(4000, 8000), new List<Support> { outside }, Mm(200), SlabKind.Aligerada, c, Diam());
            t0 = p.Bars.Where(x => x.Layer == BarLayer.JoistTop).OrderBy(x => x.Start).First();
            Near(t0.End, 800, "viga exterior: baston L/5 de 4000");

            // longitud fija
            c.Aligerada.Top.FixedLengthMm = 1000;
            p = SlabPlan.Build(Rect(4000, 8000), new List<Support> { beam }, Mm(200), SlabKind.Aligerada, c, Diam());
            tops = p.Bars.Where(x => x.Layer == BarLayer.JoistTop && Math.Abs(x.Coord - p.Joists[0].Axis) < 1e-9).OrderBy(x => x.Start).ToList();
            Check(tops.Count == 1, "con 1000 fijos los tres bastones se solapan y se unen en 1 (" + tops.Count + ")");
            if (tops.Count == 1) { Near(tops[0].Start, 25, "union desde el recubrimiento"); Near(tops[0].End, 3975, "hasta el otro recubrimiento"); }

            // viga oblicua / que no cruza esa vigueta no cuenta
            var shortBeam = new Support { U1 = Mm(1850), U2 = Mm(2150), V1 = Mm(4000), V2 = Mm(8500), Name = "V-corta" };
            c.Aligerada.Top.FixedLengthMm = 0;
            p = SlabPlan.Build(Rect(4000, 8000), new List<Support> { shortBeam }, Mm(200), SlabKind.Aligerada, c, Diam());
            int first = p.Bars.Count(x => x.Layer == BarLayer.JoistTop && Math.Abs(x.Coord - p.Joists[0].Axis) < 1e-9);
            int last = p.Bars.Count(x => x.Layer == BarLayer.JoistTop && Math.Abs(x.Coord - p.Joists[19].Axis) < 1e-9);
            Check(first == 2 && last == 3, "viga corta: 2 bastones en la vigueta 1 y 3 en la 20 (" + first + ", " + last + ")");
        }

        private static void AligeradaHole()
        {
            AppConfig c = Cfg();
            Outline2D h = Rect(4000, 8000, Box(1000, 3000, 2000, 4000));
            SlabPlan p = SlabPlan.Build(h, new List<Support>(), Mm(200), SlabKind.Aligerada, c, Diam());
            Console.WriteLine("  " + p.Describe() + " | " + p.DescribeLayers());
            Check(p.Error == null, "sin error: " + p.Error);
            // viguetas en 3000 (borde), 3400, 3800 cortadas por el hueco -> 2 tramos cada una
            int cut = p.Bars.Count(x => x.Layer == BarLayer.JoistBottom && x.Coord > Mm(2900) && x.Coord < Mm(4100));
            Check(cut == 6, "3 viguetas partidas por el hueco -> 6 barras (" + cut + ")");
            PlannedBar left = p.Bars.Where(x => x.Layer == BarLayer.JoistBottom && Math.Abs(x.Coord - Mm(3400)) < 1e-9).OrderBy(x => x.Start).First();
            Near(left.End, 975, "la barra para al recubrimiento del hueco");
            Check(!left.HookEnd, "sin gancho en el hueco");
            // bastones: en el extremo del hueco no hay baston
            var tops = p.Bars.Where(x => x.Layer == BarLayer.JoistTop && Math.Abs(x.Coord - Mm(3400)) < 1e-9).OrderBy(x => x.Start).ToList();
            // el baston del tramo corto (L/5 de 1000 = 200 - 25 = 175 mm) no llega a la longitud minima (300) y se omite
            Check(tops.Count == 1, "en una vigueta partida: baston solo en el extremo exterior del tramo largo (" + tops.Count + ")");
            if (tops.Count == 1) Near(tops[0].Start, 4000 - 400, "baston L/5 de la luz 2000");
            Check(p.Skipped == 3, "3 bastones demasiado cortos omitidos (" + p.Skipped + ")");
            Check(p.CountOf(BarLayer.JoistBottom) == 20 + 3, "20 viguetas + 3 partidas = 23 barras (" + p.CountOf(BarLayer.JoistBottom) + ")");
            Check(p.GroupsOf(BarLayer.JoistBottom) >= 3, "varios conjuntos inferiores (" + p.GroupsOf(BarLayer.JoistBottom) + ")");
            int temp = p.CountOf(BarLayer.Temperature);
            Check(temp > 17, "temperatura partida por el hueco (" + temp + " barras)");
        }

        private static void Panels()
        {
            AppConfig c = Cfg();
            // dos panos de 2.5 x 4 m separados por una viga de 300 (en v), mas un hueco en el segundo
            var rings = new List<List<Pt>> { Box(0, 0, 2500, 4000), Box(0, 4300, 2500, 8300), Box(1000, 5000, 1500, 5500) };
            var o = new Outline2D(rings, Mm(2));
            Check(o.Outers.Count == 2 && o.Holes.Count == 1, "2 panos y 1 hueco (" + o.Outers.Count + ", " + o.Holes.Count + ")");
            Near(o.Depth, 8300, "fondo total");
            List<Span> sp = o.Cut(false, Mm(500), Mm(30), Mm(2));
            Check(sp.Count == 2 && !sp[0].HoleB && !sp[1].HoleA, "una recta a lo largo de v cruza los dos panos con extremos exteriores (" + sp.Count + ")");
            if (sp.Count == 2) { Near(sp[0].B, 4000, "fin del pano 1"); Near(sp[1].A, 4300, "inicio del pano 2"); }
            SlabPlan p = SlabPlan.Build(o, new List<Support>(), Mm(200), SlabKind.Aligerada, c, Diam());
            Console.WriteLine("  " + p.Describe() + " | " + p.DescribeLayers());
            Check(p.Error == null, "sin error: " + p.Error);
            // viguetas (a lo largo de u = 2.5 m) en los dos panos; la zona de la viga (4000..4300) no lleva vigueta
            Check(p.Joists.Count >= 19 && p.Joists.Count <= 21, "viguetas repartidas en 8.3 m (" + p.Joists.Count + ")");
            int inGap = p.Bars.Count(b => b.Layer == BarLayer.JoistBottom && b.Coord > Mm(4000) && b.Coord < Mm(4300));
            Check(inGap == 0, "ninguna barra inferior en la franja de la viga (" + inGap + ")");
            PlannedBar te = p.Bars.Where(b => b.Layer == BarLayer.Temperature).OrderBy(b => b.Start).First();
            Near(te.End, 3975, "temperatura del pano 1 para al recubrimiento del borde del pano");
            Check(p.Bars.Where(b => b.Layer == BarLayer.Temperature).Any(b => Math.Abs(b.Start - Mm(4325)) < 1e-6), "temperatura del pano 2 empieza al recubrimiento");
            Check(p.Bars.All(b => o.Contains(new Pt(b.AlongU ? 0.5 * (b.InA + b.InB) : b.Coord, b.AlongU ? b.Coord : 0.5 * (b.InA + b.InB)))), "todas las barras dentro de algun pano");
        }

        private static void LShape()
        {
            AppConfig c = Cfg();
            var outer = new List<Pt>
            {
                new Pt(0, 0), new Pt(Mm(8000), 0), new Pt(Mm(8000), Mm(4000)), new Pt(Mm(4000), Mm(4000)), new Pt(Mm(4000), Mm(8000)), new Pt(0, Mm(8000))
            };
            var o = new Outline2D(outer, null, Mm(2));
            Near(o.Width, 8000, "ancho de la L"); Near(o.Depth, 8000, "fondo de la L");
            Check(Math.Abs(o.Area - (Mm(8000) * Mm(8000) - Mm(4000) * Mm(4000))) < 1e-6, "area neta de la L");
            SlabPlan p = SlabPlan.Build(o, new List<Support>(), Mm(200), SlabKind.Maciza, c, Diam());
            Console.WriteLine("  " + p.Describe() + " | " + p.DescribeLayers());
            Check(p.Error == null, "sin error: " + p.Error);
            // barras inferiores: las de v < 4000 miden 8 m, las de v > 4000 miden 4 m
            PlannedBar lowV = p.Bars.Where(x => x.Layer == BarLayer.BottomMain && x.Coord < Mm(3900)).First();
            PlannedBar highV = p.Bars.Where(x => x.Layer == BarLayer.BottomMain && x.Coord > Mm(4100)).First();
            Near(lowV.End, 7975, "barra larga en el ala"); Near(highV.End, 3975, "barra corta en el alma");
            Check(p.GroupsOf(BarLayer.BottomMain) == 2, "dos conjuntos de inferiores principales (" + p.GroupsOf(BarLayer.BottomMain) + ")");
            // la barra en v = 4000 +- franja debe quedar recortada a 4000
            PlannedBar near = p.Bars.Where(x => x.Layer == BarLayer.BottomMain && Math.Abs(x.Coord - Mm(4000)) < Mm(30)).OrderBy(x => Math.Abs(x.Coord - Mm(4000))).FirstOrDefault();
            if (near != null) Check(near.End <= Mm(3975) + 1e-9 || near.Coord < Mm(4000) - Mm(29), "barra pegada a la esquina interior respeta el recubrimiento (fin " + ToMm(near.End) + ", v " + ToMm(near.Coord) + ")");
            Check(p.Bars.All(b => o.Contains(new Pt(b.AlongU ? 0.5 * (b.InA + b.InB) : b.Coord, b.AlongU ? b.Coord : 0.5 * (b.InA + b.InB)))), "todas las barras dentro de la L");
        }

        private static void Maciza()
        {
            AppConfig c = Cfg();
            SlabPlan p = SlabPlan.Build(Rect(4000, 8000), new List<Support>(), Mm(150), SlabKind.Maciza, c, Diam());
            Console.WriteLine("  " + p.Describe() + " | " + p.DescribeLayers());
            Check(p.Error == null, "sin error: " + p.Error);
            Check(p.CountOf(BarLayer.BottomMain) == 41, "inferior principal @200 en 8 m -> 41 (" + p.CountOf(BarLayer.BottomMain) + ")");
            Check(p.CountOf(BarLayer.BottomSecondary) == 17, "inferior secundaria @250 en 4 m -> 17 (" + p.CountOf(BarLayer.BottomSecondary) + ")");
            Check(p.CountOf(BarLayer.TopMain) == 0 && p.CountOf(BarLayer.TopSecondary) == 0, "sin malla superior por defecto");
            Near(p.Bars.First(x => x.Layer == BarLayer.BottomMain).Z, 25 + 4.75, "cota inferior principal");
            Near(p.Bars.First(x => x.Layer == BarLayer.BottomSecondary).Z, 25 + 9.5 + 4.75, "cota inferior secundaria encima");
            Check(p.Groups.Count == 2, "2 conjuntos (" + p.Groups.Count + ")");

            c.Maciza.TopMain.Mode = "bastones"; c.Maciza.TopSecondary.Enabled = true;
            var beam = new Support { U1 = Mm(1850), U2 = Mm(2150), V1 = Mm(-500), V2 = Mm(8500) };
            p = SlabPlan.Build(Rect(4000, 8000), new List<Support> { beam }, Mm(150), SlabKind.Maciza, c, Diam());
            Console.WriteLine("  " + p.Describe() + " | " + p.DescribeLayers());
            Check(p.CountOf(BarLayer.TopMain) == 41 * 3, "bastones superiores 3 por linea (" + p.CountOf(BarLayer.TopMain) + ")");
            Check(p.GroupsOf(BarLayer.TopMain) == 3, "bastones superiores en 3 conjuntos (" + p.GroupsOf(BarLayer.TopMain) + ")");
            Check(p.CountOf(BarLayer.TopSecondary) == 17, "superior secundaria 17 (" + p.CountOf(BarLayer.TopSecondary) + ")");
            Near(p.Bars.First(x => x.Layer == BarLayer.TopMain).Z, 150 - 25 - 4.75, "cota superior principal");
            Near(p.Bars.First(x => x.Layer == BarLayer.TopSecondary).Z, 150 - 25 - 9.5 - 4.75, "cota superior secundaria debajo");
            c.Maciza.TopMain.Mode = "corrida";
            p = SlabPlan.Build(Rect(4000, 8000), new List<Support> { beam }, Mm(150), SlabKind.Maciza, c, Diam());
            Check(p.CountOf(BarLayer.TopMain) == 41, "superior corrida 41 (" + p.CountOf(BarLayer.TopMain) + ")");
        }

        private static void ConfigAndPartition()
        {
            var c = new AppConfig();
            c.Normalize();
            Check(c.Kind == "auto" && c.Direction.Mode == "short", "valores por defecto");
            c.Kind = "ALIGERADA"; c.Direction.Mode = "largo"; c.Aligerada.Top.Mode = "xx"; c.Aligerada.Bottom.Count = 5;
            c.Normalize();
            Check(c.Kind == "aligerada" && c.KindAligerada, "kind normalizado");
            Check(c.Direction.Mode == "long", "direccion normalizada");
            Check(c.Aligerada.Top.Mode == "bastones" && c.Aligerada.Bottom.Count == 2, "modo y cuenta normalizados");
            string tmp = System.IO.Path.GetTempFileName();
            c.Save(tmp);
            string json = System.IO.File.ReadAllText(tmp);
            Check(json.Contains("\"aligerada\"") && json.Contains("\"joistSpacingMm\""), "json en camelCase");
            AppConfig back = System.Text.Json.JsonSerializer.Deserialize<AppConfig>(json, new System.Text.Json.JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            Check(back != null && back.Aligerada.Bottom.Count == 2 && back.Kind == "aligerada", "ida y vuelta por json");
            AppConfig clone = c.Clone();
            clone.CoverTopMm = 99;
            Check(c.CoverTopMm != 99, "Clone es independiente");
            System.IO.File.Delete(tmp);

            string s = PartitionName.Expand("LOSA-{marca}-{capa}", new PartitionName.Source { Mark = "L-2", Layer = "baston" });
            Check(s == "LOSA-L-2-baston", "particion con marca y capa: " + s);
            s = PartitionName.Expand("LOSA-{marca}", new PartitionName.Source { Mark = "", Id = "1234" });
            Check(s == "LOSA-1234", "particion sin marca usa el id: " + s);
            s = PartitionName.Expand("LOSA-{conjunto}", new PartitionName.Source());
            Check(s == "LOSA", "comodin vacio sin separador huerfano: " + s);
        }
    }
}
