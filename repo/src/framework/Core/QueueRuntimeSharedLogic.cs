using System;
using System.Collections.Generic;
using System.Linq;
using Thesis.Orchestration.Analytics;
using Thesis.Orchestration.AI;
using Thesis.Orchestration.Core.Models;
using Thesis.Orchestration.Core.Policies;
using Thesis.Orchestration.Debugging;
using Thesis.Orchestration.Telemetry;

namespace Thesis.Orchestration.Core
{
    public class QueueRuntimeSharedLogic
    {
        public QueueManagerCore QueueManager { get; private set; }
        public QueueEventHistory EventHistory { get; private set; }

        private readonly HashSet<string> registeredUserIds = new();
        private readonly HashSet<string> pendingRequests = new();
        private readonly int maxHistoryEntries;

        public QueueRuntimeSharedLogic(int maxHistoryEntries)
        {
            this.maxHistoryEntries = maxHistoryEntries;
            EventHistory = new QueueEventHistory(maxHistoryEntries);
        }

        public bool IsInitialized => QueueManager != null;

        public void Initialize(string instrumentId, IEnumerable<string> userIds)
        {
            QueueManager = new QueueManagerCore(new PolicyEngineCore(new FCFSPolicy()));
            OrchestrationAiService.Instance?.ApplyControlledPolicyIfNeeded(QueueManager);
            EventHistory = new QueueEventHistory(maxHistoryEntries);

            registeredUserIds.Clear();
            pendingRequests.Clear();

            QueueManager.RegisterInstrument(instrumentId);
            AddHistory($"Registered instrument: {instrumentId}");

            foreach (string userId in userIds.Where(x => !string.IsNullOrWhiteSpace(x)))
            {
                QueueManager.RegisterUser(userId);
                registeredUserIds.Add(userId);
                AddHistory($"Registered user: {userId}");
            }
        }

        // Rebuilds authoritative state on a participant that has just become queue authority (e.g. PUN2
        // master migration), from the last replicated snapshot. Holder and queue order are restored verbatim,
        // not replayed through the allocation policy. The holder and queued users are registered even if they
        // have already left, so the next SyncUsers records their departure like any other.
        // Not restored: per-user request counters and the holder's idle clock (restarts at restore time).
        public void RestoreFromReplicatedState(
            string instrumentId,
            IEnumerable<string> activeUserIds,
            string holderUserId,
            IEnumerable<string> waitingQueue,
            IEnumerable<string> replicatedHistory)
        {
            List<string> queue = waitingQueue?.Where(x => !string.IsNullOrWhiteSpace(x)).ToList() ?? new List<string>();
            bool hasHolder = !string.IsNullOrWhiteSpace(holderUserId) &&
                             !string.Equals(holderUserId, "None", StringComparison.OrdinalIgnoreCase);

            IEnumerable<string> knownUsers = (activeUserIds ?? Enumerable.Empty<string>())
                .Concat(queue)
                .Concat(hasHolder ? new[] { holderUserId } : Array.Empty<string>())
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Distinct();
            Initialize(instrumentId, knownUsers);

            EventHistory = new QueueEventHistory(maxHistoryEntries);
            foreach (string entry in replicatedHistory ?? Enumerable.Empty<string>())
                EventHistory.AddFormatted(entry);

            InstrumentStateModel instrument = QueueManager.GetInstrumentState(instrumentId);
            if (hasHolder)
                instrument.AssignHolder(holderUserId);
            foreach (string queuedUserId in queue)
                instrument.EnqueueUser(queuedUserId);

            AddHistory($"Queue authority restored from replicated state: holder={(hasHolder ? holderUserId : "None")} queue={queue.Count}");
        }

        public void EnsureUserRegistered(string userId)
        {
            if (QueueManager == null || string.IsNullOrWhiteSpace(userId))
                return;

            if (registeredUserIds.Contains(userId))
                return;

            QueueManager.RegisterUser(userId);
            registeredUserIds.Add(userId);
            AddHistory($"Registered on demand user: {userId}");
        }

