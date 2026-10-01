using UnionesAcero.Core.Codes;
using UnionesAcero.Core.Geometry;
using UnionesAcero.Core.Model;

namespace UnionesAcero.Core.Analysis;

/// <summary>
/// Analiza el encuentro de una columna de sección arbitraria con las vigas que llegan a ella
/// y propone el detalle de anclaje de las barras longitudinales de cada viga.
/// </summary>
public sealed class JointAnalyzer
{
    private readonly JointOptions _options;

    public JointAnalyzer(JointOptions? options = null)
    {
        _options = options ?? new JointOptions();
    }

    public JointAnalysis Analyze(ColumnSection column, IEnumerable<BeamSection> beams)
    {
        var analysis = new JointAnalysis { Column = column, Options = _options };
        var beamList = beams.ToList();

        if (beamList.Count == 0)
        {
            analysis.Diagnostics.Add(new Diagnostic(Severity.Warning, column.Name, "No llega ninguna viga a la columna."));
            return analysis;
        }

        // 1. Contacto geométrico de cada viga con la columna.
        foreach (var beam in beamList)
            analysis.Joints.Add(ResolveContact(column, beam));

        // 2. Vigas colineales (barras pasantes).
        ResolveOppositeBeams(analysis);

        // 3. Decisión de anclaje por capa y generación de barras.
        var passThroughHandled = new HashSet<BeamSection>();
        foreach (var joint in analysis.Joints)
        {
            if (!joint.Connected) continue;
            foreach (var layer in _options.Layers)
                joint.Layers.Add(ResolveLayer(column, joint, layer, passThroughHandled));
            if (joint.OppositeBeam != null) passThroughHandled.Add(joint.Beam);
        }

        // 4. Cruces entre barras de vigas distintas dentro del nudo.
        ResolveCrossings(analysis);

        return analysis;
    }

    // ---------------------------------------------------------------------------------
    // Contacto viga-columna
    // ---------------------------------------------------------------------------------

    private BeamJoint ResolveContact(ColumnSection column, BeamSection beam)
    {
        var poly = column.Outline;
        var tol = Math.Max(_options.ContactTolerance, beam.Width);

        var dStart = poly.Contains(beam.AxisStart) ? 0 : poly.DistanceToBoundary(beam.AxisStart);
        var dEnd = poly.Contains(beam.AxisEnd) ? 0 : poly.DistanceToBoundary(beam.AxisEnd);

        Vec2 near, far;
        if (dStart <= tol && dEnd <= tol)
        {
            // Ambos extremos tocan la columna: viga embebida o muy corta.
            var j = new BeamJoint { Beam = beam, Connected = false };
            j.Diagnostics.Add(new Diagnostic(Severity.Error, beam.Name,
                "Los dos extremos de la viga están dentro o en contacto con la columna; no se puede definir la cara de llegada."));
            return j;
        }
        if (dStart <= tol) { near = beam.AxisStart; far = beam.AxisEnd; }
        else if (dEnd <= tol) { near = beam.AxisEnd; far = beam.AxisStart; }
        else
        {
            var j = new BeamJoint { Beam = beam, Connected = false };
            j.Diagnostics.Add(new Diagnostic(Severity.Error, beam.Name,
                $"La viga no llega a la columna (extremo más cercano a {Math.Min(dStart, dEnd):0} mm)."));
            return j;
        }

        var u = (near - far).Normalized();
        var v = u.Perp();
        var intervals = poly.LineClip(far, u);
        var axisLen = (near - far).Length;

        // Primer tramo del eje dentro de la columna cuya entrada está cerca del extremo de la viga.
        (double TIn, double TOut, int EdgeIn, int EdgeOut)? chosen = null;
        foreach (var iv in intervals)
        {
            if (iv.TOut <= 0) continue;
            if (iv.TIn > axisLen + tol) continue;
            chosen = iv;
            break;
        }
        if (chosen == null)
        {
            var j = new BeamJoint { Beam = beam, Connected = false };
            j.Diagnostics.Add(new Diagnostic(Severity.Error, beam.Name,
                "El eje de la viga no atraviesa la sección de la columna."));
            return j;
        }

        var (tIn, tOut, edgeIn, _) = chosen.Value;
        var contact = far + u * tIn;
        var available = tOut - tIn;
        var usable = available - column.Cover - column.TieDiameter - _options.HookClearance;
        var gap = Math.Max(0, tIn - axisLen);

        var joint = new BeamJoint
        {
            Beam = beam,
            Connected = true,
            ContactPoint = contact,
            ContactEdgeIndex = edgeIn,
            InwardDirection = u,
            LateralDirection = v,
            AvailableDepth = available,
            UsableDepth = usable,
            Gap = gap,
            LateralOverhang = ComputeOverhang(poly, edgeIn, contact, v, beam.Width)
        };

        if (gap > GeometryUtil.Tolerance)
            joint.Diagnostics.Add(new Diagnostic(Severity.Warning, beam.Name,
                $"El extremo modelado de la viga queda a {gap:0} mm de la cara de la columna; se asume que llega a la cara."));

        if (joint.LateralOverhang > GeometryUtil.Tolerance)
            joint.Diagnostics.Add(new Diagnostic(Severity.Warning, beam.Name,
                $"La viga sobresale {joint.LateralOverhang:0} mm de la cara de la columna donde apoya (ancho de viga {beam.Width:0} mm)."));

        if (intervals.Count > 1)
            joint.Diagnostics.Add(new Diagnostic(Severity.Info, beam.Name,
                "El eje de la viga sale y vuelve a entrar en la columna (sección cóncava); solo se usa el primer tramo continuo."));

        var angleToFace = GeometryUtil.RadToDeg(GeometryUtil.AngleBetween(u, -poly.OutwardNormal(edgeIn)));
        if (angleToFace > 15)
            joint.Diagnostics.Add(new Diagnostic(Severity.Warning, beam.Name,
                $"La viga llega oblicua a la cara de la columna ({angleToFace:0}°); revisar el detalle en obra."));

        return joint;
    }

