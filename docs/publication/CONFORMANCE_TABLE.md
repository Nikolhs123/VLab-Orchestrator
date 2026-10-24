# Backend Conformance Table - Revision Protocol 1.6

What the orchestration contract requires of a backend adapter, how each of the
five audited adapters meets it, and where it does not. "Run" means a two-process
smoke run on one machine (Netcode, Mirror, FishNet: local; PUN2, Fusion: Photon
Cloud), 21-22 October 2026, one run per entry unless stated. These are software
checks, not measurements, and say nothing about performance. "Source" means
read from the adapter code without a run.

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
| Near-concurrent requests (10 rounds, both orders of arrival observed) | Run: all granted once, no duplicate | Run | Run | Run | Run |
| Late join receives current state | Run | Run | Run | Run | Run |
| Waiting participant leaves | Run (entry removed) | Run | Run | Run | Run |
| Holder leaves abruptly while another waits | Run: next participant granted | Run | Run | Run | Run |
| Rejoin after leaving | Run: new identity, queued as new arrival | Run | Run | Run | Run |
| Queue authority leaves abruptly while another waits | Session ends (outcome `SessionEnded`) | Session ends | Session ends | Session continues; new master restores the queue from the replicated snapshot, waiting participant granted | Session continues; new state authority restores the queue, waiting participant granted |
| Abrupt departure detected after (upper bound, one run) | ~31 s | ~11 s | ~61 s | ~10 s | ~12 s |

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
| Unauthorized write by a non-holder (gate bypassed, one run, 12 probes) | Not propagated | Not propagated | Not propagated | Not propagated | Not propagated |
| Master/host write before first grant | Not propagated | Not propagated | Not propagated | Propagated 1/2 | Not propagated |
| Holder's write before native authority arrived | Published after acquisition 1/5 | 1/5 | 1/5 | 0/5 | 1/5 |
| Unauthorized write stays on the writer's own replica | Yes | Yes | Yes | No | Yes |
| Independent observation of the local native authority | Run | Run | Run | Run | Run |
| Observation of the other participant's native owner | Server only | Server only; clients cannot read it (`NotLocal(unobservable on client)`) | Server only | Yes | Yes |

PUN2 and Fusion declare a limitation (author decision, 20 October 2026): native
ownership is not arbitrated by a server, access control is cooperative, and
violations are detectable in `native_authority_observations.csv` but not
prevented by the network layer. The same takeover configuration was used for the
historical data, which keeps them comparable. The probe counts are one run per
backend and are not repeated yet.

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
| Handoff of the controlled transform | Owner writes a replicated snapshot; other replicas apply it | The holder's own snapshots returned by the server are ignored (they moved the holder back to an older value; found in the revision campaign) | Same guard as Mirror | The owner sends its exact final transform reliably before it requests release, replicas snap to it, and an owner's interpolation target follows its own transform (the last sample before the handoff was otherwise lost) | State authority writes, replicas read |
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

## Not yet demonstrated

- Shared-instrument provisioning and the transform-sync mechanism are listed
  from the source file layout only; their behavior is covered by the late-join
  and S1/S4 smoke runs, not described mechanism by mechanism here.
- Repetition: each "Run" above is a single smoke run. The pilot and campaign
  provide repeated runs and the valid-execution proportion.
- Behavior under packet delay, loss or reordering; more than two participants.
- Photon Cloud behavior from different regions (the cloud runs used one
  machine and the best region at connect).
- Identity spoofing by a modified client.

## Where each row is checked

`LifecycleScenarioDriver` (cases HolderLeavesWhileOtherWaits, QueueAuthorityLeaves,
LateJoin, LeaveAndRejoin, NearConcurrentRequests), the W03 access probes,
`tests/QueueCore`, `tests/AuthorityLifecycle` and
`tools/analyze_session_invariants.py`; see `tests/README.md`.
