# Changelog

## 1.0.0 - 2026-08-01

- Published the rights-cleared SoftwareX research-software artifact.
- Added the SDK-independent showcase, five audited backend integrations, and
  deterministic validity-aware benchmark workflow.
- Added safe backend SDK installation, uninstall, compatibility repair, and
  generated-runtime activation for Unity `6000.3.11f1`.
- Added retained Windows-to-Windows evaluation data, anomaly-validation runs,
  package/provenance documentation, and an illustrated evaluator guide.
- Excluded vendor SDK payloads, Photon credentials, generated runtime assets,
  private V-Lab media, and internal manuscript-review artifacts.

## Protocol 1.5 - Authority timing validation

- Cancelled abandoned Unity Netcode authority-transfer measurements when local
  logical access is revoked before physical ownership completes.
- Prevented later ownership completions from inheriting stale request timestamps
  from a previous benchmark cycle.
- Versioned corrected exports as protocol 1.5 so they cannot be silently pooled
  with the protocol 1.4 candidate matrix.

## Unreleased - 2026-07-12

- Versioned the final validity correction as benchmark protocol `1.4`: closed-loop
  runs require the guaranteed `cycles - 1` inter-cycle physical handoffs while
  per-phase assertions continue to validate every logical holder transition.
- Added native server spawning for configured FishNet and Mirror shared objects,
  preventing unspawned local clones from producing false benchmark evidence.
- Repaired FishNet runtime-root authoring so its spawnable-prefab collection is
  always assigned and reserialized before play or build.
- Added build-time Photon credential checks and a Fusion standalone-profiler
  compatibility gate so unusable publication executables fail before building.
- Added all-backend network-object replication-readiness preflight assertions
  and removed repeated FishNet/Mirror avatar log flooding.
- Successful exports now remove their staging copies after the completion
  manifest is published; failed staging data remains available for diagnosis.
- Removed all microscope model, texture, material, and case-study scene assets
  from the standalone publication showcase.
- Added a tabbed backend setup workflow with red/green SDK readiness states.
- Added a pinned one-click Unity Netcode `2.11.0` installation action.
- Moved setup to the top-level `VLab Orchestrator > Setup` menu and moved
  readiness status to the top of the window.
- Replaced multi-button backend controls with red/green SDK cards and one
  primary action per backend.
- Removed duplicated prefab, policy, benchmark, and developer controls from the
  installer and added one project-configuration handoff.
- Added official setup and Photon AppId links for SDKs that require explicit
  package import or credentials.
- Fixed package-request reloads leaving all backend actions disabled.
- Routed every dependency action to an official registry, Asset Store page, or
  vendor SDK download without redistributing third-party packages.
- Added automatic materialization of the complete project-authored framework
  and all five adapters after the audited SDK matrix is available.
- Added generated generic avatar, shared-instrument, queue, and runtime-root
  authoring without copying original V-Lab media or vendor SDK payloads.
- Added a freshness-aware five-backend validator and Windows publication build.
- Added a serialized Mirror/KCP runtime root to eliminate transport-order errors.
- Added asset provenance, third-party boundaries, and stricter generated/vendor
  ignore rules.
- Updated backend installation and benchmark documentation.
- Corrected the closed-loop authority assertion to require one physical backend
  handoff per completed cycle while retaining per-phase logical assertions.
- Versioned that validity-rule correction as benchmark protocol `1.3`; protocol
  `1.2` exports remain immutable diagnostic evidence and are not mixed with it.
- Added deterministic PUN room-view registration for locally provisioned shared
  instruments so ownership and controlled-child rotation replicate consistently.
- Removed FishNet demo HUD/player spawning and persistent-manager behavior from
  the generated runtime root.
- Prevented Mirror prefab reauthoring after a player build has started.
- Added a reproducible Fusion 2.0.9 Unity 6 profiler repair that excludes its
  optional native counters from standalone players while preserving framework
  CSV performance sampling.
- Made Photon App IDs an explicit local readiness requirement without storing or
  redistributing evaluator credentials.

## 0.3.0-local - 2026-07-03

- Added an SDK-independent Unity Package Manager core package.
- Moved the real FCFS queue, policy, and state models out of the showcase.
- Added a reusable project configuration asset and Unity research workspace.
- Added five optional backend package boundaries with SDK-readiness checks.
- Preserved 65 production adapter files as inspectable reference source.
- Reconnected and validated the clean showcase as a package consumer.
- Passed six edit-mode tests, a Windows build, and a runtime smoke test.

## 0.2.0-local - 2026-07-03

- Converted runtime-generated geometry into editable Unity scene objects.
- Added reusable shared-instrument and participant prefabs.
- Added six editable material assets.
- Added stable backend-neutral participant and shared-instrument definitions.
- Added a deterministic scene rebuild command.
- Revalidated the authored scene, tests, Windows build, and runtime capture.

## 0.1.0-local - 2026-07-03

- Created an independent SoftwareX-oriented repository structure.
- Added the backend-independent orchestration core.
- Added a dependency-free Unity showcase using a generic primitive instrument.
- Added deterministic two-user request, queue, handoff, rotation, and export.
- Added the five-backend source snapshot and installation boundaries.
- Added publication architecture and reproducibility documentation.

This version is a private preparation snapshot, not a public release.
