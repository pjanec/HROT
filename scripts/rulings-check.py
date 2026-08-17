#!/usr/bin/env python3
"""Verify every quote in docs/blueprints/RULINGS.md still exists in its cited source.

RULINGS.md is an INDEX over the design corpus. An index that silently rots is worse
than no index: it reads as authoritative while pointing at text that moved or changed.
This makes the index self-verifying, the same way tracker-counts.py --check keeps the
tracker honest.

Probes live in a fenced ```probes block at the end of RULINGS.md, one per line:

    <id> | <file path> | <verbatim substring that must appear in that file>

Exit 0 if every probe resolves, 1 otherwise. Run it from the repo root.
"""
import pathlib
import re
import sys

ROOT = pathlib.Path(__file__).resolve().parent.parent
RULINGS = ROOT / "docs" / "blueprints" / "RULINGS.md"


def probes(text):
    block = re.search(r"```probes\n(.*?)```", text, re.S)
    if not block:
        return None
    for line in block.group(1).splitlines():
        line = line.strip()
        if not line or line.startswith("#"):
            continue
        parts = [p.strip() for p in line.split("|", 2)]
        if len(parts) == 3:
            yield parts


def main():
    if not RULINGS.exists():
        print(f"FAIL: {RULINGS} is missing — the canon index must exist")
        return 1

    text = RULINGS.read_text(encoding="utf-8")
    found = probes(text)
    if found is None:
        print("FAIL: RULINGS.md has no ```probes block")
        return 1

    rows = list(found)
    if not rows:
        print("FAIL: the probes block is empty")
        return 1

    bad = []
    for rid, rel, needle in rows:
        target = ROOT / rel
        if not target.exists():
            bad.append(f"{rid}: cited file does not exist — {rel}")
            continue
        if needle not in target.read_text(encoding="utf-8", errors="replace"):
            bad.append(f"{rid}: quote no longer in {rel} — {needle!r}")

    for line in bad:
        print("FAIL " + line)
    print(f"{len(rows) - len(bad)}/{len(rows)} rulings verified against their sources")
    if bad:
        print("\nA failing probe means the design record MOVED, not that the ruling died.")
        print("Find the new home and update the row — do NOT delete the ruling.")
    return 1 if bad else 0


if __name__ == "__main__":
    sys.exit(main())
