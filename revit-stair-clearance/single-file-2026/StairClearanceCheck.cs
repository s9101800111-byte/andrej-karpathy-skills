using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Architecture;
using Autodesk.Revit.UI;
using StairClearanceCheck.UI;

namespace StairClearanceCheck
{
    /// <summary>一筆淨高不足的檢核結果。</summary>
    public class ClearanceViolation
    {
        /// <summary>被檢核的樓梯。</summary>
        public Stairs Stair { get; set; }

        /// <summary>樓梯所屬樓層名稱。</summary>
        public string LevelName { get; set; }

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
        private readonly View3D _view3D;
        private readonly double _minClearance; // 內部單位(英呎)
        private readonly double _spacing;      // 檢測點間距,內部單位(英呎)

        /// <summary>射線起點自表面抬升量(約 3mm),避免射線命中樓梯自身表面。</summary>
        private const double RayStartOffset = 0.01;

        /// <summary>法向量 Z 分量門檻,超過才視為「朝上」的面(踏面、平台面)。</summary>
        private const double MinUpwardNormalZ = 0.7;

        public StairClearanceChecker(View3D view3D, double minClearance, double spacing)
        {
            _view3D = view3D;
            _minClearance = minClearance;
            _spacing = spacing;
        }

        /// <summary>檢核單一樓梯,回傳每個障礙物的最小淨高違規(每個障礙物只記最差一筆)。</summary>
        public IList<ClearanceViolation> Check(Stairs stair, string levelName)
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
                        LevelName = levelName,
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
            int countU = System.Math.Max(1, (int)System.Math.Ceiling(spanU / _spacing));
            int countV = System.Math.Max(1, (int)System.Math.Ceiling(spanV / _spacing));

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

    /// <summary>
    /// 樓梯淨高檢核指令:
    /// 1. WPF 視窗讓使用者複選樓層、輸入最小淨高與檢測點間距(單位 cm)
    /// 2. 在所選樓層的樓梯踏面/平台頂面佈點,垂直向上射線找出上方障礙物
    /// 3. 列出所有淨高不足的物件 Id,並在視圖中亮顯(選取)
    /// </summary>
    [Transaction(TransactionMode.Manual)]
    public class CheckStairClearanceCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            UIDocument uidoc = commandData.Application.ActiveUIDocument;
            Document doc = uidoc.Document;

            // 1. 收集模型中所有樓梯,並依「底部樓層」分組
            List<Stairs> allStairs = new FilteredElementCollector(doc)
                .OfClass(typeof(Stairs))
                .Cast<Stairs>()
                .ToList();
            if (allStairs.Count == 0)
            {
                TaskDialog.Show("樓梯淨高檢核", "模型中找不到樓梯。");
                return Result.Succeeded;
            }

            // 樓層 Id -> 樓層名稱(只列出含樓梯的樓層,依高程排序)
            var levelOptions = allStairs
                .Select(s => GetBaseLevelId(s))
                .Where(id => id != ElementId.InvalidElementId)
                .Distinct()
                .Select(id => doc.GetElement(id) as Level)
                .Where(l => l != null)
                .OrderBy(l => l.Elevation)
                .Select(l => (Id: l.Id.Value, Name: l.Name))
                .ToList();
            if (levelOptions.Count == 0)
            {
                TaskDialog.Show("樓梯淨高檢核", "找不到任何含樓梯的樓層。");
                return Result.Succeeded;
            }

            // 2. WPF 輸入視窗(樓層複選 + cm 數值)
            var window = new ClearanceInputWindow(levelOptions);
            new System.Windows.Interop.WindowInteropHelper(window)
            {
                Owner = commandData.Application.MainWindowHandle,
            };
            if (window.ShowDialog() != true)
                return Result.Cancelled;

            var selectedLevelIds = new HashSet<long>(window.SelectedLevelIds);
            List<Stairs> stairsToCheck = allStairs
                .Where(s => selectedLevelIds.Contains(GetBaseLevelId(s).Value))
                .ToList();
            if (stairsToCheck.Count == 0)
            {
                TaskDialog.Show("樓梯淨高檢核", "所選樓層沒有樓梯可檢核。");
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

            // 4. 執行檢核(cm -> 內部單位)
            double minClearance = UnitUtils.ConvertToInternalUnits(window.MinClearanceCm, UnitTypeId.Centimeters);
            double spacing = UnitUtils.ConvertToInternalUnits(window.SpacingCm, UnitTypeId.Centimeters);
            var checker = new StairClearanceChecker(view3D, minClearance, spacing);

            var violations = new List<ClearanceViolation>();
            foreach (Stairs stair in stairsToCheck)
            {
                string levelName = GetLevelName(doc, stair);
                violations.AddRange(checker.Check(stair, levelName));
            }

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

            // 6. 在視圖中亮顯不符物件,並列出結果
            ShowResults(uidoc, commandData, stairsToCheck.Count, window.MinClearanceCm, violations);
            return Result.Succeeded;
        }

