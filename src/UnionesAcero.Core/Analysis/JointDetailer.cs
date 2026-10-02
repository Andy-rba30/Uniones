using UnionesAcero.Core.Geometry;
using UnionesAcero.Core.Model;
using UnionesAcero.Core.Verification;

namespace UnionesAcero.Core.Analysis;

/// <summary>Barra existente que se conserva tal cual: participa en la resolución de choques como obstáculo fijo.</summary>
public sealed record KeptBar(BeamJoint Joint, BarLayer Layer, ExistingBar Bar);

/// <summary>
/// Resuelve en el nudo los choques entre las barras ya decididas (nuevas o corregidas), como lo haría
/// el detallador antes de armar:
/// <list type="number">
/// <item>Las barras de la viga pasan por dentro de las verticales de la columna: la que coincide con
/// una vertical se corre en planta hacia el eje de la viga (toda la barra, para que siga recta).</item>
/// <item>En la misma esquina, el extremo de la viga secundaria se retrasa para que su gancho no
/// atraviese las barras ni los ganchos de la viga principal, sin bajar del anclaje requerido.</item>
/// <item>Si dos vigas se cruzan a la misma cota, la capa de la secundaria baja (superior) o sube
/// (inferior) un diámetro más la separación libre.</item>
/// </list>
/// La viga principal es la de mayor <see cref="BeamSection.CrossingPriority"/>; a igualdad, la de
/// mayor peralte, luego mayor ancho, luego barras de mayor diámetro y, por último, la primera en la
/// lista. Las barras existentes que se conservan nunca se mueven: las demás ceden ante ellas.
/// </summary>
public sealed class JointDetailer
{
    private readonly JointOptions _options;

    public JointDetailer(JointOptions? options = null)
    {
        _options = options ?? new JointOptions();
    }

    /// <summary>
    /// Aplica las reglas sobre las barras nuevas del análisis (solo las capas para las que
    /// <paramref name="useNewGroup"/> devuelve true; null = todas), las correcciones y las barras
    /// existentes que se conservan. Modifica en sitio los grupos del análisis y las correcciones y
    /// añade diagnósticos a cada viga.
    /// </summary>
    public void Apply(JointAnalysis analysis, IReadOnlyList<BarFix>? fixes = null, IReadOnlyList<KeptBar>? kept = null,
                      Func<BeamJoint, BarLayer, bool>? useNewGroup = null)
    {
        var layers = new List<LayerBars>();
        var order = new Dictionary<BeamJoint, int>();
        var idx = 0;
        foreach (var joint in analysis.Joints)
        {
            order[joint] = idx++;
            if (!joint.Connected) continue;
            foreach (var lr in joint.Layers)
            {
                if (lr.Group == null) continue;
                if (useNewGroup != null && !useNewGroup(joint, lr.Layer)) continue;
                layers.Add(LayerBars.FromGroup(joint, lr, order[joint]));
            }
        }
        foreach (var f in fixes ?? Array.Empty<BarFix>())
        {
            var joint = analysis.Joints.FirstOrDefault(j => ReferenceEquals(j, f.Joint)) ?? f.Joint;
            if (!joint.Connected) continue;
            layers.Add(LayerBars.FromFix(joint, f, order.TryGetValue(joint, out var o) ? o : idx));
        }
        foreach (var kb in kept ?? Array.Empty<KeptBar>())
        {
            if (!kb.Joint.Connected) continue;
            var lb = LayerBars.FromKept(kb, order.TryGetValue(kb.Joint, out var o) ? o : idx);
            if (lb != null) layers.Add(lb);
        }
        if (layers.Count == 0) return;

        if (_options.ResolveColumnBarClash)
            foreach (var l in layers.Where(l => l.Movable && !l.LateralLocked)) ClearColumnBars(analysis.Column, l);
        if (_options.ResolveHookLegClash) ResolveHookLegs(layers);
        ResolveCrossings(layers);
        foreach (var l in layers) l.Commit();
    }

    // ---------------------------------------------------------------------------------
    // Regla 1: pasar por dentro de las verticales de la columna
    // ---------------------------------------------------------------------------------

