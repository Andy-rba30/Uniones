namespace UnionesAcero.Core.Geometry;

/// <summary>Vector/punto 2D en planta (mm).</summary>
public readonly record struct Vec2(double X, double Y)
{
    public static readonly Vec2 Zero = new(0, 0);
    public static readonly Vec2 UnitX = new(1, 0);
    public static readonly Vec2 UnitY = new(0, 1);

    public double Length => Math.Sqrt(X * X + Y * Y);
    public double LengthSquared => X * X + Y * Y;

    public Vec2 Normalized()
    {
        var l = Length;
        return l < GeometryUtil.Epsilon ? Zero : new Vec2(X / l, Y / l);
    }

    /// <summary>Perpendicular a 90° en sentido antihorario.</summary>
    public Vec2 Perp() => new(-Y, X);

    public double Dot(Vec2 o) => X * o.X + Y * o.Y;

    /// <summary>Producto cruz escalar (z del producto vectorial).</summary>
    public double Cross(Vec2 o) => X * o.Y - Y * o.X;

    public double DistanceTo(Vec2 o) => (this - o).Length;

    /// <summary>Ángulo (rad) respecto al eje X, en (-π, π].</summary>
    public double Angle => Math.Atan2(Y, X);

    public static Vec2 operator +(Vec2 a, Vec2 b) => new(a.X + b.X, a.Y + b.Y);
    public static Vec2 operator -(Vec2 a, Vec2 b) => new(a.X - b.X, a.Y - b.Y);
    public static Vec2 operator -(Vec2 a) => new(-a.X, -a.Y);
    public static Vec2 operator *(Vec2 a, double s) => new(a.X * s, a.Y * s);
    public static Vec2 operator *(double s, Vec2 a) => new(a.X * s, a.Y * s);
    public static Vec2 operator /(Vec2 a, double s) => new(a.X / s, a.Y / s);

    public static Vec2 Lerp(Vec2 a, Vec2 b, double t) => a + (b - a) * t;

    public static Vec2 FromAngle(double radians) => new(Math.Cos(radians), Math.Sin(radians));

    public override string ToString() => $"({X:0.#}, {Y:0.#})";
}

/// <summary>Punto 3D (mm). Z es la cota.</summary>
public readonly record struct Vec3(double X, double Y, double Z)
{
    public Vec2 XY => new(X, Y);
    public double Length => Math.Sqrt(X * X + Y * Y + Z * Z);
    public static Vec3 From(Vec2 p, double z) => new(p.X, p.Y, z);
    public double DistanceTo(Vec3 o) => (this - o).Length;
    public double Dot(Vec3 o) => X * o.X + Y * o.Y + Z * o.Z;

    public Vec3 Cross(Vec3 o) => new(
        Y * o.Z - Z * o.Y,
        Z * o.X - X * o.Z,
        X * o.Y - Y * o.X);

    public Vec3 Normalized()
    {
        var l = Length;
        return l < GeometryUtil.Epsilon ? new Vec3(0, 0, 0) : new Vec3(X / l, Y / l, Z / l);
    }

    public static Vec3 operator +(Vec3 a, Vec3 b) => new(a.X + b.X, a.Y + b.Y, a.Z + b.Z);
    public static Vec3 operator -(Vec3 a, Vec3 b) => new(a.X - b.X, a.Y - b.Y, a.Z - b.Z);
    public static Vec3 operator *(Vec3 a, double s) => new(a.X * s, a.Y * s, a.Z * s);
    public static Vec3 operator *(double s, Vec3 a) => new(a.X * s, a.Y * s, a.Z * s);

    public override string ToString() => $"({X:0.#}, {Y:0.#}, {Z:0.#})";
}
