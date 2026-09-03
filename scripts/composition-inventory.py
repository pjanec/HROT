#!/usr/bin/env python3
# Enumerate what every composition root composes, and which units are SHARED.
#
# Answers the question a design must not guess at: "what does each host actually
# compose, and which pieces are already reusable?"  Run it BEFORE reasoning about
# host composition -- the INVENTORY-before-design rule in CLAUDE.md.
#
#   python3 scripts/composition-inventory.py
#
# Method: per composition root, strip comments (so a doc mention is not counted as
# composition), then collect `new X<suffix>(...)` constructions and
# `X.Build/Base/BasePlus/Create*(...)` calls. Cross-tabulate by how many roots use
# each unit. Deliberately broad, then filtered by frequency.
#
# KNOWN IMPRECISION, stated so nobody over-trusts the numbers:
#   - the suffix list also matches `HashSet` -- a false positive; ignore that row;
#   - a unit reached through a local variable or a helper is MISSED, so every count
#     is a LOWER BOUND and never proof of absence;
#   - it is text, not semantics: it cannot tell two same-named types apart.
# For an exhaustive claim, corroborate with search_graph (scripts/find.sh).
import re, os, json, collections
ROOTS = {
 "SimHost(boot)"   : "Hrot/Subsystems/Hrot.SimHost/SimHostNodeBootstrapper.cs",
 "IG(boot)"        : "Hrot/Subsystems/Hrot.IG/IgNodeBootstrapper.cs",
 "StrideNode(dorm)": "Hrot/Subsystems/Hrot.NodeComposition/StrideNodeBootstrapper.cs",
 "CGF(inline)"     : "Hrot/Subsystems/Hrot.CGF/CgfSubsystem.cs",
 "Editor(inline)"  : "Hrot/Subsystems/Hrot.Editor/EditorSubsystem.cs",
 "StrideEd(inline)": "Stride/HrotStrideApp.Game/EditorStrideSubsystem.cs",
 "ExCon"           : "Hrot/Subsystems/Hrot.ExCon/ExConSubsystem.cs",
 "ReplayBrowser"   : "Hrot/Subsystems/Hrot.ReplayBrowser/ReplayBrowserSubsystem.cs",
 "EditorHarness(T)": "Hrot/Runner/Hrot.ClusterRunner.Integration.Tests/EditorHarness.cs",
}
# a composable unit = a type constructed or a static Build() called, whose name ends in a
# composition-ish suffix. Deliberately broad; we filter by cross-root frequency afterwards.
SUF = r"(?:Pack|Module|ModuleSet|Set|Slave|Master|Registry|Provider|Controller|Bootstrapper|Adapter)"
NEW = re.compile(r"new\s+(?:[\w.]*\.)?([A-Z]\w*" + SUF + r")\s*[(<]")
BLD = re.compile(r"\b([A-Z]\w*" + SUF + r"|TkbTranslatorSet|StrideMuscleModules|EntityCreationPack)\s*\.\s*(Build|Base|BasePlus|Create\w*)\s*\(")
found = {}
for name, path in ROOTS.items():
    if not os.path.exists(path):
        found[name] = None; continue
    src = open(path, encoding="utf-8", errors="replace").read()
    # strip // comments and /* */ so doc mentions do not count as composition
    src = re.sub(r"/\*.*?\*/", "", src, flags=re.S)
    src = "\n".join(re.sub(r"//.*$", "", ln) for ln in src.splitlines())
    units = set(NEW.findall(src)) | {m[0] for m in BLD.findall(src)}
    found[name] = units
freq = collections.Counter()
for u in found.values():
    if u:
        for x in u: freq[x] += 1
print("ROOT                 units")
for k, v in found.items():
    print(f"  {k:<18} {'MISSING' if v is None else len(v)}")
print()
print("=== SHARED (used by >=2 roots) ===")
for u, c in sorted(freq.items(), key=lambda kv: (-kv[1], kv[0])):
    if c >= 2:
        who = ",".join(k for k, v in found.items() if v and u in v)
        print(f"  {c}  {u:<38} {who}")
print()
print("=== SINGLE-ROOT (candidates for inline / host-specific) ===")
singles = collections.defaultdict(list)
for u, c in freq.items():
    if c == 1:
        singles[[k for k, v in found.items() if v and u in v][0]].append(u)
for k in ROOTS:
    if singles.get(k):
        print(f"  {k}: {len(singles[k])}")
        print("     " + ", ".join(sorted(singles[k])))
