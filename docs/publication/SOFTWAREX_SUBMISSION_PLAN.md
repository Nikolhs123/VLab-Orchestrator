# SoftwareX Submission Plan

## Source Requirements

This plan is based on:

- `softwarex-osp-template.docx`, version 6, March 2026
- The SoftwareX Guide for Authors checked on 2026-07-03:
  `https://www.sciencedirect.com/journal/softwarex/publish/guide-for-authors`

The mandatory submission artifact is not only a manuscript. It consists of:

1. An Original Software Publication manuscript using the official template.
2. A public, open-access GitHub repository.
3. `README.md` with purpose, installation, and use instructions.
4. `LICENSE.txt` using an approved open-source license.
5. Project source code under `repo/src`.
6. A permanent repository link and current version in the metadata table.
7. Public research data or a formal data-availability explanation.

The accepted software version may be copied to the SoftwareX GitHub organization
for archival.

## Word-Limit Discrepancy

The March 2026 Word template says 4,000 words and a maximum of six main-text
pages. The current online guide says 3,000 words and a maximum of six figures.

Until the editorial office confirms otherwise, use the stricter target:

- Draft target: 2,800 to 3,000 words
- Maximum figures: six
- Abstract target: approximately 100 words

The manuscript must contain all five named sections:

1. Motivation and significance
2. Software description
3. Illustrative examples
4. Impact
5. Conclusions

## Correct Work Order

### 1. Freeze Scope

Publish the current non-AI framework as version 1.0. AI remains an experimental
future extension and is not part of the paper's evaluated contribution.

### 2. Resolve Rights and License

Before making code public, confirm ownership of the new orchestration source and
permission to publish any inherited V-Lab code or assets. Choose an approved
open-source license, preferably MIT or Apache-2.0 if institutionally permitted.

### 3. Create the Publication Repository

Create a new, clean public repository such as:

`VLab-Orchestrator`

Do not repurpose or delete the private development repository. It remains the
complete engineering workspace and history.

The publication repository should contain only:

- Project-authored framework code
- Optional backend adapters
- Generic rights-cleared sample
- Tests
- Installation and developer documentation
- Protocol definitions and analysis scripts
- Small example outputs

### 4. Prove Clean Installation

Clone the public repository into a new Unity project on a clean machine or
virtual environment. Verify that:

- The core imports without any networking SDK.
- Each optional backend reports a clear missing-dependency message.
- Installing one supported SDK enables only its adapter.
- The generic sample completes a two-client session and export.

### 5. Freeze and Run the Final Experiment

Tag the exact evaluated release before final runs. Store the Git commit and
software version in every export. Deposit final raw data and analysis scripts in
a DOI-bearing repository.

### 6. Write the Final Manuscript

Draft the manuscript structure early, but finalize its claims, metadata,
versions, repository link, and quantitative results only after the software
release and dataset are frozen.

## Why the Existing Repository Is Not the Submission Artifact

The current repository is useful and should be preserved, but it is not yet a
SoftwareX-ready distribution because:

- It derives from the broader XR2Learn V-Lab repository.
- It contains third-party Photon, FishNet, and Mirror source trees.
- It contains application assets whose redistribution rights require review.
- It may contain backend settings or application identifiers.
- The framework source is not currently packaged under the required `repo/src`
  publication structure.
- The working tree is not a clean tagged release.
- A new user cannot yet import only the framework into a clean Unity project.

The existing project can still be used as the scientific case study and internal
integration environment. The public repository should be the reusable framework
extracted from it.

## Testing Without Redistributing SDKs

Reviewers do not need vendor SDK source committed into our repository. They need
a reproducible installation path.

Use:

- One SDK-independent core assembly
- One optional assembly per backend
- Version checks and conditional compilation
- Official installation links and exact tested versions
- Empty configuration templates for application IDs
- A setup validator in the Unity editor
- A generic sample and expected output
- Continuous integration for dependencies that may be installed legally and
  automatically

Unity Netcode can be declared through Unity Package Manager. Mirror, FishNet,
PUN 2, and Fusion should be installed through their official distribution
channels according to their licenses. The repository exposes and documents our
adapter code, not copies of vendor SDK source.

The README should provide one tested path per backend:

1. Install the named SDK version.
2. Enable or detect the matching adapter.
3. Supply local credentials where required.
4. Open the generic sample.
5. Start host and client builds.
6. Run the conformance scenario.
7. Compare the generated output with the documented schema.

## Manuscript Word Budget

Use approximately:

