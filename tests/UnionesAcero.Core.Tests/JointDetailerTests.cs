using UnionesAcero.Core.Analysis;
using UnionesAcero.Core.Codes;
using UnionesAcero.Core.Geometry;
using UnionesAcero.Core.Model;
using UnionesAcero.Core.Verification;
using Xunit;

namespace UnionesAcero.Core.Tests;

public class JointDetailerTests
{
    private static JointOptions Options() => new()
    {
        Code = new Aci318Code(), Materials = new Materials { Fy = 420, Fc = 21 }, SeismicJoint = true
    };

    /// <summary>
    /// Columna en L como la del modelo: cuadrado de 250 x 250 en el origen más un brazo de 150 de alto que
    /// llega hasta x = 400; 6 verticales Ø13 a 56,5 mm de las caras.
    /// </summary>
    private static ColumnSection LColumn()
    {
        const double c = 40 + 10 + 6.5;
        var outline = new Polygon2D(new[] { new Vec2(0, 0), new Vec2(400, 0), new Vec2(400, 150), new Vec2(250, 150), new Vec2(250, 250), new Vec2(0, 250) });
        return new ColumnSection("C1", outline)
        {
            Cover = 40, TieDiameter = 10, LongitudinalBarDiameter = 13, TopElevation = 6000, BottomElevation = 0,
            LongitudinalBarPositions = new[]
            {
                new Vec2(c, c), new Vec2(250 - c, c), new Vec2(250 - c, 250 - c), new Vec2(c, 250 - c), // cuadrado
                new Vec2(400 - c, c), new Vec2(400 - c, 150 - c)                                        // extremo del brazo
            }
        };
    }

    /// <summary>Viga 150 x 300 con 2Ø10 por capa.</summary>
    private static BeamSection Beam(string name, Vec2 far, Vec2 near, int priority = 0)
        => new(name, far, near, 150, 300) { TopElevation = 3000, TopBarDiameter = 10, BottomBarDiameter = 10, TopBarCount = 2, BottomBarCount = 2, CrossingPriority = priority };

    /// <summary>VB llega por el extremo del brazo (x = 400, eje y = 75, a paño con y = 0); VA baja por el cuadrado (y = 250, eje x = 75, a paño con x = 0).</summary>
    private static (BeamSection VB, BeamSection VA) LBeams(int priorityA = 0, int priorityB = 0)
        => (Beam("VB", new Vec2(3000, 75), new Vec2(400, 75), priorityB), Beam("VA", new Vec2(75, 3000), new Vec2(75, 250), priorityA));

