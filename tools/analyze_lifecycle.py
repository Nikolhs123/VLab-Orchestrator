"""Aggregate repeated lifecycle scenario runs (L1-L5).

  py tools/analyze_lifecycle.py --ledger lifecycle_ledger.csv --exports <root> [<root> ...] --out <dir>

The ledger has one row per attempt (columns backend, case, rep, run_logs). Exports are paired through the room
name taken from the run-log folder name. An attempt passes when the coordinator and the participant exports
exist and every assertion marked required passed in both; a killed process that never wrote its export makes
the attempt fail, it is not dropped. Reported per backend and case: attempts, passes, failed assertion ids,
the observed outcome of QueueAuthorityLeaves, and the departure-detection times (an upper bound taken from the
survivor's first wait). Outputs: attempts.csv, summary.csv, report.md.
"""
import argparse
import collections
import importlib.util
import pathlib
import re
import statistics
import sys


def load_campaign():
    spec = importlib.util.spec_from_file_location("analyze_campaign", pathlib.Path(__file__).with_name("analyze_campaign.py"))
    module = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(module)
    return module


def main():
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument("--ledger", required=True)
    parser.add_argument("--exports", nargs="+", required=True)
    parser.add_argument("--out", required=True)
    args = parser.parse_args()

    campaign = load_campaign()
    exports = campaign.index_exports(args.exports)

    attempts = []
    for row in campaign.read_csv(args.ledger):
        found = exports.get(campaign.room_of(row.get("run_logs", "")), {})
        folders = {role: folder for role, folder in found.items()}
        record = {"backend": row["backend"], "case": row["case"], "rep": row.get("rep", ""),
                  "coordinator_export": "Coordinator" in folders, "participant_export": "Participant" in folders,
                  "failed_required": "", "outcome": "", "detection_seconds": ""}
        failed, outcome, detection = [], "", ""
        for role, folder in folders.items():
            for a in campaign.read_csv(folder / "scenario_assertions.csv"):
                if a.get("required_for_validity", "True") == "True" and a.get("passed") != "True":
                    failed.append(f"{role}:{a['assertion_id']}")
                if a.get("assertion_id") == "ObservedOutcomeAfterQueueAuthorityLeft":
                    outcome = a.get("actual_value", "")
                match = re.search(r"departureDetectionSeconds=([\d.]+)", a.get("details", "") or "")
                if match:
                    detection = match.group(1)
        # Both exports are expected, except that the abruptly terminated process in L1/L2 exports before it is killed.
        record["passed"] = bool(folders.get("Coordinator") and folders.get("Participant")) and not failed
        record["failed_required"] = ";".join(failed)
        record["outcome"] = outcome
        record["detection_seconds"] = detection
        attempts.append(record)

    out = pathlib.Path(args.out)
    out.mkdir(parents=True, exist_ok=True)
    campaign.write_csv(out / "attempts.csv", attempts)

    groups = collections.defaultdict(list)
    for a in attempts:
        groups[(a["backend"], a["case"])].append(a)
    summary = []
    for (backend, case), g in sorted(groups.items()):
        times = [float(a["detection_seconds"]) for a in g if a["detection_seconds"]]
        outcomes = collections.Counter(a["outcome"] for a in g if a["outcome"])
        summary.append({"backend": backend, "case": case, "attempts": len(g), "passed": sum(a["passed"] for a in g),
                        "failed_assertions": " | ".join(sorted({x for a in g for x in a["failed_required"].split(";") if x})),
                        "outcomes": "; ".join(f"{k} x{v}" for k, v in outcomes.items()),
                        "detection_seconds_median": statistics.median(times) if times else "",
                        "detection_seconds_max": max(times) if times else ""})
    campaign.write_csv(out / "summary.csv", summary)

    lines = ["# Lifecycle runs", "", "| Backend | Case | Attempts | Passed | Outcome(s) | Detection median / max (s) | Failed required assertions |",
             "| --- | --- | --- | --- | --- | --- | --- |"]
    for s in summary:
        det = "" if s["detection_seconds_median"] == "" else f"{s['detection_seconds_median']:.1f} / {s['detection_seconds_max']:.1f}"
        lines.append(f"| {s['backend']} | {s['case']} | {s['attempts']} | {s['passed']} | {s['outcomes']} | {det} | {s['failed_assertions']} |")
    (out / "report.md").write_text("\n".join(lines) + "\n", encoding="utf-8")
    print(f"attempts={len(attempts)} passed={sum(a['passed'] for a in attempts)} groups={len(summary)} -> {out}")


if __name__ == "__main__":
    sys.exit(main())
