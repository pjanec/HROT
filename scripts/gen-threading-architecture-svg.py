#!/usr/bin/env python3
"""The threading architecture across host modes — one explainer picture (docs/img/threading-architecture.svg).

Regenerate:  python3 scripts/gen-threading-architecture-svg.py
Owned by docs/DESIGN_Threaded_Simulation_Presentation.md §14 (the picture is an explainer; the UML lives in the design).
"""
from pathlib import Path
from xml.sax.saxutils import escape

W, H = 1500, 1205
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
text(30, 38, "One kernel, three schedulings — threads, worlds and crossings by host mode (AS BUILT)", 22, weight="bold")
text(30, 62, "✔ built on exp/threaded-build (overnight 2026-11-12/13, N-1…N-17) · ⏳ in flight (R-4218 the mode, R-4220 the review source, the serial systems). "
             "Numbers: Debug, 4-core VM, 1 000 men; Windows Release numbers pending (CE-4489).", 13, C["sub"])

# ---------------------------------------------------------------- the three mode panels
cols = [
    ("① WINDOWED — editor, player (HROT_THREADED_KERNEL=3)", "the UI on the main thread, the simulation on ONE sim worker"),
    ("② HEADLESS — CGF, SimHost, IG (HROT_PARALLEL_SIM=1)", "no presentation; the tick on the process's main thread"),
    ("③ DETERMINISTIC — stepped, Run-to, filming, regression, tests", "same threads as ① or ②; chosen by what is running (R-4218 ⏳)"),
]
px, pw, gap, py, ph = 30, 470, 15, 80, 485
lanes_y = [py + 70, py + 190, py + 330]   # MAIN, SIM, POOL
lane_h = [100, 120, 140]
for i, (title, sub) in enumerate(cols):
    x = px + i * (pw + gap)
    add(f'<rect x="{x}" y="{py}" width="{pw}" height="{ph}" rx="8" fill="#fbfcfe" stroke="{C["line"]}" stroke-width="1.5"/>')
    text(x + 14, py + 26, title, 14, weight="bold")
    text(x + 14, py + 46, sub, 11.5, C["sub"])
    for j, (name, col, fill) in enumerate([("MAIN thread", C["main"], C["mainf"]),
                                           ("SIM worker (one per process)", C["sim"], C["simf"]),
                                           ("WORKER POOL — FdpWorkerPool (HROT_SIM_THREADS, cores − 2)", C["pool"], C["poolf"])]):
        ly = lanes_y[j]
        add(f'<rect x="{x+10}" y="{ly}" width="{pw-20}" height="{lane_h[j]}" rx="6" fill="{fill}" fill-opacity="0.45" stroke="{col}" stroke-opacity="0.5"/>')
        text(x + 18, ly + 16, name, 11.5, col, "bold")

def lane(i, j): return px + i * (pw + gap) + 10, lanes_y[j]

# ① windowed
x, y = lane(0, 0)
box(x + 8,  y + 26, 100, 30, C["lockf"], C["lock"], "PULL newest\nstaging → A ✔", 10.5)
box(x + 113, y + 26, 150, 30, C["mainf"], C["main"], "the WHOLE gizmo block\n+ panels on WORLD A ✔", 10.5)
box(x + 268, y + 26, 60, 30, C["mainf"], C["main"], "draw", 10.5)
box(x + 333, y + 26, 107, 30, C["poolf"], C["pool"], "edits → JOBS\n(SimJobQueue) ✔", 10.5)
text(x + 8, y + 76, "never waits for a tick, no lock: UI frame ≈ 45 ms at 1 / 2 / 4 threads", 11, C["main"])
text(x + 8, y + 92, "pull ≈ 4–5 ms, only when a newer tick is complete", 11, C["sub"])
x, y = lane(0, 1)
box(x + 8, y + 24, 70, 34, C["simf"], C["sim"], "drain\nJOBS ✔", 10.5)
box(x + 83, y + 24, 140, 34, C["simf"], C["sim"], "TICK on WORLD S\n(per-entity phases)", 10.5)
box(x + 228, y + 24, 70, 34, C["simf"], C["sim"], "readouts\n(save gate…)", 10.5)
box(x + 303, y + 24, 137, 34, C["lockf"], C["lock"], "PUBLISH into the free\nbuffer — or SKIP ✔", 10.5)
text(x + 8, y + 78, "two staging buffers claimed by CAS; both busy ⇒ skip, the next", 11, C["sub"])
text(x + 8, y + 93, "publish carries it all (R-4217) · publish ≈ 4 ms · 0 skips measured", 11, C["sub"])
text(x + 8, y + 108, "--mode all: EVERY kernel of the process on this one worker ✔", 11, C["sub"])
x, y = lane(0, 2)
box(x + 8, y + 24, 215, 30, C["poolf"], C["pool"], "brains · squads (per squad) · movement ✔", 10.5)
box(x + 228, y + 24, 212, 30, C["bg2f"], C["bg2"], "background lane: EQS window ✔", 10.5)
box(x + 8, y + 58, 215, 32, C["poolf"], C["pool"], "threat · stress · callouts · rays ✔\nweapons, the toggled group ⏳", 10)
box(x + 228, y + 58, 212, 32, C["bg2f"], C["bg2"], "replica R: egress scan ✔\n(recorder: capture on the tick)", 10)
text(x + 8, y + 112, "BrainTick 352 / 182 / 135 ms at 1 / 2 / 4 threads (Debug VM)", 11, C["sub"])
text(x + 8, y + 127, "≈ 800 ms of the tick still serial — the next conversions ⏳", 11, C["sub"])

