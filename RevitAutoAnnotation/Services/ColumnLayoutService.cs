using Autodesk.Revit.DB;
using RevitAutoAnnotation.Core;

namespace RevitAutoAnnotation.Services;

/// <summary>
/// 柱位放樣圖：
/// 1. 柱標籤 —— 依類別放置結構柱／建築柱標籤。
/// 2. 定位尺寸 —— X、Y 兩方向各找最近的垂直格線，
///    以格線參考 + 柱中心參考面（CenterLeftRight / CenterFrontBack）建立尺寸。
/// 柱心已落在格線上（偏距 ≈ 0）時不建立該方向尺寸。
/// </summary>
public static class ColumnLayoutService
{
    public static AnnotationResult Annotate(Document doc, View view, AnnotationSettings settings)
    {
        var result = new AnnotationResult();

        var filter = new ElementMulticategoryFilter(new List<BuiltInCategory>
        {
            BuiltInCategory.OST_StructuralColumns,
            BuiltInCategory.OST_Columns,
        });

        var columns = new FilteredElementCollector(doc, view.Id)
            .WherePasses(filter)
            .OfClass(typeof(FamilyInstance))
            .Cast<FamilyInstance>()
            .ToList();

        var grids = new FilteredElementCollector(doc, view.Id)
            .OfClass(typeof(Grid))
            .Cast<Grid>()
            .ToList();

        using var t = new Transaction(doc, "自動標註 - 柱位放樣");
        t.Start();

        foreach (FamilyInstance column in columns)
        {
            if (column.Location is not LocationPoint lp)
            {
                result.Skipped++;
                continue;
            }

            bool annotated = false;

            if (settings.TagElements)
            {
                try
                {
                    XYZ headPosition = lp.Point + (view.UpDirection + view.RightDirection) * settings.DimensionOffset;
                    if (AnnotationHelper.CreateTag(doc, view, column, headPosition) != null)
                    {
                        result.Tags++;
                        annotated = true;
                    }
                }
                catch { /* 未載入柱標籤族群，略過 */ }
            }

            if (settings.CreateDimensions && grids.Count > 0)
            {
                foreach (XYZ axis in new[] { XYZ.BasisX, XYZ.BasisY })
                {
                    try
                    {
                        if (DimensionToNearestGrid(doc, view, column, lp.Point, grids, axis, settings))
                        {
                            result.Dimensions++;
                            annotated = true;
                        }
                    }
                    catch { /* 缺中心參考面或尺寸建立失敗，略過該方向 */ }
                }
            }

            if (!annotated) result.Skipped++;
        }

        t.Commit();
        return result;
    }

    private static bool DimensionToNearestGrid(
        Document doc, View view, FamilyInstance column, XYZ point,
        List<Grid> grids, XYZ axis, AnnotationSettings settings)
    {
        // 柱心與格線距離小於 10mm 視為對齊格線，不標
        (Grid grid, double _, double signed) = AnnotationHelper.FindNearestGridAlongAxis(
            grids, point, axis, minDistance: 10 / 304.8);
        if (grid == null) return false;

        Reference centerRef = GetCenterReference(column, axis);
        if (centerRef == null) return false;

        var refs = new ReferenceArray();
        refs.Append(AnnotationHelper.GetGridReference(grid, view));
        refs.Append(centerRef);

        // 尺寸線沿量測方向，放在柱旁（沿格線方向偏移）
        XYZ perp = axis.CrossProduct(XYZ.BasisZ).Normalize();
        XYZ basePoint = point + perp * settings.DimensionOffset;
        Line dimLine = Line.CreateBound(basePoint - axis * signed, basePoint);

        return doc.Create.NewDimension(view, dimLine, refs) != null;
    }

    /// <summary>依量測方向挑選柱的中心參考面（考慮柱的旋轉）。</summary>
    private static Reference GetCenterReference(FamilyInstance column, XYZ axis)
    {
        // CenterLeftRight 參考面的法線為 HandOrientation，CenterFrontBack 為 FacingOrientation
        var candidates = new (FamilyInstanceReferenceType type, XYZ normal)[]
        {
            (FamilyInstanceReferenceType.CenterLeftRight, column.HandOrientation),
            (FamilyInstanceReferenceType.CenterFrontBack, column.FacingOrientation),
        };

        foreach ((FamilyInstanceReferenceType type, XYZ normal) in candidates)
        {
            if (Math.Abs(normal.DotProduct(axis)) < 0.7) continue;
            IList<Reference> refs = column.GetReferences(type);
            if (refs.Count > 0) return refs[0];
        }

        return null;
    }
}
