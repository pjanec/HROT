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
| **E-2** | CE-4382 | ⏳ next | |
| **E-3** | CE-1466 | ⏳ if time is left | |

## Gates

| gate | result |
|---|---|
| `dotnet test Hrot.Terrain.Osm.Tests --no-build` | 115 / 0, 1 skipped (the opt-in live rail) — was 108 + 7 new (`MapServicesTests`) |
| `HROT_OSM_LIVE=1 … --filter Live_TileZero_AndKolin` (from the cloud VM) | ✅ 1 / 0 (735 ms: tile 0/0/0 is a PNG; "Kolín" has a box round the town) |
| `dotnet test NodeEditor.UI.Tests --no-build --filter Picker` | 72 / 0 (unchanged picker) + `ImageCacheTests` 3 / 0 |
| `xvfb-run -a dotnet test Hrot.Presentation.Tests --no-build --filter TerrainOsm` | 19 / 0 (incl. `TheWizardSearchesOnEnterOnlyTests`) |
| `dotnet test Hrot.Editor.Tests --no-build --filter TheTerrainFromOsmItem` | 1 / 0 |
| red-proofs (each compiles) | the tile gate at 6 ⇒ `Tiles_AtMostTwoRequestsAtOnce` red · "pending" remembered as failed ⇒ `APendingKey_…` red · `Tiles: null, Search: null` in the editor ⇒ `TheRealEditor_…` red |
