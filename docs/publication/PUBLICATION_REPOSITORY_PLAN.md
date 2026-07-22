# Publication Repository Plan

## Objective

Create a standalone, citable repository that demonstrates the orchestration
framework without redistributing the full V-Lab application or third-party
networking SDKs.

## Recommended Layout

```text
VLab-Orchestrator/
|-- README.md
|-- LICENSE.txt
|-- THIRD_PARTY_NOTICES.md
|-- CITATION.cff
|-- CHANGELOG.md
|-- repo/
|   `-- src/
|       |-- package.json
|       |-- Runtime/
|       |   |-- Core/
|       |   |-- Config/
|       |   |-- Interfaces/
|       |   |-- SharedSync/
|       |   `-- Telemetry/
|       |-- Editor/
|       |   |-- Setup/
|       |   `-- Evaluation/
|       `-- Backends/
|           |-- PUN2/
|           |-- Fusion/
|           |-- Netcode/
|           |-- FishNet/
|           `-- Mirror/
|-- Tests/
|   |-- Editor/
|   `-- Runtime/
|-- Samples~/
|   `-- GenericSharedInstrument/
|-- Documentation~/
|   |-- architecture/
|   |-- installation/
|   `-- protocol/
|-- Analysis~/
|   |-- scripts/
|   `-- example-data/
`-- .github/
    `-- workflows/
```

Unity Package Manager conventions can be finalized during extraction. The
official SoftwareX guide specifically requires source under `repo/src`. The
important engineering boundary is that the core package compiles without all
five backend SDKs and each adapter is optional.

## Generic Demonstration

The public sample should avoid uncertain V-Lab assets. It can use:

- A primitive shared instrument assembled from Unity shapes
- Two or three manipulable children
- A deterministic rotation target
- The shared avatar represented by a simple rights-cleared model or primitive
- The common session, queue, telemetry, and scenario UI

This sample should prove create/join, begin session, access request, queued
handoff, child synchronization, display names, benchmark execution, and export.

The V-Lab microscope remains the case study described in the paper, with images
or data included only where publication rights are confirmed.

## Minimum Test Suite

### Editor Tests

- FCFS request, queue, release, and next-user grant
- Timeout and disconnected-user cleanup
- Stable object IDs and configuration validation
- Scenario preset values and protocol-version consistency
- Export schema and required-file validation
- Ranking normalization and weight sensitivity

### Runtime Tests

- Bootstrap selects only the configured backend
- Foreign backend components are removed in dependency-safe order
- Scene object binding and runtime prefab provisioning
- Local holder interaction enabled and remote interaction disabled
- Late join receives session, queue, and object state
- Final release leaves no holder or queued phantom participant
- A completed scenario publishes a valid export manifest

At least one integration test per backend should cover host, join, session start,
handoff, transform replication, and disconnect.

## Release Milestones

### P0: Evidence Baseline

Complete this audit package and preserve the 25-run pilot dataset. This milestone
documents what exists and prevents scope drift.

### P1: Rights and Scope

Confirm source ownership, choose a framework license, review vendor terms, clear
sample assets, and formally exclude AI from the first paper.

### P2: Standalone Extraction

Create the Unity package, split optional backend assemblies, add an installation
checker, remove credentials, and build the generic sample.

### P3: Verification

Add tests and continuous integration. Validate import and execution in a clean
Unity project on a second machine or clean virtual environment.

### P4: Frozen Protocol

Publish the exact scenario catalog, primary outcomes, exclusion rules,
randomization schedule, and sample-size justification.

### P5: Final Experiment

Run the randomized replicated study, preserve failures, analyze raw metrics,
perform sensitivity analysis, and deposit data plus scripts under a DOI.

### P6: SoftwareX Submission

Write the paper from the tagged software release and deposited evidence. Archive
the exact release through Zenodo or an equivalent repository and cite its DOI.

## Fastest High-Quality Sequence

1. Resolve rights and licensing first because this can block public release.
2. Extract the package and generic sample before final experiments, so the
   measured artifact matches the published artifact.
3. Add core and export-schema tests before collecting expensive final data.
4. Run a small repeated pilot to estimate variance.
5. Freeze and preregister the final protocol.
6. Run final measurements once, under controlled randomized conditions.
7. Complete the manuscript while the data analysis is being verified.

Adding AI before these steps would broaden the research question, add provider
and reproducibility dependencies, and delay the citable software contribution.

## Definition of Done

The SoftwareX artifact is ready when:

- A clean clone installs from documented steps.
- The core compiles without optional networking SDKs.
- Every supported backend has versioned installation instructions.
- The generic sample completes a two-user handoff and benchmark.
- Automated tests pass.
- No credentials or unlicensed assets are present.
- The software has a clear license and citation metadata.
- The tagged release, manuscript commit, protocol, raw data, and analysis scripts
  are permanently archived.
- Every quantitative paper claim maps to a deposited table or script.
