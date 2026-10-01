using System.Windows.Interop;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Structure;
using Autodesk.Revit.UI;
using Autodesk.Revit.UI.Selection;
using UnionesAcero.Core.Analysis;
using UnionesAcero.Core.Verification;
using UnionesAcero.Revit.Selection;
using UnionesAcero.Revit.Services;
using UnionesAcero.Revit.Settings;
using UnionesAcero.Revit.UI;
using Units = UnionesAcero.Revit.Services.Units;

namespace UnionesAcero.Revit.Commands;

/// <summary>
/// Selecciona columnas, analiza sus nudos, abre la ventana previa y, si el usuario pulsa Armar,
/// ejecuta el plan de cada capa: crea las barras nuevas y sustituye las barras existentes que no
/// cumplen por la misma barra prolongada con su anclaje. Cada columna se arma en una
/// subtransacción: si el gancho de alguna barra queda fuera de la columna se invierte su
/// orientación y se vuelve a crear; si sigue fuera, se descarta (y en una corrección se conserva
/// la barra original) y se avisa.
/// </summary>
[Transaction(TransactionMode.Manual)]
[Regeneration(RegenerationOption.Manual)]
public sealed class NudosCommand : IExternalCommand
{
    public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
    {
        var uidoc = commandData.Application.ActiveUIDocument;
        var doc = uidoc.Document;
        var cfg = PluginSettings.Load();

        IList<FamilyInstance> columns;
        try { columns = GetColumns(uidoc); }
        catch (Autodesk.Revit.Exceptions.OperationCanceledException) { return Result.Cancelled; }
        if (columns.Count == 0)
        {
            message = "No se seleccionó ninguna columna estructural.";
            return Result.Cancelled;
        }

        var barTypes = new FilteredElementCollector(doc).OfClass(typeof(RebarBarType)).Cast<RebarBarType>().ToList();
        if (barTypes.Count == 0)
        {
            message = "El proyecto no tiene ningún tipo de barra (RebarBarType). Carga una familia de armadura primero.";
            return Result.Failed;
        }
        var diameters = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
        foreach (var bt in barTypes) diameters[bt.Name] = Units.ToMm(bt.BarNominalDiameter);
        var hookTypes = new FilteredElementCollector(doc).OfClass(typeof(RebarHookType)).Cast<RebarHookType>()
            .Where(h => h.Style == RebarStyle.Standard).ToList();
        var hookAngles = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
        foreach (var h in hookTypes) hookAngles[h.Name] = Math.Round(h.HookAngle * 180 / Math.PI);

        // 1. Análisis (solo lectura)
        var items = columns.Select(c => JointSession.Analyze(doc, c, cfg)).ToList();

        // 2. Ventana
        var win = new JointWindow(cfg.Clone(), items, barTypes.Select(b => b.Name).ToList(), diameters, hookTypes.Select(h => h.Name).ToList(), hookAngles);
        try { new WindowInteropHelper(win).Owner = commandData.Application.MainWindowHandle; } catch { }
        if (win.ShowDialog() != true || win.Result == null) return Result.Cancelled;
        cfg = win.Result;
        var options = cfg.ToJointOptions();

        // 3. Armado
        var log = new List<string>();
        var created = new List<ElementId>();
        var failingExisting = new List<ElementId>();
        int armed = 0, rejected = 0, fixedCount = 0;
        // Barras ya sustituidas en esta ejecución (una viga llega a dos columnas seleccionadas): id original → barra nueva.
        var replaced = new Dictionary<long, Rebar>();
        var builder = new RebarBuilder(doc) { HookTypeName = cfg.HookTypeName };

        using (var tx = new Transaction(doc, "Armar nudos viga-columna"))
        {
            tx.Start();
            foreach (var item in items)
            {
                if (!item.CanBuild || item.Analysis == null)
                {
                    rejected++;
                    log.Add(item.Tag + "SIN ARMAR -> " + (item.Error ?? "sin vigas"));
                    continue;
                }
                if (!item.HasWork)
                {
                    log.Add(item.Tag + "sin cambios -> " + item.PlanSummary());
                    if (item.InsufficientCount > 0) rejected++;
                    CollectFailing(item, failingExisting);
                    continue;
                }

                using var sub = new SubTransaction(doc);
                sub.Start();
                var made = new List<string>();
                var fixedBars = new List<string>();
                var failed = new List<string>();
                var flipped = new List<string>();
                try
                {
                    foreach (var beam in item.Beams.Where(b => b.Include))
                    {
                        foreach (var plan in beam.Plans)
                        {
                            if (plan.Action == LayerAction.CreateNew && plan.NewGroup != null)
                            {
                                var g = plan.NewGroup;
                                var rebar = CreateWithHookCheck(doc, builder, item, g, beam.Element, flipped, out var why);
                                if (rebar == null) { failed.Add(g.Label + why); continue; }
                                created.Add(rebar.Id);
                                made.Add(g.Label + " " + JointAnalyzer.DecisionName(g.Decision));
                            }
                            else if (plan.Action == LayerAction.FixExisting)
                            {
                                foreach (var (oldRebar, bar, fix0) in plan.Fixes)
                                {
                                    var fix = fix0;
                                    var target = oldRebar;
                                    // Si ya se sustituyó en otra columna, se vuelve a planificar sobre la barra nueva.
                                    if (replaced.TryGetValue(oldRebar.Id.Value, out var newer))
                                    {
                                        target = newer;
                                        var fresh = RebarInspector.Describe(doc, newer, bar.BeamName);
                                        var check = plan.Existing.FirstOrDefault(c => c.Id == bar.Id);
                                        if (fresh == null || check == null || beam.Joint == null)
                                        {
                                            failed.Add(fix.Label + " (ya sustituida en otra columna; no se pudo releer)");
                                            continue;
                                        }
                                        var recheck = new ExistingBarChecker(options).CheckBar(item.Analysis.Column, beam.Joint, fresh);
                                        if (recheck.Status != CheckStatus.Fail) { made.Add(fix.Label + " ya cumple tras la corrección anterior"); continue; }
                                        var replan = new BarFixer(options).Plan(item.Analysis.Column, beam.Joint, fresh, recheck, out var reason);
                                        if (replan == null) { failed.Add(fix.Label + " (" + reason + ")"); continue; }
                                        fix = replan;
                                    }

                                    var rebar = builder.CreateFixed(fix, target, beam.Element);
                                    if (rebar == null) { failed.Add(fix.Label); continue; }
                                    if (fix.EndHook != null && item.Solid != null)
                                    {
                                        doc.Regenerate();
                                        if (!HookInsideColumn(rebar, item.Solid))
                                        {
                                            doc.Delete(rebar.Id);
                                            rebar = builder.CreateFixed(fix, target, beam.Element, flipHook: true);
                                            if (rebar == null) { failed.Add(fix.Label); continue; }
                                            doc.Regenerate();
                                            if (!HookInsideColumn(rebar, item.Solid))
                                            {
                                                doc.Delete(rebar.Id);
                                                failed.Add(fix.Label + " (el gancho queda fuera de la columna con las dos orientaciones; se conserva la barra original)");
                                                continue;
                                            }
                                            flipped.Add(fix.Label);
                                        }
                                    }
                                    doc.Delete(target.Id);
                                    replaced[oldRebar.Id.Value] = rebar;
                                    replaced[target.Id.Value] = rebar;
                                    created.Add(rebar.Id);
                                    fixedCount++;
                                    fixedBars.Add($"{fix.Label} -> {JointAnalyzer.DecisionName(fix.Decision)} ({fix.EndShift:+0;-0} mm)");
                                }
                            }
                        }
                    }
                    sub.Commit();
                    armed++;
                    var line = item.Tag + made.Count + " conjunto(s) nuevo(s)" + (made.Count > 0 ? ": " + string.Join(", ", made) : "");
                    if (fixedBars.Count > 0) line += "  CORREGIDAS " + fixedBars.Count + ": " + string.Join(", ", fixedBars);
                    if (item.KeptCount > 0) line += $"  ({item.KeptCount} conjunto(s) existente(s) ya cumplen)";
                    if (item.InsufficientCount > 0) line += $"  SIN CREAR {item.InsufficientCount} capa(s)/barra(s) con anclaje insuficiente";
                    if (failed.Count > 0) line += "  NO CREADAS: " + string.Join(" | ", failed);
                    if (flipped.Count > 0) line += "  (gancho invertido en: " + string.Join(", ", flipped) + ")";
                    log.Add(line);
                }
                catch (Exception ex)
                {
                    sub.RollBack();
                    rejected++;
                    log.Add(item.Tag + "SIN ARMAR -> ERROR: " + ex.Message + ". Se ha deshecho todo lo creado para esta columna.");
                }

                CollectFailing(item, failingExisting);
            }

            // Barras existentes que siguen sin cumplir (modo "solo verificar" o sin corrección posible)
            failingExisting = failingExisting.Where(id => !replaced.ContainsKey(id.Value)).Distinct().ToList();
            if (cfg.HighlightFailingExistingBars && failingExisting.Count > 0)
            {
                var red = new OverrideGraphicSettings().SetProjectionLineColor(new Color(255, 0, 0)).SetProjectionLineWeight(6);
                var solidFill = new FilteredElementCollector(doc).OfClass(typeof(FillPatternElement)).Cast<FillPatternElement>().FirstOrDefault(f => f.GetFillPattern().IsSolidFill);
                if (solidFill != null) red.SetSurfaceForegroundPatternId(solidFill.Id).SetSurfaceForegroundPatternColor(new Color(255, 0, 0));
                foreach (var id in failingExisting) doc.ActiveView.SetElementOverrides(id, red);
            }
            tx.Commit();
        }

        if (created.Count > 0) uidoc.Selection.SetElementIds(created);
        if (builder.Notes.Count > 0) log.Add("Avisos: " + string.Join(" | ", builder.Notes.Distinct()));
        if (builderNotes(items, out var notes)) log.Add(notes);

        var td = new TaskDialog("Nudos viga-columna")
        {
            MainInstruction = $"{created.Count - fixedCount} conjunto(s) de barras nuevos y {fixedCount} corregido(s) en {armed} de {columns.Count} columna(s)." +
                              (failingExisting.Count > 0 ? $" {failingExisting.Count} barra(s) existente(s) siguen sin cumplir (en rojo en la vista)." : ""),
            MainContent = string.Join(Environment.NewLine, log)
        };
        if (rejected > 0)
        {
            td.MainInstruction += Environment.NewLine + "ATENCIÓN: " + rejected + " columna(s) sin armar o con capas insuficientes (ver detalle).";
            td.MainIcon = TaskDialogIcon.TaskDialogIconWarning;
        }
        td.Show();
        return Result.Succeeded;
    }

