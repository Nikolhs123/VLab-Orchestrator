using Thesis.Orchestration.Backends.Fusion;
using Thesis.Orchestration.Backends.FishNet;
using Thesis.Orchestration.Backends.Mirror;
using Thesis.Orchestration.Backends.Netcode;
using Thesis.Orchestration.Backends.PUN2;
using Thesis.Orchestration.AI;
using Thesis.Orchestration.EditorTools;
using Thesis.Orchestration.Runtime.Scenarios;
using Thesis.Orchestration.SharedSync;
using Thesis.Orchestration.Telemetry;
using Thesis.Orchestration.UI;
using UnityEngine;
using Fusion;
using FishNet.Managing;
using FishNet.Transporting.Tugboat;
using Unity.Netcode.Transports.UTP;
using MirrorNetworkIdentity = global::Mirror.NetworkIdentity;
using MirrorNetworkManager = global::Mirror.NetworkManager;
using KcpTransport = kcp2k.KcpTransport;

#if PHOTON_UNITY_NETWORKING
using Photon.Pun;
#endif

namespace Thesis.Orchestration.Runtime.Bootstrap
{
    public class OrchestrationRuntimeBootstrap : MonoBehaviour
    {
        [SerializeField] private OrchestrationProjectConfig projectConfig;

        private void Awake()
        {
            if (projectConfig == null)
                projectConfig = Resources.Load<OrchestrationProjectConfig>("AIMultiplayerAssistant/OrchestrationProjectConfig");

            if (projectConfig == null)
            {
                Debug.LogWarning("[OrchestrationRuntimeBootstrap] Missing OrchestrationProjectConfig.", this);
                return;
            }

            if (!projectConfig.autoBootstrapOnSceneLoad)
                return;

            EnsureEvaluationMetricsRuntime();
            SpawnAiServiceIfNeeded();
            CleanupRuntimeRootsForOtherBackends();
            ProvisionConfiguredSharedObjects();
            ApplyAutomaticBackendBindings();
            EnsureSharedObjectRegistry();
            SanitizeSharedSceneStateForSelectedBackend();
            SpawnBackendRuntimeIfNeeded();
            SpawnAutomatedScenarioRunnerIfNeeded();
            SpawnTelemetryUiIfNeeded();

            // Command-line two-participant automation (-vlabSession host|client); see CommandLineSessionAutomation.
            if (CommandLineSessionAutomation.IsRequested() && GetComponent<CommandLineSessionAutomation>() == null)
                gameObject.AddComponent<CommandLineSessionAutomation>();

            // The lifecycle driver must survive the runtime objects it observes being torn down on disconnect.
            if (!string.IsNullOrWhiteSpace(projectConfig.LifecycleCase) &&
                FindFirstObjectByType<Thesis.Orchestration.Runtime.Scenarios.LifecycleScenarioDriver>() == null)
            {
                GameObject driverHost = new GameObject("VLabLifecycleScenarioDriver");
                DontDestroyOnLoad(driverHost);
                driverHost.AddComponent<Thesis.Orchestration.Runtime.Scenarios.LifecycleScenarioDriver>();
            }
        }

        private void EnsureEvaluationMetricsRuntime()
        {
            EvaluationMetricsManager manager = EvaluationMetricsManager.ResolveOrCreate(this);
            if (manager == null)
                return;

            string scenarioId = projectConfig.enableAutomatedBenchmarkScenario
                ? projectConfig.ResolveAutomatedScenarioId()
                : null;

            manager.BeginNewRun(scenarioId);
        }

