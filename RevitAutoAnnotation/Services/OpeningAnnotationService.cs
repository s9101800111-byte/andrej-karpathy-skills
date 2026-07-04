using Autodesk.Revit.DB;
using RevitAutoAnnotation.Core;

namespace RevitAutoAnnotation.Services;

/// <summary>
/// 門窗標註：
/// 1. 標籤 —— 依類別放置門標籤／窗標籤（顯示內容由標籤族群決定，可用含寬×高的標籤族群）。
/// 2. 開口寬度 —— 用族群實例的 Left / Right 參考面建立沿牆的寬度尺寸。
/// 族群若未定義 Left / Right 參考面則只放標籤。
/// </summary>
public static class OpeningAnnotationService
{
    public static AnnotationResult Annotate(Document doc, View view, AnnotationSettings settings)
    {
        var result = new AnnotationResult();

        var filter = new ElementMulticategoryFilter(new List<BuiltInCategory>
        {
            BuiltInCategory.OST_Doors,
            BuiltInCategory.OST_Windows,
        });

        var instances = new FilteredElementCollector(doc, view.Id)
            .WherePasses(filter)
            .OfClass(typeof(FamilyInstance))
            .Cast<FamilyInstance>()
            .ToList();

        using var t = new Transaction(doc, "自動標註 - 門窗");
        t.Start();

        foreach (FamilyInstance instance in instances)
        {
            bool annotated = false;

            if (settings.TagElements)
            {
                try
                {
                    if (TagOpening(doc, view, instance, settings)) { result.Tags++; annotated = true; }
                }
                catch { /* 未載入門窗標籤族群等情形，略過 */ }
            }

            if (settings.CreateDimensions)
            {
                try
                {
                    if (CreateWidthDimension(doc, view, instance, settings)) { result.Dimensions++; annotated = true; }
                }
                catch { /* 缺少參考面等情形，略過 */ }
            }

            if (!annotated) result.Skipped++;
        }

        t.Commit();
        return result;
    }

    private static bool TagOpening(Document doc, View view, FamilyInstance instance, AnnotationSettings settings)
    {
        XYZ point = GetLocationPoint(instance, view);
        if (point == null) return false;

        XYZ headPosition = point + view.UpDirection * settings.DimensionOffset;
        return AnnotationHelper.CreateTag(doc, view, instance, headPosition) != null;
    }

    private static bool CreateWidthDimension(Document doc, View view, FamilyInstance instance, AnnotationSettings settings)
    {
        IList<Reference> left = instance.GetReferences(FamilyInstanceReferenceType.Left);
        IList<Reference> right = instance.GetReferences(FamilyInstanceReferenceType.Right);
        if (left.Count == 0 || right.Count == 0) return false;

        if (instance.Host is not Wall { Location: LocationCurve { Curve: Line hostLine } }) return false;

        XYZ point = GetLocationPoint(instance, view);
        if (point == null) return false;

        var refs = new ReferenceArray();
        refs.Append(left[0]);
        refs.Append(right[0]);

        XYZ dir = hostLine.Direction;
        XYZ offsetDir = XYZ.BasisZ.CrossProduct(dir).Normalize();
        XYZ basePoint = point + offsetDir * settings.DimensionOffset;
        Line dimLine = Line.CreateBound(basePoint - dir, basePoint + dir);

        return doc.Create.NewDimension(view, dimLine, refs) != null;
    }

    private static XYZ GetLocationPoint(FamilyInstance instance, View view)
    {
        if (instance.Location is LocationPoint lp) return lp.Point;

        BoundingBoxXYZ box = instance.get_BoundingBox(view);
        return box == null ? null : (box.Min + box.Max) * 0.5;
    }
}
