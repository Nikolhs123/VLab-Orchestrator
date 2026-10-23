using Photon.Pun;
using Photon.Realtime;
using System;
using System.Collections.Generic;
using System.Linq;
using Thesis.Orchestration.EditorTools;
using Thesis.Orchestration.Runtime;
using Thesis.Orchestration.Telemetry;
using UnityEngine;

namespace Thesis.Orchestration.Backends.PUN2
{
    public class PUN2BackendRuntimeAdapter : MonoBehaviour, IOrchestrationBackendRuntime, IOrchestrationSessionView, IOrchestrationQueueView, IExecutionEnvironmentDescriptor
    {
        [SerializeField] private SessionOrchestratorPUN2 session;
        [SerializeField] private SessionQueueRuntimePUN2 queue;

        public void DescribeExecutionEnvironment(IDictionary<string, string> into)
        {
            into["sdk"] = "Photon PUN 2";
            into["sdk_version"] = PhotonNetwork.PunVersion;
            into["room_discovery"] = "lobby room list";
            into["cloud_region"] = string.IsNullOrEmpty(PhotonNetwork.CloudRegion) ? "unavailable" : PhotonNetwork.CloudRegion;
            string fixedRegion = PhotonNetwork.PhotonServerSettings != null
                ? PhotonNetwork.PhotonServerSettings.AppSettings.FixedRegion
                : null;
            into["fixed_region_setting"] = PhotonNetwork.PhotonServerSettings == null
                ? "unavailable"
                : string.IsNullOrEmpty(fixedRegion) ? "none (best region selected at connect)" : fixedRegion;
            into["send_rate_hz"] = PhotonNetwork.SendRate.ToString();
            into["serialization_rate_hz"] = PhotonNetwork.SerializationRate.ToString();
            into["transport_protocol"] = PhotonNetwork.NetworkingClient != null
                ? PhotonNetwork.NetworkingClient.LoadBalancingPeer.TransportProtocol.ToString()
                : "unavailable";
            into["server_address"] = PhotonNetwork.ServerAddress ?? "";
            into["topology"] = PhotonNetwork.IsMasterClient ? "MasterClient" : PhotonNetwork.InRoom ? "Client" : "Offline";
        }

        public OrchestrationBackendType BackendType => OrchestrationBackendType.PUN2;
        public IOrchestrationSessionView SessionView => this;
        public IOrchestrationQueueView QueueView => this;

        public bool IsConnectedToServer => ResolveSession() != null && session.IsConnectedToMasterServer;
        public bool IsInRoom => ResolveSession() != null && session.IsInRoom;
        public bool IsMasterClient => ResolveSession() != null && session.IsMasterClient;
        public bool SessionStarted => ResolveSession() != null && session.SessionStarted;
        public string CurrentRoomName => ResolveSession() != null ? session.CurrentRoomName : "None";
        public string LocalUserId => PhotonNetwork.LocalPlayer != null ? $"Actor_{PhotonNetwork.LocalPlayer.ActorNumber}" : string.Empty;

        public string LocalPlayerDisplayText =>
            PhotonNetwork.LocalPlayer != null
                ? $"Local: {OrchestrationPlayerNameRegistry.ResolveDisplayName($"Actor_{PhotonNetwork.LocalPlayer.ActorNumber}", string.IsNullOrWhiteSpace(PhotonNetwork.LocalPlayer.NickName) ? OrchestrationPlayerNameRegistry.GetLocalPlayerName() : PhotonNetwork.LocalPlayer.NickName)} ({PhotonNetwork.LocalPlayer.ActorNumber}) [{(PhotonNetwork.IsMasterClient ? "MASTER" : "CLIENT")}]"
                : "Local: None";

        public IReadOnlyList<string> AvailableRooms =>
            ResolveSession() != null
                ? session.AvailableRooms
                    .Where(r => r != null && !r.RemovedFromList)
                    .Select(r => r.Name)
                    .ToList()
                : Array.Empty<string>();

