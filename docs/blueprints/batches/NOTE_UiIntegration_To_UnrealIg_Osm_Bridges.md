# Note: the missing Vltava bridges are HROT's — terrain makes them; you regenerate the road net and get ready to draw them

| | |
|---|---|
| **From** | the HROT-integration session, 2026-11-26 |
| **For** | `unreal-ig` |
| **User, verbatim** | *"let's start with terrain first, unreal can maybe just prepare for drawing bridges but without terrain saying the exact height it makes little sense"* |
| **Tracked** | CE-11063 — [HANDOFF_UiIntegration_To_Terrain_Osm_Bridges.md](HANDOFF_UiIntegration_To_Terrain_Osm_Bridges.md) |

## Why there is no bridge

Not your side: HROT's imported Stechovice has **0** bridges. Its importer never turns an OSM bridge into the terrain's bridge feature, and the committed input
set's `roads.net.xml` has no bridge tags at all (generated without the OSM stage's bridge option, CE-4403). So HROT's own units cannot cross there either.

## Asked of you

1. ⭐ **Regenerate Stechovice's input-set road net with the current OSM stage** (`hrot-terrain-osm`, which keeps `bridge` / `tunnel` / `layer` as edge params —
   check: `grep -c bridge roads/roads.net.xml` > 0), re-hash the manifest, and re-commit the input set as before (your OSM download is on your machine). Say the commit.
2. **Prepare, do not invent:** once terrain's fix lands, each bridge arrives in the imported terrain as a bridge feature — a DECK and two RAMPS, the same deck
   geometry as the laid bridges you get on DPS as `Deck` (corners with heights). Make your terrain export carry those features into your level (as you do
   walls / buildings). ⛔ Do not draw your own bridges from OSM meanwhile — their height must come from HROT's terrain.
