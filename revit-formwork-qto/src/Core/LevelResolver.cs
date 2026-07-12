using Autodesk.Revit.DB;

namespace FormworkQTO.Core;

/// <summary>
/// 以元件「底部高程」歸屬樓層：取 BoundingBox 最低點，找不高於它的最高樓層。
/// 這對應模板計價的慣例——梁版的模板支撐架設在其下方樓層的工作面上。
/// </summary>
public class LevelResolver
{
    // 吸收數值誤差用的容差（呎），避免底部剛好落在樓層標高上時被歸到下一層
    private const double Tolerance = 0.1;

    private readonly List<Level> _levels;

    public LevelResolver(Document doc)
    {
        _levels = new FilteredElementCollector(doc)
            .OfClass(typeof(Level))
            .Cast<Level>()
            .OrderBy(l => l.Elevation)
            .ToList();
    }

    /// <summary>回傳元件所屬樓層；模型無樓層或元件無 BoundingBox 時回傳 null。</summary>
    public Level Resolve(Element e)
    {
        BoundingBoxXYZ bb = e.get_BoundingBox(null);
        if (bb == null || _levels.Count == 0) return null;

        double baseZ = bb.Min.Z;
        Level best = _levels[0];
        foreach (Level l in _levels)
        {
            if (l.Elevation <= baseZ + Tolerance) best = l;
            else break;
        }
        return best;
    }
}
