# Five-Backend Compatibility Baseline

This document records the reproducible Unity and SDK matrix used by the
SoftwareX artifact. It is a compatibility lock, not a claim that arbitrary
future SDK versions can be overlaid safely.

## Audited Matrix

| Component | Pinned version |
|---|---:|
| Unity Editor | 6000.3.11f1 |
| Photon PUN 2 | 2.55 |
| Photon Fusion | 2.0.9, build 1566 |
| Netcode for GameObjects | 2.11.0 |
| Unity Transport | 2.6.0, resolved by Netcode |
| FishNet | 4.6.22 |
| Mirror | 96.0.1 |
| AI Navigation | 2.0.11 |
| Mono.Cecil | 1.11.6 |
| Newtonsoft JSON | 3.2.2 |
| UGUI | 2.0.0 |

All five networking SDKs are installed concurrently. Experiments activate one
backend at a time through `OrchestrationProjectConfig`, so SDK installation is
held constant across backend conditions.

## Unity Baseline

The showcase manifest declares Unity's AI, Terrain, Animation, Audio, Physics,
Physics 2D, Particle System, UI, UI Toolkit, Unity Web Request, image conversion,
asset bundle, video, XR, and related built-in modules. This intentionally
matches the broad module baseline of the development project.

These modules are required before importing Asset Store SDKs because Unity
compiles vendor examples, migration tools, inspectors, and optional components
alongside runtime code. A module can therefore be an import-time dependency
even when the VLab Orchestrator runtime does not call it directly.

`allowUnsafeCode` is enabled because the audited networking/code-generation
toolchain contains compatible unsafe-code paths. API compatibility remains
`.NET Standard 2.1` under Unity 6. Asset serialization is set to **Force Text**,
as required by Fusion and by the repository's reviewable prefab/config workflow.

## Known Cross-SDK Constraints

### PUN 2 and Fusion

PUN 2 and Fusion both distribute Photon Realtime source. Their source generation
must match. PUN `2.55` and Fusion `2.0.9 build 1566` compile together in this
artifact. Fusion `2.1.x` introduces a different split Realtime generation and
must not be overlaid on this matrix.

PUN Realtime and Fusion require separate evaluator-owned Photon applications.
The setup window keeps each card red until the corresponding local App ID is
present; credential assets are excluded from Git.

Fusion `2.0.9 build 1566` also initializes optional native profiler counters in
standalone players. Their metadata call crashes natively on Unity
`6000.3.11f1`, including when PUN 2, Netcode, FishNet, or Mirror is selected,
because the Fusion assembly remains part of the all-five build. The
compatibility postprocessor keeps the counters in the Editor and compiles them
out of standalone players. Framework CSV performance sampling is unaffected.
Final benchmark players remain non-development builds.

### FishNet and Mirror

FishNet `4.6.22` runtime is compatible with Mirror `96.0.1`, but FishNet's
optional Mirror migration helper references the removed Mirror type
`NetworkTransformChild`. `BackendSdkCompatibilityPostprocessor` patches only
that editor migration helper to discover its optional `target` field through
reflection. No network runtime, serializer, transport, or benchmark path is
changed.

FishNet's general installation documentation warns that some FishNet and
Netcode for GameObjects releases can clash in their code-generation files. This
artifact therefore does not claim arbitrary-version coexistence: the exact
FishNet `4.6.22`, Netcode `2.11.0`, and Mono.Cecil `1.11.6` matrix must pass the
all-five compile before a benchmark is accepted.

### Vendor Acquisition

The repository does not redistribute Asset Store or Photon SDK packages. The
setup window imports only locally acquired packages whose SHA-256 matches the
audited artifacts. Provider login, license acceptance, and Photon App IDs stay
outside the repository.

## Readiness Criteria

A backend tab is green only when all of the following are true:

1. Its VLab Orchestrator adapter package is installed.
2. Its official SDK type is loaded.
3. Its detected SDK version exactly matches the audited matrix.
4. Unity reports no script compilation failure.
5. Any required local Photon App ID is configured.

After any matrix change, rerun the all-five batch compile, adapter conformance
tests, two-player smoke test, and fixed benchmark scenarios before updating this
document.
