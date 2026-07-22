using UnityEngine;
using Thesis.Orchestration.EditorTools;

namespace Thesis.Orchestration.Runtime.Bootstrap
{
    public class OrchestrationRuntimeRoot : MonoBehaviour
    {
        [SerializeField] private OrchestrationBackendType backendType;

        public OrchestrationBackendType BackendType => backendType;

        public void Configure(OrchestrationBackendType selectedBackendType)
        {
            backendType = selectedBackendType;
        }
    }
}
