using Photon.Pun;
using Photon.Realtime;
using System.Collections.Generic;
using Thesis.Orchestration.SharedSync;
using Thesis.Orchestration.Telemetry;
using UnityEngine;

namespace Thesis.Orchestration.Backends.PUN2
{
    [RequireComponent(typeof(PhotonView))]
    public class MicroscopeAccessControllerPUN2 : BaseMicroscopeAccessController, IPunOwnershipCallbacks
    {
        [Header("References")]
        [SerializeField] private SessionQueueRuntimePUN2 queueRuntime;
        [SerializeField] private PhotonView photonView;

        [Header("Ownership")]
        [Tooltip("If true, ownership will also be requested for all controlled child objects that have PhotonViews.")]
        [SerializeField] private bool requestOwnershipForControlledChildren = true;

        [Tooltip("Minimum time between repeated ownership requests for the same PhotonView.")]
        [SerializeField] private float ownershipRequestCooldownSeconds = 0.5f;

        [Tooltip("Seconds before a pending authority measurement is closed as TimedOut. 0 disables the timeout.")]
        [SerializeField] private float authorityMeasurementTimeoutSeconds = 10f;

        private readonly Dictionary<int, float> lastOwnershipRequestTimeByViewId = new();
        private AuthorityMeasurementTracker authorityMeasurement;

        private void Awake()
        {
            BaseAwake();
            authorityMeasurement = new AuthorityMeasurementTracker(() => AuthorityTransferRecorder.Instance);

            if (photonView == null)
                photonView = GetComponent<PhotonView>();

            if (queueRuntime == null)
                queueRuntime = SessionQueueRuntimePUN2.Instance;
        }

        private void OnEnable()
        {
            ResolveQueueRuntime();

            PhotonNetwork.AddCallbackTarget(this);

            if (queueRuntime != null)
                queueRuntime.OnRuntimeStateUpdated += RefreshAccessState;
        }

        private void OnDisable()
        {
            CancelPendingLogicalAuthorityTransfer("Controller disabled before authority completion.");
            PhotonNetwork.RemoveCallbackTarget(this);
            lastOwnershipRequestTimeByViewId.Clear();

            if (queueRuntime != null)
                queueRuntime.OnRuntimeStateUpdated -= RefreshAccessState;
        }

        private void Start()
        {
            BaseStart();
        }

        private void Update()
        {
            BaseUpdate();
            authorityMeasurement.CheckTimeout(Time.realtimeSinceStartupAsDouble, authorityMeasurementTimeoutSeconds,
                "PUN2 ownership of every requested PhotonView was not resolved before the measurement timeout.");
            SampleNativeAuthority();
        }

        // Independent per-frame read of SDK ownership; not derived from ownership callbacks.
        private void SampleNativeAuthority()
        {
            NativeAuthorityObservationRecorder.Instance?.Observe(
                GetTrackedObjectId(),
                GetLocalUserId(),
                GetCurrentHolderUserId(),
                GetCurrentOwnerId(photonView),
                HasLocalAccess,
                photonView != null && photonView.IsMine,
                IsQueueRuntimeActive(),
                "Root PhotonView.OwnerActorNr; local=IsMine");
        }

        private static string ViewKey(PhotonView view)
        {
            return $"view:{view.ViewID}";
        }

        private string GetTrackedObjectId()
        {
            if (sharedDefinition != null && !string.IsNullOrWhiteSpace(sharedDefinition.ObjectId))
                return sharedDefinition.ObjectId;

            return gameObject.name;
        }

        private string GetCurrentOwnerId(PhotonView view)
        {
            if (view == null || view.Owner == null)
                return "None";

            return $"Actor_{view.OwnerActorNr}";
        }

        private void ResolveQueueRuntime()
        {
            queueRuntime ??= SessionQueueRuntimePUN2.Instance;
            if (queueRuntime == null)
                queueRuntime = FindFirstObjectByType<SessionQueueRuntimePUN2>();
        }

        protected override bool CanEvaluateAccessState()
        {
            ResolveQueueRuntime();
            return queueRuntime != null && sharedDefinition != null && photonView != null;
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
            if (PhotonNetwork.LocalPlayer == null)
                return "UnknownUser";

            return $"Actor_{PhotonNetwork.LocalPlayer.ActorNumber}";
        }

        protected override void RequestAuthorityIfNeeded()
        {
            var requests = new List<KeyValuePair<PhotonView, string>>();
            AddOwnershipRequestIfNeeded(requests, photonView, "root");

            if (requestOwnershipForControlledChildren)
            {
                foreach (var child in GetControlledChildObjects())
                {
                    if (child != null)
                        AddOwnershipRequestIfNeeded(requests, child.GetComponent<PhotonView>(), child.name);
                }
            }

            if (requests.Count == 0)
                return;

            // The measurement starts before any PhotonView operation is issued, so a callback raised
            // synchronously by RequestOwnership is still attributed to this measurement. A retry while
            // pending re-sends native requests inside the same measurement.
            if (!authorityMeasurement.IsPending)
            {
                var keys = new List<string>();
                foreach (var request in requests)
                    keys.Add(ViewKey(request.Key));

                authorityMeasurement.Begin(
                    GetTrackedObjectId(),
                    GetLocalUserId(),
                    GetCurrentOwnerId(requests[0].Key),
                    keys,
                    Time.realtimeSinceStartupAsDouble,
                    "PUN2 logical microscope ownership request initiated.");
            }

            foreach (var request in requests)
                IssueOwnershipRequest(request.Key, request.Value);
        }

