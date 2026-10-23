using Thesis.Orchestration.SharedSync;
using Thesis.Orchestration.Telemetry;
using UnityEngine;
using MirrorNetworkIdentity = global::Mirror.NetworkIdentity;
using MirrorNetworkServer = global::Mirror.NetworkServer;

namespace Thesis.Orchestration.Backends.Mirror
{
    [DisallowMultipleComponent]
    public class MicroscopeAccessControllerMirror : BaseMicroscopeAccessController
    {
        [Header("References")]
        [SerializeField] private SessionQueueRuntimeMirror queueRuntime;
        [SerializeField] private MirrorNetworkIdentity rootNetworkIdentity;
        [SerializeField] private MirrorOwnershipRelay rootOwnershipRelay;

        [Header("Ownership")]
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
            rootNetworkIdentity ??= GetComponent<MirrorNetworkIdentity>();
            rootOwnershipRelay ??= GetComponent<MirrorOwnershipRelay>();
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

        // Independent per-frame read of SDK authority; not derived from request/completion events.
        // A Mirror client cannot observe which other connection owns the object, only whether it is local.
        private void SampleNativeAuthority()
        {
            string nativeOwner;
            if (rootNetworkIdentity == null)
                nativeOwner = "None";
            else if (MirrorNetworkServer.active)
                nativeOwner = rootNetworkIdentity.connectionToClient != null
                    ? $"Client_{rootNetworkIdentity.connectionToClient.connectionId}"
                    : "Server";
            else
                nativeOwner = rootNetworkIdentity.isOwned ? GetLocalUserId() : "NotLocal(unobservable on client)";

            NativeAuthorityObservationRecorder.Instance?.Observe(
                GetTrackedObjectId(),
                GetLocalUserId(),
                GetCurrentHolderUserId(),
                nativeOwner,
                HasLocalAccess,
                HasLocalControl(rootNetworkIdentity),
                IsQueueRuntimeActive(),
                "Root NetworkIdentity; local=isOwned || (server && no client authority)");
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

            SessionOrchestratorMirror orchestrator = SessionOrchestratorMirror.Instance;
            if (orchestrator != null)
                return orchestrator.GetLocalUserId();

            return "UnknownUser";
        }

        protected override void RequestAuthorityIfNeeded()
        {
            if (Time.time - lastOwnershipRequestTime < ownershipRequestCooldownSeconds)
                return;

            lastOwnershipRequestTime = Time.time;

            if (rootOwnershipRelay == null || HasLocalControl(rootNetworkIdentity))
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
                    "Mirror logical microscope ownership request initiated.");
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

            if (rootOwnershipRelay != null && HasLocalControl(rootNetworkIdentity))
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
                    "Mirror local control was not observed before the measurement timeout."))
                return;

            if (!HasLocalControl(rootNetworkIdentity))
                return;

            authorityMeasurement.ObserveLocal(RootObjectKey, "Root NetworkIdentity reports local control.");
            if (!authorityMeasurement.TryResolve(
                    GetCurrentOwnerId(),
                    "Mirror logical microscope ownership transfer completed when local client became owner.",
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
                queueRuntime = SessionQueueRuntimeMirror.Instance;

            if (queueRuntime == null || !queueRuntime.IsSpawned)
                queueRuntime = FindFirstObjectByType<SessionQueueRuntimeMirror>();
        }

        private string GetTrackedObjectId()
        {
            if (sharedDefinition != null && !string.IsNullOrWhiteSpace(sharedDefinition.ObjectId))
                return sharedDefinition.ObjectId;

            return gameObject.name;
        }

        private string GetCurrentOwnerId()
        {
            if (rootNetworkIdentity == null || rootNetworkIdentity.connectionToClient == null)
                return "None";

            return $"Client_{rootNetworkIdentity.connectionToClient.connectionId}";
        }

        private static bool HasLocalControl(MirrorNetworkIdentity networkIdentity)
        {
            return networkIdentity != null &&
                   (networkIdentity.isOwned || (MirrorNetworkServer.active && networkIdentity.connectionToClient == null));
        }
    }
}
