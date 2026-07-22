# Unity Packages

This directory is the installable Unity Package Manager distribution surface.

| Package | Purpose | Clean-clone state |
|---|---|---|
| `com.vlab.orchestrator.core` | Queue, policy, state, configuration, benchmark settings, and editor workspace | Compiles and runs |
| `com.vlab.orchestrator.backend.pun2` | Photon PUN 2 integration boundary | Reference source |
| `com.vlab.orchestrator.backend.fusion` | Photon Fusion integration boundary | Reference source |
| `com.vlab.orchestrator.backend.netcode` | Unity Netcode integration boundary | Reference source |
| `com.vlab.orchestrator.backend.fishnet` | FishNet integration boundary | Reference source |
| `com.vlab.orchestrator.backend.mirror` | Mirror integration boundary | Reference source |

The five backend packages are separate so vendor dependencies do not leak into
the core assembly. Their project-authored source is stored as `.cs.txt` until
the corresponding SDK-conditioned runtime assembly is prepared and tested.

The dependency-free showcase consumes all six packages through local `file:`
references in its Unity Package Manager manifest.
