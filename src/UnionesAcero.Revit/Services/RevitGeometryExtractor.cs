using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Structure;
using UnionesAcero.Core.Geometry;
using UnionesAcero.Core.Model;
using UnionesAcero.Revit.Settings;

namespace UnionesAcero.Revit.Services;

/// <summary>Armadura ya modelada en una columna (informativa y para el núcleo confinado).</summary>
public sealed record ColumnRebarInfo(int LongitudinalCount, double LongitudinalDb, double TieDb, int RebarSets)
{
    /// <summary>Posición en planta (mm, proyecto) de cada barra vertical leída del modelo.</summary>
    public IReadOnlyList<Vec2> Positions { get; init; } = Array.Empty<Vec2>();

    public string Describe()
    {
        var parts = new List<string>();
        if (LongitudinalCount > 0) parts.Add($"{LongitudinalCount}Ø{LongitudinalDb:0}");
        if (TieDb > 0) parts.Add($"estribos Ø{TieDb:0}");
        return parts.Count == 0 ? $"{RebarSets} conjunto(s) de barras" : string.Join(" + ", parts);
    }
}

/// <summary>
/// Lee del modelo de Revit la sección de la columna (cualquier forma) y las vigas que llegan a ella,
/// y los traduce al modelo del núcleo (mm).
/// </summary>
public sealed class RevitGeometryExtractor
{
    private readonly Document _doc;
    private readonly PluginSettings _settings;

    public RevitGeometryExtractor(Document doc, PluginSettings settings)
    {
        _doc = doc;
        _settings = settings;
    }

    public List<string> Notes { get; } = new();

    // ------------------------------------------------------------------ Columna

    public ColumnSection ExtractColumn(FamilyInstance column) => ExtractColumn(column, out _);

    /// <param name="modelRebar">Armadura ya modelada en la columna (null si no tiene).</param>
    public ColumnSection ExtractColumn(FamilyInstance column, out ColumnRebarInfo? modelRebar)
    {
        modelRebar = InferColumnBars(column);
        var solid = LargestSolid(column) ?? throw new InvalidOperationException("La columna no tiene geometría sólida.");
        var outline = HorizontalSectionOutline(solid) ?? throw new InvalidOperationException(
            "No se encontró una cara horizontal en la columna para obtener su sección. Las columnas inclinadas no están soportadas.");

        var bb = solid.GetBoundingBox();
        var zMin = Units.ToMm(bb.Transform.OfPoint(bb.Min).Z);
        var zMax = Units.ToMm(bb.Transform.OfPoint(bb.Max).Z);

        var cover = CoverMm(column) ?? _settings.ColumnCoverMm;
        return new ColumnSection(ElementLabel(column), outline)
        {
            Cover = cover,
            TieDiameter = modelRebar is { TieDb: > 0 } mr ? mr.TieDb : _settings.ColumnTieDiameterMm,
            LongitudinalBarDiameter = modelRebar is { LongitudinalDb: > 0 } ml ? ml.LongitudinalDb : 16,
            LongitudinalBarPositions = modelRebar?.Positions ?? Array.Empty<Vec2>(),
            TopElevation = zMax,
            BottomElevation = zMin,
            SourceId = column.Id.Value
        };
    }

    /// <summary>
    /// Sección en planta: contorno exterior de la cara horizontal superior del sólido.
    /// Sirve para rectangulares, L, T, cruz, circulares (poligonizadas) e irregulares.
    /// </summary>
    private static Polygon2D? HorizontalSectionOutline(Solid solid)
    {
        PlanarFace? best = null;
        var bestZ = double.MinValue;
        foreach (Face f in solid.Faces)
        {
            if (f is not PlanarFace pf) continue;
            if (Math.Abs(pf.FaceNormal.Z) < 0.99) continue;
            var z = pf.Origin.Z;
            if (z > bestZ) { bestZ = z; best = pf; }
        }
        if (best == null) return null;

        List<Vec2>? outer = null;
        var outerArea = 0.0;
        foreach (var loop in best.GetEdgesAsCurveLoops())
        {
            var pts = new List<Vec2>();
            foreach (var c in loop)
            {
                var tess = c.Tessellate();
                for (var i = 0; i < tess.Count - 1; i++) pts.Add(Units.ToMm2(tess[i]));
            }
            if (pts.Count < 3) continue;
            var area = Math.Abs(ShoelaceArea(pts));
            if (area > outerArea) { outerArea = area; outer = pts; }
        }
        return outer == null ? null : new Polygon2D(outer);
    }

