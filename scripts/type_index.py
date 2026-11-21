#!/usr/bin/env python3
"""Repo-wide public-type index + prior-art query.

  python3 scripts/type_index.py                     # build + summary
  python3 scripts/type_index.py <regex> [<regex>..] # query concept nouns

For each matching public type: where it is declared, how many production
files name it, how many test files, and which subsystems adopt it.
Use before designing anything, to answer "does this already exist?".
"""
import os, re, sys, json, collections

ROOT = "/home/user/HROT"
DECL = re.compile(
    r"^\s*public\s+(?:sealed\s+|static\s+|abstract\s+|partial\s+|readonly\s+|unsafe\s+|ref\s+)*"
    r"(interface|class|record|struct|enum)\s+([A-Z]\w*)", re.M)
IDENT = re.compile(r"[A-Za-z_]\w*")


def is_test(p):
    low = p.lower()
    return "test" in low or "benchmark" in low or "/mock" in low


def bucket(rel):
    p = rel.split("/")
    if rel.startswith(("Hrot/Subsystems/", "Hrot/Editor/")):
        return p[2]
    if rel.startswith("Hrot/ClusterRunner"):
        return "ClusterRunner"
    if rel.startswith("Hrot/Engine/"):
        return "engine:" + p[2]
    if rel.startswith("FDP/"):
        return "fdp:" + p[2]
    return p[0]


def build():
    files = []
    for dp, dns, fns in os.walk(ROOT):
        dns[:] = [d for d in dns if d not in ("obj", "bin", ".git", "node_modules")]
        for fn in fns:
            if fn.endswith(".cs"):
                files.append(os.path.relpath(os.path.join(dp, fn), ROOT))

    decls, idents = {}, {}
    for rel in files:
        try:
            text = open(os.path.join(ROOT, rel), encoding="utf-8", errors="replace").read()
        except OSError:
            continue
        idents[rel] = set(IDENT.findall(text))
        if not is_test(rel):
            for kind, name in DECL.findall(text):
                decls.setdefault(name, []).append((rel, kind))

    prod_index = collections.defaultdict(list)
    test_index = collections.Counter()
    for rel, toks in idents.items():
        if is_test(rel):
            for t in toks:
                test_index[t] += 1
        else:
            for t in toks:
                prod_index[t].append(rel)

    rows = []
    for name, sites in decls.items():
        declfiles = {s[0] for s in sites}
        consumers = [f for f in prod_index.get(name, ()) if f not in declfiles]
        rows.append({
            "name": name,
            "kind": sites[0][1],
            "decls": sorted(declfiles),
            "prod_consumers": len(consumers),
            "test_consumers": test_index.get(name, 0),
            "subsystems": sorted({bucket(c) for c in consumers}),
            "sample": consumers[:6],
        })
    rows.sort(key=lambda r: r["name"])
    return rows


CACHE = os.path.join(ROOT, ".type_index.json")
if os.path.exists(CACHE) and "--rebuild" not in sys.argv:
    rows = json.load(open(CACHE))
else:
    rows = build()
    json.dump(rows, open(CACHE, "w"))

queries = [a for a in sys.argv[1:] if not a.startswith("--")]
if not queries:
    print(f"public types indexed: {len(rows)}")
    print(f"cache: {CACHE}  (pass regexes to query; --rebuild to refresh)")
    sys.exit(0)

for q in queries:
    pat = re.compile(q, re.I)
    hits = [r for r in rows if pat.search(r["name"])]
    print(f"\n### /{q}/  -> {len(hits)} type(s)")
    for r in sorted(hits, key=lambda r: r["prod_consumers"]):
        dup = "  ⚠DUP×%d" % len(r["decls"]) if len(r["decls"]) > 1 else ""
        print(f"  {r['prod_consumers']:>3}p {r['test_consumers']:>3}t  {r['kind'][:5]:<5} "
              f"{r['name']:<40} {r['decls'][0]}{dup}")
        if r["subsystems"]:
            print(f"                       adopters: {', '.join(r['subsystems'])[:100]}")
