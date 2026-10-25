using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Thesis.Orchestration.EditorTools;
using Thesis.Orchestration.SharedSync;
using Thesis.Orchestration.Telemetry;
using UnityEngine;
using FusionNetworkObject = Fusion.NetworkObject;
using FishNetNetworkObject = FishNet.Object.NetworkObject;
using MirrorNetworkIdentity = global::Mirror.NetworkIdentity;
using NetcodeNetworkObject = Unity.Netcode.NetworkObject;
using PunPhotonView = Photon.Pun.PhotonView;

namespace Thesis.Orchestration.Runtime.Scenarios
{
    [DisallowMultipleComponent]
    public class OrchestrationScenarioRunner : MonoBehaviour
    {
        public static event Action<string> BenchmarkStatusChanged;
        public static string LastBenchmarkStatus { get; private set; } = string.Empty;

        [SerializeField] private OrchestrationProjectConfig projectConfig;
        [SerializeField] private bool verboseLogs = true;

        private IOrchestrationBackendRuntime backendRuntime;
        private IOrchestrationSessionView sessionView;
        private IOrchestrationQueueView queueView;
        private ScenarioMetricsRecorder recorder;
        private Coroutine scenarioCoroutine;
        private string localRole = "Observer";
        private bool scenarioCompleted;
        private float nextRuntimeResolveTime;
        private Transform benchmarkManipulationTarget;
        private Quaternion benchmarkInitialLocalRotation;
        private Quaternion benchmarkExpectedFinalLocalRotation;
        private bool benchmarkRotationCaptured;
        private const float TransformSampleIntervalSeconds = 0.1f;
        private int observedPhaseIndex;
        private int observedPhaseCycle;
        private string observedPhaseRole = "";
        private string observedPhaseActor = "";
        private Coroutine transformSamplerCoroutine;
        private const float AccessProbePitchDegrees = 20f;
        private const float AccessProbeCheckDelaySeconds = 1f;
        private bool accessProbesEnabled;
        private int accessProbeSequence;
        private bool infoLogsSuppressed;
        private LogType previousLogFilter;

        private void Awake()
        {
            ResolveConfig();
        }

        private void OnEnable()
        {
            if (projectConfig == null || !projectConfig.enableAutomatedBenchmarkScenario)
                return;

            // A lifecycle case (LifecycleScenarioDriver) replaces the benchmark scenario in this process.
            if (!string.IsNullOrWhiteSpace(projectConfig.LifecycleCase))
                return;

            scenarioCoroutine = StartCoroutine(RunScenarioWhenReady());
        }

        private void OnDisable()
        {
            if (scenarioCoroutine != null)
            {
                StopCoroutine(scenarioCoroutine);
                scenarioCoroutine = null;
            }

            RestoreInfoLogs();
        }

        public void Configure(OrchestrationProjectConfig config)
        {
            projectConfig = config;
        }

        private IEnumerator RunScenarioWhenReady()
        {
            if (!ResolveConfig())
                yield break;

            recorder = ScenarioMetricsRecorder.Instance;
            recorder ??= EvaluationMetricsManager.ResolveOrCreate(this)?.ScenarioMetricsRecorder;

            while (!ResolveRuntime())
                yield return new WaitForSeconds(0.25f);

            Log($"Scenario waiting for {projectConfig.automatedScenarioExpectedParticipants} participants.");
            yield return WaitForParticipants(projectConfig.automatedScenarioExpectedParticipants);

            if (!ResolveRuntime())
                yield break;

            if (EvaluationRunContext.Instance != null)
                EvaluationRunContext.Instance.SessionName = sessionView?.CurrentRoomName ?? "";

            recorder?.MarkScenarioStarted(
                projectConfig.automatedScenarioMode.ToString(),
                projectConfig.automatedScenarioExpectedParticipants,
                projectConfig.automatedScenarioRepeatCount);
            recorder?.MarkObservedParticipants(sessionView?.CurrentPlayers?.Count ?? 0);

            if (projectConfig.automatedScenarioAutoBeginSession &&
                sessionView.IsMasterClient &&
                !sessionView.SessionStarted)
            {
                recorder?.RecordEvent(
                    0,
                    "Session",
                    "Master",
                    sessionView.LocalUserId,
                    "BeginSessionRequested",
                    0d,
                    "",
                    queueView?.CurrentHolderUserId ?? "None",
                    true,
                    "Automated scenario requested session start.");

                sessionView.BeginSession();
            }

            yield return WaitUntilOrTimeout(
                0,
                () => ResolveRuntime() && sessionView.SessionStarted,
                projectConfig.automatedScenarioStepTimeoutSeconds,
                "SessionStarted",
                "All",
                "True",
                () => sessionView != null ? sessionView.SessionStarted.ToString() : "False");

            bool scenarioConfigurationValid = ValidateScenarioConfiguration(out string configurationDetails);
            recorder?.RecordAssertion(
                0,
                "ScenarioConfigurationValid",
                "All",
                "CompatibleRunnerConfiguration",
                scenarioConfigurationValid ? "Valid" : "Invalid",
                scenarioConfigurationValid,
                0d,
                configurationDetails);

            if (projectConfig.automatedScenarioWarmupSeconds > 0f)
                yield return new WaitForSeconds(projectConfig.automatedScenarioWarmupSeconds);

            PrepareMeasuredWindow();

            if (scenarioConfigurationValid)
            {
                switch (projectConfig.automatedScenario)
                {
                    case OrchestrationAutomatedScenario.OwnershipPingPong2Users:
                    case OrchestrationAutomatedScenario.RapidOwnershipHandoffs2Users:
                    case OrchestrationAutomatedScenario.TransformReplication2Users:
                        yield return RunOwnershipPingPongClosedLoop();
                        break;
                    case OrchestrationAutomatedScenario.SimultaneousContention2Users:
                        yield return RunSimultaneousContentionClosedLoop();
                        break;
                    case OrchestrationAutomatedScenario.OpenLoopRequestStress2Users:
                        yield return RunOwnershipPingPongOpenLoop();
                        break;
                    default:
                        RecordScenarioFailure(
                            0,
                            "All",
                            "UnsupportedScenario",
                            $"No runner is mapped for {projectConfig.automatedScenario}.");
                        break;
                }

                yield return ValidateFinalManipulationState();
            }

            RestoreInfoLogs();

            scenarioCompleted = true;
            recorder?.RecordEvent(
                projectConfig.automatedScenarioRepeatCount,
                "Scenario",
                "All",
                sessionView?.LocalUserId ?? "",
                "ScenarioCompleted",
                0d,
                "None",
                queueView?.CurrentHolderUserId ?? "None",
                true,
                "Automated scenario completed.");

            if (projectConfig.automatedScenarioAutoExportOnComplete && sessionView != null && sessionView.IsMasterClient)
            {
                SetBenchmarkUiStatus("Benchmark completed. Finalizing CSV export - do not quit yet.");
                yield return ExportWhenReady();
            }
            else if (projectConfig.automatedScenarioAutoExportOnComplete)
            {
                // Each non-coordinator participant also keeps its own locally observed records
                // (native authority, transforms, lifecycle) instead of relying only on imports.
                string participantExport = EvaluationMetricsManager.ResolveOrCreate(this)?.ExportAll(
                    string.IsNullOrWhiteSpace(sessionView?.LocalUserId) ? "UnknownParticipant" : sessionView.LocalUserId);
                SetBenchmarkUiStatus(string.IsNullOrWhiteSpace(participantExport)
                    ? "BENCHMARK CLIENT STEPS COMPLETE - participant export FAILED - wait for the master export."
                    : $"BENCHMARK CLIENT STEPS COMPLETE - participant data exported - wait for the master export.\n{participantExport}");
            }
        }

