using System;
using System.Collections.Generic;
using System.Linq;
using Thesis.Orchestration.EditorTools;
using Thesis.Orchestration.Runtime;
using Thesis.Orchestration.Telemetry;
using Unity.Netcode;
using UnityEngine;

namespace Thesis.Orchestration.Backends.Netcode
{
    public class NetcodeBackendRuntimeAdapter : MonoBehaviour, IOrchestrationBackendRuntime, IOrchestrationSessionView, IOrchestrationQueueView
    {
        [SerializeField] private SessionOrchestratorNetcode session;
        [SerializeField] private SessionQueueRuntimeNetcode queue;
        [SerializeField] private bool verboseLogs = true;

        public OrchestrationBackendType BackendType => OrchestrationBackendType.UnityNetcode;
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
                if (ResolveSession() == null || session.NetworkManager == null)
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

                SessionQueueRuntimeNetcode runtime = ResolveQueue();
                IReadOnlyList<string> replicatedPlayers = runtime?.ConnectedPlayersSnapshot;
                if (replicatedPlayers != null && replicatedPlayers.Count > 0)
                {
                    string masterUserId = session.ConvertClientToUserId(Unity.Netcode.NetworkManager.ServerClientId);
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
                        NumericId = (int)clientId,
                        IsMasterClient = clientId == Unity.Netcode.NetworkManager.ServerClientId
                    })
                    .ToList();
            }
        }

        public bool QueueRuntimeActive => ResolveQueue() != null && queue.QueueRuntimeActive;
        public string CurrentInstrumentId => ResolveQueue() != null ? queue.CurrentInstrumentId : string.Empty;
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

        private void OnDisable()
        {
            UnbindEvents();
        }

        public void Connect() => ResolveSession()?.ConnectToServer();
        public void CreateRoom(string roomName) => ResolveSession()?.CreateRoom(roomName);
        public void JoinRoom(string roomName) => ResolveSession()?.JoinRoom(roomName);
        public void LeaveRoom() => ResolveSession()?.LeaveRoom();
        public void BeginSession() => ResolveSession()?.BeginSession();
        public bool RoomExists(string roomName) => ResolveSession() != null && session.SessionExists(roomName);

        public void RequestAccessForLocalPlayer() => ResolveQueue()?.RequestAccessForLocalPlayer();
        public void ReleaseAccessForLocalPlayer() => ResolveQueue()?.ReleaseAccessForLocalPlayer();

        private SessionOrchestratorNetcode ResolveSession()
        {
            if (session == null)
                session = SessionOrchestratorNetcode.Instance;

            if (session == null)
                session = FindFirstObjectByType<SessionOrchestratorNetcode>();

            return session;
        }

        private SessionQueueRuntimeNetcode ResolveQueue()
        {
            if (queue == null)
                queue = SessionQueueRuntimeNetcode.Instance;

            if (queue == null)
                queue = FindFirstObjectByType<SessionQueueRuntimeNetcode>();

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
        private void HandlePlayerListUpdated(List<ulong> _) => PlayersChanged?.Invoke();
        private void HandleStatusMessage(string message) => StatusMessage?.Invoke(message);
        private void HandleQueueRuntimeUpdated() => RuntimeStateChanged?.Invoke();

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
                ulong.TryParse(userId.Substring(prefix.Length), out ulong clientId))
            {
                return clientId <= int.MaxValue ? (int)clientId : int.MaxValue;
            }

            return int.MaxValue;
        }
    }
}
