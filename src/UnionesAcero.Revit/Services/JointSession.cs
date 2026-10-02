using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Structure;
using UnionesAcero.Core.Analysis;
using UnionesAcero.Core.Geometry;
using UnionesAcero.Core.Model;
using UnionesAcero.Core.Verification;
using UnionesAcero.Revit.Settings;

namespace UnionesAcero.Revit.Services;

/// <summary>Qué se va a hacer con una capa de una viga.</summary>
public enum LayerAction
{
    /// <summary>La viga está excluida o no está conectada.</summary>
    None,
    /// <summary>No tiene barras (o se ignoran): se crean barras nuevas con el anclaje calculado.</summary>
    CreateNew,
    /// <summary>Tiene barras y todas cumplen: no se toca nada.</summary>
    KeepExisting,
    /// <summary>Tiene barras que no cumplen: se sustituyen por la misma barra prolongada con el anclaje.</summary>
    FixExisting,
    /// <summary>Tiene barras; solo se verifican (modo "solo verificar").</summary>
    VerifyOnly,
    /// <summary>No cabe ningún anclaje: no se crea ni se corrige nada en esta capa.</summary>
    Insufficient
}

/// <summary>Plan de una capa (superior o inferior) de una viga: barras nuevas, existentes que cumplen, correcciones…</summary>
public sealed class LayerPlan
{
    public required BarLayer Layer { get; init; }
    public required LayerAction Action { get; init; }
    /// <summary>Resultado del analizador para la capa (anclaje calculado con las barras elegidas).</summary>
    public LayerResult? Result { get; set; }
    /// <summary>Grupo de barras nuevas a crear (solo con <see cref="LayerAction.CreateNew"/>).</summary>
    public BarGroup? NewGroup { get; set; }
    /// <summary>Verificación de cada conjunto de barras existentes de la capa.</summary>
    public List<BarCheckResult> Existing { get; } = new();
    /// <summary>Correcciones propuestas (conjunto existente → barra corregida).</summary>
    public List<(Rebar Rebar, ExistingBar Bar, BarFix Fix)> Fixes { get; } = new();
    /// <summary>Barras que no cumplen y no tienen corrección geométrica posible, con el motivo.</summary>
    public List<(ExistingBar Bar, string Reason)> Unfixable { get; } = new();

    public bool HasExisting => Existing.Count > 0;
    public int ExistingOk => Existing.Count(c => c.Status == CheckStatus.Ok);
    public int ExistingFail => Existing.Count(c => c.Status == CheckStatus.Fail);
    public bool Ok => Action is LayerAction.CreateNew or LayerAction.KeepExisting || (Action == LayerAction.FixExisting && Unfixable.Count == 0) || (Action == LayerAction.VerifyOnly && ExistingFail == 0);
    public bool NeedsHook => NewGroup?.EndHook != null || Fixes.Any(f => f.Fix.EndHook != null);
}

/// <summary>Una viga que llega a una columna, con lo leído del modelo y lo que el usuario cambia en la ventana.</summary>
public sealed class BeamItem
{
    public required FamilyInstance Element { get; init; }
    public required string Name { get; init; }
    /// <summary>Geometría leída del modelo (eje, ancho, peralte, cota, recubrimiento).</summary>
    public required BeamSection Base { get; init; }
    /// <summary>Barras encontradas en el modelo (null si no tiene).</summary>
    public (double TopDb, int TopN, double BottomDb, int BottomN)? ModelBars { get; init; }

    public bool Include { get; set; } = true;

    /// <summary>
    /// Viga principal en los cruces del nudo: conserva la cota de sus capas y la posición de sus ganchos;
    /// las demás ceden. Sin marcar, decide el peralte, el ancho, el diámetro y el orden.
    /// </summary>
    public bool Principal { get; set; }

    /// <summary>Elecciones propias de esta viga (vacío / 0 = valores generales).</summary>
    public string TopBarTypeName { get; set; } = "";
    public int TopBarCount { get; set; }
    public string BottomBarTypeName { get; set; } = "";
    public int BottomBarCount { get; set; }

    /// <summary>Barras longitudinales existentes en el modelo (un elemento por conjunto).</summary>
    public List<(Rebar Rebar, ExistingBar Bar)> ExistingBars { get; } = new();

    /// <summary>Resultado de la última verificación de las barras existentes.</summary>
    public List<BarCheckResult> Checks { get; set; } = new();