        private IEnumerator RunOwnershipPingPongClosedLoop()
        {
            ApplyAccessProbe(0, "NoHolderBeforeFirstGrant");
            if (accessProbesEnabled)
                yield return new WaitForSecondsRealtime(AccessProbeCheckDelaySeconds + 0.5f);

            for (int cycle = 1; cycle <= projectConfig.automatedScenarioRepeatCount; cycle++)
            {
                SetBenchmarkUiStatus(
                    $"BENCHMARK RUNNING - cycle {cycle}/{projectConfig.automatedScenarioRepeatCount}, User1 phase.");
                yield return RunClosedLoopOwnershipPhase(cycle, "User1", "User2");
                SetBenchmarkUiStatus(
                    $"BENCHMARK RUNNING - cycle {cycle}/{projectConfig.automatedScenarioRepeatCount}, User2 phase.");
                yield return RunClosedLoopOwnershipPhase(cycle, "User2", cycle < projectConfig.automatedScenarioRepeatCount ? "User1" : null);
                recorder?.MarkCycleCompleted(cycle);

                if (projectConfig.automatedScenarioCooldownSeconds > 0f)
                    yield return new WaitForSeconds(projectConfig.automatedScenarioCooldownSeconds);
            }
        }

        private IEnumerator RunClosedLoopOwnershipPhase(int cycle, string role, string nextQueuedRole)
        {
            if (!TryResolveRole(role, out string actorUserId))
            {
                RecordScenarioFailure(cycle, role, "RoleResolveFailed", $"Could not resolve {role}.");
                yield break;
            }

            string nextActorUserId = "";
            bool hasNextActor = !string.IsNullOrWhiteSpace(nextQueuedRole) && TryResolveRole(nextQueuedRole, out nextActorUserId);
            bool isLocalActor = IsLocalUser(actorUserId);
            bool isLocalNextActor = hasNextActor && IsLocalUser(nextActorUserId);

            recorder?.RecordEvent(
                cycle,
                "Ownership",
                role,
                actorUserId,
                "PhaseStarted",
                0d,
                actorUserId,
                queueView?.CurrentHolderUserId ?? "None",
                true,
                "Closed-loop ownership phase started.");

            ResolveRuntime();
            bool actorAlreadyQueuedOrHolding = IsUserQueuedOrHolding(actorUserId);
            if (isLocalActor && !actorAlreadyQueuedOrHolding)
            {
                recorder?.RecordEvent(
                    cycle,
                    "Ownership",
                    role,
                    actorUserId,
                    "RequestAccessForLocalPlayer",
                    0d,
                    actorUserId,
                    queueView?.CurrentHolderUserId ?? "None",
                    true,
                    "Local actor requested access.");

                queueView?.RequestAccessForLocalPlayer();
            }

            bool grantObserved = false;
            yield return WaitUntilOrTimeout(
                cycle,
                () =>
                {
                    ResolveRuntime();
                    grantObserved = string.Equals(queueView?.CurrentHolderUserId, actorUserId, StringComparison.OrdinalIgnoreCase);
                    return grantObserved;
                },
                projectConfig.automatedScenarioStepTimeoutSeconds,
                "HolderGranted",
                role,
                actorUserId);

            if (!grantObserved)
                yield break;

            BeginObservedPhase(cycle, role, actorUserId);

            if (projectConfig.automatedScenarioPreQueueNextUser && hasNextActor)
            {
                if (isLocalNextActor && !IsUserQueuedOrHolding(nextActorUserId))
                {
                    recorder?.RecordEvent(
                        cycle,
                        "Ownership",
                        nextQueuedRole,
                        nextActorUserId,
                        "PreQueueNextUser",
                        0d,
                        actorUserId,
                        queueView?.CurrentHolderUserId ?? "None",
                        true,
                        $"Next actor queued while {role} holds the instrument.");

                    queueView?.RequestAccessForLocalPlayer();
                }

                yield return WaitUntilOrTimeout(
                    cycle,
                    () =>
                    {
                        ResolveRuntime();
                        return IsUserQueuedOrHolding(nextActorUserId);
                    },
                    projectConfig.automatedScenarioStepTimeoutSeconds,
                    "NextUserQueued",
                    nextQueuedRole,
                    nextActorUserId,
                    () => BuildQueueDebugValue());
            }

            if (!isLocalActor && accessProbesEnabled)
            {
                // Probe after the actor's manipulation window so the probe is not hidden by it.
                float probeDelay = Mathf.Min(
                    projectConfig.automatedScenarioHoldSeconds * 0.85f,
                    projectConfig.automatedScenarioManipulationStartDelaySeconds +
                    projectConfig.automatedScenarioManipulationDurationSeconds + 0.5f);
                StartCoroutine(ApplyAccessProbeAfter(probeDelay, cycle, "NonHolderDuringOtherHold", actorUserId));
                StartCoroutine(ApplyProbeWhenLocalBecomesHolder(cycle));
            }

            if (isLocalActor)
                yield return HoldAndManipulateDuringPhase(cycle, role, actorUserId);
            else if (projectConfig.automatedScenarioHoldSeconds > 0f)
                yield return WaitWhileHolder(role, actorUserId, projectConfig.automatedScenarioHoldSeconds + projectConfig.automatedScenarioStepTimeoutSeconds);

            EndObservedPhase();

            if (isLocalActor)
            {
                if (accessProbesEnabled)
                    StartCoroutine(ApplyFormerHolderProbeAfterRelease(cycle, actorUserId));

                recorder?.RecordEvent(
                    cycle,
                    "Ownership",
                    role,
                    actorUserId,
                    "ReleaseAccessForLocalPlayer",
                    projectConfig.automatedScenarioHoldSeconds * 1000d,
                    "None",
                    queueView?.CurrentHolderUserId ?? "None",
                    true,
                    "Local actor released access after hold interval.");

                queueView?.ReleaseAccessForLocalPlayer();
            }

            if (projectConfig.automatedScenarioPreQueueNextUser && hasNextActor)
            {
                yield return WaitUntilOrTimeout(
                    cycle,
                    () =>
                    {
                        ResolveRuntime();
                        return string.Equals(queueView?.CurrentHolderUserId, nextActorUserId, StringComparison.OrdinalIgnoreCase);
                    },
                    projectConfig.automatedScenarioStepTimeoutSeconds,
                    "HolderTransferredToNext",
                    nextQueuedRole,
                    nextActorUserId);
            }
            else
            {
                yield return WaitUntilOrTimeout(
                    cycle,
                    () =>
                    {
                        ResolveRuntime();
                        return !string.Equals(queueView?.CurrentHolderUserId, actorUserId, StringComparison.OrdinalIgnoreCase);
                    },
                    projectConfig.automatedScenarioStepTimeoutSeconds,
                    "HolderReleased",
                    role,
                    "Not:" + actorUserId);
            }
        }

        private IEnumerator RunOwnershipPingPongOpenLoop()
        {
            float phaseDuration = Mathf.Max(0.25f, projectConfig.automatedScenarioHoldSeconds);

            for (int cycle = 1; cycle <= projectConfig.automatedScenarioRepeatCount; cycle++)
            {
                SetBenchmarkUiStatus(
                    $"BENCHMARK RUNNING - open-loop cycle {cycle}/{projectConfig.automatedScenarioRepeatCount}, User1 phase.");
                yield return RunOpenLoopOwnershipPhase(cycle, "User1", phaseDuration);
                SetBenchmarkUiStatus(
                    $"BENCHMARK RUNNING - open-loop cycle {cycle}/{projectConfig.automatedScenarioRepeatCount}, User2 phase.");
                yield return RunOpenLoopOwnershipPhase(cycle, "User2", phaseDuration);
                recorder?.MarkCycleCompleted(cycle);

                if (projectConfig.automatedScenarioCooldownSeconds > 0f)
                    yield return new WaitForSeconds(projectConfig.automatedScenarioCooldownSeconds);
            }
        }

