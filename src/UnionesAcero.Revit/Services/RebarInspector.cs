using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Structure;
using UnionesAcero.Core.Geometry;
using UnionesAcero.Core.Verification;

namespace UnionesAcero.Revit.Services;

/// <summary>Convierte las barras modeladas en las vigas al formato del verificador del núcleo.</summary>
public static class RebarInspector
{
    public static List<(Rebar Rebar, ExistingBar Bar)> CollectBars(Document doc, FamilyInstance beam, string beamName)
    {
        var result = new List<(Rebar, ExistingBar)>();
        var host = RebarHostData.GetRebarHostData(beam);
        if (host == null) return result;

        foreach (var rebar in host.GetRebarsInHost())
        {
            if (doc.GetElement(rebar.GetTypeId()) is not RebarBarType bt) continue;
            var db = Units.ToMm(bt.BarNominalDiameter);
            var positions = Math.Max(1, rebar.NumberOfBarPositions);
            for (var i = 0; i < positions; i++)
            {
                if (!rebar.DoesBarExistAtPosition(i)) continue;
                IList<Curve> curves;
                try
                {
                    // Sin radios de doblado (esquinas vivas) y con ganchos incluidos.
                    curves = rebar.GetCenterlineCurves(false, false, true, MultiplanarOption.IncludeOnlyPlanarCurves, i);
                }
                catch (Autodesk.Revit.Exceptions.ApplicationException)
                {
                    continue;
                }
                var pts = ToPolyline(curves);
                if (pts.Count < 2) continue;
                result.Add((rebar, new ExistingBar(rebar.Id.Value, beamName, db, pts, i > 0 ? $"posición {i + 1}" : null)));
                // Con una posición basta para el veredicto del conjunto: todas las barras del set son iguales.
                break;
            }
        }
        return result;
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
