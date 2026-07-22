using System.Collections.Generic;
using FishNet.Object;
using Thesis.Orchestration.Backends.FishNet;
using Thesis.Orchestration.SharedSync;
using Thesis.Orchestration.Telemetry;
using UnityEngine;

namespace Thesis.Orchestration.Backends.FishNet.StandaloneShared
{
    [RequireComponent(typeof(NetworkObject))]
    public class StandaloneSharedObjectFishNet : NetworkBehaviour
    {
        [Header("Identity")]
        [SerializeField] private string objectId;
        [SerializeField] private string displayName;
        [SerializeField] private StandaloneSharedObjectCategoryFishNet category = StandaloneSharedObjectCategoryFishNet.Tool;

        [Header("Sync Options")]
        [SerializeField] private bool syncParenting = true;
        [SerializeField] private bool syncCustomState = true;
        [SerializeField] private bool requestOwnershipAutomatically = true;

        [Header("Analytics")]
        [SerializeField] private bool trackStandaloneAnalytics = true;
        [SerializeField] private float repositionDistanceThreshold = 0.0001f;
        [SerializeField] private float repositionRotationThresholdDegrees = 0.1f;
        [SerializeField] private float repositionAnalyticsCooldownSeconds = 0.15f;

        [Header("Debug")]
        [SerializeField] private bool verboseLogs = true;

        private NetworkObject cachedNetworkObject;
        private FishNetOwnershipRelay ownershipRelay;
        private IStandaloneNetworkStateAdapterFishNet stateAdapter;

        private Vector3 lastAnalyticsPosition;
        private Quaternion lastAnalyticsRotation;
        private string lastKnownParentPath = string.Empty;
        private string lastSubmittedParentPath = string.Empty;
        private string lastSubmittedCustomState = string.Empty;
        private float lastRepositionAnalyticsTime = -999f;

        private void Reset()
        {
            AutoFillIdentityFromGameObjectName();
        }

        private void Awake()
        {
            AutoFillIdentityFromGameObjectName();

            cachedNetworkObject = GetComponent<NetworkObject>();
            ownershipRelay = GetComponent<FishNetOwnershipRelay>();
            stateAdapter = GetComponent<IStandaloneNetworkStateAdapterFishNet>();

            lastAnalyticsPosition = transform.position;
            lastAnalyticsRotation = transform.rotation;
            lastKnownParentPath = BuildParentPath(transform.parent);
            lastSubmittedParentPath = lastKnownParentPath;
            lastSubmittedCustomState = stateAdapter != null ? stateAdapter.CaptureNetworkState() : string.Empty;

            TelemetryLoggerCore.Instance?.RegisterAttachmentStateChanged(objectId, IsCurrentlyAttached());
        }

#if UNITY_EDITOR
        private void OnValidate()
        {
            AutoFillIdentityFromGameObjectName();
        }
#endif

        public override void OnStartClient()
        {
            base.OnStartClient();
            SharedObjectSyncRegistry.ResolveOrCreate()?.DiscoverSceneObjects();

            if (IsOwner)
                SubmitStateIfChanged(force: true);
        }

        private void Update()
        {
            if (!IsSpawned)
                return;

            if (!IsOwner)
                return;

            SubmitStateIfChanged(force: false);
            TryTrackStandaloneAnalytics();
        }

        public void RequestOwnershipIfNeeded()
        {
            if (!requestOwnershipAutomatically)
                return;

            if (!IsSpawned || cachedNetworkObject == null || cachedNetworkObject.IsOwner)
                return;

            ownershipRelay ??= GetComponent<FishNetOwnershipRelay>();
            if (ownershipRelay == null)
                return;

            ownershipRelay.RequestOwnershipForLocalClient();
            TLog("StandaloneAuthorityRequested", GetLocalUserId(), objectId, "Requested ownership.");

            if (trackStandaloneAnalytics)
                TelemetryLoggerCore.Instance?.RegisterObjectRegrab(GetLocalUserId(), objectId);
        }

        public bool HasLocalAuthority()
        {
            if (!IsSpawned || cachedNetworkObject == null)
                return true;

            return cachedNetworkObject.IsOwner ||
                   (cachedNetworkObject.IsServerInitialized && !cachedNetworkObject.Owner.IsValid);
        }

        [ServerRpc]
        private void SubmitStateServerRpc(string parentPath, string customState)
        {
            ApplyStateAuthoritatively(parentPath, customState);
        }

        [ObserversRpc(BufferLast = true, RunLocally = true)]
        private void RpcApplyReplicatedState(string parentPath, string customState)
        {
            if (syncParenting)
                ApplyRemoteParentIfNeeded(parentPath);

            if (syncCustomState && stateAdapter != null)
                stateAdapter.ApplyNetworkState(customState);

            lastKnownParentPath = BuildParentPath(transform.parent);
        }

