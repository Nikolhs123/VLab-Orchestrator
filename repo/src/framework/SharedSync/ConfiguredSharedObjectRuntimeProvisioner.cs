using System.Collections.Generic;
using Thesis.Orchestration.EditorTools;
using UnityEngine;

namespace Thesis.Orchestration.SharedSync
{
    public static class ConfiguredSharedObjectRuntimeProvisioner
    {
        public static void EnsureConfiguredObjects(OrchestrationProjectConfig projectConfig)
        {
            if (projectConfig == null)
                return;

            HashSet<GameObject> reservedTargets = new HashSet<GameObject>();

            EnsureConfiguredObjects(
                projectConfig.microscopeEquivalentObjects,
                AutomaticBackendBindingKind.Microscope,
                projectConfig.backendType,
                reservedTargets);
            EnsureConfiguredObjects(
                projectConfig.standaloneSharedObjects,
                AutomaticBackendBindingKind.StandaloneSharedObject,
                projectConfig.backendType,
                reservedTargets);
        }

        private static void EnsureConfiguredObjects(
            List<OrchestrationConfiguredObjectEntry> entries,
            AutomaticBackendBindingKind bindingKind,
            OrchestrationBackendType backendType,
            HashSet<GameObject> reservedTargets)
        {
            if (entries == null || entries.Count == 0)
                return;

            foreach (OrchestrationConfiguredObjectEntry entry in entries)
            {
                if (entry == null)
                    continue;

                GameObject target = ResolveOrCreateTarget(entry, backendType, reservedTargets);
                if (target == null)
                    continue;

                reservedTargets.Add(target);

                switch (bindingKind)
                {
                    case AutomaticBackendBindingKind.Microscope:
                        EnsureMicroscopeTarget(target);
                        break;

                    case AutomaticBackendBindingKind.StandaloneSharedObject:
                        EnsureStandaloneTarget(target);
                        break;
                }
            }
        }

        private static void EnsureMicroscopeTarget(GameObject target)
        {
            if (target == null)
                return;

            MarkSoftwareXControlledPart(target.transform);

            SharedSyncObjectDefinition definition = target.GetComponent<SharedSyncObjectDefinition>();
            if (definition == null)
            {
                definition = target.AddComponent<SharedSyncObjectDefinition>();
                definition.InitializeRuntimeMicroscopeDefaults();
            }

            AutomaticBackendObjectBinding binding = target.GetComponent<AutomaticBackendObjectBinding>();
            if (binding == null)
                binding = target.AddComponent<AutomaticBackendObjectBinding>();

            binding.ConfigureForMicroscope(definition);
        }

        private static void MarkSoftwareXControlledPart(Transform root)
        {
            if (root == null)
                return;

            Transform[] children = root.GetComponentsInChildren<Transform>(true);
            foreach (Transform child in children)
            {
                if (child == null || !string.Equals(child.name, "Controlled Turret"))
                    continue;

                if (child.GetComponent<SoftwareXControllablePart>() == null)
                    child.gameObject.AddComponent<SoftwareXControllablePart>();
                return;
            }
        }

        private static void EnsureStandaloneTarget(GameObject target)
        {
            if (target == null)
                return;

            AutomaticBackendObjectBinding binding = target.GetComponent<AutomaticBackendObjectBinding>();
            if (binding == null)
                binding = target.AddComponent<AutomaticBackendObjectBinding>();

            binding.ConfigureForStandalone();
        }

        private static GameObject ResolveOrCreateTarget(
            OrchestrationConfiguredObjectEntry entry,
            OrchestrationBackendType backendType,
            HashSet<GameObject> reservedTargets)
        {
            GameObject target = FindSceneObject(entry, reservedTargets);
            if (target != null)
                return target;

            if (!entry.spawnIfMissing || entry.prefab == null)
                return null;

            // FishNet and Mirror must create configured prefabs through their server spawn
            // pipelines. A local clone would have no network identity and could be mistaken
            // for the synchronized benchmark object.
            if (backendType == OrchestrationBackendType.FishNet ||
                backendType == OrchestrationBackendType.Mirror)
            {
                return null;
            }

            GameObject instance = Object.Instantiate(
                entry.prefab,
                entry.spawnPosition,
                Quaternion.Euler(entry.spawnEulerAngles));

            instance.name = entry.prefab.name;
            return instance;
        }

        private static GameObject FindSceneObject(
            OrchestrationConfiguredObjectEntry entry,
            HashSet<GameObject> reservedTargets)
        {
            if (!string.IsNullOrWhiteSpace(entry.sceneObjectPathOrName))
            {
                GameObject directMatch = GameObject.Find(entry.sceneObjectPathOrName);
                if (IsUsableSceneTarget(directMatch, reservedTargets))
                    return directMatch;

                GameObject namedMatch = FindSceneObjectByName(entry.sceneObjectPathOrName, reservedTargets);
                if (namedMatch != null)
                    return namedMatch;
            }

            if (entry.prefab == null)
                return null;

            return FindSceneObjectByName(entry.prefab.name, reservedTargets);
        }

        private static GameObject FindSceneObjectByName(string targetName, HashSet<GameObject> reservedTargets)
        {
            if (string.IsNullOrWhiteSpace(targetName))
                return null;

            Transform[] allTransforms = Object.FindObjectsByType<Transform>(
                FindObjectsInactive.Include,
                FindObjectsSortMode.None);

            foreach (Transform candidate in allTransforms)
            {
                if (candidate == null)
                    continue;

                GameObject candidateObject = candidate.gameObject;
                if (!string.Equals(candidateObject.name, targetName))
                    continue;

                if (!IsUsableSceneTarget(candidateObject, reservedTargets))
                    continue;

                return candidateObject;
            }

            return null;
        }

        private static bool IsUsableSceneTarget(GameObject target, HashSet<GameObject> reservedTargets)
        {
            if (target == null)
                return false;

            if (!target.scene.IsValid())
                return false;

            return reservedTargets == null || !reservedTargets.Contains(target);
        }
    }
}
