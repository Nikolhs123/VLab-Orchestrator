using System.Collections.Generic;
using Thesis.Orchestration.EditorTools;
using Thesis.Orchestration.SharedSync;
using UnityEditor;
using UnityEngine;

namespace Thesis.Orchestration.Editor
{
    internal static class ConfiguredSharedObjectPrefabAuthoringEditor
    {
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

            HashSet<string> authoredPrefabPaths = new HashSet<string>();

            ApplyEntries(projectConfig.microscopeEquivalentObjects, AutomaticBackendBindingKind.Microscope, authoredPrefabPaths);
            ApplyEntries(projectConfig.standaloneSharedObjects, AutomaticBackendBindingKind.StandaloneSharedObject, authoredPrefabPaths);
        }

        private static void ApplyEntries(
            List<OrchestrationConfiguredObjectEntry> entries,
            AutomaticBackendBindingKind bindingKind,
            HashSet<string> authoredPrefabPaths)
        {
            if (entries == null)
                return;

            foreach (OrchestrationConfiguredObjectEntry entry in entries)
            {
                if (entry?.prefab == null)
                    continue;

                string prefabAssetPath = AssetDatabase.GetAssetPath(entry.prefab);
                if (string.IsNullOrWhiteSpace(prefabAssetPath))
                    continue;

                if (authoredPrefabPaths != null && !authoredPrefabPaths.Add(prefabAssetPath))
                    continue;

                ApplyBindingToPrefab(prefabAssetPath, bindingKind);
            }
        }

        private static void ApplyBindingToPrefab(string prefabAssetPath, AutomaticBackendBindingKind bindingKind)
        {
            GameObject prefabContents = PrefabUtility.LoadPrefabContents(prefabAssetPath);
            if (prefabContents == null)
                return;

            try
            {
                AutomaticBackendObjectBinding binding = prefabContents.GetComponent<AutomaticBackendObjectBinding>();
                if (binding == null)
                {
                    binding = prefabContents.AddComponent<AutomaticBackendObjectBinding>();
                }

                switch (bindingKind)
                {
                    case AutomaticBackendBindingKind.Microscope:
                        SharedSyncObjectDefinition definition = prefabContents.GetComponent<SharedSyncObjectDefinition>();
                        if (definition == null)
                        {
                            definition = prefabContents.AddComponent<SharedSyncObjectDefinition>();
                            definition.InitializeRuntimeMicroscopeDefaults();
                        }

                        binding.ConfigureForMicroscope(definition);
                        break;

                    case AutomaticBackendBindingKind.StandaloneSharedObject:
                        binding.ConfigureForStandalone();
                        break;
                }

                binding.ApplySelectedBackendInEditor();
                MirrorAuthoringUtilityEditor.EnsurePrefabNetworkIdentityIds(prefabContents, prefabAssetPath);
                EditorUtility.SetDirty(prefabContents);
                PrefabUtility.SaveAsPrefabAsset(prefabContents, prefabAssetPath);
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(prefabContents);
            }
        }
    }
}
