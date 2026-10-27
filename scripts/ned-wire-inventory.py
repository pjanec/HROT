#!/usr/bin/env python3
"""Inventory of every production [DdsTopic] type, and a drift check against docs/reference/NED_DDS_Protocol.md.

  python3 scripts/ned-wire-inventory.py              # sorted table: full name | topic | keyed | file:line
  python3 scripts/ned-wire-inventory.py --verbose    # + QoS text, [DdsManaged], key members (for authoring the doc)
  python3 scripts/ned-wire-inventory.py --tsv        # machine-readable (used to author the doc's table)
  python3 scripts/ned-wire-inventory.py --all        # ALSO the excluded roots (tests, Examples, ExtDeps, experiments)
  python3 scripts/ned-wire-inventory.py --check      # exit 1 if code and the doc's "## Inventory" section disagree
  python3 scripts/ned-wire-inventory.py --skeleton   # draft doc rows (role left as TODO) for every type missing from the doc

Full names are REFLECTION style: `Namespace.Type` and `Namespace.Outer+Nested` for nested types.
Production roots: Hrot/, FDP/, Stride/.  Excluded (see the doc, "Excluded roots"): *.Tests / Tests projects, FDP/Examples,
FDP/ExtDeps (vendored GizmoMap + SumoSharp), docs/ (experiments), bin/, obj/.
Stdlib only.  The parser masks comments and string literals, then walks braces, so [DdsTopic] mentioned in a comment or a
string is never counted.
"""
import os
import re
import sys

REPO = os.path.normpath(os.path.join(os.path.dirname(os.path.abspath(__file__)), ".."))
DOC = os.path.join(REPO, "docs", "reference", "NED_DDS_Protocol.md")
ROOTS = ["Hrot", "FDP", "Stride"]
EXCLUDED_DIR_NAMES = {"bin", "obj", "node_modules", ".git", "Examples", "ExtDeps", "TestShared", "experiments"}


def is_excluded_dir(name):
    return name in EXCLUDED_DIR_NAMES or name.endswith(".Tests") or name == "Tests" or name.endswith(".Test")


def iter_cs_files(include_excluded):
    for root in ROOTS:
        top = os.path.join(REPO, root)
        for dirpath, dirnames, filenames in os.walk(top):
            if not include_excluded:
                dirnames[:] = [d for d in dirnames if not is_excluded_dir(d)]
            else:
                dirnames[:] = [d for d in dirnames if d not in ("bin", "obj", ".git")]
            for fn in filenames:
                if fn.endswith(".cs") and not fn.endswith(".g.cs") and not fn.endswith(".Designer.cs"):
                    yield os.path.join(dirpath, fn)


def mask(src):
    """Blank comments and string/char literal CONTENTS, preserving length and newlines."""
    out = list(src)
    i, n = 0, len(src)
    while i < n:
        c = src[i]
        two = src[i:i + 2]
        if two == "//":
            j = src.find("\n", i)
            j = n if j < 0 else j
            for k in range(i, j):
                out[k] = " "
            i = j
        elif two == "/*":
            j = src.find("*/", i + 2)
            j = n if j < 0 else j + 2
            for k in range(i, j):
                if src[k] != "\n":
                    out[k] = " "
            i = j
        elif c == '"':
            verbatim = i > 0 and src[i - 1] == "@" or (i > 1 and src[i - 2:i] in ('@$', '$@'))
            raw = src.startswith('"""', i)
            if raw:
                j = src.find('"""', i + 3)
                j = n if j < 0 else j + 3
            else:
                j = i + 1
                while j < n:
                    if verbatim:
                        if src[j] == '"':
                            if j + 1 < n and src[j + 1] == '"':
                                j += 2
                                continue
                            break
                    else:
                        if src[j] == "\\":
                            j += 2
                            continue
                        if src[j] == '"' or src[j] == "\n":
                            break
                    j += 1
                j = min(j + 1, n)
            for k in range(i + 1, max(i + 1, j - 1)):
                if src[k] != "\n":
                    out[k] = " "
            i = j
        elif c == "'":
            # char literal (or an apostrophe we never see in masked code)
            j = i + 1
            while j < n and src[j] != "'" and src[j] != "\n":
                j += 2 if src[j] == "\\" else 1
            j = min(j + 1, n)
            for k in range(i + 1, max(i + 1, j - 1)):
                out[k] = " "
            i = j
        else:
            i += 1
    return "".join(out)


