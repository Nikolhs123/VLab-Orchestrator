using UnityEngine;
using MirrorNetworkBehaviour = global::Mirror.NetworkBehaviour;
using MirrorNetworkConnectionToClient = global::Mirror.NetworkConnectionToClient;
using MirrorNetworkIdentity = global::Mirror.NetworkIdentity;

namespace Thesis.Orchestration.Backends.Mirror
{
    [DisallowMultipleComponent]
    [RequireComponent(typeof(MirrorNetworkIdentity))]
    public class MirrorOwnershipRelay : MirrorNetworkBehaviour
    {
        [SerializeField] private MirrorNetworkIdentity targetIdentity;
        [SerializeField] private bool verboseLogs = false;

        private void Awake()
        {
            targetIdentity ??= GetComponent<MirrorNetworkIdentity>();
        }

        public void RequestOwnershipForLocalClient()
        {
            if (isServer)
            {
                MirrorNetworkConnectionToClient localOwner = global::Mirror.NetworkServer.localConnection;
                if (localOwner != null)
                    ApplyOwnership(localOwner);

                return;
            }

            CmdRequestOwnership();
        }

        public void ReleaseOwnershipForLocalClient()
        {
            if (isServer)
            {
                ReleaseOwnershipInternal();
                return;
            }

            CmdReleaseOwnership();
        }

        [global::Mirror.Command(requiresAuthority = false)]
        private void CmdRequestOwnership(MirrorNetworkConnectionToClient sender = null)
        {
            if (sender == null || targetIdentity == null)
                return;

            ApplyOwnership(sender);
        }

        [global::Mirror.Command(requiresAuthority = false)]
        private void CmdReleaseOwnership(MirrorNetworkConnectionToClient sender = null)
        {
            if (sender == null || targetIdentity == null)
                return;

            if (targetIdentity.connectionToClient != sender)
                return;

            ReleaseOwnershipInternal();
        }

        private void ApplyOwnership(MirrorNetworkConnectionToClient newOwner)
        {
            if (targetIdentity == null || newOwner == null)
                return;

            if (targetIdentity.connectionToClient == newOwner)
                return;

            if (targetIdentity.connectionToClient != null &&
                targetIdentity.connectionToClient.identity != targetIdentity)
            {
                targetIdentity.RemoveClientAuthority();
            }

            targetIdentity.AssignClientAuthority(newOwner);
            Log($"Assigned ownership to connection {newOwner.connectionId}.");
        }

        private void ReleaseOwnershipInternal()
        {
            if (targetIdentity == null || targetIdentity.connectionToClient == null)
                return;

            if (targetIdentity.connectionToClient.identity == targetIdentity)
                return;

            targetIdentity.RemoveClientAuthority();
            Log("Released ownership.");
        }

        private void Log(string message)
        {
            if (!verboseLogs)
                return;

            Debug.Log($"[MirrorOwnershipRelay:{name}] {message}", this);
        }
    }
}
