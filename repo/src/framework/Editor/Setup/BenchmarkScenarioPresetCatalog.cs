using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using Thesis.Orchestration.Telemetry;
using UnityEngine;

namespace Thesis.Orchestration.EditorTools
{
    internal static class BenchmarkScenarioPresetCatalog
    {
        internal const string ProtocolVersion = "1.6";

        private static readonly string[] BackendNames =
        {
            "PUN2",
            "Fusion",
            "FishNet",
            "UnityNetcode",
            "Mirror"
        };

        internal sealed class Coverage
        {
            public int backendsWithValidRuns;
            public int balancedValidRuns;

            public string Label => $"{backendsWithValidRuns}/5  n={balancedValidRuns}";
        }

        internal sealed class Preset
        {
            public string id;
            public string title;
            public string objective;
            public OrchestrationAutomatedScenario scenario;
            public OrchestrationScenarioExecutionMode mode;
            public int participants;
            public int cycles;
            public float warmupSeconds;
            public float holdSeconds;
            public float cooldownSeconds;
            public float stepTimeoutSeconds;
            public bool preQueueNextUser;
            public bool manipulateChild;
            public string manipulatedChildName;
            public float manipulationStartDelaySeconds;
            public float manipulationDurationSeconds;
            public Vector3 manipulationEulerDelta;
            public bool validateFinalRotation;
            public float finalRotationSettleSeconds;
            public float rotationToleranceDegrees;

            public float EstimatedDurationSeconds =>
                warmupSeconds +
                cycles * (2f * holdSeconds + cooldownSeconds) +
                finalRotationSettleSeconds;

            public void ApplyTo(OrchestrationProjectConfig config)
            {
                config.enableAutomatedBenchmarkScenario = true;
                config.automatedScenario = scenario;
                config.automatedScenarioMode = mode;
                config.automatedScenarioAutoBeginSession = true;
                config.automatedScenarioAutoExportOnComplete = true;
                config.automatedScenarioIdOverride = id;
                config.automatedScenarioProtocolVersion = ProtocolVersion;
                config.automatedScenarioExpectedParticipants = participants;
                config.automatedScenarioRepeatCount = cycles;
                config.automatedScenarioWarmupSeconds = warmupSeconds;
                config.automatedScenarioHoldSeconds = holdSeconds;
                config.automatedScenarioCooldownSeconds = cooldownSeconds;
                config.automatedScenarioStepTimeoutSeconds = stepTimeoutSeconds;
                config.automatedScenarioPreQueueNextUser = preQueueNextUser;
                config.automatedScenarioManipulateChildObject = manipulateChild;
                config.automatedScenarioManipulatedChildName = manipulatedChildName;
                config.automatedScenarioManipulationStartDelaySeconds = manipulationStartDelaySeconds;
                config.automatedScenarioManipulationDurationSeconds = manipulationDurationSeconds;
                config.automatedScenarioManipulationEulerDelta = manipulationEulerDelta;
                config.automatedScenarioResetPerformanceSamplesAfterWarmup = true;
                config.automatedScenarioSuppressInfoLogsDuringMeasurement = true;
                config.automatedScenarioValidateFinalRotation = validateFinalRotation;
                config.automatedScenarioFinalRotationSettleSeconds = finalRotationSettleSeconds;
                config.automatedScenarioRotationToleranceDegrees = rotationToleranceDegrees;
            }

