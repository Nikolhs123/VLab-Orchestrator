using System;
using System.Collections.Generic;
using Thesis.Orchestration.EditorTools;
using Thesis.Orchestration.SharedSync;
using UnityEngine;
using MirrorNetworkClient = global::Mirror.NetworkClient;
using MirrorNetworkIdentity = global::Mirror.NetworkIdentity;
using MirrorNetworkManager = global::Mirror.NetworkManager;
using MirrorNetworkServer = global::Mirror.NetworkServer;
using MirrorSpawnMessage = global::Mirror.SpawnMessage;

namespace Thesis.Orchestration.Backends.Mirror
{
    [DisallowMultipleComponent]
    public sealed class ConfiguredSharedObjectSpawnerMirror : MonoBehaviour
    {
        private sealed class SpawnDescriptor
        {
            public string Key;
            public uint AssetId;
            public OrchestrationConfiguredObjectEntry Entry;
        }

        public static ConfiguredSharedObjectSpawnerMirror Instance { get; private set; }

        [SerializeField] private MirrorNetworkManager networkManager;
        [SerializeField] private OrchestrationProjectConfig projectConfig;
        [SerializeField] private bool verboseLogs = true;

        private readonly List<SpawnDescriptor> descriptors = new();
        private readonly Dictionary<string, MirrorNetworkIdentity> spawnedObjects = new();
        private readonly HashSet<uint> registeredAssetIds = new();
        private bool descriptorsBuilt;
        private bool handlersRegistered;

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }

            Instance = this;
            ResolveReferences();
            RegisterSpawnHandlersIfNeeded();
        }

        private void Update()
        {
            ResolveReferences();
            RegisterSpawnHandlersIfNeeded();

            if (MirrorNetworkServer.active)
                EnsureServerObjectsSpawned();
        }

        private void OnDestroy()
        {
            UnregisterSpawnHandlers();

            if (Instance == this)
                Instance = null;
        }

        public void ConfigureRuntime(MirrorNetworkManager manager, OrchestrationProjectConfig config)
        {
            UnregisterSpawnHandlers();
            networkManager = manager;
            projectConfig = config;
            descriptorsBuilt = false;
            BuildDescriptors();
            RegisterSpawnHandlersIfNeeded();
        }

        public void RegisterSpawnHandlersIfNeeded()
        {
            ResolveReferences();
            BuildDescriptors();
            if (handlersRegistered)
                return;

            foreach (SpawnDescriptor descriptor in descriptors)
            {
                SpawnDescriptor captured = descriptor;
                MirrorNetworkClient.RegisterSpawnHandler(
                    descriptor.AssetId,
                    (MirrorSpawnMessage message) => SpawnClientObject(captured, message),
                    UnspawnClientObject);
                registeredAssetIds.Add(descriptor.AssetId);
            }

            handlersRegistered = true;
        }

        public void EnsureServerObjectsSpawned()
        {
            RegisterSpawnHandlersIfNeeded();
            if (!MirrorNetworkServer.active)
                return;

            bool registryChanged = false;
            foreach (SpawnDescriptor descriptor in descriptors)
            {
                if (spawnedObjects.TryGetValue(descriptor.Key, out MirrorNetworkIdentity tracked) &&
                    tracked != null &&
                    tracked.netId != 0)
                {
                    continue;
                }

                MirrorNetworkIdentity existing = FindExistingSceneInstance(descriptor);
                if (existing != null)
                {
                    if (existing.netId == 0 && !string.IsNullOrWhiteSpace(descriptor.Entry.sceneObjectPathOrName))
                        Log($"Configured scene object '{existing.name}' exists but is not Mirror-spawned.");

                    continue;
                }

                GameObject instance = Instantiate(
                    descriptor.Entry.prefab,
                    descriptor.Entry.spawnPosition,
                    Quaternion.Euler(descriptor.Entry.spawnEulerAngles));
                instance.name = descriptor.Entry.prefab.name;

                MirrorNetworkIdentity identity = instance.GetComponent<MirrorNetworkIdentity>();
                if (identity == null)
                {
                    Destroy(instance);
                    Log($"Configured prefab '{descriptor.Entry.prefab.name}' has no Mirror NetworkIdentity.");
                    continue;
                }

                MirrorNetworkServer.Spawn(instance, descriptor.AssetId);
                if (identity.netId == 0)
                {
                    Destroy(instance);
                    Log($"Mirror refused to spawn configured shared prefab '{descriptor.Entry.prefab.name}'.");
                    continue;
                }

                spawnedObjects[descriptor.Key] = identity;
                registryChanged = true;
                Log($"Spawned configured shared object '{instance.name}'.");
            }

            if (registryChanged)
                SharedObjectSyncRegistry.ResolveOrCreate()?.DiscoverSceneObjects();
        }

        public void DespawnServerObjects()
        {
            if (MirrorNetworkServer.active)
            {
                foreach (MirrorNetworkIdentity identity in spawnedObjects.Values)
                {
                    if (identity != null && identity.netId != 0)
                        MirrorNetworkServer.Destroy(identity.gameObject);
                }
            }

            spawnedObjects.Clear();
            SharedObjectSyncRegistry.ResolveOrCreate()?.DiscoverSceneObjects();
        }

        public void ResetForNextSession()
        {
            spawnedObjects.Clear();
            UnregisterSpawnHandlers();
        }

        private GameObject SpawnClientObject(SpawnDescriptor descriptor, MirrorSpawnMessage message)
        {
            GameObject instance = Instantiate(
                descriptor.Entry.prefab,
                message.position,
                message.rotation);
            instance.name = descriptor.Entry.prefab.name;
            instance.transform.localScale = message.scale;

            SharedObjectSyncRegistry.ResolveOrCreate()?.DiscoverSceneObjects();
            return instance;
        }

        private static void UnspawnClientObject(GameObject spawned)
        {
            if (spawned != null)
                Destroy(spawned);

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
                networkManager = FindFirstObjectByType<MirrorNetworkManager>();
        }

        private void BuildDescriptors()
        {
            if (descriptorsBuilt || projectConfig == null)
                return;

            descriptorsBuilt = true;
            descriptors.Clear();
            var usedIds = new HashSet<uint> { SessionQueueRuntimeMirror.SpawnAssetId };
            AddKnownPrefabAssetId(projectConfig.networkAvatarSourcePrefab, usedIds);
            if (networkManager != null && networkManager.spawnPrefabs != null)
            {
                foreach (GameObject spawnPrefab in networkManager.spawnPrefabs)
                    AddKnownPrefabAssetId(spawnPrefab, usedIds);
            }

            AddDescriptors(projectConfig.microscopeEquivalentObjects, "Microscope", usedIds);
            AddDescriptors(projectConfig.standaloneSharedObjects, "Standalone", usedIds);
        }

        private static void AddKnownPrefabAssetId(GameObject prefab, HashSet<uint> usedIds)
        {
            if (prefab == null || usedIds == null)
                return;

            MirrorNetworkIdentity identity = prefab.GetComponent<MirrorNetworkIdentity>();
            if (identity != null && identity.assetId != 0)
                usedIds.Add(identity.assetId);
        }

        private void AddDescriptors(
            List<OrchestrationConfiguredObjectEntry> entries,
            string category,
            HashSet<uint> usedIds)
        {
            if (entries == null)
                return;

            for (int index = 0; index < entries.Count; index++)
            {
                OrchestrationConfiguredObjectEntry entry = entries[index];
                if (entry == null || !entry.spawnIfMissing || entry.prefab == null)
                    continue;

                if (entry.prefab.GetComponent<MirrorNetworkIdentity>() == null)
                {
                    Log($"Configured prefab '{entry.prefab.name}' has no Mirror NetworkIdentity.");
                    continue;
                }

                string key = $"{category}:{index}:{entry.prefab.name}";
                uint assetId = StableAssetId($"VLabOrchestrator:Mirror:{key}");
                while (assetId == 0 || usedIds.Contains(assetId))
                    assetId++;

                usedIds.Add(assetId);
                descriptors.Add(new SpawnDescriptor
                {
                    Key = key,
                    AssetId = assetId,
                    Entry = entry
                });
            }
        }

        private static uint StableAssetId(string value)
        {
            unchecked
            {
                uint hash = 2166136261;
                foreach (char character in value)
                {
                    hash ^= character;
                    hash *= 16777619;
                }

                return hash == 0 ? 1u : hash;
            }
        }

        private static MirrorNetworkIdentity FindExistingSceneInstance(SpawnDescriptor descriptor)
        {
            string targetName = !string.IsNullOrWhiteSpace(descriptor.Entry.sceneObjectPathOrName)
                ? descriptor.Entry.sceneObjectPathOrName
                : descriptor.Entry.prefab.name;

            if (!string.IsNullOrWhiteSpace(descriptor.Entry.sceneObjectPathOrName))
            {
                GameObject direct = GameObject.Find(descriptor.Entry.sceneObjectPathOrName);
                MirrorNetworkIdentity directIdentity = direct != null
                    ? direct.GetComponent<MirrorNetworkIdentity>()
                    : null;
                if (directIdentity != null && direct.scene.IsValid())
                    return directIdentity;
            }

            MirrorNetworkIdentity[] candidates = FindObjectsByType<MirrorNetworkIdentity>(
                FindObjectsInactive.Include,
                FindObjectsSortMode.None);
            foreach (MirrorNetworkIdentity candidate in candidates)
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

        private void UnregisterSpawnHandlers()
        {
            if (!handlersRegistered)
                return;

            foreach (uint assetId in registeredAssetIds)
                MirrorNetworkClient.UnregisterSpawnHandler(assetId);

            registeredAssetIds.Clear();
            handlersRegistered = false;
        }

        private void Log(string message)
        {
            if (verboseLogs)
                Debug.Log($"[ConfiguredSharedObjectSpawnerMirror] {message}", this);
        }
    }
}
