using System;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using VLabOrchestrator.Runtime;

namespace VLabOrchestrator.Editor
{
    internal static class SoftwareXRuntimeAuthoring
    {
        private const string PublicationConfigPath =
            "Assets/Resources/VLabOrchestrator/OrchestrationProjectConfig.asset";
        private const string RuntimeConfigPath =
            "Assets/Resources/AIMultiplayerAssistant/OrchestrationProjectConfig.asset";
        private const string GenericAvatarPath =
            "Assets/VLabOrchestrator/Prefabs/GenericResearcherAvatar.prefab";
        private const string GenericInstrumentPath =
            "Assets/VLabOrchestrator/Prefabs/GenericSharedInstrument.prefab";
        private const string RuntimeAvatarPath =
            "Assets/Resources/AIMultiplayerAssistant/NetworkAvatar.prefab";
        private const string RuntimeInstrumentPath =
            "Assets/Resources/AIMultiplayerAssistant/SoftwareXSharedInstrument.prefab";
        private const string RuntimeManagersPath =
            "Assets/Resources/AIMultiplayerAssistant/Managers";
        private const string FishNetDefaultPrefabObjectsPath =
            "Assets/DefaultPrefabObjects.asset";

        private const string RuntimeConfigTypeName =
            "Thesis.Orchestration.EditorTools.OrchestrationProjectConfig";

        public static void TryAuthorRuntimeAssets()
        {
            if (BuildPipeline.isBuildingPlayer)
                return;

            Type runtimeConfigType = FindLoadedType(RuntimeConfigTypeName);
            if (runtimeConfigType == null)
                return;

            EnsureFolderForAsset(RuntimeConfigPath);
            EnsureFolderForAsset(Path.Combine(RuntimeManagersPath, "placeholder.prefab"));

            ScriptableObject runtimeConfig =
                AssetDatabase.LoadAssetAtPath(RuntimeConfigPath, runtimeConfigType) as ScriptableObject;
            if (runtimeConfig == null)
            {
                runtimeConfig = ScriptableObject.CreateInstance(runtimeConfigType);
                AssetDatabase.CreateAsset(runtimeConfig, RuntimeConfigPath);
            }

            OrchestrationProjectConfig publicationConfig =
                AssetDatabase.LoadAssetAtPath<OrchestrationProjectConfig>(PublicationConfigPath);

            PrepareRuntimeVisualCopies();
            GameObject punRoot = EnsureRuntimeRootPrefab(
                $"{RuntimeManagersPath}/PUN2RuntimeRoot.prefab",
                "Thesis.Orchestration.Backends.PUN2.SessionOrchestratorPUN2",
                "Thesis.Orchestration.Backends.PUN2.NetworkAvatarSpawnerPUN2",
                "Thesis.Orchestration.Backends.PUN2.PUN2BackendRuntimeAdapter");
            GameObject fusionRoot = EnsureRuntimeRootPrefab(
                $"{RuntimeManagersPath}/FusionRuntimeRoot.prefab",
                "Thesis.Orchestration.Backends.Fusion.SessionOrchestratorFusion",
                "Thesis.Orchestration.Backends.Fusion.NetworkAvatarSpawnerFusion",
                "Thesis.Orchestration.Backends.Fusion.FusionBackendRuntimeAdapter");
            GameObject netcodeRoot = EnsureRuntimeRootPrefab(
                $"{RuntimeManagersPath}/NetcodeRuntimeRoot.prefab");
            GameObject fishNetRoot = PrepareFishNetRuntimeRoot();
            GameObject mirrorRoot = PrepareMirrorRuntimeRoot();

            var serializedConfig = new SerializedObject(runtimeConfig);
            SetObjectReference(serializedConfig, "pun2RuntimeRootPrefab", punRoot);
            SetObjectReference(serializedConfig, "fusionRuntimeRootPrefab", fusionRoot);
            SetObjectReference(serializedConfig, "fishNetRuntimeRootPrefab", fishNetRoot);
            SetObjectReference(serializedConfig, "unityNetcodeRuntimeRootPrefab", netcodeRoot);
            SetObjectReference(serializedConfig, "mirrorRuntimeRootPrefab", mirrorRoot);
            SetObjectReference(serializedConfig, "telemetryUiRootPrefab", null);
            SetObjectReference(
                serializedConfig,
                "networkAvatarSourcePrefab",
                AssetDatabase.LoadAssetAtPath<GameObject>(RuntimeAvatarPath));

            SetBool(serializedConfig, "autoBootstrapOnSceneLoad", true);
            SetBool(serializedConfig, "spawnTelemetryUI", true);
            SetBool(serializedConfig, "createDefaultUI", true);
            SetBool(serializedConfig, "createAvatarSpawner", true);
            SetBool(serializedConfig, "dontDestroyRuntimeRoots", false);
            SetString(
                serializedConfig,
                "networkAvatarPrefabPath",
                "AIMultiplayerAssistant/NetworkAvatar");
            SetString(
                serializedConfig,
                "automatedScenarioManipulatedChildName",
                "Controlled Turret");

            ConfigureSharedInstrumentEntry(serializedConfig);
            SyncBackendSelection(serializedConfig, publicationConfig);
            SyncBenchmarkSettings(serializedConfig, publicationConfig);
            serializedConfig.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(runtimeConfig);
            AssetDatabase.SaveAssets();

            if (BuildPipeline.isBuildingPlayer)
                return;

            InvokeGeneratedAuthoring(
                "Thesis.Orchestration.Editor.QueueRuntimePrefabAuthoringEditor",
                "ApplySelectedBackendAuthoring");
            InvokeGeneratedAuthoring(
                "Thesis.Orchestration.Editor.SharedAvatarPrefabAuthoringEditor",
                "ApplySelectedBackendAuthoring");
            InvokeGeneratedAuthoring(
                "Thesis.Orchestration.Editor.ConfiguredSharedObjectPrefabAuthoringEditor",
                "ApplySelectedBackendAuthoring");

            AssetDatabase.SaveAssets();
            Debug.Log(
                "[VLab Orchestrator] SoftwareX runtime is authored and ready. " +
                "The tracked project still contains no vendor SDK or original V-Lab media assets.");
        }

