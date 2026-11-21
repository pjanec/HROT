<!--STATUS
state: LIVE — REPORTED (every row reproduced, root-caused, fixed and railed)
updated: 2026-11-17
current-answer: §1 the rows, §2 gates
related-designs:
  - HANDOFF_Coordinator_To_Terrain_User_Observations.md — the dispatch
  - ../../DESIGN_Ground_Patches_And_Road_Blockage.md — §9.12 (ground shapes; the flat-terrain row)
  - ../../DESIGN_Map_3D_Mode.md — §6ai (the trees; the no-body list)
-->

# STATUS — `terrain`: the user's editor observations (CE-1963, CE-1964, CE-1968, CE-1966) — started at `ea1dd1750` (merged `coordinator@a401d7d22`)

## 1. The rows

| row | reproduced | root cause | fix | rail (red-proved) · scenario |
|---|---|---|---|---|
| **CE-1963** the ditch tool draws no ditch | ✅ live: the editor (headless), bt-range, a Ditch + a 2 m Raise feature ⇒ `/terrain/levels` z 0 at both | a FLAT terrain (no height grid, only `GroundZ`) — `GroundShapes.Apply(null, …)` returned null in the merge, so every shape vanished silently; every rail used a ZERO grid | `TerrainWorldMerge.FlatGrid`: a flat grid over the bounds when an overlay brings shapes. Live after: ditch z −1, raise z +2 | `TerrainWorldMergeTests.CE1963_*` (fix off ⇒ z 0.00 / 34.00) · `tt-ground-shaping` 7/7 |
| **CE-1964** raise / lower does nothing, no hint | ✅ the same live run (raise z 0); the tool's effect rail | the same as CE-1963, AND `GroundEffect.Of` returned `None` with no grid ⇒ no tint, no hint line while drawing | the effect uses the same flat grid. ⚠ The panel's title *"Ground — click to start"* does show at arm; an area is ≥ 3 clicks + Enter | `GroundEffectTests.CE1964_*` (guard back ⇒ red) · `tt-ground-shaping` |
| **CE-1968** wood trees ~10 m off | ✅ the rail: a wood off the 25 m grid ⇒ trunks outside it | ⛔ not the origin lean: the woods are a world-aligned 25 m grid (R-4190 T-2, a cell is wood when its CENTRE is in), and the near trees filled the WHOLE cell | near trees planted from the polygons (`AddPolygonTrees`), same cells and hash, kept inside; sim woods and far canopy stay per cell | `TerrainChunkTreesTests.CE1968_*` (clip off ⇒ a trunk at x 10.6, edge 12) |
| **CE-1966** a pink cube in every placed feature | ✅ the rail: `LookOf(8818 / 8817)` = `Unknown` | terrain features / blocks were not on the 3-D body layer's no-body list ⇒ its unknown box at the anchor (2-D already skipped them) | both on the list — ⚠ a one-line cross-lane edit in ui's `EntityBodyLayer3D`, said | `Map3DTests.CE1966_*` (red before the fix) |

⭐ **Scenario `tt-ground-shaping`** (`scenarios/demos/navigation-and-terrain/`, bt-range — the flat case that hid it): a man ON the 2 m hill, one IN the 1 m hollow, one IN the
ditch, a car that never crosses the ditch — **7/7**; with the fix off the hill and hollow rows go red. ⚠ The ditch's "no vehicle crosses" area held even un-carved (it is
read from the shape), so the Ditch Rifle row is what sees the carve. **Golden: captured, two fresh runs "same".**

## 2. Gates (rule 8)

Built from the final tree `75b2475da`, test projects built then `--no-build`.

| gate | command | result | note |
|---|---|---|---|
| toolkit terrain (merge, woods, chunk trees …) | `dotnet test FDP/Toolkits/Fdp.Toolkits.Tests --no-build --filter …Fdp.Toolkit.Terrain.Tests` | **471 / 471** | |
| presentation: Map3D, AddEntity, the draw panel, terrain editor, minefield (frame rails under `xvfb-run -a`) | `xvfb-run -a dotnet test Hrot/Engine/Hrot.Presentation.Tests --no-build --filter "…Map3D\|…AddEntity\|…TerrainDrawPanel\|…TerrainEditor\|…Minefield"` | 843 pass · **8 red** · 7 skipped | ⭐ **6 PRE-EXISTING**, confirmed red at the base `ea1dd1750` in a clean worktree: `S1_TheIconFallback…` (`"_tank"` vs null), `CE1309_TheMindStateBar…`, `CE1086_TheFpvDrone…` (size), `TheGalleryOpenedOnClutter…` (no greyed scatter tile), `CE4388_TheCachedWireSphere…` (timing), `MotorcycleModelRail…` (seats 3 vs 2) — none in this batch's code; ⚠ 2 red ONLY in the full run (`FighterHudRail…SteadyFrameAllocatesNothing`, `CE4582_TheCardsHide…`): green at base AND green 3/3 alone on this tree ⇒ in-suite interference, not this batch |
| SimHost: overlay, terrain load, bakes | `dotnet test Hrot/Subsystems/Hrot.SimHost.Tests --no-build --filter "…Overlay\|…TerrainLoad\|…Bake"` | **96 / 96** | |
| the new rails, each red-proved | `CE1963_*` (flat grid off ⇒ z 0.00 / 34.00) · `CE1964_*` (the no-grid guard back) · `CE1968_*` (clip off ⇒ a trunk at x 10.6) · `CE1966_*` (red before the fix: `Unknown`) | all red, then green | |
| ⭐ the feature scenario | `OUT=… NO_BUILD=1 scripts/scenario-regression.sh tt-ground-shaping` | **7 / 7**; golden captured + 2 fresh runs "same" | with the flat grid off: the hill and hollow rows red |
| `tracker-counts.py --check` | | OK | no new ids; CE-1963/1964/1966/1968 marked done |
| not run | E2E / `--mode all`; `HROT_PARALLEL_SIM` checks | — | no behaviour code changed (terrain data, drawing, a tool's preview) |
