#!/usr/bin/env python3
"""Drift check: everything that goes on the DDS wire must be NAMED in docs/reference/NED_DDS_Protocol.md.

  python3 scripts/protocol-doc-check.py              # exit 1 and list what the doc does not name; exit 0 when it names everything
  python3 scripts/protocol-doc-check.py --list       # also print the full sets (every topic type, every descriptor ordinal) and where each is named
  python3 scripts/protocol-doc-check.py --doc FILE   # check another copy of the doc (to prove the check catches drift)

What it collects (stdlib only, sub-second):
  * every production [DdsTopic] type — the C# struct / class / record the attribute decorates — under Hrot/, FDP/ and Stride/
    (FDP/ExtDeps included: the vendored SumoSharp and GizmoMap topics ride the same DDS domain), skipping bin/, obj/, test projects
    (*Tests*, *.Test, Tests, TestShared), Examples and benchmarks (*Bench*). Comments and string literals are masked first, by the
    one C# scanner scripts/ned-wire-inventory.py owns (imported, not copied), so a [DdsTopic] in a comment never counts.
  * every `dt<Name> = <n>` member of EDescriptorType in Hrot/Network/Hrot.Network.NED/AllDescriptors.cs (comments masked, so a
    retired name quoted in a comment is not an ordinal).

What "named" means (no false alarms, no false passes):
  * a topic type is named when the doc contains its FULL name (`Namespace.Type`, as a whole token), or its simple type name as a
    whole token when that simple name is unique among the collected types (a whole token: not part of a longer identifier, so
    `EntityMinefield` is not satisfied by `EntityMinefieldStrikes`);
  * an ordinal is named when `dt<Name>` occurs in the doc as a whole token; and where the doc's "Descriptor ordinals" table has a row
    `| <n> | `dt<Name>` |`, <n> must equal the code's value (a wrong number is drift too); a table row for a name the enum no longer
    has is reported as STALE.

ALLOW_LIST below holds the types the doc deliberately does not name, each with its reason. It is empty on purpose today: the
SumoSharp and GizmoMap types are named in the doc's "SUMO traffic" and "Excluded roots" sections.

Companion: scripts/ned-wire-inventory.py --check compares the doc's Inventory table ROW BY ROW with the production types (stale rows,
duplicates, explicit topic names). This script is the broader "is it named anywhere" check that also covers the vendored topics and
the descriptor ordinals.
"""
import importlib.util
import os
import re
import sys

REPO = os.path.normpath(os.path.join(os.path.dirname(os.path.abspath(__file__)), ".."))
DOC = os.path.join(REPO, "docs", "reference", "NED_DDS_Protocol.md")
ENUM = os.path.join(REPO, "Hrot", "Network", "Hrot.Network.NED", "AllDescriptors.cs")
ROOTS = ["Hrot", "FDP", "Stride"]
SKIP_DIRS = {"bin", "obj", ".git", "node_modules", "Examples", "TestShared", "experiments"}

# Types the doc deliberately does not name. Full name -> the reason (keep the reason honest; a type that IS on a production wire
# belongs in the doc, not here).
ALLOW_LIST = {
    # (none) — SumoSharp's eleven sumo/* types are named in the doc's "SUMO traffic" section; GizmoMap's four in "Excluded roots".
}


def _load_scanner():
    """The C# scanner (comment / string masking + brace walk) of scripts/ned-wire-inventory.py — one implementation, imported."""
    path = os.path.join(REPO, "scripts", "ned-wire-inventory.py")
    spec = importlib.util.spec_from_file_location("ned_wire_inventory", path)
    mod = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(mod)
    return mod


def skip_dir(name):
    low = name.lower()
    return (name in SKIP_DIRS or name == "Tests" or name.endswith(".Tests") or name.endswith(".Test")
            or "tests" in low.split(".") or re.search(r"bench(marks?)?$", low) is not None)


def topic_types(scanner):
    """{full name: file:line} for every [DdsTopic] type in the production roots (ExtDeps included)."""
    found = {}
    for root in ROOTS:
        top = os.path.join(REPO, root)
        for dirpath, dirnames, filenames in os.walk(top):
            dirnames[:] = [d for d in dirnames if not skip_dir(d)]
            for fn in filenames:
                if not fn.endswith(".cs") or fn.endswith(".g.cs") or fn.endswith(".Designer.cs"):
                    continue
                path = os.path.join(dirpath, fn)
                try:
                    with open(path, "rb") as f:
                        if b"DdsTopic" not in f.read():
                            continue
                except OSError:
                    continue
                for r in scanner.parse_file(path):
                    if r.get("topic"):          # decorated with [DdsTopic] (bare or named) — not a mere [DdsStruct]
                        found[r["full"]] = "%s:%d" % (r["file"], r["line"])
    return found


