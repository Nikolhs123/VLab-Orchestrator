# Test Plan

The playable showcase provides an immediate manual conformance demonstration.
Six repeatable edit-mode tests for the FCFS queue core are included under:

`repo/src/showcase/UnityProject/Assets/Tests/Editor`

Not yet automated: export-schema tests, runtime bootstrap tests, generic
shared-object binding tests and an installation test path per optional backend.

Final publication measurements are not substitutes for automated software tests.

## Queue Core Regression

```powershell
dotnet run --project tests/QueueCore/QueueCore.csproj
```

Compiles the package `QueueManagerCore` and the framework
`QueueRuntimeSharedLogic` (used by all five adapters) with recording stand-ins.
Covers ordering by authoritative arrival, duplicate requests, release
semantics, departure of a waiting participant and of the holder, rejoin,
timeout, and restoring queue authority from a replicated snapshot (holder,
queue order and history verbatim).

## Two-Process Lifecycle Scenarios

A player build started twice (`-vlabSession host|client -vlabRoom <name>`)
with `-vlabLifecycle <case>` runs one case and writes assertions to
`scenario_assertions.csv` in each participant's export:

- `HolderLeavesWhileOtherWaits`: the holder process is killed while the other waits.
- `QueueAuthorityLeaves`: the coordinator process is killed while the other waits;
  where the session survives (queue-authority migration) the waiting
  participant must be granted and obtain native authority.
- `LateJoin`: shared state and queue state set before the second participant joins.
- `LeaveAndRejoin`: graceful leave while queued, then rejoin.
- `NearConcurrentRequests`: ten rounds of requests from both participants as soon
  as the instrument is free (random 0-300 ms delay on each); every request
  granted once, no duplicate or holder in the queue, native authority on every
  grant. Which request arrived first is read from the authority's
  `queue_transitions.csv` (the later arrival is an `AccessQueued` row).

These are software checks, not measurements.

## Session Invariants Over Exports

```powershell
py tools/analyze_session_invariants.py <exports_root> --out invariants.csv
```

Replays every export's `queue_transitions.csv` through an FCFS state machine
(holder before/after, queue snapshot, grant to queue head after release,
release by holder, no duplicate entries), checks that all required scenario
assertions passed, and pairs exports by `session_run_id` (one Coordinator and
the expected Participants per session). Exit code 1 on any violation.

## Authority Measurement Lifecycle Regression

Run with a .NET SDK supporting net8.0:

```powershell
dotnet run --project tests/AuthorityLifecycle/AuthorityLifecycle.csproj
```

This compiles the actual framework `AuthorityTransferRecorder`,
`AuthorityMeasurementTracker` and `NativeAuthorityObservationRecorder` with
minimal Unity stand-ins. It covers request/terminal correlation, measurement-
scoped rejection of stale terminal events, all-object aggregate completion,
partial failure, unrelated-object isolation, late observations after
completion/cancel/timeout, timeout, captured holder identity, monotonic timing,
measured zero versus unavailable duration, count reconciliation, imports,
pending export and change-only native-authority observation journaling. It does
not exercise real SDK callbacks, Unity JsonUtility or multi-process behavior.
See `docs/publication/AUTHORITY_MEASUREMENT_SEMANTICS.md` for scope and denominators.

## SDK API Compile Check

Compiles the canonical framework, the core package assembly and all five
adapters against SDK assemblies from a local Unity project that has the SDK
matrix installed and compiled (no SDK binary is stored in this repository):

```powershell
powershell -File tests/SdkCompileCheck/run.ps1 -SdkProject "<project>" -IncludeEditor
```

Exclusions mirror `SoftwareXRuntimeInstaller`. This checks C# API compatibility
only; Unity IL weavers/code generators are not run and nothing is executed.

## ReferenceSource Consistency

```powershell
py tools/sync_reference_source.py --check
```

Fails when a backend package `ReferenceSource/*.cs.txt` copy differs from the
canonical adapter in `repo/src/backend-adapters`. Without `--check` it rewrites
drifted copies from the canonical source.
