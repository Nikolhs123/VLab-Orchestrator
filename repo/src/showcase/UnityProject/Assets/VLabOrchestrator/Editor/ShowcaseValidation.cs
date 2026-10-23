using System;
using System.IO;
using System.Linq;
using System.Reflection;
using Thesis.Orchestration.Core;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEngine;
using VLabOrchestrator.Runtime;

namespace VLabOrchestrator.Showcase.Editor
{
    public static class ShowcaseValidation
    {
        private const string ScenePath = "Assets/Scenes/Showcase.unity";
        private const string GeneratedRuntimeRoot = "Assets/GeneratedSoftwareXRuntime";
        private const string RuntimeConfigPath =
            "Assets/Resources/AIMultiplayerAssistant/OrchestrationProjectConfig.asset";
        private const string PunSettingsPath =
            "Assets/Photon/PhotonUnityNetworking/Resources/PhotonServerSettings.asset";
        private const string FusionSettingsPath =
            "Assets/Photon/Fusion/Resources/PhotonAppSettings.asset";
        private const string FusionUnityRuntimePath =
            "Assets/Photon/Fusion/Runtime/Fusion.Unity.cs";
        private const string CurrentBenchmarkProtocolVersion =
            OrchestrationProjectConfig.CanonicalBenchmarkProtocolVersion;

        [MenuItem("VLab Orchestrator/Validate Showcase")]
        public static void Validate()
        {
            if (!File.Exists(ScenePath))
                throw new FileNotFoundException("Showcase scene was not found.", ScenePath);

            var scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            if (!scene.IsValid())
                throw new InvalidOperationException("Showcase scene could not be opened.");

            ShowcaseSharedInstrumentDefinition instrument =
                UnityEngine.Object.FindAnyObjectByType<ShowcaseSharedInstrumentDefinition>();
            Require(instrument != null, "Authored shared instrument definition was not found.");
            Require(instrument.ControlledPart != null, "Controlled instrument part was not assigned.");
            Require(instrument.AccessIndicator != null, "Access indicator was not assigned.");

            ShowcaseParticipantDefinition[] participants =
                UnityEngine.Object.FindObjectsByType<ShowcaseParticipantDefinition>(FindObjectsSortMode.None);
            Require(participants.Length == 2, "The showcase must contain two authored participant instances.");
            Require(UnityEngine.Object.FindAnyObjectByType<OrchestrationShowcase>() != null,
                "Showcase runtime component was not found in the scene.");

            var queue = new QueueManagerCore();
            Require(queue.RegisterInstrument("validation_instrument"), "Instrument registration failed.");
            Require(queue.RegisterUser("validation_user_a"), "User A registration failed.");
            Require(queue.RegisterUser("validation_user_b"), "User B registration failed.");

            QueueDecision first = queue.RequestAccess("validation_user_a", "validation_instrument");
            Require(first.Type == QueueDecisionType.Granted, "First request was not granted.");

            QueueDecision second = queue.RequestAccess("validation_user_b", "validation_instrument");
            Require(second.Type == QueueDecisionType.Queued && second.QueuePosition == 1,
                "Second request was not queued in first position.");

            QueueDecision release = queue.ReleaseAccess("validation_user_a", "validation_instrument");
            Require(release.Type == QueueDecisionType.ReleasedAndGrantedNext,
                "Release did not transfer access to the queued user.");
            Require(release.UserId == "validation_user_b", "Unexpected next holder.");

            Debug.Log("[VLab Orchestrator] Showcase validation passed: scene, FCFS queue, and transfer sequence are valid.");
        }

