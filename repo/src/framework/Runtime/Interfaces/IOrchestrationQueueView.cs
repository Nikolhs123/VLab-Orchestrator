using System;
using System.Collections.Generic;
using Thesis.Orchestration.Telemetry;

namespace Thesis.Orchestration.Runtime
{
    public interface IOrchestrationQueueView
    {
        bool QueueRuntimeActive { get; }
        string CurrentInstrumentId { get; }
        string CurrentHolderUserId { get; }
        IReadOnlyList<string> CurrentQueueSnapshot { get; }

        bool HasSharedUserCardStats { get; }
        IReadOnlyList<UserCardStatsEntry> SharedUserCardStats { get; }

        event Action RuntimeStateChanged;

        void RequestAccessForLocalPlayer();
        void ReleaseAccessForLocalPlayer();
    }
}