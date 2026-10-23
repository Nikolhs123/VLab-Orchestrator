using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using Thesis.Orchestration.EditorTools;
using Thesis.Orchestration.Runtime;
using UnityEngine;

namespace Thesis.Orchestration.Telemetry
{
    // Writes execution_manifest.csv (key,value) into every export: build provenance captured at player
    // build time (source hashes, revision), the executing environment, the protocol/preset values in
    // force, and SDK versions and network settings reported by the active backend at run time.
    // Values that could not be read are written as "unavailable", never omitted silently.
    public static class ExecutionManifestWriter
    {
        public const string BuildProvenanceResource = "VLabOrchestrator/BuildProvenance";

        public static void Write(string filePath, EvaluationRunContext context, string observerUserId, string exportRole)
        {
            var values = new SortedDictionary<string, string>(StringComparer.Ordinal);

            Add(values, "manifest.schema", "1");
            Add(values, "run.id", context != null ? context.RunId : "");
            Add(values, "run.backend", context != null ? context.BackendName : "");
            Add(values, "run.scenario_id", context != null ? context.ScenarioId : "");
            Add(values, "run.session_name", context != null ? context.SessionName : "");
            Add(values, "run.session_run_id", context != null ? context.SessionRunId : "");
            Add(values, "run.observer_user_id", observerUserId);
            Add(values, "run.export_role", exportRole);
            Add(values, "run.exported_utc", DateTime.UtcNow.ToString("o"));

            AddBuildProvenance(values);

            Add(values, "env.unity_version", Application.unityVersion);
            Add(values, "env.platform", Application.platform.ToString());
            Add(values, "env.is_editor", Application.isEditor.ToString());
            Add(values, "env.is_batch_mode", Application.isBatchMode.ToString());
            Add(values, "env.machine_name", Environment.MachineName);
            Add(values, "env.os", SystemInfo.operatingSystem);
            Add(values, "env.processor", SystemInfo.processorType);
            Add(values, "env.processor_count", SystemInfo.processorCount.ToString(CultureInfo.InvariantCulture));
            Add(values, "env.graphics_device", SystemInfo.graphicsDeviceName);
            Add(values, "env.target_frame_rate", Application.targetFrameRate.ToString(CultureInfo.InvariantCulture));
            Add(values, "env.vsync_count", QualitySettings.vSyncCount.ToString(CultureInfo.InvariantCulture));
            Add(values, "env.command_line", string.Join(" ", Environment.GetCommandLineArgs()));
            Add(values, "env.stopwatch_frequency_hz", System.Diagnostics.Stopwatch.Frequency.ToString(CultureInfo.InvariantCulture));
            Add(values, "env.stopwatch_high_resolution", System.Diagnostics.Stopwatch.IsHighResolution.ToString());

            AddProtocol(values);
            AddBackend(values);

            string directory = Path.GetDirectoryName(filePath);
            if (!string.IsNullOrWhiteSpace(directory))
                Directory.CreateDirectory(directory);

            var sb = new StringBuilder("key,value\n");
            foreach (KeyValuePair<string, string> pair in values)
                sb.Append(Q(pair.Key)).Append(',').Append(Q(pair.Value)).Append('\n');
            File.WriteAllText(filePath, sb.ToString(), Encoding.UTF8);
        }

        private static void AddBuildProvenance(IDictionary<string, string> values)
        {
            TextAsset provenance = Resources.Load<TextAsset>(BuildProvenanceResource);
            if (provenance == null)
            {
                Add(values, "build.provenance", Application.isEditor ? "editor-play-mode (no player build)" : "unavailable");
                return;
            }

            foreach (string line in provenance.text.Split('\n'))
            {
                int separator = line.IndexOf('=');
                if (separator > 0)
                    Add(values, "build." + line.Substring(0, separator).Trim(), line.Substring(separator + 1).Trim());
            }
        }

