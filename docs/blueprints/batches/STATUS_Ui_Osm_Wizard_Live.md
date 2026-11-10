<!--STATUS
state: LIVE
updated: 2026-11-10
current-answer: the table "Items", then "Gates"
related-designs:
  - docs/DESIGN_Terrain_From_OSM.md — OWNS the wizard (§6 / §6b, as-built in §6b-as-built) and the library (§14); this file is the batch record only
  - docs/blueprints/batches/STATUS_Ui_Osm_Wizard.md — the wizard's first batch (CE-4375) and its C-row conformance table, which E-2 brings to ✅
-->

# STATUS — the OSM wizard LIVE (ui; CE-4381, CE-4382, CE-1466)

Handoff [HANDOFF_Coordinator_To_Ui_Osm_Wizard_Live.md](HANDOFF_Coordinator_To_Ui_Osm_Wizard_Live.md), dispatched at `e233f69a6`; started at `ea546c50e`
(coordinator merged). Cross-lane edits, all stated in the commits: `Hrot/Engine/Hrot.Terrain.Osm` (the coordinator's library, allowed by the handoff) and
`FDP/ExtDeps/NodeEdit/src/NodeEditor.UI/Picker` (the shared image cache, CE-1468).

## Items

| # | id | state | what was built |
|---|---|---|---|
| **E-1** | CE-4381 (+ CE-1468) | ✅ DONE | `OsmTileSource` + `NominatimPlaceSearch` in `Hrot.Terrain.Osm/Source/`, composed in `EditorSubsystem` on the pipeline's one `HttpRetry` client ⇒ File ▸ New ▸ Terrain from OpenStreetMap… is no longer greyed for want of them. The tile texture LRU is the shared `ImageCache<TKey>` (`PickerImageCache` is its string form). Design: [OSM §6b-as-built](../../DESIGN_Terrain_From_OSM.md) |
| **E-2** | CE-4382 | ✅ DONE | the contract extension (own commit `05c8ae69a`): per building type / storeys source / roof + shape / doors, windows + door rule / shared walls; trees; located notes with a severity; the data timestamp. Filled in `BuildingRules` + `PreviewBuilder`; the wizard draws them — C-10, C-12, C-13 are ✅ with no ⚠ ([STATUS_Ui_Osm_Wizard.md](STATUS_Ui_Osm_Wizard.md) §1). Mockup updated to the same lines |
| **E-3** | CE-1466 | ⛔ PREMISE FAILED (R-106) — nothing to build | CE-1466 was already FIXED `2026-11-09` (`b492b8c70`: a 3-D press answers the camera ray — `MapCamera3D.IsRayOnlyAnchor` — not the 2-D footprint boxes), and the ray goes through `MapCamera3D.Pick` → the pickers, whose `TerrainPicker` uses D-1's per-chunk grids (`Picking.cs`, `ChunkPickGrids`, CE-3423 L-10). Measured: `Map3DPickingTests` + `HoverPickVolumeTests` 18 / 0 — they pick a `TerrainWorldQuery` (chunked) world |

## Gates

| gate | result |
|---|---|
| `dotnet test Hrot.Terrain.Osm.Tests --no-build` | 115 / 0, 1 skipped (the opt-in live rail) — was 108 + 7 new (`MapServicesTests`) |
| `HROT_OSM_LIVE=1 … --filter Live_TileZero_AndKolin` (from the cloud VM) | ✅ 1 / 0 (735 ms: tile 0/0/0 is a PNG; "Kolín" has a box round the town) |
| `dotnet test NodeEditor.UI.Tests --no-build --filter Picker` | 72 / 0 (unchanged picker) + `ImageCacheTests` 3 / 0 |
| `xvfb-run -a dotnet test Hrot.Presentation.Tests --no-build --filter TerrainOsm` | 19 / 0 (incl. `TheWizardSearchesOnEnterOnlyTests`) |
| `dotnet test Hrot.Editor.Tests --no-build --filter TheTerrainFromOsmItem` | 1 / 0 |
| E-2: `dotnet test Hrot.Terrain.Osm.Tests --no-build` | 119 / 0, 1 skipped — + 4 `PreviewDetailsTests` |
| E-2: `xvfb-run -a dotnet test Hrot.Presentation.Tests --no-build --filter TerrainOsm` | 19 / 0 (`Preview_MatchesTheMockup` asserts the new rows); the frame matches the regenerated mockup |
| E-3 evidence: `xvfb-run -a dotnet test Hrot.Presentation.Tests --no-build --filter Map3DPickingTests\|HoverPickVolumeTests` | 18 / 0 |
| red-proofs (each compiles) | the tile gate at 6 ⇒ `Tiles_AtMostTwoRequestsAtOnce` red · "pending" remembered as failed ⇒ `APendingKey_…` red · `Tiles: null, Search: null` in the editor ⇒ `TheRealEditor_…` red · E-2: `SharedWith` not filled ⇒ `SharedWalls_NameTheNeighbour_BothWays` red · the tooltip ignoring `RoofShape` ⇒ `Preview_MatchesTheMockup` red |
