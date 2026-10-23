using Thesis.Orchestration.Core;
using Thesis.Orchestration.Telemetry;
using Unity.Netcode;
using UnityEngine;

namespace Thesis.Orchestration.Backends.Netcode
{
    [DisallowMultipleComponent]
    [RequireComponent(typeof(NetworkObject))]
    public class NetcodeOwnershipRelay : NetworkBehaviour
    {
        [SerializeField] private NetworkObject targetNetworkObject;
        [SerializeField] private bool verboseLogs = true;
        [Tooltip("Seconds a client owner may differ from the logical holder before the server removes ownership.")]
        [SerializeField] private float holderMismatchGraceSeconds = 1f;

        // Queue-controlled objects (those with a microscope access controller) accept native ownership
        // requests only from the current logical holder, identified by the RPC sender on the server.
        // While the queue runtime is active, the server also removes client ownership that has not
        // matched the holder for holderMismatchGraceSeconds; normally the former holder releases first.
        // Server-owned (unowned) objects remain under host/server authority.
        private bool queueControlled;
        private float holderMismatchSince = -1f;

        private void Awake()
        {
            if (targetNetworkObject == null)
                targetNetworkObject = GetComponent<NetworkObject>();

            queueControlled = GetComponent<MicroscopeAccessControllerNetcode>() != null;
        }

        private void Update()
        {
            EnforceHolderOwnershipOnServer();
        }

        public void RequestOwnershipForLocalClient()
        {
            Unity.Netcode.NetworkManager manager = GetNetworkManager();
            if (manager == null || targetNetworkObject == null || !targetNetworkObject.IsSpawned)
                return;

            if (manager.IsServer)
            {
                GrantOwnership(manager.LocalClientId);
                return;
            }

            RequestOwnershipServerRpc();
        }

        public void ReleaseOwnershipForLocalClient()
        {
            Unity.Netcode.NetworkManager manager = GetNetworkManager();
            if (manager == null || targetNetworkObject == null || !targetNetworkObject.IsSpawned)
                return;

            if (manager.IsServer)
            {
                RevokeOwnership(manager.LocalClientId);
                return;
            }

            ReleaseOwnershipServerRpc();
        }

        // The requester is the RPC sender observed by the server, never a client-supplied ID.
        [ServerRpc(RequireOwnership = false)]
        private void RequestOwnershipServerRpc(ServerRpcParams rpcParams = default)
        {
            GrantOwnership(rpcParams.Receive.SenderClientId);
        }

        [ServerRpc(RequireOwnership = false)]
        private void ReleaseOwnershipServerRpc(ServerRpcParams rpcParams = default)
        {
            RevokeOwnership(rpcParams.Receive.SenderClientId);
        }

        private void GrantOwnership(ulong clientId)
        {
            if (targetNetworkObject == null || !targetNetworkObject.IsSpawned)
                return;

            if (targetNetworkObject.OwnerClientId == clientId)
            {
                Log($"Ownership request ignored because '{targetNetworkObject.name}' already belongs to Client_{clientId}.");
                return;
            }

            SessionQueueRuntimeNetcode queue = GetActiveQueue();
            if (queue != null &&
                !QueueRequestIdentity.IsHolder(ToUserId(clientId), queue.CurrentHolderUserId, "NetcodeOwnershipRequest", queue.CurrentInstrumentId))
                return;

            targetNetworkObject.ChangeOwnership(clientId);
            Log($"Granted ownership of '{targetNetworkObject.name}' to Client_{clientId}.");
        }

        private void RevokeOwnership(ulong requestingClientId)
        {
            if (targetNetworkObject == null || !targetNetworkObject.IsSpawned)
                return;

            if (targetNetworkObject.OwnerClientId != requestingClientId)
            {
                Log($"Ownership release ignored because requester Client_{requestingClientId} does not own '{targetNetworkObject.name}'.");
                return;
            }

            targetNetworkObject.RemoveOwnership();
            Log($"Released ownership of '{targetNetworkObject.name}' from Client_{requestingClientId}.");
        }

        private void EnforceHolderOwnershipOnServer()
        {
            Unity.Netcode.NetworkManager manager = GetNetworkManager();
            if (manager == null || !manager.IsServer || targetNetworkObject == null || !targetNetworkObject.IsSpawned)
                return;

            SessionQueueRuntimeNetcode queue = GetActiveQueue();
            ulong owner = targetNetworkObject.OwnerClientId;
            string ownerUserId = ToUserId(owner);
            if (queue == null || owner == Unity.Netcode.NetworkManager.ServerClientId ||
                ownerUserId == queue.CurrentHolderUserId)
            {
                holderMismatchSince = -1f;
                return;
            }

            if (holderMismatchSince < 0f)
                holderMismatchSince = Time.realtimeSinceStartup;
            if (Time.realtimeSinceStartup - holderMismatchSince < holderMismatchGraceSeconds)
                return;

            holderMismatchSince = -1f;
            targetNetworkObject.RemoveOwnership();
            TelemetryLoggerCore.Instance?.LogEvent("NativeAuthorityRevokedByServer", ownerUserId, queue.CurrentInstrumentId,
                $"Netcode ownership of '{targetNetworkObject.name}' removed: owner={ownerUserId} holder={queue.CurrentHolderUserId}.");
            Log($"Removed ownership of '{targetNetworkObject.name}' from non-holder {ownerUserId}.");
        }

        private SessionQueueRuntimeNetcode GetActiveQueue()
        {
            if (!queueControlled)
                return null;

            SessionQueueRuntimeNetcode queue = SessionQueueRuntimeNetcode.Instance;
            return queue != null && queue.QueueRuntimeActive ? queue : null;
        }

        private static string ToUserId(ulong clientId)
        {
            return $"Client_{clientId}";
        }

        private Unity.Netcode.NetworkManager GetNetworkManager()
        {
            if (targetNetworkObject != null && targetNetworkObject.NetworkManager != null)
                return targetNetworkObject.NetworkManager;

            if (NetworkManager != null)
                return NetworkManager;

            return Unity.Netcode.NetworkManager.Singleton;
        }

        private void Log(string message)
        {
            if (!verboseLogs)
                return;

            Debug.Log($"[NetcodeOwnershipRelay:{name}] {message}", this);
        }
    }
}
