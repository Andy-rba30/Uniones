using UnionesAcero.Core.Analysis;
using UnionesAcero.Core.Codes;
using UnionesAcero.Core.Geometry;
using UnionesAcero.Core.Model;

namespace UnionesAcero.Core.Verification;

/// <summary>
/// Corrección propuesta para una barra existente que no cumple el anclaje: la misma barra,
/// conservada desde su extremo lejano, cortada en la columna y prolongada con el anclaje que
/// cabe (recto hasta el núcleo o gancho a 90°). No cambia la barra en el resto de la viga.
/// </summary>
public sealed class BarFix
{
    public required ExistingBar Bar { get; init; }
    public required BeamJoint Joint { get; init; }
    public required BarLayer Layer { get; init; }
    public required AnchorageDecision Decision { get; init; }

    /// <summary>Eje de la barra corregida SIN el gancho, del extremo lejano al vértice en la columna (mm).</summary>
    public required IReadOnlyList<Vec3> Centerline { get; set; }

    /// <summary>Eje completo con el tramo del gancho (esquinas vivas), para dibujar.</summary>
    public required IReadOnlyList<Vec3> FullCenterline { get; set; }

    public HookSpec? EndHook { get; init; }

    /// <summary>Normal del plano de la barra (dirección lateral de la viga).</summary>
    public required Vec3 PlaneNormal { get; init; }

    /// <summary>Cota del tramo que entra en la columna (mm).</summary>
    public required double Elevation { get; set; }

    public required double RequiredLength { get; init; }
    public required double ProvidedLength { get; set; }
    public required string Formula { get; init; }
    public required string Message { get; set; }

    /// <summary>El extremo que entra en la columna era el inicio de la barra original (se recorre al revés).</summary>
    public bool Reversed { get; init; }

    /// <summary>Cuánto se desplaza el extremo de la barra hacia el interior de la columna (mm; negativo = se recorta).</summary>
    public double EndShift { get; set; }

    /// <summary>Corrimiento lateral aplicado a toda la barra (mm, positivo en el sentido de la lateral de la viga).</summary>
    public double LateralShift { get; set; }

    /// <summary>Nueva longitud de la distribución del conjunto (mm) si se cambió; null = la original.</summary>
    public double? SetLength { get; set; }

    /// <summary>Cambio de cota aplicado a toda la barra (mm; negativo = baja).</summary>
    public double ElevationShift { get; set; }

    /// <summary>Ajustes de detallado aplicados en el nudo (corrimientos, cambios de cota, retrasos del gancho).</summary>
    public List<string> Adjustments { get; } = new();

    public double TotalLength
    {
        get
        {
            double l = 0;
            for (var i = 0; i + 1 < FullCenterline.Count; i++) l += FullCenterline[i].DistanceTo(FullCenterline[i + 1]);
            return l;
        }
    }

    public string Label => $"{Bar.BeamName} {(Layer == BarLayer.Top ? "sup." : "inf.")} {(Bar.Count > 1 ? Bar.Count + "x" : "")}Ø{Bar.Diameter:0} [{Bar.Id}]";
}

/// <summary>Propone cómo corregir las barras existentes que no cumplen.</summary>
public sealed class BarFixer
{
    private readonly JointOptions _options;

    public BarFixer(JointOptions? options = null)
    {
        _options = options ?? new JointOptions();
    }

