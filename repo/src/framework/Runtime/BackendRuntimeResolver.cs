using Thesis.Orchestration.Backends.Fusion;
using Thesis.Orchestration.Backends.FishNet;
using Thesis.Orchestration.Backends.Mirror;
using Thesis.Orchestration.Backends.PUN2;
using Thesis.Orchestration.EditorTools;
using UnityEngine;

namespace Thesis.Orchestration.Runtime
{
    public static class BackendRuntimeResolver
    {
        public static IOrchestrationBackendRuntime Resolve(OrchestrationBackendType backendType)
        {
            IOrchestrationBackendRuntime runtime = backendType switch
            {
                OrchestrationBackendType.PUN2 => Object.FindFirstObjectByType<PUN2BackendRuntimeAdapter>(),
                OrchestrationBackendType.Fusion => Object.FindFirstObjectByType<FusionBackendRuntimeAdapter>(),
                OrchestrationBackendType.UnityNetcode => FindByTypeName("NetcodeBackendRuntimeAdapter") as IOrchestrationBackendRuntime,
                OrchestrationBackendType.FishNet => ResolveFishNetRuntime(),
                OrchestrationBackendType.Mirror => ResolveMirrorRuntime(),
                _ => null
            };

            Debug.Log($"[BackendRuntimeResolver] backendType={backendType}, resolved={(runtime != null ? runtime.GetType().Name : "NULL")}");
            return runtime;
        }

        private static Component FindByTypeName(string typeName)
        {
            MonoBehaviour[] all = Object.FindObjectsByType<MonoBehaviour>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            foreach (var mb in all)
            {
                if (mb == null)
                    continue;

                if (mb.GetType().Name == typeName)
                    return mb;
            }

            return null;
        }

        private static IOrchestrationBackendRuntime ResolveFishNetRuntime()
        {
            FishNetBackendRuntimeAdapter adapter =
                Object.FindFirstObjectByType<FishNetBackendRuntimeAdapter>(FindObjectsInactive.Include);
            if (adapter != null)
                return adapter;

            SessionOrchestratorFishNet session =
                Object.FindFirstObjectByType<SessionOrchestratorFishNet>(FindObjectsInactive.Include);
            if (session == null)
                return null;

            adapter = session.GetComponent<FishNetBackendRuntimeAdapter>();
            if (adapter == null)
                adapter = session.gameObject.AddComponent<FishNetBackendRuntimeAdapter>();

            adapter.ConfigureRuntime(session);
            return adapter;
        }

        private static IOrchestrationBackendRuntime ResolveMirrorRuntime()
        {
            MirrorBackendRuntimeAdapter adapter =
                Object.FindFirstObjectByType<MirrorBackendRuntimeAdapter>(FindObjectsInactive.Include);
            if (adapter != null)
                return adapter;

            SessionOrchestratorMirror session =
                Object.FindFirstObjectByType<SessionOrchestratorMirror>(FindObjectsInactive.Include);
            if (session == null)
                return null;

            adapter = session.GetComponent<MirrorBackendRuntimeAdapter>();
            if (adapter == null)
                adapter = session.gameObject.AddComponent<MirrorBackendRuntimeAdapter>();

            adapter.ConfigureRuntime(session);
            return adapter;
        }
    }
}
