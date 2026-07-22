# Implementation Evidence

## Audit Scope

The canonical publication framework is located at:

`repo/src/framework` and `repo/src/backend-adapters`

After the evaluator installs the audited SDK matrix, the package materializes an
identical runtime copy under
`repo/src/showcase/UnityProject/Assets/GeneratedSoftwareXRuntime`.

The directory contains 130 C# files and approximately 27,870 lines. The largest
areas are the five backend implementations, telemetry and evaluation, editor
tooling, shared synchronization, and runtime orchestration.

| Area | Files | Approximate lines |
|---|---:|---:|
| Backends | 65 | 12,514 |
| Telemetry | 15 | 4,568 |
| Editor | 9 | 3,404 |
| Shared synchronization | 13 | 2,474 |
| Runtime | 10 | 1,886 |
| Core | 4 | 1,067 |
| AI, configuration, policy, timing, UI, user sync, utilities | 14 | 1,957 |

Line counts describe implementation scale only. They are not used as a quality
or research-performance metric.

## Claim-to-Code Matrix

| Candidate paper claim | Implementation evidence | Status and limitation |
|---|---|---|
| One orchestration API supports five networking backends | `Runtime/Interfaces/IOrchestrationBackendRuntime.cs`, `IOrchestrationSessionView.cs`, `IOrchestrationQueueView.cs`; five `*BackendRuntimeAdapter.cs` classes | Implemented for PUN 2, Fusion, Unity Netcode, FishNet, and Mirror |
| Access allocation is independent of the networking SDK | `Core/QueueManagerCore.cs`, `Core/QueueRuntimeSharedLogic.cs`, `Policies/PolicyEngineCore.cs` | Implemented; the evaluated default policy is FCFS |
| Session and queue state are exposed uniformly to UI and telemetry | The three runtime/view interfaces, `Runtime/BackendRuntimeResolver.cs`, and `Telemetry/TelemetryUI.cs` | Implemented |
| Existing V-Lab interactions are preserved | `SharedSync/SharedSyncObjectDefinition.cs` and `SharedSync/BaseInteractionGate.cs` integrate with the pre-existing `Assets/Scripts/Interface/MouseUI.cs` | Implemented in the audited laboratory scene; the public sample uses a separate generic interaction contract |
| Shared objects can be scene-authored or provisioned at runtime | `SharedSync/ConfiguredSharedObjectRuntimeProvisioner.cs`, the configuration lists, and native FishNet/Mirror server spawners | Implemented; the rights-cleared showcase instrument is configured as a runtime prefab |
| Setup is driven by one project configuration asset | `Config/OrchestrationProjectConfig.cs`, `Editor/Setup/OrchestrationSetupWindow.cs`, and `Assets/Resources/AIMultiplayerAssistant/OrchestrationProjectConfig.asset` | Implemented |
| Runtime networking infrastructure can bootstrap without scene manager prefabs | `Runtime/Bootstrap/OrchestrationRuntimeBootstrapEntryPoint.cs` and `OrchestrationRuntimeBootstrap.cs` | Implemented for the selected backend |
| The same avatar concept is authored for each backend | Shared avatar authoring utilities plus each backend's `NetworkAvatarSpawner*`, `AvatarDisplayNameSync*`, and `UserPresenceTransformSync*` | Implemented |
| Benchmark workloads are automated rather than manually timed | `Runtime/Scenarios/OrchestrationScenarioRunner.cs` and `Editor/Setup/BenchmarkScenarioPresetCatalog.cs` | Implemented with five protocol presets |
| Results include lifecycle, queue, authority, performance, fairness, and scenario-validity data | `Telemetry/EvaluationMetricsManager.cs`, `EvaluationSummaryBuilder.cs`, `RuntimePerformanceSampler.cs`, and scenario metric recorders | Implemented |
| The editor can compare all five backends | `Editor/Setup/BackendEvaluationAnalyzer.cs` and the research workspace in `OrchestrationSetupWindow.cs` | Implemented; TOPSIS is a decision-support output, not proof of universal superiority |
| AI controls allocation | `AI/*` and the optional policy hook in shared queue logic | Experimental only; AI was disabled in the audited configuration and pilot runs |

