using Thesis.Orchestration.SharedSync;
using Thesis.Orchestration.Telemetry;
using UnityEngine;
using MirrorNetworkClient = global::Mirror.NetworkClient;
using MirrorNetworkIdentity = global::Mirror.NetworkIdentity;
using MirrorNetworkServer = global::Mirror.NetworkServer;

namespace Thesis.Orchestration.Backends.Mirror
{
    [DisallowMultipleComponent]
    public class NetworkInteractionGateMirror : BaseInteractionGate
    {
        [Header("Cached References")]
        [SerializeField] private MirrorNetworkIdentity networkIdentity;
        [SerializeField] private SharedSyncObjectDefinition sharedDefinition;
        [SerializeField] private MicroscopeAccessControllerMirror accessController;
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
            networkIdentity ??= GetComponent<MirrorNetworkIdentity>();
            sharedDefinition ??= GetComponentInParent<SharedSyncObjectDefinition>();
            accessController ??= GetComponentInParent<MicroscopeAccessControllerMirror>();
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
            if (!MirrorNetworkClient.active && !MirrorNetworkServer.active)
                return allowInteractionWhenOffline;

            if (accessController == null)
                return true;

            SessionQueueRuntimeMirror runtime = SessionQueueRuntimeMirror.Instance;
            if (runtime == null || !runtime.QueueRuntimeActive)
                return true;

            if (!accessController.HasLocalAccess)
                return false;

            if (networkIdentity == null)
                return true;

            return networkIdentity.isOwned || (MirrorNetworkServer.active && networkIdentity.connectionToClient == null);
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
            return MirrorNetworkClient.active || MirrorNetworkServer.active;
        }

        protected override bool IsQueueRuntimeActive()
        {
            return SessionQueueRuntimeMirror.Instance != null && SessionQueueRuntimeMirror.Instance.QueueRuntimeActive;
        }

        protected override bool HasNetworkAuthorityForInteraction()
        {
            return networkIdentity == null ||
                   networkIdentity.isOwned ||
                   (MirrorNetworkServer.active && networkIdentity.connectionToClient == null);
        }

        protected override bool IsLocalHolder()
        {
            SessionQueueRuntimeMirror runtime = SessionQueueRuntimeMirror.Instance;
            return runtime != null && runtime.CurrentHolderUserId == GetLocalUserId();
        }

        protected override bool QueueHasWaitingUsers()
        {
            SessionQueueRuntimeMirror runtime = SessionQueueRuntimeMirror.Instance;
            return runtime != null &&
                   runtime.CurrentQueueSnapshot != null &&
                   runtime.CurrentQueueSnapshot.Count > 0;
        }

        protected override void SendHeartbeat()
        {
            SessionQueueRuntimeMirror.Instance?.HeartbeatForLocalPlayer();
        }

        protected override void RequestRelease()
        {
            SessionQueueRuntimeMirror.Instance?.ReleaseAccessForLocalPlayer();
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
            SessionOrchestratorMirror orchestrator = SessionOrchestratorMirror.Instance;
            if (orchestrator != null)
                return orchestrator.GetLocalUserId();

            return "UnknownUser";
        }

        protected override void Log(string message)
        {
            if (!verboseLogs)
                return;

            Debug.Log($"[NetworkInteractionGateMirror:{name}] {message}", this);
        }
    }
}