        [MenuItem("VLab Orchestrator/Validate Five-Backend Runtime")]
        public static void ValidateFiveBackendRuntime()
        {
            Validate();
            ValidateMaterializedSourceTree();

            (string Label, string TypeName)[] requiredTypes =
            {
                ("Photon PUN 2 SDK", "Photon.Pun.PhotonView"),
                ("Photon Fusion SDK", "Fusion.NetworkObject"),
                ("Unity Netcode SDK", "Unity.Netcode.NetworkObject"),
                ("FishNet SDK", "FishNet.Object.NetworkObject"),
                ("Mirror SDK", "Mirror.NetworkIdentity"),
                ("PUN 2 adapter", "Thesis.Orchestration.Backends.PUN2.PUN2BackendRuntimeAdapter"),
                ("Fusion adapter", "Thesis.Orchestration.Backends.Fusion.FusionBackendRuntimeAdapter"),
                ("Netcode adapter", "Thesis.Orchestration.Backends.Netcode.NetcodeBackendRuntimeAdapter"),
                ("FishNet adapter", "Thesis.Orchestration.Backends.FishNet.FishNetBackendRuntimeAdapter"),
                ("Mirror adapter", "Thesis.Orchestration.Backends.Mirror.MirrorBackendRuntimeAdapter")
            };

            foreach ((string label, string typeName) in requiredTypes)
                Require(FindLoadedType(typeName) != null, $"{label} is not loaded.");

            Require(Directory.Exists(GeneratedRuntimeRoot),
                "The project-authored runtime has not been materialized.");

            Type runtimeConfigType = FindLoadedType(
                "Thesis.Orchestration.EditorTools.OrchestrationProjectConfig");
            Require(runtimeConfigType != null, "The production runtime configuration type is unavailable.");

            UnityEngine.Object runtimeConfig =
                AssetDatabase.LoadAssetAtPath(RuntimeConfigPath, runtimeConfigType);
            Require(runtimeConfig != null, "The generated production runtime configuration is missing.");

            var serializedConfig = new SerializedObject(runtimeConfig);
            string[] requiredPrefabProperties =
            {
                "pun2RuntimeRootPrefab",
                "fusionRuntimeRootPrefab",
                "fishNetRuntimeRootPrefab",
                "unityNetcodeRuntimeRootPrefab",
                "mirrorRuntimeRootPrefab",
                "networkAvatarSourcePrefab"
            };
            foreach (string propertyName in requiredPrefabProperties)
            {
                SerializedProperty property = serializedConfig.FindProperty(propertyName);
                Require(property?.objectReferenceValue != null,
                    $"Runtime configuration property '{propertyName}' is not assigned.");
            }

            SerializedProperty objects = serializedConfig.FindProperty("microscopeEquivalentObjects");
            Require(objects != null && objects.arraySize == 1,
                "Exactly one generic shared instrument must be configured.");
            GameObject instrument = objects.GetArrayElementAtIndex(0)
                .FindPropertyRelative("prefab").objectReferenceValue as GameObject;
            Require(instrument != null, "The generic shared instrument prefab is not assigned.");

            Transform controlledPart = instrument.GetComponentsInChildren<Transform>(true)
                .FirstOrDefault(child => child.name == "Controlled Turret");
            Require(controlledPart != null, "The generic instrument has no Controlled Turret child.");
            Type markerType = FindLoadedType(
                "Thesis.Orchestration.SharedSync.SoftwareXControllablePart");
            Require(markerType != null && controlledPart.GetComponent(markerType) != null,
                "The controlled child is missing its backend-independent benchmark marker.");

            SerializedProperty backend = serializedConfig.FindProperty("backendType");
            Require(backend != null, "The selected production backend is unavailable.");
            string backendName = backend.enumDisplayNames[backend.enumValueIndex].Replace(" ", string.Empty);
            string identityTypeName = backendName switch
            {
                "PUN2" => "Photon.Pun.PhotonView",
                "Fusion" => "Fusion.NetworkObject",
                "FishNet" => "FishNet.Object.NetworkObject",
                "UnityNetcode" => "Unity.Netcode.NetworkObject",
                "Mirror" => "Mirror.NetworkIdentity",
                _ => string.Empty
            };
            Type identityType = FindLoadedType(identityTypeName);
            Require(identityType != null && instrument.GetComponent(identityType) != null,
                $"The generic instrument is not authored for selected backend '{backendName}'.");

            ValidateSelectedBuildAssets(serializedConfig, instrument, backendName, identityType);

            Debug.Log(
                "[VLab Orchestrator] Five-backend runtime validation passed: SDK matrix, adapters, " +
                "generated prefabs, runtime configuration, and generic instrument contract are valid.");
        }

