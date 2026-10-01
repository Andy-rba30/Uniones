using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using UnionesAcero.Revit.Settings;
using UnionesAcero.Revit.UI;

namespace UnionesAcero.Revit.Commands;

[Transaction(TransactionMode.ReadOnly)]
[Regeneration(RegenerationOption.Manual)]
public sealed class ConfiguracionCommand : IExternalCommand
{
    public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
    {
        var settings = PluginSettings.Load();
        var window = new SettingsWindow(settings, commandData.Application.MainWindowHandle);
        window.ShowDialog();
        return Result.Succeeded;
    }
}
