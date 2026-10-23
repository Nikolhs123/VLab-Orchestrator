using Thesis.Orchestration.Core;
using Thesis.Orchestration.Telemetry;
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
        [Tooltip("Seconds a client owner may differ from the logical holder before the server removes authority.")]
        [SerializeField] private float holderMismatchGraceSeconds = 1f;

        // Queue-controlled objects (those with a microscope access controller) accept native ownership
        // requests only from the current logical holder, identified by the command sender connection.
        // While the queue runtime is active, the server also removes client authority that has not
        // matched the holder for holderMismatchGraceSeconds; normally the former holder releases first.
        // Objects without client authority remain under server authority.
        private bool queueControlled;
        private float holderMismatchSince = -1f;

        private void Awake()
        {
            targetIdentity ??= GetComponent<MirrorNetworkIdentity>();
            queueControlled = GetComponent<MicroscopeAccessControllerMirror>() != null;
        }

        private void Update()
        {
            EnforceHolderOwnershipOnServer();
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

            SessionQueueRuntimeMirror queue = GetActiveQueue();
            if (queue != null &&
                !QueueRequestIdentity.IsHolder(ToUserId(newOwner), queue.CurrentHolderUserId, "MirrorOwnershipRequest", queue.CurrentInstrumentId))
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

        private void EnforceHolderOwnershipOnServer()
        {
            if (!isServer || targetIdentity == null)
                return;

            MirrorNetworkConnectionToClient owner = targetIdentity.connectionToClient;
            SessionQueueRuntimeMirror queue = GetActiveQueue();
            if (queue == null || owner == null || owner.identity == targetIdentity ||
                ToUserId(owner) == queue.CurrentHolderUserId)
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
            targetIdentity.RemoveClientAuthority();
            TelemetryLoggerCore.Instance?.LogEvent("NativeAuthorityRevokedByServer", ownerUserId, queue.CurrentInstrumentId,
                $"Mirror client authority of '{name}' removed: owner={ownerUserId} holder={queue.CurrentHolderUserId}.");
            Log($"Removed client authority from non-holder {ownerUserId}.");
        }

        private SessionQueueRuntimeMirror GetActiveQueue()
        {
            if (!queueControlled)
                return null;

            SessionQueueRuntimeMirror queue = SessionQueueRuntimeMirror.Instance;
            return queue != null && queue.QueueRuntimeActive ? queue : null;
        }

        private static string ToUserId(MirrorNetworkConnectionToClient connection)
        {
            return $"Client_{connection.connectionId}";
        }

        private void Log(string message)
        {
            if (!verboseLogs)
                return;

            Debug.Log($"[MirrorOwnershipRelay:{name}] {message}", this);
        }
    }
}
