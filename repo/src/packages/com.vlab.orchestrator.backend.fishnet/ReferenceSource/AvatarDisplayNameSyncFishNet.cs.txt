using FishNet.Connection;
using FishNet.Object;
using Thesis.Orchestration.Runtime;
using Thesis.Orchestration.SharedSync;
using Thesis.Orchestration.UserSync;
using UnityEngine;

namespace Thesis.Orchestration.Backends.FishNet
{
    [DisallowMultipleComponent]
    [RequireComponent(typeof(NetworkObject))]
    public class AvatarDisplayNameSyncFishNet : NetworkBehaviour
    {
        [SerializeField] private AvatarDisplayNameLabelShared label;
        [SerializeField] private UserSyncObjectDefinition userSyncDefinition;

        private string replicatedUserId = string.Empty;
        private string replicatedDisplayName = string.Empty;
        private string lastSubmittedDisplayName = string.Empty;
        private string lastAppliedUserId = string.Empty;
        private string lastAppliedDisplayName = string.Empty;

        private void Awake()
        {
            ResolveReferences();
        }

        public override void OnStartClient()
        {
            base.OnStartClient();
            ResolveReferences();
            ApplyResolvedIdentity();

            if (IsOwner)
                SubmitLocalIdentity(force: true);
        }

        public override void OnOwnershipClient(NetworkConnection prevOwner)
        {
            base.OnOwnershipClient(prevOwner);
            if (IsOwner)
                SubmitLocalIdentity(force: true);
        }

        private void Update()
        {
            if (!IsSpawned)
                return;

            if (IsOwner)
                SubmitLocalIdentity(force: false);

            ApplyResolvedIdentity();
        }

        [ServerRpc(RequireOwnership = false)]
        private void SubmitDisplayNameServerRpc(string userId, string displayName, NetworkConnection caller = null)
        {
            if (!IsServerInitialized)
                return;

            if (Owner.IsValid && caller != null && caller.IsValid && Owner.ClientId != caller.ClientId)
                return;

            RpcApplyDisplayName(
                string.IsNullOrWhiteSpace(userId) ? ResolveOwnerUserId() : userId,
                OrchestrationPlayerNameRegistry.SanitizeDisplayName(displayName, ResolveOwnerUserId()));
        }

        [ObserversRpc(BufferLast = true, RunLocally = true)]
        private void RpcApplyDisplayName(string userId, string displayName)
        {
            replicatedUserId = userId;
            replicatedDisplayName = displayName;
            ApplyResolvedIdentity();
        }

        private void ResolveReferences()
        {
            label ??= GetComponent<AvatarDisplayNameLabelShared>();
            userSyncDefinition ??= GetComponent<UserSyncObjectDefinition>();
        }

        private void SubmitLocalIdentity(bool force)
        {
            string userId = ResolveOwnerUserId();
            string displayName = OrchestrationPlayerNameRegistry.GetLocalPlayerName();
            if (!force && string.Equals(lastSubmittedDisplayName, displayName, System.StringComparison.Ordinal))
                return;

            lastSubmittedDisplayName = displayName;
            SubmitDisplayNameServerRpc(userId, displayName);
        }

        private void ApplyResolvedIdentity()
        {
            ResolveReferences();

            string userId = !string.IsNullOrWhiteSpace(replicatedUserId)
                ? replicatedUserId
                : ResolveOwnerUserId();

            string displayName = replicatedDisplayName;
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

        private string ResolveOwnerUserId()
        {
            NetworkObject networkObject = GetComponent<NetworkObject>();
            if (networkObject == null || !networkObject.IsSpawned)
                return "UnknownUser";

            if (networkObject.Owner.IsValid)
                return $"Client_{networkObject.Owner.ClientId}";

            return NetworkManager != null &&
                   NetworkManager.ClientManager != null &&
                   NetworkManager.ClientManager.Connection.IsValid
                ? $"Client_{NetworkManager.ClientManager.Connection.ClientId}"
                : "UnknownUser";
        }
    }
}
