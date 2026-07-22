using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using UnityEngine;

namespace Thesis.Orchestration.Telemetry
{
    public class EvaluationSummaryBuilder : MonoBehaviour
    {
        public static EvaluationSummaryBuilder Instance { get; private set; }

        [Serializable]
        public class SessionLifecycleSummary
        {
            public string runId;
            public string backend;
            public string scenarioId;

            public int clientCount;

            public double meanConnectLatencyMs;
            public double medianConnectLatencyMs;
            public double stdDevConnectLatencyMs;

            public double meanCreateLatencyMs;
            public double medianCreateLatencyMs;
            public double stdDevCreateLatencyMs;

            public double meanJoinLatencyMs;
            public double medianJoinLatencyMs;
            public double stdDevJoinLatencyMs;

            public double meanSessionStartVisibleDelayMs;
            public double medianSessionStartVisibleDelayMs;
            public double stdDevSessionStartVisibleDelayMs;
        }

        [Serializable]
        public class QueueMetricsSummary
        {
            public string runId;
            public string backend;
            public string scenarioId;

            public int requestCount;
            public int queuedCount;
            public int grantedCount;
            public int releasedCount;
            public int timeoutReleasedCount;
            public int timeoutGrantedNextCount;
            public int invalidDecisionCount;
            public int removedFromQueueCount;

            public double meanGrantDelayMs;
            public double medianGrantDelayMs;
            public double stdDevGrantDelayMs;

            public double meanWaitDurationMs;
            public double medianWaitDurationMs;
            public double stdDevWaitDurationMs;

            public double maxWaitDurationMs;
        }

        [Serializable]
        public class AuthorityMetricsSummary
        {
            public string runId;
            public string backend;
            public string scenarioId;

            public int transferCount;
            public int successfulTransfers;
            public int failedTransfers;
            public double successRate;

            public double meanTransferLatencyMs;
            public double medianTransferLatencyMs;
            public double stdDevTransferLatencyMs;
        }

        [Serializable]
        public class FairnessSummary
        {
            public string runId;
            public string backend;
            public string scenarioId;

            public int distinctUsers;
            public double meanWaitPerUserMs;
            public double stdDevWaitPerUserMs;
            public double maxUserWaitMs;
            public double jainsFairnessIndexWait;
        }

        [Serializable]
        public class RunMasterSummary
        {
            public string runId;
            public string backend;
            public string scenarioId;

            public int clientCount;

            public double meanConnectLatencyMs;
            public double meanJoinLatencyMs;
            public double meanSessionStartVisibleDelayMs;

            public int requestCount;
            public int queuedCount;
            public int grantedCount;
            public int releasedCount;
            public int timeoutReleasedCount;
            public int timeoutGrantedNextCount;

            public double meanGrantDelayMs;
            public double meanWaitDurationMs;
            public double maxWaitDurationMs;

            public int authorityTransferCount;
            public int authorityTransferSuccessCount;
            public int authorityTransferFailureCount;
            public double authorityTransferSuccessRate;
            public double meanAuthorityTransferLatencyMs;

            public int fairnessDistinctUsers;
            public double fairnessMeanWaitPerUserMs;
            public double fairnessStdDevWaitPerUserMs;
            public double fairnessJainsIndexWait;

            public double averageFps;
            public double minFps;
            public double averageFrameTimeMs;
            public double averageAllocatedMemoryMb;
            public int performanceSampleCount;

            public bool scenarioValid;
            public int scenarioCompletedCycles;
            public int scenarioFailedAssertions;
            public int scenarioFailedValidityAssertions;
            public int scenarioFailedOutcomeAssertions;
            public bool scenarioHasFinalRotationOutcome;
            public bool scenarioFinalRotationConverged;
            public double scenarioFinalRotationErrorDegrees;
            public double scenarioDurationMs;

            public int aiDecisionCount;
            public int aiFallbackCount;
            public double aiAverageLatencyMs;
        }

        [Header("Debug")]
        [SerializeField] private bool verboseLogs = true;

