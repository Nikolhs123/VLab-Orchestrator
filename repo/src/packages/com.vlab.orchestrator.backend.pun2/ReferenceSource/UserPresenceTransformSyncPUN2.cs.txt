using Photon.Pun;
using UnityEngine;

namespace Thesis.Orchestration.Backends.PUN2
{
    /// <summary>
    /// Simple transform synchronization for user-owned objects such as
    /// avatar root, head, left hand, and right hand.
    ///
    /// Local owner writes transform data.
    /// Remote clients interpolate smoothly toward the received target transform.
    /// </summary>
    [RequireComponent(typeof(PhotonView))]
    public class UserPresenceTransformSyncPUN2 : MonoBehaviourPun, IPunObservable
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

            if (syncPosition)
            {
                transform.position = Vector3.Lerp(
                    transform.position,
                    targetPosition,
                    Time.deltaTime * positionLerpSpeed);
            }

            if (syncRotation)
            {
                transform.rotation = Quaternion.Slerp(
                    transform.rotation,
                    targetRotation,
                    Time.deltaTime * rotationLerpSpeed);
            }
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
