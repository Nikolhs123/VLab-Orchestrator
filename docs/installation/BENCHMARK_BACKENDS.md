# Benchmark Backend Setup

This checklist describes what is required to run transport-level benchmark
scenarios in the standalone VLab Orchestrator project.

## Baseline

1. Open `repo/src/showcase/UnityProject` with Unity `6000.3.11f1`.
2. Run `VLab Orchestrator > Rebuild Editable Showcase Scene`.
3. Keep benchmark hardware fixed: same machine, same build settings, same graphics quality, same resolution, wired/local networking where possible.
4. Use two non-development Windows players for final measurements. Disable
   **Development Build**, **Script Debugging**, **Autoconnect Profiler**, and
   **Deep Profiling**. Use Editor plus one player only for debugging.

## Backend Setup

The public repository stores backend adapters as reference source so the project
can open without redistributing vendor SDKs. Once all five audited SDKs are
installed, the framework automatically materializes the project-authored runtime
adapters; no source promotion or assembly editing is required.

Open `VLab Orchestrator > Setup`, select a backend tab, and use the single
action in its card. Red means its SDK is missing; green means dependency setup
is complete. Use **Open Project Configuration** for all non-installation
settings.

Install the audited five-backend matrix in the same project, then select and
benchmark one backend at a time. This keeps the project artifact identical
between conditions while the active backend remains an explicit experimental
factor.

### Mirror

Use **Open SDK Setup** and install Mirror from its official stable distribution.
Use KCP transport for local benchmarks unless the scenario explicitly tests a
different transport. After import, return to **VLab Orchestrator > Setup** and
select Mirror. Runtime source materialization and prefab authoring are automatic.

### Unity Netcode for GameObjects

Use **Install SDK** in the workspace. It installs the audited
`com.unity.netcode.gameobjects@2.11.0`; that package declares Unity Transport
`2.6.0` as a dependency. After import, return to **VLab Orchestrator > Setup**
and select Unity Netcode. Runtime source materialization and prefab authoring are
automatic.

### FishNet

Use **Open SDK Setup** and install Fish-Networking from its official stable
Unity package distribution. Use
Tugboat for local two-client benchmarks. After import, return to
**VLab Orchestrator > Setup** and select FishNet. Setup binds the generated
runtime manager to this project's `Assets/DefaultPrefabObjects.asset`; the
selected-build validator rejects stale or missing FishNet collections before a
build starts. FishNet's reserialize utility is needed only if an evaluator adds
new scene `NetworkObject` instances outside the supplied showcase workflow.

### Photon PUN 2

Use **Open SDK Setup**, install Photon PUN 2 from its official distribution,
create a Photon Realtime application, and configure its App ID locally in
`PhotonServerSettings`. For reproducible benchmarks use the same region, room
settings, and send/serialization rates across runs. After import, return to
**VLab Orchestrator > Setup** and select PUN 2. Runtime source materialization
and prefab authoring are automatic.

### Photon Fusion

Use **Open SDK Setup** and install Photon Fusion from the official SDK package.
Create a Photon Fusion application, configure its App ID locally, and use the
same topology for every replicate. The PUN and Fusion App IDs are evaluator-owned
credentials and must never be committed. After import, return to
**VLab Orchestrator > Setup** and select Fusion. Runtime source materialization
and prefab authoring are automatic.

## Measurement Protocol

Run each backend with the same scenario matrix, participant count, transfer
cycles, controlled-child motion, warmup, hold time, cooldown, and export rules.
For paper-quality results, run at least ten valid replicates per backend and
scenario, export raw CSV files, and keep failed runs in a separate audit folder
instead of deleting them.

Do not mix Editor-player and player-player measurements in the same final table.
Editor-player runs are useful for debugging, but player-player runs are the
publishable benchmark condition.

Use the following staged gate to avoid wasting benchmark time:

1. For each selected backend, run **VLab Orchestrator > Validate Selected Build Configuration**, then create a fresh non-development Windows build. Never reuse an executable built before a source or authoring fix.
2. Run only S2 with two copies of that executable. This short closed-loop pilot exercises connection, native shared-object spawn, contention, remote authority handoff, controlled-child replication, and transactional export. Continue only when the host reports `VALID EXPORT SAVED` and the export uses protocol `1.4`.
3. After all five S2 pilots pass, run one two-player pilot for every backend and scenario. A protocol-valid pilot must complete every cycle, have zero failed commands and zero failed validity assertions, and contain `export_manifest.csv`. A failed final-rotation outcome is retained as a measured reliability result rather than silently discarded, but it must be investigated before final collection.
4. Begin the ten publishable replicates per backend and scenario only after the complete 5 x 5 pilot matrix is stable.

Fixing a backend invalidates that backend's earlier pilot data and any final runs
made with an older executable. It does not require repeating already-passing
pilots for unchanged backends.
