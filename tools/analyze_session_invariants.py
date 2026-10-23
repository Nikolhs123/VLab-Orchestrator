"""Check queue and pairing invariants over a tree of evaluation exports.

  py tools/analyze_session_invariants.py <exports_root> [--out invariants.csv]

For every export (a folder with participant_identity.csv):
  * required scenario assertions: all must pass;
  * queue_transitions.csv is replayed through an FCFS state machine. Every row must agree with the replay:
    holder_before/holder_after, the post-decision queue_snapshot, a grant after a release or timeout goes to
    the head of the queue, a release comes from the holder, a queued user is appended at the tail.
    (A release that grants the next user is two rows sharing the post-decision snapshot; the snapshot is
    checked on the grant row.)
    An export that starts mid-session (e.g. after queue-authority migration) is replayed from the first
    row's holder with the queue treated as unknown until it is observed empty.
Exports are paired by session_run_id (protocol 1.6+): each session must have exactly one Coordinator export
and the expected Participant exports. No clock values are compared across processes; ordering is the
authority's own row order. Exit code 1 if any invariant fails.
"""
import argparse
import collections
import csv
import pathlib
import sys

QUEUE_EVENTS = {
    "AccessRequested", "AccessGranted", "AccessQueued", "AccessDenied", "RemovedFromQueue",
    "AccessReleased", "TimeoutReleased", "TimeoutGrantedNext",
}


def read_csv(path):
    if not path.exists():
        return []
    with open(path, newline="", encoding="utf-8-sig") as handle:
        return list(csv.DictReader(handle))


def none(value):
    return not value or value.strip().lower() == "none"


def split_queue(text):
    return [entry for entry in (text or "").split("|") if entry]


def replay_queue(rows):
    """Return a list of violation strings for one authority's queue_transitions rows."""
    violations = []
    rows = [r for r in rows if r.get("event_type") in QUEUE_EVENTS]
    if not rows:
        return violations

    holder = None if none(rows[0]["holder_before"]) else rows[0]["holder_before"]
    queue = [] if holder is None else None  # None = unknown (export started mid-session)
    release_pending = False  # a release/timeout happened and the next grant must take the queue head

    for index, row in enumerate(rows, start=2):  # csv line numbers (header is line 1)
        event, user = row["event_type"], row["requester_user_id"]
        before = None if none(row["holder_before"]) else row["holder_before"]
        after = None if none(row["holder_after"]) else row["holder_after"]
        snapshot = split_queue(row.get("queue_snapshot"))
        where = f"line {index} {event}({user})"

        if event == "AccessRequested":
            continue
        if before != holder:
            violations.append(f"{where}: holder_before={before} but replay holder={holder}")

        if event == "AccessGranted":
            if holder is not None:
                violations.append(f"{where}: grant while {holder} holds")
            if queue is not None and release_pending and queue and queue[0] != user:
                violations.append(f"{where}: grant after release must go to queue head {queue[0]}")
            if queue is not None and user in queue:
                queue.remove(user)
            holder, release_pending = user, False
        elif event == "AccessQueued":
            if user == holder:
                violations.append(f"{where}: holder queued")
            if queue is not None:
                if user in queue:
                    violations.append(f"{where}: duplicate queue entry")
                queue.append(user)
        elif event == "RemovedFromQueue":
            if queue is not None:
                if user not in queue:
                    violations.append(f"{where}: removed user not in queue")
                else:
                    queue.remove(user)
        elif event in ("AccessReleased", "TimeoutReleased"):
            if holder is None:
                violations.append(f"{where}: release with no holder")
            elif event == "AccessReleased" and user != holder:
                violations.append(f"{where}: release by non-holder (holder {holder})")
            holder, release_pending = None, True
        elif event == "TimeoutGrantedNext":
            if queue is not None and (not queue or queue[0] != user):
                violations.append(f"{where}: timeout grant must go to queue head {queue[0] if queue else None}")
            if queue is not None and user in queue:
                queue.remove(user)
            holder, release_pending = user, False

        if after != holder:
            violations.append(f"{where}: holder_after={after} but replay holder={holder}")
        # A release that grants the next user is recorded as two rows that both carry the queue snapshot
        # after the whole decision; the snapshot is therefore checked on the grant row only.
        composite_release = event == "AccessReleased" and "granted next=" in (row.get("details") or "")
        if queue is not None and event != "AccessDenied" and not composite_release and snapshot != queue:
            violations.append(f"{where}: queue_snapshot={snapshot} but replay queue={queue}")
        if queue is None and holder is None and not snapshot:
            queue = []  # observed empty: from here on the queue is known

    return violations


