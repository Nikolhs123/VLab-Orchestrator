using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Thesis.Orchestration.EditorTools;
using Thesis.Orchestration.SharedSync;
using Thesis.Orchestration.Telemetry;
using UnityEngine;

namespace Thesis.Orchestration.Runtime.Scenarios
{
    // Two-participant lifecycle cases (W06/W11/W12; R1-C05, R2-C06, R2-C08), selected with
    //   -vlabLifecycle HolderLeavesWhileOtherWaits | QueueAuthorityLeaves | LateJoin | LeaveAndRejoin |
    //                  NearConcurrentRequests
    // together with -vlabSession host|client (CommandLineSessionAutomation joins the session). Each case
    // records events and assertions with ScenarioMetricsRecorder and exports this participant's data
    // (host: coordinator export, client: participant export) before it leaves. "Abrupt" departures end the
    // process without leaving the session, emulating a crash or connection loss.
    [DisallowMultipleComponent]
    public sealed class LifecycleScenarioDriver : MonoBehaviour
    {
        private const float StepTimeoutSeconds = 30f;
        private const float AuthorityTimeoutSeconds = 15f;
        private const float DepartureTimeoutSeconds = 120f;
        private const float LateJoinYawDegrees = 45f;
        private const float ToleranceDegrees = 2f;

        private OrchestrationProjectConfig config;
        private IOrchestrationBackendRuntime runtime;
        private ScenarioMetricsRecorder recorder;
        private string lifecycleCase;
        private bool isHost;
        private string roomName;

        private IOrchestrationSessionView Session => runtime?.SessionView;
        private IOrchestrationQueueView Queue => runtime?.QueueView;

        private void Start()
        {
            config = Resources.Load<OrchestrationProjectConfig>("AIMultiplayerAssistant/OrchestrationProjectConfig");
            lifecycleCase = config != null ? config.LifecycleCase : string.Empty;
            isHost = string.Equals(GetArgument("-vlabSession"), "host", StringComparison.OrdinalIgnoreCase);
            roomName = GetArgument("-vlabRoom");
            StartCoroutine(Run());
        }

        private IEnumerator Run()
        {
            while ((runtime = config != null ? BackendRuntimeResolver.Resolve(config.backendType) : null) == null)
                yield return new WaitForSeconds(0.25f);

            recorder = ScenarioMetricsRecorder.Instance ?? EvaluationMetricsManager.ResolveOrCreate(this)?.ScenarioMetricsRecorder;
            Log($"case={lifecycleCase} role={(isHost ? "host" : "client")} backend={config.backendType}");

            yield return WaitFor(() => Session != null && Session.IsInRoom, 120f, "InRoom");
            if (EvaluationRunContext.Instance != null)
                EvaluationRunContext.Instance.SessionName = Session.CurrentRoomName ?? roomName ?? "";
            recorder?.MarkScenarioStarted("Lifecycle", 2, 1);

            switch (lifecycleCase)
            {
                case "HolderLeavesWhileOtherWaits":
                    yield return isHost ? HolderLeavesHost() : HolderLeavesClient();
                    break;
                case "QueueAuthorityLeaves":
                    yield return isHost ? QueueAuthorityLeavesHost() : QueueAuthorityLeavesClient();
                    break;
                case "LateJoin":
                    yield return isHost ? LateJoinHost() : LateJoinClient();
                    break;
                case "LeaveAndRejoin":
                    yield return isHost ? LeaveAndRejoinHost() : LeaveAndRejoinClient();
                    break;
                case "NearConcurrentRequests":
                    yield return NearConcurrentRequests();
                    break;
                default:
                    Assert("KnownLifecycleCase", "Known case", lifecycleCase, false);
                    yield return ExportAndQuit(abrupt: false);
                    break;
            }
        }

        // ---- L1: the holder leaves abruptly while the other participant waits ----

