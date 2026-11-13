<!--STATUS
state: LIVE — dispatched, not started
updated: 2026-11-13
current-answer: the whole file
supersedes: HANDOFF_Coordinator_To_Backend_Render_Performance.md (held, never dispatched; its G-0 design is done — Map 3D §6ak)
related-designs:
  - ../../DESIGN_Map_3D_Mode.md — ⭐ §6ak THE design (approved): screen-size bands, instancing through one batch, the pick on input — §6ak.1 classes, §6ak.2 one frame, §6ak.3 who draws what, §6ak.4 decisions D-1 … D-8, §6ak.5 acceptance
  - ../../DESIGN_People_Display.md — the figure's look the Full band must keep
  - STATUS_Windows_Core_Utilisation.md (on exp/windows-threaded-measure) — §8 the before numbers, §5 the UI thread
-->

# HANDOFF — coordinator → WINDOWS session: OVERNIGHT, AUTONOMOUS — build Map 3D §6ak (the 3-D view's frame) and the UI thread's plain costs

**For:** the Windows session (real GPU, real cores; hidden window for every automated run). **From:** coordinator. **Dispatched at `c1a645d63`** —
scope frozen. 🔒 User, `2026-11-13`: *"approved, write the performance handoff for autonomous overnight run on windows machine with no question,
auto-deciding"* (R-4226 — §6ak approved as written).

## ⛔⛔⛔ The overnight contract — never stop, never ask

| | |
|---|---|
| ⭐⭐⭐ **Never stop until every item is built and tested. No question in chat — nobody answers at night.** | |
| ⭐⭐⭐ **Trouble ⇒ decide it yourself**: the option that follows best practice and fits the engine and §6ak, no shortcuts, no hacks; a row in DECISIONS (problem · options · chosen · why · where), then continue | the user reviews DECISIONS in the morning |
| ⭐⭐ **A deviation from §6ak** (raylib cannot do something, a band edge pops) ⇒ build the closest correct way, fold the as-built into §6ak, say it in the report | ⛔ never a pause |
| ⭐⭐ **A bug ⇒ fix + a rail. Blocked ⇒ every other item, then back with a different approach** (two before OPEN) | ⛔ never skip, disable or quarantine a test |
| ⭐ **Commit + push after every item, by path; tick its tracker row** (`🔄` → `[x]` + commit, or `⚠ OPEN`) | the status file is the resume point |
| ⛔ No screenshots in git — render to a scratch folder; describe or send them in the report | hot path: no LINQ, 0 B per steady frame |

## Where to work

A NEW branch **`exp/render-perf`** from **`origin/exp/threaded-build`** (merge it in if it moves; never rebase). Push
`chore: started render-perf at <sha>`. Status file `docs/blueprints/batches/STATUS_Render_Performance.md`. Build **Release** for every measurement.
The scenario: **`scale-rig-scale-1-1000-2s`** (regenerate with `scripts/scale-rig.py --terrain scale-1 --men 1000 --write-only` if absent); the
poses of `STATUS_Windows_Core_Utilisation.md` §8 (`POST /film/shot`: the street `lookAt [2750,2750,1.7]` 30 m pitch −8; the whole terrain:
centre, 7 500 m, pitch −60), uncapped (`--target-fps 0`).

## The work — build §6ak in this order

| # | item | done when |
|---|---|---|
| **G-1** · `CE-4555` | ⭐⭐ **`BodyBatch` + `InstancedLitShader`** (§6ak.1): the instance buffer (transform, colour, paint — D-5), grown never shrunk; one `DrawMeshInstanced` per (mesh, paint mode) bucket; the variant reuses `LitShader`'s lighting / fog and `BodySurface`'s GLSL (⛔ no copied shader code) | a rail: N instances drawn in one call per bucket; 0 B per steady frame |
| **G-2** · `CE-4556` | ⭐⭐ **`EntityBodyLayer3D` fills the batch**: every part, face, outfit detail and stain that was a `DrawMesh` becomes an `Add` (D-4); doors too (D-3) | ⭐ the street frame looks the same (a frame rail compares before / after — render to scratch); the body layer's draw-call counter (NEW, beside `TerrainDrawStats`) at the whole-terrain pose: a few calls |
| **G-3** · `CE-4557` | ⭐⭐ **the screen-size bands** (D-1, D-2, D-7): Hidden < 1.5 px (no pose) · Far 1.5–12 px one box in its main colour · Full ≥ 12 px; a selected body always Full; selection boxes, labels, weapon tips, eyes only for Full | rails at the band edges; a selected far unit still drawn in full; no visible pop at the edges (check the frames; widen the Full band if it pops — a DECISION row) |
| **G-4** · `CE-4558` | ⭐ **`BodyPickIndex` + the pick on input** (D-6): `EntityBoxPicker.TryPick` / `DoorLeaf3D.TryLeaf` run only when the mouse or the camera moved, through a uniform grid of the drawn boxes | a counter: 0 pick work while still; the pick rails green |
| **G-5** · `CE-4559` | the 2-D map: `TerrainLayer2D.Draw` (16 % of the 2-D frame) drawn from a cache rebuilt only when the view or the terrain changes | its share measured before / after; its rails green |
| **G-6** · `CE-4560` | `SoundSources.Collect` / `Movers` (13.7 %): only the sources within hearing, at the audio's own rate — decide the mechanism (a DECISION row) | its share before / after; the audio rails green |
| **G-7** · `CE-4561` | `MessageLogPanel` (8 %): only the visible rows (the ImGui list clipper) | its share before / after |
| **G-8** · `CE-4562` | ⭐⭐ **the proof on this machine** (§6ak.5): the whole-terrain pose **≤ 33 ms**, the street **≤ 16.7 ms**, the 2-D map frame — before / after, uncapped, Release; GPU and UI-thread busy %; the draw calls; 0 B per steady frame; every touched layer's frame rails (they need a display — run them on this machine's desktop, not skipped); and the threaded 1 000-man scripted session once (the M-7 chain) with the strict guard — 0 hits | the GATES table; ⭐ if a target is missed: profile the frame again (`dotnet-trace`, the UI thread), fix the next biggest piece, measure again — until met or every remaining piece is named with its ms |
| **G-9** · `CE-4562` | fold the as-built into Map 3D §6ak (`build-state: BUILT`, the deviations, the measured result) and People Display if the look's mechanics changed | the design commits named |

## The report (top of the status file)

DONE · GATES (before / after per pose) · DECISIONS (every auto-decision) · BUGS FIXED · OPEN · the frames (scratch paths, described).

---
_coordinator lane, 2026-11-13_
