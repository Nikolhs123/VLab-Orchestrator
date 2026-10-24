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
using Unity.Collections;
using Unity.Netcode;
using UnityEngine;

namespace Thesis.Orchestration.Backends.Netcode
{
    [RequireComponent(typeof(NetworkObject))]
    public class SessionQueueRuntimeNetcode : NetworkBehaviour
    {
        private const int MaxSharedUserCardStatsLength = 4096;
        private const int MaxFixedString512SafeChars = 480;
        private const float ConnectedPlayersRefreshIntervalSeconds = 0.5f;
        private const string JoinedEntrySeparator = "||";

        public static SessionQueueRuntimeNetcode Instance { get; private set; }

        [Header("References")]
        [SerializeField] private SessionOrchestratorNetcode sessionOrchestrator;
        [SerializeField] private SharedObjectSyncRegistry sharedRegistry;

        [Header("Timeout Settings")]
        [SerializeField] private float idleTimeoutSeconds = 20f;
        [SerializeField] private float timeoutCheckIntervalSeconds = 1f;

        [Header("History")]
        [SerializeField] private int maxHistoryEntries = 20;

        [Header("Debug")]
        [SerializeField] private bool verboseLogs = true;

        private QueueRuntimeSharedLogic sharedLogic;
        private float timeoutTimer;
        private bool eventsBound;
        private bool hasInitializedForCurrentSession;
        private string lastRenderedSignature = string.Empty;
        private string lastSharedUserCardStatsSignature = string.Empty;
        private string lastSubmittedLifecycleRowJson = string.Empty;
        private float nextConnectedPlayersAuditTime;
        private float nextConnectedPlayersRequestTime;
        private UserCardStatsSnapshot cachedSharedUserCardStats = new UserCardStatsSnapshot();

        private readonly NetworkVariable<bool> sessionStartedNetworked = new(
            false,
            NetworkVariableReadPermission.Everyone,
            NetworkVariableWritePermission.Server);

        private readonly NetworkVariable<bool> queueRuntimeActiveNetworked = new(
            false,
            NetworkVariableReadPermission.Everyone,
            NetworkVariableWritePermission.Server);

        private readonly NetworkVariable<FixedString64Bytes> currentInstrumentIdNetworked = new(
            default,
            NetworkVariableReadPermission.Everyone,
            NetworkVariableWritePermission.Server);

        private readonly NetworkVariable<FixedString64Bytes> sessionRunIdNetworked = new(
            default,
            NetworkVariableReadPermission.Everyone,
            NetworkVariableWritePermission.Server);

        private readonly NetworkVariable<FixedString64Bytes> currentHolderUserIdNetworked = new(
            default,
            NetworkVariableReadPermission.Everyone,
            NetworkVariableWritePermission.Server);

        private readonly NetworkVariable<FixedString512Bytes> currentQueueJoinedNetworked = new(
            default,
            NetworkVariableReadPermission.Everyone,
            NetworkVariableWritePermission.Server);

        private readonly NetworkVariable<FixedString512Bytes> replicatedHistoryJoinedNetworked = new(
            default,
            NetworkVariableReadPermission.Everyone,
            NetworkVariableWritePermission.Server);

        private readonly NetworkVariable<FixedString512Bytes> connectedPlayersJoinedNetworked = new(
            default,
            NetworkVariableReadPermission.Everyone,
            NetworkVariableWritePermission.Server);

        private readonly NetworkVariable<FixedString4096Bytes> sharedUserCardStatsJsonNetworked = new(
            default,
            NetworkVariableReadPermission.Everyone,
            NetworkVariableWritePermission.Server);

