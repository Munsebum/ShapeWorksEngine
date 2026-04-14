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
            public double Diameter;         // 철근 지름 (mm)
            public double Spacing;          // 철근 간격 (mm)
            public double CoverVertical;    // 수직 피복 (상면/하면 중 작은 값)
            public double CoverHorizontal;  // 수평 피복 (측면 2개 중 작은 값)
        }

        public static RebarResult GetTargetRebarData(Document doc, ESItem esInfo)
        {
            RebarResult result = new RebarResult { Diameter = 0, Spacing = 0, CoverVertical = 0, CoverHorizontal = 0 };
            string elementName = esInfo.LibraryElementName;

            if (elementName.Contains("거더"))
                return GetGirderRebarData(doc, esInfo);

            return result;
        }

        private static RebarResult GetGirderRebarData(Document doc, ESItem esInfo)
        {
            RebarResult finalResult = new RebarResult();
            var sectionInfo = esInfo.SectionReviewList?.FirstOrDefault();
            if (sectionInfo == null) return finalResult;

            string direction = sectionInfo.SectionDirection;
            string position = sectionInfo.SectionPosition;
            string detail = sectionInfo.DetailPosition;

            string targetRebarName = "";
            if (position == "중앙부" && detail == "상면") targetRebarName = "1";
            else if (position == "중앙부" && detail == "하면") targetRebarName = "2";
            else if (position == "단부" && detail == "상면") targetRebarName = "1";
            else if (position == "단부" && detail == "하면") targetRebarName = "2";

            if (string.IsNullOrEmpty(targetRebarName)) return finalResult;

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

                // ① 지름 추출
                Parameter diaParam = barType.get_Parameter(BuiltInParameter.REBAR_BAR_DIAMETER);
                finalResult.Diameter = (diaParam?.AsDouble() ?? 0) * 304.8;

                // ② 간격 추출
                if (rebar.LayoutRule != RebarLayoutRule.Single)
                    finalResult.Spacing = rebar.MaxSpacing * 304.8;

                // ③ 피복 추출
                var covers = GetClearCover(rebar, doc);
                finalResult.CoverVertical = covers.vertical;
                finalResult.CoverHorizontal = covers.horizontal;

                return finalResult;
            }

            return finalResult;
        }

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

            // 수직: +Z / -Z 중 작은 값
            double distPosZ = GetMinProximity(intersector, midPoint, zAxis);
            double distNegZ = GetMinProximity(intersector, midPoint, zAxis.Negate());
            double vertical = Math.Min(distPosZ, distNegZ) * 304.8;

            // 수평: +Side / -Side 중 작은 값
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