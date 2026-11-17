<!--STATUS
state: LIVE — running
updated: 2026-11-17
current-answer: the whole file
related-designs:
  - ../../designs/IG/DRAFT_Unreal_Image_Generator.md — the draft this experiment measures for (U-5, U-9); the results land there
  - HANDOFF_Coordinator_To_Windows_Unreal_Terrain.md — the handoff (CE-4651)
-->

# STATUS — our terrain in Unreal 5.8 through Landscape Combinator (CE-4651)

Branch `unreal-ig` from `coordinator@1a975b492`. The Unreal project is OUTSIDE the repo, at `D:\Work\HrotUnreal` (never committed).

## The machine

| | |
|---|---|
| Unreal | 5.8.4 (CL 58902052), installed at `C:\UE_5.8\UE_5.8` |
| Visual Studio | 2026 Community 18.10; MSVC toolchain 14.44.35207; Windows SDK 10.0.26100 |
| Python | 3.13.15 with rasterio 1.5.2 (GDAL 3.12.2), pyproj 3.8.0, numpy 2.5.5 (`pip install --user`; no OSGeo4W / conda) |
| hardware | 16 logical cores, 31 GB RAM, NVIDIA RTX 3070 Laptop + AMD integrated graphics |

## Steps

| # | step | result | time |
|---|---|---|---|
| 1a | the C++ project `HrotUnreal`: copied from the engine's `TP_Blank` template; plugins enabled: LandscapeCombinator, PCG, Water, GeometryScripting, PythonScriptPlugin, EditorScriptingUtilities | ✅ | minutes |
| 1b | Landscape Combinator from GitHub, `main` = v0.9.0 (`eef7e900`), `EngineVersion` 5.8.0, into `Plugins/`; it bundles its own GDAL (Win64 `gdal.lib` + 34 DLLs), so nothing else to install. Its modules need GeometryScripting, PCG and StructUtils | ✅ compiled first time, no errors (431 actions) | 333 s |
| 1c | Python: plain rasterio + pyproj + numpy (GDAL arrives inside the rasterio wheel) | ✅ | ~1 min |
| 2 | `scripts/terrain-to-unreal.py` — GeoTIFF + GeoJSON + `frame.json` | ✅ `basic-desert` 61 × 61 at 10 m (tmerc at the origin) · `city-battle` 1 501 × 1 501 at 1 m (EPSG:32632, E = x + 499 250, N = y + 5 599 250). ⭐ read-back: every post identical (3 721 / 20 000 sampled, max difference 0) | seconds |
| 3a | `basic-desert` through Landscape Combinator, height agreement (`scripts/unreal/lc-measure.py` → the editor with `hrot_lc_import.py`) | ✅ after 4 findings (below): landscape 0…600 m in x and y, exactly HROT's; 16 components, no padding / cropping (61 posts = 60 quads). ⭐ **posts: mean 1.1 mm, max 1.8 mm** (2 000 points — Unreal's 16-bit heights); ⚠ **inside a cell: up to 0.47 m** where Unreal's triangle diagonal differs from HROT's (10 m cells on the ridge; vs Unreal's diagonal ≤ 1.5 mm) | spawn 24 s cold / 1 s warm; editor run 24–49 s |
| 3c | `city-battle` landscape (1 501 × 1 501 at 1 m, EPSG:32632) | ✅ landscape 0…1 500 m, exactly HROT's. ⭐ **posts: mean 0.4 mm, max 2.3 mm · cell centres: mean 0.4 mm, max 2.6 mm** (4 022 traces, 0 missed) — at 1 m posts the diagonal (F-6) is below the 16-bit step | spawn 18 s; editor run 73 s |

## Findings on the way (each one cost a run)

| # | what happened | fix |
|---|---|---|
| F-1 | `-ExecutePythonScript` CLOSES THE EDITOR on the next tick after the script returns (`EditorPythonExecuter.cpp:73`); the plugin's generation is asynchronous, so it died mid-import — then crashed on an assertion in its own `Concurrency` helper while unwinding (a plugin bug: a fatal `State->IsComplete()` in `RunOnThreadAndWait`) | `unreal.EditorPythonScripting.set_keep_python_script_alive(True)`, released when done |
| F-2 | a basic (non World Partition) level refuses a fresh landscape unless "Merge Images" is set — even for one file (`LandscapeSpawner.cpp:205`) | `HeightmapDownloader.bMergeImages = true` |
| F-3 | the plugin REWRITES its input GeoTIFF in place | the driver hands it a copy (`lc-input/`) |
| F-4 | ⭐ the plugin names a working folder after the CRS string (only `:` replaced) ⇒ a WKT CRS is an invalid path and its Reproject phase fails | UL-3: give it `EPSG:n`, or the PROJ string for a transverse Mercator |
| F-5 | ⭐ the plugin puts a landscape VERTEX on each pixel's north-west CORNER ⇒ a pixel-is-area GeoTIFF of our posts landed half a cell east and south (5 m) | UL-4: the exporter puts the pixel corner ON the post |
| F-6 | ⭐ **Unreal's landscape splits a cell along the OTHER diagonal** than HROT's `TerrainHeightGrid.Sample` (south-west → north-east) | ⏳ a design question, not fixed: harmless at 1 m posts; at 10 m posts on a slope the two grounds differ by up to ~0.5 m between posts |
| F-7 | the editor's "delete asset" leaves a map file on disk and `new_level` refuses an existing path | the driver deletes the old `.umap` before launching |