        public bool SessionStarted => IsSpawned && sessionStartedNetworked.Value;
        public bool QueueRuntimeActive => IsSpawned && queueRuntimeActiveNetworked.Value;
        public string CurrentInstrumentId => IsSpawned ? currentInstrumentIdNetworked.Value.ToString() : string.Empty;
        public string SessionRunId => IsSpawned ? sessionRunIdNetworked.Value.ToString() : string.Empty;
        public string CurrentHolderUserId => NormalizeHolderId(IsSpawned ? currentHolderUserIdNetworked.Value.ToString() : string.Empty);
        public List<string> CurrentQueueSnapshot => IsSpawned ? SplitJoinedString(currentQueueJoinedNetworked.Value.ToString()) : new List<string>();
        public List<string> ReplicatedHistorySnapshot => IsSpawned ? SplitJoinedString(replicatedHistoryJoinedNetworked.Value.ToString()) : new List<string>();
        public List<string> ConnectedPlayersSnapshot => IsSpawned ? SplitJoinedString(connectedPlayersJoinedNetworked.Value.ToString()) : new List<string>();

        public bool HasSharedUserCardStats =>
            cachedSharedUserCardStats != null &&
            cachedSharedUserCardStats.users != null &&
            cachedSharedUserCardStats.users.Count > 0;

        public IReadOnlyList<UserCardStatsEntry> SharedUserCardStats =>
            cachedSharedUserCardStats != null && cachedSharedUserCardStats.users != null
                ? cachedSharedUserCardStats.users
                : Array.Empty<UserCardStatsEntry>();

        public event Action OnRuntimeStateUpdated;

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }

            Instance = this;

            if (sessionOrchestrator == null)
                sessionOrchestrator = SessionOrchestratorNetcode.Instance;

            if (sharedRegistry == null)
                sharedRegistry = SharedObjectSyncRegistry.ResolveOrCreate();

