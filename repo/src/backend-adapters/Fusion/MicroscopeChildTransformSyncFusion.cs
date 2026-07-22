using System.Collections.Generic;
using Fusion;
using Thesis.Orchestration.SharedSync;
using UnityEngine;

namespace Thesis.Orchestration.Backends.Fusion
{
    [DisallowMultipleComponent]
    [RequireComponent(typeof(NetworkObject))]
    public class MicroscopeChildTransformSyncFusion : NetworkBehaviour
    {
        private const int SnapshotChunkSize = 512;
        private const int SnapshotChunkCount = 8;
        private const int MaxSnapshotLength = SnapshotChunkSize * SnapshotChunkCount;

        [SerializeField] private SharedSyncObjectDefinition sharedDefinition;
        [SerializeField] private MicroscopeAccessControllerFusion accessController;
        [SerializeField] private bool verboseLogs = false;

        [Networked] private NetworkString<_512> ReplicatedSnapshotChunk0 { get; set; }
        [Networked] private NetworkString<_512> ReplicatedSnapshotChunk1 { get; set; }
        [Networked] private NetworkString<_512> ReplicatedSnapshotChunk2 { get; set; }
        [Networked] private NetworkString<_512> ReplicatedSnapshotChunk3 { get; set; }
        [Networked] private NetworkString<_512> ReplicatedSnapshotChunk4 { get; set; }
        [Networked] private NetworkString<_512> ReplicatedSnapshotChunk5 { get; set; }
        [Networked] private NetworkString<_512> ReplicatedSnapshotChunk6 { get; set; }
        [Networked] private NetworkString<_512> ReplicatedSnapshotChunk7 { get; set; }

        private readonly List<Transform> controlledChildren = new();
        private string lastWrittenSnapshot = string.Empty;
        private string lastAppliedSnapshot = string.Empty;

        private void Awake()
        {
            sharedDefinition ??= GetComponent<SharedSyncObjectDefinition>();
            accessController ??= GetComponent<MicroscopeAccessControllerFusion>();
            RefreshControlledChildren();
        }

        public override void Spawned()
        {
            RefreshControlledChildren();
            ApplySnapshot(SafeReadSnapshot(), force: true);
        }

        public override void FixedUpdateNetwork()
        {
            if (!Object || !Object.IsValid)
                return;

            if (controlledChildren.Count == 0)
                RefreshControlledChildren();

            if (accessController == null || !accessController.HasLocalAccess)
                return;

            string snapshot = MicroscopeChildTransformSnapshotUtility.BuildSnapshot(controlledChildren);
            if (snapshot == lastWrittenSnapshot)
                return;

            if (snapshot.Length > MaxSnapshotLength)
            {
                Log($"Skipped child snapshot write because payload length {snapshot.Length} exceeded {MaxSnapshotLength}.");
                return;
            }

            if (Object.HasStateAuthority)
            {
                ApplyAuthoritativeSnapshot(snapshot);
                return;
            }

            RPC_SubmitSnapshot(snapshot);
            lastWrittenSnapshot = snapshot;
        }

        public override void Render()
        {
            if (!Object || !Object.IsValid || Object.HasStateAuthority)
                return;

            ApplySnapshot(SafeReadSnapshot());
        }

        private void ApplySnapshot(string snapshot, bool force = false)
        {
            if (!force && snapshot == lastAppliedSnapshot)
                return;

            MicroscopeChildTransformSnapshotUtility.ApplySnapshot(snapshot, controlledChildren);
            lastAppliedSnapshot = snapshot;
        }

        [Rpc(RpcSources.All, RpcTargets.StateAuthority)]
        private void RPC_SubmitSnapshot(string snapshot, RpcInfo info = default)
        {
            if (!Object || !Object.IsValid || !Object.HasStateAuthority)
                return;

            if (string.IsNullOrWhiteSpace(snapshot) || snapshot.Length > MaxSnapshotLength)
                return;

            string sourceUserId = $"Player_{info.Source.RawEncoded}";
            string currentHolderUserId = accessController != null
                ? accessController.GetCurrentHolderUserIdForSync()
                : string.Empty;

            if (!string.Equals(currentHolderUserId, sourceUserId, System.StringComparison.Ordinal))
            {
                Log($"Ignored child snapshot from non-holder '{sourceUserId}'. Current holder is '{currentHolderUserId}'.");
                return;
            }

            ApplyAuthoritativeSnapshot(snapshot);
        }

        private void ApplyAuthoritativeSnapshot(string snapshot)
        {
            ApplySnapshot(snapshot, force: true);
            WriteReplicatedSnapshot(snapshot);
            lastWrittenSnapshot = snapshot;
        }

        private void RefreshControlledChildren()
        {
            MicroscopeChildTransformSnapshotUtility.RefreshControlledChildTransforms(sharedDefinition, controlledChildren);
        }

        private string SafeReadSnapshot()
        {
            try
            {
                return string.Concat(
                    ReplicatedSnapshotChunk0.ToString(),
                    ReplicatedSnapshotChunk1.ToString(),
                    ReplicatedSnapshotChunk2.ToString(),
                    ReplicatedSnapshotChunk3.ToString(),
                    ReplicatedSnapshotChunk4.ToString(),
                    ReplicatedSnapshotChunk5.ToString(),
                    ReplicatedSnapshotChunk6.ToString(),
                    ReplicatedSnapshotChunk7.ToString());
            }
            catch (System.Exception ex)
            {
                Log($"Ignored invalid child snapshot payload: {ex.Message}");
                return string.Empty;
            }
        }

        private void WriteReplicatedSnapshot(string snapshot)
        {
            ReplicatedSnapshotChunk0 = GetChunk(snapshot, 0);
            ReplicatedSnapshotChunk1 = GetChunk(snapshot, 1);
            ReplicatedSnapshotChunk2 = GetChunk(snapshot, 2);
            ReplicatedSnapshotChunk3 = GetChunk(snapshot, 3);
            ReplicatedSnapshotChunk4 = GetChunk(snapshot, 4);
            ReplicatedSnapshotChunk5 = GetChunk(snapshot, 5);
            ReplicatedSnapshotChunk6 = GetChunk(snapshot, 6);
            ReplicatedSnapshotChunk7 = GetChunk(snapshot, 7);
        }

        private static string GetChunk(string snapshot, int chunkIndex)
        {
            if (string.IsNullOrEmpty(snapshot))
                return string.Empty;

            int start = chunkIndex * SnapshotChunkSize;
            if (start >= snapshot.Length)
                return string.Empty;

            int length = Mathf.Min(SnapshotChunkSize, snapshot.Length - start);
            return snapshot.Substring(start, length);
        }

        private void Log(string message)
        {
            if (!verboseLogs)
                return;

            Debug.Log($"[MicroscopeChildTransformSyncFusion:{name}] {message}", this);
        }
    }
}