        public SessionLifecycleSummary BuildSessionLifecycleSummary()
        {
            SessionLifecycleTracker tracker = SessionLifecycleTracker.Instance;
            Thesis.Orchestration.Analytics.UsageTimingTracker usageTracker = Thesis.Orchestration.Analytics.UsageTimingTracker.Instance;
            var context = EvaluationRunContext.Instance;

            SessionLifecycleSummary summary = new SessionLifecycleSummary
            {
                runId = context != null ? context.RunId : "UnknownRun",
                backend = context != null ? context.BackendName : "UnknownBackend",
                scenarioId = context != null ? context.ScenarioId : "UnknownScenario"
            };

            if (tracker == null || tracker.Rows == null || tracker.Rows.Count == 0)
            {
                summary.clientCount = usageTracker != null ? usageTracker.UserStats.Count : 0;
                return summary;
            }

            var rows = tracker.Rows.ToList();
            int observedUserCount = usageTracker != null ? usageTracker.UserStats.Count : 0;
            summary.clientCount = Math.Max(rows.Count, observedUserCount);

            FillStats(rows.Select(x => x.connectLatencyMs).Where(x => x > 0), out summary.meanConnectLatencyMs, out summary.medianConnectLatencyMs, out summary.stdDevConnectLatencyMs);
            FillStats(rows.Select(x => x.createLatencyMs).Where(x => x > 0), out summary.meanCreateLatencyMs, out summary.medianCreateLatencyMs, out summary.stdDevCreateLatencyMs);
            FillStats(rows.Select(x => x.joinLatencyMs).Where(x => x > 0), out summary.meanJoinLatencyMs, out summary.medianJoinLatencyMs, out summary.stdDevJoinLatencyMs);
            FillStats(rows.Select(x => x.sessionStartVisibleDelayMs).Where(x => x > 0), out summary.meanSessionStartVisibleDelayMs, out summary.medianSessionStartVisibleDelayMs, out summary.stdDevSessionStartVisibleDelayMs);

            return summary;
        }

        public RunMasterSummary BuildRunMasterSummary()
        {
            SessionLifecycleSummary session = BuildSessionLifecycleSummary();
            QueueMetricsSummary queue = BuildQueueMetricsSummary();
            AuthorityMetricsSummary auth = BuildAuthorityMetricsSummary();
            FairnessSummary fairness = BuildFairnessSummary();
            RuntimePerformanceSampler.RuntimePerformanceSummary perf =
                RuntimePerformanceSampler.Instance != null
                    ? RuntimePerformanceSampler.Instance.BuildSummary()
                    : new RuntimePerformanceSampler.RuntimePerformanceSummary();
            ScenarioMetricsRecorder.ScenarioMetricsSummary scenario =
                ScenarioMetricsRecorder.Instance != null
                    ? ScenarioMetricsRecorder.Instance.BuildSummary()
                    : new ScenarioMetricsRecorder.ScenarioMetricsSummary { scenarioValid = true };
            AiDecisionRecorder.AiMetricsSummary ai =
                AiDecisionRecorder.Instance != null
                    ? AiDecisionRecorder.Instance.BuildSummary()
                    : new AiDecisionRecorder.AiMetricsSummary();

            return new RunMasterSummary
            {
                runId = session.runId,
                backend = session.backend,
                scenarioId = session.scenarioId,

                clientCount = session.clientCount,

                meanConnectLatencyMs = session.meanConnectLatencyMs,
                meanJoinLatencyMs = session.meanJoinLatencyMs,
                meanSessionStartVisibleDelayMs = session.meanSessionStartVisibleDelayMs,

                requestCount = queue.requestCount,
                queuedCount = queue.queuedCount,
                grantedCount = queue.grantedCount,
                releasedCount = queue.releasedCount,
                timeoutReleasedCount = queue.timeoutReleasedCount,
                timeoutGrantedNextCount = queue.timeoutGrantedNextCount,

                meanGrantDelayMs = queue.meanGrantDelayMs,
                meanWaitDurationMs = queue.meanWaitDurationMs,
                maxWaitDurationMs = queue.maxWaitDurationMs,

                authorityTransferCount = auth.transferCount,
                authorityTransferSuccessCount = auth.successfulTransfers,
                authorityTransferFailureCount = auth.failedTransfers,
                authorityTransferSuccessRate = auth.successRate,
                meanAuthorityTransferLatencyMs = auth.meanTransferLatencyMs,

                fairnessDistinctUsers = fairness.distinctUsers,
                fairnessMeanWaitPerUserMs = fairness.meanWaitPerUserMs,
                fairnessStdDevWaitPerUserMs = fairness.stdDevWaitPerUserMs,
                fairnessJainsIndexWait = fairness.jainsFairnessIndexWait,

                averageFps = perf.averageFps,
                minFps = perf.minFps,
                averageFrameTimeMs = perf.averageFrameTimeMs,
                averageAllocatedMemoryMb = perf.averageTotalAllocatedMemoryMb,
                performanceSampleCount = perf.sampleCount,

                scenarioValid = scenario.scenarioValid,
                scenarioCompletedCycles = scenario.completedCycles,
                scenarioFailedAssertions = scenario.failedAssertionCount,
                scenarioFailedValidityAssertions = scenario.failedValidityAssertionCount,
                scenarioFailedOutcomeAssertions = scenario.failedOutcomeAssertionCount,
                scenarioHasFinalRotationOutcome = scenario.hasFinalRotationOutcome,
                scenarioFinalRotationConverged = scenario.finalRotationConverged,
                scenarioFinalRotationErrorDegrees = scenario.finalRotationErrorDegrees,
                scenarioDurationMs = scenario.totalDurationMs,

                aiDecisionCount = ai.decisionCount,
                aiFallbackCount = ai.fallbackCount,
                aiAverageLatencyMs = ai.averageLatencyMs
            };
        }

