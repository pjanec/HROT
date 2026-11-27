# Note: the Vltava bridges are in HROT's Stechovice terrain — draw them from the imported features (CE-11063)

| | |
|---|---|
| **From** | the HROT-integration session, 2026-11-27 |
| **For** | `unreal-ig` |
| **Follows** | [NOTE_UiIntegration_To_UnrealIg_Osm_Bridges.md](NOTE_UiIntegration_To_UnrealIg_Osm_Bridges.md) (*"prepare, do not invent"*) and terrain's [NOTE_Terrain_To_UnrealIg_Osm_Bridges.md](NOTE_Terrain_To_UnrealIg_Osm_Bridges.md) |
| **Built by** | terrain, CE-5057 (done `2026-11-27`): [DESIGN_Terrain_OSM_Bridges.md](../../DESIGN_Terrain_OSM_Bridges.md) §8; merged into `unreal-ig` at this note's commit |

## 1. What is there now

With your regenerated net (`39c265b6d`), HROT's importer (revision 5) makes every OSM road bridge a real deck. Measured on Stechovice by terrain:
9 decks, including **the Vltava south crossing (edge `26476508`, 124 m, 0–5.7 m above the ground)** and **the north one ("most Vltavanů", edge
`4412124`, 300 m, up to 15.8 m)**, plus the footbridges and small street bridges. Rail bridges are excluded (the user's decision).
`stechovice-battle`'s 8 bridge rows pass on these decks: HROT's tanks cross at deck height.

⚠ The imported terrain folder (`assets/terrain/Stechovice/`) is generated, not in git. HROT re-imports it at its next load, because the importer
revision moved to 5 (the stale-import fix, CE-11061). So load Stechovice once in HROT, then run your terrain export.

## 2. Where they are and their shape

In `assets/terrain/Stechovice/Stechovice.world.geojson`, as Polygon features (the same frame as every other feature in that file):

| property | value |
|---|---|
| `kind` | `slab` (the deck) or `ramp` (only where a deck end stands more than 0.3 m above the bank) |
| `zRef` | `ground` — ⭐ each corner's third coordinate is its height **above the ground under that corner**, not absolute: add the terrain height there (the same rule as the laid bridge's `Deck` on DPS) |
| `structure` | `osm-bridge/<edge id>`: one bridge = all parts with the same structure |
| `label` | `osm-bridge/<edge id>/<part index>` (the parts in order along the bridge; neighbouring slabs share an edge) |
| geometry | a closed ring of 4 corners `[x, y, above]` |

The road on the bridge follows the deck, so HROT's road net (`Stechovice.net.xml`) already has the bridged lanes at deck height.

## 3. Asked

- ⭐ Make your terrain export carry the `slab` / `ramp` features with `structure` `osm-bridge/*` into the level as bridge decks, the way you carry walls
  and buildings. ⛔ Not drawn from OSM: the heights are HROT's.
- Say which commit of yours draws them, and send a screenshot of each Vltava crossing in chat (none in git).

## 4. Not part of this

`stechovice-battle`'s own **authored** bridge (the scenario's kind-10 terrain feature at (762, 1590)) is a scenario feature, not part of the terrain.
It is your open ask for a `TerrainFeature` topic on DPS ([NOTE_UnrealIg_To_UiIntegration_Scenario_Terrain_Features.md](NOTE_UnrealIg_To_UiIntegration_Scenario_Terrain_Features.md)),
which is still to be answered.
