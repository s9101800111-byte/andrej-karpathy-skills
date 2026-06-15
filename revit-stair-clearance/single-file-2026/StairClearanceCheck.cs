using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows.Forms;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Architecture;
using Autodesk.Revit.UI;
// 單檔同時用到 Revit 與 WinForms,以下別名消除同名型別衝突
using TaskDialog = Autodesk.Revit.UI.TaskDialog;
using Form = System.Windows.Forms.Form;
using TextBox = System.Windows.Forms.TextBox;
using Control = System.Windows.Forms.Control;

namespace StairClearanceCheck
{
    /// <summary>一筆淨高不足的檢核結果。</summary>
    public class ClearanceViolation
    {
        /// <summary>被檢核的樓梯。</summary>
        public Stairs Stair { get; set; }

        /// <summary>上方障礙物的元件 Id。</summary>
        public ElementId ObstructionId { get; set; }

        /// <summary>量得的最小淨高(內部單位:英呎)。</summary>
        public double Clearance { get; set; }

        /// <summary>最小淨高發生的位置(樓梯表面上的點)。</summary>
        public XYZ Location { get; set; }
    }

    /// <summary>
    /// 樓梯淨高檢核核心:
    /// 在樓梯踏面與平台的朝上表面佈點,從每個點垂直向上發射射線,
    /// 命中的第一個元件即為「垂直投影與樓梯有交集的上方物件」,
    /// 命中距離即為該點淨高;低於設定值者記為違規。
    /// </summary>
    public class StairClearanceChecker
    {
        private readonly Document _doc;
        private readonly View3D _view3D;
        private readonly double _minClearance; // 內部單位(英呎)
        private readonly double _spacing;      // 檢測點間距,內部單位(英呎)

        /// <summary>射線起點自表面抬升量(約 3mm),避免射線命中樓梯自身表面。</summary>
        private const double RayStartOffset = 0.01;

        /// <summary>法向量 Z 分量門檻,超過才視為「朝上」的面(踏面、平台面)。</summary>
        private const double MinUpwardNormalZ = 0.7;

        public StairClearanceChecker(Document doc, View3D view3D, double minClearance, double spacing)
        {
            _doc = doc;
            _view3D = view3D;
            _minClearance = minClearance;
            _spacing = spacing;
        }

        /// <summary>檢核單一樓梯,回傳每個障礙物的最小淨高違規(每個障礙物只記最差一筆)。</summary>
        public IList<ClearanceViolation> Check(Stairs stair)
        {
            // 排除樓梯自身構件(梯段、平台、支撐)與其扶手,避免射線打到自己造成誤判
            var excludeIds = new List<ElementId> { stair.Id };
            excludeIds.AddRange(stair.GetStairsRuns());
            excludeIds.AddRange(stair.GetStairsLandings());
            excludeIds.AddRange(stair.GetStairsSupports());
            excludeIds.AddRange(stair.GetAssociatedRailings());

            var intersector = new ReferenceIntersector(
                new ExclusionFilter(excludeIds), FindReferenceTarget.Face, _view3D);

            var worstPerObstruction = new Dictionary<ElementId, ClearanceViolation>();

            foreach (XYZ point in GetSamplePoints(stair))
            {
                XYZ origin = point + XYZ.BasisZ * RayStartOffset;
                ReferenceWithContext hit = intersector.FindNearest(origin, XYZ.BasisZ);
                if (hit == null)
                    continue; // 上方無任何物件

                double clearance = hit.Proximity + RayStartOffset;
                if (clearance >= _minClearance)
                    continue;

                ElementId obstructionId = hit.GetReference().ElementId;
                if (!worstPerObstruction.TryGetValue(obstructionId, out ClearanceViolation existing)
                    || clearance < existing.Clearance)
                {
                    worstPerObstruction[obstructionId] = new ClearanceViolation
                    {
                        Stair = stair,
                        ObstructionId = obstructionId,
                        Clearance = clearance,
                        Location = point,
                    };
                }
            }

            return worstPerObstruction.Values.ToList();
        }

        /// <summary>取得樓梯所有朝上表面的檢測點。</summary>
        private IEnumerable<XYZ> GetSamplePoints(Element stair)
        {
            var options = new Options { DetailLevel = ViewDetailLevel.Fine, ComputeReferences = false };
            GeometryElement geometry = stair.get_Geometry(options);
            if (geometry == null)
                yield break;

            foreach (Solid solid in GetSolids(geometry))
                foreach (Face face in solid.Faces)
                    foreach (XYZ point in SampleUpwardFace(face))
                        yield return point;
        }