    /// <summary>Crea un grupo de barras nuevas y comprueba que el gancho quede dentro de la columna (invirtiéndolo si hace falta).</summary>
    private static Rebar? CreateWithHookCheck(Document doc, RebarBuilder builder, ColumnItem item, BarGroup g, Element host, List<string> flipped, out string why)
    {
        why = "";
        var rebar = builder.Create(g, host);
        if (rebar == null) return null;
        if (g.EndHook == null || item.Solid == null) return rebar;
        doc.Regenerate();
        if (HookInsideColumn(rebar, item.Solid)) return rebar;
        doc.Delete(rebar.Id);
        rebar = builder.Create(g, host, flipHook: true);
        if (rebar == null) return null;
        doc.Regenerate();
        if (!HookInsideColumn(rebar, item.Solid))
        {
            doc.Delete(rebar.Id);
            why = " (el gancho queda fuera de la columna con las dos orientaciones)";
            return null;
        }
        flipped.Add(g.Label);
        return rebar;
    }

    private static void CollectFailing(ColumnItem item, List<ElementId> failing)
    {
        foreach (var b in item.Beams)
            foreach (var (rebar, bar) in b.ExistingBars)
                if (b.Checks.Any(c => c.Id == bar.Id && c.Status == CheckStatus.Fail)) failing.Add(rebar.Id);
    }

