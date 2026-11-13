<!--STATUS
state: LIVE — dispatched, not started
updated: 2026-11-13
current-answer: the whole file
related-designs:
  - STATUS_Windows_Core_Utilisation.md (on exp/windows-threaded-measure) — ⭐ §8 the rendering measurement (R-1 … R-4) and §5 the UI thread (C-5): every number below
  - ../../DESIGN_Map_3D_Mode.md — §6ah the terrain by chunk and LOD, §6ai trees / buildings by distance — OWNS 3-D rendering; the body / door drawing is designed nowhere yet ⇒ the design step here extends this doc
  - ../../DESIGN_People_Display.md — how people look (block figures, poses, faces) — the look the instanced path must keep
  - ../../DESIGN_Threaded_Simulation_Presentation.md (on exp/threaded-build) — §0a the UI thread draws world A
-->

# HANDOFF — coordinator → backend (VM): AUTONOMOUS — the 3-D view's frame: draw the bodies the standard way, cull by distance, stop per-frame CPU work that scales with the view

**For:** a backend VM lane. **From:** coordinator. **Dispatched at `<stamp>`** — scope frozen.
🔒 User, `2026-11-13`: *"that scenario has very poor rendering framerate when all terrain in frustum. some graphical performance optimization is a
must"* · *"the terrain is still ridiculously small and characters are made of few boxes. there must be huge performance improvement opportunities"* ·
*"yes, write both"*.

## ⛔⛔⛔ Never stop — the same contract

Never stop until every item is built and tested; no question in chat. Trouble ⇒ the best-practice option that fits the engine, no hacks,
recorded in DECISIONS. A bug ⇒ fix + a rail. Blocked ⇒ the other items, then back (two approaches before OPEN). ⛔ Never skip a test. Commit +
push per item, by path; tick its tracker row. ⛔ No screenshots in git (render to scratch). Hot path: no LINQ, 0 B per steady frame.

## What the measurement says (Windows, Release, `scale-rig-scale-1-1000-2s`, the editor, uncapped)

| pose | frame | where the UI thread goes |
|---|---|---|
| ② the whole terrain in view | **148 ms (6.7 fps)**, CPU-bound (UI thread 94 %, GPU 23 %) | ⭐ **`EntityBodyLayer3D.Draw3D` 68 %**: one `Raylib.DrawMesh` per body PART (48 %) and per DOOR leaf (15 %) — no distance cull, no far representation, no instancing (`EntityBodyLayer3D.cs:144-151`: CE-4388's frustum cull only). The terrain: 3 % |
| ① street level | 26.5 ms | the hover pick **21 %** every frame (`EntityBoxPicker.TryPick` 11, `DoorLeaf3D.TryLeaf` 7.4), `EffectLayer.Draw3D` 12, the staging pull's gizmos ~9, audio 12.6, ImGui 28 |
| the 2-D map | 32.5 ms | `TerrainLayer2D.Draw` 16 %, `SoundSources.Collect` 13.7 %, `MessageLogPanel` 8 %, the pull's `StatelessGizmoSystem` 9–12 % |

## Where to work

A NEW branch **`exp/render-perf`** from **`origin/exp/threaded-build`**; merge it in when it moves (never rebase). Status file
`docs/blueprints/batches/STATUS_Render_Performance.md`.

## The work

| # | item | done when |
|---|---|---|
| **G-0** · `CE-4554` | ⭐ **the design first**: read Map 3D §6ah / §6ai, People Display, CE-4388; state the proven solution (GPU instancing of repeated meshes, distance culling with a far representation, a pick that runs on input and uses a spatial index) and justify each piece (CLAUDE.md "START FROM THE PROVEN SOLUTION"); write it as a new Map 3D § with the UML (classes, the per-frame sequence, who draws what each frame) | the § with `build-state: READY-TO-BUILD`, its diagrams parse |
| **G-1** · `CE-4555` | ⭐⭐ **bodies, vehicles and doors drawn INSTANCED** — every copy of one part mesh in one call (`DrawMeshInstanced` or the engine's instanced path), per-instance transforms and colours in a buffer refilled without allocation; poses and faces kept | ⭐ at ② the body layer's share drops from 68 % — the draw calls counted (add a counter to the layer, next to `TerrainDrawStats`); the look unchanged at ① (a frame rail compares; side-by-side in the report, scratch only) |
| **G-2** · `CE-4556` | ⭐ **distance culling + a far representation** — beyond a distance (or below a screen size) a body is one box / an impostor, beyond a farther one nothing (the unit icon of the 2-D map is the precedent for "far"); doors beyond a distance not drawn | at ② the frame ⇒ target **≤ 33 ms (30 fps)**; the thresholds in the design with the reason |
| **G-3** · `CE-4557` | ⭐ **the hover pick only when it can change** — on mouse move or camera move, through a spatial index (the frame grid the sim already has, or a per-chunk index like the terrain picker of §6ah) — never a scan of every box every frame | at ① the pick's share from 21 % to ≈ 0 when the mouse is still; the pick rails green |
| **G-4** · `CE-4558` | the 2-D map: `TerrainLayer2D.Draw` drawn from a cached texture / mesh per zoom level, rebuilt only when the view or the terrain changes | its share from 16 % on the 2-D map |
| **G-5** · `CE-4559` | `SoundSources.Collect` / `Movers` (audio) — only the sources within hearing, from a spatial index, at the audio's own rate | its share from 13.7 % |
| **G-6** · `CE-4560` | `MessageLogPanel` — draw only the visible rows (ImGui list clipper) | its share from 8 % |
| **G-7** · `CE-4561` | ⭐ **the proof**: ① / ② / the 2-D map frame before / after on the VM (Release, uncapped) + the ONE Windows command to rerun `STATUS_Windows_Core_Utilisation.md` §8 on real cores; every touched layer's frame rails under `xvfb-run -a` (not skipped); 0 B per steady frame | the GATES table |
| **G-8** · `CE-4562` | fold the as-built into Map 3D (and People Display if the look's mechanics changed) | the design commits named |

## How to work

`scripts/cbm.sh` (graph) AND grep before claiming something exists — e.g. an instancing path, a spatial index, a list clipper already in the code.
The touched layers' OWN rails; build the TEST project before `--no-build`; display rails under `xvfb-run -a`. Long runs in the background, never
polled. Proof proportionate: G-4 … G-6 are cosmetic-risk — one key rail each.

---
_coordinator lane, 2026-11-13_