        private void CleanupRuntimeRootsForOtherBackends()
        {
            OrchestrationRuntimeRoot[] runtimeRoots = FindObjectsByType<OrchestrationRuntimeRoot>(
                FindObjectsInactive.Include,
                FindObjectsSortMode.None);

            foreach (OrchestrationRuntimeRoot runtimeRoot in runtimeRoots)
            {
                if (runtimeRoot == null || runtimeRoot.BackendType == projectConfig.backendType)
                    continue;

                Destroy(runtimeRoot.gameObject);
            }

            if (projectConfig.backendType != OrchestrationBackendType.Fusion)
            {
                NetworkRunner[] fusionRunners = FindObjectsByType<NetworkRunner>(
                    FindObjectsInactive.Include,
                    FindObjectsSortMode.None);

                foreach (NetworkRunner runner in fusionRunners)
                {
                    if (runner == null)
                        continue;

                    Destroy(runner.gameObject);
                }
            }

            if (projectConfig.backendType != OrchestrationBackendType.FishNet)
            {
                NetworkManager[] fishNetManagers = FindObjectsByType<NetworkManager>(
                    FindObjectsInactive.Include,
                    FindObjectsSortMode.None);

                foreach (NetworkManager manager in fishNetManagers)
                {
                    if (manager == null)
                        continue;

                    bool initialized = manager.Initialized &&
                                       manager.ClientManager != null &&
                                       manager.ServerManager != null;
                    if (initialized && manager.IsClientStarted)
                        manager.ClientManager.StopConnection();

                    if (initialized && manager.IsServerStarted)
                        manager.ServerManager.StopConnection(false);

                    Destroy(manager.gameObject);
                }
            }

            if (projectConfig.backendType != OrchestrationBackendType.UnityNetcode)
            {
                Unity.Netcode.NetworkManager[] netcodeManagers = FindObjectsByType<Unity.Netcode.NetworkManager>(
                    FindObjectsInactive.Include,
                    FindObjectsSortMode.None);

                foreach (Unity.Netcode.NetworkManager manager in netcodeManagers)
                {
                    if (manager == null)
                        continue;

                    if (manager.IsListening)
                        manager.Shutdown(false);

                    Destroy(manager.gameObject);
                }
            }

            if (projectConfig.backendType != OrchestrationBackendType.Mirror)
            {
                MirrorNetworkManager[] mirrorManagers = FindObjectsByType<MirrorNetworkManager>(
                    FindObjectsInactive.Include,
                    FindObjectsSortMode.None);

                foreach (MirrorNetworkManager manager in mirrorManagers)
                {
                    if (manager == null)
                        continue;

                    if (manager.isNetworkActive)
                    {
                        if (global::Mirror.NetworkServer.active && global::Mirror.NetworkClient.active)
                            manager.StopHost();
                        else if (global::Mirror.NetworkClient.active)
                            manager.StopClient();
                        else if (global::Mirror.NetworkServer.active)
                            manager.StopServer();
                    }

                    Destroy(manager.gameObject);
                }
            }

#if PHOTON_UNITY_NETWORKING
            if (projectConfig.backendType != OrchestrationBackendType.PUN2 &&
                (PhotonNetwork.InRoom || PhotonNetwork.IsConnected))
            {
                if (PhotonNetwork.InRoom)
                    PhotonNetwork.LeaveRoom(false);

                if (PhotonNetwork.IsConnected)
                    PhotonNetwork.Disconnect();
            }
#endif
        }

        private void SanitizeSharedSceneStateForSelectedBackend()
        {
            SharedObjectSyncRegistry sharedRegistry = SharedObjectSyncRegistry.ResolveOrCreate();
            if (sharedRegistry == null)
                return;

            sharedRegistry.SanitizeSharedObjectHierarchiesForBackend(projectConfig.backendType);
            sharedRegistry.DiscoverSceneObjects();
        }

        private static void EnsureSharedObjectRegistry()
        {
            SharedObjectSyncRegistry sharedRegistry = SharedObjectSyncRegistry.ResolveOrCreate();
            sharedRegistry?.DiscoverSceneObjects();
        }

        private void ApplyAutomaticBackendBindings()
        {
            AutomaticBackendObjectBinding[] bindings = FindObjectsByType<AutomaticBackendObjectBinding>(
                FindObjectsInactive.Include,
                FindObjectsSortMode.None);

            foreach (AutomaticBackendObjectBinding binding in bindings)
            {
                if (binding == null)
                    continue;

                binding.ApplySelectedBackend();
            }
        }

        private void ProvisionConfiguredSharedObjects()
        {
            ConfiguredSharedObjectRuntimeProvisioner.EnsureConfiguredObjects(projectConfig);
        }

