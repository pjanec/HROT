<!--STATUS
state: DONE
updated: 2026-11-10
current-answer: §1 verdict · §2 path A (the real UTM set) · §3 path B (OSM) · §4 navmesh bake · §5 findings · §6 fixed here
related-designs:
  - RESUME_Coordinator_Terrain_Import.md — §2 asked for these runs
  - ../DESIGN_Terrain_Import_UTM.md — §10 input set, §11 importer (I-e openings)
  - ../DESIGN_Terrain_From_OSM.md — the OSM pipeline
-->

# REPORT — the terrain import on REAL data *(source-data session, `2026-11-10`, coordinator at `30f03f007`)*

Runs of RESUME_Coordinator_Terrain_Import §2 on the user's Windows machine (Intel, 24 cores, Windows 11, .NET 8, Release). The real input
set stays on the user's drive (⛔ never in git); its terrain was written as `assets/terrain/real-1/` and kept out of git by a local
`.git/info/exclude` only. The user then allowed fixes (the coordinator on hold): F-R1, F-R2, F-R3, F-R6, F-R7 and a Windows import flake (F-R8) are FIXED in this commit (§6); F-R4 / F-R5 are left (F-R4 is in files the ui lane is editing).

## 1. Verdict

**Both paths work end to end on real data, and the real terrain loads and runs in the scale rig (warm load 25.3 s ✅).** One format mismatch
blocked path A on the first try (F-R1); the rest are fidelity / reporting findings, none blocking. After the fixes (§6) the real terrain has
**3 852 doors** in the loaded terrain (was 3 674).

## 2. Path A — the real UTM input set → `real-1`

Set: 5.5 km square, 0.488 m heights (11 265², 1.65 % NODATA), 1 805 buildings with 4 638 doors and 67 208 windows, 176 236 tree points,
153 km of SUMO net (3-D lane shapes), no walls.

```
terrain 'real-1': heights 5501 x 5501 posts at 1 m (500087 NODATA filled)
buildings: 1745 enterable, 60 solid (60 for want of a stair), 1246 roof caps, 0 shared walls merged, 2455 stairs
openings: 62299 placed, 5983 dropped (off wall 1897, party wall 0, overlap 4053, other 33); 1786 unassigned points ignored
vegetation: 0 wood polygons + 18881 clustered from points, 9932 lone trees, 0 water areas, 0 walls, 0 scrub skipped
output 142.2 MB; seconds per stage: read 1.3, heights 1.1, roads 0.1, buildings 2.8, vegetation 0.2, write 0.1 (total 5.5)
  213 warnings: "storey N is x m — raised to 2,4 m"
verify: the terrain loader parsed it in 2.1 s — 1745 buildings (0 solid), 215860 prisms, 21417 wall panels, 3674 doors, 7824 slabs/ramps, 18881 surfaces
```

