#!/usr/bin/env python3
"""Counts and an id lint for Blueprint_Issues_Tracker.md — DERIVED from the rows, never stored.

⭐ User, `2026-11-08`: the stored counts table is REMOVED from the tracker — it conflicted on almost every
merge (every lane rewrote the same lines) and it only ever restated the rows. The counts are a VIEW now:

  python3 scripts/tracker-counts.py            # print the counts (by size, by id family)
  python3 scripts/tracker-counts.py --check    # lint: exit 1 on a NEW duplicate id or a NEW letter-suffixed id

Counting rules:
  * an issue row is a top-level "- [ ] " / "- [x] " line whose FIRST bold token is an id of ANY family
  * a row's size is its FIRST `RW-x` tag (unknown letter = OTHER; none = WIRING)
  * a done row marked REFUTED is counted apart, not in the totals

Lint rules (--check):
  * one id = one row. An id may repeat only with a different lane tag right after it — `**CE-459** (ui)` —
    the protocol's way of telling apart ids allocated before the per-lane blocks. ⛔ Never renumber.
  * no letter suffix on an id from the per-lane blocks (`CE-1000` and above; user `2026-09-12`)
  * KNOWN_DUPLICATES are collisions that predate this lint; each stays until a lane tags it apart.
"""
import re
import sys
from collections import Counter, defaultdict
from pathlib import Path

TRACKER = Path(__file__).resolve().parent.parent / "docs" / "blueprints" / "Blueprint_Issues_Tracker.md"
ORDER = ["WIRING", "RW-XS", "RW-S", "RW-L", "RW-M", "RW-H", "RW-XL", "OTHER"]
ROW = re.compile(r"^- \[( |x)\] (?:~~)?\*\*\[?(([A-Z]{2,3})-(\d+)([a-z]?))\]?(?:\]\([^)]*\))?\*\*(?:~~)?"
                 r"( \((?:ui|behaviors|backend|coordinator)\))?")

# Measured 2026-11-08 when the lint replaced the stored table. Legacy (pre-block) collisions, plus two in ui's
# block (CE-1461, CE-1462: an OPEN row and a BUILT row each — NOTE_Backend_To_Ui_Tracker_Id_Collisions.md).
KNOWN_DUPLICATES = {"CE-011", "CE-170", "CE-215", "CE-217", "CE-297", "CE-298", "CE-299", "CE-1461", "CE-1462"}


def scan(text):
    counts, families, lines = Counter(), Counter(), defaultdict(list)
    refuted = rows = 0
    suffixed = []
    for n, line in enumerate(text.split("\n"), 1):
        m = ROW.match(line)
        if not m:
            continue
        rows += 1
        state, rid, fam, num, suf, lane = m.groups()
        lines[rid + (lane or "")].append(n)
        if suf and int(num) >= 1000:
            suffixed.append((rid, n))
        tag = re.search(r"`(RW-[A-Z]+)`", line)
        bucket = (tag.group(1) if tag.group(1) in ORDER else "OTHER") if tag else "WIRING"
        if state == "x" and "REFUTED" in line:
            refuted += 1
            continue
        counts[(state, bucket)] += 1
        families[(state, fam)] += 1
    return counts, families, refuted, rows, lines, suffixed


def main():
    text = TRACKER.read_text(encoding="utf-8")
    counts, families, refuted, rows, lines, suffixed = scan(text)
    to = sum(v for (s, _), v in counts.items() if s == " ")
    td = sum(v for (s, _), v in counts.items() if s == "x")

    if "--check" not in sys.argv:
        print("| Size | Open | Done |\n|---|---:|---:|")
        for b in ORDER:
            print(f"| `{b}` | {counts.get((' ', b), 0)} | {counts.get(('x', b), 0)} |")
        print(f"| **Total** | **{to}** | **{td}** |\n")
        print("| Id family | Open | Done |\n|---|---:|---:|")
        for fam in sorted({f for _, f in families}):
            print(f"| `{fam}-` | {families.get((' ', fam), 0)} | {families.get(('x', fam), 0)} |")
        print(f"\n# rows: {rows}  (open {to} + done {td} + refuted {refuted})")
        return 0

    bad = []
    for key, ns in sorted(lines.items()):
        if len(ns) > 1 and key not in KNOWN_DUPLICATES:
            bad.append(f"{key}: one id on {len(ns)} rows (lines {', '.join(map(str, ns))}) — take a new id from your block")
    for rid, n in suffixed:
        bad.append(f"{rid}: letter suffix on a block id (line {n}) — ids are plain numbers")
    if bad:
        print("TRACKER ID LINT FAILED:", file=sys.stderr)
        for b in bad:
            print("  " + b, file=sys.stderr)
        return 1
    print(f"tracker ids OK — {rows} rows, open {to} / done {td} (+{refuted} refuted), "
          f"{len(KNOWN_DUPLICATES)} known legacy duplicates")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
