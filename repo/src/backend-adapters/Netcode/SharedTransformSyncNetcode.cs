using Thesis.Orchestration.SharedSync;
using Unity.Netcode;
using UnityEngine;

namespace Thesis.Orchestration.Backends.Netcode
{
    [RequireComponent(typeof(NetworkObject))]
    public class SharedTransformSyncNetcode : NetworkBehaviour
    {
        [Header("Sync Settings")]
        [SerializeField] private bool syncPosition = true;
        [SerializeField] private bool syncRotation = true;

        [Header("Smoothing")]
        [SerializeField] private float positionLerpSpeed = 15f;
        [SerializeField] private float rotationLerpSpeed = 15f;

        private NetworkObject authorityNetworkObject;

        private readonly NetworkVariable<Vector3> networkPosition = new(
            default,
            NetworkVariableReadPermission.Everyone,
            NetworkVariableWritePermission.Owner);

        private readonly NetworkVariable<Quaternion> networkRotation = new(
            Quaternion.identity,
            NetworkVariableReadPermission.Everyone,
            NetworkVariableWritePermission.Owner);

        private void Awake()
        {
            authorityNetworkObject = GetComponent<NetworkObject>();
            if (authorityNetworkObject == null)
                authorityNetworkObject = GetComponentInParent<NetworkObject>();
        }

        private bool HasAuthority()
        {
            return authorityNetworkObject != null &&
                   authorityNetworkObject.IsSpawned &&
                   authorityNetworkObject.IsOwner;
        }

        private bool IsUsable()
        {
            return authorityNetworkObject != null && authorityNetworkObject.IsSpawned;
        }

        private void OnEnable()
        {
            if (!IsUsable() || !HasAuthority())
                return;

            if (syncPosition)
                networkPosition.Value = transform.position;

            if (syncRotation)
                networkRotation.Value = transform.rotation;
        }

        private void Update()
        {
            if (!IsUsable())
                return;

            if (HasAuthority())
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
