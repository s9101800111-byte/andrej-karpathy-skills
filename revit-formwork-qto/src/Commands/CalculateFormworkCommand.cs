using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using FormworkQTO.Core;
using FormworkQTO.Export;

namespace FormworkQTO.Commands;

/// <summary>
/// 計算全模型結構元件（柱/梁/版/牆/基礎）的模板面積，
/// 寫回「側模面積」「底模面積」共用參數，並匯出 CSV 明細。
/// </summary>
[Transaction(TransactionMode.Manual)]
public class CalculateFormworkCommand : IExternalCommand
{
    public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
    {
        Document doc = commandData.Application.ActiveUIDocument.Document;
        try
        {
            var calculator = new FormworkCalculator(doc);
            List<ElementResult> results = calculator.ComputeAll();
            if (results.Count == 0)
            {
                TaskDialog.Show("模板計算", "找不到可計算的結構元件。\n（牆與版只計「結構」屬性打勾者）");
                return Result.Cancelled;
            }

            using (var t = new Transaction(doc, "計算模板面積"))
            {
                t.Start();
                SharedParams.EnsureBound(doc);
                doc.Regenerate();
                foreach (ElementResult r in results)
                {
                    r.Element.LookupParameter(SharedParams.SideAreaParam)?.Set(r.SideFt2);
                    r.Element.LookupParameter(SharedParams.BottomAreaParam)?.Set(r.BottomFt2);
                }
                t.Commit();
            }

            string csvPath = CsvExporter.Export(results, doc.Title);

            double side = CsvExporter.ToM2(results.Sum(r => r.SideFt2));
            double bottom = CsvExporter.ToM2(results.Sum(r => r.BottomFt2));
            double deducted = CsvExporter.ToM2(results.Sum(r => r.DeductedFt2));
            TaskDialog.Show("模板計算完成",
                $"元件數：{results.Count}\n" +
                $"側模：{side:N2} m²\n" +
                $"底模：{bottom:N2} m²\n" +
                $"合計：{side + bottom:N2} m²\n" +
                $"（接觸扣除：{deducted:N2} m²）\n\n" +
                $"明細已匯出：\n{csvPath}");
            return Result.Succeeded;
        }
        catch (Exception ex)
        {
            message = ex.ToString();
            return Result.Failed;
        }
    }
}