            public bool Matches(OrchestrationProjectConfig config)
            {
                return config != null &&
                       config.automatedScenario == scenario &&
                       config.automatedScenarioMode == mode &&
                       string.Equals(config.automatedScenarioIdOverride, id, StringComparison.OrdinalIgnoreCase) &&
                       string.Equals(config.automatedScenarioProtocolVersion, ProtocolVersion, StringComparison.OrdinalIgnoreCase) &&
                       config.automatedScenarioExpectedParticipants == participants &&
                       config.automatedScenarioRepeatCount == cycles &&
                       Mathf.Approximately(config.automatedScenarioWarmupSeconds, warmupSeconds) &&
                       Mathf.Approximately(config.automatedScenarioHoldSeconds, holdSeconds) &&
                       Mathf.Approximately(config.automatedScenarioCooldownSeconds, cooldownSeconds) &&
                       Mathf.Approximately(config.automatedScenarioStepTimeoutSeconds, stepTimeoutSeconds) &&
                       config.automatedScenarioPreQueueNextUser == preQueueNextUser &&
                       config.automatedScenarioManipulateChildObject == manipulateChild &&
                       string.Equals(config.automatedScenarioManipulatedChildName, manipulatedChildName, StringComparison.Ordinal) &&
                       Mathf.Approximately(
                           config.automatedScenarioManipulationStartDelaySeconds,
                           manipulationStartDelaySeconds) &&
                       Mathf.Approximately(
                           config.automatedScenarioManipulationDurationSeconds,
                           manipulationDurationSeconds) &&
                       Vector3.Distance(config.automatedScenarioManipulationEulerDelta, manipulationEulerDelta) <= 0.001f &&
                       config.automatedScenarioValidateFinalRotation == validateFinalRotation &&
                       Mathf.Approximately(
                           config.automatedScenarioFinalRotationSettleSeconds,
                           finalRotationSettleSeconds) &&
                       Mathf.Approximately(
                           config.automatedScenarioRotationToleranceDegrees,
                           rotationToleranceDegrees);
            }
        }

        internal static readonly IReadOnlyList<Preset> Presets = new[]
        {
            new Preset
            {
                id = "S1_Baseline_FCFS_2Users_3Cycles_10s_Turret60",
                title = "Baseline FCFS",
                objective = "Reference ownership handoff, queue latency, fairness, and child-transform convergence.",
                scenario = OrchestrationAutomatedScenario.OwnershipPingPong2Users,
                mode = OrchestrationScenarioExecutionMode.ClosedLoop,
                participants = 2,
                cycles = 3,
                warmupSeconds = 5f,
                holdSeconds = 10f,
                cooldownSeconds = 1f,
                stepTimeoutSeconds = 15f,
                preQueueNextUser = true,
                manipulateChild = true,
                manipulatedChildName = "Controlled Turret",
                manipulationStartDelaySeconds = 1f,
                manipulationDurationSeconds = 6f,
                manipulationEulerDelta = new Vector3(0f, 60f, 0f),
                validateFinalRotation = true,
                finalRotationSettleSeconds = 1f,
                rotationToleranceDegrees = 2f
            },
            new Preset
            {
                id = "S2_Contention_Burst_2Users_5Cycles_5s_Turret30",
                title = "Contention Burst",
                objective = "Simultaneous requests test deterministic queue capture, transfer correctness, and fairness under contention.",
                scenario = OrchestrationAutomatedScenario.SimultaneousContention2Users,
                mode = OrchestrationScenarioExecutionMode.ClosedLoop,
                participants = 2,
                cycles = 5,
                warmupSeconds = 5f,
                holdSeconds = 5f,
                cooldownSeconds = 1f,
                stepTimeoutSeconds = 15f,
                preQueueNextUser = false,
                manipulateChild = true,
                manipulatedChildName = "Controlled Turret",
                manipulationStartDelaySeconds = 0.5f,
                manipulationDurationSeconds = 3f,
                manipulationEulerDelta = new Vector3(0f, 30f, 0f),
                validateFinalRotation = true,
                finalRotationSettleSeconds = 1f,
                rotationToleranceDegrees = 2f
            },
            new Preset
            {
                id = "S3_Rapid_Handoff_2Users_10Cycles_2.5s_Turret15",
                title = "Rapid Handoff",
                objective = "Repeated short ownership intervals expose transfer instability, duplicate grants, and queue drift.",
                scenario = OrchestrationAutomatedScenario.RapidOwnershipHandoffs2Users,
                mode = OrchestrationScenarioExecutionMode.ClosedLoop,
                participants = 2,
                cycles = 10,
                warmupSeconds = 5f,
                holdSeconds = 2.5f,
                cooldownSeconds = 0.25f,
                stepTimeoutSeconds = 10f,
                preQueueNextUser = true,
                manipulateChild = true,
                manipulatedChildName = "Controlled Turret",
                manipulationStartDelaySeconds = 0.25f,
                manipulationDurationSeconds = 1.5f,
                manipulationEulerDelta = new Vector3(0f, 15f, 0f),
                validateFinalRotation = true,
                finalRotationSettleSeconds = 1f,
                rotationToleranceDegrees = 2f
            },
            new Preset
            {
                id = "S4_Transform_Replication_2Users_5Cycles_8s_Turret90",
                title = "Transform Replication",
                objective = "Large controlled rotations emphasize child-state convergence while ownership alternates normally.",
                scenario = OrchestrationAutomatedScenario.TransformReplication2Users,
                mode = OrchestrationScenarioExecutionMode.ClosedLoop,
                participants = 2,
                cycles = 5,
                warmupSeconds = 5f,
                holdSeconds = 8f,
                cooldownSeconds = 1f,
                stepTimeoutSeconds = 15f,
                preQueueNextUser = true,
                manipulateChild = true,
                manipulatedChildName = "Controlled Turret",
                manipulationStartDelaySeconds = 0.5f,
                manipulationDurationSeconds = 6f,
                manipulationEulerDelta = new Vector3(0f, 90f, 0f),
                validateFinalRotation = true,
                finalRotationSettleSeconds = 1.5f,
                rotationToleranceDegrees = 2f
            },
            new Preset
            {
                id = "S5_OpenLoop_Stress_2Users_20Cycles_0_5s",
                title = "Open-Loop Stress",
                objective = "High-frequency request/release commands measure resilience without waiting for state convergence.",
                scenario = OrchestrationAutomatedScenario.OpenLoopRequestStress2Users,
                mode = OrchestrationScenarioExecutionMode.OpenLoop,
                participants = 2,
                cycles = 20,
                warmupSeconds = 5f,
                holdSeconds = 0.5f,
                cooldownSeconds = 0.1f,
                stepTimeoutSeconds = 10f,
                preQueueNextUser = false,
                manipulateChild = false,
                manipulatedChildName = "Controlled Turret",
                manipulationStartDelaySeconds = 0f,
                manipulationDurationSeconds = 0.25f,
                manipulationEulerDelta = Vector3.zero,
                validateFinalRotation = false,
                finalRotationSettleSeconds = 1f,
                rotationToleranceDegrees = 2f
            }
        };

