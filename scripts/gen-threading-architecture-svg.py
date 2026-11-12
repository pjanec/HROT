#!/usr/bin/env python3
"""The threading architecture across host modes — one explainer picture (docs/img/threading-architecture.svg).

Regenerate:  python3 scripts/gen-threading-architecture-svg.py
Owned by docs/DESIGN_Threaded_Simulation_Presentation.md §14 (the picture is an explainer; the UML lives in the design).
"""
from pathlib import Path
from xml.sax.saxutils import escape

W, H = 1500, 1090
out = []
def add(s): out.append(s)

C = dict(bg="#ffffff", ink="#1f2430", sub="#5b6475", line="#c9cfd9",
         main="#2f6fde", sim="#c2410c", pool="#0f8a5f", bg2="#7c3aed", lock="#d4a106",
         mainf="#e6efff", simf="#fff0e6", poolf="#e3f6ee", bg2f="#f1eaff", lockf="#fff6d6", grey="#eef0f4")

def text(x, y, s, size=13, fill=None, weight="normal", anchor="start"):
    add(f'<text x="{x}" y="{y}" font-size="{size}" fill="{fill or C["ink"]}" font-weight="{weight}" '
        f'text-anchor="{anchor}">{escape(s)}</text>')

def box(x, y, w, h, fill, stroke, label=None, size=11, fc=None, rx=4, weight="normal"):
    add(f'<rect x="{x}" y="{y}" width="{w}" height="{h}" rx="{rx}" fill="{fill}" stroke="{stroke}" stroke-width="1.2"/>')
    if label:
        lines = label.split("\n")
        y0 = y + h / 2 - (len(lines) - 1) * (size + 2) / 2 + size / 2 - 1
        for i, l in enumerate(lines):
            text(x + w / 2, y0 + i * (size + 2), l, size, fc or C["ink"], weight, "middle")

add(f'<svg xmlns="http://www.w3.org/2000/svg" width="{W}" height="{H}" viewBox="0 0 {W} {H}" font-family="Segoe UI, Helvetica, Arial, sans-serif">')
add(f'<rect width="{W}" height="{H}" fill="{C["bg"]}"/>')
text(30, 38, "One kernel, three schedulings — how the threads, worlds and copies change by host mode", 22, weight="bold")
text(30, 62, "Numbers: measured 2026-11-12 at 1 000 men (threaded-analysis A-3, the Windows parallel run, egress §7). "
             "Nothing here is built except where marked ✔ (exists) — the rest is the lean.", 13, C["sub"])

# ---------------------------------------------------------------- the three mode panels
cols = [
    ("① WINDOWED — editor, player", "interactive; Throughput while playing, Deterministic when stepping / Run-to / filming"),
    ("② HEADLESS LIVE TRAINING — CGF, SimHost, IG nodes", "no presentation at all; Throughput"),
    ("③ DETERMINISTIC — regression, filming, stepping", "same threads as ① or ②, different rules"),
]
px, pw, gap, py, ph = 30, 470, 15, 80, 485
lanes_y = [py + 70, py + 190, py + 330]   # MAIN, SIM, POOL
lane_h = [100, 120, 140]
for i, (title, sub) in enumerate(cols):
    x = px + i * (pw + gap)
    add(f'<rect x="{x}" y="{py}" width="{pw}" height="{ph}" rx="8" fill="#fbfcfe" stroke="{C["line"]}" stroke-width="1.5"/>')
    text(x + 14, py + 26, title, 15, weight="bold")
    text(x + 14, py + 46, sub, 11.5, C["sub"])
    for j, (name, col, fill) in enumerate([("MAIN thread", C["main"], C["mainf"]),
                                           ("SIM thread", C["sim"], C["simf"]),
                                           ("WORKER POOL (cores − 2)", C["pool"], C["poolf"])]):
        ly = lanes_y[j]
        add(f'<rect x="{x+10}" y="{ly}" width="{pw-20}" height="{lane_h[j]}" rx="6" fill="{fill}" fill-opacity="0.45" stroke="{col}" stroke-opacity="0.5"/>')
        text(x + 18, ly + 16, name, 11.5, col, "bold")

def lane(i, j): return px + i * (pw + gap) + 10, lanes_y[j]

