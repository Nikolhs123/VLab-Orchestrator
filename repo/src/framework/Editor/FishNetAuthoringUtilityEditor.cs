using System;
using System.Collections.Generic;
using System.Reflection;
using FishNet.Object;
using Thesis.Orchestration.EditorTools;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Thesis.Orchestration.Editor
{
    internal static class FishNetAuthoringUtilityEditor
    {
        private static readonly MethodInfo CreateSceneIdMethod = typeof(NetworkObject).GetMethod(
            "CreateSceneId",
            BindingFlags.Static | BindingFlags.NonPublic,
            binder: null,
            types: new[] { typeof(Scene), typeof(bool), typeof(int).MakeByRefType() },
            modifiers: null);

        private static readonly MethodInfo RemoveDuplicateNetworkObjectsMethod = typeof(NetworkObject).GetMethod(
            "RemoveDuplicateNetworkObjects",
            BindingFlags.Instance | BindingFlags.NonPublic);

        private static readonly MethodInfo ReserializeEditorSetValuesMethod = typeof(NetworkObject).GetMethod(
            "ReserializeEditorSetValues",
            BindingFlags.Instance | BindingFlags.NonPublic);

        public static void ReserializeConfiguredFishNetObjects(OrchestrationProjectConfig config)
        {
            if (config == null || config.backendType != OrchestrationBackendType.FishNet)
                return;

            ReserializeOpenScenes();
            ReserializePrefabAsset(config.fishNetRuntimeRootPrefab);
            ReserializePrefabAsset(config.networkAvatarSourcePrefab);
            ReserializeConfiguredPrefabs(config.microscopeEquivalentObjects);
            ReserializeConfiguredPrefabs(config.standaloneSharedObjects);
            AssetDatabase.SaveAssets();
        }

        private static void ReserializeConfiguredPrefabs(List<OrchestrationConfiguredObjectEntry> entries)
        {
            if (entries == null)
                return;

            foreach (OrchestrationConfiguredObjectEntry entry in entries)
            {
                if (entry?.prefab == null)
                    continue;

                ReserializePrefabAsset(entry.prefab);
            }
        }

        private static void ReserializeOpenScenes()
        {
            if (CreateSceneIdMethod == null || RemoveDuplicateNetworkObjectsMethod == null || ReserializeEditorSetValuesMethod == null)
            {
                Debug.LogWarning("[FishNetAuthoringUtilityEditor] FishNet reserialize methods were not found. FishNet scene objects may require manual reserialization.");
                return;
            }

            for (int i = 0; i < EditorSceneManager.sceneCount; i++)
            {
                Scene scene = EditorSceneManager.GetSceneAt(i);
                if (!scene.IsValid() || !scene.isLoaded)
                    continue;

                object[] parameters = { scene, true, 0 };
                List<NetworkObject> networkObjects = CreateSceneIdMethod.Invoke(null, parameters) as List<NetworkObject>;
                if (networkObjects == null)
                    continue;

                bool changed = false;
                foreach (NetworkObject networkObject in networkObjects)
                {
                    if (networkObject == null)
                        continue;

                    changed |= InvokeRemoveDuplicateNetworkObjects(networkObject) > 0;
                    InvokeReserializeEditorSetValues(networkObject);
                    EditorUtility.SetDirty(networkObject);
                    changed = true;
                }

                if (changed || (int)parameters[2] > 0)
                    EditorSceneManager.MarkSceneDirty(scene);
            }
        }

        private static void ReserializePrefabAsset(GameObject prefabAsset)
        {
            if (prefabAsset == null)
                return;

            string assetPath = AssetDatabase.GetAssetPath(prefabAsset);
            if (string.IsNullOrWhiteSpace(assetPath))
                return;

            GameObject prefabContents = PrefabUtility.LoadPrefabContents(assetPath);
            if (prefabContents == null)
                return;

            bool changed = false;

            try
            {
                NetworkObject[] networkObjects = prefabContents.GetComponentsInChildren<NetworkObject>(true);
                foreach (NetworkObject networkObject in networkObjects)
                {
                    if (networkObject == null)
                        continue;

                    changed |= InvokeRemoveDuplicateNetworkObjects(networkObject) > 0;
                    InvokeReserializeEditorSetValues(networkObject);
                    EditorUtility.SetDirty(networkObject);
                    changed = true;
                }

                if (changed)
                    PrefabUtility.SaveAsPrefabAsset(prefabContents, assetPath);
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(prefabContents);
            }
        }

        private static int InvokeRemoveDuplicateNetworkObjects(NetworkObject networkObject)
        {
            if (RemoveDuplicateNetworkObjectsMethod == null || networkObject == null)
                return 0;

            object result = RemoveDuplicateNetworkObjectsMethod.Invoke(networkObject, null);
            return result is int removed ? removed : 0;
        }

        private static void InvokeReserializeEditorSetValues(NetworkObject networkObject)
        {
            if (ReserializeEditorSetValuesMethod == null || networkObject == null)
                return;

            ReserializeEditorSetValuesMethod.Invoke(networkObject, new object[] { true, false });
        }
    }
}
