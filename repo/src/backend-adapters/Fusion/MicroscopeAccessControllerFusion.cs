using Fusion;
using System.Collections.Generic;
using System.Linq;
using Thesis.Orchestration.SharedSync;
using Thesis.Orchestration.Telemetry;
using UnityEngine;

namespace Thesis.Orchestration.Backends.Fusion
{
    [DisallowMultipleComponent]
    public class MicroscopeAccessControllerFusion : BaseMicroscopeAccessController
    {
        [Header("References")]
        [SerializeField] private SessionQueueRuntimeFusion queueRuntime;
        [SerializeField] private NetworkObject rootNetworkObject;

        [Header("Authority")]
        [SerializeField] private bool requestAuthorityForControlledChildren = false;
        [SerializeField] private float authorityRequestCooldownSeconds = 0.35f;
        [Tooltip("Seconds before a pending authority measurement is closed as TimedOut. 0 disables the timeout.")]
        [SerializeField] private float authorityMeasurementTimeoutSeconds = 10f;

        private readonly Dictionary<string, float> lastAuthorityRequestTimeByObjectId = new();
        // Native objects requested by the pending measurement only.
        private readonly Dictionary<int, NetworkObject> pendingAuthorityObjectsByInstanceId = new();
        private AuthorityMeasurementTracker authorityMeasurement;

        private void Awake()
        {
            BaseAwake();
            authorityMeasurement = new AuthorityMeasurementTracker(() => AuthorityTransferRecorder.Instance);

            if (queueRuntime == null)
                queueRuntime = SessionQueueRuntimeFusion.Instance;

            if (rootNetworkObject == null)
                rootNetworkObject = GetComponent<NetworkObject>();
        }

        private void OnEnable()
        {
            ResolveQueueRuntime();

            if (queueRuntime != null)
                queueRuntime.OnRuntimeStateUpdated += RefreshAccessState;
        }

        private void OnDisable()
        {
            CancelPendingLogicalAuthorityTransfer("Controller disabled before authority completion.");
            if (queueRuntime != null)
                queueRuntime.OnRuntimeStateUpdated -= RefreshAccessState;

            lastAuthorityRequestTimeByObjectId.Clear();
        }

        private void Start()
        {
            BaseStart();
        }

        private void Update()
        {
            BaseUpdate();
            RefreshPendingAuthorityTransfers();
            SampleNativeAuthority();
        }

        // Independent per-frame read of SDK state authority; not derived from request/completion events.
        private void SampleNativeAuthority()
        {
            NativeAuthorityObservationRecorder.Instance?.Observe(
                GetTrackedObjectId(),
                GetLocalUserId(),
                GetCurrentHolderUserId(),
                GetCurrentOwnerId(),
                HasLocalAccess,
                rootNetworkObject != null && rootNetworkObject.IsValid && rootNetworkObject.HasStateAuthority,
                IsQueueRuntimeActive(),
                "Root NetworkObject.StateAuthority; local=HasStateAuthority");
        }

        private string GetTrackedObjectId()
        {
            if (sharedDefinition != null && !string.IsNullOrWhiteSpace(sharedDefinition.ObjectId))
                return sharedDefinition.ObjectId;

            return gameObject.name;
        }

        private string GetCurrentOwnerId()
        {
            return GetCurrentOwnerId(rootNetworkObject);
        }

        private string GetCurrentOwnerId(NetworkObject obj)
        {
            if (obj == null || !obj.IsValid)
                return "None";

            if (obj.StateAuthority == PlayerRef.None)
                return "None";

            return $"Player_{obj.StateAuthority.RawEncoded}";
        }