        public void RegisterLateJoinUsers(IEnumerable<string> activeUserIds)
        {
            if (QueueManager == null)
                return;

            foreach (string userId in activeUserIds.Where(x => !string.IsNullOrWhiteSpace(x)))
            {
                if (registeredUserIds.Contains(userId))
                    continue;

                QueueManager.RegisterUser(userId);
                registeredUserIds.Add(userId);
                AddHistory($"Registered late-join user: {userId}");
            }
        }

        public void SyncUsers(
            string instrumentId,
            IEnumerable<string> activeUserIds,
            Func<string, QueueDecision> releaseHolderFunc)
        {
            if (QueueManager == null)
                return;

            HashSet<string> active = new(activeUserIds.Where(x => !string.IsNullOrWhiteSpace(x)));

            RegisterLateJoinUsers(active);

            List<string> removedUsers = new();
            foreach (string knownUserId in registeredUserIds)
            {
                if (!active.Contains(knownUserId))
                    removedUsers.Add(knownUserId);
            }

            // Departures are recorded like any other queue decision (queue_transitions, telemetry,
            // usage timing). Previously they changed the queue without an analytics record.
            foreach (string removedUserId in removedUsers)
            {
                registeredUserIds.Remove(removedUserId);
                pendingRequests.Remove(removedUserId);
                AddHistory($"Participant left: {removedUserId}");

                string holderBefore = GetCurrentHolder(instrumentId);
                QueueDecision removeDecision = QueueManager.RemoveQueuedUser(removedUserId, instrumentId);
                if (removeDecision != null && removeDecision.Type == QueueDecisionType.RemovedFromQueue)
                {
                    AddHistory($"Removed disconnected user from queue: {removedUserId}");
                    RecordDecisionAnalytics($"ParticipantLeft({removedUserId})", removeDecision, instrumentId,
                        holderBefore, GetCurrentHolder(instrumentId), BuildQueueSnapshot(instrumentId));
                }

                InstrumentStateModel instrument = QueueManager.GetInstrumentState(instrumentId);
                if (instrument != null && instrument.CurrentHolderUserId == removedUserId)
                {
                    QueueDecision releaseDecision = releaseHolderFunc?.Invoke(removedUserId);
                    // ReleaseAccess registers users on demand; the departed holder must stay unregistered.
                    registeredUserIds.Remove(removedUserId);
                    if (releaseDecision != null)
                    {
                        RecordDecisionAnalytics($"HolderLeft({removedUserId})", releaseDecision, instrumentId,
                            removedUserId, GetCurrentHolder(instrumentId), BuildQueueSnapshot(instrumentId));
                    }
                }
            }
        }

        public QueueDecision RequestAccess(string userId, string instrumentId)
        {
            EnsureUserRegistered(userId);
            return QueueManager.RequestAccess(userId, instrumentId);
        }

        public QueueDecision ReleaseAccess(string userId, string instrumentId)
        {
            EnsureUserRegistered(userId);
            return QueueManager.ReleaseAccess(userId, instrumentId);
        }

        public bool RegisterHeartbeat(string userId, string instrumentId)
        {
            EnsureUserRegistered(userId);
            return QueueManager.RegisterInteraction(userId, instrumentId);
        }

        public QueueDecision CheckTimeout(string instrumentId, TimeSpan timeout)
        {
            return QueueManager.CheckTimeout(instrumentId, timeout);
        }

        public InstrumentStateModel GetInstrumentState(string instrumentId)
        {
            return QueueManager?.GetInstrumentState(instrumentId);
        }

        public string GetCurrentHolder(string instrumentId)
        {
            InstrumentStateModel instrumentState = GetInstrumentState(instrumentId);
            return instrumentState?.CurrentHolderUserId ?? "None";
        }

        private List<string> BuildQueueSnapshot(string instrumentId)
        {
            InstrumentStateModel instrumentState = GetInstrumentState(instrumentId);
            return instrumentState != null ? instrumentState.WaitingQueue.ToList() : new List<string>();
        }

        public string BuildJoinedQueue(string instrumentId)
        {
            InstrumentStateModel instrumentState = GetInstrumentState(instrumentId);
            return instrumentState != null ? string.Join("||", instrumentState.WaitingQueue) : string.Empty;
        }

