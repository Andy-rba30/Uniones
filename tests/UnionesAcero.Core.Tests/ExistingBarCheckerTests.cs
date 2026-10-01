using UnionesAcero.Core.Analysis;
using UnionesAcero.Core.Codes;
using UnionesAcero.Core.Geometry;
using UnionesAcero.Core.Model;
using UnionesAcero.Core.Verification;
using Xunit;

namespace UnionesAcero.Core.Tests;

public class ExistingBarCheckerTests
{
    private static (JointAnalysis Analysis, JointOptions Options) Setup()
    {
        var options = new JointOptions { Code = new Aci318Code(), Materials = new Materials { Fy = 420, Fc = 21 }, SeismicJoint = true };
        var col = new ColumnSection("C1", Polygon2D.Rectangle(400, 400)) { Cover = 40, TieDiameter = 10 };
        var beam = new BeamSection("V1", new Vec2(-3000, 0), new Vec2(-200, 0), 250, 500) { TopElevation = 3000 };
        var analysis = new JointAnalyzer(options).Analyze(col, new[] { beam });
        return (analysis, options);
    }

    [Fact]
    public void HookedBarWithEnoughEmbedmentPasses()
    {
        var (a, o) = Setup();
        var z = 2942.0;
        // Entra 342 mm (vértice) → cara exterior 350 ≥ 272
        var bar = new ExistingBar(1, "V1", 16, new[] { new Vec3(-2000, 0, z), new Vec3(142, 0, z), new Vec3(142, 0, z - 192) });
        var r = new ExistingBarChecker(o).Check(a, new[] { bar }).Single();
        Assert.Equal(CheckStatus.Ok, r.Status);
        Assert.Equal(AnchorageDecision.Hook90, r.Decision);
        Assert.Equal(BarLayer.Top, r.Layer);
        Assert.Equal(350, r.Provided, 3);
    }

    [Fact]
    public void ShortHookedBarFails()
    {
        var (a, o) = Setup();
        var z = 2942.0;
        var bar = new ExistingBar(2, "V1", 16, new[] { new Vec3(-2000, 0, z), new Vec3(0, 0, z), new Vec3(0, 0, z - 192) });
        var r = new ExistingBarChecker(o).Check(a, new[] { bar }).Single();
        Assert.Equal(CheckStatus.Fail, r.Status);
        Assert.Contains("faltan", r.Message);
    }

    [Fact]
    public void StraightShortBarSuggestsHook()
    {
        var (a, o) = Setup();
        var z = 2058.0; // capa inferior
        var bar = new ExistingBar(3, "V1", 16, new[] { new Vec3(-2000, 0, z), new Vec3(150, 0, z) });
        var r = new ExistingBarChecker(o).Check(a, new[] { bar }).Single();
        Assert.Equal(CheckStatus.Fail, r.Status);
        Assert.Equal(AnchorageDecision.Straight, r.Decision);
        Assert.Equal(BarLayer.Bottom, r.Layer);
        Assert.Contains("Añadir gancho", r.Message);
    }

    [Fact]
    public void PassThroughBarIsOk()
    {
        var (a, o) = Setup();
        var bar = new ExistingBar(4, "V1", 16, new[] { new Vec3(-2000, 0, 2942), new Vec3(2000, 0, 2942) });
        var r = new ExistingBarChecker(o).Check(a, new[] { bar }).Single();
        Assert.Equal(CheckStatus.Ok, r.Status);
        Assert.Equal(AnchorageDecision.PassThrough, r.Decision);
    }

    [Fact]
    public void StirrupIsSkipped()
    {
        var (a, o) = Setup();
        var bar = new ExistingBar(5, "V1", 10, new[]
        {
            new Vec3(-1000, -100, 2550), new Vec3(-1000, 100, 2550), new Vec3(-1000, 100, 2950), new Vec3(-1000, -100, 2950), new Vec3(-1000, -100, 2550)
        });
        var r = new ExistingBarChecker(o).Check(a, new[] { bar }).Single();
        Assert.Equal(CheckStatus.NotApplicable, r.Status);
    }

    [Fact]
    public void BarNotReachingColumnFails()
    {
        var (a, o) = Setup();
        var bar = new ExistingBar(6, "V1", 16, new[] { new Vec3(-2000, 0, 2942), new Vec3(-300, 0, 2942) });
        var r = new ExistingBarChecker(o).Check(a, new[] { bar }).Single();
        Assert.Equal(CheckStatus.Fail, r.Status);
        Assert.Contains("no entra", r.Message);
    }
}
