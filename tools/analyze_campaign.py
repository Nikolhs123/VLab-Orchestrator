"""Summarize a campaign ledger and its exports with the definitions of CAMPAIGN_PLAN.md.

  py tools/analyze_campaign.py --ledger campaign_attempts.csv --exports <root> [<root> ...] --out <dir>

The ledger has one row per attempt (columns backend, scenario, host_status or status, run_logs; optional
block, position). Exports are paired with an attempt through the session name, which equals the room name
`phaseb-<yyyyMMdd_HHmmss>` taken from the attempt's run-log folder name. Nothing is dropped: attempts
without exports are invalid executions. Outputs: attempts.csv (one row per attempt), cells.csv (one row
per backend x scenario) and report.md.

Valid execution: host status VALID; coordinator and participant exports present with the same
session_run_id; no failed required assertion in either; FCFS replay of queue_transitions.csv clean
(tools/analyze_session_invariants.py); authority measurement counts reconcile in both exports.
Outcome metrics come from valid executions only. Clocks of different processes are never subtracted.
"""
import argparse
import collections
import csv
import importlib.util
import itertools
import math
import pathlib
import random
import re
import statistics
import sys

TOLERANCE_DEG = 2.0
BOOTSTRAP_RESAMPLES = 10000
BOOTSTRAP_SEED = 20261024
LOG_ERROR = re.compile(r"Exception|QueueIdentityRejected|NativeAuthorityRequestRejected|NativeAuthorityRevokedByServer")


def read_csv(path):
    path = pathlib.Path(path)
    if not path.exists():
        return []
    with open(path, newline="", encoding="utf-8-sig") as handle:
        return list(csv.DictReader(handle))


def load_replay():
    spec = importlib.util.spec_from_file_location(
        "invariants", pathlib.Path(__file__).with_name("analyze_session_invariants.py"))
    module = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(module)
    return module.replay_queue


def binom_cdf(k, n, p):
    return sum(math.comb(n, i) * p ** i * (1 - p) ** (n - i) for i in range(0, k + 1))


def clopper_pearson(k, n, alpha=0.05):
    """Exact two-sided interval for a binomial proportion (bisection on the binomial CDF)."""
    if n == 0:
        return (float("nan"), float("nan"))

    def solve(target, decreasing):
        lo, hi = 0.0, 1.0
        for _ in range(100):
            mid = (lo + hi) / 2
            value = binom_cdf(k, n, mid) if decreasing else 1 - binom_cdf(k - 1, n, mid)
            # binom_cdf(k, n, p) decreases in p; 1 - binom_cdf(k-1, n, p) increases in p
            if decreasing:
                if value > target:
                    lo = mid
                else:
                    hi = mid
            else:
                if value < target:
                    lo = mid
                else:
                    hi = mid
        return (lo + hi) / 2

    lower = 0.0 if k == 0 else solve(alpha / 2, decreasing=False)
    upper = 1.0 if k == n else solve(alpha / 2, decreasing=True)
    return (lower, upper)


def percentile(values, q):
    if not values:
        return float("nan")
    ordered = sorted(values)
    position = (len(ordered) - 1) * q
    low, high = math.floor(position), math.ceil(position)
    return ordered[low] + (ordered[high] - ordered[low]) * (position - low)


def bootstrap_median_ci(values, seed=BOOTSTRAP_SEED):
    if not values:
        return (float("nan"), float("nan"))
    rng = random.Random(seed)
    medians = sorted(
        statistics.median(rng.choices(values, k=len(values))) for _ in range(BOOTSTRAP_RESAMPLES))
    return (percentile(medians, 0.025), percentile(medians, 0.975))


def index_exports(roots):
    exports = collections.defaultdict(dict)
    for root in roots:
        for identity in pathlib.Path(root).rglob("participant_identity.csv"):
            if ".staging" in identity.parts:
                continue
            row = read_csv(identity)[0]
            exports[row.get("session_name", "")][row.get("export_role", "")] = identity.parent
    return exports


ROOM_PREFIX = ["phaseb-"]


def room_of(run_logs):
    match = re.search(r"_(\d{8}_\d{6})$", run_logs.rstrip("\\/"))
    return ROOM_PREFIX[0] + match.group(1) if match else ""


def manifest_value(folder, key, default=""):
    for row in read_csv(folder / "execution_manifest.csv"):
        if row.get("key") == key:
            return row.get("value", default)
    return default


def failed_required(folder):
    return [a["assertion_id"] for a in read_csv(folder / "scenario_assertions.csv")
            if a.get("required_for_validity", "True") == "True" and a.get("passed") != "True"]


def reconciles(folder):
    rows = read_csv(folder / "authority_measurement_summary.csv")
    return bool(rows) and all(r.get("counts_reconcile") == "True" for r in rows)


