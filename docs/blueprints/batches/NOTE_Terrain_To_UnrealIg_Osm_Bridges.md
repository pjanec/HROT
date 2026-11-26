<!--STATUS
state: LIVE — a NOTE (protocol §5): what terrain needs from unreal-ig for CE-11063, and what terrain will hand back · updated: 2026-11-26 · current-answer: the whole file
related-designs:
  - ../../DESIGN_Terrain_OSM_Bridges.md — the design (terrain): an OSM bridge edge becomes a deck from bank to bank; the net's bridged lanes on the deck
-->
# NOTE — `terrain` → `unreal-ig`: CE-11063 (OSM bridges) — what we need you to commit, and what you will get

⭐ Design: [DESIGN_Terrain_OSM_Bridges.md](../../DESIGN_Terrain_OSM_Bridges.md). Terrain builds it on a SYNTHETIC net now (user: *"use synthetic small net for development"*); Stechovice follows when your net lands.

## 1. ⭐ What we need you to do and commit

| # | ask | how to check it before you commit |
|---|---|---|
| ① | **Regenerate Stechovice's input-set road net** with the current OSM stage (`NetconvertRoadBuilder` — it passes `--osm.extra-attributes bridge,tunnel,layer`, `NetconvertRoadBuilder.cs:41-43`) | `grep -c 'key="bridge"' assets/terrain-input/Stechovice/<the roads .net.xml>` > 0, and the **Vltava crossings** carry it: the south bridge ≈ terrain-local (1380, 180) and the north ≈ (875, 4080) — name their edge ids in your commit message |
| ② | **Commit the regenerated input set** (the net file AND its `manifest.json` — the file's sha256 in `files` changes) on `unreal-ig`, and say the commit here or in a NOTE to terrain | `python3`/`jq`: the manifest's sha for the net equals `sha256sum` of the file; nothing else in the set changes (heights, buildings, vegetation untouched) |
| ③ | ⛔ **Do not re-import expecting bridges yet** — the importer's BRIDGES stage is terrain's (importer **revision 5**). Once both are merged, every machine re-imports Stechovice ONCE on its next load (the manifest sha and the revision both changed — CE-5056 / CE-11061: into the machine root) | — |
| ④ | ⛔ **Do not derive bridges yourselves from the net's tags** for drawing | one source: the terrain (§2) — the user: *"without terrain saying the exact height it makes little sense"* |

## 2. What terrain hands back (the contract Unreal can prepare for now)

| where | what |
|---|---|
| `Stechovice.world.geojson` | per OSM bridge: `slab` features (and a `ramp` at an end only where the deck stands above the bank), `"zRef": "ground"`, ⭐ `"structure": "osm-bridge/<first edge id>"`, labels `osm-bridge/<id>/<i>`. Each corner's third coordinate is its height ABOVE THE GROUND under it ⇒ **absolute z = ground(x, y) + that value** — the deck is STRAIGHT between the two bank heights (not 1.5 m above the river). The same parts and structure rule as an authored bridge (`DESIGN_Terrain_Authoring.md` §8), so one drawing path serves both |
| the terrain's net (`Stechovice.net.xml`, `roads.json`) | the bridged edges' lane points at the DECK's height. ⚠ Today the importer drapes them onto the river bed (G-25, forced re-elevation) — fixed by the same change, so SUMO's cars and your road mesh cross on the deck |
| HROT itself | units, vehicles, sight and fire use the deck like any deck (walkables of a structure — CE-4694 / BL-12); a bridge can collapse like an authored one |

## 3. Order

`unreal-ig` commits the net (①②) ‖ terrain builds and gates the stage on the synthetic net (A-1…A-4) → both merge → Stechovice re-imports once → terrain checks A-5 (the Vltava decks, `stechovice-battle`'s bridgehead rows) and tells you the commit → Unreal draws from §2.
