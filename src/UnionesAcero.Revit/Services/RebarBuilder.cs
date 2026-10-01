using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Structure;
using UnionesAcero.Core.Analysis;
using UnionesAcero.Core.Geometry;

namespace UnionesAcero.Revit.Services;

/// <summary>Crea elementos Rebar en Revit a partir de los grupos de barras del núcleo.</summary>
public sealed class RebarBuilder
{
    private readonly Document _doc;
    private readonly List<RebarBarType> _barTypes;
    private readonly List<RebarHookType> _hookTypes;

    public RebarBuilder(Document doc)
    {
        _doc = doc;
        _barTypes = new FilteredElementCollector(doc).OfClass(typeof(RebarBarType)).Cast<RebarBarType>().ToList();
        _hookTypes = new FilteredElementCollector(doc).OfClass(typeof(RebarHookType)).Cast<RebarHookType>().ToList();
    }

    public List<string> Notes { get; } = new();

    public bool HasBarTypes => _barTypes.Count > 0;

    /// <summary>Crea el grupo de barras. Devuelve null si no fue posible.</summary>
    public Rebar? Create(BarGroup group, Element host)
    {
        var barType = ClosestBarType(group.Diameter);
        if (barType == null)
        {
            Notes.Add($"{group.Label}: el proyecto no tiene tipos de barra (RebarBarType).");
            return null;
        }
        var actualDb = Units.ToMm(barType.BarNominalDiameter);
        if (Math.Abs(actualDb - group.Diameter) > 0.5)
            Notes.Add($"{group.Label}: no existe tipo Ø{group.Diameter:0}; se usa {barType.Name} (Ø{actualDb:0.#}).");

        var curves = new List<Curve>();
        for (var i = 0; i + 1 < group.Centerline.Count; i++)
        {
            var a = Units.ToXyz(group.Centerline[i]);
            var b = Units.ToXyz(group.Centerline[i + 1]);
            if (a.DistanceTo(b) < 1e-4) continue;
            curves.Add(Line.CreateBound(a, b));
        }
        if (curves.Count == 0)
        {
            Notes.Add($"{group.Label}: geometría vacía.");
            return null;
        }

        var normal = Units.ToXyzDirection(group.PlaneNormal).Normalize();
        var terminations = new BarTerminationsData(_doc);

        if (group.EndHook != null)
        {
            var hook = HookType((int)group.EndHook.Angle);
            if (hook == null)
            {
                Notes.Add($"{group.Label}: el proyecto no tiene un tipo de gancho de {(int)group.EndHook.Angle}°; la barra se crea sin gancho. Revisar manualmente.");
            }
            else
            {
                terminations.HookTypeIdAtEnd = hook.Id;
                terminations.TerminationOrientationAtEnd = HookOrientation(group, normal);
                var ext = Units.ToMm(hook.GetHookExtensionLength(barType));
                if (Math.Abs(ext - group.EndHook.Extension) > 1)
                    Notes.Add($"{group.Label}: el gancho '{hook.Name}' tiene extensión {ext:0} mm; la norma pide {group.EndHook.Extension:0} mm.");
            }
        }

        Rebar? rebar;
        try
        {
            rebar = Rebar.CreateFromCurves(_doc, RebarStyle.Standard, barType, host, normal, curves, terminations,
                useExistingShapeIfPossible: true, createNewShape: true);
        }
        catch (Autodesk.Revit.Exceptions.ApplicationException ex)
        {
            Notes.Add($"{group.Label}: Revit no pudo crear la barra ({ex.Message}).");
            return null;
        }
        if (rebar == null)
        {
            Notes.Add($"{group.Label}: Revit no pudo crear la barra (sin forma compatible en el proyecto; cargar familias de formas de armadura).");
            return null;
        }

        if (group.Count > 1 && group.Spacing > 0)
        {
            var arrayLength = Units.ToFt((group.Count - 1) * group.Spacing);
            rebar.GetShapeDrivenAccessor().SetLayoutAsFixedNumber(group.Count, arrayLength, barsOnNormalSide: true, includeFirstBar: true, includeLastBar: true);
        }

        var comments = rebar.get_Parameter(BuiltInParameter.ALL_MODEL_INSTANCE_COMMENTS);
        if (comments != null && !comments.IsReadOnly) comments.Set("UnionesAcero: " + group.Comment);

        return rebar;
    }

    private RebarBarType? ClosestBarType(double diameterMm)
    {
        if (_barTypes.Count == 0) return null;
        return _barTypes.OrderBy(t => Math.Abs(Units.ToMm(t.BarNominalDiameter) - diameterMm)).First();
    }

    private RebarHookType? HookType(int angleDeg)
    {
        var target = angleDeg * Math.PI / 180.0;
        return _hookTypes
            .Where(h => h.Style == RebarStyle.Standard && Math.Abs(h.HookAngle - target) < 0.02)
            .OrderBy(h => h.Name.Contains(angleDeg.ToString()) ? 0 : 1)
            .FirstOrDefault();
    }

    /// <summary>
    /// Orientación del gancho (Left/Right) según la API: de pie en el extremo de la barra, con la barra
    /// detrás y la normal del plano como "arriba", la derecha es (dirección × normal).
    /// </summary>
    private static RebarTerminationOrientation HookOrientation(BarGroup group, XYZ normal)
    {
        var n = group.Centerline.Count;
        var last = group.Centerline[n - 1];
        var prev = group.Centerline[n - 2];
        var dir = Units.ToXyzDirection((last - prev).Normalized());
        var right = dir.CrossProduct(normal);
        var hookDir = Units.ToXyzDirection(group.EndHook!.Direction);
        return hookDir.DotProduct(right) > 0 ? RebarTerminationOrientation.Right : RebarTerminationOrientation.Left;
    }
}