        internal static Preset FindSelected(OrchestrationProjectConfig config)
        {
            foreach (Preset preset in Presets)
            {
                if (preset.Matches(config))
                    return preset;
            }

            return null;
        }

        internal static Coverage GetCoverage(string scenarioId)
        {
            string exportsRoot = EvaluationExportPathUtility.GetExportsRoot();
            int backendsWithRuns = 0;
            int balancedRuns = int.MaxValue;

            foreach (string backend in BackendNames)
            {
                string scenarioFolder = Path.Combine(exportsRoot, backend, scenarioId);
                int validRuns = CountValidRuns(scenarioFolder);
                if (validRuns > 0)
                    backendsWithRuns++;
                balancedRuns = Math.Min(balancedRuns, validRuns);
            }

            return new Coverage
            {
                backendsWithValidRuns = backendsWithRuns,
                balancedValidRuns = balancedRuns == int.MaxValue ? 0 : balancedRuns
            };
        }

        internal static string ExportProtocolManifest()
        {
            string folder = Path.Combine(EvaluationExportPathUtility.GetExportsRoot(), "ProtocolDefinitions");
            Directory.CreateDirectory(folder);
            string path = Path.Combine(folder, $"benchmark_protocol_catalog_v{ProtocolVersion.Replace('.', '_')}.csv");

            StringBuilder csv = new StringBuilder();
            csv.AppendLine(
                "protocol_version,scenario_id,title,objective,runner,mode,participants,cycles,warmup_seconds," +
                "hold_seconds,cooldown_seconds,step_timeout_seconds,prequeue_next_user,manipulate_child," +
                "manipulated_child,manipulation_start_seconds,manipulation_duration_seconds," +
                "rotation_delta_x,rotation_delta_y,rotation_delta_z,validate_final_rotation," +
                "settle_seconds,rotation_tolerance_degrees,estimated_duration_seconds," +
                "final_rotation_is_outcome_metric");

            foreach (Preset preset in Presets)
            {
                csv.AppendLine(
                    $"{Quote(ProtocolVersion)},{Quote(preset.id)},{Quote(preset.title)},{Quote(preset.objective)}," +
                    $"{Quote(preset.scenario.ToString())},{Quote(preset.mode.ToString())},{preset.participants},{preset.cycles}," +
                    $"{Format(preset.warmupSeconds)},{Format(preset.holdSeconds)},{Format(preset.cooldownSeconds)}," +
                    $"{Format(preset.stepTimeoutSeconds)},{preset.preQueueNextUser},{preset.manipulateChild}," +
                    $"{Quote(preset.manipulatedChildName)},{Format(preset.manipulationStartDelaySeconds)}," +
                    $"{Format(preset.manipulationDurationSeconds)},{Format(preset.manipulationEulerDelta.x)}," +
                    $"{Format(preset.manipulationEulerDelta.y)},{Format(preset.manipulationEulerDelta.z)}," +
                    $"{preset.validateFinalRotation},{Format(preset.finalRotationSettleSeconds)}," +
                    $"{Format(preset.rotationToleranceDegrees)},{Format(preset.EstimatedDurationSeconds)},True");
            }

            File.WriteAllText(path, csv.ToString(), Encoding.UTF8);
            return path;
        }

