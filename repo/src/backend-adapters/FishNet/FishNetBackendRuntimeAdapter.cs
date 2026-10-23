using System;
using System.Collections.Generic;
using System.Linq;
using Thesis.Orchestration.EditorTools;
using Thesis.Orchestration.Runtime;
using Thesis.Orchestration.Telemetry;
using UnityEngine;

namespace Thesis.Orchestration.Backends.FishNet
{
    public class FishNetBackendRuntimeAdapter : MonoBehaviour, IOrchestrationBackendRuntime, IOrchestrationSessionView, IOrchestrationQueueView, IExecutionEnvironmentDescriptor
    {
        [SerializeField] private SessionOrchestratorFishNet session;
        [SerializeField] private SessionQueueRuntimeFishNet queue;

        public void DescribeExecutionEnvironment(IDictionary<string, string> into)
        {
            global::FishNet.Managing.NetworkManager manager = global::FishNet.InstanceFinder.NetworkManager;
            into["sdk"] = "FishNet";
            into["sdk_version"] = global::FishNet.Managing.NetworkManager.FISHNET_VERSION;
            into["room_discovery"] = "unsupported (direct connect)";
            if (manager == null)
            {
                into["network_manager"] = "unavailable";
                return;
            }

            into["tick_rate_hz"] = manager.TimeManager != null ? manager.TimeManager.TickRate.ToString() : "unavailable";
            into["transport"] = manager.TransportManager != null && manager.TransportManager.Transport != null
                ? manager.TransportManager.Transport.GetType().FullName
                : "unavailable";
            into["topology"] = manager.IsServerStarted && manager.IsClientStarted ? "Host" :
                manager.IsServerStarted ? "Server" : manager.IsClientStarted ? "Client" : "Offline";
        }

        public OrchestrationBackendType BackendType => OrchestrationBackendType.FishNet;
        public IOrchestrationSessionView SessionView => this;
        public IOrchestrationQueueView QueueView => this;

        public bool IsConnectedToServer => ResolveSession() != null && session.IsConnectedToMasterServer;
        public bool IsInRoom => ResolveSession() != null && session.IsInRoom;
        public bool IsMasterClient => ResolveSession() != null && session.IsMasterClient;
        public bool SessionStarted => ResolveSession() != null && session.SessionStarted;
        public string CurrentRoomName => ResolveSession() != null ? session.CurrentRoomName : "None";
        public string LocalUserId => ResolveSession() != null ? session.GetLocalUserId() : string.Empty;

        public string LocalPlayerDisplayText
        {
            get
            {
                if (ResolveSession() == null)
                    return "Local: None";

                string role = session.IsMasterClient ? "MASTER" : "CLIENT";
                return $"Local: {OrchestrationPlayerNameRegistry.GetLocalPlayerName()} [{role}]";
            }
        }

        public IReadOnlyList<string> AvailableRooms =>
            ResolveSession() != null ? session.AvailableSessions.ToList() : Array.Empty<string>();

        public IReadOnlyList<OrchestrationPlayerInfo> CurrentPlayers
        {
            get
            {
                if (ResolveSession() == null)
                    return Array.Empty<OrchestrationPlayerInfo>();

                SessionQueueRuntimeFishNet runtime = ResolveQueue();
                IReadOnlyList<string> replicatedPlayers = runtime?.ReplicatedPlayerUserIds;
                if (replicatedPlayers != null && replicatedPlayers.Count > 0)
                {
                    string masterUserId = runtime.ReplicatedMasterUserId;
                    return replicatedPlayers
                        .Select(userId => new OrchestrationPlayerInfo
                        {
                            UserId = userId,
                            DisplayName = ResolvePlayerDisplayName(userId),
                            NumericId = ParseClientId(userId),
                            IsMasterClient = string.Equals(userId, masterUserId, StringComparison.OrdinalIgnoreCase)
                        })
                        .OrderBy(player => player.NumericId)
                        .ToList();
                }

                return session.CurrentPlayers
                    .Select(clientId => new OrchestrationPlayerInfo
                    {
                        UserId = session.ConvertClientToUserId(clientId),
                        DisplayName = ResolvePlayerDisplayName(session.ConvertClientToUserId(clientId)),
                        NumericId = clientId,
                        IsMasterClient = clientId == session.GetMasterClientId()
                    })
                    .ToList();
            }
        }

        public bool QueueRuntimeActive => ResolveQueue() != null && queue.QueueRuntimeActive;
        public string CurrentInstrumentId => ResolveQueue() != null ? queue.CurrentInstrumentId : string.Empty;
        public string SessionRunId => ResolveQueue() != null ? queue.SessionRunId : string.Empty;
        public string CurrentHolderUserId => ResolveQueue() != null ? queue.CurrentHolderUserId : "None";
        public IReadOnlyList<string> CurrentQueueSnapshot => ResolveQueue() != null ? queue.CurrentQueueSnapshot : Array.Empty<string>();

        public bool HasSharedUserCardStats => ResolveQueue() != null && queue.HasSharedUserCardStats;
        public IReadOnlyList<UserCardStatsEntry> SharedUserCardStats =>
            ResolveQueue() != null ? queue.SharedUserCardStats : Array.Empty<UserCardStatsEntry>();

        public event Action Connected;
        public event Action<string> ConnectionFailed;
        public event Action<string> RoomCreated;
        public event Action<string> RoomJoined;
        public event Action SessionStateChanged;
        public event Action RoomsChanged;
        public event Action PlayersChanged;
        public event Action<string> StatusMessage;
        public event Action RuntimeStateChanged;

        private void Awake()
        {
            ResolveSession();
            ResolveQueue();
        }

