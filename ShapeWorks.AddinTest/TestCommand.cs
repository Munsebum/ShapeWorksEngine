using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Structure;
using Autodesk.Revit.UI;
using ShapeWorks.Engine;
using System.Linq;
using System.Windows.Forms;

namespace ShapeWorks.AddinTest
{
    [Transaction(TransactionMode.Manual)]
    public class TestCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            Document doc = commandData.Application.ActiveUIDocument.Document;

            // 파일 선택 다이얼로그
            string testJsonPath = "";
            using (OpenFileDialog dlg = new OpenFileDialog())
            {
                dlg.Title = "ES JSON 파일 선택";
                dlg.Filter = "JSON 파일 (*.json)|*.json|모든 파일 (*.*)|*.*";
                dlg.InitialDirectory = @"D:\";

                if (dlg.ShowDialog() != DialogResult.OK)
                    return Result.Cancelled;

                testJsonPath = dlg.FileName;
            }

            // 디버그: 방향별 Ray 거리 확인
            FilteredElementCollector col = new FilteredElementCollector(doc, doc.ActiveView.Id);
            var rebars = col.OfCategory(BuiltInCategory.OST_Rebar)
                            .OfClass(typeof(Rebar))
                            .Cast<Rebar>().ToList();

            Rebar debugRebar = null;
            foreach (var r in rebars)
            {
                if (r.LookupParameter("Host Mark")?.AsString() != "듀얼-PC거더") continue;
                if (r.LookupParameter("Comments")?.AsString() != "주철근") continue;
                var bt = doc.GetElement(r.GetTypeId()) as RebarBarType;
                if (bt == null || !bt.Name.Contains("1")) continue;
                debugRebar = r;
                break;
            }

            if (debugRebar == null)
            {
                TaskDialog.Show("디버그", "조건에 맞는 철근을 찾지 못했습니다.");
                return Result.Failed;
            }

            var cs = debugRebar.GetCenterlineCurves(false, true, true,
                MultiplanarOption.IncludeAllMultiplanarCurves, 0).ToList();
            var rc = cs.OrderByDescending(c => c.Length).First();
            XYZ mid = rc.Evaluate(0.5, true);
            XYZ rDir = (rc.GetEndPoint(1) - rc.GetEndPoint(0)).Normalize();
            XYZ z = XYZ.BasisZ;
            XYZ side = rDir.CrossProduct(z).Normalize();

            Element host = doc.GetElement(debugRebar.GetHostId());
            ReferenceIntersector ri = new ReferenceIntersector(
                host.Id, FindReferenceTarget.Face, (View3D)doc.ActiveView);

            var hPosZ = ri.Find(mid, z);
            var hNegZ = ri.Find(mid, z.Negate());
            var hPosSide = ri.Find(mid, side);
            var hNegSide = ri.Find(mid, side.Negate());

            string log = $"선택 파일: {System.IO.Path.GetFileName(testJsonPath)}\n\n";
            log += $"중간점: ({mid.X:F3}, {mid.Y:F3}, {mid.Z:F3})\n";
            log += $"철근방향: ({rDir.X:F3}, {rDir.Y:F3}, {rDir.Z:F3})\n";
            log += $"측면방향: ({side.X:F3}, {side.Y:F3}, {side.Z:F3})\n\n";

            log += "+Z hits:\n";
            if (hPosZ != null) foreach (var h in hPosZ) log += $"  {h.Proximity * 304.8:F2} mm\n";
            else log += "  없음\n";

            log += "-Z hits:\n";
            if (hNegZ != null) foreach (var h in hNegZ) log += $"  {h.Proximity * 304.8:F2} mm\n";
            else log += "  없음\n";

            log += "+Side hits:\n";
            if (hPosSide != null) foreach (var h in hPosSide) log += $"  {h.Proximity * 304.8:F2} mm\n";
            else log += "  없음\n";

            log += "-Side hits:\n";
            if (hNegSide != null) foreach (var h in hNegSide) log += $"  {h.Proximity * 304.8:F2} mm\n";
            else log += "  없음\n";

            TaskDialog.Show("Ray 방향별 거리", log);

            // 엔진 전체 흐름 실행
            int resultCode = ShapeExtractRunner.ExecuteShapeExtract(testJsonPath, doc);

            string resultMsg;
            if (resultCode == 0) resultMsg = "✅ 성공! ESR_ 파일을 확인하세요.";
            else if (resultCode == -1) resultMsg = "❌ 오류: JSON 경로가 비어있거나 유효하지 않음";
            else if (resultCode == -2) resultMsg = "❌ 오류: JSON 파일이 존재하지 않음";
            else if (resultCode == -99) resultMsg = "❌ 오류: 엔진 내부 예외 발생";
            else resultMsg = $"❌ 알 수 없는 코드: {resultCode}";

            TaskDialog.Show("ShapeWorks 엔진 테스트", resultMsg);

            return resultCode == 0 ? Result.Succeeded : Result.Failed;
        }
    }
}