        public void SaveRunMasterSummaryCsv(string filePath, RunMasterSummary s)
        {
            StringBuilder sb = new StringBuilder();
            sb.AppendLine("run_id,backend,scenario_id,client_count,mean_connect_latency_ms,mean_join_latency_ms,mean_session_start_visible_delay_ms,request_count,queued_count,granted_count,released_count,timeout_released_count,timeout_granted_next_count,mean_grant_delay_ms,mean_wait_duration_ms,max_wait_duration_ms,authority_transfer_count,authority_transfer_success_count,authority_transfer_failure_count,authority_transfer_success_rate,mean_authority_transfer_latency_ms,fairness_distinct_users,fairness_mean_wait_per_user_ms,fairness_stddev_wait_per_user_ms,fairness_jains_index_wait,average_fps,min_fps,average_frame_time_ms,average_allocated_memory_mb,performance_sample_count,scenario_valid,scenario_completed_cycles,scenario_failed_assertions,scenario_duration_ms,ai_decision_count,ai_fallback_count,ai_average_latency_ms,scenario_failed_validity_assertions,scenario_failed_outcome_assertions,scenario_has_final_rotation_outcome,scenario_final_rotation_converged,scenario_final_rotation_error_degrees");

            sb.AppendLine(
                $"{Q(s.runId)},{Q(s.backend)},{Q(s.scenarioId)},{s.clientCount},{F(s.meanConnectLatencyMs)},{F(s.meanJoinLatencyMs)},{F(s.meanSessionStartVisibleDelayMs)},{s.requestCount},{s.queuedCount},{s.grantedCount},{s.releasedCount},{s.timeoutReleasedCount},{s.timeoutGrantedNextCount},{F(s.meanGrantDelayMs)},{F(s.meanWaitDurationMs)},{F(s.maxWaitDurationMs)},{s.authorityTransferCount},{s.authorityTransferSuccessCount},{s.authorityTransferFailureCount},{F(s.authorityTransferSuccessRate)},{F(s.meanAuthorityTransferLatencyMs)},{s.fairnessDistinctUsers},{F(s.fairnessMeanWaitPerUserMs)},{F(s.fairnessStdDevWaitPerUserMs)},{F(s.fairnessJainsIndexWait)},{F(s.averageFps)},{F(s.minFps)},{F(s.averageFrameTimeMs)},{F(s.averageAllocatedMemoryMb)},{s.performanceSampleCount},{s.scenarioValid},{s.scenarioCompletedCycles},{s.scenarioFailedAssertions},{F(s.scenarioDurationMs)},{s.aiDecisionCount},{s.aiFallbackCount},{F(s.aiAverageLatencyMs)},{s.scenarioFailedValidityAssertions},{s.scenarioFailedOutcomeAssertions},{s.scenarioHasFinalRotationOutcome},{s.scenarioFinalRotationConverged},{F(s.scenarioFinalRotationErrorDegrees)}");

            File.WriteAllText(filePath, sb.ToString(), Encoding.UTF8);
        }

