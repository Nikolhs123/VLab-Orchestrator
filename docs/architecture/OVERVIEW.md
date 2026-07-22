# Architecture Overview

```text
Existing Unity application interactions
                 |
                 v
        Shared object definition
                 |
                 v
  Interaction gate and access controller
                 |
                 v
   Backend-independent queue and policy
                 |
                 v
 Common runtime/session/queue contracts
                 |
     +-----------+-----------+-----------+-----------+
     |           |           |           |           |
    PUN 2      Fusion      Netcode     FishNet      Mirror
     |           |           |           |           |
     +-----------+-----------+-----------+-----------+
                 |
                 v
       Telemetry and benchmark export
```

## Core

The core maintains users, instruments, requests, waiting queues, grants,
releases, timeouts, and allocation-policy selection without importing Unity or
any networking SDK.

## Shared Runtime

Shared runtime contracts expose session and queue state to configuration,
telemetry, UI, avatars, and deterministic scenarios.

## Backend Adapters

Each optional adapter maps the common model onto one networking SDK. Application
UI and evaluation code consume common views rather than backend APIs.

## Existing Application Integration

The production V-Lab integration preserves the pre-existing domain interaction
scripts. The orchestration layer controls access and synchronization above those
scripts instead of replacing laboratory behavior.

## Showcase Boundary

The public showcase imports the real queue core from
`com.vlab.orchestrator.core` but uses a local deterministic runtime. It exists
to make architecture and behavior inspectable without redistributing vendor
SDKs or application assets. Production backend results remain separate
experimental evidence.

The package dependency graph and backend activation lifecycle are documented in
[PACKAGE_ARCHITECTURE.md](PACKAGE_ARCHITECTURE.md).
