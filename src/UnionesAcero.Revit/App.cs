using System.Reflection;
using Autodesk.Revit.UI;

namespace UnionesAcero.Revit;

/// <summary>Punto de entrada: crea la pestaña "Uniones Acero" en la cinta de Revit.</summary>
public sealed class App : IExternalApplication
{
    public const string TabName = "Uniones Acero";

    public Result OnStartup(UIControlledApplication application)
    {
        try
        {
            application.CreateRibbonTab(TabName);
        }
        catch (Autodesk.Revit.Exceptions.ArgumentException)
        {
            // La pestaña ya existe (recarga del complemento).
        }

        var panel = application.CreateRibbonPanel(TabName, "Nudos viga-columna");
        var assembly = Assembly.GetExecutingAssembly().Location;

        AddButton(panel, "Analizar", "Analizar\nnudo", typeof(Commands.AnalizarNudoCommand), assembly,
            "Selecciona una columna de cualquier sección, detecta las vigas que llegan y calcula el anclaje de sus barras.");
        AddButton(panel, "Generar", "Generar\narmado", typeof(Commands.GenerarArmadoCommand), assembly,
            "Crea en el modelo las barras longitudinales de cada viga con el anclaje (recto o gancho) que corresponde en la columna.");
        AddButton(panel, "Verificar", "Verificar\narmado", typeof(Commands.VerificarArmadoCommand), assembly,
            "Revisa las barras ya modeladas en las vigas y marca en rojo las que no cumplen la longitud de anclaje.");
        panel.AddSeparator();
        AddButton(panel, "Configuracion", "Configuración", typeof(Commands.ConfiguracionCommand), assembly,
            "Normativa, materiales, recubrimientos y barras por defecto.");

        return Result.Succeeded;
    }

    public Result OnShutdown(UIControlledApplication application) => Result.Succeeded;

    private static void AddButton(RibbonPanel panel, string name, string text, Type command, string assembly, string tooltip)
    {
        var data = new PushButtonData("UnionesAcero_" + name, text, assembly, command.FullName!)
        {
            ToolTip = tooltip
        };
        panel.AddItem(data);
    }
}