| Section | Target words | Main purpose |
|---|---:|---|
| Abstract | 100 | Problem, software, five backends, main impact |
| Motivation and significance | 600 | Scientific problem, related work, exact gap |
| Software description | 900 | Architecture and principal functionality |
| Illustrative examples | 450 | V-Lab microscope and generic reproducible sample |
| Impact | 700 | Portability, reproducibility, evidence, new research enabled |
| Conclusions | 150 | Focused summary and limitations |
| Figure and table captions | 100 | Keep concise because online guidance counts them |
| Total | 3,000 | Conservative submission target |

Metadata and references are outside the online guide's stated word count.

## Publication Strength Without AI

The software contribution is already sufficient in scope:

- Five networking backends behind common session and queue contracts
- Backend-independent FCFS access allocation
- Shared-object authority and synchronization
- Runtime backend selection and setup
- Preservation of pre-existing Unity interaction scripts
- Shared avatars and display names
- Deterministic benchmark scenarios
- Common telemetry and export schemas
- Editor-based backend comparison and sensitivity analysis

AI is not needed to make this publishable. Adding it now would require a new
research question, baselines, provider reproducibility, decision-safety
evaluation, and a clear explanation of how AI improves over FCFS.

## Novelty Is Not Proved by Benchmark Scores

Novelty should be established through a related-work feature matrix and supported
by code evidence. Performance experiments validate behavior and characterize
tradeoffs; they do not by themselves prove that the software concept is new.

The literature comparison should distinguish the framework from:

- Single-backend multi-user Unity frameworks such as XR MUSE
- Application-specific collaborative virtual laboratories
- Conceptual collaborative VR architecture models
- Networking-library performance comparisons that require separate
  implementations for each SDK

The candidate novelty claim is:

> A reusable Unity orchestration and evaluation layer that preserves existing
> domain interaction code while applying a common session, fair-access,
> authority-transfer, synchronization, telemetry, and deterministic benchmark
> model across five heterogeneous multiplayer backends.

This claim must be checked systematically rather than stated as absolute
uniqueness.

## Evidence to Run Again

### Required Conformance Study

Run existing S1-S5 for every backend after the public release is frozen:

- Two standalone builds, not editor plus build
- Preferably two physical machines
- Same hardware class and fixed Unity settings
- Randomized backend and scenario order
- Balanced host/master assignment
- At least 10 pilot-planned repetitions per backend-scenario cell, adjusted by
  power or confidence-interval analysis
- Preserve invalid and failed runs with reasons

Primary outcomes:

- Access and authority-transfer success rate
- Grant and transfer latency
- FCFS ordering correctness
- Final transform error
- State convergence after handoff
- Frame-time distribution

### Required Robustness Scenarios

Add and run four short conformance cases:

1. Late join after session start receives current object, holder, and queue state.
2. Holder disconnect releases access and grants the next valid participant.
3. Concurrent requests preserve the configured deterministic allocation policy.
4. Session termination clears holders, queues, avatars, and phantom participants.

These are more directly relevant to the framework claim than a very large
concurrent-user stress test.

### Optional Network Study

Only if the paper claims network robustness, repeat a subset under documented
delay and loss profiles on two machines. Treat network condition as an
experimental factor and measure traffic externally.

### Optional Scalability Study

Only if the paper claims scalability, add controlled 2, 4, 8, and 16-client
loads. The current two-user microscope protocol cannot support a broad
concurrent-user scalability claim.

## Relationship to Existing Comparison Research

A 2024 study by Chandra et al. compares Mirror, FishNet, Unity Netcode, and
Photon Fusion using CCU, bandwidth, CPU, FPS, RTT, and platform support. Its
focus is networking-library performance under increasing NPC load.

Our evaluation should complement rather than imitate it:

- Their question: how do networking libraries perform as user count grows?
- Our question: can one application-level orchestration model provide equivalent
  access, authority, shared-state behavior, and reproducible evaluation across
  heterogeneous networking backends?

If we later add scalability measurements, their metrics and 50-CCU result can be
used as contextual comparison, but differences in versions, topology, payload,
hardware, and application prevent direct numerical ranking.

## Six-Figure Plan

1. Framework architecture and relationship to existing application scripts
2. Backend adapter and shared runtime contracts
3. Session, request, queue, authority, and release sequence
4. Generic sample and V-Lab microscope case study
5. Experimental protocol and randomized execution design
6. Results figure with distributions and uncertainty, not only average ranks

Detailed setup screenshots, full tables, protocol values, and additional plots
belong in the GitHub documentation or data deposit.
