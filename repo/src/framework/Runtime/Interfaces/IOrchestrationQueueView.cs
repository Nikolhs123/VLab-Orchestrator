using System;
using System.Collections.Generic;
using Thesis.Orchestration.Telemetry;

namespace Thesis.Orchestration.Runtime
{
    public interface IOrchestrationQueueView
    {
        bool QueueRuntimeActive { get; }
        string CurrentInstrumentId { get; }
        // Shared by all participants of one session (see Core.SessionRunIdentity); empty before the queue starts.
        string SessionRunId { get; }
        string CurrentHolderUserId { get; }
        IReadOnlyList<string> CurrentQueueSnapshot { get; }

        bool HasSharedUserCardStats { get; }
        IReadOnlyList<UserCardStatsEntry> SharedUserCardStats { get; }

        event Action RuntimeStateChanged;

        void RequestAccessForLocalPlayer();
        void ReleaseAccessForLocalPlayer();
    }
}