        private IEnumerator HolderLeavesHost()
        {
            yield return BeginSessionWhenTwoPresent();
            bool remoteHolds = false;
            yield return WaitFor(() => remoteHolds = IsRemoteHolder(), StepTimeoutSeconds, "RemoteHolderGranted");
            Event("RequestAccessWhileRemoteHolds");
            Queue.RequestAccessForLocalPlayer();
            yield return WaitFor(() => Queue.CurrentQueueSnapshot.Contains(LocalId), StepTimeoutSeconds, "LocalQueued");

            // After an abrupt departure the waiting participant can be granted either because the
            // backend reports the departure (queue processes it) or because the holder's idle timeout
            // expires first. Record which happened and how long departure detection took.
            float departureStart = Time.realtimeSinceStartup;
            yield return WaitFor(() => IsLocalHolder() || PlayerCount() < 2, DepartureTimeoutSeconds, "ReassignmentOrDepartureObserved");
            bool departureFirst = PlayerCount() < 2 && !IsLocalHolder();
            Event(departureFirst ? "DepartureDetectedBeforeReassignment" : "ReassignedBeforeDepartureDetected");
            bool reassigned = false;
            yield return WaitFor(() => reassigned = IsLocalHolder(), StepTimeoutSeconds, "HolderReassigned");
            Assert("HolderReassignedToWaitingParticipant", LocalId, Queue.CurrentHolderUserId, reassigned,
                departureFirst ? "path=DepartureDetected" : "path=GrantedBeforeDepartureDetected (idle timeout)");
            Assert("QueueEmptyAfterReassignment", "Empty", string.Join("|", Queue.CurrentQueueSnapshot), Queue.CurrentQueueSnapshot.Count == 0);
            bool native = false;
            yield return WaitFor(() => native = LocalNativeAuthority(), AuthorityTimeoutSeconds, "NativeAuthorityAcquired");
            Assert("NativeAuthorityAcquiredAfterReassignment", "True", native.ToString(), native);
            Queue.ReleaseAccessForLocalPlayer();
            yield return WaitFor(() => PlayerCount() < 2, DepartureTimeoutSeconds, "DepartureDetected");
            Assert("ParticipantCountAfterDeparture", "1", PlayerCount().ToString(CultureInfo.InvariantCulture), PlayerCount() == 1,
                $"departureDetectionSeconds={(Time.realtimeSinceStartup - departureStart).ToString("0.#", CultureInfo.InvariantCulture)} (upper bound from host's first wait)");
            yield return WaitFor(() => !IsLocalHolder(), StepTimeoutSeconds, "Released");
            Assert("NoResidualHolderAfterRelease", "None", Queue.CurrentHolderUserId, IsNone(Queue.CurrentHolderUserId));
            yield return ExportAndQuit(abrupt: false);
        }

        private IEnumerator HolderLeavesClient()
        {
            yield return WaitFor(() => Queue != null && Queue.QueueRuntimeActive && Session.SessionStarted, 60f, "SessionStarted");
            Event("RequestAccess");
            Queue.RequestAccessForLocalPlayer();
            bool granted = false;
            yield return WaitFor(() => granted = IsLocalHolder(), StepTimeoutSeconds, "LocalGranted");
            yield return WaitFor(LocalNativeAuthority, AuthorityTimeoutSeconds, "LocalNativeAuthority");
            yield return WaitFor(() => Queue.CurrentQueueSnapshot.Count > 0, StepTimeoutSeconds, "OtherWaiting");
            Assert("HolderWithWaitingParticipantBeforeDeparture", "Holder+1Waiting",
                $"holder={Queue.CurrentHolderUserId};queue={Queue.CurrentQueueSnapshot.Count}", granted && Queue.CurrentQueueSnapshot.Count > 0);
            yield return ExportAndQuit(abrupt: true);
        }

        // ---- L2: the participant running the queue (host / queue authority) leaves while the other waits ----

        private IEnumerator QueueAuthorityLeavesHost()
        {
            yield return BeginSessionWhenTwoPresent();
            Queue.RequestAccessForLocalPlayer();
            yield return WaitFor(IsLocalHolder, StepTimeoutSeconds, "LocalGranted");
            yield return WaitFor(LocalNativeAuthority, AuthorityTimeoutSeconds, "LocalNativeAuthority");
            yield return WaitFor(() => Queue.CurrentQueueSnapshot.Count > 0, StepTimeoutSeconds, "OtherWaiting");
            Assert("QueueAuthorityHoldsWithWaitingParticipant", "Holder+1Waiting",
                $"holder={Queue.CurrentHolderUserId};queue={Queue.CurrentQueueSnapshot.Count}", Queue.CurrentQueueSnapshot.Count > 0);
            yield return ExportAndQuit(abrupt: true);
        }

        private IEnumerator QueueAuthorityLeavesClient()
        {
            yield return WaitFor(() => Queue != null && Queue.QueueRuntimeActive && IsRemoteHolder(), 60f, "RemoteHolderGranted");
            Queue.RequestAccessForLocalPlayer();
            yield return WaitFor(() => Queue.CurrentQueueSnapshot.Contains(LocalId), StepTimeoutSeconds, "LocalQueued");
            float departureStart = Time.realtimeSinceStartup;
            yield return WaitFor(() => !Session.IsInRoom || PlayerCount() < 2, DepartureTimeoutSeconds, "QueueAuthorityDeparted");
            Event($"departureDetectionSeconds={(Time.realtimeSinceStartup - departureStart).ToString("0.#", CultureInfo.InvariantCulture)}");
            Event("QueueAuthorityDepartureObserved");
            LogTelemetryPresence("at departure");

            // Observe what the backend does; outcomes differ by design and are recorded, not assumed.
            float start = Time.realtimeSinceStartup;
            while (Time.realtimeSinceStartup - start < 20f && !Safe(IsLocalHolder))
                yield return null;

            LogTelemetryPresence("after observation window");
            string outcome;
            string details;
            try
            {
                outcome = !Session.IsInRoom ? "SessionEnded"
                    : IsLocalHolder() ? "QueueContinuedWaitingParticipantGranted"
                    : Queue.QueueRuntimeActive ? "QueueStateRetainedButNotAdvanced"
                    : "QueueRuntimeInactive";
                details = $"inRoom={Session.IsInRoom} master={Session.IsMasterClient} queueActive={Queue.QueueRuntimeActive} " +
                          $"holder={Queue.CurrentHolderUserId} queue={string.Join("|", Queue.CurrentQueueSnapshot)}";
            }
            catch (Exception exception)
            {
                outcome = "BackendRuntimeUnavailable";
                details = exception.GetType().Name;
            }

            Assert("ObservedOutcomeAfterQueueAuthorityLeft", "Recorded", outcome, true, details, required: false);

            // Where the backend keeps the session and the queue alive (authority migration), the queue must
            // also advance: a retained but frozen queue is a defect, not an outcome.
            if (Safe(() => Session.IsInRoom && Queue.QueueRuntimeActive))
            {
                bool granted = Safe(IsLocalHolder);
                Assert("WaitingParticipantGrantedAfterAuthorityMigration", LocalId, Queue.CurrentHolderUserId, granted, details);
                if (granted)
                {
                    bool native = false;
                    yield return WaitFor(() => native = LocalNativeAuthority(), AuthorityTimeoutSeconds, "NativeAuthorityAfterMigration");
                    Assert("NativeAuthorityAcquiredAfterAuthorityMigration", "True", native.ToString(), native);
                }
            }

            yield return ExportAndQuit(abrupt: false);
        }

        // ---- L3: late join after shared state exists ----

        private IEnumerator LateJoinHost()
        {
            Session.BeginSession();
            yield return WaitFor(() => Queue != null && Queue.QueueRuntimeActive, StepTimeoutSeconds, "QueueActive");
            Queue.RequestAccessForLocalPlayer();
            yield return WaitFor(IsLocalHolder, StepTimeoutSeconds, "LocalGranted");
            yield return WaitFor(LocalNativeAuthority, AuthorityTimeoutSeconds, "LocalNativeAuthority");
            Transform target = ResolveTarget();
            if (target != null)
                target.localRotation = Quaternion.Euler(0f, LateJoinYawDegrees, 0f);
            Event($"SharedStateSet yaw={LateJoinYawDegrees}");
            yield return new WaitForSeconds(2f);
            Queue.ReleaseAccessForLocalPlayer();
            yield return WaitFor(() => IsNone(Queue.CurrentHolderUserId), StepTimeoutSeconds, "Released");
            yield return WaitFor(() => PlayerCount() >= 2, 120f, "LateJoinerPresent");
            Event("LateJoinerObserved");
            yield return WaitFor(() => PlayerCount() < 2, 120f, "LateJoinerLeft");
            yield return ExportAndQuit(abrupt: false);
        }

        private IEnumerator LateJoinClient()
        {
            yield return WaitFor(() => Queue != null && Queue.QueueRuntimeActive, StepTimeoutSeconds, "QueueActiveOnJoin");
            yield return new WaitForSeconds(3f);
            Transform target = ResolveTarget();
            float error = target != null ? Quaternion.Angle(target.localRotation, Quaternion.Euler(0f, LateJoinYawDegrees, 0f)) : float.PositiveInfinity;
            Assert("LateJoinSharedStateReconstructed", $"yaw={LateJoinYawDegrees}",
                target != null ? $"euler={target.localRotation.eulerAngles} error={error:0.###}" : "TargetMissing", error <= ToleranceDegrees,
                target != null ? $"activeInHierarchy={target.gameObject.activeInHierarchy} root={target.root.name} controllers={DescribeAccessControllers()}" : DescribeAccessControllers());
            Assert("LateJoinHolderNone", "None", Queue.CurrentHolderUserId, IsNone(Queue.CurrentHolderUserId));
            Queue.RequestAccessForLocalPlayer();
            bool granted = false;
            yield return WaitFor(() => granted = IsLocalHolder(), StepTimeoutSeconds, "LateJoinerGranted");
            Assert("LateJoinerCanAcquire", LocalId, Queue.CurrentHolderUserId, granted);
            bool native = false;
            yield return WaitFor(() => native = LocalNativeAuthority(), AuthorityTimeoutSeconds, "LateJoinerNativeAuthority");
            Assert("LateJoinerNativeAuthorityAcquired", "True", native.ToString(), native, DescribeAccessControllers());
            Queue.ReleaseAccessForLocalPlayer();
            yield return WaitFor(() => !IsLocalHolder(), StepTimeoutSeconds, "Released");
            yield return ExportAndQuit(abrupt: false, leaveFirst: true);
        }

        // Diagnostic context for authority failures: which access controllers exist on this participant and
        // whether they are running, so an inactive replica is distinguishable from a rejected request.
        private static string DescribeAccessControllers()
        {
            return string.Join(" | ", FindObjectsByType<BaseMicroscopeAccessController>(FindObjectsInactive.Include, FindObjectsSortMode.None)
                .Select(c => $"{c.name}:active={c.gameObject.activeInHierarchy},enabled={c.enabled},access={c.HasLocalAccess}"));
        }

        // ---- L4: graceful leave while queued, then rejoin ----

        private IEnumerator LeaveAndRejoinHost()
        {
            yield return BeginSessionWhenTwoPresent();
            Queue.RequestAccessForLocalPlayer();
            yield return WaitFor(IsLocalHolder, StepTimeoutSeconds, "LocalGranted");
            yield return WaitFor(() => Queue.CurrentQueueSnapshot.Count == 1, StepTimeoutSeconds, "OtherQueued");
            yield return WaitFor(() => PlayerCount() < 2, 60f, "OtherLeft");
            bool cleared = false;
            yield return WaitFor(() => cleared = Queue.CurrentQueueSnapshot.Count == 0, StepTimeoutSeconds, "QueueClearedAfterLeave");
            Assert("QueueEntryRemovedOnLeave", "Empty", string.Join("|", Queue.CurrentQueueSnapshot), cleared);
            yield return WaitFor(() => PlayerCount() >= 2, 60f, "OtherRejoined");
            yield return WaitFor(() => Queue.CurrentQueueSnapshot.Count >= 1, StepTimeoutSeconds, "OtherQueuedAgain");
            yield return new WaitForSeconds(1f);
            Assert("SingleQueueEntryAfterRejoin", "1", Queue.CurrentQueueSnapshot.Count.ToString(CultureInfo.InvariantCulture),
                Queue.CurrentQueueSnapshot.Count == 1, string.Join("|", Queue.CurrentQueueSnapshot));
            Assert("ParticipantCountAfterRejoin", "2", PlayerCount().ToString(CultureInfo.InvariantCulture), PlayerCount() == 2);
            Queue.ReleaseAccessForLocalPlayer();
            yield return WaitFor(() => !IsLocalHolder() && !IsNone(Queue.CurrentHolderUserId), StepTimeoutSeconds, "HandedToRejoined");
            yield return WaitFor(() => PlayerCount() < 2, 90f, "OtherLeftFinally");
            Assert("NoResidualStateAfterAllLeft", "Holder=None;Queue=Empty",
                $"Holder={Queue.CurrentHolderUserId};Queue={Queue.CurrentQueueSnapshot.Count}",
                IsNone(Queue.CurrentHolderUserId) && Queue.CurrentQueueSnapshot.Count == 0);
            yield return ExportAndQuit(abrupt: false);
        }

        private IEnumerator LeaveAndRejoinClient()
        {
            yield return WaitFor(() => Queue != null && Queue.QueueRuntimeActive && IsRemoteHolder(), 60f, "RemoteHolderGranted");
            Queue.RequestAccessForLocalPlayer();
            yield return WaitFor(() => Queue.CurrentQueueSnapshot.Contains(LocalId), StepTimeoutSeconds, "LocalQueued");
            string firstId = LocalId;
            Event("LeaveRoomWhileQueued");
            Session.LeaveRoom();
            yield return WaitFor(() => !Session.IsInRoom, StepTimeoutSeconds, "Left");
            yield return new WaitForSeconds(3f);
            Event("Rejoin");
            float rejoinStart = Time.realtimeSinceStartup;
            while (!Session.IsInRoom && Time.realtimeSinceStartup - rejoinStart < 60f)
            {
                if (!Session.IsConnectedToServer)
                    Session.Connect();
                else
                    Session.JoinRoom(roomName);
                float attempt = Time.realtimeSinceStartup;
                while (!Session.IsInRoom && Time.realtimeSinceStartup - attempt < 10f)
                    yield return null;
            }

            Assert("RejoinedSession", "InRoom", Session.IsInRoom.ToString(), Session.IsInRoom, $"firstId={firstId} rejoinId={LocalId}");
            yield return WaitFor(() => Queue != null && Queue.QueueRuntimeActive, StepTimeoutSeconds, "QueueActiveAfterRejoin");
            yield return new WaitForSeconds(2f);
            Queue.RequestAccessForLocalPlayer();
            bool granted = false;
            yield return WaitFor(() => granted = IsLocalHolder(), StepTimeoutSeconds + 10f, "GrantedAfterRejoin");
            Assert("RejoinedParticipantGrantedInTurn", LocalId, Queue.CurrentHolderUserId, granted);
            Queue.ReleaseAccessForLocalPlayer();
            yield return WaitFor(() => !IsLocalHolder(), StepTimeoutSeconds, "Released");
            yield return ExportAndQuit(abrupt: false, leaveFirst: true);
        }

        // ---- L5: near-concurrent requests from both participants, repeated rounds ----
        // Both participants request as soon as the instrument is free and nobody waits, each after a random
        // 0-300 ms delay (fixed seeds), so that both authoritative arrival orders occur despite the asymmetry
        // between the authority's local request and the remote one (observed with 0-100 ms on the host only:
        // one order only on Netcode, PUN2 and Fusion). Ordering is judged from the authority's queue_transitions.csv, not from
        // cross-process clocks. Per participant: every request is granted exactly once (no lost request), the
        // queue never holds duplicates or the holder, and every grant leads to native authority.

        private const int ConcurrentRounds = 10;
        private const float ConcurrentHoldSeconds = 0.5f;
        private const float ConcurrentMaxDelaySeconds = 0.3f;

        private IEnumerator NearConcurrentRequests()
        {
            if (isHost)
                yield return BeginSessionWhenTwoPresent();
            else
                yield return WaitFor(() => Queue != null && Queue.QueueRuntimeActive && Session.SessionStarted, 60f, "QueueActive");

            var random = new System.Random(isHost ? 1 : 2);
            int grants = 0, nativeFailures = 0, contendedHolds = 0;
            bool duplicateObserved = false;
            string duplicateDetails = "";
            bool monitoring = true;
            StartCoroutine(MonitorQueueIntegrity());

            IEnumerator MonitorQueueIntegrity()
            {
                while (monitoring)
                {
                    List<string> queue = Queue != null ? Queue.CurrentQueueSnapshot.ToList() : new List<string>();
                    string holder = Queue != null ? Queue.CurrentHolderUserId : "None";
                    if (!duplicateObserved && (queue.Count != queue.Distinct(StringComparer.OrdinalIgnoreCase).Count() ||
                                               (!IsNone(holder) && queue.Contains(holder, StringComparer.OrdinalIgnoreCase))))
                    {
                        duplicateObserved = true;
                        duplicateDetails = $"holder={holder} queue={string.Join("|", queue)}";
                    }

                    yield return null;
                }
            }

            for (int round = 1; round <= ConcurrentRounds; round++)
            {
                bool free = false;
                yield return WaitFor(() => free = IsNone(Queue.CurrentHolderUserId) && Queue.CurrentQueueSnapshot.Count == 0 && PlayerCount() >= 2,
                    StepTimeoutSeconds, $"Round{round}Free");
                if (!free)
                    break;

                yield return new WaitForSecondsRealtime((float)random.NextDouble() * ConcurrentMaxDelaySeconds);

                Event($"Round{round}RequestAccess");
                Queue.RequestAccessForLocalPlayer();
                bool granted = false;
                yield return WaitFor(() => granted = IsLocalHolder(), StepTimeoutSeconds, $"Round{round}Granted");
                if (!granted)
                    break;

                grants++;
                Event($"Round{round}Granted");

                bool native = false;
                yield return WaitFor(() => native = LocalNativeAuthority(), AuthorityTimeoutSeconds, $"Round{round}NativeAuthority");
                if (!native)
                    nativeFailures++;

                // Contention as seen locally: the other participant's request reached the authority while this one
                // held the instrument (it appears in the replicated queue). Arrival order itself is taken from the
                // authority's queue_transitions.csv (AccessQueued rows).
                bool contended = false;
                float holdStart = Time.realtimeSinceStartup;
                while (Time.realtimeSinceStartup - holdStart < ConcurrentHoldSeconds)
                {
                    contended |= Queue.CurrentQueueSnapshot.Count > 0;
                    yield return null;
                }

                if (contended)
                    contendedHolds++;
                Queue.ReleaseAccessForLocalPlayer();
                yield return WaitFor(() => !IsLocalHolder(), StepTimeoutSeconds, $"Round{round}Released");
            }

            monitoring = false;
            Assert("EveryRequestGrantedOnce", ConcurrentRounds.ToString(CultureInfo.InvariantCulture),
                grants.ToString(CultureInfo.InvariantCulture), grants == ConcurrentRounds);
            Assert("NoDuplicateOrHolderInQueue", "None", duplicateObserved ? "Observed" : "None", !duplicateObserved, duplicateDetails);
            Assert("NativeAuthorityOnEveryGrant", "0 failures", $"{nativeFailures} failures", nativeFailures == 0);
            Assert("ContentionObservation", "Recorded", $"holdsWithOtherQueued={contendedHolds} of {grants}", true,
                "arrival order per request is in the authority's queue_transitions.csv", required: false);

            if (isHost)
            {
                yield return WaitFor(() => PlayerCount() < 2, 90f, "OtherLeft");
                yield return ExportAndQuit(abrupt: false);
            }
            else
            {
                // The participants' round counts drift apart (whoever releases first may start the next round);
                // leave only after the host has also finished, i.e. after the queue stayed idle for a while.
                float idleSince = Time.realtimeSinceStartup;
                float waitStart = Time.realtimeSinceStartup;
                while (Time.realtimeSinceStartup - idleSince < 5f && Time.realtimeSinceStartup - waitStart < 90f)
                {
                    if (!IsNone(Queue.CurrentHolderUserId) || Queue.CurrentQueueSnapshot.Count > 0)
                        idleSince = Time.realtimeSinceStartup;
                    yield return null;
                }

                Assert("QueueIdleBeforeLeave", "Idle 5 s", Time.realtimeSinceStartup - idleSince >= 5f ? "Idle" : "Busy", Time.realtimeSinceStartup - idleSince >= 5f);
                yield return ExportAndQuit(abrupt: false, leaveFirst: true);
            }
        }

        // ---- helpers ----

        // Whether this participant's evaluation recorders still exist (they could be torn down with
        // runtime objects when the session ends), so data loss on departure is observable in the log.
        private void LogTelemetryPresence(string when)
        {
            Log($"Telemetry presence {when}: metricsManager={EvaluationMetricsManager.Instance != null} " +
                $"scenarioRecorder={ScenarioMetricsRecorder.Instance != null} " +
                $"nativeObservations={(NativeAuthorityObservationRecorder.Instance != null ? NativeAuthorityObservationRecorder.Instance.Rows.Count : -1)} " +
                $"backendRuntime={(runtime as UnityEngine.Object) != null}");
        }

        private static bool Safe(Func<bool> condition)
        {
            try { return condition(); }
            catch (Exception) { return false; }
        }

        private string LocalId => Session?.LocalUserId ?? "";

        private int PlayerCount() => Session?.CurrentPlayers?.Count ?? 0;

        private bool IsLocalHolder() =>
            Queue != null && !string.IsNullOrWhiteSpace(LocalId) &&
            string.Equals(Queue.CurrentHolderUserId, LocalId, StringComparison.OrdinalIgnoreCase);

        private bool IsRemoteHolder() =>
            Queue != null && !IsNone(Queue.CurrentHolderUserId) && !IsLocalHolder();

        private static bool IsNone(string holder) =>
            string.IsNullOrWhiteSpace(holder) || string.Equals(holder, "None", StringComparison.OrdinalIgnoreCase);

        private bool LocalNativeAuthority()
        {
            return NativeAuthorityObservationRecorder.Instance != null &&
                   NativeAuthorityObservationRecorder.Instance.TryGetLatest(Queue?.CurrentInstrumentId,
                       out NativeAuthorityObservationRecorder.ObservationRow row) &&
                   row.localNativeAuthority;
        }

        private IEnumerator BeginSessionWhenTwoPresent()
        {
            yield return WaitFor(() => PlayerCount() >= 2, 120f, "TwoParticipantsPresent");
            if (!Session.SessionStarted)
                Session.BeginSession();
            yield return WaitFor(() => Queue != null && Queue.QueueRuntimeActive && Session.SessionStarted, StepTimeoutSeconds, "QueueActive");
        }

        private Transform ResolveTarget()
        {
            // Prefer the active child of a registered shared object; the local-only showcase visual carries a
            // child with the same name but is inactive in the networked runtime. Inactive objects are still
            // returned as a last resort so "present but inactive" is distinguishable from "missing".
            string childName = config != null ? config.automatedScenarioManipulatedChildName : "Controlled Turret";
            return FindObjectsByType<Transform>(FindObjectsInactive.Include, FindObjectsSortMode.None)
                .Where(t => t.name == childName)
                .OrderByDescending(t => t.gameObject.activeInHierarchy)
                .ThenByDescending(t => t.GetComponentInParent<SharedSyncObjectDefinition>(true) != null)
                .FirstOrDefault();
        }

        private IEnumerator WaitFor(Func<bool> condition, float timeoutSeconds, string stepName)
        {
            float start = Time.realtimeSinceStartup;
            while (Time.realtimeSinceStartup - start < timeoutSeconds)
            {
                bool done;
                try { done = condition(); }
                catch (Exception) { done = false; }
                if (done)
                {
                    Event($"{stepName} after {(Time.realtimeSinceStartup - start) * 1000f:0.#} ms");
                    yield break;
                }

                yield return null;
            }

            Assert($"Step:{stepName}", "Reached", $"Timeout after {timeoutSeconds:0.#} s", false,
                $"holder={Queue?.CurrentHolderUserId} queue={string.Join("|", Queue?.CurrentQueueSnapshot ?? new List<string>())} players={PlayerCount()}");
        }

        private void Event(string details)
        {
            Log(details);
            recorder?.RecordEvent(0, "Lifecycle", isHost ? "Host" : "Client", LocalId, lifecycleCase, 0d,
                "", Queue?.CurrentHolderUserId ?? "None", true, details);
        }

        private void Assert(string id, string expected, string actual, bool passed, string details = "", bool required = true)
        {
            Log($"ASSERT {id} {(passed ? "PASS" : "FAIL")} expected={expected} actual={actual} {details}");
            recorder?.RecordAssertion(0, id, isHost ? "Host" : "Client", expected, actual, passed, 0d, details, required);
        }

        private IEnumerator ExportAndQuit(bool abrupt, bool leaveFirst = false)
        {
            yield return new WaitForSeconds(1f);
            EvaluationMetricsManager manager = EvaluationMetricsManager.ResolveOrCreate(this);
            string path = isHost ? manager?.ExportAll(null, LocalId) : manager?.ExportAll(string.IsNullOrWhiteSpace(LocalId) ? "UnknownParticipant" : LocalId);
            Log($"Exported: {path}");

            if (abrupt)
            {
                Log("Abrupt departure: terminating process without leaving the session.");
                System.Diagnostics.Process.GetCurrentProcess().Kill();
                yield break;
            }

            if (leaveFirst && Session != null && Session.IsInRoom)
            {
                Session.LeaveRoom();
                yield return new WaitForSeconds(2f);
            }

            Log("Quit.");
#if UNITY_EDITOR
            UnityEditor.EditorApplication.isPlaying = false;
#else
            Application.Quit(0);
#endif
        }

        private static string GetArgument(string key)
        {
            string[] arguments = Environment.GetCommandLineArgs();
            for (int i = 0; i < arguments.Length - 1; i++)
            {
                if (string.Equals(arguments[i], key, StringComparison.OrdinalIgnoreCase))
                    return arguments[i + 1];
            }
            return string.Empty;
        }

        private static void Log(string message) => Debug.Log($"[VLab Lifecycle] {message}");
    }
}
