using Thesis.Orchestration.Backends.Fusion;
using Thesis.Orchestration.Backends.FishNet;
using Thesis.Orchestration.Backends.Netcode;
using Thesis.Orchestration.Backends.PUN2;
using Thesis.Orchestration.EditorTools;
using UnityEngine;

namespace Thesis.Orchestration.SharedSync
{
    public static class SharedAvatarRuntimeUtility
    {
        public const string DefaultAvatarPrefabResourcesPath = "AIMultiplayerAssistant/NetworkAvatar";
        private const string LegacyFusionAvatarPrefabResourcesPath = "AIMultiplayerAssistant/NetworkAvatarFusion";
        private const string LegacyNetcodeAvatarPrefabResourcesPath = "AIMultiplayerAssistant/NetworkAvatarNetcode";

        private static OrchestrationProjectConfig cachedProjectConfig;

        public static string ResolveAvatarPrefabResourcesPath(string fallbackResourcesPath = null)
        {
            OrchestrationProjectConfig config = ResolveProjectConfig();
            if (config != null && !string.IsNullOrWhiteSpace(config.networkAvatarPrefabPath))
                return config.networkAvatarPrefabPath;

            if (string.IsNullOrWhiteSpace(fallbackResourcesPath))
                return DefaultAvatarPrefabResourcesPath;

            string normalizedFallback = fallbackResourcesPath.Replace("\\", "/").Trim();
            return IsLegacyAvatarResourcesPath(normalizedFallback)
                ? DefaultAvatarPrefabResourcesPath
                : normalizedFallback;
        }

        public static OrchestrationBackendType ResolveSelectedBackendType()
        {
            OrchestrationProjectConfig config = ResolveProjectConfig();
            return config != null ? config.backendType : OrchestrationBackendType.PUN2;
        }

        public static bool TryAssignAnchor(GameObject avatarRoot, Transform anchor)
        {
            if (avatarRoot == null || anchor == null)
                return false;

            AvatarFollowAnchorShared sharedFollow = avatarRoot.GetComponentInChildren<AvatarFollowAnchorShared>(true);
            if (sharedFollow != null)
            {
                sharedFollow.SetAnchor(anchor);
                return true;
            }

            AvatarFollowAnchorFusion fusionFollow = avatarRoot.GetComponentInChildren<AvatarFollowAnchorFusion>(true);
            if (fusionFollow != null)
            {
                fusionFollow.SetAnchor(anchor);
                return true;
            }

            AvatarFollowAnchorNetcode netcodeFollow = avatarRoot.GetComponentInChildren<AvatarFollowAnchorNetcode>(true);
            if (netcodeFollow != null)
            {
                netcodeFollow.SetAnchor(anchor);
                return true;
            }

            AvatarFollowAnchorLocal pun2Follow = avatarRoot.GetComponentInChildren<AvatarFollowAnchorLocal>(true);
            if (pun2Follow != null)
            {
                pun2Follow.SetAnchor(anchor);
                return true;
            }

            return false;
        }

        public static bool HasCompatibleFollowAnchor(GameObject avatarRoot)
        {
            if (avatarRoot == null)
                return false;

            return avatarRoot.GetComponentInChildren<AvatarFollowAnchorShared>(true) != null ||
                   avatarRoot.GetComponentInChildren<AvatarFollowAnchorFusion>(true) != null ||
                   avatarRoot.GetComponentInChildren<AvatarFollowAnchorNetcode>(true) != null ||
                   avatarRoot.GetComponentInChildren<AvatarFollowAnchorLocal>(true) != null;
        }

        private static OrchestrationProjectConfig ResolveProjectConfig()
        {
            if (cachedProjectConfig != null)
                return cachedProjectConfig;

            cachedProjectConfig = Resources.Load<OrchestrationProjectConfig>("AIMultiplayerAssistant/OrchestrationProjectConfig");
            return cachedProjectConfig;
        }

        public static bool IsLegacyAvatarResourcesPath(string resourcesPath)
        {
            return string.Equals(resourcesPath, LegacyFusionAvatarPrefabResourcesPath) ||
                   string.Equals(resourcesPath, LegacyNetcodeAvatarPrefabResourcesPath);
        }
    }
}
