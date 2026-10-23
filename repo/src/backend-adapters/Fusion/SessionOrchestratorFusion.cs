using Fusion;
using Fusion.Sockets;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Thesis.Orchestration.SharedSync;
using Thesis.Orchestration.Telemetry;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Thesis.Orchestration.Backends.Fusion
{
    public class SessionOrchestratorFusion : MonoBehaviour, INetworkRunnerCallbacks
    {
        public static SessionOrchestratorFusion Instance { get; private set; }

        [Header("Fusion Settings")]
        [SerializeField] private bool autoCreateRunnerOnStart = false;
        [SerializeField] private string gameVersion = "0.1";
        [SerializeField] private GameMode gameMode = GameMode.Shared;

        [Header("Lobby Settings")]
        [SerializeField] private bool autoJoinSharedLobbyAfterConnect = true;
        [SerializeField] private string customLobbyName = "";

        [Header("Room Settings")]
        [SerializeField] private int maxPlayersPerSession = 8;
        [SerializeField] private string defaultSessionName = "DefaultSession";
        [SerializeField] private SessionQueueRuntimeFusion queueRuntimeTemplate;
        [SerializeField] private bool spawnQueueRuntimeAutomatically = true;

        [Header("Debug")]
        [SerializeField] private bool verboseLogs = true;

        private NetworkRunner runner;
        private string currentSessionName = "None";
        private readonly List<PlayerRef> cachedPlayers = new();
        private readonly List<SessionInfo> cachedSessions = new();
        private const string LocalLifecyclePlaceholderId = "Fusion_LocalClient";

        private bool localSessionStarted;
        private bool isInLobby;
        private SessionQueueRuntimeFusion spawnedQueueRuntime;
        private bool sceneLoadDone;

        public NetworkRunner Runner => runner;
        public bool IsConnectedToMasterServer => runner != null && runner.IsCloudReady;
        // runner.IsRunning becomes true while StartGame is still in progress, and the session scene load that
        // follows despawns objects spawned before it; callers that act on "in room" (BeginSession, avatar and
        // queue-runtime spawn) failed or lost state in that window.
        public bool IsInRoom => runner != null && runner.IsRunning && currentSessionName != "None" && sceneLoadDone;
        public bool IsInLobby => isInLobby;
        public bool IsMasterClient => runner != null && (runner.IsServer || runner.IsSharedModeMasterClient);

        [SerializeField] private bool autoConnectOnStart = true;
        private bool hasAutoConnectAttempted;

        public bool SessionStarted
        {
            get
            {
                SessionQueueRuntimeFusion runtime = SessionQueueRuntimeFusion.Instance;
                if (runtime != null && runtime.IsRuntimeSpawned)
                    return runtime.SessionStarted;

                return localSessionStarted;
            }
        }

        public string CurrentRoomName => currentSessionName;
        public IReadOnlyList<PlayerRef> CurrentPlayers => cachedPlayers;
        public IReadOnlyList<SessionInfo> AvailableSessions => cachedSessions;

        public event Action OnConnectedToServerEvent;
        public event Action<string> OnConnectionFailed;
        public event Action<string> OnRoomCreated;
        public event Action<string> OnRoomJoined;
        public event Action<List<PlayerRef>> OnPlayerListUpdated;
        public event Action<bool> OnSessionStateChanged;
        public event Action<string> OnStatusMessage;
        public event Action<List<SessionInfo>> OnSessionBrowserUpdated;

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Debug.LogWarning("[SessionOrchestratorFusion] Duplicate instance found. Destroying duplicate.", this);
                Destroy(gameObject);
                return;
            }

            Instance = this;
        }

        private void OnDestroy()
        {
            if (runner != null && IsMasterClient)
                SessionQueueRuntimeFusion.Instance?.TryForceReleaseCurrentHolderForFinalization("Runtime cleanup");

            if (runner != null)
                runner.RemoveCallbacks(this);

            if (Instance == this)
                Instance = null;
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
            ResolveQueueRuntimeTemplate();

            bool started = SessionStarted;
            if (started != localSessionStarted)
            {
                localSessionStarted = started;
                OnSessionStateChanged?.Invoke(localSessionStarted);
            }
        }

        public async void ConnectToServer()
        {
            await EnsureRunnerExistsAsync();

            SessionLifecycleTracker.Instance?.MarkConnectStarted(GetLifecycleClientId());

            if (autoJoinSharedLobbyAfterConnect)
            {
                var result = await JoinLobbyAsync();
                if (result.Ok)
                {
                    RaiseStatus("Connected to Fusion Cloud and joined Shared Lobby.");
                    OnConnectedToServerEvent?.Invoke();
                    SessionLifecycleTracker.Instance?.MarkConnectSucceeded(GetLifecycleClientId());
                }
                else
                {
                    string msg = $"Failed to join Shared Lobby: {result.ShutdownReason}";
                    RaiseStatus(msg);
                    OnConnectionFailed?.Invoke(msg);
                }
            }
            else
            {
                RaiseStatus("Fusion runner prepared.");
                OnConnectedToServerEvent?.Invoke();
            }
        }

        public async void RefreshSessionBrowser()
        {
            await EnsureRunnerExistsAsync();

            if (!isInLobby)
            {
                var result = await JoinLobbyAsync();
                if (!result.Ok)
                {
                    string msg = $"Refresh failed: could not join lobby. Reason: {result.ShutdownReason}";
                    RaiseStatus(msg);
                    OnConnectionFailed?.Invoke(msg);
                }
            }
        }

        public async void CreateRoom(string roomName)
        {
            if (string.IsNullOrWhiteSpace(roomName))
            {
                RaiseStatus("Cannot create session: empty session name.");
                return;
            }

            await EnsureRunnerExistsAsync();

            SessionLifecycleTracker.Instance?.MarkCreateStarted(GetLifecycleClientId());

            string sessionName = roomName.Trim();

            StartGameArgs args = new StartGameArgs
            {
                GameMode = gameMode,
                SessionName = sessionName,
                PlayerCount = maxPlayersPerSession,
                IsOpen = true,
                IsVisible = true,
                Scene = SceneRef.FromIndex(SceneManager.GetActiveScene().buildIndex),
                SceneManager = runner.GetComponent<NetworkSceneManagerDefault>()
            };

            if (!string.IsNullOrWhiteSpace(customLobbyName))
                args.CustomLobbyName = customLobbyName;

            StartGameResult result = await runner.StartGame(args);

            if (result.Ok)
            {
                isInLobby = false;
                currentSessionName = sessionName;
                EnsureQueueRuntimeSpawned();
                RaiseStatus($"Created Fusion session '{sessionName}'.");
                OnRoomCreated?.Invoke(sessionName);
                TryMergeLifecycleLocalPlaceholder();
                SessionLifecycleTracker.Instance?.MarkCreateSucceeded(GetLifecycleClientId());
            }
            else
            {
                string msg = $"Create session failed: {result.ShutdownReason}";
                RaiseStatus(msg);
                OnConnectionFailed?.Invoke(msg);
            }
        }

        public async void JoinRoom(string roomName)
        {
            if (string.IsNullOrWhiteSpace(roomName))
            {
                RaiseStatus("Cannot join session: empty session name.");
                return;
            }

            await EnsureRunnerExistsAsync();

            SessionLifecycleTracker.Instance?.MarkJoinStarted(GetLifecycleClientId());

            string sessionName = roomName.Trim();

            StartGameArgs args = new StartGameArgs
            {
                GameMode = gameMode,
                SessionName = sessionName,
                Scene = SceneRef.FromIndex(SceneManager.GetActiveScene().buildIndex),
                SceneManager = runner.GetComponent<NetworkSceneManagerDefault>()
            };

            if (!string.IsNullOrWhiteSpace(customLobbyName))
                args.CustomLobbyName = customLobbyName;

            StartGameResult result = await runner.StartGame(args);

            if (result.Ok)
            {
                isInLobby = false;
                currentSessionName = sessionName;
                RaiseStatus($"Joined Fusion session '{sessionName}'.");
                OnRoomJoined?.Invoke(sessionName);
                TryMergeLifecycleLocalPlaceholder();
                SessionLifecycleTracker.Instance?.MarkJoinSucceeded(GetLifecycleClientId());
            }
            else
            {
                string msg = $"Join session failed: {result.ShutdownReason}";
                RaiseStatus(msg);
                OnConnectionFailed?.Invoke(msg);
            }
        }

        public async void LeaveRoom()
        {
            if (runner == null)
            {
                RaiseStatus("Cannot leave session: runner missing.");
                return;
            }

            if (IsMasterClient)
                SessionQueueRuntimeFusion.Instance?.TryForceReleaseCurrentHolderForFinalization("Session leave");

            if (spawnedQueueRuntime != null && spawnedQueueRuntime.Object != null && spawnedQueueRuntime.Object.IsValid && IsMasterClient)
                runner.Despawn(spawnedQueueRuntime.Object);

            await runner.Shutdown();
            DisposeRunner(runner);

            currentSessionName = "None";
            cachedPlayers.Clear();
            cachedSessions.Clear();
            localSessionStarted = false;
            isInLobby = false;
            hasAutoConnectAttempted = false;
            spawnedQueueRuntime = null;
            sceneLoadDone = false;

            RaiseStatus("Left Fusion session.");
            OnPlayerListUpdated?.Invoke(new List<PlayerRef>(cachedPlayers));
            OnSessionStateChanged?.Invoke(false);
            OnSessionBrowserUpdated?.Invoke(new List<SessionInfo>(cachedSessions));
        }

        public void BeginSession()
        {
            EnsureQueueRuntimeSpawned();
            SessionQueueRuntimeFusion runtime = SessionQueueRuntimeFusion.Instance;
            SessionLifecycleTracker.Instance?.MarkSessionStartPressed(GetLifecycleClientId());
            if (runtime != null)
            {
                runtime.RequestBeginSessionForLocalPlayer();
                RaiseStatus("Requested session start.");
                TryMergeLifecycleLocalPlaceholder();
                SessionLifecycleTracker.Instance?.MarkSessionStartVisible(GetLifecycleClientId());
                NetworkAvatarSpawnerFusion.Instance?.RequestSpawn();
            }
            else
            {
                localSessionStarted = true;
                RaiseStatus("Session started locally (runtime not found).");
                OnSessionStateChanged?.Invoke(localSessionStarted);
                NetworkAvatarSpawnerFusion.Instance?.RequestSpawn();
            }
        }

        public bool SessionExists(string roomName)
        {
            if (string.IsNullOrWhiteSpace(roomName))
                return false;

            string trimmed = roomName.Trim();

            foreach (SessionInfo session in cachedSessions)
            {
                if (session.Name == trimmed)
                    return true;
            }

            return false;
        }

        public void ConfigureRuntime(SessionQueueRuntimeFusion runtimeTemplate)
        {
            queueRuntimeTemplate = runtimeTemplate;
            ResolveQueueRuntimeTemplate();
        }

        public string ConvertPlayerToUserId(PlayerRef player)
        {
            return $"Player_{player.RawEncoded}";
        }

        public string GetLocalUserId()
        {
            if (runner == null || !runner.IsRunning)
                return "UnknownUser";

            return ConvertPlayerToUserId(runner.LocalPlayer);
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
            SessionLifecycleTracker.Instance?.EnsureClientObserved(localUserId);
        }

        private async Task EnsureRunnerExistsAsync()
        {
            if (runner != null)
                return;

            runner = FindFirstObjectByType<NetworkRunner>();
            if (runner != null)
            {
                if (!runner.IsRunning && !runner.IsCloudReady)
                {
                    DisposeRunner(runner);
                    runner = null;
                }
            }

            if (runner != null)
            {
                runner.RemoveCallbacks(this);
                runner.AddCallbacks(this);
                return;
            }

            GameObject go = new GameObject("FusionNetworkRunner");
            runner = go.AddComponent<NetworkRunner>();
            runner.ProvideInput = false;

            if (go.GetComponent<NetworkSceneManagerDefault>() == null)
                go.AddComponent<NetworkSceneManagerDefault>();

            runner.AddCallbacks(this);

            await Task.Yield();
        }

        private SessionQueueRuntimeFusion ResolveQueueRuntimeTemplate()
        {
            if (queueRuntimeTemplate != null)
                return queueRuntimeTemplate;

            GameObject loaded = Resources.Load<GameObject>(OrchestrationRuntimePrefabPaths.FusionQueueRuntimeResourcesPath);
            queueRuntimeTemplate = loaded != null ? loaded.GetComponent<SessionQueueRuntimeFusion>() : null;
            return queueRuntimeTemplate;
        }

        private void EnsureQueueRuntimeSpawned()
        {
            // A spawn attempted inside StartGame (OnPlayerJoined) threw and left a half-created instance that
            // claimed SessionQueueRuntimeFusion.Instance, so the real runtime destroyed itself as a "duplicate".
            if (!spawnQueueRuntimeAutomatically || !IsInRoom || !IsMasterClient)
                return;

            // F14: this is reached from CreateRoom, OnPlayerJoined (which in Shared mode runs inside StartGame) and
            // BeginSession. A spawned runtime is not yet IsValid on the first of those calls, so the validity check
            // alone spawned up to three runtimes. Any existing instance (including one inherited through
            // master-client migration) counts.
            if (spawnedQueueRuntime != null)
                return;

            SessionQueueRuntimeFusion existing = FindObjectsByType<SessionQueueRuntimeFusion>(FindObjectsSortMode.None)
                .FirstOrDefault(runtime => runtime != null && runtime.Object != null && runtime.Object.IsValid);
            if (existing != null)
            {
                spawnedQueueRuntime = existing;
                return;
            }

            SessionQueueRuntimeFusion template = ResolveQueueRuntimeTemplate();
            if (template == null)
            {
                RaiseStatus("Fusion queue runtime template is missing.");
                return;
            }

            try
            {
                SessionQueueRuntimeFusion queueRuntimeInstance = runner.Spawn(template, Vector3.zero, Quaternion.identity, PlayerRef.None);
                spawnedQueueRuntime = queueRuntimeInstance;
                if (spawnedQueueRuntime == null)
                {
                    RaiseStatus("Fusion queue runtime spawn returned null.");
                    return;
                }

                RaiseStatus("Fusion queue runtime spawned.");
            }
            catch (Exception ex)
            {
                RaiseStatus($"Fusion queue runtime spawn failed: {ex.Message}");
            }
        }

        private async Task<StartGameResult> JoinLobbyAsync()
        {
            if (runner == null)
                await EnsureRunnerExistsAsync();

            SessionLobby lobbyType = string.IsNullOrWhiteSpace(customLobbyName)
                ? SessionLobby.Shared
                : SessionLobby.Custom;

            StartGameResult result = string.IsNullOrWhiteSpace(customLobbyName)
                ? await runner.JoinSessionLobby(lobbyType)
                : await runner.JoinSessionLobby(lobbyType, customLobbyName);

            if (result.Ok)
                isInLobby = true;

            return result;
        }

        private void RefreshPlayerCacheFromRunner(NetworkRunner networkRunner)
        {
            cachedPlayers.Clear();

            if (networkRunner == null || !networkRunner.IsRunning)
            {
                OnPlayerListUpdated?.Invoke(new List<PlayerRef>(cachedPlayers));
                return;
            }

            foreach (PlayerRef player in networkRunner.ActivePlayers)
                cachedPlayers.Add(player);

            OnPlayerListUpdated?.Invoke(new List<PlayerRef>(cachedPlayers));
        }

        private void RaiseStatus(string message)
        {
            Log(message);
            OnStatusMessage?.Invoke(message);
        }

        private void Log(string message)
        {
            if (!verboseLogs)
                return;

            Debug.Log($"[SessionOrchestratorFusion] {message}", this);
        }

        private void DisposeRunner(NetworkRunner runnerToDispose)
        {
            if (runnerToDispose == null)
                return;

            runnerToDispose.RemoveCallbacks(this);

            if (runnerToDispose.gameObject != null)
                Destroy(runnerToDispose.gameObject);

            if (ReferenceEquals(runner, runnerToDispose))
                runner = null;
        }

        void INetworkRunnerCallbacks.OnConnectedToServer(NetworkRunner runner)
        {
            RaiseStatus("Connected to Fusion session/server.");
        }

        void INetworkRunnerCallbacks.OnDisconnectedFromServer(NetworkRunner runner, NetDisconnectReason reason)
        {
            currentSessionName = "None";
            cachedPlayers.Clear();
            cachedSessions.Clear();
            localSessionStarted = false;
            isInLobby = false;
            hasAutoConnectAttempted = false;
            spawnedQueueRuntime = null;
            sceneLoadDone = false;

            string msg = $"Disconnected from Fusion. Reason: {reason}";
            RaiseStatus(msg);
            OnConnectionFailed?.Invoke(msg);
            OnPlayerListUpdated?.Invoke(new List<PlayerRef>(cachedPlayers));
            OnSessionStateChanged?.Invoke(false);
            OnSessionBrowserUpdated?.Invoke(new List<SessionInfo>(cachedSessions));

            if (this.runner == runner)
                DisposeRunner(runner);
        }

        void INetworkRunnerCallbacks.OnPlayerJoined(NetworkRunner runner, PlayerRef player)
        {
            RefreshPlayerCacheFromRunner(runner);
            EnsureQueueRuntimeSpawned();
            RaiseStatus($"Player joined: {ConvertPlayerToUserId(player)}");

            if (player == runner.LocalPlayer)
            {
                NetworkAvatarSpawnerFusion.Instance?.RequestSpawn();
            }
        }

        void INetworkRunnerCallbacks.OnPlayerLeft(NetworkRunner runner, PlayerRef player)
        {
            RefreshPlayerCacheFromRunner(runner);
            RaiseStatus($"Player left: {ConvertPlayerToUserId(player)}");
        }

        void INetworkRunnerCallbacks.OnSessionListUpdated(NetworkRunner runner, List<SessionInfo> sessionList)
        {
            cachedSessions.Clear();

            if (sessionList != null)
                cachedSessions.AddRange(sessionList);

            RaiseStatus($"Session list updated. Count={cachedSessions.Count}");
            OnSessionBrowserUpdated?.Invoke(new List<SessionInfo>(cachedSessions));
        }

        void INetworkRunnerCallbacks.OnConnectFailed(NetworkRunner runner, NetAddress remoteAddress, NetConnectFailedReason reason)
        {
            string msg = $"Fusion connect failed: {reason}";
            RaiseStatus(msg);
            OnConnectionFailed?.Invoke(msg);
        }

        void INetworkRunnerCallbacks.OnShutdown(NetworkRunner runner, ShutdownReason shutdownReason)
        {
            currentSessionName = "None";
            cachedPlayers.Clear();
            cachedSessions.Clear();
            localSessionStarted = false;
            isInLobby = false;
            hasAutoConnectAttempted = false;
            spawnedQueueRuntime = null;
            sceneLoadDone = false;

            string msg = $"Fusion shutdown: {shutdownReason}";
            RaiseStatus(msg);
            OnPlayerListUpdated?.Invoke(new List<PlayerRef>(cachedPlayers));
            OnSessionStateChanged?.Invoke(false);
            OnSessionBrowserUpdated?.Invoke(new List<SessionInfo>(cachedSessions));

            if (this.runner == runner)
                DisposeRunner(runner);
        }

        void INetworkRunnerCallbacks.OnSceneLoadStart(NetworkRunner runner)
        {
            sceneLoadDone = false;
            Log("Fusion scene load started.");
        }

        // StartGame loads the session scene; network objects spawned before this completes were despawned by
        // the load (observed: the queue runtime spawned and despawned twice, losing session state on the host).
        void INetworkRunnerCallbacks.OnSceneLoadDone(NetworkRunner runner)
        {
            sceneLoadDone = true;
            Log("Fusion scene load done.");
            EnsureQueueRuntimeSpawned();
        }
        void INetworkRunnerCallbacks.OnInput(NetworkRunner runner, NetworkInput input) { }
        void INetworkRunnerCallbacks.OnInputMissing(NetworkRunner runner, PlayerRef player, NetworkInput input) { }
        void INetworkRunnerCallbacks.OnConnectRequest(NetworkRunner runner, NetworkRunnerCallbackArgs.ConnectRequest request, byte[] token) { }
        void INetworkRunnerCallbacks.OnUserSimulationMessage(NetworkRunner runner, SimulationMessagePtr message) { }
        void INetworkRunnerCallbacks.OnCustomAuthenticationResponse(NetworkRunner runner, Dictionary<string, object> data) { }
        void INetworkRunnerCallbacks.OnHostMigration(NetworkRunner runner, HostMigrationToken hostMigrationToken) { }
        void INetworkRunnerCallbacks.OnReliableDataReceived(NetworkRunner runner, PlayerRef player, ReliableKey key, ArraySegment<byte> data) { }
        void INetworkRunnerCallbacks.OnReliableDataProgress(NetworkRunner runner, PlayerRef player, ReliableKey key, float progress) { }
        void INetworkRunnerCallbacks.OnObjectEnterAOI(NetworkRunner runner, NetworkObject obj, PlayerRef player) { }
        void INetworkRunnerCallbacks.OnObjectExitAOI(NetworkRunner runner, NetworkObject obj, PlayerRef player) { }
    }
}
