using Unity.Netcode;
using UnityEngine;

namespace Thesis.Orchestration.Backends.Netcode
{
    [RequireComponent(typeof(NetworkObject))]
    public class AvatarFollowAnchorNetcode : NetworkBehaviour
    {
        [SerializeField] private Transform anchor;
        [SerializeField] private string avatarAnchorPath = "Ego/Camera/AvatarAnchor";
        [SerializeField] private bool followPosition = true;
        [SerializeField] private bool followRotation = true;
        [SerializeField] private bool verboseLogs = false;

        private bool warnedMissingAnchor;

        public override void OnNetworkSpawn()
        {
            base.OnNetworkSpawn();

            if (IsOwner)
                ResolveAnchor();
        }

        public void SetAnchor(Transform newAnchor)
        {
            anchor = newAnchor;
            warnedMissingAnchor = false;
            Log($"Anchor assigned: {(anchor != null ? anchor.name : "null")}");
        }

        private void Update()
        {
            if (!IsSpawned || !IsOwner)
                return;

            if (anchor == null && !ResolveAnchor())
                return;

            if (anchor == null)
                return;

            if (followPosition)
                transform.position = anchor.position;

            if (followRotation)
                transform.rotation = anchor.rotation;
        }

        private bool ResolveAnchor()
        {
            if (anchor != null)
                return true;

            if (string.IsNullOrWhiteSpace(avatarAnchorPath))
                return false;

            GameObject found = GameObject.Find(avatarAnchorPath);
            if (found != null)
            {
                anchor = found.transform;
                warnedMissingAnchor = false;
                Log($"Resolved anchor from path '{avatarAnchorPath}'.");
                return true;
            }

            if (!warnedMissingAnchor)
            {
                warnedMissingAnchor = true;
                Log($"Could not resolve avatar anchor at '{avatarAnchorPath}'.");
            }

            return false;
        }

        private void Log(string message)
        {
            if (!verboseLogs)
                return;

            Debug.Log($"[AvatarFollowAnchorNetcode:{name}] {message}", this);
        }
    }
}
