#!/usr/bin/env python3
# Measure the CROSSING VALUES of a composition slice — the number that decides what a host's
# migration to a declared boot plan actually costs.
#
#   python3 scripts/crossing-values.py <file> <startLine> <endLine> [label]
#   python3 scripts/crossing-values.py --roots        # the known ECS composition roots
#
# WHY THIS IS THE RIGHT NUMBER (docs/DESIGN_Subsystem_Composition_Unification.md §4.1R).
# A composition slice becomes a NodeBootPlan by declaring its phases as steps. What makes that
# cheap or expensive is NOT the slice's length — it is how many locals are LIVE across a step
# boundary, because each one has to move to the plan's value bag (or to a method parameter)
# before the step can be declared. Measured: ExCon had 2 crossings and they cost five compile
# errors; CgfSubsystem has 40 locals whose spine values live 118-191 lines each, so every
# candidate boundary is crossed by 3-5 of them and there is no clean prefix.
#
# PEAK LIVE is the headline: the largest number of locals simultaneously live at any line in the
# slice, i.e. the worst boundary. It is a lower bound on how many values must move first.
#
# KNOWN IMPRECISION, stated so nobody over-trusts it:
#   - a local's "life" is first mention to last mention by name, so a name reused in a nested
#     scope inflates its span; this OVER-estimates, so a low number is trustworthy and a high
#     one deserves a look;
#   - declarations are matched textually (`var x =` / `Type x =`), so out/deconstruction/pattern
#     declarations are missed — the count is a LOWER BOUND;
#   - it is text, not semantics: it cannot see whether two same-named locals are the same one.
import re, sys, os

DECL = re.compile(r"^\s*(?:var|[A-Z][A-Za-z0-9_<>,\[\]\.\?]*)\s+([a-z_][A-Za-z0-9_]*)\s*=(?!=)")
KEYWORDS = {"if", "for", "while", "switch", "return", "throw", "using", "lock", "foreach", "else", "catch"}

def strip_comments(lines):
    # ⚠ LINE comments are removed FIRST, and that order is load-bearing.
    # Measured 2026-09-03: CgfSubsystem.cs:389 contains `//  ... /missions/* routes ...`. Handling
    # /* */ first read that as a block-comment opener, blanked every line after it, and the tool
    # reported CGF as having ZERO locals — a confident, silent, completely wrong answer. The guard
    # in measure() exists for the same reason: this class of bug must fail loudly, not quietly.
    out, in_block = [], False
    for ln in lines:
        if not in_block:
            ln = re.sub(r"//.*$", "", ln)
        if in_block:
            if "*/" in ln:
                ln, in_block = ln.split("*/", 1)[1], False
            else:
                out.append(""); continue
        while "/*" in ln:
            head, rest = ln.split("/*", 1)
            if "*/" in rest:
                ln = head + rest.split("*/", 1)[1]
            else:
                ln, in_block = head, True
                break
        out.append(ln)
    return out

def measure(path, start, end, label=None):
    raw = open(path, encoding="utf-8", errors="replace").read().splitlines()
    src = strip_comments(raw)
    lo, hi = max(1, start), min(len(src), end)
    window = list(range(lo, hi + 1))

    names = []
    for i in window:
        m = DECL.match(src[i - 1])
        if m and m.group(1) not in KEYWORDS:
            names.append(m.group(1))
    names = sorted(set(names))

    spans = {}
    for n in names:
        pat = re.compile(r"\b" + re.escape(n) + r"\b")
        hits = [i for i in window if pat.search(src[i - 1])]
        if len(hits) >= 2:
            spans[n] = (hits[0], hits[-1])

    live_at, peak, peak_line = {}, 0, lo
    for i in window:
        live = [n for n, (a, b) in spans.items() if a <= i <= b]
        live_at[i] = live
        if len(live) > peak:
            peak, peak_line = len(live), i

    # ⛔ DEGRADE LOUDLY. A composition slice of any size has locals; zero means the parse failed,
    #    not that the host is clean. Reporting a confident zero is how this tool would lie.
    blank_ratio = sum(1 for i in window if not src[i - 1].strip()) / max(1, len(window))
    name = label or os.path.basename(path)
    if not names and len(window) > 40:
        print(f"=== {name}  [{lo}-{hi}] ===")
        print(f"  !! PARSE FAILED — 0 locals found in {len(window)} lines "
              f"({blank_ratio:.0%} of lines blank after comment stripping).")
        print("  !! Do NOT read this as 'no crossing values'. Check for an unbalanced /* in a "
              "line comment.")
        print()
        return -1

    print(f"=== {name}  [{lo}-{hi}]  {hi - lo + 1} lines ===")
    print(f"  locals declared        : {len(names)}")
    print(f"  locals living >1 line  : {len(spans)}")
    print(f"  PEAK SIMULTANEOUS LIVE : {peak}   (at line {peak_line})")
    widest = sorted(spans.items(), key=lambda kv: kv[1][0] - kv[1][1])[:8]
    if widest:
        print("  widest spans:")
        for n, (a, b) in widest:
            print(f"    {n:<24} {a} -> {b}   ({b - a} lines)")
    print()
    return peak

ROOTS = [
    ("Hrot/Subsystems/Hrot.CGF/CgfSubsystem.cs",          509,  1192, "CGF (inline)"),
    ("Hrot/Subsystems/Hrot.Editor/EditorSubsystem.cs",    949,  1884, "Editor (inline)"),
    ("Stride/HrotStrideApp.Game/EditorStrideSubsystem.cs", 509,  718, "Stride editor (inline)"),
]

if __name__ == "__main__":
    if len(sys.argv) == 2 and sys.argv[1] == "--roots":
        for p, a, b, lbl in ROOTS:
            if os.path.exists(p):
                measure(p, a, b, lbl)
            else:
                print(f"=== {lbl}: MISSING {p} ===\n")
    elif len(sys.argv) >= 4:
        measure(sys.argv[1], int(sys.argv[2]), int(sys.argv[3]),
                sys.argv[4] if len(sys.argv) > 4 else None)
    else:
        print(__doc__ or "usage: crossing-values.py <file> <start> <end> [label] | --roots")
        sys.exit(2)