# ① windowed
x, y = lane(0, 0)
box(x + 8,  y + 26, 95, 30, C["mainf"], C["main"], "input →\ncommands", 10.5)
box(x + 108, y + 26, 150, 30, C["mainf"], C["main"], "panels · 2-D · 3-D · tool\ngizmos on WORLD A", 10.5)
box(x + 263, y + 26, 60, 30, C["mainf"], C["main"], "draw", 10.5)
box(x + 328, y + 26, 112, 30, C["lockf"], C["lock"], "waits only for a\nSYNC (≈1–6 ms)", 10.5)
text(x + 8, y + 76, "every frame at the render rate — never waits for a tick", 11, C["main"])
text(x + 8, y + 92, "world A = one object for life: selection, menus, effects stay here", 11, C["sub"])
x, y = lane(0, 1)
box(x + 8, y + 24, 70, 34, C["simf"], C["sim"], "drain\ncommands", 10.5)
box(x + 83, y + 24, 140, 34, C["simf"], C["sim"], "TICK on WORLD S\n(serial + parallel fans)", 10.5)
box(x + 228, y + 24, 70, 34, C["simf"], C["sim"], "hot egress\n(events)", 10.5)
box(x + 303, y + 24, 70, 34, C["lockf"], C["lock"], "S → A\nsync 🔒", 10.5)
box(x + 378, y + 24, 62, 34, C["bg2f"], C["bg2"], "S → R\n≈1 ms", 10.5)
text(x + 8, y + 78, "A sync = map / 3-D / overlay tables ≈ 1.3 ms + panel rows of", 11, C["sub"])
text(x + 8, y + 93, "the selected units (µs); the inspector never widens it", 11, C["sub"])
text(x + 8, y + 108, "pause / Edit: the tick keeps running with the clock held", 11, C["sub"])
x, y = lane(0, 2)
box(x + 8, y + 24, 215, 30, C["poolf"], C["pool"], "parallel fans inside the tick", 10.5)
box(x + 228, y + 24, 212, 30, C["bg2f"], C["bg2"], "off-frame, on REPLICA R:", 10.5)
box(x + 228, y + 58, 104, 32, C["bg2f"], C["bg2"], "state egress\nscan ✔ (CE-3427)", 10)
box(x + 336, y + 58, 104, 32, C["bg2f"], C["bg2"], "recorder\ncapture", 10)
box(x + 8, y + 58, 215, 32, C["poolf"], C["pool"], "background solvers (EQS …)\non SoD snapshots ✔", 10)
text(x + 8, y + 112, "the off-frame work overlaps the NEXT tick", 11, C["sub"])
text(x + 8, y + 127, "and the UI frames; it joins at the next Export", 11, C["sub"])

# ② headless
x, y = lane(1, 0)
add(f'<rect x="{x+8}" y="{y+26}" width="{pw-36}" height="44" rx="4" fill="{C["grey"]}" stroke="{C["line"]}" stroke-dasharray="5 4"/>')
text(x + (pw - 20) / 2, y + 45, "no main-thread presentation · no world A", 12, C["sub"], anchor="middle")
text(x + (pw - 20) / 2, y + 61, "no A sync, no lock, no masks — zero overhead for the UI", 12, C["sub"], anchor="middle")
x, y = lane(1, 1)
box(x + 8, y + 24, 70, 34, C["simf"], C["sim"], "ingress", 10.5)
box(x + 83, y + 24, 185, 34, C["simf"], C["sim"], "TICK on WORLD S\n(the process's main thread)", 10.5)
box(x + 273, y + 24, 80, 34, C["simf"], C["sim"], "hot egress\n(events)", 10.5)
box(x + 358, y + 24, 82, 34, C["bg2f"], C["bg2"], "S → R\n≈1–6 ms", 10.5)
text(x + 8, y + 78, "the ONE thread that writes S: no locks in the tick", 11, C["sub"])
text(x + 8, y + 93, "R is taken only when an off-frame consumer is switched on", 11, C["sub"])
x, y = lane(1, 2)
box(x + 8, y + 24, 215, 30, C["poolf"], C["pool"], "parallel fans: brains*, stress ✔,\nthreat ✔, weapons, squads", 10)
box(x + 228, y + 24, 104, 66, C["bg2f"], C["bg2"], "state egress\nscan on R ✔\n(positions,\nstate)", 10)
box(x + 336, y + 24, 104, 66, C["bg2f"], C["bg2"], "recorder\ncapture on R\n(file I/O\nalready async ✔)", 10)
box(x + 8, y + 58, 215, 32, C["poolf"], C["pool"], "background solvers on SoD ✔", 10)
text(x + 8, y + 112, "* brains in parallel: Throughput only (R-4208 — a friend's", 11, C["sub"])
text(x + 8, y + 127, "  cover claim seen one tick late, over-allocation recovers)", 11, C["sub"])

# ③ deterministic
x, y = lane(2, 0)
box(x + 8, y + 26, 432, 30, C["mainf"], C["main"], "windowed: as ① — the UI still never waits for a tick", 10.5)
text(x + 8, y + 76, "tests and rails: INLINE — one thread, the live world through a", 11, C["sub"])
text(x + 8, y + 92, "read-only view, no copies (R-4206 F)", 11, C["sub"])
x, y = lane(2, 1)
box(x + 8, y + 24, 140, 34, C["simf"], C["sim"], "TICK on WORLD S", 10.5)
box(x + 153, y + 24, 140, 34, C["simf"], C["sim"], "brains SERIAL\n(order-dependent)", 10.5)
box(x + 298, y + 24, 142, 34, C["lockf"], C["lock"], "step boundary: JOIN\nbackground jobs", 10.5)
text(x + 8, y + 78, "same input ⇒ same bytes at 1, 2, 4 or 8 threads (rails ✔)", 11, C["sub"])
text(x + 8, y + 93, "stepped / Run-to wait for background jobs (Time §17)", 11, C["sub"])
x, y = lane(2, 2)
box(x + 8, y + 24, 432, 30, C["poolf"], C["pool"], "parallel fans with PER-BATCH buffers merged in batch order ✔ (57 µs)", 10.5)
box(x + 8, y + 58, 432, 32, C["bg2f"], C["bg2"], "off-frame egress / recorder: allowed — they read, never write S", 10.5)
text(x + 8, y + 112, "the price of determinism is the WAIT at a step, not the", 11, C["sub"])
text(x + 8, y + 127, "buffers (§7.2)", 11, C["sub"])

