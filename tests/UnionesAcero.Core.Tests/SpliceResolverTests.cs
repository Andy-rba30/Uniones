using UnionesAcero.Core.Analysis;
using UnionesAcero.Core.Codes;
using UnionesAcero.Core.Geometry;
using UnionesAcero.Core.Model;
using UnionesAcero.Core.Verification;
using Xunit;

namespace UnionesAcero.Core.Tests;

public class SpliceResolverTests
{
    private static JointOptions Options() => new()
    {
        Code = new Aci318Code(), Materials = new Materials { Fy = 420, Fc = 21 }, SeismicJoint = true
    };

    [Fact]
    public void OppositeBeamsWithShortBars_PrincipalRunsThroughAndOppositeIsCutAtTwoH()
    {
        var options = Options();
        var col = new ColumnSection("C1", Polygon2D.Rectangle(400, 400)) { Cover = 40, TieDiameter = 10, TopElevation = 6000 };
        // V1 por la cara -x (más peralte: principal), V2 por la cara +x, colineales.
        var v1 = new BeamSection("V1", new Vec2(-4000, 0), new Vec2(-200, 0), 250, 600) { TopElevation = 3000 };
        var v2 = new BeamSection("V2", new Vec2(4000, 0), new Vec2(200, 0), 250, 500) { TopElevation = 3000 };
        var a = new JointAnalyzer(options).Analyze(col, new[] { v1, v2 }, resolveClashes: false);
        var j1 = a.Joints.Single(j => j.Beam.Name == "V1");
        var j2 = a.Joints.Single(j => j.Beam.Name == "V2");
        Assert.NotNull(j1.OppositeBeam);

        var z = 2942.0;
        // Las dos barras superiores (3Ø16) entran 130 mm en la columna y se quedan ahí.
        var barA = new ExistingBar(1, "V1", 16, new[] { new Vec3(-3800, 0, z), new Vec3(-70, 0, z) }) { Count = 3, SetLength = 140, SetDirection = new Vec3(0, 1, 0) };
        var barB = new ExistingBar(2, "V2", 16, new[] { new Vec3(3800, 0, z), new Vec3(70, 0, z) }) { Count = 3, SetLength = 140, SetDirection = new Vec3(0, -1, 0) };

        var res = new SpliceResolver(options).Resolve(a, new[]
        {
            new SpliceCandidate(j1, BarLayer.Top, barA), new SpliceCandidate(j2, BarLayer.Top, barB)
        });

        Assert.Equal(2, res.Fixes.Count);
        Assert.Contains(1, res.ReplacedBarIds);
        Assert.Contains(2, res.ReplacedBarIds);
        var fa = res.Fixes.Single(f => f.Bar.Id == 1);
        var fb = res.Fixes.Single(f => f.Bar.Id == 2);
        Assert.Equal(AnchorageDecision.PassThrough, fa.Decision);

        // lst clase B = 1.3·ld (Ø16 superior, no sísmico) = 1180,4 → 1190 mm; 2h de V2 = 1000 mm.
        Assert.Equal(1190, fa.RequiredLength);
        // Barra continua: lejano conservado, recta hasta la bayoneta, baja 16 mm (contacto) con pendiente 1:6 y
        // sigue 1190 mm: termina en x = 200 + 1000 + 1190 = 2390.
        Assert.Equal(-3800, fa.Centerline[0].X, 3);
        Assert.Equal(4, fa.Centerline.Count);
        Assert.Equal(200 + 1000 - 96, fa.Centerline[1].X, 3);
        Assert.Equal(z, fa.Centerline[1].Z, 3);
        Assert.Equal(200 + 1000, fa.Centerline[2].X, 3);
        Assert.Equal(z - 16, fa.Centerline[2].Z, 3);
        Assert.Equal(2390, fa.Centerline[3].X, 3);
        Assert.Equal(z - 16, fa.Centerline[3].Z, 3);
        Assert.Contains("bayoneta", fa.Message);

        // Barra de V2: se corta a 2h = 1000 mm de su cara (x = 1200), donde empieza el traslape.
        Assert.Equal(2, fb.Centerline.Count);
        Assert.Equal(3800, fb.Centerline[0].X, 3);
        Assert.Equal(1200, fb.Centerline[^1].X, 3);
        Assert.Equal(z, fb.Centerline[^1].Z, 3);
        Assert.True(fb.EndShift < 0);
        Assert.Contains("2h", fb.Message);

        Assert.Contains(res.Diagnostics, d => d.Message.Contains("estribos de confinamiento"));
        Assert.DoesNotContain(res.Diagnostics, d => d.Severity == Severity.Warning);
    }

    [Fact]
    public void LongContinuousBarIsFlaggedAgainstCommercialLength()
    {
        var options = Options();
        var col = new ColumnSection("C1", Polygon2D.Rectangle(400, 400)) { TopElevation = 6000 };
        var v1 = new BeamSection("V1", new Vec2(-9000, 0), new Vec2(-200, 0), 250, 500) { TopElevation = 3000 };
        var v2 = new BeamSection("V2", new Vec2(4000, 0), new Vec2(200, 0), 250, 500) { TopElevation = 3000 };
        var a = new JointAnalyzer(options).Analyze(col, new[] { v1, v2 }, resolveClashes: false);
        var z = 2942.0;
        var barA = new ExistingBar(1, "V1", 16, new[] { new Vec3(-8800, 0, z), new Vec3(-70, 0, z) });
        var barB = new ExistingBar(2, "V2", 16, new[] { new Vec3(3800, 0, z), new Vec3(70, 0, z) });
        var res = new SpliceResolver(options).Resolve(a, new[]
        {
            new SpliceCandidate(a.Joints[0], BarLayer.Top, barA), new SpliceCandidate(a.Joints[1], BarLayer.Top, barB)
        });
        Assert.Contains(res.Diagnostics, d => d.Severity == Severity.Warning && d.Message.Contains("comercial"));
    }
}
