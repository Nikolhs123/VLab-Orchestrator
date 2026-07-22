using System.IO;
using UnityEngine;

namespace Thesis.Orchestration.Telemetry
{
    public static class EvaluationExportPathUtility
    {
        private const string ExportFolderName = "EvaluationExports";

        public static string GetExportsRoot()
        {
            string applicationFolder = Directory.GetParent(Application.dataPath)?.FullName;
            if (string.IsNullOrWhiteSpace(applicationFolder))
                applicationFolder = Application.persistentDataPath;

            return Path.GetFullPath(Path.Combine(applicationFolder, ExportFolderName));
        }

        public static string GetComparisonSummariesFolder()
        {
            return Path.Combine(GetExportsRoot(), "ComparisonSummaries");
        }
    }
}