# ---------------------------------------------------------------- the strategies table
ty = py + ph + 30
text(30, ty, "The strategies — what holds in every mode", 18, weight="bold")
rows = [
    ("WORLDS", C["sim"],
     "S — the simulation, ONE per node, ONE writer (the sim thread)   ·   A — the presentation's world, windowed only, the same object for life   ·   "
     "R — one GDB replica per tick for off-frame readers   ·   SoD snapshots for background solvers ✔"),
    ("COPIES", C["bg2"],
     "S→A: a MASK per consumer — tables for the map / 3-D / overlays (≈1.3 ms), panel ROWS for the selected units; the inspector reads rows only "
     "(the whole-world copy, ≈13 ms, rejected)   ·   S→R: full, ≈1 ms SimHost / 6 ms CGF   ·   a copy is taken only when someone reads it"),
    ("LOCKS", C["lock"],
     "ONE short lock on A (the sync vs the UI's world work)   ·   none inside the tick — parallel loops write through per-batch command buffers   ·   "
     "R and the gizmo frame are published / leased, never locked"),
    ("SINGLETONS", C["pool"],
     "share what is immutable or swapped in one write (terrain, geo, navmesh ✔; TKB, behaviour registry → swap the inside)   ·   copy small values (time, traces)   ·   "
     "rebuild derived maps (NetworkEntityMap 0.76 ms)   ·   lease native memory (roads ✔)   ·   publish immutable views (cluster cache 0.01 ms, clock status)"),
    ("UI STATE", C["main"],
     "selection, menus, order form, effects live in A (outside the mask)   ·   the UI changes S only by COMMANDS (drag = a ghost in A, the drop = a command)   ·   "
     "UI work leaves the tick FIRST (S-1: 26 tool gizmos, camera, selection, 4 after-kernel objects)"),
    ("STATICS / READS", C["ink"],
     "3 unsafe statics fixed (RunModeChoice.PresetCache, UtilityInputNames, UtilityDecisionCatalog)   ·   a managed READ must stop stamping chunk versions (A1-2) "
     "— today it also feeds false changes to the recorder and egress"),
]
yy = ty + 16
import textwrap
for name, col, body in rows:
    lines = textwrap.wrap(body, 205)
    h = 12 + 16 * len(lines)
    add(f'<rect x="30" y="{yy}" width="{W-60}" height="{h}" rx="5" fill="#fbfcfe" stroke="{C["line"]}"/>')
    add(f'<rect x="30" y="{yy}" width="6" height="{h}" fill="{col}"/>')
    text(48, yy + 20, name, 12.5, col, "bold")
    for k, l in enumerate(lines):
        text(185, yy + 20 + 16 * k, l, 12)
    yy += h + 6

# ---------------------------------------------------------------- overhead by mode
yy += 26
text(30, yy, "What each mode pays — nothing it does not use", 18, weight="bold")
yy += 12
hdr = ["", "world A + S→A sync", "the A lock", "replica R", "worker pool", "per-batch ordered merge", "joins at a step"]
vals = [
    ("① windowed, playing", ["✔ masked, per tick", "✔ ≈ the sync", "if egress / recorder on", "✔", "—", "—"]),
    ("① windowed, stepping", ["✔", "✔", "if on", "✔", "✔", "✔"]),
    ("② headless live", ["—", "—", "if egress / recorder on", "✔", "—", "—"]),
    ("③ regression / filming", ["windowed only", "windowed only", "if on", "✔", "✔", "✔"]),
    ("tests (inline)", ["— (read-only view)", "—", "—", "optional", "✔", "—"]),
]
cw = [210, 205, 150, 200, 130, 220, 150]
xs = [30]
for w_ in cw[:-1]: xs.append(xs[-1] + w_)
rh = 24
for r, row in enumerate([("", hdr[1:])] + vals):
    label, cells = row
    ry = yy + r * rh
    fill = "#eef2f8" if r == 0 else ("#ffffff" if r % 2 else "#f7f8fb")
    add(f'<rect x="30" y="{ry}" width="{sum(cw)}" height="{rh}" fill="{fill}" stroke="{C["line"]}" stroke-width="0.6"/>')
    text(xs[0] + 8, ry + 16, label if r else "mode", 12, weight="bold")
    for k, cell in enumerate(cells):
        text(xs[k + 1] + 8, ry + 16, cell, 12, weight="bold" if r == 0 else "normal")

add("</svg>")
p = Path(__file__).resolve().parent.parent / "docs/img/threading-architecture.svg"
p.parent.mkdir(parents=True, exist_ok=True)
p.write_text("\n".join(out), encoding="utf-8")
print(p)
