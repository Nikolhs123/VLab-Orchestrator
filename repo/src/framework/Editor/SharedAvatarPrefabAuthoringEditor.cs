using System.Collections.Generic;
using System.Linq;
using Thesis.Orchestration.Backends.Fusion;
using Thesis.Orchestration.Backends.FishNet;
using Thesis.Orchestration.Backends.Mirror;
using Thesis.Orchestration.Backends.Netcode;
using Thesis.Orchestration.Backends.PUN2;
using Thesis.Orchestration.EditorTools;
using Thesis.Orchestration.SharedSync;
using Unity.Netcode;
using UnityEditor;
using UnityEngine;
using FishNetNetworkObject = FishNet.Object.NetworkObject;
using FishNetNetworkTransform = FishNet.Component.Transforming.NetworkTransform;
using MirrorNetworkIdentity = global::Mirror.NetworkIdentity;
using MirrorNetworkTransformUnreliable = global::Mirror.NetworkTransformUnreliable;
using MirrorSyncDirection = global::Mirror.SyncDirection;

#if PHOTON_UNITY_NETWORKING
using Photon.Pun;
#endif

using Fusion;
using Fusion.Editor;

namespace Thesis.Orchestration.Editor
{
    internal static class SharedAvatarPrefabAuthoringEditor
    {
        private const string DefaultAvatarAssetPath = "Assets/Resources/AIMultiplayerAssistant/NetworkAvatar.prefab";
        private const string LegacyFusionAvatarAssetPath = "Assets/Resources/AIMultiplayerAssistant/NetworkAvatarFusion.prefab";
        private const string LegacyNetcodeAvatarAssetPath = "Assets/Resources/AIMultiplayerAssistant/NetworkAvatarNetcode.prefab";
        private const string DefaultNetcodePrefabsListPath = "Assets/DefaultNetworkPrefabs.asset";

        public static void ApplySelectedBackendAuthoring()
        {
            OrchestrationProjectConfig projectConfig =
                Resources.Load<OrchestrationProjectConfig>("AIMultiplayerAssistant/OrchestrationProjectConfig");

            ApplySelectedBackendAuthoring(projectConfig);
        }

        public static void ApplySelectedBackendAuthoring(OrchestrationProjectConfig projectConfig)
        {
            if (projectConfig == null)
                return;

            if (BuildPipeline.isBuildingPlayer &&
                projectConfig.backendType == OrchestrationBackendType.Mirror)
                return;

            string avatarAssetPath = ResolveAvatarAssetPath(projectConfig);
            GameObject avatarPrefabAsset = AssetDatabase.LoadAssetAtPath<GameObject>(avatarAssetPath);
            if (avatarPrefabAsset == null)
                return;

            bool prefabChanged = ApplyBackendToSharedAvatarPrefab(avatarAssetPath, projectConfig.backendType);
            avatarPrefabAsset = AssetDatabase.LoadAssetAtPath<GameObject>(avatarAssetPath);

            bool fusionRegistrationChanged = SyncFusionRegistration(avatarPrefabAsset, projectConfig.backendType);
            bool netcodeRegistrationChanged = SyncNetcodeRegistration(avatarPrefabAsset, projectConfig.backendType);

            if (fusionRegistrationChanged)
            {
                NetworkProjectConfigImporter.RebuildPrefabHash();
                NetworkProjectConfigUtilities.ImportGlobalConfig();
            }

            if (prefabChanged || fusionRegistrationChanged || netcodeRegistrationChanged)
                AssetDatabase.SaveAssets();
        }

