# Analysis capsule (reproduction level 1)

Recomputes the tables of the revised manuscript from the archived exports of Campaigns A, B and C
without Unity. Python 3 standard library only.

```text
capsule/
  code/run            entry point used by Code Ocean (bash)
  code/reproduce.py   runs the repository analysis scripts and compares the headline numbers
  environment/Dockerfile
```

`code/tools/` is a copy of the repository folder `tools/` (stage it before building the capsule:
`cp -r tools capsule/code/tools`). When run from the repository, `reproduce.py` uses `tools/` directly.

## Data folder

```text
/data/campaign_A|campaign_B|campaign_C/
    ledgers/      attempt ledgers (campaign_attempts_ledger.csv; probes_ledger.csv and lifecycle_ledger.csv in B;
                  campaign3_regression_attempts.csv and campaign3_swapped_attempts.csv in C)
    RunLogs/      host and client logs (CampaignRunLogs in campaign_A)
    <Backend_Scenario>/EvaluationExports/   exports written by the players (coordinator and participant)
```

## Run

```text
py capsule/code/reproduce.py --data <data folder> --results <results folder>
```

Outputs per campaign part: `attempts.csv`, `cells.csv`, `report.md`; per campaign: `invariants.csv`
(pairing and FCFS replay of every export), `probes/`, `lifecycle/`; and `summary.md`, which lists the published
and recomputed number of attempts and valid executions (A 248/250, B 249/250, C regression 25/25,
C swapped 125/125). The exit code is 1 if a number differs.

This capsule does not execute Unity and does not repeat the multiplayer experiments (levels 2 and 3 of
the reproduction levels in Section IX of the manuscript).