        private IEnumerator RunSimultaneousContentionClosedLoop()
        {
            if (!TryResolveRole("User1", out string user1Id) ||
                !TryResolveRole("User2", out string user2Id))
            {
                RecordScenarioFailure(0, "All", "RoleResolveFailed", "Contention scenario requires User1 and User2.");
                yield break;
            }

            for (int cycle = 1; cycle <= projectConfig.automatedScenarioRepeatCount; cycle++)
            {
                SetBenchmarkUiStatus(
                    $"BENCHMARK RUNNING - contention cycle {cycle}/{projectConfig.automatedScenarioRepeatCount}.");
                bool cycleStartStateValid = false;
                yield return WaitUntilOrTimeout(
                    cycle,
                    () =>
                    {
                        ResolveRuntime();
                        cycleStartStateValid = IsContentionCycleStartStateValid(user1Id, user2Id);
                        return cycleStartStateValid;
                    },
                    projectConfig.automatedScenarioStepTimeoutSeconds,
                    "ContentionCycleStartStateValid",
                    "All",
                    "OnlyScenarioParticipants",
                    BuildQueueDebugValue);

                if (!cycleStartStateValid)
                    yield break;

                string localUserId = sessionView?.LocalUserId ?? "";
                bool localParticipant = IsLocalUser(user1Id) || IsLocalUser(user2Id);
                bool localRequestAlreadyPresent = IsUserQueuedOrHolding(localUserId);
                recorder?.RecordEvent(
                    cycle,
                    "Contention",
                    localRole,
                    localUserId,
                    "SimultaneousRequestBarrierReleased",
                    0d,
                    "User1||User2",
                    queueView?.CurrentHolderUserId ?? "None",
                    localParticipant,
                    "Both participants request access at the start of the contention cycle.");

                // Peer coroutines may reach this point a few frames apart. A holder here is the
                // first contention request in flight, not a reason for the second peer to wait.
                if (localParticipant && !localRequestAlreadyPresent)
                    queueView?.RequestAccessForLocalPlayer();

                bool contentionCaptured = false;
                yield return WaitUntilOrTimeout(
                    cycle,
                    () =>
                    {
                        ResolveRuntime();
                        string holder = queueView?.CurrentHolderUserId;
                        bool user1Holds = string.Equals(holder, user1Id, StringComparison.OrdinalIgnoreCase);
                        bool user2Holds = string.Equals(holder, user2Id, StringComparison.OrdinalIgnoreCase);
                        bool otherUserQueued = user1Holds
                            ? IsUserQueuedOrHolding(user2Id)
                            : user2Holds && IsUserQueuedOrHolding(user1Id);
                        contentionCaptured = (user1Holds || user2Holds) && otherUserQueued;
                        return contentionCaptured;
                    },
                    projectConfig.automatedScenarioStepTimeoutSeconds,
                    "SimultaneousContentionCaptured",
                    "All",
                    "OneHolder; OtherQueued",
                    BuildQueueDebugValue);

                if (!contentionCaptured)
                    yield break;

                string firstHolderId = queueView.CurrentHolderUserId;
                string secondHolderId = string.Equals(firstHolderId, user1Id, StringComparison.OrdinalIgnoreCase)
                    ? user2Id
                    : user1Id;
                string firstRole = string.Equals(firstHolderId, user1Id, StringComparison.OrdinalIgnoreCase)
                    ? "User1"
                    : "User2";
                string secondRole = firstRole == "User1" ? "User2" : "User1";

                yield return RunContentionHolderPhase(
                    cycle,
                    firstRole,
                    firstHolderId,
                    secondRole,
                    secondHolderId);
                yield return RunContentionHolderPhase(
                    cycle,
                    secondRole,
                    secondHolderId,
                    null,
                    null);

                recorder?.MarkCycleCompleted(cycle);
                if (projectConfig.automatedScenarioCooldownSeconds > 0f)
                    yield return new WaitForSeconds(projectConfig.automatedScenarioCooldownSeconds);
            }
        }

        private IEnumerator RunContentionHolderPhase(
            int cycle,
            string role,
            string actorUserId,
            string nextRole,
            string nextActorUserId)
        {
            bool holderObserved = false;
            yield return WaitUntilOrTimeout(
                cycle,
                () =>
                {
                    ResolveRuntime();
                    holderObserved = string.Equals(
                        queueView?.CurrentHolderUserId,
                        actorUserId,
                        StringComparison.OrdinalIgnoreCase);
                    return holderObserved;
                },
                projectConfig.automatedScenarioStepTimeoutSeconds,
                "ContentionHolderObserved",
                role,
                actorUserId);

            if (!holderObserved)
                yield break;

            BeginObservedPhase(cycle, role, actorUserId);

            recorder?.RecordEvent(
                cycle,
                "Contention",
                role,
                actorUserId,
                "ContentionHolderPhaseStarted",
                0d,
                actorUserId,
                queueView?.CurrentHolderUserId ?? "None",
                true,
                string.IsNullOrWhiteSpace(nextActorUserId)
                    ? "Final contention holder."
                    : $"Next expected holder is {nextRole} ({nextActorUserId}).");

            if (IsLocalUser(actorUserId))
                yield return HoldAndManipulateDuringPhase(cycle, role, actorUserId);
            else
                yield return WaitWhileHolder(
                    role,
                    actorUserId,
                    projectConfig.automatedScenarioHoldSeconds + projectConfig.automatedScenarioStepTimeoutSeconds);

            EndObservedPhase();

            if (IsLocalUser(actorUserId))
            {
                recorder?.RecordEvent(
                    cycle,
                    "Contention",
                    role,
                    actorUserId,
                    "ReleaseAfterContentionHold",
                    projectConfig.automatedScenarioHoldSeconds * 1000d,
                    nextActorUserId ?? "None",
                    queueView?.CurrentHolderUserId ?? "None",
                    true,
                    "Contention holder released after the configured hold interval.");
                queueView?.ReleaseAccessForLocalPlayer();
            }

            bool transitionObserved = false;
            yield return WaitUntilOrTimeout(
                cycle,
                () =>
                {
                    ResolveRuntime();
                    transitionObserved = string.IsNullOrWhiteSpace(nextActorUserId)
                        ? !string.Equals(queueView?.CurrentHolderUserId, actorUserId, StringComparison.OrdinalIgnoreCase)
                        : string.Equals(queueView?.CurrentHolderUserId, nextActorUserId, StringComparison.OrdinalIgnoreCase);
                    return transitionObserved;
                },
                projectConfig.automatedScenarioStepTimeoutSeconds,
                string.IsNullOrWhiteSpace(nextActorUserId)
                    ? "ContentionFinalHolderReleased"
                    : "ContentionTransferredToQueuedUser",
                string.IsNullOrWhiteSpace(nextRole) ? role : nextRole,
                nextActorUserId ?? "Not:" + actorUserId,
                BuildQueueDebugValue);
        }

