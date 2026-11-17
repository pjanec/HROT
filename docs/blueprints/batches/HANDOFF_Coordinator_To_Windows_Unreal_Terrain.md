<!--STATUS
state: LIVE — dispatched, not started — ⭐ UNATTENDED (an EXPERIMENT on the user's Windows machine)
updated: 2026-11-17
current-answer: the whole file
related-designs:
  - ../../designs/IG/DRAFT_Unreal_Image_Generator.md — ⭐ THE draft: the direction (option B), the one rule (what must be identical), the terrain package as the contract, U-5 / U-9 / U-10
  - ../../DESIGN_Terrain_Import_UTM.md — the terrain package: the UTM frame, the tiled heights, the importer that writes it
  - ../../reference/NED_DDS_Protocol.md — the wire (not needed for this experiment; the next one)
-->

# HANDOFF — coordinator → the WINDOWS session: OUR TERRAIN IN UNREAL 5.8 THROUGH LANDSCAPE COMBINATOR — measure it (CE-4651)

**For:** a session on the user's Windows machine with **Unreal Engine 5.8** and the HROT repo. **From:** coordinator. **Dispatched at the commit
that adds this file**; ⛔ scope FROZEN there (R-106: a step whose premise fails is reported, the rest go on).

🔒 User, `2026-11-17`: *"how to import terrains is the right question, likely the one to be solved first as everything depends on that. I need to use the
nice unreal renderer"* · *"maybe we can author terrain in unreal is a way compatible with our terrain?"* · *"start with reading their documentation"* →
the docs say **Landscape Combinator** reads a local GeoTIFF heightmap and GDAL vector files (GeoJSON) and builds buildings and roads from splines
(the draft's U-9 table) · *"i will run a session on a windows computer with most recent unreal 5.8 and HROT coordinator. pls write handoff for that session"*.

## ⭐⭐⭐ UNATTENDED — and what this session is
An EXPERIMENT that answers questions with MEASUREMENTS. Never ask, never wait: decide, measure, write down. A step that fails is a FINDING (say exactly
why), never a reason to stop the rest.
- **Branch:** create **`unreal-ig`** from `origin/coordinator`; commit and push there only. Commit the exporter script and the docs; ⛔ never the Unreal
  project, its assets, the plugin's sources or any screenshot (CLAUDE.md "no screenshots in git") — keep the Unreal project OUTSIDE the repo
  (e.g. `D:\Work\HrotUnreal`), and put stills in the chat / the status file as paths on the machine.
- **Ids:** the tracker row is `CE-4651`; no new tracker or ruling ids — record decisions as `UL-1, UL-2, …` inside the draft.
- Status after every step in **`docs/blueprints/batches/STATUS_Windows_Unreal_Terrain.md`** (what was done, the measured numbers, the paths of the stills).

## 0. Start
`git fetch origin coordinator`; branch `unreal-ig`; read the draft END TO END, then `DESIGN_Terrain_Import_UTM.md` §2 / §7 (the frame and the tiled
heights). Note the machine: Unreal version, Visual Studio version, Python version.

## 1. Setup — record each version and every surprise
| # | step |
|---|---|
| 1a | a new **C++** Unreal 5.8 project outside the repo (Landscape Combinator is a code plugin — it compiles) |
| 1b | **Landscape Combinator from GitHub** (free for personal use — this is an evaluation; ⚠ commercial use is its paid Fab listing): https://github.com/LandscapeCombinator/LandscapeCombinator — install per its docs (https://landscapecombinator.github.io/LandscapeCombinator/installation.html); enable Epic's PCG and Water plugins too |
| 1c | **Python with GDAL** for the exporter (OSGeo4W, or conda `gdal` + `pyproj`) — or plain `numpy` + `pyproj` + `rasterio` if GDAL is awkward; say which |

## 2. The exporter — `scripts/terrain-to-unreal.py` (commit it)
Reads ONE terrain package (`assets/terrain/<name>/terrain.json` + its height grid + its `*.world.geojson` + `roads.json`) and writes, into an output folder:

| output | content | the frame |
|---|---|---|
| `<name>.height.tif` | a GeoTIFF of the heights (float32 or 16-bit + scale — measure which Landscape Combinator wants), the cell size of the source | ⭐ a terrain WITH a UTM frame (`city-battle`: zone 32 N, `e0` / `n0`): write **EPSG:326zz** with E = x + e0, N = y + n0 — exact. A terrain with only a lat/lon origin (`basic-desert`): write a transverse Mercator centred on the origin (`+proj=tmerc +lat_0 +lon_0 +k=1`) — and measure whether Landscape Combinator accepts a custom CRS or needs "Skip Coordinate Conversion" |
| `<name>.vectors.geojson` | buildings as polygons with `height` and `building_levels` (= height ÷ 3, rounded), walls as lines with `material`, surfaces as polygons with `class` (open / forest / water), lone trees as points, the road centre lines from `roads.json` | the same CRS as the GeoTIFF |

## 3. The imports — record every setting used
| # | step | measure |
|---|---|---|
| 3a | **`basic-desert`** (61 × 61 at 10 m: a track, rock outcrops, a compound wall, a 10 m ridge, a 3 m wadi) — the landscape from the GeoTIFF | ⭐ **height agreement**: sample ≥ 50 points (the ridge crest, the wadi floor, flat ground, the outcrops' feet) — HROT's height (`HeightAt` — the editor's HTTP debug API or the grid file itself) vs a line trace onto the Unreal landscape — the max and mean difference; the landscape's size, its component layout, how Landscape Combinator padded or resampled |
| 3b | `basic-desert`'s buildings (the rock outcrops are `building` features with a height) and the wall | footprint and height match (a few by hand); ⭐ does our `height` attribute reach its `height` tag from a GeoJSON file? |
| 3c | **`city-battle`** (472 buildings, 194 walls, 146 surfaces, a UTM frame) — landscape + buildings + road splines | the same checks; import time; frame time in the editor viewport; how the buildings look out of the box |
| 3d | ⭐ if the machine has a LARGE imported terrain (the user's Stechovice, from the OSM wizard — the tiled 1 m store): export and import it | the tiled heights → one GeoTIFF or the `_x0_y0` folder convention; World Partition; import time and memory |
| 3e | **dressing, first look** (optional, time-boxed ~1 h): an auto-material on the landscape (slope / height), Epic's PCG scattering trees in the `forest` polygons and grass on `open` | stills next to HROT's 3-D map of the same terrain (paths in the status) |

## 4. Write it down — the deliverable
1. **`DRAFT_Unreal_Image_Generator.md`**: a new section **"Measured on Windows `2026-11-xx`"** — every number above, every setting, what worked, what did
   not, the UL-n decisions; update U-5 (the export: sizes, padding, the frame), U-9 (✅ / ⛔ verified in Unreal), and add new open questions found.
2. The exporter committed with a header saying how to run it.
3. The status file: the machine, the versions, the stills' paths, the time each step took.
4. Push `unreal-ig`; the coordinator merges it.

⛔ Not in this experiment: the DDS client, entities, gizmos, the FPS input — the terrain comes first (user).
