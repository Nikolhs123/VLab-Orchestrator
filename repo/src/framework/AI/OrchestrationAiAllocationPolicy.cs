using System;
using System.Collections.Generic;
using System.Linq;
using Thesis.Orchestration.Core.Models;
using Thesis.Orchestration.Core.Policies;
using Thesis.Orchestration.EditorTools;
using Thesis.Orchestration.Telemetry;

namespace Thesis.Orchestration.AI
{
    public class OrchestrationAiAllocationPolicy : IAllocationPolicy
    {
        private readonly OrchestrationProjectConfig config;
        private readonly IOrchestrationAiDecisionProvider provider;
        private readonly IAllocationPolicy fallbackPolicy;

        public OrchestrationAiAllocationPolicy(
            OrchestrationProjectConfig config,
            IOrchestrationAiDecisionProvider provider,
            IAllocationPolicy fallbackPolicy = null)
        {
            this.config = config;
            this.provider = provider;
            this.fallbackPolicy = fallbackPolicy ?? new FCFSPolicy();
        }

        public string PolicyName => $"AI-Controlled ({provider?.ProviderName ?? "None"})";

        public string SelectNextUser(
            InstrumentStateModel instrumentState,
            IReadOnlyDictionary<string, UserStateModel> allUsers)
        {
            if (instrumentState == null || instrumentState.WaitingQueue == null || instrumentState.WaitingQueue.Count == 0)
                return null;

            if (provider == null || !provider.SupportsSynchronousDecisions)
                return SelectFallbackUser(instrumentState, allUsers);

            OrchestrationAiStateSnapshot snapshot =
                OrchestrationAiSnapshotBuilder.BuildForPolicy(config, instrumentState, allUsers);

            DateTime startUtc = DateTime.UtcNow;
            OrchestrationAiDecision decision = provider.Evaluate(snapshot);
            double latencyMs = (DateTime.UtcNow - startUtc).TotalMilliseconds;
            string recommendedUserId = decision != null ? decision.recommendedUserId : "";

            bool accepted = IsQueued(instrumentState, recommendedUserId);
            AiDecisionRecorder.Instance?.RecordDecision(snapshot, decision, latencyMs, accepted);

            if (accepted)
                return recommendedUserId;

            return SelectFallbackUser(instrumentState, allUsers);
        }

        private string SelectFallbackUser(
            InstrumentStateModel instrumentState,
            IReadOnlyDictionary<string, UserStateModel> allUsers)
        {
            bool shouldFallback = config == null || config.aiFallbackToFcfs;
            return shouldFallback ? fallbackPolicy.SelectNextUser(instrumentState, allUsers) : null;
        }

        private bool IsQueued(InstrumentStateModel instrumentState, string userId)
        {
            if (instrumentState == null || string.IsNullOrWhiteSpace(userId))
                return false;

            return instrumentState.WaitingQueue.Any(
                queued => string.Equals(queued, userId, StringComparison.OrdinalIgnoreCase));
        }
    }
}
