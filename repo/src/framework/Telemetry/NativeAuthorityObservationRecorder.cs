using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using UnityEngine;

namespace Thesis.Orchestration.Telemetry
{
    // Records, on each participant process, the native authority state read directly from the
    // networking SDK together with the replicated logical holder. Sampling is independent of the
    // request/grant/completion path: adapters read SDK state every frame and only state changes are
    // stored. Times use this process's monotonic clock and must not be subtracted across processes.
    public class NativeAuthorityObservationRecorder : MonoBehaviour
    {
        public static NativeAuthorityObservationRecorder Instance { get; private set; }

        [Serializable]
        public class ObservationRow
        {
            public long sequence;
            public string timestampUtc;
            public long monotonicTicks;
            public int frame;
            public string objectId;
            public string observerUserId;
            public string logicalHolder;
            public string nativeOwner;
            public bool localLogicalAccess;
            public bool localNativeAuthority;
            public bool queueActive;
            public string sdkPredicate;
        }

        private readonly List<ObservationRow> rows = new();
        private readonly Dictionary<string, string> lastStateByObject = new();
        private readonly Dictionary<string, ObservationRow> lastRowByObject = new();
        private readonly string clockScopeId = Guid.NewGuid().ToString("N");
        private long sequence;

        protected virtual long ReadMonotonicTicks() => System.Diagnostics.Stopwatch.GetTimestamp();
        protected virtual long ClockFrequencyHz => System.Diagnostics.Stopwatch.Frequency;
        protected virtual int ReadFrame() => Time.frameCount;

        public IReadOnlyList<ObservationRow> Rows => rows;

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
            lastStateByObject.Clear();
            lastRowByObject.Clear();
            sequence = 0;
        }

        // Latest stored state of an object as observed on this participant.
        public bool TryGetLatest(string objectId, out ObservationRow row)
        {
            return lastRowByObject.TryGetValue(Normalize(objectId), out row);
        }

        // Returns true when the sample differs from the previous sample of the same object and was stored.
        public bool Observe(string objectId, string observerUserId, string logicalHolder, string nativeOwner,
            bool localLogicalAccess, bool localNativeAuthority, bool queueActive, string sdkPredicate)
        {
            objectId = Normalize(objectId);
            string state = string.Join("|", Normalize(observerUserId), Normalize(logicalHolder), Normalize(nativeOwner),
                localLogicalAccess, localNativeAuthority, queueActive);
            if (lastStateByObject.TryGetValue(objectId, out string previous) && previous == state)
                return false;

            lastStateByObject[objectId] = state;
            var row = new ObservationRow
            {
                sequence = ++sequence,
                timestampUtc = DateTime.UtcNow.ToString("o"),
                monotonicTicks = ReadMonotonicTicks(),
                frame = ReadFrame(),
                objectId = objectId,
                observerUserId = Normalize(observerUserId),
                logicalHolder = Normalize(logicalHolder),
                nativeOwner = Normalize(nativeOwner),
                localLogicalAccess = localLogicalAccess,
                localNativeAuthority = localNativeAuthority,
                queueActive = queueActive,
                sdkPredicate = sdkPredicate ?? string.Empty
            };
            rows.Add(row);
            lastRowByObject[objectId] = row;
            return true;
        }

        public void SaveObservationsCsvToFile(string filePath)
        {
            string directory = Path.GetDirectoryName(filePath);
            if (!string.IsNullOrWhiteSpace(directory))
                Directory.CreateDirectory(directory);

            var sb = new StringBuilder();
            sb.AppendLine("run_id,backend,scenario_id,sequence,timestamp_utc,monotonic_ticks,clock_frequency_hz,clock_scope_id,frame," +
                          "object_id,observer_user_id,logical_holder,native_owner,local_logical_access,local_native_authority," +
                          "observer_is_holder,queue_active,sdk_predicate");
            foreach (ObservationRow row in rows)
            {
                sb.AppendLine(
                    $"{Escape(GetRunId())},{Escape(GetBackend())},{Escape(GetScenarioId())}," +
                    $"{row.sequence},{Escape(row.timestampUtc)},{row.monotonicTicks.ToString(CultureInfo.InvariantCulture)}," +
                    $"{ClockFrequencyHz.ToString(CultureInfo.InvariantCulture)},{Escape(clockScopeId)},{row.frame}," +
                    $"{Escape(row.objectId)},{Escape(row.observerUserId)},{Escape(row.logicalHolder)},{Escape(row.nativeOwner)}," +
                    $"{row.localLogicalAccess},{row.localNativeAuthority},{row.observerUserId == row.logicalHolder}," +
                    $"{row.queueActive},{Escape(row.sdkPredicate)}");
            }

            File.WriteAllText(filePath, sb.ToString(), Encoding.UTF8);
        }

        private static string Normalize(string value) => string.IsNullOrWhiteSpace(value) ? "None" : value;

        private static string Escape(string value)
        {
            return string.IsNullOrEmpty(value) ? "" : $"\"{value.Replace("\"", "\"\"")}\"";
        }

        private static string GetRunId() =>
            EvaluationRunContext.Instance != null ? EvaluationRunContext.Instance.RunId : "UnknownRun";

        private static string GetBackend() =>
            EvaluationRunContext.Instance != null ? EvaluationRunContext.Instance.BackendName : "UnknownBackend";

        private static string GetScenarioId() =>
            EvaluationRunContext.Instance != null ? EvaluationRunContext.Instance.ScenarioId : "UnknownScenario";
    }
}
