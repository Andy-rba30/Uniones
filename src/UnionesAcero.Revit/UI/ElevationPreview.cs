using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using UnionesAcero.Core.Analysis;
using UnionesAcero.Core.Geometry;
using UnionesAcero.Core.Model;
using UnionesAcero.Core.Verification;
using UnionesAcero.Revit.Services;

namespace UnionesAcero.Revit.UI;

/// <summary>
/// Alzado por el eje de la viga seleccionada: la columna (con el límite del núcleo a trazos), la
/// viga, cada capa de barras con su gancho y las cotas de anclaje requerido y provisto. Las
/// barras ya modeladas se dibujan a trazos (verde cumple, rojo no).
/// </summary>
public sealed class ElevationPreview : Canvas
{
    private ColumnItem? _item;
    private BeamItem? _beam;
    private string _message = "Selecciona una viga en la lista o en la planta";

    public ElevationPreview()
    {
        Background = RevitTheme.Paper;
        ClipToBounds = true;
        SizeChanged += (_, _) => Redraw();
    }

    public void Show(ColumnItem item, BeamItem? beam)
    {
        _item = item; _beam = beam;
        Redraw();
    }

    public void Clear(string message)
    {
        _item = null; _beam = null; _message = message;
        Redraw();
    }

    private static string Mm(double v) => Math.Round(v).ToString(CultureInfo.InvariantCulture);

