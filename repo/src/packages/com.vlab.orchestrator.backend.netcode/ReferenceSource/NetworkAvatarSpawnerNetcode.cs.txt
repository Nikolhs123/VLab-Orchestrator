using System.Collections.Generic;
using Thesis.Orchestration.SharedSync;
using Unity.Netcode;
using UnityEngine;

namespace Thesis.Orchestration.Backends.Netcode
{
    public class NetworkAvatarSpawnerNetcode : MonoBehaviour
    {
        public static NetworkAvatarSpawnerNetcode Instance { get; private set; }

        [Header("References")]
        [SerializeField] private SessionOrchestratorNetcode sessionOrchestrator;
        [SerializeField] private Unity.Netcode.NetworkManager networkManager;

        [Header("Prefab")]
        [SerializeField] private NetworkObject avatarPrefab;
        [SerializeField] private bool spawnAutomaticallyOnClientConnected = true;

        [Header("Debug")]
        [SerializeField] private bool verboseLogs = true;

        private readonly Dictionary<ulong, NetworkObject> spawnedAvatars = new();
        private bool callbacksBound;

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }

            Instance = this;
            ResolveReferences();
        }

        private void OnEnable()
        {
            ResolveReferences();
            TryBindCallbacks();
        }

        private void OnDisable()
        {
            UnbindCallbacks();
        }

        private void OnDestroy()
        {
            UnbindCallbacks();
            spawnedAvatars.Clear();

            if (Instance == this)
                Instance = null;
        }

        private void Update()
        {
            ResolveReferences();
            TryBindCallbacks();
            PruneSpawnedAvatars();
        }

        private void ResolveReferences()
        {
            if (sessionOrchestrator == null)
                sessionOrchestrator = SessionOrchestratorNetcode.Instance;

            if (sessionOrchestrator == null)
                sessionOrchestrator = FindFirstObjectByType<SessionOrchestratorNetcode>();

            if (networkManager == null && sessionOrchestrator != null)
                networkManager = sessionOrchestrator.NetworkManager;

            if (networkManager == null)
                networkManager = FindFirstObjectByType<Unity.Netcode.NetworkManager>();
        }

        private void TryBindCallbacks()
        {
            if (callbacksBound || networkManager == null)
                return;

            networkManager.OnClientConnectedCallback += HandleClientConnected;
            networkManager.OnClientDisconnectCallback += HandleClientDisconnected;
            callbacksBound = true;
        }

        private void UnbindCallbacks()
        {
            if (!callbacksBound || networkManager == null)
                return;

            networkManager.OnClientConnectedCallback -= HandleClientConnected;
            networkManager.OnClientDisconnectCallback -= HandleClientDisconnected;
            callbacksBound = false;
        }

        private void HandleClientConnected(ulong clientId)
        {
            if (!spawnAutomaticallyOnClientConnected)
                return;

            TrySpawnAvatar(clientId);
        }

        private void HandleClientDisconnected(ulong clientId)
        {
            spawnedAvatars.Remove(clientId);
        }

        public void RequestSpawnForLocalClient()
        {
            ResolveReferences();
            RegisterSharedAvatarPrefabIfNeeded();

            if (networkManager == null)
            {
                Log("Local spawn request skipped because NetworkManager is missing.");
                return;
            }

            if (!networkManager.IsServer)
            {
                Log("Local spawn request skipped because only the server/host can spawn NGO avatars.");
                return;
            }

            TrySpawnAvatar(networkManager.LocalClientId);
        }

        public void ConfigureRuntime(SessionOrchestratorNetcode orchestrator, Unity.Netcode.NetworkManager manager)
        {
            sessionOrchestrator = orchestrator;
            networkManager = manager;
            ResolveReferences();
        }

        public void RegisterSharedAvatarPrefabIfNeeded()
        {
            ResolveReferences();

            if (networkManager == null)
                return;

            if (!NetcodeRuntimeUtility.EnsureNetworkManagerConfigInitialized(networkManager))
                return;

            NetworkObject prefabToRegister = ResolveAvatarPrefab();
            if (prefabToRegister == null)
                return;

            bool registered = NetcodeRuntimeUtility.EnsureNetworkPrefabRegistered(networkManager, prefabToRegister.gameObject);
            if (registered)
                Log($"Registered Netcode avatar prefab '{prefabToRegister.name}'.");
        }

        public void TrySpawnAvatar(ulong clientId)
        {
            ResolveReferences();
            PruneSpawnedAvatars();
            RegisterSharedAvatarPrefabIfNeeded();

            NetworkObject prefabToSpawn = ResolveAvatarPrefab();
            if (prefabToSpawn == null)
            {
                Log("Spawn skipped because avatar prefab is missing or invalid.");
                return;
            }

            if (networkManager == null || !networkManager.IsServer || networkManager.SpawnManager == null)
            {
                Log($"Spawn skipped for Client_{clientId} because NetworkManager is not ready for server-side spawning.");
                return;
            }

            NetworkObject existingPlayerObject = networkManager.SpawnManager.GetPlayerNetworkObject(clientId);
            if (existingPlayerObject != null)
            {
                spawnedAvatars[clientId] = existingPlayerObject;
                Log($"Spawn skipped for Client_{clientId} because a player object already exists.");
                return;
            }

            if (spawnedAvatars.TryGetValue(clientId, out NetworkObject cachedAvatar) &&
                cachedAvatar != null &&
                cachedAvatar.IsSpawned)
            {
                Log($"Spawn skipped for Client_{clientId} because a cached avatar is still spawned.");
                return;
            }

            spawnedAvatars.Remove(clientId);

            GameObject instance = Instantiate(prefabToSpawn.gameObject, Vector3.zero, Quaternion.identity);
            NetworkObject avatarObject = instance.GetComponent<NetworkObject>();
            avatarObject.SpawnAsPlayerObject(clientId, true);

            spawnedAvatars[clientId] = avatarObject;
            Log($"Spawned Netcode avatar for Client_{clientId}.");
        }

        private NetworkObject ResolveAvatarPrefab()
        {
            string preferredPath = SharedAvatarRuntimeUtility.ResolveAvatarPrefabResourcesPath();
            GameObject preferred = Resources.Load<GameObject>(preferredPath);
            if (preferred != null)
            {
                NetworkObject preferredNetworkObject = preferred.GetComponent<NetworkObject>();
                if (IsNetcodeAvatarPrefabValid(preferredNetworkObject))
                {
                    avatarPrefab = preferredNetworkObject;
                    return avatarPrefab;
                }
            }

            if (IsNetcodeAvatarPrefabValid(avatarPrefab))
                return avatarPrefab;

            GameObject loaded = Resources.Load<GameObject>(SharedAvatarRuntimeUtility.DefaultAvatarPrefabResourcesPath);
            avatarPrefab = loaded != null ? loaded.GetComponent<NetworkObject>() : null;
            return IsNetcodeAvatarPrefabValid(avatarPrefab) ? avatarPrefab : null;
        }

        private static bool IsNetcodeAvatarPrefabValid(NetworkObject candidate)
        {
            if (candidate == null)
                return false;

            return candidate.GetComponent<UserPresenceTransformSyncNetcode>() != null &&
                   SharedAvatarRuntimeUtility.HasCompatibleFollowAnchor(candidate.gameObject);
        }

        private void PruneSpawnedAvatars()
        {
            if (spawnedAvatars.Count == 0)
                return;

            List<ulong> staleClientIds = null;
            foreach (KeyValuePair<ulong, NetworkObject> pair in spawnedAvatars)
            {
                if (pair.Value != null && pair.Value.IsSpawned)
                    continue;

                staleClientIds ??= new List<ulong>();
                staleClientIds.Add(pair.Key);
            }

            if (staleClientIds == null)
                return;

            foreach (ulong clientId in staleClientIds)
                spawnedAvatars.Remove(clientId);
        }

        private void Log(string message)
        {
            if (!verboseLogs)
                return;

            Debug.Log($"[NetworkAvatarSpawnerNetcode] {message}", this);
        }
    }
}
