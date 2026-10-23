using FishNet.Object;
using Thesis.Orchestration.SharedSync;
using Thesis.Orchestration.Telemetry;
using UnityEngine;

namespace Thesis.Orchestration.Backends.FishNet
{
    [DisallowMultipleComponent]
    public class MicroscopeAccessControllerFishNet : BaseMicroscopeAccessController
    {
        [Header("References")]
        [SerializeField] private SessionQueueRuntimeFishNet queueRuntime;
        [SerializeField] private NetworkObject rootNetworkObject;
        [SerializeField] private FishNetOwnershipRelay rootOwnershipRelay;

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
            ResolveQueueRuntime();
            rootNetworkObject ??= GetComponent<NetworkObject>();
            rootOwnershipRelay ??= GetComponent<FishNetOwnershipRelay>();
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

            CancelPendingLogicalAuthorityTransfer("Controller disabled before authority completion.");
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
            string nativeOwner = rootNetworkObject == null || !rootNetworkObject.IsSpawned
                ? "None"
                : rootNetworkObject.Owner.IsValid ? $"Client_{rootNetworkObject.Owner.ClientId}" : "Server";

            NativeAuthorityObservationRecorder.Instance?.Observe(
                GetTrackedObjectId(),
                GetLocalUserId(),
                GetCurrentHolderUserId(),
                nativeOwner,
                HasLocalAccess,
                HasLocalControl(rootNetworkObject),
                IsQueueRuntimeActive(),
                "Root NetworkObject.Owner; local=IsOwner || (server && no owner)");
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
            if (queueRuntime != null)
                return queueRuntime.GetLocalUserId();

            SessionOrchestratorFishNet orchestrator = SessionOrchestratorFishNet.Instance;
            if (orchestrator != null)
                return orchestrator.GetLocalUserId();

            return "UnknownUser";
        }

        protected override void RequestAuthorityIfNeeded()
        {
            if (Time.time - lastOwnershipRequestTime < ownershipRequestCooldownSeconds)
                return;

            lastOwnershipRequestTime = Time.time;

            if (rootOwnershipRelay == null || HasLocalControl(rootNetworkObject))
                return;

            // A retry while pending re-sends the native request inside the same measurement.
            if (!authorityMeasurement.IsPending)
            {
                authorityMeasurement.Begin(
                    GetTrackedObjectId(),
                    GetLocalUserId(),
                    GetCurrentOwnerId(),
                    new[] { RootObjectKey },
                    Time.realtimeSinceStartupAsDouble,
                    "FishNet logical microscope ownership request initiated.");
            }

            rootOwnershipRelay.RequestOwnershipForLocalClient();
        }

        protected override void OnLocalAccessChanged(bool hasAccess)
        {
            if (hasAccess)
            {
                RequestAuthorityIfNeeded();
                return;
            }

            CancelPendingLogicalAuthorityTransfer("Local access revoked before authority completion.");
            if (!releaseOwnershipWhenAccessRevoked)
                return;

            if (Time.time - lastOwnershipReleaseTime < ownershipRequestCooldownSeconds)
                return;

            lastOwnershipReleaseTime = Time.time;

            if (rootOwnershipRelay != null && HasLocalControl(rootNetworkObject))
                rootOwnershipRelay.ReleaseOwnershipForLocalClient();
        }

        private void RefreshPendingLogicalAuthorityTransfer()
        {
            if (!authorityMeasurement.IsPending)
                return;

            if (!HasLocalAccess)
            {
                CancelPendingLogicalAuthorityTransfer("Local access no longer matches pending authority request.");
                return;
            }

            if (authorityMeasurement.CheckTimeout(Time.realtimeSinceStartupAsDouble, authorityMeasurementTimeoutSeconds,
                    "FishNet local control was not observed before the measurement timeout."))
                return;

            if (!HasLocalControl(rootNetworkObject))
                return;

            authorityMeasurement.ObserveLocal(RootObjectKey, "Root NetworkObject reports local control.");
            if (!authorityMeasurement.TryResolve(
                    GetCurrentOwnerId(),
                    "FishNet logical microscope ownership transfer completed when local client became owner.",
                    out _,
                    out string measurementId))
                return;

            AuthorityTransferRecorder recorder = AuthorityTransferRecorder.Instance;
            if (recorder != null)
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
            if (queueRuntime == null || !queueRuntime.IsSpawned)
                queueRuntime = SessionQueueRuntimeFishNet.Instance;

            if (queueRuntime == null || !queueRuntime.IsSpawned)
                queueRuntime = FindFirstObjectByType<SessionQueueRuntimeFishNet>();
        }

        private string GetTrackedObjectId()
        {
            if (sharedDefinition != null && !string.IsNullOrWhiteSpace(sharedDefinition.ObjectId))
                return sharedDefinition.ObjectId;

            return gameObject.name;
        }

        private string GetCurrentOwnerId()
        {
            if (rootNetworkObject == null || !rootNetworkObject.IsSpawned || !rootNetworkObject.Owner.IsValid)
                return "None";

            return $"Client_{rootNetworkObject.Owner.ClientId}";
        }

        private static bool HasLocalControl(NetworkObject networkObject)
        {
            return networkObject != null &&
                   (networkObject.IsOwner || (networkObject.IsServerInitialized && !networkObject.Owner.IsValid));
        }
    }
}
