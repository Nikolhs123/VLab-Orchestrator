using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using Thesis.Orchestration.EditorTools;
using UnityEngine;

namespace Thesis.Orchestration.Telemetry
{
    /// <summary>
    /// Central telemetry logger for the prototype.
    /// Stores structured telemetry entries and also aggregates lightweight
    /// interaction analytics for later export / research analysis.
    /// </summary>
    public class TelemetryLoggerCore : MonoBehaviour
    {
        public static TelemetryLoggerCore Instance { get; private set; }

        [Header("Settings")]
        [SerializeField] private int maxEntries = 500;
        [SerializeField] private bool verboseLogs = true;

        private readonly List<TelemetryEventData> events = new();

        // ----------------------------------------------------
        // Interaction analytics
        // ----------------------------------------------------

        private readonly Dictionary<string, string> lastManipulatedObjectByUserId = new();
        private readonly Dictionary<string, int> interactionCountByObjectId = new();
        private readonly Dictionary<string, int> interactionCountByUserAndObjectKey = new();

        private readonly Dictionary<string, int> regrabCountByObjectId = new();
        private readonly Dictionary<string, int> regrabCountByUserId = new();

        private readonly Dictionary<string, int> repositionCountByObjectId = new();
        private readonly Dictionary<string, int> repositionCountByUserId = new();

        private readonly Dictionary<string, bool> objectIsAttached = new();
        private readonly Dictionary<string, DateTime> lastAttachmentStateChangeUtcByObjectId = new();
        private readonly Dictionary<string, TimeSpan> totalAttachedTimeByObjectId = new();
        private readonly Dictionary<string, TimeSpan> totalDetachedTimeByObjectId = new();

        public IReadOnlyList<TelemetryEventData> Events => events;
        public int EventCount => events.Count;

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Debug.LogWarning("[TelemetryLoggerCore] Duplicate instance found. Destroying duplicate.", this);
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

        // ----------------------------------------------------
        // Structured telemetry
        // ----------------------------------------------------

        public void LogEvent(string eventType, string userId, string instrumentId, string details)
        {
            var evt = new TelemetryEventData(eventType, userId, instrumentId, details);
            events.Add(evt);

            if (events.Count > maxEntries)
            {
                events.RemoveAt(0);
            }

            if (verboseLogs)
            {
                Debug.Log($"[TelemetryLoggerCore] {evt}", this);
            }
        }

        public void ClearAll()
        {
            events.Clear();

            lastManipulatedObjectByUserId.Clear();
            interactionCountByObjectId.Clear();
            interactionCountByUserAndObjectKey.Clear();

            regrabCountByObjectId.Clear();
            regrabCountByUserId.Clear();

            repositionCountByObjectId.Clear();
            repositionCountByUserId.Clear();

            objectIsAttached.Clear();
            lastAttachmentStateChangeUtcByObjectId.Clear();
            totalAttachedTimeByObjectId.Clear();
            totalDetachedTimeByObjectId.Clear();

            if (verboseLogs)
            {
                Debug.Log("[TelemetryLoggerCore] Cleared all telemetry entries and analytics.", this);
            }
        }

        public string GetFormattedEntries(int maxVisibleEntries = 100)
        {
            if (events.Count == 0)
                return "No telemetry events yet.";

            int startIndex = Mathf.Max(0, events.Count - maxVisibleEntries);

            StringBuilder sb = new StringBuilder();
            for (int i = startIndex; i < events.Count; i++)
            {
                sb.AppendLine(events[i].ToString());
                sb.AppendLine();
            }

            return sb.ToString();
        }

        public int GetEntryCount()
        {
            return events.Count;
        }

        // ----------------------------------------------------
        // Interaction analytics registration
        // ----------------------------------------------------

        public void RegisterManipulatedObject(string userId, string objectId)
        {
            if (string.IsNullOrWhiteSpace(userId) || string.IsNullOrWhiteSpace(objectId))
                return;

            lastManipulatedObjectByUserId[userId] = objectId;
        }