def ordinals(scanner):
    """[(name, value)] of EDescriptorType, comments masked."""
    with open(ENUM, encoding="utf-8-sig") as f:
        src = scanner.mask(f.read())
    m = re.search(r"\benum\s+EDescriptorType\b[^{]*\{(.*?)\}", src, re.S)
    if not m:
        raise SystemExit("FAIL: enum EDescriptorType not found in %s" % os.path.relpath(ENUM, REPO))
    return [(n, int(v)) for n, v in re.findall(r"\b(dt\w+)\s*=\s*(\d+)", m.group(1))]


def token(name):
    return re.compile(r"(?<![\w])" + re.escape(name) + r"(?![\w])")


def main(argv):
    doc = DOC
    if "--doc" in argv:
        i = argv.index("--doc")
        if i + 1 >= len(argv):
            print("usage: protocol-doc-check.py [--list] [--doc FILE]")
            return 2
        doc = os.path.abspath(argv[i + 1])
    if not os.path.exists(doc):
        print("FAIL: %s does not exist" % doc)
        return 1
    with open(doc, encoding="utf-8") as f:
        text = f.read()

    scanner = _load_scanner()
    types = topic_types(scanner)
    ords = ordinals(scanner)

    simple_count = {}
    for full in types:
        s = re.split(r"[.+]", full)[-1]
        simple_count[s] = simple_count.get(s, 0) + 1

    problems = []
    named_by = {}
    for full in sorted(types):
        if full in ALLOW_LIST:
            named_by[full] = "allow-listed: " + ALLOW_LIST[full]
            continue
        simple = re.split(r"[.+]", full)[-1]
        if token(full).search(text) or token(full.replace("+", ".")).search(text):
            named_by[full] = "full name"
        elif simple_count[simple] == 1 and token(simple).search(text):
            named_by[full] = "simple name"
        else:
            problems.append("TOPIC TYPE not named in the doc: %s  (%s)" % (full, types[full]))

    table = {}
    for n, name in re.findall(r"^\|\s*(\d+)\s*\|\s*`(dt\w+)`\s*\|", text, re.M):
        table.setdefault(name, []).append(int(n))
    code_names = {n for n, _ in ords}
    for name, value in ords:
        if not token(name).search(text):
            problems.append("ORDINAL not named in the doc: %s = %d" % (name, value))
            continue
        named_by[name] = "named"
        for doc_value in table.get(name, []):
            if doc_value != value:
                problems.append("ORDINAL WRONG in the doc's table: %s is %d in code, %d in the doc" % (name, value, doc_value))
    for name in sorted(set(table) - code_names):
        problems.append("ORDINAL STALE in the doc's table: %s (no such EDescriptorType member)" % name)
    for name in sorted(ALLOW_LIST):
        if name not in types:
            problems.append("ALLOW_LIST entry is no longer a [DdsTopic] type: %s (remove it)" % name)

    if "--list" in argv:
        print("[DdsTopic] types (%d):" % len(types))
        for full in sorted(types):
            print("  %-70s %-12s %s" % (full, named_by.get(full, "NOT NAMED"), types[full]))
        print("EDescriptorType ordinals (%d):" % len(ords))
        for name, value in sorted(ords, key=lambda x: x[1]):
            print("  %4d  %-32s %s" % (value, name, named_by.get(name, "NOT NAMED")))

    rel = os.path.relpath(doc, REPO) if doc.startswith(REPO) else doc
    for p in problems:
        print(p)
    if problems:
        print("FAIL: %d problem(s) — %s does not name everything on the wire (%d topic types, %d ordinals checked)"
              % (len(problems), rel, len(types), len(ords)))
        return 1
    print("OK: %s names all %d [DdsTopic] types (%d allow-listed) and all %d EDescriptorType ordinals"
          % (rel, len(types), sum(1 for t in types if t in ALLOW_LIST), len(ords)))
    return 0


if __name__ == "__main__":
    sys.exit(main(sys.argv[1:]))
