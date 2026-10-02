using UnionesAcero.Core.Analysis;
using UnionesAcero.Core.Codes;
using UnionesAcero.Core.Geometry;
using UnionesAcero.Core.Model;

namespace UnionesAcero.Core.Verification;

/// <summary>Barra existente de una viga conectada que tiene viga opuesta: candidata a empalme.</summary>
public sealed record SpliceCandidate(BeamJoint Joint, BarLayer Layer, ExistingBar Bar);

public sealed class SpliceResult
{
    public List<BarFix> Fixes { get; } = new();
    public List<Diagnostic> Diagnostics { get; } = new();
    /// <summary>Ids de las barras existentes sustituidas por un empalme (sus otras correcciones se descartan).</summary>
    public HashSet<long> ReplacedBarIds { get; } = new();
}

/// <summary>
/// Barras ya modeladas en dos vigas colineales a los lados de la columna: en obra no se anclan las
/// dos en el nudo, se hace continua una (la de la viga principal) y se empalma por traslape con la
/// otra fuera del nudo. ACI 318-19 18.6.3.3 y E.060 21.5.2.3 prohíben el empalme dentro del nudo y a
/// menos de 2h de la cara, así que el traslape empieza a 2h de la cara de la viga opuesta y mide lst
/// (clase B; con diámetros distintos, el mayor de ld del grueso y lst del fino). La barra continua
/// hace una bayoneta (pendiente 1:6) justo antes del traslape para pasar junto a la otra, y la barra
/// de la viga opuesta se corta en el inicio del traslape.
/// </summary>
public sealed class SpliceResolver
{
    private readonly JointOptions _options;

    public SpliceResolver(JointOptions? options = null)
    {
        _options = options ?? new JointOptions();
    }

    public SpliceResult Resolve(JointAnalysis analysis, IReadOnlyList<SpliceCandidate> candidates)
    {
        var result = new SpliceResult();
        var done = new HashSet<BeamJoint>();
        foreach (var ja in analysis.Joints.Where(j => j.Connected && j.OppositeBeam != null))
        {
            if (done.Contains(ja)) continue;
            var jb = analysis.Joints.FirstOrDefault(j => j.Connected && j.Beam == ja.OppositeBeam);
            if (jb == null) continue;
            done.Add(ja); done.Add(jb);

            var (principal, secondary, why) = Rank(ja, jb, analysis.Joints.IndexOf(ja), analysis.Joints.IndexOf(jb));
            foreach (var layer in new[] { BarLayer.Top, BarLayer.Bottom })
            {
                var a = candidates.Where(c => c.Joint == principal && c.Layer == layer).ToList();
                var b = candidates.Where(c => c.Joint == secondary && c.Layer == layer).ToList();
                if (a.Count == 0 || b.Count == 0)
                {
                    if (a.Count + b.Count > 0)
                        result.Diagnostics.Add(new Diagnostic(Severity.Info, (a.Count == 0 ? principal : secondary).Beam.Name,
                            $"Capa {JointAnalyzer.LayerName(layer)}: sin empalme automático con la viga opuesta porque solo una de las dos tiene barras modeladas en esta capa."));
                    continue;
                }
                if (a.Count > 1 || b.Count > 1)
                    result.Diagnostics.Add(new Diagnostic(Severity.Warning, principal.Beam.Name,
                        $"Capa {JointAnalyzer.LayerName(layer)}: hay varios conjuntos de barras en la capa; el empalme se plantea con el primero de cada viga."));
                Splice(principal, secondary, why, layer, a[0].Bar, b[0].Bar, result);
            }
        }
        return result;
    }

