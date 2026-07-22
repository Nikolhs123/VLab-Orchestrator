using Photon.Pun;
using Photon.Realtime;
using Thesis.Orchestration.Telemetry;
using UnityEngine;

namespace Thesis.Orchestration.SharedSync
{
    [RequireComponent(typeof(PhotonView))]
    public class StandaloneSharedObjectPUN2 : MonoBehaviourPun, IPunObservable
    {
        [Header("Identity")]
        [SerializeField] private string objectId;
        [SerializeField] private string displayName;
        [SerializeField] private StandaloneSharedObjectCategory category = StandaloneSharedObjectCategory.Tool;

        [Header("Sync Options")]
        [SerializeField] private bool syncTransform = true;
        [SerializeField] private bool syncParenting = true;
        [SerializeField] private bool syncCustomState = true;
        [SerializeField] private bool allowOwnershipTakeover = true;

        [Header("Transform Sync")]
        [SerializeField] private bool syncPosition = true;
        [SerializeField] private bool syncRotation = true;
        [SerializeField] private float positionLerpSpeed = 15f;
        [SerializeField] private float rotationLerpSpeed = 15f;

        [Header("Ownership")]
        [SerializeField] private bool requestOwnershipAutomatically = true;

        [Header("Analytics")]
        [SerializeField] private bool trackStandaloneAnalytics = true;
        [SerializeField] private float repositionDistanceThreshold = 0.0001f;
        [SerializeField] private float repositionRotationThresholdDegrees = 0.1f;
        [SerializeField] private float repositionAnalyticsCooldownSeconds = 0.15f;

        [Header("Debug")]
        [SerializeField] private bool verboseLogs = true;

        public string ObjectId => objectId;
        public string DisplayName => displayName;
        public StandaloneSharedObjectCategory Category => category;

        public bool SyncTransform => syncTransform;
        public bool SyncParenting => syncParenting;
        public bool SyncCustomState => syncCustomState;
        public bool AllowOwnershipTakeover => allowOwnershipTakeover;

        private PhotonView cachedPhotonView;
        private IStandaloneNetworkStateAdapter stateAdapter;

        private Vector3 targetPosition;
        private Quaternion targetRotation;

        private string targetParentPath = string.Empty;
        private string lastReceivedCustomState = string.Empty;

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

            cachedPhotonView = GetComponent<PhotonView>();
            stateAdapter = GetComponent<IStandaloneNetworkStateAdapter>();

            if (allowOwnershipTakeover && cachedPhotonView != null)
            {
                cachedPhotonView.OwnershipTransfer = OwnershipOption.Takeover;
            }

            targetPosition = transform.position;
            targetRotation = transform.rotation;

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

        private void Update()
        {
            if (!PhotonNetwork.IsConnected || !PhotonNetwork.InRoom)
                return;

            if (!cachedPhotonView.IsMine)
            {
                if (syncTransform)
                {
                    if (syncPosition)
                    {
                        transform.position = Vector3.Lerp(
                            transform.position,
                            targetPosition,
                            Time.deltaTime * positionLerpSpeed);
                    }

                    if (syncRotation)
                    {
                        transform.rotation = Quaternion.Slerp(
                            transform.rotation,
                            targetRotation,
                            Time.deltaTime * rotationLerpSpeed);
                    }
                }

                if (syncParenting)
                {
                    ApplyRemoteParentIfNeeded();
                }

                if (syncCustomState && stateAdapter != null && !string.IsNullOrWhiteSpace(lastReceivedCustomState))
                {
                    stateAdapter.ApplyNetworkState(lastReceivedCustomState);
                }
            }
            else
            {
                TryTrackStandaloneAnalytics();
            }
        }

        public void RequestOwnershipIfNeeded()
        {
            if (!requestOwnershipAutomatically)
                return;

            if (!PhotonNetwork.IsConnected || !PhotonNetwork.InRoom)
                return;

            if (cachedPhotonView == null)
                return;

            if (cachedPhotonView.IsMine)
                return;

            cachedPhotonView.RequestOwnership();
            TLog("StandaloneOwnershipRequested", GetLocalUserId(), objectId, "Requested ownership.");
            Log("Ownership requested.");

            if (trackStandaloneAnalytics)
            {
                TelemetryLoggerCore.Instance?.RegisterObjectRegrab(GetLocalUserId(), objectId);
            }
        }

        public bool HasLocalOwnership()
        {
            if (!PhotonNetwork.IsConnected || !PhotonNetwork.InRoom)
                return true;

            return cachedPhotonView != null && cachedPhotonView.IsMine;
        }

        public void OnPhotonSerializeView(PhotonStream stream, PhotonMessageInfo info)
        {
            if (stream.IsWriting)
            {
                if (syncTransform)
                {
                    if (syncPosition)
                        stream.SendNext(transform.position);

                    if (syncRotation)
                        stream.SendNext(transform.rotation);
                }

                if (syncParenting)
                {
                    stream.SendNext(BuildParentPath(transform.parent));
                }

                if (syncCustomState)
                {
                    string customState = stateAdapter != null ? stateAdapter.CaptureNetworkState() : string.Empty;
                    stream.SendNext(customState);
                }
            }
            else
            {
                if (syncTransform)
                {
                    if (syncPosition)
                        targetPosition = (Vector3)stream.ReceiveNext();

                    if (syncRotation)
                        targetRotation = (Quaternion)stream.ReceiveNext();
                }

                if (syncParenting)
                {
                    targetParentPath = (string)stream.ReceiveNext();
                }

                if (syncCustomState)
                {
                    lastReceivedCustomState = (string)stream.ReceiveNext();
                }
            }
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

                string localUserId = GetLocalUserId();
                TelemetryLoggerCore.Instance?.RegisterObjectRegrab(localUserId, objectId);

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
            string currentParentPath = BuildParentPath(transform.parent);
            if (currentParentPath == targetParentPath)
                return;

            Transform targetParent = ResolveParentPath(targetParentPath);
            transform.SetParent(targetParent, true);

            Log($"Applied remote parent path: '{targetParentPath}'");
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
            if (PhotonNetwork.LocalPlayer == null)
                return "UnknownUser";

            return $"Actor_{PhotonNetwork.LocalPlayer.ActorNumber}";
        }

        private void TLog(string eventType, string userId, string instrumentId, string details)
        {
            TelemetryLoggerCore.Instance?.LogEvent(eventType, userId, instrumentId, details);
        }

        private void Log(string message)
        {
            if (!verboseLogs)
                return;

            Debug.Log($"[StandaloneSharedObjectPUN2:{objectId}] {message}", this);
        }
    }

    public enum StandaloneSharedObjectCategory
    {
        Tool = 0,
        Consumable = 1,
        Sample = 2,
        Container = 3,
        Attachment = 4,
        Other = 5
    }

    public interface IStandaloneNetworkStateAdapter
    {
        string CaptureNetworkState();
        void ApplyNetworkState(string stateJson);
    }

    public static class StandaloneSharedObjectPUN2Extensions
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