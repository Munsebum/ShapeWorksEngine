using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Structure;
using System;
using System.Collections.Generic;
using System.Linq;

namespace ShapeWorks.Engine
{
    public class RebarExtractor
    {
        // 결과값을 한꺼번에 담아 전달하기 위한 구조체
        public struct RebarResult
        {
            public double Diameter; // 철근 지름 (mm)
            public double Spacing;  // 철근 간격 (mm)
            public double Cover;    // 순피복 두께 (mm)
        }

        // 🌟 [총괄 지휘관] 부재 이름에 따라 적절한 추출 로직으로 분기
        public static RebarResult GetTargetRebarData(Document doc, ESItem esInfo)
        {
            RebarResult result = new RebarResult { Diameter = 0, Spacing = 0, Cover = 0 };
            string elementName = esInfo.LibraryElementName;

            if (elementName.Contains("거더"))
            {
                return GetGirderRebarData(doc, esInfo);
            }
            // 향후 슬래브, 기초 등 로직 추가 지점

            return result;
        }

        // ----------------------------------------------------
        // ▼ [거더 전담 로직] 거더 철근의 지름, 간격, 피복 추출
        // ----------------------------------------------------
        private static RebarResult GetGirderRebarData(Document doc, ESItem esInfo)
        {
            RebarResult finalResult = new RebarResult();
            var sectionInfo = esInfo.SectionReviewList?.FirstOrDefault();
            if (sectionInfo == null) return finalResult;

            string direction = sectionInfo.SectionDirection;
            string position = sectionInfo.SectionPosition;
            string detail = sectionInfo.DetailPosition;

            // 1. 위치별 타겟 철근 타입 결정 (메모지)
            string targetRebarName = "";
            if (position == "중앙부" && detail == "상면") targetRebarName = "1";
            else if (position == "중앙부" && detail == "하면") targetRebarName = "2";
            else if (position == "단부" && detail == "상면") targetRebarName = "1";
            else if (position == "단부" && detail == "하면") targetRebarName = "2";

            if (string.IsNullOrEmpty(targetRebarName)) return finalResult;

            // 2. 현재 뷰에서 철근 객체 수집 (누락 방지 필터)
            FilteredElementCollector collector = new FilteredElementCollector(doc, doc.ActiveView.Id);
            var allRebars = collector.OfCategory(BuiltInCategory.OST_Rebar)
                                     .OfClass(typeof(Rebar))
                                     .Cast<Rebar>()
                                     .ToList();

            foreach (Rebar rebar in allRebars)
            {
                // [필터링 A] Host Mark 검사
                if (rebar.LookupParameter("Host Mark")?.AsString() != "듀얼-PC거더") continue;

                // [필터링 B] 방향 및 용도(Comments) 검사
                if (direction == "교축(종방향)" && rebar.LookupParameter("Comments")?.AsString() != "주철근") continue;

                // [필터링 C] 철근 타입 이름 검사
                ElementId typeId = rebar.GetTypeId();
                RebarBarType barType = doc.GetElement(typeId) as RebarBarType;
                if (barType == null || !barType.Name.Contains(targetRebarName)) continue;

                // --- [데이터 추출 시작] ---

                // ① 지름 추출 (mm 변환)
                Parameter diaParam = barType.get_Parameter(BuiltInParameter.REBAR_BAR_DIAMETER);
                finalResult.Diameter = (diaParam?.AsDouble() ?? 0) * 304.8;

                // ② 간격 추출 (LayoutRule이 Single이 아닐 때만 mm 변환)
                if (rebar.LayoutRule != RebarLayoutRule.Single)
                {
                    finalResult.Spacing = rebar.MaxSpacing * 304.8;
                }

                // ③ 순피복 두께 추출 (평행면 기반 최단 거리 계산)
                finalResult.Cover = GetClearCover(rebar, doc, finalResult.Diameter);

                return finalResult; // 조건에 맞는 첫 번째 철근에서 데이터를 뽑았으므로 즉시 반환
            }

            return finalResult;
        }

        // ----------------------------------------------------
        // ▼ [피복 계산 도우미] 가장 긴 스케치 라인 기준 평행면 거리 계산
        // ----------------------------------------------------
        private static double GetClearCover(Rebar rebar, Document doc, double barDiaMm)
        {
            Element host = doc.GetElement(rebar.GetHostId());
            if (host == null) return 0;

            // 1. 철근의 모든 선(Curve) 중 가장 긴 선을 대표로 선정 (ㄱ자 철근 대응)
            IList<Curve> curves = rebar.GetCenterlineCurves(false, true, true, MultiplanarOption.IncludeAllMultiplanarCurves, 0).ToList();
            if (curves.Count == 0) return 0;
            Curve representativeCurve = curves.OrderByDescending(c => c.Length).First();

            // 2. 대표 선의 방향 벡터와 중간점(샘플 점) 추출
            XYZ rebarDir = (representativeCurve.GetEndPoint(1) - representativeCurve.GetEndPoint(0)).Normalize();
            XYZ midPoint = representativeCurve.Evaluate(0.5, true);

            // 3. 호스트(거더)의 기하 정보에서 모든 면(Face) 추출
            Options opt = new Options { DetailLevel = ViewDetailLevel.Fine };
            GeometryElement geoElem = host.get_Geometry(opt);
            double minDistance = double.MaxValue;

            foreach (GeometryObject obj in geoElem)
            {
                if (obj is Solid solid)
                {
                    foreach (Face face in solid.Faces)
                    {
                        // 4. 평행 검사: 면의 법선과 철근 방향이 수직(내적 0)이면 평행함
                        XYZ faceNormal = face.ComputeNormal(new UV(0.5, 0.5));
                        if (Math.Abs(faceNormal.DotProduct(rebarDir)) < 0.001)
                        {
                            // 5. 평행한 면에 수선을 내려 최단 거리 갱신
                            IntersectionResult proj = face.Project(midPoint);
                            if (proj != null)
                            {
                                if (proj.Distance < minDistance) minDistance = proj.Distance;
                            }
                        }
                    }
                }
            }

            if (minDistance == double.MaxValue) return 0;

            // 6. 결과 보정: (중심거리 * mm변환) - (철근 지름 / 2) = 순피복 두께
            double clearCover = (minDistance * 304.8) - (barDiaMm / 2.0);
            return clearCover > 0 ? clearCover : 0;
        }
    }
}