    private void ClearColumnBars(ColumnSection column, LayerBars l)
    {
        var positions = column.BarPositionsForClearance();
        if (positions.Count == 0 || l.Laterals.Count == 0) return;
        var beam = l.Joint.Beam;
        var db = l.Diameter;
        var rWanted = (db + column.LongitudinalBarDiameter) / 2 + _options.ColumnBarClearance;
        var rTouch = (db + column.LongitudinalBarDiameter) / 2 + 1;
        var half = beam.Width / 2 - beam.Cover - beam.StirrupDiameter - db / 2;
        if (half <= 0) return;

        var end = l.InColumnEnd;
        var verts = positions
            .Select(p => (Along: (p - l.C).Dot(l.U), Lat: (p - l.C).Dot(l.V)))
            .Where(p => p.Along >= -rWanted && p.Along <= end + rWanted && Math.Abs(p.Lat) <= half + rWanted)
            .Select(p => p.Lat).ToList();
        if (verts.Count == 0) return;

        var moved = new List<string>();
        var touching = false;
        var unresolved = 0;
        var gapClear = db + Math.Max(25, db);   // entre ejes, con separación libre reglamentaria
        var gapContact = db + 1;                // entre ejes, barras en contacto
        foreach (var i in Enumerable.Range(0, l.Laterals.Count).OrderByDescending(i => Math.Abs(l.Laterals[i])))
        {
            var y = l.Laterals[i];
            if (verts.All(p => Math.Abs(y - p) >= rWanted - 1e-6)) continue;
            var others = l.Laterals.Where((_, k) => k != i).ToList();
            // Primero con la separación deseada a la vertical; si no cabe, en contacto con la vertical;
            // y en último caso admitiendo también el contacto con las otras barras de la viga.
            var best = Best(y, verts, rWanted, half, others, gapClear)
                       ?? Best(y, verts, rTouch, half, others, gapClear)
                       ?? Best(y, verts, rTouch, half, others, gapContact);
            if (best == null) { unresolved++; continue; }
            if (verts.Any(p => Math.Abs(best.Value - p) < rWanted - 1e-6)) touching = true;
            l.Laterals[i] = best.Value;
            var dy = best.Value - y;
            moved.Add($"barra {i + 1}: {dy:+0;-0} mm" + (Math.Abs(best.Value) < Math.Abs(y) - 0.5 ? " hacia el eje" : ""));
        }

        if (l.Group != null) l.Laterals.Sort();
        else if (l.Laterals.Count > 1)
        {
            // Conjunto existente: la primera barra manda y las demás se reparten en el mismo sentido.
            var span = (l.Laterals[^1] - l.Laterals[0]) * l.SetSign;
            if (span < db)
            {
                l.Laterals[^1] = l.Laterals[0] + l.SetSign * db;
                l.Warn($"Capa {JointAnalyzer.LayerName(l.Layer)}: tras correr las barras para pasar por dentro de las verticales de la columna quedan en contacto entre sí; en obra se colocan pegadas o se usan menos barras.");
            }
        }

        if (moved.Count > 0)
        {
            var text = $"Capa {JointAnalyzer.LayerName(l.Layer)}: barra(s) corrida(s) en planta para pasar por dentro de las verticales de la columna ({string.Join(", ", moved)})" +
                       (touching ? $"; no cabe la separación libre deseada de {_options.ColumnBarClearance:0} mm y quedan en contacto con la vertical" : "") + ".";
            l.Adjust($"corrida(s) en planta: {string.Join(", ", moved)}" + (touching ? " (en contacto con la vertical de la columna)" : ""));
            l.Info(text);
        }
        if (unresolved > 0)
            l.Warn($"Capa {JointAnalyzer.LayerName(l.Layer)}: {unresolved} barra(s) coinciden con verticales de la columna y no hay sitio en el ancho de la viga para correrlas; revisar en obra (menos barras o paquete).");

        // Separación libre entre las barras de la viga tras el corrimiento
        var sorted = l.Laterals.OrderBy(x => x).ToList();
        var minClear = Math.Max(25, db);
        for (var i = 0; i + 1 < sorted.Count; i++)
        {
            var clear = sorted[i + 1] - sorted[i] - db;
            if (clear < minClear - 0.5 && moved.Count > 0)
            {
                l.Warn($"Capa {JointAnalyzer.LayerName(l.Layer)}: tras el corrimiento la separación libre entre barras queda en {Math.Max(0, clear):0} mm (< {minClear:0} mm); en obra irán en contacto o habrá que usar menos barras.");
                break;
            }
        }
        if (l.Laterals.Count > 2 && l.Group != null)
        {
            // Reparto uniforme entre la primera y la última: comprobar las intermedias
            var spacing = (sorted[^1] - sorted[0]) / (sorted.Count - 1);
            for (var i = 1; i + 1 < sorted.Count; i++)
            {
                var y = sorted[0] + i * spacing;
                if (verts.Any(p => Math.Abs(y - p) < rTouch - 1e-6))
                {
                    l.Warn($"Capa {JointAnalyzer.LayerName(l.Layer)}: la barra intermedia {i + 1} sigue coincidiendo con una vertical de la columna; revisar en obra.");
                    break;
                }
            }
        }
    }

