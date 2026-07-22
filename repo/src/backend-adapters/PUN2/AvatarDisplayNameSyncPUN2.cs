using Photon.Pun;
using Thesis.Orchestration.Runtime;
using Thesis.Orchestration.SharedSync;
using Thesis.Orchestration.UserSync;
using UnityEngine;

namespace Thesis.Orchestration.Backends.PUN2
{
    [DisallowMultipleComponent]
    [RequireComponent(typeof(PhotonView))]
    public class AvatarDisplayNameSyncPUN2 : MonoBehaviourPun
    {
        [SerializeField] private AvatarDisplayNameLabelShared label;
        [SerializeField] private UserSyncObjectDefinition userSyncDefinition;

        private string lastUserId = string.Empty;
        private string lastDisplayName = string.Empty;

        private void Awake()
        {
            ResolveReferences();
            ApplyResolvedIdentity();
        }

        private void Update()
        {
            ApplyResolvedIdentity();
        }

        private void ResolveReferences()
        {
            label ??= GetComponent<AvatarDisplayNameLabelShared>();
            userSyncDefinition ??= GetComponent<UserSyncObjectDefinition>();
        }

        private void ApplyResolvedIdentity()
        {
            ResolveReferences();

            string userId = ResolveUserId();
            string displayName = ResolveDisplayName();

            if (!string.Equals(lastUserId, userId, System.StringComparison.Ordinal))
            {
                lastUserId = userId;
                if (userSyncDefinition != null && !string.IsNullOrWhiteSpace(userId))
                {
                    userSyncDefinition.SetOwnerUserId(userId);
                    userSyncDefinition.NotifyPresenceUpdated();
                }
            }

            if (string.IsNullOrWhiteSpace(displayName))
                displayName = userId;

            OrchestrationPlayerNameRegistry.RegisterDisplayName(userId, displayName);

            if (!string.Equals(lastDisplayName, displayName, System.StringComparison.Ordinal))
            {
                lastDisplayName = displayName;
                label?.ApplyDisplayName(displayName);
            }
        }

        private string ResolveUserId()
        {
            if (photonView != null && photonView.Owner != null)
                return $"Actor_{photonView.Owner.ActorNumber}";

            return "UnknownUser";
        }

        private string ResolveDisplayName()
        {
            if (photonView != null && photonView.Owner != null && !string.IsNullOrWhiteSpace(photonView.Owner.NickName))
                return photonView.Owner.NickName;

            if (photonView != null && photonView.IsMine)
                return OrchestrationPlayerNameRegistry.GetLocalPlayerName();

            return string.Empty;
        }
    }
}
