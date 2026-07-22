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

        private void Awake()
        {
            if (targetNetworkObject == null)
                targetNetworkObject = GetComponent<NetworkObject>();
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

            RequestOwnershipServerRpc(manager.LocalClientId);
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

            ReleaseOwnershipServerRpc(manager.LocalClientId);
        }

        [ServerRpc(RequireOwnership = false)]
        private void RequestOwnershipServerRpc(ulong requestingClientId)
        {
            GrantOwnership(requestingClientId);
        }

        [ServerRpc(RequireOwnership = false)]
        private void ReleaseOwnershipServerRpc(ulong requestingClientId)
        {
            RevokeOwnership(requestingClientId);
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
