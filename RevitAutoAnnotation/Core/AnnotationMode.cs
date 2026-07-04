namespace RevitAutoAnnotation.Core;

/// <summary>標註模式，每種模式對應一種施工圖。</summary>
public enum AnnotationMode
{
    /// <summary>牆體尺寸標註（牆長、牆厚）。</summary>
    Walls,

    /// <summary>門窗標註（標籤 + 開口寬度尺寸）。</summary>
    Openings,

    /// <summary>柱位放樣圖（柱標籤 + 柱心至最近格線距離）。</summary>
    ColumnLayout,

    /// <summary>梁定位圖（梁標籤 + 梁心至最近平行格線距離）。</summary>
    BeamLocation,
}