    [Fact]
    public void CornerJoint_BeamBarsClearColumnVerticalsAndTheBeamWithSlackRetreatsItsHook()
    {
        var col = LColumn();
        var (vb, va) = LBeams();
        var a = new JointAnalyzer(Options()).Analyze(col, new[] { vb, va });

        var jb = a.Joints.Single(j => j.Beam.Name == "VB");
        var ja = a.Joints.Single(j => j.Beam.Name == "VA");
        Assert.Equal(400, jb.AvailableDepth, 3);
        Assert.Equal(250, ja.AvailableDepth, 3);
        var tb = jb.Layers.Single(l => l.Layer == BarLayer.Top);
        var ta = ja.Layers.Single(l => l.Layer == BarLayer.Top);
        var gb = tb.Group!; var ga = ta.Group!;

        // Regla 1: las barras de las dos vigas coincidían con verticales (a 56,5 mm de las caras) y se corren
        // hacia el eje; el brazo de 150 no deja sitio para 25 mm libres, así que quedan en contacto y se avisa.
        Assert.Contains(ga.Adjustments, s => s.Contains("corrida"));
        Assert.Contains(gb.Adjustments, s => s.Contains("corrida"));
        var xsA = Enumerable.Range(0, ga.Count).Select(i => ga.Centerline[^1].X + i * ga.Spacing * ja.LateralDirection.X).ToList();
        Assert.All(xsA, x => Assert.True(col.LongitudinalBarPositions.Min(p => Math.Abs(p.X - x)) >= 11.5 - 0.1, $"barra de VA en x = {x}"));
        Assert.All(xsA, x => Assert.InRange(x, 55, 95));
        var ysB = Enumerable.Range(0, gb.Count).Select(i => gb.Centerline[^1].Y + i * gb.Spacing * jb.LateralDirection.Y).ToList();
        Assert.All(ysB, y => Assert.True(col.LongitudinalBarPositions.Where(p => p.X > 300).Min(p => Math.Abs(p.Y - y)) >= 11.5 - 0.1, $"barra de VB en y = {y}"));
        Assert.Contains(jb.Diagnostics, d => d.Severity == Severity.Warning && d.Message.Contains("separación libre"));

        // Regla 2: VA no puede retrasar su gancho (solo tiene 200 mm útiles para ldh = 170), VB sí (351 útiles):
        // cede VB aunque sea la primera de la lista, con la separación completa de 25 mm.
        Assert.Contains(gb.Adjustments, s => s.Contains("retrasado") && !s.Contains("contacto"));
        Assert.DoesNotContain(ga.Adjustments, s => s.Contains("retrasado"));
        Assert.True(tb.Provided >= tb.HookRequired, $"prov. {tb.Provided} < ldh {tb.HookRequired}");
        Assert.True(tb.Provided < 346, $"no se retrasó: prov. {tb.Provided}");
        Assert.Equal(tb.Provided, gb.ProvidedLength, 3);
        Assert.True(gb.Centerline[^1].X >= 130, $"extremo de VB en x = {gb.Centerline[^1].X}");
        Assert.Equal(gb.Centerline[^1].XY, gb.FullCenterline[^1].XY);
        Assert.Contains(jb.Diagnostics, d => d.Message.Contains("holgura"));
        // Las patas de VB quedan a 25 mm libres o más de las barras de VA.
        foreach (var y in ysB)
            foreach (var x in xsA)
                Assert.True(Math.Abs(gb.Centerline[^1].X - x) >= 35 - 0.5, $"pata de VB en x = {gb.Centerline[^1].X} contra barra de VA en x = {x}");

        // Con el retraso, las barras de VB ya no cruzan las de VA: ninguna capa cambia de cota.
        Assert.DoesNotContain(gb.Adjustments, s => s.Contains("capa"));
        Assert.DoesNotContain(ga.Adjustments, s => s.Contains("capa"));
        Assert.Equal(ta.Elevation, tb.Elevation, 3);
    }

    [Fact]
    public void ManualPriorityIsOverriddenWhenTheOtherBeamCannotRetreat()
    {
        var col = LColumn();
        // VB elegida como principal: VA debería ceder, pero no puede sin perder ldh, así que cede VB y se explica.
        var (vb, va) = LBeams(priorityB: 1);
        var a = new JointAnalyzer(Options()).Analyze(col, new[] { vb, va });
        var jb = a.Joints.Single(j => j.Beam.Name == "VB");
        var gb = jb.Layers.Single(l => l.Layer == BarLayer.Top).Group!;
        var ga = a.Joints.Single(j => j.Beam.Name == "VA").Layers.Single(l => l.Layer == BarLayer.Top).Group!;
        Assert.Contains(gb.Adjustments, s => s.Contains("retrasado"));
        Assert.DoesNotContain(ga.Adjustments, s => s.Contains("retrasado"));
        Assert.Contains(jb.Diagnostics, d => d.Message.Contains("no puede retrasar"));
    }

    [Fact]
    public void CrossingLayersOfSecondaryBeamDropBelowThePrincipal()
    {
        // Columna 500 x 500 sin verticales conocidas; dos vigas 250 x 500 (3Ø16) por caras contiguas, centradas:
        // los ganchos no se estorban, pero las barras se cruzan en planta a la misma cota.
        var col = new ColumnSection("C1", Polygon2D.Rectangle(500, 500)) { Cover = 40, TieDiameter = 10, TopElevation = 6000 };
        var v1 = new BeamSection("V1", new Vec2(-3000, 0), new Vec2(-250, 0), 250, 500) { TopElevation = 3000 };
        var v2 = new BeamSection("V2", new Vec2(0, -3000), new Vec2(0, -250), 250, 600) { TopElevation = 3000 };
        var a = new JointAnalyzer(Options()).Analyze(col, new[] { v1, v2 });
        var j1 = a.Joints.Single(j => j.Beam.Name == "V1");
        var j2 = a.Joints.Single(j => j.Beam.Name == "V2");
        var t1 = j1.Layers.Single(l => l.Layer == BarLayer.Top);
        var t2 = j2.Layers.Single(l => l.Layer == BarLayer.Top);
        // V2 tiene más peralte: es la principal; la capa superior de V1 baja un diámetro más 25 mm.
        Assert.Equal(2942, t2.Elevation, 3);
        Assert.Equal(2942 - 41, t1.Elevation, 3);
        Assert.Contains(j1.Diagnostics, d => d.Message.Contains("se cruzan") && d.Message.Contains("mayor peralte"));
        Assert.All(t1.Group!.FullCenterline, p => Assert.True(p.Z <= 2942 - 41 + 1e-6));
    }

