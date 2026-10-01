using UnionesAcero.Core.Model;

namespace UnionesAcero.Core.Codes;

/// <summary>Datos de entrada para calcular una longitud de anclaje.</summary>
public sealed record AnchorageContext
{
    /// <summary>Diámetro de la barra (mm).</summary>
    public required double BarDiameter { get; init; }

    public required Materials Materials { get; init; }

    /// <summary>Barra superior con más de 300 mm de concreto fresco debajo (ψt).</summary>
    public bool IsTopBar { get; init; }

    /// <summary>Nudo de pórtico sísmico especial.</summary>
    public bool Seismic { get; init; }

    /// <summary>
    /// Se cumplen las condiciones de recubrimiento y separación del caso "favorable"
    /// (ACI 318 tabla 25.4.2.3: recubrimiento ≥ db, separación libre ≥ db y estribos mínimos).
    /// </summary>
    public bool FavorableSpacingAndCover { get; init; } = true;

    /// <summary>
    /// El gancho queda dentro del núcleo confinado de la columna con recubrimiento lateral ≥ 65 mm
    /// (ψo = 1.0 en ACI 318-19) y con estribos de confinamiento (ψr = 1.0).
    /// </summary>
    public bool HookConfinedInColumnCore { get; init; } = true;

    /// <summary>Recubrimiento lateral/distancia al borde cd disponible (mm). Usado por Eurocódigo 2.</summary>
    public double? EdgeDistance { get; init; }
}

/// <summary>Resultado de un cálculo de anclaje con su memoria breve.</summary>
public sealed record AnchorageLength(double Length, string Formula)
{
    public override string ToString() => $"{Length:0} mm ({Formula})";
}

/// <summary>Normativa de longitudes de desarrollo y ganchos.</summary>
public interface IAnchorageCode
{
    /// <summary>Clave estable (ACI318, NSR10, E060, EC2).</summary>
    string Key { get; }

    string Name { get; }

    /// <summary>Longitud de desarrollo recta en tracción ld (mm).</summary>
    AnchorageLength StraightDevelopmentLength(AnchorageContext ctx);

    /// <summary>
    /// Longitud de desarrollo con gancho estándar ldh (mm), medida desde la sección crítica
    /// (cara de la columna) hasta la cara exterior del gancho.
    /// </summary>
    AnchorageLength HookDevelopmentLength(AnchorageContext ctx);

    /// <summary>Extensión recta del gancho después del doblez (mm).</summary>
    double HookExtension(double barDiameter, HookAngle angle);

    /// <summary>Diámetro interior mínimo de doblado (mm).</summary>
    double BendInnerDiameter(double barDiameter);

    /// <summary>Longitud de traslape en tracción clase B (mm), usada para la extensión dentro de la viga.</summary>
    AnchorageLength LapSpliceLength(AnchorageContext ctx);

    /// <summary>
    /// Dimensión mínima de la columna, paralela a las barras, para barras pasantes
    /// por el nudo (mm). Null si la norma no lo exige en este contexto.
    /// </summary>
    double? MinimumJointDimensionForPassThrough(AnchorageContext ctx);
}
