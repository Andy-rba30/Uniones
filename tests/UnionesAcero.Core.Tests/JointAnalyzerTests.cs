using UnionesAcero.Core.Analysis;
using UnionesAcero.Core.Codes;
using UnionesAcero.Core.Geometry;
using UnionesAcero.Core.Model;
using UnionesAcero.Core.Reporting;
using Xunit;

namespace UnionesAcero.Core.Tests;

public class JointAnalyzerTests
{
    private static JointOptions Options(bool seismic = true) => new()
    {
        Code = new Aci318Code(),
        Materials = new Materials { Fy = 420, Fc = 21 },
        SeismicJoint = seismic
    };

    private static BeamSection Beam(string name, Vec2 far, Vec2 near, double w = 250, double h = 500, double db = 16, int n = 3, double top = 3000)
        => new(name, far, near, w, h)
        {
            TopElevation = top, TopBarDiameter = db, BottomBarDiameter = db, TopBarCount = n, BottomBarCount = n
        };

    [Fact]
    public void ExteriorJointOnRectangularColumnUsesHooks()
    {
        var col = new ColumnSection("C1", Polygon2D.Rectangle(400, 400)) { Cover = 40, TieDiameter = 10, TopElevation = 6000, BottomElevation = 0 };
        // Viga llega por la cara oeste (x = -200) viniendo desde x = -3000
        var beam = Beam("V1", new Vec2(-3000, 0), new Vec2(-200, 0));
        var a = new JointAnalyzer(Options()).Analyze(col, new[] { beam });

        var j = Assert.Single(a.Joints);
        Assert.True(j.Connected);
        Assert.Equal(400, j.AvailableDepth, 3);
        Assert.Equal(350, j.UsableDepth, 3);
        Assert.Equal(new Vec2(-200, 0), j.ContactPoint);
        Assert.Equal(Vec2.UnitX, j.InwardDirection);

        var top = j.Layers.Single(l => l.Layer == BarLayer.Top);
        var bottom = j.Layers.Single(l => l.Layer == BarLayer.Bottom);
        Assert.Equal(AnchorageDecision.Hook90, top.Decision);
        Assert.Equal(AnchorageDecision.Hook90, bottom.Decision);
        Assert.Equal(272, top.HookRequired);
        Assert.Equal(350, top.Provided);

        var g = top.Group!;
        Assert.NotNull(g.EndHook);
        Assert.Equal(-1, g.EndHook!.Direction.Z);          // superiores: gancho hacia abajo
        Assert.Equal(1, bottom.Group!.EndHook!.Direction.Z); // inferiores: hacia arriba
        Assert.Equal(192, g.EndHook.Extension);
        Assert.Equal(3, g.Count);
        // Cota de la capa superior: 3000 - 40 - 10 - 8
        Assert.Equal(2942, g.Elevation, 3);
        // El vértice del gancho queda db/2 antes de la profundidad útil: x = -200 + 350 - 8 = 142
        Assert.Equal(142, g.Centerline[^1].X, 3);
        // La extensión en la viga: 2h + 1.3·ld(no sísmico, superior = 908) = 1000 + 1180.4 → 2190
        Assert.Equal(-200 - 2190, g.Centerline[0].X, 3);
        Assert.Equal(3, g.FullCenterline.Count);
        Assert.Equal(2942 - 192, g.FullCenterline[^1].Z, 3);
        Assert.False(a.HasErrors);
    }

    [Fact]
    public void LargeColumnAllowsStraightAnchorage()
    {
        var col = new ColumnSection("C1", Polygon2D.Rectangle(1200, 400)) { Cover = 40, TieDiameter = 10 };
        var beam = Beam("V1", new Vec2(-3000, 0), new Vec2(-600, 0));
        var a = new JointAnalyzer(Options()).Analyze(col, new[] { beam });
        var top = a.Joints[0].Layers.Single(l => l.Layer == BarLayer.Top);
        Assert.Equal(AnchorageDecision.Straight, top.Decision);
        Assert.Equal(883, top.StraightRequired);
        Assert.Equal(1150, top.Provided); // hasta la cara lejana del núcleo
        Assert.Null(top.Group!.EndHook);
    }

    [Fact]
    public void EmbedExactlyRequiredWhenNotToFarFace()
    {
        var col = new ColumnSection("C1", Polygon2D.Rectangle(1200, 400)) { Cover = 40, TieDiameter = 10 };
        var beam = Beam("V1", new Vec2(-3000, 0), new Vec2(-600, 0));
        var a = new JointAnalyzer(Options() with { EmbedToFarFace = false }).Analyze(col, new[] { beam });
        var top = a.Joints[0].Layers.Single(l => l.Layer == BarLayer.Top);
        Assert.Equal(890, top.Provided); // 883 redondeado a 10
    }

