using System;
using System.Collections.Generic;
using System.Linq;
using Thesis.Orchestration.Analytics;
using Thesis.Orchestration.Core;
using Thesis.Orchestration.Core.Models;
using Thesis.Orchestration.EditorTools;
using Thesis.Orchestration.Runtime;
using Thesis.Orchestration.SharedSync;
using Thesis.Orchestration.Telemetry;
using UnityEngine;
using MirrorNetworkBehaviour = global::Mirror.NetworkBehaviour;
using MirrorNetworkClient = global::Mirror.NetworkClient;
using MirrorNetworkIdentity = global::Mirror.NetworkIdentity;
using MirrorNetworkServer = global::Mirror.NetworkServer;

namespace Thesis.Orchestration.Backends.Mirror
{
    [DisallowMultipleComponent]
    [RequireComponent(typeof(MirrorNetworkIdentity))]
    public class SessionQueueRuntimeMirror : MirrorNetworkBehaviour
    {
        public const uint SpawnAssetId = 0x51A9A5E1;
        private const string JoinedEntrySeparator = "||";

        public static SessionQueueRuntimeMirror Instance { get; private set; }

        [Header("References")]
        [SerializeField] private SessionOrchestratorMirror sessionOrchestrator;
        [SerializeField] private SharedObjectSyncRegistry sharedRegistry;

        [Header("Timeout Settings")]
        [SerializeField] private float idleTimeoutSeconds = 20f;
        [SerializeField] private float timeoutCheckIntervalSeconds = 1f;

        [Header("History")]
        [SerializeField] private int maxHistoryEntries = 20;

        [Header("Debug")]
        [SerializeField] private bool verboseLogs = true;

        private QueueRuntimeSharedLogic sharedLogic;
        private bool queueRuntimeActive;
        private bool sessionStarted;
        private string currentInstrumentId = string.Empty;
        private string currentHolderUserId = "None";
        private readonly List<string> currentQueueSnapshot = new();
        private readonly List<string> replicatedHistorySnapshot = new();

        private float timeoutTimer;
        private bool eventsBound;
        private bool hasInitializedForCurrentSession;
        private string lastSubmittedLifecycleRowJson = string.Empty;

        private string sharedUserCardStatsJson = string.Empty;
        private UserCardStatsSnapshot cachedSharedUserCardStats = new UserCardStatsSnapshot();

        public bool HasSharedUserCardStats =>
            cachedSharedUserCardStats != null &&
            cachedSharedUserCardStats.users != null &&
            cachedSharedUserCardStats.users.Count > 0;

        public IReadOnlyList<UserCardStatsEntry> SharedUserCardStats =>
            cachedSharedUserCardStats != null && cachedSharedUserCardStats.users != null
                ? cachedSharedUserCardStats.users
                : Array.Empty<UserCardStatsEntry>();

        public bool SessionStarted => sessionStarted;
        public bool QueueRuntimeActive => queueRuntimeActive;
        public string CurrentInstrumentId => currentInstrumentId;
        public string CurrentHolderUserId => string.IsNullOrWhiteSpace(currentHolderUserId) ? "None" : currentHolderUserId;
        public List<string> CurrentQueueSnapshot => new(currentQueueSnapshot);
        public List<string> ReplicatedHistorySnapshot => new(replicatedHistorySnapshot);
        public bool IsSpawned => netId != 0;

        public event Action OnRuntimeStateUpdated;

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }

            Instance = this;
            sharedLogic = new QueueRuntimeSharedLogic(maxHistoryEntries);

