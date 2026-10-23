using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Globalization;
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
            public string measurementId;
            public bool latencyAvailable;
            public long requestMonotonicTicks;
            public long completionMonotonicTicks;
            public long clockFrequencyHz;
            public string clockScopeId;
            public string observationSource;
        }

        [Serializable]
        public class AuthorityLifecycleRow
        {
            public string measurementId;
            public string eventType;
            public string timestampUtc;
            public long monotonicTicks;
            public string objectId;
            public string logicalHolder;
            public string observationSource;
            public string details;
        }

        [Header("Debug")]
        [SerializeField] private bool verboseLogs = true;

        private const double DuplicateTransferWindowMs = 500d;
        private readonly List<AuthorityTransferRow> rows = new();
        private readonly Dictionary<string, AuthorityTransferRow> pendingTransfers = new();
        private readonly Dictionary<string, AuthorityTransferRow> recentCompletedTransfers = new();
        private readonly HashSet<string> importedTransferSignatures = new();
        private readonly List<AuthorityLifecycleRow> lifecycleRows = new();
        private readonly Dictionary<string, long> pendingStartTicks = new();
        private readonly HashSet<string> acceptedMeasurementIds = new();
        private readonly string clockScopeId = Guid.NewGuid().ToString("N");

        public class MeasurementSummary
        {
            public int initiated, completed, failed, canceled, timedOut, superseded, pending;
            public int closedByImport, unmatchedCompletions, importedCompletions;
            public int staleTerminalRejected, lateObservationsIgnored;
            public bool countsReconcile;
        }

        protected virtual long ReadMonotonicTicks() => System.Diagnostics.Stopwatch.GetTimestamp();
        protected virtual long ClockFrequencyHz => System.Diagnostics.Stopwatch.Frequency;

        public IReadOnlyList<AuthorityTransferRow> Rows => rows;
        public IReadOnlyList<AuthorityLifecycleRow> LifecycleRows => lifecycleRows;
        public int PendingTransferCount => pendingTransfers.Count;

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
            lifecycleRows.Clear();
            pendingStartTicks.Clear();
            acceptedMeasurementIds.Clear();
        }

        // Returns the measurement ID that now owns the pending request (the existing one when a
        // duplicate request is suppressed). Adapters pass it back to the measurement-scoped
        // terminal methods so a delayed callback cannot close a newer measurement.
        public string RecordTransferRequested(string objectId, string logicalHolder, string ownerBefore, string details = "")
        {
            string key = BuildKey(objectId, logicalHolder);
            if (ShouldSuppressDuplicateRequest(key, ownerBefore))
            {
                pendingTransfers.TryGetValue(key, out AuthorityTransferRow duplicate);
                RecordLifecycle(duplicate, "DuplicateRequestSuppressed", "Local", objectId, logicalHolder, details);
                return duplicate?.measurementId ?? string.Empty;
            }

            if (pendingTransfers.TryGetValue(key, out AuthorityTransferRow superseded))
                RecordLifecycle(superseded, "Superseded", "Local", objectId, logicalHolder, details);

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

            row.measurementId = Guid.NewGuid().ToString("N");
            row.requestMonotonicTicks = ReadMonotonicTicks();
            row.clockFrequencyHz = ClockFrequencyHz;
            row.clockScopeId = clockScopeId;
            row.observationSource = "Local";
            pendingTransfers[key] = row;
            pendingStartTicks[key] = row.requestMonotonicTicks;
            RecordLifecycle(row, "Requested", "Local", objectId, logicalHolder, details);
            Log($"Transfer requested | object={objectId} | logicalHolder={logicalHolder} | ownerBefore={ownerBefore}");
            return row.measurementId;
        }

        public bool CompleteMeasurement(string measurementId, string objectId, string logicalHolder,
            string ownerAfter, bool success, string details = "")
        {
            if (!IsCurrentPendingMeasurement(measurementId, objectId, logicalHolder, success ? "Completed" : "Failed", details))
                return false;
            RecordTransferCompleted(objectId, logicalHolder, ownerAfter, success, details);
            return true;
        }

        public bool CancelMeasurement(string measurementId, string objectId, string logicalHolder, string details = "")
        {
            if (!IsCurrentPendingMeasurement(measurementId, objectId, logicalHolder, "Canceled", details))
                return false;
            CancelPendingTransfer(objectId, logicalHolder, details);
            return true;
        }

        public bool TimeoutMeasurement(string measurementId, string objectId, string logicalHolder, string details = "")
        {
            if (!IsCurrentPendingMeasurement(measurementId, objectId, logicalHolder, "TimedOut", details))
                return false;
            TimeoutPendingTransfer(objectId, logicalHolder, details);
            return true;
        }

        // A native callback observed after its measurement already reached a terminal state.
        // Journaled for audit; never changes counts, pending state or completion rows.
        public void RecordLateObservation(string measurementId, string objectId, string logicalHolder, string details = "")
        {
            RecordLifecycle(new AuthorityTransferRow { measurementId = measurementId ?? string.Empty },
                "LateObservationIgnored", "Local", objectId, logicalHolder, details);
        }

        public string BuildCompletedRowJson(string measurementId)
        {
            for (int i = rows.Count - 1; i >= 0; i--)
            {
                if (rows[i].measurementId == measurementId && rows[i].observationSource == "Local")
                    return JsonUtility.ToJson(rows[i]);
            }
            return string.Empty;
        }

        private bool IsCurrentPendingMeasurement(string measurementId, string objectId, string logicalHolder,
            string attemptedEvent, string details)
        {
            if (pendingTransfers.TryGetValue(BuildKey(objectId, logicalHolder), out AuthorityTransferRow pending) &&
                !string.IsNullOrEmpty(measurementId) && pending.measurementId == measurementId)
                return true;

            RecordLifecycle(new AuthorityTransferRow { measurementId = measurementId ?? string.Empty },
                "StaleTerminalRejected", "Local", objectId, logicalHolder,
                $"{attemptedEvent} rejected; current pending={pending?.measurementId ?? "none"}. {details}");
            return false;
        }

        public void CancelPendingTransfer(string objectId, string logicalHolder, string details = "")
        {
            string key = BuildKey(objectId, logicalHolder);
            if (!pendingTransfers.TryGetValue(key, out AuthorityTransferRow row))
                return;

            pendingTransfers.Remove(key);
            pendingStartTicks.Remove(key);
            RecordLifecycle(row, "Canceled", "Local", objectId, logicalHolder, details);

            Log($"Pending transfer cancelled | object={objectId} | logicalHolder={logicalHolder} | details={details}");
        }

        public void TimeoutPendingTransfer(string objectId, string logicalHolder, string details = "")
        {
            string key = BuildKey(objectId, logicalHolder);
            if (!pendingTransfers.TryGetValue(key, out AuthorityTransferRow row))
                return;
            pendingTransfers.Remove(key);
            pendingStartTicks.Remove(key);
            RecordLifecycle(row, "TimedOut", "Local", objectId, logicalHolder, details);
        }

        public void RecordTransferCompleted(string objectId, string logicalHolder, string ownerAfter, bool success, string details = "")
        {
            string key = BuildKey(objectId, logicalHolder);

            bool hadPendingRequest = pendingTransfers.TryGetValue(key, out AuthorityTransferRow row);
            if (!hadPendingRequest)
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
                    transferRequestedUtc = "",
                    measurementId = Guid.NewGuid().ToString("N"),
                    observationSource = "Local",
                    clockScopeId = clockScopeId
                };
            }

            string normalizedOwnerAfter = Normalize(ownerAfter);
            if (!hadPendingRequest && ShouldSuppressDuplicateCompletion(key, normalizedOwnerAfter, success))
            {
                RecordLifecycle(row, "DuplicateCompletionSuppressed", "Local", objectId, logicalHolder, details);
                pendingTransfers.Remove(key);
                Log($"Duplicate transfer completion suppressed | object={objectId} | logicalHolder={logicalHolder} | ownerAfter={ownerAfter} | success={success}");
                return;
            }

            row.ownerAfter = normalizedOwnerAfter;
            row.transferCompletedUtc = DateTime.UtcNow.ToString("o");
            row.transferSuccess = success;
            row.completionMonotonicTicks = ReadMonotonicTicks();
            row.latencyAvailable = hadPendingRequest && pendingStartTicks.TryGetValue(key, out long startTicks) &&
                                   row.completionMonotonicTicks >= startTicks && row.clockFrequencyHz > 0;
            row.transferLatencyMs = row.latencyAvailable
                ? (row.completionMonotonicTicks - row.requestMonotonicTicks) * 1000d / row.clockFrequencyHz
                : -1d;
            row.details = details;

            RecordLifecycle(row,
                hadPendingRequest ? (success ? "Completed" : "Failed") : "CompletionWithoutRequest",
                "Local", objectId, logicalHolder, details);

            rows.Add(row);
            acceptedMeasurementIds.Add(row.measurementId);
            pendingTransfers.Remove(key);
            pendingStartTicks.Remove(key);
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
            incomingRow.observationSource = "RemoteImported";
            // Imported elapsed times are usable only with explicit source clock provenance.
            incomingRow.latencyAvailable = incomingRow.latencyAvailable &&
                !string.IsNullOrWhiteSpace(incomingRow.measurementId) &&
                !string.IsNullOrWhiteSpace(incomingRow.clockScopeId) && incomingRow.clockFrequencyHz > 0 &&
                incomingRow.completionMonotonicTicks >= incomingRow.requestMonotonicTicks;
            incomingRow.transferLatencyMs = incomingRow.latencyAvailable
                ? (incomingRow.completionMonotonicTicks - incomingRow.requestMonotonicTicks) *
                    1000d / incomingRow.clockFrequencyHz
                : -1d;
            if (!string.IsNullOrWhiteSpace(incomingRow.measurementId) &&
                !acceptedMeasurementIds.Add(incomingRow.measurementId))
                return;

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
            if (pendingTransfers.TryGetValue(key, out AuthorityTransferRow reconciled) &&
                reconciled.measurementId == incomingRow.measurementId)
            {
                RecordLifecycle(reconciled, "ClosedByImportedCompletion", "RemoteImported",
                    incomingRow.objectId, incomingRow.logicalHolder,
                    "Imported completion closed local pending tracking; not an independent local authority observation.");
                pendingTransfers.Remove(key);
                pendingStartTicks.Remove(key);
            }
            recentCompletedTransfers[key] = incomingRow;
            RecordLifecycle(incomingRow, "ImportedCompletion", "RemoteImported",
                incomingRow.objectId, incomingRow.logicalHolder, incomingRow.details);

            Log($"Imported transfer completion | object={incomingRow.objectId} | logicalHolder={incomingRow.logicalHolder} | ownerAfter={incomingRow.ownerAfter} | success={incomingRow.transferSuccess}");
        }

        public void SaveAuthorityTransfersCsvToFile(string filePath)
        {
            string directory = Path.GetDirectoryName(filePath);
            if (!string.IsNullOrWhiteSpace(directory) && !Directory.Exists(directory))
                Directory.CreateDirectory(directory);

            StringBuilder sb = new StringBuilder();
            sb.AppendLine("run_id,backend,scenario_id,timestamp_utc,object_id,logical_holder,owner_before,owner_after,transfer_requested_utc,transfer_completed_utc,transfer_success,transfer_latency_ms,details,measurement_id,latency_available,request_monotonic_ticks,completion_monotonic_ticks,clock_frequency_hz,clock_scope_id,observation_source");

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
                    $"{row.transferLatencyMs.ToString(CultureInfo.InvariantCulture)}," +
                    $"{Escape(row.details)},{Escape(row.measurementId)},{row.latencyAvailable}," +
                    $"{row.requestMonotonicTicks},{row.completionMonotonicTicks},{row.clockFrequencyHz}," +
                    $"{Escape(row.clockScopeId)},{Escape(row.observationSource)}");
            }

            File.WriteAllText(filePath, sb.ToString(), Encoding.UTF8);
            SaveAuthorityLifecycleCsvToFile(Path.Combine(directory ?? string.Empty, "authority_measurement_lifecycle.csv"));
            SaveMeasurementSummary(Path.Combine(directory ?? string.Empty, "authority_measurement_summary.csv"));
        }

        public MeasurementSummary BuildMeasurementSummary()
        {
            var result = new MeasurementSummary { pending = pendingTransfers.Count };
            foreach (AuthorityLifecycleRow row in lifecycleRows)
            {
                switch (row.eventType)
                {
                    case "Requested": result.initiated++; break;
                    case "Completed": result.completed++; break;
                    case "Failed": result.failed++; break;
                    case "Canceled": result.canceled++; break;
                    case "TimedOut": result.timedOut++; break;
                    case "Superseded": result.superseded++; break;
                    case "ClosedByImportedCompletion": result.closedByImport++; break;
                    case "CompletionWithoutRequest": result.unmatchedCompletions++; break;
                    case "ImportedCompletion": result.importedCompletions++; break;
                    case "StaleTerminalRejected": result.staleTerminalRejected++; break;
                    case "LateObservationIgnored": result.lateObservationsIgnored++; break;
                }
            }
            result.countsReconcile = result.initiated == result.completed + result.failed + result.canceled +
                result.timedOut + result.superseded + result.closedByImport + result.pending;
            return result;
        }

        private void SaveMeasurementSummary(string filePath)
        {
            MeasurementSummary s = BuildMeasurementSummary();
            File.WriteAllText(filePath,
                "run_id,backend,scenario_id,local_initiated,local_completed,local_failed,local_canceled,local_timed_out,local_superseded,local_pending,local_closed_by_import,unmatched_completions,imported_completions,stale_terminal_rejected,late_observations_ignored,counts_reconcile\n" +
                $"{Escape(GetRunId())},{Escape(GetBackend())},{Escape(GetScenarioId())},{s.initiated}," +
                $"{s.completed},{s.failed},{s.canceled},{s.timedOut},{s.superseded},{s.pending}," +
                $"{s.closedByImport},{s.unmatchedCompletions},{s.importedCompletions},{s.staleTerminalRejected},{s.lateObservationsIgnored},{s.countsReconcile}\n",
                Encoding.UTF8);
        }

        public void SaveAuthorityLifecycleCsvToFile(string filePath)
        {
            string directory = Path.GetDirectoryName(filePath);
            if (!string.IsNullOrWhiteSpace(directory))
                Directory.CreateDirectory(directory);
            var sb = new StringBuilder();
            sb.AppendLine("run_id,backend,scenario_id,measurement_id,event_type,timestamp_utc,monotonic_ticks,clock_frequency_hz,clock_scope_id,object_id,logical_holder,observation_source,details");
            foreach (AuthorityLifecycleRow row in lifecycleRows)
                AppendLifecycleCsvRow(sb, row);
            // Snapshot pending requests without adding repeated export events to the journal.
            foreach (AuthorityTransferRow pending in pendingTransfers.Values)
                AppendLifecycleCsvRow(sb, CreateLifecycle(pending, "PendingAtExport", "Local",
                    pending.objectId, pending.logicalHolder, "No terminal event observed at export."));
            File.WriteAllText(filePath, sb.ToString(), Encoding.UTF8);
        }

        private void RecordLifecycle(AuthorityTransferRow transfer, string eventType, string source,
            string objectId, string logicalHolder, string details)
        {
            lifecycleRows.Add(CreateLifecycle(transfer, eventType, source, objectId, logicalHolder, details));
        }

        private AuthorityLifecycleRow CreateLifecycle(AuthorityTransferRow transfer, string eventType,
            string source, string objectId, string logicalHolder, string details)
        {
            return new AuthorityLifecycleRow
            {
                measurementId = transfer?.measurementId ?? string.Empty,
                eventType = eventType,
                timestampUtc = DateTime.UtcNow.ToString("o"),
                monotonicTicks = ReadMonotonicTicks(),
                objectId = Normalize(objectId),
                logicalHolder = Normalize(logicalHolder),
                observationSource = source,
                details = details
            };
        }

        private void AppendLifecycleCsvRow(StringBuilder sb, AuthorityLifecycleRow row)
        {
            sb.AppendLine($"{Escape(GetRunId())},{Escape(GetBackend())},{Escape(GetScenarioId())}," +
                $"{Escape(row.measurementId)},{Escape(row.eventType)},{Escape(row.timestampUtc)}," +
                $"{row.monotonicTicks.ToString(CultureInfo.InvariantCulture)}," +
                $"{ClockFrequencyHz.ToString(CultureInfo.InvariantCulture)}," +
                $"{Escape(clockScopeId)}," +
                $"{Escape(row.objectId)},{Escape(row.logicalHolder)},{Escape(row.observationSource)}," +
                $"{Escape(row.details)}");
        }

        private string BuildKey(string objectId, string logicalHolder)
        {
            return $"{Normalize(objectId)}::{Normalize(logicalHolder)}";
        }

        private bool ShouldSuppressDuplicateRequest(string key, string ownerBefore)
        {
            string normalizedOwnerBefore = Normalize(ownerBefore);
            if (pendingTransfers.TryGetValue(key, out AuthorityTransferRow pendingRow) &&
                pendingRow != null &&
                pendingRow.ownerBefore == normalizedOwnerBefore)
            {
                Log($"Duplicate transfer request suppressed | key={key}");
                return true;
            }

            // A completed request must not suppress a genuinely new attempt.
            return false;
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
