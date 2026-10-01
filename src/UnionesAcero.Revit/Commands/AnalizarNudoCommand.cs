using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using UnionesAcero.Core.Reporting;
using UnionesAcero.Revit.Services;
using UnionesAcero.Revit.Settings;
using UnionesAcero.Revit.UI;

namespace UnionesAcero.Revit.Commands;

/// <summary>Analiza el nudo de una columna y muestra el informe sin modificar el modelo.</summary>
[Transaction(TransactionMode.Manual)]
[Regeneration(RegenerationOption.Manual)]
public sealed class AnalizarNudoCommand : IExternalCommand
{
    public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
    {
        var uiapp = commandData.Application;
        try
        {
            var session = JointSession.Start(uiapp.ActiveUIDocument, PluginSettings.Load());
            if (session == null) return Result.Cancelled;

            var report = ReportFormatter.Detailed(session.Analysis);
            if (session.Notes.Count > 0)
                report += Environment.NewLine + "Notas del modelo:" + Environment.NewLine + string.Join(Environment.NewLine, session.Notes.Select(n => "  - " + n));

            ReportWindow.Show("Análisis del nudo – " + session.ColumnSection.Name, report, uiapp.MainWindowHandle);
            return Result.Succeeded;
        }
        catch (Exception ex)
        {
            message = ex.Message;
            TaskDialog.Show("Uniones Acero", "No se pudo analizar el nudo:\n" + ex.Message);
            return Result.Failed;
        }
    }
}