    /// <summary>
    /// Armadura existente de la columna: barras verticales (longitudinales) y estribos. Se usa el
    /// diámetro del estribo para el núcleo confinado y se informa del resto.
    /// </summary>
    private ColumnRebarInfo? InferColumnBars(FamilyInstance column)
    {
        RebarHostData? host;
        try { host = RebarHostData.GetRebarHostData(column); }
        catch { return null; }
        if (host == null) return null;

        var sets = 0;
        var longCount = 0;
        double longDb = 0, tieDb = 0;
        var positions = new List<Vec2>();
        foreach (var rebar in host.GetRebarsInHost())
        {
            sets++;
            if (_doc.GetElement(rebar.GetTypeId()) is not RebarBarType bt) continue;
            var db = Units.ToMm(bt.BarNominalDiameter);
            var isTie = _doc.GetElement(rebar.GetShapeId()) is RebarShape shape && shape.RebarStyle == RebarStyle.StirrupTie;
            if (!isTie)
            {
                IList<Curve> curves;
                try { curves = rebar.GetCenterlineCurves(false, true, true, MultiplanarOption.IncludeOnlyPlanarCurves, 0); }
                catch { continue; }
                var main = curves.OfType<Line>().OrderByDescending(l => l.Length).FirstOrDefault();
                if (main == null) continue;
                if (Math.Abs(main.Direction.Z) > 0.95)
                {
                    var n = Math.Max(1, rebar.NumberOfBarPositions);
                    longCount += n;
                    longDb = Math.Max(longDb, db);
                    // Posición en planta de cada barra del conjunto (la primera más la traslación de cada posición).
                    var p0 = main.Evaluate(0.5, true);
                    for (var i = 0; i < n; i++)
                    {
                        try
                        {
                            if (!rebar.DoesBarExistAtPosition(i)) continue;
                            var t = i == 0 || !rebar.IsRebarShapeDriven() ? Transform.Identity : rebar.GetShapeDrivenAccessor().GetBarPositionTransform(i);
                            positions.Add(Units.ToMm2(t.OfPoint(p0)));
                        }
                        catch (Autodesk.Revit.Exceptions.ApplicationException)
                        {
                            // posición no disponible: se omite
                        }
                    }
                    continue;
                }
                // Horizontal con estilo estándar: estribo abierto, grapa o barra de cosido.
                isTie = Math.Abs(main.Direction.Z) < 0.1;
            }
            if (isTie) tieDb = Math.Max(tieDb, db);
        }
        if (sets == 0) return null;
        var info = new ColumnRebarInfo(longCount, longDb, tieDb, sets) { Positions = positions };
        Notes.Add($"{ElementLabel(column)}: armadura en el modelo: {info.Describe()}.");
        return info;
    }

    private static double ShoelaceArea(List<Vec2> pts)
    {
        double a = 0;
        for (var i = 0; i < pts.Count; i++) a += pts[i].Cross(pts[(i + 1) % pts.Count]);
        return a / 2;
    }

    // ------------------------------------------------------------------ Vigas

