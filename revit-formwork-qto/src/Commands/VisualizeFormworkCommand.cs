using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using FormworkQTO.Core;

namespace FormworkQTO.Commands;

/// <summary>
/// 把選取元件的模板面以薄殼 DirectShape（通用模型）顯示在模型中，
/// 並在目前視圖覆寫顏色：藍色＝側模、紅色＝底模。用於人工驗證判斷是否正確。
/// 檢視完畢用 Ctrl+Z 復原即可移除。
/// </summary>
[Transaction(TransactionMode.Manual)]
public class VisualizeFormworkCommand : IExternalCommand
{
    public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
    {
        UIDocument uidoc = commandData.Application.ActiveUIDocument;
        Document doc = uidoc.Document;
        try
        {
            ICollection<ElementId> selected = uidoc.Selection.GetElementIds();
            if (selected.Count == 0)
            {
                TaskDialog.Show("模板視覺化", "請先選取要檢視的結構元件，再執行本指令。");
                return Result.Cancelled;
            }

            var calculator = new FormworkCalculator(doc, keepShells: true);
            List<ElementResult> results = calculator.Compute(selected.Select(doc.GetElement));
            if (results.Count == 0)
            {
                TaskDialog.Show("模板視覺化", "選取範圍內沒有可計算的結構元件。");
                return Result.Cancelled;
            }

            FillPatternElement solidFill = new FilteredElementCollector(doc)
                .OfClass(typeof(FillPatternElement))
                .Cast<FillPatternElement>()
                .FirstOrDefault(f => f.GetFillPattern().IsSolidFill);

            View view = doc.ActiveView;
            var sideColor = new Color(0, 120, 255);
            var bottomColor = new Color(255, 60, 60);
            int created = 0;
            int skippedCurved = 0;

            using (var t = new Transaction(doc, "模板視覺化"))
            {
                t.Start();
                foreach (ElementResult r in results)
                {
                    foreach (FaceResult f in r.Faces)
                    {
                        if (f.Shell == null) { skippedCurved++; continue; }

                        var ds = DirectShape.CreateElement(doc, new ElementId(BuiltInCategory.OST_GenericModel));
                        ds.ApplicationId = "FormworkQTO";
                        ds.ApplicationDataId = r.Element.Id.Value.ToString();
                        ds.SetShape(new List<GeometryObject> { f.Shell });

                        Color color = f.Type == FormworkFaceType.Side ? sideColor : bottomColor;
                        var ogs = new OverrideGraphicSettings().SetProjectionLineColor(color);
                        if (solidFill != null)
                        {
                            ogs.SetSurfaceForegroundPatternId(solidFill.Id)
                               .SetSurfaceForegroundPatternColor(color);
                        }
                        view.SetElementOverrides(ds.Id, ogs);
                        created++;
                    }
                }
                t.Commit();
            }

            double side = Export.CsvExporter.ToM2(results.Sum(r => r.SideFt2));
            double bottom = Export.CsvExporter.ToM2(results.Sum(r => r.BottomFt2));
            TaskDialog.Show("模板視覺化",
                $"已建立 {created} 個模板示意面（通用模型 DirectShape）。\n" +
                $"藍色＝側模（{side:N2} m²）、紅色＝底模（{bottom:N2} m²）。\n" +
                (skippedCurved > 0 ? $"曲面 {skippedCurved} 個未顯示（面積仍有計入）。\n" : "") +
                "注意：示意面顯示的是「毛面積」範圍，接觸扣除只反映在數字上。\n" +
                "檢視完畢請按 Ctrl+Z 復原以移除示意面。");
            return Result.Succeeded;
        }
        catch (Exception ex)
        {
            message = ex.ToString();
            return Result.Failed;
        }
    }
}
