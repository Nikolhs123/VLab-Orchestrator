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
        bool RoomExists(string roomName);
    }
}