    /// <summary>Plan por capa tras el último recálculo.</summary>
    public List<LayerPlan> Plans { get; set; } = new();

    public BeamJoint? Joint { get; set; }

    public string Label => Name;

    public LayerPlan? Plan(BarLayer layer) => Plans.FirstOrDefault(p => p.Layer == layer);

    public string ModelBarsText => ModelBars.HasValue
        ? $"sup. {ModelBars.Value.TopN}Ø{ModelBars.Value.TopDb:0}, inf. {ModelBars.Value.BottomN}Ø{ModelBars.Value.BottomDb:0}"
        : "sin barras longitudinales";
}

/// <summary>Una columna seleccionada: sección deducida (o motivo del rechazo), sus vigas y el análisis vigente.</summary>
public sealed class ColumnItem
{
    public required FamilyInstance Element { get; init; }
    public required string Tag { get; init; }
    public ColumnSection? Section { get; init; }
    public string? Error { get; init; }
    public ColumnRebarInfo? ModelRebar { get; init; }
    public List<BeamItem> Beams { get; } = new();
    public List<string> Notes { get; } = new();
    public JointAnalysis? Analysis { get; set; }

    public bool CanBuild => Error == null && Section != null && Beams.Count > 0;

    public string Kind => Error != null ? "SIN ARMAR" : Section == null ? "?" : "Columna " + ShapeName(Section.Outline);

    public IEnumerable<LayerPlan> ActivePlans => Beams.Where(b => b.Include).SelectMany(b => b.Plans);
    public int NewGroups => ActivePlans.Count(p => p.Action == LayerAction.CreateNew && p.NewGroup != null);
    public int FixCount => ActivePlans.Sum(p => p.Fixes.Count);
    public int KeptCount => ActivePlans.Sum(p => p.ExistingOk);
    public int InsufficientCount => ActivePlans.Count(p => p.Action == LayerAction.Insufficient) + ActivePlans.Sum(p => p.Unfixable.Count);
    /// <summary>Ajustes de detallado en el nudo (corrimientos, cotas, ganchos retrasados) sobre barras nuevas y corregidas.</summary>
    public int AdjustmentCount => ActivePlans.Sum(p => (p.Action == LayerAction.CreateNew ? p.NewGroup?.Adjustments.Count ?? 0 : 0) + p.Fixes.Sum(f => f.Fix.Adjustments.Count));
    public bool HasWork => ActivePlans.Any(p => (p.Action == LayerAction.CreateNew && p.NewGroup != null) || p.Fixes.Count > 0);

    public static string ShapeName(Polygon2D p)
    {
        var n = p.Count;
        if (n == 4 && p.IsConvex) return "rectangular";
        if (n == 6) return "en L";
        if (n == 8)
        {
            // T o Z: contar vértices cóncavos
            var concave = 0;
            for (var i = 0; i < n; i++)
            {
                var a = p.Vertices[i]; var b = p.Vertices[(i + 1) % n]; var c = p.Vertices[(i + 2) % n];
                if ((b - a).Cross(c - b) < 0) concave++;
            }
            return concave == 2 ? "en T" : "en Z";
        }
        if (n == 12) return "en cruz";
        return p.IsConvex ? $"convexa de {n} lados" : $"de {n} lados";
    }

    public string Describe()
    {
        if (Section == null) return Error ?? "";
        var (min, max) = Section.Outline.Bounds;
        var s = $"{(max.X - min.X):0} x {(max.Y - min.Y):0} mm, {Beams.Count} viga(s)";
        s += ModelRebar != null ? $", armada ({ModelRebar.Describe()})" : ", sin armadura modelada";
        return s;
    }

    /// <summary>Resumen de lo que se haría al armar: barras nuevas, correcciones, existentes que cumplen, insuficientes.</summary>
    public string PlanSummary()
    {
        var parts = new List<string>();
        var nw = NewGroups; var fx = FixCount; var kp = KeptCount; var ins = InsufficientCount;
        if (nw > 0) parts.Add($"{nw} grupo(s) de barras nuevas");
        if (fx > 0) parts.Add($"{fx} conjunto(s) existente(s) a corregir");
        if (kp > 0) parts.Add($"{kp} existente(s) cumplen");
        var adj = AdjustmentCount;
        if (adj > 0) parts.Add($"{adj} ajuste(s) de detallado en el nudo");
        var verify = ActivePlans.Where(p => p.Action == LayerAction.VerifyOnly).Sum(p => p.ExistingFail);
        if (verify > 0) parts.Add($"{verify} existente(s) NO cumplen (solo verificación)");
        if (ins > 0) parts.Add($"{ins} INSUFICIENTE(S)");
        return parts.Count == 0 ? "nada que armar" : string.Join(", ", parts);
    }
}

