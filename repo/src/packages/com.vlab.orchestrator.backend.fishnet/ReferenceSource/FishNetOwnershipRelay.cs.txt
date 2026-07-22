using FishNet.Connection;
using FishNet.Object;
using UnityEngine;

namespace Thesis.Orchestration.Backends.FishNet
{
    [DisallowMultipleComponent]
    [RequireComponent(typeof(NetworkObject))]
    public class FishNetOwnershipRelay : NetworkBehaviour
    {
        [SerializeField] private NetworkObject targetNetworkObject;
        [SerializeField] private bool verboseLogs = true;

        private void Awake()
        {
            targetNetworkObject ??= GetComponent<NetworkObject>();
        }

        public void RequestOwnershipForLocalClient()
        {
            if (!CanMutateOwnership())
                return;

            if (IsServerInitialized)
            {
                GrantOwnership(LocalConnection);
                return;
            }

            RequestOwnershipServerRpc();
        }

        public void ReleaseOwnershipForLocalClient()
        {
            if (!CanMutateOwnership())
                return;

            if (IsServerInitialized)
            {
                RevokeOwnership(LocalConnection);
                return;
            }

            ReleaseOwnershipServerRpc();
        }

        [ServerRpc(RequireOwnership = false)]
        private void RequestOwnershipServerRpc(NetworkConnection caller = null)
        {
            GrantOwnership(caller);
        }

        [ServerRpc(RequireOwnership = false)]
        private void ReleaseOwnershipServerRpc(NetworkConnection caller = null)
        {
            RevokeOwnership(caller);
        }

        private bool CanMutateOwnership()
        {
            return targetNetworkObject != null &&
                   targetNetworkObject.IsSpawned &&
                   NetworkManager != null &&
                   (IsServerInitialized || IsClientInitialized);
        }

        private void GrantOwnership(NetworkConnection requester)
        {
            if (targetNetworkObject == null || !targetNetworkObject.IsSpawned || requester == null || !requester.IsValid)
                return;

            if (targetNetworkObject.Owner == requester)
            {
                Log($"Ownership request ignored because '{targetNetworkObject.name}' already belongs to Client_{requester.ClientId}.");
                return;
            }

            targetNetworkObject.GiveOwnership(requester);
            Log($"Granted ownership of '{targetNetworkObject.name}' to Client_{requester.ClientId}.");
        }

        private void RevokeOwnership(NetworkConnection requester)
        {
            if (targetNetworkObject == null || !targetNetworkObject.IsSpawned || requester == null || !requester.IsValid)
                return;

            if (targetNetworkObject.Owner != requester)
            {
                Log($"Ownership release ignored because requester Client_{requester.ClientId} does not own '{targetNetworkObject.name}'.");
                return;
            }

            targetNetworkObject.RemoveOwnership();
            Log($"Released ownership of '{targetNetworkObject.name}' from Client_{requester.ClientId}.");
        }

        private void Log(string message)
        {
            if (!verboseLogs)
                return;

            Debug.Log($"[FishNetOwnershipRelay:{name}] {message}", this);
        }
    }
}