    private void Redraw()
    {
        Children.Clear();
        double W = ActualWidth, H = ActualHeight;
        if (W < 10 || H < 10) return;
        if (_item?.Section == null || _beam?.Joint is not { Connected: true } j)
        {
            Text(_beam != null && _beam.Joint != null && !_beam.Joint.Connected ? _beam.Name + ": no conectada a la columna" : _message, 10, 10, RevitTheme.Hint, 12);
            return;
        }

        var beam = j.Beam;
        var u = j.InwardDirection;
        var col = _item.Section;
        double Along(Vec2 p) => (p - j.ContactPoint).Dot(u);

        // extensión horizontal: desde el inicio de las barras en la viga hasta la salida de la columna (y la viga opuesta si la hay)
        var groups = j.Layers.Select(l => l.Group).Where(g => g != null).Select(g => g!).ToList();
        var xMin = groups.Select(g => g.FullCenterline.Min(p => Along(p.XY))).DefaultIfEmpty(-2 * beam.Depth).Min() - 150;
        var xMax = groups.Select(g => g.FullCenterline.Max(p => Along(p.XY))).DefaultIfEmpty(j.AvailableDepth).Max();
        xMax = Math.Max(xMax, j.AvailableDepth) + (j.OppositeBeam != null ? 150 : 100);
        var beamTop = beam.TopElevation; var beamBot = beamTop - beam.Depth;
        var hasRange = col.TopElevation > col.BottomElevation + 1;
        var zMax = Math.Min(hasRange ? col.TopElevation : beamTop + beam.Depth, beamTop + beam.Depth);
        var zMin = Math.Max(hasRange ? col.BottomElevation : beamBot - beam.Depth, beamBot - beam.Depth);
        var zTop = Math.Max(zMax, beamTop); var zBot = Math.Min(zMin, beamBot);
        foreach (var g in groups) { zTop = Math.Max(zTop, g.FullCenterline.Max(p => p.Z) + 60); zBot = Math.Min(zBot, g.FullCenterline.Min(p => p.Z) - 60); }

        const double margin = 30, labelW = 170;
        var k = Math.Min((W - 2 * margin - labelW) / Math.Max(xMax - xMin, 1), (H - 2 * margin - 24) / Math.Max(zTop - zBot, 1));
        var x0 = margin; var y0 = H - margin - 24;
        double X(double x) => x0 + (x - xMin) * k;
        double Y(double z) => y0 - (z - zBot) * k;

        // columna (desde la cara de llegada hasta la salida)
        var colRect = new Rectangle
        {
            Width = j.AvailableDepth * k, Height = (zMax - zMin) * k, Fill = PreviewColors.Concrete, Stroke = PreviewColors.Edge, StrokeThickness = 1.4,
            ToolTip = $"Columna {col.Name}: {j.AvailableDepth:0} mm en la dirección de la viga; útil {j.UsableDepth:0} mm"
        };
        SetLeft(colRect, X(0)); SetTop(colRect, Y(zMax));
        Children.Add(colRect);
        // límite del núcleo (cara interior del estribo lejano)
        Children.Add(new Line { X1 = X(j.UsableDepth), Y1 = Y(zMax), X2 = X(j.UsableDepth), Y2 = Y(zMin), Stroke = PreviewColors.Core, StrokeThickness = 1, StrokeDashArray = new DoubleCollection { 3, 3 } });
        Text("núcleo", X(j.UsableDepth) - 36, Y(zMax) + 2, PreviewColors.Dim, 9);

        // viga
        var beamLen = Math.Max(0, -xMin);
        var beamRect = new Rectangle { Width = beamLen * k, Height = beam.Depth * k, Fill = PreviewColors.Beam, Stroke = PreviewColors.Edge, StrokeThickness = 1.2, ToolTip = $"{beam.Name}: {beam.Width:0} x {beam.Depth:0} mm" };
        SetLeft(beamRect, X(xMin)); SetTop(beamRect, Y(beamTop));
        Children.Add(beamRect);
        Text(beam.Name, X(xMin) + 4, Y(beamTop) - 16, PreviewColors.Dim, 11, true);

        if (j.OppositeBeam != null)
        {
            var ob = j.OppositeBeam;
            var oRect = new Rectangle { Width = (xMax - j.AvailableDepth) * k, Height = ob.Depth * k, Fill = PreviewColors.Beam, Stroke = PreviewColors.Edge, StrokeThickness = 1.2 };
            SetLeft(oRect, X(j.AvailableDepth)); SetTop(oRect, Y(ob.TopElevation));
            Children.Add(oRect);
            Text(ob.Name, X(j.AvailableDepth) + 4, Y(ob.TopElevation) - 16, PreviewColors.Dim, 11, true);
        }

        // barras existentes
        foreach (var (_, bar) in _beam.ExistingBars)
        {
            var check = _beam.Checks.FirstOrDefault(c => c.Id == bar.Id);
            if (check == null || check.Status == CheckStatus.NotApplicable) continue;
            var brush = check.Status == CheckStatus.Ok ? PreviewColors.ExistingOk : PreviewColors.ExistingFail;
            for (var i = 0; i + 1 < bar.Centerline.Count; i++)
            {
                var a = bar.Centerline[i]; var c = bar.Centerline[i + 1];
                Children.Add(new Line
                {
                    X1 = X(Along(a.XY)), Y1 = Y(a.Z), X2 = X(Along(c.XY)), Y2 = Y(c.Z), Stroke = brush, StrokeThickness = 1.4,
                    StrokeDashArray = new DoubleCollection { 5, 3 }, ToolTip = "Barra existente " + bar.Id + ": " + check.Message
                });
            }
        }

        // barras nuevas con cotas
        var labelY = Y(zTop) + 2;
        foreach (var l in j.Layers)
        {
            var g = l.Group;
            if (g == null) continue;
            var brush = PreviewColors.ForGroup(g);
            var th = Math.Max(2, g.Diameter * k);
            var tip = g.Label + ": " + JointAnalyzer.DecisionName(g.Decision) + $", requerido {g.RequiredLength:0} / provisto {g.ProvidedLength:0} mm";
            for (var s = 0; s + 1 < g.FullCenterline.Count; s++)
            {
                var a = g.FullCenterline[s]; var c = g.FullCenterline[s + 1];
                Children.Add(new Line
                {
                    X1 = X(Along(a.XY)), Y1 = Y(a.Z), X2 = X(Along(c.XY)), Y2 = Y(c.Z), Stroke = brush, StrokeThickness = th,
                    StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round, StrokeLineJoin = PenLineJoin.Round, ToolTip = tip
                });
            }

            // cota del anclaje provisto (desde la cara) y del requerido
            if (g.Decision != AnchorageDecision.PassThrough)
            {
                var zDim = g.Layer == BarLayer.Top ? g.Elevation + 2.5 * g.Diameter + 40 : g.Elevation - 2.5 * g.Diameter - 40;
                Dimension(X(0), X(g.ProvidedLength), Y(zDim), brush, $"prov. {g.ProvidedLength:0}");
                var zReq = g.Layer == BarLayer.Top ? zDim + 60 : zDim - 60;
                var reqBrush = g.ProvidedLength + 0.5 >= g.RequiredLength ? PreviewColors.Dim : PreviewColors.Insufficient;
                Dimension(X(0), X(g.RequiredLength), Y(zReq), reqBrush, $"req. {(g.EndHook != null ? "ldh" : "ld")} {g.RequiredLength:0}");
            }

            // etiqueta de la capa
            var ok = g.Decision != AnchorageDecision.Insufficient;
            var label = $"{g.Label}: {JointAnalyzer.DecisionName(g.Decision)}" +
                        (g.EndHook != null ? $" (ext. {g.EndHook.Extension:0}, hacia {(g.EndHook.Direction.Z < 0 ? "abajo" : "arriba")})" : "") +
                        $" · L = {g.TotalLength:0} mm";
            Text(label, W - labelW - 4, labelY, ok ? brush : PreviewColors.Insufficient, 10, true).MaxWidth = labelW;
            labelY += 30;
        }

        foreach (var d in j.Diagnostics.Where(d => d.Severity != Severity.Info).Take(3))
        {
            Text((d.Severity == Severity.Error ? "✖ " : "⚠ ") + d.Message, W - labelW - 4, labelY, d.Severity == Severity.Error ? PreviewColors.Insufficient : PreviewColors.BottomBar, 9).MaxWidth = labelW;
            labelY += 36;
        }

        Text($"Alzado por el eje de {beam.Name} · cara de la columna en x = 0", 8, H - 18, PreviewColors.Dim, 10);
    }

    private void Dimension(double xa, double xb, double y, Brush brush, string text)
    {
        Children.Add(new Line { X1 = xa, Y1 = y, X2 = xb, Y2 = y, Stroke = brush, StrokeThickness = 1 });
        foreach (var x in new[] { xa, xb })
            Children.Add(new Line { X1 = x, Y1 = y - 4, X2 = x, Y2 = y + 4, Stroke = brush, StrokeThickness = 1 });
        Text(text, (xa + xb) / 2 - 20, y - 14, brush, 9);
    }

    private TextBlock Text(string s, double x, double y, Brush brush, double size, bool bold = false)
    {
        var t = new TextBlock { Text = s, Foreground = brush, FontSize = size, TextWrapping = TextWrapping.Wrap };
        if (bold) t.FontWeight = FontWeights.SemiBold;
        SetLeft(t, x); SetTop(t, y);
        Children.Add(t);
        return t;
    }
}
