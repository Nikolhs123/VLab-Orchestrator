using System.Collections.Generic;
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
    ///
    /// Handoff: PUN streams are sampled at the serialization rate and stop when ownership moves, so the owner's
    /// last changes (up to one serialization interval) never reach the other replicas, and the next owner
    /// would continue from that stale state. The owner therefore sends its exact final state once, reliably,
    /// before it releases access (<see cref="FlushOwnedFinalState"/>), and replicas snap to it. While a replica
    /// is the owner its interpolation target follows its own transform, so it is not pulled back toward an old
    /// target the moment it loses ownership.
    /// </summary>
    [RequireComponent(typeof(PhotonView))]
    public class SharedTransformSyncPUN2 : MonoBehaviourPun, IPunObservable
    {
        private static readonly HashSet<SharedTransformSyncPUN2> Instances = new();

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

        private void OnEnable()
        {
            Instances.Add(this);
        }

        private void OnDisable()
        {
            Instances.Remove(this);
        }

        private void Update()
        {
            if (photonView.IsMine)
            {
                targetPosition = transform.position;
                targetRotation = transform.rotation;
                return;
            }

            TransformSyncUtilities.ApplyRemoteTransform(
                transform,
                syncPosition,
                syncRotation,
                targetPosition,
                targetRotation,
                positionLerpSpeed,
                rotationLerpSpeed);
        }

        /// <summary>
        /// Sends the exact current transform of every synchronized object this client owns to all other
        /// clients. Called before the local player releases access.
        /// </summary>
        public static void FlushOwnedFinalState()
        {
            if (!PhotonNetwork.InRoom)
                return;

            foreach (SharedTransformSyncPUN2 sync in Instances)
            {
                if (sync == null || sync.photonView == null || !sync.photonView.IsMine || sync.photonView.ViewID == 0)
                    continue;

                sync.photonView.RPC(
                    nameof(RPC_ApplyFinalState),
                    RpcTarget.Others,
                    sync.transform.position,
                    sync.transform.rotation);
            }
        }

        [PunRPC]
        private void RPC_ApplyFinalState(Vector3 position, Quaternion rotation)
        {
            // A client that already owns the object has its own, newer state.
            if (photonView.IsMine)
                return;

            targetPosition = position;
            targetRotation = rotation;

            if (syncPosition)
                transform.position = position;

            if (syncRotation)
                transform.rotation = rotation;
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