        private void ResolveQueueRuntime()
        {
            if (queueRuntime == null)
                queueRuntime = SessionQueueRuntimeFusion.Instance;
            if (queueRuntime == null)
                queueRuntime = FindFirstObjectByType<SessionQueueRuntimeFusion>();
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

        public string GetCurrentHolderUserIdForSync()
        {
            return GetCurrentHolderUserId();
        }

        protected override string GetLocalUserId()
        {
            if (queueRuntime != null && queueRuntime.Runner != null && queueRuntime.Runner.IsRunning)
                return $"Player_{queueRuntime.Runner.LocalPlayer.RawEncoded}";

            SessionOrchestratorFusion orchestrator = SessionOrchestratorFusion.Instance;
            if (orchestrator != null)
            {
                string userId = orchestrator.GetLocalUserId();
                if (!string.IsNullOrWhiteSpace(userId) && userId != "UnknownUser")
                    return userId;
            }

            return "UnknownUser";
        }

        protected override void OnLocalAccessChanged(bool hasAccess)
        {
            if (!hasAccess)
            {
                CancelPendingLogicalAuthorityTransfer("Local access revoked before authority completion.");
                return;
            }

            RequestAuthorityIfNeeded();
        }

        protected override void RequestAuthorityIfNeeded()
        {
            var requests = new List<KeyValuePair<NetworkObject, string>>();
            AddAuthorityRequestIfNeeded(requests, rootNetworkObject, "root");

            foreach (var child in GetControlledChildObjects())
            {
                if (child == null)
                    continue;

                NetworkObject childNetworkObject = child.GetComponent<NetworkObject>();
                if (!ShouldRequestAuthorityForChild(child, childNetworkObject))
                    continue;

                AddAuthorityRequestIfNeeded(requests, childNetworkObject, child.name);
            }

            if (requests.Count == 0)
                return;

            // The measurement starts before RequestStateAuthority is issued. A retry while pending
            // re-sends native requests inside the same measurement.
            if (!authorityMeasurement.IsPending)
            {
                pendingAuthorityObjectsByInstanceId.Clear();
                var keys = new List<string>();
                foreach (var request in requests)
                {
                    keys.Add(ObjectKey(request.Key.GetInstanceID()));
                    pendingAuthorityObjectsByInstanceId[request.Key.GetInstanceID()] = request.Key;
                }

                authorityMeasurement.Begin(
                    GetTrackedObjectId(),
                    GetLocalUserId(),
                    GetCurrentOwnerId(requests[0].Key),
                    keys,
                    Time.realtimeSinceStartupAsDouble,
                    "Fusion logical microscope authority request initiated.");
            }

            foreach (var request in requests)
            {
                request.Key.RequestStateAuthority();
                Log($"Requested state authority for {request.Value}.");
            }
        }

        private static string ObjectKey(int instanceId)
        {
            return $"object:{instanceId}";
        }

        private bool ShouldRequestAuthorityForChild(GameObject childObject, NetworkObject childNetworkObject)
        {
            if (childNetworkObject == null || !childNetworkObject.IsValid)
                return false;

            if (childNetworkObject == rootNetworkObject)
                return false;

            if (rootNetworkObject == null)
                return true;

            bool isInteractiveChild = childObject != null && childObject.GetComponent("MouseUI") != null;
            if (!requestAuthorityForControlledChildren && !isInteractiveChild)
                return false;

            bool isNestedUnderRoot = childNetworkObject.transform.IsChildOf(rootNetworkObject.transform);
            if (!isNestedUnderRoot)
                return true;

            // Interactive microscope children may intentionally use a plain child NetworkObject
            // without additional Fusion NetworkBehaviours. Those still need state authority so
            // the holder can manipulate them and broadcast their transform via the root sync.
            if (isInteractiveChild)
                return true;

            // For non-interactive nested children, stay conservative unless they explicitly carry
            // Fusion network behaviour and the scene author opted into requesting child authority.
            return requestAuthorityForControlledChildren &&
                   childNetworkObject.GetComponents<NetworkBehaviour>().Length > 0;
        }

        private void AddAuthorityRequestIfNeeded(List<KeyValuePair<NetworkObject, string>> requests,
            NetworkObject networkObject, string label)
        {
            if (networkObject == null || !networkObject.IsValid || networkObject.HasStateAuthority)
                return;

            string authorityRequestKey = BuildAuthorityRequestKey(networkObject, label);
            if (lastAuthorityRequestTimeByObjectId.TryGetValue(authorityRequestKey, out float lastRequestTime) &&
                Time.time - lastRequestTime < authorityRequestCooldownSeconds)
            {
                return;
            }

            lastAuthorityRequestTimeByObjectId[authorityRequestKey] = Time.time;
            requests.Add(new KeyValuePair<NetworkObject, string>(networkObject, label));
        }

        private void RefreshPendingAuthorityTransfers()
        {
            if (!authorityMeasurement.IsPending)
                return;

            if (authorityMeasurement.CheckTimeout(Time.realtimeSinceStartupAsDouble, authorityMeasurementTimeoutSeconds,
                    "Fusion state authority of every requested object was not resolved before the measurement timeout."))
            {
                pendingAuthorityObjectsByInstanceId.Clear();
                return;
            }

            foreach (KeyValuePair<int, NetworkObject> pending in pendingAuthorityObjectsByInstanceId)
            {
                NetworkObject networkObject = pending.Value;
                if (networkObject == null || !networkObject.IsValid)
                    authorityMeasurement.ObserveFailed(ObjectKey(pending.Key), "NetworkObject destroyed or no longer valid.");
                else if (networkObject.HasStateAuthority)
                    authorityMeasurement.ObserveLocal(ObjectKey(pending.Key), "NetworkObject.HasStateAuthority is true.");
            }

            if (!authorityMeasurement.TryResolve(
                    GetCurrentOwnerId(),
                    "Fusion logical microscope authority transfer resolved for every requested object.",
                    out _,
                    out string measurementId))
                return;

            pendingAuthorityObjectsByInstanceId.Clear();
            AuthorityTransferRecorder recorder = AuthorityTransferRecorder.Instance;
            if (recorder != null)
            {
                ResolveQueueRuntime();
                queueRuntime?.SubmitAuthorityTransferSnapshot(recorder.BuildCompletedRowJson(measurementId));
            }
        }

        private void CancelPendingLogicalAuthorityTransfer(string details)
        {
            pendingAuthorityObjectsByInstanceId.Clear();
            authorityMeasurement?.Cancel(details);
        }

        private string BuildAuthorityRequestKey(NetworkObject networkObject, string label)
        {
            string objectName = networkObject != null && networkObject.gameObject != null
                ? networkObject.gameObject.name
                : "NetworkObject";

            return $"{GetTrackedObjectId()}/{label}/{objectName}";
        }
    }
}