        private void SpawnBackendRuntimeIfNeeded()
        {
            switch (projectConfig.backendType)
            {
                case OrchestrationBackendType.PUN2:
                    SpawnIfMissing<PUN2BackendRuntimeAdapter>(projectConfig.pun2RuntimeRootPrefab, "PUN2", OrchestrationBackendType.PUN2);
                    break;

                case OrchestrationBackendType.Fusion:
                    SpawnIfMissing<FusionBackendRuntimeAdapter>(projectConfig.fusionRuntimeRootPrefab, "Fusion", OrchestrationBackendType.Fusion);
                    break;

                case OrchestrationBackendType.UnityNetcode:
                    SpawnNetcodeRuntimeIfNeeded();
                    break;

                case OrchestrationBackendType.FishNet:
                    SpawnFishNetRuntimeIfNeeded();
                    break;

                case OrchestrationBackendType.Mirror:
                    SpawnMirrorRuntimeIfNeeded();
                    break;

                default:
                    Debug.LogWarning($"[Bootstrap] Unsupported backend type: {projectConfig.backendType}", this);
                    break;
            }
        }

        private void SpawnAiServiceIfNeeded()
        {
            if (!projectConfig.enableAiOrchestration || projectConfig.aiMode == OrchestrationAiMode.Disabled)
                return;

            OrchestrationAiService existing = FindFirstObjectByType<OrchestrationAiService>();
            if (existing != null)
            {
                existing.Configure(projectConfig);
                return;
            }

            OrchestrationAiService service = gameObject.AddComponent<OrchestrationAiService>();
            service.Configure(projectConfig);
        }

        private void SpawnIfMissing<T>(GameObject prefab, string label, OrchestrationBackendType backendType) where T : Component
        {
            if (FindFirstObjectByType<T>() != null)
                return;

            if (prefab == null)
            {
                Debug.LogWarning($"[Bootstrap] {label} runtime root prefab is null.", this);
                return;
            }

            GameObject go = Instantiate(prefab);
            ConfigureRuntimeRoot(go, backendType);

            if (projectConfig.dontDestroyRuntimeRoots)
                DontDestroyOnLoad(go);
        }

        private void SpawnByComponentNameIfMissing(string componentTypeName, GameObject prefab, string label, OrchestrationBackendType backendType)
        {
            Component existing = FindFirstComponentByTypeName(componentTypeName);
            if (existing != null)
                return;

            if (prefab == null)
            {
                Debug.LogWarning($"[Bootstrap] {label} runtime root prefab is null.", this);
                return;
            }

            GameObject go = Instantiate(prefab);
            ConfigureRuntimeRoot(go, backendType);

            if (projectConfig.dontDestroyRuntimeRoots)
                DontDestroyOnLoad(go);
        }

        private void SpawnFishNetRuntimeIfNeeded()
        {
            if (FindFirstComponentByTypeName("FishNetBackendRuntimeAdapter") != null)
                return;

            if (projectConfig.fishNetRuntimeRootPrefab == null)
            {
                Debug.LogWarning("[Bootstrap] FishNet runtime root prefab is null.", this);
                return;
            }

            GameObject go = Instantiate(projectConfig.fishNetRuntimeRootPrefab);
            ConfigureRuntimeRoot(go, OrchestrationBackendType.FishNet);

            if (projectConfig.dontDestroyRuntimeRoots)
                DontDestroyOnLoad(go);

            NetworkManager networkManager = go.GetComponentInChildren<NetworkManager>(true);
            if (networkManager == null)
            {
                Debug.LogWarning("[Bootstrap] FishNet runtime root is missing FishNet NetworkManager.", go);
                return;
            }

            SessionOrchestratorFishNet orchestrator = go.GetComponentInChildren<SessionOrchestratorFishNet>(true);
            if (orchestrator == null)
                orchestrator = go.AddComponent<SessionOrchestratorFishNet>();

            NetworkAvatarSpawnerFishNet avatarSpawner = go.GetComponentInChildren<NetworkAvatarSpawnerFishNet>(true);
            if (avatarSpawner == null)
                avatarSpawner = go.AddComponent<NetworkAvatarSpawnerFishNet>();

            ConfiguredSharedObjectSpawnerFishNet sharedObjectSpawner =
                go.GetComponentInChildren<ConfiguredSharedObjectSpawnerFishNet>(true);
            if (sharedObjectSpawner == null)
                sharedObjectSpawner = go.AddComponent<ConfiguredSharedObjectSpawnerFishNet>();

            FishNetBackendRuntimeAdapter adapter = go.GetComponentInChildren<FishNetBackendRuntimeAdapter>(true);
            if (adapter == null)
                adapter = go.AddComponent<FishNetBackendRuntimeAdapter>();

            SessionQueueRuntimeFishNet queueTemplate = EnsureFishNetQueueRuntimeTemplate(go.transform);
            Tugboat tugboat = networkManager.GetComponent<Tugboat>();

            orchestrator.ConfigureRuntime(networkManager, tugboat, queueTemplate);
            avatarSpawner.ConfigureRuntime(orchestrator, networkManager);
            sharedObjectSpawner.ConfigureRuntime(networkManager, projectConfig);
            adapter.ConfigureRuntime(orchestrator);
            sharedObjectSpawner.RegisterConfiguredPrefabsIfNeeded();
            orchestrator.RegisterQueueRuntimePrefabIfNeeded();
            avatarSpawner.RegisterSharedAvatarPrefabIfNeeded();
        }