        private IEnumerator RunOpenLoopOwnershipPhase(int cycle, string role, float phaseDuration)
        {
            if (!TryResolveRole(role, out string actorUserId))
            {
                RecordScenarioFailure(cycle, role, "RoleResolveFailed", $"Could not resolve {role}.");
                yield break;
            }

            bool isLocalActor = IsLocalUser(actorUserId);

            recorder?.RecordEvent(
                cycle,
                "OwnershipOpenLoop",
                role,
                actorUserId,
                "PhaseStarted",
                0d,
                actorUserId,
                queueView?.CurrentHolderUserId ?? "None",
                true,
                "Open-loop ownership phase started.");

            if (isLocalActor)
            {
                recorder?.RecordEvent(
                    cycle,
                    "OwnershipOpenLoop",
                    role,
                    actorUserId,
                    "RequestAccessForLocalPlayer",
                    0d,
                    actorUserId,
                    queueView?.CurrentHolderUserId ?? "None",
                    true,
                    "Open-loop local actor requested access.");

                queueView?.RequestAccessForLocalPlayer();
            }

            yield return new WaitForSeconds(phaseDuration);

            if (isLocalActor)
            {
                recorder?.RecordEvent(
                    cycle,
                    "OwnershipOpenLoop",
                    role,
                    actorUserId,
                    "ReleaseAccessForLocalPlayer",
                    phaseDuration * 1000d,
                    "None",
                    queueView?.CurrentHolderUserId ?? "None",
                    true,
                    "Open-loop local actor released access.");

                queueView?.ReleaseAccessForLocalPlayer();
            }

        }

        private IEnumerator WaitForParticipants(int expectedParticipants)
        {
            while (true)
            {
                ResolveRuntime();
                int count = sessionView?.CurrentPlayers?.Count ?? 0;
                recorder?.MarkObservedParticipants(count);

                if (sessionView != null && sessionView.IsInRoom && count >= expectedParticipants && ResolveLocalRole())
                    yield break;

                yield return new WaitForSeconds(0.25f);
            }
        }

        private IEnumerator WaitUntilOrTimeout(
            int cycle,
            Func<bool> predicate,
            float timeoutSeconds,
            string assertionId,
            string role,
            string expectedValue,
            Func<string> actualValueFactory = null)
        {
            float start = Time.realtimeSinceStartup;
            bool passed = false;

            while (Time.realtimeSinceStartup - start <= timeoutSeconds)
            {
                if (predicate != null && predicate())
                {
                    passed = true;
                    break;
                }

                yield return null;
            }

            string actual = actualValueFactory != null
                ? actualValueFactory()
                : queueView?.CurrentHolderUserId ?? "None";
            recorder?.RecordAssertion(
                cycle,
                assertionId,
                role,
                expectedValue,
                actual,
                passed,
                timeoutSeconds * 1000d);
        }

        private IEnumerator WaitWhileHolder(string role, string actorUserId, float timeoutSeconds)
        {
            float start = Time.realtimeSinceStartup;

            while (Time.realtimeSinceStartup - start <= timeoutSeconds)
            {
                ResolveRuntime();
                if (!string.Equals(queueView?.CurrentHolderUserId, actorUserId, StringComparison.OrdinalIgnoreCase))
                    yield break;

                yield return null;
            }

            recorder?.RecordAssertion(
                0,
                "RemoteHolderReleaseObserved",
                role,
                "Not:" + actorUserId,
                queueView?.CurrentHolderUserId ?? "None",
                false,
                timeoutSeconds * 1000d,
                "Remote holder did not release inside expected interval.");
        }

        private IEnumerator HoldAndManipulateDuringPhase(int cycle, string role, string actorUserId)
        {
            float holdSeconds = Mathf.Max(0f, projectConfig.automatedScenarioHoldSeconds);
            if (holdSeconds <= 0f)
                yield break;

            if (!projectConfig.automatedScenarioManipulateChildObject)
            {
                yield return new WaitForSeconds(holdSeconds);
                yield break;
            }

            Transform target = benchmarkManipulationTarget != null
                ? benchmarkManipulationTarget
                : ResolveManipulationTarget();
            if (target == null)
            {
                recorder?.RecordEvent(
                    cycle,
                    "Manipulation",
                    role,
                    actorUserId,
                    "ManipulationTargetMissing",
                    0d,
                    actorUserId,
                    queueView?.CurrentHolderUserId ?? "None",
                    false,
                    $"Could not find child '{projectConfig.automatedScenarioManipulatedChildName}'. Holding without manipulation.");

                yield return new WaitForSeconds(holdSeconds);
                yield break;
            }

            float startDelay = Mathf.Clamp(projectConfig.automatedScenarioManipulationStartDelaySeconds, 0f, holdSeconds);
            float availableDuration = Mathf.Max(0.01f, holdSeconds - startDelay);
            float manipulationDuration = Mathf.Clamp(projectConfig.automatedScenarioManipulationDurationSeconds, 0.01f, availableDuration);
            Quaternion startRotation = target.localRotation;
            Quaternion endRotation = startRotation * Quaternion.Euler(projectConfig.automatedScenarioManipulationEulerDelta);
            bool manipulationStarted = false;
            bool manipulationCompleted = false;
            bool holderMaintained = true;
            float startTime = Time.realtimeSinceStartup;

            recorder?.RecordEvent(
                cycle,
                "Manipulation",
                role,
                actorUserId,
                "ManipulationScheduled",
                startDelay * 1000d,
                actorUserId,
                queueView?.CurrentHolderUserId ?? "None",
                true,
                $"Target={BuildTransformPath(target)}, DeltaEuler={projectConfig.automatedScenarioManipulationEulerDelta}");

            while (Time.realtimeSinceStartup - startTime < holdSeconds)
            {
                ResolveRuntime();
                float elapsed = Time.realtimeSinceStartup - startTime;

                if (!string.Equals(queueView?.CurrentHolderUserId, actorUserId, StringComparison.OrdinalIgnoreCase))
                {
                    holderMaintained = false;
                    break;
                }

                if (elapsed >= startDelay)
                {
                    if (!manipulationStarted)
                    {
                        manipulationStarted = true;
                        TelemetryLoggerCore.Instance?.RegisterObjectInteraction(actorUserId, BuildTrackedObjectId(target));
                        recorder?.RecordEvent(
                            cycle,
                            "Manipulation",
                            role,
                            actorUserId,
                            "ManipulationStarted",
                            elapsed * 1000d,
                            actorUserId,
                            queueView?.CurrentHolderUserId ?? "None",
                            true,
                            $"Target={BuildTransformPath(target)}");
                    }

                    float t = Mathf.Clamp01((elapsed - startDelay) / manipulationDuration);
                    target.localRotation = Quaternion.Slerp(startRotation, endRotation, t);

                    if (!manipulationCompleted && t >= 1f)
                    {
                        manipulationCompleted = true;
                        TelemetryLoggerCore.Instance?.RegisterObjectReposition(actorUserId, BuildTrackedObjectId(target));
                        recorder?.RecordEvent(
                            cycle,
                            "Manipulation",
                            role,
                            actorUserId,
                            "ManipulationCompleted",
                            elapsed * 1000d,
                            actorUserId,
                            queueView?.CurrentHolderUserId ?? "None",
                            true,
                            $"Target={BuildTransformPath(target)}");
                    }
                }

                yield return null;
            }

            if (manipulationStarted && holderMaintained)
            {
                target.localRotation = endRotation;
                TelemetryLoggerCore.Instance?.RegisterManipulatedObject(actorUserId, BuildTrackedObjectId(target));
            }

            recorder?.RecordAssertion(
                cycle,
                "HolderRetainedDuringManipulation",
                role,
                actorUserId,
                queueView?.CurrentHolderUserId ?? "None",
                holderMaintained,
                holdSeconds * 1000d,
                $"Target={BuildTransformPath(target)}");
        }

