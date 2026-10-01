using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using UnionesAcero.Core.Analysis;
using UnionesAcero.Core.Geometry;
using UnionesAcero.Core.Model;
using UnionesAcero.Core.Verification;
using UnionesAcero.Revit.Services;

namespace UnionesAcero.Revit.UI;

/// <summary>
/// Planta del nudo: la sección de la columna (con su núcleo a trazos), las vigas que llegan y
/// cada barra que se va a crear, con su gancho (punto) o su paso a través. Las barras ya
/// modeladas se dibujan a trazos (verde cumple, rojo no). Rueda: zoom; arrastrar: mover;
/// doble clic: encajar; clic en una viga: seleccionarla para el alzado.
/// </summary>
public sealed class PlanPreview : Canvas
{
    private ColumnItem? _item;
    private BeamItem? _selectedBeam;
    private string _message = "Sin columna armable";

    private double _zoom = 1;
    private Vector _pan;
    private double _x0, _y0, _k;
    private Point _dragStart;
    private Vector _panStart;
    private bool _dragging, _moved;
    private (Vec2 Min, Vec2 Max) _bounds;

    public event Action<BeamItem>? BeamClicked;

    public PlanPreview()
    {
        Background = RevitTheme.Paper;
        ClipToBounds = true;
        SizeChanged += (_, _) => Redraw();
        MouseWheel += OnWheel;
        MouseLeftButtonDown += OnDown;
        MouseMove += OnMove;
        MouseLeftButtonUp += OnUp;
        MouseLeave += (_, _) => { _dragging = false; ReleaseMouseCapture(); };
        Cursor = Cursors.Hand;
    }

    public void Show(ColumnItem item, BeamItem? selectedBeam)
    {
        var changed = !ReferenceEquals(_item, item);
        _item = item;
        _selectedBeam = selectedBeam;
        if (changed) ResetView(); else Redraw();
    }

    public void Clear(string message)
    {
        _item = null;
        _message = message;
        Redraw();
    }

    public void ResetView()
    {
        _zoom = 1; _pan = new Vector(0, 0);
        Redraw();
    }

    // --- zoom y desplazamiento -------------------------------------------------------
    private void OnWheel(object sender, MouseWheelEventArgs e)
    {
        if (_item?.Section == null) return;
        var factor = e.Delta > 0 ? 1.25 : 1 / 1.25;
        var newZoom = Math.Max(1, Math.Min(40, _zoom * factor));
        factor = newZoom / _zoom;
        var m = e.GetPosition(this);
        _pan = new Vector(m.X - _x0 - (m.X - _pan.X - _x0) * factor, m.Y - _y0 - (m.Y - _pan.Y - _y0) * factor);
        _zoom = newZoom;
        if (_zoom <= 1.0001) _pan = new Vector(0, 0);
        Redraw();
        e.Handled = true;
    }

    private void OnDown(object sender, MouseButtonEventArgs e)
    {
        if (_item?.Section == null) return;
        if (e.ClickCount == 2) { ResetView(); e.Handled = true; return; }
        _dragging = true; _moved = false; _dragStart = e.GetPosition(this); _panStart = _pan;
        CaptureMouse();
    }

    private void OnMove(object sender, MouseEventArgs e)
    {
        if (!_dragging) return;
        var d = e.GetPosition(this) - _dragStart;
        if (d.Length > 3) _moved = true;
        _pan = _panStart + d;
        Redraw();
    }

    private void OnUp(object sender, MouseButtonEventArgs e)
    {
        _dragging = false;
        ReleaseMouseCapture();
        if (_moved || _item == null) return;
        // clic simple: ¿sobre una viga?
        var p = e.GetPosition(this);
        var world = ToWorld(p);
        foreach (var b in _item.Beams)
        {
            if (BeamRect(b) is { } rect && RectContains(rect, world))
            {
                BeamClicked?.Invoke(b);
                return;
            }
        }
    }

    // --- geometría auxiliar ----------------------------------------------------------
    /// <summary>Rectángulo (en planta, mm) con el que se dibuja la viga: desde la cara de la columna hacia fuera.</summary>
    private (Vec2 A, Vec2 B, Vec2 C, Vec2 D)? BeamRect(BeamItem b)
    {
        var s = b.Base;
        if (b.Joint is { Connected: true } j)
        {
            var len = BeamDrawLength(j);
            var u = j.InwardDirection; var v = j.LateralDirection;
            var p0 = j.ContactPoint; var p1 = j.ContactPoint - u * len;
            return (p0 + v * (s.Width / 2), p1 + v * (s.Width / 2), p1 - v * (s.Width / 2), p0 - v * (s.Width / 2));
        }
        var dir = (s.AxisEnd - s.AxisStart).Normalized();
        var vv = dir.Perp();
        return (s.AxisStart + vv * (s.Width / 2), s.AxisEnd + vv * (s.Width / 2), s.AxisEnd - vv * (s.Width / 2), s.AxisStart - vv * (s.Width / 2));
    }

