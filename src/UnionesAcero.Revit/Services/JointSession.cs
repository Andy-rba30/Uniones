using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Structure;
using UnionesAcero.Core.Analysis;
using UnionesAcero.Core.Geometry;
using UnionesAcero.Core.Model;
using UnionesAcero.Core.Verification;
using UnionesAcero.Revit.Settings;

namespace UnionesAcero.Revit.Services;

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

    /// <summary>Elecciones propias de esta viga (vacío / 0 = valores generales).</summary>
    public string TopBarTypeName { get; set; } = "";
    public int TopBarCount { get; set; }
    public string BottomBarTypeName { get; set; } = "";
    public int BottomBarCount { get; set; }

    /// <summary>Barras existentes en el modelo para verificar.</summary>
    public List<(Rebar Rebar, ExistingBar Bar)> ExistingBars { get; } = new();

    /// <summary>Resultado de la última verificación de las barras existentes.</summary>
    public List<BarCheckResult> Checks { get; set; } = new();

    public BeamJoint? Joint { get; set; }

    public string Label => Name;
}

/// <summary>Una columna seleccionada: sección deducida (o motivo del rechazo), sus vigas y el análisis vigente.</summary>
public sealed class ColumnItem
{
    public required FamilyInstance Element { get; init; }
    public required string Tag { get; init; }
    public ColumnSection? Section { get; init; }
    public string? Error { get; init; }
    public List<BeamItem> Beams { get; } = new();
    public List<string> Notes { get; } = new();
    public JointAnalysis? Analysis { get; set; }
    public Solid? Solid { get; init; }

    public bool CanBuild => Error == null && Section != null && Beams.Count > 0;

    public string Kind => Error != null ? "SIN ARMAR" : Section == null ? "?" : "Columna " + ShapeName(Section.Outline);

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
        return $"{(max.X - min.X):0} x {(max.Y - min.Y):0} mm, {Beams.Count} viga(s)";
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
        Solid? solid;
        try
        {
            var host = RebarHostData.GetRebarHostData(column);
            if (host == null || !host.IsValidHost())
                return new ColumnItem { Element = column, Tag = tag, Error = "no admite armadura. Revisa que el material sea hormigón y que sea un pilar estructural." };
            solid = RevitGeometryExtractor.LargestSolid(column);
            section = extractor.ExtractColumn(column);
        }
        catch (Exception ex)
        {
            return new ColumnItem { Element = column, Tag = tag, Error = "RECHAZADO, " + ex.Message };
        }

        var item = new ColumnItem { Element = column, Tag = tag, Section = section, Solid = solid };
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
    /// Vuelve a construir las vigas con las elecciones de la ventana y ejecuta el análisis.
    /// Devuelve null si la columna no es armable.
    /// </summary>
    public static JointAnalysis? Recompute(ColumnItem item, PluginSettings settings, Func<string, double> diameterOf)
    {
        if (item.Section == null) return null;
        var options = settings.ToJointOptions();
        var column = new ColumnSection(item.Section.Name, item.Section.Outline)
        {
            Cover = item.Section.Cover > 0 ? item.Section.Cover : settings.ColumnCoverMm,
            TieDiameter = settings.ColumnTieDiameterMm,
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
                if (topDb <= 0) topDb = m.TopDb;
                if (botDb <= 0) botDb = m.BottomDb;
                if (b.TopBarCount <= 0) topN = m.TopN;
                if (b.BottomBarCount <= 0) botN = m.BottomN;
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
                SourceId = b.Base.SourceId
            });
        }

        var analysis = new JointAnalyzer(options).Analyze(column, beams);
        item.Analysis = analysis;
        foreach (var b in item.Beams)
        {
            b.Joint = analysis.Joints.FirstOrDefault(j => j.Beam.SourceId == b.Base.SourceId);
            b.Checks = b.Joint != null && b.ExistingBars.Count > 0
                ? new ExistingBarChecker(options).Check(analysis, b.ExistingBars.Select(e => e.Bar))
                : new List<BarCheckResult>();
        }
        return analysis;
    }
}
