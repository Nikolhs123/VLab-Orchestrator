using System;
using System.Linq;
using Thesis.Orchestration.EditorTools;

namespace Thesis.Orchestration.AI
{
    public class LocalHeuristicAiDecisionProvider : IOrchestrationAiDecisionProvider
    {
        private readonly OrchestrationProjectConfig config;

        public LocalHeuristicAiDecisionProvider(OrchestrationProjectConfig config)
        {
            this.config = config;
        }

        public string ProviderName => "LocalHeuristic";
        public bool SupportsSynchronousDecisions => true;

        public OrchestrationAiDecision Evaluate(OrchestrationAiStateSnapshot snapshot)
        {
            if (snapshot == null)
                return OrchestrationAiDecision.Fallback(ProviderName, "Unknown", "Snapshot was null.");

            if (snapshot.waitingQueue == null || snapshot.waitingQueue.Count == 0)
            {
                return OrchestrationAiDecision.NoAction(
                    ProviderName,
                    snapshot.mode,
                    "No queued users are waiting.");
            }

            string recommendedUserId = SelectFairQueuedUser(snapshot);
            if (string.IsNullOrWhiteSpace(recommendedUserId))
            {
                return OrchestrationAiDecision.Fallback(
                    ProviderName,
                    snapshot.mode,
                    "Could not select a valid queued user.");
            }

            bool shouldSuggestRelease =
                snapshot.instrumentLocked &&
                snapshot.currentHolderUsageMs >= Math.Max(0d, config != null ? config.aiSuggestReleaseAfterHolderSeconds * 1000d : 10000d);

            string action = shouldSuggestRelease ? "SuggestReleaseAndGrantNext" : "RecommendNextGrant";
            string explanation = shouldSuggestRelease
                ? $"Current holder has exceeded the configured AI suggestion threshold; recommended next user is {recommendedUserId}."
                : $"Recommended queued user is {recommendedUserId}, balancing waiting time, previous usage, and FCFS order.";

            return new OrchestrationAiDecision
            {
                provider = ProviderName,
                mode = snapshot.mode,
                action = action,
                recommendedUserId = recommendedUserId,
                explanation = explanation,
                confidence = 0.75f,
                valid = true,
                fallbackUsed = false
            };
        }

        private string SelectFairQueuedUser(OrchestrationAiStateSnapshot snapshot)
        {
            float fairnessWeight = config != null ? config.aiFairnessWeight : 0.65f;
            float fcfsWeight = config != null ? config.aiFcfsWeight : 0.35f;

            string bestUserId = "";
            double bestScore = double.NegativeInfinity;

            for (int i = 0; i < snapshot.waitingQueue.Count; i++)
            {
                string queuedUserId = snapshot.waitingQueue[i];
                OrchestrationAiUserSnapshot user =
                    snapshot.users.FirstOrDefault(x => string.Equals(x.userId, queuedUserId, StringComparison.OrdinalIgnoreCase));

                if (user == null)
                    continue;

                double fcfsScore = snapshot.waitingQueue.Count - i;
                double waitScore = user.currentWaitingMs + (user.totalWaitingMs * 0.1d);
                double usagePenalty = user.totalUsageMs * 0.05d;
                double grantPenalty = user.grantedTurns * 100d;
                double fairnessScore = waitScore - usagePenalty - grantPenalty;
                double score = (fairnessScore * fairnessWeight) + (fcfsScore * 100d * fcfsWeight);

                if (score <= bestScore)
                    continue;

                bestScore = score;
                bestUserId = queuedUserId;
            }

            return bestUserId;
        }
    }
}
