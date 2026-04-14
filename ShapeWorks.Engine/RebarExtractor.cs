using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Structure;
using System;
using System.Collections.Generic;
using System.Linq;

namespace ShapeWorks.Engine
{
    public class RebarExtractor
    {
        public struct RebarResult
        {
            public double Diameter;
            public double Spacing;
            public double CoverVertical;
            public double CoverHorizontal;
            public double TotalBarLength;
            public double RebarArea;
            public double ElementHeight;
            public double ElementLength;
            public Dictionary<string, string> VariableMap;
        }

        public static RebarResult GetTargetRebarData(Document doc, ESItem esInfo)
        {
            RebarResult result = new RebarResult();
            string elementName = esInfo.LibraryElementName;

            if (elementName.Contains("거더"))
                return GetGirderRebarData(doc, esInfo);

            // 향후 슬래브, 교대, 기초 등 추가 지점

            return result;
        }

        // ──────────────────────────────────────────────
        // 거더 전담 로직
        // ──────────────────────────────────────────────
        private static RebarResult GetGirderRebarData(Document doc, ESItem esInfo)
        {
            RebarResult finalResult = new RebarResult();
            var sectionInfo = esInfo.SectionReviewList?.FirstOrDefault();
            if (sectionInfo == null) return finalResult;

            string direction = sectionInfo.SectionDirection;
            string position = sectionInfo.SectionPosition;
            string detail = sectionInfo.DetailPosition;

            // 1. 하드코딩 값 주입
            finalResult = ApplyGirderHardcodedValues(finalResult, direction, position, detail);

            // 2. 타겟 철근 이름 결정
            string targetRebarName = "";
            if (position == "중앙부" && detail == "상면") targetRebarName = "1";
            else if (position == "중앙부" && detail == "하면") targetRebarName = "2";
            else if (position == "단부" && detail == "상면") targetRebarName = "1";
            else if (position == "단부" && detail == "하면") targetRebarName = "2";

            if (string.IsNullOrEmpty(targetRebarName)) return finalResult;

            // 3. 현재 뷰에서 철근 수집
            FilteredElementCollector collector = new FilteredElementCollector(doc, doc.ActiveView.Id);
            var allRebars = collector.OfCategory(BuiltInCategory.OST_Rebar)
                                     .OfClass(typeof(Rebar))
                                     .Cast<Rebar>()
                                     .ToList();

            foreach (Rebar rebar in allRebars)
            {
                if (rebar.LookupParameter("Host Mark")?.AsString() != "듀얼-PC거더") continue;
                if (direction == "교축(종방향)" && rebar.LookupParameter("Comments")?.AsString() != "주철근") continue;

                ElementId typeId = rebar.GetTypeId();
                RebarBarType barType = doc.GetElement(typeId) as RebarBarType;
                if (barType == null || !barType.Name.Contains(targetRebarName)) continue;

                // ① 지름
                Parameter diaParam = barType.get_Parameter(BuiltInParameter.REBAR_BAR_DIAMETER);
                finalResult.Diameter = (diaParam?.AsDouble() ?? 0) * 304.8;

                // ② 철근 단면적 (π × r²)
                if (finalResult.Diameter > 0)
                    finalResult.RebarArea = Math.PI * Math.Pow(finalResult.Diameter / 2.0, 2);

                // ③ 간격
                if (rebar.LayoutRule != RebarLayoutRule.Single)
                    finalResult.Spacing = rebar.MaxSpacing * 304.8;

                // ④ Total Bar Length
                Parameter totalLenParam = rebar.LookupParameter("Total Bar Length");
                if (totalLenParam != null)
                    finalResult.TotalBarLength = totalLenParam.AsDouble() * 304.8;

                // ⑤ 피복
                var covers = GetClearCover(rebar, doc);
                finalResult.CoverVertical = covers.vertical;
                finalResult.CoverHorizontal = covers.horizontal;

                // ⑥ VariableMap 구성
                finalResult.VariableMap = BuildVariableMap(finalResult);

                return finalResult;
            }

            return finalResult;
        }

