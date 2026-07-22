using Fusion;
using Thesis.Orchestration.Telemetry;
using UnityEngine;

namespace Thesis.Orchestration.Backends.Fusion.StandaloneShared
{
    /// <summary>
    /// Fusion equivalent of StandaloneSharedObjectPUN2.
    ///
    /// Attach this to any standalone shared scene object already placed in the scene
    /// (e.g. Slide, ImmersionOil).
    ///
    /// Responsibilities:
    /// - auto-uses GameObject name as object id / display name by default
    /// - Fusion state authority request
    /// - transform sync
    /// - parenting sync (by hierarchy path)
    /// - custom state sync through an optional adapter implementing
    ///   IStandaloneNetworkStateAdapterFusion
    /// - interaction analytics emission
    /// </summary>
    [RequireComponent(typeof(NetworkObject))]
    public class StandaloneSharedObjectFusion : NetworkBehaviour
    {
        [Header("Identity")]
        [SerializeField] private string objectId;
        [SerializeField] private string displayName;
        [SerializeField] private StandaloneSharedObjectCategoryFusion category = StandaloneSharedObjectCategoryFusion.Tool;

        [Header("Sync Options")]
        [SerializeField] private bool syncTransform = true;
        [SerializeField] private bool syncParenting = true;
        [SerializeField] private bool syncCustomState = true;
        [SerializeField] private bool requestAuthorityAutomatically = true;

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

        public string ObjectId => objectId;
        public string DisplayName => displayName;
        public StandaloneSharedObjectCategoryFusion Category => category;

        [Networked] private Vector3 NetworkPosition { get; set; }
        [Networked] private Quaternion NetworkRotation { get; set; }
        [Networked] private NetworkString<_256> NetworkParentPath { get; set; }
        [Networked] private NetworkString<_512> NetworkCustomState { get; set; }

        private NetworkObject cachedNetworkObject;
        private IStandaloneNetworkStateAdapterFusion stateAdapter;

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
            stateAdapter = GetComponent<IStandaloneNetworkStateAdapterFusion>();

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

        public override void Spawned()
        {
            if (Object.HasStateAuthority)
            {
                if (syncPosition)
                    NetworkPosition = transform.position;

                if (syncRotation)
                    NetworkRotation = transform.rotation;

                if (syncParenting)
                    NetworkParentPath = BuildParentPath(transform.parent);

                if (syncCustomState)
                    NetworkCustomState = stateAdapter != null ? stateAdapter.CaptureNetworkState() : string.Empty;
            }
            else
            {
                if (syncPosition)
                    transform.position = NetworkPosition;

                if (syncRotation)
                    transform.rotation = NetworkRotation;

                if (syncParenting)
                {
                    Transform targetParent = ResolveParentPath(NetworkParentPath.ToString());
                    transform.SetParent(targetParent, true);
                }

                if (syncCustomState && stateAdapter != null)
                {
                    string incoming = NetworkCustomState.ToString();
                    if (!string.IsNullOrWhiteSpace(incoming))
                    {
                        stateAdapter.ApplyNetworkState(incoming);
                    }
                }
            }
        }

        public override void FixedUpdateNetwork()
        {
            if (!Object || !Object.IsValid)
                return;

            if (!Object.HasStateAuthority)
                return;

            if (syncTransform)
            {
                if (syncPosition)
                    NetworkPosition = transform.position;

                if (syncRotation)
                    NetworkRotation = transform.rotation;
            }

            if (syncParenting)
            {
                NetworkParentPath = BuildParentPath(transform.parent);
            }

            if (syncCustomState)
            {
                NetworkCustomState = stateAdapter != null ? stateAdapter.CaptureNetworkState() : string.Empty;
            }
        }

