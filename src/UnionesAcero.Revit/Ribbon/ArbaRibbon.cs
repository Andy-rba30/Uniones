using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Autodesk.Revit.UI;

namespace UnionesAcero.Revit.Ribbon;

/// <summary>
/// Gestión compartida de la pestaña "ARBA" y sus paneles (IA, Acero, Encofrado), igual que en
/// los add-ins de columnas y muros: el mismo orden de paneles sin importar cuál cargue primero,
/// y los botones de acero dentro del desplegable "Acero".
/// </summary>
public static class ArbaRibbon
{
    public const string TabName = "ARBA";
    public const string PanelIaName = "IA";
    public const string PanelAceroName = "Acero";
    public const string PanelEncofradoName = "Encofrado";

    private static readonly string[] OrderedPanels = { PanelIaName, PanelAceroName, PanelEncofradoName };

    public static void Ensure(UIControlledApplication app)
    {
        try { app.CreateRibbonTab(TabName); }
        catch (Exception) { /* ya creada por otro add-in */ }

        var existing = app.GetRibbonPanels(TabName);
        foreach (var panelName in OrderedPanels)
        {
            var exists = existing != null && existing.Any(p => string.Equals(p.Name, panelName, StringComparison.OrdinalIgnoreCase));
            if (!exists)
            {
                var panel = app.CreateRibbonPanel(TabName, panelName);
                panel.Visible = false;
            }
        }
    }

    public static RibbonPanel GetPanel(UIControlledApplication app, string panelName)
    {
        var existing = app.GetRibbonPanels(TabName);
        var found = existing?.FirstOrDefault(p => string.Equals(p.Name, panelName, StringComparison.OrdinalIgnoreCase));
        if (found != null) return found;
        var created = app.CreateRibbonPanel(TabName, panelName);
        created.Visible = false;
        return created;
    }

    public static void AddToPulldown(UIControlledApplication app, string panelName, string pulldownName, PushButtonData data)
    {
        var panel = GetPanel(app, panelName);
        var pulldown = panel.GetItems()?.OfType<PulldownButton>()
            .FirstOrDefault(pb => string.Equals(pb.Name, pulldownName, StringComparison.OrdinalIgnoreCase));

        if (pulldown == null)
        {
            var pbData = new PulldownButtonData(pulldownName, pulldownName);
            if (string.Equals(pulldownName, PanelAceroName, StringComparison.OrdinalIgnoreCase))
            {
                pbData.ToolTip = "Herramientas de armado de acero";
                pbData.LargeImage = IconAcero(32);
                pbData.Image = IconAcero(16);
            }
            pulldown = panel.AddItem(pbData) as PulldownButton;
        }

        pulldown?.AddPushButton(data);
        panel.Visible = true;
    }

    /// <summary>Icono del desplegable Acero (sección en L con estribos y barras), el mismo que en Columnas.</summary>
    public static BitmapSource IconAcero(int size)
    {
        var s = size / 32.0;
        var visual = new DrawingVisual();
        using (var dc = visual.RenderOpen())
        {
            var concrete = new SolidColorBrush(Color.FromRgb(0xD9, 0xD9, 0xD9));
            var edge = new Pen(new SolidColorBrush(Color.FromRgb(0x55, 0x55, 0x55)), 1.2 * s);
            var stirrup1 = new Pen(new SolidColorBrush(Color.FromRgb(0x1F, 0x7A, 0x7A)), 1.6 * s) { LineJoin = PenLineJoin.Round };
            var stirrup2 = new Pen(new SolidColorBrush(Color.FromRgb(0xE0, 0x8A, 0x2E)), 1.6 * s) { LineJoin = PenLineJoin.Round };
            var bar = new SolidColorBrush(Color.FromRgb(0x8B, 0x2E, 0x2E));

            var outline = new StreamGeometry();
            using (var g = outline.Open())
            {
                g.BeginFigure(new Point(2 * s, 2 * s), true, true);
                g.LineTo(new Point(30 * s, 2 * s), true, false);
                g.LineTo(new Point(30 * s, 14 * s), true, false);
                g.LineTo(new Point(14 * s, 14 * s), true, false);
                g.LineTo(new Point(14 * s, 30 * s), true, false);
                g.LineTo(new Point(2 * s, 30 * s), true, false);
            }
            dc.DrawGeometry(concrete, edge, outline);
            dc.DrawRectangle(null, stirrup1, new Rect(5 * s, 5 * s, 22 * s, 6 * s));
            dc.DrawRectangle(null, stirrup2, new Rect(5 * s, 5 * s, 6 * s, 22 * s));
            var rr = 1.7 * s;
            foreach (var p in new[]
                     {
                         new Point(5 * s, 5 * s), new Point(27 * s, 5 * s), new Point(27 * s, 11 * s),
                         new Point(11 * s, 11 * s), new Point(11 * s, 27 * s), new Point(5 * s, 27 * s), new Point(5 * s, 11 * s)
                     })
                dc.DrawEllipse(bar, null, p, rr, rr);
        }
        return Render(visual, size);
    }

    /// <summary>Icono del botón Nudos: columna en planta con dos vigas y sus barras con gancho.</summary>
    public static BitmapSource IconNudo(int size)
    {
        var s = size / 32.0;
        var visual = new DrawingVisual();
        using (var dc = visual.RenderOpen())
        {
            var concrete = new SolidColorBrush(Color.FromRgb(0xD9, 0xD9, 0xD9));
            var beam = new SolidColorBrush(Color.FromRgb(0xBF, 0xBF, 0xBF));
            var edge = new Pen(new SolidColorBrush(Color.FromRgb(0x55, 0x55, 0x55)), 1.2 * s);
            var bar = new Pen(new SolidColorBrush(Color.FromRgb(0x8B, 0x2E, 0x2E)), 1.8 * s) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round };
            var hook = new SolidColorBrush(Color.FromRgb(0x8B, 0x2E, 0x2E));

            dc.DrawRectangle(beam, edge, new Rect(1 * s, 12 * s, 12 * s, 8 * s));
            dc.DrawRectangle(beam, edge, new Rect(12 * s, 19 * s, 8 * s, 12 * s));
            dc.DrawRectangle(concrete, edge, new Rect(12 * s, 6 * s, 14 * s, 14 * s));
            foreach (var y in new[] { 14.0, 18.0 })
            {
                dc.DrawLine(bar, new Point(2 * s, y * s), new Point(23 * s, y * s));
                dc.DrawEllipse(hook, null, new Point(23 * s, y * s), 1.6 * s, 1.6 * s);
            }
            foreach (var x in new[] { 14.5, 18.0 })
            {
                dc.DrawLine(bar, new Point(x * s, 30 * s), new Point(x * s, 9 * s));
                dc.DrawEllipse(hook, null, new Point(x * s, 9 * s), 1.6 * s, 1.6 * s);
            }
        }
        return Render(visual, size);
    }

    private static BitmapSource Render(DrawingVisual visual, int size)
    {
        var bmp = new RenderTargetBitmap(size, size, 96, 96, PixelFormats.Pbgra32);
        bmp.Render(visual);
        bmp.Freeze();
        return bmp;
    }
}
