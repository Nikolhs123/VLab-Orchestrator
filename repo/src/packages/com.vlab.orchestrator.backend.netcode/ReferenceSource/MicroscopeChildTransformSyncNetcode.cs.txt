using System.Collections.Generic;
using Thesis.Orchestration.SharedSync;
using Unity.Collections;
using Unity.Netcode;
using UnityEngine;

namespace Thesis.Orchestration.Backends.Netcode
{
    [DisallowMultipleComponent]
    [RequireComponent(typeof(NetworkObject))]
    public class MicroscopeChildTransformSyncNetcode : NetworkBehaviour
    {
        private const int MaxSnapshotLength = 4096;

        [SerializeField] private SharedSyncObjectDefinition sharedDefinition;
        [SerializeField] private MicroscopeAccessControllerNetcode accessController;
        [SerializeField] private bool verboseLogs = false;

        private readonly NetworkVariable<FixedString4096Bytes> replicatedSnapshot = new(
            default,
            NetworkVariableReadPermission.Everyone,
            NetworkVariableWritePermission.Owner);

        private readonly List<Transform> controlledChildren = new();
        private string lastWrittenSnapshot = string.Empty;
        private string lastAppliedSnapshot = string.Empty;
        private string lastObservedSnapshot = string.Empty;

        private void Awake()
        {
            sharedDefinition ??= GetComponent<SharedSyncObjectDefinition>();
            accessController ??= GetComponent<MicroscopeAccessControllerNetcode>();
            RefreshControlledChildren();
        }

        public override void OnNetworkSpawn()
        {
            base.OnNetworkSpawn();
            RefreshControlledChildren();
            ApplySnapshot(replicatedSnapshot.Value.ToString(), force: true);
        }

        private void Update()
        {
            if (!IsSpawned)
                return;

            if (controlledChildren.Count == 0)
                RefreshControlledChildren();

            if (IsOwner && accessController != null && accessController.HasLocalAccess)
            {
                string snapshot = MicroscopeChildTransformSnapshotUtility.BuildSnapshot(controlledChildren);
                if (snapshot == lastWrittenSnapshot)
                    return;

                if (snapshot.Length > MaxSnapshotLength)
                {
                    Log($"Skipped child snapshot write because payload length {snapshot.Length} exceeded 4096 characters.");
                    return;
                }

                replicatedSnapshot.Value = new FixedString4096Bytes(snapshot);
                lastWrittenSnapshot = snapshot;
                return;
            }

            string observedSnapshot = replicatedSnapshot.Value.ToString();
            if (observedSnapshot == lastObservedSnapshot)
                return;

            lastObservedSnapshot = observedSnapshot;
            ApplySnapshot(observedSnapshot);
        }

        private void ApplySnapshot(string snapshot, bool force = false)
        {
            if (!force && snapshot == lastAppliedSnapshot)
                return;

            MicroscopeChildTransformSnapshotUtility.ApplySnapshot(snapshot, controlledChildren);
            lastAppliedSnapshot = snapshot;
        }

        private void RefreshControlledChildren()
        {
            MicroscopeChildTransformSnapshotUtility.RefreshControlledChildTransforms(sharedDefinition, controlledChildren);
        }

        private void Log(string message)
        {
            if (!verboseLogs)
                return;

            Debug.Log($"[MicroscopeChildTransformSyncNetcode:{name}] {message}", this);
        }
    }
}
