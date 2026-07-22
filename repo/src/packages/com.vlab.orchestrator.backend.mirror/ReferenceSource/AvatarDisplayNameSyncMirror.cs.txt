using Thesis.Orchestration.Runtime;
using Thesis.Orchestration.SharedSync;
using Thesis.Orchestration.UserSync;
using UnityEngine;
using MirrorNetworkBehaviour = global::Mirror.NetworkBehaviour;
using MirrorNetworkIdentity = global::Mirror.NetworkIdentity;

namespace Thesis.Orchestration.Backends.Mirror
{
    [DisallowMultipleComponent]
    [RequireComponent(typeof(MirrorNetworkIdentity))]
    public class AvatarDisplayNameSyncMirror : MirrorNetworkBehaviour
    {
        [SerializeField] private AvatarDisplayNameLabelShared label;
        [SerializeField] private UserSyncObjectDefinition userSyncDefinition;

        [global::Mirror.SyncVar] private string syncedUserId = string.Empty;
        [global::Mirror.SyncVar] private string syncedDisplayName = string.Empty;

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
        }

        public override void OnStartAuthority()
        {
            base.OnStartAuthority();
            SubmitLocalIdentity(force: true);
        }

        private void Update()
        {
            if (isOwned)
                SubmitLocalIdentity(force: false);

            ApplyResolvedIdentity();
        }

        [global::Mirror.Command]
        private void CmdSetDisplayIdentity(string userId, string displayName)
        {
            syncedUserId = string.IsNullOrWhiteSpace(userId) ? ResolveAuthorityUserId() : userId;
            syncedDisplayName = OrchestrationPlayerNameRegistry.SanitizeDisplayName(displayName, syncedUserId);
            ApplyResolvedIdentity();
        }

        private void ResolveReferences()
        {
            label ??= GetComponent<AvatarDisplayNameLabelShared>();
            userSyncDefinition ??= GetComponent<UserSyncObjectDefinition>();
        }

        private void SubmitLocalIdentity(bool force)
        {
            string displayName = OrchestrationPlayerNameRegistry.GetLocalPlayerName();
            if (!force && string.Equals(lastSubmittedDisplayName, displayName, System.StringComparison.Ordinal))
                return;

            lastSubmittedDisplayName = displayName;
            CmdSetDisplayIdentity(ResolveAuthorityUserId(), displayName);
        }

        private void ApplyResolvedIdentity()
        {
            ResolveReferences();

            string userId = !string.IsNullOrWhiteSpace(syncedUserId)
                ? syncedUserId
                : ResolveAuthorityUserId();

            string displayName = syncedDisplayName;
            if (string.IsNullOrWhiteSpace(displayName) && isOwned)
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
            MirrorNetworkIdentity identity = GetComponent<MirrorNetworkIdentity>();
            if (identity == null)
                return "UnknownUser";

            if (identity.connectionToClient != null)
                return $"Client_{identity.connectionToClient.connectionId}";

            if (isOwned)
                return SessionOrchestratorMirror.Instance != null
                    ? SessionOrchestratorMirror.Instance.GetLocalUserId()
                    : "UnknownUser";

            return "UnknownUser";
        }
    }
}
