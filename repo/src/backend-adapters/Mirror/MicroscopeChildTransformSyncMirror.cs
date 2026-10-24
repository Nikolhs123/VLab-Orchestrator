using System.Collections.Generic;
using Thesis.Orchestration.SharedSync;
using UnityEngine;
using MirrorNetworkBehaviour = global::Mirror.NetworkBehaviour;
using MirrorNetworkClient = global::Mirror.NetworkClient;
using MirrorNetworkIdentity = global::Mirror.NetworkIdentity;

namespace Thesis.Orchestration.Backends.Mirror
{
    [DisallowMultipleComponent]
    [RequireComponent(typeof(MirrorNetworkIdentity))]
    public class MicroscopeChildTransformSyncMirror : MirrorNetworkBehaviour
    {
        private const int MaxSnapshotLength = 4096;

        [SerializeField] private SharedSyncObjectDefinition sharedDefinition;
        [SerializeField] private MicroscopeAccessControllerMirror accessController;
        [SerializeField] private SessionQueueRuntimeMirror queueRuntime;
        [SerializeField] private bool verboseLogs = false;

        private readonly List<Transform> controlledChildren = new();
        private string lastSubmittedSnapshot = string.Empty;
        private string lastBroadcastSnapshot = string.Empty;
        private string lastAppliedSnapshot = string.Empty;

        private void Awake()
        {
            sharedDefinition ??= GetComponent<SharedSyncObjectDefinition>();
            accessController ??= GetComponent<MicroscopeAccessControllerMirror>();
            ResolveQueueRuntime();
            RefreshControlledChildren();
        }

        public override void OnStartClient()
        {
            base.OnStartClient();
            ResolveQueueRuntime();
            RefreshControlledChildren();
            SharedObjectSyncRegistry.ResolveOrCreate()?.DiscoverSceneObjects();

            // ClientRpcs are not buffered: a late-joining client requests the last authoritative snapshot.
            if (!isServer)
                CmdRequestSnapshot();
        }

        [global::Mirror.Command(requiresAuthority = false)]
        private void CmdRequestSnapshot(global::Mirror.NetworkConnectionToClient sender = null)
        {
            if (sender != null && !string.IsNullOrEmpty(lastBroadcastSnapshot))
                TargetApplySnapshot(sender, lastBroadcastSnapshot);
        }

        [global::Mirror.TargetRpc]
        private void TargetApplySnapshot(global::Mirror.NetworkConnectionToClient target, string snapshot)
        {
            ApplySnapshot(snapshot);
        }

        private void Update()
        {
            if (controlledChildren.Count == 0)
                RefreshControlledChildren();

            ResolveQueueRuntime();

            if (accessController == null || !accessController.HasLocalAccess)
                return;

            string snapshot = MicroscopeChildTransformSnapshotUtility.BuildSnapshot(controlledChildren);
            if (snapshot == lastSubmittedSnapshot)
                return;

            if (snapshot.Length > MaxSnapshotLength)
            {
                Log($"Skipped child snapshot write because payload length {snapshot.Length} exceeded {MaxSnapshotLength} characters.");
                return;
            }

            if (isServer)
            {
                if (!IsCurrentHolder(GetLocalUserId()))
                    return;

                ApplyAuthoritativeSnapshot(snapshot);
                return;
            }

            CmdSubmitSnapshot(snapshot);
            lastSubmittedSnapshot = snapshot;
        }

        [global::Mirror.Command(requiresAuthority = false)]
        private void CmdSubmitSnapshot(string snapshot, global::Mirror.NetworkConnectionToClient sender = null)
        {
            if (!isServer)
                return;

            if (string.IsNullOrWhiteSpace(snapshot) || snapshot.Length > MaxSnapshotLength)
                return;

            string sourceUserId = sender != null ? $"Client_{sender.connectionId}" : string.Empty;
            if (!IsCurrentHolder(sourceUserId))
            {
                Log($"Ignored child snapshot from non-holder '{sourceUserId}'.");
                return;
            }

            ApplyAuthoritativeSnapshot(snapshot);
        }

        [global::Mirror.ClientRpc]
        private void RpcApplyReplicatedSnapshot(string snapshot)
        {
            // The logical holder is the writer of this state. The broadcast also reaches the submitting
            // client, delayed by the round trip; applying that echo moves the holder's transform back to an
            // older value, which Update() then submits again as if it were new. Observed in the revision
            // campaign: with unlucky frame timing the holder's rotation stayed at its start value for the
            // whole phase (Mirror S1/S4). The holder never applies remote snapshots of its own controlled children.
            if (HoldsLocalAuthority())
                return;

            ApplySnapshot(snapshot);
        }

        private bool HoldsLocalAuthority()
        {
            return accessController != null && accessController.HasLocalAccess;
        }

        private void ApplyAuthoritativeSnapshot(string snapshot)
        {
            if (snapshot == lastBroadcastSnapshot)
                return;

            lastBroadcastSnapshot = snapshot;
            lastSubmittedSnapshot = snapshot;
            ApplySnapshot(snapshot);
            RpcApplyReplicatedSnapshot(snapshot);
        }

        private void ApplySnapshot(string snapshot)
        {
            if (snapshot == lastAppliedSnapshot)
                return;

            MicroscopeChildTransformSnapshotUtility.ApplySnapshot(snapshot, controlledChildren);
            lastAppliedSnapshot = snapshot;
        }

        private void RefreshControlledChildren()
        {
            MicroscopeChildTransformSnapshotUtility.RefreshControlledChildTransforms(sharedDefinition, controlledChildren);
        }

        private void ResolveQueueRuntime()
        {
            if (queueRuntime == null || !queueRuntime.IsSpawned)
                queueRuntime = SessionQueueRuntimeMirror.Instance;

            if (queueRuntime == null || !queueRuntime.IsSpawned)
                queueRuntime = FindFirstObjectByType<SessionQueueRuntimeMirror>();
        }

        private bool IsCurrentHolder(string userId)
        {
            if (queueRuntime == null || string.IsNullOrWhiteSpace(userId))
                return false;

            return string.Equals(queueRuntime.CurrentHolderUserId, userId, System.StringComparison.Ordinal);
        }

        private string GetLocalUserId()
        {
            if (queueRuntime != null)
                return queueRuntime.GetLocalUserId();

            SessionOrchestratorMirror orchestrator = SessionOrchestratorMirror.Instance;
            if (orchestrator != null)
                return orchestrator.GetLocalUserId();

            return "UnknownUser";
        }

        private void Log(string message)
        {
            if (!verboseLogs)
                return;

            Debug.Log($"[MicroscopeChildTransformSyncMirror:{name}] {message}", this);
        }
    }
}
