using Autodesk.Revit.DB;

namespace FormworkQTO.Core;

/// <summary>
/// 模板面積計算核心。演算法分三步：
/// 1. 取出元件 Solid，依面的法向量分類（垂直→側模、朝下→底模、朝上→不計）。
/// 2. 對每個模板候選面沿法向外擠一層薄殼，與鄰接混凝土元件的 Solid 取布林交集，
///    交集體積 ÷ 殼厚 ＝ 混凝土對混凝土的接觸面積，予以扣除（該處不需模板）。
/// 3. 面積以內部單位（平方呎）累計，輸出時再轉公制。
/// </summary>
public class FormworkCalculator
{
    // 薄殼厚度（呎），約 1.5 cm。太薄布林運算容易失敗，太厚會誤判鄰近但未接觸的元件。
    private const double ShellThickness = 0.05;

    // 鄰居搜尋時 BoundingBox 的外擴量（呎）
    private const double BBoxExpand = 0.3;

    // 法向量 Z 分量的分類容差：|nz| 小於此值視為垂直面
    private const double VerticalTolerance = 0.02;

    internal static readonly List<BuiltInCategory> ConcreteCategories = new()
    {
        BuiltInCategory.OST_StructuralColumns,
        BuiltInCategory.OST_StructuralFraming,
        BuiltInCategory.OST_Floors,
        BuiltInCategory.OST_Walls,
        BuiltInCategory.OST_StructuralFoundation,
    };

    private readonly Document _doc;
    private readonly LevelResolver _levels;
    private readonly bool _keepShells;
    private readonly Dictionary<long, IList<Solid>> _solidCache = new();

    /// <param name="keepShells">是否保留每個模板面的薄殼 Solid（視覺化用，較耗記憶體）。</param>
    public FormworkCalculator(Document doc, bool keepShells = false)
    {
        _doc = doc;
        _levels = new LevelResolver(doc);
        _keepShells = keepShells;
    }

    /// <summary>計算全模型所有結構元件。</summary>
    public List<ElementResult> ComputeAll()
    {
        var elements = new FilteredElementCollector(_doc)
            .WherePasses(new ElementMulticategoryFilter(ConcreteCategories))
            .WhereElementIsNotElementType()
            .ToElements();
        return Compute(elements);
    }

    /// <summary>計算指定元件（非結構元件會被略過）。</summary>
    public List<ElementResult> Compute(IEnumerable<Element> elements)
    {
        return elements
            .Where(IsFormworkTarget)
            .Select(ComputeElement)
            .Where(r => r != null && r.Faces.Count > 0)
            .ToList();
    }

    /// <summary>只計算結構用途的柱/梁/版/牆/基礎（過濾掉隔間磚牆、建築裝修版等）。</summary>
    private static bool IsFormworkTarget(Element e)
    {
        if (e.Category == null) return false;
        if (!ConcreteCategories.Contains(e.Category.BuiltInCategory)) return false;

        if (e is Wall wall)
            return wall.get_Parameter(BuiltInParameter.WALL_STRUCTURAL_SIGNIFICANT)?.AsInteger() == 1;
        if (e is Floor floor)
            return floor.get_Parameter(BuiltInParameter.FLOOR_PARAM_IS_STRUCTURAL)?.AsInteger() == 1;
        return true;
    }

    private ElementResult ComputeElement(Element e)
    {
        IList<Solid> solids = GetSolids(e);
        if (solids.Count == 0) return null;

        IList<Solid> neighborSolids = GetNeighborSolids(e);
        Level level = _levels.Resolve(e);
        bool isFoundation = e.Category.BuiltInCategory == BuiltInCategory.OST_StructuralFoundation;

        var result = new ElementResult
        {
            Element = e,
            CategoryName = e.Category.Name,
            TypeName = GetTypeName(e),
            LevelName = level?.Name ?? "(無樓層)",
            LevelElevation = level?.Elevation ?? double.MinValue,
        };

        foreach (Solid solid in solids)
        {
            foreach (Face face in solid.Faces)
            {
                FormworkFaceType? type = Classify(face, isFoundation);
                if (type == null) continue;

                double gross = face.Area;
                Solid shell = TryCreateShell(face);
                double deducted = shell == null ? 0 : ComputeContactArea(shell, neighborSolids);
                deducted = Math.Min(deducted, gross);

                result.Faces.Add(new FaceResult
                {
                    Type = type.Value,
                    GrossAreaFt2 = gross,
                    DeductedFt2 = deducted,
                    Shell = _keepShells ? shell : null,
                });
            }
        }
        return result;
    }