        private void SpawnNetcodeRuntimeIfNeeded()
        {
            Component existingAdapter = FindFirstComponentByTypeName("NetcodeBackendRuntimeAdapter");
            GameObject go = existingAdapter != null ? existingAdapter.transform.root.gameObject : null;

            if (go == null)
            {
                if (projectConfig.unityNetcodeRuntimeRootPrefab == null)
                {
                    Debug.LogWarning("[Bootstrap] UnityNetcode runtime root prefab is null.", this);
                    return;
                }

                go = Instantiate(projectConfig.unityNetcodeRuntimeRootPrefab);
                ConfigureRuntimeRoot(go, OrchestrationBackendType.UnityNetcode);

                if (projectConfig.dontDestroyRuntimeRoots)
                    DontDestroyOnLoad(go);
            }

            ConfigureRuntimeRoot(go, OrchestrationBackendType.UnityNetcode);

            Unity.Netcode.NetworkManager networkManager = go.GetComponentInChildren<Unity.Netcode.NetworkManager>(true);
            if (networkManager == null)
                networkManager = go.GetComponent<Unity.Netcode.NetworkManager>() ?? go.AddComponent<Unity.Netcode.NetworkManager>();

            UnityTransport unityTransport = go.GetComponentInChildren<UnityTransport>(true);
            if (unityTransport == null)
                unityTransport = networkManager.GetComponent<UnityTransport>() ?? networkManager.gameObject.AddComponent<UnityTransport>();

            if (!NetcodeRuntimeUtility.EnsureNetworkManagerConfigInitialized(networkManager))
            {
                Debug.LogWarning("[Bootstrap] Failed to initialize Unity Netcode NetworkConfig.", go);
                return;
            }

            networkManager.NetworkConfig.NetworkTransport = unityTransport;
            networkManager.NetworkConfig.PlayerPrefab = null;

            SessionOrchestratorNetcode orchestrator = go.GetComponentInChildren<SessionOrchestratorNetcode>(true);
            if (orchestrator == null)
                orchestrator = go.AddComponent<SessionOrchestratorNetcode>();

            NetworkAvatarSpawnerNetcode avatarSpawner = go.GetComponentInChildren<NetworkAvatarSpawnerNetcode>(true);
            if (avatarSpawner == null)
                avatarSpawner = go.AddComponent<NetworkAvatarSpawnerNetcode>();

            NetcodeBackendRuntimeAdapter adapter = go.GetComponentInChildren<NetcodeBackendRuntimeAdapter>(true);
            if (adapter == null)
                adapter = go.AddComponent<NetcodeBackendRuntimeAdapter>();

            SessionQueueRuntimeNetcode queueTemplate = ResolveNetcodeQueueRuntimeTemplate();

            orchestrator.ConfigureRuntime(networkManager, unityTransport, queueTemplate);
            avatarSpawner.ConfigureRuntime(orchestrator, networkManager);
            orchestrator.RegisterQueueRuntimePrefabIfNeeded();
            avatarSpawner.RegisterSharedAvatarPrefabIfNeeded();
        }