        private void PrepareMeasuredWindow()
        {
            benchmarkManipulationTarget = null;
            benchmarkRotationCaptured = false;

            if (projectConfig.automatedScenarioManipulateChildObject)
            {
                benchmarkManipulationTarget = ResolveManipulationTarget();
                bool targetFound = benchmarkManipulationTarget != null;
                recorder?.RecordAssertion(
                    0,
                    "ManipulationTargetResolved",
                    "All",
                    projectConfig.automatedScenarioManipulatedChildName,
                    targetFound ? BuildTransformPath(benchmarkManipulationTarget) : "Missing",
                    targetFound,
                    0d,
                    "Benchmark manipulation target preflight.");

                if (targetFound)
                {
                    benchmarkInitialLocalRotation = benchmarkManipulationTarget.localRotation;
                    benchmarkExpectedFinalLocalRotation = benchmarkInitialLocalRotation;
                    int phaseCount = Mathf.Max(0, projectConfig.automatedScenarioRepeatCount * 2);
                    Quaternion phaseDelta = Quaternion.Euler(projectConfig.automatedScenarioManipulationEulerDelta);
                    for (int i = 0; i < phaseCount; i++)
                        benchmarkExpectedFinalLocalRotation *= phaseDelta;

                    benchmarkRotationCaptured = true;
                    observedPhaseIndex = 0;
                    observedPhaseCycle = 0;
                    observedPhaseRole = "";
                    observedPhaseActor = "";
                    if (transformSamplerCoroutine != null)
                        StopCoroutine(transformSamplerCoroutine);
                    transformSamplerCoroutine = StartCoroutine(SampleTransformDuringMeasuredWindow());
                    accessProbesEnabled = projectConfig.AccessControlProbesEnabled;
                    accessProbeSequence = 0;
                }
            }

            if (projectConfig.automatedScenarioResetPerformanceSamplesAfterWarmup)
                RuntimePerformanceSampler.Instance?.RestartSamplingForMeasuredWindow();

            recorder?.RecordEvent(
                0,
                "Scenario",
                "All",
                sessionView?.LocalUserId ?? "",
                "MeasurementWindowStarted",
                0d,
                "",
                queueView?.CurrentHolderUserId ?? "None",
                true,
                $"WarmupSeconds={projectConfig.automatedScenarioWarmupSeconds:0.###}; Target={projectConfig.automatedScenarioManipulatedChildName}");

            SetBenchmarkUiStatus("BENCHMARK RUNNING - do not stop the session.");

            SuppressInfoLogs();
        }

        private IEnumerator ValidateFinalManipulationState()
        {
            float settleSeconds = Mathf.Max(0f, projectConfig.automatedScenarioFinalRotationSettleSeconds);
            if (settleSeconds > 0f)
                yield return new WaitForSeconds(settleSeconds);

            ResolveRuntime();
            IReadOnlyList<string> queue = queueView?.CurrentQueueSnapshot;
            bool queueIdle = string.IsNullOrWhiteSpace(queueView?.CurrentHolderUserId) ||
                             string.Equals(queueView.CurrentHolderUserId, "None", StringComparison.OrdinalIgnoreCase);
            queueIdle &= queue == null || queue.Count == 0;

            recorder?.RecordAssertion(
                projectConfig.automatedScenarioRepeatCount,
                "FinalQueueIdle",
                "All",
                "Holder=None; Queue=Empty",
                BuildQueueDebugValue(),
                queueIdle,
                settleSeconds * 1000d,
                "Final queue state before export.");

            if (projectConfig.automatedScenarioMode == OrchestrationScenarioExecutionMode.ClosedLoop)
            {
                // The first logical grant may already match backend authority. Only transitions
                // between completed cycles are guaranteed to require a physical handoff.
                int expectedTransfers = GetMinimumExpectedPhysicalAuthorityTransfers(
                    projectConfig.automatedScenarioRepeatCount);
                int observedTransfers = AuthorityTransferRecorder.Instance != null
                    ? AuthorityTransferRecorder.Instance.Rows.Count(row => row != null && row.transferSuccess)
                    : 0;

                recorder?.RecordAssertion(
                    projectConfig.automatedScenarioRepeatCount,
                    "AuthorityTransferTelemetryComplete",
                    "All",
                    $"Transfers>={expectedTransfers}",
                    $"Transfers={observedTransfers}",
                    observedTransfers >= expectedTransfers,
                    settleSeconds * 1000d,
                    "Closed-loop runs require each inter-cycle successful physical authority handoff. The initial grant may already match backend authority; phase assertions independently validate every logical holder transition.");
            }

            if (!projectConfig.automatedScenarioManipulateChildObject ||
                !projectConfig.automatedScenarioValidateFinalRotation)
                yield break;

            Transform target = benchmarkManipulationTarget != null
                ? benchmarkManipulationTarget
                : ResolveManipulationTarget();
            float tolerance = Mathf.Max(0.01f, projectConfig.automatedScenarioRotationToleranceDegrees);
            float angleError = target != null && benchmarkRotationCaptured
                ? Quaternion.Angle(target.localRotation, benchmarkExpectedFinalLocalRotation)
                : float.PositiveInfinity;
            bool converged = target != null && benchmarkRotationCaptured && angleError <= tolerance;
            recorder?.RecordFinalRotationOutcome(angleError, converged);
            RecordTransformObservation("Final", benchmarkExpectedFinalLocalRotation);

            recorder?.RecordAssertion(
                projectConfig.automatedScenarioRepeatCount,
                "FinalManipulationRotationConverged",
                "All",
                $"AngleError<={tolerance:0.###}deg",
                float.IsInfinity(angleError) ? "TargetMissing" : $"AngleError={angleError:0.###}deg",
                converged,
                settleSeconds * 1000d,
                target != null
                    ? $"Target={BuildTransformPath(target)}; Initial={benchmarkInitialLocalRotation.eulerAngles}; Expected={benchmarkExpectedFinalLocalRotation.eulerAngles}; Actual={target.localRotation.eulerAngles}"
                    : $"Target '{projectConfig.automatedScenarioManipulatedChildName}' was not found.",
                requiredForValidity: false);
        }

        // ---- Per-phase transform observations (both participants; R2-C09) ----
        // Phase k (1-based) is expected to end at Initial * Delta^k. Boundary rows do not wait or
        // change protocol timing; PhaseEnd is taken at the end of the hold interval, before release.

        private Quaternion ExpectedRotationAfterPhases(int phases)
        {
            Quaternion expected = benchmarkInitialLocalRotation;
            Quaternion delta = Quaternion.Euler(projectConfig.automatedScenarioManipulationEulerDelta);
            for (int i = 0; i < phases; i++)
                expected *= delta;
            return expected;
        }

        private void BeginObservedPhase(int cycle, string role, string actorUserId)
        {
            observedPhaseIndex++;
            observedPhaseCycle = cycle;
            observedPhaseRole = role ?? "";
            observedPhaseActor = actorUserId ?? "";
            RecordTransformObservation("PhaseStart", ExpectedRotationAfterPhases(observedPhaseIndex - 1));
        }

        private void EndObservedPhase()
        {
            RecordTransformObservation("PhaseEnd", ExpectedRotationAfterPhases(observedPhaseIndex));
        }

        private void RecordTransformObservation(string kind, Quaternion expected)
        {
            Transform target = benchmarkManipulationTarget;
            if (!benchmarkRotationCaptured || target == null || TransformObservationRecorder.Instance == null)
                return;

            ResolveRuntime();
            TransformObservationRecorder.Instance.Record(
                kind,
                observedPhaseCycle,
                observedPhaseIndex,
                observedPhaseRole,
                observedPhaseActor,
                sessionView?.LocalUserId ?? "",
                queueView?.CurrentHolderUserId ?? "None",
                BuildTransformPath(target),
                expected,
                target.localRotation);
        }

        private IEnumerator SampleTransformDuringMeasuredWindow()
        {
            var wait = new WaitForSecondsRealtime(TransformSampleIntervalSeconds);
            while (benchmarkRotationCaptured && benchmarkManipulationTarget != null && !scenarioCompleted)
            {
                // Expected value of a sample is the end state of the current phase; samples taken while
                // the actor is still rotating are labeled with that phase and interpreted in analysis.
                RecordTransformObservation("Sample", ExpectedRotationAfterPhases(observedPhaseIndex));
                yield return wait;
            }

            transformSamplerCoroutine = null;
        }

