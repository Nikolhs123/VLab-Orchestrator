using System;
using System.Collections.Generic;
using System.Linq;
using Thesis.Orchestration.Core;
using Thesis.Orchestration.Telemetry;

// Regression for the FCFS queue core (package QueueManagerCore) and the shared queue-runtime logic used by
// all five adapters (framework QueueRuntimeSharedLogic). Telemetry singletons are stand-ins that record calls.
// Ordering here is authoritative arrival order: the order in which the authoritative peer applies requests.
internal static class Program
{
    private const string Instrument = "instrument";
    private static int failures;

    private static void Check(bool condition, string name)
    {
        Console.WriteLine($"{(condition ? "PASS" : "FAIL")}: {name}");
        if (!condition) failures++;
    }

    private static QueueRuntimeSharedLogic NewLogic(params string[] users)
    {
        var logic = new QueueRuntimeSharedLogic(50);
        logic.Initialize(Instrument, users);
        return logic;
    }

    private static List<string> Queue(QueueRuntimeSharedLogic logic) =>
        logic.GetInstrumentState(Instrument).WaitingQueue.ToList();

    private static int Main()
    {
        // Arrival order: grant to the first applied request, queue the rest in application order.
        var logic = NewLogic("A", "B", "C");
        Check(logic.RequestAccess("B", Instrument).Type == QueueDecisionType.Granted, "first applied request is granted");
        Check(logic.RequestAccess("C", Instrument).Type == QueueDecisionType.Queued, "second applied request is queued");
        Check(logic.RequestAccess("A", Instrument).Type == QueueDecisionType.Queued, "third applied request is queued");
        Check(Queue(logic).SequenceEqual(new[] { "C", "A" }), "queue keeps application order");

        // Duplicates.
        Check(logic.RequestAccess("B", Instrument).Type == QueueDecisionType.Denied, "holder re-request is denied");
        Check(logic.RequestAccess("C", Instrument).Type == QueueDecisionType.Denied, "queued user re-request is denied");
        Check(Queue(logic).SequenceEqual(new[] { "C", "A" }), "duplicates do not change queue order");

        // Release semantics.
        Check(logic.ReleaseAccess("A", Instrument).Type == QueueDecisionType.Denied, "non-holder release is denied");
        Check(logic.GetCurrentHolder(Instrument) == "B", "non-holder release keeps holder");
        QueueDecision release = logic.ReleaseAccess("B", Instrument);
        Check(release.Type == QueueDecisionType.ReleasedAndGrantedNext && release.UserId == "C" && release.SecondaryUserId == "B",
            "holder release grants next in FCFS order");
        Check(logic.RequestAccess("B", Instrument).Type == QueueDecisionType.Queued &&
              Queue(logic).SequenceEqual(new[] { "A", "B" }), "released user re-requesting goes to the back");

        // Unknown participants are registered on demand (identity is bound by the adapters before this layer).
        Check(logic.RequestAccess("Late", Instrument).Type == QueueDecisionType.Queued, "late joiner can queue");

        // Participant departure while waiting.
        var events = new List<string>();
        QueueTransitionRecorder.Instance = new QueueTransitionRecorder(events);
        logic.SyncUsers(Instrument, new[] { "C", "B", "Late" }, user => logic.ReleaseAccess(user, Instrument));
        Check(Queue(logic).SequenceEqual(new[] { "B", "Late" }), "departed queued user is removed, order kept");
        Check(events.Any(e => e.StartsWith("RemovedFromQueue|A")), "departed queued user removal is recorded");

        // Holder departure while another participant waits.
        events.Clear();
        logic.SyncUsers(Instrument, new[] { "B", "Late" }, user => logic.ReleaseAccess(user, Instrument));
        Check(logic.GetCurrentHolder(Instrument) == "B", "holder departure grants the next waiting participant");
        Check(Queue(logic).SequenceEqual(new[] { "Late" }), "queue after holder departure");
        Check(events.Any(e => e.StartsWith("AccessReleased|C")) && events.Any(e => e.StartsWith("AccessGranted|B")),
            "holder departure release and next grant are recorded");
        events.Clear();
        logic.SyncUsers(Instrument, new[] { "B", "Late" }, user => logic.ReleaseAccess(user, Instrument));
        Check(events.Count == 0 && logic.EventHistory.GetAll().Count(h => h.EndsWith("] Participant left: C")) == 1,
            "departed holder is not re-registered (no repeated departure on next sync)");

        // Rejoin after departure: treated as a new arrival.
        logic.SyncUsers(Instrument, new[] { "B", "Late", "C" }, user => logic.ReleaseAccess(user, Instrument));
        Check(logic.RequestAccess("C", Instrument).Type == QueueDecisionType.Queued &&
              Queue(logic).Last() == "C", "rejoined participant queues at the back");

        // Everyone leaves: no residual holder or queue.
        logic.SyncUsers(Instrument, Array.Empty<string>(), user => logic.ReleaseAccess(user, Instrument));
        Check(logic.GetCurrentHolder(Instrument) == "None" && Queue(logic).Count == 0, "no residual holder or queue after all leave");

        // Timeout.
        var timed = NewLogic("A", "B");
        timed.RequestAccess("A", Instrument);
        timed.RequestAccess("B", Instrument);
        Check(timed.CheckTimeout(Instrument, TimeSpan.FromHours(1)) == null, "no timeout before idle limit");
        QueueDecision timeout = timed.CheckTimeout(Instrument, TimeSpan.Zero);
        Check(timeout.Type == QueueDecisionType.TimeoutAndGrantedNext && timeout.UserId == "B" && timeout.SecondaryUserId == "A",
            "timeout releases holder and grants next");
        Check(!timed.RegisterHeartbeat("A", Instrument), "heartbeat from non-holder is not accepted");

        // Queue-authority migration: the new authority restores the replicated snapshot verbatim, then the
        // departed former authority (who was the holder) is removed and the waiting participant is granted.
        var migrated = new QueueRuntimeSharedLogic(50);
        migrated.RestoreFromReplicatedState(Instrument, new[] { "B", "C" }, "A", new[] { "C", "B" }, new[] { "h1", "h2" });
        Check(migrated.GetCurrentHolder(Instrument) == "A" && Queue(migrated).SequenceEqual(new[] { "C", "B" }),
            "restore keeps holder and queue order verbatim (not re-ordered by policy)");
        Check(migrated.EventHistory.GetAll().Take(2).SequenceEqual(new[] { "h1", "h2" }), "restore keeps replicated history");
        events.Clear();
        migrated.SyncUsers(Instrument, new[] { "B", "C" }, user => migrated.ReleaseAccess(user, Instrument));
        Check(migrated.GetCurrentHolder(Instrument) == "C" && Queue(migrated).SequenceEqual(new[] { "B" }),
            "departed holder is removed after restore and the first waiting participant is granted");
        Check(events.Any(e => e.StartsWith("AccessReleased|A")) && events.Any(e => e.StartsWith("AccessGranted|C")),
            "departure after restore is recorded");
        var empty = new QueueRuntimeSharedLogic(50);
        empty.RestoreFromReplicatedState(Instrument, new[] { "B" }, "None", Array.Empty<string>(), Array.Empty<string>());
        Check(empty.GetCurrentHolder(Instrument) == "None" && Queue(empty).Count == 0 &&
              empty.RequestAccess("B", Instrument).Type == QueueDecisionType.Granted, "restore of an idle queue accepts requests");

        Console.WriteLine(failures == 0 ? "ALL PASS" : $"{failures} FAILED");
        return failures == 0 ? 0 : 1;
    }
}