        public string BuildJoinedHistory()
        {
            return string.Join("||", EventHistory.GetAll());
        }

        public void AddHistory(string message)
        {
            EventHistory ??= new QueueEventHistory(maxHistoryEntries);
            EventHistory.Add(message);
        }

        public void RecordQueueRequest(
            string userId,
            string instrumentId,
            string holderBefore,
            IEnumerable<string> queueSnapshot,
            string details)
        {
            QueueTransitionRecorder.Instance?.RecordRequest(
                userId,
                instrumentId,
                holderBefore,
                queueSnapshot,
                details);
        }

        public void RecordDecisionAnalytics(
    string source,
    QueueDecision decision,
    string instrumentId,
    string holderBefore,
    string holderAfter,
    IEnumerable<string> queueSnapshot)
        {
            LogDecision(source, decision, instrumentId, holderBefore);
            UpdateUsageTimingFromDecision(decision, instrumentId, holderBefore);
            RecordQueueDecision(decision, instrumentId, holderBefore, holderAfter, queueSnapshot, source);
            OrchestrationAiService.Instance?.EvaluateQueueDecision(
                source,
                QueueManager,
                instrumentId,
                decision,
                holderBefore,
                holderAfter,
                queueSnapshot);
        }

        public void LogDecision(string source, QueueDecision decision, string instrumentId, string holderBefore)
        {
            AddHistory($"{source} -> DecisionType={decision?.Type.ToString() ?? "NULL"}");

            if (decision == null)
            {
                TelemetryLoggerCore.Instance?.LogEvent("QueueDecisionNone", "UnknownUser", instrumentId, $"{source} returned no decision.");
                return;
            }

            switch (decision.Type)
            {
                case QueueDecisionType.Granted:
                    AddHistory($"{source} -> Granted access to {decision.UserId}");
                    TelemetryLoggerCore.Instance?.LogEvent("AccessGranted", decision.UserId, instrumentId, $"{source} granted access.");
                    break;

                case QueueDecisionType.Queued:
                    AddHistory($"{source} -> {decision.UserId} queued at position {decision.QueuePosition}");
                    TelemetryLoggerCore.Instance?.LogEvent("AccessQueued", decision.UserId, instrumentId, $"Queued at position {decision.QueuePosition}.");
                    break;

                case QueueDecisionType.Denied:
                    AddHistory($"{source} -> Denied. Reason: {decision.Reason}");
                    TelemetryLoggerCore.Instance?.LogEvent("AccessDenied", decision.UserId, instrumentId, $"Denied. Reason={decision.Reason}");
                    break;

                case QueueDecisionType.RemovedFromQueue:
                    AddHistory($"{source} -> Removed {decision.UserId} from queue");
                    TelemetryLoggerCore.Instance?.LogEvent("RemovedFromQueue", decision.UserId, instrumentId, "Removed from queue.");
                    break;

                case QueueDecisionType.ReleasedWithoutNext:
                    AddHistory($"{source} -> Released holder. Queue empty");
                    TelemetryLoggerCore.Instance?.LogEvent("ReleasedWithoutNext", !string.IsNullOrWhiteSpace(decision.UserId) ? decision.UserId : holderBefore, instrumentId, "Released holder; queue empty.");
                    break;

                case QueueDecisionType.ReleasedAndGrantedNext:
                    AddHistory($"{source} -> Released {decision.SecondaryUserId}, granted {decision.UserId}");
                    TelemetryLoggerCore.Instance?.LogEvent("AccessReleased", decision.SecondaryUserId, instrumentId, $"{source} released holder; granted next={decision.UserId}.");
                    TelemetryLoggerCore.Instance?.LogEvent("AccessGranted", decision.UserId, instrumentId, $"{source} granted next after release; previous holder={decision.SecondaryUserId}.");
                    break;

                case QueueDecisionType.TimeoutReleased:
                    AddHistory($"{source} -> Timeout released {(!string.IsNullOrWhiteSpace(decision.UserId) ? decision.UserId : holderBefore)}. No next user");
                    TelemetryLoggerCore.Instance?.LogEvent("TimeoutReleased", !string.IsNullOrWhiteSpace(decision.UserId) ? decision.UserId : holderBefore, instrumentId, "Timed out and released; no next user.");
                    break;

                case QueueDecisionType.TimeoutAndGrantedNext:
                    AddHistory($"{source} -> Timeout released {decision.SecondaryUserId}, granted {decision.UserId}");
                    TelemetryLoggerCore.Instance?.LogEvent("TimeoutAndGrantedNext", decision.UserId, instrumentId, $"Timed out {decision.SecondaryUserId}, granted next.");
                    break;

                case QueueDecisionType.Invalid:
                    AddHistory($"{source} -> Invalid. Reason: {decision.Reason}");
                    TelemetryLoggerCore.Instance?.LogEvent("QueueDecisionInvalid", decision.UserId, instrumentId, $"Invalid. Reason={decision.Reason}");
                    break;
            }
        }

