using System;
using System.Collections.Generic;
using FishNet.Managing;
using FishNet.Object;
using Thesis.Orchestration.EditorTools;
using Thesis.Orchestration.SharedSync;
using UnityEngine;

namespace Thesis.Orchestration.Backends.FishNet
{
    [DisallowMultipleComponent]
    public sealed class ConfiguredSharedObjectSpawnerFishNet : MonoBehaviour
    {
        private sealed class SpawnDescriptor
        {
            public string Key;
            public OrchestrationConfiguredObjectEntry Entry;
            public NetworkObject Prefab;
        }

        public static ConfiguredSharedObjectSpawnerFishNet Instance { get; private set; }

        [SerializeField] private NetworkManager networkManager;
        [SerializeField] private OrchestrationProjectConfig projectConfig;
        [SerializeField] private bool verboseLogs = true;

        private readonly List<SpawnDescriptor> descriptors = new();
        private readonly Dictionary<string, NetworkObject> spawnedObjects = new();
        private bool descriptorsBuilt;

        private bool ManagerReady =>
            networkManager != null &&
            networkManager.Initialized &&
            networkManager.ClientManager != null &&
            networkManager.ServerManager != null &&
            networkManager.SpawnablePrefabs != null;

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }

            Instance = this;
            ResolveReferences();
            RegisterConfiguredPrefabsIfNeeded();
        }

        private void Update()
        {
            ResolveReferences();
            if (!ManagerReady)
                return;

            RegisterConfiguredPrefabsIfNeeded();
            if (networkManager.IsServerStarted)
                EnsureServerObjectsSpawned();
        }

        private void OnDestroy()
        {
            if (Instance == this)
                Instance = null;
        }

        public void ConfigureRuntime(NetworkManager manager, OrchestrationProjectConfig config)
        {
            networkManager = manager;
            projectConfig = config;
            descriptorsBuilt = false;
            BuildDescriptors();
            RegisterConfiguredPrefabsIfNeeded();
        }

        public void RegisterConfiguredPrefabsIfNeeded()
        {
            ResolveReferences();
            BuildDescriptors();
            if (!ManagerReady)
                return;

            foreach (SpawnDescriptor descriptor in descriptors)
            {
                bool registered = false;
                int count = networkManager.SpawnablePrefabs.GetObjectCount();
                for (int index = 0; index < count; index++)
                {
                    if (networkManager.SpawnablePrefabs.GetObject(true, index) != descriptor.Prefab)
                        continue;

                    registered = true;
                    break;
                }

                if (!registered)
                {
                    networkManager.SpawnablePrefabs.AddObject(
                        descriptor.Prefab,
                        checkForDuplicates: true,
                        initializeAdded: true);
                    Log($"Registered configured shared prefab '{descriptor.Prefab.name}'.");
                }
            }
        }

        public void EnsureServerObjectsSpawned()
        {
            ResolveReferences();
            RegisterConfiguredPrefabsIfNeeded();
            if (!ManagerReady || !networkManager.IsServerStarted)
                return;

            bool registryChanged = false;
            foreach (SpawnDescriptor descriptor in descriptors)
            {
                if (spawnedObjects.TryGetValue(descriptor.Key, out NetworkObject tracked) &&
                    tracked != null &&
                    tracked.IsSpawned)
                {
                    continue;
                }

                NetworkObject existing = FindExistingSceneInstance(descriptor);
                if (existing != null)
                {
                    if (!existing.IsSpawned && !string.IsNullOrWhiteSpace(descriptor.Entry.sceneObjectPathOrName))
                        Log($"Configured scene object '{existing.name}' exists but is not FishNet-spawned.");

                    continue;
                }

                NetworkObject instance = networkManager.GetPooledInstantiated(
                    descriptor.Prefab,
                    descriptor.Entry.spawnPosition,
                    Quaternion.Euler(descriptor.Entry.spawnEulerAngles),
                    true);
                if (instance == null)
                {
                    Log($"Could not instantiate configured shared prefab '{descriptor.Prefab.name}'.");
                    continue;
                }

                instance.name = descriptor.Entry.prefab.name;
                networkManager.ServerManager.Spawn(instance);
                if (!instance.IsSpawned)
                {
                    Destroy(instance.gameObject);
                    Log($"FishNet refused to spawn configured shared prefab '{descriptor.Prefab.name}'.");
                    continue;
                }

                spawnedObjects[descriptor.Key] = instance;
                registryChanged = true;
                Log($"Spawned configured shared object '{instance.name}'.");
            }

            if (registryChanged)
                SharedObjectSyncRegistry.ResolveOrCreate()?.DiscoverSceneObjects();
        }

        public void DespawnServerObjects()
        {
            if (!ManagerReady || !networkManager.IsServerStarted)
            {
                spawnedObjects.Clear();
                return;
            }

            foreach (NetworkObject networkObject in spawnedObjects.Values)
            {
                if (networkObject != null && networkObject.IsSpawned)
                    networkManager.ServerManager.Despawn(networkObject.gameObject);
            }

            spawnedObjects.Clear();
            SharedObjectSyncRegistry.ResolveOrCreate()?.DiscoverSceneObjects();
        }

        private void ResolveReferences()
        {
            if (projectConfig == null)
            {
                projectConfig = Resources.Load<OrchestrationProjectConfig>(
                    "AIMultiplayerAssistant/OrchestrationProjectConfig");
            }

            if (networkManager == null)
                networkManager = FindFirstObjectByType<NetworkManager>();
        }

        private void BuildDescriptors()
        {
            if (descriptorsBuilt || projectConfig == null)
                return;

            descriptorsBuilt = true;
            descriptors.Clear();
            AddDescriptors(projectConfig.microscopeEquivalentObjects, "Microscope");
            AddDescriptors(projectConfig.standaloneSharedObjects, "Standalone");
        }

        private void AddDescriptors(List<OrchestrationConfiguredObjectEntry> entries, string category)
        {
            if (entries == null)
                return;

            for (int index = 0; index < entries.Count; index++)
            {
                OrchestrationConfiguredObjectEntry entry = entries[index];
                if (entry == null || !entry.spawnIfMissing || entry.prefab == null)
                    continue;

                NetworkObject prefab = entry.prefab.GetComponent<NetworkObject>();
                if (prefab == null)
                {
                    Log($"Configured prefab '{entry.prefab.name}' has no FishNet NetworkObject.");
                    continue;
                }

                descriptors.Add(new SpawnDescriptor
                {
                    Key = $"{category}:{index}:{entry.prefab.name}",
                    Entry = entry,
                    Prefab = prefab
                });
            }
        }

        private static NetworkObject FindExistingSceneInstance(SpawnDescriptor descriptor)
        {
            string targetName = !string.IsNullOrWhiteSpace(descriptor.Entry.sceneObjectPathOrName)
                ? descriptor.Entry.sceneObjectPathOrName
                : descriptor.Entry.prefab.name;

            if (!string.IsNullOrWhiteSpace(descriptor.Entry.sceneObjectPathOrName))
            {
                GameObject direct = GameObject.Find(descriptor.Entry.sceneObjectPathOrName);
                NetworkObject directObject = direct != null ? direct.GetComponent<NetworkObject>() : null;
                if (directObject != null && direct.scene.IsValid())
                    return directObject;
            }

            NetworkObject[] candidates = FindObjectsByType<NetworkObject>(
                FindObjectsInactive.Include,
                FindObjectsSortMode.None);
            foreach (NetworkObject candidate in candidates)
            {
                if (candidate == null || !candidate.gameObject.scene.IsValid())
                    continue;

                if (string.Equals(candidate.name, targetName, StringComparison.Ordinal) ||
                    string.Equals(candidate.name, descriptor.Entry.prefab.name, StringComparison.Ordinal))
                {
                    return candidate;
                }
            }

            return null;
        }

        private void Log(string message)
        {
            if (verboseLogs)
                Debug.Log($"[ConfiguredSharedObjectSpawnerFishNet] {message}", this);
        }
    }
}