    private void Splice(BeamJoint ja, BeamJoint jb, string why, BarLayer layer, ExistingBar a, ExistingBar b, SpliceResult result)
    {
        if (!BarFixer.TryOrientTowardsColumn(a, ja, out var pa, out var ka, out var revA) ||
            !BarFixer.TryOrientTowardsColumn(b, jb, out var pb, out var kb, out var revB))
        {
            result.Diagnostics.Add(new Diagnostic(Severity.Warning, ja.Beam.Name,
                $"Capa {JointAnalyzer.LayerName(layer)}: no se pudo plantear el empalme con {jb.Beam.Name}: alguna barra no tiene un tramo recto hacia la columna."));
            return;
        }

        var code = _options.Code;
        var ctxA = Ctx(a.Diameter, layer);
        var ctxB = Ctx(b.Diameter, layer);
        double lst;
        string lstFormula;
        if (Math.Abs(a.Diameter - b.Diameter) < 0.5)
        {
            var lap = code.LapSpliceLength(ctxA);
            lst = lap.Length; lstFormula = lap.Formula;
        }
        else
        {
            var big = a.Diameter > b.Diameter ? ctxA : ctxB;
            var small = a.Diameter > b.Diameter ? ctxB : ctxA;
            var ld = code.StraightDevelopmentLength(big with { Seismic = false });
            var lap = code.LapSpliceLength(small);
            lst = Math.Max(ld.Length, lap.Length);
            lstFormula = $"máx(ld Ø{big.BarDiameter:0} = {ld.Length:0}, lst Ø{small.BarDiameter:0} = {lap.Length:0})";
        }
        lst = GeometryUtil.RoundUpTo(lst, _options.LengthRounding);

        var uA = ja.InwardDirection; var vA = ja.LateralDirection;
        var uB = jb.InwardDirection; var vB = jb.LateralDirection;
        var hB = jb.Beam.Depth;
        var faceB = (jb.ContactPoint - ja.ContactPoint).Dot(uA);          // cara de la viga opuesta, desde la cara de entrada de A
        var twoH = GeometryUtil.RoundUpTo(2 * hB, _options.LengthRounding);
        var s = (a.Diameter + b.Diameter) / 2 + _options.LapSpliceClearance;  // desnivel de la bayoneta (ejes)
        var crank = 6 * s;                                                   // pendiente 1:6
        var zA = pa[ka + 1].Z;
        var yA = (pa[ka + 1].XY - ja.ContactPoint).Dot(vA);
        var zB = pb[kb + 1].Z;
        var yB = (pb[kb + 1].XY - jb.ContactPoint).Dot(vB);
        var dzSign = layer == BarLayer.Top ? -1 : 1;                           // superior: bayoneta hacia abajo

        // Alineación en planta de las dos barras (la lateral de B es la opuesta de la de A)
        var lateralGap = Math.Abs(yA + yB);
        if (lateralGap > s + 0.5)
            result.Diagnostics.Add(new Diagnostic(Severity.Warning, ja.Beam.Name,
                $"Capa {JointAnalyzer.LayerName(layer)}: las barras de {ja.Beam.Name} y {jb.Beam.Name} no quedan alineadas en planta ({lateralGap:0} mm entre ejes); el traslape queda de lado, revisar en obra."));

        // ---- Barra continua (viga principal) ----
        var startA = pa.Take(ka + 1).ToList();
        Vec3 PA(double along, double z) => Vec3.From(ja.ContactPoint + uA * along + vA * yA, z);
        var p1 = PA(faceB + twoH - crank, zA);
        var p2 = PA(faceB + twoH, zA + dzSign * s);
        var p3 = PA(faceB + twoH + lst, zA + dzSign * s);
        var clA = new List<Vec3>(startA) { p1, p2, p3 };
        var endShiftA = (p3.XY - pa[ka + 1].XY).Dot(uA);
        var fixA = new BarFix
        {
            Bar = a, Joint = ja, Layer = layer, Decision = AnchorageDecision.PassThrough, Centerline = clA, FullCenterline = clA,
            PlaneNormal = Vec3.From(vA, 0), Elevation = zA, RequiredLength = lst, ProvidedLength = lst, Formula = lstFormula,
            Reversed = revA, EndShift = endShiftA,
            Message = $"Continua a través del nudo ({why}) y empalmada por traslape con la barra de {jb.Beam.Name} fuera del nudo: " +
                      $"traslape lst = {lst:0} mm desde 2h = {twoH:0} mm de la cara de {jb.Beam.Name}; bayoneta de {s:0} mm con pendiente 1:6 antes del traslape."
        };
        var total = fixA.TotalLength;
        if (total > _options.CommercialBarLength + 0.5)
            result.Diagnostics.Add(new Diagnostic(Severity.Warning, ja.Beam.Name,
                $"Capa {JointAnalyzer.LayerName(layer)}: la barra continua mide {total:0} mm, más que la varilla comercial de {_options.CommercialBarLength:0} mm; habrá que empalmarla también en el tramo (tercio central para superiores, lejos de 2h de la cara para inferiores)."));

        // ---- Barra cortada (viga opuesta): termina en el inicio del traslape, a 2h de su cara ----
        var startB = pb.Take(kb + 1).ToList();
        var endB = Vec3.From(jb.ContactPoint - uB * twoH + vB * yB, zB);
        var farB = (pb[kb].XY - jb.ContactPoint).Dot(uB);   // negativo: dentro de la viga B
        var clB = new List<Vec3>(startB) { endB };
        var endShiftB = (endB.XY - pb[kb + 1].XY).Dot(uB);
        var fixB = new BarFix
        {
            Bar = b, Joint = jb, Layer = layer, Decision = AnchorageDecision.PassThrough, Centerline = clB, FullCenterline = clB,
            PlaneNormal = Vec3.From(vB, 0), Elevation = zB, RequiredLength = lst, ProvidedLength = lst, Formula = lstFormula,
            Reversed = revB, EndShift = endShiftB,
            Message = $"Se corta a 2h = {twoH:0} mm de la cara de la columna: ahí empieza el traslape de {lst:0} mm con la barra continua de {ja.Beam.Name}, que viene a través del nudo."
        };
        if (-farB < twoH + lst - 0.5)
            result.Diagnostics.Add(new Diagnostic(Severity.Warning, jb.Beam.Name,
                $"Capa {JointAnalyzer.LayerName(layer)}: la barra de {jb.Beam.Name} solo llega a {-farB:0} mm de la cara y el traslape necesita {twoH + lst:0} mm (2h + lst); prolongarla o empalmar más adentro."));
        if (endShiftB > 0.5)
            fixB.Message += $" (se prolonga {endShiftB:0} mm hasta el inicio del traslape)";

        result.Diagnostics.Add(new Diagnostic(Severity.Info, ja.Beam.Name,
            $"Capa {JointAnalyzer.LayerName(layer)}: barra continua {ja.Beam.Name} → {jb.Beam.Name} con traslape de {lst:0} mm a partir de 2h = {twoH:0} mm de la cara; " +
            "colocar estribos de confinamiento en la zona del traslape (s ≤ d/4 y ≤ 100 mm)."));

        result.Fixes.Add(fixA);
        result.Fixes.Add(fixB);
        result.ReplacedBarIds.Add(a.Id);
        result.ReplacedBarIds.Add(b.Id);
    }

    private AnchorageContext Ctx(double db, BarLayer layer) => new()
    {
        BarDiameter = db, Materials = _options.Materials, IsTopBar = layer == BarLayer.Top, Seismic = _options.SeismicJoint
    };

    private static (BeamJoint Principal, BeamJoint Secondary, string Why) Rank(BeamJoint a, BeamJoint b, int ia, int ib)
    {
        var ba = a.Beam; var bb = b.Beam;
        if (ba.CrossingPriority != bb.CrossingPriority)
            return ba.CrossingPriority > bb.CrossingPriority ? (a, b, "viga elegida como principal") : (b, a, "viga elegida como principal");
        if (Math.Abs(ba.Depth - bb.Depth) > 0.5) return ba.Depth > bb.Depth ? (a, b, "viga de mayor peralte") : (b, a, "viga de mayor peralte");
        if (Math.Abs(ba.Width - bb.Width) > 0.5) return ba.Width > bb.Width ? (a, b, "viga de mayor ancho") : (b, a, "viga de mayor ancho");
        return ia <= ib ? (a, b, "primera viga de la lista") : (b, a, "primera viga de la lista");
    }
}