/// <summary>Lectura del modelo y re-análisis con la configuración vigente.</summary>
public static class JointSession
{
    public static ColumnItem Analyze(Document doc, FamilyInstance column, PluginSettings settings)
    {
        var tag = "[" + column.Id.Value + " " + RevitGeometryExtractor.ElementLabel(column) + "] ";
        var extractor = new RevitGeometryExtractor(doc, settings);
        ColumnSection section;
        ColumnRebarInfo? modelRebar;
        try
        {
            var host = RebarHostData.GetRebarHostData(column);
            if (host == null || !host.IsValidHost())
                return new ColumnItem { Element = column, Tag = tag, Error = "no admite armadura. Revisa que el material sea hormigón y que sea un pilar estructural." };
            section = extractor.ExtractColumn(column, out modelRebar);
        }
        catch (Exception ex)
        {
            return new ColumnItem { Element = column, Tag = tag, Error = "RECHAZADO, " + ex.Message };
        }

        var item = new ColumnItem { Element = column, Tag = tag, Section = section, ModelRebar = modelRebar };
        foreach (var beam in extractor.FindBeamsNear(column))
        {
            var bs = extractor.ExtractBeam(beam, out var modelBars);
            if (bs == null) continue;
            var bi = new BeamItem { Element = beam, Name = bs.Name, Base = bs, ModelBars = modelBars };
            bi.ExistingBars.AddRange(RebarInspector.CollectBars(doc, beam, bs.Name));
            item.Beams.Add(bi);
        }
        item.Notes.AddRange(extractor.Notes);
        if (item.Beams.Count == 0) item.Notes.Add("No se encontró ninguna viga que llegue a esta columna (radio de búsqueda " + settings.BeamSearchDistanceMm + " mm).");
        return item;
    }

    /// <summary>
    /// Vuelve a construir las vigas con las elecciones de la ventana, ejecuta el análisis, verifica las
    /// barras existentes y decide el plan de cada capa. Devuelve null si la columna no es armable.
    /// </summary>
    public static JointAnalysis? Recompute(ColumnItem item, PluginSettings settings, Func<string, double> diameterOf)
    {
        if (item.Section == null) return null;
        var options = settings.ToJointOptions();
        var column = new ColumnSection(item.Section.Name, item.Section.Outline)
        {
            Cover = item.Section.Cover > 0 ? item.Section.Cover : settings.ColumnCoverMm,
            TieDiameter = item.ModelRebar is { TieDb: > 0 } mr ? mr.TieDb : settings.ColumnTieDiameterMm,
            LongitudinalBarDiameter = item.Section.LongitudinalBarDiameter,
            LongitudinalBarPositions = item.Section.LongitudinalBarPositions,
            TopElevation = item.Section.TopElevation,
            BottomElevation = item.Section.BottomElevation,
            SourceId = item.Section.SourceId
        };

        var beams = new List<BeamSection>();
        foreach (var b in item.Beams.Where(b => b.Include))
        {
            var topType = string.IsNullOrEmpty(b.TopBarTypeName) ? settings.TopBarTypeName : b.TopBarTypeName;
            var botType = string.IsNullOrEmpty(b.BottomBarTypeName) ? settings.BottomBarTypeName : b.BottomBarTypeName;
            var topDb = diameterOf(topType);
            var botDb = diameterOf(botType);
            var topN = b.TopBarCount > 0 ? b.TopBarCount : settings.TopBarCount;
            var botN = b.BottomBarCount > 0 ? b.BottomBarCount : settings.BottomBarCount;
            if (settings.InferBarsFromModel && b.ModelBars.HasValue)
            {
                var m = b.ModelBars.Value;
                if (topDb <= 0 && m.TopDb > 0) topDb = m.TopDb;
                if (botDb <= 0 && m.BottomDb > 0) botDb = m.BottomDb;
                if (b.TopBarCount <= 0 && m.TopN > 0) topN = m.TopN;
                if (b.BottomBarCount <= 0 && m.BottomN > 0) botN = m.BottomN;
            }
            if (topDb <= 0) topDb = 16;
            if (botDb <= 0) botDb = 16;

            beams.Add(new BeamSection(b.Base.Name, b.Base.AxisStart, b.Base.AxisEnd, b.Base.Width, b.Base.Depth)
            {
                TopElevation = b.Base.TopElevation,
                Cover = b.Base.Cover > 0 ? b.Base.Cover : settings.BeamCoverMm,
                StirrupDiameter = settings.BeamStirrupDiameterMm,
                TopBarDiameter = topDb, TopBarCount = topN, TopBarTypeName = topType,
                BottomBarDiameter = botDb, BottomBarCount = botN, BottomBarTypeName = botType,
                SourceId = b.Base.SourceId,
                CrossingPriority = b.Principal ? 1 : 0
            });
        }

        // Sin resolver choques todavía: se hace al final, con las correcciones y las barras existentes.
        var analysis = new JointAnalyzer(options).Analyze(column, beams, resolveClashes: false);
        item.Analysis = analysis;
        var checker = new ExistingBarChecker(options);
        var fixer = new BarFixer(options);
        var mode = settings.ExistingBarsAction;

        foreach (var b in item.Beams)
        {
            b.Joint = analysis.Joints.FirstOrDefault(j => j.Beam.SourceId == b.Base.SourceId);
            b.Checks = b.Joint != null && b.ExistingBars.Count > 0
                ? checker.Check(analysis, b.ExistingBars.Select(e => e.Bar))
                : new List<BarCheckResult>();
            b.Plans = options.Layers.Select(layer => PlanLayer(b, layer, analysis, mode, fixer)).ToList();
        }

        if (options.SplicePassThroughBars && mode == ExistingBarsAction.Fix) ApplySplices(item, analysis, options);
        ApplyDetailing(item, analysis, options);
        return analysis;
    }