        public QueueMetricsSummary BuildQueueMetricsSummary()
        {
            QueueTransitionRecorder recorder = QueueTransitionRecorder.Instance;
            var context = EvaluationRunContext.Instance;

            QueueMetricsSummary summary = new QueueMetricsSummary
            {
                runId = context != null ? context.RunId : "UnknownRun",
                backend = context != null ? context.BackendName : "UnknownBackend",
                scenarioId = context != null ? context.ScenarioId : "UnknownScenario"
            };

            if (recorder == null || recorder.Rows == null || recorder.Rows.Count == 0)
                return summary;

            var rows = recorder.Rows.ToList();

            summary.requestCount = rows.Count(x => x.eventType == "AccessRequested");
            summary.queuedCount = rows.Count(x => x.eventType == "AccessQueued");
            summary.grantedCount = rows.Count(x => x.eventType == "AccessGranted");
            summary.releasedCount = rows.Count(x => x.eventType == "AccessReleased");
            summary.timeoutReleasedCount = rows.Count(x => x.eventType == "TimeoutReleased");
            summary.timeoutGrantedNextCount = rows.Count(x => x.eventType == "TimeoutGrantedNext");
            summary.invalidDecisionCount = rows.Count(x => x.eventType == "QueueDecisionInvalid");
            summary.removedFromQueueCount = rows.Count(x => x.eventType == "RemovedFromQueue");

            var grantDelays = rows.Select(x => x.grantDelayMs).Where(x => x > 0).ToList();
            var waitDurations = rows.Select(x => x.waitDurationMs).Where(x => x > 0).ToList();

            FillStats(grantDelays, out summary.meanGrantDelayMs, out summary.medianGrantDelayMs, out summary.stdDevGrantDelayMs);
            FillStats(waitDurations, out summary.meanWaitDurationMs, out summary.medianWaitDurationMs, out summary.stdDevWaitDurationMs);
            summary.maxWaitDurationMs = waitDurations.Count > 0 ? waitDurations.Max() : 0d;

            return summary;
        }

        public AuthorityMetricsSummary BuildAuthorityMetricsSummary()
        {
            AuthorityTransferRecorder recorder = AuthorityTransferRecorder.Instance;
            var context = EvaluationRunContext.Instance;

            AuthorityMetricsSummary summary = new AuthorityMetricsSummary
            {
                runId = context != null ? context.RunId : "UnknownRun",
                backend = context != null ? context.BackendName : "UnknownBackend",
                scenarioId = context != null ? context.ScenarioId : "UnknownScenario"
            };

            if (recorder == null || recorder.Rows == null || recorder.Rows.Count == 0)
                return summary;

            var rows = recorder.Rows.ToList();
            summary.transferCount = rows.Count;
            summary.successfulTransfers = rows.Count(x => x.transferSuccess);
            summary.failedTransfers = rows.Count(x => !x.transferSuccess);
            summary.successRate = summary.transferCount > 0
                ? (double)summary.successfulTransfers / summary.transferCount
                : 0d;

            FillStats(rows.Select(x => x.transferLatencyMs).Where(x => x > 0), out summary.meanTransferLatencyMs, out summary.medianTransferLatencyMs, out summary.stdDevTransferLatencyMs);

            return summary;
        }