        public void RegisterObjectInteraction(string userId, string objectId)
        {
            if (string.IsNullOrWhiteSpace(userId) || string.IsNullOrWhiteSpace(objectId))
                return;

            RegisterManipulatedObject(userId, objectId);

            if (!interactionCountByObjectId.ContainsKey(objectId))
                interactionCountByObjectId[objectId] = 0;

            interactionCountByObjectId[objectId]++;

            string key = BuildUserObjectKey(userId, objectId);
            if (!interactionCountByUserAndObjectKey.ContainsKey(key))
                interactionCountByUserAndObjectKey[key] = 0;

            interactionCountByUserAndObjectKey[key]++;
        }

        public void RegisterObjectRegrab(string userId, string objectId)
        {
            if (string.IsNullOrWhiteSpace(userId) || string.IsNullOrWhiteSpace(objectId))
                return;

            if (!regrabCountByObjectId.ContainsKey(objectId))
                regrabCountByObjectId[objectId] = 0;

            if (!regrabCountByUserId.ContainsKey(userId))
                regrabCountByUserId[userId] = 0;

            regrabCountByObjectId[objectId]++;
            regrabCountByUserId[userId]++;
        }

        public void RegisterObjectReposition(string userId, string objectId)
        {
            if (string.IsNullOrWhiteSpace(userId) || string.IsNullOrWhiteSpace(objectId))
                return;

            if (!repositionCountByObjectId.ContainsKey(objectId))
                repositionCountByObjectId[objectId] = 0;

            if (!repositionCountByUserId.ContainsKey(userId))
                repositionCountByUserId[userId] = 0;

            repositionCountByObjectId[objectId]++;
            repositionCountByUserId[userId]++;
        }

        public void RegisterAttachmentStateChanged(string objectId, bool isAttached)
        {
            if (string.IsNullOrWhiteSpace(objectId))
                return;

            DateTime now = DateTime.UtcNow;

            if (!objectIsAttached.ContainsKey(objectId))
            {
                objectIsAttached[objectId] = isAttached;
                lastAttachmentStateChangeUtcByObjectId[objectId] = now;

                if (!totalAttachedTimeByObjectId.ContainsKey(objectId))
                    totalAttachedTimeByObjectId[objectId] = TimeSpan.Zero;

                if (!totalDetachedTimeByObjectId.ContainsKey(objectId))
                    totalDetachedTimeByObjectId[objectId] = TimeSpan.Zero;

                return;
            }

            bool previousState = objectIsAttached[objectId];
            DateTime previousChangeTime = lastAttachmentStateChangeUtcByObjectId[objectId];
            TimeSpan elapsed = now - previousChangeTime;

            if (previousState)
                totalAttachedTimeByObjectId[objectId] += elapsed;
            else
                totalDetachedTimeByObjectId[objectId] += elapsed;

            objectIsAttached[objectId] = isAttached;
            lastAttachmentStateChangeUtcByObjectId[objectId] = now;
        }

        // ----------------------------------------------------
        // Analytics queries
        // ----------------------------------------------------

        public string GetLastManipulatedObject(string userId)
        {
            if (string.IsNullOrWhiteSpace(userId))
                return string.Empty;

            return lastManipulatedObjectByUserId.TryGetValue(userId, out string value) ? value : string.Empty;
        }

        public int GetInteractionCountForObject(string objectId)
        {
            if (string.IsNullOrWhiteSpace(objectId))
                return 0;

            return interactionCountByObjectId.TryGetValue(objectId, out int value) ? value : 0;
        }

        public int GetInteractionCountForUserAndObject(string userId, string objectId)
        {
            string key = BuildUserObjectKey(userId, objectId);
            return interactionCountByUserAndObjectKey.TryGetValue(key, out int value) ? value : 0;
        }

        public string GetMostFrequentlyUsedObjectId()
        {
            string bestObjectId = string.Empty;
            int bestCount = -1;

            foreach (var kvp in interactionCountByObjectId)
            {
                if (kvp.Value > bestCount)
                {
                    bestCount = kvp.Value;
                    bestObjectId = kvp.Key;
                }
            }

            return bestObjectId;
        }

