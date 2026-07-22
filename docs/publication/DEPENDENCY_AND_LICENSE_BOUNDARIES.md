# Dependencies and License Boundaries

This document is an engineering publication audit, not legal advice. Final
redistribution decisions should be confirmed by the author, supervisor,
institution, and, where necessary, the relevant vendor or rights holder.

## Audited Versions

| Dependency | Audited version evidence | Distribution approach |
|---|---|---|
| Unity Editor | 6000.3.11f1 | State as the tested editor version |
| Photon PUN 2 | `PhotonNetwork.PunVersion` reports 2.55 | Do not copy the SDK into the public repository; document installation |
| Photon Fusion | Release history reports SDK 2.0.9, build 1566; local package metadata reports 1.1.0 | Record both identifiers and verify the authoritative installed SDK version before release |
| Unity Netcode for GameObjects | UPM 2.11.0 | Declare the package dependency through Unity Package Manager |
| FishNet | 4.6.22 | Custom license; require official acquisition and do not redistribute the SDK |
| Mirror | 96.0.1 | MIT-licensed upstream; still install separately to keep one consistent boundary |
| Newtonsoft JSON | UPM 3.2.2 | Declare only if required by the extracted package |
| Unity UI | UPM 2.0.0 | Declare as a Unity Package Manager dependency |

## Current Repository Ownership Risk

The repository root README and license describe the broader Beacon Application
2 / XR2Learn project. The existing license applies MIT terms to source code and
CC BY-NC-ND 4.0 terms to media and assets, with existing XR2Learn attribution.

Before publishing a derived standalone framework, obtain written confirmation
for:

- Authorship and ownership of the newly written orchestration source
- The right to publish modifications made inside the existing V-Lab repository
- The intended license for the new framework
- Any institutional intellectual-property requirements
- Any figures, logos, models, textures, audio, UI, or sample assets included

A new license file must not silently replace obligations inherited from the
original repository.

## Public Repository Boundary

### Include

- Backend-independent orchestration core
- Runtime contracts and resolver
- Shared synchronization abstractions
- Project configuration and runtime bootstrap
- Project-authored telemetry and benchmark implementation
- Project-authored editor setup and analysis tools
- Project-authored backend adapters, if vendor APIs may legally be referenced
- Protocol definitions and analysis code
- Rights-cleared documentation and diagrams
- A generic sample built from original or clearly redistributable assets
- Tests

### Exclude

- `Assets/Photon`
- `Assets/FishNet`
- `Assets/Mirror`
- Unity package-cache contents
- Vendor example projects and media
- Original V-Lab scenes, models, textures, sounds, and avatars unless rights are
  explicitly cleared
- App IDs, API keys, tokens, service URLs containing credentials, or user data
- Generated benchmark output inside the source repository
- Local build folders, `Library`, `Temp`, `Logs`, and editor state

Raw publication data should be deposited separately and referenced by DOI.

## SDK-Specific Notes

### Photon PUN 2 and Fusion

No clear permission to republish the complete SDK source was established from
the local files. The safe engineering boundary is to publish only the
project-authored adapters and require users to install the official SDKs.

Photon settings may contain application identifiers. All settings assets must be
reviewed and replaced with empty templates or setup instructions.

### Unity Netcode for GameObjects

Use the Unity Package Manager dependency rather than copying package source from
the package cache. Pin the exact tested version in the publication release.

### FishNet

FishNet describes its free edition as usable without personal/commercial fees,
but distributes it under a custom license rather than a standard OSI license.
The project must not imply that FishNet is relicensed by this repository. Require
official acquisition and acceptance, and do not vendor it in the publication.

### Mirror

Mirror is locally distributed under the MIT License. Its copyright and license
notice must be retained if any Mirror source is redistributed. Requiring users
to install the dependency separately still provides a cleaner package boundary.

## Compile-Time Packaging Status

Backend implementations directly import their SDK namespaces. The tracked core
therefore remains SDK-independent, while the full runtime is materialized only
after the complete audited SDK matrix is present.

The publication repository now provides:

- `com.vlab.orchestrator.core`
- `VLab.Orchestrator.Core.asmdef`
- `VLab.Orchestrator.Editor.asmdef`
- Five separate backend package boundaries
- A clean showcase runtime assembly and editor test assembly
- An idempotent generated-runtime installer and post-reload authoring step
- A cold-start five-backend validator

The core imports and compiles without any networking SDK. Adapter snapshots are
retained as `.cs.txt`, while authoritative project-authored source is copied to
an ignored top-level Unity folder once all five SDKs are loaded. This gives the
generated assembly access to every installed vendor assembly and code weaver
without committing provider files or fragile hard-coded assembly references.
The generated runtime has passed the repository's five-backend cold-start
validation. Two-client conformance and benchmark runs remain required for the
archived evaluation release.

## Required Release Files

- `LICENSE.txt` for the new framework
- `THIRD_PARTY_NOTICES.md`
- `CITATION.cff`
- `CHANGELOG.md`
- Exact Unity and dependency versions
- Installation instructions for each optional backend
- Credential and privacy statement
- Asset provenance table
- DOI links for software release and evaluation data

## Go/No-Go Checklist

Do not make the publication repository public until:

- Framework ownership is confirmed in writing.
- Every included asset has a known source and compatible license.
- Vendor SDK code is absent unless redistribution is explicitly allowed.
- Generated runtime assets containing vendor component serialization are absent.
- Credentials and application identifiers are removed.
- A clean-room clone builds from the documented dependency instructions.
- The chosen framework license and third-party notices have been reviewed.