        protected override void OnLocalAccessChanged(bool hasAccess)
        {
            if (!hasAccess)
                CancelPendingLogicalAuthorityTransfer("Local access revoked before authority completion.");
            TLog(
                hasAccess ? "MicroscopeLocalAccessGranted" : "MicroscopeLocalAccessRevoked",
                GetLocalUserId(),
                sharedDefinition != null ? sharedDefinition.ObjectId : name,
                $"Local access changed to {hasAccess}.");
        }

        private void AddOwnershipRequestIfNeeded(List<KeyValuePair<PhotonView, string>> requests, PhotonView targetView, string label)
        {
            // Already authoritative: avoid per-frame telemetry spam.
            if (targetView == null || targetView.IsMine)
                return;

            int viewId = targetView.ViewID;
            float now = Time.time;

            if (lastOwnershipRequestTimeByViewId.TryGetValue(viewId, out float lastTime) &&
                now - lastTime < ownershipRequestCooldownSeconds)
                return;

            lastOwnershipRequestTimeByViewId[viewId] = now;
            requests.Add(new KeyValuePair<PhotonView, string>(targetView, label));
        }

        private void IssueOwnershipRequest(PhotonView targetView, string label)
        {
            int viewId = targetView.ViewID;
            int ownerBefore = targetView.OwnerActorNr;
            targetView.RequestOwnership();

            Log($"Requested ownership for {label}. ViewID={viewId}, OwnerActorNr={ownerBefore}");
            TLog(
                "OwnershipRequested",
                GetLocalUserId(),
                sharedDefinition != null ? sharedDefinition.ObjectId : name,
                $"Requested ownership for {label}, ViewID={viewId}, CurrentOwner={ownerBefore}, MeasurementId={authorityMeasurement.MeasurementId}");
        }

        private void CancelPendingLogicalAuthorityTransfer(string details)
        {
            authorityMeasurement?.Cancel(details);
        }

        private void TryCompleteLogicalAuthorityTransfer(string ownerAfter, string details)
        {
            if (!authorityMeasurement.TryResolve(ownerAfter, details, out _, out string measurementId))
                return;

            AuthorityTransferRecorder recorder = AuthorityTransferRecorder.Instance;
            if (recorder != null)
            {
                ResolveQueueRuntime();
                queueRuntime?.SubmitAuthorityTransferSnapshot(recorder.BuildCompletedRowJson(measurementId));
            }
        }

        // ----------------------------------------------------
        // Photon ownership callbacks (authority transfer analytics)
        // IPunOwnershipCallbacks are global: they fire for every PhotonView in the room,
        // so only views requested by the pending measurement are considered.
        // ----------------------------------------------------

        public void OnOwnershipRequest(PhotonView targetView, Player requestingPlayer)
        {
            // No-op: analytics are recorded by the requesting client.
        }

        public void OnOwnershipTransfered(PhotonView targetView, Player previousOwner)
        {
            if (targetView == null || PhotonNetwork.LocalPlayer == null)
                return;

            // Record completion only on the client that became the owner (avoids duplicate rows).
            if (targetView.OwnerActorNr != PhotonNetwork.LocalPlayer.ActorNumber)
                return;

            if (!authorityMeasurement.ObserveLocal(ViewKey(targetView),
                    $"OnOwnershipTransfered ViewID={targetView.ViewID} previousOwner={previousOwner?.ActorNumber ?? 0}."))
                return;

            TryCompleteLogicalAuthorityTransfer(
                GetLocalUserId(),
                "PUN2 logical microscope ownership transfer callbacks received for every requested PhotonView.");
        }

        public void OnOwnershipTransferFailed(PhotonView targetView, Player senderOfFailedRequest)
        {
            if (targetView == null || senderOfFailedRequest == null || PhotonNetwork.LocalPlayer == null)
                return;

            // Record failure only on the client that sent the request.
            if (senderOfFailedRequest.ActorNumber != PhotonNetwork.LocalPlayer.ActorNumber)
                return;

            if (!authorityMeasurement.ObserveFailed(ViewKey(targetView),
                    $"OnOwnershipTransferFailed ViewID={targetView.ViewID}."))
                return;

            TryCompleteLogicalAuthorityTransfer(
                GetCurrentOwnerId(photonView),
                "PUN2 logical microscope ownership transfer resolved with at least one failed PhotonView.");
        }

        private void TLog(string eventType, string userId, string instrumentId, string details)
        {
            TelemetryLoggerCore.Instance?.LogEvent(eventType, userId, instrumentId, details);
        }
    }
}
