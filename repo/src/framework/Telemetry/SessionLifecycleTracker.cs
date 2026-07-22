using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEngine;

namespace Thesis.Orchestration.Telemetry
{
    public class SessionLifecycleTracker : MonoBehaviour
    {
        public static SessionLifecycleTracker Instance { get; private set; }

        [Serializable]
        public class SessionLifecycleRow
        {
            public string runId;
            public string backend;
            public string scenarioId;
            public string clientId;

            public string connectStartUtc;
            public string connectSuccessUtc;

            public string createStartUtc;
            public string createSuccessUtc;

            public string joinStartUtc;
            public string joinSuccessUtc;

            public string sessionStartPressedUtc;
            public string sessionStartVisibleUtc;

            public double connectLatencyMs;
            public double createLatencyMs;
            public double joinLatencyMs;
            public double sessionStartVisibleDelayMs;
        }

        [Header("Debug")]
        [SerializeField] private bool verboseLogs = true;

        private readonly Dictionary<string, SessionLifecycleRow> rowsByClientId = new();
        private string globalSessionStartPressedUtc = string.Empty;

        public IReadOnlyCollection<SessionLifecycleRow> Rows => rowsByClientId.Values;

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

        private SessionLifecycleRow GetOrCreateRow(string clientId)
        {
            if (string.IsNullOrWhiteSpace(clientId))
                clientId = "UnknownClient";

            if (!rowsByClientId.TryGetValue(clientId, out SessionLifecycleRow row))
            {
                row = new SessionLifecycleRow
                {
                    runId = EvaluationRunContext.Instance != null ? EvaluationRunContext.Instance.RunId : "UnknownRun",
                    backend = EvaluationRunContext.Instance != null ? EvaluationRunContext.Instance.BackendName : "UnknownBackend",
                    scenarioId = EvaluationRunContext.Instance != null ? EvaluationRunContext.Instance.ScenarioId : "UnknownScenario",
                    clientId = clientId,
                    sessionStartPressedUtc = globalSessionStartPressedUtc
                };

                rowsByClientId[clientId] = row;
            }

            return row;
        }

        public void EnsureClientObserved(string clientId)
        {
            if (string.IsNullOrWhiteSpace(clientId))
                return;

            GetOrCreateRow(clientId);
        }

        public void EnsureClientsObserved(IEnumerable<string> clientIds)
        {
            if (clientIds == null)
                return;

            foreach (string clientId in clientIds)
                EnsureClientObserved(clientId);
        }

        public void MergeClientRow(string fromClientId, string toClientId)
        {
            if (string.IsNullOrWhiteSpace(fromClientId) ||
                string.IsNullOrWhiteSpace(toClientId) ||
                fromClientId == toClientId)
            {
                return;
            }

            if (!rowsByClientId.TryGetValue(fromClientId, out SessionLifecycleRow fromRow))
                return;

            SessionLifecycleRow toRow = GetOrCreateRow(toClientId);

            if (string.IsNullOrWhiteSpace(toRow.connectStartUtc)) toRow.connectStartUtc = fromRow.connectStartUtc;
            if (string.IsNullOrWhiteSpace(toRow.connectSuccessUtc)) toRow.connectSuccessUtc = fromRow.connectSuccessUtc;
            if (string.IsNullOrWhiteSpace(toRow.createStartUtc)) toRow.createStartUtc = fromRow.createStartUtc;
            if (string.IsNullOrWhiteSpace(toRow.createSuccessUtc)) toRow.createSuccessUtc = fromRow.createSuccessUtc;
            if (string.IsNullOrWhiteSpace(toRow.joinStartUtc)) toRow.joinStartUtc = fromRow.joinStartUtc;
            if (string.IsNullOrWhiteSpace(toRow.joinSuccessUtc)) toRow.joinSuccessUtc = fromRow.joinSuccessUtc;
            if (string.IsNullOrWhiteSpace(toRow.sessionStartPressedUtc)) toRow.sessionStartPressedUtc = fromRow.sessionStartPressedUtc;
            if (string.IsNullOrWhiteSpace(toRow.sessionStartVisibleUtc)) toRow.sessionStartVisibleUtc = fromRow.sessionStartVisibleUtc;

            if (toRow.connectLatencyMs <= 0d) toRow.connectLatencyMs = fromRow.connectLatencyMs;
            if (toRow.createLatencyMs <= 0d) toRow.createLatencyMs = fromRow.createLatencyMs;
            if (toRow.joinLatencyMs <= 0d) toRow.joinLatencyMs = fromRow.joinLatencyMs;
            if (toRow.sessionStartVisibleDelayMs <= 0d) toRow.sessionStartVisibleDelayMs = fromRow.sessionStartVisibleDelayMs;
            RefreshComputedLatencies(toRow);

            rowsByClientId.Remove(fromClientId);

            Log($"Merged lifecycle row '{fromClientId}' into '{toClientId}'");
        }

