using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Autodesk.Revit.UI;
using RevitAutoAnnotation.Core;
// Autodesk.Revit.UI 也有 TextBox / RibbonPanel 等同名類別，明確指定 WPF 版本
using TextBox = System.Windows.Controls.TextBox;

namespace RevitAutoAnnotation.UI;

/// <summary>
/// 非模態標註面板：收集使用者設定後透過 ExternalEvent 請 Revit 執行標註。
/// UI 以純 C# 建構（不使用 XAML），可在任何平台交叉建置。
/// </summary>
public class AnnotationWindow : Window
{
    private readonly ExternalEvent _externalEvent;
    private readonly AnnotationRequestHandler _handler;

    private readonly RadioButton _wallMode;
    private readonly RadioButton _openingMode;
    private readonly RadioButton _columnMode;
    private readonly RadioButton _beamMode;
    private readonly CheckBox _createDimsCheck;
    private readonly CheckBox _createTagsCheck;
    private readonly CheckBox _wallThicknessCheck;
    private readonly TextBox _offsetBox;
    private readonly Button _runButton;
    private readonly TextBlock _statusText;

    public AnnotationWindow(ExternalEvent externalEvent, AnnotationRequestHandler handler)
    {
        _externalEvent = externalEvent;
        _handler = handler;

        Title = "自動標註";
        Width = 360;
        SizeToContent = System.Windows.SizeToContent.Height;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        ResizeMode = ResizeMode.NoResize;
        Topmost = true;

        var panel = new StackPanel { Margin = new Thickness(14) };

        panel.Children.Add(new TextBlock
        {
            Text = "標註模式",
            FontWeight = FontWeights.Bold,
            Margin = new Thickness(0, 0, 0, 6),
        });

        _wallMode = AddRadio(panel, "牆體尺寸標註（牆長＋牆厚）", isChecked: true);
        _openingMode = AddRadio(panel, "門窗標註（標籤＋開口寬度）");
        _columnMode = AddRadio(panel, "柱位放樣圖（柱標籤＋柱心至格線距離）");
        _beamMode = AddRadio(panel, "梁定位圖（梁標籤＋梁心至格線距離）");

        panel.Children.Add(new Separator { Margin = new Thickness(0, 10, 0, 10) });

        panel.Children.Add(new TextBlock
        {
            Text = "選項",
            FontWeight = FontWeights.Bold,
            Margin = new Thickness(0, 0, 0, 6),
        });

        _createDimsCheck = AddCheck(panel, "建立尺寸標註");
        _createTagsCheck = AddCheck(panel, "建立標籤");
        _wallThicknessCheck = AddCheck(panel, "同時標註牆厚（僅牆體模式）");

        var offsetRow = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Margin = new Thickness(0, 8, 0, 0),
        };
        offsetRow.Children.Add(new TextBlock
        {
            Text = "標註偏移距離 (mm)：",
            VerticalAlignment = VerticalAlignment.Center,
        });
        _offsetBox = new TextBox
        {
            Text = "500",
            Width = 70,
            VerticalAlignment = VerticalAlignment.Center,
        };
        offsetRow.Children.Add(_offsetBox);
        panel.Children.Add(offsetRow);

        _runButton = new Button
        {
            Content = "執行標註",
            Height = 34,
            Margin = new Thickness(0, 14, 0, 0),
            FontWeight = FontWeights.Bold,
        };
        _runButton.Click += RunButton_Click;
        panel.Children.Add(_runButton);

        _statusText = new TextBlock
        {
            Margin = new Thickness(0, 10, 0, 0),
            TextWrapping = TextWrapping.Wrap,
            Foreground = new SolidColorBrush(Color.FromRgb(0x44, 0x44, 0x44)),
            Text = "提示：請先切換到要出圖的平面視圖，再按「執行標註」。",
        };
        panel.Children.Add(_statusText);

        Content = panel;

        // ExternalEvent 在 Revit 執行緒完成後，把結果回寫到 UI 執行緒
        _handler.ReportResult = message => Dispatcher.Invoke(() =>
        {
            _statusText.Text = message;
            _runButton.IsEnabled = true;
        });
    }

    private static RadioButton AddRadio(StackPanel panel, string content, bool isChecked = false)
    {
        var radio = new RadioButton
        {
            Content = content,
            IsChecked = isChecked,
            Margin = new Thickness(0, 2, 0, 2),
            GroupName = "AnnotationMode",
        };
        panel.Children.Add(radio);
        return radio;
    }

    private static CheckBox AddCheck(StackPanel panel, string content)
    {
        var check = new CheckBox
        {
            Content = content,
            IsChecked = true,
            Margin = new Thickness(0, 2, 0, 2),
        };
        panel.Children.Add(check);
        return check;
    }

    private void RunButton_Click(object sender, RoutedEventArgs e)
    {
        if (!double.TryParse(_offsetBox.Text, out double offsetMm) || offsetMm <= 0)
        {
            _statusText.Text = "標註偏移距離請輸入正數（mm）。";
            return;
        }

        _handler.Mode = GetSelectedMode();
        _handler.Settings = new AnnotationSettings
        {
            CreateDimensions = _createDimsCheck.IsChecked == true,
            TagElements = _createTagsCheck.IsChecked == true,
            IncludeWallThickness = _wallThicknessCheck.IsChecked == true,
            DimensionOffsetMm = offsetMm,
        };

        _runButton.IsEnabled = false;
        _statusText.Text = "執行中…";
        _externalEvent.Raise();
    }

    private AnnotationMode GetSelectedMode()
    {
        if (_openingMode.IsChecked == true) return AnnotationMode.Openings;
        if (_columnMode.IsChecked == true) return AnnotationMode.ColumnLayout;
        if (_beamMode.IsChecked == true) return AnnotationMode.BeamLocation;
        return AnnotationMode.Walls;
    }
}
