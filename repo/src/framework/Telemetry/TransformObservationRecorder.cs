using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using UnityEngine;

namespace Thesis.Orchestration.Telemetry
{
    // Records, on each participant, the locally replicated orientation of the controlled object
    // against the protocol-expected orientation. Boundary rows are written when a phase starts (after
    // the holder is observed) and when its hold interval ends (before release); periodic samples
    // cover the whole measured window. Every phase rotates by at most the configured delta, so a
    // per-phase comparison is not aliased by full turns, unlike a single final-yaw comparison.
    // Clock values are process-local (monotonic ticks, frame); do not subtract across participants.
    public class TransformObservationRecorder : MonoBehaviour
    {
        public static TransformObservationRecorder Instance { get; private set; }

        [Serializable]
        public class ObservationRow
        {
            public long sequence;
            public string kind;
            public string timestampUtc;
            public long monotonicTicks;
            public int frame;
            public int cycle;
            public int phaseIndex;
            public string role;
            public string actorUserId;
            public string observerUserId;
            public string holderUserId;
            public string target;
            public Quaternion expected;
            public Quaternion actual;
            public float angleErrorDegrees;
        }

        private readonly List<ObservationRow> rows = new();
        private readonly string clockScopeId = Guid.NewGuid().ToString("N");
        private long sequence;

        protected virtual long ReadMonotonicTicks() => System.Diagnostics.Stopwatch.GetTimestamp();
        protected virtual long ClockFrequencyHz => System.Diagnostics.Stopwatch.Frequency;

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
            sequence = 0;
        }

        public void Record(string kind, int cycle, int phaseIndex, string role, string actorUserId,
            string observerUserId, string holderUserId, string target, Quaternion expected, Quaternion actual)
        {
            rows.Add(new ObservationRow
            {
                sequence = ++sequence,
                kind = kind ?? string.Empty,
                timestampUtc = DateTime.UtcNow.ToString("o"),
                monotonicTicks = ReadMonotonicTicks(),
                frame = Time.frameCount,
                cycle = cycle,
                phaseIndex = phaseIndex,
                role = role ?? string.Empty,
                actorUserId = actorUserId ?? string.Empty,
                observerUserId = observerUserId ?? string.Empty,
                holderUserId = string.IsNullOrWhiteSpace(holderUserId) ? "None" : holderUserId,
                target = target ?? string.Empty,
                expected = expected,
                actual = actual,
                angleErrorDegrees = Quaternion.Angle(expected, actual)
            });
        }

        public void SaveObservationsCsvToFile(string filePath)
        {
            string directory = Path.GetDirectoryName(filePath);
            if (!string.IsNullOrWhiteSpace(directory))
                Directory.CreateDirectory(directory);

            var sb = new StringBuilder();
            sb.AppendLine("run_id,backend,scenario_id,sequence,kind,timestamp_utc,monotonic_ticks,clock_frequency_hz," +
                          "clock_scope_id,frame,cycle,phase_index,role,actor_user_id,observer_user_id,observer_is_actor," +
                          "holder_user_id,target,expected_euler_x,expected_euler_y,expected_euler_z," +
                          "actual_euler_x,actual_euler_y,actual_euler_z,angle_error_deg");
            foreach (ObservationRow row in rows)
            {
                Vector3 e = row.expected.eulerAngles;
                Vector3 a = row.actual.eulerAngles;
                sb.AppendLine(
                    $"{Escape(GetRunId())},{Escape(GetBackend())},{Escape(GetScenarioId())},{row.sequence},{row.kind}," +
                    $"{Escape(row.timestampUtc)},{row.monotonicTicks.ToString(CultureInfo.InvariantCulture)}," +
                    $"{ClockFrequencyHz.ToString(CultureInfo.InvariantCulture)},{Escape(clockScopeId)},{row.frame}," +
                    $"{row.cycle},{row.phaseIndex},{Escape(row.role)},{Escape(row.actorUserId)},{Escape(row.observerUserId)}," +
                    $"{row.observerUserId == row.actorUserId},{Escape(row.holderUserId)},{Escape(row.target)}," +
                    $"{F(e.x)},{F(e.y)},{F(e.z)},{F(a.x)},{F(a.y)},{F(a.z)},{F(row.angleErrorDegrees)}");
            }

            File.WriteAllText(filePath, sb.ToString(), Encoding.UTF8);
        }

        private static string F(float value) => value.ToString("0.####", CultureInfo.InvariantCulture);

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
