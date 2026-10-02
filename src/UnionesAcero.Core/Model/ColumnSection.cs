using UnionesAcero.Core.Geometry;

namespace UnionesAcero.Core.Model;

/// <summary>
/// Sección de una columna en planta: cualquier polígono simple (rectangular, L, T, cruz, irregular).
/// Coordenadas en mm, en el sistema global de planta.
/// </summary>
public sealed class ColumnSection
{
    public ColumnSection(string name, Polygon2D outline)
    {
        Name = name;
        Outline = outline;
    }

    public string Name { get; }
    public Polygon2D Outline { get; }

    /// <summary>Recubrimiento libre al estribo (mm).</summary>
    public double Cover { get; init; } = 40;

    /// <summary>Diámetro del estribo de la columna (mm).</summary>
    public double TieDiameter { get; init; } = 10;

    /// <summary>Diámetro de las barras longitudinales de la columna (mm), para holguras.</summary>
    public double LongitudinalBarDiameter { get; init; } = 16;

    /// <summary>Cota superior de la columna (mm). Informativa.</summary>
    public double TopElevation { get; init; }

    /// <summary>Cota inferior de la columna (mm). Informativa.</summary>
    public double BottomElevation { get; init; }

    /// <summary>Identificador en el modelo de origen (ElementId en Revit).</summary>
    public long? SourceId { get; init; }

    /// <summary>
    /// Posición en planta (mm) de cada barra longitudinal de la columna, si se leyó del modelo.
    /// Vacío = desconocida.
    /// </summary>
    public IReadOnlyList<Vec2> LongitudinalBarPositions { get; init; } = Array.Empty<Vec2>();

    /// <summary>Contorno interior del estribo (núcleo confinado aproximado).</summary>
    public Polygon2D CorePolygon => Outline.InwardOffset(Cover + TieDiameter);

    /// <summary>
    /// Verticales de la columna con las que no deben chocar las barras de las vigas: las del modelo si
    /// se leyeron; si no, se supone una barra en cada esquina del núcleo (toda columna las tiene).
    /// </summary>
    public IReadOnlyList<Vec2> BarPositionsForClearance()
    {
        if (LongitudinalBarPositions.Count > 0) return LongitudinalBarPositions;
        try { return CorePolygon.InwardOffset(LongitudinalBarDiameter / 2).Vertices; }
        catch (ArgumentException) { return Array.Empty<Vec2>(); }
    }
}