    /// <summary>Vigas cuya caja envolvente toca la de la columna (ampliada por BeamSearchDistance).</summary>
    public List<FamilyInstance> FindBeamsNear(FamilyInstance column)
    {
        var bb = column.get_BoundingBox(null);
        if (bb == null) return new List<FamilyInstance>();
        var tol = Units.ToFt(_settings.BeamSearchDistanceMm);
        var outline = new Outline(bb.Min - new XYZ(tol, tol, tol), bb.Max + new XYZ(tol, tol, tol));
        var filter = new BoundingBoxIntersectsFilter(outline);
        return new FilteredElementCollector(_doc)
            .OfCategory(BuiltInCategory.OST_StructuralFraming)
            .WhereElementIsNotElementType()
            .WherePasses(filter)
            .OfType<FamilyInstance>()
            .Where(b => b.StructuralType == StructuralType.Beam || b.StructuralType == StructuralType.Brace)
            .ToList();
    }

    public BeamSection? ExtractBeam(FamilyInstance beam) => ExtractBeam(beam, out _);

    /// <param name="modelBars">Barras longitudinales encontradas en la viga (null si no tiene).</param>
    public BeamSection? ExtractBeam(FamilyInstance beam, out (double TopDb, int TopN, double BottomDb, int BottomN)? modelBars)
    {
        modelBars = null;
        if (beam.Location is not LocationCurve lc || lc.Curve is not Line line)
        {
            Notes.Add($"{ElementLabel(beam)}: la viga no es recta; se omite.");
            return null;
        }

        var solid = LargestSolid(beam);
        if (solid == null)
        {
            Notes.Add($"{ElementLabel(beam)}: la viga no tiene geometría sólida; se omite.");
            return null;
        }

        var p0 = line.GetEndPoint(0);
        var p1 = line.GetEndPoint(1);
        var axis = (p1 - p0);
        var axisXy = new XYZ(axis.X, axis.Y, 0);
        if (axisXy.GetLength() < 1e-6)
        {
            Notes.Add($"{ElementLabel(beam)}: la viga es vertical; se omite.");
            return null;
        }
        var u = axisXy.Normalize();
        var v = new XYZ(-u.Y, u.X, 0);

        // Extensión real del sólido: ancho a lo largo de v, peralte y cota superior en Z.
        double vMin = double.MaxValue, vMax = double.MinValue, zMin = double.MaxValue, zMax = double.MinValue;
        foreach (Edge e in solid.Edges)
        {
            foreach (var p in e.Tessellate())
            {
                var dv = p.DotProduct(v);
                vMin = Math.Min(vMin, dv); vMax = Math.Max(vMax, dv);
                zMin = Math.Min(zMin, p.Z); zMax = Math.Max(zMax, p.Z);
            }
        }
        var width = Units.ToMm(vMax - vMin);
        var depth = Units.ToMm(zMax - zMin);

        // Desplazamiento lateral entre la línea de ubicación y el centro real del sólido.
        var centerV = (vMin + vMax) / 2;
        var shift = v * (centerV - p0.DotProduct(v));
        var start = Units.ToMm2(p0 + shift);
        var end = Units.ToMm2(p1 + shift);

        var cover = CoverMm(beam) ?? _settings.BeamCoverMm;
        modelBars = InferBars(beam, u, zMin, zMax);

        return new BeamSection(ElementLabel(beam), start, end, width, depth)
        {
            TopElevation = Units.ToMm(zMax),
            Cover = cover,
            StirrupDiameter = _settings.BeamStirrupDiameterMm,
            TopBarDiameter = modelBars?.TopDb ?? 16,
            TopBarCount = modelBars?.TopN ?? _settings.TopBarCount,
            BottomBarDiameter = modelBars?.BottomDb ?? 16,
            BottomBarCount = modelBars?.BottomN ?? _settings.BottomBarCount,
            SourceId = beam.Id.Value
        };
    }

