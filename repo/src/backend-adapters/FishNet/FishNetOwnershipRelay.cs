using FishNet.Connection;
using FishNet.Object;
using Thesis.Orchestration.Core;
using Thesis.Orchestration.Telemetry;
using UnityEngine;

namespace Thesis.Orchestration.Backends.FishNet
{
    [DisallowMultipleComponent]
    [RequireComponent(typeof(NetworkObject))]
    public class FishNetOwnershipRelay : NetworkBehaviour
    {
        [SerializeField] private NetworkObject targetNetworkObject;
        [SerializeField] private bool verboseLogs = true;
        [Tooltip("Seconds a client owner may differ from the logical holder before the server removes ownership.")]
        [SerializeField] private float holderMismatchGraceSeconds = 1f;

        // Queue-controlled objects (those with a microscope access controller) accept native ownership
        // requests only from the current logical holder, identified by the ServerRpc caller connection.
        // While the queue runtime is active, the server also removes ownership that has not matched the
        // holder for holderMismatchGraceSeconds; normally the former holder releases first.
        // Unowned objects remain under server authority.
        private bool queueControlled;
        private float holderMismatchSince = -1f;

        private void Awake()
        {
            targetNetworkObject ??= GetComponent<NetworkObject>();
            queueControlled = GetComponent<MicroscopeAccessControllerFishNet>() != null;
        }

        private void Update()
        {
            EnforceHolderOwnershipOnServer();
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

            SessionQueueRuntimeFishNet queue = GetActiveQueue();
            if (queue != null &&
                !QueueRequestIdentity.IsHolder(ToUserId(requester), queue.CurrentHolderUserId, "FishNetOwnershipRequest", queue.CurrentInstrumentId))
                return;

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

        private void EnforceHolderOwnershipOnServer()
        {
            if (!IsServerInitialized || targetNetworkObject == null || !targetNetworkObject.IsSpawned)
                return;

            NetworkConnection owner = targetNetworkObject.Owner;
            SessionQueueRuntimeFishNet queue = GetActiveQueue();
            if (queue == null || owner == null || !owner.IsValid || ToUserId(owner) == queue.CurrentHolderUserId)
            {
                holderMismatchSince = -1f;
                return;
            }

            if (holderMismatchSince < 0f)
                holderMismatchSince = Time.realtimeSinceStartup;
            if (Time.realtimeSinceStartup - holderMismatchSince < holderMismatchGraceSeconds)
                return;

            holderMismatchSince = -1f;
            string ownerUserId = ToUserId(owner);
            targetNetworkObject.RemoveOwnership();
            TelemetryLoggerCore.Instance?.LogEvent("NativeAuthorityRevokedByServer", ownerUserId, queue.CurrentInstrumentId,
                $"FishNet ownership of '{targetNetworkObject.name}' removed: owner={ownerUserId} holder={queue.CurrentHolderUserId}.");
            Log($"Removed ownership of '{targetNetworkObject.name}' from non-holder {ownerUserId}.");
        }

        private SessionQueueRuntimeFishNet GetActiveQueue()
        {
            if (!queueControlled)
                return null;

            SessionQueueRuntimeFishNet queue = SessionQueueRuntimeFishNet.Instance;
            return queue != null && queue.QueueRuntimeActive ? queue : null;
        }

        private static string ToUserId(NetworkConnection connection)
        {
            return $"Client_{connection.ClientId}";
        }

        private void Log(string message)
        {
            if (!verboseLogs)
                return;

            Debug.Log($"[FishNetOwnershipRelay:{name}] {message}", this);
        }
    }
}