        private static string Quote(string value)
        {
            return $"\"{(value ?? string.Empty).Replace("\"", "\"\"")}\"";
        }

        private static string Format(float value)
        {
            return value.ToString("0.###", CultureInfo.InvariantCulture);
        }

        private static int CountValidRuns(string scenarioFolder)
        {
            if (!Directory.Exists(scenarioFolder))
                return 0;

            int count = 0;
            foreach (string runFolder in Directory.GetDirectories(scenarioFolder))
            {
                string folderName = Path.GetFileName(runFolder);
                string scenarioSummary = Path.Combine(runFolder, "scenario_metrics_summary.csv");
                bool complete =
                    File.Exists(scenarioSummary) &&
                    File.Exists(Path.Combine(runFolder, "run_master_summary.csv")) &&
                    File.Exists(Path.Combine(runFolder, "runtime_performance_summary.csv"));
                if (!complete)
                    continue;

                if (folderName.StartsWith("Run_", StringComparison.OrdinalIgnoreCase) &&
                    !File.Exists(Path.Combine(runFolder, "export_manifest.csv")))
                {
                    continue;
                }

                string protocolVersion = ReadCsvValue(
                    Path.Combine(runFolder, "run_metadata.csv"),
                    "protocol_version");
                if (!string.Equals(protocolVersion, ProtocolVersion, StringComparison.OrdinalIgnoreCase))
                    continue;

                if (ReadScenarioValidity(scenarioSummary))
                    count++;
            }

            return count;
        }

        private static bool ReadScenarioValidity(string filePath)
        {
            string rawValidity = ReadCsvValue(filePath, "scenario_valid");
            return bool.TryParse(rawValidity, out bool valid) && valid;
        }

        private static string ReadCsvValue(string filePath, string columnName)
        {
            if (!File.Exists(filePath))
                return string.Empty;

            string[] lines = File.ReadAllLines(filePath);
            if (lines.Length < 2)
                return string.Empty;

            string[] headers = SplitCsvLine(lines[0]);
            string[] values = SplitCsvLine(lines[1]);
            int valueIndex = Array.IndexOf(headers, columnName);
            return valueIndex >= 0 && valueIndex < values.Length
                ? values[valueIndex].Trim('"')
                : string.Empty;
        }

        private static string[] SplitCsvLine(string line)
        {
            List<string> fields = new List<string>();
            StringBuilder current = new StringBuilder();
            bool quoted = false;
            for (int index = 0; index < line.Length; index++)
            {
                char character = line[index];
                if (character == '"')
                {
                    quoted = !quoted;
                    continue;
                }

                if (character == ',' && !quoted)
                {
                    fields.Add(current.ToString());
                    current.Clear();
                    continue;
                }

                current.Append(character);
            }

            fields.Add(current.ToString());
            return fields.ToArray();
        }
    }
}
