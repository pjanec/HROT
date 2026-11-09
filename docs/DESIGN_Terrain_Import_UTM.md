<!--STATUS
state: LIVE
build-state: PRELIMINARY — an idea with open questions (Q-U1…Q-U12); NOT ready to build. Answers expected from another session (the source data owner)
updated: 2026-11-09 (first version — coordinator)
current-answer: §0 why · §1 measured · §2 the idea (UML) · §3 the stages · §4 OPEN QUESTIONS · §5 ANSWERS (source-data session)
stale-below: nothing
known-rot: none
known-conflict: none
related-designs:
  - DESIGN_Geo_Origin.md — OWNS the simulation's origin and IGeographicTransform (R-229); this adds a second, UTM implementation
  - DESIGN_Terrain_Zones_And_Assets.md — OWNS what a terrain is (the content pack, TerrainCatalog) and zones as windows onto it (R-146)
  - DESIGN_Artifact_Staging.md — OWNS getting terrain files from the NAS to every node (a 100–200 MB terrain rides it)
  - blueprints/Architect_Question_93_Sumo_Traffic_Node.md — E1 (R-4025): the existing UTM ↔ WGS84 maths (Hrot.Traffic/Utm.cs) this reuses
  - DESIGN_Map_3D_Mode.md — the 3-D terrain layer and BeyondGround (the procedural outer band) a heightmap would replace inside its bounds
  - DESIGN_Content_Layout.md — where a terrain folder lives (assets/terrain/<name>/)
-->

# DESIGN (preliminary) — importing UTM terrains: a UTM frame, a heightmap, many buildings

## 0. Why

🔒 User, `2026-11-09`: *"HROT is now using tangent plane flat coordinates; what would it take to start using UTM grid map projection instead? still
with local small coordinates around zero, just placed somewhere to the UTM grid. Why: i need to import UTM terrains into HROT. Around 6x6km of
heightmap data and lots of buildings. What that would mean to how we store data? there would need to be some quite big optimizations i guess."*

⚠ R-250 / R-252: HROT's own terrain, 3-D view and SimHost are a development stand-in; production uses a mature 3-D engine behind interfaces. The
import feeds the SAME interfaces (heights, surfaces, per-purpose traces) and adds no shortcut the final engine would not have.

## 1. Measured *(`2026-11-09`, grep + reads; the graph index was used for the transform's users)*

| fact | where |
|---|---|
| the simulation is local metres at ONE origin; geodetic ↔ local goes through ONE seam `IGeographicTransform` (`SetOrigin`, `ToCartesian`, `ToGeodetic`, `Origin`) — 53 files use it through the interface | `FDP/Toolkits/Fdp.Toolkits/Geographic/IGeographicTransform.cs` · R-229 |
| its ONE implementation is the tangent plane (ECEF → ENU at the origin) | `WGS84Transform.cs` |
| the origin comes from the terrain (`terrain.json` `origin {lat, lon, alt}`) and is carried by a recording | `TerrainDefinition` · `RecordingMetadata.GeoOriginRecord` |
| UTM ↔ WGS84 maths EXISTS (the SUMO net placement) | `Hrot/Engine/Hrot.Traffic/Utm.cs` (`ToGeodetic`, `FromGeodetic`), `SumoNetPlacement.cs:55-66` |
| ⛔ **no heightmap**: the ground is walkable triangle meshes; outside the terrain a procedural hilly band (`BeyondGround`) | `TerrainWorld.cs`, `BeyondGround.cs` |
| buildings are GeoJSON footprints extruded to a height (`*.world.geojson`; the largest, demo-city, 84 KB) + optional `houses.json` | `assets/terrain/*` |
| ⛔ **fixed grids sized for ~1.5 km**: the collider hash 300 × 300 × 5 m from −750 m; the perception grid 200 × 200 × 5 m | `SpatialHashConstants.cs`, `PerceptionConstants.cs:81-84` |
| the navmesh is TILED (24 m tiles) but BAKED AT LOAD; a cold bake already ejects SimHost | `RecastNavmeshBaker.cs:110-113` · CE-4033 |
| terrains reach every node through the NAS staging copy | `DESIGN_Artifact_Staging.md` |

