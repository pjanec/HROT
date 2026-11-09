<!--STATUS
state: LIVE
build-state: DESIGN — the source-data answers are in (§5); Q-U8 decided (R-4186: open floors + a stair ramp); ONE question left (§5.4 ②: unit counts / where units path — the lean stands until answered); §6 re-bases the stages on Terrain World §9
updated: 2026-11-09 (§5 answers from the source-data session; §1 corrected — a height grid EXISTS; §2 / §3 revised — coordinator) · 2026-11-09 (first version — coordinator)
current-answer: ⭐ §6 the prior designs this builds on (sweep 2026-11-09) · ⭐ §5 ANSWERS (source-data session) · §5.4 the two questions left · §2 the idea (UML, revised) · §3 the stages (revised) · §1 measured · §4 the questions as asked
stale-below: nothing
known-rot: the first version missed DESIGN_Terrain_World §9 (sparse terrain), the World Query Seam and the built NavTileCache — §6 corrects §2 / §3; §1's first-version row "⛔ no heightmap" was WRONG (corrected by §5.3) — a single-grid ESRI ASCII height grid exists (CE-1034)
known-conflict: none
related-designs:
  - DESIGN_Terrain_World.md — ⭐ THE owning terrain design (the world file, the ONE TerrainWorld model); ⭐ its §9 SPARSE TERRAIN (S1–S7 approved, R-4051; CE-4011 outer ground, CE-4012 insets, deferred) is the frame this import builds on
  - DESIGN_World_Query_Seam.md — OWNS IWorldQuery (R-253): every height / trace / LOS reader goes through it — the import adds DATA, never a second query path
  - designs/navig-2/Navigation_Design_v2_0.md — OWNS the navmesh bake (§14 P2: tiled, 24 m) and the BUILT NavTileCache (memory + local folder, keyed by each tile's input hash)
  - blueprints/Architect_Question_71_Terrain_Zones_And_The_Asset_Build.md — R7: the per-node tile cache, keyed geographically; zones as fidelity windows
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
| buildings | ✅ **ENTERABLE, OPEN FLOORS** (R-4186): the importer GENERATES a `*.building.json` per house (1 988): footprint → walls; each storey ONE empty floor slab + a stair `ramp` between storeys (the existing `TerrainWalkable` slab / ramp — Building Interiors `:71-97`); the ground floor at the door points' altitude, upper floors at their window points' altitude − a ≈ 0.9 m sill; door and window OPENINGS exactly at the source points (they sit where the visual engine draws the openings, so HROT's openings match); a flat roof; no partitions |
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
| | 🔒 User, `2026-11-09`: *"I do not care about rooms on a floor. Can stay one big empty floor. At most we might need to add staircase ramp so soldier can go up."* → **✅ decided: each storey is ONE empty open floor + a stair `ramp` between storeys; no partitions.** The hard requirement is the OPENINGS: HROT soldiers enter through the same doors and look out of the same windows the visual engine draws, standing on matching floors. The input points sit where the openings are drawn. Doors are complete; the complete window set is prepared in the input set (outside this repo). Storey floors: ground floor = the door points' altitude; upper storeys = their window points' altitude − a standard sill (≈ 0.9 m). Doors in the visual engine are painted (never open) — accepted | decided |
| **Q-U9** | a **repeatable tool** reading the input set; the building generator will be tuned iteratively | lean |
| **Q-U10** | the other engine converts geodetic ↔ local with standard WGS84 UTM maths (zone forced to the terrain's), the same as `Utm.cs` → lat/lon at a gateway agrees to sub-mm. With e0/n0 from Q-U2 the two engines' local frames are IDENTICAL, so convergence never appears between them; it matters only where a heading is exchanged relative to TRUE north (DIS/HLA, lat/lon gateways) — apply the −1.6…−1.7° there | verified / lean |
| **Q-U11** | not known here | — (the user) |
| **Q-U12** | outside the 6 × 6 km the source has no real data → real data inside, `BeyondGround` outside, as leaned; the heights at the edge should blend into the band | lean confirmed |

### 5.3 Correction to §1

- ⚠ §1 "⛔ no heightmap" is out of date: `FDP/Toolkits/Fdp.Toolkits/Terrain/TerrainHeightGrid.cs` (CE-1034, `DESIGN_Terrain_Height.md`) already loads ONE ESRI ASCII `.asc` grid per terrain (`basic-desert`, `highway-rest` use it). What is missing is a TILED / large heightfield — 6 × 6 km at 1 m is 36 M floats (144 MB) in one array, plus an equally large render mesh.

### 5.4 Still open — for the user

1. ~~the INSIDE of the generated houses: open-plan storeys, or a procedural room + stair layout (Q-U8)~~ ✅ decided `2026-11-09`: open floors + a stair ramp (Q-U8)
2. expected unit counts / view distances (Q-U11) and where units must path (Q-U7)

| # | ⭐ coordinator's lean `2026-11-09` | rejected — one line |
|---|---|---|
| 1 | **open-plan storeys + ONE stair per house first** (enterable, clearable, cheap to generate and to bake); a procedural room / corridor generator later, as its own stage, if the CQB drills need rooms | rooms first: new generator work before anything is enterable |
| 2 | **paths everywhere outdoors + inside every generated house**, baked offline; unit counts and view distances decide the grid cell sizes in stage 2 — until known, size for the largest shipped scenario (`perf-stress`: 1 000 soldiers) | a partial navmesh: a unit ordered outside it silently cannot move |


## 6. ⭐ The prior designs this builds on — the corpus sweep *(coordinator, `2026-11-09`; the user: "check the design documents for the related terrains so you do not repeat the error")*

INVENTORY: the `docs/` file names matching terrain · height · building · nav · zone · ground · world · geo · staging · content (44 files), their
STATUS lines read; `grep -l TerrainWorld docs` (40 files); `.dev/_DONE/{navig-2, map-features, stride-1}`; the classes confirmed in code
(`TerrainHeightGrid`, `NavTileCache`, `Utm`). ⚠ `check_index_coverage` is not available through the CLI.

| design | applies — how | ✏ what it changes in §2 / §3 |
|---|---|---|
| [Terrain World](DESIGN_Terrain_World.md) §2 (the world file), ⭐ §9 SPARSE TERRAIN (S1–S7 ✅ R-4051; CE-4011, CE-4012 deferred) | ✅ **directly**: S1 an `outer` ground block (flat / coarse grid / SUMO lattice) · S2 `insets[]`, each a rectangle with its own height grid and navmesh bounds · S3 the ground query picks the inset, else the outer ground, blended at the edge · S4 the mesh and navmesh PER INSET · S5 each inset an implicit fidelity zone · S6 every inset loaded at terrain load (streaming = the zone 2PC, later) · ⭐ S7 float local metres are precise enough to hundreds of km — **no double precision needed** | the 6 × 6 km area is **ONE INSET** (S2); Q-U12's "real data inside, a band outside" is **S1's outer ground**; ⇒ **CE-4011 + CE-4012 become PREREQUISITES of this import** (stage 0), not new inventions. What §9 does NOT cover and this design adds: a TILED height store INSIDE one large inset, the UTM frame, the building generator |
| [Terrain Height](DESIGN_Terrain_Height.md) (CE-1034; §8 the ambient ground outside, A1–A4 ✅ R-4050) | ✅ the ONE `TerrainHeightGrid` (ESRI ASCII, one array) and the outer-ground height fallback (A1) and the straight open-ground legs between areas (A2) | the tiled store EXTENDS this class (as §2 now says); the outer ground's heights use §8 A1 |
| [World Query Seam](DESIGN_World_Query_Seam.md) (WQ-A…WQ-H ✅ R-253; Bepu for objects, a plain grid for heights) | ✅ every height / trace / LOS reader (31 production files) goes through `IWorldQuery` | the import adds DATA behind the seam; ⛔ no reader learns about tiles or UTM |
| [Navigation v2](designs/navig-2/Navigation_Design_v2_0.md) §14 P2 + [AQ71](blueprints/Architect_Question_71_Terrain_Zones_And_The_Asset_Build.md) R7 | ✅ the bake is tiled (24 m) and **`NavTileCache` is BUILT** (`NavTileCache.cs:35`: memory + a local folder, key = layer, tile x/z, the hash of the tile's inputs) | "bake offline" = the importer **pre-fills the tile cache** that ships with the terrain (same keys) — ⛔ not a second navmesh store |
| [Terrain Zones and Assets](DESIGN_Terrain_Zones_And_Assets.md) (zones are windows, R-146; fidelity zones §11, R-4042) | ✅ the terrain is the content pack; an inset is an implicit fidelity zone (§9 S5) | nothing new |
| [Building Interiors](DESIGN_Building_Interiors.md) | ✅ the `*.building.json` template the generator writes | the generator's output must pass the templates' own validation; 1 988 houses × doors is a scale its door / room systems have not seen — measure in stage 4 |
| [Geo Origin](DESIGN_Geo_Origin.md) (R-229) | ✅ the ONE origin and `IGeographicTransform` | `UtmTransform` is its second implementation |
| [Artifact Staging](DESIGN_Artifact_Staging.md) · [Content Layout](DESIGN_Content_Layout.md) | ✅ how a terrain reaches every node; where it lives | the terrain stays OUTSIDE git (Q-U5) — the content root is configured, not the repo copy |
| [AQ93 SUMO](blueprints/Architect_Question_93_Sumo_Traffic_Node.md) E1 (R-4025) | ✅ `Utm.cs` and the net anchor | the SUMO crop + anchor of Q-U4 |
| [Map 3D Mode](DESIGN_Map_3D_Mode.md) (`BeyondGround`, the apron) · [Terrain Combat Tuning](DESIGN_Terrain_Combat_Tuning.md) · [World Survey Sensor](DESIGN_World_Survey_Sensor.md) · [Body Geometry and Ground Contact](DESIGN_Body_Geometry_And_Ground_Contact.md) | ✅ consumers of the world model — unchanged by construction (they read `TerrainWorld` / `IWorldQuery`) | the 3-D view's LOD for a 6 km ground is new work in Map 3D Mode |
| [PLAN 3D World and Realism](PLAN_3D_World_And_Realism.md) | ⛔ its roadmap has no large-terrain phase — this import is a new line in it | add it there when this design is approved |

### 6.1 ✏ The stages, re-based on §9 *(supersedes §3's list)*

0. **CE-4011 + CE-4012** — Terrain World §9 S1 (outer ground) and S2–S4 (insets, per-inset ground and navmesh): the approved, deferred groundwork
1. `UtmTransform` + the terrain's frame (e0/n0 = the source engine's origin)
2. the collider and perception grids sized from the terrain's bounds
3. the TILED store inside one large inset's `TerrainHeightGrid` + the importer's height path (1 m), the 3-D LOD, the 2-D hillshade
4. the building generator (1 988 enterable templates: open floors + one stair ramp, openings at the source points — R-4186), measured against Building Interiors' door systems at that scale; ✅ the complete door / window point set is prepared in the input set (outside this repo)
5. the other layers (the SUMO crop + anchor, walls, derived forest surfaces)
6. the importer pre-fills the `NavTileCache` per inset (interiors included)
7. the importer as a repeatable tool; ⛔ the converted terrain never in git

---
_source-data session, 2026-11-09_