TOKEN = re.compile(
    r"\bnamespace\s+(?P<ns>[\w.]+)\s*(?P<nsend>;|(?=\{))"
    r"|\b(?P<kw>class|struct|record|interface|enum)\s+(?:(?:struct|class)\s+)?(?P<name>[A-Za-z_]\w*)"
    r"|(?P<open>\{)|(?P<close>\})|(?P<semi>;)"
    r"|(?<=[\[,\s])(?P<attr>DdsTopic|DdsKey|DdsQos|DdsManaged|DdsIdlFile|DdsStruct|DdsUnion)(?:Attribute)?\b"
)


def match_paren(masked, i):
    """masked[i] == '(' -> index of matching ')'."""
    depth = 0
    for j in range(i, len(masked)):
        if masked[j] == "(":
            depth += 1
        elif masked[j] == ")":
            depth -= 1
            if depth == 0:
                return j
    return len(masked) - 1


def targets_type(m, pos):
    """True when the attribute list containing offset `pos` decorates a type declaration (not a member)."""
    j = m.find("]", pos)
    if j < 0:
        return False
    tail = m[j + 1:j + 600]
    tail = re.sub(r"^(?:\s*\[[^\]]*\])*", "", tail)  # further attribute lists
    head = re.match(r"^[\s\w<>,.?]*?\b(class|struct|record|interface|enum)\b", tail)
    if not head:
        return False
    return not re.search(r"[;{=(]", head.group(0))


def parse_file(path):
    with open(path, encoding="utf-8-sig", errors="replace") as f:
        src = f.read()
    if "DdsTopic" not in src and "DdsStruct" not in src and "DdsUnion" not in src:
        return []
    m = mask(src)
    line_of = lambda pos: src.count("\n", 0, pos) + 1
    stack = []          # entries: dict(kind='ns'|'type', name, topic?, ...)
    file_ns = ""
    pending_decl = None  # ("ns"|"type", name) awaiting its '{'
    pend = {}            # attributes seen, waiting for the next type declaration
    results = []
    last_attr_end = 0
    for t in TOKEN.finditer(m):
        if t.group("ns"):
            if t.group("nsend") == ";":
                file_ns = t.group("ns")
            else:
                pending_decl = ("ns", t.group("ns"))
        elif t.group("kw"):
            if t.group("kw") == "enum" and False:
                pass
            pending_decl = ("type", t.group("name"))
            pending_attrs = dict(pend)
            pend = {}
            pending_decl = ("type", t.group("name"), pending_attrs, line_of(t.start()))
        elif t.group("open"):
            if pending_decl:
                if pending_decl[0] == "ns":
                    stack.append({"kind": "ns", "name": pending_decl[1]})
                else:
                    entry = {"kind": "type", "name": pending_decl[1], "attrs": pending_decl[2],
                             "line": pending_decl[3], "keys": []}
                    stack.append(entry)
                pending_decl = None
            else:
                stack.append({"kind": "blk"})
        elif t.group("close"):
            if stack:
                e = stack.pop()
                if e["kind"] == "type" and ("topic" in e["attrs"] or "struct" in e["attrs"]):
                    ns_parts = [file_ns] if file_ns else []
                    ns_parts += [s["name"] for s in stack if s["kind"] == "ns"]
                    outer = [s["name"] for s in stack if s["kind"] == "type"]
                    full = ".".join(ns_parts) + ("." if ns_parts else "") + "+".join(outer + [e["name"]])
                    results.append({"full": full, "topic": e["attrs"].get("topic"), "simple": e["name"], "keyed": bool(e["keys"]),
                                    "keys": e["keys"], "file": os.path.relpath(path, REPO).replace(os.sep, "/"),
                                    "line": e["attrs"].get("topic_line", e["line"]), "qos": e["attrs"].get("qos", ""),
                                    "managed": "managed" in e["attrs"], "idl": e["attrs"].get("idl", "")})
        elif t.group("semi"):
            # a ';' ends a bodiless declaration (e.g. `record struct X(...);`, `enum E;`) - drop the pending one
            pending_decl = None if (pending_decl and pending_decl[0] == "type") else pending_decl
        elif t.group("attr"):
            a = t.group("attr")
            pos = t.end()
            # only an attribute if it sits inside [...] : walk back over spaces / commas to a '['
            k = t.start() - 1
            while k >= 0 and m[k] in " \t\r\n,":
                k -= 1
            # attribute lists may hold several entries; accept if the closest bracket char to the left is '['
            br = max(m.rfind("[", 0, t.start()), -1)
            cl = m.rfind("]", 0, t.start())
            if br < 0 or cl > br:
                continue
            args = ""
            q = pos
            while q < len(m) and m[q] in " \t":
                q += 1
            if q < len(m) and m[q] == "(":
                e = match_paren(m, q)
                args = src[q + 1:e].strip()
            if a != "DdsKey" and not targets_type(m, t.start()):
                continue
            if a == "DdsTopic":
                name = args
                mm = re.match(r'@?"([^"]*)"', args)
                if mm:
                    name = mm.group(1)
                elif not args:
                    name = "(bare [DdsTopic] - no name argument)"
                else:
                    name = "(expr) " + args
                pend["topic"] = name
                pend["topic_line"] = line_of(t.start())
            elif a == "DdsQos":
                pend["qos"] = re.sub(r"\s+", " ", args)
            elif a == "DdsManaged":
                pend["managed"] = True
            elif a in ("DdsStruct", "DdsUnion"):
                pend["struct"] = True
            elif a == "DdsIdlFile":
                pend["idl"] = args.strip('"')
            elif a == "DdsKey":
                # belongs to the innermost enclosing type (the attribute precedes the member)
                for s in reversed(stack):
                    if s["kind"] == "type":
                        # member name: first identifier before `;`, `{`, `=` after the attribute list end
                        tail = m[t.end():t.end() + 400]
                        mem = re.search(r"\]\s*(?:\[[^\]]*\]\s*)*(?:public|internal|private|protected)?\s*(?:readonly\s+|unsafe\s+|fixed\s+)*([\w<>\[\],.?]+)\s+(\w+)\s*(?:[;={]|\()", tail)
                        s["keys"].append("%s %s" % (mem.group(1), mem.group(2)) if mem else "?")
                        break
    return results