        public IReadOnlyList<OrchestrationPlayerInfo> CurrentPlayers =>
            ResolveSession() != null
                ? session.CurrentPlayers
                    .Select(p => new OrchestrationPlayerInfo
                    {
                        UserId = $"Actor_{p.ActorNumber}",
                        DisplayName = OrchestrationPlayerNameRegistry.ResolveDisplayName(
                            $"Actor_{p.ActorNumber}",
                            string.IsNullOrWhiteSpace(p.NickName) ? $"User {p.ActorNumber}" : p.NickName),
                        NumericId = p.ActorNumber,
                        IsMasterClient = p.IsMasterClient
                    })
                    .ToList()
                : Array.Empty<OrchestrationPlayerInfo>();

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

        private void OnDisable()
        {
            UnbindEvents();
        }

        private SessionOrchestratorPUN2 ResolveSession()
        {
            session ??= SessionOrchestratorPUN2.Instance;
            if (session == null)
                session = FindFirstObjectByType<SessionOrchestratorPUN2>();
            return session;
        }

        private SessionQueueRuntimePUN2 ResolveQueue()
        {
            queue ??= SessionQueueRuntimePUN2.Instance;
            if (queue == null)
                queue = FindFirstObjectByType<SessionQueueRuntimePUN2>();
            return queue;
        }

        private void BindEvents()
        {
            if (ResolveSession() != null)
            {
                session.OnConnectedToServer -= HandleConnected;
                session.OnConnectedToServer += HandleConnected;

                session.OnConnectionFailed -= HandleConnectionFailed;
                session.OnConnectionFailed += HandleConnectionFailed;

                session.OnRoomCreated -= HandleRoomCreated;
                session.OnRoomCreated += HandleRoomCreated;

                session.OnRoomJoined -= HandleRoomJoined;
                session.OnRoomJoined += HandleRoomJoined;

                session.OnSessionStateChanged -= HandleSessionStateChanged;
                session.OnSessionStateChanged += HandleSessionStateChanged;

                session.OnRoomListUpdatedEvent -= HandleRoomListUpdated;
                session.OnRoomListUpdatedEvent += HandleRoomListUpdated;

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
                session.OnConnectedToServer -= HandleConnected;
                session.OnConnectionFailed -= HandleConnectionFailed;
                session.OnRoomCreated -= HandleRoomCreated;
                session.OnRoomJoined -= HandleRoomJoined;
                session.OnSessionStateChanged -= HandleSessionStateChanged;
                session.OnRoomListUpdatedEvent -= HandleRoomListUpdated;
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
        // The room list is only maintained while in the lobby; outside it the answer is not known.
        public RoomLookupResult LookupRoom(string roomName)
        {
            if (ResolveSession() == null || !PhotonNetwork.InLobby)
                return RoomLookupResult.Unavailable;

            return session.RoomExists(roomName) ? RoomLookupResult.Exists : RoomLookupResult.NotFound;
        }

        public void RequestAccessForLocalPlayer() => ResolveQueue()?.RequestAccessForLocalPlayer();
        public void ReleaseAccessForLocalPlayer() => ResolveQueue()?.ReleaseAccessForLocalPlayer();

        private void HandleConnected() => Connected?.Invoke();
        private void HandleConnectionFailed(string msg) => ConnectionFailed?.Invoke(msg);
        private void HandleRoomCreated(string room) => RoomCreated?.Invoke(room);
        private void HandleRoomJoined(string room) => RoomJoined?.Invoke(room);
        private void HandleSessionStateChanged(bool _) => SessionStateChanged?.Invoke();
        private void HandleRoomListUpdated(List<RoomInfo> _) => RoomsChanged?.Invoke();
        private void HandlePlayerListUpdated(List<Player> _) => PlayersChanged?.Invoke();
        private void HandleStatusMessage(string msg) => StatusMessage?.Invoke(msg);
        private void HandleQueueRuntimeUpdated()
        {
            EvaluationRunContext.Instance?.ObserveSessionRunId(SessionRunId);
            RuntimeStateChanged?.Invoke();
        }
    }
}