    /// <summary>
    /// Corrección de una barra que no cumple. Devuelve null, con el motivo, cuando no hay una
    /// corrección geométrica posible (columna demasiado pequeña, barra pasante, barra no reconocible).
    /// </summary>
    public BarFix? Plan(ColumnSection column, BeamJoint joint, ExistingBar bar, BarCheckResult check, out string? reason)
    {
        reason = null;
        if (check.Status == CheckStatus.Ok) { reason = "la barra ya cumple."; return null; }
        if (check.Status == CheckStatus.NotApplicable) { reason = check.Message; return null; }
        if (!joint.Connected) { reason = "la viga no está conectada a la columna."; return null; }
        if (check.Decision == AnchorageDecision.PassThrough)
        {
            reason = $"barra pasante: la columna ({joint.AvailableDepth:0} mm) no alcanza los {check.Required:0} mm (20·db) que exige la norma; no se corrige cambiando la barra.";
            return null;
        }

        var layer = check.Layer ?? BarLayer.Top;
        if (!TryOrientTowardsColumn(bar, joint, out var pts, out var k, out var reversed))
        {
            reason = pts.Count < 2 ? "la barra no tiene geometría de eje." : "la barra no tiene un tramo recto paralelo a la viga que vaya hacia la columna.";
            return null;
        }

        var u = joint.InwardDirection;
        var v = joint.LateralDirection;
        double Depth(Vec3 p) => (p.XY - joint.ContactPoint).Dot(u);

        var start = pts[k];
        var next = pts[k + 1];
        var dirXy = (next.XY - start.XY).Normalized();
        var cosU = dirXy.Dot(u);
        var z = next.Z;
        var db = bar.Diameter;
        var beam = joint.Beam;
        var code = _options.Code;
        var ctx = new AnchorageContext
        {
            BarDiameter = db,
            Materials = _options.Materials,
            IsTopBar = layer == BarLayer.Top,
            Seismic = _options.SeismicJoint,
            HookConfinedInColumnCore = true,
            EdgeDistance = column.Cover + column.TieDiameter
        };
        var ld = code.StraightDevelopmentLength(ctx);
        var ldh = code.HookDevelopmentLength(ctx);
        var usable = RoundDown(joint.UsableDepth);

        AnchorageDecision decision;
        double required, provided, cornerDepth;
        string formula;
        HookSpec? hook = null;
        if (usable >= ld.Length)
        {
            decision = AnchorageDecision.Straight;
            required = ld.Length;
            formula = ld.Formula;
            provided = _options.EmbedToFarFace ? usable : RoundUp(ld.Length);
            cornerDepth = provided;
        }
        else if (usable >= ldh.Length)
        {
            decision = AnchorageDecision.Hook90;
            required = ldh.Length;
            formula = ldh.Formula;
            provided = _options.EmbedToFarFace ? usable : RoundUp(ldh.Length);
            cornerDepth = provided - db / 2; // la cara exterior del gancho queda en 'provided'
            var ext = code.HookExtension(db, HookAngle.Hook90);
            var hookDir = JointAnalyzer.HookDirection(column, beam, layer, z, ext, out _);
            hook = new HookSpec(HookAngle.Hook90, ext, hookDir, code.BendInnerDiameter(db));
        }
        else
        {
            reason = $"ni el anclaje recto (ld = {ld.Length:0} mm) ni el gancho a 90° (ldh = {ldh.Length:0} mm) caben en los {usable:0} mm útiles " +
                     "de la columna en la dirección de la viga: reducir el diámetro, agrandar la columna o usar barras con cabeza.";
            return null;
        }

        var t = (cornerDepth - Depth(start)) / cosU;
        if (t < 8 * db)
        {
            reason = "el tramo recto que quedaría dentro de la viga es demasiado corto; la barra empieza casi en la columna.";
            return null;
        }
        var corner = Vec3.From(start.XY + dirXy * t, z);
        var centerline = new List<Vec3>(pts.Take(k + 1)) { corner };
        var full = new List<Vec3>(centerline);
        if (hook != null) full.Add(corner + hook.Direction * hook.Extension);

        var endShift = t - (next.XY - start.XY).Length;

        var what = decision == AnchorageDecision.Straight
            ? $"anclaje recto hasta {provided:0} mm dentro de la columna (ld = {required:0})"
            : $"gancho a 90° {(hook!.Direction.Z < 0 ? "hacia abajo" : "hacia arriba")} con {provided:0} mm hasta la cara exterior del gancho (ldh = {required:0})";
        var msg = $"Corregir: {what}. " +
                  (check.Provided > 0 ? $"Ahora tiene {check.Provided:0} mm." : "Ahora no entra en la columna.");
        if (joint.OppositeBeam != null)
            msg += $" Hay una viga opuesta ({joint.OppositeBeam.Name}): considerar hacer la barra continua a través del nudo.";

        return new BarFix
        {
            Bar = bar, Joint = joint, Layer = layer, Decision = decision, Centerline = centerline, FullCenterline = full, EndHook = hook,
            PlaneNormal = Vec3.From(v, 0), Elevation = z, RequiredLength = required, ProvidedLength = provided, Formula = formula,
            Message = msg, Reversed = reversed, EndShift = endShift
        };
    }

    /// <summary>
    /// Orienta el eje sin ganchos de la barra de modo que el extremo que entra en la columna quede al
    /// final, y localiza el último tramo recto paralelo al eje de la viga (índice <paramref name="k"/>
    /// de su primer vértice). Devuelve false si no hay tal tramo.
    /// </summary>
    public static bool TryOrientTowardsColumn(ExistingBar bar, BeamJoint joint, out List<Vec3> pts, out int k, out bool reversed)
    {
        pts = (bar.BareCenterline ?? bar.Centerline).ToList();
        k = -1; reversed = false;
        if (pts.Count < 2) return false;
        var u = joint.InwardDirection;
        double Depth(Vec3 p) => (p.XY - joint.ContactPoint).Dot(u);
        reversed = Depth(pts[0]) > Depth(pts[^1]);
        if (reversed) pts.Reverse();
        for (var i = pts.Count - 2; i >= 0; i--)
        {
            var a = pts[i]; var b = pts[i + 1];
            var len = a.DistanceTo(b);
            if (len < 1) continue;
            if (Math.Abs(b.Z - a.Z) / len >= 0.26) continue;
            var dir = (b.XY - a.XY).Normalized();
            if (!GeometryUtil.SameDirection(dir, u, 15)) continue;
            k = i;
            return true;
        }
        return false;
    }

    private double RoundUp(double v) => GeometryUtil.RoundUpTo(v, _options.LengthRounding);

    private double RoundDown(double v)
    {
        var m = _options.LengthRounding;
        return m <= 0 ? v : Math.Floor(v / m + 1e-9) * m;
    }
}