    private static double? Best(double y, List<double> verts, double r, double half, List<double> others, double gap)
    {
        var candidates = new List<double> { y };
        foreach (var p in verts) { candidates.Add(p - r); candidates.Add(p + r); }
        foreach (var o in others) { candidates.Add(o - gap); candidates.Add(o + gap); }
        double? best = null;
        foreach (var c in candidates)
        {
            if (Math.Abs(c) > half + 1e-6) continue;
            if (verts.Any(p => Math.Abs(c - p) < r - 1e-6)) continue;
            if (others.Any(o => Math.Abs(c - o) < gap - 1e-6)) continue;
            if (best == null || Math.Abs(c - y) < Math.Abs(best.Value - y) - 1e-9 ||
                (Math.Abs(Math.Abs(c - y) - Math.Abs(best.Value - y)) <= 1e-9 && Math.Abs(c) < Math.Abs(best.Value)))
                best = c;
        }
        return best;
    }

    // ---------------------------------------------------------------------------------
    // Regla 2: ganchos y barras en la misma esquina
    // ---------------------------------------------------------------------------------

    private void ResolveHookLegs(List<LayerBars> layers)
    {
        foreach (var (a, b) in Pairs(layers))
        {
            var (p, s, why) = Rank(a, b);
            if (s == null) continue;
            if (why == "primera en la lista")
            {
                // Vigas iguales: cede la que tiene más holgura de anclaje (puede retrasar más su gancho).
                var slackP = p.Provided - p.Retreat - p.Required;
                var slackS = s.Provided - s.Retreat - s.Required;
                if (slackP > slackS + 0.5) { (p, s) = (s, p); }
                if (Math.Abs(slackP - slackS) > 0.5) why = "misma sección: cede la viga con más holgura de anclaje";
            }
            var rWanted = (p.Diameter + s.Diameter) / 2 + _options.CrossingClearance;
            var rContact = (p.Diameter + s.Diameter) / 2 + 1;
            if (!HasLegConflict(p, s, s.CornerDepth - s.Retreat, rWanted)) continue;

            // Orden de preferencia: la secundaria con separación; la principal con separación (si la
            // secundaria no puede); y solo después admitir el contacto, habitual en obra.
            var swappedWhy = $"{s.Joint.Beam.Name} no puede retrasar su gancho sin perder el anclaje requerido y cede {p.Joint.Beam.Name}";
            var attempts = new (LayerBars Fixed, LayerBars Yields, double R, bool Contact, string Why)[]
            {
                (p, s, rWanted, false, why), (s, p, rWanted, false, swappedWhy),
                (p, s, rContact, true, why), (s, p, rContact, true, swappedWhy)
            };
            var done = false;
            foreach (var (fixedL, yields, r, contact, text) in attempts)
            {
                var depth = Fit(fixedL, yields, r);
                if (depth == null) continue;
                var depth0 = yields.CornerDepth - yields.Retreat;
                var retreat = depth0 - depth.Value;
                if (retreat > 1e-6)
                {
                    yields.Retreat += retreat;
                    var provided = yields.Provided - yields.Retreat;
                    var how = contact ? " (en contacto: no cabe la separación libre)" : "";
                    yields.Adjust($"extremo retrasado {retreat:0} mm por los ganchos de {fixedL.Joint.Beam.Name} (prov. {provided:0} ≥ req. {yields.Required:0}){how}");
                    yields.Info($"Capa {JointAnalyzer.LayerName(yields.Layer)}: el extremo se retrasa {retreat:0} mm (prov. {yields.Provided:0} → {provided:0} mm ≥ {yields.Required:0} requerido) para que su gancho no atraviese las barras ni los ganchos de {fixedL.Joint.Beam.Name} en la misma esquina ({text}){how}.");
                }
                else if (contact)
                {
                    yields.Warn($"Capa {JointAnalyzer.LayerName(yields.Layer)}: el gancho queda a menos de {_options.CrossingClearance:0} mm de las barras o los ganchos de {fixedL.Joint.Beam.Name} en la misma esquina (en contacto); no hay sitio para retrasarlo con separación.");
                }
                done = true;
                break;
            }
            if (done) continue;
            if (s.PassThrough)
                s.Warn($"Capa {JointAnalyzer.LayerName(s.Layer)}: las barras pasantes atraviesan los ganchos de {p.Joint.Beam.Name} en el nudo; revisar en obra (desplazar lateralmente o reordenar).");
            else if (!s.NoRetreat)
                s.Warn($"Capa {JointAnalyzer.LayerName(s.Layer)}: el gancho choca con las barras o los ganchos de {p.Joint.Beam.Name} en la misma esquina y ninguna de las dos vigas puede retrasar el suyo sin perder el anclaje requerido; revisar en obra.");
        }
    }

