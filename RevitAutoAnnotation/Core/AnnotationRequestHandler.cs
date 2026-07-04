using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using RevitAutoAnnotation.Services;

namespace RevitAutoAnnotation.Core;

/// <summary>
/// ExternalEvent 處理器：由非模態 WPF 面板觸發，
/// 在 Revit API 合法的執行緒上依模式執行標註。
/// </summary>
public class AnnotationRequestHandler : IExternalEventHandler
{
    public AnnotationMode Mode { get; set; }
    public AnnotationSettings Settings { get; set; } = new();

    /// <summary>執行完成後回報訊息給 WPF 面板。</summary>
    public Action<string> ReportResult { get; set; }

    public void Execute(UIApplication app)
    {
        try
        {
            UIDocument uiDoc = app.ActiveUIDocument;
            if (uiDoc == null)
            {
                ReportResult?.Invoke("目前沒有開啟的文件。");
                return;
            }

            Document doc = uiDoc.Document;
            View view = doc.ActiveView;

            if (view is not ViewPlan)
            {
                ReportResult?.Invoke("請先切換到平面視圖（樓層平面／結構平面）再執行標註。");
                return;
            }

            AnnotationResult result = Mode switch
            {
                AnnotationMode.Walls => WallDimensionService.Annotate(doc, view, Settings),
                AnnotationMode.Openings => OpeningAnnotationService.Annotate(doc, view, Settings),
                AnnotationMode.ColumnLayout => ColumnLayoutService.Annotate(doc, view, Settings),
                AnnotationMode.BeamLocation => BeamLocationService.Annotate(doc, view, Settings),
                _ => new AnnotationResult(),
            };

            ReportResult?.Invoke(result.ToString());
        }
        catch (Exception ex)
        {
            ReportResult?.Invoke("標註失敗：" + ex.Message);
        }
    }

    public string GetName() => "自動標註";
}