    private (double TopDb, int TopN, double BottomDb, int BottomN)? InferBars(FamilyInstance beam, XYZ u, double zMin, double zMax)
    {
        var host = RebarHostData.GetRebarHostData(beam);
        if (host == null) return null;

        var zMid = (zMin + zMax) / 2;
        var top = new List<(double Db, int N)>();
        var bottom = new List<(double Db, int N)>();
        foreach (var rebar in host.GetRebarsInHost())
        {
            if (_doc.GetElement(rebar.GetShapeId()) is RebarShape shape && shape.RebarStyle == RebarStyle.StirrupTie) continue;
            if (_doc.GetElement(rebar.GetTypeId()) is not RebarBarType bt) continue;
            IList<Curve> curves;
            try { curves = rebar.GetCenterlineCurves(false, true, true, MultiplanarOption.IncludeOnlyPlanarCurves, 0); }
            catch { continue; }
            var main = curves.OfType<Line>().OrderByDescending(l => l.Length).FirstOrDefault();
            if (main == null) continue;
            var d = main.Direction;
            if (Math.Abs(d.DotProduct(u)) < 0.95) continue; // no es longitudinal
            var z = (main.GetEndPoint(0).Z + main.GetEndPoint(1).Z) / 2;
            var entry = (Units.ToMm(bt.BarNominalDiameter), rebar.NumberOfBarPositions);
            if (z >= zMid) top.Add(entry); else bottom.Add(entry);
        }

        if (top.Count == 0 && bottom.Count == 0) return null;
        var result = (
            TopDb: top.Count > 0 ? top.Max(t => t.Db) : 0,
            TopN: top.Count > 0 ? top.Sum(t => t.N) : 0,
            BottomDb: bottom.Count > 0 ? bottom.Max(t => t.Db) : 0,
            BottomN: bottom.Count > 0 ? bottom.Sum(t => t.N) : 0);
        Notes.Add($"{ElementLabel(beam)}: barras en el modelo: sup. {result.TopN}Ø{result.TopDb:0}, inf. {result.BottomN}Ø{result.BottomDb:0}.");
        return result;
    }

    // ------------------------------------------------------------------ Utilidades

    public static Solid? LargestSolid(Element element)
    {
        var options = new Options { DetailLevel = ViewDetailLevel.Fine, ComputeReferences = false, IncludeNonVisibleObjects = false };
        GeometryElement? geometry = null;
        // Si el elemento está unido a otros (vigas, losas) la geometría visible tiene recortes; la
        // original de la familia es la sección completa, que es la que atraviesan las barras.
        // OJO: GetOriginalGeometry devuelve la geometría en el sistema de coordenadas de la FAMILIA
        // (sin la posición ni el giro de la instancia); hay que transformarla al proyecto.
        if (element is FamilyInstance fi)
        {
            try
            {
                var original = fi.GetOriginalGeometry(options);
                geometry = original?.GetTransformed(fi.GetTransform());
            }
            catch
            {
                geometry = null;
            }
        }
        geometry ??= element.get_Geometry(options);
        if (geometry == null) return null;
        Solid? best = null;
        foreach (var obj in geometry)
        {
            foreach (var s in SolidsOf(obj))
                if (s.Volume > 1e-9 && (best == null || s.Volume > best.Volume)) best = s;
        }
        return best;
    }

    private static IEnumerable<Solid> SolidsOf(GeometryObject obj)
    {
        switch (obj)
        {
            case Solid s:
                yield return s;
                break;
            case GeometryInstance gi:
                foreach (var o in gi.GetInstanceGeometry())
                    foreach (var s in SolidsOf(o)) yield return s;
                break;
        }
    }

    private static double? CoverMm(Element host)
    {
        try
        {
            var data = RebarHostData.GetRebarHostData(host);
            var cover = data?.GetCommonCoverType();
            return cover == null ? null : Units.ToMm(cover.CoverDistance);
        }
        catch
        {
            return null;
        }
    }

    public static string ElementLabel(Element e)
    {
        var mark = e.get_Parameter(BuiltInParameter.ALL_MODEL_MARK)?.AsString();
        if (!string.IsNullOrWhiteSpace(mark)) return mark!;
        var type = e.Document.GetElement(e.GetTypeId());
        var typeName = type?.Name ?? e.Name;
        return $"{typeName} [{e.Id.Value}]";
    }
}
