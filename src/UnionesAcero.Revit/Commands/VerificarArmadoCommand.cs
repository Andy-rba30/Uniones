using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using UnionesAcero.Core.Reporting;
using UnionesAcero.Core.Verification;
using UnionesAcero.Revit.Services;
using UnionesAcero.Revit.Settings;
using UnionesAcero.Revit.UI;

namespace UnionesAcero.Revit.Commands;

/// <summary>
/// Verifica las barras ya modeladas en las vigas que llegan a la columna seleccionada y
/// resalta en rojo (en la vista activa) las que no cumplen el anclaje.
/// </summary>
[Transaction(TransactionMode.Manual)]
[Regeneration(RegenerationOption.Manual)]
public sealed class VerificarArmadoCommand : IExternalCommand
{
    public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
    {
        var uiapp = commandData.Application;
        try
        {
            var settings = PluginSettings.Load();
            var session = JointSession.Start(uiapp.ActiveUIDocument, settings);
            if (session == null) return Result.Cancelled;

            var bars = new List<(Autodesk.Revit.DB.Structure.Rebar Rebar, ExistingBar Bar)>();
            foreach (var (element, section) in session.Beams)
                bars.AddRange(RebarInspector.CollectBars(session.Doc, element, section.Name));

            if (bars.Count == 0)
            {
                TaskDialog.Show("Uniones Acero", "Las vigas que llegan a esta columna no tienen barras modeladas.\n\n" + ReportFormatter.Summary(session.Analysis));
                return Result.Succeeded;
            }

            var checker = new ExistingBarChecker(settings.ToJointOptions());
            var results = checker.Check(session.Analysis, bars.Select(b => b.Bar));
            var byId = results.ToDictionary(r => r.Id);

            var failing = bars.Where(b => byId[b.Bar.Id].Status == CheckStatus.Fail).Select(b => b.Rebar.Id).ToList();
            var passing = bars.Where(b => byId[b.Bar.Id].Status == CheckStatus.Ok).Select(b => b.Rebar.Id).ToList();

            var view = session.Doc.ActiveView;
            using (var t = new Transaction(session.Doc, "Uniones Acero: marcar barras"))
            {
                t.Start();
                var red = new OverrideGraphicSettings().SetProjectionLineColor(new Color(255, 0, 0)).SetProjectionLineWeight(6);
                var green = new OverrideGraphicSettings().SetProjectionLineColor(new Color(0, 160, 0));
                var solid = SolidFillPattern(session.Doc);
                if (solid != null)
                {
                    red.SetSurfaceForegroundPatternId(solid.Id).SetSurfaceForegroundPatternColor(new Color(255, 0, 0));
                    green.SetSurfaceForegroundPatternId(solid.Id).SetSurfaceForegroundPatternColor(new Color(0, 160, 0));
                }
                foreach (var id in failing) view.SetElementOverrides(id, red);
                foreach (var id in passing) view.SetElementOverrides(id, green);
                t.Commit();
            }

            if (failing.Count > 0) session.UiDoc.Selection.SetElementIds(failing);

            var report = ReportFormatter.Verification(results) + "\n\n" +
                         "Las barras que no cumplen quedan en rojo y las correctas en verde en la vista activa (reemplazos de gráficos por elemento).\n\n" +
                         ReportFormatter.Detailed(session.Analysis);
            ReportWindow.Show("Verificación de armado – " + session.ColumnSection.Name, report, uiapp.MainWindowHandle);
            return Result.Succeeded;
        }
        catch (Exception ex)
        {
            message = ex.Message;
            TaskDialog.Show("Uniones Acero", "No se pudo verificar el armado:\n" + ex.Message);
            return Result.Failed;
        }
    }

    private static FillPatternElement? SolidFillPattern(Document doc)
        => new FilteredElementCollector(doc).OfClass(typeof(FillPatternElement)).Cast<FillPatternElement>()
            .FirstOrDefault(f => f.GetFillPattern().IsSolidFill);
}
