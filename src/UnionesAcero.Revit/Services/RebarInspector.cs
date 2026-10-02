using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Structure;
using UnionesAcero.Core.Geometry;
using UnionesAcero.Core.Verification;

namespace UnionesAcero.Revit.Services;

/// <summary>Convierte las barras modeladas en las vigas al formato del verificador del núcleo.</summary>
public static class RebarInspector
{
    /// <summary>Barras longitudinales (no estribos) alojadas en la viga, una entrada por conjunto (Rebar).</summary>
    public static List<(Rebar Rebar, ExistingBar Bar)> CollectBars(Document doc, FamilyInstance beam, string beamName)
    {
        var result = new List<(Rebar, ExistingBar)>();
        RebarHostData? host;
        try { host = RebarHostData.GetRebarHostData(beam); }
        catch { return result; }
        if (host == null) return result;

        foreach (var rebar in host.GetRebarsInHost())
        {
            if (doc.GetElement(rebar.GetShapeId()) is RebarShape shape && shape.RebarStyle == RebarStyle.StirrupTie) continue;
            var bar = Describe(doc, rebar, beamName);
            if (bar != null) result.Add((rebar, bar));
        }
        return result;
    }

    /// <summary>
    /// Eje (con y sin ganchos) de la primera posición existente del conjunto. Con una posición basta
    /// para el veredicto y para reconstruir el conjunto: todas las barras del set son iguales.
    /// </summary>
    public static ExistingBar? Describe(Document doc, Rebar rebar, string beamName)
    {
        if (doc.GetElement(rebar.GetTypeId()) is not RebarBarType bt) return null;
        var db = Units.ToMm(bt.BarNominalDiameter);
        var positions = Math.Max(1, rebar.NumberOfBarPositions);
        for (var i = 0; i < positions; i++)
        {
            if (!rebar.DoesBarExistAtPosition(i)) continue;
            IList<Curve> hooked, bare;
            try
            {
                // Sin radios de doblado (esquinas vivas); una vez con ganchos y otra sin ellos.
                hooked = rebar.GetCenterlineCurves(false, false, true, MultiplanarOption.IncludeOnlyPlanarCurves, i);
                bare = rebar.GetCenterlineCurves(false, true, true, MultiplanarOption.IncludeOnlyPlanarCurves, i);
            }
            catch (Autodesk.Revit.Exceptions.ApplicationException)
            {
                continue;
            }
            var pts = ToPolyline(hooked);
            if (pts.Count < 2) continue;
            var barePts = ToPolyline(bare);
            double setLength = 0;
            Vec3? setDir = null;
            if (positions > 1 && rebar.IsRebarShapeDriven())
            {
                try
                {
                    var acc = rebar.GetShapeDrivenAccessor();
                    setLength = Units.ToMm(acc.ArrayLength);
                    var n = acc.Normal.Normalize();
                    if (!acc.BarsOnNormalSide) n = -n;
                    setDir = new Vec3(n.X, n.Y, n.Z);
                }
                catch (Autodesk.Revit.Exceptions.ApplicationException)
                {
                    setLength = 0; setDir = null;
                }
            }
            return new ExistingBar(rebar.Id.Value, beamName, db, pts, i > 0 ? $"posición {i + 1}" : null)
            {
                BareCenterline = barePts.Count >= 2 ? barePts : pts,
                Count = positions,
                SetLength = setLength,
                SetDirection = setDir
            };
        }
        return null;
    }

    private static List<Vec3> ToPolyline(IList<Curve> curves)
    {
        var pts = new List<Vec3>();
        foreach (var c in curves)
        {
            var tess = c.Tessellate();
            foreach (var p in tess)
            {
                var v = Units.ToMm3(p);
                if (pts.Count == 0 || pts[^1].DistanceTo(v) > 0.5) pts.Add(v);
            }
        }
        return pts;
    }
}
