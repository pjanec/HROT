# Note: the OSM bridge decks are in Unreal's Stechovice (CE-11063)

| | |
|---|---|
| **From** | `unreal-ig`, 2026-11-27 |
| **For** | the HROT-integration session (and terrain, FYI) |
| **Answers** | [NOTE_UiIntegration_To_UnrealIg_Osm_Bridges_Ready.md](NOTE_UiIntegration_To_UnrealIg_Osm_Bridges_Ready.md) §3 |
| **Commit** | `c8986ba29` (scripts only; the level and its meshes are in the Unreal project, never in this repo) |

⭐ **Done.** All 9 decks of the re-imported Stechovice are in the Unreal level, from HROT's `world.geojson` and nothing else; screenshots of both
Vltava crossings went to the user in chat (none in git).

## 1. How

| step | what |
|---|---|
| import | HROT's own `hrot-terrain-import` (HROT-live `9c38cb332`, importer revision 5) into `data/shared/assets/terrain/Stechovice` — the input set's manifest had changed after the last import (the regenerated net), so the editor would have re-imported it the same way |
| export | `scripts/terrain-to-unreal.py` re-run on the new import (the ground changed: the new net re-levels the roads — 1.3 M posts moved, up to 5.9 m; up to 2.3 m near the bridges), then the level rebuilt (`scripts/unreal/look.py`) |
| ⭐ decks | **new** `scripts/unreal/hrot_bridge_mesh.py`: every `slab` / `ramp` with `structure` `osm-bridge/*`, `zRef: ground`; each corner at `ground + above`, the ground sampled by `TerrainHeightGrid.Sample`'s rule (planar in a cell's two triangles) on the same posts, a ramp's foot buried 0.3 m as `TerrainWorldParser.Buried` does ⇒ **the deck's top is HROT's walkable**. Laid by `hrot_look.py`'s new `step_bridges` on every rebuild (and by `hrot_add_bridges.py` into an existing level) |
| look only | a walkable has no thickness, so the deck is drawn 0.6 m deep below HROT's top (sides and underside concrete, top dark asphalt, flat placeholders). No piers, no railings |

| bridge (`structure`) | slabs | above the ground (m) |
|---|---|---|
| **`osm-bridge/26476508`** — Vltava south, 123 m | 13 | 0 … 5.71 |
| **`osm-bridge/4412124`** — Vltava north ("most Vltavanů"), 297 m | 32 | −0.30 … 15.83 |
| `osm-bridge/4929683` | 13 | −0.43 … 9.41 |
| `osm-bridge/153205115#1` | 16 | −0.03 … 3.89 |
| `osm-bridge/1369180768#1` | 16 | 0.04 … 1.63 |
| `osm-bridge/26127089` | 17 | −0.35 … 0.65 |
| `osm-bridge/1151327975` | 6 | −0.35 … 0.97 |
| `osm-bridge/1533993177` | 3 | −0.92 … 0.98 |
| `osm-bridge/951897515` | 3 | −0.47 … 1.39 |

No `ramp` parts in this import (no deck end stands > 0.3 m above its bank).

## 2. FYI — corners below the ground, as written by the importer

The `above` values in `world.geojson` go **negative** on 7 of the 9 bridges (the column above is the file's own numbers; our sampled ground
reproduces them exactly). Largest: `osm-bridge/1533993177` −0.92 m, `4929683` −0.43 m. So a deck end is buried in its bank there — drawn as
sent (the ground hides it). If a deck is meant to meet its bank at ≥ 0, that is the importer's (`DeckGeometry.BuildAlong`); ⛔ not changed here.

## 3. Still open, unchanged

`stechovice-battle`'s **authored** bridge (kind-10 at (762, 1590)) needs the `TerrainFeature` topic on DPS
([NOTE_UnrealIg_To_UiIntegration_Scenario_Terrain_Features.md](NOTE_UnrealIg_To_UiIntegration_Scenario_Terrain_Features.md)).
