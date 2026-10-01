using System.Reflection;
using Autodesk.Revit.UI;
using UnionesAcero.Revit.Ribbon;

namespace UnionesAcero.Revit;

/// <summary>Añade el botón "Nudos" al desplegable "Acero" del panel "Acero" de la pestaña "ARBA".</summary>
public sealed class App : IExternalApplication
{
    public Result OnStartup(UIControlledApplication application)
    {
        try
        {
            ArbaRibbon.Ensure(application);
            var assembly = Assembly.GetExecutingAssembly().Location;
            var data = new PushButtonData("ARBA_Acero_Nudos", "Nudos", assembly, typeof(Commands.NudosCommand).FullName!)
            {
                ToolTip = "Detalla el anclaje de las barras de las vigas en columnas de cualquier sección (rectangular, L, T, cruz...)",
                LongDescription = "Selecciona una o varias columnas estructurales y pulsa el botón. Se abre una ventana con la " +
                                  "planta del nudo y el alzado de cada viga: ahí eliges normativa, barras, ganchos y recubrimientos, " +
                                  "ves si el anclaje es recto, con gancho o insuficiente, y solo entonces se crean las barras.",
                LargeImage = ArbaRibbon.IconNudo(32),
                Image = ArbaRibbon.IconNudo(16)
            };
            ArbaRibbon.AddToPulldown(application, ArbaRibbon.PanelAceroName, ArbaRibbon.PanelAceroName, data);
            return Result.Succeeded;
        }
        catch (Exception ex)
        {
            TaskDialog.Show("ARBA", "No se pudo añadir el botón Nudos a la cinta: " + ex.Message +
                                    "\nEl comando sigue disponible en Complementos > Herramientas externas.");
            return Result.Failed;
        }
    }

    public Result OnShutdown(UIControlledApplication application) => Result.Succeeded;
}