## Backend Parity

Each backend contains an implementation of the following roles:

| Role | PUN 2 | Fusion | Unity Netcode | FishNet | Mirror |
|---|:---:|:---:|:---:|:---:|:---:|
| Session orchestrator | Yes | Yes | Yes | Yes | Yes |
| Replicated queue runtime | Yes | Yes | Yes | Yes | Yes |
| Interaction gate | Yes | Yes | Yes | Yes | Yes |
| Avatar spawner | Yes | Yes | Yes | Yes | Yes |
| Avatar display-name synchronization | Yes | Yes | Yes | Yes | Yes |
| User-presence transform synchronization | Yes | Yes | Yes | Yes | Yes |
| Standalone shared-object synchronization | Yes | Yes | Yes | Yes | Yes |
| Common runtime/session/queue adapter | Yes | Yes | Yes | Yes | Yes |

The presence of equivalent roles demonstrates structural parity. Behavioral
equivalence must be supported by tests and replicated experimental results.

## Runtime Architecture Evidence

The runtime entry point loads the configuration from `Resources` after scene
loading. The bootstrap then:

1. Selects the configured backend.
2. Removes or disables foreign backend state.
3. Creates the required runtime root for the selected backend.
4. Provisions configured shared objects.
5. Applies shared-object and avatar binding.
6. Creates the shared telemetry UI.
7. Starts optional AI services only when explicitly enabled.

The runtime adapter exposes backend state without forcing the UI, telemetry, or
scenario runner to call backend SDK APIs directly.

## Shared-Object Evidence

The publication showcase uses a project-authored generic instrument prefab with:

- `AutomaticBackendObjectBinding`
- `SharedSyncObjectDefinition`
- Stable shared-object identity
- Queue and authority requirements
- Transform and state synchronization
- A deterministic `Controlled Turret` benchmark child

The original V-Lab integration can identify manipulable children through its
existing `MouseUI` component. The rights-cleared publication sample instead uses
the backend-independent `SoftwareXControllablePart` marker. Both paths preserve
the same orchestration contract without redistributing the original media.

## Evaluation Evidence

Protocol 1.1-1.3 exports are retained as diagnostic evidence, not final paper
data. The latest diagnostic matrix established that PUN 2 completed all five
presets, Fusion and one Netcode run were rejected by an overly strict physical
handoff-count rule, Mirror manipulated an unspawned local instrument, and
FishNet's generated manager lacked its spawnable-prefab collection. Protocol 1.4
corrects the validity definition and rejects FishNet/Mirror measurement unless
the shared object is genuinely network-spawned.

The protocol 1.4 acceptance matrix must be completed before randomized final
replicates begin. AI remains disabled for this baseline experiment.

## Gaps Before Publication

- Project-authored edit-mode tests cover the clean showcase, canonical presets,
  protocol version, and core FCFS behavior; two-player backend pilots are still
  required because transport behavior cannot be proven by edit-mode tests.
- The SDK-independent package and the materialized five-backend runtime compile
  cleanly with the audited SDK matrix.
- A clean mirrored checkout of the showcase now resolves its local packages,
  passes tests, builds for Windows, and completes a runtime smoke test.
- The public sample uses original Unity-primitive assets; any replacement
  artwork still requires documented provenance and compatible licensing.
- The current Git working tree is not a clean citable release.
- Final results need replicated, randomized execution and statistical analysis.
- AI behavior has not been evaluated and should not be included in v1 claims.

## Recommended Claim Language

Use:

> VLab Orchestrator provides a common Unity orchestration and evaluation layer
> for five multiplayer networking backends, demonstrated in a collaborative
> virtual laboratory and evaluated through backend-independent automated
> scenarios.

Avoid:

> The framework proves that backend X is the best networking solution.

Also avoid describing runtime-provisioned objects, AI allocation, or behavioral
parity as fully evaluated unless dedicated evidence is added.
