using Thesis.Orchestration.Analytics;
using Thesis.Orchestration.Backends.FishNet;
using Thesis.Orchestration.Backends.Fusion;
using Thesis.Orchestration.Backends.Mirror;
using Thesis.Orchestration.Backends.Netcode;
using Thesis.Orchestration.Backends.PUN2;
using Thesis.Orchestration.EditorTools;
using Thesis.Orchestration.Runtime;

namespace Thesis.Orchestration.Telemetry
{
    internal static class OrchestrationExportFinalizationUtility
    {
        public static bool TryBeginFinalizeForExport(
            OrchestrationBackendType backendType,
            IOrchestrationQueueView queueView,
            out string statusMessage)
        {
            if (queueView == null || !queueView.QueueRuntimeActive)
            {
                statusMessage = "No active queue runtime. Exporting current analytics snapshot.";
                return false;
            }

            if (IsHolderReleased(queueView.CurrentHolderUserId))
            {
                statusMessage = "No active holder. Exporting current analytics snapshot.";
                return false;
            }

            bool initiated = backendType switch
            {
                OrchestrationBackendType.PUN2 => SessionQueueRuntimePUN2.Instance != null &&
                    SessionQueueRuntimePUN2.Instance.TryForceReleaseCurrentHolderForFinalization("Analytics export"),
                OrchestrationBackendType.Fusion => SessionQueueRuntimeFusion.Instance != null &&
                    SessionQueueRuntimeFusion.Instance.TryForceReleaseCurrentHolderForFinalization("Analytics export"),
                OrchestrationBackendType.UnityNetcode => SessionQueueRuntimeNetcode.Instance != null &&
                    SessionQueueRuntimeNetcode.Instance.TryForceReleaseCurrentHolderForFinalization("Analytics export"),
                OrchestrationBackendType.FishNet => SessionQueueRuntimeFishNet.Instance != null &&
                    SessionQueueRuntimeFishNet.Instance.TryForceReleaseCurrentHolderForFinalization("Analytics export"),
                OrchestrationBackendType.Mirror => SessionQueueRuntimeMirror.Instance != null &&
                    SessionQueueRuntimeMirror.Instance.TryForceReleaseCurrentHolderForFinalization("Analytics export"),
                _ => false
            };

            statusMessage = initiated
                ? $"Finalizing active access for {queueView.CurrentHolderUserId} before export..."
                : "Could not finalize active holder automatically. Exporting after timeout if needed.";

            return initiated;
        }

        public static bool IsReadyForExport(IOrchestrationQueueView queueView)
        {
            if (queueView == null || !queueView.QueueRuntimeActive)
                return true;

            if (!IsHolderReleased(queueView.CurrentHolderUserId))
                return false;

            string instrumentId = queueView.CurrentInstrumentId;
            if (string.IsNullOrWhiteSpace(instrumentId))
                return true;

            InstrumentUsageStats instrumentStats = UsageTimingTracker.Instance?.GetInstrumentStats(instrumentId);
            return instrumentStats == null || !instrumentStats.IsCurrentlyInUse;
        }

        private static bool IsHolderReleased(string holderUserId)
        {
            return string.IsNullOrWhiteSpace(holderUserId) || holderUserId == "None";
        }
    }
}