    /// <summary>
    /// Barras ya modeladas en dos vigas colineales: la de la viga principal se hace continua a través del
    /// nudo y se empalma fuera de él con la de la opuesta, que se corta en el inicio del traslape. Sustituye
    /// a cualquier otra corrección de esas barras (y convierte en corrección las que cumplían).
    /// </summary>
    private static void ApplySplices(ColumnItem item, JointAnalysis analysis, JointOptions options)
    {
        var candidates = new List<SpliceCandidate>();
        foreach (var b in item.Beams.Where(b => b.Include && b.Joint is { Connected: true, OppositeBeam: not null }))
            foreach (var c in b.Checks.Where(c => c.Layer.HasValue && c.Status != CheckStatus.NotApplicable))
            {
                var entry = b.ExistingBars.FirstOrDefault(e => e.Bar.Id == c.Id);
                if (entry.Rebar != null) candidates.Add(new SpliceCandidate(b.Joint!, c.Layer!.Value, entry.Bar));
            }
        if (candidates.Count == 0) return;

        var splice = new SpliceResolver(options).Resolve(analysis, candidates);
        foreach (var d in splice.Diagnostics)
        {
            var j = analysis.Joints.FirstOrDefault(j => j.Beam.Name == d.Element);
            if (j != null) j.Diagnostics.Add(d); else analysis.Diagnostics.Add(d);
        }
        if (splice.Fixes.Count == 0) return;

        foreach (var b in item.Beams)
        {
            for (var i = 0; i < b.Plans.Count; i++)
            {
                var plan = b.Plans[i];
                var here = splice.Fixes.Where(f => ReferenceEquals(f.Joint, b.Joint) && f.Layer == plan.Layer).ToList();
                if (here.Count == 0) continue;
                var np = new LayerPlan { Layer = plan.Layer, Action = LayerAction.FixExisting, Result = plan.Result };
                np.Existing.AddRange(plan.Existing);
                np.Fixes.AddRange(plan.Fixes.Where(f => !splice.ReplacedBarIds.Contains(f.Bar.Id)));
                foreach (var f in here)
                {
                    var e = b.ExistingBars.FirstOrDefault(e => e.Bar.Id == f.Bar.Id);
                    if (e.Rebar != null) np.Fixes.Add((e.Rebar, e.Bar, f));
                }
                np.Unfixable.AddRange(plan.Unfixable.Where(u => !splice.ReplacedBarIds.Contains(u.Bar.Id)));
                b.Plans[i] = np;
            }
        }
    }

