using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using UnityEngine;

namespace Thesis.Orchestration.Telemetry
{
    public class ScenarioMetricsRecorder : MonoBehaviour
    {
        public static ScenarioMetricsRecorder Instance { get; private set; }

        [Serializable]
        public class ScenarioEventRow
        {
            public string runId;
            public string backend;
            public string scenarioId;
            public string timestampUtc;
            public double scenarioElapsedMs;
            public int sequence;
            public int cycle;
            public string mode;
            public string phase;
            public string role;
            public string actorUserId;
            public string command;
            public double scheduledOffsetMs;
            public double actualOffsetMs;
            public string expectedHolder;
            public string actualHolder;
            public bool success;
            public string details;
        }

        [Serializable]
        public class ScenarioAssertionRow
        {
            public string runId;
            public string backend;
            public string scenarioId;
            public string timestampUtc;
            public double scenarioElapsedMs;
            public int sequence;
            public int cycle;
            public string assertionId;
            public string role;
            public string expectedValue;
            public string actualValue;
            public bool passed;
            public double timeoutMs;
            public string details;
            public bool requiredForValidity;
        }

        [Serializable]
        public class ScenarioMetricsSummary
        {
            public string runId;
            public string backend;
            public string scenarioId;
            public string mode;
            public bool scenarioValid;
            public int expectedParticipantCount;
            public int observedParticipantCount;
            public int expectedCycles;
            public int completedCycles;
            public int commandCount;
            public int failedCommandCount;
            public int assertionCount;
            public int failedAssertionCount;
            public int validityAssertionCount;
            public int failedValidityAssertionCount;
            public int outcomeAssertionCount;
            public int failedOutcomeAssertionCount;
            public bool hasFinalRotationOutcome;
            public bool finalRotationConverged;
            public double finalRotationErrorDegrees;
            public double totalDurationMs;
            public string notes;
        }

        [Header("Debug")]
        [SerializeField] private bool verboseLogs = true;

        private readonly List<ScenarioEventRow> events = new();
        private readonly List<ScenarioAssertionRow> assertions = new();
        private double scenarioStartRealtime;
        private int sequence;
        private string mode = "Manual";
        private int expectedParticipants;
        private int observedParticipants;
        private int expectedCycles;
        private int completedCycles;
        private bool hasFinalRotationOutcome;
        private bool finalRotationConverged;
        private double finalRotationErrorDegrees;
        private string notes = "";

        public IReadOnlyList<ScenarioEventRow> Events => events;
        public IReadOnlyList<ScenarioAssertionRow> Assertions => assertions;

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }

