# Windows-to-Windows Same-Machine Candidate Batch

## Scope

This batch contains one protocol 1.4 export for each combination of five
networking backends and five deterministic scenarios (25 runs total). The
operator ran two Windows player instances on the same physical computer. The
exported metadata confirms that the exporting host was a Windows build and that
both participants used the same machine. It does not independently encode the
executable type of the second participant, so the two-player execution topology
is also recorded here as an operator-reported condition.

Collection window: 2026-07-22 23:22 to 2026-07-23 00:02 Europe/Zurich.

## Structural Audit

- 5 backends and 5 scenarios are present.
- All 25 selected exports use protocol 1.4 and have complete manifests.
- All runs observed 2/2 participants and completed all expected cycles.
- No command, validity assertion, or authority transfer was recorded as failed.
- Every run contains two lifecycle rows and runtime-performance samples.
- One additional PUN2 S4 run was not selected; the latest valid run is retained.

## Quality-Control Findings

Two exports pass protocol validity but miss a measured outcome threshold:

- Mirror S1: final rotation error 84.5800 degrees (tolerance 2 degrees).
- PUN2 S3: final rotation error 3.3318 degrees (tolerance 2 degrees).

Unity Netcode authority-transfer means are also anomalously scenario-dependent:
approximately 7405.95 ms in S1, 2323.62 ms in S3, and 7286.92 ms in S4, versus
approximately 17 ms in S2 and S5. Raw rows show alternating near-zero and
hold-duration-scale observations. The authority event definition must be
reviewed before using these values for cross-backend ranking.

These anomalies were investigated in the protocol 1.5 validation addendum under
`data/validation/protocol-1.5-anomaly-validation-2026-07-23`. The Netcode values
were traced to abandoned pending timestamps and corrected. Mirror S1 converged
on rerun, while the PUN2 S3 threshold miss reproduced.

## Interpretation

This is a candidate functional dataset, not the final statistical dataset. It
contains one independent run per backend-scenario cell. The preregistered target
is at least 10 independent repetitions per cell, subject to pilot variance and
power analysis. Do not pool this batch with Editor-to-player pilot exports.