        private void UpdateUsageTimingFromDecision(
            QueueDecision decision,
            string instrumentId,
            string holderBefore)
        {
            UsageTimingTracker tracker = UsageTimingTracker.Instance;
            if (tracker == null || decision == null || string.IsNullOrWhiteSpace(instrumentId))
                return;

            string effectiveUserId = !string.IsNullOrWhiteSpace(decision.UserId)
                ? decision.UserId
                : holderBefore;

            switch (decision.Type)
            {
                case QueueDecisionType.Granted:
                    if (pendingRequests.Add(decision.UserId))
                    {
                        tracker.RegisterRequest(decision.UserId, instrumentId);
                    }

                    tracker.RegisterGrant(decision.UserId, instrumentId);
                    pendingRequests.Remove(decision.UserId);
                    break;

                case QueueDecisionType.Queued:
                    if (pendingRequests.Add(decision.UserId))
                    {
                        tracker.RegisterRequest(decision.UserId, instrumentId);
                    }
                    break;

                case QueueDecisionType.ReleasedWithoutNext:
                    tracker.RegisterRelease(effectiveUserId, instrumentId);
                    break;

                case QueueDecisionType.ReleasedAndGrantedNext:
                    // Close the previous holder's usage interval, then grant the next user.
                    if (!string.IsNullOrWhiteSpace(decision.SecondaryUserId))
                        tracker.RegisterRelease(decision.SecondaryUserId, instrumentId);
                    else if (!string.IsNullOrWhiteSpace(holderBefore) && holderBefore != "None")
                        tracker.RegisterRelease(holderBefore, instrumentId);

                    if (pendingRequests.Add(decision.UserId))
                    {
                        tracker.RegisterRequest(decision.UserId, instrumentId);
                    }

                    tracker.RegisterGrant(decision.UserId, instrumentId);
                    pendingRequests.Remove(decision.UserId);
                    break;

                case QueueDecisionType.TimeoutReleased:
                    tracker.RegisterTimeout(effectiveUserId, instrumentId);
                    break;

                case QueueDecisionType.TimeoutAndGrantedNext:
                    if (!string.IsNullOrWhiteSpace(decision.SecondaryUserId))
                        tracker.RegisterTimeout(decision.SecondaryUserId, instrumentId);
                    else if (!string.IsNullOrWhiteSpace(holderBefore) && holderBefore != "None")
                        tracker.RegisterTimeout(holderBefore, instrumentId);

                    if (pendingRequests.Add(decision.UserId))
                    {
                        tracker.RegisterRequest(decision.UserId, instrumentId);
                    }

                    tracker.RegisterGrant(decision.UserId, instrumentId);
                    pendingRequests.Remove(decision.UserId);
                    break;

                case QueueDecisionType.RemovedFromQueue:
                    tracker.RegisterQueueRemoval(decision.UserId, instrumentId);
                    pendingRequests.Remove(decision.UserId);
                    break;
            }
        }