        public int GetRegrabCountForObject(string objectId)
        {
            return regrabCountByObjectId.TryGetValue(objectId, out int value) ? value : 0;
        }

        public int GetRegrabCountForUser(string userId)
        {
            return regrabCountByUserId.TryGetValue(userId, out int value) ? value : 0;
        }

        public int GetRepositionCountForObject(string objectId)
        {
            return repositionCountByObjectId.TryGetValue(objectId, out int value) ? value : 0;
        }

        public int GetRepositionCountForUser(string userId)
        {
            return repositionCountByUserId.TryGetValue(userId, out int value) ? value : 0;
        }

        public TimeSpan GetTotalAttachedTime(string objectId)
        {
            return totalAttachedTimeByObjectId.TryGetValue(objectId, out TimeSpan value) ? value : TimeSpan.Zero;
        }

        public TimeSpan GetTotalDetachedTime(string objectId)
        {
            return totalDetachedTimeByObjectId.TryGetValue(objectId, out TimeSpan value) ? value : TimeSpan.Zero;
        }

        // ----------------------------------------------------
        // Export
        // ----------------------------------------------------

        public string ExportTelemetryCsvString()
        {
            StringBuilder sb = new StringBuilder();
            sb.AppendLine("TimestampUtc,EventType,UserId,InstrumentId,Details");

            foreach (var evt in events)
            {
                if (evt == null)
                    continue;

                sb.AppendLine(
                    $"{CsvEscape(evt.TimestampUtc.ToString("o", CultureInfo.InvariantCulture))}," +
                    $"{CsvEscape(evt.EventType)}," +
                    $"{CsvEscape(evt.UserId)}," +
                    $"{CsvEscape(evt.InstrumentId)}," +
                    $"{CsvEscape(evt.Details)}");
            }

            return sb.ToString();
        }

        public string ExportInteractionAnalyticsCsvString()
        {
            StringBuilder sb = new StringBuilder();
            sb.AppendLine("ObjectId,InteractionCount,RegrabCount,RepositionCount,TotalAttachedSeconds,TotalDetachedSeconds");

            HashSet<string> allObjectIds = new HashSet<string>();
            foreach (var key in interactionCountByObjectId.Keys) allObjectIds.Add(key);
            foreach (var key in regrabCountByObjectId.Keys) allObjectIds.Add(key);
            foreach (var key in repositionCountByObjectId.Keys) allObjectIds.Add(key);
            foreach (var key in totalAttachedTimeByObjectId.Keys) allObjectIds.Add(key);
            foreach (var key in totalDetachedTimeByObjectId.Keys) allObjectIds.Add(key);
            foreach (var key in objectIsAttached.Keys) allObjectIds.Add(key);
            foreach (var key in lastAttachmentStateChangeUtcByObjectId.Keys) allObjectIds.Add(key);

            foreach (string objectId in allObjectIds)
            {
                TimeSpan attached = GetEffectiveAttachedTimeForExport(objectId);
                TimeSpan detached = GetEffectiveDetachedTimeForExport(objectId);

                sb.AppendLine(
                    $"{CsvEscape(objectId)}," +
                    $"{GetInteractionCountForObject(objectId)}," +
                    $"{GetRegrabCountForObject(objectId)}," +
                    $"{GetRepositionCountForObject(objectId)}," +
                    $"{attached.TotalSeconds.ToString(CultureInfo.InvariantCulture)}," +
                    $"{detached.TotalSeconds.ToString(CultureInfo.InvariantCulture)}");
            }

            return sb.ToString();
        }

        public string SaveTelemetryCsvToFile(string filePath)
        {
            string csv = ExportTelemetryCsvString();
            File.WriteAllText(filePath, csv, Encoding.UTF8);
            return filePath;
        }

