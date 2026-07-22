using Thesis.Orchestration.SharedSync;
using Unity.Netcode;
using UnityEngine;

namespace Thesis.Orchestration.Backends.Netcode
{
    [RequireComponent(typeof(NetworkObject))]
    public class UserPresenceTransformSyncNetcode : NetworkBehaviour
    {
        [Header("Sync Settings")]
        [SerializeField] private bool syncPosition = true;
        [SerializeField] private bool syncRotation = true;

        [Header("Smoothing")]
        [SerializeField] private float positionLerpSpeed = 15f;
        [SerializeField] private float rotationLerpSpeed = 15f;

        private readonly NetworkVariable<Vector3> networkPosition = new(
            default,
            NetworkVariableReadPermission.Everyone,
            NetworkVariableWritePermission.Owner);

        private readonly NetworkVariable<Quaternion> networkRotation = new(
            Quaternion.identity,
            NetworkVariableReadPermission.Everyone,
            NetworkVariableWritePermission.Owner);

        public override void OnNetworkSpawn()
        {
            base.OnNetworkSpawn();

            if (IsOwner)
            {
                if (syncPosition)
                    networkPosition.Value = transform.position;

                if (syncRotation)
                    networkRotation.Value = transform.rotation;
            }
            else
            {
                TransformSyncUtilities.SnapRemoteTransform(
                    transform,
                    syncPosition,
                    syncRotation,
                    networkPosition.Value,
                    networkRotation.Value);
            }
        }

        private void Update()
        {
            if (!IsSpawned)
                return;

            if (IsOwner)
            {
                if (syncPosition)
                    networkPosition.Value = transform.position;

                if (syncRotation)
                    networkRotation.Value = transform.rotation;

                return;
            }

            TransformSyncUtilities.ApplyRemoteTransform(
                transform,
                syncPosition,
                syncRotation,
                networkPosition.Value,
                networkRotation.Value,
                positionLerpSpeed,
                rotationLerpSpeed);
        }
    }
}
