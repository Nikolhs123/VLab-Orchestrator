using Photon.Pun;
using Thesis.Orchestration.SharedSync;
using Thesis.Orchestration.Telemetry;
using UnityEngine;

namespace Thesis.Orchestration.Backends.PUN2
{
    [DisallowMultipleComponent]
    public class NetworkInteractionGatePUN2 : BaseInteractionGate
    {
        [Header("Cached References")]
        [SerializeField] private PhotonView photonView;
        [SerializeField] private SharedSyncObjectDefinition sharedDefinition;
        [SerializeField] private MicroscopeAccessControllerPUN2 accessController;
        [SerializeField] private MonoBehaviour mouseUiBehaviour;

        [Header("Options")]
        [SerializeField] private bool controlCollidersToo = true;
        [SerializeField] private bool requestOwnershipAutomatically = true;
        [Tooltip("Minimum seconds between automatic ownership requests; permission is evaluated every frame.")]
        [SerializeField] private float ownershipRequestCooldownSeconds = 0.5f;
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
        private float lastOwnershipRequestTime = -999f;

        private void Awake()
        {
            if (photonView == null)
                photonView = GetComponent<PhotonView>();

            if (sharedDefinition == null)
                sharedDefinition = GetComponentInParent<SharedSyncObjectDefinition>();

            if (accessController == null)
                accessController = GetComponentInParent<MicroscopeAccessControllerPUN2>();

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
            if (!PhotonNetwork.IsConnected || !PhotonNetwork.InRoom)
                return allowInteractionWhenOffline;

            if (accessController == null)
                return true;

            if (SessionQueueRuntimePUN2.Instance == null || !SessionQueueRuntimePUN2.Instance.QueueRuntimeActive)
                return true;

            if (!accessController.HasLocalAccess)
                return false;

            if (photonView == null)
                return true;

            if (photonView.IsMine)
                return true;

            // Without a cooldown this issued one takeover per frame until ownership arrived, so the
            // number of redundant requests per handoff depended on the frame rate.
            if (requestOwnershipAutomatically &&
                Time.unscaledTime - lastOwnershipRequestTime >= ownershipRequestCooldownSeconds)
            {
                lastOwnershipRequestTime = Time.unscaledTime;
                photonView.RequestOwnership();
                Log($"Requested ownership for child '{name}', ViewID={photonView.ViewID}");
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
            return PhotonNetwork.IsConnected && PhotonNetwork.InRoom;
        }

        protected override bool IsQueueRuntimeActive()
        {
            return SessionQueueRuntimePUN2.Instance != null && SessionQueueRuntimePUN2.Instance.QueueRuntimeActive;
        }

        protected override bool HasNetworkAuthorityForInteraction()
        {
            if (photonView == null)
                return true;

            return photonView.IsMine;
        }

        protected override bool IsLocalHolder()
        {
            var runtime = SessionQueueRuntimePUN2.Instance;
            if (runtime == null)
                return false;

            return runtime.CurrentHolderUserId == GetLocalUserId();
        }

        protected override bool QueueHasWaitingUsers()
        {
            var runtime = SessionQueueRuntimePUN2.Instance;
            return runtime != null &&
                   runtime.CurrentQueueSnapshot != null &&
                   runtime.CurrentQueueSnapshot.Count > 0;
        }

        protected override void SendHeartbeat()
        {
            SessionQueueRuntimePUN2.Instance?.HeartbeatForLocalPlayer();
        }

        protected override void RequestRelease()
        {
            SessionQueueRuntimePUN2.Instance?.ReleaseAccessForLocalPlayer();
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
            if (PhotonNetwork.LocalPlayer == null)
                return "UnknownUser";

            return $"Actor_{PhotonNetwork.LocalPlayer.ActorNumber}";
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

            Debug.Log($"[NetworkInteractionGatePUN2:{name}] {message}", this);
        }
    }
}