        // ---- W03 access-control probes (R2-C07) ----
        // A probe bypasses the interaction gate and writes a pitch rotation directly to the local
        // replica of the controlled object, at defined access states. Whether the write is overwritten
        // locally (ProbeCheck on the prober) and whether it reaches the other participant (its
        // transform samples) shows what the network layer, not only the gate, enforces. Legitimate
        // manipulation only changes yaw, so a pitch near the probe angle identifies a probe.

        private void ApplyAccessProbe(int cycle, string condition)
        {
            Transform target = benchmarkManipulationTarget;
            if (!accessProbesEnabled || target == null)
                return;

            ResolveRuntime();
            string localUserId = sessionView?.LocalUserId ?? "";
            string holder = queueView?.CurrentHolderUserId ?? "None";
            string instrumentId = queueView?.CurrentInstrumentId ?? "";
            string nativeState = "unobserved";
            if (NativeAuthorityObservationRecorder.Instance != null &&
                NativeAuthorityObservationRecorder.Instance.TryGetLatest(instrumentId, out NativeAuthorityObservationRecorder.ObservationRow latest))
            {
                nativeState = $"localNativeAuthority={latest.localNativeAuthority} nativeOwner={latest.nativeOwner}";
            }

            BaseInteractionGate gate = target.GetComponentInParent<BaseInteractionGate>();
            string gateState = gate != null ? gate.InteractionPermitted.ToString() : "NoGate";
            Quaternion beforeProbe = target.localRotation;
            int probeId = ++accessProbeSequence;

            // Unique signature per probe: User1 rotates about local X (pitch), others about local Z (roll),
            // by 12 + 2*probeId degrees. With Unity's ZXY Euler order, yaw*Rx(p) has Euler x == p and
            // yaw*Rz(p) has Euler z == p, so the other replica can be checked for this exact value.
            bool pitchAxis = string.Equals(localRole, "User1", StringComparison.OrdinalIgnoreCase);
            float probeAngle = AccessProbePitchDegrees * 0.6f + 2f * probeId;
            Quaternion probeRotation = pitchAxis
                ? Quaternion.Euler(probeAngle, 0f, 0f)
                : Quaternion.Euler(0f, 0f, probeAngle);
            target.localRotation = beforeProbe * probeRotation;

            recorder?.RecordEvent(
                cycle,
                "AccessProbe",
                localRole,
                localUserId,
                "AccessProbeApplied",
                0d,
                "None",
                holder,
                true,
                $"probe={probeId} condition={condition} axis={(pitchAxis ? "x" : "z")} " +
                $"angleDeg={probeAngle.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture)} holder={holder} " +
                $"localIsHolder={string.Equals(holder, localUserId, StringComparison.OrdinalIgnoreCase)} " +
                $"{nativeState} gatePermitted={gateState}");
            RecordTransformObservation($"Probe{probeId}:{condition}", beforeProbe);
            StartCoroutine(CheckAccessProbe(cycle, probeId, condition, beforeProbe, probeRotation, pitchAxis));
        }

        // After the check delay: records whether the probe remained on the local replica, then removes it
        // locally if it did, so later phases start from the protocol state on this replica. A probe that
        // already reached other replicas through native authority is not undone; that is the finding.
        private IEnumerator CheckAccessProbe(int cycle, int probeId, string condition, Quaternion beforeProbe,
            Quaternion probeRotation, bool pitchAxis)
        {
            yield return new WaitForSecondsRealtime(AccessProbeCheckDelaySeconds);
            Transform target = benchmarkManipulationTarget;
            if (target == null)
                yield break;

            Vector3 residualEuler = (Quaternion.Inverse(beforeProbe) * target.localRotation).eulerAngles;
            float pitchResidual = Mathf.Abs(Mathf.DeltaAngle(0f, pitchAxis ? residualEuler.x : residualEuler.z));
            RecordTransformObservation($"ProbeCheck{probeId}:{condition}", beforeProbe);
            bool cleanedUp = false;
            if (pitchResidual > AccessProbePitchDegrees * 0.25f)
            {
                target.localRotation = target.localRotation * Quaternion.Inverse(probeRotation);
                cleanedUp = true;
            }
            recorder?.RecordEvent(
                cycle,
                "AccessProbe",
                localRole,
                sessionView?.LocalUserId ?? "",
                "AccessProbeChecked",
                AccessProbeCheckDelaySeconds * 1000d,
                "None",
                queueView?.CurrentHolderUserId ?? "None",
                true,
                $"probe={probeId} condition={condition} localResidualDeg={pitchResidual:0.###} " +
                $"probePersistedLocally={pitchResidual > AccessProbePitchDegrees * 0.25f} localCleanupApplied={cleanedUp}");
        }

        // Runs alongside the release: probes on the first frame where the local participant is no longer
        // the logical holder, the window in which it may still have native authority.
        private IEnumerator ApplyFormerHolderProbeAfterRelease(int cycle, string formerHolderUserId)
        {
            float start = Time.realtimeSinceStartup;
            while (Time.realtimeSinceStartup - start < projectConfig.automatedScenarioStepTimeoutSeconds)
            {
                ResolveRuntime();
                if (!string.Equals(queueView?.CurrentHolderUserId, formerHolderUserId, StringComparison.OrdinalIgnoreCase))
                {
                    ApplyAccessProbe(cycle, "FormerHolderAfterRelease");
                    yield break;
                }

                yield return null;
            }
        }

        // Probes on the first frame where the local participant observes itself as the logical holder,
        // before native authority can have arrived.
        private IEnumerator ApplyProbeWhenLocalBecomesHolder(int cycle)
        {
            float limit = projectConfig.automatedScenarioHoldSeconds + projectConfig.automatedScenarioStepTimeoutSeconds;
            float start = Time.realtimeSinceStartup;
            while (Time.realtimeSinceStartup - start < limit)
            {
                ResolveRuntime();
                if (IsLocalUser(queueView?.CurrentHolderUserId))
                {
                    ApplyAccessProbe(cycle, "LogicalHolderBeforeNativeAuthority");
                    yield break;
                }

                yield return null;
            }
        }

        private IEnumerator ApplyAccessProbeAfter(float delaySeconds, int cycle, string condition, string expectedHolder)
        {
            yield return new WaitForSecondsRealtime(delaySeconds);
            ResolveRuntime();
            // Only probe if the other participant still holds; otherwise the condition no longer applies.
            if (string.Equals(queueView?.CurrentHolderUserId, expectedHolder, StringComparison.OrdinalIgnoreCase))
                ApplyAccessProbe(cycle, condition);
        }

        public static int GetMinimumExpectedPhysicalAuthorityTransfers(int completedCycles)
        {
            return Mathf.Max(0, completedCycles - 1);
        }

        private void SuppressInfoLogs()
        {
            if (infoLogsSuppressed || !projectConfig.automatedScenarioSuppressInfoLogsDuringMeasurement)
                return;

            previousLogFilter = Debug.unityLogger.filterLogType;
            Debug.unityLogger.filterLogType = LogType.Warning;
            infoLogsSuppressed = true;
        }

        private void RestoreInfoLogs()
        {
            if (!infoLogsSuppressed)
                return;

            Debug.unityLogger.filterLogType = previousLogFilter;
            infoLogsSuppressed = false;
        }

