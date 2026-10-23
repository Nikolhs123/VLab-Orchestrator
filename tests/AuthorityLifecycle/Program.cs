using System;
using System.IO;
using System.Linq;
using Thesis.Orchestration.Telemetry;

// Execute the real recorder source with minimal stand-ins, without networking SDKs.
// This is a recorder regression, not a Unity/SDK integration test.
internal static class Program
{
    private static void Check(bool condition, string message)
    {
        if (!condition) throw new Exception(message);
    }

    private static void Main()
    {
        var recorder = new AuthorityTransferRecorder();
        recorder.RecordTransferRequested("object", "user", "old", "first");
        string first = recorder.LifecycleRows[0].measurementId;
        recorder.CancelPendingTransfer("object", "user", "holder changed");
        Check(recorder.PendingTransferCount == 0, "Canceled request remained pending.");
        recorder.RecordTransferRequested("object", "user", "old", "second");
        string second = recorder.LifecycleRows[2].measurementId;
        Check(first != second, "Canceled request ID was reused.");
        recorder.RecordTransferCompleted("object", "user", "user", true);
        Check(recorder.Rows.Count == 1 && recorder.Rows[0].measurementId == second,
            "Completion matched an abandoned request.");
        Check(recorder.LifecycleRows[1].eventType == "Canceled", "Cancellation lost.");

        recorder.RecordTransferCompleted("orphan", "user", "user", true);
        Check(recorder.LifecycleRows.Last().eventType == "CompletionWithoutRequest",
            "Orphan completion not explicitly labeled.");
        recorder.RecordTransferRequested("pending", "user", "old");
        int count = recorder.LifecycleRows.Count;
        string folder = Path.Combine(Path.GetTempPath(), "VLabAuthorityRegression_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        recorder.SaveAuthorityTransfersCsvToFile(Path.Combine(folder, "authority_transfers.csv"));
        string csv = File.ReadAllText(Path.Combine(folder, "authority_measurement_lifecycle.csv"));
        Check(csv.Contains("PendingAtExport"), "Pending request missing from export.");
        Check(csv.Contains("clock_frequency_hz"), "Clock metadata missing.");
        Check(recorder.LifecycleRows.Count == count, "Export mutated journal.");
        recorder.ClearAll();
        Check(recorder.Rows.Count == 0 && recorder.LifecycleRows.Count == 0 && recorder.PendingTransferCount == 0,
            "Reset left residual state.");

        recorder.RecordTransferRequested("object", "user", "old");
        recorder.RecordTransferRequested("object", "user", "old");
        Check(recorder.LifecycleRows.Last().eventType == "DuplicateRequestSuppressed", "Duplicate request untracked.");
        recorder.RecordTransferRequested("object", "user", "different-owner");
        Check(recorder.LifecycleRows.Any(r => r.eventType == "Superseded"), "Superseded request lost.");
        recorder.RecordTransferCompleted("object", "user", "user", false);
        Check(recorder.LifecycleRows.Last().eventType == "Failed", "Failed completion lost.");
        Check(recorder.PendingTransferCount == 0, "Failed request remained pending.");
        Check(recorder.BuildMeasurementSummary().countsReconcile, "Lifecycle counts do not reconcile.");

        var clocked = new ControlledClockRecorder { Ticks = 1000 };
        clocked.RecordTransferRequested("timed", "user", "old");
        clocked.Ticks = 1250;
        clocked.RecordTransferCompleted("timed", "user", "user", true);
        Check(clocked.Rows[0].latencyAvailable && clocked.Rows[0].transferLatencyMs == 250d,
            "Duration did not use monotonic ticks and frequency.");
        clocked.RecordTransferRequested("zero", "user", "old");
        clocked.RecordTransferCompleted("zero", "user", "user", true);
        Check(clocked.Rows.Last().latencyAvailable && clocked.Rows.Last().transferLatencyMs == 0d,
            "An observed zero-tick duration was treated as unavailable.");
        clocked.RecordTransferCompleted("missing", "user", "user", true);
        Check(!clocked.Rows.Last().latencyAvailable && clocked.Rows.Last().transferLatencyMs == -1d &&
            clocked.Rows.Last().transferRequestedUtc == "", "Unmatched completion fabricated a duration.");
        clocked.RecordTransferRequested("timeout", "user", "old");
        clocked.TimeoutPendingTransfer("timeout", "user");
        var summary = clocked.BuildMeasurementSummary();
        Check(summary.initiated == 3 && summary.completed == 2 && summary.timedOut == 1 &&
            summary.unmatchedCompletions == 1 && summary.countsReconcile, "Terminal count reconciliation failed.");

        var recipient = new AuthorityTransferRecorder();
        recipient.RecordTransferRequested("timed", "user", "old");
        string json = clocked.BuildMostRecentCompletedRowJson("timed", "user");
        recipient.ApplyCompletedRowJson(json);
        recipient.ApplyCompletedRowJson(json);
        Check(recipient.Rows.Count == 1 && recipient.PendingTransferCount == 1,
            "Remote completion duplicated a row or consumed a different local request.");
        Check(recipient.Rows[0].observationSource == "RemoteImported" &&
            recipient.Rows[0].latencyAvailable && recipient.Rows[0].transferLatencyMs == 250d,
            "Source monotonic duration/provenance not preserved.");
        Check(recipient.BuildMeasurementSummary().importedCompletions == 1 &&
            recipient.BuildMeasurementSummary().countsReconcile, "Imported completion polluted local counts.");
        clocked.RecordTransferRequested("timed", "user", "old");
        clocked.Ticks += 10;
        clocked.RecordTransferCompleted("timed", "user", "user", true);
        Check(clocked.Rows.Last().latencyAvailable && clocked.Rows.Last().transferLatencyMs == 10d,
            "A rapid new attempt was suppressed by a recently completed attempt.");
        string legacy = "{\"objectId\":\"legacy\",\"logicalHolder\":\"user\",\"ownerAfter\":\"user\",\"transferCompletedUtc\":\"2026-01-01T00:00:00Z\",\"transferLatencyMs\":42}";
        recipient.ApplyCompletedRowJson(legacy);
        Check(!recipient.Rows.Last().latencyAvailable && recipient.Rows.Last().transferLatencyMs == -1d,
            "Legacy imported duration accepted without explicit timing provenance.");
        clocked.SaveAuthorityTransfersCsvToFile(Path.Combine(folder, "timed_authority_transfers.csv"));
        Check(File.ReadAllText(Path.Combine(folder, "authority_measurement_summary.csv")).Contains("True"),
            "Reconciliation status missing from summary export.");
        CheckMeasurementCorrelation();
        CheckNativeAuthorityObservations(folder);
        Console.WriteLine("PASS: exact monotonic duration, measured zero, unavailable duration, timeout counts, import deduplication, provenance, pending isolation, legacy import rejection.");
        Console.WriteLine("PASS: request correlation, cancellation, orphan completion, pending export, clock metadata, export immutability, reset, duplicates, supersession, failed completion.");
        Console.WriteLine("Evidence CSV: " + folder);
    }

    // W01 observation journal: state changes only, raw SDK-read values preserved, export schema.
    private static void CheckNativeAuthorityObservations(string folder)
    {
        var o = new ControlledObservationRecorder { Ticks = 10, Frame = 1 };
        Check(o.Observe("scope", "Client_1", "None", "Server", false, false, true, "pred"), "Initial state not stored.");
        Check(!o.Observe("scope", "Client_1", "None", "Server", false, false, true, "pred"), "Unchanged sample stored.");
        o.Ticks = 20; o.Frame = 2;
        Check(o.Observe("scope", "Client_1", "Client_1", "Server", true, false, true, "pred"), "Logical grant not stored.");
        o.Ticks = 35; o.Frame = 5;
        Check(o.Observe("scope", "Client_1", "Client_1", "Client_1", true, true, true, "pred"), "Native acquisition not stored.");
        Check(o.Observe("other", "Client_1", "Client_1", "Server", true, false, true, "pred"), "Objects not tracked separately.");
        Check(o.Rows.Count == 4 && o.Rows[2].monotonicTicks == 35 && o.Rows[2].frame == 5 && o.Rows[3].sequence == 4,
            "Observation ordering/clock values incorrect.");
        o.SaveObservationsCsvToFile(Path.Combine(folder, "native_authority_observations.csv"));
        string[] lines = File.ReadAllLines(Path.Combine(folder, "native_authority_observations.csv"));
        Check(lines.Length == 5 && lines[0].Contains("observer_is_holder") && lines[0].Contains("sdk_predicate") &&
              lines[3].Contains(",True,True,True,True,"), "Observation export schema/values incorrect.");
        o.ClearAll();
        Check(o.Rows.Count == 0 && o.Observe("scope", "Client_1", "Client_1", "Client_1", true, true, true, "pred"),
            "Reset did not clear change-detection state.");
        Console.WriteLine("PASS: native-authority observation journal stores state changes only, per object, with frame/monotonic provenance and export schema.");
    }

    // Adapter-side correlation: AuthorityMeasurementTracker + measurement-scoped recorder API.
    private static void CheckMeasurementCorrelation()
    {
        // A terminal event carrying an older measurement ID cannot close a newer measurement.
        var recorder = new ControlledClockRecorder { Ticks = 0 };
        string first = recorder.RecordTransferRequested("scope", "Client_1", "Client_0");
        Check(recorder.CancelMeasurement(first, "scope", "Client_1", "access revoked"), "Current measurement not cancellable.");
        string second = recorder.RecordTransferRequested("scope", "Client_1", "Client_0");
        Check(!recorder.CompleteMeasurement(first, "scope", "Client_1", "Client_1", true, "delayed callback"),
            "Delayed completion of a canceled measurement closed the newer measurement.");
        Check(recorder.PendingTransferCount == 1 && recorder.Rows.Count == 0, "Stale completion changed pending/rows.");
        Check(!recorder.CancelMeasurement("", "scope", "Client_1"), "Empty measurement ID accepted.");
        Check(recorder.CompleteMeasurement(second, "scope", "Client_1", "Client_1", true), "Current measurement rejected.");
        Check(recorder.BuildCompletedRowJson(second).Contains(second) && recorder.BuildCompletedRowJson(first) == "",
            "Completed row lookup is not measurement-scoped.");
        Check(recorder.RecordTransferRequested("dup", "Client_1", "Client_0") ==
              recorder.RecordTransferRequested("dup", "Client_1", "Client_0"),
            "Suppressed duplicate request did not return the pending measurement ID.");
        recorder.CancelPendingTransfer("dup", "Client_1");
        var s = recorder.BuildMeasurementSummary();
        Check(s.staleTerminalRejected == 2 && s.initiated == 3 && s.completed == 1 && s.canceled == 2 && s.countsReconcile,
            "Stale rejections changed or broke local reconciliation.");

        // Multi-object measurement completes only when every requested native object resolved.
        var r = new ControlledClockRecorder { Ticks = 0 };
        var tracker = new AuthorityMeasurementTracker(() => r);
        Check(tracker.Begin("microscope", "Actor_2", "Actor_1", new[] { "view:1", "view:2" }, 0d, "start"), "Begin failed.");
        Check(!tracker.Begin("microscope", "Actor_2", "Actor_1", new[] { "view:1" }, 0d, "retry"),
            "Retry while pending started a second measurement.");
        Check(!tracker.ObserveLocal("view:99", "unrelated PhotonView"), "Unrelated native object was attributed.");
        Check(tracker.ObserveLocal("view:1", "callback") && !tracker.TryResolve("Actor_2", "", out _, out _),
            "Aggregate completed after the first of two objects.");
        r.Ticks = 40;
        tracker.ObserveLocal("view:2", "callback");
        Check(tracker.TryResolve("Actor_2", "all local", out bool ok, out string id) && ok && !tracker.IsPending,
            "Aggregate did not complete after every object resolved.");
        Check(r.Rows.Count == 1 && r.Rows[0].measurementId == id && r.Rows[0].transferLatencyMs == 40d,
            "Aggregate completion row/duration incorrect.");
        Check(!tracker.ObserveLocal("view:1", "duplicate callback") && r.Rows.Count == 1 &&
              r.LifecycleRows.Last().eventType == "LateObservationIgnored" && r.LifecycleRows.Last().measurementId == id,
            "Callback after completion was not journaled as late or changed results.");

        // Any failed native object makes the aggregate a failure.
        tracker.Begin("microscope", "Actor_2", "Actor_1", new[] { "view:1", "view:2" }, 1d, "start");
        tracker.ObserveLocal("view:1", "");
        tracker.ObserveFailed("view:2", "");
        Check(tracker.TryResolve("Actor_1", "", out ok, out _) && !ok && r.LifecycleRows.Any(x => x.eventType == "Failed"),
            "Partial failure was not reported as a failed measurement.");

        // Delayed callback after cancel is late; it does not create a completion.
        tracker.Begin("microscope", "Actor_2", "Actor_1", new[] { "view:1" }, 2d, "start");
        string canceledId = tracker.MeasurementId;
        tracker.Cancel("holder changed");
        int rowsBefore = r.Rows.Count;
        Check(!tracker.ObserveLocal("view:1", "delayed") && r.Rows.Count == rowsBefore &&
              r.LifecycleRows.Last().measurementId == canceledId, "Delayed callback after cancel was not isolated.");

        // Captured holder: terminal events use the request-time holder ID, not a later local ID.
        tracker.Begin("microscope", "Client_7", "None", new[] { "root" }, 3d, "start");
        tracker.Cancel("disconnect");
        Check(r.PendingTransferCount == 0, "Cancellation did not close the request-time holder's measurement.");

        // Timeout closes the measurement; a later native observation is late, not a completion.
        tracker.Begin("microscope", "Actor_2", "Actor_1", new[] { "root" }, 10d, "start");
        Check(!tracker.CheckTimeout(19.9d, 10d, "") && tracker.IsPending, "Timeout fired early.");
        Check(!tracker.CheckTimeout(30d, 0d, "") && tracker.IsPending, "Disabled timeout fired.");
        Check(tracker.CheckTimeout(20d, 10d, "no authority") && !tracker.IsPending, "Timeout did not fire.");
        rowsBefore = r.Rows.Count;
        tracker.ObserveLocal("root", "late");
        Check(r.Rows.Count == rowsBefore, "Observation after timeout produced a completion.");

        // Documented limitation: SDK callbacks carry no request token. After cancel + new request on the
        // same native object, a local-authority observation is attributed to the newer measurement.
        tracker.Begin("microscope", "Actor_2", "Actor_1", new[] { "root" }, 40d, "A");
        tracker.Cancel("revoked");
        tracker.Begin("microscope", "Actor_2", "Actor_1", new[] { "root" }, 41d, "B");
        string newer = tracker.MeasurementId;
        tracker.ObserveLocal("root", "ambiguous origin");
        tracker.TryResolve("Actor_2", "", out _, out string closed);
        Check(closed == newer, "Observation after re-request was not attributed to the pending measurement.");

        var summary = r.BuildMeasurementSummary();
        Check(summary.countsReconcile && summary.timedOut == 1 && summary.lateObservationsIgnored >= 3 && summary.pending == 0,
            "Tracker lifecycle counts do not reconcile.");
        Check(!new AuthorityMeasurementTracker(() => r).Begin("x", "u", "o", new string[0], 0d, ""),
            "Measurement began without native objects.");
        Console.WriteLine("PASS: measurement-scoped terminal events, stale rejection, aggregate all-object completion, partial failure, unrelated callback isolation, late callbacks after completion/cancel/timeout, captured holder, timeout, re-request attribution limitation.");
    }
}

internal sealed class ControlledObservationRecorder : NativeAuthorityObservationRecorder
{
    public long Ticks;
    public int Frame;
    protected override long ReadMonotonicTicks() => Ticks;
    protected override long ClockFrequencyHz => 1000;
    protected override int ReadFrame() => Frame;
}

internal sealed class ControlledClockRecorder : AuthorityTransferRecorder
{
    public long Ticks;
    protected override long ReadMonotonicTicks() => Ticks;
    protected override long ClockFrequencyHz => 1000;
}

namespace UnityEngine
{
    public class MonoBehaviour
    {
        protected object gameObject;
        protected static void Destroy(object value) {}
    }
    public class HeaderAttribute : Attribute { public HeaderAttribute(string value) {} }
    public class SerializeField : Attribute {}
    public static class Debug { public static void Log(object value, object context) {} }
    public static class Time { public static int frameCount; }
    public static class JsonUtility
    {
        private static readonly System.Text.Json.JsonSerializerOptions Options =
            new System.Text.Json.JsonSerializerOptions { IncludeFields = true };
        public static string ToJson(object value) => System.Text.Json.JsonSerializer.Serialize(value, Options);
        public static T FromJson<T>(string value) => System.Text.Json.JsonSerializer.Deserialize<T>(value, Options);
    }
}

namespace Thesis.Orchestration.Telemetry
{
    public class EvaluationRunContext
    {
        public static EvaluationRunContext Instance;
        public string RunId = "Regression";
        public string BackendName = "Stubbed";
        public string ScenarioId = "AuthorityLifecycle";
    }
}
