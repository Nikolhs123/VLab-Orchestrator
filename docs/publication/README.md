# VLab Orchestrator Publication Evidence Package

This directory records the implementation and evaluation evidence for a
SoftwareX submission about the VLab Orchestrator.

## Snapshot

| Item | Value |
|---|---|
| Audit date | 2026-07-22 |
| Unity version | 6000.3.11f1 |
| Working branch | `main` |
| Nearest clean commit | `e31802417bc2c7a3d196004c93dfcc521df3afee` |
| Framework location | `repo/src/framework` and `repo/src/backend-adapters` |
| Implemented backends | Photon PUN 2, Photon Fusion, Unity Netcode for GameObjects, FishNet, Mirror |
| Benchmark protocol | 1.4 |

The audit was performed on a modified working tree. The commit above is only the
nearest clean snapshot; protocol 1.4 and its backend stabilization changes still
require a clean publication commit and version tag before final data collection.

## Evidence Status

| Area | Current status |
|---|---|
| Backend-independent orchestration core | Implemented and code-audited |
| Five backend adapters | Implemented and code-audited |
| Shared-object access and synchronization | Implemented and exercised in the V-Lab |
| Runtime bootstrap and editor configuration | Implemented and code-audited |
| Automated benchmark scenarios | Implemented; five protocol presets exist |
| Pilot benchmark coverage | Protocol 1.1-1.3 diagnostic runs retained; protocol 1.4 acceptance matrix pending |
| Statistical evidence for final paper | Not complete; replicated final runs are still required |
| Automated tests | Core/preset edit-mode tests and build-time backend asset validation implemented |
| Standalone package | Extracted into this rights-cleared publication repository |
| Third-party redistribution rights | Vendor SDKs are excluded from redistribution and installed separately |
| AI orchestration | Experimental code exists, but it was disabled in the audited evaluation |

## Documents

- [Implementation Evidence](IMPLEMENTATION_EVIDENCE.md)
- [Architecture](ARCHITECTURE.md)
- [Dependencies and License Boundaries](DEPENDENCY_AND_LICENSE_BOUNDARIES.md)
- [Benchmark Protocol and Reproducibility](BENCHMARK_PROTOCOL_AND_REPRODUCIBILITY.md)
- [Publication Repository Plan](PUBLICATION_REPOSITORY_PLAN.md)
- [SoftwareX Submission Plan](SOFTWAREX_SUBMISSION_PLAN.md)

## Defensible Contribution

The strongest current contribution is a Unity orchestration layer that applies
one shared session, access-allocation, object-binding, telemetry, and benchmark
model across five heterogeneous multiplayer networking backends. It preserves
the existing V-Lab interaction scripts and places backend-specific networking
behind a common runtime contract.

The paper should present the framework and its reproducible evaluation protocol
as the primary contribution. It should not claim that one backend is universally
best, that AI has been evaluated, or that the current single-run pilot establishes
statistical superiority.

## Fast, High-Quality Route

1. Confirm that the framework code and any included demonstration assets may be
   released under the intended license.
2. Extract a backend-neutral Unity package and keep third-party SDK source out of
   the publication repository.
3. Add a generic, rights-cleared sample scene and automated edit/play mode tests.
4. Freeze the protocol, versions, machine configuration, and analysis scripts.
5. Use pilot variance to select a justified replicate count, then execute the
   randomized final experiment.
6. Deposit raw data, processed tables, protocol definitions, and analysis code
   under a DOI.
7. Write the SoftwareX manuscript from the frozen release and deposited evidence.

AI should remain outside the first publication unless a separate research
question, baseline, evaluation design, and reproducible provider configuration
can be completed without delaying the validated framework contribution.
