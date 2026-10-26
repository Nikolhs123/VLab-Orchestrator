# Backend Conformance Table - Revision Protocol 1.6

What the orchestration contract requires of a backend adapter, how each of the
five audited adapters meets it, and where it does not. "Run" means two-process
runs on one machine (Netcode, Mirror, FishNet: local; PUN2, Fusion: Photon
Cloud) of the revision software (tag `freeze-protocol-1.6-b`, 24-25 October 2026):
the lifecycle cases were run 3 times per backend and case (75 of 75 passed), the
access-control probes 5 times per backend (counts in section 3), and the
scripted benchmark scenarios S1-S5 10 times per backend and scenario (249 of 250
executions valid). Entries marked "Run" for other behavior were first checked in
single smoke runs on 21-22 October 2026. These are software checks and
counts, not performance claims. "Source" means read from the adapter code
without a run.

Audited SDKs: Netcode for GameObjects 2.11.0, Mirror 96.0.1, FishNet 4.6.22,
Photon PUN 2 2.55, Photon Fusion 2.0.9 (build 1566, Shared mode).

## 1. Session and participants

| Contract operation | Netcode | Mirror | FishNet | PUN2 | Fusion |
| --- | --- | --- | --- | --- | --- |
| Topology | Host (server+client) | Host | Host | Photon Cloud room, master client | Photon Cloud, Shared mode |
| Create / join | Direct connect: room name hashed to a port (`basePort` 7777 + 0..499) | same | same | Room by name on Photon Cloud | Session by name on Photon Cloud |
| Room discovery (`LookupRoom`) | Unsupported | Unsupported | Unsupported | Lobby list (Unavailable outside the lobby) | Session list (Unavailable outside the lobby) |
| Participant identity | `Client_<id>` | `Client_<id>` | `Client_<id>` | `Actor_<n>` | `Player_<raw>` |
| Identity bound to transport sender on queue RPCs | Yes (`ServerRpcParams`) | Yes (Command sender) | Yes (ServerRpc caller) | Yes (`PhotonMessageInfo`) | Yes (`RpcInfo.Source`) |
| Session run id shared by participants | Run | Run | Run | Run | Run |

Notes. Direct-connect room names are not discovered, they are mapped to a port:
two different names can map to the same port (500 ports) and the mapping relies
on a deterministic string hash, which held across processes in the Unity players
used here; this is a limitation of direct-connect backends, not a property of
the contract. `Unsupported` means the backend has no room discovery; it is not
the same as "room absent". Spoofing attempts by a modified client were not
tested (see `ACCESS_CONTROL_AND_IDENTITY.md`).

## 2. Queue and logical holder

| Contract operation | Netcode | Mirror | FishNet | PUN2 | Fusion |
| --- | --- | --- | --- | --- | --- |
| Queue authority (runs FCFS logic) | Server/host | Server/host | Server/host | Master client | State authority of the queue runtime (master-client object) |
| Ordering | Authoritative arrival order | same | same | same | same |
| Duplicate request / holder re-request | Denied, no duplicate queue entry (shared logic, `tests/QueueCore`) | same | same | same | same |
| Replication of queue state | NetworkVariables | ClientRpc + TargetRpc on spawn | ObserversRpc (buffered) | Buffered PunRPC | `[Networked]` properties |
| Near-concurrent requests (10 rounds, both orders of arrival observed) | Run, 3/3 attempts: all granted once, no duplicate | Run, 3/3 | Run, 3/3 | Run, 3/3 | Run, 3/3 |
| Late join receives current state | Run, 3/3 attempts | Run, 3/3 | Run, 3/3 | Run, 3/3 | Run, 3/3 |
| Waiting participant leaves | Run (entry removed) | Run | Run | Run | Run |
| Holder leaves abruptly while another waits | Run, 3/3: next participant granted | Run, 3/3 | Run, 3/3 | Run, 3/3 | Run, 3/3 |
| Rejoin after leaving | Run, 3/3: new identity, queued as new arrival | Run, 3/3 | Run, 3/3 | Run, 3/3 | Run, 3/3 |
| Queue authority leaves abruptly while another waits (3 attempts each) | Session ends (outcome `SessionEnded`) 3/3 | Session ends 3/3 | Session ends 3/3 | Session continues; new master restores the queue from the replicated snapshot, waiting participant granted 3/3 | Session continues; new state authority restores the queue, waiting participant granted 3/3 |
| Abrupt departure detected after (upper bound, median of 3 attempts) | 30.8 s | 11.1 s | 61.4 s | 9.6 s | 11.5 s |

Notes. In the holder-leaves case the waiting participant was granted by the
holder's idle timeout before the departure was detected on all five backends.
Per-user request counters and the holder's idle clock are not restored after a
queue-authority migration (the idle clock restarts). The measured detection
times are the time to observe the departure, not a guaranteed bound.