        private void SpawnMirrorRuntimeIfNeeded()
        {
            if (FindFirstComponentByTypeName("MirrorBackendRuntimeAdapter") != null)
                return;

            GameObject go = projectConfig.mirrorRuntimeRootPrefab != null
                ? Instantiate(projectConfig.mirrorRuntimeRootPrefab)
                : CreateMirrorRuntimeFallbackRoot();

            ConfigureRuntimeRoot(go, OrchestrationBackendType.Mirror);

            if (projectConfig.dontDestroyRuntimeRoots)
                DontDestroyOnLoad(go);

            MirrorNetworkManager networkManager = go.GetComponentInChildren<MirrorNetworkManager>(true);
            if (networkManager == null)
            {
                KcpTransport precreatedTransport = go.GetComponentInChildren<KcpTransport>(true);
                if (precreatedTransport == null)
                    precreatedTransport = go.AddComponent<KcpTransport>();

                networkManager = go.GetComponent<MirrorNetworkManager>();
                if (networkManager == null)
                    networkManager = go.AddComponent<MirrorNetworkManager>();
            }

            KcpTransport transport = go.GetComponentInChildren<KcpTransport>(true);
            if (transport == null)
            {
                transport = networkManager.GetComponent<KcpTransport>();
                if (transport == null)
                    transport = networkManager.gameObject.AddComponent<KcpTransport>();
            }

            networkManager.transport = transport;
            networkManager.autoCreatePlayer = false;
            networkManager.playerPrefab = null;

            SessionOrchestratorMirror orchestrator = go.GetComponentInChildren<SessionOrchestratorMirror>(true);
            if (orchestrator == null)
                orchestrator = go.AddComponent<SessionOrchestratorMirror>();

            NetworkAvatarSpawnerMirror avatarSpawner = go.GetComponentInChildren<NetworkAvatarSpawnerMirror>(true);
            if (avatarSpawner == null)
                avatarSpawner = go.AddComponent<NetworkAvatarSpawnerMirror>();

            ConfiguredSharedObjectSpawnerMirror sharedObjectSpawner =
                go.GetComponentInChildren<ConfiguredSharedObjectSpawnerMirror>(true);
            if (sharedObjectSpawner == null)
                sharedObjectSpawner = go.AddComponent<ConfiguredSharedObjectSpawnerMirror>();

            MirrorBackendRuntimeAdapter adapter = go.GetComponentInChildren<MirrorBackendRuntimeAdapter>(true);
            if (adapter == null)
                adapter = go.AddComponent<MirrorBackendRuntimeAdapter>();

            SessionQueueRuntimeMirror queueTemplate = EnsureMirrorQueueRuntimeTemplate(go.transform);

            orchestrator.ConfigureRuntime(networkManager, transport, queueTemplate);
            avatarSpawner.ConfigureRuntime(orchestrator, networkManager);
            sharedObjectSpawner.ConfigureRuntime(networkManager, projectConfig);
            adapter.ConfigureRuntime(orchestrator);
            sharedObjectSpawner.RegisterSpawnHandlersIfNeeded();
            orchestrator.RegisterQueueRuntimePrefabIfNeeded();
            avatarSpawner.RegisterSharedAvatarPrefabIfNeeded();
        }

        private SessionQueueRuntimeFishNet EnsureFishNetQueueRuntimeTemplate(Transform parent)
        {
            Transform existingChild = parent.Find("FishNetQueueRuntimeTemplate");
            GameObject template = existingChild != null ? existingChild.gameObject : null;

            if (template == null)
            {
                template = new GameObject("FishNetQueueRuntimeTemplate");
                template.transform.SetParent(parent, false);
            }

            template.SetActive(false);

            if (template.GetComponent<FishNet.Object.NetworkObject>() == null)
                template.AddComponent<FishNet.Object.NetworkObject>();

            SessionQueueRuntimeFishNet queueRuntime = template.GetComponent<SessionQueueRuntimeFishNet>();
            if (queueRuntime == null)
                queueRuntime = template.AddComponent<SessionQueueRuntimeFishNet>();

            return queueRuntime;
        }