    private static double ComputeOverhang(Polygon2D poly, int edgeIndex, Vec2 contact, Vec2 lateral, double width)
    {
        var edge = poly.Edge(edgeIndex);
        var left = contact + lateral * (width / 2);
        var right = contact - lateral * (width / 2);
        var sL = edge.ProjectParameter(left);
        var sR = edge.ProjectParameter(right);
        var sMin = Math.Min(sL, sR);
        var sMax = Math.Max(sL, sR);
        var over = 0.0;
        if (sMin < 0) over = Math.Max(over, -sMin * edge.Length);
        if (sMax > 1) over = Math.Max(over, (sMax - 1) * edge.Length);
        return over;
    }

    // ---------------------------------------------------------------------------------
    // Vigas opuestas
    // ---------------------------------------------------------------------------------

    private void ResolveOppositeBeams(JointAnalysis analysis)
    {
        var joints = analysis.Joints.Where(j => j.Connected).ToList();
        var paired = new Dictionary<BeamJoint, BeamJoint>();
        for (var i = 0; i < joints.Count; i++)
        {
            for (var k = i + 1; k < joints.Count; k++)
            {
                var a = joints[i];
                var b = joints[k];
                if (paired.ContainsKey(a) || paired.ContainsKey(b)) continue;
                if (!GeometryUtil.OppositeDirection(a.InwardDirection, b.InwardDirection, _options.CollinearAngleTolerance)) continue;
                // Desfase lateral entre ejes
                var offset = Math.Abs((b.ContactPoint - a.ContactPoint).Dot(a.LateralDirection));
                if (offset > Math.Max(a.Beam.Width, b.Beam.Width) / 2) continue;
                paired[a] = b;
                paired[b] = a;
            }
        }

        // Reemplazar los BeamJoint para fijar OppositeBeam (init-only) conservando diagnósticos.
        for (var i = 0; i < analysis.Joints.Count; i++)
        {
            var j = analysis.Joints[i];
            if (!paired.TryGetValue(j, out var other)) continue;
            var nj = new BeamJoint
            {
                Beam = j.Beam, Connected = j.Connected, ContactPoint = j.ContactPoint, ContactEdgeIndex = j.ContactEdgeIndex,
                InwardDirection = j.InwardDirection, LateralDirection = j.LateralDirection, AvailableDepth = j.AvailableDepth,
                UsableDepth = j.UsableDepth, Gap = j.Gap, LateralOverhang = j.LateralOverhang, OppositeBeam = other.Beam
            };
            nj.Diagnostics.AddRange(j.Diagnostics);
            analysis.Joints[i] = nj;
            // Actualizar el mapa para que la pareja apunte al nuevo objeto.
            paired[other] = nj;
        }
    }

    // ---------------------------------------------------------------------------------
    // Decisión por capa
    // ---------------------------------------------------------------------------------

