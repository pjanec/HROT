#!/usr/bin/env python3
"""The anti-race strategy and the gizmo split — one explainer picture (docs/img/threading-anti-race.svg).

Regenerate:  python3 scripts/gen-anti-race-svg.py
Owned by docs/DESIGN_Threaded_Simulation_Presentation.md §14.2 (explainer; the rules are §14.1).
"""
from pathlib import Path
from xml.sax.saxutils import escape

W, H = 1500, 1155
out = []
add = out.append
C = dict(ink="#1f2430", sub="#5b6475", line="#c9cfd9", sim="#c2410c", simf="#fff0e6", main="#2f6fde", mainf="#e6efff",
         lock="#b88a00", lockf="#fff6d6", pub="#7c3aed", pubf="#f1eaff", cmd="#0f8a5f", cmdf="#e3f6ee", grey="#eef0f4")

def text(x, y, s, size=12.5, fill=None, weight="normal", anchor="start"):
    add(f'<text x="{x}" y="{y}" font-size="{size}" fill="{fill or C["ink"]}" font-weight="{weight}" text-anchor="{anchor}">{escape(s)}</text>')

def box(x, y, w, h, fill, stroke, label="", size=11.5, weight="normal", dash=None):
    d = f' stroke-dasharray="{dash}"' if dash else ""
    add(f'<rect x="{x}" y="{y}" width="{w}" height="{h}" rx="5" fill="{fill}" stroke="{stroke}" stroke-width="1.3"{d}/>')
    if label:
        lines = label.split("\n")
        y0 = y + h / 2 - (len(lines) - 1) * (size + 2) / 2 + size / 2 - 1
        for i, l in enumerate(lines):
            text(x + w / 2, y0 + i * (size + 2), l, size, C["ink"], weight, "middle")

def arrow(x1, x2, y, col):
    add(f'<line x1="{x1}" y1="{y}" x2="{x2}" y2="{y}" stroke="{col}" stroke-width="2" marker-end="url(#a{col[1:]})"/>')

add(f'<svg xmlns="http://www.w3.org/2000/svg" width="{W}" height="{H}" viewBox="0 0 {W} {H}" font-family="Segoe UI, Helvetica, Arial, sans-serif">')
add('<defs>' + "".join(
    f'<marker id="a{c[1:]}" viewBox="0 0 10 10" refX="9" refY="5" markerWidth="7" markerHeight="7" orient="auto-start-reverse">'
    f'<path d="M0,0 L10,5 L0,10 z" fill="{c}"/></marker>' for c in (C["sim"], C["main"], C["pub"], C["cmd"], C["lock"])) + '</defs>')
add(f'<rect width="{W}" height="{H}" fill="#ffffff"/>')
text(30, 38, "No races by construction — who writes what, and how each kind of data crosses between the threads", 21, weight="bold")
text(30, 60, "Rule: each world has ONE writer. The UI reads only its own world A; the simulation reads only S. Every crossing below uses one of the mechanisms shown — and only the yellow ones take a lock.", 13, C["sub"])

# column frame
LX, LW = 30, 330          # S column
MX, MW = 380, 740         # crossing column
RX, RW = 1140, 330        # A column
top = 80
box(LX, top, LW, 44, C["simf"], C["sim"], "WORLD S — written ONLY by the SIM thread and its pool\n(own entity directly, other entities by command)", 12, "bold")
box(MX, top, MW, 44, C["grey"], C["line"], "HOW IT CROSSES", 13, "bold")
box(RX, top, RW, 44, C["mainf"], C["main"], "WORLD A — written ONLY by the MAIN thread\n(except the sync, which holds A's lock)", 12, "bold")

