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

        private float lastOwnershipRequestTime = -999f;
        private float lastOwnershipReleaseTime = -999f;
        private bool logicalAuthorityTransferPending;

        private void Awake()
        {
            BaseAwake();
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

            logicalAuthorityTransferPending = false;
        }

        private void Start()
        {
            BaseStart();
        }

        private void Update()
        {
            BaseUpdate();
            RefreshPendingLogicalAuthorityTransfer();
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

            if (rootOwnershipRelay != null && !HasLocalControl(rootNetworkIdentity))
            {
                AuthorityTransferRecorder.Instance?.RecordTransferRequested(
                    GetTrackedObjectId(),
                    GetLocalUserId(),
                    GetCurrentOwnerId(),
                    "Mirror logical microscope ownership request initiated.");

                rootOwnershipRelay.RequestOwnershipForLocalClient();
                logicalAuthorityTransferPending = true;
                return;
            }

            logicalAuthorityTransferPending = false;
        }

        protected override void OnLocalAccessChanged(bool hasAccess)
        {
            if (hasAccess)
            {
                RequestAuthorityIfNeeded();
                return;
            }

            if (!releaseOwnershipWhenAccessRevoked)
                return;

            if (Time.time - lastOwnershipReleaseTime < ownershipRequestCooldownSeconds)
                return;

            lastOwnershipReleaseTime = Time.time;
            logicalAuthorityTransferPending = false;

            if (rootOwnershipRelay != null && HasLocalControl(rootNetworkIdentity))
                rootOwnershipRelay.ReleaseOwnershipForLocalClient();
        }

        private void RefreshPendingLogicalAuthorityTransfer()
        {
            if (!logicalAuthorityTransferPending)
                return;

            if (!HasLocalAccess)
            {
                logicalAuthorityTransferPending = false;
                return;
            }

            if (!HasLocalControl(rootNetworkIdentity))
                return;

            logicalAuthorityTransferPending = false;
            AuthorityTransferRecorder recorder = AuthorityTransferRecorder.Instance;
            recorder?.RecordTransferCompleted(
                GetTrackedObjectId(),
                GetLocalUserId(),
                GetCurrentOwnerId(),
                true,
                "Mirror logical microscope ownership transfer completed when local client became owner.");

            if (recorder != null)
            {
                ResolveQueueRuntime();
                string rowJson = recorder.BuildMostRecentCompletedRowJson(GetTrackedObjectId(), GetLocalUserId());
                queueRuntime?.SubmitAuthorityTransferSnapshot(rowJson);
            }
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
