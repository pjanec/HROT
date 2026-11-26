# Handoff: unreal-ig (integration) → terrain — an OSM bridge becomes a terrain bridge (the Vltava has none in Stechovice)

| | |
|---|---|
| **From** | the HROT-integration session (`unreal-ig`), 2026-11-26 |
| **For** | the `terrain` lane |
| **Dispatched at** | `unreal-ig` (the commit adding this file) — fetch `origin/unreal-ig` |
| **Id** | **CE-11063** (the `unreal-ig` block; the work is yours) |
| **User, verbatim** | *"unreal is not showing any bridges where they should be in stechovice terrain across vltava river - the roads are leading to the river bank from both sides but no bridge - maybe the OSM import did not mark the stuff as bridge?"* · *"let's start with terrain first, unreal can maybe just prepare for drawing bridges but without terrain saying the exact height it makes little sense"* |

## 1. Measured (HROT `unreal-ig`, `assets/terrain/Stechovice` imported here)

| stage | what happens to an OSM `bridge=yes` road | |
|---|---|---|
| OSM stage | ✅ keeps `bridge` / `tunnel` / `layer` as edge params (`NetconvertRoadBuilder.cs:41-43`, CE-4403, 2026-11-14) | ⚠ but Stechovice's committed `roads.net.xml` (2026-11-22) has **0** bridge tags — generated without that option; `unreal-ig` regenerates it (§4) |
| importer | uses the tag ONLY to skip the road corridor under it (`Corridors.cs:35`, V-15 / G-22) | ⛔ **never makes a bridge** — the terrain format has one (`TerrainFeature.Bridge`, `FeatureKinds.cs:52, :235`) but nothing turns an OSM bridge into it; `DESIGN_Terrain_From_OSM.md:169` only says *"a bridge stays SUMO-elevated with the valley under it untouched"* |
| imported terrain | 9 585 buildings, 91 surfaces, 16 fences, 7 walls, 3 trees — **0 bridges** | the vehicles' road graph has the river crossing; the navmesh, the map, the units and Unreal have no deck |

## 2. Asked

1. **The importer turns each bridged road edge (OSM `bridge` tag, or a `layer` > 0 over lower ground — `Corridors.OffGround`) into a terrain bridge**: a deck from its first to its last bank point, as wide as the road (its lanes' widths), with the SAME deck geometry as an authored and a laid bridge (`DeckGeometry` — one rule). Consecutive bridged edges of one OSM bridge are ONE deck (or chained decks that meet), not a staircase.
2. ⚠ **The deck height over a valley:** today's `Bridge` puts the slab `BridgeDeckHeight` (1.5 m) above the ground UNDER it — right for an authored footbridge over a stream, wrong over the Vltava (a road bridge runs from bank to bank, metres above the water). Lean: the deck follows the road's own height at both ends (the banks — or the net's elevated z, which netconvert gives a bridged edge) and is straight between; the ramps stay where the bank meets the road. Yours to decide; say which and why in the design.
3. Units, vehicles, sight and fire use it like any deck (the decks already do — CE-4692); the road graph's crossing and the deck agree.

## 3. Acceptance

- Re-import Stechovice with the regenerated net (§4): the Vltava road crossings exist as bridges in `Stechovice.world.geojson`, a vehicle drives across one (a scenario row), and the navmesh connects the banks there.
- A rail on a synthetic net: a bridged edge over a dip ⇒ one deck at the banks' height; an unbridged edge ⇒ none.
- The design: `DESIGN_Terrain_From_OSM.md` (F-2 / the roads row) and `DESIGN_Terrain_Import_UTM.md` updated — the OSM bridge rule, cross-linked to `designs/engineering/DESIGN_Bridge_Laying.md` (the shared deck).

## 4. In parallel (not yours)

`unreal-ig` regenerates Stechovice's input-set road net with the current OSM stage (bridge tags kept) and re-commits the input set — they hold the OSM download.
Until then, test on a synthetic net. Unreal draws the bridges only from what the terrain then says (their export reads HROT's terrain).
