using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using Thesis.Orchestration.AI;
using UnityEngine;

namespace Thesis.Orchestration.Telemetry
{
    public class AiDecisionRecorder : MonoBehaviour
    {
        public static AiDecisionRecorder Instance { get; private set; }

        [Serializable]
        public class AiDecisionRow
        {
            public string runId;
            public string backend;
            public string scenarioId;
            public string timestampUtc;
            public string mode;
            public string provider;
            public string trigger;
            public string instrumentId;
            public string requestingUserId;
            public string currentHolderUserId;
            public string queueSnapshot;
            public string baselineDecisionType;
            public string baselineGrantedUserId;
            public string aiAction;
            public string recommendedUserId;
            public float confidence;
            public double latencyMs;
            public bool valid;
            public bool fallbackUsed;
            public bool acceptedForControl;
            public string explanation;
        }

        [Serializable]
        public class AiMetricsSummary
        {
            public string runId;
            public string backend;
            public string scenarioId;
            public int decisionCount;
            public int validDecisionCount;
            public int fallbackCount;
            public int acceptedForControlCount;
            public double averageLatencyMs;
            public double maxLatencyMs;
        }

        [Header("Debug")]
        [SerializeField] private bool verboseLogs = true;

        private readonly List<AiDecisionRow> rows = new();

        public IReadOnlyList<AiDecisionRow> Rows => rows;

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

        public void ClearAll()
        {
            rows.Clear();
        }

        public void RecordDecision(
            OrchestrationAiStateSnapshot snapshot,
            OrchestrationAiDecision decision,
            double latencyMs,
            bool acceptedForControl)
        {
            if (snapshot == null)
                return;

            decision ??= OrchestrationAiDecision.Fallback(snapshot.provider, snapshot.mode, "Decision was null.");

            AiDecisionRow row = new AiDecisionRow
            {
                runId = GetRunId(),
                backend = GetBackend(),
                scenarioId = GetScenarioId(),
                timestampUtc = DateTime.UtcNow.ToString("o"),
                mode = snapshot.mode,
                provider = decision.provider,
                trigger = snapshot.trigger,
                instrumentId = snapshot.instrumentId,
                requestingUserId = snapshot.requestingUserId,
                currentHolderUserId = snapshot.currentHolderUserId,
                queueSnapshot = snapshot.queueSnapshot,
                baselineDecisionType = snapshot.baselineDecisionType,
                baselineGrantedUserId = snapshot.baselineGrantedUserId,
                aiAction = decision.action,
                recommendedUserId = decision.recommendedUserId,
                confidence = decision.confidence,
                latencyMs = latencyMs,
                valid = decision.valid,
                fallbackUsed = decision.fallbackUsed,
                acceptedForControl = acceptedForControl,
                explanation = decision.explanation
            };

            rows.Add(row);
            Log($"{row.provider} {row.aiAction} -> {row.recommendedUserId} valid={row.valid} fallback={row.fallbackUsed}");
        }

        public AiMetricsSummary BuildSummary()
        {
            List<double> latencies = rows.Select(x => x.latencyMs).Where(x => x >= 0d).ToList();
            return new AiMetricsSummary
            {
                runId = GetRunId(),
                backend = GetBackend(),
                scenarioId = GetScenarioId(),
                decisionCount = rows.Count,
                validDecisionCount = rows.Count(x => x.valid),
                fallbackCount = rows.Count(x => x.fallbackUsed),
                acceptedForControlCount = rows.Count(x => x.acceptedForControl),
                averageLatencyMs = latencies.Count > 0 ? latencies.Average() : 0d,
                maxLatencyMs = latencies.Count > 0 ? latencies.Max() : 0d
            };
        }

        public void SaveAiDecisionsCsvToFile(string filePath)
        {
            EnsureDirectory(filePath);
            StringBuilder sb = new StringBuilder();
            sb.AppendLine("run_id,backend,scenario_id,timestamp_utc,mode,provider,trigger,instrument_id,requesting_user_id,current_holder_user_id,queue_snapshot,baseline_decision_type,baseline_granted_user_id,ai_action,recommended_user_id,confidence,latency_ms,valid,fallback_used,accepted_for_control,explanation");

            foreach (AiDecisionRow row in rows)
            {
                sb.AppendLine(
                    $"{Q(row.runId)},{Q(row.backend)},{Q(row.scenarioId)},{Q(row.timestampUtc)},{Q(row.mode)},{Q(row.provider)},{Q(row.trigger)},{Q(row.instrumentId)},{Q(row.requestingUserId)},{Q(row.currentHolderUserId)},{Q(row.queueSnapshot)},{Q(row.baselineDecisionType)},{Q(row.baselineGrantedUserId)},{Q(row.aiAction)},{Q(row.recommendedUserId)},{F(row.confidence)},{F(row.latencyMs)},{row.valid},{row.fallbackUsed},{row.acceptedForControl},{Q(row.explanation)}");
            }

            File.WriteAllText(filePath, sb.ToString(), Encoding.UTF8);
        }

        public void SaveAiMetricsSummaryCsvToFile(string filePath)
        {
            EnsureDirectory(filePath);
            AiMetricsSummary s = BuildSummary();
            StringBuilder sb = new StringBuilder();
            sb.AppendLine("run_id,backend,scenario_id,decision_count,valid_decision_count,fallback_count,accepted_for_control_count,average_latency_ms,max_latency_ms");
            sb.AppendLine($"{Q(s.runId)},{Q(s.backend)},{Q(s.scenarioId)},{s.decisionCount},{s.validDecisionCount},{s.fallbackCount},{s.acceptedForControlCount},{F(s.averageLatencyMs)},{F(s.maxLatencyMs)}");
            File.WriteAllText(filePath, sb.ToString(), Encoding.UTF8);
        }

        private void EnsureDirectory(string filePath)
        {
            string directory = Path.GetDirectoryName(filePath);
            if (!string.IsNullOrWhiteSpace(directory) && !Directory.Exists(directory))
                Directory.CreateDirectory(directory);
        }

        private string GetRunId()
        {
            return EvaluationRunContext.Instance != null ? EvaluationRunContext.Instance.RunId : "UnknownRun";
        }

        private string GetBackend()
        {
            return EvaluationRunContext.Instance != null ? EvaluationRunContext.Instance.BackendName : "UnknownBackend";
        }

        private string GetScenarioId()
        {
            return EvaluationRunContext.Instance != null ? EvaluationRunContext.Instance.ScenarioId : "UnknownScenario";
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

            Debug.Log($"[AiDecisionRecorder] {message}", this);
        }
    }
}