    [Fact]
    public void ExistingSetIsShiftedAsAWholeAndItsLengthReduced()
    {
        var col = LColumn();
        var (vb, _) = LBeams();
        var options = Options();
        var a = new JointAnalyzer(options).Analyze(col, new[] { vb }, resolveClashes: false);
        var joint = a.Joints[0];
        // Conjunto existente de 2Ø10 superiores de VB que termina en la cara de la columna: primera barra en y = 55,
        // la segunda 40 mm más allá (+y). Las dos coinciden con verticales del brazo (y = 56,5 y 93,5).
        var z = 2945.0;
        var bar = new ExistingBar(7, "VB", 10, new[] { new Vec3(2500, 55, z), new Vec3(400, 55, z) })
        {
            Count = 2, SetLength = 40, SetDirection = new Vec3(0, 1, 0)
        };
        var check = new ExistingBarChecker(options).Check(a, new[] { bar }).Single();
        var fix = new BarFixer(options).Plan(a.Column, joint, bar, check, out _)!;
        Assert.NotNull(fix);

        new JointDetailer(options).Apply(a, new[] { fix }, useNewGroup: (_, _) => false);

        // La primera barra (y = 55) coincidía con la vertical; se corre hacia el eje y queda en contacto con ella.
        Assert.InRange(Math.Abs(fix.LateralShift), 10, 20);
        Assert.NotNull(fix.SetLength);
        Assert.True(fix.SetLength!.Value < 40, $"longitud del conjunto {fix.SetLength}");
        // Toda la barra se corre lateralmente, no solo el tramo del nudo.
        var yExpected = 55 + fix.LateralShift * joint.LateralDirection.Y;
        Assert.Equal(yExpected, fix.Centerline[0].Y, 3);
        Assert.Equal(yExpected, fix.Centerline[^1].Y, 3);
        Assert.InRange(yExpected, 65, 75);
        Assert.Contains(fix.Adjustments, s => s.Contains("corrida"));
        Assert.Contains(joint.Diagnostics, d => d.Severity == Severity.Warning && d.Message.Contains("separación libre"));
    }

    [Fact]
    public void KeptExistingBarsNeverMoveAndTheOtherBeamYields()
    {
        var col = LColumn();
        var (vb, va) = LBeams();
        var options = Options();
        var a = new JointAnalyzer(options).Analyze(col, new[] { vb, va }, resolveClashes: false);
        var jb = a.Joints.Single(j => j.Beam.Name == "VB");
        var ja = a.Joints.Single(j => j.Beam.Name == "VA");
        // VA tiene barras existentes con gancho hasta el fondo del cuadrado (y = 55) que se conservan: aunque VB
        // no tenga holgura de sobra, es la única que se puede mover y cede.
        var z = 2945.0;
        var kept = new ExistingBar(9, "VA", 10, new[] { new Vec3(69, 2500, z), new Vec3(69, 55, z), new Vec3(69, 55, z - 120) })
        {
            BareCenterline = new[] { new Vec3(69, 2500, z), new Vec3(69, 55, z) }, Count = 2, SetLength = 26, SetDirection = new Vec3(1, 0, 0)
        };
        new JointDetailer(options).Apply(a, kept: new[] { new KeptBar(ja, BarLayer.Top, kept) },
            useNewGroup: (j, layer) => !(j.Beam.Name == "VA" && layer == BarLayer.Top));

        var gb = jb.Layers.Single(l => l.Layer == BarLayer.Top).Group!;
        Assert.Contains(gb.Adjustments, s => s.Contains("retrasado"));
        // La barra conservada no recibe ningún ajuste (no es un grupo ni una corrección) y VB queda libre de ella.
        var tb = jb.Layers.Single(l => l.Layer == BarLayer.Top);
        Assert.True(tb.Provided >= tb.HookRequired);
        Assert.True(Math.Abs(gb.Centerline[^1].X - 69) >= 35 - 0.5, $"pata de VB en x = {gb.Centerline[^1].X}");
    }
}
