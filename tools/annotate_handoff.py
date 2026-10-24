"""Produce an annotated event record of one ownership phase (request, logical grant, authority acquisition,
manipulation, release, authority removal, next grant) from the exports of one run.

  py tools/annotate_handoff.py --actor <export folder of the phase's actor> --queue <coordinator export folder> --phase <phase_index> [--out record.md]

Rows come from the actor's own export (scenario events, authority measurement lifecycle, native-authority
observations, per-phase transform rows) and from the coordinator's queue_transitions.csv (the authoritative
queue). All rows are placed on one list ordered by UTC timestamp; this is legitimate here only because both
processes ran on the same machine, and the table says so. It is a reading aid, not a way to compute durations
between processes: durations in the exports come from monotonic clocks of one process.
"""
import argparse
import csv
import datetime
import pathlib
import sys


def read_csv(path):
    path = pathlib.Path(path)
    if not path.exists():
        return []
    with open(path, newline="", encoding="utf-8-sig") as handle:
        return list(csv.DictReader(handle))


def parse_time(text):
    text = (text or "").strip()
    if not text:
        return None
    if text.endswith("Z"):
        text = text[:-1] + "+00:00"
    if "." in text:
        head, rest = text.split(".", 1)
        digits = ""
        for ch in rest:
            if ch.isdigit():
                digits += ch
            else:
                break
        tail = rest[len(digits):]
        text = head + "." + digits[:6].ljust(6, "0") + tail
    return datetime.datetime.fromisoformat(text)


def main():
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument("--actor", required=True)
    parser.add_argument("--queue", required=True)
    parser.add_argument("--phase", type=int, required=True)
    parser.add_argument("--before", type=float, default=2.0)
    parser.add_argument("--after", type=float, default=2.0)
    parser.add_argument("--out")
    args = parser.parse_args()

    actor, queue = pathlib.Path(args.actor), pathlib.Path(args.queue)
    identity = read_csv(actor / "participant_identity.csv")[0]
    transforms = read_csv(actor / "transform_observations.csv")
    phase_rows = [r for r in transforms if r["kind"] in ("PhaseStart", "PhaseEnd") and int(r["phase_index"]) == args.phase]
    start = next((parse_time(r["timestamp_utc"]) for r in phase_rows if r["kind"] == "PhaseStart"), None)
    end = next((parse_time(r["timestamp_utc"]) for r in phase_rows if r["kind"] == "PhaseEnd"), None)
    if start is None or end is None:
        print("phase not found in the actor export", file=sys.stderr)
        return 1
    low = start - datetime.timedelta(seconds=args.before)
    high = end + datetime.timedelta(seconds=args.after)

    events = []

    def add(source, stamp, event, detail):
        t = parse_time(stamp)
        if t is not None and low <= t <= high:
            events.append((t, source, event, detail))

    for r in read_csv(queue / "queue_transitions.csv"):
        add("queue (coordinator)", r["timestamp_utc"], r["event_type"],
            f"requester={r['requester_user_id']} holder {r['holder_before']} -> {r['holder_after']} queue=[{r['queue_snapshot']}]")
    for r in read_csv(actor / "scenario_events.csv"):
        add("scenario (actor)", r["timestamp_utc"], r["command"], f"role={r['role']} cycle={r['cycle']} holder={r['actual_holder']}")
    for r in read_csv(actor / "authority_measurement_lifecycle.csv"):
        add("authority measurement (actor)", r["timestamp_utc"], r["event_type"], f"id={r['measurement_id'][:8]} {r['details'][:90]}")
    for r in read_csv(actor / "native_authority_observations.csv"):
        add("native authority (actor)", r["timestamp_utc"], "StateChange",
            f"holder={r['logical_holder']} nativeOwner={r['native_owner']} logicalAccess={r['local_logical_access']} nativeAuthority={r['local_native_authority']}")
    for r in phase_rows:
        add("transform (actor)", r["timestamp_utc"], r["kind"],
            f"expected yaw={float(r['expected_euler_y']):.2f} actual yaw={float(r['actual_euler_y']):.2f} error={r['angle_error_deg']} deg")

    events.sort(key=lambda e: e[0])
    lines = [f"# Annotated record: phase {args.phase} of {identity['backend']} / {identity['scenario_id']}", "",
             f"Actor export `{actor.name}` (observer {identity['observer_user_id']}, role {identity['export_role']}); "
             f"queue rows from `{queue.name}`. Time is milliseconds since this phase started on the actor "
             "(rows of the two processes are ordered by UTC stamp because both ran on the same machine; "
             "this is a reading aid, not a duration measurement).", "",
             "| ms | Source | Event | Detail |", "| ---: | --- | --- | --- |"]
    for t, source, event, detail in events:
        lines.append(f"| {(t - start).total_seconds() * 1000:.0f} | {source} | {event} | {detail} |")
    text = "\n".join(lines) + "\n"
    if args.out:
        pathlib.Path(args.out).write_text(text, encoding="utf-8")
    print(text)
    return 0


if __name__ == "__main__":
    sys.exit(main())