        [MenuItem("VLab Orchestrator/Validate Selected Build Configuration")]
        public static void ValidateSelectedBuildConfiguration()
        {
            Type runtimeConfigType = FindLoadedType(
                "Thesis.Orchestration.EditorTools.OrchestrationProjectConfig");
            Require(runtimeConfigType != null, "The production runtime configuration type is unavailable.");

            UnityEngine.Object runtimeConfig =
                AssetDatabase.LoadAssetAtPath(RuntimeConfigPath, runtimeConfigType);
            Require(runtimeConfig != null, "The generated production runtime configuration is missing.");

            var serializedConfig = new SerializedObject(runtimeConfig);
            SerializedProperty configuredObjects = serializedConfig.FindProperty("microscopeEquivalentObjects");
            Require(configuredObjects != null && configuredObjects.arraySize > 0,
                "At least one shared instrument must be configured.");

            GameObject instrument = configuredObjects.GetArrayElementAtIndex(0)
                .FindPropertyRelative("prefab").objectReferenceValue as GameObject;
            Require(instrument != null, "The configured shared instrument prefab is missing.");

            SerializedProperty backend = serializedConfig.FindProperty("backendType");
            Require(backend != null, "The selected backend is unavailable.");
            string backendName = backend.enumDisplayNames[backend.enumValueIndex].Replace(" ", string.Empty);
            string identityTypeName = ResolveIdentityTypeName(backendName);
            Type identityType = FindLoadedType(identityTypeName);
            Require(identityType != null,
                $"The networking SDK for selected backend '{backendName}' is not loaded.");
            Require(instrument.GetComponent(identityType) != null,
                $"The shared instrument is not authored for selected backend '{backendName}'.");

            ValidateSelectedBuildAssets(serializedConfig, instrument, backendName, identityType);
            Debug.Log(
                $"[VLab Orchestrator] Selected build configuration is ready for {backendName} " +
                $"and benchmark protocol {CurrentBenchmarkProtocolVersion}.");
        }

        public static void ValidateBuildConfiguration()
        {
            if (Directory.Exists(GeneratedRuntimeRoot))
            {
                ValidateSelectedBuildConfiguration();
                return;
            }

            Require(File.Exists(ScenePath), "Showcase scene was not found.");
            Debug.Log(
                "[VLab Orchestrator] Building the SDK-independent showcase. " +
                "Install all five audited SDKs to activate multiplayer benchmark builds.");
        }