    private LayerResult ResolveLayer(ColumnSection column, BeamJoint joint, BarLayer layer, HashSet<BeamSection> passThroughHandled)
    {
        var beam = joint.Beam;
        var code = _options.Code;
        var db = beam.BarDiameter(layer);
        var n = beam.BarCount(layer);
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
        var z = beam.LayerElevation(layer);
        var u = joint.InwardDirection;
        var v = joint.LateralDirection;
        var extension = ExtensionIntoBeam(beam, ctx);

        // Separación de barras en la viga
        var clearWidth = beam.Width - 2 * (beam.Cover + beam.StirrupDiameter) - db;
        var spacing = n > 1 ? clearWidth / (n - 1) : 0;
        if (n > 1 && spacing - db < Math.Max(25, db))
            joint.Diagnostics.Add(new Diagnostic(Severity.Warning, beam.Name,
                $"Capa {LayerName(layer)}: separación libre entre barras {spacing - db:0} mm < {Math.Max(25, db):0} mm; considerar dos capas o menos barras."));
        var firstLateral = -clearWidth / 2;

        // ---- Barras pasantes --------------------------------------------------------
        if (joint.OppositeBeam != null)
        {
            var minDim = code.MinimumJointDimensionForPassThrough(ctx);
            if (minDim.HasValue && joint.AvailableDepth < minDim.Value - GeometryUtil.Tolerance)
                joint.Diagnostics.Add(new Diagnostic(Severity.Error, beam.Name,
                    $"Capa {LayerName(layer)}: barras pasantes Ø{db:0} requieren columna ≥ {minDim.Value:0} mm en la dirección de la viga; hay {joint.AvailableDepth:0} mm."));

            BarGroup? group = null;
            if (!passThroughHandled.Contains(joint.OppositeBeam))
            {
                var other = joint.OppositeBeam;
                if (other.BarDiameter(layer) != db || other.BarCount(layer) != n)
                    joint.Diagnostics.Add(new Diagnostic(Severity.Warning, beam.Name,
                        $"Capa {LayerName(layer)}: la viga opuesta {other.Name} tiene {other.BarCount(layer)}Ø{other.BarDiameter(layer):0} y esta {n}Ø{db:0}; se generan barras pasantes con las de {beam.Name}."));

                // Desde la viga actual, atravesando la columna, hasta la extensión en la viga opuesta.
                var otherExt = ExtensionIntoBeam(other, ctx);
                var start = joint.ContactPoint - u * extension + v * firstLateral;
                var end = joint.ContactPoint + u * (joint.AvailableDepth + otherExt) + v * firstLateral;
                var pts = new[] { Vec3.From(start, z), Vec3.From(end, z) };
                group = new BarGroup
                {
                    BeamName = beam.Name, BeamSourceId = beam.SourceId, Layer = layer, Diameter = db, BarTypeName = beam.BarTypeName(layer), Count = n, Spacing = spacing,
                    Decision = AnchorageDecision.PassThrough, Centerline = pts, FullCenterline = pts,
                    PlaneNormal = Vec3.From(v, 0), ArrayDirection = Vec3.From(v, 0), Elevation = z,
                    RequiredLength = minDim ?? 0, ProvidedLength = joint.AvailableDepth,
                    Formula = minDim.HasValue ? "h_col ≥ 20·db (ACI 18.8.2.3)" : "Barra continua a través del nudo",
                    Comment = $"Pasante {beam.Name} ↔ {other.Name}, capa {LayerName(layer)}"
                };
            }

            return new LayerResult
            {
                Layer = layer, Decision = AnchorageDecision.PassThrough, Elevation = z,
                StraightRequired = ld.Length, HookRequired = ldh.Length, Provided = joint.AvailableDepth,
                Formula = minDim.HasValue ? $"Pasante: h_col {joint.AvailableDepth:0} ≥ {minDim.Value:0}" : "Pasante",
                Group = group
            };
        }

        // ---- Anclaje recto o con gancho ----------------------------------------------
        var usable = RoundDown(joint.UsableDepth);
        AnchorageDecision decision;
        double required, provided;
        string formula;
        HookSpec? hook = null;
        List<Vec3> centerline;
        List<Vec3> full;
        var start2 = joint.ContactPoint - u * extension + v * firstLateral;

        if (usable >= ld.Length)
        {
            decision = AnchorageDecision.Straight;
            required = ld.Length;
            formula = ld.Formula;
            var embed = _options.EmbedToFarFace ? usable : RoundUp(ld.Length);
            provided = embed;
            var end = joint.ContactPoint + u * embed + v * firstLateral;
            centerline = new List<Vec3> { Vec3.From(start2, z), Vec3.From(end, z) };
            full = centerline;
        }
        else
        {
            var fits = usable >= ldh.Length;
            decision = fits ? AnchorageDecision.Hook90 : AnchorageDecision.Insufficient;
            required = ldh.Length;
            formula = ldh.Formula;
            // La cara exterior del gancho queda en 'usable'; el vértice de la polilínea queda db/2 antes.
            var embedOuter = fits && !_options.EmbedToFarFace ? RoundUp(ldh.Length) : usable;
            provided = embedOuter;
            var corner = joint.ContactPoint + u * (embedOuter - db / 2) + v * firstLateral;

            var ext = code.HookExtension(db, HookAngle.Hook90);
            var hookDir = HookDirection(column, beam, layer, z, ext, out var hookWarning);
            if (hookWarning != null) joint.Diagnostics.Add(hookWarning);
            hook = new HookSpec(HookAngle.Hook90, ext, hookDir, code.BendInnerDiameter(db));
            centerline = new List<Vec3> { Vec3.From(start2, z), Vec3.From(corner, z) };
            full = new List<Vec3>(centerline) { Vec3.From(corner, z) + hookDir * ext };

            if (!fits)
                joint.Diagnostics.Add(new Diagnostic(Severity.Error, beam.Name,
                    $"Capa {LayerName(layer)}: Ø{db:0} necesita ldh = {ldh.Length:0} mm y la columna solo ofrece {usable:0} mm útiles " +
                    $"(faltan {ldh.Length - usable:0} mm). Opciones: reducir diámetro, aumentar la columna en esa dirección, usar barras con cabeza o prolongar la viga."));
        }

        var group2 = new BarGroup
        {
            BeamName = beam.Name, BeamSourceId = beam.SourceId, Layer = layer, Diameter = db, BarTypeName = beam.BarTypeName(layer), Count = n, Spacing = spacing,
            Decision = decision, Centerline = centerline, EndHook = hook, FullCenterline = full,
            PlaneNormal = Vec3.From(v, 0), ArrayDirection = Vec3.From(v, 0), Elevation = z,
            RequiredLength = required, ProvidedLength = provided, Formula = formula,
            Comment = $"{beam.Name} capa {LayerName(layer)}: {DecisionName(decision)} (req. {required:0} / prov. {provided:0} mm)"
        };

        return new LayerResult
        {
            Layer = layer, Decision = decision, Elevation = z,
            StraightRequired = ld.Length, HookRequired = ldh.Length, Provided = provided, Formula = formula, Group = group2
        };
    }

