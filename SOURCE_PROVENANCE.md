# Source Provenance

## Extraction

| Item | Value |
|---|---|
| Extraction date | 2026-07-03 |
| Development repository | `https://github.com/Nikolhs123/PhDThesisOarchestrationFramework.git` |
| Development branch | `vlab-PhDThesis_NickMarmaras_OrchestrationFramework` |
| Nearest committed baseline | `dd6186dc6938b20ad722f202da6f6f97645efcb4` |
| Unity version | `6000.3.11f1` |

The framework snapshot was extracted from the active development working tree,
which contained changes newer than the baseline commit. This publication
repository therefore becomes the authoritative history for the extracted
artifact once its initial commit is created.

## Included

- Project-authored orchestration source
- Five project-authored backend adapter implementations
- Backend-independent queue and FCFS policy core
- Publication documentation
- A newly authored generic showcase and tests

## Excluded

- XR2Learn Git history
- Photon, FishNet, and Mirror SDK source
- Unity package-cache source
- Original V-Lab models, textures, audio, avatars, and scene
- Backend application settings and credentials
- AI credentials, hosted model payloads, and AI-generated evaluation results;
  experimental provider interfaces remain inspectable but outside v1 claims
- Generated benchmark exports

## Verification

The showcase and generated integration workspace were imported with Unity
`6000.3.11f1`. The authored scene, FCFS handoff, five audited SDKs, five runtime
adapters, generated backend roots, generic network prefabs, and controlled-child
contract passed the cold-start repository validator. The final Windows build and
two-client release protocol are recorded separately from source provenance.
