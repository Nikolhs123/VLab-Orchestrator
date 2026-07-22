using System;
using System.Collections.Generic;

namespace Thesis.Orchestration.AI
{
    [Serializable]
    public class OrchestrationAiUserSnapshot
    {
        public string userId;
        public bool isCurrentHolder;
        public bool isWaiting;
        public int queuePosition;
        public int totalRequests;
        public int grantedTurns;
        public int deniedRequests;
        public int timeoutCount;
        public double totalWaitingMs;
        public double totalUsageMs;
        public double currentWaitingMs;
        public double currentUsageMs;
    }

    [Serializable]
    public class OrchestrationAiStateSnapshot
    {
        public string timestampUtc;
        public string runId;
        public string backend;
        public string scenarioId;
        public string mode;
        public string provider;
        public string trigger;
        public string instrumentId;
        public string requestingUserId;
        public string holderBefore;
        public string holderAfter;
        public string currentHolderUserId;
        public string baselineDecisionType;
        public string baselineGrantedUserId;
        public string queueSnapshot;
        public bool instrumentLocked;
        public double currentHolderUsageMs;
        public double currentHolderIdleMs;
        public List<string> waitingQueue = new();
        public List<OrchestrationAiUserSnapshot> users = new();
    }

    [Serializable]
    public class OrchestrationAiDecision
    {
        public string provider;
        public string mode;
        public string action;
        public string recommendedUserId;
        public string explanation;
        public float confidence;
        public bool valid;
        public bool fallbackUsed;

        public static OrchestrationAiDecision NoAction(string provider, string mode, string explanation)
        {
            return new OrchestrationAiDecision
            {
                provider = provider,
                mode = mode,
                action = "NoAction",
                recommendedUserId = "",
                explanation = explanation,
                confidence = 1f,
                valid = true,
                fallbackUsed = false
            };
        }

        public static OrchestrationAiDecision Fallback(string provider, string mode, string explanation)
        {
            return new OrchestrationAiDecision
            {
                provider = provider,
                mode = mode,
                action = "Fallback",
                recommendedUserId = "",
                explanation = explanation,
                confidence = 0f,
                valid = false,
                fallbackUsed = true
            };
        }
    }

    public interface IOrchestrationAiDecisionProvider
    {
        string ProviderName { get; }
        bool SupportsSynchronousDecisions { get; }
        OrchestrationAiDecision Evaluate(OrchestrationAiStateSnapshot snapshot);
    }
}