        private static void ValidateSelectedBuildAssets(
            SerializedObject serializedConfig,
            GameObject instrument,
            string backendName,
            Type identityType)
        {
            SerializedProperty benchmarkEnabled =
                serializedConfig.FindProperty("enableAutomatedBenchmarkScenario");
            if (benchmarkEnabled != null && benchmarkEnabled.boolValue)
            {
                SerializedProperty protocol =
                    serializedConfig.FindProperty("automatedScenarioProtocolVersion");
                Require(
                    protocol != null &&
                    string.Equals(
                        protocol.stringValue,
                        CurrentBenchmarkProtocolVersion,
                        StringComparison.Ordinal),
                    $"The benchmark must use protocol {CurrentBenchmarkProtocolVersion} before building.");
            }

            string rootPropertyName = backendName switch
            {
                "PUN2" => "pun2RuntimeRootPrefab",
                "Fusion" => "fusionRuntimeRootPrefab",
                "UnityNetcode" => "unityNetcodeRuntimeRootPrefab",
                "FishNet" => "fishNetRuntimeRootPrefab",
                "Mirror" => "mirrorRuntimeRootPrefab",
                _ => string.Empty
            };
            string adapterTypeName =
                $"Thesis.Orchestration.Backends.{backendName}.{backendName}BackendRuntimeAdapter";
            if (backendName == "UnityNetcode")
            {
                adapterTypeName =
                    "Thesis.Orchestration.Backends.Netcode.NetcodeBackendRuntimeAdapter";
            }

            Require(!string.IsNullOrWhiteSpace(rootPropertyName),
                $"Unsupported selected backend '{backendName}'.");
            GameObject runtimeRoot = serializedConfig.FindProperty(rootPropertyName)?.objectReferenceValue as GameObject;
            Require(runtimeRoot != null,
                $"The {backendName} runtime-root prefab is not assigned.");

            Type adapterType = FindLoadedType(adapterTypeName);
            Require(adapterType != null,
                $"The {backendName} runtime adapter type is unavailable.");
            if (backendName != "UnityNetcode")
            {
                Require(runtimeRoot.GetComponentInChildren(adapterType, true) != null,
                    $"The {backendName} runtime root is missing its runtime adapter.");
            }

            GameObject avatar = serializedConfig.FindProperty("networkAvatarSourcePrefab")
                ?.objectReferenceValue as GameObject;
            Require(avatar != null && avatar.GetComponent(identityType) != null,
                $"The shared avatar is not authored for selected backend '{backendName}'.");

            switch (backendName)
            {
                case "PUN2":
                    ValidatePhotonAppId(PunSettingsPath, "AppIdRealtime", "Photon PUN 2");
                    break;

                case "Fusion":
                    ValidatePhotonAppId(FusionSettingsPath, "AppIdFusion", "Photon Fusion");
                    ValidateFusionProfilerCompatibility();
                    break;

                case "FishNet":
                    ValidateFishNetBuildAssets(runtimeRoot);
                    Require(
                        FindLoadedType(
                            "Thesis.Orchestration.Backends.FishNet.ConfiguredSharedObjectSpawnerFishNet") != null,
                        "The FishNet configured-object server spawner is unavailable.");
                    break;

                case "Mirror":
                    ValidateMirrorBuildAssets(runtimeRoot);
                    Require(
                        FindLoadedType(
                            "Thesis.Orchestration.Backends.Mirror.ConfiguredSharedObjectSpawnerMirror") != null,
                        "The Mirror configured-object server spawner is unavailable.");
                    break;
            }

            Require(instrument.scene.IsValid() == false,
                "The configured instrument reference must be a prefab asset, not a scene instance.");
        }

        private static void ValidatePhotonAppId(
            string settingsPath,
            string settingName,
            string backendLabel)
        {
            Require(File.Exists(settingsPath),
                $"{backendLabel} settings are missing at '{settingsPath}'.");

            string prefix = settingName + ":";
            string appId = File.ReadLines(settingsPath)
                .Select(line => line.Trim())
                .Where(line => line.StartsWith(prefix, StringComparison.Ordinal))
                .Select(line => line.Substring(prefix.Length).Trim().Trim('"'))
                .FirstOrDefault();

            Require(
                !string.IsNullOrWhiteSpace(appId) &&
                appId.Length >= 16 &&
                !appId.Contains("YOUR", StringComparison.OrdinalIgnoreCase) &&
                !appId.Contains("APPID", StringComparison.OrdinalIgnoreCase),
                $"{backendLabel} requires the evaluator's own {settingName} before building. " +
                "Configure it locally from VLab Orchestrator > Setup; credentials are not committed.");
        }

