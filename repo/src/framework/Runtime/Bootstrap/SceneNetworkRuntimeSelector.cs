using Thesis.Orchestration.Backends.Fusion;
using Thesis.Orchestration.Backends.PUN2;
using Thesis.Orchestration.EditorTools;
using UnityEngine;

namespace Thesis.Orchestration.Runtime.Bootstrap
{
    public class SceneNetworkRuntimeSelector : MonoBehaviour
    {
        [SerializeField] private OrchestrationProjectConfig projectConfig;

        [Header("Scene-Owned Network Runtime Roots")]
        [SerializeField] private GameObject pun2QueueRuntimeRoot;
        [SerializeField] private GameObject fusionQueueRuntimeRoot;

        [Header("Debug")]
        [SerializeField] private bool verboseLogs = true;

        private void Awake()
        {
            if (projectConfig == null)
                projectConfig = Resources.Load<OrchestrationProjectConfig>("AIMultiplayerAssistant/OrchestrationProjectConfig");

            if (projectConfig == null)
            {
                Log("Missing OrchestrationProjectConfig. Cannot select scene network runtime.");
                return;
            }

            ApplySelection(projectConfig.backendType);
        }

        public void ApplySelection(OrchestrationBackendType backendType)
        {
            bool usePun2 = backendType == OrchestrationBackendType.PUN2;
            bool useFusion = backendType == OrchestrationBackendType.Fusion;

            ApplyRootSelection(ref pun2QueueRuntimeRoot, usePun2);
            ApplyRootSelection(ref fusionQueueRuntimeRoot, useFusion);

            Log($"Scene network runtime selected: {backendType}");
        }

        private void ApplyRootSelection(ref GameObject root, bool shouldKeep)
        {
            if (root == null)
                return;

            if (shouldKeep)
            {
                root.SetActive(true);
                return;
            }

            Destroy(root);
            root = null;
        }

        private void Log(string message)
        {
            if (!verboseLogs)
                return;

            Debug.Log($"[SceneNetworkRuntimeSelector] {message}", this);
        }
    }
}