        public static void SyncRuntimeConfiguration(OrchestrationProjectConfig publicationConfig)
        {
            if (publicationConfig == null || !SoftwareXRuntimeInstaller.IsRuntimeMaterialized())
                return;

            Type runtimeConfigType = FindLoadedType(RuntimeConfigTypeName);
            ScriptableObject runtimeConfig = runtimeConfigType == null
                ? null
                : AssetDatabase.LoadAssetAtPath(RuntimeConfigPath, runtimeConfigType) as ScriptableObject;
            if (runtimeConfig == null)
                return;

            var serializedConfig = new SerializedObject(runtimeConfig);
            SyncBackendSelection(serializedConfig, publicationConfig);
            SyncBenchmarkSettings(serializedConfig, publicationConfig);
            serializedConfig.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(runtimeConfig);
            AssetDatabase.SaveAssets();

            if (BuildPipeline.isBuildingPlayer)
                return;

            InvokeGeneratedAuthoring(
                "Thesis.Orchestration.Editor.SharedAvatarPrefabAuthoringEditor",
                "ApplySelectedBackendAuthoring");
            InvokeGeneratedAuthoring(
                "Thesis.Orchestration.Editor.ConfiguredSharedObjectPrefabAuthoringEditor",
                "ApplySelectedBackendAuthoring");
        }

        private static void PrepareRuntimeVisualCopies()
        {
            EnsureFolderForAsset(RuntimeAvatarPath);

            CopyAssetIfMissing(GenericAvatarPath, RuntimeAvatarPath);
            CopyAssetIfMissing(GenericInstrumentPath, RuntimeInstrumentPath);

            GameObject instrumentContents = PrefabUtility.LoadPrefabContents(RuntimeInstrumentPath);
            if (instrumentContents == null)
                return;

            try
            {
                instrumentContents.name = "SoftwareXSharedInstrument";
                Type markerType = FindLoadedType(
                    "Thesis.Orchestration.SharedSync.SoftwareXControllablePart");
                Transform controlledPart = instrumentContents
                    .GetComponentsInChildren<Transform>(true)
                    .FirstOrDefault(child => child != null && child.name == "Controlled Turret");
                if (markerType != null && controlledPart != null &&
                    controlledPart.GetComponent(markerType) == null)
                {
                    controlledPart.gameObject.AddComponent(markerType);
                }

                PrefabUtility.SaveAsPrefabAsset(instrumentContents, RuntimeInstrumentPath);
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(instrumentContents);
            }
        }

