using System.Collections.Generic;
using System.Linq;
using System.Windows.Forms;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Architecture;
using Autodesk.Revit.UI;

namespace StairClearanceCheck
{
    /// <summary>
    /// 樓梯淨高檢核指令:
    /// 1. 讓使用者輸入最小淨高要求
    /// 2. 在樓梯踏面/平台頂面佈點,垂直向上射線找出上方障礙物
    /// 3. 淨高不足者列入報告並加入選取集
    /// </summary>
    [Transaction(TransactionMode.Manual)]
    public class CheckStairClearanceCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            UIDocument uidoc = commandData.Application.ActiveUIDocument;
            Document doc = uidoc.Document;

            // 1. 取得使用者輸入
            double minClearanceMm, spacingMm;
            using (var form = new ClearanceInputForm())
            {
                if (form.ShowDialog() != DialogResult.OK)
                    return Result.Cancelled;
                minClearanceMm = form.MinClearanceMm;
                spacingMm = form.SampleSpacingMm;
            }

            // 2. 決定檢核範圍:有預選樓梯就只檢核預選,否則檢核全模型的樓梯
            List<Stairs> stairs = uidoc.Selection.GetElementIds()
                .Select(id => doc.GetElement(id))
                .OfType<Stairs>()
                .ToList();
            if (stairs.Count == 0)
            {
                stairs = new FilteredElementCollector(doc)
                    .OfClass(typeof(Stairs))
                    .Cast<Stairs>()
                    .ToList();
            }
            if (stairs.Count == 0)
            {
                TaskDialog.Show("樓梯淨高檢核", "模型中找不到樓梯。");
                return Result.Succeeded;
            }

            // 3. 準備 3D 視圖(ReferenceIntersector 必須在 3D 視圖中運作);
            //    優先用沒有啟用剖面框的視圖,避免剖面框外的物件被忽略
            View3D view3D = new FilteredElementCollector(doc)
                .OfClass(typeof(View3D))
                .Cast<View3D>()
                .Where(v => !v.IsTemplate)
                .OrderBy(v => v.IsSectionBoxActive ? 1 : 0)
                .FirstOrDefault();

            ElementId tempViewId = ElementId.InvalidElementId;
            if (view3D == null)
            {
                using (var t = new Transaction(doc, "建立淨高檢核暫存 3D 視圖"))
                {
                    t.Start();
                    ViewFamilyType viewType = new FilteredElementCollector(doc)
                        .OfClass(typeof(ViewFamilyType))
                        .Cast<ViewFamilyType>()
                        .First(x => x.ViewFamily == ViewFamily.ThreeDimensional);
                    view3D = View3D.CreateIsometric(doc, viewType.Id);
                    t.Commit();
                }
                tempViewId = view3D.Id;
            }

            // 4. 執行檢核
            double minClearance = UnitUtils.ConvertToInternalUnits(minClearanceMm, UnitTypeId.Millimeters);
            double spacing = UnitUtils.ConvertToInternalUnits(spacingMm, UnitTypeId.Millimeters);
            var checker = new StairClearanceChecker(doc, view3D, minClearance, spacing);

            var violations = new List<ClearanceViolation>();
            foreach (Stairs stair in stairs)
                violations.AddRange(checker.Check(stair));

            // 5. 清掉暫存視圖
            if (tempViewId != ElementId.InvalidElementId)
            {
                using (var t = new Transaction(doc, "刪除淨高檢核暫存 3D 視圖"))
                {
                    t.Start();
                    doc.Delete(tempViewId);
                    t.Commit();
                }
            }

            // 6. 顯示結果
            ShowResults(uidoc, stairs.Count, minClearanceMm, violations);
            return Result.Succeeded;
        }

        private static void ShowResults(
            UIDocument uidoc, int stairCount, double minClearanceMm, List<ClearanceViolation> violations)
        {
            var dialog = new TaskDialog("樓梯淨高檢核結果") { TitleAutoPrefix = false };

            if (violations.Count == 0)
            {
                dialog.MainInstruction =
                    $"檢核通過:{stairCount} 座樓梯上方淨高皆不小於 {minClearanceMm:0} mm。";
            }
            else
            {
                dialog.MainInstruction =
                    $"發現 {violations.Count} 處淨高不足(要求不小於 {minClearanceMm:0} mm)";

                const int maxLines = 30;
                IEnumerable<string> lines = violations
                    .OrderBy(v => v.Clearance)
                    .Select(v => Describe(uidoc.Document, v));
                dialog.MainContent = string.Join("\n", lines.Take(maxLines));
                if (violations.Count > maxLines)
                    dialog.MainContent += $"\n…其餘 {violations.Count - maxLines} 筆省略";

                dialog.FooterText = "淨高不足的障礙物已加入目前選取集。";

                // 把違規障礙物選起來,方便使用者直接定位
                uidoc.Selection.SetElementIds(
                    violations.Select(v => v.ObstructionId).Distinct().ToList());
            }

            dialog.Show();
        }

        private static string Describe(Document doc, ClearanceViolation violation)
        {
            Element obstruction = doc.GetElement(violation.ObstructionId);
            string obstructionName = obstruction == null
                ? "(未知元件)"
                : $"{obstruction.Category?.Name}「{obstruction.Name}」(Id {violation.ObstructionId.IntegerValue})";

            double clearanceMm = UnitUtils.ConvertFromInternalUnits(violation.Clearance, UnitTypeId.Millimeters);
            double xMeters = UnitUtils.ConvertFromInternalUnits(violation.Location.X, UnitTypeId.Meters);
            double yMeters = UnitUtils.ConvertFromInternalUnits(violation.Location.Y, UnitTypeId.Meters);

            return $"樓梯「{violation.Stair.Name}」(Id {violation.Stair.Id.IntegerValue})上方 {obstructionName}:"
                 + $"淨高僅 {clearanceMm:0} mm,位置 ({xMeters:0.00}, {yMeters:0.00}) m";
        }
    }
}
