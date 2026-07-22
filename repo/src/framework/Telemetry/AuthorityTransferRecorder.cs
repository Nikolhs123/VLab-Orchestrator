using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;

namespace Thesis.Orchestration.Telemetry
{
    public class AuthorityTransferRecorder : MonoBehaviour
    {
        public static AuthorityTransferRecorder Instance { get; private set; }

        [Serializable]
        public class AuthorityTransferRow
        {
            public string runId;
            public string backend;
            public string scenarioId;

            public string timestampUtc;
            public string objectId;
            public string logicalHolder;

            public string ownerBefore;
            public string ownerAfter;

            public string transferRequestedUtc;
            public string transferCompletedUtc;

            public bool transferSuccess;
            public double transferLatencyMs;
            public string details;
        }

        [Header("Debug")]
        [SerializeField] private bool verboseLogs = true;

        private const double DuplicateTransferWindowMs = 500d;
        private readonly List<AuthorityTransferRow> rows = new();
        private readonly Dictionary<string, AuthorityTransferRow> pendingTransfers = new();
        private readonly Dictionary<string, AuthorityTransferRow> recentCompletedTransfers = new();
        private readonly HashSet<string> importedTransferSignatures = new();

        public IReadOnlyList<AuthorityTransferRow> Rows => rows;

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
            pendingTransfers.Clear();
            recentCompletedTransfers.Clear();
            importedTransferSignatures.Clear();
        }

        public void RecordTransferRequested(string objectId, string logicalHolder, string ownerBefore, string details = "")
        {
            string key = BuildKey(objectId, logicalHolder);
            if (ShouldSuppressDuplicateRequest(key, logicalHolder, ownerBefore))
                return;

            AuthorityTransferRow row = new AuthorityTransferRow
            {
                runId = GetRunId(),
                backend = GetBackend(),
                scenarioId = GetScenarioId(),
                timestampUtc = DateTime.UtcNow.ToString("o"),
                objectId = objectId,
                logicalHolder = Normalize(logicalHolder),
                ownerBefore = Normalize(ownerBefore),
                ownerAfter = Normalize(ownerBefore),
                transferRequestedUtc = DateTime.UtcNow.ToString("o"),
                transferCompletedUtc = "",
                transferSuccess = false,
                transferLatencyMs = 0d,
                details = details
            };

            pendingTransfers[key] = row;
            Log($"Transfer requested | object={objectId} | logicalHolder={logicalHolder} | ownerBefore={ownerBefore}");
        }

        public void CancelPendingTransfer(string objectId, string logicalHolder, string details = "")
        {
            string key = BuildKey(objectId, logicalHolder);
            if (!pendingTransfers.Remove(key))
                return;

            Log($"Pending transfer cancelled | object={objectId} | logicalHolder={logicalHolder} | details={details}");
        }

        public void RecordTransferCompleted(string objectId, string logicalHolder, string ownerAfter, bool success, string details = "")
        {
            string key = BuildKey(objectId, logicalHolder);

            if (!pendingTransfers.TryGetValue(key, out AuthorityTransferRow row))
            {
                row = new AuthorityTransferRow
                {
                    runId = GetRunId(),
                    backend = GetBackend(),
                    scenarioId = GetScenarioId(),
                    timestampUtc = DateTime.UtcNow.ToString("o"),
                    objectId = objectId,
                    logicalHolder = Normalize(logicalHolder),
                    ownerBefore = "Unknown",
                    transferRequestedUtc = DateTime.UtcNow.ToString("o")
                };
            }

            string normalizedOwnerAfter = Normalize(ownerAfter);
            if (ShouldSuppressDuplicateCompletion(key, normalizedOwnerAfter, success))
            {
                pendingTransfers.Remove(key);
                Log($"Duplicate transfer completion suppressed | object={objectId} | logicalHolder={logicalHolder} | ownerAfter={ownerAfter} | success={success}");
                return;
            }

            row.ownerAfter = normalizedOwnerAfter;
            row.transferCompletedUtc = DateTime.UtcNow.ToString("o");
            row.transferSuccess = success;
            row.transferLatencyMs = ComputeLatencyMs(row.transferRequestedUtc, row.transferCompletedUtc);
            row.details = details;

            rows.Add(row);
            pendingTransfers.Remove(key);
            recentCompletedTransfers[key] = row;

            Log($"Transfer completed | object={objectId} | logicalHolder={logicalHolder} | ownerAfter={ownerAfter} | success={success}");
        }

        public string BuildMostRecentCompletedRowJson(string objectId, string logicalHolder)
        {
            string key = BuildKey(objectId, logicalHolder);
            return recentCompletedTransfers.TryGetValue(key, out AuthorityTransferRow row) && row != null
                ? JsonUtility.ToJson(row)
                : string.Empty;
        }

