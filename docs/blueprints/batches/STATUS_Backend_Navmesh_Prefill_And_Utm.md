<!--STATUS
state: LIVE
updated: 2026-11-10
current-answer: the table below
stale-below: nothing
related-designs:
  - HANDOFF_Coordinator_To_Backend_Navmesh_Prefill_And_Utm.md — the frame (dispatched at bedca0325)
  - ../../DESIGN_Terrain_Import_UTM.md — the owning design; the details go into its §12
-->

# STATUS — backend: the navmesh baked at import (CE-4393) and `UtmTransform` (CE-4394)

Started at `a2c6f6fa0` (merged `coordinator@4b1cf15c5`), marker `639e54cc4`. ⚠ The off-frame egress (CE-3427 N-1) is PAUSED for this, at
`bcff5a430` — its resume point is RESUME_Backend_Large_Terrain_Perf.md §0.

| step | state |
|---|---|
| inventory (graph + grep) | ✅ UTM §12 INVENTORY |
| design — UTM §12 (UML, INVENTORY, leans) | ✅ `9abda457f` |
| build U (CE-4394) | ✅ `c08674f77` — DONE |
| build N (CE-4393) | ✅ `75080d0f1`, `9967c86d9` — ⏳ `real-1`'s first open in the editor on Windows (the user's machine) |
| as built | ✅ UTM §12.3; follow-ups CE-3428 (pack the tiles), CE-3429 (SUMO's private UTM), CE-3430 (the Replay Browser's transform) |

## Gates (base `a2c6f6fa0`; every suite built as its TEST project, then `--no-build`)

| suite | command | result |
|---|---|---|
| geographic + terrain definition | `dotnet test FDP/Toolkits/Fdp.Toolkits.Tests --no-build --filter "…Geographic|…TerrainDefinition"` | 229 / 0 (7 new) |
| navigation + terrain + Recast + geographic | `… --filter "…Navigation|…Terrain|…Recast|…Geographic"` | 1 017 / 0 |
| terrain load + building damage | `dotnet test Hrot/Subsystems/Hrot.SimHost.Tests --no-build --filter "…TerrainLoadStepTests|…BuildingDamageBakeTests"` | 33 / 0 (1 new) |
| the importer | `dotnet test Hrot/Engine/Hrot.Terrain.Import.Tests --no-build` | 42 / 0 (4 new) — ⚠ the project needed a restore (never restored on this VM; the new reference changed its graph) |
| builds | `Hrot.ClusterRunner` (editor, Player, debug API), both CLI tools (Debug + Release) | 0 errors |

**Red-proofs (compiling mutations, built and run):** Commit not passing the frame ⇒ `CE4394_…` red · the baker ignoring the seed ⇒ both
prefill rails red · the seed's files touched ⇒ `APrefilledTerrains_FirstLoad…` red.

**Needs the user:** `real-1`'s first open in the editor on Windows (≤ 30 s, R-4192) and the `navtiles/` size of `real-1` / `osm-kolin-test`
(the input sets are on the user's machine) — re-import with `hrot-terrain-import --set … --name … --replace --verify`: the report prints
the navtiles size, `--verify` the first load's navmesh time and how many tiles came from the seed.
