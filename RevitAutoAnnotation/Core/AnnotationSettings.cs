namespace RevitAutoAnnotation.Core;

/// <summary>使用者在 WPF 面板上的設定。</summary>
public class AnnotationSettings
{
    /// <summary>是否建立尺寸標註。</summary>
    public bool CreateDimensions { get; set; } = true;

    /// <summary>是否建立標籤（門窗標籤、柱標籤、梁標籤）。</summary>
    public bool TagElements { get; set; } = true;

    /// <summary>牆模式下是否同時標註牆厚。</summary>
    public bool IncludeWallThickness { get; set; } = true;

    /// <summary>尺寸線／標籤與構件的偏移距離（公釐）。</summary>
    public double DimensionOffsetMm { get; set; } = 500;

    /// <summary>偏移距離換算為 Revit 內部單位（英呎）。</summary>
    public double DimensionOffset => DimensionOffsetMm / 304.8;
}

/// <summary>單次執行的統計結果。</summary>
public class AnnotationResult
{
    public int Dimensions { get; set; }
    public int Tags { get; set; }
    public int Skipped { get; set; }

    public override string ToString() =>
        $"完成：建立 {Dimensions} 個尺寸標註、{Tags} 個標籤" +
        (Skipped > 0 ? $"（{Skipped} 個構件因缺少參考或標籤族群而略過）" : "。");
}