        private static void ConfigureSharedInstrumentEntry(SerializedObject serializedConfig)
        {
            SerializedProperty entries = serializedConfig.FindProperty("microscopeEquivalentObjects");
            if (entries == null)
                return;

            entries.arraySize = 1;
            SerializedProperty entry = entries.GetArrayElementAtIndex(0);
            entry.FindPropertyRelative("prefab").objectReferenceValue =
                AssetDatabase.LoadAssetAtPath<GameObject>(RuntimeInstrumentPath);
            entry.FindPropertyRelative("sceneObjectPathOrName").stringValue = string.Empty;
            entry.FindPropertyRelative("spawnIfMissing").boolValue = true;
            entry.FindPropertyRelative("spawnPosition").vector3Value = new Vector3(1.7f, 0f, 0.8f);
            entry.FindPropertyRelative("spawnEulerAngles").vector3Value = Vector3.zero;
        }

        private static void SyncBackendSelection(
            SerializedObject runtimeConfig,
            OrchestrationProjectConfig publicationConfig)
        {
            SerializedProperty backend = runtimeConfig.FindProperty("backendType");
            if (backend == null)
                return;

            string backendName = publicationConfig != null
                ? publicationConfig.backendType.ToString()
                : OrchestrationBackendType.UnityNetcode.ToString();
            int backendIndex = Array.FindIndex(
                backend.enumNames,
                name => string.Equals(name, backendName, StringComparison.Ordinal));
            if (backendIndex >= 0)
                backend.enumValueIndex = backendIndex;
        }