## 2. The idea

```mermaid
classDiagram
    direction LR
    class IGeographicTransform { <<existing seam>> SetOrigin · ToCartesian · ToGeodetic · Origin }
    class WGS84Transform { <<existing>> tangent plane (ENU) }
    class UtmTransform { <<NEW>> local = (E − E0, N − N0, h) · grid convergence for headings }
    class Utm { <<existing, moved down to the toolkit>> ToGeodetic · FromGeodetic }
    class TerrainDefinition { <<existing>> origin (lat, lon) or utm (zone, hemisphere, e0, n0) NEW }
    class TerrainHeightfield { <<NEW>> tiled binary heights · HeightAt(x, y) · tiles on demand }
    class BuildingSet { <<NEW cache>> footprints + heights, binary, beside the source }
    class TerrainImporter { <<NEW tool>> source → terrain folder: heights, buildings, baked navmesh }
    IGeographicTransform <|.. WGS84Transform
    IGeographicTransform <|.. UtmTransform
    UtmTransform --> Utm
    TerrainDefinition --> UtmTransform : utm frame
    TerrainDefinition --> TerrainHeightfield
    TerrainDefinition --> BuildingSet
    TerrainImporter --> TerrainHeightfield : writes
    TerrainImporter --> BuildingSet : writes
```
*What it shows: the frame is ONE new implementation of the existing seam — nothing that uses positions changes; the size problem is in the TERRAIN
data (a heightfield and a building set that do not exist today) and in the grids sized as constants.*

| part | the idea |
|---|---|
| frame | a terrain declares `utm {zone, hemisphere, e0, n0}`; local x = E − E0, y = N − N0 (grid north), z = height; existing terrains keep the tangent plane |
| heights | a tiled binary heightfield (e.g. 256 × 256 posts per tile, 16-bit heights + an offset/scale), loaded where heights are asked (Muscle, map hosts — ⛔ never the Brain, R-2037); the 3-D view renders it with LOD; the 2-D map gets a hillshade image |
| buildings | imported once into a binary set (footprint, base, top, material, floors); rendered batched; JSON stays the source |
| grids | the collider hash and the perception grid sized from the terrain's bounds, not constants |
| navmesh | baked OFFLINE by the importer, stored with the terrain, loaded as a file (also closes CE-4033) |
| sizes (estimate) | 6 × 6 km at 1 m posts = 36 M samples ≈ 72 MB (16-bit); at 2 m ≈ 18 MB; buildings unknown (Q-U3); the navmesh ≈ 62 500 tiles per layer — staged once per terrain version |

## 3. The stages (sketch)

1. `UtmTransform` + the terrain's frame declaration (small, independent)
2. the grids sized from the terrain's bounds (needed for anything over ~1.5 km)
3. the heightfield: format, height queries, 3-D rendering with LOD, the 2-D hillshade
4. the building import + binary cache + batched rendering
5. the offline navmesh bake stored with the asset
6. the importer tool (source → terrain folder) and the staging of a large terrain

## 4. OPEN QUESTIONS — for the session that knows the source data

