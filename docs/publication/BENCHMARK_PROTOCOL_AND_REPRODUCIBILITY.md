# Benchmark Protocol and Reproducibility

## Implemented Protocol

The current benchmark protocol version is 1.4. The same scenario runner,
assertion model, and export pipeline operate above all five networking backends.

| ID | Purpose | Mode | Participants | Cycles | Hold | Prequeue | Controlled child rotation |
|---|---|---|---:|---:|---:|:---:|---|
| S1 | Baseline FCFS | Closed loop | 2 | 3 | 10 s | Yes | Controlled Turret, +60 degrees Y |
| S2 | Contention burst | Closed loop | 2 | 5 | 5 s | No | Controlled Turret, +30 degrees Y |
| S3 | Rapid handoff | Closed loop | 2 | 10 | 2.5 s | Yes | Controlled Turret, +15 degrees Y |
| S4 | Transform replication | Closed loop | 2 | 5 | 8 s | Yes | Controlled Turret, +90 degrees Y |
| S5 | Open-loop stress | Open loop | 2 | 20 | 0.5 s | No | None |

All scenarios use a five-second warmup. Their cooldown, timeout, manipulation,
settling, and tolerance values are defined in
`Editor/Setup/BenchmarkScenarioPresetCatalog.cs`.

Protocol 1.4 requires at least `completed cycles - 1` successful physical backend
authority handoffs in a closed-loop run. These are the guaranteed inter-cycle
handoffs. Per-phase assertions independently verify every logical holder
transition, because SDKs differ in whether an initial assignment or a return to
server authority is reported as a physical transfer. Every backend must also
prove that the configured shared object is live in its SDK network object graph
before measurement.
Protocol 1.3 and earlier exports are retained unchanged as diagnostic evidence,
but are never pooled with 1.4 data.

## What the Runner Validates

`OrchestrationScenarioRunner`:

1. Waits for the expected participants.
2. Begins the session when configured.
3. Validates the scenario configuration.
4. Executes warmup.
5. Issues deterministic requests and releases.
6. Performs controlled child rotation where applicable.
7. Records lifecycle and outcome assertions.
8. Finalizes release state.
9. Waits for telemetry readiness.
10. Exports the completed run.

Configuration and lifecycle validity are separate from outcome success. A run
can be valid while exposing a synchronization or outcome failure. This
distinction must be preserved in the paper and analysis.

## Exported Evidence

The export pipeline can produce:

- `run_metadata.csv`
- `session_lifecycle.csv`
- `queue_transitions.csv`
- `authority_transfers.csv`
- `runtime_performance.csv`
- `runtime_performance_summary.csv`
- `scenario_events.csv`
- `scenario_assertions.csv`
- `scenario_metrics_summary.csv`
- User, instrument, fairness, and interaction summaries
- Optional AI decision files
- A completion manifest written after successful publication

Exports are first staged in a short `.staging` path and a run folder is
published only after all required files have been written. In the Editor, the
root is `<UnityProject>/EvaluationExports`. In a standalone player, the root is
`<executable-folder>/EvaluationExports`. The same root is used by protocol
coverage, backend comparison, ranked-report, and "Open Exports" tooling.

## Pilot Coverage

Earlier runs are diagnostic evidence from protocol versions up to 1.3. The
protocol 1.4 five-backend pilot matrix is intentionally pending until every
backend passes one two-player run for all five scenarios. Only then should the
balanced final replicate collection begin.

The pilot topology was:

- Local client/host deployment
- Two participants
- Two player builds on the same physical machine
- Unity version 6000.3.11f1
- AI disabled
- No recorded network impairment
- No independent external traffic or energy measurement

This dataset verifies protocol execution and identifies variance or defects. It
is not sufficient for inferential claims because every experimental cell has
only one observation and both clients share hardware resources.

## Final Experimental Design

### Freeze the Experimental Artifact

Before final measurement:

