using Fusion;
using UnityEngine;

namespace Thesis.Orchestration.Backends.Fusion
{
    [RequireComponent(typeof(NetworkObject))]
    public class AvatarFollowAnchorFusion : NetworkBehaviour
    {
        [SerializeField] private Transform anchor;
        [SerializeField] private bool followPosition = true;
        [SerializeField] private bool followRotation = true;
        [SerializeField] private bool verboseLogs = false;

        public void SetAnchor(Transform newAnchor)
        {
            anchor = newAnchor;
            Log($"Anchor assigned: {(anchor != null ? anchor.name : "null")}");
        }

        private void Update()
        {
            if (!Object || !Object.IsValid)
                return;

            if (!Object.HasStateAuthority)
                return;

            if (anchor == null)
                return;

            if (followPosition)
                transform.position = anchor.position;

            if (followRotation)
                transform.rotation = anchor.rotation;
        }

        private void Log(string message)
        {
            if (!verboseLogs)
                return;

            Debug.Log($"[AvatarFollowAnchorFusion:{name}] {message}", this);
        }
    }
}