- **Import 5.5 s** (vs 18.1 s for synth full-1 on the cloud VM — the read + hash cost CE-4380 is 1.3 s on this machine's NVMe).
- **Solid for want of a stair: 60 of 1 805 (3.3 %)** — fewer than synth's 6 %.
- **Doors 4 638 → 3 674 in the loaded terrain.** Breakdown (replayed I-e offline on the set):
  - 700 doors are > 0.6 m from their building's outline — ALL lie INSIDE the footprint: 246 at roof level (roof exits) and 453 at ground level,
    of which only 52 are within 1.5 m of a wall (recessed doorways); the rest are courtyard / passage doors with no outline wall to sit on.
    ⇒ correct to drop; **only 3 of 1 805 buildings are left without a usable ground-floor door.**
  - **129 doors are dropped as "overlap" because a WINDOW earlier in the list took the spot** — see F-R2.
  - ≈ 135 doors belong to the 60 solid buildings.
- **Windows: 4 270 (6.4 %) dropped as overlap** (synth: 165) — the real window points are denser than one 1.2 m window per 1.2 m; acceptable for
  closed, opaque windows, noted as F-R5.
- **Storey raise to 2.4 m:** 148 buildings touched; 56 inner storeys raised → the floors above shift by a median 0.11 m, max 0.4 m; 97 top
  storeys raised (only lifts the roof). Harmless.
- **0 shared walls merged** is right: the real buildings are free-standing (only 10 parallel edge pairs within 0.3 m in the whole set).
- Trees: the points became 18 881 clustered surfaces + 9 932 lone trees (R-4190 as designed).

## 3. Path B — OSM `--bbox 50.02,15.19,50.04,15.22` → `osm-kolin-test`

```
enterable buildings 3402, doors 4559, windows 99867, trees ≈ 6720 (lone 47, woods 58.0 ha), roads 198.8 km, walls 135, water 10
  1120 SOLID (garages, sheds, … < 25 m²) · 1113 heights inferred · 9 courtyards split · 3199 shared walls · 103 corners snapped
  283 dropped < 12 m² · 65 thinner than 2.5 m · 1134 "dropped: they cross the edge of the area" (see F-R4) · 10 roofs skipped · 3 no free wall
budget: Ok
terrain 'osm-kolin-test': heights 2142 x 2218 posts at 1 m (0 NODATA filled)
buildings: 3288 enterable, 1234 solid (114 for want of a stair), 3007 roof caps, 1978 shared walls merged, 1597 stairs
openings: 86253 placed, 128 dropped (off wall 20, party wall 2, overlap 79, other 27)
vegetation: 19 wood polygons, 47 lone trees, 10 water areas, 135 walls, 51 scrub skipped
output 31.7 MB; import 6.5 s (buildings 6.1)
verify (re-import with --verify): loader parsed it in 3.6 s — 3288 buildings, 296524 prisms, 37864 wall panels, 4387 doors, 6483 slabs/ramps
```

- Overpass answered from this machine (the whole CLI run incl. Overpass, Copernicus DEM and netconvert ≈ 62 s).
- `scripts/install-sumo.ps1` installed netconvert 1.20.0 cleanly (≈ 1 min).
- ⚠ the OSM CLI writes its input set to `terrain-input/<name>/` in the repo root, which is **not gitignored** (F-R6).

## 4. Navmesh bake — `real-1` through the scale rig

`scripts/scale-rig.py --terrain real-1 --men 96 --sides 1` (orchestrator + cgf + simhost on the one machine), `HROT_NAVTILE_CACHE` empty, then again:

| run | load | cgf work / garbage / peak | simhost work / garbage / peak | moved | verdict |
|---|---|---|---|---|---|
| cold (bakes 106 722 navmesh tiles into the cache, both loading nodes baking) | **366.1 s** ⛔ | 7.4 ms · 120 KB · 0.75 GB | 7.5 ms · 122 KB · 2.59 GB (gen0 17 573) | 12 / 12 | misses load, garbage |
| warm (every tile from the cache) | **25.3 s ✅** | 9.7 ms · 124 KB · 0.81 GB | 9.8 ms · 130 KB · 2.30 GB | 11 / 12 | misses garbage only |

- the cold first load (≈ 6 min) is the case for the **navmesh prefill at import** (RESUME §3 item 3) — an editor user would wait this long on
  the first open.
- the garbage miss is the known per-frame garbage (backend's queue), not terrain-specific.
- the cache on disk after the cold run: 106 722 tiles.
- ⚠ the cold run was BEFORE the §6 importer fixes; the re-import adds 178 doors, so a few tiles will rebake on the next load (not re-measured).

## 5. Findings for the coordinator

| id | finding | where | lean |
|---|---|---|---|
| **F-R1** | `heights.json` must carry `frame.utm` — the reader requires it (`InputSetReader.ReadFrame`), the writer writes it, but **§10's list of `heights.json` fields does not name it**. And the error was a bare `JsonElement` message with no file name: *"The requested operation requires an element of type 'Object', but the target element has type 'String'"* (the set had `"frame"` as a text note). | UTM §10 · `HeightsGrid` / `InputSetReader.ReadFrame` | add `frame` to §10's `heights.json` line (or fall back to the manifest's frame); wrap reader `JsonException` / `InvalidOperationException` with the file name. Data side already fixed. |
| **F-R2** | **Doors lose to windows** in I-e: openings are placed in list order, so a window listed before a door takes the spot and the door is dropped as overlap — 129 real doors. Placing doors first (then windows) keeps all 129 (verified offline: doors 3 809 → 3 938 placed, windows −123). | `OpeningPlacer.Place` | sort doors before windows; doors are the gameplay-critical openings |
| **F-R3** | `scripts/scale-rig.py terrain_info` unpacks lane shape points as `x,y` — a net with elevation (`x,y,z`, as real nets have) fails with `too many values to unpack`. | `scale-rig.py:73` | `q.split(",")[:2]` |
| **F-R4** | The OSM report's "N buildings dropped: they cross the edge of the area" counts buildings fully OUTSIDE the box (in the fetch margin) too: of 1 134, only 96 actually cross the box edge; ≈ 1 021 are wholly in the margin. Misleading in the wizard preview. | `BuildingRules` (`DroppedAtEdge`) / `PreviewBuilder` | count only rings with a vertex inside the box, ignore the rest silently |
| F-R5 | 6.4 % of real windows dropped as overlap (dense source points) | I-e | optional: window width 1.0 m, or merge overlapping windows into one wider one |
| F-R6 | `terrain-input/` (the OSM CLI's default `--out`) is not in `.gitignore` | `.gitignore` | add it (OSM-derived data is ODbL — keep it out of git like `artifacts/`) |
| F-R7 | 52 recessed ground doors (0.6 – 1.5 m inside the outline) are dropped | I-e reach | a 1.5 m reach for DOORS on storey 0 |
| F-R8 | **Windows: the importer's final rename of the temp folder fails intermittently** — `IOException: Access to the path '…\<name>.import-xxxx' is denied` at `TerrainImporter.Swap` (`Directory.Move`): the virus scanner / indexer briefly holds just-written files. Reproduced in `Hrot.Terrain.Import.Tests` (≈ 3 of 7 runs had a failure). An editor user would hit it too. | `TerrainImporter.Swap` | retry the move briefly |

## 6. Fixed in this commit *(source-data session, the user: "you can actually do any fixes and importer improvements if you like")*

| id | change | rail (red first) |
|---|---|---|
| F-R1 | `HeightsGrid.ReadInfo`: a `heights.json` without a frame OBJECT (absent, or a text note) takes the manifest's frame; §10's line now says `frame` is optional. The real set's original `heights.json` imports unchanged. | `HeightsJson_WithoutAFrameObject_TakesTheManifestFrame` (2 cases) |
| F-R2 | `OpeningPlacer`: doors placed before windows (stable otherwise) | `ADoor_WinsOverAnOverlappingWindow_ListedBeforeIt` |
| F-R7 | `OpeningPlacer.GroundDoorReach = 1.5` m for storey-0 doors; windows and upper doors keep 0.6 m (UTM I-e row updated) | `ARecessedGroundDoor_UpTo15mInside_SnapsToItsWall_ACourtyardDoorDeeperIn_IsDropped` |
| F-R8 | `TerrainImporter.Move`: up to 20 retries (50 ms × n) on IOException / UnauthorizedAccess while the source exists and the target does not | the suite 8 / 8 green after (was flaky) |
| F-R3 | `scale-rig.py`: lane shape points read as `x,y[,z]` | the real-1 rig runs above |
| F-R6 | `.gitignore`: `terrain-input/` | — |

**Re-import of the real set after the fixes:** openings 62 354 placed / 5 928 dropped (off wall 1 845, overlap 4 050); the loader: 3 852 doors
(was 3 674); import 5.8 s, verify 2.2 s. Suites: Hrot.Terrain.Import.Tests 38 / 38, Hrot.Terrain.InputSet.Tests 13 / 13.

**Left for the coordinator / ui:** F-R4 (ui is editing `BuildingRules` / `PreviewBuilder` — CE-4382), F-R5 (window overlap; acceptable for opaque
windows), the navmesh prefill (§4), CE-4379 / CE-4380 as in RESUME §3.
