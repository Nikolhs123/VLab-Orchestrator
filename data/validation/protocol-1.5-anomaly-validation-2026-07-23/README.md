# Protocol 1.5 Anomaly Validation

## Purpose

Five targeted Windows-to-Windows, same-machine runs investigated anomalies in
the protocol 1.4 candidate matrix. Protocol 1.5 cancels abandoned Unity Netcode
authority-transfer measurements before a later cycle can reuse their request
timestamps.

## Results

| Backend | Scenario | Protocol valid | Rotation error | Mean authority latency |
|---|---|---:|---:|---:|
| Mirror | S1 Baseline FCFS | Yes | 0.0000 deg | 13.3555 ms |
| PUN2 | S3 Rapid Handoff | Yes | 2.7591 deg | 62.4679 ms |
| Unity Netcode | S1 Baseline FCFS | Yes | 0.0000 deg | 12.6956 ms |
| Unity Netcode | S3 Rapid Handoff | Yes | 0.0000 deg | 15.2507 ms |
| Unity Netcode | S4 Transform Replication | Yes | 0.0000 deg | 14.3328 ms |

All five runs completed their expected cycles with two participants, no failed
commands, no failed validity assertions, and no failed authority transfers.

## Conclusions

- The previous multi-second Unity Netcode values were measurement artifacts.
  Corrected raw observations range from 0 to 18.5965 ms in the validation runs.
- Mirror S1's earlier 84.58-degree miss did not reproduce and is classified as
  a transient outcome in the candidate run, not a persistent backend failure.
- PUN2 S3 missed the 2-degree threshold twice: 3.3318 degrees in protocol 1.4
  and 2.7591 degrees in protocol 1.5. Rapid-handoff transform convergence is
  therefore retained as a reproducible measured limitation under this setup.

These targeted runs validate interpretation of the candidate matrix. They do
not form a balanced replacement matrix and are not pooled as extra replicates.

