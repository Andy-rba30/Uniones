namespace UnionesAcero.Core.Geometry;

/// <summary>Segmento de recta en planta.</summary>
public readonly record struct Segment2D(Vec2 A, Vec2 B)
{
    public Vec2 Direction => (B - A).Normalized();
    public double Length => (B - A).Length;
    public Vec2 MidPoint => Vec2.Lerp(A, B, 0.5);

    public Vec2 PointAt(double t) => Vec2.Lerp(A, B, t);

    /// <summary>Parámetro t (no acotado) de la proyección del punto sobre la recta soporte.</summary>
    public double ProjectParameter(Vec2 p)
    {
        var ab = B - A;
        var l2 = ab.LengthSquared;
        return l2 < GeometryUtil.Epsilon ? 0 : (p - A).Dot(ab) / l2;
    }

    public Vec2 ClosestPoint(Vec2 p)
    {
        var t = Math.Clamp(ProjectParameter(p), 0, 1);
        return PointAt(t);
    }

    public double DistanceTo(Vec2 p) => ClosestPoint(p).DistanceTo(p);

    /// <summary>
    /// Intersección de la recta infinita (origin + t·dir) con este segmento.
    /// Devuelve t sobre la recta y s en [0,1] sobre el segmento.
    /// </summary>
    public bool IntersectLine(Vec2 origin, Vec2 dir, out double t, out double s)
    {
        var e = B - A;
        var denom = dir.Cross(e);
        t = s = 0;
        if (Math.Abs(denom) < GeometryUtil.Epsilon) return false; // paralelos
        var ao = A - origin;
        t = ao.Cross(e) / denom;
        s = ao.Cross(dir) / denom;
        return s >= -1e-9 && s <= 1 + 1e-9;
    }

    /// <summary>Intersección entre dos segmentos (acotados).</summary>
    public bool Intersects(Segment2D other, out Vec2 point)
    {
        point = default;
        if (!IntersectLine(other.A, other.B - other.A, out var t, out var s)) return false;
        // t está sobre la recta "other" parametrizada por A..B (dir = B-A), por eso t∈[0,1]
        if (t < -1e-9 || t > 1 + 1e-9) return false;
        point = other.A + (other.B - other.A) * t;
        _ = s;
        return true;
    }

    public override string ToString() => $"[{A} → {B}]";
}