    /// <summary>
    /// Sentido del gancho de 90°: superiores hacia abajo; inferiores hacia arriba salvo que la
    /// columna termine en la viga (nudo de cubierta), en cuyo caso también hacia abajo.
    /// </summary>
    public static Vec3 HookDirection(ColumnSection column, BeamSection beam, BarLayer layer, double z, double hookExtension, out Diagnostic? warning)
    {
        warning = null;
        // Superiores: gancho hacia abajo. Inferiores: hacia arriba (si la columna continúa).
        var down = new Vec3(0, 0, -1);
        var up = new Vec3(0, 0, 1);
        if (layer == BarLayer.Top) return down;

        var hasColumnRange = column.TopElevation > column.BottomElevation + 1;
        if (!hasColumnRange) return up;

        var hookTop = z + hookExtension;
        if (hookTop > column.TopElevation - column.Cover)
        {
            warning = new Diagnostic(Severity.Warning, beam.Name,
                $"Capa {LayerName(layer)}: la columna termina a {column.TopElevation - z:0} mm sobre la barra; el gancho se gira hacia abajo (nudo de cubierta).");
            return down;
        }
        return up;
    }

    private double ExtensionIntoBeam(BeamSection beam, AnchorageContext ctx)
    {
        if (_options.ExtensionIntoBeam.HasValue) return RoundUp(_options.ExtensionIntoBeam.Value);
        var lap = _options.Code.LapSpliceLength(ctx).Length;
        return RoundUp(2 * beam.Depth + lap);
    }

    // ---------------------------------------------------------------------------------
    // Cruces de barras
    // ---------------------------------------------------------------------------------