        private void OnEnable()
        {
            ResolveSession();
            ResolveQueue();
            BindEvents();
        }

        private void Update()
        {
            ResolveSession();
            ResolveQueue();
            BindEvents();
        }

        private void OnDisable()
        {
            UnbindEvents();
        }

        public void ConfigureRuntime(SessionOrchestratorFishNet sessionReference)
        {
            session = sessionReference;
            queue = SessionQueueRuntimeFishNet.Instance;
            ResolveSession();
            ResolveQueue();
        }

        public void Connect() => ResolveSession()?.ConnectToServer();
        public void CreateRoom(string roomName) => ResolveSession()?.CreateRoom(roomName);
        public void JoinRoom(string roomName) => ResolveSession()?.JoinRoom(roomName);
        public void LeaveRoom() => ResolveSession()?.LeaveRoom();
        public void BeginSession() => ResolveSession()?.BeginSession();
        // Direct connect: no room discovery; the room name is mapped to a port and a join attempts the connection.
        public RoomLookupResult LookupRoom(string roomName) => RoomLookupResult.Unsupported;

        public void RequestAccessForLocalPlayer() => ResolveQueue()?.RequestAccessForLocalPlayer();
        public void ReleaseAccessForLocalPlayer() => ResolveQueue()?.ReleaseAccessForLocalPlayer();

        private SessionOrchestratorFishNet ResolveSession()
        {
            if (session == null)
                session = SessionOrchestratorFishNet.Instance;

            if (session == null)
                session = FindFirstObjectByType<SessionOrchestratorFishNet>();

            return session;
        }

        private SessionQueueRuntimeFishNet ResolveQueue()
        {
            if (queue == null || !queue.IsSpawned)
                queue = SessionQueueRuntimeFishNet.Instance;

            if (queue == null || !queue.IsSpawned)
                queue = FindFirstObjectByType<SessionQueueRuntimeFishNet>();

            return queue;
        }

        private void BindEvents()
        {
            if (ResolveSession() != null)
            {
                session.OnConnectedToServerEvent -= HandleConnected;
                session.OnConnectedToServerEvent += HandleConnected;
                session.OnConnectionFailed -= HandleConnectionFailed;
                session.OnConnectionFailed += HandleConnectionFailed;
                session.OnRoomCreated -= HandleRoomCreated;
                session.OnRoomCreated += HandleRoomCreated;
                session.OnRoomJoined -= HandleRoomJoined;
                session.OnRoomJoined += HandleRoomJoined;
                session.OnSessionStateChanged -= HandleSessionStateChanged;
                session.OnSessionStateChanged += HandleSessionStateChanged;
                session.OnSessionBrowserUpdated -= HandleRoomBrowserUpdated;
                session.OnSessionBrowserUpdated += HandleRoomBrowserUpdated;
                session.OnPlayerListUpdated -= HandlePlayerListUpdated;
                session.OnPlayerListUpdated += HandlePlayerListUpdated;
                session.OnStatusMessage -= HandleStatusMessage;
                session.OnStatusMessage += HandleStatusMessage;
            }

            if (ResolveQueue() != null)
            {
                queue.OnRuntimeStateUpdated -= HandleQueueRuntimeUpdated;
                queue.OnRuntimeStateUpdated += HandleQueueRuntimeUpdated;
            }
        }

        private void UnbindEvents()
        {
            if (session != null)
            {
                session.OnConnectedToServerEvent -= HandleConnected;
                session.OnConnectionFailed -= HandleConnectionFailed;
                session.OnRoomCreated -= HandleRoomCreated;
                session.OnRoomJoined -= HandleRoomJoined;
                session.OnSessionStateChanged -= HandleSessionStateChanged;
                session.OnSessionBrowserUpdated -= HandleRoomBrowserUpdated;
                session.OnPlayerListUpdated -= HandlePlayerListUpdated;
                session.OnStatusMessage -= HandleStatusMessage;
            }

            if (queue != null)
                queue.OnRuntimeStateUpdated -= HandleQueueRuntimeUpdated;
        }

        private void HandleConnected() => Connected?.Invoke();
        private void HandleConnectionFailed(string message) => ConnectionFailed?.Invoke(message);
        private void HandleRoomCreated(string roomName) => RoomCreated?.Invoke(roomName);
        private void HandleRoomJoined(string roomName) => RoomJoined?.Invoke(roomName);
        private void HandleSessionStateChanged(bool _) => SessionStateChanged?.Invoke();
        private void HandleRoomBrowserUpdated(List<string> _) => RoomsChanged?.Invoke();
        private void HandlePlayerListUpdated(List<int> _) => PlayersChanged?.Invoke();
        private void HandleStatusMessage(string message) => StatusMessage?.Invoke(message);
        private void HandleQueueRuntimeUpdated()
        {
            EvaluationRunContext.Instance?.ObserveSessionRunId(SessionRunId);
            RuntimeStateChanged?.Invoke();
        }

        private string ResolvePlayerDisplayName(string userId)
        {
            if (ResolveSession() != null &&
                string.Equals(userId, session.GetLocalUserId(), StringComparison.OrdinalIgnoreCase))
            {
                return OrchestrationPlayerNameRegistry.GetLocalPlayerName();
            }

            return OrchestrationPlayerNameRegistry.ResolveDisplayName(userId, userId);
        }

        private static int ParseClientId(string userId)
        {
            const string prefix = "Client_";
            if (!string.IsNullOrWhiteSpace(userId) &&
                userId.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) &&
                int.TryParse(userId.Substring(prefix.Length), out int clientId))
            {
                return clientId;
            }

            return int.MaxValue;
        }
    }
}
