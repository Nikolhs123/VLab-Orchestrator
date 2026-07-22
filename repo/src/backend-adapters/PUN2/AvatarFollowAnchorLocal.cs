using Photon.Pun;
using UnityEngine;

namespace Thesis.Orchestration.Backends.PUN2
{
    /// <summary>
    /// Makes the local player's spawned avatar follow a local scene anchor.
    /// Remote avatars do not run follow logic; they are driven by network sync.
    /// </summary>
    [RequireComponent(typeof(PhotonView))]
    public class AvatarFollowAnchorLocal : MonoBehaviourPun
    {
        [Header("Follow Settings")]
        [SerializeField] private Transform anchor;
        [SerializeField] private bool followPosition = true;
        [SerializeField] private bool followRotation = true;

        [Header("Debug")]
        [SerializeField] private bool verboseLogs = true;

        private bool warnedMissingAnchor;

        public void SetAnchor(Transform newAnchor)
        {
            anchor = newAnchor;
            warnedMissingAnchor = false;
            Log($"Anchor assigned: {(anchor != null ? anchor.name : "null")}");
        }

        private void Update()
        {
            if (!photonView.IsMine)
                return;

            if (anchor == null)
            {
                if (!warnedMissingAnchor)
                {
                    warnedMissingAnchor = true;
                    Log("No anchor assigned. Local avatar cannot follow anchor.");
                }
                return;
            }

            if (followPosition)
            {
                transform.position = anchor.position;
            }

            if (followRotation)
            {
                transform.rotation = anchor.rotation;
            }
        }

        private void Log(string message)
        {
            if (!verboseLogs)
                return;

            Debug.Log($"[AvatarFollowAnchorLocal:{name}] {message}", this);
        }
    }
}