        public FairnessSummary BuildFairnessSummary()
        {
            QueueTransitionRecorder recorder = QueueTransitionRecorder.Instance;
            var context = EvaluationRunContext.Instance;

            FairnessSummary summary = new FairnessSummary
            {
                runId = context != null ? context.RunId : "UnknownRun",
                backend = context != null ? context.BackendName : "UnknownBackend",
                scenarioId = context != null ? context.ScenarioId : "UnknownScenario"
            };

            if (recorder == null || recorder.Rows == null || recorder.Rows.Count == 0)
                return summary;

            var grantedRows = recorder.Rows
                .Where(x => (x.eventType == "AccessGranted" || x.eventType == "TimeoutGrantedNext") &&
                            !string.IsNullOrWhiteSpace(x.requesterUserId) &&
                            x.waitDurationMs > 0)
                .ToList();

            var perUserWaits = grantedRows
                .GroupBy(x => x.requesterUserId)
                .Select(g => g.Average(x => x.waitDurationMs))
                .ToList();

            summary.distinctUsers = perUserWaits.Count;

            FillStats(perUserWaits, out summary.meanWaitPerUserMs, out _, out summary.stdDevWaitPerUserMs);
            summary.maxUserWaitMs = perUserWaits.Count > 0 ? perUserWaits.Max() : 0d;
            summary.jainsFairnessIndexWait = ComputeJainsFairness(perUserWaits);

            return summary;
        }

        public void SaveAllSummariesToFolder(string folderPath)
        {
            Directory.CreateDirectory(folderPath);

            SaveRunMasterSummaryCsv(Path.Combine(folderPath, "run_master_summary.csv"), BuildRunMasterSummary());
            SaveSessionLifecycleSummaryCsv(Path.Combine(folderPath, "session_lifecycle_summary.csv"), BuildSessionLifecycleSummary());
            SaveQueueMetricsSummaryCsv(Path.Combine(folderPath, "queue_metrics_summary.csv"), BuildQueueMetricsSummary());
            SaveAuthorityMetricsSummaryCsv(Path.Combine(folderPath, "authority_metrics_summary.csv"), BuildAuthorityMetricsSummary());
            SaveFairnessSummaryCsv(Path.Combine(folderPath, "fairness_summary.csv"), BuildFairnessSummary());

            Log($"Saved all per-run summaries to: {folderPath}");
        }

        public void SaveSessionLifecycleSummaryCsv(string filePath, SessionLifecycleSummary s)
        {
            StringBuilder sb = new StringBuilder();
            sb.AppendLine("run_id,backend,scenario_id,client_count,mean_connect_latency_ms,median_connect_latency_ms,stddev_connect_latency_ms,mean_create_latency_ms,median_create_latency_ms,stddev_create_latency_ms,mean_join_latency_ms,median_join_latency_ms,stddev_join_latency_ms,mean_session_start_visible_delay_ms,median_session_start_visible_delay_ms,stddev_session_start_visible_delay_ms");
            sb.AppendLine($"{Q(s.runId)},{Q(s.backend)},{Q(s.scenarioId)},{s.clientCount},{F(s.meanConnectLatencyMs)},{F(s.medianConnectLatencyMs)},{F(s.stdDevConnectLatencyMs)},{F(s.meanCreateLatencyMs)},{F(s.medianCreateLatencyMs)},{F(s.stdDevCreateLatencyMs)},{F(s.meanJoinLatencyMs)},{F(s.medianJoinLatencyMs)},{F(s.stdDevJoinLatencyMs)},{F(s.meanSessionStartVisibleDelayMs)},{F(s.medianSessionStartVisibleDelayMs)},{F(s.stdDevSessionStartVisibleDelayMs)}");
            File.WriteAllText(filePath, sb.ToString(), Encoding.UTF8);
        }