    /// <summary>
    /// Profundidad del extremo de <paramref name="yields"/> (sin cambiar <paramref name="fixedL"/>) a la
    /// que desaparece el choque con separación <paramref name="r"/>: la actual si ya no choca, o la
    /// mayor posible retrocediendo a saltos sin bajar del anclaje requerido. Null si no la hay.
    /// </summary>
    private double? Fit(LayerBars fixedL, LayerBars yields, double r)
    {
        if (!yields.Movable || yields.NoRetreat || yields.PassThrough) return null;
        var d0 = yields.CornerDepth - yields.Retreat;
        if (!HasLegConflict(fixedL, yields, d0, r)) return d0;
        var step = Math.Max(5, _options.LengthRounding);
        for (var depth = d0 - step; depth >= yields.MinCorner - 1e-6; depth -= step)
            if (!HasLegConflict(fixedL, yields, depth, r)) return depth;
        return null;
    }

    private static bool HasLegConflict(LayerBars p, LayerBars s, double sDepth, double r)
    {
        var pEnd = p.InColumnEnd;
        var sEnd = Math.Min(sDepth, s.Joint.AvailableDepth);
        // Ganchos de la principal contra barras de la secundaria
        foreach (var (leg, z1, z2) in p.Legs(p.CornerDepth - p.Retreat))
        {
            if (s.Z < z1 - r || s.Z > z2 + r) continue;
            foreach (var seg in s.Segments(sEnd))
                if (seg.DistanceTo(leg) < r) return true;
        }
        // Ganchos de la secundaria contra barras de la principal
        foreach (var (leg, z1, z2) in s.Legs(sDepth))
        {
            if (p.Z < z1 - r || p.Z > z2 + r) continue;
            foreach (var seg in p.Segments(pEnd))
                if (seg.DistanceTo(leg) < r) return true;
        }
        // Ganchos contra ganchos (patas paralelas)
        foreach (var (lp, pz1, pz2) in p.Legs(p.CornerDepth - p.Retreat))
            foreach (var (ls, sz1, sz2) in s.Legs(sDepth))
                if (Math.Max(pz1, sz1) <= Math.Min(pz2, sz2) + r && lp.DistanceTo(ls) < r) return true;
        return false;
    }

    // ---------------------------------------------------------------------------------
    // Regla 3: cruces a la misma cota
    // ---------------------------------------------------------------------------------

