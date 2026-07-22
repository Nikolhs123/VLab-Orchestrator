using Thesis.Orchestration.EditorTools;
using Unity.Netcode;
using UnityEngine;
using FishNetNetworkObject = FishNet.Object.NetworkObject;
using MirrorNetworkIdentity = global::Mirror.NetworkIdentity;

#if PHOTON_UNITY_NETWORKING
using Photon.Pun;
#endif

using Fusion;

namespace Thesis.Orchestration.SharedSync
{
    [DisallowMultipleComponent]
    public class AvatarFollowAnchorShared : MonoBehaviour
    {
        [Header("Follow Settings")]
        [SerializeField] private Transform anchor;
        [SerializeField] private string avatarAnchorPath = "Ego/Camera/AvatarAnchor";
        [SerializeField] private bool followPosition = true;
        [SerializeField] private bool followRotation = true;

        [Header("Debug")]
        [SerializeField] private bool verboseLogs = false;

        private OrchestrationBackendType selectedBackendType;
        private bool warnedMissingAnchor;
        private bool backendResolved;

        public void SetAnchor(Transform newAnchor)
        {
            anchor = newAnchor;
            warnedMissingAnchor = false;
            Log($"Anchor assigned: {(anchor != null ? anchor.name : "null")}");
        }

        private void Awake()
        {
            ResolveBackendTypeIfNeeded();
        }

        private void Update()
        {
            if (!HasLocalAuthority())
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

        private bool HasLocalAuthority()
        {
            ResolveBackendTypeIfNeeded();

            switch (selectedBackendType)
            {
                case OrchestrationBackendType.Fusion:
                    if (TryGetComponent(out Fusion.NetworkObject fusionObject) && fusionObject != null && fusionObject.IsValid)
                        return fusionObject.HasStateAuthority;
                    break;

                case OrchestrationBackendType.UnityNetcode:
                    if (TryGetComponent(out Unity.Netcode.NetworkObject netcodeObject) && netcodeObject != null && netcodeObject.IsSpawned)
                        return netcodeObject.IsOwner;
                    break;

                case OrchestrationBackendType.PUN2:
#if PHOTON_UNITY_NETWORKING
                    if (TryGetComponent(out PhotonView photonView) && photonView != null)
                        return photonView.IsMine;
#endif
                    break;

                case OrchestrationBackendType.FishNet:
                    if (TryGetComponent(out FishNetNetworkObject fishNetObject) && fishNetObject != null && fishNetObject.IsSpawned)
                        return fishNetObject.IsOwner;
                    break;

                case OrchestrationBackendType.Mirror:
                    if (TryGetComponent(out MirrorNetworkIdentity mirrorIdentity) && mirrorIdentity != null)
                        return mirrorIdentity.isOwned;
                    break;
            }

            return DetectAuthorityFromAnyAvailableBackend();
        }

        private bool DetectAuthorityFromAnyAvailableBackend()
        {
#if PHOTON_UNITY_NETWORKING
            if (TryGetComponent(out PhotonView photonView) && photonView != null)
                return photonView.IsMine;
#endif

            if (TryGetComponent(out Fusion.NetworkObject fusionObject) && fusionObject != null && fusionObject.IsValid)
                return fusionObject.HasStateAuthority;

            if (TryGetComponent(out Unity.Netcode.NetworkObject netcodeObject) && netcodeObject != null && netcodeObject.IsSpawned)
                return netcodeObject.IsOwner;

            if (TryGetComponent(out FishNetNetworkObject fishNetObject) && fishNetObject != null && fishNetObject.IsSpawned)
                return fishNetObject.IsOwner;

            if (TryGetComponent(out MirrorNetworkIdentity mirrorIdentity) && mirrorIdentity != null)
                return mirrorIdentity.isOwned;

            return false;
        }

        private void ResolveBackendTypeIfNeeded()
        {
            if (backendResolved)
                return;

            selectedBackendType = SharedAvatarRuntimeUtility.ResolveSelectedBackendType();
            backendResolved = true;
        }

        private void Log(string message)
        {
            if (!verboseLogs)
                return;

            Debug.Log($"[AvatarFollowAnchorShared:{name}] {message}", this);
        }
    }
}
