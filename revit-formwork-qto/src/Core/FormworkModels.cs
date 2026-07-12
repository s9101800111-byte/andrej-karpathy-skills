using Autodesk.Revit.DB;

namespace FormworkQTO.Core;

public enum FormworkFaceType
{
    /// <summary>側模：垂直面（柱側、梁側、牆面、版邊）</summary>
    Side,

    /// <summary>底模：朝下的面（梁底、版底、斜版底面）</summary>
    Bottom,
}

/// <summary>單一模板面的計算結果。面積一律為 Revit 內部單位（平方呎）。</summary>
public class FaceResult
{
    public FormworkFaceType Type { get; init; }

    /// <summary>面的毛面積（已扣除面上的開口洞）。</summary>
    public double GrossAreaFt2 { get; init; }

    /// <summary>與鄰接混凝土元件的接觸面積（不需模板，予以扣除）。</summary>
    public double DeductedFt2 { get; init; }

    public double NetAreaFt2 => Math.Max(0, GrossAreaFt2 - DeductedFt2);

    /// <summary>沿法向擠出的薄殼，供視覺化使用；曲面或擠出失敗時為 null。</summary>
    public Solid Shell { get; init; }
}

/// <summary>單一元件的模板計算結果。</summary>
public class ElementResult
{
    public Element Element { get; init; }
    public string CategoryName { get; init; }
    public string TypeName { get; init; }
    public string LevelName { get; init; }
    public double LevelElevation { get; init; }

    public List<FaceResult> Faces { get; } = new();

    public double SideFt2 => Faces.Where(f => f.Type == FormworkFaceType.Side).Sum(f => f.NetAreaFt2);
    public double BottomFt2 => Faces.Where(f => f.Type == FormworkFaceType.Bottom).Sum(f => f.NetAreaFt2);
    public double DeductedFt2 => Faces.Sum(f => f.DeductedFt2);
    public double TotalFt2 => SideFt2 + BottomFt2;
}