        private static void SyncBenchmarkSettings(
            SerializedObject runtimeConfig,
            OrchestrationProjectConfig publicationConfig)
        {
            if (publicationConfig == null)
                return;

            if (publicationConfig.useCanonicalPaperPreset)
            {
                publicationConfig.ApplySelectedPaperPreset();
                EditorUtility.SetDirty(publicationConfig);
            }

            SetBool(
                runtimeConfig,
                "enableAutomatedBenchmarkScenario",
                publicationConfig.enableAutomatedBenchmark);
            SetBool(runtimeConfig, "automatedScenarioAutoBeginSession", true);
            SetBool(
                runtimeConfig,
                "automatedScenarioAutoExportOnComplete",
                publicationConfig.autoExportOnComplete);
            SetString(runtimeConfig, "automatedScenarioIdOverride", publicationConfig.ResolveScenarioId());
            SetString(
                runtimeConfig,
                "automatedScenarioProtocolVersion",
                OrchestrationProjectConfig.CanonicalBenchmarkProtocolVersion);
            SetInt(runtimeConfig, "automatedScenarioExpectedParticipants", publicationConfig.expectedParticipants);
            SetInt(runtimeConfig, "automatedScenarioRepeatCount", publicationConfig.repeatCount);
            SetFloat(runtimeConfig, "automatedScenarioWarmupSeconds", publicationConfig.warmupSeconds);
            SetFloat(runtimeConfig, "automatedScenarioHoldSeconds", publicationConfig.holdSeconds);
            SetFloat(runtimeConfig, "automatedScenarioCooldownSeconds", publicationConfig.cooldownSeconds);
            SetFloat(runtimeConfig, "automatedScenarioStepTimeoutSeconds", publicationConfig.stepTimeoutSeconds);
            SetBool(runtimeConfig, "automatedScenarioPreQueueNextUser", publicationConfig.preQueueNextParticipant);
            SetBool(runtimeConfig, "automatedScenarioManipulateChildObject", publicationConfig.manipulateControlledChild);
            SetString(runtimeConfig, "automatedScenarioManipulatedChildName", publicationConfig.controlledChildName);
            SetFloat(
                runtimeConfig,
                "automatedScenarioManipulationStartDelaySeconds",
                publicationConfig.manipulationStartDelaySeconds);
            SetFloat(
                runtimeConfig,
                "automatedScenarioManipulationDurationSeconds",
                publicationConfig.manipulationDurationSeconds);
            SetBool(runtimeConfig, "automatedScenarioResetPerformanceSamplesAfterWarmup", true);
            SetBool(runtimeConfig, "automatedScenarioSuppressInfoLogsDuringMeasurement", true);
            SetBool(runtimeConfig, "automatedScenarioValidateFinalRotation", publicationConfig.validateFinalRotation);
            SetFloat(
                runtimeConfig,
                "automatedScenarioFinalRotationSettleSeconds",
                publicationConfig.finalRotationSettleSeconds);
            SetFloat(
                runtimeConfig,
                "automatedScenarioRotationToleranceDegrees",
                publicationConfig.rotationToleranceDegrees);
            SetString(runtimeConfig, "backendEvaluationScenarioId", publicationConfig.ResolveScenarioId());

            SerializedProperty rotation = runtimeConfig.FindProperty("automatedScenarioManipulationEulerDelta");
            if (rotation != null)
                rotation.vector3Value = publicationConfig.controlledRotationDelta;

            string scenarioName = publicationConfig.benchmarkScenario switch
            {
                OrchestrationBenchmarkScenario.ContentionBurst => "SimultaneousContention2Users",
                OrchestrationBenchmarkScenario.RapidHandoff => "RapidOwnershipHandoffs2Users",
                OrchestrationBenchmarkScenario.TransformReplication => "TransformReplication2Users",
                OrchestrationBenchmarkScenario.OpenLoopStress => "OpenLoopRequestStress2Users",
                _ => "OwnershipPingPong2Users"
            };
            SetEnumByName(runtimeConfig, "automatedScenario", scenarioName);
            SetEnumByName(
                runtimeConfig,
                "automatedScenarioMode",
                publicationConfig.benchmarkScenario == OrchestrationBenchmarkScenario.OpenLoopStress
                    ? "OpenLoop"
                    : "ClosedLoop");
        }

        private static GameObject PrepareFishNetRuntimeRoot()
        {
            const string sourcePath = "Assets/FishNet/Demos/Prefabs/NetworkManager.prefab";
            string destinationPath = $"{RuntimeManagersPath}/FishNetRuntimeRoot.prefab";
            GameObject source = AssetDatabase.LoadAssetAtPath<GameObject>(sourcePath);
            if (source == null)
                return null;

            if (AssetDatabase.LoadAssetAtPath<GameObject>(destinationPath) == null)
                AssetDatabase.CopyAsset(sourcePath, destinationPath);

            EnsureRuntimeRootPrefab(
                destinationPath,
                "Thesis.Orchestration.Backends.FishNet.SessionOrchestratorFishNet",
                "Thesis.Orchestration.Backends.FishNet.NetworkAvatarSpawnerFishNet",
                "Thesis.Orchestration.Backends.FishNet.FishNetBackendRuntimeAdapter");

            GameObject contents = PrefabUtility.LoadPrefabContents(destinationPath);
            if (contents == null)
                return null;

            try
            {
                for (int childIndex = contents.transform.childCount - 1; childIndex >= 0; childIndex--)
                    UnityEngine.Object.DestroyImmediate(contents.transform.GetChild(childIndex).gameObject);

                Type playerSpawnerType = FindLoadedType("FishNet.Component.Spawning.PlayerSpawner");
                Component playerSpawner = playerSpawnerType != null
                    ? contents.GetComponent(playerSpawnerType)
                    : null;
                if (playerSpawner != null)
                    UnityEngine.Object.DestroyImmediate(playerSpawner);

                Type managerType = FindLoadedType("FishNet.Managing.NetworkManager");
                Component manager = managerType != null ? contents.GetComponent(managerType) : null;
                if (manager != null)
                {
                    var serializedManager = new SerializedObject(manager);
                    UnityEngine.Object spawnablePrefabs = ResolveFishNetSpawnablePrefabs();
                    SetObjectReference(serializedManager, "_spawnablePrefabs", spawnablePrefabs);

                    if (spawnablePrefabs == null)
                    {
                        Debug.LogError(
                            "[VLab Orchestrator] FishNet DefaultPrefabObjects could not be resolved. " +
                            "Use Fish-Networking > Utility > Refresh Default Prefabs, then run setup again.");
                    }

                    SetBool(serializedManager, "_dontDestroyOnLoad", false);
                    SetBool(serializedManager, "_runInBackground", true);
                    serializedManager.ApplyModifiedPropertiesWithoutUndo();
                }

                Type tugboatType = FindLoadedType("FishNet.Transporting.Tugboat.Tugboat");
                if (tugboatType != null && contents.GetComponent(tugboatType) == null)
                    contents.AddComponent(tugboatType);

                PrefabUtility.SaveAsPrefabAsset(contents, destinationPath);
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(contents);
            }

            return AssetDatabase.LoadAssetAtPath<GameObject>(destinationPath);
        }

