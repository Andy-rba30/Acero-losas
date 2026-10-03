using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows.Interop;
using Arba.Comun;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Structure;
using Autodesk.Revit.UI;
using Autodesk.Revit.UI.Selection;

namespace SlabRebar
{
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class ArmarLosaCommand : IExternalCommand
    {
        /// <summary>Que hacer con la armadura que este add-in ya habia creado en las losas seleccionadas.</summary>
        private enum ExistingAction
        {
            /// <summary>No hay armadura previa del add-in en ninguna losa.</summary>
            None,
            /// <summary>Borrar los conjuntos propios (migrando antes los anteriores al contrato) y armar de nuevo.</summary>
            DeleteAndRebuild,
            /// <summary>Dejar lo que hay y armar encima (quedara duplicado).</summary>
            Keep,
            /// <summary>Solo migrar las barras antiguas al contrato (particion y origen); esas losas no se rearman.</summary>
            MigrateOnly,
        }

        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            UIDocument uidoc = commandData.Application.ActiveUIDocument;
            Document doc = uidoc.Document;

            AppConfig cfg;
            try { cfg = AppConfig.Load(); }
            catch (Exception ex)
            {
                message = "No se pudo leer config.json (" + AppConfig.ConfigPath() + "): " + ex.Message;
                return Result.Failed;
            }

            IList<Element> hosts;
            try { hosts = GetHosts(uidoc); }
            catch (Autodesk.Revit.Exceptions.OperationCanceledException) { return Result.Cancelled; }

            if (hosts.Count == 0)
            {
                message = "No se selecciono ninguna losa (suelo estructural).";
                return Result.Cancelled;
            }

            var allTypes = RebarGenerator.AllBarTypes(doc);
            List<string> barTypes = allTypes.Select(b => b.Name).ToList();
            var diametersMm = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
            foreach (RebarBarType bt in allTypes)
                diametersMm[bt.Name] = UnitUtils.ConvertFromInternalUnits(bt.BarNominalDiameter, UnitTypeId.Millimeters);
            if (barTypes.Count == 0)
            {
                message = "El proyecto no tiene ningun tipo de barra (RebarBarType). Carga una familia de armadura primero.";
                return Result.Failed;
            }
            var allHooks = RebarGenerator.AllHookTypes(doc);
            List<string> hookTypes = allHooks.Select(h => h.Name).ToList();
            // angulo de cada gancho (grados) para dibujarlo en los esquemas
            var hookAngles = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
            foreach (RebarHookType h in allHooks)
            {
                double deg = 90;
                try { deg = Math.Round(h.HookAngle * 180 / Math.PI); } catch { }
                hookAngles[h.Name] = deg;
            }
            // medidas de cada gancho con cada tipo de barra (longitud y doblado), para avisar ya en la ventana si no cabe
            var hookDims = new Dictionary<string, HookDims>();
            foreach (RebarBarType bt in allTypes)
                foreach (RebarHookType h in allHooks)
                    hookDims[HookDims.Key(bt.Name, h.Name)] = RebarGenerator.HookDimsOf(bt, h.Id);

            // --- 1. Analisis geometrico de cada elemento (solo lectura, sin transaccion) ---
            var items = hosts.Select(h => HostAnalysis.Analyze(doc, h, cfg)).ToList();

            // --- 2. Interfaz: el usuario revisa que se ha detectado y elige el armado ---
            var win = new RebarOptionsWindow(cfg.Clone(), barTypes, diametersMm, hookTypes, hookAngles, hookDims, items);
            try { new WindowInteropHelper(win).Owner = commandData.Application.MainWindowHandle; } catch { }
            bool? ok = win.ShowDialog();
            if (ok != true || win.Result == null) return Result.Cancelled;
            cfg = win.Result;

            // --- 2b. Armadura previa de este add-in en las losas armables (contrato ARBA-comun) ---
            // Propia = con "ARBA - Origen" = LOSAS. Antigua = particion "LOSA-…" sin origen (anterior al contrato):
            // no se reconoce como propia hasta migrarla, por eso se ofrece migrar antes de rearmar.
            var own = new Dictionary<ElementId, int>();
            var legacy = new HashSet<ElementId>();
            foreach (HostAnalysis item in items.Where(i => i.CanBuild))
            {
                int n = 0;
                try { n = ArbaOrigin.Find(doc, ArbaContract.Losas, item.Host).Count; } catch { }
                if (n > 0) own[item.Host.Id] = n;
                bool hasLegacy = false;
                try { hasLegacy = ArbaMigration.HasLegacy(doc, item.Host, ArbaContract.Losas); } catch { }
                if (hasLegacy) legacy.Add(item.Host.Id);
            }
            ExistingAction action = ExistingAction.None;
            if (own.Count > 0 || legacy.Count > 0)
            {
                action = AskExisting(own, legacy);
                if (action == ExistingAction.None) return Result.Cancelled;
            }

            // --- 3. Armado ---
            var log = new List<string>();
            var avisos = new List<string>();
            int total = 0, armed = 0, rejected = 0, migrated = 0, deletedSets = 0;

            using (Transaction tx = new Transaction(doc, "Armar losas"))
            {
                tx.Start();

                // Parametros compartidos del contrato (ARBA - Origen, ARBA - Codigo, Metrado - Elemento), antes
                // de la primera subtransaccion: vinculados a armaduras por GUID fijo; los avisos van al informe.
                ArbaSharedParams.Ensure(doc, new[] { ArbaContract.Origen, ArbaContract.Codigo, ArbaContract.Elemento }, avisos);
                doc.Regenerate();

                foreach (HostAnalysis item in items)
                {
                    string tag = item.Tag;
                    if (!item.CanBuild)
                    {
                        rejected++;
                        log.Add(tag + "SIN ARMAR -> " + item.Detail(cfg));
                        continue;
                    }

                    bool hasOwn = own.ContainsKey(item.Host.Id);
                    bool hasLegacy = legacy.Contains(item.Host.Id);

                    // Migrar sin rearmar: solo las losas con armadura previa del add-in; las demas se arman normalmente.
                    if (action == ExistingAction.MigrateOnly && (hasOwn || hasLegacy))
                    {
                        using (SubTransaction sub = new SubTransaction(doc))
                        {
                            sub.Start();
                            try
                            {
                                ArbaMigrationResult mr = ArbaMigration.MigrateHost(doc, item.Host, ArbaContract.Losas);
                                sub.Commit();
                                migrated++;
                                log.Add(tag + "MIGRADA sin rearmar -> " + mr.Migradas + " conjunto(s) pasados al contrato (" +
                                        mr.ParticionesCambiadas + " particiones reescritas, " + mr.OrigenEscrito + " origenes escritos), " +
                                        mr.YaConformes + " ya conformes." +
                                        (mr.Avisos.Count > 0 ? "  AVISOS: " + string.Join(" | ", mr.Avisos) : ""));
                            }
                            catch (Exception ex)
                            {
                                sub.RollBack();
                                rejected++;
                                log.Add(tag + "SIN MIGRAR -> ERROR: " + ex.Message);
                            }
                        }
                        continue;
                    }

                    // Cada elemento se arma dentro de una subtransaccion. Si cualquier barra
                    // queda fuera del hormigon (red de seguridad), se deshace TODO lo creado
                    // para ese elemento (y el borrado de su armadura previa): o se arma entero y bien, o no se arma.
                    using (SubTransaction sub = new SubTransaction(doc))
                    {
                        sub.Start();
                        BuildResult res = null;
                        string error = null;
                        string previous = "";
                        try
                        {
                            if (action == ExistingAction.DeleteAndRebuild && (hasOwn || hasLegacy))
                            {
                                // las barras anteriores al contrato se migran primero para que lleven origen y se reconozcan como propias
                                if (hasLegacy) ArbaMigration.MigrateHost(doc, item.Host, ArbaContract.Losas);
                                int sets = ArbaOrigin.Delete(doc, ArbaContract.Losas, item.Host, out int bars);
                                doc.Regenerate();
                                deletedSets += sets;
                                previous = "borrados " + sets + " conjunto(s) anteriores (" + bars + " barras); ";
                            }
                            else if (action == ExistingAction.Keep && (hasOwn || hasLegacy))
                            {
                                previous = "se conserva la armadura anterior del add-in (quedara duplicada); ";
                            }

                            res = RebarGenerator.Build(doc, item, cfg);
                            if (res.Safe && res.Created.Count > 0)
                            {
                                doc.Regenerate();
                                RebarGenerator.VerifyCreated(doc, item, cfg, res);
                            }
                        }
                        catch (Exception ex)
                        {
                            error = ex.Message;
                        }

                        bool keep = error == null && res != null && res.Safe;
                        string desc = item.Kind(cfg) + ", " + item.Frame(cfg).Describe() + ", " + item.Detail(cfg);
                        if (keep)
                        {
                            sub.Commit();
                            armed++;
                            total += res.Created.Count;
                            string line = tag + desc + "  ->  " + previous + res.Summary;
                            if (res.Failed.Count > 0)
                                line += "  INCOMPLETO, no se pudieron crear: " + string.Join(" | ", res.Failed);
                            if (res.Warnings.Count > 0)
                                line += "  AVISOS: " + string.Join(" | ", res.Warnings);
                            log.Add(line);
                        }
                        else
                        {
                            sub.RollBack();
                            rejected++;
                            string undone = previous.Length > 0 ? " (su armadura anterior sigue intacta)" : "";
                            if (error != null)
                                log.Add(tag + "SIN ARMAR -> ERROR: " + error + ". Se ha deshecho todo lo creado para este elemento" + undone + ".");
                            else
                                log.Add(tag + "SIN ARMAR -> " + desc + ": barras fuera del hormigon, se ha deshecho todo el " +
                                        "elemento" + undone + " (" + res.Rejected.Count + "): " + string.Join(" | ", res.Rejected));
                        }
                    }
                }
                tx.Commit();
            }

            var td = new TaskDialog("Armado de losas")
            {
                MainInstruction = total + " conjuntos de armadura creados en " + armed + " de " + hosts.Count + " elemento(s)." +
                                  (deletedSets > 0 ? " " + deletedSets + " conjunto(s) anteriores borrados." : "") +
                                  (migrated > 0 ? " " + migrated + " elemento(s) migrados sin rearmar." : ""),
                MainContent = string.Join(Environment.NewLine, log),
                FooterText = "Particion " + ArbaPartition.FilterPrefix(ArbaContract.CatLosas, ArbaContract.Losas.Prefix) + "marca, " +
                             ArbaContract.Origen.Name + " = " + ArbaContract.Losas.Origin + ". Contrato ARBA-comun " + ArbaContract.Version + "."
            };
            if (avisos.Count > 0)
                td.MainContent += Environment.NewLine + Environment.NewLine + "Parametros compartidos: " + string.Join(" | ", avisos);
            if (rejected > 0)
            {
                td.MainInstruction += Environment.NewLine + "ATENCION: " + rejected +
                                      " elemento(s) SIN ARMAR (ver detalle). No se ha creado ninguna barra en ellos.";
                td.MainIcon = TaskDialogIcon.TaskDialogIconWarning;
            }
            td.Show();
            return Result.Succeeded;
        }

