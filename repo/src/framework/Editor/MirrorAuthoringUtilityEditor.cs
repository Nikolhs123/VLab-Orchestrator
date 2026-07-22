using System;
using UnityEditor;
using UnityEngine;
using MirrorNetworkIdentity = global::Mirror.NetworkIdentity;

namespace Thesis.Orchestration.Editor
{
    internal static class MirrorAuthoringUtilityEditor
    {
        public static bool EnsurePrefabNetworkIdentityIds(GameObject prefabRoot, string prefabAssetPath)
        {
            if (prefabRoot == null || string.IsNullOrWhiteSpace(prefabAssetPath))
                return false;

            string guidString = AssetDatabase.AssetPathToGUID(prefabAssetPath);
            if (string.IsNullOrWhiteSpace(guidString))
                return false;

            uint assetId = MirrorNetworkIdentity.AssetGuidToUint(new Guid(guidString));
            if (assetId == 0)
                return false;

            bool changed = false;
            MirrorNetworkIdentity[] identities = prefabRoot.GetComponentsInChildren<MirrorNetworkIdentity>(true);
            foreach (MirrorNetworkIdentity identity in identities)
            {
                if (identity == null)
                    continue;

                SerializedObject serializedIdentity = new SerializedObject(identity);
                SerializedProperty assetIdProperty = serializedIdentity.FindProperty("_assetId");
                SerializedProperty sceneIdProperty = serializedIdentity.FindProperty("sceneId");

                if (assetIdProperty != null && (uint)assetIdProperty.intValue != assetId)
                {
                    assetIdProperty.intValue = unchecked((int)assetId);
                    changed = true;
                }

                if (sceneIdProperty != null && sceneIdProperty.longValue != 0)
                {
                    sceneIdProperty.longValue = 0;
                    changed = true;
                }

                if (!changed)
                    continue;

                serializedIdentity.ApplyModifiedPropertiesWithoutUndo();
                EditorUtility.SetDirty(identity);
            }

            return changed;
        }
    }
}
