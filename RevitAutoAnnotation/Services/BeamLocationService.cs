using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Structure;
using RevitAutoAnnotation.Core;

namespace RevitAutoAnnotation.Services;

/// <summary>
/// 梁定位圖：
/// 1. 梁標籤 —— 依類別在梁中點放置結構構架標籤（顯示梁編號／尺寸由標籤族群決定）。
/// 2. 定位尺寸 —— 找與梁平行且最近的格線，
///    以格線參考 + 梁中心參考面建立垂直梁軸向的定位尺寸。
/// 僅處理直線梁；梁心與格線重合（偏距 ≈ 0）時不建立尺寸。
/// </summary>
public static class BeamLocationService
{
    public static AnnotationResult Annotate(Document doc, View view, AnnotationSettings settings)
    {
        var result = new AnnotationResult();

        var beams = new FilteredElementCollector(doc, view.Id)
            .OfCategory(BuiltInCategory.OST_StructuralFraming)
            .OfClass(typeof(FamilyInstance))
            .Cast<FamilyInstance>()
            .Where(b => b.StructuralType == StructuralType.Beam)
            .ToList();

        var grids = new FilteredElementCollector(doc, view.Id)
            .OfClass(typeof(Grid))
            .Cast<Grid>()
            .ToList();

        using var t = new Transaction(doc, "自動標註 - 梁定位");
        t.Start();

        foreach (FamilyInstance beam in beams)
        {
            if (beam.Location is not LocationCurve { Curve: Line line })
            {
                result.Skipped++;
                continue;
            }

            XYZ mid = line.Evaluate(0.5, true);
            bool annotated = false;

            if (settings.TagElements)
            {
                try
                {
                    if (AnnotationHelper.CreateTag(doc, view, beam, mid) != null)
                    {
                        result.Tags++;
                        annotated = true;
                    }
                }
                catch { /* 未載入結構構架標籤族群，略過 */ }
            }

            if (settings.CreateDimensions && grids.Count > 0)
            {
                try
                {
                    if (DimensionToNearestParallelGrid(doc, view, beam, line, mid, grids, settings))
                    {
                        result.Dimensions++;
                        annotated = true;
                    }
                }
                catch { /* 缺中心參考面或尺寸建立失敗，略過 */ }
            }

            if (!annotated) result.Skipped++;
        }

        t.Commit();
        return result;
    }

    private static bool DimensionToNearestParallelGrid(
        Document doc, View view, FamilyInstance beam, Line beamLine, XYZ mid,
        List<Grid> grids, AnnotationSettings settings)
    {
        // 量測方向：水平面上垂直梁軸向
        XYZ axis = XYZ.BasisZ.CrossProduct(beamLine.Direction).Normalize();

        // 梁心與格線距離小於 10mm 視為梁沿格線配置，不標
        (Grid grid, double _, double signed) = AnnotationHelper.FindNearestGridAlongAxis(
            grids, mid, axis, minDistance: 10 / 304.8);
        if (grid == null) return false;

        Reference centerRef = GetBeamCenterReference(beam);
        if (centerRef == null) return false;

        var refs = new ReferenceArray();
        refs.Append(AnnotationHelper.GetGridReference(grid, view));
        refs.Append(centerRef);

        // 尺寸線沿量測方向，放在梁中點沿梁軸向偏移處
        XYZ basePoint = mid + beamLine.Direction * settings.DimensionOffset;
        Line dimLine = Line.CreateBound(basePoint - axis * signed, basePoint);

        return doc.Create.NewDimension(view, dimLine, refs) != null;
    }

    /// <summary>取得梁沿軸向的垂直中心參考面（平面圖上的梁中心線）。</summary>
    private static Reference GetBeamCenterReference(FamilyInstance beam)
    {
        foreach (FamilyInstanceReferenceType type in new[]
        {
            FamilyInstanceReferenceType.CenterLeftRight,
            FamilyInstanceReferenceType.CenterFrontBack,
        })
        {
            IList<Reference> refs = beam.GetReferences(type);
            if (refs.Count > 0) return refs[0];
        }
        return null;
    }
}
