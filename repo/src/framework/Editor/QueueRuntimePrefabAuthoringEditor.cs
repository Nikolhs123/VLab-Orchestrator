using System.Collections.Generic;
using System.Linq;
using Thesis.Orchestration.Backends.Fusion;
using Thesis.Orchestration.Backends.Netcode;
using Thesis.Orchestration.Backends.PUN2;
using Thesis.Orchestration.EditorTools;
using Thesis.Orchestration.SharedSync;
using Unity.Netcode;
using UnityEditor;
using UnityEngine;
using Fusion;
using Fusion.Editor;

#if PHOTON_UNITY_NETWORKING
using Photon.Pun;
#endif

namespace Thesis.Orchestration.Editor
{
    internal static class QueueRuntimePrefabAuthoringEditor
    {
        public static void ApplySelectedBackendAuthoring()
        {
            bool changed = false;

            changed |= EnsurePun2QueueRuntimePrefab();
            changed |= EnsureFusionQueueRuntimePrefab();
            changed |= EnsureNetcodeQueueRuntimePrefab();

            if (!changed)
                return;

            AssetDatabase.SaveAssets();
        }

        private static bool EnsurePun2QueueRuntimePrefab()
        {
#if !PHOTON_UNITY_NETWORKING
            return false;
#else
            return EnsureQueueRuntimePrefab(
                OrchestrationRuntimePrefabPaths.PUN2QueueRuntimeAssetPath,
                OrchestrationBackendType.PUN2,
                root =>
                {
                    bool changed = false;

                    PhotonView photonView = root.GetComponent<PhotonView>();
                    if (photonView == null)
                    {
                        photonView = root.AddComponent<PhotonView>();
                        changed = true;
                    }

                    if (root.GetComponent<SessionQueueRuntimePUN2>() == null)
                    {
                        root.AddComponent<SessionQueueRuntimePUN2>();
                        changed = true;
                    }

                    if (photonView.ObservedComponents == null)
                    {
                        photonView.ObservedComponents = new List<Component>();
                        changed = true;
                    }

                    return changed;
                });
#endif
        }

        private static bool EnsureFusionQueueRuntimePrefab()
        {
            bool prefabChanged = EnsureQueueRuntimePrefab(
                OrchestrationRuntimePrefabPaths.FusionQueueRuntimeAssetPath,
                OrchestrationBackendType.Fusion,
                root =>
                {
                    bool changed = false;

                    if (root.GetComponent<Fusion.NetworkObject>() == null)
                    {
                        root.AddComponent<Fusion.NetworkObject>();
                        changed = true;
                    }

                    if (root.GetComponent<SessionQueueRuntimeFusion>() == null)
                    {
                        root.AddComponent<SessionQueueRuntimeFusion>();
                        changed = true;
                    }

                    return changed;
                });

            GameObject fusionQueuePrefab = AssetDatabase.LoadAssetAtPath<GameObject>(OrchestrationRuntimePrefabPaths.FusionQueueRuntimeAssetPath);
            bool labelsChanged = EnsureFusionPrefabLabel(fusionQueuePrefab);

            if (labelsChanged)
            {
                NetworkProjectConfigImporter.RebuildPrefabHash();
                NetworkProjectConfigUtilities.ImportGlobalConfig();
            }

            return prefabChanged || labelsChanged;
        }

        private static bool EnsureNetcodeQueueRuntimePrefab()
        {
            return EnsureQueueRuntimePrefab(
                OrchestrationRuntimePrefabPaths.NetcodeQueueRuntimeAssetPath,
                OrchestrationBackendType.UnityNetcode,
                root =>
                {
                    bool changed = false;

                    if (root.GetComponent<Unity.Netcode.NetworkObject>() == null)
                    {
                        root.AddComponent<Unity.Netcode.NetworkObject>();
                        changed = true;
                    }

                    if (root.GetComponent<SessionQueueRuntimeNetcode>() == null)
                    {
                        root.AddComponent<SessionQueueRuntimeNetcode>();
                        changed = true;
                    }

                    return changed;
                });
        }

        private static bool EnsureQueueRuntimePrefab(
            string assetPath,
            OrchestrationBackendType backendType,
            System.Func<GameObject, bool> ensureComponents)
        {
            EnsureParentFolders(assetPath);

            GameObject prefabContents = LoadOrCreatePrefabContents(assetPath);
            if (prefabContents == null)
                return false;

            bool changed = false;

            try
            {
                if (prefabContents.name != System.IO.Path.GetFileNameWithoutExtension(assetPath))
                {
                    prefabContents.name = System.IO.Path.GetFileNameWithoutExtension(assetPath);
                    changed = true;
                }

                int componentCountBeforeCleanup = prefabContents.GetComponents<Component>().Length;
                BackendComponentUtility.RemoveForeignBackendComponentsFromGameObject(prefabContents, backendType);
                changed |= componentCountBeforeCleanup != prefabContents.GetComponents<Component>().Length;

                changed |= ensureComponents(prefabContents);

                if (!changed)
                    return false;

                EditorUtility.SetDirty(prefabContents);
                PrefabUtility.SaveAsPrefabAsset(prefabContents, assetPath);
                return true;
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(prefabContents);
            }
        }

        private static GameObject LoadOrCreatePrefabContents(string assetPath)
        {
            GameObject existingPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(assetPath);
            if (existingPrefab != null)
                return PrefabUtility.LoadPrefabContents(assetPath);

            GameObject root = new GameObject(System.IO.Path.GetFileNameWithoutExtension(assetPath));
            PrefabUtility.SaveAsPrefabAsset(root, assetPath);
            Object.DestroyImmediate(root);
            return PrefabUtility.LoadPrefabContents(assetPath);
        }

        private static void EnsureParentFolders(string assetPath)
        {
            string normalizedPath = assetPath.Replace("\\", "/");
            string[] segments = normalizedPath.Split('/');
            if (segments.Length < 2)
                return;

            string current = segments[0];
            for (int index = 1; index < segments.Length - 1; index++)
            {
                string next = current + "/" + segments[index];
                if (!AssetDatabase.IsValidFolder(next))
                    AssetDatabase.CreateFolder(current, segments[index]);

                current = next;
            }
        }

        private static bool EnsureFusionPrefabLabel(GameObject prefab)
        {
            if (prefab == null)
                return false;

            string[] existingLabels = AssetDatabase.GetLabels(prefab);
            if (existingLabels.Contains(NetworkProjectConfigImporter.FusionPrefabTag))
                return false;

            List<string> updatedLabels = existingLabels.ToList();
            updatedLabels.Add(NetworkProjectConfigImporter.FusionPrefabTag);
            AssetDatabase.SetLabels(prefab, updatedLabels.ToArray());
            return true;
        }
    }
}
