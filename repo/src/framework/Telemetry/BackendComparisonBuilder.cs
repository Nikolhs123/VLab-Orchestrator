using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using Thesis.Orchestration.EditorTools;
using UnityEngine;

namespace Thesis.Orchestration.Telemetry
{
    public class BackendComparisonBuilder : MonoBehaviour
    {
        public static BackendComparisonBuilder Instance { get; private set; }

        [Header("Debug")]
        [SerializeField] private bool verboseLogs = true;

        [Serializable]
        public class BackendComparisonRow
        {
            public string scenarioId;
            public string backend;

            public double meanConnectLatencyMs;
            public double meanJoinLatencyMs;
            public double meanSessionStartDelayMs;

            public double meanGrantDelayMs;
            public double meanWaitDurationMs;
            public double maxWaitDurationMs;

            public double transferSuccessRate;
            public double meanTransferLatencyMs;

            public double fairnessIndexWait;

            public double averageFps;
            public double minFps;
            public double averageFrameTimeMs;
            public int finalRotationOutcomeRunCount;
            public double meanFinalRotationErrorDegrees;

            public int runCount;
            public int validRunCount;
            public int invalidRunCount;
            public double meanScenarioDurationMs;
            public double meanAiDecisionCount;
            public double meanAiFallbackCount;
            public double meanAiLatencyMs;
        }

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }

            Instance = this;
        }

        private void OnDestroy()
        {
            if (Instance == this)
                Instance = null;
        }

        public string BuildComparisonFromExports()
        {
            string exportsRoot = EvaluationExportPathUtility.GetExportsRoot();
            if (!Directory.Exists(exportsRoot))
            {
                Log($"Exports root not found: {exportsRoot}");
                return null;
            }

            List<BackendComparisonRow> rows = new List<BackendComparisonRow>();
            OrchestrationProjectConfig projectConfig =
                Resources.Load<OrchestrationProjectConfig>("AIMultiplayerAssistant/OrchestrationProjectConfig");
            string requiredProtocolVersion = projectConfig?.automatedScenarioProtocolVersion ?? string.Empty;

            foreach (string backendDir in Directory.GetDirectories(exportsRoot))
            {
                string backend = Path.GetFileName(backendDir);

                foreach (string scenarioDir in Directory.GetDirectories(backendDir))
                {
                    string scenario = Path.GetFileName(scenarioDir);
                    List<string> runDirs = Directory.GetDirectories(scenarioDir)
                        .Where(IsCompleteRunDirectory)
                        .Where(runDirectory =>
                            IsProtocolCompatible(runDirectory, requiredProtocolVersion))
                        .ToList();

                    if (runDirs.Count == 0)
                        continue;

                    BackendComparisonRow row = new BackendComparisonRow
                    {
                        backend = backend,
                        scenarioId = scenario,
                        runCount = runDirs.Count
                    };

                    List<double> connectMeans = new();
                    List<double> joinMeans = new();
                    List<double> sessionDelayMeans = new();
                    List<double> grantMeans = new();
                    List<double> waitMeans = new();
                    List<double> maxWaits = new();
                    List<double> transferSuccessRates = new();
                    List<double> transferLatencyMeans = new();
                    List<double> fairnessMeans = new();
                    List<double> avgFps = new();
                    List<double> minFps = new();
                    List<double> avgFrameTimes = new();
                    List<double> finalRotationErrors = new();
                    List<double> scenarioDurations = new();
                    List<double> aiDecisionCounts = new();
                    List<double> aiFallbackCounts = new();
                    List<double> aiLatencies = new();
                    int invalidRunCount = 0;
                    int validRunCount = 0;

                    foreach (string runDir in runDirs)
                    {
                        if (TryReadSingleBoolCsv(Path.Combine(runDir, "scenario_metrics_summary.csv"), "scenario_valid", out bool scenarioValid) &&
                            !scenarioValid)
                        {
                            invalidRunCount++;
                            continue;
                        }

                        validRunCount++;

                        ReadSingleValueCsv(Path.Combine(runDir, "session_lifecycle_summary.csv"), "mean_connect_latency_ms", connectMeans);
                        ReadSingleValueCsv(Path.Combine(runDir, "session_lifecycle_summary.csv"), "mean_join_latency_ms", joinMeans);
                        ReadSingleValueCsv(Path.Combine(runDir, "session_lifecycle_summary.csv"), "mean_session_start_visible_delay_ms", sessionDelayMeans);

                        ReadSingleValueCsv(Path.Combine(runDir, "queue_metrics_summary.csv"), "mean_grant_delay_ms", grantMeans);
                        ReadSingleValueCsv(Path.Combine(runDir, "queue_metrics_summary.csv"), "mean_wait_duration_ms", waitMeans);
                        ReadSingleValueCsv(Path.Combine(runDir, "queue_metrics_summary.csv"), "max_wait_duration_ms", maxWaits);

                        ReadSingleValueCsv(Path.Combine(runDir, "authority_metrics_summary.csv"), "success_rate", transferSuccessRates);
                        ReadSingleValueCsv(Path.Combine(runDir, "authority_metrics_summary.csv"), "mean_transfer_latency_ms", transferLatencyMeans);

                        ReadSingleValueCsv(Path.Combine(runDir, "fairness_summary.csv"), "jains_fairness_index_wait", fairnessMeans);

                        ReadSingleValueCsv(Path.Combine(runDir, "runtime_performance_summary.csv"), "average_fps", avgFps);
                        ReadSingleValueCsv(Path.Combine(runDir, "runtime_performance_summary.csv"), "min_fps", minFps);
                        ReadSingleValueCsv(Path.Combine(runDir, "runtime_performance_summary.csv"), "average_frame_time_ms", avgFrameTimes);

                        string scenarioSummaryPath = Path.Combine(runDir, "scenario_metrics_summary.csv");
                        if (TryReadSingleBoolCsv(
                                scenarioSummaryPath,
                                "has_final_rotation_outcome",
                                out bool hasFinalRotationOutcome) &&
                            hasFinalRotationOutcome)
                        {
                            ReadSingleValueCsv(
                                scenarioSummaryPath,
                                "final_rotation_error_degrees",
                                finalRotationErrors);
                        }

                        ReadSingleValueCsv(Path.Combine(runDir, "scenario_metrics_summary.csv"), "total_duration_ms", scenarioDurations);
                        ReadSingleValueCsv(Path.Combine(runDir, "ai_metrics_summary.csv"), "decision_count", aiDecisionCounts);
                        ReadSingleValueCsv(Path.Combine(runDir, "ai_metrics_summary.csv"), "fallback_count", aiFallbackCounts);
                        ReadSingleValueCsv(Path.Combine(runDir, "ai_metrics_summary.csv"), "average_latency_ms", aiLatencies);
                    }

                    row.validRunCount = validRunCount;
                    row.invalidRunCount = invalidRunCount;
                    row.meanConnectLatencyMs = Mean(connectMeans);
                    row.meanJoinLatencyMs = Mean(joinMeans);
                    row.meanSessionStartDelayMs = Mean(sessionDelayMeans);
                    row.meanGrantDelayMs = Mean(grantMeans);
                    row.meanWaitDurationMs = Mean(waitMeans);
                    row.maxWaitDurationMs = Mean(maxWaits);
                    row.transferSuccessRate = Mean(transferSuccessRates);
                    row.meanTransferLatencyMs = Mean(transferLatencyMeans);
                    row.fairnessIndexWait = Mean(fairnessMeans);
                    row.averageFps = Mean(avgFps);
                    row.minFps = Mean(minFps);
                    row.averageFrameTimeMs = Mean(avgFrameTimes);
                    row.finalRotationOutcomeRunCount = finalRotationErrors.Count;
                    row.meanFinalRotationErrorDegrees = Mean(finalRotationErrors);
                    row.meanScenarioDurationMs = Mean(scenarioDurations);
                    row.meanAiDecisionCount = Mean(aiDecisionCounts);
                    row.meanAiFallbackCount = Mean(aiFallbackCounts);
                    row.meanAiLatencyMs = Mean(aiLatencies);

                    rows.Add(row);
                }
            }

            string outputFolder = EvaluationExportPathUtility.GetComparisonSummariesFolder();
            Directory.CreateDirectory(outputFolder);

            string outputPath = Path.Combine(outputFolder, $"backend_comparison_{DateTime.Now:yyyyMMdd_HHmmss}.csv");
            SaveComparisonCsv(outputPath, rows);

            Log($"Saved backend comparison summary to: {outputPath}");
            return outputPath;
        }

        private void SaveComparisonCsv(string filePath, List<BackendComparisonRow> rows)
        {
            StringBuilder sb = new StringBuilder();
            sb.AppendLine("scenario_id,backend,mean_connect_latency_ms,mean_join_latency_ms,mean_session_start_delay_ms,mean_grant_delay_ms,mean_wait_duration_ms,max_wait_duration_ms,transfer_success_rate,mean_transfer_latency_ms,fairness_index_wait,average_fps,min_fps,average_frame_time_ms,mean_scenario_duration_ms,mean_ai_decision_count,mean_ai_fallback_count,mean_ai_latency_ms,run_count,valid_run_count,invalid_run_count,final_rotation_outcome_run_count,mean_final_rotation_error_degrees");

            foreach (BackendComparisonRow row in rows.OrderBy(x => x.scenarioId).ThenBy(x => x.backend))
            {
                sb.AppendLine(
                    $"{Q(row.scenarioId)}," +
                    $"{Q(row.backend)}," +
                    $"{F(row.meanConnectLatencyMs)}," +
                    $"{F(row.meanJoinLatencyMs)}," +
                    $"{F(row.meanSessionStartDelayMs)}," +
                    $"{F(row.meanGrantDelayMs)}," +
                    $"{F(row.meanWaitDurationMs)}," +
                    $"{F(row.maxWaitDurationMs)}," +
                    $"{F(row.transferSuccessRate)}," +
                    $"{F(row.meanTransferLatencyMs)}," +
                    $"{F(row.fairnessIndexWait)}," +
                    $"{F(row.averageFps)}," +
                    $"{F(row.minFps)}," +
                    $"{F(row.averageFrameTimeMs)}," +
                    $"{F(row.meanScenarioDurationMs)}," +
                    $"{F(row.meanAiDecisionCount)}," +
                    $"{F(row.meanAiFallbackCount)}," +
                    $"{F(row.meanAiLatencyMs)}," +
                    $"{row.runCount}," +
                    $"{row.validRunCount}," +
                    $"{row.invalidRunCount}," +
                    $"{row.finalRotationOutcomeRunCount}," +
                    $"{F(row.meanFinalRotationErrorDegrees)}");
            }

            File.WriteAllText(filePath, sb.ToString(), Encoding.UTF8);
        }

        private static bool IsCompleteRunDirectory(string runDirectory)
        {
            if (string.IsNullOrWhiteSpace(runDirectory))
                return false;

            string folderName = Path.GetFileName(runDirectory);
            if (folderName.StartsWith(".pending_", StringComparison.OrdinalIgnoreCase))
                return false;

            bool hasRequiredSummaries =
                File.Exists(Path.Combine(runDirectory, "scenario_metrics_summary.csv")) &&
                File.Exists(Path.Combine(runDirectory, "run_master_summary.csv")) &&
                File.Exists(Path.Combine(runDirectory, "runtime_performance_summary.csv"));
            if (!hasRequiredSummaries)
                return false;

            // New short-form run folders are published file-by-file. The manifest is written last.
            return !folderName.StartsWith("Run_", StringComparison.OrdinalIgnoreCase) ||
                   File.Exists(Path.Combine(runDirectory, "export_manifest.csv"));
        }

        private void ReadSingleValueCsv(string filePath, string columnName, List<double> target)
        {
            if (!File.Exists(filePath))
                return;

            string[] lines = File.ReadAllLines(filePath);
            if (lines.Length < 2)
                return;

            string[] headers = SplitCsvLine(lines[0]);
            string[] values = SplitCsvLine(lines[1]);

            int idx = Array.IndexOf(headers, columnName);
            if (idx < 0 || idx >= values.Length)
                return;

            if (double.TryParse(values[idx].Trim('"'), NumberStyles.Any, CultureInfo.InvariantCulture, out double parsed))
                target.Add(parsed);
        }

        private bool TryReadSingleBoolCsv(string filePath, string columnName, out bool value)
        {
            value = true;

            if (!File.Exists(filePath))
                return false;

            string[] lines = File.ReadAllLines(filePath);
            if (lines.Length < 2)
                return false;

            string[] headers = SplitCsvLine(lines[0]);
            string[] values = SplitCsvLine(lines[1]);

            int idx = Array.IndexOf(headers, columnName);
            if (idx < 0 || idx >= values.Length)
                return false;

            return bool.TryParse(values[idx].Trim('"'), out value);
        }

        private bool IsProtocolCompatible(string runDirectory, string requiredProtocolVersion)
        {
            if (string.IsNullOrWhiteSpace(requiredProtocolVersion))
                return true;

            string metadataPath = Path.Combine(runDirectory, "run_metadata.csv");
            if (!File.Exists(metadataPath))
                return false;

            string[] lines = File.ReadAllLines(metadataPath);
            if (lines.Length < 2)
                return false;

            string[] headers = SplitCsvLine(lines[0]);
            string[] values = SplitCsvLine(lines[1]);
            int index = Array.IndexOf(headers, "protocol_version");
            return index >= 0 &&
                   index < values.Length &&
                   string.Equals(
                       values[index].Trim('"'),
                       requiredProtocolVersion,
                       StringComparison.OrdinalIgnoreCase);
        }

        private string[] SplitCsvLine(string line)
        {
            List<string> result = new List<string>();
            StringBuilder current = new StringBuilder();
            bool inQuotes = false;

            for (int i = 0; i < line.Length; i++)
            {
                char c = line[i];

                if (c == '"')
                {
                    if (inQuotes && i + 1 < line.Length && line[i + 1] == '"')
                    {
                        current.Append('"');
                        i++;
                    }
                    else
                    {
                        inQuotes = !inQuotes;
                    }
                }
                else if (c == ',' && !inQuotes)
                {
                    result.Add(current.ToString());
                    current.Clear();
                }
                else
                {
                    current.Append(c);
                }
            }

            result.Add(current.ToString());
            return result.ToArray();
        }

        private double Mean(List<double> values)
        {
            return values.Count > 0 ? values.Average() : 0d;
        }

        private string Q(string value)
        {
            if (string.IsNullOrEmpty(value))
                return "\"\"";

            return $"\"{value.Replace("\"", "\"\"")}\"";
        }

        private string F(double value)
        {
            return value.ToString("F4", CultureInfo.InvariantCulture);
        }

        private void Log(string message)
        {
            if (!verboseLogs)
                return;

            Debug.Log($"[BackendComparisonBuilder] {message}", this);
        }
    }
}