| # | question | why it matters | lean (if any) |
|---|---|---|---|
| **Q-U1** | the HEIGHTMAP's format (GeoTIFF, ASCII grid, raw + world file …), post spacing (m), vertical datum, units | the importer's reader; memory and file size (1 m vs 2 m is 4×) | GeoTIFF; keep the source spacing |
| **Q-U2** | the UTM zone(s) and hemisphere; does the 6 × 6 km area cross a zone edge? | one zone per terrain is the simple case | one zone, chosen from the area's centre |
| **Q-U3** | the BUILDINGS: footprints with heights (shapefile / GeoJSON / CityGML LoD1) or full 3-D models (OBJ / FBX / CityGML LoD2+)? roughly how many? roof shapes, floors, interiors? | footprints extrude like today; real models need a model pipeline, LOD and instancing | footprints + heights first; models later |
| **Q-U4** | other layers: roads, vegetation / forests, water, land-use / surface type, bridges, walls | each is a separate import (surfaces feed movement and perception) | roads + surface type first |
| **Q-U5** | is the data licensed / allowed in the repo, or NAS-only? | a 100–200 MB terrain should not go into git (R-4135 allows only a small preview) | NAS / content root only, never git |
| **Q-U6** | which hosts must show it: 2-D map only, 3-D view, Stride? | decides what must render it | 2-D + the editor's 3-D first |
| **Q-U7** | do units need to path everywhere in the 6 × 6 km (vehicles AND foot), or in parts? | the navmesh size and bake time | everywhere, baked offline |
| **Q-U8** | building interiors / enterable buildings (doors, rooms) in the imported set? | today's CQB features need authored interiors | not imported; authored where needed |
| **Q-U9** | how often does the source data change — a one-off import or a repeated pipeline? | a tool vs a one-time conversion | a repeatable tool |
| **Q-U10** | does the output also go to an external engine / DIS / HLA in UTM or in lat/lon? | where grid convergence must be applied | lat/lon at the gateway, as today |
| **Q-U11** | the expected unit count and view distances on this terrain | perception and collider grid cell sizes over 6 km | — |
| **Q-U12** | is the beyond-terrain hilly band still wanted around an imported terrain, or does the import bring its own surroundings? | `BeyondGround` vs real data at the edges | real data inside; the band only outside |

---
_coordinator lane, 2026-11-09_

## 5. ANSWERS — from the source-data session *(`2026-11-09`; "verified" = read in the source data or the source engine's code, "lean" = a proposal, not a decision)*

🔒 User, `2026-11-09`: *"I would like to import all 6x6 km and convert the buildings (enterable of course)."* — so the scope is the whole source
data area and Q-U8 is decided: the buildings are imported as ENTERABLE.

🔒 User, `2026-11-09`: the converted terrain NEVER enters this git repository; it stays on a local drive (later a company content repository).
This document stays generic: it describes the source only by its formats and contents, not by product or project names.

### 5.1 The source terrain

| fact | |
|---|---|
| the source is another engine's terrain database; its internals do not matter here. What the importer reads is an **IMPORT INPUT SET** prepared from it OUTSIDE this repo (on a local drive) in plain formats: heights, building footprints with door / window points, tree positions, a road network | verified |
| ✅ the data area is **6 × 6 km: UTM 32N E 364 000–370 000, N 6 041 000–6 047 000** | verified |

### 5.2 Answers