        /// <summary>在當前視圖選取(亮顯)不符物件,並用 WPF 視窗列出清單。</summary>
        private static void ShowResults(
            UIDocument uidoc, ExternalCommandData commandData,
            int stairCount, double minClearanceCm, List<ClearanceViolation> violations)
        {
            List<ElementId> obstructionIds = violations
                .Select(v => v.ObstructionId)
                .Distinct()
                .ToList();

            if (obstructionIds.Count > 0)
            {
                // 選取 = 在視圖中亮顯;並把視圖縮放到這些物件
                uidoc.Selection.SetElementIds(obstructionIds);
                uidoc.ShowElements(obstructionIds);
            }

            string header = violations.Count == 0
                ? $"檢核通過:{stairCount} 座樓梯上方淨高皆不小於 {minClearanceCm:0.#} cm。"
                : $"發現 {violations.Count} 處淨高不足(要求不小於 {minClearanceCm:0.#} cm),已在視圖中亮顯。";

            List<string> lines = violations
                .OrderBy(v => v.Clearance)
                .Select(v => Describe(uidoc.Document, v))
                .ToList();

            var window = new ResultsWindow(header, lines);
            new System.Windows.Interop.WindowInteropHelper(window)
            {
                Owner = commandData.Application.MainWindowHandle,
            };
            window.ShowDialog();
        }

        private static string Describe(Document doc, ClearanceViolation violation)
        {
            Element obstruction = doc.GetElement(violation.ObstructionId);
            string obstructionName = obstruction == null
                ? "(未知元件)"
                : $"{obstruction.Category?.Name}「{obstruction.Name}」(Id {violation.ObstructionId.Value})";

            double clearanceCm = UnitUtils.ConvertFromInternalUnits(violation.Clearance, UnitTypeId.Centimeters);
            double xMeters = UnitUtils.ConvertFromInternalUnits(violation.Location.X, UnitTypeId.Meters);
            double yMeters = UnitUtils.ConvertFromInternalUnits(violation.Location.Y, UnitTypeId.Meters);

            return $"[{violation.LevelName}] 樓梯「{violation.Stair.Name}」(Id {violation.Stair.Id.Value}) "
                 + $"← 障礙物 {obstructionName}:淨高僅 {clearanceCm:0.0} cm,"
                 + $"位置 ({xMeters:0.00}, {yMeters:0.00}) m";
        }

        /// <summary>樓梯的底部樓層 Id(取不到時退回 Element.LevelId)。</summary>
        private static ElementId GetBaseLevelId(Stairs stair)
        {
            Parameter p = stair.get_Parameter(BuiltInParameter.STAIRS_BASE_LEVEL_PARAM);
            ElementId id = p?.AsElementId() ?? ElementId.InvalidElementId;
            return id != ElementId.InvalidElementId ? id : stair.LevelId;
        }

        private static string GetLevelName(Document doc, Stairs stair)
        {
            return (doc.GetElement(GetBaseLevelId(stair)) as Level)?.Name ?? "(無樓層)";
        }
    }
}

namespace StairClearanceCheck.UI
{
    using System.Collections.Generic;
    using System.Linq;
    using System.Windows;
    using System.Windows.Controls;

    /// <summary>樓層複選 + cm 數值輸入的 WPF 視窗(以程式碼建構,無 XAML)。</summary>
    public class ClearanceInputWindow : Window
    {
        private readonly List<(long Id, CheckBox Box)> _levelBoxes = new List<(long, CheckBox)>();
        private readonly TextBox _clearanceBox;
        private readonly TextBox _spacingBox;

        /// <summary>使用者勾選的樓層 Id。</summary>
        public List<long> SelectedLevelIds { get; private set; } = new List<long>();

        /// <summary>最小淨高要求(cm)。</summary>
        public double MinClearanceCm { get; private set; }

        /// <summary>檢測點間距(cm)。</summary>
        public double SpacingCm { get; private set; }

