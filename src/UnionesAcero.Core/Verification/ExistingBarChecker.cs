using UnionesAcero.Core.Analysis;
using UnionesAcero.Core.Codes;
using UnionesAcero.Core.Geometry;
using UnionesAcero.Core.Model;

namespace UnionesAcero.Core.Verification;

/// <summary>
/// Barra existente en el modelo (eje en mm, con cota). <see cref="Centerline"/> incluye los ganchos
/// (para verificar y dibujar); <see cref="BareCenterline"/> es el eje sin ganchos (para reconstruir
/// la barra al corregirla). Si no se da, se usa el mismo eje.
/// </summary>
public sealed record ExistingBar(long Id, string BeamName, double Diameter, IReadOnlyList<Vec3> Centerline, string? Description = null)
{
    public IReadOnlyList<Vec3>? BareCenterline { get; init; }

    /// <summary>Número de barras del conjunto en el modelo.</summary>
    public int Count { get; init; } = 1;

    /// <summary>Longitud de la distribución del conjunto (mm, entre ejes de la primera y la última barra). 0 si es una sola barra.</summary>
    public double SetLength { get; init; }

    /// <summary>Dirección unitaria en la que se reparten las barras del conjunto desde la primera. Null si es una sola barra.</summary>
    public Vec3? SetDirection { get; init; }
}

public enum CheckStatus
{
    /// <summary>Cumple.</summary>
    Ok,
    /// <summary>No cumple.</summary>
    Fail,
    /// <summary>La barra no es longitudinal de la viga hacia la columna (estribo, transversal...).</summary>
    NotApplicable
}

public sealed record BarCheckResult(
    long Id,
    string BeamName,
    BarLayer? Layer,
    AnchorageDecision Decision,
    CheckStatus Status,
    double Required,
    double Provided,
    string Message);

/// <summary>
/// Verifica barras longitudinales ya modeladas en las vigas contra la longitud de anclaje
/// requerida en la columna.
/// </summary>
public sealed class ExistingBarChecker
{
    private readonly JointOptions _options;

    public ExistingBarChecker(JointOptions? options = null)
    {
        _options = options ?? new JointOptions();
    }

    public List<BarCheckResult> Check(JointAnalysis analysis, IEnumerable<ExistingBar> bars)
    {
        var results = new List<BarCheckResult>();
        foreach (var bar in bars)
        {
            var joint = analysis.Joints.FirstOrDefault(j => j.Connected && j.Beam.Name == bar.BeamName)
                        ?? analysis.Joints.FirstOrDefault(j => j.Connected && j.Beam.SourceId.HasValue && j.Beam.SourceId.ToString() == bar.BeamName);
            if (joint == null)
            {
                results.Add(new BarCheckResult(bar.Id, bar.BeamName, null, AnchorageDecision.NotConnected, CheckStatus.NotApplicable, 0, 0,
                    "La viga de la barra no está conectada a la columna analizada."));
                continue;
            }
            results.Add(CheckBar(analysis.Column, joint, bar));
        }
        return results;
    }

