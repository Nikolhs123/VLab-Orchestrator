using Fusion;
using Thesis.Orchestration.SharedSync;
using UnityEngine;

namespace Thesis.Orchestration.Backends.Fusion
{
    /// <summary>
    /// Fusion equivalent of SharedTransformSyncPUN2.
    ///
    /// Local state authority writes transform into [Networked] state.
    /// Remote peers smoothly interpolate toward the replicated transform.
    /// </summary>
    [RequireComponent(typeof(NetworkObject))]
    public class SharedTransformSyncFusion : NetworkBehaviour
    {
        [Header("Sync Settings")]
        [SerializeField] private bool syncPosition = true;
        [SerializeField] private bool syncRotation = true;

        [Header("Smoothing")]
        [SerializeField] private float positionLerpSpeed = 15f;
        [SerializeField] private float rotationLerpSpeed = 15f;

        [Header("Debug")]
        [SerializeField] private bool verboseLogs = false;

        [Networked] private Vector3 NetworkPosition { get; set; }
        [Networked] private Quaternion NetworkRotation { get; set; }

        public override void Spawned()
        {
            if (Object.HasStateAuthority)
            {
                NetworkPosition = transform.position;
                NetworkRotation = transform.rotation;
            }
            else
            {
                TransformSyncUtilities.SnapRemoteTransform(
                    transform,
                    syncPosition,
                    syncRotation,
                    NetworkPosition,
                    NetworkRotation);
            }
        }

        public override void FixedUpdateNetwork()
        {
            if (!Object || !Object.IsValid)
                return;

            if (!Object.HasStateAuthority)
                return;

            if (syncPosition)
                NetworkPosition = transform.position;

            if (syncRotation)
                NetworkRotation = transform.rotation;
        }

        public override void Render()
        {
            if (!Object || !Object.IsValid)
                return;

            if (Object.HasStateAuthority)
                return;

            TransformSyncUtilities.ApplyRemoteTransform(
                transform,
                syncPosition,
                syncRotation,
                NetworkPosition,
                NetworkRotation,
                positionLerpSpeed,
                rotationLerpSpeed);
        }

        private void Log(string message)
        {
            if (!verboseLogs)
                return;

            Debug.Log($"[SharedTransformSyncFusion:{name}] {message}", this);
        }
    }
}