using UnionesAcero.Core.Analysis;
using UnionesAcero.Core.Codes;
using UnionesAcero.Core.Geometry;
using UnionesAcero.Core.Model;
using UnionesAcero.Core.Verification;
using Xunit;

namespace UnionesAcero.Core.Tests;

public class BarFixerTests
{
    private static (JointAnalysis Analysis, JointOptions Options) Setup(double colWidth = 400)
    {
        var options = new JointOptions { Code = new Aci318Code(), Materials = new Materials { Fy = 420, Fc = 21 }, SeismicJoint = true };
        var col = new ColumnSection("C1", Polygon2D.Rectangle(colWidth, 400)) { Cover = 40, TieDiameter = 10, TopElevation = 6000, BottomElevation = 0 };
        var beam = new BeamSection("V1", new Vec2(-3000, 0), new Vec2(-colWidth / 2, 0), 250, 500) { TopElevation = 3000 };
        var analysis = new JointAnalyzer(options).Analyze(col, new[] { beam });
        return (analysis, options);
    }

    private static BarFix? Fix(JointAnalysis a, JointOptions o, ExistingBar bar, out string? reason)
    {
        var check = new ExistingBarChecker(o).Check(a, new[] { bar }).Single();
        return new BarFixer(o).Plan(a.Column, a.Joints[0], bar, check, out reason);
    }

    [Fact]
    public void ShortStraightTopBarGetsHookToFarFaceOfCore()
    {
        var (a, o) = Setup();
        var z = 2942.0;
        // Barra recta que termina en la cara de la columna (x = -200): no cumple.
        var bar = new ExistingBar(1, "V1", 16, new[] { new Vec3(-2000, 30, z), new Vec3(-200, 30, z) });
        var fix = Fix(a, o, bar, out var reason);
        Assert.Null(reason);
        Assert.NotNull(fix);
        Assert.Equal(AnchorageDecision.Hook90, fix!.Decision);
        Assert.Equal(BarLayer.Top, fix.Layer);
        Assert.False(fix.Reversed);
        // Se conserva el extremo lejano y la posición lateral de la barra (y = 30).
        Assert.Equal(new Vec3(-2000, 30, z), fix.Centerline[0]);
        Assert.Equal(2, fix.Centerline.Count);
        // Vértice del gancho db/2 antes de la profundidad útil (350): x = -200 + 350 - 8 = 142
        Assert.Equal(142, fix.Centerline[^1].X, 3);
        Assert.Equal(30, fix.Centerline[^1].Y, 3);
        Assert.Equal(350, fix.ProvidedLength, 3);
        Assert.Equal(272, fix.RequiredLength);
        Assert.NotNull(fix.EndHook);
        Assert.Equal(-1, fix.EndHook!.Direction.Z);  // superior: hacia abajo
        Assert.Equal(3, fix.FullCenterline.Count);
        Assert.Equal(z - 192, fix.FullCenterline[^1].Z, 3);
        Assert.Equal(0, fix.PlaneNormal.X, 6);
        Assert.Equal(1, Math.Abs(fix.PlaneNormal.Y), 6);
        Assert.Contains("gancho a 90°", fix.Message);
    }

    [Fact]
    public void BarDrawnFromColumnOutwardIsReversed()
    {
        var (a, o) = Setup();
        var z = 2558.0; // capa inferior: 3000 - 500 + 40 + 10 + 8
        var bar = new ExistingBar(2, "V1", 16, new[] { new Vec3(-100, 0, z), new Vec3(-2500, 0, z) });
        var fix = Fix(a, o, bar, out _);
        Assert.NotNull(fix);
        Assert.True(fix!.Reversed);
        Assert.Equal(BarLayer.Bottom, fix.Layer);
        Assert.Equal(-2500, fix.Centerline[0].X, 3);
        Assert.Equal(142, fix.Centerline[^1].X, 3);
        Assert.Equal(1, fix.EndHook!.Direction.Z); // inferior: hacia arriba
        Assert.Equal(100 + 142, fix.EndShift, 3); // de x = -100 a x = 142
    }

    [Fact]
    public void ExistingShortHookIsReplacedAndFarHookIsKept()
    {
        var (a, o) = Setup();
        var z = 2942.0;
        // Gancho al extremo lejano (hacia abajo en x = -2000) y gancho corto en la columna (solo 100 mm).
        var hooked = new[] { new Vec3(-2000, 0, z - 192), new Vec3(-2000, 0, z), new Vec3(-100, 0, z), new Vec3(-100, 0, z - 192) };
        var bare = new[] { new Vec3(-2000, 0, z), new Vec3(-100, 0, z) };
        var bar = new ExistingBar(3, "V1", 16, hooked) { BareCenterline = bare };
        var fix = Fix(a, o, bar, out var reason);
        Assert.Null(reason);
        Assert.NotNull(fix);
        // El eje sin ganchos se reconstruye: lejano conservado, nuevo vértice en la columna.
        Assert.Equal(2, fix!.Centerline.Count);
        Assert.Equal(-2000, fix.Centerline[0].X, 3);
        Assert.Equal(142, fix.Centerline[^1].X, 3);
        Assert.Contains("Ahora tiene 108 mm", fix.Message);
    }

    [Fact]
    public void WideColumnGivesStraightFix()
    {
        var (a, o) = Setup(colWidth: 2000);
        var z = 2942.0;
        var bar = new ExistingBar(4, "V1", 16, new[] { new Vec3(-3000, 0, z), new Vec3(-900, 0, z) });
        var fix = Fix(a, o, bar, out _);
        Assert.NotNull(fix);
        Assert.Equal(AnchorageDecision.Straight, fix!.Decision);
        Assert.Null(fix.EndHook);
        Assert.Equal(1950, fix.ProvidedLength, 3); // hasta la cara lejana del núcleo
        Assert.Equal(-1000 + 1950, fix.Centerline[^1].X, 3);
    }

    [Fact]
    public void TooThinColumnHasNoFix()
    {
        var (a, o) = Setup(colWidth: 250);
        var z = 2942.0;
        var bar = new ExistingBar(5, "V1", 16, new[] { new Vec3(-3000, 0, z), new Vec3(-125, 0, z) });
        var fix = Fix(a, o, bar, out var reason);
        Assert.Null(fix);
        Assert.NotNull(reason);
        Assert.Contains("ldh", reason);
    }

    [Fact]
    public void CompliantBarIsNotFixed()
    {
        var (a, o) = Setup();
        var z = 2942.0;
        var bar = new ExistingBar(6, "V1", 16, new[] { new Vec3(-2000, 0, z), new Vec3(142, 0, z), new Vec3(142, 0, z - 192) });
        var fix = Fix(a, o, bar, out var reason);
        Assert.Null(fix);
        Assert.Contains("cumple", reason);
    }
}