    [Fact]
    public void BeamModeledToColumnCentreStillConnects()
    {
        var col = new ColumnSection("C1", Polygon2D.Rectangle(400, 400));
        var beam = Beam("V1", new Vec2(-3000, 0), new Vec2(0, 0)); // extremo en el centro de la columna
        var a = new JointAnalyzer(Options()).Analyze(col, new[] { beam });
        var j = a.Joints[0];
        Assert.True(j.Connected);
        Assert.Equal(new Vec2(-200, 0), j.ContactPoint);
        Assert.Equal(0, j.Gap, 3);
    }

    [Fact]
    public void BeamFarFromColumnIsNotConnected()
    {
        var col = new ColumnSection("C1", Polygon2D.Rectangle(400, 400));
        var beam = Beam("V1", new Vec2(-3000, 0), new Vec2(-800, 0));
        var a = new JointAnalyzer(Options()).Analyze(col, new[] { beam });
        Assert.False(a.Joints[0].Connected);
        Assert.True(a.HasErrors);
    }

    [Fact]
    public void CollinearBeamsProducePassThroughBars()
    {
        var col = new ColumnSection("C1", Polygon2D.Rectangle(400, 400));
        var v1 = Beam("V1", new Vec2(-3000, 0), new Vec2(-200, 0));
        var v2 = Beam("V2", new Vec2(3000, 0), new Vec2(200, 0));
        var a = new JointAnalyzer(Options()).Analyze(col, new[] { v1, v2 });

        Assert.All(a.Joints, j => Assert.NotNull(j.OppositeBeam));
        Assert.All(a.Joints.SelectMany(j => j.Layers), l => Assert.Equal(AnchorageDecision.PassThrough, l.Decision));
        // Solo se generan grupos una vez (desde V1): 2 capas
        var groups = a.BarGroups.ToList();
        Assert.Equal(2, groups.Count);
        Assert.All(groups, g => Assert.Equal("V1", g.BeamName));
        var g0 = groups[0];
        // De -200-ext a +200+ext, pasando por la columna
        Assert.True(g0.Centerline[0].X < -200 && g0.Centerline[^1].X > 200);
        // 400 ≥ 20·16 = 320 → sin error
        Assert.False(a.HasErrors);
    }

    [Fact]
    public void PassThroughOnThinColumnIsAnError()
    {
        var col = new ColumnSection("C1", Polygon2D.Rectangle(250, 600));
        var v1 = Beam("V1", new Vec2(-3000, 0), new Vec2(-125, 0));
        var v2 = Beam("V2", new Vec2(3000, 0), new Vec2(125, 0));
        var a = new JointAnalyzer(Options()).Analyze(col, new[] { v1, v2 });
        Assert.True(a.HasErrors);
        Assert.Contains(a.AllDiagnostics, d => d.Message.Contains("pasantes") && d.Message.Contains("320"));
    }

    [Fact]
    public void TColumnWithTwoBeamsLikeScreenshot()
    {
        // Columna en T (ala 1000x250 abajo, alma 250x750 arriba). Dos vigas 150x250:
        //  - V1 llega al extremo izquierdo del ala (cara x=0) viniendo desde la izquierda.
        //  - V2 llega a la cara frontal del ala (y=0) en x=150, viniendo desde abajo: solo hay 250 mm de ala.
        var col = new ColumnSection("C1", GeometryTests.TColumn()) { Cover = 40, TieDiameter = 10, TopElevation = 6000, BottomElevation = 0 };
        var v1 = Beam("V1", new Vec2(-2000, 125), new Vec2(0, 125), w: 150, h: 250, db: 12, n: 2, top: 3000);
        var v2 = Beam("V2", new Vec2(150, -2000), new Vec2(150, 0), w: 150, h: 250, db: 12, n: 2, top: 3000);
        var a = new JointAnalyzer(Options()).Analyze(col, new[] { v1, v2 });

        var j1 = a.Joints.Single(j => j.Beam.Name == "V1");
        var j2 = a.Joints.Single(j => j.Beam.Name == "V2");
        Assert.True(j1.Connected && j2.Connected);
        Assert.Equal(1000, j1.AvailableDepth, 3);   // atraviesa toda el ala
        Assert.Equal(250, j2.AvailableDepth, 3);    // solo el espesor del ala
        Assert.Equal(200, j2.UsableDepth, 3);

        // Ø12 sísmico: ldh = 420·12/(5.4·4.583) = 203.6 → 204; ld sup = 3.25·204 = 663
        var l1 = j1.Layers.Single(l => l.Layer == BarLayer.Top);
        Assert.Equal(AnchorageDecision.Straight, l1.Decision);
        Assert.Equal(950, l1.Provided);

        var l2 = j2.Layers.Single(l => l.Layer == BarLayer.Top);
        Assert.Equal(AnchorageDecision.Insufficient, l2.Decision);
        Assert.Equal(204, l2.HookRequired);
        Assert.True(a.HasErrors);
        Assert.Contains(j2.Diagnostics, d => d.Severity == Severity.Error && d.Message.Contains("faltan 4 mm"));

        // Las dos vigas no se cruzan dentro de la columna (V2 entra en x=150, V1 recorre y=125 → sí se cruzan en planta)
        // V1 va por y=125 a lo largo de todo el ala, V2 sube por x=150 hasta y=200: se cruzan → la capa de V2 se desplaza.
        Assert.Contains(j2.Diagnostics, d => d.Message.Contains("se cruzan"));
        var shifted = j2.Layers.Single(l => l.Layer == BarLayer.Top);
        Assert.True(shifted.Elevation < l1.Elevation);

        var text = ReportFormatter.Detailed(a);
        Assert.Contains("INSUFICIENTE", text);
        Assert.Contains("V1", text);
    }

