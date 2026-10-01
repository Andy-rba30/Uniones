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
/// Alzado por el eje de la viga seleccionada: la viga a la izquierda, la columna a la derecha (con
/// el límite del núcleo a trazos), y por cada capa lo que se va a hacer: barras nuevas con su gancho
/// y sus cotas, barras existentes a trazos (verde cumple, rojo no) y barras corregidas en naranja.
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
        if (_item?.Section == null || _beam == null)
        {
            Text(_message, 10, 10, RevitTheme.Hint, 12);
            return;
        }
        if (!_beam.Include)
        {
            Text(_beam.Name + ": excluida del armado (marca la casilla de la tabla para incluirla).", 10, 10, RevitTheme.Hint, 12);
            return;
        }
        if (_beam.Joint is not { Connected: true } j)
        {
            var why = _beam.Joint?.Diagnostics.FirstOrDefault()?.Message ?? "sin análisis";
            var t = Text(_beam.Name + ": NO CONECTADA a la columna. " + why, 10, 10, PreviewColors.Insufficient, 12);
            t.MaxWidth = W - 20;
            Text("Barras en el modelo: " + _beam.ModelBarsText + ". Revisa que el extremo de la viga llegue a la cara de la columna (tolerancia 50 mm).", 10, 48, RevitTheme.Hint, 11).MaxWidth = W - 20;
            return;
        }

        var beam = j.Beam;
        var u = j.InwardDirection;
        var col = _item.Section;
        double Along(Vec2 p) => (p - j.ContactPoint).Dot(u);

        var groups = _beam.Plans.Where(p => p.Action == LayerAction.CreateNew && p.NewGroup != null).Select(p => p.NewGroup!).ToList();
        var fixes = _beam.Plans.SelectMany(p => p.Fixes).ToList();
        var existing = _beam.ExistingBars.Select(e => (e.Bar, Check: _beam.Checks.FirstOrDefault(c => c.Id == e.Bar.Id)))
            .Where(e => e.Check != null && e.Check.Status != CheckStatus.NotApplicable).ToList();
        var fixedIds = fixes.Select(f => f.Bar.Id).ToHashSet();

        // extensión horizontal: desde el inicio de las barras en la viga hasta la salida de la columna (y la viga opuesta si la hay)
        var allX = new List<double>();
        foreach (var g in groups) allX.AddRange(g.FullCenterline.Select(p => Along(p.XY)));
        foreach (var (_, _, f) in fixes) allX.AddRange(f.FullCenterline.Select(p => Along(p.XY)));
        foreach (var (bar, _) in existing) allX.AddRange(bar.Centerline.Select(p => Along(p.XY)));
        var xMin = (allX.Count > 0 ? allX.Min() : -2 * beam.Depth) - 150;
        xMin = Math.Max(xMin, -3.5 * beam.Depth - 600); // no alargar el dibujo con barras que recorren toda la viga
        var xMax = (allX.Count > 0 ? allX.Max() : j.AvailableDepth);
        xMax = Math.Max(xMax, j.AvailableDepth) + (j.OppositeBeam != null ? 150 : 100);
        xMax = Math.Min(xMax, j.AvailableDepth + 2 * beam.Depth + 300);

        var beamTop = beam.TopElevation; var beamBot = beamTop - beam.Depth;
        var hasRange = col.TopElevation > col.BottomElevation + 1;
        var zMax = Math.Min(hasRange ? col.TopElevation : beamTop + beam.Depth, beamTop + beam.Depth);
        var zMin = Math.Max(hasRange ? col.BottomElevation : beamBot - beam.Depth, beamBot - beam.Depth);
        var zTop = Math.Max(zMax, beamTop); var zBot = Math.Min(zMin, beamBot);
        foreach (var g in groups) { zTop = Math.Max(zTop, g.FullCenterline.Max(p => p.Z) + 60); zBot = Math.Min(zBot, g.FullCenterline.Min(p => p.Z) - 60); }
        foreach (var (_, _, f) in fixes) { zTop = Math.Max(zTop, f.FullCenterline.Max(p => p.Z) + 60); zBot = Math.Min(zBot, f.FullCenterline.Min(p => p.Z) - 60); }

        const double margin = 30, labelW = 210;
        var k = Math.Min((W - 2 * margin - labelW) / Math.Max(xMax - xMin, 1), (H - 2 * margin - 24) / Math.Max(zTop - zBot, 1));
        var x0 = margin; var y0 = H - margin - 24;
        double X(double x) => x0 + (x - xMin) * k;
        double Y(double z) => y0 - (z - zBot) * k;

        // columna (desde la cara de llegada hasta la salida)
        var colRect = new Rectangle
        {
            Width = j.AvailableDepth * k, Height = (zMax - zMin) * k, Fill = PreviewColors.Concrete, Stroke = PreviewColors.Edge, StrokeThickness = 1.4,
            ToolTip = $"Columna {col.Name}: {j.AvailableDepth:0} mm en la dirección de la viga; útil {j.UsableDepth:0} mm (recubrimiento {col.Cover:0} + estribo Ø{col.TieDiameter:0})"
        };
        SetLeft(colRect, X(0)); SetTop(colRect, Y(zMax));
        Children.Add(colRect);
        Text("columna " + col.Name, X(0) + 4, Y(zMax) + 2, PreviewColors.Dim, 10, true).MaxWidth = Math.Max(40, j.AvailableDepth * k - 8);
        // límite del núcleo (cara interior del estribo lejano)
        Children.Add(new Line { X1 = X(j.UsableDepth), Y1 = Y(zMax), X2 = X(j.UsableDepth), Y2 = Y(zMin), Stroke = PreviewColors.Core, StrokeThickness = 1, StrokeDashArray = new DoubleCollection { 3, 3 } });
        Text("núcleo", X(j.UsableDepth) - 36, Y(zMin) - 14, PreviewColors.Dim, 9);

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

        var labelY = Y(zTop) + 2;
        var labelX = W - labelW - 4;

        // barras existentes (a trazos)
        foreach (var (bar, check) in existing)
        {
            var willFix = fixedIds.Contains(bar.Id);
            var brush = willFix ? PreviewColors.Existing : check!.Status == CheckStatus.Ok ? PreviewColors.ExistingOk : PreviewColors.ExistingFail;
            var tip = $"Barra existente {bar.Id} ({bar.Count}Ø{bar.Diameter:0}): {check!.Message}";
            for (var i = 0; i + 1 < bar.Centerline.Count; i++)
            {
                var a = bar.Centerline[i]; var c = bar.Centerline[i + 1];
                Children.Add(new Line
                {
                    X1 = X(Along(a.XY)), Y1 = Y(a.Z), X2 = X(Along(c.XY)), Y2 = Y(c.Z), Stroke = brush, StrokeThickness = willFix ? 1.2 : 1.6,
                    StrokeDashArray = new DoubleCollection { 5, 3 }, ToolTip = tip
                });
            }
            if (!willFix)
            {
                var ok = check!.Status == CheckStatus.Ok;
                var layerName = check.Layer.HasValue ? JointAnalyzer.LayerName(check.Layer.Value) : "?";
                Text($"existente {bar.Count}Ø{bar.Diameter:0} {layerName}: {(ok ? "cumple" : "NO cumple")} · prov. {check.Provided:0} / req. {check.Required:0}", labelX, labelY, brush, 10, true).MaxWidth = labelW;
                labelY += 30;
            }
        }

        // barras corregidas (naranja) con cotas
        foreach (var (_, bar, f) in fixes)
        {
            var th = Math.Max(2, bar.Diameter * k);
            var tip = $"Corregida {f.Label}: {f.Message}";
            for (var s = 0; s + 1 < f.FullCenterline.Count; s++)
            {
                var a = f.FullCenterline[s]; var c = f.FullCenterline[s + 1];
                Children.Add(new Line
                {
                    X1 = X(Along(a.XY)), Y1 = Y(a.Z), X2 = X(Along(c.XY)), Y2 = Y(c.Z), Stroke = PreviewColors.Fixed, StrokeThickness = th,
                    StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round, StrokeLineJoin = PenLineJoin.Round, ToolTip = tip
                });
            }
            var zDim = f.Layer == BarLayer.Top ? f.Elevation + 2.5 * bar.Diameter + 40 : f.Elevation - 2.5 * bar.Diameter - 40;
            Dimension(X(0), X(f.ProvidedLength), Y(zDim), PreviewColors.Fixed, $"prov. {f.ProvidedLength:0}");
            var zReq = f.Layer == BarLayer.Top ? zDim + 60 : zDim - 60;
            Dimension(X(0), X(f.RequiredLength), Y(zReq), PreviewColors.Dim, $"req. {(f.EndHook != null ? "ldh" : "ld")} {f.RequiredLength:0}");
            var label = $"corregir {bar.Count}Ø{bar.Diameter:0} {JointAnalyzer.LayerName(f.Layer)}: {JointAnalyzer.DecisionName(f.Decision)}" +
                        (f.EndHook != null ? $" (hacia {(f.EndHook.Direction.Z < 0 ? "abajo" : "arriba")})" : "") +
                        $" · extremo {f.EndShift:+0;-0} mm";
            Text(label, labelX, labelY, PreviewColors.Fixed, 10, true).MaxWidth = labelW;
            labelY += 30;
        }

        // barras nuevas con cotas
        foreach (var g in groups)
        {
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

            var ok = g.Decision != AnchorageDecision.Insufficient;
            var label = $"nuevas {g.Label}: {JointAnalyzer.DecisionName(g.Decision)}" +
                        (g.EndHook != null ? $" (ext. {g.EndHook.Extension:0}, hacia {(g.EndHook.Direction.Z < 0 ? "abajo" : "arriba")})" : "") +
                        $" · L = {g.TotalLength:0} mm";
            Text(label, labelX, labelY, ok ? brush : PreviewColors.Insufficient, 10, true).MaxWidth = labelW;
            labelY += 30;
        }

        // capas insuficientes (nuevas o existentes sin corrección)
        foreach (var p in _beam.Plans.Where(p => p.Action == LayerAction.Insufficient))
        {
            var msg = p.Unfixable.Count > 0 ? "sin corrección: " + p.Unfixable[0].Reason
                : p.Result != null ? $"INSUFICIENTE: ldh {p.Result.HookRequired:0} > útil {j.UsableDepth:0} mm" : "INSUFICIENTE";
            Text("✖ " + JointAnalyzer.LayerName(p.Layer) + ": " + msg, labelX, labelY, PreviewColors.Insufficient, 9).MaxWidth = labelW;
            labelY += 36;
        }

        foreach (var d in j.Diagnostics.Where(d => d.Severity != Severity.Info).Take(3))
        {
            Text((d.Severity == Severity.Error ? "✖ " : "⚠ ") + d.Message, labelX, labelY, d.Severity == Severity.Error ? PreviewColors.Insufficient : PreviewColors.BottomBar, 9).MaxWidth = labelW;
            labelY += 36;
        }

        Text($"Alzado por el eje de {beam.Name} · cara de la columna en x = 0 · a trazos: existente, naranja: corregida", 8, H - 18, PreviewColors.Dim, 10);
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