        public void ApplyCompletedRowJson(string rowJson)
        {
            if (string.IsNullOrWhiteSpace(rowJson))
                return;

            AuthorityTransferRow incomingRow;
            try
            {
                incomingRow = JsonUtility.FromJson<AuthorityTransferRow>(rowJson);
            }
            catch (Exception)
            {
                return;
            }

            if (incomingRow == null ||
                string.IsNullOrWhiteSpace(incomingRow.objectId) ||
                string.IsNullOrWhiteSpace(incomingRow.logicalHolder) ||
                string.IsNullOrWhiteSpace(incomingRow.transferCompletedUtc))
            {
                return;
            }

            incomingRow.runId = GetRunId();
            incomingRow.backend = GetBackend();
            incomingRow.scenarioId = GetScenarioId();
            incomingRow.objectId = Normalize(incomingRow.objectId);
            incomingRow.logicalHolder = Normalize(incomingRow.logicalHolder);
            incomingRow.ownerBefore = Normalize(incomingRow.ownerBefore);
            incomingRow.ownerAfter = Normalize(incomingRow.ownerAfter);

            string signature = string.Join("|", new[]
            {
                incomingRow.objectId,
                incomingRow.logicalHolder,
                incomingRow.transferRequestedUtc ?? string.Empty,
                incomingRow.transferCompletedUtc,
                incomingRow.ownerAfter,
                incomingRow.transferSuccess.ToString()
            });

            if (!importedTransferSignatures.Add(signature))
                return;

            string key = BuildKey(incomingRow.objectId, incomingRow.logicalHolder);
            rows.Add(incomingRow);
            pendingTransfers.Remove(key);
            recentCompletedTransfers[key] = incomingRow;

            Log($"Imported transfer completion | object={incomingRow.objectId} | logicalHolder={incomingRow.logicalHolder} | ownerAfter={incomingRow.ownerAfter} | success={incomingRow.transferSuccess}");
        }

        public void SaveAuthorityTransfersCsvToFile(string filePath)
        {
            string directory = Path.GetDirectoryName(filePath);
            if (!string.IsNullOrWhiteSpace(directory) && !Directory.Exists(directory))
                Directory.CreateDirectory(directory);

            StringBuilder sb = new StringBuilder();
            sb.AppendLine("run_id,backend,scenario_id,timestamp_utc,object_id,logical_holder,owner_before,owner_after,transfer_requested_utc,transfer_completed_utc,transfer_success,transfer_latency_ms,details");

            foreach (AuthorityTransferRow row in rows)
            {
                sb.AppendLine(
                    $"{Escape(row.runId)}," +
                    $"{Escape(row.backend)}," +
                    $"{Escape(row.scenarioId)}," +
                    $"{Escape(row.timestampUtc)}," +
                    $"{Escape(row.objectId)}," +
                    $"{Escape(row.logicalHolder)}," +
                    $"{Escape(row.ownerBefore)}," +
                    $"{Escape(row.ownerAfter)}," +
                    $"{Escape(row.transferRequestedUtc)}," +
                    $"{Escape(row.transferCompletedUtc)}," +
                    $"{row.transferSuccess}," +
                    $"{row.transferLatencyMs}," +
                    $"{Escape(row.details)}");
            }

            File.WriteAllText(filePath, sb.ToString(), Encoding.UTF8);
        }

        private string BuildKey(string objectId, string logicalHolder)
        {
            return $"{Normalize(objectId)}::{Normalize(logicalHolder)}";
        }

        private bool ShouldSuppressDuplicateRequest(string key, string logicalHolder, string ownerBefore)
        {
            string normalizedOwnerBefore = Normalize(ownerBefore);
            if (pendingTransfers.TryGetValue(key, out AuthorityTransferRow pendingRow) &&
                pendingRow != null &&
                pendingRow.ownerBefore == normalizedOwnerBefore)
            {
                Log($"Duplicate transfer request suppressed | key={key}");
                return true;
            }

            if (!recentCompletedTransfers.TryGetValue(key, out AuthorityTransferRow recentRow) || recentRow == null)
                return false;

            if (!recentRow.transferSuccess)
                return false;

            if (recentRow.ownerBefore != normalizedOwnerBefore)
                return false;

            if (recentRow.ownerAfter != Normalize(logicalHolder))
                return false;

            return GetElapsedMsSince(recentRow.transferCompletedUtc) <= DuplicateTransferWindowMs;
        }

        private bool ShouldSuppressDuplicateCompletion(string key, string ownerAfter, bool success)
        {
            if (!recentCompletedTransfers.TryGetValue(key, out AuthorityTransferRow recentRow) || recentRow == null)
                return false;

            if (recentRow.transferSuccess != success)
                return false;

            if (recentRow.ownerAfter != ownerAfter)
                return false;

            return GetElapsedMsSince(recentRow.transferCompletedUtc) <= DuplicateTransferWindowMs;
        }

        private string Normalize(string value)
        {
            return string.IsNullOrWhiteSpace(value) ? "None" : value;
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

        private double GetElapsedMsSince(string timestampUtc)
        {
            if (string.IsNullOrWhiteSpace(timestampUtc))
                return double.MaxValue;

            if (!DateTime.TryParse(timestampUtc, null, System.Globalization.DateTimeStyles.RoundtripKind, out DateTime timestamp))
                return double.MaxValue;

            return (DateTime.UtcNow - timestamp).TotalMilliseconds;
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

            Debug.Log($"[AuthorityTransferRecorder] {message}", this);
        }
    }
}