    [Fact]
    public void TColumnBeamIntoWebGetsFullDepth()
    {
        var col = new ColumnSection("C1", GeometryTests.TColumn()) { Cover = 40, TieDiameter = 10 };
        // Viga por la cara frontal del ala en x=500: atraviesa ala + alma = 1000 mm (útil 950 ≥ ld sup. 883)
        var v = Beam("V3", new Vec2(500, -2000), new Vec2(500, 0), w: 250, h: 500, db: 16, n: 3);
        var a = new JointAnalyzer(Options()).Analyze(col, new[] { v });
        var j = a.Joints[0];
        Assert.Equal(1000, j.AvailableDepth, 3);
        Assert.Equal(0, j.LateralOverhang, 3);
        Assert.Equal(AnchorageDecision.Straight, j.Layers[0].Decision);
    }

    [Fact]
    public void BeamWiderThanFaceIsReported()
    {
        var col = new ColumnSection("C1", GeometryTests.TColumn());
        // Viga de 400 de ancho llegando al alma (250 de ancho) por arriba
        var v = Beam("V4", new Vec2(500, 3000), new Vec2(500, 1000), w: 400, h: 500);
        var a = new JointAnalyzer(Options()).Analyze(col, new[] { v });
        Assert.Equal(75, a.Joints[0].LateralOverhang, 3);
        Assert.Contains(a.Joints[0].Diagnostics, d => d.Message.Contains("sobresale"));
    }

    [Fact]
    public void RoofJointFlipsBottomHookDown()
    {
        // Columna que termina en la cara superior de una viga de 250: el gancho hacia arriba (12·db = 192 mm)
        // de la capa inferior (cota 2808) superaría la cota 2960 (tope − recubrimiento).
        var col = new ColumnSection("C1", Polygon2D.Rectangle(400, 400)) { TopElevation = 3000, BottomElevation = 0 };
        var beam = Beam("V1", new Vec2(-3000, 0), new Vec2(-200, 0), h: 250, top: 3000);
        var a = new JointAnalyzer(Options()).Analyze(col, new[] { beam });
        var bottom = a.Joints[0].Layers.Single(l => l.Layer == BarLayer.Bottom);
        Assert.Equal(-1, bottom.Group!.EndHook!.Direction.Z);
        Assert.Contains(a.Joints[0].Diagnostics, d => d.Message.Contains("cubierta"));
    }

    [Fact]
    public void DuplicateBeamOnSameFaceIsReported()
    {
        var col = new ColumnSection("C1", Polygon2D.Rectangle(400, 400));
        var v1 = Beam("V1", new Vec2(-3000, 0), new Vec2(-200, 0));
        var v2 = Beam("V1 copia", new Vec2(-2500, 10), new Vec2(-200, 10));
        var a = new JointAnalyzer(Options()).Analyze(col, new[] { v1, v2 });
        Assert.All(a.Joints, j => Assert.Contains(j.Diagnostics, d => d.Severity == Severity.Warning && d.Message.Contains("duplicada")));
        // Dos vigas distintas por caras distintas no se confunden con duplicadas.
        var v3 = Beam("V3", new Vec2(0, -3000), new Vec2(0, -200));
        var b = new JointAnalyzer(Options()).Analyze(col, new[] { v1, v3 });
        Assert.DoesNotContain(b.AllDiagnostics, d => d.Message.Contains("duplicada"));
    }

    [Fact]
    public void CrossingBeamsWithoutAutoStaggerWarn()
    {
        var col = new ColumnSection("C1", Polygon2D.Rectangle(400, 400));
        var v1 = Beam("V1", new Vec2(-3000, 0), new Vec2(-200, 0));
        var v2 = Beam("V2", new Vec2(0, -3000), new Vec2(0, -200));
        var a = new JointAnalyzer(Options() with { AutoStaggerCrossingLayers = false }).Analyze(col, new[] { v1, v2 });
        Assert.Contains(a.AllDiagnostics, d => d.Severity == Severity.Warning && d.Message.Contains("misma cota"));
    }
}