        private IEnumerator ExportWhenReady()
        {
            float timeout = Mathf.Max(2f, projectConfig.automatedScenarioStepTimeoutSeconds);
            float start = Time.realtimeSinceStartup;

            while (Time.realtimeSinceStartup - start <= timeout)
            {
                ResolveRuntime();
                if (OrchestrationExportFinalizationUtility.IsReadyForExport(queueView))
                    break;

                yield return null;
            }

            string exportPath = EvaluationMetricsManager.ResolveOrCreate(this)?.ExportAll(null, sessionView?.LocalUserId);
            if (string.IsNullOrWhiteSpace(exportPath))
            {
                SetBenchmarkUiStatus("BENCHMARK EXPORT FAILED - do not use this run.");
                yield break;
            }

            ScenarioMetricsRecorder.ScenarioMetricsSummary summary = recorder?.BuildSummary();
            if (summary != null && !summary.scenarioValid)
            {
                SetBenchmarkUiStatus(
                    $"BENCHMARK COMPLETE - INVALID RUN EXPORTED - SAFE TO QUIT - DO NOT USE\n" +
                    $"Failed assertions: {summary.failedAssertionCount}; failed commands: {summary.failedCommandCount}; " +
                    $"cycles: {summary.completedCycles}/{summary.expectedCycles}\n{exportPath}");
                yield break;
            }

            if (summary != null && summary.failedOutcomeAssertionCount > 0)
            {
                SetBenchmarkUiStatus(
                    $"BENCHMARK COMPLETE - VALID EXPORT SAVED - SAFE TO QUIT\n" +
                    $"Measured quality threshold misses: {summary.failedOutcomeAssertionCount} " +
                    $"(retained for comparison, not a protocol failure)\n{exportPath}");
                yield break;
            }

            SetBenchmarkUiStatus($"BENCHMARK COMPLETE - VALID EXPORT SAVED - SAFE TO QUIT\n{exportPath}");
        }

        private void SetBenchmarkUiStatus(string message)
        {
            LastBenchmarkStatus = message ?? string.Empty;
            BenchmarkStatusChanged?.Invoke(LastBenchmarkStatus);

            TelemetryUI telemetryUi = FindFirstObjectByType<TelemetryUI>();
            telemetryUi?.SetBenchmarkStatus(LastBenchmarkStatus);
        }

        private bool ValidateScenarioConfiguration(out string details)
        {
            List<string> issues = new();

            if (projectConfig.automatedScenarioExpectedParticipants != 2)
                issues.Add("The implemented benchmark runners require exactly two participants.");

            if (projectConfig.automatedScenarioRepeatCount < 1)
                issues.Add("Repeat count must be at least one.");

            OrchestrationScenarioExecutionMode expectedMode =
                projectConfig.automatedScenario == OrchestrationAutomatedScenario.OpenLoopRequestStress2Users
                    ? OrchestrationScenarioExecutionMode.OpenLoop
                    : OrchestrationScenarioExecutionMode.ClosedLoop;
            if (projectConfig.automatedScenarioMode != expectedMode)
            {
                issues.Add(
                    $"{projectConfig.automatedScenario} requires {expectedMode} mode, " +
                    $"not {projectConfig.automatedScenarioMode}.");
            }

            if (projectConfig.automatedScenario == OrchestrationAutomatedScenario.OpenLoopRequestStress2Users)
            {
                if (projectConfig.automatedScenarioManipulateChildObject)
                    issues.Add("Open-loop request stress must not manipulate a child transform.");
            }
            else if (projectConfig.automatedScenarioManipulateChildObject)
            {
                if (string.IsNullOrWhiteSpace(projectConfig.automatedScenarioManipulatedChildName))
                    issues.Add("A manipulation target name is required.");

                float manipulationEnd =
                    projectConfig.automatedScenarioManipulationStartDelaySeconds +
                    projectConfig.automatedScenarioManipulationDurationSeconds;
                if (manipulationEnd > projectConfig.automatedScenarioHoldSeconds + 0.001f)
                {
                    issues.Add(
                        $"Manipulation ends at {manipulationEnd:0.###}s but the hold interval is only " +
                        $"{projectConfig.automatedScenarioHoldSeconds:0.###}s.");
                }
            }

            if (!TryValidateBackendReplicationReady(out string replicationIssue))
                issues.Add(replicationIssue);

            details = issues.Count == 0
                ? $"Runner={projectConfig.automatedScenario}; Mode={projectConfig.automatedScenarioMode}; Participants=2"
                : string.Join(" ", issues);
            return issues.Count == 0;
        }

        private bool TryValidateBackendReplicationReady(out string issue)
        {
            issue = string.Empty;

            SharedObjectSyncRegistry registry = SharedObjectSyncRegistry.ResolveOrCreate();
            registry?.DiscoverSceneObjects();
            SharedSyncObjectDefinition definition = registry?.GetPreferredQueueControlledObject(projectConfig.backendType);
            if (definition == null)
            {
                issue = $"{projectConfig.backendType} has no active queue-controlled shared object.";
                return false;
            }

            GameObject root = definition.RootObject != null ? definition.RootObject : definition.gameObject;
            switch (projectConfig.backendType)
            {
                case OrchestrationBackendType.PUN2:
                    PunPhotonView photonView = root.GetComponent<PunPhotonView>();
                    if (photonView != null && photonView.ViewID != 0)
                        return true;

                    issue = $"PUN2 shared object '{root.name}' has no allocated PhotonView ID.";
                    return false;

                case OrchestrationBackendType.Fusion:
                    FusionNetworkObject fusionObject = root.GetComponent<FusionNetworkObject>();
                    if (fusionObject != null && fusionObject.IsValid)
                        return true;

                    issue = $"Fusion shared object '{root.name}' is not attached to a running NetworkRunner.";
                    return false;

                case OrchestrationBackendType.UnityNetcode:
                    NetcodeNetworkObject netcodeObject = root.GetComponent<NetcodeNetworkObject>();
                    if (netcodeObject != null && netcodeObject.IsSpawned)
                        return true;

                    issue = $"Unity Netcode shared object '{root.name}' is not network-spawned.";
                    return false;

                case OrchestrationBackendType.FishNet:
                    FishNetNetworkObject fishNetObject = root.GetComponent<FishNetNetworkObject>();
                    if (fishNetObject != null && fishNetObject.IsSpawned)
                        return true;

                    issue = $"FishNet shared object '{root.name}' is not network-spawned.";
                    return false;

                case OrchestrationBackendType.Mirror:
                    MirrorNetworkIdentity mirrorIdentity = root.GetComponent<MirrorNetworkIdentity>();
                    if (mirrorIdentity != null && mirrorIdentity.netId != 0)
                        return true;

                    issue = $"Mirror shared object '{root.name}' is not network-spawned (netId=0).";
                    return false;

                default:
                    issue = $"Unsupported backend replication preflight: {projectConfig.backendType}.";
                    return false;
            }
        }

        private bool ResolveRuntime()
        {
            if (projectConfig == null && !ResolveConfig())
                return false;

            if (sessionView != null && queueView != null)
                return true;

            if (Time.realtimeSinceStartup < nextRuntimeResolveTime)
                return false;

            nextRuntimeResolveTime = Time.realtimeSinceStartup + 0.25f;
            backendRuntime = BackendRuntimeResolver.Resolve(projectConfig.backendType);
            sessionView = backendRuntime?.SessionView;
            queueView = backendRuntime?.QueueView;
            return sessionView != null && queueView != null;
        }

        private bool ResolveLocalRole()
        {
            List<OrchestrationPlayerInfo> players = GetOrderedParticipants();
            string localUserId = sessionView?.LocalUserId;

            if (string.IsNullOrWhiteSpace(localUserId))
                return false;

            for (int i = 0; i < players.Count; i++)
            {
                if (!string.Equals(players[i].UserId, localUserId, StringComparison.OrdinalIgnoreCase))
                    continue;

                localRole = $"User{i + 1}";
                recorder?.RecordEvent(
                    0,
                    "Participants",
                    localRole,
                    localUserId,
                    "RoleAssigned",
                    0d,
                    "",
                    queueView?.CurrentHolderUserId ?? "None",
                    true,
                    BuildParticipantDetails(players));

                Log($"Assigned local role {localRole} to {localUserId}.");
                SetBenchmarkUiStatus($"BENCHMARK READY - {localRole} assigned to {localUserId}.");
                return true;
            }

            localRole = "Observer";
            return true;
        }

