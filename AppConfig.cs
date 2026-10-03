using System;
using System.IO;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace SlabRebar
{
    /// <summary>Direccion de las viguetas (aligeradas) o de las barras principales (macizas).</summary>
    public class DirectionCfg
    {
        /// <summary>
        /// "short" = paralelas al lado corto de la losa (la luz menor; lo normal en viguetas),
        /// "long" = paralelas al lado largo, "x" / "y" = ejes del proyecto, "angle" = el angulo
        /// AngleDeg medido desde el eje X del proyecto. Cambiable losa a losa en la ventana.
        /// </summary>
        public string Mode { get; set; } = "short";
        public double AngleDeg { get; set; } = 0;
    }

    /// <summary>Una capa de barras rectas y paralelas (malla inferior o superior, temperatura).</summary>
    public class LayerCfg
    {
        /// <summary>Se coloca o no (en las capas obligatorias se ignora).</summary>
        public bool Enabled { get; set; } = true;
        /// <summary>Tipo de barra (RebarBarType): exacto, o un fragmento que lo identifique.</summary>
        public string BarTypeName { get; set; } = "";
        /// <summary>Separacion maxima entre barras (mm); se reparten por igual sin superarla.</summary>
        public double SpacingMm { get; set; } = 200;
        /// <summary>
        /// Prolongacion mas alla del borde exterior de la losa (mm): anclaje en la viga. 0 = las
        /// barras paran al recubrimiento lateral dentro de la losa. En los bordes de un hueco
        /// siempre paran al recubrimiento.
        /// </summary>
        public double ExtensionMm { get; set; } = 0;
        /// <summary>Nombre (o fragmento) del RebarHookType en los extremos que llegan al borde exterior. Vacio = sin gancho.</summary>
        public string HookTypeName { get; set; } = "";
    }

    /// <summary>Barras superiores: bastones en los apoyos, barra corrida o ninguna.</summary>
    public class TopCfg : LayerCfg
    {
        /// <summary>"bastones" (en cada apoyo), "corrida" (de extremo a extremo) o "ninguna".</summary>
        public string Mode { get; set; } = "bastones";
        /// <summary>Longitud del baston a cada lado de un apoyo interior, como fraccion de la luz libre adyacente (0.25 = L/4).</summary>
        public double InteriorFraction { get; set; } = 0.25;
        /// <summary>Longitud del baston desde la cara del apoyo extremo hacia el interior, como fraccion de la luz libre (0.2 = L/5).</summary>
        public double EndFraction { get; set; } = 0.2;
        /// <summary>Longitud fija del baston desde la cara del apoyo (mm). Mayor que 0 anula las fracciones.</summary>
        public double FixedLengthMm { get; set; } = 0;

        [JsonIgnore]
        public bool None => string.Equals((Mode ?? "").Trim(), "ninguna", StringComparison.OrdinalIgnoreCase) || !Enabled;
        [JsonIgnore]
        public bool Continuous => string.Equals((Mode ?? "").Trim(), "corrida", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Barra inferior de cada vigueta.</summary>
    public class JoistBottomCfg : LayerCfg
    {
        /// <summary>Barras inferiores por vigueta (1 o 2, lado a lado).</summary>
        public int Count { get; set; } = 1;
    }

    /// <summary>Acero de temperatura en la losa superior de la aligerada (perpendicular a las viguetas).</summary>
    public class TemperatureCfg : LayerCfg
    {
        /// <summary>
        /// Profundidad del eje de la barra desde la cara superior (mm). 0 = al recubrimiento
        /// superior (recubrimiento + medio diametro); los bastones van justo por debajo.
        /// </summary>
        public double DepthMm { get; set; } = 0;
    }

    /// <summary>Losa aligerada: viguetas con ladrillo de techo y losa superior.</summary>
    public class AligeradaCfg
    {
        /// <summary>Ancho de la vigueta (mm), normalmente 100.</summary>
        public double JoistWidthMm { get; set; } = 100;
        /// <summary>Separacion entre ejes de viguetas (mm), normalmente 400 (ladrillo de 300).</summary>
        public double JoistSpacingMm { get; set; } = 400;
        /// <summary>Espesor de la losa superior sobre el ladrillo (mm), normalmente 50.</summary>
        public double TopSlabMm { get; set; } = 50;
        /// <summary>Distancia del borde de la losa al eje de la primera vigueta (mm). 0 = reparto centrado.</summary>
        public double FirstJoistOffsetMm { get; set; } = 0;

        public JoistBottomCfg Bottom { get; set; } = new JoistBottomCfg { SpacingMm = 400 };
        public TopCfg Top { get; set; } = new TopCfg { Mode = "bastones", SpacingMm = 400 };
        public TemperatureCfg Temperature { get; set; } = new TemperatureCfg { SpacingMm = 250 };
    }

    /// <summary>Losa maciza: malla inferior (principal + secundaria) y malla superior opcional.</summary>
    public class MacizaCfg
    {
        /// <summary>Barras inferiores en la direccion principal (la de las barras, eje u).</summary>
        public LayerCfg BottomMain { get; set; } = new LayerCfg { SpacingMm = 200 };
        /// <summary>Barras inferiores perpendiculares (temperatura / reparticion en losas en una direccion).</summary>
        public LayerCfg BottomSecondary { get; set; } = new LayerCfg { SpacingMm = 250 };
        /// <summary>Barras superiores en la direccion principal: bastones, corridas o ninguna.</summary>
        public TopCfg TopMain { get; set; } = new TopCfg { Mode = "ninguna", SpacingMm = 200 };
        /// <summary>Barras superiores perpendiculares, corridas.</summary>
        public LayerCfg TopSecondary { get; set; } = new LayerCfg { Enabled = false, SpacingMm = 250 };
    }

    public class AppConfig
    {
        /// <summary>Recubrimiento desde la cara inferior de la losa a la cara de la barra (mm).</summary>
        public double CoverBottomMm { get; set; } = 25;
        /// <summary>Recubrimiento desde la cara superior de la losa (mm).</summary>
        public double CoverTopMm { get; set; } = 25;
        /// <summary>Recubrimiento en los bordes de la losa y de los huecos (mm).</summary>
        public double CoverEdgeMm { get; set; } = 25;

        /// <summary>
        /// Tipo de losa por defecto: "auto" (aligerada si el nombre del tipo contiene "aliger",
        /// si no maciza), "aligerada" o "maciza". Cambiable losa a losa en la ventana.
        /// </summary>
        public string Kind { get; set; } = "auto";

        public DirectionCfg Direction { get; set; } = new DirectionCfg();
        public AligeradaCfg Aligerada { get; set; } = new AligeradaCfg();
        public MacizaCfg Maciza { get; set; } = new MacizaCfg();

        /// <summary>Detectar las vigas (armazon estructural) que cruzan la losa como apoyos de los bastones.</summary>
        public bool DetectBeams { get; set; } = true;

        /// <summary>
        /// Plantilla por defecto del parametro Particion, segun el contrato ARBA-comun:
        /// "LOSAS - LOS-L2" (categoria del anfitrion, prefijo del add-in y marca; la capa no va en la
        /// particion sino en "ARBA - Codigo"). Una plantilla que no empiece por "{categoria} - {prefijo}-"
        /// incumple el contrato y la ventana lo avisa.
        /// </summary>
        public const string DefaultPartitionTemplate = "{categoria} - {prefijo}-{marca}";

        /// <summary>
        /// Plantilla del parametro Particion de cada barra. Comodines: {categoria} (LOSAS, segun el
        /// anfitrion), {prefijo} (LOS), {marca} (Marca del elemento; si esta vacia se usa el Id), {id},
        /// {codigo} o {capa} (inferior, baston, temperatura...), {tipo}, {familia} y {conjunto}
        /// (nombre del juego de barras).
        /// </summary>
        public string PartitionTemplate { get; set; } = DefaultPartitionTemplate;

        /// <summary>Tolerancia geometrica al agrupar coordenadas y comparar (mm).</summary>
        public double ToleranceMm { get; set; } = 2;

        /// <summary>Longitud minima de una barra para colocarla (mm); las mas cortas se omiten con aviso.</summary>
        public double MinBarLengthMm { get; set; } = 300;

        [JsonIgnore]
        public bool KindAuto => !KindAligerada && !KindMaciza;
        [JsonIgnore]
        public bool KindAligerada => string.Equals((Kind ?? "").Trim(), "aligerada", StringComparison.OrdinalIgnoreCase);
        [JsonIgnore]
        public bool KindMaciza => string.Equals((Kind ?? "").Trim(), "maciza", StringComparison.OrdinalIgnoreCase);

        /// <summary>Deja la configuracion en un estado coherente.</summary>
        public void Normalize()
        {
            if (Direction == null) Direction = new DirectionCfg();
            if (Aligerada == null) Aligerada = new AligeradaCfg();
            if (Maciza == null) Maciza = new MacizaCfg();
            if (Aligerada.Bottom == null) Aligerada.Bottom = new JoistBottomCfg();
            if (Aligerada.Top == null) Aligerada.Top = new TopCfg();
            if (Aligerada.Temperature == null) Aligerada.Temperature = new TemperatureCfg();
            if (Maciza.BottomMain == null) Maciza.BottomMain = new LayerCfg();
            if (Maciza.BottomSecondary == null) Maciza.BottomSecondary = new LayerCfg();
            if (Maciza.TopMain == null) Maciza.TopMain = new TopCfg { Mode = "ninguna" };
            if (Maciza.TopSecondary == null) Maciza.TopSecondary = new LayerCfg();

            Direction.Mode = NormalizeDirection(Direction.Mode);
            Kind = KindAligerada ? "aligerada" : KindMaciza ? "maciza" : "auto";

            foreach (LayerCfg l in new LayerCfg[] { Aligerada.Bottom, Aligerada.Top, Aligerada.Temperature, Maciza.BottomMain, Maciza.BottomSecondary, Maciza.TopMain, Maciza.TopSecondary })
            {
                if (l.BarTypeName == null) l.BarTypeName = "";
                if (l.HookTypeName == null) l.HookTypeName = "";
                if (l.SpacingMm <= 0) l.SpacingMm = 200;
                if (l.ExtensionMm < 0) l.ExtensionMm = 0;
            }
            foreach (TopCfg t in new[] { Aligerada.Top, Maciza.TopMain })
            {
                string m = (t.Mode ?? "").Trim().ToLowerInvariant();
                t.Mode = m == "corrida" || m == "ninguna" ? m : "bastones";
                if (t.InteriorFraction <= 0) t.InteriorFraction = 0.25;
                if (t.EndFraction <= 0) t.EndFraction = 0.2;
                if (t.FixedLengthMm < 0) t.FixedLengthMm = 0;
            }
            Aligerada.Bottom.Enabled = true;
            Aligerada.Bottom.Count = Aligerada.Bottom.Count >= 2 ? 2 : 1;
            Maciza.BottomMain.Enabled = true;
            if (Aligerada.Temperature.DepthMm < 0) Aligerada.Temperature.DepthMm = 0;
            if (Aligerada.JoistWidthMm <= 0) Aligerada.JoistWidthMm = 100;
            if (Aligerada.JoistSpacingMm <= Aligerada.JoistWidthMm) Aligerada.JoistSpacingMm = Math.Max(400, Aligerada.JoistWidthMm * 2);
            if (Aligerada.TopSlabMm <= 0) Aligerada.TopSlabMm = 50;
            if (Aligerada.FirstJoistOffsetMm < 0) Aligerada.FirstJoistOffsetMm = 0;

            if (CoverBottomMm < 0) CoverBottomMm = 0;
            if (CoverTopMm < 0) CoverTopMm = 0;
            if (CoverEdgeMm < 0) CoverEdgeMm = 0;
            if (ToleranceMm <= 0) ToleranceMm = 2;
            if (MinBarLengthMm < 0) MinBarLengthMm = 0;
            if (string.IsNullOrWhiteSpace(PartitionTemplate)) PartitionTemplate = DefaultPartitionTemplate;
        }

        /// <summary>"short", "long", "x", "y" o "angle"; cualquier otra cosa es "short".</summary>
        public static string NormalizeDirection(string mode)
        {
            string m = (mode ?? "").Trim().ToLowerInvariant();
            switch (m)
            {
                case "long": case "largo": return "long";
                case "x": return "x";
                case "y": return "y";
                case "angle": case "angulo": return "angle";
                default: return "short";
            }
        }

        public static string ConfigPath()
        {
            string dir = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
            return Path.Combine(dir ?? "", "config.json");
        }

        private static JsonSerializerOptions ReadOptions() => new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
            ReadCommentHandling = JsonCommentHandling.Skip,
            AllowTrailingCommas = true
        };

        private static JsonSerializerOptions WriteOptions() => new JsonSerializerOptions
        {
            WriteIndented = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
        };

        public static AppConfig Load()
        {
            string path = ConfigPath();
            AppConfig cfg = File.Exists(path)
                ? JsonSerializer.Deserialize<AppConfig>(File.ReadAllText(path), ReadOptions()) ?? new AppConfig()
                : new AppConfig();
            cfg.Normalize();
            return cfg;
        }

        /// <summary>Guarda esta configuracion como config.json junto a la DLL (valores por defecto de la interfaz).</summary>
        public void Save(string path = null)
        {
            File.WriteAllText(path ?? ConfigPath(), JsonSerializer.Serialize(this, WriteOptions()));
        }

        /// <summary>Copia independiente, para que la interfaz edite sin tocar la configuracion cargada.</summary>
        public AppConfig Clone()
        {
            AppConfig c = JsonSerializer.Deserialize<AppConfig>(JsonSerializer.Serialize(this, WriteOptions()), ReadOptions())
                          ?? new AppConfig();
            c.Normalize();
            return c;
        }
    }
}