    /// <summary>
    /// Reglas de detallado del nudo (paso por dentro de las verticales de la columna, ganchos en la misma
    /// esquina, cruces a la misma cota) sobre las barras nuevas, las corregidas y, como obstáculos fijos,
    /// las existentes que se conservan. Después refresca en cada plan el resultado de su capa.
    /// </summary>
    private static void ApplyDetailing(ColumnItem item, JointAnalysis analysis, JointOptions options)
    {
        var active = item.Beams.Where(b => b.Include && b.Joint is { Connected: true }).ToList();
        var fixes = active.SelectMany(b => b.Plans.SelectMany(p => p.Fixes.Select(f => f.Fix))).ToList();
        var kept = new List<KeptBar>();
        foreach (var b in active)
            foreach (var p in b.Plans)
            {
                var fixedIds = p.Fixes.Select(f => f.Bar.Id).ToHashSet();
                foreach (var c in p.Existing)
                {
                    if (fixedIds.Contains(c.Id)) continue;
                    if (c.Status != CheckStatus.Ok && p.Action != LayerAction.VerifyOnly) continue;
                    var e = b.ExistingBars.FirstOrDefault(e => e.Bar.Id == c.Id);
                    if (e.Rebar != null) kept.Add(new KeptBar(b.Joint!, p.Layer, e.Bar));
                }
            }

        new JointDetailer(options).Apply(analysis, fixes, kept,
            (j, layer) => active.Any(b => ReferenceEquals(b.Joint, j) && b.Plan(layer)?.Action == LayerAction.CreateNew));

        // Los resultados por capa pueden haberse sustituido (cota, extremo, reparto): refrescar los planes.
        foreach (var b in active)
            foreach (var p in b.Plans)
            {
                var lr = b.Joint!.Layers.FirstOrDefault(l => l.Layer == p.Layer);
                if (lr == null) continue;
                p.Result = lr;
                if (p.Action == LayerAction.CreateNew) p.NewGroup = lr.Group;
            }
    }

    private static LayerPlan PlanLayer(BeamItem b, BarLayer layer, JointAnalysis analysis, ExistingBarsAction mode, BarFixer fixer)
    {
        var joint = b.Joint;
        if (!b.Include || joint == null || !joint.Connected)
            return new LayerPlan { Layer = layer, Action = LayerAction.None };

        var result = joint.Layers.FirstOrDefault(l => l.Layer == layer);
        var existing = b.Checks.Where(c => c.Layer == layer && c.Status != CheckStatus.NotApplicable).ToList();

        if (existing.Count == 0 || mode == ExistingBarsAction.AddNew)
        {
            var action = result == null ? LayerAction.None
                : result.Decision == AnchorageDecision.Insufficient ? LayerAction.Insufficient
                : LayerAction.CreateNew;
            var plan = new LayerPlan { Layer = layer, Action = action, Result = result, NewGroup = action == LayerAction.CreateNew ? result?.Group : null };
            plan.Existing.AddRange(existing);
            return plan;
        }

        if (mode == ExistingBarsAction.VerifyOnly)
        {
            var plan = new LayerPlan { Layer = layer, Action = LayerAction.VerifyOnly, Result = result };
            plan.Existing.AddRange(existing);
            return plan;
        }

        // Corregir: cada conjunto que no cumple se sustituye por la misma barra prolongada con el anclaje.
        var fixes = new List<(Rebar, ExistingBar, BarFix)>();
        var unfixable = new List<(ExistingBar, string)>();
        foreach (var check in existing.Where(c => c.Status == CheckStatus.Fail))
        {
            var entry = b.ExistingBars.FirstOrDefault(e => e.Bar.Id == check.Id);
            if (entry.Rebar == null) continue;
            var fix = fixer.Plan(analysis.Column, joint, entry.Bar, check, out var reason);
            if (fix != null) fixes.Add((entry.Rebar, entry.Bar, fix));
            else unfixable.Add((entry.Bar, reason ?? "sin corrección posible"));
        }
        var fixAction = fixes.Count > 0 ? LayerAction.FixExisting
            : unfixable.Count > 0 ? LayerAction.Insufficient
            : LayerAction.KeepExisting;
        var fp = new LayerPlan { Layer = layer, Action = fixAction, Result = result };
        fp.Existing.AddRange(existing);
        fp.Fixes.AddRange(fixes);
        fp.Unfixable.AddRange(unfixable);
        return fp;
    }
}
