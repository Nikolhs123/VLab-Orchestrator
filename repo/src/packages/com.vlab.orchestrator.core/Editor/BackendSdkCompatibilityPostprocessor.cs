using System;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace VLabOrchestrator.Editor
{
    public sealed class BackendSdkCompatibilityPostprocessor : AssetPostprocessor
    {
        private const string FishNetMirrorUpgradePath =
            "Assets/FishNet/Upgrading/MirrorUpgrade.cs";
        private const string FishNetMirrorUpgradeMenuPath =
            "Assets/FishNet/Upgrading/UpgradeFromMirrorMenu.cs";
        private const string FishNetMirrorUpgradeDefaultGuard = "#if MIRROR\n";
        private const string FishNetMirrorUpgradeOptInGuard =
            "#if MIRROR && VLAB_ENABLE_FISHNET_MIRROR_UPGRADE\n";
        private const string FusionProjectConfigPath =
            "Assets/Photon/Fusion/Resources/NetworkProjectConfig.fusion";
        private const string FusionUnityRuntimePath =
            "Assets/Photon/Fusion/Runtime/Fusion.Unity.cs";

        [InitializeOnLoadMethod]
        private static void ScheduleCompatibilityRepair()
        {
            DisableOptionalFishNetMirrorUpgrade();
            RepairImportedSdkCompatibility();
        }

        public static void RunCompatibilityRepair()
        {
            RepairImportedSdkCompatibility();
        }

        private static void OnPostprocessAllAssets(
            string[] importedAssets,
            string[] deletedAssets,
            string[] movedAssets,
            string[] movedFromAssetPaths)
        {
            foreach (string assetPath in importedAssets)
            {
                if (assetPath.StartsWith("Assets/Photon/Fusion/", StringComparison.Ordinal) ||
                    assetPath.StartsWith("Assets/FishNet/", StringComparison.Ordinal) ||
                    assetPath.StartsWith("Assets/Mirror/", StringComparison.Ordinal))
                {
                    DisableOptionalFishNetMirrorUpgrade();
                    EditorApplication.delayCall += RepairImportedSdkCompatibility;
                    return;
                }
            }
        }

        private static void DisableOptionalFishNetMirrorUpgrade()
        {
            string[] migrationToolPaths =
            {
                FishNetMirrorUpgradePath,
                FishNetMirrorUpgradeMenuPath
            };
            bool changed = false;
            foreach (string migrationToolPath in migrationToolPaths)
            {
                if (!File.Exists(migrationToolPath))
                    continue;

                string original = File.ReadAllText(migrationToolPath);
                string contents = original.Replace("\r\n", "\n");
                if (!contents.Contains(
                        FishNetMirrorUpgradeDefaultGuard,
                        StringComparison.Ordinal))
                {
                    continue;
                }

                contents = contents.Replace(
                    FishNetMirrorUpgradeDefaultGuard,
                    FishNetMirrorUpgradeOptInGuard);
                File.WriteAllText(migrationToolPath, contents);
                changed = true;
            }

            if (changed)
            {
                Debug.Log(
                    "[VLab Orchestrator] Disabled FishNet's optional Mirror migration " +
                    "utilities. Define VLAB_ENABLE_FISHNET_MIRROR_UPGRADE only when " +
                    "performing a FishNet migration from an installed Mirror SDK.");
            }
        }

        private static void RepairImportedSdkCompatibility()
        {
            if (EditorApplication.isCompiling || EditorApplication.isUpdating)
            {
                EditorApplication.delayCall += RepairImportedSdkCompatibility;
                return;
            }

            EnsureFusionProjectConfig();
            EnsureFusionProfilerCompatibility();

            if (!File.Exists(FishNetMirrorUpgradePath))
                return;

            string original = File.ReadAllText(FishNetMirrorUpgradePath);
            string contents = original.Replace("\r\n", "\n");
            const string legacyAlias =
                "using MirrorNetworkTransformChild = Mirror.NetworkTransformChild;\n";
            const string legacyTargetSelection =
                "                Transform target;\n" +
                "                if (nt1 is MirrorNetworkTransformChild mc1)\n" +
                "                    target = mc1.target;\n" +
                "                else\n" +
                "                    target = go.transform;";
            const string compatibleTargetSelection =
                "                Transform target = TryGetMirrorChildTarget(nt1) ?? go.transform;";

            bool hasLegacyAlias = contents.Contains(legacyAlias, StringComparison.Ordinal);
            bool hasLegacyTargetSelection =
                contents.Contains(legacyTargetSelection, StringComparison.Ordinal);
            if (!hasLegacyAlias && !hasLegacyTargetSelection)
                return;

            contents = contents.Replace(legacyAlias, string.Empty);
            contents = contents.Replace(legacyTargetSelection, compatibleTargetSelection);

            if (!contents.Contains(
                    "private static Transform TryGetMirrorChildTarget(",
                    StringComparison.Ordinal))
            {
                const string insertionPoint =
                    "        private bool IterateNetworkAnimator(GameObject go)";
                const string helper =
                    "        private static Transform TryGetMirrorChildTarget(MirrorNetworkTransformBase networkTransform)\n" +
                    "        {\n" +
                    "            if (networkTransform == null)\n" +
                    "                return null;\n\n" +
                    "            System.Type networkTransformType = networkTransform.GetType();\n" +
                    "            if (networkTransformType == typeof(MirrorNetworkTransformBase))\n" +
                    "                return null;\n\n" +
                    "            System.Reflection.FieldInfo targetField = networkTransformType.GetField(\n" +
                    "                \"target\",\n" +
                    "                System.Reflection.BindingFlags.Instance |\n" +
                    "                System.Reflection.BindingFlags.Public |\n" +
                    "                System.Reflection.BindingFlags.NonPublic);\n" +
                    "            return targetField?.GetValue(networkTransform) as Transform;\n" +
                    "        }\n\n";

                if (!contents.Contains(insertionPoint, StringComparison.Ordinal))
                {
                    Debug.LogError(
                        "[VLab Orchestrator] FishNet/Mirror compatibility repair could not find " +
                        "the expected insertion point. Use the audited FishNet 4.6.22 package.");
                    return;
                }

                contents = contents.Replace(insertionPoint, helper + insertionPoint);
            }

            File.WriteAllText(FishNetMirrorUpgradePath, contents);
            AssetDatabase.ImportAsset(
                FishNetMirrorUpgradePath,
                ImportAssetOptions.ForceUpdate);
            Debug.Log(
                "[VLab Orchestrator] Applied the FishNet 4.6.22 migration-tool " +
                "compatibility repair for Mirror 96.0.1.");
        }

        private static void EnsureFusionProfilerCompatibility()
        {
            if (!File.Exists(FusionUnityRuntimePath))
                return;

            string contents = File.ReadAllText(FusionUnityRuntimePath);
            const string unsafeMetadataCall =
                "ProfilerUnsafeUtility.SetMarkerMetadata(marker, 0, null, (byte)dataType, (byte)unit);";
            const string emptyMetadataCall =
                "ProfilerUnsafeUtility.SetMarkerMetadata(marker, 0, string.Empty, (byte)dataType, (byte)unit);";
            const string compatibleMetadataCall =
                "ProfilerUnsafeUtility.SetMarkerMetadata(marker, 0, name, (byte)dataType, (byte)unit);";
            const string createCounterPlayerGuard =
                "static IntPtr CreateCounter(string name, ProfilerMarkerDataType dataType, ProfilerMarkerDataUnit unit) {\n#if ENABLE_PROFILER\n";
            const string createCounterEditorGuard =
                "static IntPtr CreateCounter(string name, ProfilerMarkerDataType dataType, ProfilerMarkerDataUnit unit) {\n#if ENABLE_PROFILER && UNITY_EDITOR\n";
            const string createCounterValuePlayerGuard =
                "static IntPtr CreateCounterValue(string name, ProfilerMarkerDataUnit unit, ProfilerCounterOptions options = ProfilerCounterOptions.FlushOnEndOfFrame) {\n#if ENABLE_PROFILER\n";
            const string createCounterValueEditorGuard =
                "static IntPtr CreateCounterValue(string name, ProfilerMarkerDataUnit unit, ProfilerCounterOptions options = ProfilerCounterOptions.FlushOnEndOfFrame) {\n#if ENABLE_PROFILER && UNITY_EDITOR\n";

            string repaired = contents.Replace("\r\n", "\n");
            repaired = repaired.Replace(unsafeMetadataCall, compatibleMetadataCall);
            repaired = repaired.Replace(emptyMetadataCall, compatibleMetadataCall);
            repaired = repaired.Replace(createCounterPlayerGuard, createCounterEditorGuard);
            repaired = repaired.Replace(createCounterValuePlayerGuard, createCounterValueEditorGuard);

            bool compatibilityApplied =
                repaired.Contains(compatibleMetadataCall, StringComparison.Ordinal) &&
                repaired.Contains(createCounterEditorGuard, StringComparison.Ordinal) &&
                repaired.Contains(createCounterValueEditorGuard, StringComparison.Ordinal);
            if (!compatibilityApplied)
            {
                Debug.LogError(
                    "[VLab Orchestrator] Fusion profiler compatibility repair could not " +
                    "recognize the installed source. Use the audited Fusion 2.0.9 build 1566 package.");
                return;
            }

            if (string.Equals(repaired, contents.Replace("\r\n", "\n"), StringComparison.Ordinal))
                return;

            contents = repaired;
            File.WriteAllText(FusionUnityRuntimePath, contents);
            AssetDatabase.ImportAsset(
                FusionUnityRuntimePath,
                ImportAssetOptions.ForceUpdate);
            Debug.Log(
                "[VLab Orchestrator] Disabled Fusion 2.0.9 optional native profiler counters " +
                "in standalone players to avoid the Unity 6.3 startup crash. Editor counters " +
                "and the orchestration CSV performance sampler remain available.");
        }

        private static void EnsureFusionProjectConfig()
        {
            if (File.Exists(FusionProjectConfigPath))
                return;

            Type utilitiesType = null;
            foreach (System.Reflection.Assembly assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                utilitiesType = assembly.GetType(
                    "Fusion.Editor.NetworkProjectConfigUtilities",
                    false);
                if (utilitiesType != null)
                    break;
            }
            System.Reflection.MethodInfo saveMethod = utilitiesType?.GetMethod(
                "SaveGlobalConfig",
                System.Reflection.BindingFlags.Public |
                System.Reflection.BindingFlags.Static,
                null,
                Type.EmptyTypes,
                null);
            if (saveMethod == null)
                return;

            try
            {
                saveMethod.Invoke(null, null);
                Debug.Log(
                    "[VLab Orchestrator] Created Fusion's credential-free default " +
                    "NetworkProjectConfig for deterministic code generation.");
            }
            catch (Exception exception)
            {
                Debug.LogWarning(
                    "[VLab Orchestrator] Fusion NetworkProjectConfig could not be " +
                    $"created automatically: {exception.GetBaseException().Message}");
            }
        }
    }
}
