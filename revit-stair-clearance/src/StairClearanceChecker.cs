using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Architecture;

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
}
