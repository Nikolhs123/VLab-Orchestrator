using System;
using System.Collections.Generic;
using System.Linq;
using Thesis.Orchestration.Telemetry;
using UnityEngine;
using KcpTransport = kcp2k.KcpTransport;
using MirrorNetworkClient = global::Mirror.NetworkClient;
using MirrorNetworkConnectionToClient = global::Mirror.NetworkConnectionToClient;
using MirrorNetworkManager = global::Mirror.NetworkManager;
using MirrorNetworkMessage = global::Mirror.NetworkMessage;
using MirrorNetworkServer = global::Mirror.NetworkServer;

namespace Thesis.Orchestration.Backends.Mirror
{
    public struct MirrorPlayerListMessage : MirrorNetworkMessage
    {
        public string joinedPlayerIds;
    }

    public struct MirrorAssignedClientIdMessage : MirrorNetworkMessage
    {
        public int clientId;
    }

    public class SessionOrchestratorMirror : MonoBehaviour
    {
        public static SessionOrchestratorMirror Instance { get; private set; }

        [Header("Mirror Settings")]
        [SerializeField] private MirrorNetworkManager networkManager;
        [SerializeField] private KcpTransport kcpTransport;
        [SerializeField] private bool autoConnectOnStart = true;
        [SerializeField] private string connectAddress = "127.0.0.1";
        [SerializeField] private ushort basePort = 7777;
        [SerializeField] private ushort portRange = 500;
        [SerializeField] private int maxPlayersPerSession = 8;
        [SerializeField] private string defaultSessionName = "DefaultSession";
        [SerializeField] private SessionQueueRuntimeMirror queueRuntimeTemplate;
        [SerializeField] private bool spawnQueueRuntimeAutomatically = true;
        [SerializeField] private bool verboseLogs = true;

        private readonly List<int> cachedPlayers = new();
        private readonly List<string> cachedSessions = new();
        private readonly HashSet<int> observerSynchronizedConnectionIds = new();
        private const string LocalLifecyclePlaceholderId = "Mirror_LocalClient";

        private bool transportPrepared;
        private bool localSessionStarted;
        private bool hasAutoConnectAttempted;
        private bool intentionalShutdownInProgress;
        private bool clientHandlersRegistered;
        private bool queueSpawnHandlerRegistered;
        private bool lastKnownClientConnected;
        private string currentSessionName = "None";
        private SessionQueueRuntimeMirror spawnedQueueRuntime;
        private int localAssignedClientId = UnassignedClientId;

        private const int UnassignedClientId = int.MinValue;

        public MirrorNetworkManager NetworkManager => networkManager;
        public bool IsConnectedToMasterServer => transportPrepared || (networkManager != null && networkManager.isNetworkActive);
        public bool IsInRoom => MirrorNetworkClient.isConnected;
        public bool IsMasterClient => MirrorNetworkServer.active && MirrorNetworkClient.isConnected;

        public bool SessionStarted
        {
            get
            {
                SessionQueueRuntimeMirror runtime = SessionQueueRuntimeMirror.Instance;
                if (runtime != null && runtime.IsSpawned)
                    return runtime.SessionStarted;

                return localSessionStarted;
            }
        }

        public string CurrentRoomName => currentSessionName;
        public IReadOnlyList<int> CurrentPlayers => cachedPlayers;
        public IReadOnlyList<string> AvailableSessions => cachedSessions;

        public event Action OnConnectedToServerEvent;
        public event Action<string> OnConnectionFailed;
        public event Action<string> OnRoomCreated;
        public event Action<string> OnRoomJoined;
        public event Action<List<int>> OnPlayerListUpdated;
        public event Action<bool> OnSessionStateChanged;
        public event Action<string> OnStatusMessage;
        public event Action<List<string>> OnSessionBrowserUpdated;

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }

            Instance = this;
            ResolveReferences();
            RegisterClientHandlersIfNeeded();
        }

        private void OnEnable()
        {
            ResolveReferences();
            RegisterClientHandlersIfNeeded();
        }

        private void Start()
        {
            if (!autoConnectOnStart || hasAutoConnectAttempted)
                return;

            hasAutoConnectAttempted = true;
            ConnectToServer();
        }

        private void Update()
        {
            ResolveReferences();
            RegisterClientHandlersIfNeeded();
            TryMergeLifecycleLocalPlaceholder();
            RegisterQueueRuntimePrefabIfNeeded();
            NetworkAvatarSpawnerMirror.Instance?.RegisterSharedAvatarPrefabIfNeeded();
            EnsureClientReadyIfNeeded();

            RefreshClientConnectionState();
            RefreshServerPlayerCacheIfNeeded();
            EnsureReadyConnectionsHaveObservers();

            bool started = SessionStarted;
            if (started == localSessionStarted)
                return;

            localSessionStarted = started;
            OnSessionStateChanged?.Invoke(localSessionStarted);
        }

        private void OnDisable()
        {
            UnregisterClientHandlers();
        }

        private void OnDestroy()
        {
            if (MirrorNetworkServer.active)
            {
                SessionQueueRuntimeMirror.Instance?.TryForceReleaseCurrentHolderForFinalization("Runtime cleanup");
                ConfiguredSharedObjectSpawnerMirror.Instance?.DespawnServerObjects();
            }

            ConfiguredSharedObjectSpawnerMirror.Instance?.ResetForNextSession();
            UnregisterClientHandlers();

            if (Instance == this)
                Instance = null;
        }

        public void ConnectToServer()
        {
            if (!ResolveReferences())
                return;

            SessionLifecycleTracker.Instance?.MarkConnectStarted(GetLifecycleClientId());

            transportPrepared = true;
            cachedSessions.Clear();
            networkManager.networkAddress = connectAddress;
            networkManager.maxConnections = maxPlayersPerSession;
            networkManager.autoCreatePlayer = false;
            networkManager.playerPrefab = null;

            ConfiguredSharedObjectSpawnerMirror.Instance?.RegisterSpawnHandlersIfNeeded();
            NetworkAvatarSpawnerMirror.Instance?.RegisterSharedAvatarPrefabIfNeeded();
            RegisterQueueRuntimePrefabIfNeeded();

            RaiseStatus("Mirror transport prepared. Direct-connect mode ready. Enter a room name manually to create or join.");
            OnConnectedToServerEvent?.Invoke();
            OnSessionBrowserUpdated?.Invoke(new List<string>(cachedSessions));
            SessionLifecycleTracker.Instance?.MarkConnectSucceeded(GetLifecycleClientId());
            TLog("ConnectRequested", GetLocalUserId(), "None", "Prepared Mirror direct-connect transport.");
        }

        public void CreateRoom(string roomName)
        {
            if (!ResolveReferences())
                return;

            string sessionName = string.IsNullOrWhiteSpace(roomName) ? defaultSessionName : roomName.Trim();
            if (string.IsNullOrWhiteSpace(sessionName))
            {
                RaiseFailure("Cannot create Mirror session: empty room name.");
                return;
            }

            if (networkManager.isNetworkActive)
            {
                RaiseFailure("Cannot create Mirror session: already running a client or host.");
                return;
            }

            ushort port = SessionNameToPort(sessionName);
            PrepareManagerForSession(port);

            SessionLifecycleTracker.Instance?.MarkCreateStarted(GetLifecycleClientId());

            networkManager.StartHost();
            if (!networkManager.isNetworkActive)
            {
                RaiseFailure($"Failed to create Mirror session '{sessionName}' on port {port}.");
                return;
            }

            transportPrepared = true;
            currentSessionName = sessionName;
            localAssignedClientId = global::Mirror.NetworkConnection.LocalConnectionId;
            cachedSessions.Clear();
            RefreshServerPlayerCacheIfNeeded(forceBroadcast: true);
            ConfiguredSharedObjectSpawnerMirror.Instance?.EnsureServerObjectsSpawned();
            EnsureQueueRuntimeSpawned();
            TryMergeLifecycleLocalPlaceholder();

            RaiseStatus($"Created Mirror session '{sessionName}' on port {port}.");
            OnRoomCreated?.Invoke(sessionName);
            OnSessionBrowserUpdated?.Invoke(new List<string>(cachedSessions));
            SessionLifecycleTracker.Instance?.MarkCreateSucceeded(GetLifecycleClientId());
            TLog("CreateRoomRequested", GetLocalUserId(), sessionName, $"Created direct-connect Mirror session on port {port}.");
        }

        public void JoinRoom(string roomName)
        {
            if (!ResolveReferences())
                return;

            if (string.IsNullOrWhiteSpace(roomName))
            {
                RaiseFailure("Cannot join Mirror session: empty room name.");
                return;
            }

            if (networkManager.isNetworkActive)
            {
                RaiseFailure("Cannot join Mirror session: already running a client or host.");
                return;
            }

            string sessionName = roomName.Trim();
            ushort port = SessionNameToPort(sessionName);
            PrepareManagerForSession(port);

            SessionLifecycleTracker.Instance?.MarkJoinStarted(GetLifecycleClientId());

            networkManager.StartClient();
            if (!networkManager.isNetworkActive)
            {
                RaiseFailure($"Failed to join Mirror session '{sessionName}' at {connectAddress}:{port}.");
                return;
            }

            transportPrepared = true;
            currentSessionName = sessionName;
            localAssignedClientId = UnassignedClientId;
            cachedSessions.Clear();
            RaiseStatus($"Joining Mirror session '{sessionName}' at {connectAddress}:{port}...");
            OnSessionBrowserUpdated?.Invoke(new List<string>(cachedSessions));
            TLog("JoinRoomRequested", GetLocalUserId(), sessionName, $"Joining Mirror direct-connect session at {connectAddress}:{port}.");
        }

        public void LeaveRoom()
        {
            if (!ResolveReferences())
                return;

            if (!networkManager.isNetworkActive)
            {
                RaiseFailure("Cannot leave Mirror session: no active client or host.");
                return;
            }

            if (MirrorNetworkServer.active)
                SessionQueueRuntimeMirror.Instance?.TryForceReleaseCurrentHolderForFinalization("Session leave");
            TLog("LeaveRoomRequested", GetLocalUserId(), CurrentRoomName, "Requested to leave Mirror session.");
            intentionalShutdownInProgress = true;

            ConfiguredSharedObjectSpawnerMirror.Instance?.DespawnServerObjects();

            if (MirrorNetworkServer.active && spawnedQueueRuntime != null && spawnedQueueRuntime.IsSpawned)
            {
                MirrorNetworkServer.Destroy(spawnedQueueRuntime.gameObject);
                spawnedQueueRuntime = null;
            }

            if (MirrorNetworkServer.active && MirrorNetworkClient.isConnected)
                networkManager.StopHost();
            else if (MirrorNetworkClient.active)
                networkManager.StopClient();
            else if (MirrorNetworkServer.active)
                networkManager.StopServer();

            ResetLocalState(notifyObservers: true);
            ConfiguredSharedObjectSpawnerMirror.Instance?.ResetForNextSession();
            RaiseStatus("Left Mirror session.");

            intentionalShutdownInProgress = false;
        }

        public void BeginSession()
        {
            EnsureQueueRuntimeSpawned();

            SessionQueueRuntimeMirror runtime = SessionQueueRuntimeMirror.Instance;
            SessionLifecycleTracker.Instance?.MarkSessionStartPressed(GetLifecycleClientId());

            if (runtime != null && runtime.IsSpawned)
            {
                runtime.RequestBeginSessionForLocalPlayer();
                RaiseStatus("Requested session start.");
                TryMergeLifecycleLocalPlaceholder();
                SessionLifecycleTracker.Instance?.MarkSessionStartVisible(GetLifecycleClientId());
                NetworkAvatarSpawnerMirror.Instance?.EnsureSpawnForConnectedClients();
                return;
            }

            localSessionStarted = true;
            RaiseStatus("Session started locally (runtime not found).");
            OnSessionStateChanged?.Invoke(localSessionStarted);
            NetworkAvatarSpawnerMirror.Instance?.EnsureSpawnForConnectedClients();
        }

        public bool SessionExists(string roomName)
        {
            return false;
        }

        public void ConfigureRuntime(MirrorNetworkManager manager, KcpTransport transport, SessionQueueRuntimeMirror runtimeTemplate)
        {
            networkManager = manager;
            kcpTransport = transport;
            queueRuntimeTemplate = runtimeTemplate;
            ResolveReferences();
            RegisterClientHandlersIfNeeded();
        }

        public void RegisterQueueRuntimePrefabIfNeeded()
        {
            if (!ResolveReferences())
                return;

            RegisterClientHandlersIfNeeded();

            if (queueSpawnHandlerRegistered)
                return;

            MirrorNetworkClient.RegisterSpawnHandler(
                SessionQueueRuntimeMirror.SpawnAssetId,
                (position, assetId) => SpawnQueueRuntimeFromTemplate(position),
                UnspawnQueueRuntimeInstance);

            queueSpawnHandlerRegistered = true;
        }

        public string ConvertClientToUserId(int clientId)
        {
            return $"Client_{clientId}";
        }

        public int GetMasterClientId()
        {
            if (cachedPlayers.Count > 0)
                return cachedPlayers[0];

            if (TryGetLocalClientId(out int localClientId))
                return localClientId;

            return 0;
        }

        public string GetMasterUserId()
        {
            return ConvertClientToUserId(GetMasterClientId());
        }

        public string GetLocalUserId()
        {
            if (TryGetLocalClientId(out int localClientId))
                return ConvertClientToUserId(localClientId);

            return "UnknownUser";
        }

        private string GetLifecycleClientId()
        {
            string userId = GetLocalUserId();
            return string.IsNullOrWhiteSpace(userId) || userId == "UnknownUser"
                ? LocalLifecyclePlaceholderId
                : userId;
        }

        private void TryMergeLifecycleLocalPlaceholder()
        {
            string localUserId = GetLocalUserId();
            if (string.IsNullOrWhiteSpace(localUserId) || localUserId == "UnknownUser")
                return;

            SessionLifecycleTracker.Instance?.MergeClientRow(LocalLifecyclePlaceholderId, localUserId);
            SessionLifecycleTracker.Instance?.MergePlaceholderRowsInto(localUserId);
            SessionLifecycleTracker.Instance?.EnsureClientObserved(localUserId);
        }

        public bool TryGetLocalClientId(out int clientId)
        {
            if (MirrorNetworkClient.connection is global::Mirror.LocalConnectionToServer)
            {
                clientId = global::Mirror.NetworkConnection.LocalConnectionId;
                return true;
            }

            if (localAssignedClientId != UnassignedClientId)
            {
                clientId = localAssignedClientId;
                return true;
            }

            clientId = default;
            return false;
        }

        private bool ResolveReferences()
        {
            if (networkManager == null)
                networkManager = FindFirstObjectByType<MirrorNetworkManager>();

            if (networkManager == null)
            {
                RaiseFailure("SessionOrchestratorMirror is missing Mirror NetworkManager.");
                return false;
            }

            if (kcpTransport == null)
                kcpTransport = networkManager.GetComponent<KcpTransport>();

            if (kcpTransport == null)
                kcpTransport = FindFirstObjectByType<KcpTransport>();

            if (kcpTransport == null)
            {
                RaiseFailure("SessionOrchestratorMirror is missing KcpTransport.");
                return false;
            }

            networkManager.transport = kcpTransport;
            ResolveQueueRuntimeTemplate();
            return true;
        }

        private void PrepareManagerForSession(ushort port)
        {
            networkManager.transport = kcpTransport;
            networkManager.networkAddress = connectAddress;
            networkManager.maxConnections = maxPlayersPerSession;
            networkManager.autoCreatePlayer = false;
            networkManager.playerPrefab = null;
            kcpTransport.Port = port;

            ConfiguredSharedObjectSpawnerMirror.Instance?.RegisterSpawnHandlersIfNeeded();
            NetworkAvatarSpawnerMirror.Instance?.RegisterSharedAvatarPrefabIfNeeded();
            RegisterQueueRuntimePrefabIfNeeded();
        }

        private void RegisterClientHandlersIfNeeded()
        {
            if (clientHandlersRegistered)
                return;

            MirrorNetworkClient.RegisterHandler<MirrorPlayerListMessage>(HandlePlayerListMessage, false);
            MirrorNetworkClient.RegisterHandler<MirrorAssignedClientIdMessage>(HandleAssignedClientIdMessage, false);
            clientHandlersRegistered = true;
        }

        private void UnregisterClientHandlers()
        {
            if (!clientHandlersRegistered)
                return;

            MirrorNetworkClient.UnregisterHandler<MirrorPlayerListMessage>();
            MirrorNetworkClient.UnregisterHandler<MirrorAssignedClientIdMessage>();
            clientHandlersRegistered = false;

            if (queueSpawnHandlerRegistered)
            {
                MirrorNetworkClient.UnregisterSpawnHandler(SessionQueueRuntimeMirror.SpawnAssetId);
                queueSpawnHandlerRegistered = false;
            }
        }

        private GameObject SpawnQueueRuntimeFromTemplate(Vector3 position)
        {
            SessionQueueRuntimeMirror template = ResolveQueueRuntimeTemplate();
            if (template == null)
                return null;

            GameObject runtimeObject = Instantiate(template.gameObject, position, Quaternion.identity);
            runtimeObject.name = "MirrorQueueRuntime";
            runtimeObject.SetActive(true);
            return runtimeObject;
        }

        private void UnspawnQueueRuntimeInstance(GameObject spawned)
        {
            if (spawned == null)
                return;

            Destroy(spawned);
        }

        private void HandlePlayerListMessage(MirrorPlayerListMessage message)
        {
            cachedPlayers.Clear();

            if (!string.IsNullOrWhiteSpace(message.joinedPlayerIds))
            {
                foreach (string part in message.joinedPlayerIds.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries))
                {
                    if (int.TryParse(part, out int parsed))
                        cachedPlayers.Add(parsed);
                }
            }

            cachedPlayers.Sort();
            OnPlayerListUpdated?.Invoke(new List<int>(cachedPlayers));
        }

        private void HandleAssignedClientIdMessage(MirrorAssignedClientIdMessage message)
        {
            localAssignedClientId = message.clientId;
            TryMergeLifecycleLocalPlaceholder();

            if (cachedPlayers.Contains(message.clientId))
                return;

            cachedPlayers.Add(message.clientId);
            cachedPlayers.Sort();
            OnPlayerListUpdated?.Invoke(new List<int>(cachedPlayers));
        }

        private void RefreshClientConnectionState()
        {
            bool connected = MirrorNetworkClient.isConnected;
            if (connected == lastKnownClientConnected)
                return;

            lastKnownClientConnected = connected;

            if (connected)
            {
                transportPrepared = true;
                EnsureClientReadyIfNeeded();

                if (!MirrorNetworkServer.active)
                {
                    TryMergeLifecycleLocalPlaceholder();
                    RaiseStatus($"Joined Mirror session '{currentSessionName}'.");
                    OnRoomJoined?.Invoke(currentSessionName);
                    SessionLifecycleTracker.Instance?.MarkJoinSucceeded(GetLifecycleClientId());
                }

                if (cachedPlayers.Count == 0 && TryGetLocalClientId(out int localClientId))
                {
                    cachedPlayers.Add(localClientId);
                    OnPlayerListUpdated?.Invoke(new List<int>(cachedPlayers));
                }

                return;
            }

            if (intentionalShutdownInProgress)
                return;

            string message = $"Disconnected from Mirror session '{currentSessionName}'.";
            ResetLocalState(notifyObservers: true);
            RaiseStatus(message);
            OnConnectionFailed?.Invoke(message);
        }

        private void EnsureClientReadyIfNeeded()
        {
            if (!MirrorNetworkClient.isConnected || MirrorNetworkClient.ready || MirrorNetworkClient.connection == null)
                return;

            if (!MirrorNetworkClient.connection.isAuthenticated)
                return;

            MirrorNetworkClient.Ready();
        }

        private void RefreshServerPlayerCacheIfNeeded(bool forceBroadcast = false)
        {
            if (!MirrorNetworkServer.active)
                return;

            List<int> authoritativePlayers = new List<int>();

            foreach (KeyValuePair<int, MirrorNetworkConnectionToClient> pair in MirrorNetworkServer.connections)
            {
                if (pair.Value == null)
                    continue;

                authoritativePlayers.Add(pair.Key);
            }

            authoritativePlayers.Sort();

            bool changed = forceBroadcast ||
                           authoritativePlayers.Count != cachedPlayers.Count ||
                           !authoritativePlayers.SequenceEqual(cachedPlayers);

            if (!changed)
                return;

            cachedPlayers.Clear();
            cachedPlayers.AddRange(authoritativePlayers);
            OnPlayerListUpdated?.Invoke(new List<int>(cachedPlayers));
            BroadcastPlayerListToClients();
        }

        private void BroadcastPlayerListToClients()
        {
            if (!MirrorNetworkServer.active)
                return;

            string joined = string.Join(",", cachedPlayers);
            MirrorNetworkServer.SendToAll(new MirrorPlayerListMessage
            {
                joinedPlayerIds = joined
            }, sendToReadyOnly: false);

            foreach (KeyValuePair<int, MirrorNetworkConnectionToClient> pair in MirrorNetworkServer.connections)
            {
                if (pair.Value == null)
                    continue;

                pair.Value.Send(new MirrorAssignedClientIdMessage
                {
                    clientId = pair.Key
                });
            }
        }

        private void EnsureReadyConnectionsHaveObservers()
        {
            if (!MirrorNetworkServer.active)
            {
                observerSynchronizedConnectionIds.Clear();
                return;
            }

            List<int> staleConnectionIds = null;
            foreach (int connectionId in observerSynchronizedConnectionIds)
            {
                if (MirrorNetworkServer.connections.TryGetValue(connectionId, out MirrorNetworkConnectionToClient existingConnection) &&
                    existingConnection != null &&
                    existingConnection.isReady)
                {
                    continue;
                }

                staleConnectionIds ??= new List<int>();
                staleConnectionIds.Add(connectionId);
            }

            if (staleConnectionIds != null)
            {
                foreach (int staleConnectionId in staleConnectionIds)
                    observerSynchronizedConnectionIds.Remove(staleConnectionId);
            }

            foreach (KeyValuePair<int, MirrorNetworkConnectionToClient> pair in MirrorNetworkServer.connections)
            {
                MirrorNetworkConnectionToClient connection = pair.Value;
                if (connection == null || !connection.isReady || observerSynchronizedConnectionIds.Contains(pair.Key))
                    continue;

                if (connection.identity != null)
                {
                    observerSynchronizedConnectionIds.Add(pair.Key);
                    continue;
                }

                if (!TrySpawnObserversForReadyConnection(connection))
                    continue;

                observerSynchronizedConnectionIds.Add(pair.Key);
            }
        }

        private bool TrySpawnObserversForReadyConnection(MirrorNetworkConnectionToClient connection)
        {
            try
            {
                System.Reflection.MethodInfo spawnObserversMethod = typeof(global::Mirror.NetworkServer).GetMethod(
                    "SpawnObserversForConnection",
                    System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic);

                if (spawnObserversMethod == null)
                {
                    RaiseFailure("Mirror observer bootstrap method could not be resolved.");
                    return false;
                }

                spawnObserversMethod.Invoke(null, new object[] { connection });
                Log($"Initialized observers for ready Mirror connection {connection.connectionId} without a player object.");
                return true;
            }
            catch (System.Exception ex)
            {
                RaiseFailure($"Failed to initialize observers for ready Mirror connection {connection.connectionId}: {ex.Message}");
                return false;
            }
        }

        private SessionQueueRuntimeMirror ResolveQueueRuntimeTemplate()
        {
            if (queueRuntimeTemplate != null)
                return queueRuntimeTemplate;

            Transform searchRoot = networkManager != null ? networkManager.transform.root : transform.root;
            SessionQueueRuntimeMirror[] runtimes = searchRoot.GetComponentsInChildren<SessionQueueRuntimeMirror>(true);
            foreach (SessionQueueRuntimeMirror runtime in runtimes)
            {
                if (runtime == null)
                    continue;

                if (runtime.gameObject.activeInHierarchy)
                    continue;

                queueRuntimeTemplate = runtime;
                break;
            }

            return queueRuntimeTemplate;
        }

        private void EnsureQueueRuntimeSpawned()
        {
            if (!spawnQueueRuntimeAutomatically || !ResolveReferences() || !MirrorNetworkServer.active)
                return;

            if (spawnedQueueRuntime != null && spawnedQueueRuntime.IsSpawned)
                return;

            SessionQueueRuntimeMirror template = ResolveQueueRuntimeTemplate();
            if (template == null)
            {
                Log("Queue runtime spawn skipped because no template is configured.");
                return;
            }

            GameObject runtimeObject = Instantiate(template.gameObject);
            runtimeObject.name = "MirrorQueueRuntime";
            runtimeObject.SetActive(true);

            if (runtimeObject.GetComponent<global::Mirror.NetworkIdentity>() == null)
            {
                Destroy(runtimeObject);
                Log("Queue runtime spawn skipped because the instantiated template is missing NetworkIdentity.");
                return;
            }

            MirrorNetworkServer.Spawn(runtimeObject, SessionQueueRuntimeMirror.SpawnAssetId);
            spawnedQueueRuntime = runtimeObject.GetComponent<SessionQueueRuntimeMirror>();
            Log("Spawned Mirror queue runtime.");
        }

        private ushort SessionNameToPort(string roomName)
        {
            string normalized = string.IsNullOrWhiteSpace(roomName)
                ? defaultSessionName
                : roomName.Trim().ToLowerInvariant();

            int offset = Mathf.Abs(normalized.GetHashCode() % portRange);
            return (ushort)(basePort + offset);
        }

        private void ResetLocalState(bool notifyObservers)
        {
            transportPrepared = false;
            localSessionStarted = false;
            currentSessionName = "None";
            spawnedQueueRuntime = null;
            cachedPlayers.Clear();
            cachedSessions.Clear();
            lastKnownClientConnected = false;
            localAssignedClientId = UnassignedClientId;
            observerSynchronizedConnectionIds.Clear();

            if (!notifyObservers)
                return;

            OnPlayerListUpdated?.Invoke(new List<int>(cachedPlayers));
            OnSessionBrowserUpdated?.Invoke(new List<string>(cachedSessions));
            OnSessionStateChanged?.Invoke(false);
        }

        private void RaiseStatus(string message)
        {
            Log(message);
            OnStatusMessage?.Invoke(message);
        }

        private void RaiseFailure(string message)
        {
            Log(message);
            OnStatusMessage?.Invoke(message);
            OnConnectionFailed?.Invoke(message);
        }

        private void Log(string message)
        {
            if (!verboseLogs)
                return;

            Debug.Log($"[SessionOrchestratorMirror] {message}", this);
        }

        private void TLog(string eventType, string userId, string instrumentId, string details)
        {
            TelemetryLoggerCore.Instance?.LogEvent(eventType, userId, instrumentId, details);
        }
    }
}