    private void ResolveCrossings(JointAnalysis analysis)
    {
        var joints = analysis.Joints.Where(j => j.Connected).ToList();
        for (var i = 0; i < joints.Count; i++)
        {
            for (var k = i + 1; k < joints.Count; k++)
            {
                var a = joints[i];
                var b = joints[k];
                if (ReferenceEquals(a.OppositeBeam, b.Beam)) continue; // colineales: no se cruzan
                var angle = GeometryUtil.RadToDeg(GeometryUtil.AngleBetween(a.InwardDirection, b.InwardDirection));
                if (angle < _options.CollinearAngleTolerance || angle > 180 - _options.CollinearAngleTolerance) continue;

                // ¿Las franjas de barras se cruzan dentro de la columna?
                var segA = new Segment2D(a.ContactPoint, a.ContactPoint + a.InwardDirection * a.AvailableDepth);
                var segB = new Segment2D(b.ContactPoint, b.ContactPoint + b.InwardDirection * b.AvailableDepth);
                if (!StripsCross(segA, a.Beam.Width, segB, b.Beam.Width)) continue;

                foreach (var layer in _options.Layers)
                {
                    var la = a.Layers.FirstOrDefault(l => l.Layer == layer);
                    var lb = b.Layers.FirstOrDefault(l => l.Layer == layer);
                    if (la?.Group == null || lb?.Group == null) continue;
                    var minSep = (la.Group.Diameter + lb.Group.Diameter) / 2 + _options.CrossingClearance;
                    var dz = Math.Abs(la.Elevation - lb.Elevation);
                    if (dz >= minSep) continue;

                    if (_options.AutoStaggerCrossingLayers)
                    {
                        var shift = layer == BarLayer.Top ? -(minSep - dz) : (minSep - dz);
                        var shifted = ShiftGroup(lb.Group, shift);
                        var idx = b.Layers.IndexOf(lb);
                        b.Layers[idx] = new LayerResult
                        {
                            Layer = lb.Layer, Decision = lb.Decision, Elevation = lb.Elevation + shift,
                            StraightRequired = lb.StraightRequired, HookRequired = lb.HookRequired, Provided = lb.Provided,
                            Formula = lb.Formula, Group = shifted
                        };
                        b.Diagnostics.Add(new Diagnostic(Severity.Info, b.Beam.Name,
                            $"Capa {LayerName(layer)}: las barras se cruzan con las de {a.Beam.Name} en el nudo; la capa se desplaza {shift:+0;-0} mm para pasar {(shift < 0 ? "por debajo" : "por encima")}."));
                    }
                    else
                    {
                        b.Diagnostics.Add(new Diagnostic(Severity.Warning, b.Beam.Name,
                            $"Capa {LayerName(layer)}: las barras se cruzan con las de {a.Beam.Name} a la misma cota (Δz = {dz:0} mm < {minSep:0} mm). Desplazar una capa."));
                    }
                }
            }
        }
    }

    private static bool StripsCross(Segment2D a, double widthA, Segment2D b, double widthB)
    {
        if (a.Intersects(b, out _)) return true;
        var d = Math.Min(Math.Min(a.DistanceTo(b.A), a.DistanceTo(b.B)), Math.Min(b.DistanceTo(a.A), b.DistanceTo(a.B)));
        return d <= (widthA + widthB) / 2;
    }

    private static BarGroup ShiftGroup(BarGroup g, double dz)
    {
        var d = new Vec3(0, 0, dz);
        return new BarGroup
        {
            BeamName = g.BeamName, BeamSourceId = g.BeamSourceId, Layer = g.Layer, Diameter = g.Diameter, BarTypeName = g.BarTypeName, Count = g.Count, Spacing = g.Spacing,
            Decision = g.Decision, Centerline = g.Centerline.Select(p => p + d).ToList(), EndHook = g.EndHook,
            FullCenterline = g.FullCenterline.Select(p => p + d).ToList(), PlaneNormal = g.PlaneNormal, ArrayDirection = g.ArrayDirection,
            Elevation = g.Elevation + dz, RequiredLength = g.RequiredLength, ProvidedLength = g.ProvidedLength, Formula = g.Formula,
            Comment = g.Comment + $" [capa desplazada {dz:+0;-0} mm]"
        };
    }

    // ---------------------------------------------------------------------------------

    private double RoundUp(double v) => GeometryUtil.RoundUpTo(v, _options.LengthRounding);

    private double RoundDown(double v)
    {
        var m = _options.LengthRounding;
        return m <= 0 ? v : Math.Floor(v / m + 1e-9) * m;
    }

    public static string LayerName(BarLayer layer) => layer == BarLayer.Top ? "superior" : "inferior";

    public static string DecisionName(AnchorageDecision d) => d switch
    {
        AnchorageDecision.Straight => "anclaje recto",
        AnchorageDecision.Hook90 => "gancho 90°",
        AnchorageDecision.PassThrough => "barras pasantes",
        AnchorageDecision.Insufficient => "INSUFICIENTE",
        _ => "sin conexión"
    };
}
