using Fusion;
using Thesis.Orchestration.Runtime;
using Thesis.Orchestration.SharedSync;
using Thesis.Orchestration.UserSync;
using UnityEngine;

namespace Thesis.Orchestration.Backends.Fusion
{
    [DisallowMultipleComponent]
    [RequireComponent(typeof(NetworkObject))]
    public class AvatarDisplayNameSyncFusion : NetworkBehaviour
    {
        [SerializeField] private AvatarDisplayNameLabelShared label;
        [SerializeField] private UserSyncObjectDefinition userSyncDefinition;

        [Networked] private NetworkString<_32> NetworkUserId { get; set; }
        [Networked] private NetworkString<_64> NetworkDisplayName { get; set; }

        private string lastAppliedUserId = string.Empty;
        private string lastAppliedDisplayName = string.Empty;

        public override void Spawned()
        {
            ResolveReferences();
            if (Object != null && Object.HasStateAuthority)
                UpdateNetworkIdentity();

            ApplyResolvedIdentity();
        }

        public override void FixedUpdateNetwork()
        {
            if (Object == null || !Object.IsValid || !Object.HasStateAuthority)
                return;

            UpdateNetworkIdentity();
        }

        public override void Render()
        {
            ApplyResolvedIdentity();
        }

        private void ResolveReferences()
        {
            label ??= GetComponent<AvatarDisplayNameLabelShared>();
            userSyncDefinition ??= GetComponent<UserSyncObjectDefinition>();
        }

        private void UpdateNetworkIdentity()
        {
            string userId = ResolveAuthorityUserId();
            string displayName = OrchestrationPlayerNameRegistry.GetLocalPlayerName();

            if (!string.IsNullOrWhiteSpace(userId))
                NetworkUserId = userId;

            NetworkDisplayName = displayName;
        }

        private void ApplyResolvedIdentity()
        {
            ResolveReferences();

            string userId = NetworkUserId.ToString();
            if (string.IsNullOrWhiteSpace(userId))
                userId = ResolveAuthorityUserId();

            string displayName = NetworkDisplayName.ToString();
            if (string.IsNullOrWhiteSpace(displayName) && Object != null && Object.HasStateAuthority)
                displayName = OrchestrationPlayerNameRegistry.GetLocalPlayerName();

            if (!string.IsNullOrWhiteSpace(userId))
            {
                OrchestrationPlayerNameRegistry.RegisterDisplayName(userId, displayName);

                if (!string.Equals(lastAppliedUserId, userId, System.StringComparison.Ordinal))
                {
                    lastAppliedUserId = userId;
                    if (userSyncDefinition != null)
                    {
                        userSyncDefinition.SetOwnerUserId(userId);
                        userSyncDefinition.NotifyPresenceUpdated();
                    }
                }
            }

            string resolvedDisplayName = OrchestrationPlayerNameRegistry.ResolveDisplayName(userId, displayName);
            if (string.Equals(lastAppliedDisplayName, resolvedDisplayName, System.StringComparison.Ordinal))
                return;

            lastAppliedDisplayName = resolvedDisplayName;
            label?.ApplyDisplayName(resolvedDisplayName);
        }

        private string ResolveAuthorityUserId()
        {
            if (Object == null || !Object.IsValid)
                return "UnknownUser";

            PlayerRef authority = Object.InputAuthority != PlayerRef.None ? Object.InputAuthority : Object.StateAuthority;
            return authority != PlayerRef.None
                ? $"Player_{authority.RawEncoded}"
                : "UnknownUser";
        }
    }
}