        private void SubmitStateIfChanged(bool force)
        {
            string parentPath = syncParenting ? BuildParentPath(transform.parent) : string.Empty;
            string customState = syncCustomState && stateAdapter != null ? stateAdapter.CaptureNetworkState() : string.Empty;

            if (!force &&
                parentPath == lastSubmittedParentPath &&
                customState == lastSubmittedCustomState)
            {
                return;
            }

            if (IsServerInitialized)
                ApplyStateAuthoritatively(parentPath, customState);
            else
                SubmitStateServerRpc(parentPath, customState);

            lastSubmittedParentPath = parentPath;
            lastSubmittedCustomState = customState;
        }

        private void ApplyStateAuthoritatively(string parentPath, string customState)
        {
            RpcApplyReplicatedState(parentPath, customState);
        }

        private void TryTrackStandaloneAnalytics()
        {
            if (!trackStandaloneAnalytics)
                return;

            bool positionChanged = Vector3.Distance(transform.position, lastAnalyticsPosition) > repositionDistanceThreshold;
            bool rotationChanged = Quaternion.Angle(transform.rotation, lastAnalyticsRotation) > repositionRotationThresholdDegrees;

            if ((positionChanged || rotationChanged) &&
                Time.time - lastRepositionAnalyticsTime >= repositionAnalyticsCooldownSeconds)
            {
                string localUserId = GetLocalUserId();
                TelemetryLoggerCore.Instance?.RegisterObjectReposition(localUserId, objectId);
                TelemetryLoggerCore.Instance?.RegisterObjectInteraction(localUserId, objectId);
                TelemetryLoggerCore.Instance?.RegisterManipulatedObject(localUserId, objectId);

                lastRepositionAnalyticsTime = Time.time;
                lastAnalyticsPosition = transform.position;
                lastAnalyticsRotation = transform.rotation;
            }

            string currentParentPath = BuildParentPath(transform.parent);
            if (currentParentPath == lastKnownParentPath)
                return;

            TelemetryLoggerCore.Instance?.RegisterAttachmentStateChanged(objectId, IsCurrentlyAttached());
            TelemetryLoggerCore.Instance?.RegisterObjectRegrab(GetLocalUserId(), objectId);
            lastKnownParentPath = currentParentPath;
        }

        private bool IsCurrentlyAttached()
        {
            return transform.parent != null;
        }

        private void ApplyRemoteParentIfNeeded(string targetPath)
        {
            string currentPath = BuildParentPath(transform.parent);
            if (currentPath == targetPath)
                return;

            Transform targetParent = ResolveParentPath(targetPath);
            transform.SetParent(targetParent, true);
        }

        private void AutoFillIdentityFromGameObjectName()
        {
            if (string.IsNullOrWhiteSpace(objectId))
                objectId = gameObject.name;

            if (string.IsNullOrWhiteSpace(displayName))
                displayName = gameObject.name;
        }

        private string BuildParentPath(Transform parent)
        {
            if (parent == null)
                return string.Empty;

            return parent.GetHierarchyPathFromRoot();
        }

        private Transform ResolveParentPath(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
                return null;

            GameObject found = GameObject.Find(path);
            return found != null ? found.transform : null;
        }

        private string GetLocalUserId()
        {
            SessionOrchestratorFishNet orchestrator = SessionOrchestratorFishNet.Instance;
            if (orchestrator != null)
                return orchestrator.GetLocalUserId();

            return NetworkManager != null &&
                   NetworkManager.ClientManager != null &&
                   NetworkManager.ClientManager.Connection.IsValid
                ? $"Client_{NetworkManager.ClientManager.Connection.ClientId}"
                : "UnknownUser";
        }

        private void TLog(string eventType, string userId, string instrumentId, string details)
        {
            TelemetryLoggerCore.Instance?.LogEvent(eventType, userId, instrumentId, details);
        }

        private void Log(string message)
        {
            if (!verboseLogs)
                return;

            Debug.Log($"[StandaloneSharedObjectFishNet:{objectId}] {message}", this);
        }
    }

    public interface IStandaloneNetworkStateAdapterFishNet
    {
        string CaptureNetworkState();
        void ApplyNetworkState(string stateJson);
    }

    public enum StandaloneSharedObjectCategoryFishNet
    {
        Tool = 0,
        Consumable = 1,
        Sample = 2,
        Container = 3,
        Attachment = 4,
        Other = 5
    }

    public static class StandaloneSharedObjectFishNetExtensions
    {
        public static string GetHierarchyPathFromRoot(this Transform current)
        {
            if (current == null)
                return string.Empty;

            Stack<string> parts = new Stack<string>();
            Transform walker = current;

            while (walker != null)
            {
                parts.Push(walker.name);
                walker = walker.parent;
            }

            return string.Join("/", parts.ToArray());
        }
    }
}
