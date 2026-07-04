using Autodesk.Revit.DB;

namespace RevitAutoAnnotation.Services;

/// <summary>各標註服務共用的幾何／參考取得工具。</summary>
internal static class AnnotationHelper
{
    internal const double Tolerance = 1e-6;

    /// <summary>取得元素在指定視圖中所有帶參考的平面（ComputeReferences = true）。</summary>
    internal static IEnumerable<PlanarFace> GetPlanarFaces(Element element, View view)
    {
        var options = new Options { ComputeReferences = true, View = view };
        GeometryElement geometry = element.get_Geometry(options);
        if (geometry == null) yield break;

        foreach (GeometryObject obj in geometry)
        {
            if (obj is not Solid solid || solid.Faces.IsEmpty) continue;
            foreach (Face face in solid.Faces)
            {
                if (face is PlanarFace pf && pf.Reference != null)
                    yield return pf;
            }
        }
    }

    /// <summary>
    /// 取得格線可供尺寸標註使用的參考。
    /// 先從視圖幾何取線參考，取不到時退回元素參考。
    /// </summary>
    internal static Reference GetGridReference(Grid grid, View view)
    {
        var options = new Options { ComputeReferences = true, View = view };
        GeometryElement geometry = grid.get_Geometry(options);
        if (geometry != null)
        {
            foreach (GeometryObject obj in geometry)
            {
                if (obj is Line line && line.Reference != null)
                    return line.Reference;
            }
        }
        return new Reference(grid);
    }

    /// <summary>
    /// 在直線格線中，尋找與 <paramref name="axis"/> 垂直（即沿 axis 方向量距）且距點 p 最近的格線。
    /// 距離小於 minDistance（構件已落在格線上）者不回傳。
    /// </summary>
    internal static (Grid grid, double distance, double signedDistance) FindNearestGridAlongAxis(
        IEnumerable<Grid> grids, XYZ p, XYZ axis, double minDistance)
    {
        Grid best = null;
        double bestAbs = double.MaxValue;
        double bestSigned = 0;

        foreach (Grid grid in grids)
        {
            if (grid.Curve is not Line line) continue;
            // 只取與量測方向垂直的格線（格線方向與 axis 內積 ≈ 0）
            if (Math.Abs(line.Direction.DotProduct(axis)) > 0.05) continue;

            double signed = (p - line.Origin).DotProduct(axis);
            double abs = Math.Abs(signed);
            if (abs < bestAbs)
            {
                bestAbs = abs;
                bestSigned = signed;
                best = grid;
            }
        }

        if (best == null || bestAbs < minDistance)
            return (null, 0, 0);

        return (best, bestAbs, bestSigned);
    }

    /// <summary>建立依類別的標籤（需已載入對應類別的標籤族群，否則丟出例外由呼叫端略過）。</summary>
    internal static IndependentTag CreateTag(Document doc, View view, Element element, XYZ headPosition)
    {
        return IndependentTag.Create(
            doc, view.Id, new Reference(element),
            false, TagMode.TM_ADDBY_CATEGORY, TagOrientation.Horizontal, headPosition);
    }
}