    /// <summary>
    /// 依外法向量的 Z 分量分類：
    /// 朝上（含朝上斜面）不計模板；接近垂直為側模；朝下為底模（含斜版底面）。
    /// 基礎的底面貼在墊層上，不計底模。
    /// </summary>
    private static FormworkFaceType? Classify(Face face, bool isFoundation)
    {
        XYZ normal = GetOutwardNormal(face);
        if (normal == null) return null;

        double nz = normal.Z;
        if (nz >= VerticalTolerance) return null;
        if (nz > -VerticalTolerance) return FormworkFaceType.Side;
        return isFoundation ? null : FormworkFaceType.Bottom;
    }

    private static XYZ GetOutwardNormal(Face face)
    {
        try
        {
            if (face is PlanarFace pf) return pf.FaceNormal;
            // 曲面（圓柱等）取參數範圍中點的法向量作為代表
            BoundingBoxUV bb = face.GetBoundingBox();
            var mid = new UV((bb.Min.U + bb.Max.U) / 2, (bb.Min.V + bb.Max.V) / 2);
            return face.ComputeNormal(mid);
        }
        catch
        {
            return null;
        }
    }

    /// <summary>把平面沿外法向擠出成薄殼。曲面或擠出失敗（退化輪廓等）回傳 null。</summary>
    private static Solid TryCreateShell(Face face)
    {
        if (face is not PlanarFace pf) return null;
        try
        {
            IList<CurveLoop> loops = pf.GetEdgesAsCurveLoops();
            return GeometryCreationUtilities.CreateExtrusionGeometry(loops, pf.FaceNormal, ShellThickness);
        }
        catch
        {
            return null;
        }
    }

    /// <summary>薄殼與所有鄰居 Solid 的交集體積 ÷ 殼厚 ＝ 接觸面積。</summary>
    private static double ComputeContactArea(Solid shell, IList<Solid> neighborSolids)
    {
        double volume = 0;
        foreach (Solid neighbor in neighborSolids)
        {
            try
            {
                Solid intersection = BooleanOperationsUtils.ExecuteBooleanOperation(
                    shell, neighbor, BooleanOperationsType.Intersect);
                if (intersection != null) volume += intersection.Volume;
            }
            catch
            {
                // 共面相切等情況布林運算可能失敗，該鄰居略過不扣
            }
        }
        return volume / ShellThickness;
    }

    private IList<Solid> GetNeighborSolids(Element e)
    {
        BoundingBoxXYZ bb = e.get_BoundingBox(null);
        if (bb == null) return Array.Empty<Solid>();

        var expand = new XYZ(BBoxExpand, BBoxExpand, BBoxExpand);
        var outline = new Outline(bb.Min - expand, bb.Max + expand);

        var neighbors = new FilteredElementCollector(_doc)
            .WherePasses(new ElementMulticategoryFilter(ConcreteCategories))
            .WhereElementIsNotElementType()
            .WherePasses(new BoundingBoxIntersectsFilter(outline))
            .Where(n => n.Id.Value != e.Id.Value && IsFormworkTarget(n));

        var solids = new List<Solid>();
        foreach (Element n in neighbors) solids.AddRange(GetSolids(n));
        return solids;
    }

    private IList<Solid> GetSolids(Element e)
    {
        if (_solidCache.TryGetValue(e.Id.Value, out IList<Solid> cached)) return cached;

        var list = new List<Solid>();
        GeometryElement geo = e.get_Geometry(new Options { DetailLevel = ViewDetailLevel.Fine });
        if (geo != null) CollectSolids(geo, list);
        _solidCache[e.Id.Value] = list;
        return list;
    }

    private static void CollectSolids(GeometryElement geo, List<Solid> list)
    {
        foreach (GeometryObject obj in geo)
        {
            if (obj is Solid solid && solid.Volume > 1e-6)
                list.Add(solid);
            else if (obj is GeometryInstance instance)
                CollectSolids(instance.GetInstanceGeometry(), list);
        }
    }

    private string GetTypeName(Element e)
    {
        return _doc.GetElement(e.GetTypeId()) is ElementType t
            ? $"{t.FamilyName}: {t.Name}"
            : "";
    }
}
