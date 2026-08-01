# Manuscript claim-evidence audit

This audit distinguishes implementation evidence, internal experimental
evidence, cited external evidence, and claims intentionally not made.

| Manuscript topic | Evidence available | Permitted wording | Wording avoided |
|---|---|---|---|
| Five backend adapters | Source tree, runtime interfaces, five adapter implementations | Implemented for five named SDKs | Fully equivalent backends |
| Common allocation model | Queue core, policy engine, adapter views | Common FCFS model expressed through each adapter | Identical native semantics |
| Existing application scripts | Audited private V-Lab integration and generic public contract | Designed to retain domain scripts; demonstrated in the audited case study | Works without modification in every Unity project |
| Shared-object provisioning | Provisioner source and evaluated showcase | Scene-authored and runtime-prefab paths are implemented | All arbitrary prefabs work automatically |
| Benchmark automation | Scenario runner, five presets, manifests | Deterministic high-level command schedules reduce manual timing variation | Eliminates all experimental confounding |
| Candidate matrix | 25 retained protocol 1.4 exports | All 25 passed predefined structural checks | Statistical proof of backend parity |
| Targeted reruns | Five protocol 1.5 anomaly investigations | Diagnostic evidence for the identified anomalies | Balanced replication |
| Netcode latency correction | Raw protocol 1.5 observations and correction history | Corrected targeted observations under this setup | General Netcode latency |
| Mirror angular miss | One candidate miss and one zero-error rerun | Did not reproduce in one targeted rerun | Proven transient defect |
| PUN 2 angular miss | Candidate and targeted rerun above 2-degree tolerance | Reproduced in two runs under this workload | Universal PUN 2 synchronization failure |
| Reproducibility | Source, protocol, raw data, environment and rebuild instructions | Reproducibility-oriented or controlled protocol | Independently reproduced results |
| Backend ranking | One independent run per balanced cell | Descriptive observations only | Best, fastest, superior, statistically significant |
| AI | Experimental hooks; disabled in evaluated configuration | Future extension point | Evaluated AI allocation |

## Review rule

Any future quantitative conclusion must identify:

1. the exact protocol version;
2. the independent experimental unit and repetition count;
3. the hardware, topology, frame cap and network condition;
4. the primary outcome and tolerance;
5. exclusions and invalid-run handling;
6. uncertainty intervals and effect size when comparative inference is made.
