using UnionesAcero.Core.Geometry;
using Xunit;

namespace UnionesAcero.Core.Tests;

public class GeometryTests
{
    public static Polygon2D TColumn() => new(new[]
    {
        // Ala de 1000 x 250 (y 0..250) y alma de 250 x 750 centrada (x 375..625, y 250..1000)
        new Vec2(0, 0), new Vec2(1000, 0), new Vec2(1000, 250), new Vec2(625, 250),
        new Vec2(625, 1000), new Vec2(375, 1000), new Vec2(375, 250), new Vec2(0, 250)
    });

    [Fact]
    public void RectangleIsCcwAndHasArea()
    {
        var r = Polygon2D.Rectangle(400, 600);
        Assert.Equal(240000, r.Area, 3);
        Assert.True(r.IsConvex);
        Assert.Equal(new Vec2(0, 0), r.Centroid);
    }

    [Fact]
    public void ClockwiseInputIsReversed()
    {
        var cw = new Polygon2D(new[] { new Vec2(0, 0), new Vec2(0, 1), new Vec2(1, 1), new Vec2(1, 0) });
        var ccw = new Polygon2D(new[] { new Vec2(0, 0), new Vec2(1, 0), new Vec2(1, 1), new Vec2(0, 1) });
        Assert.Equal(new Vec2(0, -1), ccw.OutwardNormal(0));
        // Tras invertir, todas las normales exteriores apuntan hacia fuera del centroide.
        foreach (var poly in new[] { cw, ccw })
            for (var i = 0; i < poly.Count; i++)
                Assert.True(poly.OutwardNormal(i).Dot(poly.Edge(i).MidPoint - poly.Centroid) > 0);
    }

    [Fact]
    public void TColumnContainsAndArea()
    {
        var t = TColumn();
        Assert.Equal(1000 * 250 + 250 * 750, t.Area, 3);
        Assert.False(t.IsConvex);
        Assert.True(t.Contains(new Vec2(500, 125)));
        Assert.True(t.Contains(new Vec2(500, 800)));
        Assert.False(t.Contains(new Vec2(100, 800)));
        Assert.True(t.Contains(new Vec2(0, 100))); // borde
    }

    [Fact]
    public void RayDepthThroughTColumn()
    {
        var t = TColumn();
        // Desde la cara frontal del ala en x=150 hacia +Y: solo atraviesa el ala.
        Assert.Equal(250, t.RayDepth(new Vec2(150, 0), Vec2.UnitY), 3);
        // Desde la cara frontal en x=500: ala + alma.
        Assert.Equal(1000, t.RayDepth(new Vec2(500, 0), Vec2.UnitY), 3);
        // Desde el extremo izquierdo del ala hacia +X.
        Assert.Equal(1000, t.RayDepth(new Vec2(0, 125), Vec2.UnitX), 3);
    }

    [Fact]
    public void LineClipFindsReentry()
    {
        var t = TColumn();
        // Recta horizontal a y = 500 atraviesa solo el alma.
        var clips = t.LineClip(new Vec2(-100, 500), Vec2.UnitX);
        Assert.Single(clips);
        Assert.Equal(475, clips[0].TIn, 3);
        Assert.Equal(725, clips[0].TOut, 3);
    }

    [Fact]
    public void InwardOffsetOfRectangle()
    {
        var r = Polygon2D.Rectangle(400, 600);
        var o = r.InwardOffset(50);
        Assert.Equal(300 * 500, o.Area, 3);
    }

    [Fact]
    public void InwardOffsetOfTColumnKeepsShape()
    {
        var t = TColumn();
        var o = t.InwardOffset(40);
        Assert.Equal(8, o.Count);
        // Ala: (1000-80) x (250-40 (solo un lado abierto hacia el alma)) ... comprobamos puntos clave.
        Assert.True(o.Contains(new Vec2(500, 125)));
        Assert.False(o.Contains(new Vec2(20, 125)));
        Assert.True(o.Contains(new Vec2(500, 950)));
        Assert.False(o.Contains(new Vec2(390, 900)));
        Assert.True(o.Area < t.Area);
    }

    [Fact]
    public void ClipSegmentLength()
    {
        var r = Polygon2D.Rectangle(400, 400);
        var len = r.ClipSegmentLength(new Segment2D(new Vec2(-500, 0), new Vec2(100, 0)));
        Assert.Equal(300, len, 3);
    }

    [Fact]
    public void SegmentsIntersect()
    {
        var a = new Segment2D(new Vec2(0, 0), new Vec2(10, 10));
        var b = new Segment2D(new Vec2(0, 10), new Vec2(10, 0));
        Assert.True(a.Intersects(b, out var p));
        Assert.Equal(new Vec2(5, 5), p);
        var c = new Segment2D(new Vec2(20, 0), new Vec2(30, 0));
        Assert.False(a.Intersects(c, out _));
    }
}
