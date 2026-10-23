using System;
using System.Collections.Generic;
using System.Linq;
using FishNet.Connection;
using FishNet.Managing;
using FishNet.Object;
using FishNet.Transporting;
using FishNet.Transporting.Tugboat;
using Thesis.Orchestration.Telemetry;
using UnityEngine;

namespace Thesis.Orchestration.Backends.FishNet
{
    public class SessionOrchestratorFishNet : MonoBehaviour
    {
        public static SessionOrchestratorFishNet Instance { get; private set; }

        private bool playerCacheRefreshPending;

        [Header("FishNet Settings")]
        [SerializeField] private NetworkManager networkManager;
        [SerializeField] private Tugboat tugboat;
        [SerializeField] private bool autoConnectOnStart = true;
        [SerializeField] private string connectAddress = "127.0.0.1";
        [SerializeField] private string serverListenAddress = "0.0.0.0";
        [SerializeField] private ushort basePort = 7777;
        [SerializeField] private ushort portRange = 500;
        [SerializeField] private int maxPlayersPerSession = 8;
        [SerializeField] private string defaultSessionName = "DefaultSession";
        [SerializeField] private SessionQueueRuntimeFishNet queueRuntimeTemplate;
        [SerializeField] private bool spawnQueueRuntimeAutomatically = true;
        [SerializeField] private bool verboseLogs = true;

        private readonly List<int> cachedPlayers = new();
        private readonly List<string> cachedSessions = new();
        private const string LocalLifecyclePlaceholderId = "FishNet_LocalClient";

        private bool callbacksBound;
        private bool transportPrepared;
        private bool localSessionStarted;
        private bool hasAutoConnectAttempted;
        private bool intentionalShutdownInProgress;
        private bool initializationFailureReported;
        private string currentSessionName = "None";
        private SessionQueueRuntimeFishNet spawnedQueueRuntime;

        public NetworkManager NetworkManager => networkManager;
        private bool IsNetworkManagerInitialized =>
            networkManager != null &&
            networkManager.Initialized &&
            networkManager.ClientManager != null &&
            networkManager.ServerManager != null;

        public bool IsConnectedToMasterServer => transportPrepared ||
                                                 (IsNetworkManagerInitialized &&
                                                  (networkManager.IsClientStarted || networkManager.IsServerStarted));
        public bool IsInRoom => IsNetworkManagerInitialized && networkManager.IsClientStarted;
        public bool IsMasterClient => IsNetworkManagerInitialized && networkManager.IsServerStarted && networkManager.IsClientStarted;