    public BarCheckResult CheckBar(ColumnSection column, BeamJoint joint, ExistingBar bar)
    {
        var pts = bar.Centerline;
        if (pts.Count < 2)
            return NotApplicable(bar, "La barra no tiene geometría de eje.");

        // Tramos horizontales (|dz| pequeño frente a la longitud)
        var horizontal = new List<(Vec3 A, Vec3 B)>();
        var vertical = new List<(Vec3 A, Vec3 B)>();
        for (var i = 0; i + 1 < pts.Count; i++)
        {
            var a = pts[i]; var b = pts[i + 1];
            var len = a.DistanceTo(b);
            if (len < 1) continue;
            var dz = Math.Abs(b.Z - a.Z);
            if (dz / len < 0.26) horizontal.Add((a, b));      // < ~15° respecto a la horizontal
            else if (dz / len > 0.9) vertical.Add((a, b));    // > ~64° (gancho)
        }
        if (horizontal.Count == 0) return NotApplicable(bar, "La barra no tiene tramos horizontales (estribo o barra vertical).");

        // Tramo principal: el más largo
        var main = horizontal.OrderByDescending(s => s.A.DistanceTo(s.B)).First();
        var dir = (main.B.XY - main.A.XY).Normalized();
        var u = joint.InwardDirection;
        var along = GeometryUtil.SameDirection(dir, u, 15) || GeometryUtil.OppositeDirection(dir, u, 15);
        if (!along) return NotApplicable(bar, "La barra no es paralela al eje de la viga.");

        // Barra cerrada (estribo) → descartar
        if (pts[0].DistanceTo(pts[^1]) < bar.Diameter * 2 && pts.Count > 3)
            return NotApplicable(bar, "Barra cerrada (estribo).");

        // Capa
        var z = (main.A.Z + main.B.Z) / 2;
        var beam = joint.Beam;
        var layer = Math.Abs(z - beam.LayerElevation(BarLayer.Top)) <= Math.Abs(z - beam.LayerElevation(BarLayer.Bottom))
            ? BarLayer.Top : BarLayer.Bottom;

        // Profundidad alcanzada dentro de la columna (medida desde la cara de contacto a lo largo de u)
        double Depth(Vec3 p) => (p.XY - joint.ContactPoint).Dot(u);
        var poly = column.Outline;
        var maxDepth = double.MinValue;
        var touchesColumn = false;
        foreach (var (a, b) in horizontal)
        {
            if (poly.ClipSegmentLength(new Segment2D(a.XY, b.XY)) > GeometryUtil.Tolerance) touchesColumn = true;
            maxDepth = Math.Max(maxDepth, Math.Max(Depth(a), Depth(b)));
        }
        if (!touchesColumn || maxDepth <= 0)
            return new BarCheckResult(bar.Id, bar.BeamName, layer, AnchorageDecision.NotConnected, CheckStatus.Fail, 0, Math.Max(0, maxDepth),
                "La barra no entra en la columna.");

        var ctx = new AnchorageContext
        {
            BarDiameter = bar.Diameter,
            Materials = _options.Materials,
            IsTopBar = layer == BarLayer.Top,
            Seismic = _options.SeismicJoint,
            EdgeDistance = column.Cover + column.TieDiameter
        };
        var code = _options.Code;

        // Pasante: sale por la cara opuesta
        if (maxDepth >= joint.AvailableDepth - GeometryUtil.Tolerance)
        {
            var min = code.MinimumJointDimensionForPassThrough(ctx);
            var ok = !min.HasValue || joint.AvailableDepth >= min.Value;
            return new BarCheckResult(bar.Id, bar.BeamName, layer, AnchorageDecision.PassThrough, ok ? CheckStatus.Ok : CheckStatus.Fail,
                min ?? 0, joint.AvailableDepth,
                ok ? "Barra pasante por el nudo."
                   : $"Barra pasante: la columna ({joint.AvailableDepth:0} mm) es menor que {min!.Value:0} mm (20·db).");
        }

        // ¿Tiene gancho dentro de la columna?
        var hasHook = vertical.Any(s => s.A.DistanceTo(s.B) >= 4 * bar.Diameter && Depth(s.A) > 0);
        var usable = joint.UsableDepth;

        if (hasHook)
        {
            var ldh = code.HookDevelopmentLength(ctx);
            var provided = maxDepth + bar.Diameter / 2; // hasta la cara exterior del gancho
            var ok = provided + GeometryUtil.Tolerance >= ldh.Length;
            var msg = ok
                ? $"Gancho 90°: ldh provisto {provided:0} ≥ requerido {ldh.Length:0} mm."
                : $"Gancho 90°: ldh provisto {provided:0} < requerido {ldh.Length:0} mm (faltan {ldh.Length - provided:0} mm).";
            if (provided > usable + 1)
                msg += $" El gancho invade el recubrimiento de la columna ({provided - usable:0} mm más allá del núcleo).";
            return new BarCheckResult(bar.Id, bar.BeamName, layer, AnchorageDecision.Hook90, ok ? CheckStatus.Ok : CheckStatus.Fail,
                ldh.Length, provided, msg);
        }
        else
        {
            var ld = code.StraightDevelopmentLength(ctx);
            var ldh = code.HookDevelopmentLength(ctx);
            var ok = maxDepth + GeometryUtil.Tolerance >= ld.Length;
            string msg;
            if (ok) msg = $"Anclaje recto: ld provisto {maxDepth:0} ≥ requerido {ld.Length:0} mm.";
            else if (usable >= ldh.Length)
                msg = $"Anclaje recto insuficiente ({maxDepth:0} < {ld.Length:0} mm). Añadir gancho 90°: ldh requerido {ldh.Length:0} mm, útil {usable:0} mm.";
            else
                msg = $"Anclaje recto insuficiente ({maxDepth:0} < {ld.Length:0} mm) y tampoco cabe gancho (ldh {ldh.Length:0} > útil {usable:0} mm).";
            if (maxDepth > usable + 1)
                msg += $" La barra invade el recubrimiento de la columna ({maxDepth - usable:0} mm).";
            return new BarCheckResult(bar.Id, bar.BeamName, layer, AnchorageDecision.Straight, ok ? CheckStatus.Ok : CheckStatus.Fail,
                ld.Length, maxDepth, msg);
        }
    }

    private static BarCheckResult NotApplicable(ExistingBar bar, string msg)
        => new(bar.Id, bar.BeamName, null, AnchorageDecision.NotConnected, CheckStatus.NotApplicable, 0, 0, msg);
}
