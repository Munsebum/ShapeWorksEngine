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

                        if (result.VariableMap != null)
                        {
                            foreach (var pset in esItem.PSETList)
                            {
                                foreach (var variable in pset.VariableList)
                                {
                                    if (result.VariableMap.TryGetValue(variable.VariableName, out string val))
                                        variable.VariableValue = val;
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

                NotifyESCompleted(responseJsonFile);
                return 0;
            }
            catch (Exception)
            {
                return -99;
            }
        }

        private static void NotifyESCompleted(string resultFileName)
        {
            try
            {
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
            catch { }
        }

        private static string EscapeJson(string value)
        {
            return value
                .Replace("\\", "\\\\")
                .Replace("\"", "\\\"");
        }
    }
}