<!--STATUS
state: LIVE
build-state: DESIGN — the source-data answers are in (§5); TWO questions left for the user (§5.4: the houses' insides; unit counts and where units path); §2 and §3 revised to the answers
updated: 2026-11-09 (§5 answers from the source-data session; §1 corrected — a height grid EXISTS; §2 / §3 revised — coordinator) · 2026-11-09 (first version — coordinator)
current-answer: ⭐ §5 ANSWERS (source-data session) · §5.4 the two questions left · §2 the idea (UML, revised) · §3 the stages (revised) · §1 measured · §4 the questions as asked
stale-below: nothing
known-rot: §1's first-version row "⛔ no heightmap" was WRONG (corrected by §5.3) — a single-grid ESRI ASCII height grid exists (CE-1034)
known-conflict: none
related-designs:
  - DESIGN_Terrain_Height.md — OWNS the terrain's height grid (TerrainHeightGrid, one .asc per terrain, CE-1034) this extends to a tiled store
  - DESIGN_Building_Interiors.md — OWNS the *.building.json template (walls, storeys, doors, windows) the importer GENERATES for each house
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
| ✏ (corrected by §5.3) the ground is ONE height grid per terrain — an ESRI ASCII `.asc` loaded into one `float[]` (`basic-desert`, `highway-rest` use it) — plus walkable meshes; outside the terrain a procedural hilly band (`BeyondGround`). ⛔ What is missing is a TILED / large grid: 6 × 6 km at 1 m is 36 M floats (144 MB) in one array, and an equally large render mesh | `TerrainHeightGrid.cs:14-31, :187` · `TerrainWorldParser.cs:81` · `DESIGN_Terrain_Height.md` (CE-1034) · `BeyondGround.cs` |
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
    class TerrainHeightGrid { <<existing, extended>> HeightAt(x, y) · today one .asc array · NEW a tiled binary store, tiles on demand }
    class BuildingTemplate { <<existing format>> the building.json template · NEW one GENERATED per house, enterable }
    class TerrainImporter { <<NEW tool, repeatable>> exported heights · building DB + model boxes · SUMO crop · walls · navmesh }
    IGeographicTransform <|.. WGS84Transform
    IGeographicTransform <|.. UtmTransform
    UtmTransform --> Utm
    TerrainDefinition --> UtmTransform : utm frame
    TerrainDefinition --> TerrainHeightGrid
    TerrainDefinition --> BuildingTemplate
    TerrainImporter --> TerrainHeightGrid : writes tiles
    TerrainImporter --> BuildingTemplate : generates
```
*What it shows: the frame is ONE new implementation of the existing seam — nothing that uses positions changes; the height grid and the building
template EXIST and are extended (tiled storage, generated templates); the grids sized as constants are the rest of the size problem.*

| part | the idea |
|---|---|
| frame | a terrain declares `utm {zone, hemisphere, e0, n0}`; local x = E − E0, y = N − N0 (grid north), z = height; existing terrains keep the tangent plane. ⭐ For this terrain **e0/n0 = the source engine's own origin (32N, 372 000 / 6 043 000)** so both engines' local positions are numerically identical (§5 Q-U2); convergence (−1.6…−1.7°) only at a true-north gateway |
| heights | the EXISTING `TerrainHeightGrid` gains a tiled binary store (e.g. 256 × 256 posts per tile, 16-bit + offset/scale), resampled from the input's float32 height raster at **1 m** (72 MB); loaded where heights are asked (Muscle, map hosts — ⛔ never the Brain, R-2037); the 3-D view renders it with LOD; the 2-D map gets a hillshade |
| buildings | ✅ **ENTERABLE** (the user): the importer GENERATES a `*.building.json` per house (1 988): footprint → walls, storeys = round(model height / 3 m), a slab per storey, doors and windows at the source points (+ generated windows per facade), a flat roof; the inside per §5.4 ① |
| grids | the collider hash and the perception grid sized from the terrain's bounds, not constants |
| navmesh | baked OFFLINE by the importer, stored with the terrain, loaded as a file (also closes CE-4033) |
| sizes (estimate) | 6 × 6 km at 1 m posts = 36 M samples ≈ 72 MB (16-bit); at 2 m ≈ 18 MB; buildings unknown (Q-U3); the navmesh ≈ 62 500 tiles per layer — staged once per terrain version |

## 3. The stages (revised to §5)

1. `UtmTransform` + the terrain's frame declaration (e0/n0 = the source engine's origin) — small, independent
2. the collider and perception grids sized from the terrain's bounds (the data area is x −8000…−2000, y −2000…4000)
3. the tiled `TerrainHeightGrid` store + the importer's height path (input height raster → 1 m tiles), 3-D LOD, the 2-D hillshade
4. the building GENERATOR (1 988 enterable `*.building.json`, doors / windows from the source points), batched rendering
5. the other layers: the SUMO net cropped to the box with its anchor; walls from model instances; forest surfaces derived from tree positions
6. the offline navmesh bake incl. the interiors, stored with the terrain
7. the importer as a repeatable tool here; ⛔ the converted terrain never in git (§5 Q-U5)

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

| # | ⭐ coordinator's lean `2026-11-09` | rejected — one line |
|---|---|---|
| 1 | **open-plan storeys + ONE stair per house first** (enterable, clearable, cheap to generate and to bake); a procedural room / corridor generator later, as its own stage, if the CQB drills need rooms | rooms first: new generator work before anything is enterable |
| 2 | **paths everywhere outdoors + inside every generated house**, baked offline; unit counts and view distances decide the grid cell sizes in stage 2 — until known, size for the largest shipped scenario (`perf-stress`: 1 000 soldiers) | a partial navmesh: a unit ordered outside it silently cannot move |

---
_source-data session, 2026-11-09_