        public bool SessionStarted
        {
            get
            {
                SessionQueueRuntimeFishNet runtime = SessionQueueRuntimeFishNet.Instance;
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
        }

        private void OnEnable()
        {
            ResolveReferences();
            TryBindCallbacks();
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
            TryBindCallbacks();
            if (playerCacheRefreshPending)
            {
                playerCacheRefreshPending = false;
                RefreshPlayerCache();
            }
            TryMergeLifecycleLocalPlaceholder();
            RegisterQueueRuntimePrefabIfNeeded();
            ReactivateClientSpawnedObjectsDeactivatedAtStart();

            bool started = SessionStarted;
            if (started == localSessionStarted)
                return;

            localSessionStarted = started;
            OnSessionStateChanged?.Invoke(localSessionStarted);
        }

        private void OnDisable()
        {
            UnbindCallbacks();
        }

        private void OnDestroy()
        {
            if (IsNetworkManagerInitialized && networkManager.IsServerStarted)
                SessionQueueRuntimeFishNet.Instance?.TryForceReleaseCurrentHolderForFinalization("Runtime cleanup");
            UnbindCallbacks();

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
            ConfiguredSharedObjectSpawnerFishNet.Instance?.RegisterConfiguredPrefabsIfNeeded();
            NetworkAvatarSpawnerFishNet.Instance?.RegisterSharedAvatarPrefabIfNeeded();
            RegisterQueueRuntimePrefabIfNeeded();

            RaiseStatus("FishNet transport prepared. Direct-connect mode ready. Enter a room name manually to create or join.");
            OnConnectedToServerEvent?.Invoke();
            OnSessionBrowserUpdated?.Invoke(new List<string>(cachedSessions));
            SessionLifecycleTracker.Instance?.MarkConnectSucceeded(GetLifecycleClientId());
            TLog("ConnectRequested", GetLocalUserId(), "None", "Prepared FishNet direct-connect transport.");
        }

        public void CreateRoom(string roomName)
        {
            if (!ResolveReferences())
                return;

            string sessionName = string.IsNullOrWhiteSpace(roomName) ? defaultSessionName : roomName.Trim();
            if (string.IsNullOrWhiteSpace(sessionName))
            {
                RaiseFailure("Cannot create FishNet session: empty room name.");
                return;
            }

            if (networkManager.IsServerStarted || networkManager.IsClientStarted)
            {
                RaiseFailure("Cannot create FishNet session: already running a client or host.");
                return;
            }

            ushort port = SessionNameToPort(sessionName);
            tugboat.SetServerBindAddress(serverListenAddress, IPAddressType.IPv4);
            tugboat.SetPort(port);
            tugboat.SetMaximumClients(maxPlayersPerSession);
            tugboat.SetClientAddress(connectAddress);
            ConfiguredSharedObjectSpawnerFishNet.Instance?.RegisterConfiguredPrefabsIfNeeded();
            NetworkAvatarSpawnerFishNet.Instance?.RegisterSharedAvatarPrefabIfNeeded();
            RegisterQueueRuntimePrefabIfNeeded();

            SessionLifecycleTracker.Instance?.MarkCreateStarted(GetLifecycleClientId());

            bool serverStarted = networkManager.ServerManager.StartConnection(port);
            bool clientStarted = serverStarted && networkManager.ClientManager.StartConnection(connectAddress, port);
            if (!serverStarted || !clientStarted)
            {
                if (networkManager.IsClientStarted)
                    networkManager.ClientManager.StopConnection();

                if (networkManager.IsServerStarted)
                    networkManager.ServerManager.StopConnection(false);

                RaiseFailure($"Failed to create FishNet session '{sessionName}' on port {port}.");
                return;
            }

            transportPrepared = true;
            currentSessionName = sessionName;
            cachedSessions.Clear();
            RefreshPlayerCache();
            ConfiguredSharedObjectSpawnerFishNet.Instance?.EnsureServerObjectsSpawned();
            EnsureQueueRuntimeSpawned();
            TryMergeLifecycleLocalPlaceholder();

            RaiseStatus($"Created FishNet session '{sessionName}' on port {port}.");
            OnRoomCreated?.Invoke(sessionName);
            OnSessionBrowserUpdated?.Invoke(new List<string>(cachedSessions));
            SessionLifecycleTracker.Instance?.MarkCreateSucceeded(GetLifecycleClientId());
            TLog("CreateRoomRequested", GetLocalUserId(), sessionName, $"Created direct-connect FishNet session on port {port}.");
        }

        public void JoinRoom(string roomName)
        {
            if (!ResolveReferences())
                return;

            if (string.IsNullOrWhiteSpace(roomName))
            {
                RaiseFailure("Cannot join FishNet session: empty room name.");
                return;
            }

            if (networkManager.IsServerStarted || networkManager.IsClientStarted)
            {
                RaiseFailure("Cannot join FishNet session: already running a client or host.");
                return;
            }

            string sessionName = roomName.Trim();
            ushort port = SessionNameToPort(sessionName);
            tugboat.SetClientAddress(connectAddress);
            tugboat.SetPort(port);
            ConfiguredSharedObjectSpawnerFishNet.Instance?.RegisterConfiguredPrefabsIfNeeded();
            NetworkAvatarSpawnerFishNet.Instance?.RegisterSharedAvatarPrefabIfNeeded();
            RegisterQueueRuntimePrefabIfNeeded();

            SessionLifecycleTracker.Instance?.MarkJoinStarted(GetLifecycleClientId());

            if (!networkManager.ClientManager.StartConnection(connectAddress, port))
            {
                RaiseFailure($"Failed to join FishNet session '{sessionName}' at {connectAddress}:{port}.");
                return;
            }

            transportPrepared = true;
            currentSessionName = sessionName;
            cachedSessions.Clear();
            RaiseStatus($"Joining FishNet session '{sessionName}' at {connectAddress}:{port}...");
            OnSessionBrowserUpdated?.Invoke(new List<string>(cachedSessions));
            TLog("JoinRoomRequested", GetLocalUserId(), sessionName, $"Joining FishNet direct-connect session at {connectAddress}:{port}.");
        }

        public void LeaveRoom()
        {
            if (!ResolveReferences())
                return;

            if (!networkManager.IsServerStarted && !networkManager.IsClientStarted)
            {
                RaiseFailure("Cannot leave FishNet session: no active client or host.");
                return;
            }

            if (networkManager.IsServerStarted)
                SessionQueueRuntimeFishNet.Instance?.TryForceReleaseCurrentHolderForFinalization("Session leave");
            TLog("LeaveRoomRequested", GetLocalUserId(), CurrentRoomName, "Requested to leave FishNet session.");
            intentionalShutdownInProgress = true;

            ConfiguredSharedObjectSpawnerFishNet.Instance?.DespawnServerObjects();

            if (networkManager.IsServerStarted && spawnedQueueRuntime != null && spawnedQueueRuntime.IsSpawned)
            {
                networkManager.ServerManager.Despawn(spawnedQueueRuntime.gameObject);
                spawnedQueueRuntime = null;
            }

            if (networkManager.IsClientStarted)
                networkManager.ClientManager.StopConnection();

            if (networkManager.IsServerStarted)
                networkManager.ServerManager.StopConnection(false);

            ResetLocalState(notifyObservers: true);
            RaiseStatus("Left FishNet session.");

            intentionalShutdownInProgress = false;
        }

        public void BeginSession()
        {
            EnsureQueueRuntimeSpawned();

            SessionQueueRuntimeFishNet runtime = SessionQueueRuntimeFishNet.Instance;
            SessionLifecycleTracker.Instance?.MarkSessionStartPressed(GetLifecycleClientId());

            if (runtime != null && runtime.IsSpawned)
            {
                runtime.RequestBeginSessionForLocalPlayer();
                RaiseStatus("Requested session start.");
                TryMergeLifecycleLocalPlaceholder();
                SessionLifecycleTracker.Instance?.MarkSessionStartVisible(GetLifecycleClientId());
                NetworkAvatarSpawnerFishNet.Instance?.EnsureSpawnForConnectedClients();
                return;
            }

            localSessionStarted = true;
            RaiseStatus("Session started locally (runtime not found).");
            OnSessionStateChanged?.Invoke(localSessionStarted);
            NetworkAvatarSpawnerFishNet.Instance?.EnsureSpawnForConnectedClients();
        }

        public void ConfigureRuntime(NetworkManager manager, Tugboat transport, SessionQueueRuntimeFishNet runtimeTemplate)
        {
            networkManager = manager;
            tugboat = transport;
            queueRuntimeTemplate = runtimeTemplate;
            ResolveReferences();
        }

        public void RegisterQueueRuntimePrefabIfNeeded()
        {
            if (!ResolveReferences())
                return;

            SessionQueueRuntimeFishNet template = ResolveQueueRuntimeTemplate();
            if (template == null || networkManager.SpawnablePrefabs == null)
                return;

            NetworkObject templateNetworkObject = template.GetComponent<NetworkObject>();
            if (templateNetworkObject == null)
                return;

            int count = networkManager.SpawnablePrefabs.GetObjectCount();
            for (int index = 0; index < count; index++)
            {
                if (networkManager.SpawnablePrefabs.GetObject(true, index) == templateNetworkObject)
                    return;
            }

            networkManager.SpawnablePrefabs.AddObject(templateNetworkObject, checkForDuplicates: true, initializeAdded: true);
            Log($"Registered FishNet queue runtime template '{templateNetworkObject.name}' in SpawnablePrefabs.");
        }

        public string ConvertClientToUserId(int clientId)
        {
            return $"Client_{clientId}";
        }

        public int GetMasterClientId()
        {
            if (IsNetworkManagerInitialized &&
                networkManager.IsServerStarted &&
                networkManager.IsClientStarted &&
                networkManager.ClientManager != null &&
                networkManager.ClientManager.Connection.IsValid)
            {
                return networkManager.ClientManager.Connection.ClientId;
            }

            if (cachedPlayers.Count > 0)
                return cachedPlayers[0];

            if (IsNetworkManagerInitialized &&
                networkManager.IsClientStarted &&
                networkManager.ClientManager != null &&
                networkManager.ClientManager.Connection.IsValid)
            {
                return networkManager.ClientManager.Connection.ClientId;
            }

            return 0;
        }

        public string GetMasterUserId()
        {
            return ConvertClientToUserId(GetMasterClientId());
        }

        public string GetLocalUserId()
        {
            if (IsNetworkManagerInitialized &&
                networkManager.IsClientStarted &&
                networkManager.ClientManager != null &&
                networkManager.ClientManager.Connection.IsValid)
            {
                return ConvertClientToUserId(networkManager.ClientManager.Connection.ClientId);
            }

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

        private bool ResolveReferences()
        {
            if (networkManager == null)
                networkManager = FindFirstObjectByType<NetworkManager>();

            if (tugboat == null && networkManager != null)
                tugboat = networkManager.GetComponent<Tugboat>();

            ResolveQueueRuntimeTemplate();

            if (networkManager == null || tugboat == null)
            {
                ReportInitializationFailureOnce("SessionOrchestratorFishNet is missing NetworkManager or Tugboat transport.");
                return false;
            }

            if (!networkManager.Initialized ||
                networkManager.ClientManager == null ||
                networkManager.ServerManager == null ||
                networkManager.TransportManager == null ||
                networkManager.SpawnablePrefabs == null)
            {
                ReportInitializationFailureOnce(
                    "FishNet NetworkManager did not initialize. Re-run showcase authoring so its SpawnablePrefabs reference is repaired.");
                return false;
            }

            initializationFailureReported = false;
            return true;
        }

        private void ReportInitializationFailureOnce(string message)
        {
            if (initializationFailureReported)
                return;

            initializationFailureReported = true;
            RaiseFailure(message);
        }

        private void TryBindCallbacks()
        {
            if (callbacksBound || !IsNetworkManagerInitialized)
                return;

            networkManager.ClientManager.OnClientConnectionState += HandleClientConnectionState;
            networkManager.ClientManager.OnRemoteConnectionState += HandleClientRemoteConnectionState;
            networkManager.ServerManager.OnServerConnectionState += HandleServerConnectionState;
            networkManager.ServerManager.OnRemoteConnectionState += HandleServerRemoteConnectionState;
            callbacksBound = true;
        }

        private void UnbindCallbacks()
        {
            if (!callbacksBound || !IsNetworkManagerInitialized)
                return;

            networkManager.ClientManager.OnClientConnectionState -= HandleClientConnectionState;
            networkManager.ClientManager.OnRemoteConnectionState -= HandleClientRemoteConnectionState;
            networkManager.ServerManager.OnServerConnectionState -= HandleServerConnectionState;
            networkManager.ServerManager.OnRemoteConnectionState -= HandleServerRemoteConnectionState;
            callbacksBound = false;
        }

        private void HandleClientConnectionState(ClientConnectionStateArgs args)
        {
            RefreshPlayerCache();

            if (args.ConnectionState == LocalConnectionState.Started)
            {
                transportPrepared = true;
                EnsureQueueRuntimeSpawned();
                TryMergeLifecycleLocalPlaceholder();
                RaiseStatus($"Joined FishNet session '{currentSessionName}'.");
                OnRoomJoined?.Invoke(currentSessionName);
                SessionLifecycleTracker.Instance?.MarkJoinSucceeded(GetLifecycleClientId());
                return;
            }

            if (args.ConnectionState != LocalConnectionState.Stopped || intentionalShutdownInProgress)
                return;

            string message = $"Disconnected from FishNet session '{currentSessionName}'.";
            ResetLocalState(notifyObservers: true);
            RaiseStatus(message);
            OnConnectionFailed?.Invoke(message);
        }

        private void HandleServerConnectionState(ServerConnectionStateArgs args)
        {
            RefreshPlayerCache();

            if (args.ConnectionState == LocalConnectionState.Started)
            {
                EnsureQueueRuntimeSpawned();
                return;
            }

            if (args.ConnectionState != LocalConnectionState.Stopped || intentionalShutdownInProgress)
                return;

            if (networkManager != null && networkManager.IsClientStarted)
                return;

            ResetLocalState(notifyObservers: true);
        }

        // When a remote connection stops, FishNet still lists it in the Clients dictionaries while the
        // event runs. Refreshing only then kept the departed participant in the player list, so the queue
        // never processed the departure. Exclude the stopping connection now and refresh again next frame.
        private void HandleServerRemoteConnectionState(NetworkConnection connection, RemoteConnectionStateArgs args)
        {
            RefreshPlayerCache(args.ConnectionState == RemoteConnectionState.Stopped ? args.ConnectionId : (int?)null);
            playerCacheRefreshPending = true;
        }

        // FishNet 4.6.22 NetworkObject.Start() deactivates an object when the client is not yet reported as
        // started+authenticated. Objects in the initial spawn batch of a late-joining client can reach Start()
        // in that window although they are already spawned and client-initialized, leaving the replica inactive
        // (its behaviours never run). Observed in the L3 LateJoin lifecycle case; the server never despawned it.
        private void ReactivateClientSpawnedObjectsDeactivatedAtStart()
        {
            if (!IsNetworkManagerInitialized || networkManager.IsServerStarted || !networkManager.IsClientStarted)
                return;

            foreach (NetworkObject spawned in networkManager.ClientManager.Objects.Spawned.Values)
            {
                if (spawned == null || spawned.gameObject.activeSelf ||
                    !spawned.IsClientInitialized || !spawned.IsSpawned || spawned.IsNested)
                {
                    continue;
                }

                spawned.gameObject.SetActive(true);
                Debug.Log($"[SessionOrchestratorFishNet] Reactivated client-spawned object '{spawned.name}' " +
                          $"(objectId={spawned.ObjectId}) that FishNet deactivated at Start before the client was reported started.");
                TelemetryLoggerCore.Instance?.LogEvent(
                    "FishNetSpawnedObjectReactivated",
                    GetLocalUserId(),
                    spawned.name,
                    $"objectId={spawned.ObjectId}; deactivated by NetworkObject.Start while client not yet reported started.");
            }
        }

        private void HandleClientRemoteConnectionState(RemoteConnectionStateArgs args)
        {
            RefreshPlayerCache(args.ConnectionState == RemoteConnectionState.Stopped ? args.ConnectionId : (int?)null);
            playerCacheRefreshPending = true;
        }

        private void RefreshPlayerCache(int? stoppingConnectionId = null)
        {
            HashSet<int> playerIds = new HashSet<int>();

            if (IsNetworkManagerInitialized)
            {
                if (networkManager.IsClientStarted &&
                    networkManager.ClientManager != null &&
                    networkManager.ClientManager.Connection.IsValid)
                {
                    playerIds.Add(networkManager.ClientManager.Connection.ClientId);
                }

                if (networkManager.ClientManager != null && networkManager.ClientManager.Clients != null)
                {
                    foreach (int clientId in networkManager.ClientManager.Clients.Keys)
                        playerIds.Add(clientId);
                }

                if (networkManager.ServerManager != null && networkManager.ServerManager.Clients != null)
                {
                    foreach (int clientId in networkManager.ServerManager.Clients.Keys)
                        playerIds.Add(clientId);
                }
            }

            if (stoppingConnectionId.HasValue)
                playerIds.Remove(stoppingConnectionId.Value);

            cachedPlayers.Clear();
            cachedPlayers.AddRange(playerIds.OrderBy(id => id));
            OnPlayerListUpdated?.Invoke(new List<int>(cachedPlayers));
        }

        private SessionQueueRuntimeFishNet ResolveQueueRuntimeTemplate()
        {
            if (queueRuntimeTemplate != null)
                return queueRuntimeTemplate;

            SessionQueueRuntimeFishNet[] runtimes = GetComponentsInChildren<SessionQueueRuntimeFishNet>(true);
            foreach (SessionQueueRuntimeFishNet runtime in runtimes)
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
            if (!spawnQueueRuntimeAutomatically || !ResolveReferences() || !networkManager.IsServerStarted)
                return;

            if (spawnedQueueRuntime != null && spawnedQueueRuntime.IsSpawned)
                return;

            RegisterQueueRuntimePrefabIfNeeded();

            SessionQueueRuntimeFishNet template = ResolveQueueRuntimeTemplate();
            if (template == null)
            {
                Log("Queue runtime spawn skipped because no template is configured.");
                return;
            }

            NetworkObject templateNetworkObject = template.GetComponent<NetworkObject>();
            if (templateNetworkObject == null)
            {
                Log("Queue runtime spawn skipped because the template is missing NetworkObject.");
                return;
            }

            NetworkObject spawnedNetworkObject = networkManager.GetPooledInstantiated(
                templateNetworkObject,
                Vector3.zero,
                Quaternion.identity,
                true);

            if (spawnedNetworkObject == null)
            {
                Log("Queue runtime spawn skipped because pooled instantiation failed.");
                return;
            }

            networkManager.ServerManager.Spawn(spawnedNetworkObject);
            spawnedQueueRuntime = spawnedNetworkObject.GetComponent<SessionQueueRuntimeFishNet>();
            Log("Spawned FishNet queue runtime.");
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

            Debug.Log($"[SessionOrchestratorFishNet] {message}", this);
        }

        private void TLog(string eventType, string userId, string instrumentId, string details)
        {
            TelemetryLoggerCore.Instance?.LogEvent(eventType, userId, instrumentId, details);
        }
    }
}