GENERIC_USE = re.compile(r"\b(?:I?Dds(?:Reader|Writer)|I?AnimDds(?:Reader|Writer))\s*<\s*([\w.]+)\s*>")
TRANSLATOR_BOUND = "(bound in a translator, not by attribute)"


def collect(include_excluded=False):
    """Rows for every [DdsTopic] type, plus (topic == TRANSLATOR_BOUND) every [DdsStruct]/[DdsUnion] type that has NO
    [DdsTopic] but is instantiated as the T of a DdsReader<T>/DdsWriter<T> somewhere in the scanned roots: its topic name is
    a string constant in a translator, so only the type is discoverable here."""
    topics, structs, used = [], [], set()
    for p in iter_cs_files(include_excluded):
        for r in parse_file(p):
            (topics if r["topic"] else structs).append(r)
        try:
            txt = open(p, encoding="utf-8-sig", errors="replace").read()
        except OSError:
            continue
        if "Dds" in txt:
            used.update(x.split(".")[-1] for x in GENERIC_USE.findall(mask(txt)))
    for r in structs:
        if r["simple"] in used:
            r["topic"] = TRANSLATOR_BOUND
            topics.append(r)
    topics.sort(key=lambda r: (r["full"], r["file"]))
    return topics


def doc_inventory_names():
    """Backticked full names in the FIRST cell of table rows inside the doc's '## Inventory' section."""
    if not os.path.exists(DOC):
        return None
    text = open(DOC, encoding="utf-8").read()
    names, inside = [], False
    for line in text.splitlines():
        if re.match(r"^##\s+(?!#)", line):
            inside = bool(re.match(r"^##\s+(?:\d+\.?\s*)?Inventory\b", line))
            continue
        if inside and line.startswith("|"):
            cells = [c.strip() for c in line.strip().strip("|").split("|")]
            mm = re.match(r"^`([A-Za-z_][\w.+]*)`$", cells[0]) if cells else None
            if mm:
                names.append(mm.group(1))
    return names


KEY_WORDS = {
    "EntityId": "entity id", "ParentNetworkId": "parent entity id", "LocalChildIndex": "child instance id", "PartId": "part instance id",
    "NodeId": "node id", "MapId": "map id", "MapGroupId": "map group id", "TransferId": "transfer id",
    "ObserverEntityId": "observer entity id", "TargetEntityId": "target entity id", "ComputerHash": "computer hash",
    "ProcessId": "process id", "ClientId": "client id", "ClientNodeId": "client node id", "TargetNodeId": "target node id",
    "SourceNodeId": "source node id", "FileIndex": "file index", "DescriptorType": "descriptor type",
}


