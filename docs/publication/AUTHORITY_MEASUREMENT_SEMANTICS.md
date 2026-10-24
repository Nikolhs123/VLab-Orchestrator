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

## Protocol 1.5: Abandoned Request Timestamps (historical change)

**What 1.5 changed** (commit 154af92, `CHANGELOG.md` "Protocol 1.5"): when local
logical access is revoked before physical ownership completes, the pending
Netcode authority-transfer measurement is cancelled
(`AuthorityTransferRecorder.CancelPendingTransfer`), so a later ownership
completion cannot inherit the request timestamp of a previous benchmark cycle.
Exports produced after the change are versioned 1.5 so they are not pooled with
the 1.4 candidate matrix. The same pending-request table existed before; what was
missing was removing an abandoned entry.

**Where the effect is visible in retained data.** The candidate batch
(`data/candidate/.../UnityNetcode`, protocol 1.4, 22 July 2026) and the targeted
reruns (`data/validation/protocol-1.5-anomaly-validation-2026-07-23/UnityNetcode`,
protocol 1.5) are separate datasets; the reruns are validation, not additional
independent samples. One run each, `authority_transfers.csv`
`transfer_latency_ms` (the rows alternate between a request observed by this
process and a completion without a locally observed request, recorded as 0):

| Run | Transfers | Observed durations (ms) | Mean (ms) |
| --- | --- | --- | --- |
| 1.4 candidate, S1 (10 s hold) | 5 | 18.5, 11099.4, 11100.0; two rows 0 | 7405.9 |
| 1.5 rerun, S1 | 5 | 17.2, 16.2, 16.8, 0.5; one row 0 | 12.7 |
| 1.4 candidate, S3 (2.5 s hold) | 19 | 18.1, 1.0 and nine values 2832.6-2841.8; the other rows 0 | 2323.6 |
| 1.5 rerun, S3 | 19 | 15.9-17.8 and 0.5; the other rows 0 | 15.3 |

In the 1.4 runs the large values are of the order of the preceding hold time
(10 s and 2.5 s) instead of tens of milliseconds, which is what a timestamp
taken at an earlier request and not discarded would produce. This is consistent
with the stated cause; it does not prove it for each row, because the 1.4 files
carry no measurement identifiers (introduced later). The difference must not be
read as an improvement of Netcode networking performance: both are the same
code path with a bookkeeping error removed, and 1.4 values that include the
stale timestamp are measurement errors, kept as historical data and not
corrected.

**Annotated record (1.4 candidate, S1, `Run_20260722_214040_350`, third row of
`authority_transfers.csv`, joined with the same run's `queue_transitions.csv`).**
All stamps are from one machine and are used here only to read the record; they
are not a general way to compute durations.

| UTC (21:41:..) | File | Event |
| --- | --- | --- |
| 12.923 | queue_transitions | Client_1 released, Client_0 granted |
| 12.932 | authority_transfers | Row 3 `transfer_requested_utc`: a transfer request for logical holder Client_1 is recorded, although Client_1 is not the holder |
| 13.965 | queue_transitions | Client_1 requests access, queued behind Client_0 (1.03 s after the recorded transfer request) |
| 23.973 | queue_transitions | Client_0 released, Client_1 granted |
| 24.031 | authority_transfers | Row 3 `transfer_completed_utc`: Client_1 becomes owner, 58 ms after its grant; recorded duration 11099.4 ms |

The request timestamp precedes Client_1's own access request by about one
second and its grant by about 11 s, so the recorded duration measures the time
since an abandoned earlier request, not the ownership handoff that followed the
grant. In the protocol 1.5 rerun the same row type shows 16-17 ms. Row 5 of the
same run (11099.96 ms) shows the same pattern.

**Regression coverage today** (`tests/AuthorityLifecycle/Program.cs`):

- cancel then new request for the same object and holder: the canceled request
  leaves no pending entry, the new request gets a different measurement ID, the
  completion is attributed to the new request and not to the abandoned one, and
  a `Canceled` lifecycle row is written (the first block of `Main`);
- measurement-scoped terminal events: a delayed completion of a canceled
  measurement is rejected as `StaleTerminalRejected` and does not close the newer
  one; a delayed native callback after cancel is journaled as
  `LateObservationIgnored` and creates no completion.

Limits: this regression exercises the recorder and tracker with stand-ins, not a
Unity Netcode run; the specific cancel-then-re-request check was not
mutation-tested (two other mutations were, see below). In the current revision
protocol 1.6 the measurement-ID mechanism supersedes the 1.5 key-based removal.
SDK ownership callbacks carry no request token, so after a cancel and a new
request for the same object, an observed local authority is attributed to the
newer request (documented above).

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
