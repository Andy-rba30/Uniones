using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using UnionesAcero.Core.Analysis;
using UnionesAcero.Core.Reporting;
using UnionesAcero.Revit.Services;
using UnionesAcero.Revit.Settings;
using UnionesAcero.Revit.UI;

namespace UnionesAcero.Revit.Commands;

/// <summary>Genera en el modelo las barras longitudinales de cada viga con su anclaje en la columna.</summary>
[Transaction(TransactionMode.Manual)]
[Regeneration(RegenerationOption.Manual)]
public sealed class GenerarArmadoCommand : IExternalCommand
{
    public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
    {
        var uiapp = commandData.Application;
        try
        {
            var session = JointSession.Start(uiapp.ActiveUIDocument, PluginSettings.Load());
            if (session == null) return Result.Cancelled;

            var analysis = session.Analysis;
            var groups = analysis.BarGroups.ToList();
            if (groups.Count == 0)
            {
                TaskDialog.Show("Uniones Acero", "No hay barras que generar.\n\n" + ReportFormatter.Detailed(analysis));
                return Result.Succeeded;
            }

            if (analysis.HasErrors)
            {
                var dlg = new TaskDialog("Uniones Acero")
                {
                    MainInstruction = "El nudo tiene errores de anclaje.",
                    MainContent = ReportFormatter.Summary(analysis) +
                                  "\n\nLas barras marcadas como INSUFICIENTE se generan con la geometría disponible para que puedas revisarlas, pero no cumplen la norma.",
                    CommonButtons = TaskDialogCommonButtons.Yes | TaskDialogCommonButtons.No,
                    DefaultButton = TaskDialogResult.No
                };
                dlg.AddCommandLink(TaskDialogCommandLinkId.CommandLink1, "Generar de todos modos");
                dlg.AddCommandLink(TaskDialogCommandLinkId.CommandLink2, "Ver el informe y cancelar");
                var r = dlg.Show();
                if (r != TaskDialogResult.CommandLink1 && r != TaskDialogResult.Yes)
                {
                    ReportWindow.Show("Análisis del nudo – " + session.ColumnSection.Name, ReportFormatter.Detailed(analysis), uiapp.MainWindowHandle);
                    return Result.Cancelled;
                }
            }

            var builder = new RebarBuilder(session.Doc);
            if (!builder.HasBarTypes)
            {
                TaskDialog.Show("Uniones Acero", "El proyecto no tiene tipos de barra de armadura. Carga una plantilla estructural o crea un tipo de barra primero.");
                return Result.Failed;
            }

            var created = new List<ElementId>();
            using (var t = new Transaction(session.Doc, "Uniones Acero: generar armado de nudo"))
            {
                t.Start();
                foreach (var g in groups)
                {
                    var host = session.BeamElement(g.BeamSourceId) ?? session.Column;
                    var rebar = builder.Create(g, host);
                    if (rebar != null) created.Add(rebar.Id);
                }
                if (created.Count == 0)
                {
                    t.RollBack();
                }
                else
                {
                    t.Commit();
                }
            }

            if (created.Count > 0) session.UiDoc.Selection.SetElementIds(created);

            var report = $"Barras creadas: {created.Count} de {groups.Count} grupo(s).\n\n" +
                         string.Join("\n", groups.Select(g => $"  {g.Label}: {JointAnalyzer.DecisionName(g.Decision)}, longitud {g.TotalLength:0} mm" +
                                                               (g.EndHook != null ? $", gancho {(int)g.EndHook.Angle}° de {g.EndHook.Extension:0} mm" : "")));
            if (builder.Notes.Count > 0)
                report += "\n\nNotas:\n" + string.Join("\n", builder.Notes.Select(n => "  - " + n));
            if (session.Notes.Count > 0)
                report += "\n\nNotas del modelo:\n" + string.Join("\n", session.Notes.Select(n => "  - " + n));
            report += "\n\n" + ReportFormatter.Detailed(analysis);

            ReportWindow.Show("Armado generado – " + session.ColumnSection.Name, report, uiapp.MainWindowHandle);
            return created.Count > 0 ? Result.Succeeded : Result.Failed;
        }
        catch (Exception ex)
        {
            message = ex.Message;
            TaskDialog.Show("Uniones Acero", "No se pudo generar el armado:\n" + ex.Message);
            return Result.Failed;
        }
    }
}
