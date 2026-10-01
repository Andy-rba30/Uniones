using System.Windows.Media;
using UnionesAcero.Core.Analysis;
using UnionesAcero.Core.Model;

namespace UnionesAcero.Revit.UI;

/// <summary>Colores comunes de los esquemas de planta y alzado.</summary>
public static class PreviewColors
{
    public static readonly Brush Concrete = Freeze(Color.FromRgb(0xE6, 0xE6, 0xE6));
    public static readonly Brush Beam = Freeze(Color.FromRgb(0xD2, 0xD2, 0xD2));
    public static readonly Brush Edge = Freeze(Color.FromRgb(0x69, 0x69, 0x69));
    public static readonly Brush Core = Freeze(Color.FromRgb(0x9A, 0x9A, 0x9A));
    public static readonly Brush TopBar = Freeze(Color.FromRgb(0x8B, 0x2E, 0x2E));
    public static readonly Brush BottomBar = Freeze(Color.FromRgb(0xD9, 0x6C, 0x2A));
    public static readonly Brush PassThrough = Freeze(Color.FromRgb(0x1F, 0x7A, 0x7A));
    public static readonly Brush Insufficient = Freeze(Color.FromRgb(0xE0, 0x1B, 0x1B));
    public static readonly Brush ExistingOk = Freeze(Color.FromRgb(0x3F, 0x9C, 0x5A));
    public static readonly Brush ExistingFail = Freeze(Color.FromRgb(0xE0, 0x1B, 0x1B));
    public static readonly Brush Existing = Freeze(Color.FromRgb(0x9A, 0x9A, 0x9A));
    /// <summary>Barra corregida (sustituye a una existente que no cumple).</summary>
    public static readonly Brush Fixed = Freeze(Color.FromRgb(0xC8, 0x7A, 0x00));
    public static readonly Brush Selected = Freeze(Color.FromRgb(0x2F, 0x7B, 0xD9));
    public static readonly Brush Dim = Freeze(Color.FromRgb(0x50, 0x50, 0x50));

    public static Brush ForGroup(BarGroup g) => ForDecision(g.Decision, g.Layer);

    public static Brush ForDecision(AnchorageDecision d, BarLayer layer)
    {
        if (d == AnchorageDecision.Insufficient) return Insufficient;
        if (d == AnchorageDecision.PassThrough) return PassThrough;
        return layer == BarLayer.Top ? TopBar : BottomBar;
    }

    private static Brush Freeze(Color c)
    {
        var b = new SolidColorBrush(c);
        b.Freeze();
        return b;
    }
}