        public void MergePlaceholderRowsInto(string toClientId)
        {
            if (string.IsNullOrWhiteSpace(toClientId) || IsPlaceholderClientId(toClientId))
                return;

            List<string> placeholderClientIds = rowsByClientId.Keys
                .Where(IsPlaceholderClientId)
                .ToList();

            foreach (string placeholderClientId in placeholderClientIds)
                MergeClientRow(placeholderClientId, toClientId);
        }

        public string BuildBestRowJson(string preferredClientId)
        {
            SessionLifecycleRow row = null;

            if (!string.IsNullOrWhiteSpace(preferredClientId))
                rowsByClientId.TryGetValue(preferredClientId, out row);

            if (row == null)
            {
                string placeholderClientId = rowsByClientId.Keys.FirstOrDefault(IsPlaceholderClientId);
                if (!string.IsNullOrWhiteSpace(placeholderClientId) &&
                    rowsByClientId.TryGetValue(placeholderClientId, out SessionLifecycleRow placeholderRow))
                {
                    row = CloneRow(placeholderRow);
                    row.clientId = string.IsNullOrWhiteSpace(preferredClientId) ? placeholderClientId : preferredClientId;
                }
            }

            if (row == null)
                return string.Empty;

            EnsureRowMetadata(row, preferredClientId);
            return JsonUtility.ToJson(row);
        }

        public void ApplyRowJson(string rowJson, string fallbackClientId = null)
        {
            if (string.IsNullOrWhiteSpace(rowJson))
                return;

            SessionLifecycleRow incomingRow;
            try
            {
                incomingRow = JsonUtility.FromJson<SessionLifecycleRow>(rowJson);
            }
            catch (Exception)
            {
                return;
            }

            if (incomingRow == null)
                return;

            EnsureRowMetadata(incomingRow, fallbackClientId);

            if (string.IsNullOrWhiteSpace(incomingRow.clientId))
                return;

            CaptureGlobalSessionStartPressed(incomingRow.sessionStartPressedUtc);

            SessionLifecycleRow targetRow = GetOrCreateRow(incomingRow.clientId);
            MergeRowData(targetRow, incomingRow);
            RefreshComputedLatencies(targetRow);

            if (!string.Equals(targetRow.clientId, incomingRow.clientId, StringComparison.Ordinal))
                targetRow.clientId = incomingRow.clientId;
        }

        public void MarkConnectStarted(string clientId)
        {
            SessionLifecycleRow row = GetOrCreateRow(clientId);
            row.connectStartUtc = DateTime.UtcNow.ToString("o");
            Log($"Connect started for {clientId}");
        }

        public void MarkConnectSucceeded(string clientId)
        {
            SessionLifecycleRow row = GetOrCreateRow(clientId);
            row.connectSuccessUtc = DateTime.UtcNow.ToString("o");
            row.connectLatencyMs = ComputeLatencyMs(row.connectStartUtc, row.connectSuccessUtc);
            Log($"Connect succeeded for {clientId}");
        }

        public void MarkCreateStarted(string clientId)
        {
            SessionLifecycleRow row = GetOrCreateRow(clientId);
            row.createStartUtc = DateTime.UtcNow.ToString("o");
            Log($"Create started for {clientId}");
        }

        public void MarkCreateSucceeded(string clientId)
        {
            SessionLifecycleRow row = GetOrCreateRow(clientId);
            row.createSuccessUtc = DateTime.UtcNow.ToString("o");
            row.createLatencyMs = ComputeLatencyMs(row.createStartUtc, row.createSuccessUtc);
            Log($"Create succeeded for {clientId}");
        }

        public void MarkJoinStarted(string clientId)
        {
            SessionLifecycleRow row = GetOrCreateRow(clientId);
            row.joinStartUtc = DateTime.UtcNow.ToString("o");
            Log($"Join started for {clientId}");
        }

        public void MarkJoinSucceeded(string clientId)
        {
            SessionLifecycleRow row = GetOrCreateRow(clientId);
            row.joinSuccessUtc = DateTime.UtcNow.ToString("o");
            row.joinLatencyMs = ComputeLatencyMs(row.joinStartUtc, row.joinSuccessUtc);
            Log($"Join succeeded for {clientId}");
        }