# ② headless
x, y = lane(1, 0)
add(f'<rect x="{x+8}" y="{y+26}" width="{pw-36}" height="44" rx="4" fill="{C["grey"]}" stroke="{C["line"]}" stroke-dasharray="5 4"/>')
text(x + (pw - 20) / 2, y + 45, "no main-thread presentation · no world A", 12, C["sub"], anchor="middle")
text(x + (pw - 20) / 2, y + 61, "no staging, no job queue — nothing paid for a UI", 12, C["sub"], anchor="middle")
x, y = lane(1, 1)
box(x + 8, y + 24, 70, 34, C["simf"], C["sim"], "ingress", 10.5)
box(x + 83, y + 24, 185, 34, C["simf"], C["sim"], "TICK on WORLD S\n(the process's main thread)", 10.5)
box(x + 273, y + 24, 80, 34, C["simf"], C["sim"], "egress\n(events)", 10.5)
box(x + 358, y + 24, 82, 34, C["bg2f"], C["bg2"], "S → R", 10.5)
text(x + 8, y + 78, "the ONE thread that writes S; no locks in the tick", 11, C["sub"])
text(x + 8, y + 93, "Windows: EcoQoS / power throttling OFF, QuickEdit off ✔ (N-14)", 11, C["sub"])
x, y = lane(1, 2)
box(x + 8, y + 24, 215, 30, C["poolf"], C["pool"], "the same per-entity phases as ①", 10)
box(x + 228, y + 24, 104, 66, C["bg2f"], C["bg2"], "egress scan\non R ✔", 10)
box(x + 336, y + 24, 104, 66, C["bg2f"], C["bg2"], "recorder: capture\non the tick, file\nwrite async ✔", 10)
box(x + 8, y + 58, 215, 32, C["poolf"], C["pool"], "background solvers (EQS) ✔", 10)
text(x + 8, y + 112, "own entity written directly; others by command,", 11, C["sub"])
text(x + 8, y + 127, "played in batch order; their data read from the tick-start view", 11, C["sub"])

# ③ deterministic
x, y = lane(2, 0)
box(x + 8, y + 26, 432, 30, C["mainf"], C["main"], "windowed: as ① — the UI still never waits for a tick", 10.5)
text(x + 8, y + 76, "tests and rails: INLINE (HROT_THREADED_SINGLE=1 runs the", 11, C["sub"])
text(x + 8, y + 92, "same sim step, pull and presentation step on one thread)", 11, C["sub"])
x, y = lane(2, 1)
box(x + 8, y + 24, 140, 34, C["simf"], C["sim"], "TICK on WORLD S", 10.5)
box(x + 153, y + 24, 140, 34, C["simf"], C["sim"], "per-entity phases\nPARALLEL too", 10.5)
box(x + 298, y + 24, 142, 34, C["lockf"], C["lock"], "step boundary: JOIN\nbackground jobs", 10.5)
text(x + 8, y + 78, "✔ the SAME world at 1, 2, 4, 8 threads — 7 stepped runs, 1 000 men", 11, C["sub"])
text(x + 8, y + 93, "one value for the whole process (R-4215), set from the clock (R-4218 ⏳)", 11, C["sub"])
x, y = lane(2, 2)
box(x + 8, y + 24, 432, 30, C["poolf"], C["pool"], "fixed 32-entity batches, buffers played in batch order = entity order ✔", 10.5)
box(x + 8, y + 58, 432, 32, C["bg2f"], C["bg2"], "Throughput (continuous live play): adaptive batch size — the same rule", 10.5)
text(x + 8, y + 112, "a result that differs by batch size or thread count is a BUG,", 11, C["sub"])
text(x + 8, y + 127, "never a reason for a mode (R-4218)", 11, C["sub"])

