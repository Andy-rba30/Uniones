using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Autodesk.Revit.UI.Selection;
using UnionesAcero.Core.Analysis;
using UnionesAcero.Core.Model;
using UnionesAcero.Revit.Selection;
using UnionesAcero.Revit.Settings;

namespace UnionesAcero.Revit.Services;

/// <summary>Flujo común de los comandos: elegir columna, leer geometría, analizar.</summary>
public sealed class JointSession
{
    public required UIDocument UiDoc { get; init; }
    public required PluginSettings Settings { get; init; }
    public required FamilyInstance Column { get; init; }
    public required ColumnSection ColumnSection { get; init; }
    public required List<(FamilyInstance Element, BeamSection Section)> Beams { get; init; }
    public required JointAnalysis Analysis { get; init; }
    public required List<string> Notes { get; init; }

    public Document Doc => UiDoc.Document;

    /// <summary>Pide al usuario una columna y analiza su nudo. Devuelve null si cancela.</summary>
    public static JointSession? Start(UIDocument uidoc, PluginSettings settings)
    {
        Reference pick;
        try
        {
            pick = uidoc.Selection.PickObject(ObjectType.Element, new ColumnSelectionFilter(),
                "Seleccione la columna del nudo (Esc para cancelar)");
        }
        catch (Autodesk.Revit.Exceptions.OperationCanceledException)
        {
            return null;
        }

        var doc = uidoc.Document;
        var column = (FamilyInstance)doc.GetElement(pick);
        var extractor = new RevitGeometryExtractor(doc, settings);
        var columnSection = extractor.ExtractColumn(column);

        var beams = new List<(FamilyInstance, BeamSection)>();
        foreach (var b in extractor.FindBeamsNear(column))
        {
            var section = extractor.ExtractBeam(b);
            if (section != null) beams.Add((b, section));
        }

        var analysis = new JointAnalyzer(settings.ToJointOptions()).Analyze(columnSection, beams.Select(b => b.Item2));

        return new JointSession
        {
            UiDoc = uidoc,
            Settings = settings,
            Column = column,
            ColumnSection = columnSection,
            Beams = beams,
            Analysis = analysis,
            Notes = extractor.Notes
        };
    }

    public FamilyInstance? BeamElement(long? sourceId)
        => Beams.FirstOrDefault(b => b.Section.SourceId == sourceId).Element;
}