## 3. Native authority

| Contract operation | Netcode | Mirror | FishNet | PUN2 | Fusion |
| --- | --- | --- | --- | --- | --- |
| Native authority object | Root `NetworkObject` owner | Root `NetworkIdentity` owner | Root `NetworkObject` owner | `PhotonView` owner (root and optional child) | State authority of root and eligible children |
| Arbitration of who may take it | Server grants only to the logical holder; server removes other owners after a 1 s grace | same | same | None: `OwnershipOption.Takeover` | None: `AllowStateAuthorityOverride` |
| Unauthorized write by a non-holder or former holder (gate bypassed; 60 probes in 5 attempts) | Not propagated (0/60) | 0/60 | 0/60 | 0/60 | 0/60 |
| Master/host write before first grant (10 probes) | Not propagated (0/10) | 0/10 | 0/10 | Propagated 5/10 | 0/10 |
| Holder's write before native authority arrived (25 probes) | Published after acquisition 5/25 (first cycle of each attempt) | 5/25 | 5/25 | 0/25 | 5/25 |
| Unauthorized write stays on the writer's own replica | Yes | Yes | Yes | No | Yes |
| Independent observation of the local native authority | Run | Run | Run | Run | Run |
| Observation of the other participant's native owner | Server only | Server only; clients cannot read it (`NotLocal(unobservable on client)`) | Server only | Yes | Yes |

Note on the PUN 2 child sync. The class comment of `SharedTransformSyncPUN2` explains the handoff fix with the statement that PUN streams stop when ownership moves and lose the last changes. That is the design rationale, not a proven mechanism: the cause inside the PUN stream was not isolated (see the campaign analysis); the explicit final-state send does not depend on it. The source file is kept byte-identical to the measured builds, so its hash matches the manifests of the campaigns.

PUN2 and Fusion declare a limitation (author decision, 20 October 2026): native
ownership is not arbitrated by a server, access control is cooperative, and
violations are detectable in `native_authority_observations.csv` but not
prevented by the network layer. The same takeover configuration was used for the
historical data, which keeps them comparable. The probe counts are from 5 attempts
per backend; they are counts, not rates.

## 4. Handoff completion measurement

| Contract operation | Netcode | Mirror | FishNet | PUN2 | Fusion |
| --- | --- | --- | --- | --- | --- |
| Native objects in a measurement | Root | Root | Root | Root + controlled child views not yet owned | Root + eligible children without state authority |
| Local-authority observation | `OwnerClientId == LocalClientId` each frame | `isOwned` (or server without client authority) | `IsOwner` (or server without owner) | `OnOwnershipTransfered` for a requested view | `HasStateAuthority` per requested object |
| Failure observation | Timeout only | Timeout only | Timeout only | `OnOwnershipTransferFailed` or timeout | Object destroyed or invalid, or timeout |
| Stale terminal event after a newer request | Rejected, journaled | same | same | same | same |

Source of the hook table and the correlation rules:
`AUTHORITY_MEASUREMENT_SEMANTICS.md`. Multi-object (PUN2, Fusion) and
root-only (Netcode, Mirror, FishNet) measurements are not physically equivalent;
the object count is recorded with each measurement.

## 5. Provisioning, synchronization and cleanup

