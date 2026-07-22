# Asset Provenance

## Publication Showcase

| Asset group | Source | Construction | Publication status |
|---|---|---|---|
| Generic shared instrument | This repository | Unity primitive meshes and project-authored materials | Included |
| Generic researcher avatar | This repository | Unity primitive meshes and project-authored materials | Included |
| Showcase room and props | This repository | Unity primitive meshes and built-in components | Included |
| Showcase UI | This repository | IMGUI and project-authored editor/runtime code | Included |
| Controlled-child marker | This repository | Project-authored C# component | Included |

No external model, texture, font, audio, logo, animation, or avatar media is
required by the publication showcase.

## Explicitly Excluded

- Original XR2Learn/V-Lab laboratory scenes and microscope models
- Original V-Lab textures, sounds, avatars, logos, and UI artwork
- Photon, FishNet, and Mirror examples, templates, prefabs, and media
- Unity package-cache contents
- Generated network prefabs containing vendor component serialization

## Replacement Contract

Reviewers may replace the child visuals of the generic instrument and avatar.
To preserve runtime behavior, retain the prefab root, stable object identifier,
`Controlled Turret` child name, interaction/participant anchors, and orchestration
definition components. Replacement media must have separately documented rights.
