# Prefab and Synchronization Contracts

## Visual Sources

The publication showcase contains:

- `GenericSharedInstrument.prefab`
- `GenericResearcherAvatar.prefab`
- Six editable material assets

These are rights-cleared visual sources built from Unity primitives. Their
geometry, scale, materials, and child visuals may be edited before runtime.

## Stable Shared-Instrument Contract

`ShowcaseSharedInstrumentDefinition` records:

- Stable object ID: `generic_instrument_01`
- Controlled child: `Controlled Turret`
- Access-state indicator renderer

The visual object may change, but the definition must continue to reference the
part that the scenario manipulates and validates.

## Stable Participant Contract

Each scene avatar instance has a `ShowcaseParticipantDefinition` containing:

- Stable participant ID
- Display name
- Visual renderers

The same `GenericResearcherAvatar.prefab` is reused for both participants, with
scene-level identity and material overrides.

## What Is Synchronized

The dependency-free showcase is a local deterministic simulation. It proves the
queue and interaction contract but does not transmit packets.

In a backend-enabled sample, each SDK still requires its own network identity
and synchronization components:

| Backend | Typical network root |
|---|---|
| PUN 2 | `PhotonView` root |
| Fusion | `NetworkObject` root |
| Unity Netcode | `NetworkObject` root |
| FishNet | FishNet `NetworkObject` root |
| Mirror | `NetworkIdentity` root |

The preferred structure is:

```text
Backend-specific network root
|-- backend ownership and transform components
`-- Generic visual prefab
    `-- backend-neutral participant or instrument definition
```

This avoids maintaining five copies of the artwork. The orchestration setup
process will author or validate the backend-specific root while preserving one
visual source.

## Relationship to the Existing V-Lab

The production `NetworkAvatar` and microscope remain unchanged in the private
V-Lab project. The generic publication prefabs do not replace or modify them.

Later backend conformance work will apply the same adapter contracts to the
generic prefabs. The original microscope remains the scientific case study, and
the generic instrument remains the legally redistributable reproducibility
sample.

## Safe Customization

Safe changes:

- Replace primitive meshes with original or appropriately licensed models
- Adjust scale, positions, colors, and materials
- Add decorative child objects
- Change participant visuals
- Add animations that do not replace the controlled transform

Changes requiring rebinding:

- Rename or replace `Controlled Turret`
- Replace the access indicator renderer
- Remove either definition component
- Change stable IDs
- Reparent objects beneath a backend-specific network identity