        private static UnityEngine.Object ResolveFishNetSpawnablePrefabs()
        {
            UnityEngine.Object collection =
                AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(FishNetDefaultPrefabObjectsPath);
            if (collection != null)
                return collection;

            Type generatorType = FindLoadedType(
                "FishNet.Editing.PrefabCollectionGenerator.Generator");
            MethodInfo getDefaultPrefabs = generatorType?.GetMethod(
                "GetDefaultPrefabObjects",
                BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
            if (getDefaultPrefabs == null)
                return null;

            try
            {
                collection = getDefaultPrefabs.Invoke(null, new object[] { null }) as UnityEngine.Object;
                AssetDatabase.SaveAssets();
                return collection;
            }
            catch (Exception ex)
            {
                Debug.LogError(
                    $"[VLab Orchestrator] FishNet DefaultPrefabObjects generation failed: {ex.Message}");
                return null;
            }
        }

        private static GameObject PrepareMirrorRuntimeRoot()
        {
            string assetPath = $"{RuntimeManagersPath}/MirrorRuntimeRoot.prefab";
            EnsureRuntimeRootPrefab(
                assetPath,
                "kcp2k.KcpTransport",
                "Mirror.NetworkManager",
                "Thesis.Orchestration.Backends.Mirror.SessionOrchestratorMirror",
                "Thesis.Orchestration.Backends.Mirror.NetworkAvatarSpawnerMirror",
                "Thesis.Orchestration.Backends.Mirror.MirrorBackendRuntimeAdapter");

            GameObject contents = PrefabUtility.LoadPrefabContents(assetPath);
            if (contents == null)
                return null;

            try
            {
                Type managerType = FindLoadedType("Mirror.NetworkManager");
                Type transportType = FindLoadedType("kcp2k.KcpTransport");
                Component manager = managerType != null ? contents.GetComponent(managerType) : null;
                Component transport = transportType != null ? contents.GetComponent(transportType) : null;
                if (manager == null || transport == null)
                    return null;

                var serializedManager = new SerializedObject(manager);
                SetObjectReference(serializedManager, "transport", transport);
                SetBool(serializedManager, "autoCreatePlayer", false);
                SetObjectReference(serializedManager, "playerPrefab", null);
                SetBool(serializedManager, "dontDestroyOnLoad", false);
                SetBool(serializedManager, "runInBackground", true);
                serializedManager.ApplyModifiedPropertiesWithoutUndo();
                PrefabUtility.SaveAsPrefabAsset(contents, assetPath);
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(contents);
            }

            return AssetDatabase.LoadAssetAtPath<GameObject>(assetPath);
        }

        private static GameObject EnsureRuntimeRootPrefab(
            string assetPath,
            params string[] componentTypeNames)
        {
            EnsureFolderForAsset(assetPath);
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(assetPath);
            if (prefab == null)
            {
                var root = new GameObject(Path.GetFileNameWithoutExtension(assetPath));
                PrefabUtility.SaveAsPrefabAsset(root, assetPath);
                UnityEngine.Object.DestroyImmediate(root);
            }

            GameObject contents = PrefabUtility.LoadPrefabContents(assetPath);
            if (contents == null)
                return null;

            try
            {
                contents.name = Path.GetFileNameWithoutExtension(assetPath);
                foreach (string typeName in componentTypeNames)
                {
                    Type componentType = FindLoadedType(typeName);
                    if (componentType != null && contents.GetComponent(componentType) == null)
                        contents.AddComponent(componentType);
                }

                Canvas[] canvases = contents.GetComponentsInChildren<Canvas>(true);
                foreach (Canvas canvas in canvases)
                    canvas.gameObject.SetActive(false);

                PrefabUtility.SaveAsPrefabAsset(contents, assetPath);
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(contents);
            }

            return AssetDatabase.LoadAssetAtPath<GameObject>(assetPath);
        }

        private static void CopyAssetIfMissing(string sourcePath, string destinationPath)
        {
            if (AssetDatabase.LoadMainAssetAtPath(destinationPath) != null)
                return;

            if (!AssetDatabase.CopyAsset(sourcePath, destinationPath))
                Debug.LogError($"[VLab Orchestrator] Could not copy '{sourcePath}' to '{destinationPath}'.");
        }

        private static void InvokeGeneratedAuthoring(string typeName, string methodName)
        {
            Type type = FindLoadedType(typeName);
            MethodInfo method = type?.GetMethod(
                methodName,
                BindingFlags.Public | BindingFlags.Static,
                null,
                Type.EmptyTypes,
                null);
            method?.Invoke(null, null);
        }

        private static Type FindLoadedType(string fullName)
        {
            foreach (Assembly assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                Type type = assembly.GetType(fullName, false);
                if (type != null)
                    return type;
            }

            return null;
        }

        private static void EnsureFolderForAsset(string assetPath)
        {
            string directory = Path.GetDirectoryName(assetPath)?.Replace('\\', '/');
            if (string.IsNullOrWhiteSpace(directory))
                return;

            string[] segments = directory.Split('/');
            string current = segments[0];
            for (int index = 1; index < segments.Length; index++)
            {
                string next = $"{current}/{segments[index]}";
                if (!AssetDatabase.IsValidFolder(next))
                    AssetDatabase.CreateFolder(current, segments[index]);
                current = next;
            }
        }

        private static void SetObjectReference(
            SerializedObject serializedObject,
            string propertyName,
            UnityEngine.Object value)
        {
            SerializedProperty property = serializedObject.FindProperty(propertyName);
            if (property != null)
                property.objectReferenceValue = value;
        }

        private static void SetBool(SerializedObject serializedObject, string propertyName, bool value)
        {
            SerializedProperty property = serializedObject.FindProperty(propertyName);
            if (property != null)
                property.boolValue = value;
        }

        private static void SetString(SerializedObject serializedObject, string propertyName, string value)
        {
            SerializedProperty property = serializedObject.FindProperty(propertyName);
            if (property != null)
                property.stringValue = value;
        }

        private static void SetInt(SerializedObject serializedObject, string propertyName, int value)
        {
            SerializedProperty property = serializedObject.FindProperty(propertyName);
            if (property != null)
                property.intValue = value;
        }

        private static void SetFloat(SerializedObject serializedObject, string propertyName, float value)
        {
            SerializedProperty property = serializedObject.FindProperty(propertyName);
            if (property != null)
                property.floatValue = value;
        }

        private static void SetEnumByName(
            SerializedObject serializedObject,
            string propertyName,
            string enumName)
        {
            SerializedProperty property = serializedObject.FindProperty(propertyName);
            if (property == null)
                return;

            int enumIndex = Array.FindIndex(
                property.enumNames,
                name => string.Equals(name, enumName, StringComparison.Ordinal));
            if (enumIndex >= 0)
                property.enumValueIndex = enumIndex;
        }
    }
}
