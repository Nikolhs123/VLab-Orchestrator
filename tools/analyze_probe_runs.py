"""Aggregate repeated W03 access-control probe runs.

  py tools/analyze_probe_runs.py --ledger probes_ledger.csv --exports <root> [<root> ...] --out <dir>

The ledger has one row per attempt (columns backend, run_logs; optional rep). Exports are paired through the
room name taken from the run-log folder name (`phaseb-<yyyyMMdd_HHmmss>`). For every attempt the coordinator
and participant exports are analysed with tools/analyze_access_probes.py (each probe is joined to the other
participant's samples through the phase index; clocks of different processes are never compared).
Outputs: probes.csv (one row per probe) and summary.csv (backend x condition: probes, propagated to the other
participant, still present on the writer's own replica after 1 s, writer still had native authority).
Attempts without a pair of exports are listed, not dropped.
"""
import argparse
import collections
import csv
import importlib.util
import pathlib
import sys


def load_module(name, filename):
    spec = importlib.util.spec_from_file_location(name, pathlib.Path(__file__).with_name(filename))
    module = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(module)
    return module


def main():
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument("--ledger", required=True)
    parser.add_argument("--exports", nargs="+", required=True)
    parser.add_argument("--out", required=True)
    args = parser.parse_args()

    campaign = load_module("analyze_campaign", "analyze_campaign.py")
    probes_tool = load_module("analyze_access_probes", "analyze_access_probes.py")
    exports = campaign.index_exports(args.exports)

    rows, missing = [], []
    for attempt in campaign.read_csv(args.ledger):
        found = exports.get(campaign.room_of(attempt.get("run_logs", "")), {})
        coord, part = found.get("Coordinator"), found.get("Participant")
        if not coord or not part:
            missing.append(f"{attempt['backend']} rep {attempt.get('rep', '?')}: exports missing")
            continue
        sides = [probes_tool.load(coord), probes_tool.load(part)]
        for index, (identity, events, transforms) in enumerate(sides):
            other_transforms = sides[1 - index][2]
            for probe_id, probe in sorted(probes_tool.probes_of(identity, events, transforms).items(), key=lambda kv: int(kv[0])):
                phase = probe.get("phase_index", -1)
                samples = [r for r in other_transforms
                           if r["kind"] == "Sample" and int(r["phase_index"]) in (phase, phase + 1)]
                column = "actual_euler_x" if probe.get("axis") == "x" else "actual_euler_z"
                other_column = "actual_euler_z" if probe.get("axis") == "x" else "actual_euler_x"
                angle = float(probe.get("angleDeg") or "nan")
                matches = [r for r in samples
                           if probes_tool.angular_distance(float(r[column]), angle) <= probes_tool.MATCH_TOLERANCE
                           and probes_tool.angular_distance(float(r[other_column]), 0.0) <= probes_tool.MATCH_TOLERANCE]
                rows.append({"backend": attempt["backend"], "rep": attempt.get("rep", ""), "prober": identity["export_role"],
                             "probe": probe_id, "condition": probe.get("condition", ""), "cycle": probe.get("cycle", ""),
                             "localNativeAuthority": probe.get("localNativeAuthority", ""),
                             "gatePermitted": probe.get("gatePermitted", ""),
                             "persistedLocally": probe.get("persistedLocally", ""),
                             "samples_checked": len(samples), "matching_samples": len(matches),
                             "propagated": str(len(matches) > 0) if samples else ""})

    out = pathlib.Path(args.out)
    out.mkdir(parents=True, exist_ok=True)
    campaign.write_csv(out / "probes.csv", rows)

    groups = collections.defaultdict(list)
    for r in rows:
        groups[(r["backend"], r["condition"])].append(r)
    summary = []
    for (backend, condition), g in sorted(groups.items()):
        summary.append({"backend": backend, "condition": condition, "probes": len(g),
                        "propagated": sum(r["propagated"] == "True" for r in g),
                        "no_sample_to_check": sum(r["propagated"] == "" for r in g),
                        "persisted_on_writer_replica": sum(str(r["persistedLocally"]).lower() == "true" for r in g),
                        "writer_had_native_authority": sum(str(r["localNativeAuthority"]).lower() == "true" for r in g),
                        "gate_permitted": sum(str(r["gatePermitted"]).lower() == "true" for r in g)})
    campaign.write_csv(out / "summary.csv", summary)

    lines = ["# Access-control probe runs", "",
             "| Backend | Condition | Probes | Propagated to other | Persisted on writer's replica | Writer had native authority | Gate permitted |",
             "| --- | --- | --- | --- | --- | --- | --- |"]
    for s in summary:
        lines.append(f"| {s['backend']} | {s['condition']} | {s['probes']} | {s['propagated']} | {s['persisted_on_writer_replica']} | "
                     f"{s['writer_had_native_authority']} | {s['gate_permitted']} |")
    lines += ["", f"Attempts without a pair of exports: {len(missing)}"] + [f"- {m}" for m in missing]
    (out / "report.md").write_text("\n".join(lines) + "\n", encoding="utf-8")
    print(f"probes={len(rows)} groups={len(summary)} missing={len(missing)} -> {out}")


if __name__ == "__main__":
    sys.exit(main())