        private static void ValidateFusionProfilerCompatibility()
        {
            Require(File.Exists(FusionUnityRuntimePath),
                $"Fusion runtime source is missing at '{FusionUnityRuntimePath}'.");

            string source = File.ReadAllText(FusionUnityRuntimePath).Replace("\r\n", "\n");
            const string counterGuard =
                "static IntPtr CreateCounter(string name, ProfilerMarkerDataType dataType, ProfilerMarkerDataUnit unit) {\n#if ENABLE_PROFILER && UNITY_EDITOR";
            const string counterValueGuard =
                "static IntPtr CreateCounterValue(string name, ProfilerMarkerDataUnit unit, ProfilerCounterOptions options = ProfilerCounterOptions.FlushOnEndOfFrame) {\n#if ENABLE_PROFILER && UNITY_EDITOR";

            Require(
                source.Contains(counterGuard, StringComparison.Ordinal) &&
                source.Contains(counterValueGuard, StringComparison.Ordinal),
                "Fusion's audited Unity 6 standalone-profiler compatibility repair is missing. " +
                "Return to VLab Orchestrator > Setup and wait for SDK compatibility repair before building.");
        }

        private static void ValidateFishNetBuildAssets(GameObject runtimeRoot)
        {
            Type managerType = FindLoadedType("FishNet.Managing.NetworkManager");
            Type transportType = FindLoadedType("FishNet.Transporting.Tugboat.Tugboat");
            Require(managerType != null && transportType != null,
                "FishNet NetworkManager or Tugboat is unavailable.");

            Component manager = runtimeRoot.GetComponentInChildren(managerType, true);
            Require(manager != null, "The FishNet runtime root has no NetworkManager.");
            Require(runtimeRoot.GetComponentInChildren(transportType, true) != null,
                "The FishNet runtime root has no Tugboat transport.");

            var serializedManager = new SerializedObject(manager);
            SerializedProperty spawnablePrefabs = serializedManager.FindProperty("_spawnablePrefabs");
            Require(spawnablePrefabs?.objectReferenceValue != null,
                "The FishNet runtime root has no spawnable-prefab collection. Re-run Finalize SoftwareX Runtime.");
            Require(
                string.Equals(
                    AssetDatabase.GetAssetPath(spawnablePrefabs.objectReferenceValue),
                    "Assets/DefaultPrefabObjects.asset",
                    StringComparison.OrdinalIgnoreCase),
                "The FishNet runtime root is not bound to this project's DefaultPrefabObjects asset. " +
                "Re-run Finalize SoftwareX Runtime.");
        }

        private static void ValidateMirrorBuildAssets(GameObject runtimeRoot)
        {
            Type managerType = FindLoadedType("Mirror.NetworkManager");
            Type transportType = FindLoadedType("kcp2k.KcpTransport");
            Require(managerType != null && transportType != null,
                "Mirror NetworkManager or KCP transport is unavailable.");

            Component manager = runtimeRoot.GetComponentInChildren(managerType, true);
            Component transport = runtimeRoot.GetComponentInChildren(transportType, true);
            Require(manager != null, "The Mirror runtime root has no NetworkManager.");
            Require(transport != null, "The Mirror runtime root has no KCP transport.");

            var serializedManager = new SerializedObject(manager);
            SerializedProperty assignedTransport = serializedManager.FindProperty("transport");
            Require(assignedTransport?.objectReferenceValue == transport,
                "The Mirror NetworkManager is not assigned to the runtime root's KCP transport.");
        }

        private static string ResolveIdentityTypeName(string backendName)
        {
            return backendName switch
            {
                "PUN2" => "Photon.Pun.PhotonView",
                "Fusion" => "Fusion.NetworkObject",
                "FishNet" => "FishNet.Object.NetworkObject",
                "UnityNetcode" => "Unity.Netcode.NetworkObject",
                "Mirror" => "Mirror.NetworkIdentity",
                _ => string.Empty
            };
        }