        private static bool ApplyBackendToSharedAvatarPrefab(string avatarAssetPath, OrchestrationBackendType backendType)
        {
            GameObject prefabContents = PrefabUtility.LoadPrefabContents(avatarAssetPath);
            if (prefabContents == null)
                return false;

            bool changed = false;

            try
            {
                changed |= RemoveLegacyFollowComponents(prefabContents);
                changed |= EnsureSharedFollowComponent(prefabContents);
                changed |= EnsureSharedAvatarLabelComponent(prefabContents);

                int componentCountBeforeCleanup = prefabContents.GetComponents<Component>().Length;
                BackendComponentUtility.RemoveForeignBackendComponentsFromGameObject(prefabContents, backendType);
                changed |= componentCountBeforeCleanup != prefabContents.GetComponents<Component>().Length;

                switch (backendType)
                {
                    case OrchestrationBackendType.PUN2:
                        changed |= EnsurePun2AvatarComponents(prefabContents);
                        break;

                    case OrchestrationBackendType.Fusion:
                        changed |= EnsureFusionAvatarComponents(prefabContents);
                        break;

                    case OrchestrationBackendType.UnityNetcode:
                        changed |= EnsureNetcodeAvatarComponents(prefabContents);
                        break;

                    case OrchestrationBackendType.FishNet:
                        changed |= EnsureFishNetAvatarComponents(prefabContents);
                        break;

                    case OrchestrationBackendType.Mirror:
                        changed |= EnsureMirrorAvatarComponents(prefabContents);
                        changed |= MirrorAuthoringUtilityEditor.EnsurePrefabNetworkIdentityIds(prefabContents, avatarAssetPath);
                        break;
                }

                if (!changed)
                    return false;

                EditorUtility.SetDirty(prefabContents);
                PrefabUtility.SaveAsPrefabAsset(prefabContents, avatarAssetPath);
                return true;
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(prefabContents);
            }
        }

        private static bool EnsureSharedFollowComponent(GameObject root)
        {
            if (root.GetComponent<AvatarFollowAnchorShared>() != null)
                return false;

            root.AddComponent<AvatarFollowAnchorShared>();
            return true;
        }

        private static bool EnsureSharedAvatarLabelComponent(GameObject root)
        {
            if (root.GetComponent<AvatarDisplayNameLabelShared>() != null)
                return false;

            root.AddComponent<AvatarDisplayNameLabelShared>();
            return true;
        }

        private static bool RemoveLegacyFollowComponents(GameObject root)
        {
            bool changed = false;

            changed |= DestroyIfPresent(root.GetComponent<AvatarFollowAnchorLocal>());
            changed |= DestroyIfPresent(root.GetComponent<AvatarFollowAnchorFusion>());
            changed |= DestroyIfPresent(root.GetComponent<AvatarFollowAnchorNetcode>());

            return changed;
        }

        private static bool EnsurePun2AvatarComponents(GameObject root)
        {
            bool changed = false;

#if PHOTON_UNITY_NETWORKING
            PhotonView photonView = root.GetComponent<PhotonView>();
            if (photonView == null)
            {
                photonView = root.AddComponent<PhotonView>();
                changed = true;
            }

            UserPresenceTransformSyncPUN2 sync = root.GetComponent<UserPresenceTransformSyncPUN2>();
            if (sync == null)
            {
                sync = root.AddComponent<UserPresenceTransformSyncPUN2>();
                changed = true;
            }

            if (root.GetComponent<AvatarDisplayNameSyncPUN2>() == null)
            {
                root.AddComponent<AvatarDisplayNameSyncPUN2>();
                changed = true;
            }

            if (photonView.Synchronization != ViewSynchronization.UnreliableOnChange)
            {
                photonView.Synchronization = ViewSynchronization.UnreliableOnChange;
                changed = true;
            }

            if (photonView.ObservedComponents == null)
            {
                photonView.ObservedComponents = new List<Component>();
                changed = true;
            }

            bool removedObservedComponents = photonView.ObservedComponents.RemoveAll(component =>
                component == null ||
                component is AvatarFollowAnchorShared ||
                component is AvatarFollowAnchorLocal ||
                component is AvatarFollowAnchorFusion ||
                component is AvatarFollowAnchorNetcode) > 0;

            bool addedObservedComponent = false;
            if (!photonView.ObservedComponents.Contains(sync))
            {
                photonView.ObservedComponents.Add(sync);
                addedObservedComponent = true;
            }

            changed |= removedObservedComponents || addedObservedComponent;
#endif

            return changed;
        }