rows_sim_to_ui = [
    ("components the map, 3-D, overlays draw", "TABLE COPY at the sync — a mask of types, all rows  (≈1.3 ms)", "lock", "the same tables in A"),
    ("the selected / inspected units' components", "ROW COPY at the sync — types × selected entities  (µs)", "lock", "their rows in A (panels, inspector)"),
    ("small per-tick singletons: GlobalTime, traces", "COPIED BY VALUE at the sync", "lock", "their copies in A"),
    ("derived maps: NetworkEntityMap", "REBUILT in A from the copied ids  (0.76 ms)", "lock", "A's own map"),
    ("changing service state: cluster cache, clock", "IMMUTABLE VIEW published — one atomic reference swap  (0.01 ms)", "pub", "the latest view (no lock)"),
    ("shared objects: terrain, geo, TKB, behaviours, navmesh", "SHARED BY REFERENCE — immutable, or replaced in ONE atomic swap", "pub", "the same object, old or new, never half"),
    ("native memory: the road graph", "LEASED — the old version lives until the last reader lets go", "pub", "a lease per sync"),
    ("logs: shots, detonations, edit outcomes, squad reports", "FORWARDED — the new entries copied at the sync", "lock", "A's own logs"),
    ("events on S's bus (hits, deaths, outcomes)", "FORWARDED into A's bus at the sync — each reader gets each once", "lock", "A's bus"),
    ("remote nodes' gizmo streams (CGF, SimHost …)", "RECEIVED over the network, published to the map — triple buffer", "pub", "the map draws them with A's own gizmos"),
]
rows_ui_to_sim = [
    ("edits: move, delete, spawn, orders, rules", "COMMAND — a thread-safe queue, drained at the tick's start (Input), validated; refused ⇒ the edit outcome"),
    ("drag / rotate / place", "a GHOST in A every frame — the drop is ONE command"),
    ("run controls: play, pause, step, Run-to", "COMMANDS — the clock lives with the simulation"),
    ("settings the sim reads (viewport, debug toggles)", "RIDE WITH THE COMMANDS — copied to S once per tick"),
]

y = top + 60
text(LX, y + 14, "SIM → UI   (the sync runs on the sim thread at the tick's end)", 14, C["sim"], "bold")
y += 24
rh = 32
legend_col = {"lock": (C["lock"], C["lockf"]), "pub": (C["pub"], C["pubf"])}
for (s_lbl, how, kind, a_lbl) in rows_sim_to_ui:
    col, fill = legend_col[kind]
    box(LX, y, LW, rh - 6, "#fbfcfe", C["line"], s_lbl, 11.5)
    box(MX + 40, y, MW - 80, rh - 6, fill, col, how, 11.5)
    arrow(LX + LW + 2, MX + 38, y + (rh - 6) / 2, col)
    arrow(MX + MW - 40, RX - 2, y + (rh - 6) / 2, col)
    box(RX, y, RW, rh - 6, "#fbfcfe", C["line"], a_lbl, 11.5)
    y += rh

y += 10
text(LX, y + 14, "UI → SIM   (the UI never writes S)", 14, C["cmd"], "bold")
y += 24
for (a_lbl, how) in rows_ui_to_sim:
    box(RX, y, RW, rh - 6, "#fbfcfe", C["line"], a_lbl if len(a_lbl) < 52 else a_lbl[:48] + "…", 11.5)
    box(MX + 40, y, MW - 80, rh - 6, C["cmdf"], C["cmd"], how, 11.5)
    arrow(RX - 2, MX + MW - 38, y + (rh - 6) / 2, C["cmd"])
    arrow(MX + 40 - 2, LX + LW + 4, y + (rh - 6) / 2, C["cmd"])
    box(LX, y, LW, rh - 6, "#fbfcfe", C["line"], "applied by the sim thread", 11.5)
    y += rh

y += 10
text(LX, y + 14, "STAYS ON ONE SIDE — never crosses", 14, C["ink"], "bold")
y += 24
box(LX, y, LW, 40, "#fbfcfe", C["line"], "brain memory, sensors, EQS, physics, the\nnode frame, the orchestrator, recording", 11.5)
box(MX + 40, y, MW - 80, 40, C["grey"], C["line"], "a reader on the other side asks through a command or the HTTP API (answered at a tick boundary)", 11.5, dash="5 4")
box(RX, y, RW, 40, "#fbfcfe", C["line"], "selection, menus, camera, order form,\neffects, panel layout — written directly", 11.5)
y += 56