    private void ResolveCrossings(List<LayerBars> layers)
    {
        foreach (var (a, b) in Pairs(layers))
        {
            var r = (a.Diameter + b.Diameter) / 2 + _options.CrossingClearance;
            var minSep = r;
            var dz = Math.Abs(a.Z + a.Dz - (b.Z + b.Dz));
            if (dz >= minSep) continue;
            if (!CrossInPlan(a, b, r)) continue;

            var (p, s, why) = Rank(a, b);
            if (s == null)
            {
                a.Warn($"Capa {JointAnalyzer.LayerName(a.Layer)}: las barras existentes se cruzan con las de {b.Joint.Beam.Name} a la misma cota (Δz = {dz:0} mm < {minSep:0} mm) y ninguna se modifica; revisar en obra.");
                continue;
            }
            if (!_options.AutoStaggerCrossingLayers)
            {
                s.Warn($"Capa {JointAnalyzer.LayerName(s.Layer)}: las barras se cruzan con las de {p.Joint.Beam.Name} a la misma cota (Δz = {dz:0} mm < {minSep:0} mm). Desplazar una capa.");
                continue;
            }
            var pz = p.Z + p.Dz;
            var target = s.Layer == BarLayer.Top ? pz - minSep : pz + minSep;
            var shift = target - (s.Z + s.Dz);
            if (Math.Abs(shift) < 0.5) continue;
            s.Dz += shift;
            s.Adjust($"capa {shift:+0;-0} mm para cruzar {(shift < 0 ? "por debajo" : "por encima")} de {p.Joint.Beam.Name}");
            s.Info($"Capa {JointAnalyzer.LayerName(s.Layer)}: las barras se cruzan con las de {p.Joint.Beam.Name} en el nudo; {p.Joint.Beam.Name} es la viga principal ({why}) y la capa de {s.Joint.Beam.Name} se desplaza {shift:+0;-0} mm para pasar {(shift < 0 ? "por debajo" : "por encima")}.");
        }
    }

    private static bool CrossInPlan(LayerBars a, LayerBars b, double r)
    {
        foreach (var sa in a.Polylines())
            foreach (var sb in b.Polylines())
                if (sa.DistanceTo(sb) < r) return true;
        return false;
    }

    // ---------------------------------------------------------------------------------
    // Jerarquía y utilidades
    // ---------------------------------------------------------------------------------

    private static IEnumerable<(LayerBars, LayerBars)> Pairs(List<LayerBars> layers)
    {
        for (var i = 0; i < layers.Count; i++)
            for (var k = i + 1; k < layers.Count; k++)
            {
                var a = layers[i]; var b = layers[k];
                if (ReferenceEquals(a.Joint, b.Joint) || a.Joint.Beam == b.Joint.Beam) continue;
                if (a.Joint.OppositeBeam == b.Joint.Beam || b.Joint.OppositeBeam == a.Joint.Beam) continue;
                yield return (a, b);
            }
    }

    /// <summary>Principal, secundaria (la que cede; null si ninguna se puede mover) y el criterio que decidió.</summary>
    private static (LayerBars P, LayerBars? S, string Why) Rank(LayerBars a, LayerBars b)
    {
        if (!a.Movable && !b.Movable) return (a, null, "ninguna se modifica");
        if (!a.Movable) return (a, b, "sus barras ya están modeladas y no se tocan");
        if (!b.Movable) return (b, a, "sus barras ya están modeladas y no se tocan");
        var ba = a.Joint.Beam; var bb = b.Joint.Beam;
        if (ba.CrossingPriority != bb.CrossingPriority)
            return ba.CrossingPriority > bb.CrossingPriority ? (a, b, "elegida como principal") : (b, a, "elegida como principal");
        if (Math.Abs(ba.Depth - bb.Depth) > 0.5)
            return ba.Depth > bb.Depth ? (a, b, "mayor peralte") : (b, a, "mayor peralte");
        if (Math.Abs(ba.Width - bb.Width) > 0.5)
            return ba.Width > bb.Width ? (a, b, "mayor ancho") : (b, a, "mayor ancho");
        var da = Math.Max(ba.TopBarDiameter, ba.BottomBarDiameter);
        var dbb = Math.Max(bb.TopBarDiameter, bb.BottomBarDiameter);
        if (Math.Abs(da - dbb) > 0.5)
            return da > dbb ? (a, b, "barras de mayor diámetro") : (b, a, "barras de mayor diámetro");
        return a.Order <= b.Order ? (a, b, "primera en la lista") : (b, a, "primera en la lista");
    }