        private static bool EnsureFusionAvatarComponents(GameObject root)
        {
            bool changed = false;

            if (root.GetComponent<Fusion.NetworkObject>() == null)
            {
                root.AddComponent<Fusion.NetworkObject>();
                changed = true;
            }

            if (root.GetComponent<UserPresenceTransformSyncFusion>() == null)
            {
                root.AddComponent<UserPresenceTransformSyncFusion>();
                changed = true;
            }

            if (root.GetComponent<AvatarDisplayNameSyncFusion>() == null)
            {
                root.AddComponent<AvatarDisplayNameSyncFusion>();
                changed = true;
            }

            return changed;
        }

        private static bool EnsureNetcodeAvatarComponents(GameObject root)
        {
            bool changed = false;

            if (root.GetComponent<Unity.Netcode.NetworkObject>() == null)
            {
                root.AddComponent<Unity.Netcode.NetworkObject>();
                changed = true;
            }

            if (root.GetComponent<UserPresenceTransformSyncNetcode>() == null)
            {
                root.AddComponent<UserPresenceTransformSyncNetcode>();
                changed = true;
            }

            if (root.GetComponent<AvatarDisplayNameSyncNetcode>() == null)
            {
                root.AddComponent<AvatarDisplayNameSyncNetcode>();
                changed = true;
            }

            return changed;
        }

        private static bool EnsureFishNetAvatarComponents(GameObject root)
        {
            bool changed = false;

            if (root.GetComponent<FishNetNetworkObject>() == null)
            {
                root.AddComponent<FishNetNetworkObject>();
                changed = true;
            }

            if (root.GetComponent<FishNetNetworkTransform>() == null)
            {
                root.AddComponent<FishNetNetworkTransform>();
                changed = true;
            }

            if (root.GetComponent<UserPresenceTransformSyncFishNet>() == null)
            {
                root.AddComponent<UserPresenceTransformSyncFishNet>();
                changed = true;
            }

            if (root.GetComponent<AvatarDisplayNameSyncFishNet>() == null)
            {
                root.AddComponent<AvatarDisplayNameSyncFishNet>();
                changed = true;
            }

            return changed;
        }

        private static bool EnsureMirrorAvatarComponents(GameObject root)
        {
            bool changed = false;

            if (root.GetComponent<MirrorNetworkIdentity>() == null)
            {
                root.AddComponent<MirrorNetworkIdentity>();
                changed = true;
            }

            MirrorNetworkTransformUnreliable networkTransform = root.GetComponent<MirrorNetworkTransformUnreliable>();
            if (networkTransform == null)
            {
                networkTransform = root.AddComponent<MirrorNetworkTransformUnreliable>();
                changed = true;
            }

            if (networkTransform.syncDirection != MirrorSyncDirection.ClientToServer)
            {
                networkTransform.syncDirection = MirrorSyncDirection.ClientToServer;
                changed = true;
            }

            if (root.GetComponent<UserPresenceTransformSyncMirror>() == null)
            {
                root.AddComponent<UserPresenceTransformSyncMirror>();
                changed = true;
            }

            if (root.GetComponent<AvatarDisplayNameSyncMirror>() == null)
            {
                root.AddComponent<AvatarDisplayNameSyncMirror>();
                changed = true;
            }

            return changed;
        }

        private static bool SyncFusionRegistration(GameObject avatarPrefabAsset, OrchestrationBackendType backendType)
        {
            bool changed = false;

            if (avatarPrefabAsset != null)
            {
                bool shouldHaveLabel =
                    backendType == OrchestrationBackendType.Fusion &&
                    avatarPrefabAsset.GetComponent<Fusion.NetworkObject>() != null;

                string[] existingLabels = AssetDatabase.GetLabels(avatarPrefabAsset);
                bool hasLabel = existingLabels.Contains(NetworkProjectConfigImporter.FusionPrefabTag);
                if (hasLabel != shouldHaveLabel)
                {
                    List<string> updatedLabels = existingLabels.ToList();
                    updatedLabels.Remove(NetworkProjectConfigImporter.FusionPrefabTag);

                    if (shouldHaveLabel)
                        updatedLabels.Add(NetworkProjectConfigImporter.FusionPrefabTag);

                    AssetDatabase.SetLabels(avatarPrefabAsset, updatedLabels.ToArray());
                    changed = true;
                }
            }

            changed |= RemoveFusionLabelFromLegacyAvatar(LegacyFusionAvatarAssetPath);
            changed |= RemoveFusionLabelFromLegacyAvatar(LegacyNetcodeAvatarAssetPath);
            return changed;
        }

