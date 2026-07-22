using Photon.Pun;
using Thesis.Orchestration.SharedSync;
using UnityEngine;

namespace Thesis.Orchestration.Backends.PUN2
{
    /// <summary>
    /// Simple transform sync for shared objects.
    ///
    /// The current owner writes transform data.
    /// Remote clients interpolate toward the received target.
    /// </summary>
    [RequireComponent(typeof(PhotonView))]
    public class SharedTransformSyncPUN2 : MonoBehaviourPun, IPunObservable
    {
        [Header("Sync Settings")]
        [SerializeField] private bool syncPosition = true;
        [SerializeField] private bool syncRotation = true;

        [Header("Smoothing")]
        [SerializeField] private float positionLerpSpeed = 15f;
        [SerializeField] private float rotationLerpSpeed = 15f;

        private Vector3 targetPosition;
        private Quaternion targetRotation;

        private void Awake()
        {
            targetPosition = transform.position;
            targetRotation = transform.rotation;
        }

        private void Update()
        {
            if (photonView.IsMine)
                return;

            TransformSyncUtilities.ApplyRemoteTransform(
                transform,
                syncPosition,
                syncRotation,
                targetPosition,
                targetRotation,
                positionLerpSpeed,
                rotationLerpSpeed);
        }

        public void OnPhotonSerializeView(PhotonStream stream, PhotonMessageInfo info)
        {
            if (stream.IsWriting)
            {
                if (syncPosition)
                    stream.SendNext(transform.position);

                if (syncRotation)
                    stream.SendNext(transform.rotation);
            }
            else
            {
                if (syncPosition)
                    targetPosition = (Vector3)stream.ReceiveNext();

                if (syncRotation)
                    targetRotation = (Quaternion)stream.ReceiveNext();
            }
        }
    }
}