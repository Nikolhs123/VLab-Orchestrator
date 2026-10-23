using Fusion;
using Thesis.Orchestration.SharedSync;
using Thesis.Orchestration.Telemetry;
using UnityEngine;

namespace Thesis.Orchestration.Backends.Fusion
{
    [DisallowMultipleComponent]
    public class NetworkInteractionGateFusion : BaseInteractionGate
    {
        [Header("Cached References")]
        [SerializeField] private NetworkObject networkObject;
        [SerializeField] private SharedSyncObjectDefinition sharedDefinition;
        [SerializeField] private MicroscopeAccessControllerFusion accessController;
        [SerializeField] private MonoBehaviour mouseUiBehaviour;

        [Header("Options")]
        [SerializeField] private bool controlCollidersToo = true;
        [SerializeField] private bool requestAuthorityAutomatically = true;
        [Tooltip("Minimum seconds between automatic state-authority requests; permission is evaluated every frame.")]
        [SerializeField] private float authorityRequestCooldownSeconds = 0.5f;
        [SerializeField] private bool allowInteractionWhenOffline = true;

        [Header("Automatic Heartbeat")]
        [SerializeField] private bool sendHeartbeatOnTransformChange = true;
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
        private float lastAuthorityRequestTime = -999f;

        private void Awake()
        {
            if (networkObject == null)
                networkObject = GetComponent<NetworkObject>();

            if (sharedDefinition == null)
                sharedDefinition = GetComponentInParent<SharedSyncObjectDefinition>();

            if (accessController == null)
                accessController = GetComponentInParent<MicroscopeAccessControllerFusion>();

            if (mouseUiBehaviour == null)
            {
                var candidate = GetComponent("MouseUI") as MonoBehaviour;
                if (candidate != null)
                    mouseUiBehaviour = candidate;
            }

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
            SessionQueueRuntimeFusion runtime = SessionQueueRuntimeFusion.Instance;

            if (runtime == null || runtime.Runner == null || !runtime.Runner.IsRunning)
                return allowInteractionWhenOffline;

            if (accessController == null)
                return true;

            if (!runtime.QueueRuntimeActive)
                return true;

            if (!accessController.HasLocalAccess)
                return false;

            if (networkObject == null)
                return true;

            if (networkObject.HasStateAuthority)
                return true;

            // Without a cooldown this issued one request per frame until authority arrived.
            if (requestAuthorityAutomatically &&
                Time.unscaledTime - lastAuthorityRequestTime >= authorityRequestCooldownSeconds)
            {
                lastAuthorityRequestTime = Time.unscaledTime;
                networkObject.RequestStateAuthority();
                Log($"Requested state authority for child '{name}'.");
            }

            return false;
        }

        protected override void ApplyInteractionState(bool shouldEnableInteraction)
        {
            if (mouseUiBehaviour != null)
                mouseUiBehaviour.enabled = shouldEnableInteraction;

            if (controlCollidersToo && cachedColliders != null)
            {
                foreach (var col in cachedColliders)
                {
                    if (col != null)
                        col.enabled = shouldEnableInteraction;
                }
            }
        }

        protected override bool GetHasLocalAccess()
        {
            return accessController != null && accessController.HasLocalAccess;
        }

        protected override bool IsNetworkSessionUsable()
        {
            SessionQueueRuntimeFusion runtime = SessionQueueRuntimeFusion.Instance;
            return runtime != null && runtime.Runner != null && runtime.Runner.IsRunning;
        }

        protected override bool IsQueueRuntimeActive()
        {
            SessionQueueRuntimeFusion runtime = SessionQueueRuntimeFusion.Instance;
            return runtime != null && runtime.QueueRuntimeActive;
        }

        protected override bool HasNetworkAuthorityForInteraction()
        {
            if (networkObject == null)
                return true;

            return networkObject.HasStateAuthority;
        }

        protected override bool IsLocalHolder()
        {
            SessionQueueRuntimeFusion runtime = SessionQueueRuntimeFusion.Instance;
            if (runtime == null)
                return false;

            return runtime.CurrentHolderUserId == GetLocalUserId();
        }

        protected override bool QueueHasWaitingUsers()
        {
            SessionQueueRuntimeFusion runtime = SessionQueueRuntimeFusion.Instance;
            return runtime != null &&
                   runtime.CurrentQueueSnapshot != null &&
                   runtime.CurrentQueueSnapshot.Count > 0;
        }

        protected override void SendHeartbeat()
        {
            SessionQueueRuntimeFusion.Instance?.HeartbeatForLocalPlayer();
        }

        protected override void RequestRelease()
        {
            SessionQueueRuntimeFusion.Instance?.ReleaseAccessForLocalPlayer();
        }

        protected override void RegisterInteractionAnalytics()
        {
            string localUserId = GetLocalUserId();
            string objectId = BuildTrackedObjectId();

            TelemetryLoggerCore.Instance?.RegisterObjectInteraction(localUserId, objectId);
            TelemetryLoggerCore.Instance?.RegisterManipulatedObject(localUserId, objectId);
        }

        private string BuildTrackedObjectId()
        {
            if (sharedDefinition != null && !string.IsNullOrWhiteSpace(sharedDefinition.ObjectId))
                return $"{sharedDefinition.ObjectId}/{name}";

            return name;
        }

        private string GetLocalUserId()
        {
            SessionQueueRuntimeFusion runtime = SessionQueueRuntimeFusion.Instance;
            if (runtime != null && runtime.Runner != null && runtime.Runner.IsRunning)
                return $"Player_{runtime.Runner.LocalPlayer.RawEncoded}";

            SessionOrchestratorFusion orchestrator = SessionOrchestratorFusion.Instance;
            if (orchestrator != null)
            {
                string userId = orchestrator.GetLocalUserId();
                if (!string.IsNullOrWhiteSpace(userId) && userId != "UnknownUser")
                    return userId;
            }

            return "UnknownUser";
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

        protected override void Log(string message)
        {
            if (!verboseLogs)
                return;

            Debug.Log($"[NetworkInteractionGateFusion:{name}] {message}", this);
        }
    }
}