    /// <summary>Una capa de barras de una viga (nuevas, corregidas o existentes) vista en planta y alzado.</summary>
    private sealed class LayerBars
    {
        public required BeamJoint Joint { get; init; }
        public required BarLayer Layer { get; init; }
        public required double Diameter { get; init; }
        public required double Z { get; init; }
        public required int Order { get; init; }
        /// <summary>Posiciones laterales de cada barra respecto al eje de la viga (mm); la primera es la barra prototipo.</summary>
        public List<double> Laterals { get; } = new();
        public List<double> Original { get; } = new();
        /// <summary>Sentido en el que se reparte el conjunto existente respecto a la lateral (+1/−1).</summary>
        public int SetSign { get; init; } = 1;
        /// <summary>Conjunto repartido en una dirección que no es la lateral: no se corre en planta.</summary>
        public bool LateralLocked { get; init; }
        /// <summary>Profundidad del último vértice del eje dentro de la columna (mm desde la cara).</summary>
        public double CornerDepth { get; init; }
        public double Provided { get; init; }
        public double Required { get; init; }
        /// <summary>Profundidad mínima admisible del vértice (anclaje requerido).</summary>
        public double MinCorner { get; init; }
        public bool Hooked { get; init; }
        public double HookExt { get; init; }
        public Vec3 HookDir { get; init; }
        public bool PassThrough { get; init; }
        /// <summary>Capa con anclaje insuficiente: se mueve con las demás pero no se retrasa su extremo.</summary>
        public bool NoRetreat { get; init; }
        public bool Movable { get; init; }
        /// <summary>Eje (XY) de la barra prototipo, del extremo lejano al vértice en la columna.</summary>
        public required IReadOnlyList<Vec3> Base { get; init; }
        public BarGroup? Group { get; init; }
        public LayerResult? Result { get; init; }
        public BarFix? Fix { get; init; }

        public double Dz;
        public double Retreat;
        public List<string> Adjustments { get; } = new();

        public Vec2 C => Joint.ContactPoint;
        public Vec2 U => Joint.InwardDirection;
        public Vec2 V => Joint.LateralDirection;
        public double InColumnEnd => Math.Min(CornerDepth - Retreat, Joint.AvailableDepth);

        public void Info(string m) => AddOnce(Severity.Info, m);
        public void Warn(string m) => AddOnce(Severity.Warning, m);

        private void AddOnce(Severity sev, string m)
        {
            if (Joint.Diagnostics.Any(d => d.Message == m)) return;
            Joint.Diagnostics.Add(new Diagnostic(sev, Joint.Beam.Name, m));
        }
        public void Adjust(string m) => Adjustments.Add(m);

        public IEnumerable<Segment2D> Segments(double depth)
        {
            var end = Math.Max(0, Math.Min(depth, Joint.AvailableDepth));
            foreach (var y in Laterals)
                yield return new Segment2D(C + V * y, C + U * end + V * y);
        }

        public IEnumerable<(Vec2 Point, double Z1, double Z2)> Legs(double depth)
        {
            if (!Hooked) yield break;
            var z = Z + Dz;
            var z1 = Math.Min(z, z + HookDir.Z * HookExt);
            var z2 = Math.Max(z, z + HookDir.Z * HookExt);
            foreach (var y in Laterals)
                yield return (C + U * depth + V * y, z1, z2);
        }

        /// <summary>Tramos en planta de todas las barras (eje prototipo trasladado a cada posición lateral).</summary>
        public IEnumerable<Segment2D> Polylines()
        {
            var y0 = Original[0];
            foreach (var y in Laterals)
            {
                var d = V * (y - y0);
                for (var i = 0; i + 1 < Base.Count; i++)
                {
                    var a = Base[i].XY + d;
                    var b = Base[i + 1].XY + d;
                    if (i + 2 == Base.Count && Retreat > 0) b -= U * Retreat;
                    yield return new Segment2D(a, b);
                }
            }
        }