namespace Thesis.Orchestration.Telemetry
{
    public class QueueTransitionRecorder
    {
        public static QueueTransitionRecorder Instance;
        private readonly List<string> events;
        public QueueTransitionRecorder(List<string> events) { this.events = events; }
        public void RecordRequest(string userId, string instrumentId, string holderBefore, IEnumerable<string> queueSnapshot, string details = "") =>
            events.Add($"Request|{userId}");
        public void RecordDecision(string eventType, string userId, string instrumentId, string holderBefore, string holderAfter,
            IEnumerable<string> queueSnapshot, int queuePosition, string details = "") =>
            events.Add($"{eventType}|{userId}|{details}");
    }

    public class TelemetryLoggerCore
    {
        public static TelemetryLoggerCore Instance;
        public void LogEvent(string eventType, string userId, string instrumentId, string details) { }
    }
}

namespace Thesis.Orchestration.Analytics
{
    public class UsageTimingTracker
    {
        public static UsageTimingTracker Instance;
        public void RegisterRequest(string userId, string instrumentId) { }
        public void RegisterGrant(string userId, string instrumentId) { }
        public void RegisterRelease(string userId, string instrumentId) { }
        public void RegisterTimeout(string userId, string instrumentId) { }
        public void RegisterQueueRemoval(string userId, string instrumentId) { }
    }
}

namespace Thesis.Orchestration.AI
{
    public class OrchestrationAiService
    {
        public static OrchestrationAiService Instance;
        public void ApplyControlledPolicyIfNeeded(QueueManagerCore queueManager) { }
        public void EvaluateQueueDecision(string trigger, QueueManagerCore queueManager, string instrumentId,
            QueueDecision baselineDecision, string holderBefore, string holderAfter, IEnumerable<string> queueSnapshot) { }
    }
}