        public ClearanceInputWindow(IEnumerable<(long Id, string Name)> levels)
        {
            Title = "樓梯淨高檢核";
            Width = 340;
            SizeToContent = SizeToContent.Height;
            WindowStartupLocation = WindowStartupLocation.CenterScreen;
            ResizeMode = ResizeMode.NoResize;

            var root = new StackPanel { Margin = new Thickness(14) };

            root.Children.Add(new TextBlock
            {
                Text = "選擇樓層(可複選):",
                FontWeight = FontWeights.Bold,
                Margin = new Thickness(0, 0, 0, 6),
            });

            var selectAll = new CheckBox { Content = "全選 / 全不選", IsChecked = true, Margin = new Thickness(0, 0, 0, 4) };
            selectAll.Checked += (s, e) => SetAll(true);
            selectAll.Unchecked += (s, e) => SetAll(false);
            root.Children.Add(selectAll);

            var levelPanel = new StackPanel();
            foreach (var lv in levels)
            {
                var box = new CheckBox { Content = lv.Name, IsChecked = true, Margin = new Thickness(14, 2, 0, 2) };
                _levelBoxes.Add((lv.Id, box));
                levelPanel.Children.Add(box);
            }
            root.Children.Add(new ScrollViewer
            {
                Content = levelPanel,
                MaxHeight = 220,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                Margin = new Thickness(0, 0, 0, 10),
            });

            root.Children.Add(new TextBlock { Text = "最小淨高 (cm):", Margin = new Thickness(0, 4, 0, 2) });
            _clearanceBox = new TextBox { Text = "190" };
            root.Children.Add(_clearanceBox);

            root.Children.Add(new TextBlock { Text = "檢測點間距 (cm):", Margin = new Thickness(0, 8, 0, 2) });
            _spacingBox = new TextBox { Text = "30" };
            root.Children.Add(_spacingBox);

            var buttons = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Right,
                Margin = new Thickness(0, 14, 0, 0),
            };
            var ok = new Button { Content = "開始檢核", Width = 90, Margin = new Thickness(0, 0, 8, 0), IsDefault = true };
            var cancel = new Button { Content = "取消", Width = 70, IsCancel = true };
            ok.Click += OnOk;
            buttons.Children.Add(ok);
            buttons.Children.Add(cancel);
            root.Children.Add(buttons);

            Content = root;
        }

        private void SetAll(bool value)
        {
            foreach (var (_, box) in _levelBoxes)
                box.IsChecked = value;
        }

        private void OnOk(object sender, RoutedEventArgs e)
        {
            var selected = _levelBoxes.Where(x => x.Box.IsChecked == true).Select(x => x.Id).ToList();
            if (selected.Count == 0)
            {
                MessageBox.Show("請至少選擇一個樓層。", "樓梯淨高檢核", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            if (!double.TryParse(_clearanceBox.Text, out double clearance) || clearance <= 0
                || !double.TryParse(_spacingBox.Text, out double spacing) || spacing <= 0)
            {
                MessageBox.Show("請輸入大於 0 的數值。", "樓梯淨高檢核", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            SelectedLevelIds = selected;
            MinClearanceCm = clearance;
            SpacingCm = spacing;
            DialogResult = true;
        }
    }

    /// <summary>檢核結果清單視窗(可捲動、可複製文字)。</summary>
    public class ResultsWindow : Window
    {
        public ResultsWindow(string header, IEnumerable<string> lines)
        {
            Title = "樓梯淨高檢核結果";
            Width = 600;
            Height = 440;
            WindowStartupLocation = WindowStartupLocation.CenterScreen;

            var root = new DockPanel { Margin = new Thickness(14) };

            var head = new TextBlock
            {
                Text = header,
                FontWeight = FontWeights.Bold,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 0, 0, 10),
            };
            DockPanel.SetDock(head, Dock.Top);
            root.Children.Add(head);

            var close = new Button
            {
                Content = "關閉",
                Width = 80,
                HorizontalAlignment = HorizontalAlignment.Right,
                Margin = new Thickness(0, 10, 0, 0),
                IsDefault = true,
                IsCancel = true,
            };
            close.Click += (s, e) => Close();
            DockPanel.SetDock(close, Dock.Bottom);
            root.Children.Add(close);

            var list = new ListBox { SelectionMode = SelectionMode.Extended };
            foreach (var line in lines)
                list.Items.Add(line);
            root.Children.Add(list);

            Content = root;
        }
    }
}