        public void SaveQueueMetricsSummaryCsv(string filePath, QueueMetricsSummary s)
        {
            StringBuilder sb = new StringBuilder();
            sb.AppendLine("run_id,backend,scenario_id,request_count,queued_count,granted_count,released_count,timeout_released_count,timeout_granted_next_count,invalid_decision_count,removed_from_queue_count,mean_grant_delay_ms,median_grant_delay_ms,stddev_grant_delay_ms,mean_wait_duration_ms,median_wait_duration_ms,stddev_wait_duration_ms,max_wait_duration_ms");
            sb.AppendLine($"{Q(s.runId)},{Q(s.backend)},{Q(s.scenarioId)},{s.requestCount},{s.queuedCount},{s.grantedCount},{s.releasedCount},{s.timeoutReleasedCount},{s.timeoutGrantedNextCount},{s.invalidDecisionCount},{s.removedFromQueueCount},{F(s.meanGrantDelayMs)},{F(s.medianGrantDelayMs)},{F(s.stdDevGrantDelayMs)},{F(s.meanWaitDurationMs)},{F(s.medianWaitDurationMs)},{F(s.stdDevWaitDurationMs)},{F(s.maxWaitDurationMs)}");
            File.WriteAllText(filePath, sb.ToString(), Encoding.UTF8);
        }

        public void SaveAuthorityMetricsSummaryCsv(string filePath, AuthorityMetricsSummary s)
        {
            StringBuilder sb = new StringBuilder();
            sb.AppendLine("run_id,backend,scenario_id,transfer_count,successful_transfers,failed_transfers,success_rate,mean_transfer_latency_ms,median_transfer_latency_ms,stddev_transfer_latency_ms");
            sb.AppendLine($"{Q(s.runId)},{Q(s.backend)},{Q(s.scenarioId)},{s.transferCount},{s.successfulTransfers},{s.failedTransfers},{F(s.successRate)},{F(s.meanTransferLatencyMs)},{F(s.medianTransferLatencyMs)},{F(s.stdDevTransferLatencyMs)}");
            File.WriteAllText(filePath, sb.ToString(), Encoding.UTF8);
        }

        public void SaveFairnessSummaryCsv(string filePath, FairnessSummary s)
        {
            StringBuilder sb = new StringBuilder();
            sb.AppendLine("run_id,backend,scenario_id,distinct_users,mean_wait_per_user_ms,stddev_wait_per_user_ms,max_user_wait_ms,jains_fairness_index_wait");
            sb.AppendLine($"{Q(s.runId)},{Q(s.backend)},{Q(s.scenarioId)},{s.distinctUsers},{F(s.meanWaitPerUserMs)},{F(s.stdDevWaitPerUserMs)},{F(s.maxUserWaitMs)},{F(s.jainsFairnessIndexWait)}");
            File.WriteAllText(filePath, sb.ToString(), Encoding.UTF8);
        }

        private void FillStats(IEnumerable<double> source, out double mean, out double median, out double stdDev)
        {
            List<double> values = source
                .Where(x => !double.IsNaN(x) && !double.IsInfinity(x))
                .OrderBy(x => x)
                .ToList();

            if (values.Count == 0)
            {
                mean = 0d;
                median = 0d;
                stdDev = 0d;
                return;
            }

            double meanValue = values.Average();

            int n = values.Count;
            double medianValue = n % 2 == 1
                ? values[n / 2]
                : (values[(n / 2) - 1] + values[n / 2]) / 2d;

            double variance = values.Sum(v => Math.Pow(v - meanValue, 2)) / values.Count;
            double stdDevValue = Math.Sqrt(variance);

            mean = meanValue;
            median = medianValue;
            stdDev = stdDevValue;
        }

        private double ComputeJainsFairness(List<double> values)
        {
            if (values == null || values.Count == 0)
                return 0d;

            double sum = values.Sum();
            double sumSquares = values.Sum(v => v * v);

            if (sumSquares <= 0d)
                return 0d;

            return (sum * sum) / (values.Count * sumSquares);
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

            Debug.Log($"[EvaluationSummaryBuilder] {message}", this);
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
    }
}
