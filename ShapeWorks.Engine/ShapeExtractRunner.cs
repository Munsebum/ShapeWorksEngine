using Newtonsoft.Json;
using System;
using System.IO;
using System.IO.Pipes;
using System.Text;
using Autodesk.Revit.DB;

namespace ShapeWorks.Engine
{
    public class ShapeExtractRunner
    {
        private const string PipeName = "ES_PIPE";

        public static int ExecuteShapeExtract(string requestJsonFile, Document doc)
        {
            try
            {
                // 테스트
                if (string.IsNullOrWhiteSpace(requestJsonFile))
                    return -1; 

                if (!File.Exists(requestJsonFile))
                    return -2; 

                
                string jsonText = File.ReadAllText(requestJsonFile);
                ShapeExtractRequest requestData = JsonConvert.DeserializeObject<ShapeExtractRequest>(jsonText);

                
                if (requestData.ES != null)
                {
                    foreach (var esItem in requestData.ES)
                    {
                        
                        var result = RebarExtractor.GetTargetRebarData(doc, esItem);
                        double extractedDiameter = result.Diameter;

                        if (extractedDiameter > 0)
                        {
                            foreach (var pset in esItem.PSETList)
                            {
                                foreach (var variable in pset.VariableList)
                                {
                                    if (variable.VariableName == "fIdiareb")
                                    {
                                        variable.VariableValue = Math.Round(extractedDiameter, 1).ToString();
                                    }
                                }
                            }
                        }
                    }
                }

                
                string requestFolder = Path.GetDirectoryName(requestJsonFile) ?? string.Empty;
                string requestFileName = Path.GetFileName(requestJsonFile);
                string responseFileName = requestFileName.StartsWith("ES_")
                    ? "ESR_" + requestFileName.Substring(3)
                    : "ESR_" + requestFileName;

                string responseJsonFile = Path.Combine(requestFolder, responseFileName);

                
                string outputJson = JsonConvert.SerializeObject(requestData, Formatting.Indented);
                File.WriteAllText(responseJsonFile, outputJson);

                // 완료 알림 전송
                NotifyESCompleted(responseJsonFile);

                return 0; // 정상 완료
            }
            catch (Exception)
            {
                return -99; // 예외 발생
            }
        }

        
        private static void NotifyESCompleted(string resultFileName)
        {
            try
            {
                // 가이드의 JSON 형식 및 EscapeJson 적용
                string json = "{\r\n" +
                              " \"action\": \"shape_extract_completed\",\r\n" +
                              " \"result_file\": \"" + EscapeJson(resultFileName) + "\"\r\n" +
                              "}";

                using (var pipe = new NamedPipeClientStream(".", PipeName, PipeDirection.Out))
                {
                    pipe.Connect(1000);
                    
                    using (var sw = new StreamWriter(pipe, new UTF8Encoding(false)))
                    {
                        sw.Write(json);
                        sw.Flush();
                    }
                }
            }
            catch
            {
                // 알림 실패 시 별도 처리는 하지 않음 (가이드 준수)
            }
        }

        
        private static string EscapeJson(string value)
        {
            return value
                .Replace("\\", "\\\\")
                .Replace("\"", "\\\"");
        }
    }
}