# ---------------------------------------------------------------- the strategies table
ty = py + ph + 30
text(30, ty, "The strategies — what holds in every mode", 18, weight="bold")
rows = [
    ("WORLDS", C["sim"],
     "S — the simulation, ONE writer (the sim worker and its pool)   ·   A — the UI's world, ONE writer (the main thread), the same object for life   ·   "
     "B1 / B2 — two staging buffers (102 MB at 1 000 men)   ·   the tick-start view (11 MB) for cross-entity reads   ·   R — the replica for off-frame egress; the recorder captures on the tick and writes its file asynchronously"),
    ("CROSSINGS", C["bg2"],
     "DOWN: one SimJobQueue — UI writes, run controls, sim-side HTTP, the UI's bus publishes, review commands (R-4220 ⏳)   ·   UP: the staging — masked tables (the active "
     "gizmo layers), the selected rows, singletons, and a tick-tagged packet FIFO: events, logs, edit outcomes, undo steps, the bus pair, readouts — exactly once, in order"),
    ("NO LOCK", C["lock"],
     "buffers claimed by compare-and-swap; the sim never waits — both busy ⇒ it skips that publish (R-4217)   ·   the UI pulls only a newer complete tick   ·   "
     "inside the tick per-batch command buffers, never a lock"),
    ("SINGLETONS", C["pool"],
     "managed components are immutable and replaced whole (audit: 6 in-place sites, the traffic frame fixed and value-copied)   ·   a managed read never stamps (R-4210)   ·   "
     "shared objects immutable or swapped in one write   ·   derived maps rebuilt   ·   values only the sim can compute are published as readouts"),
    ("UI STATE", C["main"],
     "the WHOLE gizmo block on the main thread over A (R-4211)   ·   a drag is a ghost in A, the drop one job through the shared write router; refused ⇒ snap back   ·   "
     "undo: the recorder on S, the ONE history on the UI   ·   UI-side HTTP routes read the shown world"),
    ("INSIDE THE TICK", C["pool"],
     "OWN entity (its parts; a squad job its members) read and written directly   ·   ANOTHER entity: declared types from the tick-start view, declared stable reads live   ·   "
     "writes to others, add / remove / create / destroy, publishes ⇒ the batch's buffer, played in batch order   ·   callouts, egress marks, rule messages ⇒ follow-ups in batch order   ·   "
     "HROT_READ_CHECK=1 throws on an undeclared read, =2 lists them"),
    ("JOB SYSTEM", C["ink"],
     "ONE pool (frame lane + background lane), no dependency scheduler   ·   ParallelEntities.Run: 32-entity batches carrying the tick context and the current entity   ·   "
     "EQS's window sized by the same budget   ·   switches: HROT_THREADED_KERNEL=3 · HROT_PARALLEL_SIM=1 · HROT_SIM_THREADS — all OFF by default, 0 B per steady frame"),
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
hdr = ["", "staging B1 / B2 + pull", "SimJobQueue", "per-entity phases", "tick-start view", "replica R", "joins at a step"]
vals = [
    ("① windowed, playing", ["✔ ≈ 4 ms + 4–5 ms", "✔", "✔ Throughput", "✔ masked", "if off-frame egress on", "—"]),
    ("① windowed, stepping", ["✔", "✔", "✔ Deterministic", "✔", "if on", "✔"]),
    ("② headless live", ["—", "—", "✔ Throughput", "✔", "if off-frame egress on", "—"]),
    ("③ regression / filming", ["windowed only", "windowed only", "✔ Deterministic", "✔", "if on", "✔"]),
    ("--mode all", ["per staged subsystem", "one per kernel", "✔ one worker, in sequence", "✔", "if on", "as ① / ③"]),
    ("tests (inline)", ["same step, one thread", "inline", "✔", "✔", "—", "—"]),
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
print("bottom", yy + rh * (len(vals) + 1))

add("</svg>")
p = Path(__file__).resolve().parent.parent / "docs/img/threading-architecture.svg"
p.parent.mkdir(parents=True, exist_ok=True)
p.write_text("\n".join(out), encoding="utf-8")
print(p)
