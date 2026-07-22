using Thesis.Orchestration.Telemetry;
using Unity.Collections;
using Unity.Netcode;
using UnityEngine;

namespace Thesis.Orchestration.Backends.Netcode.StandaloneShared
{
    [RequireComponent(typeof(NetworkObject))]
    public class StandaloneSharedObjectNetcode : NetworkBehaviour
    {
        [Header("Identity")]
        [SerializeField] private string objectId;
        [SerializeField] private string displayName;
        [SerializeField] private StandaloneSharedObjectCategoryNetcode category = StandaloneSharedObjectCategoryNetcode.Tool;

        [Header("Sync Options")]
        [SerializeField] private bool syncTransform = true;
        [SerializeField] private bool syncParenting = true;
        [SerializeField] private bool syncCustomState = true;
        [SerializeField] private bool requestOwnershipAutomatically = true;

        [Header("Transform Sync")]
        [SerializeField] private bool syncPosition = true;
        [SerializeField] private bool syncRotation = true;
        [SerializeField] private float positionLerpSpeed = 15f;
        [SerializeField] private float rotationLerpSpeed = 15f;

        [Header("Analytics")]
        [SerializeField] private bool trackStandaloneAnalytics = true;
        [SerializeField] private float repositionDistanceThreshold = 0.0001f;
        [SerializeField] private float repositionRotationThresholdDegrees = 0.1f;
        [SerializeField] private float repositionAnalyticsCooldownSeconds = 0.15f;

        [Header("Debug")]
        [SerializeField] private bool verboseLogs = true;

        private readonly NetworkVariable<Vector3> networkPosition = new(
            default,
            NetworkVariableReadPermission.Everyone,
            NetworkVariableWritePermission.Owner);

        private readonly NetworkVariable<Quaternion> networkRotation = new(
            Quaternion.identity,
            NetworkVariableReadPermission.Everyone,
            NetworkVariableWritePermission.Owner);

        private readonly NetworkVariable<FixedString512Bytes> networkParentPath = new(
            default,
            NetworkVariableReadPermission.Everyone,
            NetworkVariableWritePermission.Owner);

        private readonly NetworkVariable<FixedString512Bytes> networkCustomState = new(
            default,
            NetworkVariableReadPermission.Everyone,
            NetworkVariableWritePermission.Owner);

        private NetworkObject cachedNetworkObject;
        private NetcodeOwnershipRelay ownershipRelay;
        private IStandaloneNetworkStateAdapterNetcode stateAdapter;

        private Vector3 lastAnalyticsPosition;
        private Quaternion lastAnalyticsRotation;
        private string lastKnownParentPath = string.Empty;
        private float lastRepositionAnalyticsTime = -999f;

        private void Reset()
        {
            AutoFillIdentityFromGameObjectName();
        }

        private void Awake()
        {
            AutoFillIdentityFromGameObjectName();

            cachedNetworkObject = GetComponent<NetworkObject>();
            ownershipRelay = GetComponent<NetcodeOwnershipRelay>();
            stateAdapter = GetComponent<IStandaloneNetworkStateAdapterNetcode>();

            lastAnalyticsPosition = transform.position;
            lastAnalyticsRotation = transform.rotation;
            lastKnownParentPath = BuildParentPath(transform.parent);

            TelemetryLoggerCore.Instance?.RegisterAttachmentStateChanged(objectId, IsCurrentlyAttached());
        }

#if UNITY_EDITOR
        private void OnValidate()
        {
            AutoFillIdentityFromGameObjectName();
        }
#endif

        public override void OnNetworkSpawn()
        {
            base.OnNetworkSpawn();

            if (IsOwner)
            {
                WriteLocalStateToNetwork();
            }
            else
            {
                ApplyRemoteStateImmediately();
            }
        }

        private void Update()
        {
            if (!IsSpawned)
                return;

            if (IsOwner)
            {
                WriteLocalStateToNetwork();
                TryTrackStandaloneAnalytics();
                return;
            }

            ApplyRemoteStateSmoothed();
        }

