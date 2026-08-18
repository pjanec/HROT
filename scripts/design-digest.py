#!/usr/bin/env python3
"""What changed in the DESIGN corpus recently — the post-compaction re-learn tool.

RULINGS.md indexes what is SETTLED. This answers a different question: what has
MOVED lately. Newer design documents overrule older ones, and after a compaction
the recent ones are exactly what a session has lost and cannot know it has lost.

This is a SCRIPT, not a document, on purpose: a hand-maintained "recent changes"
file rots the moment someone forgets to update it, which is the disease being
treated. Generated from git, it cannot lie about what changed.

    python3 scripts/design-digest.py             # last 7 days
    python3 scripts/design-digest.py --days 14
    python3 scripts/design-digest.py --check     # audit STATUS headers only

Excludes HANDOFF_*/REPORT_* by default: per the lookup order those restate the
design rather than setting it. Pass --all to include them.
"""
import argparse
import pathlib
import re
import subprocess
import sys

ROOT = pathlib.Path(__file__).resolve().parent.parent

# Lines that carry a decision rather than prose. Deliberately narrow — a digest
# nobody reads because it prints everything is the same as no digest.
RULING_HINTS = re.compile(
    r"(USER RULING|User,? verbatim|⭐⭐⭐|SUPERSEDED|SUPERSEDES|WITHDRAWN|RETIRED|"
    r"OVERRULED|RULED:|✅ RESOLVED|DECIDED|is NOT|MUST NOT|DO NOT|never|CORRECTED)",
    re.I,
)
STATUS_RE = re.compile(r"<!--\s*STATUS(.*?)-->", re.S)

# An architect question decides WHERE something lives. That is only sound if the
# alternatives were enumerated first -- and grep cannot enumerate, it can only
# confirm a guess. Three designs in this programme were written against a partial
# inventory (R-11: three variable surfaces, not one; R-72: two watch windows, then
# four). The block is the evidence the enumeration happened.
INVENTORY_RE = re.compile(r"\bINVENTORY\b")

# The rule binds designs written UNDER it. Demanding the back catalogue retro-fit
# would make the gate red on arrival, and a gate nobody can turn green is a gate
# somebody switches off -- the same way the optional-parameter detector died.
# A document opts in by carrying a STATUS 'updated:' on or after this date.
INVENTORY_RULE_DATE = "2026-08-18"

# Same reasoning for STATUS headers themselves. CLAUDE.md says "retro-fit lazily --
# add a STATUS block to any design document you TOUCH; do not spend a batch on the
# back catalogue." A blanket sweep would be worse than the debt it clears: a
# current-answer nobody actually determined is a lie with a checkbox. So the check
# binds documents CREATED on or after this date; the back catalogue is repaired as
# it is touched, which is what the rule asks for.
STATUS_RULE_DATE = "2026-08-18"


def git(*args):
    return subprocess.run(
        ["git", *args], cwd=ROOT, capture_output=True, text=True
    ).stdout


def changed(days, include_all):
    out = git("log", f"--since={days} days ago", "--name-only",
              "--pretty=format:@%ad", "--date=short", "--", "docs/")
    dates, seen = {}, None
    for line in out.splitlines():
        if line.startswith("@"):
            seen = line[1:]
        elif line.strip().endswith(".md"):
            dates.setdefault(line.strip(), seen)
    for path, date in dates.items():
        name = pathlib.Path(path).name
        if not include_all and (name.startswith("HANDOFF_") or name.startswith("REPORT_")):
            continue
        if (ROOT / path).exists():
            yield date, path


def created(path):
    """First-commit date of a file, 'YYYY-MM-DD'. Empty when git cannot say."""
    out = git("log", "--diff-filter=A", "--format=%ad", "--date=short", "--", path)
    return out.strip().splitlines()[-1] if out.strip() else ""


def status_of(text):
    m = STATUS_RE.search(text)
    if not m:
        return None
    fields = {}
    for line in m.group(1).splitlines():
        if ":" in line:
            k, v = line.split(":", 1)
            fields[k.strip().lower()] = v.strip()
    return fields


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--days", type=int, default=7)
    ap.add_argument("--all", action="store_true")
    ap.add_argument("--check", action="store_true",
                    help="only audit STATUS headers; exit 1 if any are missing")
    args = ap.parse_args()

    rows = sorted(changed(args.days, args.all), reverse=True)
    if not rows:
        print(f"No design documents changed in the last {args.days} days.")
        return 0

    missing, no_inventory, out = [], [], []
    for date, path in rows:
        text = (ROOT / path).read_text(encoding="utf-8", errors="replace")
        st = status_of(text)
        if st is None and created(path) >= STATUS_RULE_DATE:
            missing.append(path)
        if pathlib.Path(path).name.startswith("Architect_Question_") \
                and (st or {}).get("updated", "") >= INVENTORY_RULE_DATE \
                and not INVENTORY_RE.search(text):
            no_inventory.append(path)
        state = (st or {}).get("state", "?")
        superseded_by = (st or {}).get("superseded-by", "")

        if args.check:
            continue

        hits = [l.strip() for l in text.splitlines()
                if RULING_HINTS.search(l) and len(l.strip()) > 30][:4]
        flag = ""
        if state.upper() in {"SUPERSEDED", "WITHDRAWN"}:
            flag = f"  <-- {state.upper()}" + (f" by {superseded_by}" if superseded_by else "")
        elif st is None:
            flag = "  <-- no STATUS header"
        out.append(f"\n{date}  {path}{flag}")
        out.extend("\n      " + h[:150] for h in hits)

    if args.check:
        bad = False
        if missing:
            bad = True
            print(f"{len(missing)} design document(s) with no STATUS header:")
            for p in missing:
                print("  " + p)
            print("\nAdd one (see .claude/CLAUDE.md, 'Design document format').")
        if no_inventory:
            bad = True
            print(f"\n{len(no_inventory)} architect question(s) with no INVENTORY block:")
            for p in no_inventory:
                print("  " + p)
            print("\nA design that names WHERE something should live must first enumerate")
            print("what already exists. grep answers 'does X exist?'; only the codebase-memory")
            print("graph answers 'what are ALL the X?'. Add a section containing the literal")
            print("word INVENTORY, the search_graph call you ran, and its total count.")
            print("See .claude/CLAUDE.md, 'Inventory before design'.")
        if bad:
            return 1
        print(f"All {len(rows)} recently-changed design documents carry a STATUS header,")
        print("and every recently-changed architect question carries an INVENTORY block.")
        return 0

    print(f"DESIGN DIGEST — {len(rows)} document(s) changed in the last {args.days} days")
    print("Newer overrules older. Read the flagged ones before acting on anything they touch.")
    print("".join(out))
    if missing:
        print(f"\n{len(missing)} of these carry no STATUS header — their state is unknown:")
        for p in missing:
            print("  " + p)
    return 0


if __name__ == "__main__":
    sys.exit(main())