    private static bool builderNotes(List<ColumnItem> items, out string notes)
    {
        var all = items.SelectMany(i => i.Notes).Distinct().ToList();
        notes = all.Count == 0 ? "" : "Notas: " + string.Join(" | ", all);
        return all.Count > 0;
    }

    /// <summary>
    /// ¿El extremo del gancho (y el centro de la pata) de la primera barra del conjunto queda dentro
    /// del sólido de la columna? Se comprueba con un segmento corto vertical en cada punto.
    /// </summary>
    private static bool HookInsideColumn(Rebar rebar, Solid column)
    {
        IList<Curve> curves;
        try { curves = rebar.GetCenterlineCurves(false, false, false, MultiplanarOption.IncludeOnlyPlanarCurves, 0); }
        catch { return true; }
        if (curves.Count == 0) return true;
        var last = curves[^1];
        var end = last.GetEndPoint(1);
        var mid = last.Evaluate(0.5, true);
        return PointInside(column, end) && PointInside(column, mid);
    }

    private static bool PointInside(Solid solid, XYZ p)
    {
        var d = Units.ToFt(2);
        var probe = Line.CreateBound(p - new XYZ(0, 0, d), p + new XYZ(0, 0, d));
        try
        {
            var result = solid.IntersectWithCurve(probe, new SolidCurveIntersectionOptions { ResultType = SolidCurveIntersectionMode.CurveSegmentsInside });
            if (result == null || result.SegmentCount == 0) return false;
            double inside = 0;
            for (var i = 0; i < result.SegmentCount; i++) inside += result.GetCurveSegment(i).Length;
            return inside >= 0.9 * probe.Length;
        }
        catch
        {
            return true; // sin comprobación posible: no bloquear
        }
    }

    private static IList<FamilyInstance> GetColumns(UIDocument uidoc)
    {
        var doc = uidoc.Document;
        var filter = new ColumnSelectionFilter();
        var pre = uidoc.Selection.GetElementIds().Select(id => doc.GetElement(id)).Where(filter.AllowElement).Cast<FamilyInstance>().ToList();
        if (pre.Count > 0) return pre;
        var refs = uidoc.Selection.PickObjects(ObjectType.Element, filter, "Selecciona las columnas cuyos nudos quieres armar y pulsa Finalizar");
        return refs.Select(r => (FamilyInstance)doc.GetElement(r)).ToList();
    }
}