def analyze_export(folder):
    identity = read_csv(folder / "participant_identity.csv")[0]
    assertions = read_csv(folder / "scenario_assertions.csv")
    failed = [a["assertion_id"] for a in assertions
              if a.get("required_for_validity", "True") == "True" and a.get("passed") != "True"]
    transitions = read_csv(folder / "queue_transitions.csv")
    queue_violations = replay_queue(transitions)

    def per_user(event_type):
        counts = collections.Counter(r["requester_user_id"] for r in transitions if r.get("event_type") == event_type)
        return ";".join(f"{user}={count}" for user, count in sorted(counts.items()))
    return {
        "folder": str(folder),
        "backend": identity.get("backend", ""),
        "scenario_id": identity.get("scenario_id", ""),
        "session_run_id": identity.get("session_run_id", ""),
        "export_role": identity.get("export_role", ""),
        "observer_user_id": identity.get("observer_user_id", ""),
        "required_assertions": len([a for a in assertions if a.get("required_for_validity", "True") == "True"]),
        "failed_required_assertions": ";".join(failed),
        "queue_rows": len(transitions),
        # Arrival order as applied by this export's authority: a request that arrived while another
        # participant held the instrument is an AccessQueued row for the later arrival.
        "grants_by_user": per_user("AccessGranted"),
        "queued_by_user": per_user("AccessQueued"),
        "queue_violations": " | ".join(queue_violations),
    }


def main():
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument("root")
    parser.add_argument("--out")
    parser.add_argument("--expected-participants", type=int, default=1,
                        help="Participant exports expected per session (default 1, two-participant runs)")
    args = parser.parse_args()

    folders = sorted({p.parent for p in pathlib.Path(args.root).rglob("participant_identity.csv")
                      if ".staging" not in p.parts})
    results = [analyze_export(folder) for folder in folders]

    sessions = collections.defaultdict(list)
    for result in results:
        if result["session_run_id"]:
            sessions[result["session_run_id"]].append(result)

    pairing_issues = []
    for session_id, members in sorted(sessions.items()):
        roles = collections.Counter(m["export_role"] for m in members)
        if roles["Coordinator"] != 1 or roles["Participant"] != args.expected_participants:
            pairing_issues.append(f"{session_id}: Coordinator={roles['Coordinator']} Participant={roles['Participant']}")
        if len({(m["backend"], m["scenario_id"]) for m in members}) > 1:
            pairing_issues.append(f"{session_id}: backend/scenario differ between exports")

    if args.out:
        with open(args.out, "w", newline="", encoding="utf-8") as handle:
            writer = csv.DictWriter(handle, fieldnames=list(results[0].keys()) if results else ["folder"])
            writer.writeheader()
            writer.writerows(results)

    failing = [r for r in results if r["failed_required_assertions"] or r["queue_violations"]]
    unpaired = [r for r in results if not r["session_run_id"]]
    print(f"exports={len(results)} sessions={len(sessions)} without_session_run_id={len(unpaired)}")
    for r in failing:
        print(f"FAIL {r['folder']}")
        if r["failed_required_assertions"]:
            print(f"     assertions: {r['failed_required_assertions']}")
        if r["queue_violations"]:
            print(f"     queue: {r['queue_violations']}")
    for issue in pairing_issues:
        print(f"PAIRING {issue}")
    print("ALL INVARIANTS HOLD" if not failing and not pairing_issues else
          f"{len(failing)} export(s) and {len(pairing_issues)} session(s) with violations")
    return 1 if failing or pairing_issues else 0


if __name__ == "__main__":
    sys.exit(main())
