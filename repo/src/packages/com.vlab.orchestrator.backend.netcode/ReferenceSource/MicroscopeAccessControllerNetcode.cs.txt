using Thesis.Orchestration.SharedSync;
using Thesis.Orchestration.Telemetry;
using Unity.Netcode;
using UnityEngine;

namespace Thesis.Orchestration.Backends.Netcode
{
    [DisallowMultipleComponent]
    public class MicroscopeAccessControllerNetcode : BaseMicroscopeAccessController
    {
        [Header("References")]
        [SerializeField] private SessionQueueRuntimeNetcode queueRuntime;
        [SerializeField] private NetworkObject rootNetworkObject;
        [SerializeField] private NetcodeOwnershipRelay ownershipRelay;

        [Header("Ownership")]
        [SerializeField] private bool requestOwnershipForControlledChildren = false;
        [SerializeField] private bool releaseOwnershipWhenAccessRevoked = true;
        [SerializeField] private float ownershipRequestCooldownSeconds = 0.35f;
        [Tooltip("Seconds before a pending authority measurement is closed as TimedOut. 0 disables the timeout.")]
        [SerializeField] private float authorityMeasurementTimeoutSeconds = 10f;

        private const string RootObjectKey = "root";
        private float lastOwnershipRequestTime = -999f;
        private float lastOwnershipReleaseTime = -999f;
        private AuthorityMeasurementTracker authorityMeasurement;

        private void Awake()
        {
            BaseAwake();
            authorityMeasurement = new AuthorityMeasurementTracker(() => AuthorityTransferRecorder.Instance);

            if (queueRuntime == null)
                queueRuntime = SessionQueueRuntimeNetcode.Instance;

            if (rootNetworkObject == null)
                rootNetworkObject = GetComponent<NetworkObject>();

            if (ownershipRelay == null)
                ownershipRelay = GetComponent<NetcodeOwnershipRelay>();
        }

        private void OnEnable()
        {
            ResolveQueueRuntime();

            if (queueRuntime != null)
                queueRuntime.OnRuntimeStateUpdated += RefreshAccessState;
        }

        private void OnDisable()
        {
            if (queueRuntime != null)
                queueRuntime.OnRuntimeStateUpdated -= RefreshAccessState;

            CancelPendingLogicalAuthorityTransfer("Controller disabled before ownership completion.");
        }

        private void Start()
        {
            BaseStart();
        }

        private void Update()
        {
            BaseUpdate();
            RefreshPendingLogicalAuthorityTransfer();
            SampleNativeAuthority();
        }

        // Independent per-frame read of SDK ownership; not derived from request/completion events.
        private void SampleNativeAuthority()
        {
            bool spawned = rootNetworkObject != null && rootNetworkObject.IsSpawned;
            NativeAuthorityObservationRecorder.Instance?.Observe(
                GetTrackedObjectId(),
                GetLocalUserId(),
                GetCurrentHolderUserId(),
                GetCurrentOwnerId(),
                HasLocalAccess,
                spawned && rootNetworkObject.IsOwner,
                IsQueueRuntimeActive(),
                "Root NetworkObject.OwnerClientId; local=IsOwner");
        }

        protected override bool CanEvaluateAccessState()
        {
            ResolveQueueRuntime();
            return queueRuntime != null && sharedDefinition != null;
        }

        protected override bool IsQueueRuntimeActive()
        {
            return queueRuntime != null && queueRuntime.QueueRuntimeActive;
        }

        protected override string GetCurrentInstrumentId()
        {
            return queueRuntime != null ? queueRuntime.CurrentInstrumentId : string.Empty;
        }

        protected override string GetCurrentHolderUserId()
        {
            return queueRuntime != null ? queueRuntime.CurrentHolderUserId : "None";
        }

        protected override string GetLocalUserId()
        {
            if (queueRuntime != null && queueRuntime.NetworkManager != null && queueRuntime.NetworkManager.IsListening)
                return ConvertClientToUserId(queueRuntime.NetworkManager.LocalClientId);

            SessionOrchestratorNetcode orchestrator = SessionOrchestratorNetcode.Instance;
            if (orchestrator != null)
                return orchestrator.GetLocalUserId();

            return "UnknownUser";
        }

