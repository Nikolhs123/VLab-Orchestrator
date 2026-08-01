# Optional Backend Installation

For a complete screenshot-led clean-clone workflow, see
[`ILLUSTRATED_GUIDE.md`](ILLUSTRATED_GUIDE.md).

The dependency-free showcase requires no multiplayer SDK. Production adapters
require the corresponding official dependency.

| Backend | Audited version | Publication installation boundary |
|---|---|---|
| Photon PUN 2 | 2.55 | Open the official Unity Asset Store package and provide a local App ID |
| Photon Fusion | SDK 2.0.9, build 1566 | Open Photon's official SDK download and provide local settings |
| Unity Netcode for GameObjects | 2.11.0 | Install through Unity Package Manager |
| FishNet | 4.6.22 | Open the official Unity Asset Store package |
| Mirror | 96.0.1 | Open the official Unity Asset Store package |

## Rules

- Do not commit application IDs, API keys, tokens, or private service URLs.
- Do not copy Unity package-cache contents into this repository.
- Do not copy vendor examples or SDK source unless its license explicitly allows
  redistribution and the publication has a reason to vendor it.
- Pin every dependency version used for final experiments.
- Compile each adapter in an optional assembly only when its SDK is present.

## Current Package Setup

The clean showcase imports the core and all five adapter boundaries together.
It pins the complete compatibility baseline before vendor import: AI Navigation,
Terrain, UI/UI Toolkit, Animation, Audio, 2D/3D Physics, Particle System, Unity
Web Request, JSON, and Mono.Cecil. Asset Store SDKs compile optional utilities
and examples automatically, so removing an apparently unused built-in module
can break an SDK import.

1. Open `repo/src/showcase/UnityProject` with Unity `6000.3.11f1`.
2. Open **VLab Orchestrator > Setup**.
3. Select a backend tab.
4. Use the single install action shown in its red card.
5. If PUN 2 triggers Unity's API Updater, approve migration for all affected
   files. Select the project-wide **Yes, for these and other files that might be
   found** option when Unity presents it. PUN 2.55 predates Unity 6 API changes,
   and declining leaves its locally imported provider scripts uncompilable.
6. For PUN 2 or Fusion, create a separate application in the Photon dashboard
   and paste its App ID into the settings asset selected by the setup window.
7. When the card turns green, adapter, dependency, compilation, and credential
   setup are complete.
8. Select **Open Project Configuration** for prefabs, policy, and benchmark settings.

The setup window intentionally shows only dependency state:

- **Red** means the exact audited SDK is missing, mismatched, or not compiling.
- **Green** means the adapter, audited SDK version, compilation, and any required
  local Photon App ID are ready.
- **Script errors** keeps the card red even if an SDK type was detected; a
  backend is never reported ready until Unity compiles successfully.
- Adapter implementation details remain a framework concern and are not exposed
  as user setup steps.

## Safe Uninstall And Reinstall

Use **Safely Uninstall SDK** in the selected backend tab instead of deleting a
vendor package manually. The operation first removes the generated five-backend
runtime, then removes only paths owned by that SDK and clears its stale scripting
defines. Project configuration, showcase assets, benchmark exports, and the
SDK-independent core are preserved. Photon shared libraries are deliberately
retained when PUN 2 is removed because another Photon product may use them.

The lightweight adapter boundary remains installed for PUN 2, Fusion, FishNet,
and Mirror, allowing the audited SDK to be reinstalled with the same setup
action. Netcode's adapter is also removed because its package declares Netcode
as a required dependency. Re-add that adapter from the Netcode tab before
reinstalling Netcode. The generated runtime activates again only after all five
audited SDKs compile, preventing partially generated cross-backend source from
causing compilation errors.

Unity Netcode `2.11.0` is installed through Unity Package Manager. For FishNet,
Mirror, PUN 2, and Fusion, the setup action first looks for the exact audited
`.unitypackage` in Unity's licensed local Asset Store cache or Downloads folder.
It verifies the package SHA-256 before importing it. If absent, the action opens
the official provider route so the user can authenticate and accept its license.
No vendor package or credential is committed by this repository.