| Contract operation | Netcode | Mirror | FishNet | PUN2 | Fusion |
| --- | --- | --- | --- | --- | --- |
| Shared instrument provisioning (source) | Not audited for this table | `ConfiguredSharedObjectSpawnerMirror`, server spawn | `ConfiguredSharedObjectSpawnerFishNet`, server spawn | Not audited for this table | Not audited for this table |
| Controlled-child transform sync component (source) | `MicroscopeChildTransformSyncNetcode` | `MicroscopeChildTransformSyncMirror` (late joiners request a snapshot) | `MicroscopeChildTransformSyncFishNet` | `SharedTransformSyncPUN2` (no dedicated child component) | `MicroscopeChildTransformSyncFusion` |
| Cleanup on leave | Queue entry removed, holder released | same | same | same | same |
| Handoff of the controlled transform | Owner writes a replicated snapshot; other replicas apply it | The holder's own snapshots returned by the server are ignored (they moved the holder back to an older value; found in the revision campaign) | Same guard as Mirror | The owner sends its exact final transform reliably before it requests release, replicas snap to it, and an owner's interpolation target follows its own transform (before this, replicas settled on a value short of the owner's final rotation; the reason inside the PUN stream was not isolated) | State authority writes, replicas read |
| Ownership request while a local release is pending | A pending local release suppresses ownership requests (otherwise a spurious request at every handoff to the host, rejected by the server) | same | same | same | same |
| SDK-specific defect handled by the adapter | - | Stale state for late joiners (non-buffered ClientRpc) and lost handlers after rejoin, both fixed in the adapter | SDK deactivates spawned objects of a late joiner at `Start`; the adapter re-activates them and logs `FishNetSpawnedObjectReactivated` (4 objects per late join or rejoin, 0 in the benchmark path) | `OnEnable`/`OnDisable` base calls were missing, so room callbacks never ran (fixed) | Spawn before the session scene finished loading was despawned; `IsInRoom` and spawns now wait for the scene load; the queue runtime is spawned once per session |

## 6. Capabilities a backend may not provide

| Capability | Where it is absent | Consequence |
| --- | --- | --- |
| Room discovery | Netcode, Mirror, FishNet | `LookupRoom` returns `Unsupported`; the UI does not pre-validate create/join |
| Server arbitration of native ownership | PUN2, Fusion | Cooperative access control, detection only (section 3) |
| Observing another client's native owner on a client | Mirror (clients) | Recorded as `NotLocal(unobservable on client)` |
| Native failure callback for an authority request | Netcode, Mirror, FishNet | Timeout is the only failure signal |
| Queue continuity when the queue authority leaves | Netcode, Mirror, FishNet | Session ends with the host |

## 7. Operation contract: preconditions, expected result, completion and failure

The same contract holds for all five adapters; backend differences are in
sections 1-6. "Checked by" names the run that exercised the row.

| Operation | Required initial condition | Expected result | Completion event | Failure or refusal | Checked by |
| --- | --- | --- | --- | --- | --- |
| Create or join a session | Backend selected; for Photon backends an App ID is configured; the room name is the same for both participants | The participant is connected, is listed with a framework identity, and a session run id is shared | Connection and participant-list events of the session view | Photon: join fails if the room is not visible (seen once, region mismatch); direct connect: no discovery, `LookupRoom` is `Unsupported` | Every attempt of the scripted scenarios (250 sessions per campaign) |
| Leave | Participant in a session | Queue entry and holder state of the participant are removed | Participant-left event; queue snapshot without the participant | - | Lifecycle cases (leave, rejoin, holder leaves, queue authority leaves) |
| Request access | Participant registered; queue runtime spawned on the queue authority; identity claimed equals the transport sender | Free instrument: participant becomes holder. Held instrument: participant is appended to the waiting queue | `AccessGranted` or `AccessQueued` in the queue history | Holder re-request and duplicate queued request are denied without changing the queue; a request whose claimed identity differs from the sender is rejected | `tests/QueueCore`; near-concurrent case (60 grants per backend) |
| Release | Requesting participant is the current holder | Holder cleared and the first waiting participant is granted (FCFS) | `AccessReleased` followed by `AccessGranted` | A release by a non-holder changes nothing | Every cycle of S1-S5 |
| Authority transfer | Logical access held; the native objects of the instrument listed in the shared-object definition | The local participant becomes the native owner (state authority on Fusion) of every requested object | Local observation of ownership (`OwnerClientId`, `isOwned`, `IsOwner`, `OnOwnershipTransfered`, `HasStateAuthority`); the measurement ends as completed | PUN 2 and Fusion report a failure callback; all backends end an unresolved measurement as timed out after 10 s; a revoked or superseded request is canceled | Authority measurement counts in every export (count identity holds) |
| Shared-object creation | Shared-object definition with root and controlled children | A network object exists on both participants, replicated and observable (readiness check before the scenario starts) | Spawn and network-readiness events | Spawn before the session scene finished loading is despawned on Fusion (handled by the adapter) | Preflight of every attempt; late-join case |

## Not yet demonstrated

- Shared-instrument provisioning and the transform-sync mechanism are listed
  from the source file layout only; their behavior is covered by the late-join
  and S1/S4 smoke runs, not described mechanism by mechanism here.
- Larger repetition: 3 (lifecycle) or 5 (probes) attempts per backend and case
  show stable counts, not rates; the valid-execution proportion of the scripted
  scenarios is 249 of 250 (10 per cell, one PUN2 attempt failed because the two
  processes selected different Photon regions).
- Behavior under packet delay, loss or reordering; more than two participants;
  a second machine.
- Photon Cloud region: processes choose the best region at connect time; a
  fixed region option does not exist in this revision (see the failed attempt above).
- Identity spoofing by a modified client.

## Where each row is checked

`LifecycleScenarioDriver` (cases HolderLeavesWhileOtherWaits, QueueAuthorityLeaves,
LateJoin, LeaveAndRejoin, NearConcurrentRequests), the W03 access probes,
`tests/QueueCore`, `tests/AuthorityLifecycle` and
`tools/analyze_session_invariants.py`, `tools/analyze_campaign.py`,
`tools/analyze_probe_runs.py` and `tools/analyze_lifecycle.py`; see `tests/README.md`.
