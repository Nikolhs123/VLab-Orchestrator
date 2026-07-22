using Thesis.Orchestration.SharedSync;
using UnityEngine;
using MirrorNetworkBehaviour = global::Mirror.NetworkBehaviour;
using MirrorNetworkIdentity = global::Mirror.NetworkIdentity;
using MirrorNetworkTransformUnreliable = global::Mirror.NetworkTransformUnreliable;

namespace Thesis.Orchestration.Backends.Mirror
{
    [DisallowMultipleComponent]
    [RequireComponent(typeof(MirrorNetworkIdentity))]
    [RequireComponent(typeof(MirrorNetworkTransformUnreliable))]
    public class UserPresenceTransformSyncMirror : MirrorNetworkBehaviour
    {
        [SerializeField] private string avatarAnchorPath = "Ego/Camera/AvatarAnchor";
        [SerializeField] private float retryDelaySeconds = 0.5f;
        [SerializeField] private bool verboseLogs = false;

        private Transform avatarAnchor;
        private float nextRetryTime;

        public override void OnStartClient()
        {
            base.OnStartClient();
            TryAssignOwnerAnchor(forceSearch: true);
        }

        public override void OnStartAuthority()
        {
            base.OnStartAuthority();
            TryAssignOwnerAnchor(forceSearch: true);
        }

        private void Update()
        {
            if (!isOwned || Time.time < nextRetryTime)
                return;

            TryAssignOwnerAnchor(forceSearch: avatarAnchor == null);
        }

        private void TryAssignOwnerAnchor(bool forceSearch)
        {
            if (!isOwned)
                return;

            if (forceSearch || avatarAnchor == null)
            {
                GameObject found = GameObject.Find(avatarAnchorPath);
                avatarAnchor = found != null ? found.transform : null;
            }

            if (avatarAnchor == null)
            {
                nextRetryTime = Time.time + retryDelaySeconds;
                return;
            }

            if (SharedAvatarRuntimeUtility.TryAssignAnchor(gameObject, avatarAnchor))
            {
                nextRetryTime = Time.time + retryDelaySeconds;
                Log($"Assigned owner avatar anchor '{avatarAnchor.name}'.");
                return;
            }

            nextRetryTime = Time.time + retryDelaySeconds;
        }

        private void Log(string message)
        {
            if (!verboseLogs)
                return;

            Debug.Log($"[UserPresenceTransformSyncMirror:{name}] {message}", this);
        }
    }
}
