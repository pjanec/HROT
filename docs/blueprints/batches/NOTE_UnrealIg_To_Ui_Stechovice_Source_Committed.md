# Note: Stechovice's import input is committed

| | |
|---|---|
| **From** | `unreal-ig`, 2026-11-22 |
| **For** | `ui` |
| **Answers** | [NOTE_Ui_To_UnrealIg_Stechovice_Source.md](NOTE_Ui_To_UnrealIg_Stechovice_Source.md) |

## Where

Branch **`unreal-ig`**, commit **`db50572aa`**: `assets/terrain-input/Stechovice/` in the `InputSetLayout` (manifest, `heights/` x3, `buildings/` x2, `roads/roads.net.xml`, `trees/trees.csv`, `walls/`, `water/`, `vegetation/` x2) + the three `.gitignore` lines right after `terrain-input/` (as given; `assets/terrain-input/Other/x` still ignored, tested) + `assets/terrain-input/Stechovice/.gitattributes` (`* -text`).
⚠ **The `.gitattributes` is an addition you did not ask for:** `core.autocrlf=true` is set on this machine, and without it a Windows checkout turns the LF text files into CRLF and every manifest hash fails. The committed blobs were checked against the manifest (sha256 + bytes, all 11 files) from the index.

## Size

Raw **17.94 MB** (buildings 9.04, roads 8.70, heights 0.08, the rest 0.12); git stores it as about **2.95 MB** (zlib of each blob).

## Heights: SAMPLED, not from the tile

The GLO-30 tile is not in the DownloadCache any more (`%TEMP%\hrot-terrain-osm-cache\dem-tiles` is empty; the area folders hold nothing). So: the existing 1 m grid sampled every 30th post, written through `HeightsWriter` + `Manifest.Write` + `InputSetChecks.Check` (clean) by a small scratch tool (nothing hand-edited): origin -2021,-2128 (SW cell centre), **cellSize 30, 137 cols x 145 rows**, `sourceResolution` 30, minZ / maxZ from the writer. The last col / row lie past the 1 m grid's edge (a 4066 x 4304 m grid is not a multiple of 30): they repeat the edge post. Verified: every committed post equals the 1 m post it was taken from (max difference 0.0). The 1 m grid was itself the generator's resample of the tile, so this is the tile's surface re-sampled twice - fine for a source that is resampled to 1 m again.
⚠ A reader trap for anyone writing such a tool: `HeightsGrid.ReadAll` returns row 0 = SOUTH while the file is north first (my first version was flipped: 62 m mean error, caught by the comparison).

## The proof: `hrot-terrain-import` of the committed folder, into a scratch root

| | |
|---|---|
| buildings | **4 099 building files in both** the existing `data/shared/assets/terrain/Stechovice` and the import (4 099 enterable + 723 solid = the 4 822 footprints in the input) |
| heights vs the existing terrain (17 508 435 posts at 1 m) | **mean |difference| 0.52 m**, rms 0.82 m, **max 10.84 m** (at the north edge, col 1306 row 4304); the existing store has the house pads and roads too, both sides |
| import wall time | **514 s** with the navmesh bake (487.9 s of it); stages: read 0.5, heights 0.2, roads 0.6, pads 22.4, buildings 2.8, vegetation 0.0, write 0.1 - **about 27 s without the navmesh bake** (`--no-navmesh`). An earlier identical run: 477 s. 16-core machine, Release |
| output | 165.9 MB written (the existing one: 237 MB on disk) |

`data/shared` was only read.
