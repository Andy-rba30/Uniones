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

    /// <summary>Tolerancia para considerar que una viga "llega" a la columna (mm).</summary>
    public double ContactTolerance { get; init; } = 50;

    /// <summary>Ángulo máximo (°) para considerar dos vigas colineales (barras pasantes).</summary>
    public double CollinearAngleTolerance { get; init; } = 10;

    /// <summary>Redondeo de longitudes de corte (mm). 0 = sin redondeo.</summary>
    public double LengthRounding { get; init; } = 10;
}
