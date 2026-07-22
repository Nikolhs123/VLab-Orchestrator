using Fusion;
using System;
using System.Collections.Generic;
using System.Linq;
using Thesis.Orchestration.EditorTools;
using Thesis.Orchestration.Runtime;
using Thesis.Orchestration.Telemetry;
using UnityEngine;

namespace Thesis.Orchestration.Backends.Fusion
{
    public class FusionBackendRuntimeAdapter : MonoBehaviour, IOrchestrationBackendRuntime, IOrchestrationSessionView, IOrchestrationQueueView
    {
        [SerializeField] private SessionOrchestratorFusion session;
        [SerializeField] private SessionQueueRuntimeFusion queue;

        public OrchestrationBackendType BackendType => OrchestrationBackendType.Fusion;
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
                if (ResolveSession() == null || session.Runner == null || !session.Runner.IsRunning)
                    return "Local: None";

                string role = session.IsMasterClient ? "MASTER" : "CLIENT";
                return $"Local: {OrchestrationPlayerNameRegistry.GetLocalPlayerName()} [{role}]";
            }
        }

        public IReadOnlyList<string> AvailableRooms =>
            ResolveSession() != null
                ? session.AvailableSessions
                    .Where(s => s != null && s.IsValid)
                    .Select(s => s.Name)
                    .ToList()
                : Array.Empty<string>();

        public IReadOnlyList<OrchestrationPlayerInfo> CurrentPlayers
        {
            get
            {
                if (ResolveSession() == null)
                    return Array.Empty<OrchestrationPlayerInfo>();

                PlayerRef masterPlayer = ResolveMasterPlayer();
                return session.CurrentPlayers
                    .Select(player => new OrchestrationPlayerInfo
                    {
                        UserId = session.ConvertPlayerToUserId(player),
                        DisplayName = ResolvePlayerDisplayName(
                            session.ConvertPlayerToUserId(player),
                            $"User {player.RawEncoded}"),
                        NumericId = player.RawEncoded,
                        IsMasterClient = player == masterPlayer
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

        private SessionOrchestratorFusion ResolveSession()
        {
            if (session == null)
                session = SessionOrchestratorFusion.Instance;

            if (session == null)
                session = FindFirstObjectByType<SessionOrchestratorFusion>();

            return session;
        }

        private SessionQueueRuntimeFusion ResolveQueue()
        {
            if (queue == null)
                queue = SessionQueueRuntimeFusion.Instance;

            if (queue == null)
                queue = FindFirstObjectByType<SessionQueueRuntimeFusion>();

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

        public void Connect() => ResolveSession()?.ConnectToServer();
        public void CreateRoom(string roomName) => ResolveSession()?.CreateRoom(roomName);
        public void JoinRoom(string roomName) => ResolveSession()?.JoinRoom(roomName);
        public void LeaveRoom() => ResolveSession()?.LeaveRoom();
        public void BeginSession() => ResolveSession()?.BeginSession();
        public bool RoomExists(string roomName) => ResolveSession() != null && session.AvailableSessions.Any(s => s != null && s.IsValid && s.Name == roomName);

        public void RequestAccessForLocalPlayer() => ResolveQueue()?.RequestAccessForLocalPlayer();
        public void ReleaseAccessForLocalPlayer() => ResolveQueue()?.ReleaseAccessForLocalPlayer();

        private void HandleConnected() => Connected?.Invoke();
        private void HandleConnectionFailed(string msg) => ConnectionFailed?.Invoke(msg);
        private void HandleRoomCreated(string room) => RoomCreated?.Invoke(room);
        private void HandleRoomJoined(string room) => RoomJoined?.Invoke(room);
        private void HandleSessionStateChanged(bool _) => SessionStateChanged?.Invoke();
        private void HandleRoomBrowserUpdated(List<SessionInfo> _) => RoomsChanged?.Invoke();
        private void HandlePlayerListUpdated(List<PlayerRef> _) => PlayersChanged?.Invoke();
        private void HandleStatusMessage(string msg) => StatusMessage?.Invoke(msg);
        private void HandleQueueRuntimeUpdated() => RuntimeStateChanged?.Invoke();

        private string ResolvePlayerDisplayName(string userId, string fallback)
        {
            if (ResolveSession() != null &&
                string.Equals(userId, session.GetLocalUserId(), StringComparison.OrdinalIgnoreCase))
            {
                return OrchestrationPlayerNameRegistry.GetLocalPlayerName();
            }

            return OrchestrationPlayerNameRegistry.ResolveDisplayName(userId, fallback);
        }

        private PlayerRef ResolveMasterPlayer()
        {
            SessionQueueRuntimeFusion runtime = ResolveQueue();
            if (runtime != null &&
                runtime.Object != null &&
                runtime.Object.IsValid &&
                runtime.Object.StateAuthority != PlayerRef.None)
            {
                return runtime.Object.StateAuthority;
            }

            if (session != null && session.IsMasterClient && session.Runner != null)
                return session.Runner.LocalPlayer;

            return session != null && session.CurrentPlayers.Count > 0
                ? session.CurrentPlayers.OrderBy(player => player.RawEncoded).First()
                : PlayerRef.None;
        }
    }
}