            sharedLogic = new QueueRuntimeSharedLogic(maxHistoryEntries);
        }

        private void OnEnable()
        {
            TryBindEvents();
        }

        public override void OnNetworkSpawn()
        {
            base.OnNetworkSpawn();

            if (sessionOrchestrator == null)
                sessionOrchestrator = SessionOrchestratorNetcode.Instance;

            if (sharedRegistry == null)
                sharedRegistry = SharedObjectSyncRegistry.ResolveOrCreate();

            sharedLogic ??= new QueueRuntimeSharedLogic(maxHistoryEntries);
            timeoutTimer = 0f;
            nextConnectedPlayersAuditTime = 0f;
            nextConnectedPlayersRequestTime = 0f;
            lastRenderedSignature = string.Empty;
            lastSharedUserCardStatsSignature = string.Empty;
            TryBindEvents();
            PullSharedUserCardStats();
            SubmitLocalLifecycleSnapshotIfNeeded();
            RefreshRuntimeStateIfNeeded();

            if (IsServer)
                BroadcastState("Netcode queue runtime spawned.");
        }

        public override void OnNetworkDespawn()
        {
            base.OnNetworkDespawn();

            UnbindEvents();
            timeoutTimer = 0f;
            nextConnectedPlayersAuditTime = 0f;
            nextConnectedPlayersRequestTime = 0f;
            hasInitializedForCurrentSession = false;
            lastRenderedSignature = string.Empty;
            lastSharedUserCardStatsSignature = string.Empty;
            cachedSharedUserCardStats = new UserCardStatsSnapshot();
        }

        private void Update()
        {
            TryBindEvents();

            if (!IsSpawned)
                return;

            SubmitLocalLifecycleSnapshotIfNeeded();

            if (IsServer)
            {
                ReconcileConnectedPlayersIfNeeded();

                if (sessionStartedNetworked.Value && !queueRuntimeActiveNetworked.Value && !hasInitializedForCurrentSession)
                    TryInitializeQueueRuntime();

                if (queueRuntimeActiveNetworked.Value && sharedLogic != null && sharedLogic.IsInitialized)
                {
                    timeoutTimer += Time.deltaTime;
                    if (timeoutTimer >= timeoutCheckIntervalSeconds)
                    {
                        timeoutTimer = 0f;
                        CheckTimeoutForCurrentInstrument();
                    }
                }
            }
            else
            {
                RequestConnectedPlayersRefreshIfNeeded();
            }

            RefreshRuntimeStateIfNeeded();
        }

        private void OnDisable()
        {
            UnbindEvents();
        }

        public override void OnDestroy()
        {
            UnbindEvents();

            if (Instance == this)
                Instance = null;

            base.OnDestroy();
        }

        public void RequestBeginSessionForLocalPlayer()
        {
            if (!IsSpawned)
            {
                AddLocalStatus("Cannot begin session yet: SessionQueueRuntimeNetcode is not spawned.");
                return;
            }

            RequestBeginSessionServerRpc(GetLocalUserId());
        }

        public void RequestAccessForLocalPlayer()
        {
            if (!QueueRuntimeActive)
            {
                AddLocalStatus("Queue runtime not active yet.");
                return;
            }

            string userId = GetLocalUserId();
            TLog("AccessRequested", userId, CurrentInstrumentId, "Local player requested access.");
            RequestAccessServerRpc(userId, CurrentInstrumentId);
        }

        public void ReleaseAccessForLocalPlayer()
        {
            if (!QueueRuntimeActive)
            {
                AddLocalStatus("Queue runtime not active yet.");
                return;
            }

            string userId = GetLocalUserId();
            TLog("ReleaseRequested", userId, CurrentInstrumentId, "Local player requested release.");
            Thesis.Orchestration.SharedSync.LocalReleaseIntent.Mark();
            ReleaseAccessServerRpc(userId, CurrentInstrumentId);
        }

        public bool TryForceReleaseCurrentHolderForFinalization(string reason)
        {
            if (!QueueRuntimeActive || string.IsNullOrWhiteSpace(CurrentHolderUserId) || CurrentHolderUserId == "None")
                return false;

            if (IsServer)
            {
                ForceReleaseCurrentHolderOnServer(reason);
                return true;
            }

            ForceReleaseCurrentHolderForFinalizationServerRpc(reason ?? "Finalization");
            return true;
        }

        public void HeartbeatForLocalPlayer()
        {
            if (!QueueRuntimeActive)
                return;

            HeartbeatServerRpc(GetLocalUserId(), CurrentInstrumentId);
        }

        [ServerRpc(RequireOwnership = false)]
        private void RequestBeginSessionServerRpc(string requestingUserId, ServerRpcParams rpcParams = default)
        {
            if (!IsServer || sessionStartedNetworked.Value)
                return;

            if (!QueueRequestIdentity.Accept(requestingUserId, ConvertClientToUserId(rpcParams.Receive.SenderClientId),
                    "BeginSession", CurrentInstrumentId))
                return;

            sessionStartedNetworked.Value = true;
            hasInitializedForCurrentSession = false;
            sharedLogic.AddHistory("Session started by " + requestingUserId);
            TLog("NetcodeSessionStarted", requestingUserId, CurrentInstrumentId, "Session start approved on server.");

            TryInitializeQueueRuntime();
            BroadcastState("Session started.");
        }

        [ServerRpc(RequireOwnership = false)]
        private void RequestAccessServerRpc(string userId, string instrumentId, ServerRpcParams rpcParams = default)
        {
            if (!IsServer || !queueRuntimeActiveNetworked.Value || sharedLogic == null || !sharedLogic.IsInitialized)
                return;

            if (!QueueRequestIdentity.Accept(userId, ConvertClientToUserId(rpcParams.Receive.SenderClientId),
                    "RequestAccess", instrumentId))
                return;

            sharedLogic.RecordQueueRequest(
                userId,
                instrumentId,
                CurrentHolderUserId,
                CurrentQueueSnapshot,
                "Netcode request received on server.");

            QueueDecision decision = sharedLogic.RequestAccess(userId, instrumentId);
            HandleDecisionAuthoritatively($"RequestAccess({userId})", decision);
        }

        [ServerRpc(RequireOwnership = false)]
        private void ReleaseAccessServerRpc(string userId, string instrumentId, ServerRpcParams rpcParams = default)
        {
            if (!IsServer || !queueRuntimeActiveNetworked.Value || sharedLogic == null || !sharedLogic.IsInitialized)
                return;

            if (!QueueRequestIdentity.Accept(userId, ConvertClientToUserId(rpcParams.Receive.SenderClientId),
                    "ReleaseAccess", instrumentId))
                return;

            QueueDecision decision = sharedLogic.ReleaseAccess(userId, instrumentId);
            HandleDecisionAuthoritatively($"ReleaseAccess({userId})", decision);
        }

        [ServerRpc(RequireOwnership = false)]
        private void ForceReleaseCurrentHolderForFinalizationServerRpc(string reason)
        {
            ForceReleaseCurrentHolderOnServer(reason);
        }

        [ServerRpc(RequireOwnership = false)]
        private void HeartbeatServerRpc(string userId, string instrumentId, ServerRpcParams rpcParams = default)
        {
            if (!IsServer || !queueRuntimeActiveNetworked.Value || sharedLogic == null || !sharedLogic.IsInitialized)
                return;

            if (!QueueRequestIdentity.Accept(userId, ConvertClientToUserId(rpcParams.Receive.SenderClientId),
                    "Heartbeat", instrumentId))
                return;

            sharedLogic.RegisterHeartbeat(userId, instrumentId);
        }

        [ServerRpc(RequireOwnership = false)]
        private void RequestConnectedPlayersRefreshServerRpc()
        {
            BroadcastState("Connected-player roster refresh requested.");
        }

        [ServerRpc(RequireOwnership = false)]
        private void SubmitLifecycleSnapshotServerRpc(string rowJson)
        {
            SessionLifecycleTracker.Instance?.ApplyRowJson(rowJson);
        }

        [ServerRpc(RequireOwnership = false)]
        private void SubmitAuthorityTransferSnapshotServerRpc(string rowJson)
        {
            AuthorityTransferRecorder.Instance?.ApplyCompletedRowJson(rowJson);
        }

        public void SubmitAuthorityTransferSnapshot(string rowJson)
        {
            if (!IsSpawned || string.IsNullOrWhiteSpace(rowJson))
                return;

            if (IsServer)
            {
                AuthorityTransferRecorder.Instance?.ApplyCompletedRowJson(rowJson);
                return;
            }

            SubmitAuthorityTransferSnapshotServerRpc(rowJson);
        }

        private void TryBindEvents()
        {
            if (eventsBound)
                return;

            sessionOrchestrator ??= SessionOrchestratorNetcode.Instance;
            if (sessionOrchestrator == null)
                sessionOrchestrator = FindFirstObjectByType<SessionOrchestratorNetcode>();

            if (sessionOrchestrator == null)
                return;

            sessionOrchestrator.OnPlayerListUpdated += HandlePlayerListUpdated;
            eventsBound = true;
        }

        private void UnbindEvents()
        {
            if (!eventsBound || sessionOrchestrator == null)
                return;

            sessionOrchestrator.OnPlayerListUpdated -= HandlePlayerListUpdated;
            eventsBound = false;
        }

        private void HandlePlayerListUpdated(List<ulong> players)
        {
            if (!IsServer)
                return;

            List<string> activeUserIds = players != null
                ? players.Select(ConvertClientToUserId).ToList()
                : new List<string>();

            if (queueRuntimeActiveNetworked.Value && sharedLogic != null && sharedLogic.IsInitialized)
            {
                sharedLogic.SyncUsers(
                    CurrentInstrumentId,
                    activeUserIds,
                    removedHolderId => sharedLogic.ReleaseAccess(removedHolderId, CurrentInstrumentId));
            }

            BroadcastState("Netcode player list synced.");
        }

        private void TryInitializeQueueRuntime()
        {
            if (!IsServer)
                return;

            if (sharedRegistry == null)
                sharedRegistry = SharedObjectSyncRegistry.ResolveOrCreate();

            if (sharedRegistry == null)
            {
                AddLocalStatus("Cannot initialize queue runtime: missing shared registry.");
                return;
            }

            SharedSyncObjectDefinition instrumentDefinition =
                sharedRegistry.GetPreferredQueueControlledObject(OrchestrationBackendType.UnityNetcode);

            if (instrumentDefinition == null)
            {
                AddLocalStatus("No shared instrument with queue control found.");
                return;
            }

            string instrumentId = instrumentDefinition.ObjectId;
            currentInstrumentIdNetworked.Value = new FixedString64Bytes(instrumentId);
            sessionRunIdNetworked.Value = new FixedString64Bytes(SessionRunIdentity.NewId());

            IEnumerable<string> userIds = GetActiveUserIds();
            sharedLogic.Initialize(instrumentId, userIds);

            queueRuntimeActiveNetworked.Value = true;
            hasInitializedForCurrentSession = true;
            timeoutTimer = 0f;

            TLog("QueueRuntimeInitialized", GetLocalUserId(), instrumentId, "Netcode queue runtime initialized on server.");
            BroadcastState("Queue runtime initialized.");
        }

        private void CheckTimeoutForCurrentInstrument()
        {
            if (!IsServer || sharedLogic == null || !sharedLogic.IsInitialized)
                return;

            string instrumentId = CurrentInstrumentId;
            if (string.IsNullOrWhiteSpace(instrumentId))
                return;

            QueueDecision decision = sharedLogic.CheckTimeout(instrumentId, TimeSpan.FromSeconds(idleTimeoutSeconds));
            if (decision != null)
                HandleDecisionAuthoritatively("TimeoutCheck()", decision);
        }

        private void BroadcastState(string reason)
        {
            if (!IsServer)
                return;

            bool logicReady = sharedLogic != null && sharedLogic.IsInitialized;
            if (logicReady)
            {
                string instrumentId = CurrentInstrumentId;
                currentHolderUserIdNetworked.Value = new FixedString64Bytes(sharedLogic.GetCurrentHolder(instrumentId));
                currentQueueJoinedNetworked.Value = CreateSafeFixedString512(
                    sharedLogic.BuildJoinedQueue(instrumentId),
                    "queue",
                    keepMostRecentEntries: false);
                replicatedHistoryJoinedNetworked.Value = CreateSafeFixedString512(
                    sharedLogic.BuildJoinedHistory(),
                    "history",
                    keepMostRecentEntries: true);
            }

            connectedPlayersJoinedNetworked.Value = CreateSafeFixedString512(
                string.Join(JoinedEntrySeparator, GetActiveUserIds()),
                "connected players",
                keepMostRecentEntries: false);

            AddLocalStatus($"State updated: {reason}");

            if (logicReady)
                PublishSharedUserCardStats();

            PullSharedUserCardStats();
            OnRuntimeStateUpdated?.Invoke();
        }

        private void PublishSharedUserCardStats()
        {
            if (!IsServer)
                return;

            List<string> activeUserIds = GetActiveUserIds().Distinct().ToList();
            SessionLifecycleTracker.Instance?.EnsureClientsObserved(activeUserIds);

            UserCardStatsSnapshot snapshot = Thesis.Orchestration.Telemetry.SharedUserCardStats.BuildSnapshot(
                activeUserIds,
                userId => OrchestrationPlayerNameRegistry.ResolveDisplayName(userId, userId),
                userId => userId == ConvertClientToUserId(Unity.Netcode.NetworkManager.ServerClientId),
                CurrentHolderUserId,
                CurrentQueueSnapshot,
                UsageTimingTracker.Instance);

            string encoded = UserCardStatsSnapshot.Encode(snapshot);
            if (encoded.Length > MaxSharedUserCardStatsLength)
            {
                Log($"Shared user-card stats payload exceeded 4096 characters and was not published. Length={encoded.Length}");
                return;
            }

            sharedUserCardStatsJsonNetworked.Value = new FixedString4096Bytes(encoded);
            cachedSharedUserCardStats = snapshot;
        }

        private void PullSharedUserCardStats()
        {
            cachedSharedUserCardStats = UserCardStatsSnapshot.Decode(sharedUserCardStatsJsonNetworked.Value.ToString());
        }

        private void RefreshRuntimeStateIfNeeded()
        {
            string signature = BuildReplicatedStateSignature();
            string sharedStatsSignature = sharedUserCardStatsJsonNetworked.Value.ToString();

            if (signature == lastRenderedSignature && sharedStatsSignature == lastSharedUserCardStatsSignature)
                return;

            lastRenderedSignature = signature;
            lastSharedUserCardStatsSignature = sharedStatsSignature;
            PullSharedUserCardStats();
            OnRuntimeStateUpdated?.Invoke();
        }

        private void SubmitLocalLifecycleSnapshotIfNeeded()
        {
            if (!IsSpawned || SessionLifecycleTracker.Instance == null)
                return;

            string localUserId = GetLocalUserId();
            if (string.IsNullOrWhiteSpace(localUserId) || localUserId == "UnknownUser")
                return;

            SessionLifecycleTracker.Instance.MergePlaceholderRowsInto(localUserId);
            string rowJson = SessionLifecycleTracker.Instance.BuildBestRowJson(localUserId);
            if (string.IsNullOrWhiteSpace(rowJson) ||
                string.Equals(lastSubmittedLifecycleRowJson, rowJson, StringComparison.Ordinal))
            {
                return;
            }

            lastSubmittedLifecycleRowJson = rowJson;

            if (IsServer)
            {
                SessionLifecycleTracker.Instance.ApplyRowJson(rowJson, localUserId);
                return;
            }

            SubmitLifecycleSnapshotServerRpc(rowJson);
        }

        private void ReconcileConnectedPlayersIfNeeded()
        {
            if (Time.unscaledTime < nextConnectedPlayersAuditTime)
                return;

            nextConnectedPlayersAuditTime =
                Time.unscaledTime + ConnectedPlayersRefreshIntervalSeconds;
            string authoritativeRoster = string.Join(JoinedEntrySeparator, GetActiveUserIds());
            if (!string.Equals(
                    connectedPlayersJoinedNetworked.Value.ToString(),
                    authoritativeRoster,
                    StringComparison.Ordinal))
            {
                BroadcastState("Connected-player roster reconciled.");
            }
        }

        private void RequestConnectedPlayersRefreshIfNeeded()
        {
            if (ConnectedPlayersSnapshot.Count >= 2 ||
                Time.unscaledTime < nextConnectedPlayersRequestTime)
            {
                return;
            }

            nextConnectedPlayersRequestTime =
                Time.unscaledTime + ConnectedPlayersRefreshIntervalSeconds;
            RequestConnectedPlayersRefreshServerRpc();
        }

        private string BuildReplicatedStateSignature()
        {
            return string.Join("|", new[]
            {
                sessionStartedNetworked.Value.ToString(),
                queueRuntimeActiveNetworked.Value.ToString(),
                currentInstrumentIdNetworked.Value.ToString(),
                currentHolderUserIdNetworked.Value.ToString(),
                currentQueueJoinedNetworked.Value.ToString(),
                replicatedHistoryJoinedNetworked.Value.ToString(),
                connectedPlayersJoinedNetworked.Value.ToString()
            });
        }

        private void HandleDecisionAuthoritatively(string source, QueueDecision decision)
        {
            string holderBefore = CurrentHolderUserId;
            string holderAfter = sharedLogic.GetCurrentHolder(CurrentInstrumentId);
            List<string> queueSnapshotAfter = SplitJoinedString(sharedLogic.BuildJoinedQueue(CurrentInstrumentId));

            sharedLogic.RecordDecisionAnalytics(
                source,
                decision,
                CurrentInstrumentId,
                holderBefore,
                holderAfter,
                queueSnapshotAfter);

            BroadcastState(source);
        }

        private void ForceReleaseCurrentHolderOnServer(string reason)
        {
            if (!IsServer || !queueRuntimeActiveNetworked.Value || sharedLogic == null || !sharedLogic.IsInitialized)
                return;

            string holderUserId = CurrentHolderUserId;
            if (string.IsNullOrWhiteSpace(holderUserId) || holderUserId == "None")
                return;

            QueueDecision decision = sharedLogic.ReleaseAccess(holderUserId, CurrentInstrumentId);
            HandleDecisionAuthoritatively($"ForceReleaseForFinalization({reason ?? "Finalization"})", decision);
        }

        private IEnumerable<string> GetActiveUserIds()
        {
            HashSet<string> userIds = new HashSet<string>();

            if (sessionOrchestrator != null && sessionOrchestrator.CurrentPlayers != null)
            {
                foreach (ulong clientId in sessionOrchestrator.CurrentPlayers)
                    userIds.Add(ConvertClientToUserId(clientId));
            }

            if (NetworkManager != null)
            {
                foreach (ulong clientId in NetworkManager.ConnectedClientsIds)
                    userIds.Add(ConvertClientToUserId(clientId));
            }

            return userIds.OrderBy(id => id);
        }

        private static List<string> SplitJoinedString(string joined)
        {
            if (string.IsNullOrWhiteSpace(joined))
                return new List<string>();

            return new List<string>(joined.Split(new[] { JoinedEntrySeparator }, StringSplitOptions.RemoveEmptyEntries));
        }

        private static string NormalizeHolderId(string holderUserId)
        {
            return string.IsNullOrWhiteSpace(holderUserId) ? "None" : holderUserId;
        }

        private static string ConvertClientToUserId(ulong clientId)
        {
            return $"Client_{clientId}";
        }

        private string GetLocalUserId()
        {
            if (sessionOrchestrator != null)
                return sessionOrchestrator.GetLocalUserId();

            if (NetworkManager != null)
                return ConvertClientToUserId(NetworkManager.LocalClientId);

            return "UnknownUser";
        }

        private void TLog(string eventType, string userId, string instrumentId, string details)
        {
            TelemetryLoggerCore.Instance?.LogEvent(eventType, userId, instrumentId, details);
        }

        private void AddLocalStatus(string message)
        {
            Log(message);
        }

        private FixedString512Bytes CreateSafeFixedString512(
            string value,
            string fieldName,
            bool keepMostRecentEntries)
        {
            value ??= string.Empty;

            if (value.Length <= MaxFixedString512SafeChars)
                return new FixedString512Bytes(value);

            string truncated = TruncateJoinedValueToFit(value, keepMostRecentEntries);
            Log($"{fieldName} payload exceeded FixedString512 budget and was truncated. OriginalLength={value.Length}, TruncatedLength={truncated.Length}");
            return new FixedString512Bytes(truncated);
        }

        private static string TruncateJoinedValueToFit(string value, bool keepMostRecentEntries)
        {
            if (string.IsNullOrEmpty(value))
                return string.Empty;

            string[] entries = value.Split(new[] { JoinedEntrySeparator }, StringSplitOptions.RemoveEmptyEntries);
            if (entries.Length == 0)
                return TrimStringToFit(value, keepMostRecentEntries);

            List<string> keptEntries = new List<string>();
            int start = keepMostRecentEntries ? entries.Length - 1 : 0;
            int end = keepMostRecentEntries ? -1 : entries.Length;
            int step = keepMostRecentEntries ? -1 : 1;

            for (int index = start; index != end; index += step)
            {
                string entry = entries[index];
                if (string.IsNullOrEmpty(entry))
                    continue;

                List<string> candidateEntries = new List<string>(keptEntries);
                if (keepMostRecentEntries)
                    candidateEntries.Insert(0, entry);
                else
                    candidateEntries.Add(entry);

                string candidate = string.Join(JoinedEntrySeparator, candidateEntries);
                if (candidate.Length > MaxFixedString512SafeChars)
                    break;

                keptEntries = candidateEntries;
            }

            if (keptEntries.Count == 0)
                return TrimStringToFit(value, keepMostRecentEntries);

            return string.Join(JoinedEntrySeparator, keptEntries);
        }

        private static string TrimStringToFit(string value, bool keepMostRecentCharacters)
        {
            if (string.IsNullOrEmpty(value) || value.Length <= MaxFixedString512SafeChars)
                return value ?? string.Empty;

            return keepMostRecentCharacters
                ? value.Substring(value.Length - MaxFixedString512SafeChars, MaxFixedString512SafeChars)
                : value.Substring(0, MaxFixedString512SafeChars);
        }

        private void Log(string message)
        {
            if (!verboseLogs)
                return;

            Debug.Log($"[SessionQueueRuntimeNetcode] {message}", this);
        }
    }
}
