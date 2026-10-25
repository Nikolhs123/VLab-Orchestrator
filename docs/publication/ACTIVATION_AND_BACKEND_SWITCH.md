# Backend Independence, Activation, Minimal Run And Backend Switch - Revision Protocol 1.6

Addresses: what "backend independent" does and does not mean in this
distribution, why activation needs all five SDKs, how to run two participants
and what to expect, and what changes when a project switches backend.

## 1. What is backend-independent, and what is not

| Layer | Location | SDK references | Status |
| --- | --- | --- | --- |
| Orchestration core (FCFS queue, policy, models) | `repo/src/packages/com.vlab.orchestrator.core/Runtime` (`VLab.Orchestrator.Core.asmdef`, no assembly references) | None; compiled against Unity only in `tests/SdkCompileCheck/Core` | Backend-independent, also at compile level |
| Shared queue logic, telemetry, scenarios, UI, shared-object definitions | `repo/src/framework` | None in most files; 78 references to adapter or SDK namespaces in 14 files (backend resolver, runtime bootstrap, shared-object binding and registry, avatar utilities, editor authoring, setup window) | Contract-level independent; not compile-level independent |
| Adapters | `repo/src/backend-adapters/<Backend>` | One SDK each | Backend-specific by design |

The accurate claim is therefore: a backend-independent application-level
orchestration contract (interfaces, queue, telemetry, scenarios) with
backend-specific adapters. The current distribution is **not
deployment-independent at the package activation level**: the runtime is
activated as one unit.

## 2. Why activation requires all five SDKs

`SoftwareXRuntimeInstaller` activates the runtime only when the types
`Photon.Pun.PhotonView`, `Fusion.NetworkObject`, `Unity.Netcode.NetworkObject`,
`FishNet.Object.NetworkObject` and `Mirror.NetworkIdentity` are all loaded, and
otherwise logs which SDK is missing and stops. The reason is structural, not a
property of the contract: the installer copies the framework and all adapters
into the project as one compilation, and the framework files listed above refer
to all five adapters (for example the runtime resolver and the shared-object
binding switch over every backend). Without one SDK the whole set would fail to
compile. Making each adapter installable alone would require splitting those
files into per-backend assemblies; that has not been done in this release and is
listed as future work. The same constraint is why the compile check
(`tests/SdkCompileCheck`) needs all five SDKs in one project.

The distributed packages `com.vlab.orchestrator.backend.*` contain only the
adapter boundary documentation and a reference copy of the adapter source
(`ReferenceSource/*.cs.txt`, checked against the canonical source by
`tools/sync_reference_source.py --check`). Vendor SDKs are not redistributed.

## 3. Minimal two-participant run

Prerequisites: Unity 6000.3.11f1, all five SDKs installed in the project (see
`DEPENDENCY_AND_LICENSE_BOUNDARIES.md`), Photon App IDs configured if PUN2 or
Fusion is used (the other three need no account).

1. Select backend and scenario and build, in batch mode (or with the Setup window):

```text
Unity.exe -batchmode -nographics -projectPath <project> -quit -executeMethod VLabOrchestrator.Editor.SoftwareXRuntimeInstaller.InstallOrUpdateRuntime
Unity.exe -batchmode -nographics -projectPath <project> -quit -executeMethod VLabOrchestrator.Editor.SoftwareXBatchCommands.SelectBackendAndScenario -vlabBackend UnityNetcode -vlabScenario BaselineFcfs
```

   then build the player from the showcase project (`ShowcaseValidation.BuildWindows`).
   Run the install step twice on a fresh project: the first run materializes
   the runtime, the second authors the assets after compilation.

2. Start two processes on one machine (the client a few seconds after the host):

```text
VLabOrchestratorShowcase.exe -batchmode -nographics -vlabSession host   -vlabRoom demo1 -vlabQuitWhenDone -vlabTimeoutSeconds 300
VLabOrchestratorShowcase.exe -batchmode -nographics -vlabSession client -vlabRoom demo1 -vlabQuitWhenDone -vlabTimeoutSeconds 300
```

3. Expected output (Netcode S1, one smoke run, 23 October 2026; the other
   backends differ only in names and timings):

   - Both processes exit with code 0 (the host exits 3 if the run is marked
     INVALID). The host log ends with
     `BENCHMARK COMPLETE - VALID EXPORT SAVED - SAFE TO QUIT`; the client log
     with `participant data exported`.
   - `EvaluationExports/UnityNetcode/S1_Baseline_FCFS_2Users_3Cycles_10s_Turret60/Run_<timestamp>/`
     (coordinator) with 27 CSV files, among them `run_master_summary.csv`,
     `scenario_assertions.csv`, `queue_transitions.csv`,
     `authority_transfers.csv`, `native_authority_observations.csv`,
     `transform_observations.csv`, `execution_manifest.csv` and
     `participant_identity.csv`; and
     `EvaluationExports/ParticipantExports/UnityNetcode/<scenario>/Run_<timestamp>_Client_1/`
     with the client's own files.
   - In `run_master_summary.csv` of that run: `client_count=2`,
     `request_count=6`, `granted_count=6`, `released_count=6`,
     `authority_transfer_count=5`, `authority_transfer_success_count=5`,
     `scenario_valid=True`, `scenario_completed_cycles=3`,
     `scenario_failed_validity_assertions=0`. Timing columns vary between runs
     and between backends and are not expected values.
   - `scenario_assertions.csv` lists the protocol assertions (SessionStarted,
     HolderGranted, NextUserQueued, HolderTransferredToNext, FinalQueueIdle,
     AuthorityTransferTelemetryComplete, FinalManipulationRotationConverged,
     ...); all `required_for_validity` assertions must be `True`.
   - Both exports carry the same `session_run_id` in `participant_identity.csv`.
   - To check a set of exports: `py tools/analyze_session_invariants.py <exports_root>`
     prints `ALL INVARIANTS HOLD` or the violated invariants.

