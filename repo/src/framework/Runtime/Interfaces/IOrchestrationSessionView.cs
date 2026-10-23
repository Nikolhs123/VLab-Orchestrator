using System;
using System.Collections.Generic;

namespace Thesis.Orchestration.Runtime
{
    [Serializable]
    public class OrchestrationPlayerInfo
    {
        public string UserId;
        public string DisplayName;
        public int NumericId;
        public bool IsMasterClient;
    }

    // Result of asking a backend whether a named room exists. A boolean conflated "not found" with "this
    // backend cannot answer": direct-connect adapters (Netcode, Mirror, FishNet) have no room discovery, the
    // room name selects the address/port, and a join simply attempts the connection.
    public enum RoomLookupResult
    {
        Exists,
        NotFound,          // discovery is supported and the current list does not contain the room
        Unavailable,       // discovery is supported but no list is available now (e.g. not in the lobby)
        Unsupported        // the backend has no room discovery
    }

    public interface IOrchestrationSessionView
    {
        bool IsConnectedToServer { get; }
        bool IsInRoom { get; }
        bool IsMasterClient { get; }
        bool SessionStarted { get; }
        string CurrentRoomName { get; }
        string LocalUserId { get; }
        string LocalPlayerDisplayText { get; }

        IReadOnlyList<string> AvailableRooms { get; }
        IReadOnlyList<OrchestrationPlayerInfo> CurrentPlayers { get; }

        event Action Connected;
        event Action<string> ConnectionFailed;
        event Action<string> RoomCreated;
        event Action<string> RoomJoined;
        event Action SessionStateChanged;
        event Action RoomsChanged;
        event Action PlayersChanged;
        event Action<string> StatusMessage;

        void Connect();
        void CreateRoom(string roomName);
        void JoinRoom(string roomName);
        void LeaveRoom();
        void BeginSession();
        RoomLookupResult LookupRoom(string roomName);
    }
}