        public string SaveInteractionAnalyticsCsvToFile(string filePath)
        {
            string csv = ExportInteractionAnalyticsCsvString();
            File.WriteAllText(filePath, csv, Encoding.UTF8);
            return filePath;
        }

        private string BuildUserObjectKey(string userId, string objectId)
        {
            return $"{userId}||{objectId}";
        }

        private string CsvEscape(string value)
        {
            if (string.IsNullOrEmpty(value))
                return "\"\"";

            string escaped = value.Replace("\"", "\"\"");
            return $"\"{escaped}\"";
        }

        private TimeSpan GetEffectiveAttachedTimeForExport(string objectId)
        {
            TimeSpan baseValue = totalAttachedTimeByObjectId.TryGetValue(objectId, out TimeSpan value)
                ? value
                : TimeSpan.Zero;

            if (!objectIsAttached.TryGetValue(objectId, out bool isAttached))
                return baseValue;

            if (!lastAttachmentStateChangeUtcByObjectId.TryGetValue(objectId, out DateTime lastChangeUtc))
                return baseValue;

            if (!isAttached)
                return baseValue;

            return baseValue + (DateTime.UtcNow - lastChangeUtc);
        }

        private TimeSpan GetEffectiveDetachedTimeForExport(string objectId)
        {
            TimeSpan baseValue = totalDetachedTimeByObjectId.TryGetValue(objectId, out TimeSpan value)
                ? value
                : TimeSpan.Zero;

            if (!objectIsAttached.TryGetValue(objectId, out bool isAttached))
                return baseValue;

            if (!lastAttachmentStateChangeUtcByObjectId.TryGetValue(objectId, out DateTime lastChangeUtc))
                return baseValue;

            if (isAttached)
                return baseValue;

            return baseValue + (DateTime.UtcNow - lastChangeUtc);
        }

