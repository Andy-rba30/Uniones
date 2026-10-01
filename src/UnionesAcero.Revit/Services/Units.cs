using Autodesk.Revit.DB;
using UnionesAcero.Core.Geometry;

namespace UnionesAcero.Revit.Services;

/// <summary>Conversión entre unidades internas de Revit (pies) y el núcleo (mm).</summary>
public static class Units
{
    private const double FeetToMm = 304.8;

    public static double ToMm(double feet) => feet * FeetToMm;
    public static double ToFt(double mm) => mm / FeetToMm;

    public static Vec2 ToMm2(XYZ p) => new(ToMm(p.X), ToMm(p.Y));
    public static Vec3 ToMm3(XYZ p) => new(ToMm(p.X), ToMm(p.Y), ToMm(p.Z));
    public static XYZ ToXyz(Vec3 p) => new(ToFt(p.X), ToFt(p.Y), ToFt(p.Z));
    public static XYZ ToXyzDirection(Vec3 d) => new(d.X, d.Y, d.Z);
}