# reads are reads
box(30, y, W - 60, 30, C["lockf"], C["lock"],
    "⚠ READS MUST BE READS: a managed-component read and a singleton read stop returning a writable ref / stamping the chunk (A1-2)  ·  the 3 unsafe statics get a lock / Concurrent / Lazy  ·  a Debug guard throws on a write to S off the sim thread", 11.5)
y += 40
box(30, y, W - 60, 30, C["cmdf"], C["cmd"],
    "INSIDE THE TICK (the same rule one level down): a per-entity system writes its OWN entity  ·  other entities by command, merged in a fixed order  ·  their data read as LAST tick  ·  ParallelGuard", 11.5)
y += 46

# ---------------------------------------------------------------- the gizmo block (R-4211)
text(30, y + 14, "The gizmos — the WHOLE gizmo block runs on the main thread over world A (R-4211: no split by kind)", 17, weight="bold")
y += 28
gw = (W - 60 - 40) / 3
g = [
    ("ONE BLOCK — main thread, every frame", C["main"], C["mainf"],
     ["all 75 registered gizmos: overlays (navmesh, cover,", "EQS, brain debug, routes, names, traces …), tools",
      "(drag, rotate, editors, pickers, RTS mode, box select)", "and UI gizmos — one module, one place, as today",
      "", "pick boxes, pickers and selection stay together", "(CE-259r: they hit-test THIS frame's boxes)", "", "maintained as one thing — no per-kind homes", ""]),
    ("WHAT IT READS — world A", C["lock"], C["lockf"],
     ["the overlays read A's copies; the S→A mask follows", "the ACTIVE gizmo layers: nothing when none is shown,", "≈ 57 types / 1.3 ms with all on, usually just labels",
      "", "content still changes only when S ticks — drawn", "at the frame rate from the latest copy", "", "cost moved off the SIM tick: ≈ 4.4–5.5 ms", "(it now runs on the main thread's ~16 ms frame)", ""]),
    ("WHAT IT WRITES — commands only", C["cmd"], C["cmdf"],
     ["input → the tool's FSM every frame → a GHOST in A", "→ the commit is ONE COMMAND to S", "",
      "⛔ today the drag writes SimTransform every frame", "(EntityDragGizmo.cs:278) — that becomes the ghost", "",
      "headless nodes (CGF, SimHost) have no main-thread", "presentation: their gizmo stream is produced in", "their own tick and sent over the network, as today", ""]),
]
for i, (title, col, fill, lines) in enumerate(g):
    gx = 30 + i * (gw + 20)
    gh = 30 + 17 * len(lines) + 10
    add(f'<rect x="{gx}" y="{y}" width="{gw}" height="{gh}" rx="7" fill="{fill}" fill-opacity="0.55" stroke="{col}" stroke-width="1.4"/>')
    text(gx + 14, y + 22, title, 14, col, "bold")
    for k, l in enumerate(lines):
        text(gx + 14, y + 44 + 17 * k, l, 12)
y += gh + 14
box(30, y, W - 60, 30, C["grey"], C["line"],
    "the gizmo MODULE also runs non-gizmo world WRITERS (action dispatch: Delete / LoadZone, effect + wreck spawn, lifespan) — those are simulation and stay in the tick", 11.5)
y += 44
add(f'<rect x="30" y="{y}" width="14" height="14" fill="{C["lockf"]}" stroke="{C["lock"]}"/>')
text(50, y + 12, "copied at the sync, under A's lock (the only lock)", 12)
add(f'<rect x="420" y="{y}" width="14" height="14" fill="{C["pubf"]}" stroke="{C["pub"]}"/>')
text(440, y + 12, "published / shared — an atomic reference swap or a lease, no lock", 12)
add(f'<rect x="880" y="{y}" width="14" height="14" fill="{C["cmdf"]}" stroke="{C["cmd"]}"/>')
text(900, y + 12, "a command — the UI's only way into S", 12)
add("</svg>")
p = Path(__file__).resolve().parent.parent / "docs/img/threading-anti-race.svg"
p.write_text("\n".join(out), encoding="utf-8")
print(p, int(y + 30))
