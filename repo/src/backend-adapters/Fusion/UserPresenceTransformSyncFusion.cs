using Fusion;
using UnityEngine;

namespace Thesis.Orchestration.Backends.Fusion
{
    /// <summary>
    /// Fusion equivalent of UserPresenceTransformSyncPUN2.
    ///
    /// Intended for user-owned objects such as:
    /// - avatar root
    /// - head
    /// - left hand
    /// - right hand
    ///
    /// Local state authority writes transform data.
    /// Remote peers interpolate smoothly toward the replicated transform.
    /// </summary>
    [RequireComponent(typeof(NetworkObject))]
    public class UserPresenceTransformSyncFusion : NetworkBehaviour
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
                if (syncPosition)
                    NetworkPosition = transform.position;

                if (syncRotation)
                    NetworkRotation = transform.rotation;
            }
            else
            {
                if (syncPosition)
                    transform.position = NetworkPosition;

                if (syncRotation)
                    transform.rotation = NetworkRotation;
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

            if (syncPosition)
            {
                transform.position = Vector3.Lerp(
                    transform.position,
                    NetworkPosition,
                    Time.deltaTime * positionLerpSpeed);
            }

            if (syncRotation)
            {
                transform.rotation = Quaternion.Slerp(
                    transform.rotation,
                    NetworkRotation,
                    Time.deltaTime * rotationLerpSpeed);
            }
        }

        private void Log(string message)
        {
            if (!verboseLogs)
                return;

            Debug.Log($"[UserPresenceTransformSyncFusion:{name}] {message}", this);
        }
    }
}