All five SDKs are supported in the same project with the pinned matrix above.
The critical Photon constraint is version generation: use PUN `2.55` with
Fusion `2.0.9 build 1566`. Do not overlay Fusion `2.1.x`; it carries a newer
split Photon Realtime source generation that collides with PUN 2's Realtime
source. The setup window reports a version mismatch rather than declaring an
unknown/latest package ready.

Photon requires an account to download Fusion and separate Photon applications
for PUN Realtime and Fusion. Each evaluator creates those applications and uses
their own App IDs; IDs from the development project are neither required nor
distributed. Authentication and license acceptance happen on Photon's official
site and cannot be automated or bypassed by this repository.
The adapter evidence targets Fusion 2.0.9 build 1566. A newer provider release
must pass the all-five compile and conformance protocol before it can replace
that pinned version.

## Installer And License Boundary

The setup window never downloads, copies, or republishes vendor SDK files from
this repository:

- Netcode is requested from Unity's official package registry.
- PUN 2, FishNet, and Mirror use packages acquired through the user's official
  Unity Asset Store account and import only a matching audited local package.
- Fusion uses a package acquired through Photon's official site and requires the
  user's Photon account and App ID; a matching local package is then imported.
- Credentials and vendor settings remain local and are excluded from version
  control.

This keeps the SoftwareX artifact limited to project-authored source while
making every optional dependency installable from one setup button after any
provider-controlled acquisition step.

## Recommended Import Sequence

1. Open the project and wait for Unity Package Manager to resolve the baseline.
2. Confirm Unity Netcode `2.11.0`.
3. Import PUN 2 `2.55`.
4. Import Fusion `2.0.9 build 1566`, not Fusion `2.1.x`.
5. Import FishNet `4.6.22`.
6. Import Mirror `96.0.1`.
7. Wait for compilation and confirm every backend tab is green.

The setup lifecycle is designed to tolerate a different installation order.
The sequence above is recommended because it makes provider prompts and Photon
compatibility easier to diagnose. In every order, allow Unity to finish package
resolution, API migration, import, and compilation before starting the next SDK.

FishNet `4.6.22` contains an optional Mirror migration editor utility that
references `Mirror.NetworkTransformChild`, which no longer exists in Mirror
`96.0.1`. The core editor postprocessor automatically makes that utility use a
reflection-based child target lookup. This repair does not alter either SDK's
runtime networking code.

Fusion `2.0.9 build 1566` initializes optional native profiler counters during
static startup. On Unity `6000.3.11f1`, that metadata path can crash every
standalone player because all audited SDK assemblies are present regardless of
the selected backend. The core postprocessor keeps those counters in the Editor
but compiles them out of standalone players. This does not alter Fusion
transport, simulation, serialization, or the orchestration CSV performance
sampler. Final performance measurements must use non-development players.

## Automatic Runtime Activation

The adapter package snapshots remain `.cs.txt` so the clean project opens with
zero vendor SDKs. No manual source promotion or assembly editing is required.
After the fifth audited SDK compiles, the core package automatically:

1. Materializes the project-authored framework and adapter source under the
   ignored `Assets/GeneratedSoftwareXRuntime` folder.
2. Creates ignored network-ready copies of the generic avatar and instrument.
3. Creates PUN 2, Fusion, Netcode, FishNet, and Mirror runtime roots; the Mirror
   root stores its KCP transport assignment before `NetworkManager.Awake()`.
4. Authors the selected backend onto shared prefabs and creates queue prefabs.
5. Writes the ignored production runtime configuration.

Use **VLab Orchestrator > Finalize SoftwareX Runtime** only if an interrupted
Unity import needs to be resumed. The operation is idempotent. Then use
**VLab Orchestrator > Validate Five-Backend Runtime**. A passing validation
confirms the SDK matrix, adapters, generated assets, and generic shared-object
contract; it does not replace a two-client network benchmark.

Photon App IDs remain local. PUN 2 and Fusion cannot connect to Photon Cloud
until the evaluator supplies valid IDs through the providers' normal settings.