        public void MarkSessionStartPressed(string clientId)
        {
            SessionLifecycleRow row = GetOrCreateRow(clientId);
            row.sessionStartPressedUtc = DateTime.UtcNow.ToString("o");
            CaptureGlobalSessionStartPressed(row.sessionStartPressedUtc);
            Log($"Session start pressed for {clientId}");
        }

        public void MarkSessionStartVisible(string clientId)
        {
            SessionLifecycleRow row = GetOrCreateRow(clientId);
            row.sessionStartVisibleUtc = DateTime.UtcNow.ToString("o");
            row.sessionStartVisibleDelayMs = ComputeLatencyMs(row.sessionStartPressedUtc, row.sessionStartVisibleUtc);
            Log($"Session start visible for {clientId}");
        }

        public void ClearAll()
        {
            rowsByClientId.Clear();
            globalSessionStartPressedUtc = string.Empty;
        }

        public void SaveSessionLifecycleCsvToFile(string filePath)
        {
            string directory = Path.GetDirectoryName(filePath);
            if (!string.IsNullOrWhiteSpace(directory) && !Directory.Exists(directory))
                Directory.CreateDirectory(directory);

            StringBuilder sb = new StringBuilder();
            sb.AppendLine("run_id,backend,scenario_id,client_id,connect_start_utc,connect_success_utc,create_start_utc,create_success_utc,join_start_utc,join_success_utc,session_start_pressed_utc,session_start_visible_utc,connect_latency_ms,create_latency_ms,join_latency_ms,session_start_visible_delay_ms");

            foreach (SessionLifecycleRow row in rowsByClientId.Values.OrderBy(r => r.clientId, StringComparer.OrdinalIgnoreCase))
            {
                sb.AppendLine(
                    $"{Escape(row.runId)}," +
                    $"{Escape(row.backend)}," +
                    $"{Escape(row.scenarioId)}," +
                    $"{Escape(row.clientId)}," +
                    $"{Escape(row.connectStartUtc)}," +
                    $"{Escape(row.connectSuccessUtc)}," +
                    $"{Escape(row.createStartUtc)}," +
                    $"{Escape(row.createSuccessUtc)}," +
                    $"{Escape(row.joinStartUtc)}," +
                    $"{Escape(row.joinSuccessUtc)}," +
                    $"{Escape(row.sessionStartPressedUtc)}," +
                    $"{Escape(row.sessionStartVisibleUtc)}," +
                    $"{row.connectLatencyMs}," +
                    $"{row.createLatencyMs}," +
                    $"{row.joinLatencyMs}," +
                    $"{row.sessionStartVisibleDelayMs}");
            }

            File.WriteAllText(filePath, sb.ToString(), Encoding.UTF8);
        }