        public static LayerBars FromGroup(BeamJoint joint, LayerResult lr, int order)
        {
            var g = lr.Group!;
            var last = g.Centerline[^1];
            var y0 = (last.XY - joint.ContactPoint).Dot(joint.LateralDirection);
            var depth = (last.XY - joint.ContactPoint).Dot(joint.InwardDirection);
            var pass = g.Decision == AnchorageDecision.PassThrough;
            var l = new LayerBars
            {
                Joint = joint, Layer = lr.Layer, Diameter = g.Diameter, Z = g.Elevation, Order = order,
                CornerDepth = depth, Provided = g.ProvidedLength, Required = g.RequiredLength,
                MinCorner = pass ? depth : g.EndHook != null ? g.RequiredLength - g.Diameter / 2 : g.RequiredLength,
                Hooked = g.EndHook != null, HookExt = g.EndHook?.Extension ?? 0, HookDir = g.EndHook?.Direction ?? new Vec3(0, 0, -1),
                PassThrough = pass, NoRetreat = lr.Decision == AnchorageDecision.Insufficient, Movable = true, Base = g.Centerline, Group = g, Result = lr
            };
            for (var i = 0; i < g.Count; i++) { l.Laterals.Add(y0 + i * g.Spacing); l.Original.Add(y0 + i * g.Spacing); }
            return l;
        }

        public static LayerBars FromFix(BeamJoint joint, BarFix f, int order)
        {
            var last = f.Centerline[^1];
            var y0 = (last.XY - joint.ContactPoint).Dot(joint.LateralDirection);
            var depth = (last.XY - joint.ContactPoint).Dot(joint.InwardDirection);
            var pass = f.Decision == AnchorageDecision.PassThrough;
            var (sign, locked, count) = SetLayout(f.Bar, joint);
            var l = new LayerBars
            {
                Joint = joint, Layer = f.Layer, Diameter = f.Bar.Diameter, Z = f.Elevation, Order = order,
                SetSign = sign, LateralLocked = locked,
                CornerDepth = depth, Provided = f.ProvidedLength, Required = f.RequiredLength,
                MinCorner = pass ? depth : f.EndHook != null ? f.RequiredLength - f.Bar.Diameter / 2 : f.RequiredLength,
                Hooked = f.EndHook != null, HookExt = f.EndHook?.Extension ?? 0, HookDir = f.EndHook?.Direction ?? new Vec3(0, 0, -1),
                PassThrough = pass, Movable = true, Base = f.Centerline, Fix = f
            };
            var step = count > 1 ? f.Bar.SetLength / (count - 1) : 0;
            for (var i = 0; i < count; i++) { l.Laterals.Add(y0 + i * step * sign); l.Original.Add(y0 + i * step * sign); }
            return l;
        }

        public static LayerBars? FromKept(KeptBar kb, int order)
        {
            var bar = kb.Bar;
            if (!BarFixer.TryOrientTowardsColumn(bar, kb.Joint, out var pts, out var k, out _)) return null;
            var joint = kb.Joint;
            var bare = pts.Take(k + 2).ToList();
            var last = bare[^1];
            var y0 = (last.XY - joint.ContactPoint).Dot(joint.LateralDirection);
            var depth = (last.XY - joint.ContactPoint).Dot(joint.InwardDirection);
            // Gancho: el eje con ganchos sigue más allá del extremo sin ganchos.
            var hooked = bar.Centerline;
            var tailEnd = hooked[0].DistanceTo(last) < hooked[^1].DistanceTo(last) ? hooked[0] : hooked[^1];
            var tail = tailEnd - last;
            var hasHook = tail.Length > 2 * bar.Diameter && Math.Abs(tail.Z) > 0.5 * tail.Length;
            var (sign, locked, count) = SetLayout(bar, joint);
            var l = new LayerBars
            {
                Joint = joint, Layer = kb.Layer, Diameter = bar.Diameter, Z = last.Z, Order = order,
                SetSign = sign, LateralLocked = locked,
                CornerDepth = depth, Provided = depth, Required = depth, MinCorner = depth,
                Hooked = hasHook, HookExt = hasHook ? tail.Length : 0, HookDir = hasHook ? tail.Normalized() : new Vec3(0, 0, -1),
                PassThrough = depth >= joint.AvailableDepth - 1, Movable = false, Base = bare
            };
            var step = count > 1 ? bar.SetLength / (count - 1) : 0;
            for (var i = 0; i < count; i++) { l.Laterals.Add(y0 + i * step * sign); l.Original.Add(y0 + i * step * sign); }
            return l;
        }