        public void SaveRunMetadataCsvToFile(string filePath)
        {
            var context = EvaluationRunContext.Instance;
            if (context == null)
                return;

            OrchestrationProjectConfig config = Resources.Load<OrchestrationProjectConfig>(
                "AIMultiplayerAssistant/OrchestrationProjectConfig");

            string directory = System.IO.Path.GetDirectoryName(filePath);
            if (!string.IsNullOrWhiteSpace(directory) && !System.IO.Directory.Exists(directory))
                System.IO.Directory.CreateDirectory(directory);

            StringBuilder sb = new StringBuilder();
            sb.AppendLine("run_id,backend,scenario_id,run_start_utc,same_machine,host_machine,client_machine,unity_version,build_type,ping_ms,download_mbps,upload_mbps,simulated_latency_ms,simulated_packet_loss_percent,notes,scenario_runner,protocol_version,scenario_mode,expected_participants,repeat_count,warmup_seconds,hold_seconds,cooldown_seconds,step_timeout_seconds,prequeue_next_user,manipulate_child,manipulation_target,manipulation_start_delay_seconds,manipulation_duration_seconds,manipulation_delta_x,manipulation_delta_y,manipulation_delta_z,reset_performance_after_warmup,suppress_info_logs,validate_final_rotation,rotation_settle_seconds,rotation_tolerance_degrees,ai_enabled,ai_mode,ai_provider,deployment_profile,network_rate_policy");
            sb.AppendLine(
                $"{EscapeCsv(context.RunId)}," +
                $"{EscapeCsv(context.BackendName)}," +
                $"{EscapeCsv(context.ScenarioId)}," +
                $"{EscapeCsv(context.RunStartUtc.ToString("o"))}," +
                $"{context.SameMachine}," +
                $"{EscapeCsv(context.HostMachineName)}," +
                $"{EscapeCsv(context.ClientMachineName)}," +
                $"{EscapeCsv(context.UnityVersion)}," +
                $"{EscapeCsv(context.BuildType)}," +
                $"{FormatMetadataFloat(context.PingMs)}," +
                $"{FormatMetadataFloat(context.DownloadMbps)}," +
                $"{FormatMetadataFloat(context.UploadMbps)}," +
                $"{FormatMetadataFloat(context.SimulatedLatencyMs)}," +
                 $"{FormatMetadataFloat(context.SimulatedPacketLossPercent)}," +
                 $"{EscapeCsv(context.Notes)}," +
                $"{EscapeCsv(config != null ? config.automatedScenario.ToString() : "Unknown")}," +
                $"{EscapeCsv(config != null ? config.automatedScenarioProtocolVersion : "Unknown")}," +
                 $"{EscapeCsv(config != null ? config.automatedScenarioMode.ToString() : "Unknown")}," +
                $"{(config != null ? config.automatedScenarioExpectedParticipants : 0)}," +
                $"{(config != null ? config.automatedScenarioRepeatCount : 0)}," +
                $"{FormatMetadataFloat(config != null ? config.automatedScenarioWarmupSeconds : 0f)}," +
                $"{FormatMetadataFloat(config != null ? config.automatedScenarioHoldSeconds : 0f)}," +
                $"{FormatMetadataFloat(config != null ? config.automatedScenarioCooldownSeconds : 0f)}," +
                $"{FormatMetadataFloat(config != null ? config.automatedScenarioStepTimeoutSeconds : 0f)}," +
                $"{(config != null && config.automatedScenarioPreQueueNextUser)}," +
                $"{(config != null && config.automatedScenarioManipulateChildObject)}," +
                $"{EscapeCsv(config != null ? config.automatedScenarioManipulatedChildName : "")}," +
                $"{FormatMetadataFloat(config != null ? config.automatedScenarioManipulationStartDelaySeconds : 0f)}," +
                $"{FormatMetadataFloat(config != null ? config.automatedScenarioManipulationDurationSeconds : 0f)}," +
                $"{FormatMetadataFloat(config != null ? config.automatedScenarioManipulationEulerDelta.x : 0f)}," +
                $"{FormatMetadataFloat(config != null ? config.automatedScenarioManipulationEulerDelta.y : 0f)}," +
                $"{FormatMetadataFloat(config != null ? config.automatedScenarioManipulationEulerDelta.z : 0f)}," +
                $"{(config != null && config.automatedScenarioResetPerformanceSamplesAfterWarmup)}," +
                $"{(config != null && config.automatedScenarioSuppressInfoLogsDuringMeasurement)}," +
                $"{(config != null && config.automatedScenarioValidateFinalRotation)}," +
                $"{FormatMetadataFloat(config != null ? config.automatedScenarioFinalRotationSettleSeconds : 0f)}," +
                $"{FormatMetadataFloat(config != null ? config.automatedScenarioRotationToleranceDegrees : 0f)}," +
                $"{(config != null && config.enableAiOrchestration)}," +
                $"{EscapeCsv(config != null ? config.aiMode.ToString() : "Unknown")}," +
                $"{EscapeCsv(config != null ? config.aiProvider.ToString() : "Unknown")}," +
                $"{EscapeCsv(ResolveDeploymentProfile(config))}," +
                $"{EscapeCsv("BackendNativeConfiguration")}");

            System.IO.File.WriteAllText(filePath, sb.ToString(), Encoding.UTF8);
        }

        private static string ResolveDeploymentProfile(OrchestrationProjectConfig config)
        {
            if (config == null)
                return "Unknown";

            return config.backendType switch
            {
                OrchestrationBackendType.PUN2 => "PhotonCloud",
                OrchestrationBackendType.Fusion => "PhotonCloudShared",
                OrchestrationBackendType.FishNet => "LocalClientHost",
                OrchestrationBackendType.UnityNetcode => "LocalClientHost",
                OrchestrationBackendType.Mirror => "LocalClientHost",
                _ => "Unknown"
            };
        }

        private static string FormatMetadataFloat(float value)
        {
            return value.ToString("F4", CultureInfo.InvariantCulture);
        }

        private string EscapeCsv(string value)
        {
            if (string.IsNullOrEmpty(value))
                return "";

            return $"\"{value.Replace("\"", "\"\"")}\"";
        }
    }
}