        private static void ValidateMaterializedSourceTree()
        {
            // Resolve like SoftwareXRuntimeInstaller: repo/src beside the core package, so the
            // check also works when the project references the packages from another location.
            UnityEditor.PackageManager.PackageInfo corePackage =
                UnityEditor.PackageManager.PackageInfo.FindForAssetPath("Packages/com.vlab.orchestrator.core");
            string publicationSourceRoot = corePackage != null && !string.IsNullOrEmpty(corePackage.resolvedPath)
                ? Path.GetFullPath(Path.Combine(corePackage.resolvedPath, "..", ".."))
                : Path.GetFullPath(Path.Combine(Application.dataPath, "..", "..", ".."));
            string sourceRoot = Path.Combine(publicationSourceRoot, "framework");
            string adaptersRoot = Path.Combine(publicationSourceRoot, "backend-adapters");
            string generatedRoot = Path.GetFullPath(Path.Combine(
                Application.dataPath, "GeneratedSoftwareXRuntime"));

            ValidateSourceTree(
                sourceRoot,
                Path.Combine(generatedRoot, "Framework"),
                new[]
                {
                    "Core/InstrumentStateModel.cs",
                    "Core/QueueManagerCore.cs",
                    "Core/UserStateModel.cs",
                    "Policies/PolicyEngineCore.cs",
                    "Editor/Setup/OrchestrationSetupWindow.cs"
                });
            ValidateSourceTree(
                adaptersRoot,
                Path.Combine(generatedRoot, "Backends"),
                new[]
                {
                    "PUN2/StandaloneShared/ImmersionOilNetworkStateAdapter.cs",
                    "PUN2/StandaloneShared/SlideNetworkStateAdapter.cs"
                });
        }

        private static void ValidateSourceTree(
            string sourceRoot,
            string generatedRoot,
            string[] exclusions)
        {
            Require(Directory.Exists(sourceRoot), $"Publication source root is missing: {sourceRoot}");
            Require(Directory.Exists(generatedRoot), $"Generated source root is missing: {generatedRoot}");

            foreach (string sourcePath in Directory.GetFiles(
                         sourceRoot,
                         "*.cs",
                         SearchOption.AllDirectories))
            {
                string relativePath = Path.GetRelativePath(sourceRoot, sourcePath).Replace('\\', '/');
                if (exclusions.Contains(relativePath, StringComparer.OrdinalIgnoreCase))
                    continue;

                string generatedPath = Path.Combine(generatedRoot, relativePath);
                Require(File.Exists(generatedPath), $"Generated source is missing: {relativePath}");
                Require(
                    string.Equals(
                        File.ReadAllText(sourcePath),
                        File.ReadAllText(generatedPath),
                        StringComparison.Ordinal),
                    $"Generated source is stale: {relativePath}");
            }
        }

        [MenuItem("VLab Orchestrator/Build Windows Showcase")]
        public static void BuildWindows()
        {
            if (Directory.Exists(GeneratedRuntimeRoot))
                ValidateFiveBackendRuntime();
            else
                Validate();

            string buildDirectory = Path.GetFullPath("Builds");
            Directory.CreateDirectory(buildDirectory);
            string executablePath = Path.Combine(buildDirectory, "VLabOrchestratorShowcase.exe");

            var options = new BuildPlayerOptions
            {
                scenes = new[] { ScenePath },
                locationPathName = executablePath,
                target = BuildTarget.StandaloneWindows64,
                options = BuildOptions.None
            };

            BuildReport report = BuildPipeline.BuildPlayer(options);
            if (report.summary.result != BuildResult.Succeeded)
            {
                throw new InvalidOperationException(
                    $"Showcase build failed with result {report.summary.result} and {report.summary.totalErrors} errors.");
            }

            Debug.Log($"[VLab Orchestrator] Windows showcase built at {executablePath}");
        }

        private static void Require(bool condition, string message)
        {
            if (!condition)
                throw new InvalidOperationException(message);
        }

        private static Type FindLoadedType(string fullName)
        {
            if (string.IsNullOrWhiteSpace(fullName))
                return null;

            foreach (Assembly assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                Type type = assembly.GetType(fullName, false);
                if (type != null)
                    return type;
            }

            return null;
        }
    }

    internal sealed class ShowcaseBuildConfigurationPreprocessor : IPreprocessBuildWithReport
    {
        public int callbackOrder => 10000;

        public void OnPreprocessBuild(BuildReport report)
        {
            ShowcaseValidation.ValidateBuildConfiguration();
        }
    }
}
