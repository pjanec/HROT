#!/usr/bin/env python3
"""Unadopted-seam inventory.

For every public type declared in the SHARED UI layers, count how many
production (non-test) files elsewhere reference it, and which subsystems.
0-1 production consumers = the signature of a built-but-unadopted seam.

Tokenises each file ONCE into an identifier set, then resolves every type
by set membership -- O(files) scanning instead of O(types x files) regex.
"""
import os, re, json, collections

ROOT = "/home/user/HROT"

SHARED_DIRS = (
    "FDP/Engine/Fdp.Presentation",
    "Hrot/Engine/Hrot.Presentation",
    "Hrot/Engine/Hrot.Common",
    "FDP/Toolkits/Fdp.Toolkits",
    "FDP/ExtDeps/GizmoMap/GizmoMap.Contracts",
    "FDP/ExtDeps/GizmoMap/GizmoMap.Presentation",
)

EXCLUDE_PARTS = ("Hrot.UI.Common", "ExtDeps/NodeEdit")

DECL = re.compile(
    r"^\s*public\s+(?:sealed\s+|static\s+|abstract\s+|partial\s+|readonly\s+|unsafe\s+)*"
    r"(interface|class|record|struct|enum)\s+([A-Z]\w*)", re.M)

IDENT = re.compile(r"[A-Za-z_]\w*")


def is_test(p):
    low = p.lower()
    return "test" in low or "benchmark" in low or "/mock" in low


def bucket(rel):
    parts = rel.split("/")
    if rel.startswith("Hrot/Subsystems/"):
        return parts[2]
    if rel.startswith("Hrot/Editor/"):
        return parts[2]
    if rel.startswith("Hrot/ClusterRunner"):
        return "ClusterRunner"
    if rel.startswith("Hrot/Engine/"):
        return "engine:" + parts[2]
    if rel.startswith("FDP/"):
        return "fdp:" + parts[2]
    return parts[0]


# ---- 1. gather files -------------------------------------------------------
files = []
for dp, dns, fns in os.walk(ROOT):
    dns[:] = [d for d in dns if d not in ("obj", "bin", ".git", "node_modules")]
    for fn in fns:
        if fn.endswith(".cs"):
            rel = os.path.relpath(os.path.join(dp, fn), ROOT)
            if not any(x in rel for x in EXCLUDE_PARTS):
                files.append(rel)

# ---- 2. one pass: declarations + identifier sets ---------------------------
decls = {}          # name -> (declfile, kind)
idents = {}         # rel  -> set of identifiers
for rel in files:
    try:
        text = open(os.path.join(ROOT, rel), encoding="utf-8", errors="replace").read()
    except OSError:
        continue
    idents[rel] = set(IDENT.findall(text))
    if rel.startswith(SHARED_DIRS) and not is_test(rel):
        for kind, name in DECL.findall(text):
            decls.setdefault(name, (rel, kind))

prod = [r for r in idents if not is_test(r)]
tests = [r for r in idents if is_test(r)]

# ---- 3. invert: identifier -> files ---------------------------------------
prod_index = collections.defaultdict(list)
test_index = collections.Counter()
for rel in prod:
    for tok in idents[rel]:
        prod_index[tok].append(rel)
for rel in tests:
    for tok in idents[rel]:
        test_index[tok] += 1

# ---- 4. rows ---------------------------------------------------------------
rows = []
for name, (declfile, kind) in decls.items():
    consumers = [f for f in prod_index.get(name, ()) if f != declfile]
    # consumers OUTSIDE the shared layers = actual adoption by subsystems
    external = [c for c in consumers if not c.startswith(SHARED_DIRS)]
    rows.append({
        "name": name,
        "kind": kind,
        "decl": declfile,
        "prod_consumers": len(consumers),
        "external_consumers": len(external),
        "test_consumers": test_index.get(name, 0) - (1 if is_test(declfile) else 0),
        "subsystems": sorted({bucket(c) for c in external}),
        "sample": consumers[:5],
    })

rows.sort(key=lambda r: (r["prod_consumers"], -r["test_consumers"], r["name"]))
out = os.path.join(os.path.dirname(os.path.abspath(__file__)), "seam_inventory.json")
json.dump(rows, open(out, "w"), indent=1)

print(f"files scanned: {len(idents)}  (prod {len(prod)}, test {len(tests)})")
print(f"public types declared in shared layers: {len(rows)}")
h = collections.Counter(min(r["prod_consumers"], 6) for r in rows)
for k in sorted(h):
    print(f"  {k}{'+' if k == 6 else ''} prod consumers: {h[k]}")
z = [r for r in rows if r["prod_consumers"] <= 1]
print(f"\n0-1 band: {len(z)}   (with tests: {sum(1 for r in z if r['test_consumers'] > 0)})")
print(f"written: {out}")
