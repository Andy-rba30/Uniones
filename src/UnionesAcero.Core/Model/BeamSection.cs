using UnionesAcero.Core.Geometry;

namespace UnionesAcero.Core.Model;

/// <summary>
/// Viga que llega a la columna. El eje se da por sus dos extremos en planta (mm);
/// el analizador decide cuál extremo toca la columna.
/// </summary>
public sealed class BeamSection
{
    public BeamSection(string name, Vec2 axisStart, Vec2 axisEnd, double width, double depth)
    {
        Name = name;
        AxisStart = axisStart;
        AxisEnd = axisEnd;
        Width = width;
        Depth = depth;
    }

    public string Name { get; }
    public Vec2 AxisStart { get; }
    public Vec2 AxisEnd { get; }

    /// <summary>Ancho b (mm).</summary>
    public double Width { get; }

    /// <summary>Peralte h (mm).</summary>
    public double Depth { get; }

    /// <summary>Cota de la cara superior (mm).</summary>
    public double TopElevation { get; init; }

    /// <summary>Recubrimiento libre al estribo (mm).</summary>
    public double Cover { get; init; } = 40;

    /// <summary>Diámetro del estribo (mm).</summary>
    public double StirrupDiameter { get; init; } = 10;

    public double TopBarDiameter { get; init; } = 16;
    public int TopBarCount { get; init; } = 3;
    public double BottomBarDiameter { get; init; } = 16;
    public int BottomBarCount { get; init; } = 3;

    /// <summary>Identificador en el modelo de origen (ElementId en Revit).</summary>
    public long? SourceId { get; init; }

    public Segment2D Axis => new(AxisStart, AxisEnd);

    public double BarDiameter(BarLayer layer) => layer == BarLayer.Top ? TopBarDiameter : BottomBarDiameter;
    public int BarCount(BarLayer layer) => layer == BarLayer.Top ? TopBarCount : BottomBarCount;

    /// <summary>Cota del eje de las barras de la capa (mm).</summary>
    public double LayerElevation(BarLayer layer)
    {
        var db = BarDiameter(layer);
        return layer == BarLayer.Top
            ? TopElevation - Cover - StirrupDiameter - db / 2
            : TopElevation - Depth + Cover + StirrupDiameter + db / 2;
    }
}
