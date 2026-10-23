using System.Collections.Generic;

namespace Thesis.Orchestration.Runtime
{
    // Optional companion to IOrchestrationBackendRuntime: reports SDK versions and network/synchronization
    // settings as read from the networking SDK at run time, for the execution manifest.
    public interface IExecutionEnvironmentDescriptor
    {
        void DescribeExecutionEnvironment(IDictionary<string, string> into);
    }
}
