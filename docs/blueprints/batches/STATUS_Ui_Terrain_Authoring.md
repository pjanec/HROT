<!--STATUS
state: LIVE
updated: 2026-11-11
current-answer: the table
related-designs:
  - HANDOFF_Coordinator_To_Ui_Terrain_Authoring.md — the frame (dispatched at 2235ff0c2; scope frozen there)
  - ../../DESIGN_Terrain_Authoring.md — the owning design; §15a holds the as-built of each item
  - STATUS_Backend_Terrain_Authoring_2.md — backend's parallel batch (T-1 generator library, T-2 feature kinds, T-4 Unpack: NOT built when this started)
-->

# STATUS — ui: terrain authoring in the editor ([handoff](HANDOFF_Coordinator_To_Ui_Terrain_Authoring.md))

Started at `c8dc93df4` (merged `coordinator` + `backend@80051b2f0`). Tracker ids from the ui block: CE-1481 … (appended after CE-1480).

| # | id | state | sha | note |
|---|---|---|---|---|
| U-1 | CE-1481 | ✅ DONE | see git log | Add Entity ▸ six terrain groups; rail `TerrainAuthoringPickerTests.U1_*` + frame rail `TerrainAddEntityFrameRail` (red-proved) |
| U-2 | CE-1481 | ✅ DONE | see git log | the placing ghost, R, one `TerrainBlock` / `TerrainFeature` per click; rails `TerrainAuthoringPickerTests.U2_*` (red-proved, 0 B steady ghost) |
| U-3 | CE-1482 | ⏳ | | |
| U-4 | CE-1483 | | | |
| U-5 | CE-1484 | | | |
| U-6 | CE-1485 | | | |
| U-7 | CE-1486 | | | |
| U-8 | CE-1487 | | | |

## Claim table (decisions taken, with their basis)

| decision | code | design |
|---|---|---|
| the terrain groups are intrinsic (appended by `GalleryGroups`), not a `groups.json` edit | ✅ `groups.json` is seeded to the NAS and an older copy would hide them | ✅ §2f G1 (the file owns DIS groups; a recipe is not a DIS type, G-15) |
| a block / house is a NEW TKB type pair 8817 / 8818 | ✅ measured free on every remote branch; `TkbEntityTypes.cs:147` is the last 88xx | ✅ §18 line 783: *"the TKB type + the Add Entity groups for blocks / features — ui's (B-5)"* |
| the ghost expands with the base world, not the merged one | ✅ `TerrainResidency.ExpansionBase()` is what `LiveOverlay.Expand` uses (O-7 B) | ✅ §18e-2 B-2 |
| tile pictures generated from the recipe, not the §2j atlas | ✅ the atlas ships model renders of TKB types; a recipe has no model | ✅ §2j (atlas for entity types); B-5 says *"rendered from its default seed by the existing atlas pipeline"* — ⚠ deviation: a plain footprint picture until the atlas pipeline can render a recipe |
