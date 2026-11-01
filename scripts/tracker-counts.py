#!/usr/bin/env python3
"""Derive the Blueprint_Issues_Tracker.md counts table from the rows themselves.

Why this exists
---------------
The counts table has been wrong in the done column in three consecutive batches (29, 30, 31),
each time by a different mechanism, and each time the OPEN column was correct:

  29 — inherited drift that predated the batch
  30 — a row ticked done but never added to its complexity column
  31 — two rows moved open->done; open was decremented by 2, done incremented by 1

The shape of the error is always the same: maintaining two representations of one fact by hand.
So stop doing that. Run this, paste the table.

  python3 scripts/tracker-counts.py            # print the table
  python3 scripts/tracker-counts.py --check    # exit 1 if the file's table disagrees

Counting rules:
  * an issue row is a top-level "- [ ] " / "- [x] " line whose FIRST bold token is an id of ANY family
    (BP- CE- HN- AX- MA- MD- MX- QA- ST- TM- …). ⭐ CE-073 (`2026-11-01`): it used to count BP- rows only,
    so every `--check` OK ignored the majority of the file.
  * a row's complexity is its FIRST `RW-x` tag: the size buckets in ORDER; an unknown letter buckets as
    OTHER (a handful of rows used RW-N/O/P/Q/W as private labels); rows with none are counted under WIRING
  * a second table breaks the same rows down by id family (open / done)
  * the *(refuted on verification)* row sits OUTSIDE the Total, so the checkbox tally of
    done rows is exactly one higher than the Total. That is correct, not a discrepancy.
"""
import re
import sys
from collections import Counter
from pathlib import Path

TRACKER = Path(__file__).resolve().parent.parent / "docs" / "blueprints" / "Blueprint_Issues_Tracker.md"
ORDER = ["WIRING", "RW-XS", "RW-S", "RW-L", "RW-M", "RW-H", "RW-XL", "OTHER"]


def scan(text):
    """Return (counter keyed by (state, bucket), refuted_count, total_rows, counter keyed by (state, family))."""
    counts = Counter()
    families = Counter()
    refuted = 0
    rows = 0
    for line in text.split("\n"):
        m = re.match(r"^- \[( |x)\] (?:~~)?\*\*\[?([A-Z]{2,3})-\d+", line)
        if not m:
            continue
        rows += 1
        state = m.group(1)
        tag = re.search(r"`(RW-[A-Z]+)`", line)         # FIRST tag wins
        bucket = (tag.group(1) if tag.group(1) in ORDER else "OTHER") if tag else "WIRING"
        # A refuted row is a done row struck through and marked REFUTED; it is excluded
        # from the Total by the table's own convention.
        if state == "x" and "REFUTED" in line:
            refuted += 1
            continue
        counts[(state, bucket)] += 1
        families[(state, m.group(2))] += 1
    return counts, refuted, rows, families


def render(counts, refuted):
    out = ["| Complexity | Open | Done |", "|---|---:|---:|"]
    to = td = 0
    for bucket in ORDER:
        o = counts.get((" ", bucket), 0)
        d = counts.get(("x", bucket), 0)
        to += o
        td += d
        label = f"`{bucket}`"
        out.append(f"| {label} | {o} | {d} |")
    out.append(f"| **Total** | **{to}** | **{td}** |")
    if refuted:
        out.append(f"| *(refuted on verification)* | | *{refuted}* |")
    return "\n".join(out), to, td


def render_families(families):
    out = ["| Id family | Open | Done |", "|---|---:|---:|"]
    for fam in sorted({f for _, f in families}):
        out.append(f"| `{fam}-` | {families.get((' ', fam), 0)} | {families.get(('x', fam), 0)} |")
    return "\n".join(out)


def main():
    text = TRACKER.read_text(encoding="utf-8")
    counts, refuted, rows, families = scan(text)
    table, to, td = render(counts, refuted)

    check = "--check" in sys.argv
    if not check:
        print(table)
        print()
        print(render_families(families))
        print()
        print(f"# rows scanned: {rows}  (open {to} + done {td} + refuted {refuted})")
        return 0

    # --check: compare against what the file currently claims.
    bad = []
    for bucket in ORDER:
        m = re.search(rf"^\| `{re.escape(bucket)}` \| (\d+) \| (\d+) \|", text, re.M)
        if not m:
            bad.append(f"{bucket}: row not found in the table")
            continue
        want = (counts.get((" ", bucket), 0), counts.get(("x", bucket), 0))
        got = (int(m.group(1)), int(m.group(2)))
        if want != got:
            bad.append(f"{bucket}: table says open={got[0]} done={got[1]}, rows say open={want[0]} done={want[1]}")

    m = re.search(r"^\| \*\*Total\*\* \| \*\*(\d+)\*\* \| \*\*(\d+)\*\* \|", text, re.M)
    if not m:
        bad.append("Total: row not found")
    elif (int(m.group(1)), int(m.group(2))) != (to, td):
        bad.append(f"Total: table says open={m.group(1)} done={m.group(2)}, rows say open={to} done={td}")

    if bad:
        print("TRACKER COUNTS DISAGREE WITH THE ROWS:", file=sys.stderr)
        for b in bad:
            print("  " + b, file=sys.stderr)
        print("\nCorrect table:\n" + table, file=sys.stderr)
        return 1
    print(f"tracker counts OK — open {to} / done {td} (+{refuted} refuted)")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