| # | answer | status |
|---|---|---|
| **Q-U1** | **Format:** a float32 raster with an ESRI BIL header (`.hdr`) and a JSON with origin / extents / size, in local metres (Q-U2). Source spacing ≈ **0.5 m**. **Units:** metres. **Vertical datum:** none to convert — heights are taken as-is (≈ 480–770 m in this area); the import copies them unchanged. | verified (format, spacing, units); datum undefined by design |
| | → the importer resamples to **1 m** (36 M posts, 72 MB as 16-bit). 0.5 m is 4× the size for no simulation gain; 2 m is coarse next to building walls | lean |
| **Q-U2** | **UTM zone 32, northern hemisphere** (WGS 84 / UTM 32N, EPSG 32632). The area is ≈ 54.50–54.55°N, 6.90–6.99°E: inside zone 32, ≈ 58 km from its western edge — **no zone crossing**. Grid convergence here ≈ **−1.64° … −1.71°**, i.e. a tangent-plane frame would be rotated ≈ 1.7° against the UTM grid (≈ 30 m at 1 km). | verified |
| | ⭐ **`utm {zone: 32, hemisphere: N, e0: 372000, n0: 6043000}`** — the local origin the other engine uses for this terrain, so HROT local positions are numerically identical to the other engine's (both run together on this terrain). The data area is then x −8000…−2000, y −2000…4000 (off-centre — the grids need bounds-based sizing, stage 2, anyway) | lean |
| **Q-U3** | **Footprints + door/window points; no interiors.** Per building: footprint polygon (local metres; median 5 vertices, max 47), DOOR and WINDOW points (position, altitude, heading, storey index). In the 6 × 6 km: **1 988 buildings, 5 091 doors, 4 600 windows**. Building heights come separately (to be added to the input set). No interior geometry exists in the source | verified |
| **Q-U4** | **Roads:** a SUMO net covering a larger area (UTM 32 WGS84) → crop it to the box (`netconvert --keep-edges.in-boundary`) and reference it from `terrain.json` `sumoNet` with **anchor (139 761.25, 162 886.77)**, so net − anchor == HROT local; `scripts/terrain-from-sumo.py` already has this shape. **Vegetation:** individual tree positions; no forest polygons → `forest` surfaces are DERIVED. **Water:** none in this area. **Walls / fences:** placed objects → `wall` LineStrings. **Bridges / unique structures:** few → author as `slab` / `ramp`. **Also present:** tunnels, power lines, street lamps (shapefiles, UTM 32N). **Land use:** no vector land use | verified (what exists); the derivations are leans |
| **Q-U5** | **✅ decided: never in this git repository.** The converted terrain stays on a local drive, later in a company content repository; only the importer TOOL lives here | decided |
| **Q-U6** | HROT runs **alongside another engine that renders the full visual scene of the same terrain**; HROT's own hosts need an engineering view only: the 2-D map (+ hillshade, footprints) and the editor's 3-D view for checking; Stride not needed for this terrain | lean |
| **Q-U7** | the whole 6 × 6 km is imported; where units must path is not stated — lean as §4 (everywhere, offline bake); the infantry navmesh inside 1 988 interiors is the expensive part | — (the user) |
| **Q-U8** | **✅ decided: enterable.** The importer GENERATES each house as a `*.building.json` template: footprint → exterior walls, storeys = round(model height / 3 m), a floor slab per storey, DOOR openings at the source door points (position + heading → wall + offset; storey index → level), WINDOW openings at the source window points (sparse: 4 600 for 1 988 houses) plus generated ones along each facade per storey, a flat roof (sloped roofs approximated). Open: the INSIDE — open-plan storeys vs procedurally partitioned rooms + stairs (a room/stair generator is new work). Scale: 1 988 enterable houses vs 164 in demo-city — door entities and the interior navmesh grow with it | decided (enterable); interior layout = the user |
| **Q-U9** | a **repeatable tool** reading the input set; the building generator will be tuned iteratively | lean |
| **Q-U10** | the other engine converts geodetic ↔ local with standard WGS84 UTM maths (zone forced to the terrain's), the same as `Utm.cs` → lat/lon at a gateway agrees to sub-mm. With e0/n0 from Q-U2 the two engines' local frames are IDENTICAL, so convergence never appears between them; it matters only where a heading is exchanged relative to TRUE north (DIS/HLA, lat/lon gateways) — apply the −1.6…−1.7° there | verified / lean |
| **Q-U11** | not known here | — (the user) |
| **Q-U12** | outside the 6 × 6 km the source has no real data → real data inside, `BeyondGround` outside, as leaned; the heights at the edge should blend into the band | lean confirmed |

### 5.3 Correction to §1

- ⚠ §1 "⛔ no heightmap" is out of date: `FDP/Toolkits/Fdp.Toolkits/Terrain/TerrainHeightGrid.cs` (CE-1034, `DESIGN_Terrain_Height.md`) already loads ONE ESRI ASCII `.asc` grid per terrain (`basic-desert`, `highway-rest` use it). What is missing is a TILED / large heightfield — 6 × 6 km at 1 m is 36 M floats (144 MB) in one array, plus an equally large render mesh.

### 5.4 Still open — for the user

1. the INSIDE of the generated houses: open-plan storeys, or a procedural room + stair layout (Q-U8)
2. expected unit counts / view distances (Q-U11) and where units must path (Q-U7)

---
_source-data session, 2026-11-09_
