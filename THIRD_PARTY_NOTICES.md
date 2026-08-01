# Third-Party Notices

## Distribution Boundary

This repository distributes no Photon, FishNet, Mirror, Unity package-cache,
XR2Learn media, or original V-Lab model source. It also excludes generated
prefabs that serialize vendor networking components. The playable showcase is
built from Unity primitives and uses no external models, textures, fonts,
audio, logos, or avatar media.

## Optional Dependencies

| Dependency | Tested version | Repository handling |
|---|---:|---|
| Photon PUN 2 | 2.55 | Acquired from the official provider; SDK and App ID excluded |
| Photon Fusion | 2.0.9 build 1566 | Acquired from Photon; SDK and App ID excluded |
| Unity Netcode for GameObjects | 2.11.0 | Resolved by UPM under Unity's package terms; package cache excluded |
| FishNet | 4.6.22 | Acquired from the official provider under its custom license; SDK excluded |
| Mirror | 96.0.1 | Upstream MIT license; SDK still excluded for a consistent clean boundary |

Users must obtain these dependencies through their official distribution
channels and accept their respective current licenses. The framework's MIT
License covers only project-authored code and does not relicense any provider
SDK. Exact versions and installation steps are documented in
`docs/installation/BACKENDS.md`.

Authoritative terms and notices should be reviewed at release time:

- Photon license terms: <https://www.photonengine.com/terms/licenseterms>
- Unity legal and Asset Store terms: <https://unity.com/legal>
- FishNet license: <https://github.com/FirstGearGames/FishNet/blob/main/LICENSE.md>
- Mirror license: <https://github.com/MirrorNetworking/Mirror/blob/master/LICENSE>

Unity and Unity Editor are products and trademarks of Unity Technologies.
Photon is a product and trademark of Exit Games GmbH. Other product names and
marks belong to their respective owners.

The locally installed SDK versions retain their own bundled notices. Those
files are not copied here because the corresponding SDK payloads are not part
of the publication repository.