        private static bool SyncNetcodeRegistration(GameObject avatarPrefabAsset, OrchestrationBackendType backendType)
        {
            NetworkPrefabsList networkPrefabsList = AssetDatabase.LoadAssetAtPath<NetworkPrefabsList>(DefaultNetcodePrefabsListPath);
            if (networkPrefabsList == null || avatarPrefabAsset == null)
                return false;

            bool changed = RemoveLegacyNetcodeAvatarEntries(networkPrefabsList);
            NetworkPrefab existingEntry = networkPrefabsList.PrefabList.FirstOrDefault(entry => entry != null && entry.Prefab == avatarPrefabAsset);
            bool shouldBeRegistered =
                backendType == OrchestrationBackendType.UnityNetcode &&
                avatarPrefabAsset.GetComponent<Unity.Netcode.NetworkObject>() != null;

            if (shouldBeRegistered)
            {
                if (existingEntry != null)
                    return changed;

                networkPrefabsList.Add(new NetworkPrefab
                {
                    Override = NetworkPrefabOverride.None,
                    Prefab = avatarPrefabAsset
                });

                EditorUtility.SetDirty(networkPrefabsList);
                return true;
            }

            if (existingEntry == null)
                return changed;

            networkPrefabsList.Remove(existingEntry);
            EditorUtility.SetDirty(networkPrefabsList);
            return true;
        }

        private static string ResolveAvatarAssetPath(OrchestrationProjectConfig projectConfig)
        {
            if (projectConfig == null || string.IsNullOrWhiteSpace(projectConfig.networkAvatarPrefabPath))
                return DefaultAvatarAssetPath;

            string normalized = projectConfig.networkAvatarPrefabPath.Replace("\\", "/").Trim();

            if (normalized.StartsWith("Assets/"))
                return normalized.EndsWith(".prefab") ? normalized : $"{normalized}.prefab";

            if (normalized.StartsWith("Resources/"))
                return $"Assets/{(normalized.EndsWith(".prefab") ? normalized : $"{normalized}.prefab")}";

            return $"Assets/Resources/{normalized}.prefab";
        }

        private static bool DestroyIfPresent(Component component)
        {
            if (component == null)
                return false;

            Object.DestroyImmediate(component);
            return true;
        }

        private static bool RemoveFusionLabelFromLegacyAvatar(string assetPath)
        {
            GameObject legacyAvatar = AssetDatabase.LoadAssetAtPath<GameObject>(assetPath);
            if (legacyAvatar == null)
                return false;

            string[] existingLabels = AssetDatabase.GetLabels(legacyAvatar);
            if (!existingLabels.Contains(NetworkProjectConfigImporter.FusionPrefabTag))
                return false;

            List<string> updatedLabels = existingLabels.ToList();
            updatedLabels.Remove(NetworkProjectConfigImporter.FusionPrefabTag);
            AssetDatabase.SetLabels(legacyAvatar, updatedLabels.ToArray());
            return true;
        }

        private static bool RemoveLegacyNetcodeAvatarEntries(NetworkPrefabsList networkPrefabsList)
        {
            bool changed = false;
            List<NetworkPrefab> entriesToRemove = new List<NetworkPrefab>();

            foreach (NetworkPrefab entry in networkPrefabsList.PrefabList)
            {
                if (entry?.Prefab == null)
                    continue;

                string prefabPath = AssetDatabase.GetAssetPath(entry.Prefab);
                if (!string.Equals(prefabPath, LegacyNetcodeAvatarAssetPath))
                    continue;

                entriesToRemove.Add(entry);
            }

            foreach (NetworkPrefab entry in entriesToRemove)
            {
                networkPrefabsList.Remove(entry);
                changed = true;
            }

            if (changed)
                EditorUtility.SetDirty(networkPrefabsList);

            return changed;
        }
    }
}
