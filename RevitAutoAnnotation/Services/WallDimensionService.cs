using Autodesk.Revit.DB;
using RevitAutoAnnotation.Core;

namespace RevitAutoAnnotation.Services;

/// <summary>
/// 牆體尺寸標註：
/// 1. 牆長 —— 取牆兩端面（法線與牆軸向平行）建立沿牆尺寸線。
/// 2. 牆厚 —— 取牆兩側面（法線水平且垂直牆軸向）在牆中點建立垂直牆的尺寸線。
/// 僅處理直線牆；弧牆略過並計入 Skipped。
/// </summary>
public static class WallDimensionService
{
    public static AnnotationResult Annotate(Document doc, View view, AnnotationSettings settings)
    {
        var result = new AnnotationResult();

        var walls = new FilteredElementCollector(doc, view.Id)
            .OfClass(typeof(Wall))
            .Cast<Wall>()
            .ToList();

        using var t = new Transaction(doc, "自動標註 - 牆體尺寸");
        t.Start();

        foreach (Wall wall in walls)
        {
            if (wall.Location is not LocationCurve { Curve: Line line })
            {
                result.Skipped++;
                continue;
            }

            try
            {
                if (settings.CreateDimensions && CreateLengthDimension(doc, view, wall, line, settings))
                    result.Dimensions++;

                if (settings.CreateDimensions && settings.IncludeWallThickness &&
                    CreateThicknessDimension(doc, view, wall, line))
                    result.Dimensions++;
            }
            catch
            {
                result.Skipped++;
            }
        }

        t.Commit();
        return result;
    }

    private static bool CreateLengthDimension(Document doc, View view, Wall wall, Line line, AnnotationSettings settings)
    {
        XYZ dir = line.Direction;
        var refs = new ReferenceArray();

        // 端面：法線與牆軸向平行
        foreach (PlanarFace face in AnnotationHelper.GetPlanarFaces(wall, view))
        {
            if (Math.Abs(Math.Abs(face.FaceNormal.DotProduct(dir)) - 1.0) < AnnotationHelper.Tolerance)
                refs.Append(face.Reference);
        }

        if (refs.Size < 2) return false;

        XYZ offsetDir = XYZ.BasisZ.CrossProduct(dir).Normalize();
        XYZ offset = offsetDir * settings.DimensionOffset;
        Line dimLine = Line.CreateBound(line.GetEndPoint(0) + offset, line.GetEndPoint(1) + offset);

        return doc.Create.NewDimension(view, dimLine, refs) != null;
    }

    private static bool CreateThicknessDimension(Document doc, View view, Wall wall, Line line)
    {
        XYZ dir = line.Direction;
        var refs = new ReferenceArray();

        // 側面：法線水平且與牆軸向垂直
        foreach (PlanarFace face in AnnotationHelper.GetPlanarFaces(wall, view))
        {
            XYZ n = face.FaceNormal;
            if (Math.Abs(n.Z) < AnnotationHelper.Tolerance &&
                Math.Abs(n.DotProduct(dir)) < AnnotationHelper.Tolerance)
                refs.Append(face.Reference);
        }

        if (refs.Size < 2) return false;

        XYZ mid = line.Evaluate(0.5, true);
        XYZ normal = XYZ.BasisZ.CrossProduct(dir).Normalize();
        Line dimLine = Line.CreateBound(mid - normal * wall.Width, mid + normal * wall.Width);

        return doc.Create.NewDimension(view, dimLine, refs) != null;
    }
}
