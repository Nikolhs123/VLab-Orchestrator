using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEngine;

namespace Thesis.Orchestration.Telemetry
{
    public class QueueTransitionRecorder : MonoBehaviour
    {
        public static QueueTransitionRecorder Instance { get; private set; }

        [Serializable]
        public class QueueTransitionRow
        {
            public string runId;
            public string backend;
            public string scenarioId;

            public string timestampUtc;
            public string eventType;
            public string instrumentId;

            public string requesterUserId;
            public string holderBefore;
            public string holderAfter;

            public string queueSnapshot;
            public int queueLength;
            public int queuePosition;

            public double waitDurationMs;
            public double grantDelayMs;
            public string details;
        }

        [Header("Debug")]
        [SerializeField] private bool verboseLogs = true;

        private readonly List<QueueTransitionRow> rows = new();
        private readonly Dictionary<string, DateTime> requestStartByUserAndInstrument = new();

        public IReadOnlyList<QueueTransitionRow> Rows => rows;

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
            requestStartByUserAndInstrument.Clear();
        }

        public void RecordRequest(
            string userId,
            string instrumentId,
            string holderBefore,
            IEnumerable<string> queueSnapshot,
            string details = "")
        {
            if (!string.IsNullOrWhiteSpace(userId) && !string.IsNullOrWhiteSpace(instrumentId))
            {
                requestStartByUserAndInstrument[BuildKey(userId, instrumentId)] = DateTime.UtcNow;
            }

            AddRow(new QueueTransitionRow
            {
                runId = GetRunId(),
                backend = GetBackend(),
                scenarioId = GetScenarioId(),
                timestampUtc = DateTime.UtcNow.ToString("o"),
                eventType = "AccessRequested",
                instrumentId = instrumentId,
                requesterUserId = userId,
                holderBefore = Normalize(holderBefore),
                holderAfter = Normalize(holderBefore),
                queueSnapshot = JoinQueue(queueSnapshot),
                queueLength = CountQueue(queueSnapshot),
                queuePosition = -1,
                waitDurationMs = 0d,
                grantDelayMs = 0d,
                details = details
            });
        }

        public void RecordDecision(
            string eventType,
            string userId,
            string instrumentId,
            string holderBefore,
            string holderAfter,
            IEnumerable<string> queueSnapshot,
            int queuePosition,
            string details = "")
        {
            double waitMs = 0d;
            double grantDelayMs = 0d;

            string key = BuildKey(userId, instrumentId);

            // Defensive: if a backend forgets to record an explicit AccessRequested row
            // but we do record that the user entered the queue, seed the start time here
            // so wait/grant delays won't be zero.
            if (eventType == "AccessQueued" && !string.IsNullOrWhiteSpace(userId) && !string.IsNullOrWhiteSpace(instrumentId))
            {
                if (!requestStartByUserAndInstrument.ContainsKey(key))
                    requestStartByUserAndInstrument[key] = DateTime.UtcNow;
            }

            if (eventType == "AccessGranted" ||
                eventType == "TimeoutGrantedNext")
            {
                if (requestStartByUserAndInstrument.TryGetValue(key, out DateTime start))
                {
                    grantDelayMs = (DateTime.UtcNow - start).TotalMilliseconds;
                    waitMs = grantDelayMs;

                    // Terminal for the request/wait interval.
                    requestStartByUserAndInstrument.Remove(key);
                }
            }
            else if (eventType == "AccessDenied" || eventType == "RemovedFromQueue" || eventType == "QueueDecisionInvalid")
            {
                // Terminal / reset for this request attempt.
                requestStartByUserAndInstrument.Remove(key);
            }

            AddRow(new QueueTransitionRow
            {
                runId = GetRunId(),
                backend = GetBackend(),
                scenarioId = GetScenarioId(),
                timestampUtc = DateTime.UtcNow.ToString("o"),
                eventType = eventType,
                instrumentId = instrumentId,
                requesterUserId = userId,
                holderBefore = Normalize(holderBefore),
                holderAfter = Normalize(holderAfter),
                queueSnapshot = JoinQueue(queueSnapshot),
                queueLength = CountQueue(queueSnapshot),
                queuePosition = queuePosition,
                waitDurationMs = waitMs,
                grantDelayMs = grantDelayMs,
                details = details
            });
        }

        public void SaveQueueTransitionsCsvToFile(string filePath)
        {
            string directory = Path.GetDirectoryName(filePath);
            if (!string.IsNullOrWhiteSpace(directory) && !Directory.Exists(directory))
                Directory.CreateDirectory(directory);

            StringBuilder sb = new StringBuilder();
            sb.AppendLine("run_id,backend,scenario_id,timestamp_utc,event_type,instrument_id,requester_user_id,holder_before,holder_after,queue_snapshot,queue_length,queue_position,wait_duration_ms,grant_delay_ms,details");

            foreach (QueueTransitionRow row in rows)
            {
                sb.AppendLine(
                    $"{Escape(row.runId)}," +
                    $"{Escape(row.backend)}," +
                    $"{Escape(row.scenarioId)}," +
                    $"{Escape(row.timestampUtc)}," +
                    $"{Escape(row.eventType)}," +
                    $"{Escape(row.instrumentId)}," +
                    $"{Escape(row.requesterUserId)}," +
                    $"{Escape(row.holderBefore)}," +
                    $"{Escape(row.holderAfter)}," +
                    $"{Escape(row.queueSnapshot)}," +
                    $"{row.queueLength}," +
                    $"{row.queuePosition}," +
                    $"{row.waitDurationMs}," +
                    $"{row.grantDelayMs}," +
                    $"{Escape(row.details)}");
            }

            File.WriteAllText(filePath, sb.ToString(), Encoding.UTF8);
        }

        private void AddRow(QueueTransitionRow row)
        {
            rows.Add(row);
            Log($"{row.eventType} | requester={row.requesterUserId} | holderBefore={row.holderBefore} | holderAfter={row.holderAfter}");
        }

        private string BuildKey(string userId, string instrumentId)
        {
            return $"{userId}::{instrumentId}";
        }

        private string JoinQueue(IEnumerable<string> queueSnapshot)
        {
            if (queueSnapshot == null)
                return string.Empty;

            return string.Join("||", queueSnapshot);
        }

        private int CountQueue(IEnumerable<string> queueSnapshot)
        {
            return queueSnapshot?.Count() ?? 0;
        }

        private string Normalize(string value)
        {
            return string.IsNullOrWhiteSpace(value) ? "None" : value;
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

        private string Escape(string value)
        {
            if (string.IsNullOrEmpty(value))
                return "";

            return $"\"{value.Replace("\"", "\"\"")}\"";
        }

        private void Log(string message)
        {
            if (!verboseLogs)
                return;

            Debug.Log($"[QueueTransitionRecorder] {message}", this);
        }
    }
}
