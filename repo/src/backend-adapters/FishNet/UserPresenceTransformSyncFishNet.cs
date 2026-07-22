using FishNet.Component.Transforming;
using FishNet.Connection;
using FishNet.Object;
using Thesis.Orchestration.SharedSync;
using UnityEngine;

namespace Thesis.Orchestration.Backends.FishNet
{
    [DisallowMultipleComponent]
    [RequireComponent(typeof(NetworkObject))]
    [RequireComponent(typeof(NetworkTransform))]
    public class UserPresenceTransformSyncFishNet : NetworkBehaviour
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

        public override void OnOwnershipClient(NetworkConnection prevOwner)
        {
            base.OnOwnershipClient(prevOwner);
            TryAssignOwnerAnchor(forceSearch: true);
        }

        private void Update()
        {
            if (!IsOwner || Time.time < nextRetryTime)
                return;

            TryAssignOwnerAnchor(forceSearch: avatarAnchor == null);
        }

        private void TryAssignOwnerAnchor(bool forceSearch)
        {
            if (!IsOwner)
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

            Debug.Log($"[UserPresenceTransformSyncFishNet:{name}] {message}", this);
        }
    }
}
