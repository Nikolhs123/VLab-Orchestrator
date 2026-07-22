using System;
using System.Collections.Generic;
using System.Linq;
using Thesis.Orchestration.Core;
using Thesis.Orchestration.Core.Models;
using Thesis.Orchestration.EditorTools;
using Thesis.Orchestration.Telemetry;

namespace Thesis.Orchestration.AI
{
    public static class OrchestrationAiSnapshotBuilder
    {
        public static OrchestrationAiStateSnapshot Build(
            OrchestrationProjectConfig config,
            string trigger,
            QueueManagerCore queueManager,
            string instrumentId,
            QueueDecision baselineDecision,
            string holderBefore,
            string holderAfter,
            IEnumerable<string> queueSnapshot)
        {
            InstrumentStateModel instrument = queueManager?.GetInstrumentState(instrumentId);
            IReadOnlyDictionary<string, UserStateModel> users =
                queueManager?.GetAllUsers() ?? new Dictionary<string, UserStateModel>();

            OrchestrationAiStateSnapshot snapshot = new OrchestrationAiStateSnapshot
            {
                timestampUtc = DateTime.UtcNow.ToString("o"),
                runId = EvaluationRunContext.Instance != null ? EvaluationRunContext.Instance.RunId : "UnknownRun",
                backend = EvaluationRunContext.Instance != null ? EvaluationRunContext.Instance.BackendName : "UnknownBackend",
                scenarioId = EvaluationRunContext.Instance != null ? EvaluationRunContext.Instance.ScenarioId : "UnknownScenario",
                mode = config != null ? config.aiMode.ToString() : "Disabled",
                provider = config != null ? config.aiProvider.ToString() : "None",
                trigger = trigger,
                instrumentId = instrumentId,
                requestingUserId = baselineDecision != null ? baselineDecision.UserId : "",
                holderBefore = Normalize(holderBefore),
                holderAfter = Normalize(holderAfter),
                currentHolderUserId = Normalize(instrument?.CurrentHolderUserId),
                baselineDecisionType = baselineDecision != null ? baselineDecision.Type.ToString() : "None",
                baselineGrantedUserId = baselineDecision != null ? baselineDecision.UserId : "",
                queueSnapshot = JoinQueue(queueSnapshot),
                instrumentLocked = instrument != null && instrument.IsLocked,
                currentHolderUsageMs = instrument?.GetLockDuration()?.TotalMilliseconds ?? 0d,
                currentHolderIdleMs = instrument?.GetIdleDuration()?.TotalMilliseconds ?? 0d,
                waitingQueue = queueSnapshot != null
                    ? queueSnapshot.Where(x => !string.IsNullOrWhiteSpace(x)).ToList()
                    : instrument != null
                        ? instrument.WaitingQueue.ToList()
                        : new List<string>()
            };

            foreach (UserStateModel user in users.Values.OrderBy(x => x.UserId, StringComparer.OrdinalIgnoreCase))
            {
                snapshot.users.Add(new OrchestrationAiUserSnapshot
                {
                    userId = user.UserId,
                    isCurrentHolder = string.Equals(user.UserId, instrument?.CurrentHolderUserId, StringComparison.OrdinalIgnoreCase),
                    isWaiting = user.IsWaiting,
                    queuePosition = instrument != null ? instrument.GetQueuePosition(user.UserId) : -1,
                    totalRequests = user.TotalRequests,
                    grantedTurns = user.GrantedTurns,
                    deniedRequests = user.DeniedRequests,
                    timeoutCount = user.TimeoutCount,
                    totalWaitingMs = user.TotalWaitingTime.TotalMilliseconds,
                    totalUsageMs = user.TotalUsageTime.TotalMilliseconds,
                    currentWaitingMs = user.GetCurrentWaitingDuration()?.TotalMilliseconds ?? 0d,
                    currentUsageMs = user.GetCurrentUsageDuration()?.TotalMilliseconds ?? 0d
                });
            }

            return snapshot;
        }

        public static OrchestrationAiStateSnapshot BuildForPolicy(
            OrchestrationProjectConfig config,
            InstrumentStateModel instrument,
            IReadOnlyDictionary<string, UserStateModel> users)
        {
            string instrumentId = instrument != null ? instrument.InstrumentId : "";
            List<string> queue = instrument != null ? instrument.WaitingQueue.ToList() : new List<string>();

            OrchestrationAiStateSnapshot snapshot = new OrchestrationAiStateSnapshot
            {
                timestampUtc = DateTime.UtcNow.ToString("o"),
                runId = EvaluationRunContext.Instance != null ? EvaluationRunContext.Instance.RunId : "UnknownRun",
                backend = EvaluationRunContext.Instance != null ? EvaluationRunContext.Instance.BackendName : "UnknownBackend",
                scenarioId = EvaluationRunContext.Instance != null ? EvaluationRunContext.Instance.ScenarioId : "UnknownScenario",
                mode = config != null ? config.aiMode.ToString() : "Controlled",
                provider = config != null ? config.aiProvider.ToString() : "LocalHeuristic",
                trigger = "PolicySelectNextUser",
                instrumentId = instrumentId,
                holderBefore = Normalize(instrument?.CurrentHolderUserId),
                holderAfter = Normalize(instrument?.CurrentHolderUserId),
                currentHolderUserId = Normalize(instrument?.CurrentHolderUserId),
                baselineDecisionType = "PolicySelection",
                queueSnapshot = JoinQueue(queue),
                instrumentLocked = instrument != null && instrument.IsLocked,
                currentHolderUsageMs = instrument?.GetLockDuration()?.TotalMilliseconds ?? 0d,
                currentHolderIdleMs = instrument?.GetIdleDuration()?.TotalMilliseconds ?? 0d,
                waitingQueue = queue
            };

            if (users != null)
            {
                foreach (UserStateModel user in users.Values.OrderBy(x => x.UserId, StringComparer.OrdinalIgnoreCase))
                {
                    snapshot.users.Add(new OrchestrationAiUserSnapshot
                    {
                        userId = user.UserId,
                        isCurrentHolder = string.Equals(user.UserId, instrument?.CurrentHolderUserId, StringComparison.OrdinalIgnoreCase),
                        isWaiting = user.IsWaiting,
                        queuePosition = instrument != null ? instrument.GetQueuePosition(user.UserId) : -1,
                        totalRequests = user.TotalRequests,
                        grantedTurns = user.GrantedTurns,
                        deniedRequests = user.DeniedRequests,
                        timeoutCount = user.TimeoutCount,
                        totalWaitingMs = user.TotalWaitingTime.TotalMilliseconds,
                        totalUsageMs = user.TotalUsageTime.TotalMilliseconds,
                        currentWaitingMs = user.GetCurrentWaitingDuration()?.TotalMilliseconds ?? 0d,
                        currentUsageMs = user.GetCurrentUsageDuration()?.TotalMilliseconds ?? 0d
                    });
                }
            }

            return snapshot;
        }

        private static string JoinQueue(IEnumerable<string> queueSnapshot)
        {
            return queueSnapshot != null ? string.Join("||", queueSnapshot) : "";
        }

        private static string Normalize(string value)
        {
            return string.IsNullOrWhiteSpace(value) ? "None" : value;
        }
    }
}