        private void RecordQueueDecision(
            QueueDecision decision,
            string instrumentId,
            string holderBefore,
            string holderAfter,
            IEnumerable<string> queueSnapshot,
            string source)
        {
            if (decision == null)
            {
                QueueTransitionRecorder.Instance?.RecordDecision(
                    "QueueDecisionNone",
                    "UnknownUser",
                    instrumentId,
                    holderBefore,
                    holderAfter,
                    queueSnapshot,
                    -1,
                    $"{source} returned no decision.");
                return;
            }

            switch (decision.Type)
            {
                case QueueDecisionType.Granted:
                    QueueTransitionRecorder.Instance?.RecordDecision(
                        "AccessGranted",
                        decision.UserId,
                        instrumentId,
                        holderBefore,
                        holderAfter,
                        queueSnapshot,
                        decision.QueuePosition,
                        $"{source} granted access.");
                    break;

                case QueueDecisionType.Queued:
                    QueueTransitionRecorder.Instance?.RecordDecision(
                        "AccessQueued",
                        decision.UserId,
                        instrumentId,
                        holderBefore,
                        holderAfter,
                        queueSnapshot,
                        decision.QueuePosition,
                        $"{source} queued user.");
                    break;

                case QueueDecisionType.Denied:
                    QueueTransitionRecorder.Instance?.RecordDecision(
                        "AccessDenied",
                        decision.UserId,
                        instrumentId,
                        holderBefore,
                        holderAfter,
                        queueSnapshot,
                        decision.QueuePosition,
                        $"{source} denied access. Reason={decision.Reason}");
                    break;

                case QueueDecisionType.RemovedFromQueue:
                    QueueTransitionRecorder.Instance?.RecordDecision(
                        "RemovedFromQueue",
                        decision.UserId,
                        instrumentId,
                        holderBefore,
                        holderAfter,
                        queueSnapshot,
                        decision.QueuePosition,
                        $"{source} removed user from queue.");
                    break;

                case QueueDecisionType.ReleasedWithoutNext:
                    QueueTransitionRecorder.Instance?.RecordDecision(
                        "AccessReleased",
                        !string.IsNullOrWhiteSpace(decision.UserId) ? decision.UserId : holderBefore,
                        instrumentId,
                        holderBefore,
                        holderAfter,
                        queueSnapshot,
                        decision.QueuePosition,
                        $"{source} released holder; queue empty.");
                    break;

                case QueueDecisionType.ReleasedAndGrantedNext:
                    // Record two rows: a release of the previous holder, and a grant to the next user.
                    QueueTransitionRecorder.Instance?.RecordDecision(
                        "AccessReleased",
                        !string.IsNullOrWhiteSpace(decision.SecondaryUserId) ? decision.SecondaryUserId : holderBefore,
                        instrumentId,
                        holderBefore,
                        "None",
                        queueSnapshot,
                        -1,
                        $"{source} released holder; granted next={decision.UserId}.");

                    QueueTransitionRecorder.Instance?.RecordDecision(
                        "AccessGranted",
                        decision.UserId,
                        instrumentId,
                        "None",
                        holderAfter,
                        queueSnapshot,
                        decision.QueuePosition,
                        $"{source} granted next after release; previous holder={decision.SecondaryUserId}.");
                    break;

                case QueueDecisionType.TimeoutReleased:
                    QueueTransitionRecorder.Instance?.RecordDecision(
                        "TimeoutReleased",
                        !string.IsNullOrWhiteSpace(decision.UserId) ? decision.UserId : holderBefore,
                        instrumentId,
                        holderBefore,
                        holderAfter,
                        queueSnapshot,
                        decision.QueuePosition,
                        $"{source} timeout released holder.");
                    break;

                case QueueDecisionType.TimeoutAndGrantedNext:
                    QueueTransitionRecorder.Instance?.RecordDecision(
                        "TimeoutGrantedNext",
                        decision.UserId,
                        instrumentId,
                        holderBefore,
                        holderAfter,
                        queueSnapshot,
                        decision.QueuePosition,
                        $"{source} timeout granted next; previous holder={decision.SecondaryUserId}");
                    break;

                case QueueDecisionType.Invalid:
                    QueueTransitionRecorder.Instance?.RecordDecision(
                        "QueueDecisionInvalid",
                        decision.UserId,
                        instrumentId,
                        holderBefore,
                        holderAfter,
                        queueSnapshot,
                        decision.QueuePosition,
                        $"{source} invalid. Reason={decision.Reason}");
                    break;
            }
        }
    }
}