def orientation(folder):
    """Boundary rows: (max error, share above tolerance) for actor and non-actor observer."""
    rows = [r for r in read_csv(folder / "transform_observations.csv") if r.get("kind") in ("PhaseStart", "PhaseEnd")]
    result = {}
    for label, is_actor in (("actor", "True"), ("other", "False")):
        errors = [float(r["angle_error_deg"]) for r in rows if r.get("observer_is_actor") == is_actor]
        result[label] = (max(errors) if errors else None,
                         sum(e > TOLERANCE_DEG for e in errors) / len(errors) if errors else None, len(errors))
    return result


def frame_stats(folder):
    values = [float(r["frame_time_ms"]) for r in read_csv(folder / "runtime_performance.csv") if r.get("frame_time_ms")]
    return (statistics.median(values), percentile(values, 0.95)) if values else (None, None)


def log_errors(run_logs):
    total = 0
    for name in ("host.log", "client.log"):
        path = pathlib.Path(run_logs) / name
        if path.exists():
            with open(path, encoding="utf-8", errors="replace") as handle:
                total += sum(1 for line in handle if LOG_ERROR.search(line))
    return total


def analyze_attempt(row, exports, replay):
    status = row.get("host_status") or row.get("status") or ""
    out = {"block": row.get("block", ""), "position": row.get("position", ""), "backend": row["backend"],
           "scenario": row["scenario"], "host_status": status, "run_logs": row.get("run_logs", ""),
           "valid_execution": False, "reason": ""}
    found = exports.get(room_of(row.get("run_logs", "")), {})
    coord, part = found.get("Coordinator"), found.get("Participant")
    reasons = []
    if status != "VALID":
        reasons.append("host status " + status)
    if not coord or not part:
        reasons.append("export missing")
    else:
        ids = {read_csv(f / "participant_identity.csv")[0].get("session_run_id", "") for f in (coord, part)}
        if len(ids) != 1 or "" in ids:
            reasons.append("session_run_id differs or missing")
        for label, folder in (("coordinator", coord), ("participant", part)):
            failed = failed_required(folder)
            if failed:
                reasons.append(f"{label} failed assertions: {';'.join(failed)}")
            if not reconciles(folder):
                reasons.append(f"{label} counts do not reconcile")
        violations = replay(read_csv(coord / "queue_transitions.csv"))
        if violations:
            reasons.append("queue replay violations: " + str(len(violations)))
    if coord:
        # exports from software without the key were all produced host-first
        out["role_order"] = manifest_value(coord, "protocol.role_order", "host-first")
        if row.get("role_order") and row["role_order"] != out["role_order"]:
            reasons.append("role order in manifest (" + out["role_order"] + ") differs from the ledger (" + row["role_order"] + ")")
    out["reason"] = " | ".join(reasons)
    out["valid_execution"] = not reasons
    out["log_error_lines"] = log_errors(row["run_logs"]) if row.get("run_logs") else ""
    if not coord or not part:
        return out

    summary = (read_csv(coord / "authority_measurement_summary.csv") or [{}])[0]
    out["coord_initiated"] = summary.get("local_initiated", "")
    out["coord_completed"] = summary.get("local_completed", "")
    psummary = (read_csv(part / "authority_measurement_summary.csv") or [{}])[0]
    out["part_initiated"] = psummary.get("local_initiated", "")
    out["part_completed"] = psummary.get("local_completed", "")

    transfers = read_csv(coord / "authority_transfers.csv")
    available = [float(r["transfer_latency_ms"]) for r in transfers if r.get("latency_available") == "True"]
    out["transfer_rows"] = len(transfers)
    out["duration_rows"] = len(available)
    out["zero_duration_rows"] = sum(1 for v in available if v == 0.0)
    out["no_local_request_rows"] = len(transfers) - len(available)
    positive = [v for v in available if v > 0.0]
    out["transfer_median_ms"] = statistics.median(positive) if positive else ""
    queue = (read_csv(coord / "queue_metrics_summary.csv") or [{}])[0]
    out["median_grant_delay_ms"] = queue.get("median_grant_delay_ms", "")

    for label, folder in (("coord", coord), ("part", part)):
        for who, (mx, share, count) in orientation(folder).items():
            out[f"{label}_{who}_boundary_rows"] = count
            out[f"{label}_{who}_max_err_deg"] = "" if mx is None else mx
            out[f"{label}_{who}_share_over_tol"] = "" if share is None else share
        median, p95 = frame_stats(folder)
        out[f"{label}_frame_median_ms"] = "" if median is None else median
        out[f"{label}_frame_p95_ms"] = "" if p95 is None else p95
    return out


def numeric(rows, key):
    values = []
    for r in rows:
        v = r.get(key, "")
        if v not in ("", None):
            values.append(float(v))
    return values


def fmt(value, digits=3):
    return "" if value is None or (isinstance(value, float) and math.isnan(value)) else f"{value:.{digits}f}"