        private static (int Sign, bool Locked, int Count) SetLayout(ExistingBar bar, BeamJoint joint)
        {
            if (bar.Count <= 1 || bar.SetDirection == null || bar.SetLength <= 0) return (1, false, 1);
            var along = bar.SetDirection.Value.XY.Dot(joint.LateralDirection);
            if (Math.Abs(along) < 0.9) return (1, true, 1);
            return (along >= 0 ? 1 : -1, false, bar.Count);
        }

        /// <summary>Vuelca los ajustes acumulados en el grupo (sustituyéndolo en el análisis) o en la corrección.</summary>
        public void Commit()
        {
            if (!Movable) return;
            var dy = Laterals[0] - Original[0];
            var shift = V * dy;
            Vec3 Move(Vec3 p) => new(p.X + shift.X, p.Y + shift.Y, p.Z + Dz);
            if (Math.Abs(dy) < 1e-9 && Math.Abs(Dz) < 1e-9 && Retreat <= 1e-9 && Adjustments.Count == 0) return;

            if (Group != null && Result != null)
            {
                var g = Group;
                var cl = g.Centerline.Select(Move).ToList();
                if (Retreat > 1e-9) cl[^1] = cl[^1] - Vec3.From(U * Retreat, 0);
                var full = new List<Vec3>(cl);
                if (g.EndHook != null) full.Add(cl[^1] + g.EndHook.Direction * g.EndHook.Extension);
                var spacing = Laterals.Count > 1 ? (Laterals[^1] - Laterals[0]) / (Laterals.Count - 1) : 0;
                var notes = Adjustments.Count > 0 ? " [" + string.Join("; ", Adjustments) + "]" : "";
                var ng = g with
                {
                    Centerline = cl, FullCenterline = full, Spacing = spacing, Elevation = g.Elevation + Dz,
                    ProvidedLength = g.ProvidedLength - Retreat, Comment = g.Comment + notes,
                    Adjustments = new List<string>(g.Adjustments.Concat(Adjustments))
                };
                var idx = Joint.Layers.IndexOf(Result);
                if (idx < 0) return;
                Joint.Layers[idx] = new LayerResult
                {
                    Layer = Result.Layer, Decision = Result.Decision, Elevation = Result.Elevation + Dz,
                    StraightRequired = Result.StraightRequired, HookRequired = Result.HookRequired,
                    Provided = Result.Provided - Retreat, Formula = Result.Formula, Group = ng
                };
            }
            else if (Fix != null)
            {
                var f = Fix;
                var cl = f.Centerline.Select(Move).ToList();
                if (Retreat > 1e-9) cl[^1] = cl[^1] - Vec3.From(U * Retreat, 0);
                var full = new List<Vec3>(cl);
                if (f.EndHook != null) full.Add(cl[^1] + f.EndHook.Direction * f.EndHook.Extension);
                f.Centerline = cl;
                f.FullCenterline = full;
                f.LateralShift += dy;
                f.ElevationShift += Dz;
                f.Elevation += Dz;
                f.ProvidedLength -= Retreat;
                f.EndShift -= Retreat;
                if (Laterals.Count > 1)
                {
                    var newLen = Math.Abs(Laterals[^1] - Laterals[0]);
                    if (Math.Abs(newLen - f.Bar.SetLength) > 0.5) f.SetLength = newLen;
                }
                f.Adjustments.AddRange(Adjustments);
                if (Adjustments.Count > 0) f.Message += " Ajustes en el nudo: " + string.Join("; ", Adjustments) + ".";
            }
        }
    }
}