        private bool TryResolveRole(string role, out string userId)
        {
            userId = "";
            List<OrchestrationPlayerInfo> players = GetOrderedParticipants();

            if (!role.StartsWith("User", StringComparison.OrdinalIgnoreCase))
                return false;

            if (!int.TryParse(role.Substring(4), out int roleNumber))
                return false;

            int index = roleNumber - 1;
            if (index < 0 || index >= players.Count)
                return false;

            userId = players[index].UserId;
            return !string.IsNullOrWhiteSpace(userId);
        }

        private List<OrchestrationPlayerInfo> GetOrderedParticipants()
        {
            IReadOnlyList<OrchestrationPlayerInfo> source = sessionView?.CurrentPlayers;
            if (source == null)
                return new List<OrchestrationPlayerInfo>();

            List<OrchestrationPlayerInfo> ordered = source
                .Where(p => p != null && !string.IsNullOrWhiteSpace(p.UserId))
                .OrderByDescending(p => p.IsMasterClient)
                .ThenBy(p => p.NumericId)
                .ThenBy(p => p.UserId, StringComparer.OrdinalIgnoreCase)
                .Take(Mathf.Max(2, projectConfig.automatedScenarioExpectedParticipants))
                .ToList();

            // -vlabRoleOrder swapped: the first user of the scenario is the non-master participant.
            if (OrchestrationRoleOrder.IsSwapped)
                ordered.Reverse();

            return ordered;
        }

        private bool IsLocalUser(string userId)
        {
            return !string.IsNullOrWhiteSpace(userId) &&
                   string.Equals(sessionView?.LocalUserId, userId, StringComparison.OrdinalIgnoreCase);
        }

        private bool IsUserQueuedOrHolding(string userId)
        {
            if (string.IsNullOrWhiteSpace(userId))
                return false;

            if (string.Equals(queueView?.CurrentHolderUserId, userId, StringComparison.OrdinalIgnoreCase))
                return true;

            IReadOnlyList<string> queue = queueView?.CurrentQueueSnapshot;
            return queue != null && queue.Any(queued => string.Equals(queued, userId, StringComparison.OrdinalIgnoreCase));
        }

        private bool IsContentionCycleStartStateValid(string user1Id, string user2Id)
        {
            string holder = queueView?.CurrentHolderUserId;
            if (!IsNoHolder(holder) &&
                !string.Equals(holder, user1Id, StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(holder, user2Id, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            IReadOnlyList<string> queue = queueView?.CurrentQueueSnapshot;
            if (queue == null)
                return true;

            return queue.All(userId =>
                string.Equals(userId, user1Id, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(userId, user2Id, StringComparison.OrdinalIgnoreCase));
        }

        private static bool IsNoHolder(string userId)
        {
            return string.IsNullOrWhiteSpace(userId) ||
                   string.Equals(userId, "None", StringComparison.OrdinalIgnoreCase);
        }

        private string BuildQueueDebugValue()
        {
            string holder = queueView?.CurrentHolderUserId ?? "None";
            IReadOnlyList<string> queue = queueView?.CurrentQueueSnapshot;
            string joinedQueue = queue != null && queue.Count > 0 ? string.Join("||", queue) : "Empty";
            return $"Holder={holder}; Queue={joinedQueue}";
        }

        private Transform ResolveManipulationTarget()
        {
            string targetName = projectConfig != null ? projectConfig.automatedScenarioManipulatedChildName : "";
            if (string.IsNullOrWhiteSpace(targetName))
                return null;

            SharedSyncObjectDefinition[] definitions = FindObjectsByType<SharedSyncObjectDefinition>(
                FindObjectsInactive.Exclude,
                FindObjectsSortMode.None);

            SharedSyncObjectDefinition preferredDefinition = null;
            if (!string.IsNullOrWhiteSpace(queueView?.CurrentInstrumentId))
            {
                preferredDefinition = definitions.FirstOrDefault(definition =>
                    definition != null &&
                    string.Equals(definition.ObjectId, queueView.CurrentInstrumentId, StringComparison.OrdinalIgnoreCase));
            }

            if (TryFindManipulationTarget(preferredDefinition, targetName, out Transform preferredTarget))
                return preferredTarget;

            foreach (SharedSyncObjectDefinition definition in definitions)
            {
                if (TryFindManipulationTarget(definition, targetName, out Transform target))
                    return target;
            }

            return null;
        }

        private bool TryFindManipulationTarget(
            SharedSyncObjectDefinition definition,
            string targetName,
            out Transform target)
        {
            target = null;
            if (definition == null || string.IsNullOrWhiteSpace(targetName))
                return false;

            definition.RefreshControlledChildren();

            IReadOnlyList<GameObject> children = definition.ControlledChildObjects;
            if (children != null)
            {
                foreach (GameObject child in children)
                {
                    if (child == null)
                        continue;

                    if (IsTargetNameMatch(child.name, targetName))
                    {
                        target = child.transform;
                        return true;
                    }
                }
            }

            Transform root = definition.RootObject != null ? definition.RootObject.transform : definition.transform;
            Transform[] transforms = root.GetComponentsInChildren<Transform>(true);
            foreach (Transform candidate in transforms)
            {
                if (candidate == null || candidate == root)
                    continue;

                if (!IsTargetNameMatch(candidate.name, targetName))
                    continue;

                target = candidate;
                return true;
            }

            return false;
        }

        private bool IsTargetNameMatch(string candidateName, string targetName)
        {
            return string.Equals(candidateName, targetName, StringComparison.OrdinalIgnoreCase) ||
                   (!string.IsNullOrWhiteSpace(candidateName) &&
                    candidateName.IndexOf(targetName, StringComparison.OrdinalIgnoreCase) >= 0);
        }

        private string BuildTrackedObjectId(Transform target)
        {
            if (target == null)
                return "UnknownManipulationTarget";

            SharedSyncObjectDefinition definition = target.GetComponentInParent<SharedSyncObjectDefinition>();
            if (definition != null && !string.IsNullOrWhiteSpace(definition.ObjectId))
                return $"{definition.ObjectId}/{target.name}";

            return target.name;
        }

        private string BuildTransformPath(Transform target)
        {
            if (target == null)
                return "None";

            Stack<string> segments = new Stack<string>();
            Transform current = target;
            while (current != null)
            {
                segments.Push(current.name);
                current = current.parent;
            }

            return string.Join("/", segments);
        }

        private void RecordScenarioFailure(int cycle, string role, string command, string details)
        {
            recorder?.RecordEvent(
                cycle,
                "Scenario",
                role,
                sessionView?.LocalUserId ?? "",
                command,
                0d,
                "",
                queueView?.CurrentHolderUserId ?? "None",
                false,
                details);
        }

        private bool ResolveConfig()
        {
            if (projectConfig != null)
                return true;

            projectConfig = Resources.Load<OrchestrationProjectConfig>("AIMultiplayerAssistant/OrchestrationProjectConfig");
            return projectConfig != null;
        }

        private string BuildParticipantDetails(List<OrchestrationPlayerInfo> players)
        {
            return string.Join(
                " | ",
                players.Select((p, i) => $"User{i + 1}={p.UserId}({p.DisplayName})"));
        }

        private void Log(string message)
        {
            if (!verboseLogs && (projectConfig == null || !projectConfig.verboseLogs))
                return;

            Debug.Log($"[OrchestrationScenarioRunner] {message}", this);
        }
    }
}