        public static bool IsPlaceholderClientId(string clientId)
        {
            if (string.IsNullOrWhiteSpace(clientId))
                return true;

            return string.Equals(clientId, "UnknownUser", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(clientId, "UnknownClient", StringComparison.OrdinalIgnoreCase) ||
                   clientId.EndsWith("_LocalClient", StringComparison.OrdinalIgnoreCase);
        }

        private double ComputeLatencyMs(string startUtc, string endUtc)
        {
            if (string.IsNullOrWhiteSpace(startUtc) || string.IsNullOrWhiteSpace(endUtc))
                return 0d;

            if (!DateTime.TryParse(startUtc, null, System.Globalization.DateTimeStyles.RoundtripKind, out DateTime start))
                return 0d;

            if (!DateTime.TryParse(endUtc, null, System.Globalization.DateTimeStyles.RoundtripKind, out DateTime end))
                return 0d;

            return (end - start).TotalMilliseconds;
        }

        private void CaptureGlobalSessionStartPressed(string timestampUtc)
        {
            if (string.IsNullOrWhiteSpace(timestampUtc) || !string.IsNullOrWhiteSpace(globalSessionStartPressedUtc))
                return;

            globalSessionStartPressedUtc = timestampUtc;
            foreach (SessionLifecycleRow row in rowsByClientId.Values)
            {
                if (string.IsNullOrWhiteSpace(row.sessionStartPressedUtc))
                    row.sessionStartPressedUtc = globalSessionStartPressedUtc;

                RefreshComputedLatencies(row);
            }
        }

        private void RefreshComputedLatencies(SessionLifecycleRow row)
        {
            if (row == null)
                return;

            if (!string.IsNullOrWhiteSpace(row.connectStartUtc) && !string.IsNullOrWhiteSpace(row.connectSuccessUtc))
                row.connectLatencyMs = ComputeLatencyMs(row.connectStartUtc, row.connectSuccessUtc);
            if (!string.IsNullOrWhiteSpace(row.createStartUtc) && !string.IsNullOrWhiteSpace(row.createSuccessUtc))
                row.createLatencyMs = ComputeLatencyMs(row.createStartUtc, row.createSuccessUtc);
            if (!string.IsNullOrWhiteSpace(row.joinStartUtc) && !string.IsNullOrWhiteSpace(row.joinSuccessUtc))
                row.joinLatencyMs = ComputeLatencyMs(row.joinStartUtc, row.joinSuccessUtc);
            if (!string.IsNullOrWhiteSpace(row.sessionStartPressedUtc) && !string.IsNullOrWhiteSpace(row.sessionStartVisibleUtc))
                row.sessionStartVisibleDelayMs = ComputeLatencyMs(row.sessionStartPressedUtc, row.sessionStartVisibleUtc);
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

            Debug.Log($"[SessionLifecycleTracker] {message}", this);
        }

        private void EnsureRowMetadata(SessionLifecycleRow row, string fallbackClientId = null)
        {
            if (row == null)
                return;

            var context = EvaluationRunContext.Instance;

            if (string.IsNullOrWhiteSpace(row.runId))
                row.runId = context != null ? context.RunId : "UnknownRun";

            if (string.IsNullOrWhiteSpace(row.backend))
                row.backend = context != null ? context.BackendName : "UnknownBackend";

            if (string.IsNullOrWhiteSpace(row.scenarioId))
                row.scenarioId = context != null ? context.ScenarioId : "UnknownScenario";

            if (string.IsNullOrWhiteSpace(row.clientId))
                row.clientId = string.IsNullOrWhiteSpace(fallbackClientId) ? "UnknownClient" : fallbackClientId;
        }

        private SessionLifecycleRow CloneRow(SessionLifecycleRow row)
        {
            if (row == null)
                return null;

            return new SessionLifecycleRow
            {
                runId = row.runId,
                backend = row.backend,
                scenarioId = row.scenarioId,
                clientId = row.clientId,
                connectStartUtc = row.connectStartUtc,
                connectSuccessUtc = row.connectSuccessUtc,
                createStartUtc = row.createStartUtc,
                createSuccessUtc = row.createSuccessUtc,
                joinStartUtc = row.joinStartUtc,
                joinSuccessUtc = row.joinSuccessUtc,
                sessionStartPressedUtc = row.sessionStartPressedUtc,
                sessionStartVisibleUtc = row.sessionStartVisibleUtc,
                connectLatencyMs = row.connectLatencyMs,
                createLatencyMs = row.createLatencyMs,
                joinLatencyMs = row.joinLatencyMs,
                sessionStartVisibleDelayMs = row.sessionStartVisibleDelayMs
            };
        }

        private void MergeRowData(SessionLifecycleRow targetRow, SessionLifecycleRow sourceRow)
        {
            if (targetRow == null || sourceRow == null)
                return;

            if (string.IsNullOrWhiteSpace(targetRow.connectStartUtc)) targetRow.connectStartUtc = sourceRow.connectStartUtc;
            if (string.IsNullOrWhiteSpace(targetRow.connectSuccessUtc)) targetRow.connectSuccessUtc = sourceRow.connectSuccessUtc;
            if (string.IsNullOrWhiteSpace(targetRow.createStartUtc)) targetRow.createStartUtc = sourceRow.createStartUtc;
            if (string.IsNullOrWhiteSpace(targetRow.createSuccessUtc)) targetRow.createSuccessUtc = sourceRow.createSuccessUtc;
            if (string.IsNullOrWhiteSpace(targetRow.joinStartUtc)) targetRow.joinStartUtc = sourceRow.joinStartUtc;
            if (string.IsNullOrWhiteSpace(targetRow.joinSuccessUtc)) targetRow.joinSuccessUtc = sourceRow.joinSuccessUtc;
            if (string.IsNullOrWhiteSpace(targetRow.sessionStartPressedUtc)) targetRow.sessionStartPressedUtc = sourceRow.sessionStartPressedUtc;
            if (string.IsNullOrWhiteSpace(targetRow.sessionStartVisibleUtc)) targetRow.sessionStartVisibleUtc = sourceRow.sessionStartVisibleUtc;

            if (targetRow.connectLatencyMs <= 0d) targetRow.connectLatencyMs = sourceRow.connectLatencyMs;
            if (targetRow.createLatencyMs <= 0d) targetRow.createLatencyMs = sourceRow.createLatencyMs;
            if (targetRow.joinLatencyMs <= 0d) targetRow.joinLatencyMs = sourceRow.joinLatencyMs;
            if (targetRow.sessionStartVisibleDelayMs <= 0d) targetRow.sessionStartVisibleDelayMs = sourceRow.sessionStartVisibleDelayMs;
        }
    }
}
