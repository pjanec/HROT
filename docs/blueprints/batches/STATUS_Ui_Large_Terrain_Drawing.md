<!--STATUS
state: LIVE
updated: 2026-11-10
current-answer: §1 the items · §2 the numbers · §3 the frames · §4 gates
stale-below: nothing
related-designs:
  - ../../DESIGN_Map_3D_Mode.md — §6ah the design of D-1 (chunks, levels, the picker per chunk) and its as-built
  - ../../DESIGN_Scale_Rig.md — §5.2 N-5 handed the editor's whole-soup build to this batch
  - HANDOFF_Coordinator_To_Ui_Large_Terrain_Drawing.md — the dispatch (D-1 … D-4)
-->

# STATUS — drawing the large terrain (ui; CE-3423, CE-4376, CE-4377, CE-4378)

Handoff: [HANDOFF_Coordinator_To_Ui_Large_Terrain_Drawing.md](HANDOFF_Coordinator_To_Ui_Large_Terrain_Drawing.md) (dispatched at `e0865ce9e`; started at `3db0cef53`).

## 1. The items

| item | state |
|---|---|
| **D-1** CE-3423 the 3-D ground by level of detail | ✅ DONE — design [§6ah](../../DESIGN_Map_3D_Mode.md) · built: `ITerrainChunkedGeometry` (`TerrainWorldQuery`), `TerrainWorldMesh.Chunks.cs`, `TerrainChunks3D`, `ChunkPickGrids` |
| **D-3** CE-4377 buildings and trees at scale | 🟡 TREES DONE ([§6ai](../../DESIGN_Map_3D_Mode.md), mockup `docs/UX/terrain-trees-3d.svg`): lone crowns over the trunks, woods near as trees (one per density cell), far as a canopy sheet; scale-1 with trees 2.0–2.6 M triangles a frame, 517–534 MB resident. The 85 000 template prisms are batched per chunk (1.1 M triangles). ⏳ pitched roofs + imported facades next (the importer landed at the D-1 merge) |
| **D-2** CE-4376 the 2-D map at 5.5 km | ✅ DONE ([Terrain World §10a](../../DESIGN_Terrain_World.md)) — the fills were already one retained mesh (281 k triangles, 12 MB, built in 0.4 s); the per-frame stroke cull now walks only the cells in view: scale-1 whole **14.5 → 3.65 ms**, close **4.2 → 0.24 ms**, 0 B, the same strokes (rail). ⚠ The whole-terrain zoom still draws 64 k outlines through the gizmo renderer. ⛔ **The "height shading" does not exist in 2-D** (graph + grep) — a NEW look, not built (R-106): it would change the small hilly terrains that must stay unchanged; for the coordinator to decide |
| **D-4** CE-4378 the importer in the wizard | ✅ DONE — the importer (CE-4368, landed at the D-1 merge) composed in `EditorSubsystem` with the NAS roots the New Scenario picker lists; "Open in New Scenario" opens that picker on the new terrain (`NewAssetLauncher.OpenScenarioOn`); "Show folder" via the shell; ONE terrain-name rule (`Hrot.Terrain.InputSet.TerrainName`) for the importer and the wizard. Rails: `TheWizardImportsWithTheRealImporterTests` (fake pipeline → the importer's MiniSet → the REAL importer → the catalog lists it; red-proved), `NewScenarioOnATerrainTests`; C-14 frame rail green. ⚠ The menu item stays greyed until a tile source or place search exists (no way to pick a place) |

## 2. Numbers — scale-1 (5.5 × 5.5 km, 1 m heights, 1 651 houses → 95 669 prisms), this VM (4 cores, software GL: CPU side only)

| | before (the whole soup) | after (by chunk) |
|---|---|---|
| what the load waits for | the whole tagged soup: **9.4 s, 61.4 M triangles, peak 7.0 GB** (Scale Rig §5.1) — ⛔ on this VM the process is **killed for memory at 6.0 GB** before it finishes (`HROT_MEASURE_SOUP=1`) | the coarse picture: **0.8 s**, 2.4 M triangles (ground 1.06 M at 8 m + objects 1.11 M + apron 0.21 M), 191 MB of vertices, peak 640 MB |
| near camera (40 m up, centre) | the same 61.4 M | 16 chunks at 1 m, 20 at 2 m, 96 at 4 m, 352 at 8 m: 4.4 M ground triangles + 1.1 M objects (before frustum culling) |
| far camera (3 km up) | the same 61.4 M | all 484 chunks at 8 m: 1.06 M + 1.1 M |
| the picker | a grid over the whole soup | a grid per chunk the ray crosses (level 0 + objects), ≤ 64 kept |
| drawn per frame, frustum-culled (the look rail, the map camera's clip planes) | — | near 1.85 M triangles / 230 meshes · mid 1.85 M / 584 · far 2.39 M / 855; resident 478–511 MB; process peak ≈ 2.1 GB |
| draw calls | 1 | ≤ 2 per visible chunk + 1 (≤ 969 before culling) |
| garbage per steady frame | — | **0 B** (rail) |

## 3. Frames *(rail `TheTerrainChunksLookTests`, `HROT_RAIL_SHOTS`; to the scratchpad and the chat — never git)*

| | before | after | same pixels |
|---|---|---|---|
| demo-city near / mid / far | the whole-mesh path (the geometry wrapped non-chunked, L-11) | by chunk | **100.0 % / 100.0 % / 100.0 %** (rail: near ≥ 98 %, mid ≥ 95 %) |
| scale-1 near / mid / far | ⛔ not drawable — the whole soup is the 61 M triangles this removes | by chunk: the whole 5.5 km visible, no cracks between levels, the apron round it | — |

Visible differences: none on demo-city. On scale-1 the far ground is the 8 m level (smooth hills, coarser forest edges) — what level of detail means.

## 4. Gates

| gate | result |
|---|---|
| `dotnet test Fdp.Toolkits.Tests --filter TerrainChunkTests\|GroundBinsEquality\|TileSourceEquality` | **15 / 0** — level 0 + objects + outside = the whole tagged build on demo-city, test-town, highway-rest, bt-range; the navmesh tiles unchanged |
| `xvfb-run -a dotnet test Hrot.Presentation.Tests --filter Map3D` | 573 / 2 — ⚠ the 2 reds are PRE-EXISTING: `FilmFrameRail.CE1380_A2DFilmFrame…` and `Map3DFrameRail.CE1227_V_DemoCity…` (both 2-D) fail identically at the start commit `890d6feaf` (measured in a clean worktree) |
| `xvfb-run -a … --filter TheTerrainChunksTests` | 3 / 0 — the eye and frustum from rlgl, the level rule, coarse-first + level 0 near + **0 B a steady frame** |
| `xvfb-run -a … --filter TheTerrainChunksLookTests` (`HROT_MEASURE=1` for scale-1) | 2 / 0 |
| red-proofs (compiling mutations) | the eye read from M41…M43 ⇒ `TheEyeAndTheFrustum…` red · level 0 without the building cut ⇒ the equality rail red on 4 / 4 terrains · the importer on another terrains root ⇒ the D-4 rail red |
| D-3 trees: `dotnet test Fdp.Toolkits.Tests --filter TerrainChunk\|TerrainWoods` | 20 / 0 |
| D-2: `dotnet test Hrot.Presentation.Tests --filter TheBinnedStrokeCull` · `xvfb-run -a … --filter Map2D\|CE1227\|TerrainLayer2D` | 4 / 0 (red-proof: the one-cell slack dropped ⇒ red) · 29 / 1 — the 1 is the PRE-EXISTING `CE1227_V_DemoCity…` (red at the start commit) |
| D-4: `Hrot.Terrain.Import.Tests` · `Hrot.Presentation.Tests --filter TerrainOsm` · the C-14 frame rails (xvfb) · `Hrot.Editor.Tests --filter NewAssetLauncher\|ScenarioMenu\|FileMenu\|TheNewScenarioPane\|NewScenarioOnATerrain` | 36 / 0 · 12 / 0 · 6 / 0 · 50 / 0 |
