using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Callbacks;
using UnityEditor.PackageManager;
using UnityEngine;
using PackageInfo = UnityEditor.PackageManager.PackageInfo;

namespace VLabOrchestrator.Editor
{
    public static class SoftwareXRuntimeInstaller
    {
        private const string GeneratedRuntimeRoot =
            "Assets/GeneratedSoftwareXRuntime";
        private const string PendingAuthoringSessionKey =
            "VLabOrchestrator.SoftwareXRuntime.PendingAuthoring";

        private static readonly HashSet<string> ExcludedFrameworkFiles =
            new(StringComparer.OrdinalIgnoreCase)
            {
                "Core/InstrumentStateModel.cs",
                "Core/QueueManagerCore.cs",
                "Core/UserStateModel.cs",
                "Policies/PolicyEngineCore.cs",
                "Editor/Setup/OrchestrationSetupWindow.cs"
            };

        private static readonly HashSet<string> ExcludedAdapterFiles =
            new(StringComparer.OrdinalIgnoreCase)
            {
                "PUN2/StandaloneShared/ImmersionOilNetworkStateAdapter.cs",
                "PUN2/StandaloneShared/SlideNetworkStateAdapter.cs"
            };

        private static bool installScheduled;

        [InitializeOnLoadMethod]
        private static void ScheduleAutomaticInstall()
        {
            ScheduleInstall();
        }

        [DidReloadScripts]
        private static void CompletePendingAuthoringAfterReload()
        {
            if (SessionState.GetBool(PendingAuthoringSessionKey, false))
                ScheduleInstall();
        }

        [MenuItem("VLab Orchestrator/Finalize SoftwareX Runtime", false, 20)]
        public static void InstallOrUpdateRuntime()
        {
            installScheduled = false;

            if (BuildPipeline.isBuildingPlayer)
                return;

            if (EditorApplication.isCompiling || EditorApplication.isUpdating)
            {
                ScheduleInstall();
                return;
            }

            if (!AreAllAuditedSdksLoaded(out string missingSdk))
            {
                Debug.Log(
                    "[VLab Orchestrator] Runtime activation is waiting for the complete " +
                    $"audited SDK matrix. Missing: {missingSdk}.");
                return;
            }

            if (!TryResolveSourceRoots(out string frameworkRoot, out string adaptersRoot))
            {
                Debug.LogError(
                    "[VLab Orchestrator] Publication source roots could not be resolved. " +
                    "Keep repo/src/framework and repo/src/backend-adapters beside repo/src/packages.");
                return;
            }

            bool changed = false;
            changed |= MaterializeSourceTree(
                frameworkRoot,
                Path.Combine(GeneratedRuntimeRoot, "Framework"),
                ExcludedFrameworkFiles);
            changed |= MaterializeSourceTree(
                adaptersRoot,
                Path.Combine(GeneratedRuntimeRoot, "Backends"),
                ExcludedAdapterFiles);

            if (changed)
            {
                SessionState.SetBool(PendingAuthoringSessionKey, true);
                AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
                Debug.Log(
                    "[VLab Orchestrator] Materialized the project-authored SoftwareX runtime. " +
                    "Unity will compile it, then complete runtime asset authoring automatically.");
                ScheduleInstall();
                return;
            }

            SessionState.EraseBool(PendingAuthoringSessionKey);
            SoftwareXRuntimeAuthoring.TryAuthorRuntimeAssets();
        }

        public static bool IsRuntimeMaterialized()
        {
            return File.Exists(Path.Combine(
                GeneratedRuntimeRoot,
                "Framework",
                "Runtime",
                "Bootstrap",
                "OrchestrationRuntimeBootstrap.cs"));
        }

        private static void ScheduleInstall()
        {
            if (installScheduled)
                return;

            installScheduled = true;
            EditorApplication.delayCall += InstallOrUpdateRuntime;
        }

        private static bool AreAllAuditedSdksLoaded(out string missingSdk)
        {
            (string Label, string TypeName)[] requiredTypes =
            {
                ("Photon PUN 2", "Photon.Pun.PhotonView"),
                ("Photon Fusion", "Fusion.NetworkObject"),
                ("Unity Netcode", "Unity.Netcode.NetworkObject"),
                ("FishNet", "FishNet.Object.NetworkObject"),
                ("Mirror", "Mirror.NetworkIdentity")
            };

            foreach ((string label, string typeName) in requiredTypes)
            {
                if (FindLoadedType(typeName) != null)
                    continue;

                missingSdk = label;
                return false;
            }

            missingSdk = string.Empty;
            return true;
        }

        private static Type FindLoadedType(string fullName)
        {
            foreach (System.Reflection.Assembly assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                Type type = assembly.GetType(fullName, false);
                if (type != null)
                    return type;
            }

            return null;
        }

        private static bool TryResolveSourceRoots(
            out string frameworkRoot,
            out string adaptersRoot)
        {
            frameworkRoot = string.Empty;
            adaptersRoot = string.Empty;

            PackageInfo packageInfo = PackageInfo.FindForAssembly(typeof(SoftwareXRuntimeInstaller).Assembly);
            string corePackageRoot = packageInfo?.resolvedPath;
            string packagesRoot = string.IsNullOrWhiteSpace(corePackageRoot)
                ? string.Empty
                : Directory.GetParent(corePackageRoot)?.FullName;
            string sourceRoot = string.IsNullOrWhiteSpace(packagesRoot)
                ? string.Empty
                : Directory.GetParent(packagesRoot)?.FullName;
            if (string.IsNullOrWhiteSpace(sourceRoot))
                return false;

            frameworkRoot = Path.Combine(sourceRoot, "framework");
            adaptersRoot = Path.Combine(sourceRoot, "backend-adapters");
            return Directory.Exists(frameworkRoot) && Directory.Exists(adaptersRoot);
        }

        private static bool MaterializeSourceTree(
            string sourceRoot,
            string destinationRoot,
            HashSet<string> excludedFiles)
        {
            bool changed = false;
            string[] sourceFiles = Directory.GetFiles(sourceRoot, "*.cs", SearchOption.AllDirectories);
            var expectedDestinations = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (string sourceFile in sourceFiles)
            {
                string relativePath = NormalizeRelativePath(
                    Path.GetRelativePath(sourceRoot, sourceFile));
                if (excludedFiles.Contains(relativePath))
                    continue;

                string destination = Path.Combine(destinationRoot, relativePath);
                expectedDestinations.Add(Path.GetFullPath(destination));
                string sourceContents = File.ReadAllText(sourceFile);
                if (File.Exists(destination) &&
                    string.Equals(
                        File.ReadAllText(destination),
                        sourceContents,
                        StringComparison.Ordinal))
                {
                    continue;
                }

                Directory.CreateDirectory(Path.GetDirectoryName(destination) ?? destinationRoot);
                File.WriteAllText(destination, sourceContents);
                changed = true;
            }

            if (!Directory.Exists(destinationRoot))
                return changed;

            foreach (string generatedFile in
                     Directory.GetFiles(destinationRoot, "*.cs", SearchOption.AllDirectories))
            {
                if (expectedDestinations.Contains(Path.GetFullPath(generatedFile)))
                    continue;

                File.Delete(generatedFile);
                string metaPath = generatedFile + ".meta";
                if (File.Exists(metaPath))
                    File.Delete(metaPath);
                changed = true;
            }

            return changed;
        }

        private static string NormalizeRelativePath(string relativePath)
        {
            return relativePath.Replace('\\', '/');
        }
    }
}
