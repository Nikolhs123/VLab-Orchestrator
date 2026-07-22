# Unity Package Architecture

## Package Graph

```text
Dependency-free Unity showcase
                |
                v
 com.vlab.orchestrator.core
   | queue and FCFS policy
   | user and instrument state
   | project configuration
   | benchmark protocol
   ` research workspace

Optional backend boundaries
   |-- com.vlab.orchestrator.backend.pun2
   |-- com.vlab.orchestrator.backend.fusion
   |-- com.vlab.orchestrator.backend.netcode
   |-- com.vlab.orchestrator.backend.fishnet
   `-- com.vlab.orchestrator.backend.mirror
```

No networking SDK is referenced by the core assembly. This lets reviewers open
the repository, inspect the orchestration policy, edit prefabs, run the
deterministic sample, and execute core tests without proprietary credentials or
vendor packages.

## Setup Window

Open **VLab Orchestrator > Setup**. The window exposes:

- The selected backend
- Package-installation and SDK-detection status
- Audited SDK versions
- Reusable participant and shared-instrument prefabs
- Stable object and participant identifiers
- FCFS policy settings
- The deterministic benchmark protocol
- Evaluation weights and configuration validation

The asset is stored at:

`Assets/Resources/VLabOrchestrator/OrchestrationProjectConfig.asset`

## Source Surfaces

The repository intentionally keeps four distinct source surfaces:

1. `framework` and `backend-adapters` are an extracted, inspectable snapshot of
   the production implementation used in the V-Lab case study.
2. `packages` is the clean Unity Package Manager distribution and setup layer.
3. `showcase` is a rights-cleared consumer project with editable generic
   prefabs and an SDK-independent local mode.
4. `Assets/GeneratedSoftwareXRuntime` is an ignored, reproducible integration
   workspace created only after all audited SDKs are installed.

This separation prevents the paper artifact from silently depending on the
original laboratory scene or private vendor configuration.

## Backend Activation

Each backend package contains an inspectable adapter snapshot under
`ReferenceSource` with a `.cs.txt` suffix. This is deliberate:

- Unity can import every package when no networking SDK is installed.
- Vendor SDK source and credentials are not redistributed.
- Reviewers can inspect the complete adapter design before accepting vendor terms.
- Missing vendor types cannot break the dependency-free sample.

The authoritative source in `repo/src/framework` and
`repo/src/backend-adapters` is executable after activation. The core installer:

1. Verifies that all five audited SDK types are loaded.
2. Copies only project-authored `.cs` files to the ignored top-level generated
   workspace, excluding legacy V-Lab-only state adapters.
3. Compiles the runtime in `Assembly-CSharp`, where all five official SDK
   assemblies and their IL post-processors are available.
4. Creates ignored network-ready copies of the generic avatar and instrument.
5. Creates backend roots, queue prefabs, and the production runtime config.
6. Reauthors those prefabs whenever the selected backend changes.

The public Git history therefore contains the framework and adapters but never
contains Photon, FishNet, Mirror, generated vendor identities, credentials, or
provider examples. Publication measurements must still come from two built
clients using the fixed protocol, not from the deterministic local showcase.

## Prefab Replacement

The generic instrument and participant prefabs are stable semantic contracts,
not final artwork. Rights-cleared models can replace their child visuals while
preserving:

- The root definition component
- The stable object or participant identifier
- The controlled-child reference
- The interaction anchor
- The participant label anchor

Backend-specific network identities belong on adapter-managed roots after the
corresponding SDK is activated. The visual prefab remains reusable across all
five backends.