    private double BeamDrawLength(BeamJoint j)
    {
        var (min, max) = _item!.Section!.Outline.Bounds;
        var colDim = Math.Max(max.X - min.X, max.Y - min.Y);
        var ext = j.Layers.Select(l => l.Group).Where(g => g != null)
            .Select(g => (g!.Centerline[0].XY - j.ContactPoint).Length).DefaultIfEmpty(0).Max();
        return Math.Max(Math.Max(ext + 150, 0.6 * colDim), 400);
    }

    private static bool RectContains((Vec2 A, Vec2 B, Vec2 C, Vec2 D) r, Vec2 p)
        => new Polygon2D(new[] { r.A, r.B, r.C, r.D }).Contains(p);

    private Vec2 ToWorld(Point p)
    {
        var x0 = _x0 + _pan.X; var y0 = _y0 + _pan.Y;
        return new Vec2(_bounds.Min.X + (p.X - x0) / _k, _bounds.Min.Y + (y0 - p.Y) / _k);
    }

    private static string Mm(double v) => Math.Round(v).ToString(CultureInfo.InvariantCulture);

    // --- dibujo ----------------------------------------------------------------------
    private void Redraw()
    {
        Children.Clear();
        double W = ActualWidth, H = ActualHeight;
        if (W < 10 || H < 10) return;
        if (_item?.Section == null)
        {
            Text(_message, 10, 10, RevitTheme.Hint, 12);
            return;
        }

        var outline = _item.Section.Outline;
        // extensión a dibujar: columna + vigas
        var pts = new List<Vec2>(outline.Vertices);
        foreach (var b in _item.Beams)
            if (BeamRect(b) is { } r) { pts.Add(r.A); pts.Add(r.B); pts.Add(r.C); pts.Add(r.D); }
        var minX = pts.Min(p => p.X); var maxX = pts.Max(p => p.X);
        var minY = pts.Min(p => p.Y); var maxY = pts.Max(p => p.Y);
        _bounds = (new Vec2(minX, minY), new Vec2(maxX, maxY));
        var w = Math.Max(maxX - minX, 1); var h = Math.Max(maxY - minY, 1);

        const double margin = 40;
        _k = Math.Min((W - 2 * margin) / w, (H - 2 * margin) / h) * _zoom;
        _x0 = 0.5 * (W - w * _k / _zoom);
        _y0 = 0.5 * (H + h * _k / _zoom);
        var x0 = _x0 + _pan.X; var y0 = _y0 + _pan.Y;
        double X(double x) => x0 + (x - minX) * _k;
        double Y(double y) => y0 - (y - minY) * _k;
        Point P(Vec2 p) => new(X(p.X), Y(p.Y));

        // vigas
        foreach (var b in _item.Beams)
        {
            if (BeamRect(b) is not { } r) continue;
            var connected = b.Joint is { Connected: true };
            var selected = ReferenceEquals(b, _selectedBeam);
            var poly = new Polygon
            {
                Fill = b.Include ? PreviewColors.Beam : Brushes.Transparent,
                Stroke = selected ? PreviewColors.Selected : (connected ? PreviewColors.Edge : PreviewColors.Insufficient),
                StrokeThickness = selected ? 2.4 : 1.2,
                StrokeDashArray = connected ? null : new DoubleCollection { 4, 3 },
                ToolTip = b.Name + ": " + b.Base.Width.ToString("0") + " x " + b.Base.Depth.ToString("0") + " mm" +
                          (connected ? $", profundidad disponible {b.Joint!.AvailableDepth:0} mm (útil {b.Joint.UsableDepth:0})" : " (no conectada)")
            };
            foreach (var p in new[] { r.A, r.B, r.C, r.D }) poly.Points.Add(P(p));
            Children.Add(poly);
            var mid = (r.B + r.C) / 2;
            var lbl = Text(b.Name, X(mid.X) - 4 * b.Name.Length, Y(mid.Y) - 8, selected ? PreviewColors.Selected : PreviewColors.Dim, 11, true);
            lbl.IsHitTestVisible = false;
        }

        // columna
        var col = new Polygon { Fill = PreviewColors.Concrete, Stroke = PreviewColors.Edge, StrokeThickness = 1.4, StrokeLineJoin = PenLineJoin.Miter };
        foreach (var p in outline.Vertices) col.Points.Add(P(p));
        col.ToolTip = _item.Kind + ", " + _item.Describe();
        Children.Add(col);

        // núcleo (interior del estribo)
        try
        {
            var core = _item.Section.CorePolygon;
            var cp = new Polygon { Fill = null, Stroke = PreviewColors.Core, StrokeThickness = 1, StrokeDashArray = new DoubleCollection { 3, 3 }, IsHitTestVisible = false };
            foreach (var p in core.Vertices) cp.Points.Add(P(p));
            Children.Add(cp);
        }
        catch { /* recubrimiento mayor que la sección */ }

        // cotas generales de la columna
        var (cmin, cmax) = outline.Bounds;
        Text(Mm(cmax.X - cmin.X) + " mm", X((cmin.X + cmax.X) / 2) - 22, Y(cmin.Y) + 4, PreviewColors.Dim, 10);
        Text(Mm(cmax.Y - cmin.Y) + " mm", X(cmax.X) + 4, Y((cmin.Y + cmax.Y) / 2) - 7, PreviewColors.Dim, 10);

        // barras existentes (a trazos)
        foreach (var b in _item.Beams)
        {
            foreach (var (_, bar) in b.ExistingBars)
            {
                var check = b.Checks.FirstOrDefault(c => c.Id == bar.Id);
                if (check == null || check.Status == CheckStatus.NotApplicable) continue;
                var brush = check.Status == CheckStatus.Ok ? PreviewColors.ExistingOk : PreviewColors.ExistingFail;
                for (var i = 0; i + 1 < bar.Centerline.Count; i++)
                {
                    var a = bar.Centerline[i]; var c = bar.Centerline[i + 1];
                    if (Math.Abs(c.Z - a.Z) > 0.5 * a.DistanceTo(c)) continue; // tramo vertical (gancho): se omite en planta
                    Children.Add(new Line
                    {
                        X1 = X(a.X), Y1 = Y(a.Y), X2 = X(c.X), Y2 = Y(c.Y), Stroke = brush, StrokeThickness = 1.3,
                        StrokeDashArray = new DoubleCollection { 5, 3 }, ToolTip = "Barra existente " + bar.Id + ": " + check.Message
                    });
                }
            }
        }

        // barras nuevas
        if (_item.Analysis != null)
        {
            foreach (var j in _item.Analysis.Joints)
            {
                foreach (var l in j.Layers)
                {
                    var g = l.Group;
                    if (g == null) continue;
                    var brush = PreviewColors.ForGroup(g);
                    var th = Math.Max(1.6, g.Diameter * _k);
                    var dash = g.Layer == BarLayer.Bottom ? new DoubleCollection { 6, 2 } : null;
                    var tip = g.Label + ": " + JointAnalyzer.DecisionName(g.Decision) +
                              (g.Decision == AnchorageDecision.PassThrough ? "" : $", requerido {g.RequiredLength:0} / provisto {g.ProvidedLength:0} mm") +
                              (g.EndHook != null ? $", gancho {(int)g.EndHook.Angle}° hacia {(g.EndHook.Direction.Z < 0 ? "abajo" : "arriba")}" : "");
                    for (var i = 0; i < g.Count; i++)
                    {
                        var off = g.ArrayDirection * (i * g.Spacing);
                        for (var s = 0; s + 1 < g.Centerline.Count; s++)
                        {
                            var a = g.Centerline[s] + off; var c = g.Centerline[s + 1] + off;
                            Children.Add(new Line
                            {
                                X1 = X(a.X), Y1 = Y(a.Y), X2 = X(c.X), Y2 = Y(c.Y), Stroke = brush, StrokeThickness = th,
                                StrokeDashArray = dash, StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round, ToolTip = tip
                            });
                        }
                        if (g.EndHook != null)
                        {
                            var end = g.Centerline[^1] + off;
                            var rr = Math.Max(3, 0.9 * g.Diameter * _k);
                            var dot = new Ellipse { Width = 2 * rr, Height = 2 * rr, Fill = brush, Stroke = Brushes.Black, StrokeThickness = 0.6, ToolTip = tip };
                            SetLeft(dot, X(end.X) - rr); SetTop(dot, Y(end.Y) - rr);
                            Children.Add(dot);
                        }
                    }
                    if (g.Decision == AnchorageDecision.Insufficient)
                    {
                        var end = g.Centerline[^1];
                        Text("✖", X(end.X) + 4, Y(end.Y) - 10, PreviewColors.Insufficient, 14, true);
                    }
                }

                // profundidad disponible / útil junto a la cara
                if (j.Connected)
                {
                    var p = j.ContactPoint + j.InwardDirection * (j.AvailableDepth / 2);
                    var t = Text($"{j.AvailableDepth:0} / útil {j.UsableDepth:0}", X(p.X) - 30, Y(p.Y) - 7, PreviewColors.Dim, 9);
                    t.IsHitTestVisible = false;
                }
            }
        }

        // resumen
        if (_item.Analysis != null)
        {
            var groups = _item.Analysis.BarGroups.ToList();
            var bad = groups.Count(g => g.Decision == AnchorageDecision.Insufficient);
            var summary = $"{groups.Count} grupo(s) de barras" + (bad > 0 ? $", {bad} insuficiente(s)" : "") +
                          $" · {_item.Analysis.Options.Code.Name}" + (_item.Analysis.Options.SeismicJoint ? " (sísmico)" : "");
            Text(summary, 8, H - 20, PreviewColors.Dim, 11);
        }
    }

    private TextBlock Text(string s, double x, double y, Brush brush, double size, bool bold = false)
    {
        var t = new TextBlock { Text = s, Foreground = brush, FontSize = size, TextWrapping = TextWrapping.NoWrap };
        if (bold) t.FontWeight = FontWeights.SemiBold;
        SetLeft(t, x); SetTop(t, y);
        Children.Add(t);
        return t;
    }
}
