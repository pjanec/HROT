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

*(filled when the background gate run lands)*
