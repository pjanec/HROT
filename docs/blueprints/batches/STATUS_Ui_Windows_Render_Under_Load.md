<!--STATUS
state: LIVE
updated: 2026-11-10
current-answer: "Where it stands" — then the table "R-1 measured"
related-designs:
  - docs/DESIGN_Map_3D_Mode.md — OWNS §6ah (the chunked terrain) where the "measured on Windows" empty-terrain baseline (L-3) lives; the under-load block goes beside it
  - docs/DESIGN_Scale_Rig.md — OWNS the rig scenario (1 000 men, 50 vehicles, 10 aircraft) used as the load
  - docs/UX/UX_Feature_Map_Parity.md — §3.2f / §3.2g: culling as a gizmo POLICY (one implementation, default off for compatibility only) — the basis of the view-filter fix
-->

# STATUS — rendering the large terrain UNDER LOAD (ui; CE-4387 … CE-4390)

Handoff [HANDOFF_Coordinator_To_Ui_Windows_Render_Under_Load.md](HANDOFF_Coordinator_To_Ui_Windows_Render_Under_Load.md), dispatched at `bdccd3d86` (stamped
`a1a0054be`); started after the live-terrain batch, `coordinator@a1a0054be` merged into ui at `7489ecfe9`. The user's Windows machine, Release, **every run hidden**
(`HROT_HIDDEN_WINDOW=1`) and **uncapped** (`--target-fps 0`) — GPU-honest, no vsync / compositor.

## ⛔ A premise that failed — the editor is NOT a cluster viewer

The handoff said *"run the cluster (orchestrator + cgf + simhost) and the editor as a live viewer"*. 🔒 User, `2026-11-10`: *"Editor is networkless no point running it
with orchestrator."* 📐 Measured first: started beside the rig's cgf + simhost, both nodes died at start (`[DdsIdAllocator] Publication match not established within
30 s. Hrot.Orchestrator must be running`) — also with `--mode orchestrator,editor`. ⇒ ⭐ **The load runs IN the editor**: the rig scenario loaded for editing and
played in **preview** (the sim in-process). ⚠ So the frame also carries the SIMULATION (brains, EQS, weapons) that a real viewer does not run — the per-system profile
separates it. ⭐ The networked viewer is R-4's Player.

## Where it stands

| | |
|---|---|
| scale-1 + 1 000 men / 50 vehicles / 10 aircraft, preview | **5–7.5 fps in every view** (134–194 ms) — loaded in 31.5 s, 14 364 entities |
| the frame (3-D near) | update **115 ms** · world draw **55 ms** · UI 7.5 ms; garbage 3.2 / 3.1 / 0.85 MB per frame |
| ⭐ the update is mostly the SIM | `EqsSolverSystem` 142 ms per execution · brains / weapons / stress / callouts / pathfinding ≈ 40 ms — not the viewer's cost |
| ⭐ the RENDER side | **≈ 25 ms map preparation** (`StatelessGizmoSystem` 21.6 ms — 11.4 on the empty terrain; styling 1.65 ms + 290 KB; layer assignment 1.2 ms; data-driven gizmos 0.8 ms) **+ 55 ms world draw** (3 MB / frame) ≈ **80 ms** against the 33 ms budget |
| next | the world draw split by map layer (now logged per layer under `HROT_FRAME_STATS`), then the fixes biggest first: the shared view filter for the gizmos (cull + zoom), then the draw's biggest layer |

## The world draw by map layer (`HROT_FRAME_STATS`, per layer — new; scale-1, 1 000 men, preview)

| layer | 3-D mid | 2-D whole terrain |
|---|---|---|
| **Entities (3-D)** — bodies, vehicles, aircraft | **30.7 ms** · 685 KB | — |
| **Terrain (2-D)** — the 2-D plan | — | **17.3 ms · 4.1 MB** (also on the EMPTY terrain: L-3's 6.3 MB 2-D draw) |
| **Effects** | **12.7 ms** · 147 KB | 1.5 ms |
| **Debug Gizmos** — drawing the gizmo buffer | 5.5 ms · **2.3 MB** | 7.9 ms · **4.3 MB** |
| the chunked 3-D terrain | under the top six | — |

⇒ the render cost, biggest first: **unit bodies 31 ms** · **gizmos 22 ms build + 6–8 ms draw, 2–4 MB** · **the 2-D plan 17 ms, 4 MB** · **effects 13 ms (3-D)**.

## R-1 measured — scale-1, 1 000 men, preview (hidden, uncapped, 2200 × 1200)

| view | fps min / avg | ms mean / p95 | terrain tris / meshes | garbage / frame |
|---|---|---|---|---|
| 3-D near 40 m (the terrain centre — see the finding below) | 6.0 / 6.2 | 161 / 185 | 2.1 M / 268 | 9.4 MB |
| 3-D mid 400 m | 5.2 / 5.4 | 194 / 221 | 2.2 M / 278 | 8.8 MB |
| 3-D far 3 km | 4.7 / 5.1 | 178 / 211 | 2.1 M / 930 | 9.9 MB |
| 3-D flight 15 m/s | 4.7 / 5.3 | 167 / 195 | 2.2 M / 314 | 8.7 MB |
| 2-D whole terrain | 5.8 / 7.0 | 134 / 152 | — | 14.1 MB |
| 2-D close 150 m | 6.7 / 7.5 | 148 / 169 | — | 16.5 MB |

Empty-terrain baseline (L-3): [Map 3D §6ah "Measured on Windows"](../../DESIGN_Map_3D_Mode.md).

## Findings

- ⛔ **`GET /entities` fails as a whole** on the loaded rig scenario: *".NET number values such as positive and negative infinity cannot be written as valid JSON"* —
  one component holds ±∞ and the whole list is refused (so the run could not find the fighting units to aim the near camera at). → to file.
- ⭐ Doors: 6 912 door entities on scale-1 are walked by ~20 gizmo rules every frame (the rules match `SimTransform`); styled every frame (nothing reads a door's
  style — "no palette entry", `DESIGN_Building_Interiors.md` §3j 5b); the 2-D door lines are drawn for all doors at every zoom. A door's pick box IS designed
  (selectable) — so the fix is the view filter, not excluding doors.