def qos_code(q):
    """The doc's QoS kind code (see the doc, 'QoS kinds'): S SL SN SA Q QN L B D."""
    d = dict(re.findall(r"(\w+)\s*=\s*(?:Dds\w+\.)?(\w+)", q))
    r, du, hk, hd = d.get("Reliability"), d.get("Durability"), d.get("HistoryKind"), d.get("HistoryDepth")
    if r is None and du is None:
        return "D"   # no [DdsQos], or history only
    table = {
        ("Reliable", "TransientLocal", "KeepLast"): "S" if hd == "1" else "SN",
        ("Reliable", "TransientLocal", "KeepAll"): "SA",
        ("BestEffort", "TransientLocal", "KeepLast"): "SL",
        ("Reliable", "Volatile", "KeepAll"): "Q",
        ("Reliable", "Volatile", "KeepLast"): "QN",
        ("Reliable", "Volatile", None): "B",
        ("BestEffort", "Volatile", "KeepLast"): "L",
        ("BestEffort", "Volatile", None): "L",
    }
    return table.get((r, du, hk), "?")


def skeleton_row(r):
    keyed = "no"
    if r["keyed"]:
        keyed = "yes: " + " + ".join(KEY_WORDS.get(k.split()[-1], k.split()[-1]) for k in r["keys"])
    code = qos_code(r["qos"])
    kind = "descriptor" if (r["keyed"] and code in ("S", "SN", "SA", "SL")) else "message"
    asm = r["file"].split("/")[2] if r["file"].count("/") > 2 else r["file"].split("/")[1]
    topic = r["topic"] if r["topic"] != TRANSLATOR_BOUND else "TODO topic name (translator constant)"
    return "| `%s` | `%s` | %s | %s | %s | %s | TODO role |" % (r["full"], topic, kind, keyed, code, asm)


def main(argv):
    rows = collect("--all" in argv)
    if "--check" in argv:
        doc_names = doc_inventory_names()
        if doc_names is None:
            print("FAIL: %s does not exist" % os.path.relpath(DOC, REPO))
            return 1
        code_names = [r["full"] for r in rows]
        missing = sorted(set(code_names) - set(doc_names))
        stale = sorted(set(doc_names) - set(code_names))
        dups = sorted({n for n in doc_names if doc_names.count(n) > 1})
        for n in missing:
            print("MISSING in doc inventory (wire type in code): " + n)
        for n in stale:
            print("STALE in doc inventory (no such wire type in code): " + n)
        for n in dups:
            print("DUPLICATE row in doc inventory: " + n)
        if missing or stale or dups:
            print("FAIL: %d in code, %d in doc inventory rows (%d unique)" % (len(set(code_names)), len(doc_names), len(set(doc_names))))
            return 1
        print("OK: %d wire types in code == %d rows in the doc's Inventory" % (len(set(code_names)), len(doc_names)))
        return 0
    if "--skeleton" in argv:
        have = set(doc_inventory_names() or [])
        for r in rows:
            if r["full"] not in have:
                print(skeleton_row(r))
        return 0
    if "--tsv" in argv:
        for r in rows:
            print("\t".join([r["full"], r["topic"], "yes" if r["keyed"] else "no", r["qos"], "managed" if r["managed"] else "",
                             ",".join(r["keys"]), r["idl"], "%s:%d" % (r["file"], r["line"])]))
        return 0
    w = max([len(r["full"]) for r in rows] + [8])
    tw = max([len(r["topic"]) for r in rows] + [5])
    print("%-*s  %-*s  %-5s  %s" % (w, "full name", tw, "topic", "keyed", "file:line"))
    for r in rows:
        extra = ""
        if "--verbose" in argv:
            extra = "   qos=[%s]%s keys=%s" % (r["qos"], " managed" if r["managed"] else "", ",".join(r["keys"]))
        print("%-*s  %-*s  %-5s  %s:%d%s" % (w, r["full"], tw, r["topic"], "yes" if r["keyed"] else "no", r["file"], r["line"], extra))
    nb = sum(1 for r in rows if r["topic"] == TRANSLATOR_BOUND)
    print("\n%d wire types = %d [DdsTopic] types (%d distinct topic names) + %d translator-bound [DdsStruct] types"
          % (len(rows), len(rows) - nb, len({r["topic"] for r in rows if r["topic"] != TRANSLATOR_BOUND}), nb))
    return 0


if __name__ == "__main__":
    sys.exit(main(sys.argv[1:]))
