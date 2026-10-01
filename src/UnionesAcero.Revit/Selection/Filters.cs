using Autodesk.Revit.DB;
using Autodesk.Revit.UI.Selection;

namespace UnionesAcero.Revit.Selection;

public sealed class ColumnSelectionFilter : ISelectionFilter
{
    public bool AllowElement(Element elem)
        => elem is FamilyInstance fi && fi.Category?.Id.Value == (long)BuiltInCategory.OST_StructuralColumns;

    public bool AllowReference(Reference reference, XYZ position) => false;
}

public sealed class BeamSelectionFilter : ISelectionFilter
{
    public bool AllowElement(Element elem)
        => elem is FamilyInstance fi && fi.Category?.Id.Value == (long)BuiltInCategory.OST_StructuralFraming;

    public bool AllowReference(Reference reference, XYZ position) => false;
}
