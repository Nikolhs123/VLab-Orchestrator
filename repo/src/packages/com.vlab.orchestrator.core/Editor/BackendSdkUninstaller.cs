using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using VLabOrchestrator.Runtime;

namespace VLabOrchestrator.Editor
{
    internal static class BackendSdkUninstaller
    {
        private const string GeneratedRuntimeRoot = "Assets/GeneratedSoftwareXRuntime";

        public static void ConfirmAndUninstall(
            OrchestrationBackendType backend,
            string displayName)
        {
            if (EditorApplication.isCompiling || EditorApplication.isUpdating ||
                EditorApplication.isPlayingOrWillChangePlaymode || BuildPipeline.isBuildingPlayer)
            {
                EditorUtility.DisplayDialog(
                    "SDK uninstall unavailable",
                    "Wait for compilation/import to finish and leave Play mode before uninstalling an SDK.",
                    "OK");
                return;
            }

            string note = backend == OrchestrationBackendType.UnityNetcode
                ? "The Netcode adapter is also removed because it declares Netcode as a package dependency."
                : "The lightweight VLab adapter remains installed so this SDK can be installed again with one click.";
            if (!EditorUtility.DisplayDialog(
                    $"Uninstall {displayName}?",
                    "This safely removes the selected vendor SDK and the generated multi-backend runtime. " +
                    "Your project configuration, showcase assets, benchmark exports, and VLab core are preserved.\n\n" +
                    note,
                    "Uninstall SDK",
                    "Cancel"))
            {
                return;
            }

            try
            {
                AssetDatabase.StartAssetEditing();
                DeleteAssetIfPresent(GeneratedRuntimeRoot);
                foreach (string assetPath in GetOwnedAssetPaths(backend))
                    DeleteAssetIfPresent(assetPath);
            }
            finally
            {
                AssetDatabase.StopAssetEditing();
            }

            RemoveBackendDefines(backend);
            if (backend == OrchestrationBackendType.UnityNetcode)
            {
                RemoveManifestDependency("com.vlab.orchestrator.backend.netcode");
                RemoveManifestDependency("com.unity.netcode.gameobjects");
            }

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
            Debug.Log(
                $"[VLab Orchestrator] Safely uninstalled {displayName}. " +
                "Configuration and SDK-independent core were preserved. Use Setup to reinstall it.");
        }

        private static IEnumerable<string> GetOwnedAssetPaths(OrchestrationBackendType backend)
        {
            switch (backend)
            {
                case OrchestrationBackendType.PUN2:
                    // Photon Realtime/Libs may be shared with other Photon products.
                    return new[] { "Assets/Photon/PhotonUnityNetworking" };
                case OrchestrationBackendType.Fusion:
                    return new[] { "Assets/Photon/Fusion" };
                case OrchestrationBackendType.FishNet:
                    return new[] { "Assets/FishNet" };
                case OrchestrationBackendType.Mirror:
                    return new[] { "Assets/Mirror" };
                default:
                    return Array.Empty<string>();
            }
        }

        private static void DeleteAssetIfPresent(string assetPath)
        {
            if (!AssetDatabase.IsValidFolder(assetPath) &&
                AssetDatabase.LoadMainAssetAtPath(assetPath) == null)
            {
                return;
            }

            if (!AssetDatabase.DeleteAsset(assetPath))
                throw new IOException($"Unity could not remove '{assetPath}'.");
        }

        private static void RemoveManifestDependency(string packageId)
        {
            string manifestPath = Path.GetFullPath("Packages/manifest.json");
            string[] lines = File.ReadAllLines(manifestPath);
            string marker = $"\"{packageId}\"";
            string[] retained = lines
                .Where(line => !line.Contains(marker, StringComparison.Ordinal))
                .ToArray();
            if (retained.Length != lines.Length)
                File.WriteAllLines(manifestPath, retained);
        }

        private static void RemoveBackendDefines(OrchestrationBackendType backend)
        {
            string[] prefixes = backend switch
            {
                OrchestrationBackendType.PUN2 => new[] { "PHOTON_UNITY_NETWORKING", "PUN_" },
                OrchestrationBackendType.Fusion => new[] { "FUSION", "FUSION_" },
                OrchestrationBackendType.FishNet => new[] { "FISHNET", "FISHNET_" },
                OrchestrationBackendType.Mirror => new[] { "MIRROR", "MIRROR_" },
                _ => Array.Empty<string>()
            };
            if (prefixes.Length == 0)
                return;

            foreach (BuildTargetGroup group in Enum.GetValues(typeof(BuildTargetGroup)))
            {
                if (group == BuildTargetGroup.Unknown)
                    continue;

                try
                {
                    string current = PlayerSettings.GetScriptingDefineSymbolsForGroup(group);
                    string cleaned = string.Join(";", current.Split(';')
                        .Select(item => item.Trim())
                        .Where(item => item.Length > 0)
                        .Where(item => !prefixes.Any(prefix =>
                            item.Equals(prefix, StringComparison.OrdinalIgnoreCase) ||
                            item.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)))
                        .Distinct(StringComparer.Ordinal));
                    if (!string.Equals(current, cleaned, StringComparison.Ordinal))
                        PlayerSettings.SetScriptingDefineSymbolsForGroup(group, cleaned);
                }
                catch (ArgumentException)
                {
                    // Some obsolete/unsupported target groups cannot be queried.
                }
            }
        }
    }
}
