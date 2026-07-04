using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using RevitAutoAnnotation.Core;
using RevitAutoAnnotation.UI;

namespace RevitAutoAnnotation.Commands;

/// <summary>
/// 開啟非模態的自動標註面板。
/// 使用 ExternalEvent 讓面板在開啟狀態下仍可操作 Revit。
/// </summary>
[Transaction(TransactionMode.Manual)]
public class ShowAnnotationWindowCommand : IExternalCommand
{
    private static AnnotationWindow _window;

    public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
    {
        if (_window is { IsLoaded: true })
        {
            _window.Activate();
            return Result.Succeeded;
        }

        var handler = new AnnotationRequestHandler();
        ExternalEvent externalEvent = ExternalEvent.Create(handler);

        _window = new AnnotationWindow(externalEvent, handler);
        _window.Closed += (_, _) => _window = null;
        _window.Show();

        return Result.Succeeded;
    }
}
