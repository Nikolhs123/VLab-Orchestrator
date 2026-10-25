"""Recompute the tables of the revised manuscript from the archived campaign exports (no Unity needed).

Expected layout of the data folder (default /data):

  campaign_A|campaign_B|campaign_C/
      ledgers/            attempt ledgers (CSV), one row per attempt
      RunLogs/            host and client logs of every attempt (CampaignRunLogs in campaign_A)
      <Backend_Scenario>/EvaluationExports/...   exports written by the players

Writes to the results folder (default /results): one sub-folder per campaign part with attempts.csv,
cells.csv, report.md, the queue-invariant check, probe and lifecycle summaries, and summary.md that
compares the recomputed headline numbers with the numbers printed in the manuscript.
Only the Python standard library is used. The analysis scripts are the ones of the repository (tools/).
"""
import argparse
import csv
import pathlib
import subprocess
import sys

CAMPAIGNS = {
    "campaign_A": {"prefix": "phaseb-", "attempts": [("main", "campaign_attempts_ledger.csv", 250, 248)]},
    "campaign_B": {"prefix": "phaseb-", "attempts": [("main", "campaign_attempts_ledger.csv", 250, 249)],
                   "probes": "probes_ledger.csv", "lifecycle": "lifecycle_ledger.csv"},
    "campaign_C": {"prefix": "phasec-", "attempts": [("regression", "campaign3_regression_attempts.csv", 25, 25),
                                                     ("swapped", "campaign3_swapped_attempts.csv", 125, 125)]},
}


def run(tools, script, *args):
    cmd = [sys.executable, str(tools / script), *map(str, args)]
    print("$", " ".join(cmd))
    return subprocess.run(cmd, check=False, capture_output=True, text=True)


def read(path):
    with open(path, newline="", encoding="utf-8-sig") as handle:
        return list(csv.DictReader(handle))


def main():
    here = pathlib.Path(__file__).resolve().parent
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument("--data", default="/data")
    parser.add_argument("--results", default="/results")
    parser.add_argument("--tools", default=str(here / "tools"),
                        help="folder with analyze_campaign.py and the other analysis scripts")
    args = parser.parse_args()
    data, results, tools = pathlib.Path(args.data), pathlib.Path(args.results), pathlib.Path(args.tools)
    if not (tools / "analyze_campaign.py").exists():
        tools = here.parent.parent / "tools"
    results.mkdir(parents=True, exist_ok=True)

    lines = ["# Recomputed headline numbers", "",
             "| Campaign part | Attempts (published) | Attempts (recomputed) | Valid (published) | Valid (recomputed) | Result |",
             "| --- | --- | --- | --- | --- | --- |"]
    ok = True
    for name, cfg in CAMPAIGNS.items():
        root = data / name
        if not root.exists():
            print("skip", name, "(not in the data folder)")
            continue
        logs = next((root / n for n in ("RunLogs", "CampaignRunLogs") if (root / n).exists()), None)
        for part, ledger, published_n, published_valid in cfg["attempts"]:
            out = results / name / part
            proc = run(tools, "analyze_campaign.py", "--ledger", root / "ledgers" / ledger, "--exports", root,
                       "--out", out, "--room-prefix", cfg["prefix"], *(["--logs-root", logs] if logs else []))
            print(proc.stdout.strip(), proc.stderr.strip()[:300])
            rows = read(out / "attempts.csv") if (out / "attempts.csv").exists() else []
            valid = sum(1 for r in rows if r.get("valid_execution") == "True")
            same = len(rows) == published_n and valid == published_valid
            ok &= same
            lines.append(f"| {name} {part} | {published_n} | {len(rows)} | {published_valid} | {valid} | {'same' if same else 'DIFFERENT'} |")
        inv = results / name / "invariants.csv"
        proc = run(tools, "analyze_session_invariants.py", root, "--out", inv)
        print(proc.stdout.strip().splitlines()[-1] if proc.stdout.strip() else proc.stderr.strip()[:300])
        if "probes" in cfg:
            run(tools, "analyze_probe_runs.py", "--ledger", root / "ledgers" / cfg["probes"], "--exports", root,
                "--out", results / name / "probes")
        if "lifecycle" in cfg:
            run(tools, "analyze_lifecycle.py", "--ledger", root / "ledgers" / cfg["lifecycle"], "--exports", root,
                "--out", results / name / "lifecycle")
    (results / "summary.md").write_text("\n".join(lines) + "\n", encoding="utf-8")
    print("\n".join(lines))
    return 0 if ok else 1


if __name__ == "__main__":
    sys.exit(main())