This run is a software check. Partial verification (25 October 2026): the tag
`freeze-protocol-1.6-c` was cloned into a new folder of the development machine,
the Photon, Mirror and FishNet SDK folders were copied from the audited
installation into the showcase project (Netcode comes through the package
manifest), the installer was run twice, and the commands above were executed for
Netcode, Mirror and FishNet. All three players were built, both processes exited
with code 0, the host log ended with the status line above, and the coordinator
summary showed `client_count=2`, `request_count=6`, `granted_count=6`,
`released_count=6`, `authority_transfer_count=5`, `authority_transfer_success_count=5`,
`scenario_valid=True`, `scenario_completed_cycles=3` and no failed validity
assertion on each of the three backends. Note that the first `run_master_summary.csv`
found in an export tree can be the participant's copy (request count 0): read the
one in the coordinator export folder. Not covered: a second machine, the SDK
downloads (copied, not acquired), a fresh Photon account (PUN 2 and Fusion were not
run), and the Code Ocean capsule. The verification on a clean machine is
therefore still **pending**.

## 4. Switching between two backends

The example switches the same project between Mirror (direct connect, no
account) and Photon Fusion (Photon Cloud, App ID required).

Stays the same:

- the scene and the laboratory objects, including the shared instrument and its
  `SharedSyncObjectDefinition` (object id, controlled children);
- the application's own interaction scripts (clicking, dragging), which are only
  gated by `BaseInteractionGate` and never call a networking SDK;
- the orchestration configuration `OrchestrationProjectConfig` except the
  backend selection: protocol preset, scenario, expected participants, timing
  and tolerance values;
- the queue policy (FCFS), the telemetry and the export formats.

Changes when the backend is switched (`-vlabBackend Mirror` to
`-vlabBackend Fusion`, or the backend field in the Setup window):

1. `OrchestrationProjectConfig.backendType`, and through
   `SoftwareXRuntimeAuthoring.SyncRuntimeConfiguration` the runtime
   configuration copy and the runtime root prefab of the selected backend.
2. The backend components on each shared object: `AutomaticBackendObjectBinding`
   removes the components of the other backends and adds the selected backend's
   (for example the network identity component, the access controller and the
   child transform sync; the exact set differs per backend).
   This happens in the editor authoring step and is validated again at runtime.
3. Backend-specific settings that do not exist for the other backend: Fusion
   needs the Photon App ID and region setting; Mirror uses the base port and
   port range of the direct-connect orchestrator.
4. The SDK must be present in the project (all five, see section 2).

Recorded diff (25 October 2026, Campaign B software, scenario S1): selecting
Mirror and then Fusion with the batch command above and comparing 36 authored
files (`Assets/Resources`, `Assets/Scenes`, `ProjectSettings`, without `.meta`),
exactly 4 files differ and nothing else:

| File | Change |
| --- | --- |
| `Resources/VLabOrchestrator/OrchestrationProjectConfig.asset` | `backendType: 4` (Mirror) to `1` (Fusion) |
| `Resources/AIMultiplayerAssistant/OrchestrationProjectConfig.asset` | the same single field |
| `Resources/AIMultiplayerAssistant/SoftwareXSharedInstrument.prefab` | the Mirror components of the instrument (network identity, access controller, child sync, gate) are replaced by the Fusion ones (network object with the instrument's nested object, access controller, child sync, gate) |
| `Resources/AIMultiplayerAssistant/NetworkAvatar.prefab` | the Mirror network identity, transform sync and name sync are replaced by the Fusion ones |

The scene (`Assets/Scenes`), the project settings and all application scripts
are byte-identical between the two selections. The Photon App ID needed by Fusion
is a separate setting in the Photon settings asset and was not part of this
comparison. The complete diff is in the revision materials
(`switch_example/mirror_vs_fusion_asset_diff.txt`).

## 5. What the answer to the reviewers can and cannot say

- It can say that the core and the contract are SDK-free and that the five
  adapters implement the same operations (see `CONFORMANCE_TABLE.md`).
- It cannot say that one adapter can be installed alone: activation requires
  the full audited matrix because of the framework's direct references.
- Splitting the framework into per-backend assemblies is a possible
  packaging change; it was not made, to keep the audited and measured
  source identical to the evaluated one.