        // ──────────────────────────────────────────────
        // VariableMap 구성
        // ──────────────────────────────────────────────
        private static Dictionary<string, string> BuildVariableMap(RebarResult r)
        {
            var vm = new Dictionary<string, string>();

            if (r.Diameter > 0)
            {
                vm["fIdiareb"] = Math.Round(r.Diameter, 1).ToString();
                vm["fIdb"] = Math.Round(r.Diameter, 1).ToString();
                vm["fIdbt"] = Math.Round(r.Diameter, 1).ToString();
            }
            if (r.CoverVertical > 0)
            {
                vm["fItc"] = Math.Round(r.CoverVertical, 1).ToString();
                vm["fIc"] = Math.Round(r.CoverVertical, 1).ToString();
            }
            if (r.CoverHorizontal > 0)
                vm["fIc1"] = Math.Round(r.CoverHorizontal, 1).ToString();

            if (r.Spacing > 0)
            {
                vm["fIcensst"] = Math.Round(r.Spacing, 1).ToString();
                vm["fIa"] = Math.Round(r.Spacing, 1).ToString();
            }
            if (r.RebarArea > 0)
                vm["fItrbacr"] = Math.Round(r.RebarArea, 1).ToString();

            if (r.TotalBarLength > 0)
                vm["fIamoreb"] = Math.Round(r.TotalBarLength, 1).ToString();

            if (r.ElementHeight > 0)
            {
                vm["fIHei"] = Math.Round(r.ElementHeight, 1).ToString();
                vm["fIh"] = Math.Round(r.ElementHeight, 1).ToString();
            }
            if (r.ElementLength > 0)
                vm["fIlength"] = Math.Round(r.ElementLength, 1).ToString();

            // fIAv, fIs, fIAc, fIAp → 패스 (나중에 구현)

            return vm;
        }

        // ──────────────────────────────────────────────
        // 거더 하드코딩 값 테이블
        // ──────────────────────────────────────────────
        private static RebarResult ApplyGirderHardcodedValues(
            RebarResult result, string direction, string position, string detail)
        {
            if (direction == "교축(종방향)" && position == "중앙부" && detail == "상면")
            {
                result.ElementHeight = 2000;
                result.ElementLength = 24850;
            }
            else if (direction == "교축(종방향)" && position == "중앙부" && detail == "하면")
            {
                result.ElementHeight = 2000;
                result.ElementLength = 24850;
            }
            else if (direction == "교축(종방향)" && position == "단부" && detail == "상면")
            {
                result.ElementHeight = 2000;
                result.ElementLength = 24850;
            }
            else if (direction == "교축(종방향)" && position == "단부" && detail == "하면")
            {
                result.ElementHeight = 2000;
                result.ElementLength = 24850;
            }
            // 향후 슬래브, 교대, 기초 추가 지점

            return result;
        }

        // ──────────────────────────────────────────────
        // 피복 추출 (Ray Casting)
        // ──────────────────────────────────────────────
        private static (double vertical, double horizontal) GetClearCover(Rebar rebar, Document doc)
        {
            Element host = doc.GetElement(rebar.GetHostId());
            if (host == null) return (0, 0);

            IList<Curve> curves = rebar.GetCenterlineCurves(false, true, true,
                MultiplanarOption.IncludeAllMultiplanarCurves, 0).ToList();
            if (curves.Count == 0) return (0, 0);
            Curve representativeCurve = curves.OrderByDescending(c => c.Length).First();

            XYZ midPoint = representativeCurve.Evaluate(0.5, true);
            XYZ rebarDir = (representativeCurve.GetEndPoint(1) - representativeCurve.GetEndPoint(0)).Normalize();

            XYZ zAxis = XYZ.BasisZ;
            XYZ sideDir = rebarDir.CrossProduct(zAxis).Normalize();

            ReferenceIntersector intersector = new ReferenceIntersector(
                host.Id,
                FindReferenceTarget.Face,
                (View3D)doc.ActiveView);

            double distPosZ = GetMinProximity(intersector, midPoint, zAxis);
            double distNegZ = GetMinProximity(intersector, midPoint, zAxis.Negate());
            double vertical = Math.Min(distPosZ, distNegZ) * 304.8;

            double distPosSide = GetMinProximity(intersector, midPoint, sideDir);
            double distNegSide = GetMinProximity(intersector, midPoint, sideDir.Negate());
            double horizontal = Math.Min(distPosSide, distNegSide) * 304.8;

            return (vertical, horizontal);
        }

        private static double GetMinProximity(ReferenceIntersector intersector, XYZ origin, XYZ direction)
        {
            var hits = intersector.Find(origin, direction);
            if (hits == null || hits.Count == 0) return double.MaxValue;
            double min = double.MaxValue;
            foreach (var hit in hits)
                if (hit.Proximity < min) min = hit.Proximity;
            return min;
        }
    }
}