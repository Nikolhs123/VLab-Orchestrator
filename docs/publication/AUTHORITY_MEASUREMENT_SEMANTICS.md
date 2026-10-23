# Authority Measurement Semantics - Revision Protocol 1.6

This describes the current revision source, not a completed multiplayer
validation campaign. Historical 1.4/1.5 data and releases are unchanged.
The older package preset test asserted 1.4 while package/production defaults
were 1.5. Revision source now uses 1.6 consistently in both config definitions,
the production preset catalog and the package preset test. This does not
retroactively establish the exact configuration of an archived historical run.

## Clock And Missing Durations

Local elapsed duration is `(completion ticks - request ticks) * 1000 / frequency`,
using `System.Diagnostics.Stopwatch`. UTC strings label observations and are not
subtracted for elapsed duration. Clock frequency and a recorder scope ID are
exported; frequency describes tick units, not measured effective resolution.
Ticks must not be subtracted between processes or machines.

A completion with no locally observed request has an empty request timestamp,
`latency_available=False` and `transfer_latency_ms=-1`. It remains an observed
completion; it is excluded from duration statistics. An observed request and
completion at the same monotonic tick has `latency_available=True` and zero
duration, and is included. Summary sample counts distinguish no duration samples
from a measured zero. Neither event implies instantaneous network propagation.

Imported completions retain their source clock scope and are marked
`RemoteImported`. Their source duration is reconstructed only with explicit
timing provenance. Legacy payloads lacking that provenance have unavailable
duration. IDs deduplicate imported/echoed completions. An imported completion
cannot remove a different local pending measurement merely because its object
and logical holder match. Imported records are not independent local observations.

## Outputs And Count Invariant

- `authority_transfers.csv`: observed completion rows, now with measurement ID,
  availability, monotonic ticks/frequency, scope and provenance appended.
- `authority_measurement_lifecycle.csv`: request/terminal events, duplicate
  suppression, unmatched/imported observations and pending-at-export snapshots.
- `authority_measurement_summary.csv`: local lifecycle counts plus separate
  unmatched/imported observation counts, `stale_terminal_rejected`,
  `late_observations_ignored` and `counts_reconcile`.
- `native_authority_observations.csv`: independent per-participant SDK state
  samples (see below).
- `authority_metrics_summary.csv`: existing completion statistics with explicit
  available/unavailable latency sample counts. Completion success rate is not
  the proportion of all initiated requests that completed successfully.

Local count invariant:

`initiated = completed + failed + canceled + timed_out + superseded + closed_by_import + pending`.

Duplicate suppression, unmatched/imported observations, stale terminal events
and late observations are outside the local initiation denominator. Export
snapshots do not mutate the journal. Failed SDK completion is distinct from a
timed-out request.

## Measurement Correlation (Adapter Side)

All five adapters use `AuthorityMeasurementTracker`
(`repo/src/framework/Telemetry/AuthorityMeasurementTracker.cs`):

- The measurement starts before any native request is issued and captures the
  measurement ID, tracked object ID, request-time logical holder ID and the set
  of native objects requested. Terminal events reuse these captured values, so
  a local identity change (for example after disconnect) cannot redirect them.
- Terminal events go through measurement-scoped recorder methods
  (`CompleteMeasurement`, `CancelMeasurement`, `TimeoutMeasurement`). An event
  carrying a measurement ID other than the current pending one is rejected and
  journaled as `StaleTerminalRejected`; it never closes a newer measurement.
- A native observation for an object of a measurement that already ended
  (completed, failed, canceled or timed out) is journaled as
  `LateObservationIgnored` and changes no counts or rows.
- Native observations for objects not requested by the pending measurement are
  ignored (PUN2 ownership callbacks are global to the room).
- A retry while pending re-sends native requests inside the same measurement.
- Each adapter closes an unresolved measurement as `TimedOut` after
  `authorityMeasurementTimeoutSeconds` (serialized, default 10 s, 0 disables),
  measured with `Time.realtimeSinceStartupAsDouble`.

SDK ownership callbacks and state predicates carry no request token. After a
cancel and a new request for the same native object, local authority observed
while the newer measurement is pending is attributed to the newer measurement;
the regression asserts this behavior explicitly. It records "local authority
observed after this request", not that this specific request caused it.

## Adapter Hook Audit

| Backend | Native objects in a measurement | Local-authority observation | Failure observation |
| --- | --- | --- | --- |
| Netcode | Root `NetworkObject` | `Update`: root `OwnerClientId == LocalClientId` | Timeout only |
| Mirror | Root `NetworkIdentity` | `Update`: `isOwned`, or server with no client authority | Timeout only |
| FishNet | Root `NetworkObject` | `Update`: `IsOwner`, or server with no owner | Timeout only |
| PUN2 | Root and, if enabled, controlled child `PhotonView`s not already owned | `OnOwnershipTransfered` for a requested view whose new owner is local | `OnOwnershipTransferFailed` sent by the local player; timeout |
| Fusion | Root and eligible child `NetworkObject`s without state authority | `Update`: `HasStateAuthority` per requested object | Object destroyed or invalid; timeout |

A measurement completes only when every requested native object resolved;
success requires all of them to be locally authoritative. This replaces the
earlier PUN2/Fusion behavior, where the first resolved object closed the
aggregate. PUN2/Fusion multi-object measurements are still not physically
equivalent to the root-only Netcode/Mirror/FishNet measurements; reports must
state the object count (`nativeObjects=` in the details field).

## Independent Native-Authority Observations

Each participant's access controller reads SDK state every frame, independently
of the request/completion path, and `NativeAuthorityObservationRecorder` stores
each change of: replicated logical holder, SDK-reported native owner of the root
object, local native authority, local logical access and queue activity, with
frame number and the participant's monotonic clock. A Mirror client cannot read
which other connection owns an object; it records `NotLocal(unobservable on
client)`. Every participant now exports its own records: the coordinator under
`<exports>/<backend>/<scenario>/Run_*`, other participants under
`<exports>/ParticipantExports/<backend>/<scenario>/Run_*_<participant>`, each with
`participant_identity.csv`. Records from different participants must be aligned
by event sequence or a shared reference, not by subtracting their clocks.

## Executed Recorder Regression

`dotnet run --project tests/AuthorityLifecycle/AuthorityLifecycle.csproj`

Compiles actual recorder, tracker and observation-recorder source with Unity
stand-ins. Tests include exact monotonic elapsed time, measured zero vs
unavailable, cancellation/re-request, timeout/count reconciliation,
duplicates/supersession, pending snapshots, source timing import, import
deduplication, local-pending isolation, legacy timing rejection, stale terminal
rejection, all-object aggregate completion, partial failure, unrelated-object
isolation, late observations after completion/cancel/timeout, captured holder
identity, and change-only observation journaling. Two mutations (first-object
completion; unchecked measurement ID) were confirmed to fail the regression.
Test JSON uses System.Text.Json with public-field support.

`tests/SdkCompileCheck` compiles the canonical framework (runtime and, with
`-IncludeEditor`, editor code), the core package assembly and all five adapters
against locally installed SDK assemblies, mirroring the installer's exclusions.
It is a C# API compatibility check: it does not run Unity IL weavers or execute
networking code. Unity JsonUtility, real SDK callbacks and multi-process
behavior still require Unity integration runs. This is software measurement
verification, not evidence of faster networking.
