using System.Collections.Generic;
using System.Linq;
using Thesis.Orchestration.EditorTools;
using Unity.Netcode;
using UnityEngine;
using FishNetNetworkObject = FishNet.Object.NetworkObject;
using MirrorNetworkIdentity = global::Mirror.NetworkIdentity;

#if PHOTON_UNITY_NETWORKING
using Photon.Pun;
#endif

using Fusion;

namespace Thesis.Orchestration.SharedSync
{
    /// <summary>
    /// Central registry of all SharedSyncObjectDefinition components in the scene.
    /// </summary>
    public class SharedObjectSyncRegistry : MonoBehaviour
    {
        public static SharedObjectSyncRegistry Instance { get; private set; }

        [Header("Discovery")]
        [SerializeField] private bool autoDiscoverOnAwake = true;
        [SerializeField] private bool includeInactiveObjects = true;

        [Header("Debug")]
        [SerializeField] private bool verboseLogs = true;

        private readonly Dictionary<string, SharedSyncObjectDefinition> objectsById = new();
        private readonly List<SharedSyncObjectDefinition> registeredObjects = new();

        public IReadOnlyList<SharedSyncObjectDefinition> RegisteredObjects => registeredObjects;

        public static SharedObjectSyncRegistry ResolveOrCreate()
        {
            if (Instance != null)
                return Instance;

            SharedObjectSyncRegistry existing = FindFirstObjectByType<SharedObjectSyncRegistry>();
            if (existing != null)
            {
                Instance = existing;
                return existing;
            }

            var registryObject = new GameObject("SharedObjectSyncRegistry (Runtime)");
            return registryObject.AddComponent<SharedObjectSyncRegistry>();
        }

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Debug.LogWarning("[SharedObjectSyncRegistry] Duplicate instance found. Destroying duplicate.", this);
                Destroy(gameObject);
                return;
            }

            Instance = this;

