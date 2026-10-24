using System.Collections.Generic;
using FishNet.Connection;
using FishNet.Object;
using Thesis.Orchestration.SharedSync;
using UnityEngine;

namespace Thesis.Orchestration.Backends.FishNet
{
    [DisallowMultipleComponent]
    [RequireComponent(typeof(NetworkObject))]
    public class MicroscopeChildTransformSyncFishNet : NetworkBehaviour
    {
        private const int MaxSnapshotLength = 4096;

        [SerializeField] private SharedSyncObjectDefinition sharedDefinition;
        [SerializeField] private MicroscopeAccessControllerFishNet accessController;
        [SerializeField] private SessionQueueRuntimeFishNet queueRuntime;
        [SerializeField] private bool verboseLogs = false;

        private readonly List<Transform> controlledChildren = new();
        private string lastSubmittedSnapshot = string.Empty;
        private string lastBroadcastSnapshot = string.Empty;
        private string lastAppliedSnapshot = string.Empty;

        private void Awake()
        {
            sharedDefinition ??= GetComponent<SharedSyncObjectDefinition>();
            accessController ??= GetComponent<MicroscopeAccessControllerFishNet>();
            ResolveQueueRuntime();
            RefreshControlledChildren();
        }

        public override void OnStartNetwork()
        {
            base.OnStartNetwork();
            ResolveQueueRuntime();
            RefreshControlledChildren();
        }

        public override void OnStartClient()
        {
            base.OnStartClient();
            RefreshControlledChildren();
            SharedObjectSyncRegistry.ResolveOrCreate()?.DiscoverSceneObjects();
        }

        private void Update()
        {
            if (!IsSpawned)
                return;

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

            if (IsServerInitialized)
            {
                if (!IsCurrentHolder(GetLocalUserId()))
                    return;

                ApplyAuthoritativeSnapshot(snapshot);
                return;
            }

            SubmitSnapshotServerRpc(snapshot);
            lastSubmittedSnapshot = snapshot;
        }

        [ServerRpc(RequireOwnership = false)]
        private void SubmitSnapshotServerRpc(string snapshot, NetworkConnection caller = null)
        {
            if (!IsServerInitialized)
                return;

            if (string.IsNullOrWhiteSpace(snapshot) || snapshot.Length > MaxSnapshotLength)
                return;

            string sourceUserId = caller != null && caller.IsValid
                ? $"Client_{caller.ClientId}"
                : string.Empty;

            if (!IsCurrentHolder(sourceUserId))
            {
                Log($"Ignored child snapshot from non-holder '{sourceUserId}'.");
                return;
            }

            ApplyAuthoritativeSnapshot(snapshot);
        }

        [ObserversRpc(BufferLast = true, RunLocally = true)]
        private void RpcApplyReplicatedSnapshot(string snapshot)
        {
            // The logical holder is the writer; the broadcast also reaches the submitting client after a round
            // trip and would move the holder's transform back to an older value (see the Mirror adapter, where
            // this stalled the holder's rotation). The holder never applies remote snapshots of its own children.
            if (accessController != null && accessController.HasLocalAccess)
                return;

            ApplySnapshot(snapshot);
        }

        private void ApplyAuthoritativeSnapshot(string snapshot)
        {
            if (snapshot == lastBroadcastSnapshot)
                return;

            lastBroadcastSnapshot = snapshot;
            lastSubmittedSnapshot = snapshot;
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
                queueRuntime = SessionQueueRuntimeFishNet.Instance;

            if (queueRuntime == null || !queueRuntime.IsSpawned)
                queueRuntime = FindFirstObjectByType<SessionQueueRuntimeFishNet>();
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

            SessionOrchestratorFishNet orchestrator = SessionOrchestratorFishNet.Instance;
            if (orchestrator != null)
                return orchestrator.GetLocalUserId();

            return NetworkManager != null &&
                   NetworkManager.ClientManager != null &&
                   NetworkManager.ClientManager.Connection.IsValid
                ? $"Client_{NetworkManager.ClientManager.Connection.ClientId}"
                : "UnknownUser";
        }

        private void Log(string message)
        {
            if (!verboseLogs)
                return;

            Debug.Log($"[MicroscopeChildTransformSyncFishNet:{name}] {message}", this);
        }
    }
}