        public void RequestOwnershipIfNeeded()
        {
            if (!requestOwnershipAutomatically)
                return;

            if (!IsSpawned || cachedNetworkObject == null || cachedNetworkObject.IsOwner)
                return;

            ownershipRelay ??= GetComponent<NetcodeOwnershipRelay>();
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

            return cachedNetworkObject.IsOwner;
        }

        private void WriteLocalStateToNetwork()
        {
            if (syncTransform)
            {
                if (syncPosition)
                    networkPosition.Value = transform.position;

                if (syncRotation)
                    networkRotation.Value = transform.rotation;
            }

            if (syncParenting)
                networkParentPath.Value = new FixedString512Bytes(BuildParentPath(transform.parent));

            if (syncCustomState)
                networkCustomState.Value = new FixedString512Bytes(stateAdapter != null ? stateAdapter.CaptureNetworkState() : string.Empty);
        }

        private void ApplyRemoteStateImmediately()
        {
            SnapRemoteTransform(
                transform,
                syncPosition && syncTransform,
                syncRotation && syncTransform,
                networkPosition.Value,
                networkRotation.Value);

            ApplyRemoteParentIfNeeded();

            if (syncCustomState && stateAdapter != null)
                stateAdapter.ApplyNetworkState(networkCustomState.Value.ToString());
        }

        private void ApplyRemoteStateSmoothed()
        {
            if (syncTransform)
            {
                ApplyRemoteTransform(
                    transform,
                    syncPosition,
                    syncRotation,
                    networkPosition.Value,
                    networkRotation.Value,
                    positionLerpSpeed,
                    rotationLerpSpeed);
            }

            if (syncParenting)
                ApplyRemoteParentIfNeeded();

            if (syncCustomState && stateAdapter != null)
                stateAdapter.ApplyNetworkState(networkCustomState.Value.ToString());
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

        private void ApplyRemoteParentIfNeeded()
        {
            string targetPath = networkParentPath.Value.ToString();
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
            SessionOrchestratorNetcode orchestrator = SessionOrchestratorNetcode.Instance;
            if (orchestrator != null)
                return orchestrator.GetLocalUserId();

            return NetworkManager != null ? $"Client_{NetworkManager.LocalClientId}" : "UnknownUser";
        }

        private void TLog(string eventType, string userId, string instrumentId, string details)
        {
            TelemetryLoggerCore.Instance?.LogEvent(eventType, userId, instrumentId, details);
        }

        private void Log(string message)
        {
            if (!verboseLogs)
                return;

            Debug.Log($"[StandaloneSharedObjectNetcode:{objectId}] {message}", this);
        }

        private static void ApplyRemoteTransform(
            Transform targetTransform,
            bool shouldSyncPosition,
            bool shouldSyncRotation,
            Vector3 targetPosition,
            Quaternion targetRotation,
            float targetPositionLerpSpeed,
            float targetRotationLerpSpeed)
        {
            if (targetTransform == null)
                return;

            if (shouldSyncPosition)
            {
                targetTransform.position = Vector3.Lerp(
                    targetTransform.position,
                    targetPosition,
                    Time.deltaTime * targetPositionLerpSpeed);
            }

            if (shouldSyncRotation)
            {
                targetTransform.rotation = Quaternion.Slerp(
                    targetTransform.rotation,
                    targetRotation,
                    Time.deltaTime * targetRotationLerpSpeed);
            }
        }

        private static void SnapRemoteTransform(
            Transform targetTransform,
            bool shouldSyncPosition,
            bool shouldSyncRotation,
            Vector3 targetPosition,
            Quaternion targetRotation)
        {
            if (targetTransform == null)
                return;

            if (shouldSyncPosition)
                targetTransform.position = targetPosition;

            if (shouldSyncRotation)
                targetTransform.rotation = targetRotation;
        }
    }
}