            if (autoDiscoverOnAwake)
            {
                DiscoverSceneObjects();
            }
        }

        [ContextMenu("Discover Scene Objects")]
        public void DiscoverSceneObjects()
        {
            ClearRegistry();

#if UNITY_2023_1_OR_NEWER
            var foundObjects = FindObjectsByType<SharedSyncObjectDefinition>(
                includeInactiveObjects ? FindObjectsInactive.Include : FindObjectsInactive.Exclude,
                FindObjectsSortMode.None);
#else
            var foundObjects = FindObjectsOfType<SharedSyncObjectDefinition>(includeInactiveObjects);
#endif

            foreach (var definition in foundObjects
                .Where(definition => definition != null)
                .OrderByDescending(definition => definition.gameObject.activeInHierarchy)
                .ThenBy(definition => definition.transform.root != null ? definition.transform.root.GetSiblingIndex() : int.MaxValue))
            {
                Register(definition);
            }

            Log($"Discovery completed. Registered {registeredObjects.Count} shared object(s).");
        }

        public bool Register(SharedSyncObjectDefinition definition)
        {
            if (definition == null)
            {
                Log("Register failed: definition is null.");
                return false;
            }

            if (!definition.IsSharedObject)
            {
                Log($"Skipping '{definition.name}' because IsSharedObject is false.");
                return false;
            }

            if (string.IsNullOrWhiteSpace(definition.ObjectId))
            {
                Log($"Register failed: object '{definition.name}' has empty ObjectId.");
                return false;
            }

            if (objectsById.ContainsKey(definition.ObjectId))
            {
                Log($"Register failed: duplicate ObjectId '{definition.ObjectId}'.");
                return false;
            }

            objectsById.Add(definition.ObjectId, definition);
            registeredObjects.Add(definition);

            Log($"Registered shared object '{definition.DisplayName}' with ID '{definition.ObjectId}'.");
            return true;
        }

        public bool Unregister(SharedSyncObjectDefinition definition)
        {
            if (definition == null)
                return false;

            bool removedFromList = registeredObjects.Remove(definition);
            bool removedFromDictionary = objectsById.Remove(definition.ObjectId);

            if (removedFromList || removedFromDictionary)
            {
                Log($"Unregistered shared object '{definition.DisplayName}' with ID '{definition.ObjectId}'.");
                return true;
            }

            return false;
        }

        public SharedSyncObjectDefinition GetById(string objectId)
        {
            if (string.IsNullOrWhiteSpace(objectId))
                return null;

            objectsById.TryGetValue(objectId, out var definition);
            return definition;
        }

        public SharedSyncObjectDefinition GetPreferredQueueControlledObject()
        {
            SharedSyncObjectDefinition activeDefinition = registeredObjects
                .FirstOrDefault(definition =>
                    definition != null &&
                    definition.RequiresQueueControl &&
                    definition.gameObject.activeInHierarchy);

            if (activeDefinition != null)
                return activeDefinition;

            return registeredObjects.FirstOrDefault(definition =>
                definition != null &&
                definition.RequiresQueueControl);
        }

        public SharedSyncObjectDefinition GetPreferredQueueControlledObject(OrchestrationBackendType backendType)
        {
            IEnumerable<SharedSyncObjectDefinition> candidates = registeredObjects
                .Where(definition => definition != null && definition.RequiresQueueControl);

            SharedSyncObjectDefinition matchingActiveDefinition = candidates.FirstOrDefault(definition =>
                definition.gameObject.activeInHierarchy &&
                MatchesBackend(definition, backendType));

            if (matchingActiveDefinition != null)
                return matchingActiveDefinition;

            SharedSyncObjectDefinition matchingDefinition = candidates.FirstOrDefault(definition =>
                MatchesBackend(definition, backendType));

            if (matchingDefinition != null)
                return matchingDefinition;

            return GetPreferredQueueControlledObject();
        }

        public List<SharedSyncObjectDefinition> GetByCategory(SharedObjectCategory category)
        {
            return registeredObjects
                .Where(obj => obj.Category == category)
                .ToList();
        }

        public SharedSyncObjectDefinition GetDefinitionForGameObject(GameObject target)
        {
            if (target == null)
                return null;

            foreach (var definition in registeredObjects)
            {
                if (definition.ContainsObject(target))
                    return definition;
            }

            return null;
        }

        public bool ContainsId(string objectId)
        {
            if (string.IsNullOrWhiteSpace(objectId))
                return false;

            return objectsById.ContainsKey(objectId);
        }

        public bool ContainsGameObject(GameObject target)
        {
            return GetDefinitionForGameObject(target) != null;
        }

        public void ClearRegistry()
        {
            objectsById.Clear();
            registeredObjects.Clear();
        }

        public void SanitizeSharedObjectHierarchiesForBackend(OrchestrationBackendType backendType)
        {
            if (Application.isPlaying)
            {
                Log($"Skipping runtime shared-object sanitation for backend '{backendType}' to keep authored network scene state stable.");
                return;
            }

            SharedSyncObjectDefinition[] definitions =
#if UNITY_2023_1_OR_NEWER
                FindObjectsByType<SharedSyncObjectDefinition>(FindObjectsInactive.Include, FindObjectsSortMode.None);
#else
                FindObjectsOfType<SharedSyncObjectDefinition>(true);
#endif

            foreach (SharedSyncObjectDefinition definition in definitions)
            {
                if (definition == null)
                    continue;

                GameObject root = definition.RootObject != null ? definition.RootObject : definition.gameObject;
                BackendComponentUtility.RemoveForeignBackendComponentsFromHierarchy(root, backendType);
            }
        }

        private void OnDestroy()
        {
            if (Instance == this)
                Instance = null;
        }

        private bool MatchesBackend(SharedSyncObjectDefinition definition, OrchestrationBackendType backendType)
        {
            if (definition == null)
                return false;

            GameObject root = definition.RootObject != null ? definition.RootObject : definition.gameObject;
            if (root == null)
                return false;

            switch (backendType)
            {
                case OrchestrationBackendType.Fusion:
                    return root.GetComponentInChildren<Thesis.Orchestration.Backends.Fusion.MicroscopeAccessControllerFusion>(true) != null ||
                           root.GetComponentInChildren<Fusion.NetworkObject>(true) != null;

                case OrchestrationBackendType.UnityNetcode:
                    return root.GetComponentInChildren<Thesis.Orchestration.Backends.Netcode.MicroscopeAccessControllerNetcode>(true) != null ||
                           root.GetComponentInChildren<Unity.Netcode.NetworkObject>(true) != null;

                case OrchestrationBackendType.PUN2:
#if PHOTON_UNITY_NETWORKING
                    return root.GetComponentInChildren<Thesis.Orchestration.Backends.PUN2.MicroscopeAccessControllerPUN2>(true) != null ||
                           root.GetComponentInChildren<PhotonView>(true) != null;
#else
                    return root.GetComponentInChildren<Thesis.Orchestration.Backends.PUN2.MicroscopeAccessControllerPUN2>(true) != null;
#endif

                case OrchestrationBackendType.FishNet:
                    return root.GetComponentInChildren<Thesis.Orchestration.Backends.FishNet.MicroscopeAccessControllerFishNet>(true) != null ||
                           root.GetComponentInChildren<FishNetNetworkObject>(true) != null;

                case OrchestrationBackendType.Mirror:
                    return root.GetComponentInChildren<Thesis.Orchestration.Backends.Mirror.MicroscopeAccessControllerMirror>(true) != null ||
                           root.GetComponentInChildren<MirrorNetworkIdentity>(true) != null;

                default:
                    return false;
            }
        }

        private void Log(string message)
        {
            if (!verboseLogs)
                return;

            Debug.Log($"[SharedObjectSyncRegistry] {message}", this);
        }
    }
}
