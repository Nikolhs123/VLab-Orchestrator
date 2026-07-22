using Thesis.Orchestration.EditorTools;
using UnityEngine;

namespace Thesis.Orchestration.Runtime.Bootstrap
{
    internal static class OrchestrationRuntimeBootstrapEntryPoint
    {
        private const string ProjectConfigResourcesPath = "AIMultiplayerAssistant/OrchestrationProjectConfig";

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void EnsureBootstrapExists()
        {
            OrchestrationProjectConfig projectConfig = Resources.Load<OrchestrationProjectConfig>(ProjectConfigResourcesPath);
            if (projectConfig == null || !projectConfig.autoBootstrapOnSceneLoad)
                return;

            if (Object.FindFirstObjectByType<OrchestrationRuntimeBootstrap>() != null)
                return;

            GameObject bootstrapRoot = new GameObject("OrchestrationBootstrap");
            bootstrapRoot.AddComponent<OrchestrationRuntimeBootstrap>();
        }
    }
}
