using UnionesAcero.Core.Geometry;
using UnionesAcero.Core.Model;

namespace UnionesAcero.Core.Analysis;

public enum AnchorageDecision
{
    /// <summary>La viga no llega a la columna o no se pudo determinar el contacto.</summary>
    NotConnected,
    /// <summary>Anclaje recto: la profundidad disponible cubre ld.</summary>
    Straight,
    /// <summary>Anclaje con gancho estándar a 90° dentro del núcleo de la columna.</summary>
    Hook90,
    /// <summary>Hay una viga colineal al otro lado: las barras pasan a través del nudo.</summary>
    PassThrough,
    /// <summary>Ni recto ni con gancho cabe en la columna: requiere cambio de diseño.</summary>
    Insufficient
}

public enum Severity { Info, Warning, Error }

public sealed record Diagnostic(Severity Severity, string Element, string Message)
{
    public override string ToString() => $"[{Severity}] {Element}: {Message}";
}

/// <summary>Gancho al final de una barra.</summary>
public sealed record HookSpec(HookAngle Angle, double Extension, Vec3 Direction, double BendInnerDiameter);

/// <summary>
/// Grupo de barras iguales (misma capa, misma viga) listo para dibujar: una barra prototipo
/// más una regla de repetición lateral.
/// </summary>
public sealed class BarGroup
{
    public required string BeamName { get; init; }
    public long? BeamSourceId { get; init; }
    public required BarLayer Layer { get; init; }
    public required double Diameter { get; init; }
    public required int Count { get; init; }
    /// <summary>Separación entre ejes de barras (mm). 0 si Count == 1.</summary>
    public required double Spacing { get; init; }
    public required AnchorageDecision Decision { get; init; }

    /// <summary>Eje de la barra prototipo SIN el gancho (vértices en mm, cota incluida).</summary>
    public required IReadOnlyList<Vec3> Centerline { get; init; }

    /// <summary>Gancho al final de la barra (en la columna), si lo hay.</summary>
    public HookSpec? EndHook { get; init; }

    /// <summary>Eje completo incluyendo el tramo del gancho (esquinas vivas), para dibujo/verificación.</summary>
    public required IReadOnlyList<Vec3> FullCenterline { get; init; }

    /// <summary>Normal del plano que contiene la barra (dirección lateral de la viga).</summary>
    public required Vec3 PlaneNormal { get; init; }

    /// <summary>Dirección en la que se repiten las barras del grupo.</summary>
    public required Vec3 ArrayDirection { get; init; }

    public required double Elevation { get; init; }

    /// <summary>Longitud de anclaje requerida (mm) según la decisión.</summary>
    public required double RequiredLength { get; init; }

    /// <summary>Longitud de anclaje provista (mm).</summary>
    public required double ProvidedLength { get; init; }

    public required string Formula { get; init; }

    public string Comment { get; init; } = string.Empty;

    public double TotalLength
    {
        get
        {
            double l = 0;
            for (var i = 0; i + 1 < FullCenterline.Count; i++) l += FullCenterline[i].DistanceTo(FullCenterline[i + 1]);
            return l;
        }
    }

    public string Label => $"{BeamName} {(Layer == BarLayer.Top ? "sup." : "inf.")} {Count}Ø{Diameter:0}";
}

/// <summary>Resultado por capa de una viga.</summary>
public sealed class LayerResult
{
    public required BarLayer Layer { get; init; }
    public required AnchorageDecision Decision { get; init; }
    public required double Elevation { get; init; }
    public required double StraightRequired { get; init; }
    public required double HookRequired { get; init; }
    public required double Provided { get; init; }
    public required string Formula { get; init; }
    public BarGroup? Group { get; init; }
    public bool Ok => Decision is AnchorageDecision.Straight or AnchorageDecision.Hook90 or AnchorageDecision.PassThrough;
}

/// <summary>Resultado del encuentro de una viga con la columna.</summary>
public sealed class BeamJoint
{
    public required BeamSection Beam { get; init; }
    public required bool Connected { get; init; }

    /// <summary>Punto del eje de la viga en la cara de la columna.</summary>
    public Vec2 ContactPoint { get; init; }
    public int ContactEdgeIndex { get; init; } = -1;

    /// <summary>Dirección unitaria del eje hacia el interior de la columna.</summary>
    public Vec2 InwardDirection { get; init; }

    /// <summary>Dirección lateral unitaria (perpendicular en planta).</summary>
    public Vec2 LateralDirection { get; init; }

    /// <summary>Profundidad de columna atravesada por el eje, desde la cara de entrada hasta la salida (mm).</summary>
    public double AvailableDepth { get; init; }

    /// <summary>Profundidad útil para anclar: AvailableDepth − recubrimiento − estribo − holgura (mm).</summary>
    public double UsableDepth { get; init; }

    /// <summary>Hueco entre el extremo modelado de la viga y la cara de la columna (mm, 0 si toca).</summary>
    public double Gap { get; init; }

    /// <summary>Cuánto sobresale la viga lateralmente de la cara de la columna (mm).</summary>
    public double LateralOverhang { get; init; }

    /// <summary>Viga colineal al otro lado del nudo, si existe.</summary>
    public BeamSection? OppositeBeam { get; init; }

    public List<LayerResult> Layers { get; } = new();
    public List<Diagnostic> Diagnostics { get; } = new();

    public bool HasErrors => Diagnostics.Any(d => d.Severity == Severity.Error);
}

/// <summary>Resultado completo del nudo.</summary>
public sealed class JointAnalysis
{
    public required ColumnSection Column { get; init; }
    public required JointOptions Options { get; init; }
    public List<BeamJoint> Joints { get; } = new();
    public List<Diagnostic> Diagnostics { get; } = new();

    public IEnumerable<BarGroup> BarGroups => Joints.SelectMany(j => j.Layers).Select(l => l.Group).Where(g => g != null)!;

    public IEnumerable<Diagnostic> AllDiagnostics => Diagnostics.Concat(Joints.SelectMany(j => j.Diagnostics));

    public bool HasErrors => AllDiagnostics.Any(d => d.Severity == Severity.Error);
}
