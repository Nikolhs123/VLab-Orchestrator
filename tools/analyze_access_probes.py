"""Summarize W03 access-control probes from the two participant exports of one run.

  py tools/analyze_access_probes.py <coordinator_run_dir> <participant_run_dir> [--out probes.csv]

Each probe (scenario_events AccessProbeApplied/Checked, transform_observations Probe rows) is reported
with the prober's access state at probe time, whether the probe persisted on the prober's own replica,
and whether it reached the other participant. Every probe has a unique signature (axis x or z and a
unique angle); legitimate manipulation changes only yaw. A probe counts as propagated when a sample of
the other participant in the probe's phase or the next shows that exact Euler value on that axis with
the other tilt axis near zero (within MATCH_TOLERANCE degrees). No clock values are compared across
processes.
"""
import argparse
import csv
import pathlib
import re
import sys

MATCH_TOLERANCE = 0.5


def read_csv(path):
    with open(path, newline="", encoding="utf-8-sig") as handle:
        return list(csv.DictReader(handle))


def angular_distance(a, b):
    d = (a - b) % 360.0
    return min(d, 360.0 - d)


def details_map(text):
    return dict(re.findall(r"(\w+)=([^\s]+)", text or ""))


def load(run_dir):
    run_dir = pathlib.Path(run_dir)
    identity = read_csv(run_dir / "participant_identity.csv")[0]
    events = read_csv(run_dir / "scenario_events.csv")
    transforms = read_csv(run_dir / "transform_observations.csv")
    return identity, events, transforms


def probes_of(identity, events, transforms):
    applied = {}
    for event in events:
        name = event.get("command") or event.get("event_type") or ""
        info = details_map(event.get("details"))
        if "probe" not in info:
            continue
        probe = applied.setdefault(info["probe"], {"observer": identity["observer_user_id"]})
        if name == "AccessProbeApplied":
            probe.update({k: info.get(k, "") for k in
                          ("condition", "axis", "angleDeg", "holder", "localIsHolder", "localNativeAuthority",
                           "nativeOwner", "gatePermitted")})
            probe["cycle"] = event.get("cycle", "")
        elif name == "AccessProbeChecked":
            probe["persistedLocally"] = info.get("probePersistedLocally", "")
            probe["localResidualDeg"] = info.get("localResidualDeg", "")
            probe["localCleanupApplied"] = info.get("localCleanupApplied", "")
    for row in transforms:
        match = re.match(r"Probe(\d+):", row["kind"])
        if match and match.group(1) in applied:
            applied[match.group(1)]["phase_index"] = int(row["phase_index"])
    return applied


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("coordinator")
    parser.add_argument("participant")
    parser.add_argument("--out")
    args = parser.parse_args()

    sides = [load(args.coordinator), load(args.participant)]
    if sides[0][0].get("session_name") != sides[1][0].get("session_name"):
        print("WARNING: session names differ; these exports may not belong to the same run.", file=sys.stderr)

    results = []
    for index, (identity, events, transforms) in enumerate(sides):
        other_transforms = sides[1 - index][2]
        for probe_id, probe in sorted(probes_of(identity, events, transforms).items(), key=lambda kv: int(kv[0])):
            # A match needs the probe angle on the probe axis, the other tilt axis near zero (a pure
            # probe signature; legitimate yaw rotation of a contaminated state sweeps through other
            # values with both tilts non-zero), and a sample in the probe's phase or the next one.
            phase = probe.get("phase_index", -1)
            samples = [r for r in other_transforms
                       if r["kind"] == "Sample" and int(r["phase_index"]) in (phase, phase + 1)]
            column = "actual_euler_x" if probe.get("axis") == "x" else "actual_euler_z"
            other_column = "actual_euler_z" if probe.get("axis") == "x" else "actual_euler_x"
            angle = float(probe.get("angleDeg") or "nan")
            matches = [r for r in samples
                       if angular_distance(float(r[column]), angle) <= MATCH_TOLERANCE
                       and angular_distance(float(r[other_column]), 0.0) <= MATCH_TOLERANCE]
            probe.update({
                "probe": probe_id,
                "other_samples_checked": len(samples),
                "other_matching_samples": len(matches),
                "propagated_to_other": str(len(matches) > 0) if samples else "",
            })
            results.append(probe)

    columns = ["observer", "probe", "cycle", "phase_index", "condition", "axis", "angleDeg", "holder",
               "localIsHolder", "localNativeAuthority", "nativeOwner", "gatePermitted", "persistedLocally",
               "localResidualDeg", "localCleanupApplied", "other_samples_checked", "other_matching_samples",
               "propagated_to_other"]
    writer = csv.DictWriter(open(args.out, "w", newline="", encoding="utf-8") if args.out else sys.stdout,
                            fieldnames=columns, extrasaction="ignore")
    writer.writeheader()
    for row in results:
        writer.writerow(row)
    return 0


if __name__ == "__main__":
    sys.exit(main())
