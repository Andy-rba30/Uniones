namespace UnionesAcero.Core.Model;

/// <summary>Propiedades de materiales usadas en las fórmulas de anclaje (MPa).</summary>
public sealed record Materials
{
    /// <summary>Fluencia del acero de refuerzo, MPa (p. ej. 420).</summary>
    public double Fy { get; init; } = 420;

    /// <summary>Resistencia a compresión del concreto, MPa (p. ej. 21, 28, 35).</summary>
    public double Fc { get; init; } = 21;

    /// <summary>Concreto liviano (λ = 0.75 en ACI).</summary>
    public bool LightweightConcrete { get; init; }

    /// <summary>Barras con recubrimiento epóxico (ψe).</summary>
    public bool EpoxyCoated { get; init; }

    public double Lambda => LightweightConcrete ? 0.75 : 1.0;
}
