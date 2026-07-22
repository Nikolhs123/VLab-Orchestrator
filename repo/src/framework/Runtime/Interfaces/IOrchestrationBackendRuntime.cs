using Thesis.Orchestration.EditorTools;

namespace Thesis.Orchestration.Runtime
{
    public interface IOrchestrationBackendRuntime
    {
        OrchestrationBackendType BackendType { get; }
        IOrchestrationSessionView SessionView { get; }
        IOrchestrationQueueView QueueView { get; }
    }
}