def summarize(attempts):
    cells = []
    cell_key = lambda a: (a["backend"], a["scenario"])
    for (backend, scenario), grouped in itertools.groupby(sorted(attempts, key=cell_key), key=cell_key):
        group = list(grouped)
        valid = [a for a in group if a["valid_execution"]]
        low, high = clopper_pearson(len(valid), len(group))
        row = {"backend": backend, "scenario": scenario, "attempts": len(group), "valid": len(valid),
               "invalid": len(group) - len(valid), "valid_prop": len(valid) / len(group),
               "valid_ci_low": low, "valid_ci_high": high}
        for key in ("transfer_median_ms", "median_grant_delay_ms", "coord_frame_median_ms", "part_frame_median_ms"):
            values = numeric(valid, key)
            low_b, high_b = bootstrap_median_ci(values)
            row[key + "_median"] = statistics.median(values) if values else ""
            row[key + "_iqr"] = (percentile(values, 0.75) - percentile(values, 0.25)) if values else ""
            row[key + "_ci_low"], row[key + "_ci_high"] = low_b, high_b
        for key in ("coord_actor_max_err_deg", "coord_other_max_err_deg", "part_actor_max_err_deg", "part_other_max_err_deg"):
            values = numeric(valid, key)
            row[key + "_max"] = max(values) if values else ""
        row["runs_with_boundary_rows_over_tol"] = sum(
            1 for a in valid if any(float(a.get(k) or 0) > 0 for k in a if k.endswith("_share_over_tol")))
        row["initiated_total"] = int(sum(numeric(valid, "coord_initiated")))
        row["completed_total"] = int(sum(numeric(valid, "coord_completed")))
        row["zero_duration_rows"] = int(sum(numeric(valid, "zero_duration_rows")))
        row["no_local_request_rows"] = int(sum(numeric(valid, "no_local_request_rows")))
        row["log_error_lines"] = int(sum(numeric(group, "log_error_lines")))
        cells.append(row)
    return cells


def write_csv(path, rows):
    keys = []
    for r in rows:
        for k in r:
            if k not in keys:
                keys.append(k)
    with open(path, "w", newline="", encoding="utf-8") as handle:
        writer = csv.DictWriter(handle, fieldnames=keys)
        writer.writeheader()
        writer.writerows(rows)


def main():
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument("--ledger", required=True)
    parser.add_argument("--exports", nargs="+", required=True)
    parser.add_argument("--out", required=True)
    parser.add_argument("--logs-root", default="",
                        help="folder with the run-log folders; run_logs of the ledger are remapped to it by folder name")
    parser.add_argument("--room-prefix", default="phaseb-",
                        help="room name prefix of the campaign (phaseb- for A and B, phasec- for C)")
    args = parser.parse_args()
    ROOM_PREFIX[0] = args.room_prefix

    replay = load_replay()
    exports = index_exports(args.exports)
    rows = read_csv(args.ledger)
    if args.logs_root:
        for row in rows:
            name = re.split(r"[\\/]", row.get("run_logs", "").rstrip("\\/"))[-1]
            if name:
                row["run_logs"] = str(pathlib.Path(args.logs_root) / name)
    attempts = [analyze_attempt(row, exports, replay) for row in rows]
    cells = summarize(attempts)

    out = pathlib.Path(args.out)
    out.mkdir(parents=True, exist_ok=True)
    write_csv(out / "attempts.csv", attempts)
    write_csv(out / "cells.csv", cells)

    lines = [f"# Campaign summary ({len(attempts)} attempts, {sum(a['valid_execution'] for a in attempts)} valid executions)", "",
             "| Backend | Scenario | Attempts | Valid | Valid proportion (95% CP) | Transfer median ms [95% CI] | Grant delay median ms | Max boundary error deg (actor / other) | Runs with rows > tol |",
             "| --- | --- | --- | --- | --- | --- | --- | --- | --- |"]
    for c in cells:
        lines.append("| {} | {} | {} | {} | {:.2f} ({:.2f}-{:.2f}) | {} [{}-{}] | {} | {} / {} | {} |".format(
            c["backend"], c["scenario"], c["attempts"], c["valid"], c["valid_prop"], c["valid_ci_low"], c["valid_ci_high"],
            fmt(c["transfer_median_ms_median"] if c["transfer_median_ms_median"] != "" else None, 1),
            fmt(c["transfer_median_ms_ci_low"], 1), fmt(c["transfer_median_ms_ci_high"], 1),
            fmt(c["median_grant_delay_ms_median"] if c["median_grant_delay_ms_median"] != "" else None, 1),
            fmt(max([x for x in (c["coord_actor_max_err_deg_max"], c["part_actor_max_err_deg_max"]) if x != ""], default=None), 2),
            fmt(max([x for x in (c["coord_other_max_err_deg_max"], c["part_other_max_err_deg_max"]) if x != ""], default=None), 2),
            c["runs_with_boundary_rows_over_tol"]))
    invalid = [a for a in attempts if not a["valid_execution"]]
    lines += ["", f"Invalid executions: {len(invalid)}"]
    lines += [f"- {a['backend']} {a['scenario']} (block {a['block']}): {a['reason']}" for a in invalid]
    (out / "report.md").write_text("\n".join(lines) + "\n", encoding="utf-8")
    print(f"attempts={len(attempts)} valid={sum(a['valid_execution'] for a in attempts)} cells={len(cells)} -> {out}")


if __name__ == "__main__":
    sys.exit(main())