        /// <summary>
        /// Pregunta una sola vez que hacer con la armadura que este add-in ya creo en las losas seleccionadas:
        /// borrar y rearmar, conservar (armar encima) o, si hay barras anteriores al contrato, solo migrarlas.
        /// Devuelve None si el usuario cancela.
        /// </summary>
        private static ExistingAction AskExisting(Dictionary<ElementId, int> own, HashSet<ElementId> legacy)
        {
            int sets = own.Values.Sum();
            var hostsWithAny = new HashSet<ElementId>(own.Keys);
            hostsWithAny.UnionWith(legacy);

            var td = new TaskDialog("Armar losas")
            {
                MainInstruction = "Ya hay armadura de este add-in en " + hostsWithAny.Count + " de las losas seleccionadas",
                MainContent = (own.Count > 0
                                   ? sets + " conjunto(s) con " + ArbaContract.Origen.Name + " = " + ArbaContract.Losas.Origin + " en " + own.Count + " losa(s)."
                                   : "") +
                              (legacy.Count > 0
                                   ? (own.Count > 0 ? Environment.NewLine : "") + legacy.Count + " losa(s) con barras anteriores al contrato (particion " +
                                     string.Join("/", ArbaContract.Losas.Legacy.Select(l => l + "-…")) + " sin " + ArbaContract.Origen.Name +
                                     "): no se reconocen como propias hasta migrarlas."
                                   : ""),
                CommonButtons = TaskDialogCommonButtons.Cancel,
                DefaultButton = TaskDialogResult.Cancel,
                FooterText = "Contrato ARBA-comun " + ArbaContract.Version
            };
            td.AddCommandLink(TaskDialogCommandLinkId.CommandLink1, "Borrar la armadura del add-in y rearmar",
                "Se borran solo los conjuntos creados por Losas en esas losas (las anteriores al contrato se migran antes para " +
                "reconocerlas) y se arman de nuevo. El resto de la armadura del modelo no se toca. Si una losa se rechaza, " +
                "su armadura anterior se conserva.");
            td.AddCommandLink(TaskDialogCommandLinkId.CommandLink2, "Conservar y armar encima",
                "La armadura existente se queda tal cual y se anaden los conjuntos nuevos (quedaran duplicados).");
            if (legacy.Count > 0)
                td.AddCommandLink(TaskDialogCommandLinkId.CommandLink3, "Migrar sin rearmar",
                    "Solo pasa las barras antiguas a la particion del contrato (" + ArbaPartition.FilterPrefix(ArbaContract.CatLosas, ArbaContract.Losas.Prefix) +
                    "marca) y les escribe " + ArbaContract.Origen.Name + ", " + ArbaContract.Codigo.Name + " y " + ArbaContract.Elemento.Name +
                    ", sin crear ni borrar barras. Esas losas no se rearman; las que no tienen armadura previa si.");

            TaskDialogResult r = td.Show();
            switch (r)
            {
                case TaskDialogResult.CommandLink1: return ExistingAction.DeleteAndRebuild;
                case TaskDialogResult.CommandLink2: return ExistingAction.Keep;
                case TaskDialogResult.CommandLink3: return ExistingAction.MigrateOnly;
                default: return ExistingAction.None;
            }
        }

        private static IList<Element> GetHosts(UIDocument uidoc)
        {
            Document doc = uidoc.Document;
            var sel = uidoc.Selection.GetElementIds()
                .Select(id => doc.GetElement(id))
                .Where(IsCandidate)
                .ToList();
            if (sel.Count > 0) return sel;

            IList<Reference> refs = uidoc.Selection.PickObjects(
                ObjectType.Element, new HostFilter(),
                "Selecciona las losas (suelos estructurales) a armar y pulsa Finalizar");
            return refs.Select(r => doc.GetElement(r)).ToList();
        }

        /// <summary>Suelos (Floor), incluidas las losas de cimentacion, que son Floor con otra categoria.</summary>
        private static bool IsCandidate(Element e) => e is Floor && e.Category != null;

        private class HostFilter : ISelectionFilter
        {
            public bool AllowElement(Element e) => IsCandidate(e);
            public bool AllowReference(Reference r, XYZ p) => false;
        }
    }
}
