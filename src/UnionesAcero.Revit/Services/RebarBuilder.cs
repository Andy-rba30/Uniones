using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Structure;
using UnionesAcero.Core.Analysis;
using UnionesAcero.Core.Geometry;
using UnionesAcero.Core.Verification;

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

    /// <summary>Nombre (o fragmento) del tipo de gancho de 90° a usar. Vacío = el primero de 90° del proyecto.</summary>
    public string HookTypeName { get; set; } = "";

    /// <summary>Tipo de barra por nombre exacto o fragmento (null si no hay coincidencia).</summary>
    public RebarBarType? BarTypeByName(string? name) => MatchName(_barTypes, name);

    public RebarHookType? HookByName(string? name) => MatchName(_hookTypes, name);

    public static T? MatchName<T>(IEnumerable<T> items, string? name) where T : Element
    {
        if (string.IsNullOrWhiteSpace(name)) return null;
        var list = items.ToList();
        return list.FirstOrDefault(i => string.Equals(i.Name, name, StringComparison.OrdinalIgnoreCase))
               ?? list.FirstOrDefault(i => i.Name.Contains(name, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Crea el grupo de barras. Devuelve null si no fue posible. Con <paramref name="flipHook"/>
    /// se invierte la orientación del gancho (para reintentar si quedó fuera del hormigón).
    /// </summary>
    public Rebar? Create(BarGroup group, Element host, bool flipHook = false)
    {
        var barType = BarTypeByName(group.BarTypeName) ?? ClosestBarType(group.Diameter);
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
            var hook = HookByName(HookTypeName) ?? HookType((int)group.EndHook.Angle);
            if (hook == null)
            {
                Notes.Add($"{group.Label}: el proyecto no tiene un tipo de gancho de {(int)group.EndHook.Angle}°; la barra se crea sin gancho. Revisar manualmente.");
            }
            else
            {
                terminations.HookTypeIdAtEnd = hook.Id;
                var orient = HookOrientation(group, normal);
                if (flipHook) orient = orient == RebarTerminationOrientation.Left ? RebarTerminationOrientation.Right : RebarTerminationOrientation.Left;
                terminations.TerminationOrientationAtEnd = orient;
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

    /// <summary>
    /// Crea la barra corregida que sustituye a <paramref name="existing"/>: mismo tipo de barra, mismo
    /// anfitrión y misma distribución (número de barras y longitud del conjunto), con el eje de
    /// <paramref name="fix"/> y el gancho en la columna. NO borra la barra original: lo hace quien
    /// llama, una vez comprobado que el gancho queda dentro de la columna.
    /// Devuelve null si no se pudo crear (y lo anota en <see cref="Notes"/>).
    /// </summary>
    public Rebar? CreateFixed(BarFix fix, Rebar existing, Element host, bool flipHook = false)
    {
        var barType = _doc.GetElement(existing.GetTypeId()) as RebarBarType ?? ClosestBarType(fix.Bar.Diameter);
        if (barType == null)
        {
            Notes.Add($"{fix.Label}: no se encontró el tipo de barra.");
            return null;
        }

        var curves = new List<Curve>();
        for (var i = 0; i + 1 < fix.Centerline.Count; i++)
        {
            var a = Units.ToXyz(fix.Centerline[i]);
            var b = Units.ToXyz(fix.Centerline[i + 1]);
            if (a.DistanceTo(b) < 1e-4) continue;
            curves.Add(Line.CreateBound(a, b));
        }
        if (curves.Count == 0)
        {
            Notes.Add($"{fix.Label}: geometría vacía.");
            return null;
        }

        // Normal del plano: la lateral de la viga, con el mismo sentido que la del conjunto original
        // para que la distribución (lado de la normal) coincida.
        var normal = Units.ToXyzDirection(fix.PlaneNormal).Normalize();
        var positions = Math.Max(1, existing.NumberOfBarPositions);
        var copyLayout = positions <= 1;
        double arrayLength = 0;
        var onNormalSide = true;
        if (positions > 1 && existing.IsRebarShapeDriven())
        {
            var acc = existing.GetShapeDrivenAccessor();
            var en = acc.Normal;
            if (Math.Abs(en.DotProduct(normal)) > 0.95)
            {
                if (en.DotProduct(normal) < 0) normal = -normal;
                arrayLength = acc.ArrayLength;
                onNormalSide = acc.BarsOnNormalSide;
                copyLayout = true;
            }
        }
        if (!copyLayout)
        {
            Notes.Add($"{fix.Label}: el conjunto tiene {positions} barras repartidas en una dirección que no es la lateral de la viga; no se corrige automáticamente.");
            return null;
        }

        var terminations = new BarTerminationsData(_doc);
        if (fix.EndHook != null)
        {
            var hook = HookByName(HookTypeName) ?? HookType((int)fix.EndHook.Angle);
            if (hook == null)
            {
                Notes.Add($"{fix.Label}: el proyecto no tiene un tipo de gancho de {(int)fix.EndHook.Angle}°; la barra se crea sin gancho. Revisar manualmente.");
            }
            else
            {
                terminations.HookTypeIdAtEnd = hook.Id;
                var n = fix.Centerline.Count;
                var dir = Units.ToXyzDirection((fix.Centerline[n - 1] - fix.Centerline[n - 2]).Normalized());
                var right = dir.CrossProduct(normal);
                var hookDir = Units.ToXyzDirection(fix.EndHook.Direction);
                var orient = hookDir.DotProduct(right) > 0 ? RebarTerminationOrientation.Right : RebarTerminationOrientation.Left;
                if (flipHook) orient = orient == RebarTerminationOrientation.Left ? RebarTerminationOrientation.Right : RebarTerminationOrientation.Left;
                terminations.TerminationOrientationAtEnd = orient;
                var ext = Units.ToMm(hook.GetHookExtensionLength(barType));
                if (Math.Abs(ext - fix.EndHook.Extension) > 1)
                    Notes.Add($"{fix.Label}: el gancho '{hook.Name}' tiene extensión {ext:0} mm; la norma pide {fix.EndHook.Extension:0} mm.");
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
            Notes.Add($"{fix.Label}: Revit no pudo crear la barra corregida ({ex.Message}).");
            return null;
        }
        if (rebar == null)
        {
            Notes.Add($"{fix.Label}: Revit no pudo crear la barra corregida (sin forma compatible; cargar familias de formas de armadura).");
            return null;
        }

        if (positions > 1 && arrayLength > 1e-9)
            rebar.GetShapeDrivenAccessor().SetLayoutAsFixedNumber(positions, arrayLength, onNormalSide, includeFirstBar: true, includeLastBar: true);

        // La visibilidad "sin obstrucción" es por barra y por vista y NO se hereda: una barra nueva
        // queda oculta dentro del hormigón en las vistas 3D sombreadas, así que al sustituir la
        // original parecía que la barra de la viga se había borrado. Se copia vista por vista.
        CopyViewVisibility(existing, rebar);

        var comments = rebar.get_Parameter(BuiltInParameter.ALL_MODEL_INSTANCE_COMMENTS);
        if (comments != null && !comments.IsReadOnly)
            comments.Set($"UnionesAcero: corregida (sustituye a {existing.Id.Value}). {fix.Message}");

        return rebar;
    }

    /// <summary>Copia a <paramref name="to"/> las vistas en las que <paramref name="from"/> se muestra sin obstrucción.</summary>
    public void CopyViewVisibility(Rebar from, Rebar to)
    {
        foreach (var view in Views())
        {
            try
            {
                if (from.IsUnobscuredInView(view)) to.SetUnobscuredInView(view, true);
            }
            catch (Autodesk.Revit.Exceptions.ApplicationException)
            {
                // vista sin datos de visibilidad para esta barra: se ignora
            }
        }
    }

    /// <summary>Muestra la barra sin obstrucción en la vista dada (normalmente la activa), para que se vea al terminar.</summary>
    public void ShowUnobscured(Rebar rebar, View? view)
    {
        if (view == null || view.IsTemplate) return;
        try { rebar.SetUnobscuredInView(view, true); }
        catch (Autodesk.Revit.Exceptions.ApplicationException) { }
    }

    private List<View>? _views;

    private List<View> Views() => _views ??= new FilteredElementCollector(_doc).OfClass(typeof(View)).Cast<View>()
        .Where(v => !v.IsTemplate).ToList();

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
