using System.Reflection;
using Autodesk.Revit.UI;

namespace RevitAutoAnnotation;

/// <summary>
/// 外掛入口：建立 Ribbon 頁籤與「自動標註」按鈕。
/// </summary>
public class App : IExternalApplication
{
    public Result OnStartup(UIControlledApplication application)
    {
        const string tabName = "自動標註";
        try
        {
            application.CreateRibbonTab(tabName);
        }
        catch (Autodesk.Revit.Exceptions.ArgumentException)
        {
            // 頁籤已存在（例如多個外掛共用同名頁籤），直接沿用
        }

        RibbonPanel panel = application.CreateRibbonPanel(tabName, "施工圖標註");

        var buttonData = new PushButtonData(
            "AutoAnnotationButton",
            "自動標註",
            Assembly.GetExecutingAssembly().Location,
            "RevitAutoAnnotation.Commands.ShowAnnotationWindowCommand")
        {
            ToolTip = "開啟自動標註面板：依模式自動標註牆體尺寸、門窗、柱位放樣、梁定位。"
        };

        panel.AddItem(buttonData);
        return Result.Succeeded;
    }

    public Result OnShutdown(UIControlledApplication application) => Result.Succeeded;
}
