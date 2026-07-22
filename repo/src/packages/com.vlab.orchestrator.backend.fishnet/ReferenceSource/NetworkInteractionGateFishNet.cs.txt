using FishNet;
using FishNet.Object;
using Thesis.Orchestration.SharedSync;
using Thesis.Orchestration.Telemetry;
using UnityEngine;

namespace Thesis.Orchestration.Backends.FishNet
{
    [DisallowMultipleComponent]
    public class NetworkInteractionGateFishNet : BaseInteractionGate
    {
        [Header("Cached References")]
        [SerializeField] private NetworkObject networkObject;
        [SerializeField] private SharedSyncObjectDefinition sharedDefinition;
        [SerializeField] private MicroscopeAccessControllerFishNet accessController;
        [SerializeField] private MonoBehaviour mouseUiBehaviour;

        [Header("Options")]
        [SerializeField] private bool controlCollidersToo = true;
        [SerializeField] private bool allowInteractionWhenOffline = true;
        [SerializeField] private bool sendHeartbeatOnTransformChange = true;

        [Header("Heartbeat")]
        [SerializeField] private float heartbeatCooldownSeconds = 0.25f;
        [SerializeField] private float positionChangeThreshold = 0.0001f;
        [SerializeField] private float rotationChangeThresholdDegrees = 0.1f;

        [Header("Automatic Release")]
        [SerializeField] private bool autoReleaseWhenIdleAndQueueWaiting = true;
        [SerializeField] private float autoReleaseAfterIdleSeconds = 8f;
        [SerializeField] private float autoReleaseCooldownSeconds = 1f;

        [Header("Analytics")]
        [SerializeField] private bool trackInteractionAnalytics = true;
        [SerializeField] private float analyticsInteractionCooldownSeconds = 0.15f;

        [Header("Debug")]
        [SerializeField] private bool verboseLogs = true;

        private Collider[] cachedColliders;

        private void Awake()
        {
            networkObject ??= GetComponent<NetworkObject>();
            sharedDefinition ??= GetComponentInParent<SharedSyncObjectDefinition>();
            accessController ??= GetComponentInParent<MicroscopeAccessControllerFishNet>();
            mouseUiBehaviour ??= GetComponent("MouseUI") as MonoBehaviour;
            cachedColliders = GetComponents<Collider>();
            BaseInitialize();
        }

        private void Start()
        {
            RefreshInteractionStateInternal(force: true);
            BaseStart();
        }

        private void Update()
        {
            BaseUpdate();
        }

        protected override bool EvaluateInteractionPermission()
        {
            if (!InstanceFinder.IsClientStarted && !InstanceFinder.IsServerStarted)
                return allowInteractionWhenOffline;

            if (accessController == null)
                return true;

            SessionQueueRuntimeFishNet runtime = SessionQueueRuntimeFishNet.Instance;
            if (runtime == null || !runtime.QueueRuntimeActive)
                return true;

            if (!accessController.HasLocalAccess)
                return false;

            if (networkObject == null)
                return true;

            return networkObject.IsOwner || (networkObject.IsServerInitialized && !networkObject.Owner.IsValid);
        }

        protected override void ApplyInteractionState(bool shouldEnableInteraction)
        {
            if (mouseUiBehaviour != null)
                mouseUiBehaviour.enabled = shouldEnableInteraction;

            if (!controlCollidersToo || cachedColliders == null)
                return;

            foreach (Collider col in cachedColliders)
            {
                if (col != null)
                    col.enabled = shouldEnableInteraction;
            }
        }

        protected override bool GetHasLocalAccess()
        {
            return accessController != null && accessController.HasLocalAccess;
        }

        protected override bool IsNetworkSessionUsable()
        {
            return InstanceFinder.IsClientStarted || InstanceFinder.IsServerStarted;
        }

        protected override bool IsQueueRuntimeActive()
        {
            return SessionQueueRuntimeFishNet.Instance != null && SessionQueueRuntimeFishNet.Instance.QueueRuntimeActive;
        }

        protected override bool HasNetworkAuthorityForInteraction()
        {
            return networkObject == null ||
                   networkObject.IsOwner ||
                   (networkObject.IsServerInitialized && !networkObject.Owner.IsValid);
        }

        protected override bool IsLocalHolder()
        {
            SessionQueueRuntimeFishNet runtime = SessionQueueRuntimeFishNet.Instance;
            return runtime != null && runtime.CurrentHolderUserId == GetLocalUserId();
        }

        protected override bool QueueHasWaitingUsers()
        {
            SessionQueueRuntimeFishNet runtime = SessionQueueRuntimeFishNet.Instance;
            return runtime != null &&
                   runtime.CurrentQueueSnapshot != null &&
                   runtime.CurrentQueueSnapshot.Count > 0;
        }

        protected override void SendHeartbeat()
        {
            SessionQueueRuntimeFishNet.Instance?.HeartbeatForLocalPlayer();
        }

        protected override void RequestRelease()
        {
            SessionQueueRuntimeFishNet.Instance?.ReleaseAccessForLocalPlayer();
        }

        protected override void RegisterInteractionAnalytics()
        {
            string localUserId = GetLocalUserId();
            string objectId = BuildTrackedObjectId();

            TelemetryLoggerCore.Instance?.RegisterObjectInteraction(localUserId, objectId);
            TelemetryLoggerCore.Instance?.RegisterManipulatedObject(localUserId, objectId);
        }

        protected override bool GetSendHeartbeatOnTransformChange() => sendHeartbeatOnTransformChange;
        protected override float GetHeartbeatCooldownSeconds() => heartbeatCooldownSeconds;
        protected override float GetPositionChangeThreshold() => positionChangeThreshold;
        protected override float GetRotationChangeThresholdDegrees() => rotationChangeThresholdDegrees;
        protected override bool GetAutoReleaseWhenIdleAndQueueWaiting() => autoReleaseWhenIdleAndQueueWaiting;
        protected override float GetAutoReleaseAfterIdleSeconds() => autoReleaseAfterIdleSeconds;
        protected override float GetAutoReleaseCooldownSeconds() => autoReleaseCooldownSeconds;
        protected override bool GetTrackInteractionAnalytics() => trackInteractionAnalytics;
        protected override float GetAnalyticsInteractionCooldownSeconds() => analyticsInteractionCooldownSeconds;

        private string BuildTrackedObjectId()
        {
            if (sharedDefinition != null && !string.IsNullOrWhiteSpace(sharedDefinition.ObjectId))
                return $"{sharedDefinition.ObjectId}/{name}";

            return name;
        }

        private string GetLocalUserId()
        {
            SessionOrchestratorFishNet orchestrator = SessionOrchestratorFishNet.Instance;
            if (orchestrator != null)
                return orchestrator.GetLocalUserId();

            return InstanceFinder.ClientManager != null && InstanceFinder.ClientManager.Connection.IsValid
                ? $"Client_{InstanceFinder.ClientManager.Connection.ClientId}"
                : "UnknownUser";
        }

        protected override void Log(string message)
        {
            if (!verboseLogs)
                return;

            Debug.Log($"[NetworkInteractionGateFishNet:{name}] {message}", this);
        }
    }
}