- Create a clean release commit and semantic version tag.
- Record the complete Git hash.
- Pin Unity and every SDK version.
- Export the exact scenario preset catalog.
- Freeze the analysis script and metric definitions.
- Disable AI and all unrelated experimental features.
- Record build target, scripting backend, architecture, graphics API, quality
  settings, frame cap, VSync, resolution, and full-screen mode.

### Use Build-to-Build Execution

Do not mix an editor process with a player build in the final performance
experiment. The editor adds instrumentation and workload that are not present in
a deployed application.

Use two equivalent standalone builds. If they run on one machine, state this as
the co-located deployment profile and control process placement and background
applications. For stronger network claims, repeat a subset on two physical
machines connected through a documented network.

### Randomize and Block

- Randomize backend order within each experimental block.
- Randomize scenario order or use a balanced Latin-square order.
- Treat day or execution block as a recorded factor.
- Reboot or restore a documented clean state between blocks when feasible.
- Use the same build, scene, spawn positions, user roles, and scenario seed.
- Alternate which process is host/master to detect role effects.

### Select Replicate Count From Pilot Variance

The current configuration target of 10 replicates per backend and scenario is a
reasonable planning value, but it must not be justified only by convention.

Use repeated pilot measurements to estimate variance for the primary outcomes.
Choose the final sample size through power analysis or a precision target for
confidence-interval width. Report the method and assumptions.

### Primary Outcomes

Pre-register a small set of primary metrics:

- Successful authority-transfer rate
- Authority-transfer latency
- Access grant delay
- Scenario assertion success rate
- Final controlled-transform error
- Frame time, preferably median and high percentiles

Use request/grant/release counts, fairness, memory, and other telemetry as
secondary outcomes. Average FPS alone should not be the principal performance
measure.

### External Measurements

Unity measurements should be complemented where possible by:

- Operating-system process CPU utilization
- Process memory working set
- GPU utilization and frame-time capture
- Network bytes, packets, retransmissions, and message rate
- Machine power or energy measurement if efficiency is claimed

Record these measurements per process. Two builds sharing one machine otherwise
make per-client resource attribution difficult.

### Network Conditions

Local host/client results measure framework and backend behavior under a
near-zero-distance topology. They do not establish Internet robustness.

If latency and loss are research outcomes, define separate controlled network
profiles, for example:

- Baseline LAN
- Fixed moderate delay
- Higher delay
- Low packet loss
- Combined delay and loss

Use one documented impairment tool and verify the applied condition externally.
Do not mix these profiles into the baseline comparison without treating network
condition as an experimental factor.

## Statistical Analysis

For every metric and backend-scenario cell, report:

- Number of valid and invalid runs
- Mean and standard deviation when distributionally meaningful
- Median and interquartile range
- 95% confidence intervals, preferably bootstrap intervals
- Raw points or distribution plots

Analyze backend and scenario jointly using a repeated-measures or mixed-effects
model where assumptions are supported. If assumptions are not supported, use an
appropriate non-parametric repeated comparison such as Friedman tests, followed
by corrected pairwise comparisons.

Report effect sizes and multiplicity correction. Do not interpret a small
p-value without practical effect magnitude and uncertainty.

TOPSIS may be reported as a transparent secondary decision analysis:

- Publish raw metric values first.
- Publish normalization, direction, and weights.
- Include sensitivity analysis across plausible weights.
- Do not call its highest-ranked backend universally best.

## Reproducibility Checklist

For each final run, preserve:

- Run UUID and timestamp
- Commit and release version
- Backend and exact SDK version
- Scenario ID and protocol version
- Randomization block and order
- Host/client role assignment
- Machine, OS, CPU, GPU, RAM, and driver versions
- Unity build settings
- Display and frame-control settings
- Network topology and impairment profile
- Warmup and cooldown
- Validity and outcome assertions
- Raw telemetry
- External performance and traffic measurements
- Export completion manifest and checksums

The final data deposit should contain raw data, exclusions with reasons,
processed tables, analysis scripts, protocol definitions, and a machine-readable
data dictionary.
