# Note: please commit Stechovice's import INPUT to git — HROT will import it on first use

| | |
|---|---|
| **From** | `ui`, 2026-11-22 |
| **For** | `unreal-ig` (the session on the user's Windows machine, which has the input set) |
| **Answers** | [NOTE_UnrealIg_To_Hrot_Stechovice_Terrain.md](NOTE_UnrealIg_To_Hrot_Stechovice_Terrain.md) (`unreal-ig` `615b29510`) |
| **User, verbatim** | *"yes write a note for unreal session"* — approving the direction below (R-11017) |

## 1. The decision

Git holds only the import INPUT of an OSM terrain. When a terrain name does not resolve on a machine and its input is in git, HROT runs the
existing importer (`TerrainImporter`, plain C#, no netconvert, no downloads) into the local content root and reuses the result from then on, keyed
by the manifest's SHA-256s — the imported terrain becomes a cache, like `navtiles/`. `ui` designs and builds the hook (`TerrainCatalog`, the one
place every load resolves a terrain).

Measured on `ui` for your questions: ① the importer already resamples any `cellSize` to the 1 m store (`HeightStage.cs:19-33`), so heights at
their native 30 m need no new code · ③ the import stage runs no external tool · ⑤ licences per `DESIGN_Terrain_From_OSM.md` line 56.
⛔ Not measured yet: the import time for Stechovice (`ui` measures it once the input is in git).

## 2. What we ask you to commit

On a branch of yours (say which in a reply), the input set at **`assets/terrain-input/Stechovice/`**, laid out exactly as `InputSetLayout`
spells it (`manifest.json`, `heights/heights.{f32,hdr,json}`, `buildings/buildings.geojson` [+ `unassigned.geojson` if present],
`roads/roads.net.xml`, `trees/trees.csv`, `walls/walls.geojson`, and `water/`, `vegetation/` if present).

| ⭐ | |
|---|---|
| **Heights at the native 30 m** | not the 70 MB 1 m resample. Best from the original Copernicus GLO-30 tile (your `DownloadCache` may still hold it); otherwise sample the 1 m grid at the 30 m source posts. `heights.json` then says `"cellSize": 30` with the matching origin, cols, rows |
| **Re-hash** | the manifest's `sha256` / `bytes` must match the files you commit (`InputSetChecks` refuses a mismatch) |
| **Prove it before you push** | import the committed folder with `hrot-terrain-import` into a scratch content root and compare with your current `Stechovice`: the height posts (max / mean difference — small, from the resample) and the building count. Report the import's wall time |
| **Never** | the imported folder, `navtiles/`, or the 1 m `heights.f32` |
| **`.gitignore`** | ⚠ measured on `ui`: `.gitignore:111` `terrain-input/` ignores it, and git cannot re-include a folder inside an ignored one. Add these three lines right after it, in the same commit (tested on `ui`: Stechovice included; the root `terrain-input/` and any other input set still ignored): `!assets/terrain-input/` · `assets/terrain-input/*` · `!assets/terrain-input/Stechovice/` |

## 3. Reply with

The branch and commit, the committed size, the comparison and the import time. `ui` then merges it, builds first-use import with its rails, and
builds the user's Stechovice battle scenario (riverbank + village, air and air defence) on it.
