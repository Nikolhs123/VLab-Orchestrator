# Access Control And Participant Identity - Revision Protocol 1.6

This describes the revision source (tag `freeze-protocol-1.6-b`). It records what
the code enforces, where, and what it does not enforce, and the result of the
repeated two-process probe runs below.

## Layers

1. **Logical allocation.** The FCFS queue runs on one authoritative peer
   (Netcode/Mirror/FishNet server or host, PUN2 master client, Fusion queue
   state authority). It decides the logical holder and replicates it.
2. **Native authority.** The networking SDK's ownership/state authority decides
   which process may write replicated transforms of the instrument.
3. **Local interaction gate.** Each participant's gate enables manipulation
   with logical access and, where the gated object carries its own network
   identity, local native authority.

Native authority alone does not enable the local gate. Correction from the W03
probes (below): on Netcode, Mirror and FishNet the gate on the controlled child
permitted interaction to a logical holder that did not yet have native
authority; the gates check the network identity on the gated GameObject itself,
and the authored controlled child has none (inferred from the gate code, consistent
with the probe records). On PUN2 and Fusion the gate did not permit it.

## Participant Identity Binding

Participant IDs are derived from transport identity: `Client_<id>` (Netcode,
Mirror, FishNet), `Actor_<n>` (PUN2), `Player_<raw>` (Fusion). The queue RPCs
for begin-session, request, release and heartbeat still carry a claimed
participant ID, but the authoritative peer now accepts the call only if that ID
equals the identity of the observed sender:

| Backend | Sender identity used |
| --- | --- |
| Netcode | `ServerRpcParams.Receive.SenderClientId` |
| Mirror | Command `NetworkConnectionToClient sender` (host calls are local) |
| FishNet | ServerRpc `NetworkConnection caller` (host calls are local) |
| PUN2 | `PhotonMessageInfo.Sender` |
| Fusion | `RpcInfo.Source`; a local invocation without a source is the local player |

Mismatches are rejected and logged as `QueueIdentityRejected`. Before this
change one participant could request, release or heartbeat on behalf of another.
The finalization force-release operation remains callable by any participant; it
is an experiment-controller operation and is part of the trusted setup.

## Native Authority Gating

**Netcode, Mirror, FishNet (server-authoritative).** For a queue-controlled
object (an object carrying the backend's microscope access controller), while
the queue runtime is active:

- the server grants native ownership only when the request sender is the
  current logical holder (`NativeAuthorityRequestRejected` otherwise); the
  Netcode relay now uses the RPC sender instead of a client-supplied client ID;
- each frame, the server removes client ownership that does not match the
  current holder (`NativeAuthorityRevokedByServer`).

A delayed ownership request from a former holder is therefore refused by the
server rather than reassigning authority. Unowned objects remain under
server/host authority: in host topology the host process can write them while
it is not the holder. Its local gate stays closed, but this is a trust
assumption on the host, not a network-level guarantee. Standalone shared objects
without an access controller are not queue-gated.

**PUN2 and Fusion (no server arbitration of ownership).** Instruments use
`OwnershipOption.Takeover` (PUN2) and `AllowStateAuthorityOverride` (Fusion
Shared mode). Any participant can take native authority without approval. Access
control is cooperative: correct clients only request authority while holding
logical access and release on revocation. A modified or delayed client is not
prevented by the network layer. Violations are not prevented but are observable
in `native_authority_observations.csv` (native owner differing from the logical
holder on each participant).

## Removal And Recovery

On revocation, every adapter cancels its pending authority measurement, and the
Netcode/Mirror/FishNet adapters ask the server to release native ownership; the
server-side enforcement above also removes it if the client does not. PUN2 and
Fusion rely on the next holder's takeover.

`SessionQueueRuntimePUN2` previously hid `MonoBehaviourPunCallbacks.OnEnable`/
`OnDisable`, so its `OnLeftRoom`, `OnDisconnected`, `OnJoinedRoom` and
`OnRoomPropertiesUpdate` overrides were never registered and never ran; it now
calls the base methods. `SessionQueueRuntimeNetcode.OnDestroy` now overrides and
calls `NetworkBehaviour.OnDestroy`. Historical PUN2 runs were collected without
those callbacks.

## Access-Control Probes (W03)

With `-vlabAccessProbes` (or `automatedScenarioAccessControlProbes`) the S1
closed-loop scenario runs under a separate scenario id (`..._W03AccessProbes`)
and participants write a probe rotation directly to their local replica of the
controlled object, bypassing the interaction gate, in four access states:
before the first grant (both), during the other participant's hold (non-holder),
on the first frame after release (former holder, which may still hold native
authority), and on the first frame of becoming logical holder (before native
authority). Each probe has a unique signature (User1 pitch, User2 roll, unique
angle; legitimate manipulation changes only yaw). The prober records its holder,
SDK-read native authority and gate decision, and 1 s later whether the probe
remained locally (then removes it locally). Propagation is detected in the other
participant's 10 Hz transform samples by the exact signature, joined by phase
index without comparing clocks (`tools/analyze_access_probes.py`).

Observed in 5 two-process attempts per backend (Campaign B players, same machine,
19 probes per attempt, 475 probes; `tools/analyze_probe_runs.py`). An earlier
single smoke run per backend gave the same counts per attempt:

| Condition (probes over 5 attempts) | Netcode | Mirror | FishNet | PUN2 | Fusion |
| --- | --- | --- | --- | --- | --- |
| Non-holder during other's hold: propagated | 0/30 | 0/30 | 0/30 | 0/30 | 0/30 |
| Former holder after release: propagated | 0/30 | 0/30 | 0/30 | 0/30 | 0/30 |
| (of which the prober still had native authority) | 20 | 18 | 20 | 22 | 30 |
| Before first grant: propagated | 0/10 | 0/10 | 0/10 | 5/10 | 0/10 |
| Logical holder before native authority: published after acquisition | 5/25 | 5/25 | 5/25 | 0/25 | 5/25 |
| Gate permitted the write of a logical holder without native authority | 25/25 | 25/25 | 25/25 | 0/25 | 0/25 |
| Non-holder write remained on prober's own replica after 1 s | 30/30 | 30/30 | 30/30 | 0/30 | 30/30 |

Interpretation, limited to these runs: no participant without logical access
changed the shared state of the other participant on any backend, including
110 former-holder probes made while the writer still had native authority; the
exception is the PUN2 master client, which propagated a write made before the
first grant in 5 of 10 probes (once per attempt) while owning the unowned scene
object without logical access (consistent with the cooperative model above). A
logical holder's write made before native authority arrived was published once
authority arrived in the first cycle of each attempt (5 of 25 probes) on Netcode,
Mirror, FishNet and Fusion. On every backend except PUN2, an unauthorized local
write was not corrected on the writer's own replica until the next replicated
change, so that participant's view diverged from the shared state.

Evidence: probe CSVs and summary in the revision materials (`campaign_B/probes`).
These are counts of 5 repeated attempts per backend, not rates with an interval.

## Not Yet Demonstrated

- Identity spoofing attempts by a modified client.
- Behavior under real packet delay or reordering.
- Any guarantee for PUN2/Fusion beyond detection.