        private SessionQueueRuntimeMirror EnsureMirrorQueueRuntimeTemplate(Transform parent)
        {
            Transform existingChild = parent.Find("MirrorQueueRuntimeTemplate");
            GameObject template = existingChild != null ? existingChild.gameObject : null;

            if (template == null)
            {
                template = new GameObject("MirrorQueueRuntimeTemplate");
                template.transform.SetParent(parent, false);
            }

            template.SetActive(false);

            if (template.GetComponent<MirrorNetworkIdentity>() == null)
                template.AddComponent<MirrorNetworkIdentity>();

            SessionQueueRuntimeMirror queueRuntime = template.GetComponent<SessionQueueRuntimeMirror>();
            if (queueRuntime == null)
                queueRuntime = template.AddComponent<SessionQueueRuntimeMirror>();

            return queueRuntime;
        }

        private GameObject CreateMirrorRuntimeFallbackRoot()
        {
            GameObject root = new GameObject("MirrorRuntimeRoot");
            KcpTransport transport = root.AddComponent<KcpTransport>();
            MirrorNetworkManager networkManager = root.AddComponent<MirrorNetworkManager>();
            networkManager.transport = transport;
            networkManager.autoCreatePlayer = false;
            networkManager.playerPrefab = null;
            networkManager.dontDestroyOnLoad = false;
            networkManager.runInBackground = true;
            root.AddComponent<SessionOrchestratorMirror>();
            root.AddComponent<NetworkAvatarSpawnerMirror>();
            root.AddComponent<MirrorBackendRuntimeAdapter>();
            EnsureMirrorQueueRuntimeTemplate(root.transform);
            return root;
        }

        private SessionQueueRuntimeNetcode ResolveNetcodeQueueRuntimeTemplate()
        {
            GameObject template = Resources.Load<GameObject>(OrchestrationRuntimePrefabPaths.NetcodeQueueRuntimeResourcesPath);
            return template != null ? template.GetComponent<SessionQueueRuntimeNetcode>() : null;
        }

        private Component FindFirstComponentByTypeName(string typeName)
        {
            MonoBehaviour[] all = FindObjectsByType<MonoBehaviour>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            foreach (var mb in all)
            {
                if (mb == null)
                    continue;

                if (mb.GetType().Name == typeName)
                    return mb;
            }

            return null;
        }

        private void ConfigureRuntimeRoot(GameObject runtimeRoot, OrchestrationBackendType backendType)
        {
            if (runtimeRoot == null)
                return;

            OrchestrationRuntimeRoot rootMarker = runtimeRoot.GetComponent<OrchestrationRuntimeRoot>();
            if (rootMarker == null)
                rootMarker = runtimeRoot.AddComponent<OrchestrationRuntimeRoot>();

            rootMarker.Configure(backendType);
        }

        private void SpawnTelemetryUiIfNeeded()
        {
            if (!projectConfig.spawnTelemetryUI)
                return;

            if (FindFirstObjectByType<Thesis.Orchestration.Telemetry.TelemetryUI>() != null)
                return;

            if (projectConfig.telemetryUiRootPrefab == null)
            {
                if (GetComponent<SoftwareXRuntimeUI>() == null)
                    gameObject.AddComponent<SoftwareXRuntimeUI>();
                return;
            }

            GameObject go = Instantiate(projectConfig.telemetryUiRootPrefab);

            if (projectConfig.dontDestroyRuntimeRoots)
                DontDestroyOnLoad(go);
        }

        private void SpawnAutomatedScenarioRunnerIfNeeded()
        {
            if (!projectConfig.enableAutomatedBenchmarkScenario)
                return;

            OrchestrationScenarioRunner existing = FindFirstObjectByType<OrchestrationScenarioRunner>();
            if (existing != null)
            {
                existing.Configure(projectConfig);
                return;
            }

            OrchestrationScenarioRunner runner = gameObject.AddComponent<OrchestrationScenarioRunner>();
            runner.Configure(projectConfig);
        }
    }
}