        public override void Render()
        {
            if (!Object || !Object.IsValid)
                return;

            if (Object.HasStateAuthority)
            {
                TryTrackStandaloneAnalytics();
                return;
            }

            if (syncTransform)
            {
                if (syncPosition)
                {
                    transform.position = Vector3.Lerp(
                        transform.position,
                        NetworkPosition,
                        Time.deltaTime * positionLerpSpeed);
                }

                if (syncRotation)
                {
                    transform.rotation = Quaternion.Slerp(
                        transform.rotation,
                        NetworkRotation,
                        Time.deltaTime * rotationLerpSpeed);
                }
            }

            if (syncParenting)
            {
                ApplyRemoteParentIfNeeded();
            }

            if (syncCustomState && stateAdapter != null)
            {
                string incoming = NetworkCustomState.ToString();
                if (!string.IsNullOrWhiteSpace(incoming))
                {
                    stateAdapter.ApplyNetworkState(incoming);
                }
            }
        }

        public void RequestAuthorityIfNeeded()
        {
            if (!requestAuthorityAutomatically)
                return;

            if (Runner == null || !Runner.IsRunning)
                return;

            if (cachedNetworkObject == null || !cachedNetworkObject.IsValid)
                return;

            if (cachedNetworkObject.HasStateAuthority)
                return;

            cachedNetworkObject.RequestStateAuthority();
            TLog("StandaloneAuthorityRequested", GetLocalUserId(), objectId, "Requested state authority.");
            Log("State authority requested.");

            if (trackStandaloneAnalytics)
            {
                TelemetryLoggerCore.Instance?.RegisterObjectRegrab(GetLocalUserId(), objectId);
            }
        }

        public bool HasLocalAuthority()
        {
            if (Runner == null || !Runner.IsRunning)
                return true;

            return cachedNetworkObject != null && cachedNetworkObject.HasStateAuthority;
        }

        private void TryTrackStandaloneAnalytics()
        {
            if (!trackStandaloneAnalytics)
                return;

            bool positionChanged =
                Vector3.Distance(transform.position, lastAnalyticsPosition) > repositionDistanceThreshold;

            bool rotationChanged =
                Quaternion.Angle(transform.rotation, lastAnalyticsRotation) > repositionRotationThresholdDegrees;

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
            if (currentParentPath != lastKnownParentPath)
            {
                bool nowAttached = IsCurrentlyAttached();
                TelemetryLoggerCore.Instance?.RegisterAttachmentStateChanged(objectId, nowAttached);
                TelemetryLoggerCore.Instance?.RegisterObjectRegrab(GetLocalUserId(), objectId);

                lastKnownParentPath = currentParentPath;
                Log($"Parent changed. Attached={nowAttached}");
            }
        }

        private bool IsCurrentlyAttached()
        {
            return transform.parent != null;
        }

        private void ApplyRemoteParentIfNeeded()
        {
            string targetPath = NetworkParentPath.ToString();
            string currentPath = BuildParentPath(transform.parent);

            if (currentPath == targetPath)
                return;

            Transform targetParent = ResolveParentPath(targetPath);
            transform.SetParent(targetParent, true);

            Log($"Applied remote parent path: '{targetPath}'");
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
            SessionOrchestratorFusion orchestrator = SessionOrchestratorFusion.Instance;
            if (orchestrator != null)
                return orchestrator.GetLocalUserId();

            if (Runner == null || !Runner.IsRunning)
                return "UnknownUser";

            return $"Player_{Runner.LocalPlayer.RawEncoded}";
        }

        private void TLog(string eventType, string userId, string instrumentId, string details)
        {
            TelemetryLoggerCore.Instance?.LogEvent(eventType, userId, instrumentId, details);
        }

        private void Log(string message)
        {
            if (!verboseLogs)
                return;

            Debug.Log($"[StandaloneSharedObjectFusion:{objectId}] {message}", this);
        }
    }

    public enum StandaloneSharedObjectCategoryFusion
    {
        Tool = 0,
        Consumable = 1,
        Sample = 2,
        Container = 3,
        Attachment = 4,
        Other = 5
    }

    public interface IStandaloneNetworkStateAdapterFusion
    {
        string CaptureNetworkState();
        void ApplyNetworkState(string stateJson);
    }

    public static class StandaloneSharedObjectFusionExtensions
    {
        public static string GetHierarchyPathFromRoot(this Transform current)
        {
            if (current == null)
                return string.Empty;

            System.Collections.Generic.Stack<string> parts = new System.Collections.Generic.Stack<string>();
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