            ResolveReferences();
            ResetRuntimeState(clearSharedUserCardStats: true, notifyListeners: false);
        }

        private void OnEnable()
        {
            ResolveReferences();
            TryBindEvents();
        }

        public override void OnStartServer()
        {
            base.OnStartServer();
            Instance = this;
            timeoutTimer = 0f;
            hasInitializedForCurrentSession = false;
        }

        public override void OnStartClient()
        {
            base.OnStartClient();
            Instance = this;
            ResolveReferences();
            TryBindEvents();
            OnRuntimeStateUpdated?.Invoke();
        }

        public override void OnStopClient()
        {
            base.OnStopClient();
            HandleStopNetwork();
        }

        public override void OnStopServer()
        {
            base.OnStopServer();
            HandleStopNetwork();
        }

        private void OnDestroy()
        {
            HandleStopNetwork();
        }

        private void Update()
        {
            ResolveReferences();
            TryBindEvents();
            SubmitLocalLifecycleSnapshotIfNeeded();

            if (!MirrorNetworkServer.active)
                return;

            if (sessionStarted && !queueRuntimeActive && !hasInitializedForCurrentSession)
                TryInitializeQueueRuntime();

            if (!queueRuntimeActive || sharedLogic == null || !sharedLogic.IsInitialized)
                return;

            timeoutTimer += Time.deltaTime;
            if (timeoutTimer >= timeoutCheckIntervalSeconds)
            {
                timeoutTimer = 0f;
                CheckTimeoutForCurrentInstrument();
            }
        }

        public void RequestBeginSessionForLocalPlayer()
        {
            if (!IsSpawned)
            {
                AddLocalStatus("Cannot begin session yet: SessionQueueRuntimeMirror is not spawned.");
                return;
            }

            string userId = GetLocalUserId();
            if (isServer)
                HandleBeginSessionRequest(userId);
            else
                CmdRequestBeginSession(userId);
        }

        public void RequestAccessForLocalPlayer()
        {
            if (!queueRuntimeActive)
            {
                AddLocalStatus("Queue runtime not active yet.");
                return;
            }

            string userId = GetLocalUserId();
            TLog("AccessRequested", userId, currentInstrumentId, "Local player requested access.");

            if (isServer)
                HandleRequestAccess(userId);
            else
                CmdRequestAccess(userId);
        }

        public void ReleaseAccessForLocalPlayer()
        {
            if (!queueRuntimeActive)
            {
                AddLocalStatus("Queue runtime not active yet.");
                return;
            }

            string userId = GetLocalUserId();
            TLog("ReleaseRequested", userId, currentInstrumentId, "Local player requested release.");

            if (isServer)
                HandleReleaseAccess(userId);
            else
                CmdReleaseAccess(userId);
        }

        public bool TryForceReleaseCurrentHolderForFinalization(string reason)
        {
            if (!queueRuntimeActive)
                return false;

            string holderUserId = CurrentHolderUserId;
            if (string.IsNullOrWhiteSpace(holderUserId) || holderUserId == "None")
                return false;

            if (isServer)
            {
                ForceReleaseCurrentHolderOnServer(reason);
                return true;
            }

            CmdForceReleaseCurrentHolderForFinalization(reason ?? "Finalization");
            return true;
        }

        public void HeartbeatForLocalPlayer()
        {
            if (!queueRuntimeActive)
                return;

            string userId = GetLocalUserId();
            if (isServer)
                HandleHeartbeat(userId);
            else
                CmdHeartbeat(userId);
        }

        public string GetLocalUserId()
        {
            if (sessionOrchestrator != null)
                return sessionOrchestrator.GetLocalUserId();

            return "UnknownUser";
        }

        [global::Mirror.Command(requiresAuthority = false)]
        private void CmdRequestBeginSession(string requestingUserId)
        {
            HandleBeginSessionRequest(requestingUserId);
        }

        [global::Mirror.Command(requiresAuthority = false)]
        private void CmdRequestAccess(string userId)
        {
            HandleRequestAccess(userId);
        }

        [global::Mirror.Command(requiresAuthority = false)]
        private void CmdReleaseAccess(string userId)
        {
            HandleReleaseAccess(userId);
        }

        [global::Mirror.Command(requiresAuthority = false)]
        private void CmdForceReleaseCurrentHolderForFinalization(string reason)
        {
            ForceReleaseCurrentHolderOnServer(reason);
        }

        [global::Mirror.Command(requiresAuthority = false)]
        private void CmdHeartbeat(string userId)
        {
            HandleHeartbeat(userId);
        }

        [global::Mirror.Command(requiresAuthority = false)]
        private void CmdSubmitLifecycleSnapshot(string rowJson)
        {
            SessionLifecycleTracker.Instance?.ApplyRowJson(rowJson);
        }

        [global::Mirror.Command(requiresAuthority = false)]
        private void CmdSubmitAuthorityTransferSnapshot(string rowJson)
        {
            AuthorityTransferRecorder.Instance?.ApplyCompletedRowJson(rowJson);
        }

        public void SubmitAuthorityTransferSnapshot(string rowJson)
        {
            if (!IsSpawned || isServer || string.IsNullOrWhiteSpace(rowJson))
                return;

            CmdSubmitAuthorityTransferSnapshot(rowJson);
        }

        [global::Mirror.ClientRpc]
        private void RpcApplyReplicatedState(
            bool runtimeActive,
            bool replicatedSessionStarted,
            string instrumentId,
            string holderUserId,
            string joinedQueue,
            string joinedHistory,
            string encodedSharedUserCardStats)
        {
            ApplyReplicatedState(runtimeActive, replicatedSessionStarted, instrumentId, holderUserId, joinedQueue, joinedHistory, encodedSharedUserCardStats);
        }

        private void ApplyReplicatedState(
            bool runtimeActive,
            bool replicatedSessionStarted,
            string instrumentId,
            string holderUserId,
            string joinedQueue,
            string joinedHistory,
            string encodedSharedUserCardStats)
        {
            queueRuntimeActive = runtimeActive;
            sessionStarted = replicatedSessionStarted;
            currentInstrumentId = instrumentId ?? string.Empty;
            currentHolderUserId = string.IsNullOrWhiteSpace(holderUserId) ? "None" : holderUserId;

            currentQueueSnapshot.Clear();
            currentQueueSnapshot.AddRange(SplitJoinedString(joinedQueue));

            replicatedHistorySnapshot.Clear();
            replicatedHistorySnapshot.AddRange(SplitJoinedString(joinedHistory));

            sharedUserCardStatsJson = encodedSharedUserCardStats ?? string.Empty;
            cachedSharedUserCardStats = UserCardStatsSnapshot.Decode(sharedUserCardStatsJson);

            OnRuntimeStateUpdated?.Invoke();
        }

        private void SubmitLocalLifecycleSnapshotIfNeeded()
        {
            if (!IsSpawned || SessionLifecycleTracker.Instance == null)
                return;

            string localUserId = GetLocalUserId();
            if (string.IsNullOrWhiteSpace(localUserId) || localUserId == "UnknownUser")
                return;

            string rowJson = SessionLifecycleTracker.Instance.BuildBestRowJson(localUserId);
            if (string.IsNullOrWhiteSpace(rowJson) ||
                string.Equals(lastSubmittedLifecycleRowJson, rowJson, StringComparison.Ordinal))
            {
                return;
            }

            lastSubmittedLifecycleRowJson = rowJson;

            if (isServer)
            {
                SessionLifecycleTracker.Instance.ApplyRowJson(rowJson, localUserId);
                return;
            }

            CmdSubmitLifecycleSnapshot(rowJson);
        }

        private void HandleBeginSessionRequest(string requestingUserId)
        {
            if (!isServer || sessionStarted)
                return;

            sessionStarted = true;
            hasInitializedForCurrentSession = false;
            sharedLogic ??= new QueueRuntimeSharedLogic(maxHistoryEntries);
            sharedLogic.AddHistory("Session started by " + requestingUserId);
            TLog("MirrorSessionStarted", requestingUserId, currentInstrumentId, "Session start approved on server.");

            TryInitializeQueueRuntime();
            BroadcastState("Session started.");
        }

        private void HandleRequestAccess(string userId)
        {
            if (!isServer || !queueRuntimeActive || sharedLogic == null || !sharedLogic.IsInitialized)
                return;

            sharedLogic.RecordQueueRequest(
                userId,
                currentInstrumentId,
                CurrentHolderUserId,
                CurrentQueueSnapshot,
                "Mirror request received on server.");

            QueueDecision decision = sharedLogic.RequestAccess(userId, currentInstrumentId);
            HandleDecisionAuthoritatively($"RequestAccess({userId})", decision);
        }

        private void HandleReleaseAccess(string userId)
        {
            if (!isServer || !queueRuntimeActive || sharedLogic == null || !sharedLogic.IsInitialized)
                return;

            QueueDecision decision = sharedLogic.ReleaseAccess(userId, currentInstrumentId);
            HandleDecisionAuthoritatively($"ReleaseAccess({userId})", decision);
        }

        private void HandleHeartbeat(string userId)
        {
            if (!isServer || !queueRuntimeActive || sharedLogic == null || !sharedLogic.IsInitialized)
                return;

            sharedLogic.RegisterHeartbeat(userId, currentInstrumentId);
        }

        private void ResolveReferences()
        {
            sessionOrchestrator ??= SessionOrchestratorMirror.Instance;
            if (sessionOrchestrator == null)
                sessionOrchestrator = FindFirstObjectByType<SessionOrchestratorMirror>();

            sharedRegistry ??= SharedObjectSyncRegistry.Instance;
            if (sharedRegistry == null)
                sharedRegistry = FindFirstObjectByType<SharedObjectSyncRegistry>();

            sharedLogic ??= new QueueRuntimeSharedLogic(maxHistoryEntries);
        }

        private void TryBindEvents()
        {
            if (eventsBound || sessionOrchestrator == null)
                return;

            sessionOrchestrator.OnSessionStateChanged += HandleSessionStateChanged;
            sessionOrchestrator.OnPlayerListUpdated += HandlePlayerListUpdated;
            eventsBound = true;
        }

        private void UnbindEvents()
        {
            if (!eventsBound || sessionOrchestrator == null)
                return;

            sessionOrchestrator.OnSessionStateChanged -= HandleSessionStateChanged;
            sessionOrchestrator.OnPlayerListUpdated -= HandlePlayerListUpdated;
            eventsBound = false;
        }

        private void HandleSessionStateChanged(bool started)
        {
            if (!started)
            {
                ResetRuntimeState(clearSharedUserCardStats: true, notifyListeners: true);
                return;
            }

            if (!isServer)
                return;

            if (sessionStarted)
                return;

            sessionStarted = true;
            hasInitializedForCurrentSession = false;
            TryInitializeQueueRuntime();
            BroadcastState("Mirror session state synced.");
        }

        private void HandlePlayerListUpdated(List<int> players)
        {
            if (!isServer || !queueRuntimeActive || sharedLogic == null || !sharedLogic.IsInitialized)
                return;

            List<string> activeUserIds = players != null
                ? players.Select(ConvertClientToUserId).ToList()
                : new List<string>();

            sharedLogic.SyncUsers(
                currentInstrumentId,
                activeUserIds,
                removedHolderId => sharedLogic.ReleaseAccess(removedHolderId, currentInstrumentId));

            BroadcastState("Mirror player list synced.");
        }

        private void TryInitializeQueueRuntime()
        {
            if (!isServer)
                return;

            if (sharedRegistry == null)
            {
                AddLocalStatus("Cannot initialize Mirror queue runtime: missing shared registry.");
                return;
            }

            SharedSyncObjectDefinition instrumentDefinition =
                sharedRegistry.GetPreferredQueueControlledObject(OrchestrationBackendType.Mirror);

            if (instrumentDefinition == null)
            {
                AddLocalStatus("No shared instrument with queue control found for Mirror.");
                return;
            }

            currentInstrumentId = instrumentDefinition.ObjectId;
            currentHolderUserId = "None";

            IEnumerable<string> userIds = GetActiveUserIds();
            sharedLogic.Initialize(currentInstrumentId, userIds);

            currentQueueSnapshot.Clear();
            replicatedHistorySnapshot.Clear();

            queueRuntimeActive = true;
            hasInitializedForCurrentSession = true;
            timeoutTimer = 0f;

            TLog("QueueRuntimeInitialized", GetLocalUserId(), currentInstrumentId, "Mirror queue runtime initialized on server.");
            BroadcastState("Queue runtime initialized.");
        }

        private void CheckTimeoutForCurrentInstrument()
        {
            if (!isServer || sharedLogic == null || !sharedLogic.IsInitialized)
                return;

            if (string.IsNullOrWhiteSpace(currentInstrumentId))
                return;

            QueueDecision decision = sharedLogic.CheckTimeout(currentInstrumentId, TimeSpan.FromSeconds(idleTimeoutSeconds));
            if (decision != null)
                HandleDecisionAuthoritatively("TimeoutCheck()", decision);
        }

        private void BroadcastState(string reason)
        {
            if (!isServer || sharedLogic == null || !sharedLogic.IsInitialized)
                return;

            currentHolderUserId = sharedLogic.GetCurrentHolder(currentInstrumentId);

            string joinedQueue = sharedLogic.BuildJoinedQueue(currentInstrumentId);
            string joinedHistory = sharedLogic.BuildJoinedHistory();

            currentQueueSnapshot.Clear();
            currentQueueSnapshot.AddRange(SplitJoinedString(joinedQueue));

            replicatedHistorySnapshot.Clear();
            replicatedHistorySnapshot.AddRange(SplitJoinedString(joinedHistory));

            AddLocalStatus($"State updated: {reason}");
            PublishSharedUserCardStats();

            ApplyReplicatedState(
                queueRuntimeActive,
                sessionStarted,
                currentInstrumentId,
                currentHolderUserId,
                joinedQueue,
                joinedHistory,
                sharedUserCardStatsJson);

            RpcApplyReplicatedState(
                queueRuntimeActive,
                sessionStarted,
                currentInstrumentId,
                currentHolderUserId,
                joinedQueue,
                joinedHistory,
                sharedUserCardStatsJson);
        }

        private void PublishSharedUserCardStats()
        {
            if (!isServer)
                return;

            List<string> activeUserIds = GetActiveUserIds().Distinct().ToList();
            SessionLifecycleTracker.Instance?.EnsureClientsObserved(activeUserIds);

            string masterUserId = sessionOrchestrator != null ? sessionOrchestrator.GetMasterUserId() : "None";
            UserCardStatsSnapshot snapshot = Thesis.Orchestration.Telemetry.SharedUserCardStats.BuildSnapshot(
                activeUserIds,
                userId => OrchestrationPlayerNameRegistry.ResolveDisplayName(userId, userId),
                userId => userId == masterUserId,
                CurrentHolderUserId,
                CurrentQueueSnapshot,
                UsageTimingTracker.Instance);

            sharedUserCardStatsJson = UserCardStatsSnapshot.Encode(snapshot);
            cachedSharedUserCardStats = snapshot;
        }

        private void ResetRuntimeState(bool clearSharedUserCardStats, bool notifyListeners)
        {
            queueRuntimeActive = false;
            sessionStarted = false;
            currentInstrumentId = string.Empty;
            currentHolderUserId = "None";
            timeoutTimer = 0f;
            hasInitializedForCurrentSession = false;

            currentQueueSnapshot.Clear();
            replicatedHistorySnapshot.Clear();

            sharedLogic = new QueueRuntimeSharedLogic(maxHistoryEntries);

            if (clearSharedUserCardStats)
            {
                sharedUserCardStatsJson = string.Empty;
                cachedSharedUserCardStats = new UserCardStatsSnapshot();
            }

            if (notifyListeners)
                OnRuntimeStateUpdated?.Invoke();
        }

        private void HandleDecisionAuthoritatively(string source, QueueDecision decision)
        {
            string holderBefore = currentHolderUserId;
            string holderAfter = sharedLogic.GetCurrentHolder(currentInstrumentId);
            List<string> queueSnapshotAfter = SplitJoinedString(sharedLogic.BuildJoinedQueue(currentInstrumentId));

            sharedLogic.RecordDecisionAnalytics(
                source,
                decision,
                currentInstrumentId,
                holderBefore,
                holderAfter,
                queueSnapshotAfter);

            BroadcastState(source);
        }

        private void ForceReleaseCurrentHolderOnServer(string reason)
        {
            if (!isServer || !queueRuntimeActive || sharedLogic == null || !sharedLogic.IsInitialized)
                return;

            string holderUserId = CurrentHolderUserId;
            if (string.IsNullOrWhiteSpace(holderUserId) || holderUserId == "None")
                return;

            QueueDecision decision = sharedLogic.ReleaseAccess(holderUserId, currentInstrumentId);
            HandleDecisionAuthoritatively($"ForceReleaseForFinalization({reason ?? "Finalization"})", decision);
        }

        private IEnumerable<string> GetActiveUserIds()
        {
            HashSet<string> userIds = new HashSet<string>();

            if (sessionOrchestrator != null && sessionOrchestrator.CurrentPlayers != null)
            {
                foreach (int clientId in sessionOrchestrator.CurrentPlayers)
                    userIds.Add(ConvertClientToUserId(clientId));
            }

            if (sessionOrchestrator != null && sessionOrchestrator.TryGetLocalClientId(out int localClientId))
                userIds.Add(ConvertClientToUserId(localClientId));

            if (MirrorNetworkServer.active)
            {
                foreach (KeyValuePair<int, global::Mirror.NetworkConnectionToClient> pair in MirrorNetworkServer.connections)
                {
                    if (pair.Value != null)
                        userIds.Add(ConvertClientToUserId(pair.Key));
                }
            }

            return userIds.OrderBy(id => id);
        }

        private static List<string> SplitJoinedString(string joined)
        {
            if (string.IsNullOrWhiteSpace(joined))
                return new List<string>();

            return new List<string>(joined.Split(new[] { JoinedEntrySeparator }, StringSplitOptions.RemoveEmptyEntries));
        }

        private static string ConvertClientToUserId(int clientId)
        {
            return $"Client_{clientId}";
        }

        private void HandleStopNetwork()
        {
            UnbindEvents();
            ResetRuntimeState(clearSharedUserCardStats: true, notifyListeners: false);

            if (Instance == this)
                Instance = null;
        }

        private void TLog(string eventType, string userId, string instrumentId, string details)
        {
            TelemetryLoggerCore.Instance?.LogEvent(eventType, userId, instrumentId, details);
        }

        private void AddLocalStatus(string message)
        {
            Log(message);
        }

        private void Log(string message)
        {
            if (!verboseLogs)
                return;

            Debug.Log($"[SessionQueueRuntimeMirror] {message}", this);
        }
    }
}