            Instance = this;
            scenarioStartRealtime = Time.realtimeSinceStartupAsDouble;
        }

        private void OnDestroy()
        {
            if (Instance == this)
                Instance = null;
        }

        public void ClearAll()
        {
            events.Clear();
            assertions.Clear();
            sequence = 0;
            scenarioStartRealtime = Time.realtimeSinceStartupAsDouble;
            mode = "Manual";
            expectedParticipants = 0;
            observedParticipants = 0;
            expectedCycles = 0;
            completedCycles = 0;
            hasFinalRotationOutcome = false;
            finalRotationConverged = false;
            finalRotationErrorDegrees = 0d;
            notes = "";
        }

        public void MarkScenarioStarted(string scenarioMode, int expectedParticipantCount, int expectedCycleCount)
        {
            mode = scenarioMode;
            expectedParticipants = expectedParticipantCount;
            expectedCycles = expectedCycleCount;
            scenarioStartRealtime = Time.realtimeSinceStartupAsDouble;

            RecordEvent(
                cycle: 0,
                phase: "Scenario",
                role: "All",
                actorUserId: "",
                command: "ScenarioStarted",
                scheduledOffsetMs: 0d,
                expectedHolder: "",
                actualHolder: "",
                success: true,
                details: $"expectedParticipants={expectedParticipantCount}; expectedCycles={expectedCycleCount}");
        }

        public void MarkObservedParticipants(int count)
        {
            observedParticipants = Math.Max(observedParticipants, count);
        }

        public void MarkCycleCompleted(int cycle)
        {
            completedCycles = Math.Max(completedCycles, cycle);
        }

        public void AppendNote(string message)
        {
            if (string.IsNullOrWhiteSpace(message))
                return;

            if (!string.IsNullOrWhiteSpace(notes))
                notes += " | ";

            notes += message.Trim();
        }

        public void RecordFinalRotationOutcome(double angleErrorDegrees, bool converged)
        {
            hasFinalRotationOutcome = true;
            finalRotationConverged = converged;
            finalRotationErrorDegrees =
                double.IsNaN(angleErrorDegrees) || double.IsInfinity(angleErrorDegrees)
                    ? -1d
                    : Math.Max(0d, angleErrorDegrees);
        }

        public void RecordEvent(
            int cycle,
            string phase,
            string role,
            string actorUserId,
            string command,
            double scheduledOffsetMs,
            string expectedHolder,
            string actualHolder,
            bool success,
            string details = "")
        {
            ScenarioEventRow row = new ScenarioEventRow
            {
                runId = GetRunId(),
                backend = GetBackend(),
                scenarioId = GetScenarioId(),
                timestampUtc = DateTime.UtcNow.ToString("o"),
                scenarioElapsedMs = GetScenarioElapsedMs(),
                sequence = ++sequence,
                cycle = cycle,
                mode = mode,
                phase = phase,
                role = role,
                actorUserId = actorUserId,
                command = command,
                scheduledOffsetMs = scheduledOffsetMs,
                actualOffsetMs = GetScenarioElapsedMs(),
                expectedHolder = expectedHolder,
                actualHolder = actualHolder,
                success = success,
                details = details
            };

            events.Add(row);
            Log($"{row.command} | role={row.role} | success={row.success}");
        }

        public void RecordAssertion(
            int cycle,
            string assertionId,
            string role,
            string expectedValue,
            string actualValue,
            bool passed,
            double timeoutMs,
            string details = "",
            bool requiredForValidity = true)
        {
            ScenarioAssertionRow row = new ScenarioAssertionRow
            {
                runId = GetRunId(),
                backend = GetBackend(),
                scenarioId = GetScenarioId(),
                timestampUtc = DateTime.UtcNow.ToString("o"),
                scenarioElapsedMs = GetScenarioElapsedMs(),
                sequence = ++sequence,
                cycle = cycle,
                assertionId = assertionId,
                role = role,
                expectedValue = expectedValue,
                actualValue = actualValue,
                passed = passed,
                timeoutMs = timeoutMs,
                details = details,
                requiredForValidity = requiredForValidity
            };

            assertions.Add(row);
            if (!passed)
            {
                string category = requiredForValidity ? "Validity" : "Outcome";
                AppendNote($"[{category}] {assertionId} failed for {role}: expected={expectedValue}, actual={actualValue}");
            }

            Log(
                $"{row.assertionId} | role={row.role} | passed={row.passed} | " +
                $"requiredForValidity={row.requiredForValidity}");
        }

        public ScenarioMetricsSummary BuildSummary()
        {
            int failedEvents = events.Count(x => !x.success);
            int failedAssertions = assertions.Count(x => !x.passed);
            int validityAssertions = assertions.Count(x => x.requiredForValidity);
            int failedValidityAssertions = assertions.Count(x => x.requiredForValidity && !x.passed);
            int outcomeAssertions = assertions.Count(x => !x.requiredForValidity);
            int failedOutcomeAssertions = assertions.Count(x => !x.requiredForValidity && !x.passed);
            bool valid =
                failedEvents == 0 &&
                failedValidityAssertions == 0 &&
                (expectedParticipants <= 0 || observedParticipants >= expectedParticipants) &&
                (expectedCycles <= 0 || completedCycles >= expectedCycles);

            return new ScenarioMetricsSummary
            {
                runId = GetRunId(),
                backend = GetBackend(),
                scenarioId = GetScenarioId(),
                mode = mode,
                scenarioValid = valid,
                expectedParticipantCount = expectedParticipants,
                observedParticipantCount = observedParticipants,
                expectedCycles = expectedCycles,
                completedCycles = completedCycles,
                commandCount = events.Count,
                failedCommandCount = failedEvents,
                assertionCount = assertions.Count,
                failedAssertionCount = failedAssertions,
                validityAssertionCount = validityAssertions,
                failedValidityAssertionCount = failedValidityAssertions,
                outcomeAssertionCount = outcomeAssertions,
                failedOutcomeAssertionCount = failedOutcomeAssertions,
                hasFinalRotationOutcome = hasFinalRotationOutcome,
                finalRotationConverged = finalRotationConverged,
                finalRotationErrorDegrees = finalRotationErrorDegrees,
                totalDurationMs = GetScenarioElapsedMs(),
                notes = notes
            };
        }

        public void SaveScenarioEventsCsvToFile(string filePath)
        {
            EnsureDirectory(filePath);
            StringBuilder sb = new StringBuilder();
            sb.AppendLine("run_id,backend,scenario_id,timestamp_utc,scenario_elapsed_ms,sequence,cycle,mode,phase,role,actor_user_id,command,scheduled_offset_ms,actual_offset_ms,expected_holder,actual_holder,success,details");

            foreach (ScenarioEventRow row in events)
            {
                sb.AppendLine(
                    $"{Q(row.runId)},{Q(row.backend)},{Q(row.scenarioId)},{Q(row.timestampUtc)},{F(row.scenarioElapsedMs)},{row.sequence},{row.cycle},{Q(row.mode)},{Q(row.phase)},{Q(row.role)},{Q(row.actorUserId)},{Q(row.command)},{F(row.scheduledOffsetMs)},{F(row.actualOffsetMs)},{Q(row.expectedHolder)},{Q(row.actualHolder)},{row.success},{Q(row.details)}");
            }

            File.WriteAllText(filePath, sb.ToString(), Encoding.UTF8);
        }

        public void SaveScenarioAssertionsCsvToFile(string filePath)
        {
            EnsureDirectory(filePath);
            StringBuilder sb = new StringBuilder();
            sb.AppendLine("run_id,backend,scenario_id,timestamp_utc,scenario_elapsed_ms,sequence,cycle,assertion_id,role,expected_value,actual_value,passed,timeout_ms,details,required_for_validity");

            foreach (ScenarioAssertionRow row in assertions)
            {
                sb.AppendLine(
                    $"{Q(row.runId)},{Q(row.backend)},{Q(row.scenarioId)},{Q(row.timestampUtc)},{F(row.scenarioElapsedMs)},{row.sequence},{row.cycle},{Q(row.assertionId)},{Q(row.role)},{Q(row.expectedValue)},{Q(row.actualValue)},{row.passed},{F(row.timeoutMs)},{Q(row.details)},{row.requiredForValidity}");
            }

            File.WriteAllText(filePath, sb.ToString(), Encoding.UTF8);
        }

        public void SaveScenarioMetricsSummaryCsvToFile(string filePath)
        {
            EnsureDirectory(filePath);
            ScenarioMetricsSummary s = BuildSummary();
            StringBuilder sb = new StringBuilder();
            sb.AppendLine("run_id,backend,scenario_id,mode,scenario_valid,expected_participant_count,observed_participant_count,expected_cycles,completed_cycles,command_count,failed_command_count,assertion_count,failed_assertion_count,total_duration_ms,notes,validity_assertion_count,failed_validity_assertion_count,outcome_assertion_count,failed_outcome_assertion_count,has_final_rotation_outcome,final_rotation_converged,final_rotation_error_degrees");
            sb.AppendLine($"{Q(s.runId)},{Q(s.backend)},{Q(s.scenarioId)},{Q(s.mode)},{s.scenarioValid},{s.expectedParticipantCount},{s.observedParticipantCount},{s.expectedCycles},{s.completedCycles},{s.commandCount},{s.failedCommandCount},{s.assertionCount},{s.failedAssertionCount},{F(s.totalDurationMs)},{Q(s.notes)},{s.validityAssertionCount},{s.failedValidityAssertionCount},{s.outcomeAssertionCount},{s.failedOutcomeAssertionCount},{s.hasFinalRotationOutcome},{s.finalRotationConverged},{F(s.finalRotationErrorDegrees)}");
            File.WriteAllText(filePath, sb.ToString(), Encoding.UTF8);
        }

        private double GetScenarioElapsedMs()
        {
            return Math.Max(0d, (Time.realtimeSinceStartupAsDouble - scenarioStartRealtime) * 1000d);
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

            Debug.Log($"[ScenarioMetricsRecorder] {message}", this);
        }
    }
}