        protected override void RequestAuthorityIfNeeded()
        {
            if (ownershipRelay == null)
            {
                Log("Ownership request skipped because NetcodeOwnershipRelay is missing.");
                return;
            }

            Unity.Netcode.NetworkManager manager = ownershipRelay.NetworkManager != null
                ? ownershipRelay.NetworkManager
                : Unity.Netcode.NetworkManager.Singleton;

            if (manager == null || rootNetworkObject == null || !rootNetworkObject.IsSpawned)
                return;

            if (rootNetworkObject.OwnerClientId == manager.LocalClientId)
                return;

            if (Time.time - lastOwnershipRequestTime < ownershipRequestCooldownSeconds)
                return;

            lastOwnershipRequestTime = Time.time;

            // A retry while pending re-sends the native request inside the same measurement.
            if (!authorityMeasurement.IsPending)
            {
                authorityMeasurement.Begin(
                    GetTrackedObjectId(),
                    GetLocalUserId(),
                    GetCurrentOwnerId(),
                    new[] { RootObjectKey },
                    Time.realtimeSinceStartupAsDouble,
                    "Netcode logical microscope ownership request initiated.");
            }

            ownershipRelay.RequestOwnershipForLocalClient();
        }

        protected override void OnLocalAccessChanged(bool hasAccess)
        {
            if (hasAccess)
            {
                RequestAuthorityIfNeeded();
                return;
            }

            CancelPendingLogicalAuthorityTransfer("Local logical access was revoked before ownership completion.");

            if (!releaseOwnershipWhenAccessRevoked || ownershipRelay == null || rootNetworkObject == null || !rootNetworkObject.IsSpawned)
                return;

            Unity.Netcode.NetworkManager manager = ownershipRelay.NetworkManager != null
                ? ownershipRelay.NetworkManager
                : Unity.Netcode.NetworkManager.Singleton;

            if (manager == null)
                return;

            if (rootNetworkObject.OwnerClientId != manager.LocalClientId)
                return;

            if (Time.time - lastOwnershipReleaseTime < ownershipRequestCooldownSeconds)
                return;

            lastOwnershipReleaseTime = Time.time;
            ownershipRelay.ReleaseOwnershipForLocalClient();
        }

        private void RefreshPendingLogicalAuthorityTransfer()
        {
            if (!authorityMeasurement.IsPending)
                return;

            if (authorityMeasurement.CheckTimeout(Time.realtimeSinceStartupAsDouble, authorityMeasurementTimeoutSeconds,
                    "Netcode ownership was not observed as local before the measurement timeout."))
                return;

            Unity.Netcode.NetworkManager manager = ownershipRelay != null && ownershipRelay.NetworkManager != null
                ? ownershipRelay.NetworkManager
                : Unity.Netcode.NetworkManager.Singleton;

            if (manager == null || rootNetworkObject == null || !rootNetworkObject.IsSpawned)
                return;

            if (rootNetworkObject.OwnerClientId != manager.LocalClientId)
            {
                if (!HasLocalAccess)
                    CancelPendingLogicalAuthorityTransfer("Local logical access no longer matches the pending ownership request.");

                return;
            }

            authorityMeasurement.ObserveLocal(RootObjectKey, "Root NetworkObject.OwnerClientId equals LocalClientId.");
            if (!authorityMeasurement.TryResolve(
                    GetCurrentOwnerId(),
                    "Netcode logical microscope ownership transfer completed when local client became owner.",
                    out _,
                    out string measurementId))
                return;

            AuthorityTransferRecorder recorder = AuthorityTransferRecorder.Instance;
            if (!manager.IsServer && recorder != null)
            {
                ResolveQueueRuntime();
                queueRuntime?.SubmitAuthorityTransferSnapshot(recorder.BuildCompletedRowJson(measurementId));
            }
        }

        private void CancelPendingLogicalAuthorityTransfer(string details)
        {
            authorityMeasurement?.Cancel(details);
        }

        private void ResolveQueueRuntime()
        {
            if (queueRuntime == null)
                queueRuntime = SessionQueueRuntimeNetcode.Instance;

            if (queueRuntime == null)
                queueRuntime = FindFirstObjectByType<SessionQueueRuntimeNetcode>();
        }

        private string GetTrackedObjectId()
        {
            if (sharedDefinition != null && !string.IsNullOrWhiteSpace(sharedDefinition.ObjectId))
                return sharedDefinition.ObjectId;

            return gameObject.name;
        }

        private string GetCurrentOwnerId()
        {
            if (rootNetworkObject == null || !rootNetworkObject.IsSpawned)
                return "None";

            return ConvertClientToUserId(rootNetworkObject.OwnerClientId);
        }

        private static string ConvertClientToUserId(ulong clientId)
        {
            return $"Client_{clientId}";
        }
    }
}
