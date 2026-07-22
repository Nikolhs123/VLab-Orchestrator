using Thesis.Orchestration.Runtime;
using Thesis.Orchestration.SharedSync;
using Thesis.Orchestration.UserSync;
using Unity.Collections;
using Unity.Netcode;
using UnityEngine;

namespace Thesis.Orchestration.Backends.Netcode
{
    [DisallowMultipleComponent]
    [RequireComponent(typeof(NetworkObject))]
    public class AvatarDisplayNameSyncNetcode : NetworkBehaviour
    {
        [SerializeField] private AvatarDisplayNameLabelShared label;
        [SerializeField] private UserSyncObjectDefinition userSyncDefinition;

        private readonly NetworkVariable<FixedString64Bytes> networkUserId = new(
            default,
            NetworkVariableReadPermission.Everyone,
            NetworkVariableWritePermission.Owner);

        private readonly NetworkVariable<FixedString64Bytes> networkDisplayName = new(
            default,
            NetworkVariableReadPermission.Everyone,
            NetworkVariableWritePermission.Owner);

        private string lastAppliedUserId = string.Empty;
        private string lastAppliedDisplayName = string.Empty;

        public override void OnNetworkSpawn()
        {
            base.OnNetworkSpawn();
            ResolveReferences();

            if (IsOwner)
                UpdateNetworkIdentity();

            ApplyResolvedIdentity();
        }

        private void Update()
        {
            if (!IsSpawned)
                return;

            if (IsOwner)
                UpdateNetworkIdentity();

            ApplyResolvedIdentity();
        }

        private void ResolveReferences()
        {
            label ??= GetComponent<AvatarDisplayNameLabelShared>();
            userSyncDefinition ??= GetComponent<UserSyncObjectDefinition>();
        }

        private void UpdateNetworkIdentity()
        {
            string userId = $"Client_{OwnerClientId}";
            string displayName = OrchestrationPlayerNameRegistry.GetLocalPlayerName();

            if (networkUserId.Value.ToString() != userId)
                networkUserId.Value = new FixedString64Bytes(userId);

            if (networkDisplayName.Value.ToString() != displayName)
                networkDisplayName.Value = new FixedString64Bytes(displayName);
        }

        private void ApplyResolvedIdentity()
        {
            ResolveReferences();

            string userId = networkUserId.Value.ToString();
            if (string.IsNullOrWhiteSpace(userId))
                userId = $"Client_{OwnerClientId}";

            string displayName = networkDisplayName.Value.ToString();
            if (string.IsNullOrWhiteSpace(displayName) && IsOwner)
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
    }
}