        private static IEnumerable<Solid> GetSolids(GeometryElement geometry)
        {
            foreach (GeometryObject obj in geometry)
            {
                if (obj is Solid solid && solid.Volume > 1e-9)
                    yield return solid;
                else if (obj is GeometryInstance instance)
                    foreach (Solid nested in GetSolids(instance.GetInstanceGeometry()))
                        yield return nested;
            }
        }

        /// <summary>在朝上的面內以固定間距佈點(UV 網格,含邊界點)。</summary>
        private IEnumerable<XYZ> SampleUpwardFace(Face face)
        {
            BoundingBoxUV bounds = face.GetBoundingBox();
            double spanU = bounds.Max.U - bounds.Min.U;
            double spanV = bounds.Max.V - bounds.Min.V;

            // 平面的 UV 參數與模型長度同尺度,可直接以間距切分;
            // 至少切 1 格,確保窄踏面也有檢測點
            int countU = Math.Max(1, (int)Math.Ceiling(spanU / _spacing));
            int countV = Math.Max(1, (int)Math.Ceiling(spanV / _spacing));

            for (int i = 0; i <= countU; i++)
            {
                for (int j = 0; j <= countV; j++)
                {
                    var uv = new UV(
                        bounds.Min.U + spanU * i / countU,
                        bounds.Min.V + spanV * j / countV);

                    if (!face.IsInside(uv))
                        continue;
                    if (face.ComputeNormal(uv).Z < MinUpwardNormalZ)
                        continue; // 只檢核朝上的面

                    yield return face.Evaluate(uv);
                }
            }
        }
    }

    /// <summary>輸入最小淨高要求與檢測點間距的對話框。</summary>
    public class ClearanceInputForm : Form
    {
        private readonly TextBox _clearanceBox;
        private readonly TextBox _spacingBox;

        /// <summary>最小淨高要求(mm)。</summary>
        public double MinClearanceMm { get; private set; }

        /// <summary>檢測點間距(mm)。</summary>
        public double SampleSpacingMm { get; private set; }

        public ClearanceInputForm()
        {
            Text = "樓梯淨高檢核";
            FormBorderStyle = FormBorderStyle.FixedDialog;
            StartPosition = FormStartPosition.CenterScreen;
            MaximizeBox = false;
            MinimizeBox = false;
            ShowInTaskbar = false;
            ClientSize = new System.Drawing.Size(290, 125);

            var clearanceLabel = new Label { Text = "最小淨高要求 (mm):", Left = 12, Top = 16, Width = 145 };
            _clearanceBox = new TextBox { Left = 165, Top = 13, Width = 110, Text = "1900" };

            var spacingLabel = new Label { Text = "檢測點間距 (mm):", Left = 12, Top = 46, Width = 145 };
            _spacingBox = new TextBox { Left = 165, Top = 43, Width = 110, Text = "300" };

            var okButton = new Button { Text = "開始檢核", Left = 70, Top = 85, Width = 95, DialogResult = DialogResult.OK };
            var cancelButton = new Button { Text = "取消", Left = 180, Top = 85, Width = 95, DialogResult = DialogResult.Cancel };
            okButton.Click += OnOkClicked;

            Controls.AddRange(new Control[] { clearanceLabel, _clearanceBox, spacingLabel, _spacingBox, okButton, cancelButton });
            AcceptButton = okButton;
            CancelButton = cancelButton;
        }

        private void OnOkClicked(object sender, EventArgs e)
        {
            if (!double.TryParse(_clearanceBox.Text, out double clearance) || clearance <= 0
                || !double.TryParse(_spacingBox.Text, out double spacing) || spacing <= 0)
            {
                MessageBox.Show("請輸入大於 0 的數值。", "樓梯淨高檢核",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
                DialogResult = DialogResult.None; // 留在對話框讓使用者修正
                return;
            }

            MinClearanceMm = clearance;
            SampleSpacingMm = spacing;
        }
    }

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
                : $"{obstruction.Category?.Name}「{obstruction.Name}」(Id {violation.ObstructionId.Value})";

            double clearanceMm = UnitUtils.ConvertFromInternalUnits(violation.Clearance, UnitTypeId.Millimeters);
            double xMeters = UnitUtils.ConvertFromInternalUnits(violation.Location.X, UnitTypeId.Meters);
            double yMeters = UnitUtils.ConvertFromInternalUnits(violation.Location.Y, UnitTypeId.Meters);

            return $"樓梯「{violation.Stair.Name}」(Id {violation.Stair.Id.Value})上方 {obstructionName}:"
                 + $"淨高僅 {clearanceMm:0} mm,位置 ({xMeters:0.00}, {yMeters:0.00}) m";
        }
    }
}