        private static void AddProtocol(IDictionary<string, string> values)
        {
            OrchestrationProjectConfig config =
                Resources.Load<OrchestrationProjectConfig>("AIMultiplayerAssistant/OrchestrationProjectConfig");
            if (config == null)
            {
                Add(values, "protocol.config", "unavailable");
                return;
            }

            Add(values, "protocol.version", config.automatedScenarioProtocolVersion);
            Add(values, "protocol.backend_type", config.backendType.ToString());
            Add(values, "protocol.scenario", config.automatedScenario.ToString());
            Add(values, "protocol.scenario_id_override", config.automatedScenarioIdOverride);
            Add(values, "protocol.mode", config.automatedScenarioMode.ToString());
            Add(values, "protocol.expected_participants", I(config.automatedScenarioExpectedParticipants));
            Add(values, "protocol.repeat_count", I(config.automatedScenarioRepeatCount));
            Add(values, "protocol.warmup_seconds", F(config.automatedScenarioWarmupSeconds));
            Add(values, "protocol.hold_seconds", F(config.automatedScenarioHoldSeconds));
            Add(values, "protocol.cooldown_seconds", F(config.automatedScenarioCooldownSeconds));
            Add(values, "protocol.step_timeout_seconds", F(config.automatedScenarioStepTimeoutSeconds));
            Add(values, "protocol.pre_queue_next_user", config.automatedScenarioPreQueueNextUser.ToString());
            Add(values, "protocol.manipulate_child", config.automatedScenarioManipulateChildObject.ToString());
            Add(values, "protocol.manipulated_child_name", config.automatedScenarioManipulatedChildName);
            Add(values, "protocol.manipulation_start_delay_seconds", F(config.automatedScenarioManipulationStartDelaySeconds));
            Add(values, "protocol.manipulation_duration_seconds", F(config.automatedScenarioManipulationDurationSeconds));
            Add(values, "protocol.manipulation_euler_delta", config.automatedScenarioManipulationEulerDelta.ToString("F3"));
            Add(values, "protocol.validate_final_rotation", config.automatedScenarioValidateFinalRotation.ToString());
            Add(values, "protocol.final_rotation_settle_seconds", F(config.automatedScenarioFinalRotationSettleSeconds));
            Add(values, "protocol.rotation_tolerance_degrees", F(config.automatedScenarioRotationToleranceDegrees));
            Add(values, "protocol.access_control_probes", config.AccessControlProbesEnabled.ToString());
            Add(values, "protocol.resolved_scenario_id", config.ResolveAutomatedScenarioId());
        }

        private static void AddBackend(IDictionary<string, string> values)
        {
            OrchestrationProjectConfig config =
                Resources.Load<OrchestrationProjectConfig>("AIMultiplayerAssistant/OrchestrationProjectConfig");
            IOrchestrationBackendRuntime runtime = config != null ? BackendRuntimeResolver.Resolve(config.backendType) : null;
            if (runtime is IExecutionEnvironmentDescriptor descriptor)
            {
                var backendValues = new Dictionary<string, string>();
                try
                {
                    descriptor.DescribeExecutionEnvironment(backendValues);
                }
                catch (Exception exception)
                {
                    backendValues["descriptor_error"] = exception.Message;
                }

                foreach (KeyValuePair<string, string> pair in backendValues)
                    Add(values, "backend." + pair.Key, pair.Value);
            }
            else
            {
                Add(values, "backend.descriptor", "unavailable");
            }
        }

        private static void Add(IDictionary<string, string> values, string key, string value)
        {
            values[key] = string.IsNullOrEmpty(value) ? "unavailable" : value;
        }

        private static string I(int value) => value.ToString(CultureInfo.InvariantCulture);
        private static string F(float value) => value.ToString("0.###", CultureInfo.InvariantCulture);
        private static string Q(string value) => $"\"{(value ?? string.Empty).Replace("\"", "\"\"")}\"";
    }
}
