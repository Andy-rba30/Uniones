namespace UnionesAcero.Core.Geometry;

public static class GeometryUtil
{
    /// <summary>Tolerancia general en mm.</summary>
    public const double Epsilon = 1e-6;

    /// <summary>Tolerancia de coincidencia geométrica "práctica" (mm).</summary>
    public const double Tolerance = 0.5;

    public static bool NearlyEqual(double a, double b, double tol = Tolerance) => Math.Abs(a - b) <= tol;

    public static double DegToRad(double deg) => deg * Math.PI / 180.0;
    public static double RadToDeg(double rad) => rad * 180.0 / Math.PI;

    /// <summary>Ángulo sin signo (rad) entre dos vectores, en [0, π].</summary>
    public static double AngleBetween(Vec2 a, Vec2 b)
    {
        var an = a.Normalized();
        var bn = b.Normalized();
        var d = Math.Clamp(an.Dot(bn), -1.0, 1.0);
        return Math.Acos(d);
    }

    /// <summary>¿Los vectores son paralelos (mismo sentido) dentro de un ángulo dado?</summary>
    public static bool SameDirection(Vec2 a, Vec2 b, double maxAngleDeg = 10)
        => AngleBetween(a, b) <= DegToRad(maxAngleDeg);

    /// <summary>¿Los vectores son opuestos dentro de un ángulo dado?</summary>
    public static bool OppositeDirection(Vec2 a, Vec2 b, double maxAngleDeg = 10)
        => AngleBetween(a, -b) <= DegToRad(maxAngleDeg);

    /// <summary>Redondea hacia arriba a un múltiplo (p. ej. longitudes de corte a 10 mm).</summary>
    public static double RoundUpTo(double value, double multiple)
    {
        if (multiple <= 0) return value;
        return Math.Ceiling(value / multiple - 1e-9) * multiple;
    }
}
