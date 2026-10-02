using UnionesAcero.Core.Codes;

namespace UnionesAcero.Core.Model;

/// <summary>Opciones de análisis y detallado del nudo.</summary>
public sealed record JointOptions
{
    public IAnchorageCode Code { get; init; } = new Aci318Code();

    public Materials Materials { get; init; } = new();

    /// <summary>
    /// Nudo de pórtico especial resistente a momento (sísmico): usa las fórmulas del
    /// capítulo sísmico (ACI 318 cap. 18 / NSR-10 C.21 / E.060 cap. 21) y exige que el
    /// gancho termine dentro del núcleo confinado.
    /// </summary>
    public bool SeismicJoint { get; init; } = true;

    /// <summary>Capas a detallar.</summary>
    public IReadOnlyList<BarLayer> Layers { get; init; } = new[] { BarLayer.Top, BarLayer.Bottom };

    /// <summary>
    /// Si es cierto, las barras ancladas se llevan hasta la cara lejana del núcleo de la
    /// columna (práctica habitual) aunque la longitud requerida sea menor. Si es falso,
    /// se embeben exactamente la longitud requerida.
    /// </summary>
    public bool EmbedToFarFace { get; init; } = true;

    /// <summary>
    /// Longitud de la barra generada dentro de la viga, medida desde la cara de la columna (mm).
    /// Si es null se usa 2·h + traslape clase B (1.3·ld).
    /// </summary>
    public double? ExtensionIntoBeam { get; init; }

    /// <summary>
    /// Holgura libre adicional entre el exterior del gancho y el estribo de la columna (mm).
    /// </summary>
    public double HookClearance { get; init; } = 0;

    /// <summary>
    /// Si dos vigas se cruzan en el nudo con la misma cota de capa, desplaza la capa
    /// de la segunda viga para evitar el choque de barras.
    /// </summary>
    public bool AutoStaggerCrossingLayers { get; init; } = true;

    /// <summary>Separación libre mínima entre barras que se cruzan (mm).</summary>
    public double CrossingClearance { get; init; } = 25;

    /// <summary>
    /// Correr en planta las barras de la viga que coinciden con las verticales de la columna, hacia
    /// el eje de la viga, para que pasen por dentro de ellas (como en obra).
    /// </summary>
    public bool ResolveColumnBarClash { get; init; } = true;

    /// <summary>
    /// Separación libre deseada entre una barra de viga y una vertical de columna (mm). Si no cabe en
    /// el ancho de la viga se admite el contacto y se avisa.
    /// </summary>
    public double ColumnBarClearance { get; init; } = 25;

    /// <summary>
    /// En la misma esquina, retrasar el extremo de la viga secundaria para que su gancho no atraviese
    /// las barras ni los ganchos de la viga principal (manteniendo el anclaje requerido).
    /// </summary>
    public bool ResolveHookLegClash { get; init; } = true;

    /// <summary>
    /// Barras ya modeladas en dos vigas colineales: hacer continua a través del nudo la de la viga
    /// principal y empalmarla por traslape, fuera del nudo (a 2h de la cara), con la de la opuesta,
    /// que se corta en el inicio del traslape.
    /// </summary>
    public bool SplicePassThroughBars { get; init; } = true;

    /// <summary>Longitud comercial de la varilla (mm); se avisa cuando una barra continua la supera.</summary>
    public double CommercialBarLength { get; init; } = 9000;

    /// <summary>Separación libre entre las dos barras de un traslape (mm). 0 = traslape en contacto.</summary>
    public double LapSpliceClearance { get; init; } = 0;

    /// <summary>Tolerancia para considerar que una viga "llega" a la columna (mm).</summary>
    public double ContactTolerance { get; init; } = 50;

    /// <summary>Ángulo máximo (°) para considerar dos vigas colineales (barras pasantes).</summary>
    public double CollinearAngleTolerance { get; init; } = 10;

    /// <summary>Redondeo de longitudes de corte (mm). 0 = sin redondeo.</summary>
    public double LengthRounding { get; init; } = 10;
}
