using System;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Structure;

namespace SlabRebar
{
    /// <summary>
    /// Resultado del analisis de una losa seleccionada, antes de armar nada: el contorno
    /// deducido o el motivo del rechazo, mas las elecciones por elemento hechas en la
    /// ventana (tipo de losa y direccion propias).
    /// </summary>
    public sealed class HostAnalysis
    {
        public Element Host;
        public string Tag;
        public string Mark = "", TypeName = "", FamilyName = "";

        public SlabOutline Outline;
        public string Error;

        /// <summary>Tipo de losa propio de este elemento: "" (el general), "aligerada" o "maciza".</summary>
        public string KindOverride = "";
        /// <summary>Direccion propia: "" (la general), "short", "long", "x" o "y".</summary>
        public string DirectionOverride = "";

        public bool CanBuild => Error == null && Outline != null;

        /// <summary>Tipo de losa efectivo con la configuracion dada (eleccion propia, general, o por el nombre del tipo).</summary>
        public SlabKind KindFor(AppConfig cfg)
        {
            string k = string.IsNullOrWhiteSpace(KindOverride) ? cfg.Kind : KindOverride;
            k = (k ?? "").Trim().ToLowerInvariant();
            if (k == "aligerada") return SlabKind.Aligerada;
            if (k == "maciza") return SlabKind.Maciza;
            return IsAligeradaByName(TypeName) || IsAligeradaByName(FamilyName) ? SlabKind.Aligerada : SlabKind.Maciza;
        }

        public static bool IsAligeradaByName(string name) =>
            !string.IsNullOrEmpty(name) && name.IndexOf("aliger", StringComparison.OrdinalIgnoreCase) >= 0;

        /// <summary>Si el tipo de losa viene del nombre del tipo (ni propio ni general fijado).</summary>
        public bool KindIsAuto(AppConfig cfg) => string.IsNullOrWhiteSpace(KindOverride) && cfg.KindAuto;

        public string DirectionMode(AppConfig cfg) =>
            AppConfig.NormalizeDirection(string.IsNullOrWhiteSpace(DirectionOverride) ? cfg.Direction.Mode : DirectionOverride);

        /// <summary>La losa en el sistema local de la direccion efectiva.</summary>
        public SlabFrame Frame(AppConfig cfg) => Outline?.Frame(DirectionMode(cfg), cfg.Direction.AngleDeg, cfg.DetectBeams);

        public string Kind(AppConfig cfg) => Error != null ? "SIN ARMAR" : "Losa " + (KindFor(cfg) == SlabKind.Aligerada ? "aligerada" : "maciza");

        public string Detail(AppConfig cfg) => Error ?? Outline.Describe();

        public string Partition(AppConfig cfg, string setName, string layer)
        {
            return PartitionName.Expand(cfg.PartitionTemplate, new PartitionName.Source
            {
                Mark = Mark, Id = Host.Id.ToString(), TypeName = TypeName, FamilyName = FamilyName,
                SetName = setName, Layer = layer
            });
        }

        public static HostAnalysis Analyze(Document doc, Element host, AppConfig cfg)
        {
            var a = new HostAnalysis { Host = host, Tag = "[" + host.Id + " " + host.Name + "] " };
            try
            {
                a.Mark = host.get_Parameter(BuiltInParameter.ALL_MODEL_MARK)?.AsString() ?? "";
                a.TypeName = SlabOutline.TypeNameOf(doc, host) ?? "";
                a.FamilyName = host.Category?.Name ?? "";
                if (host is Floor fl)
                {
                    try { a.FamilyName = fl.FloorType?.FamilyName ?? a.FamilyName; } catch { }
                }

                RebarHostData hd = RebarHostData.GetRebarHostData(host);
                if (hd == null || !hd.IsValidHost())
                {
                    a.Error = "no admite armadura. Revisa que el suelo sea estructural y que su material sea hormigon.";
                    return a;
                }

                a.Outline = SlabOutline.Probe(doc, host, cfg);
                if (a.Outline == null)
                    a.Error = "RECHAZADA, " + (SlabOutline.LastError ?? "no se pudo deducir el contorno (motivo desconocido)") +
                              ". No se ha creado ninguna barra.";
            }
            catch (Exception ex)
            {
                a.Error = "ERROR: " + ex.Message;
            }
            return a;
        }
    }
}
