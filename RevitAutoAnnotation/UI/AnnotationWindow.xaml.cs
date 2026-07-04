using System.Windows;
using Autodesk.Revit.UI;
using RevitAutoAnnotation.Core;

namespace RevitAutoAnnotation.UI;

/// <summary>
/// 非模態標註面板：收集使用者設定後透過 ExternalEvent 請 Revit 執行標註。
/// </summary>
public partial class AnnotationWindow : Window
{
    private readonly ExternalEvent _externalEvent;
    private readonly AnnotationRequestHandler _handler;

    public AnnotationWindow(ExternalEvent externalEvent, AnnotationRequestHandler handler)
    {
        InitializeComponent();
        _externalEvent = externalEvent;
        _handler = handler;

        // ExternalEvent 在 Revit 執行緒完成後，把結果回寫到 UI 執行緒
        _handler.ReportResult = message => Dispatcher.Invoke(() =>
        {
            StatusText.Text = message;
            RunButton.IsEnabled = true;
        });
    }

    private void RunButton_Click(object sender, RoutedEventArgs e)
    {
        if (!double.TryParse(OffsetBox.Text, out double offsetMm) || offsetMm <= 0)
        {
            StatusText.Text = "標註偏移距離請輸入正數（mm）。";
            return;
        }

        _handler.Mode = GetSelectedMode();
        _handler.Settings = new AnnotationSettings
        {
            CreateDimensions = CreateDimsCheck.IsChecked == true,
            TagElements = CreateTagsCheck.IsChecked == true,
            IncludeWallThickness = WallThicknessCheck.IsChecked == true,
            DimensionOffsetMm = offsetMm,
        };

        RunButton.IsEnabled = false;
        StatusText.Text = "執行中…";
        _externalEvent.Raise();
    }

    private AnnotationMode GetSelectedMode()
    {
        if (OpeningMode.IsChecked == true) return AnnotationMode.Openings;
        if (ColumnMode.IsChecked == true) return AnnotationMode.ColumnLayout;
        if (BeamMode.IsChecked == true) return AnnotationMode.BeamLocation;
        return AnnotationMode.Walls;
    }
}
