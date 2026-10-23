using System;
using UnityEditor;
using UnityEngine;
using VLabOrchestrator.Runtime;

namespace VLabOrchestrator.Editor
{
    // Batch-mode equivalent of the Setup window's backend and benchmark selection:
    //
    //   Unity.exe -batchmode -projectPath <project> -quit
    //     -executeMethod VLabOrchestrator.Editor.SoftwareXBatchCommands.SelectBackendAndScenario
    //     -vlabBackend PUN2|Fusion|UnityNetcode|FishNet|Mirror
    //     -vlabScenario BaselineFcfs|ContentionBurst|RapidHandoff|TransformReplication|OpenLoopStress
    //
    // The canonical paper preset is enforced and the runtime configuration is synchronized exactly as
    // when the backend is selected in the Setup window.
    public static class SoftwareXBatchCommands
    {
        private const string PublicationConfigPath =
            "Assets/Resources/VLabOrchestrator/OrchestrationProjectConfig.asset";

        public static void SelectBackendAndScenario()
        {
            OrchestrationProjectConfig config =
                AssetDatabase.LoadAssetAtPath<OrchestrationProjectConfig>(PublicationConfigPath);
            if (config == null)
                throw new InvalidOperationException($"Publication configuration not found: {PublicationConfigPath}");

            config.backendType = ParseEnum<OrchestrationBackendType>("-vlabBackend");
            config.benchmarkScenario = ParseEnum<OrchestrationBenchmarkScenario>("-vlabScenario");
            config.enableAutomatedBenchmark = true;
            config.autoExportOnComplete = true;
            config.useCanonicalPaperPreset = true;
            config.ApplySelectedPaperPreset();
            EditorUtility.SetDirty(config);
            AssetDatabase.SaveAssets();
            SoftwareXRuntimeAuthoring.SyncRuntimeConfiguration(config);

            Debug.Log($"[VLab Orchestrator] Batch selection: backend={config.backendType} " +
                      $"scenario={config.ResolveScenarioId()} protocol={OrchestrationProjectConfig.CanonicalBenchmarkProtocolVersion}.");
        }

        private static T ParseEnum<T>(string key) where T : struct
        {
            string[] arguments = Environment.GetCommandLineArgs();
            for (int i = 0; i < arguments.Length - 1; i++)
            {
                if (string.Equals(arguments[i], key, StringComparison.OrdinalIgnoreCase) &&
                    Enum.TryParse(arguments[i + 1], true, out T value))
                    return value;
            }

            throw new ArgumentException($"Missing or invalid {key}. Allowed: {string.Join(", ", Enum.GetNames(typeof(T)))}");
        }
    }
}
