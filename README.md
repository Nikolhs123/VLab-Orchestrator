# VLab Orchestrator

VLab Orchestrator is a Unity research-software framework for applying one
session, shared-access, authority-transfer, synchronization, telemetry, and
evaluation model across heterogeneous multiplayer networking backends.

This repository is a local publication-preparation artifact. It is intentionally
separate from the complete XR2Learn V-Lab development project.

![Dependency-free VLab Orchestrator showcase](docs/images/showcase.png)

## Open the Rights-Cleared Showcase

The included showcase uses the real backend-independent FCFS queue core and
editable instrument and participant prefabs made entirely from Unity primitives.
It contains no original microscope, laboratory scene, avatar, texture, audio,
or third-party SDK payload. It can be used in two modes:

- **Dependency-free mode:** run the local deterministic FCFS demonstration.
- **Five-backend mode:** install the audited SDK matrix through the setup window;
  the project then materializes the project-authored adapters and network-ready
  generic prefabs automatically.

1. Install Unity `6000.3.11f1`.
2. In Unity Hub, select **Add project from disk**.
3. Select `repo/src/showcase/UnityProject`.
4. Open `Assets/Scenes/Showcase.unity`.
5. Open **VLab Orchestrator > Setup** to inspect or install the networking matrix.
6. Adjust the scene, materials, or prefabs if desired.
7. Press Play.
8. Request and release the instrument as Researcher A or Researcher B, rotate
   the controlled turret, or run the deterministic three-cycle scenario.
9. Use **Export Demo CSV** to create a small reproducible result file.

Editable assets:

- `Assets/VLabOrchestrator/Prefabs/GenericSharedInstrument.prefab`
- `Assets/VLabOrchestrator/Prefabs/GenericResearcherAvatar.prefab`
- `Assets/VLabOrchestrator/Materials`

The Unity menu **VLab Orchestrator > Rebuild Editable Showcase Scene** restores
the known-good sample. It overwrites the showcase scene and generated prefab
assets, so use it as a reset command rather than after intentional customization.

The showcase demonstrates:

- Real FCFS request, queue, grant, and release decisions
- Two simulated participants
- Holder-only manipulation of a shared instrument
- Deterministic authority handoffs
- Controlled child-object rotation
- Live request, grant, transfer, and rotation metrics
- Human-readable event history
- CSV result export

The local demo does not simulate transport or claim backend-performance
equivalence. Paper results must be produced in five-backend mode using two
standalone clients and the fixed benchmark protocol.

## Repository Layout

```text
repo/src/
|-- framework/           Project-authored orchestration source snapshot
|-- packages/            Installable UPM core and five backend boundaries
|-- showcase/            Dependency-free playable Unity project
`-- backend-adapters/    Adapter source grouped by optional networking SDK

docs/
|-- architecture/
|-- installation/
`-- publication/

tests/                   Publication test specifications and future automation
```

SoftwareX requires the project source beneath `repo/src`; this repository keeps
that boundary explicit.

## Package Architecture

The showcase consumes the framework through Unity Package Manager rather than
duplicating orchestration code under `Assets`. The package graph contains:

- `com.vlab.orchestrator.core`, which compiles without a networking SDK
- `com.vlab.orchestrator.backend.pun2`
- `com.vlab.orchestrator.backend.fusion`
- `com.vlab.orchestrator.backend.netcode`
- `com.vlab.orchestrator.backend.fishnet`
- `com.vlab.orchestrator.backend.mirror`

The core package contains the FCFS queue, state models, configuration asset,
benchmark settings, setup window, SDK compatibility checks, and reproducible
runtime installer. The five package boundaries retain inspectable `.cs.txt`
copies, while authoritative project-authored source is stored under
`repo/src/framework` and `repo/src/backend-adapters`. Once all audited SDKs are
present, that source is copied to the ignored
`Assets/GeneratedSoftwareXRuntime` workspace and compiled together. Generated
network prefabs and the production configuration are likewise ignored and can
be recreated at any time with **VLab Orchestrator > Finalize SoftwareX Runtime**.

See
[`docs/architecture/PACKAGE_ARCHITECTURE.md`](docs/architecture/PACKAGE_ARCHITECTURE.md)
for the dependency and promotion model.

## Supported Production Backends

The complete integration currently supports:

- Photon PUN 2
- Photon Fusion
- Unity Netcode for GameObjects
- FishNet
- Mirror

Third-party SDK source is not redistributed here. Each backend adapter is
project-authored source and requires the corresponding official SDK before it
can be compiled and exercised. See
[`docs/installation/BACKENDS.md`](docs/installation/BACKENDS.md).

The tracked generic prefabs are visual and semantic sources, not vendor network
identities. Five-backend activation creates ignored backend-authored copies while
preserving the same stable object and controlled-child contracts.
See [`docs/architecture/PREFAB_CONTRACTS.md`](docs/architecture/PREFAB_CONTRACTS.md).

## Current Status

| Area | Status |
|---|---|
| Dependency-free playable showcase | Included |
| Editable generic scene and prefabs | Included |
| Installable SDK-independent core package | Included and tested |
| Unity research workspace and configuration asset | Included |
| Five optional backend package boundaries | Included |
| Five adapter source implementations | Included for inspection |
| Generic rights-cleared instrument | Editable Unity prefab |
| Original V-Lab case study | Maintained in the private development project |
| Executable five-backend runtime | Reproducibly materialized after SDK installation |
| Core Unity edit-mode tests | Included |
| Five-backend cold-start validation | Included and passing locally |
| Windows publication build | Produced by repository build command |
| Public open-source license | Pending ownership approval |
| Public GitHub release | Not yet created |

## Publication Boundary

The original V-Lab scene remains an important case study, but it is not required
to understand or run this repository. This separation avoids redistributing
models, media, vendor packages, credentials, or unrelated application code.

The public release will contain only project-authored framework code,
rights-cleared samples, tests, documentation, and protocol definitions.

## Documentation

- [Architecture](docs/architecture/OVERVIEW.md)
- [Unity package architecture](docs/architecture/PACKAGE_ARCHITECTURE.md)
- [Prefab and synchronization contracts](docs/architecture/PREFAB_CONTRACTS.md)
- [Backend installation boundaries](docs/installation/BACKENDS.md)
- [SoftwareX submission plan](docs/publication/SOFTWAREX_SUBMISSION_PLAN.md)
- [Research protocol](docs/publication/BENCHMARK_PROTOCOL_AND_REPRODUCIBILITY.md)
- [Asset provenance](ASSET_PROVENANCE.md)
- [Third-party notices](THIRD_PARTY_NOTICES.md)

## License

The repository is not yet licensed for public redistribution. `LICENSE.txt`
contains a temporary private-preparation notice. It must be replaced by the
institutionally approved OSI license before publication. Vendor SDKs are never
covered by that future framework license and are excluded from version control.
