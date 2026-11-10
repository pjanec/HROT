<!--STATUS
state: LIVE
updated: 2026-11-10
current-answer: §1 where we are · §2 run it on your machine · §3 what is next · §4 lanes in flight
stale-below: nothing
related-designs:
  - ../PLAN_Large_Terrain.md — the programme: scale work (backend), drawing (ui), import + OSM (coordinator); budgets R-4192
  - ../DESIGN_Terrain_Import_UTM.md — THE import design: §5 source answers, §6.1 stages, §7 / §7a units + performance, §8 destruction, §9.6 trees (R-4190), §10 the input set (R-4191 unique houses), §10.10 the format library, §11 the importer (+ §11.4 as built)
  - ../DESIGN_Terrain_From_OSM.md — the OSM pipeline: §4a contract, §6 / §6a / §6b wizard (R-4194), §14 detailed design + §14.8 / §14.9 as built, R-4193
  - ../DESIGN_Terrain_Synth.md — hrot-terrain-synth, the synthetic input set every test and measurement uses
  - ../DESIGN_Scale_Rig.md — backend's load / frame measurements at 5.5 km (§5, §7 many-core)
  - ../DESIGN_Map_3D_Mode.md — §6ah / §6ai ui's chunked 3-D terrain, trees and roofs (R-4195)
-->

# RESUME — coordinator: the terrain import (UTM data from the other simulator, and OpenStreetMap)

> RELEARN. Read `docs/blueprints/RULINGS.md` first (R-4186 … R-4195 are this programme's rulings), then this file's §1.

## 1. Where we are *(2026-11-10, coordinator at `ea546c50e` + this commit)*

**Both import paths are BUILT end to end. Neither has run on REAL data yet** — the real input set lives only on the user's drive (⛔ never in git,
licensing), and Overpass is blocked from the cloud VM.

```
other simulator ─► input set (§10 format, on the user's drive) ─┐
OpenStreetMap ─► Hrot.Terrain.Osm (pipeline) ─► input set ──────┼─► Hrot.Terrain.Import (importer) ─► assets/terrain/<name>/ ─► HROT
hrot-terrain-synth ─► synthetic input set (tests, measurements) ┘          (one translator, PLAN §9)
```

| piece | id | state | where |
|---|---|---|---|
| the input-set format library (writers, THE reader, format checks, netconvert locator) | CE-4371 | ✅ | `Hrot/Engine/Hrot.Terrain.InputSet` (UTM §10.10) |
| one UTM class (moved from Hrot.Traffic) | CE-4372 | ✅ | `FDP/Toolkits/Fdp.Toolkits/Geographic/Transforms/Utm.cs` |
| OSM pipeline + `hrot-terrain-osm` CLI | CE-4373 | ✅ 108 offline rails | `Hrot/Engine/Hrot.Terrain.Osm`, `Hrot/Tools/Hrot.Tools.TerrainOsm` (OSM §14.8) |
| Copernicus 30 m DEM (own COG reader) + netconvert road builder + `scripts/install-sumo.*` | CE-4374 | ✅ DEM verified LIVE (Sněžka) | `Hrot.Terrain.Osm/Dem`, `/Roads` (OSM §14.9) |
| the importer + `hrot-terrain-import` CLI | CE-4368 | ✅ 36 rails; full-1: 18.1 s import, 2.2 s parse | `Hrot/Engine/Hrot.Terrain.Import`, `Hrot/Tools/Hrot.Tools.TerrainImport` (UTM §11.4) |
| synthetic sets (tiny / full / full + extras) | — | ✅ regenerated on demand | `hrot-terrain-synth`, under `artifacts/` (gitignored) |
| loading + simulating 5.5 km in budget | backend L-1…L-8 | ✅ warm load ≈ 19.5 s one process; many-core tuning ongoing | DESIGN_Scale_Rig |
| drawing 5.5 km (3-D chunks + LOD, trees, gable roofs, 2-D) | ui D-1…D-3 | ✅ | Map 3D §6ah / §6ai |
| OSM wizard (File ▸ New ▸ Terrain from OpenStreetMap…) | CE-4375, CE-4378 | ✅ built + conformant + importer composed; ⏳ GREYED until CE-4381 | `Hrot.Presentation/TerrainOsm/` |

## 2. Run it on your machine *(the real end-to-end checks — do these first)*

**Prerequisites:** .NET 8 SDK · Python 3 (only for the SUMO install on Linux) · network to `overpass-api.de`, `copernicus-dem-30m.s3.amazonaws.com`,
`tile.openstreetmap.org`, `nominatim.openstreetmap.org` · `git pull origin coordinator`.

```bash
# 0. SUMO 1.20.0 (netconvert) into the repo's gitignored .local/ — once
bash scripts/install-sumo.sh                 # Windows: powershell -File scripts/install-sumo.ps1

# A. the REAL set from the other simulator → an HROT terrain (+ load it with the real loader, print the time)
dotnet run -c Release --project Hrot/Tools/Hrot.Tools.TerrainImport -- --set <path to the prepared input set> --name <terrain-name> --verify
#    then open the editor: File ▸ New Scenario → the terrain is in the picker; the first load bakes the navmesh (minutes), later loads use the cache

# B. a real OSM area → input set → HROT terrain   (--preview-only instead of --import = just the counts and the budget verdict)
dotnet run -c Release --project Hrot/Tools/Hrot.Tools.TerrainOsm -- --bbox 50.02,15.19,50.04,15.22 --name kolin-test --import

# C. (optional) the synthetic stand-ins, to compare
dotnet run -c Release --project Hrot/Tools/Hrot.Tools.TerrainSynth -- --preset full --seed 1
dotnet run -c Release --project Hrot/Tools/Hrot.Tools.TerrainImport -- --set artifacts/terrain-synth/full-1 --name synth-full-1 --verify
```

**What to send back if anything fails:** the full console output (the CLIs print per-stage seconds, counts and every warning). Useful numbers either
way: import seconds per stage, enterable / solid houses, openings placed / dropped, "solid for want of a stair", the `--verify` parse time, and the
first navmesh bake time when the editor opens it.

⚠ Known on real data: `InputSetChecks` refuses a set with format problems — its message names each one. A real set's ≈ 3 % inward headings and
≤ 0.3 m off-wall points are expected and tolerated (UTM §11.3 I-e).

## 3. What is next *(coordinator; none needs a user decision)*

| order | item | why |
|---|---|---|
| 1 | ⭐ **the results of §2** — fix whatever the real data breaks | the first real proof of both paths |
| 2 | **`UtmTransform : IGeographicTransform`** (stage 1, UTM §6.1) — chosen when `terrain.json` carries `frame.utm` (the importer already writes it, I-b) | lat / lon read-outs drift ≲ 2 m at 5 km on the tangent plane today; ⚠ touches the geographic origin (R-229, `DESIGN_Geo_Origin.md`, 53 users) — design first |
| 3 | **navmesh prefill** in the importer (stage 6, I-l) | the first load bakes ≈ 127 s (server GC, 4 cores) — pre-fill `NavTileCache` at import |
| 4 | **CE-4379** a switch-back stair when a straight one does not fit | 6 % of houses become solid today |
| 5 | **CE-4380** hash once while streaming | 14.9 of 18.1 s of an import is reading + hashing |
| later | OSM: split a too-large Overpass result, an Overpass 200 with an error `<remark>`, coastlines (OSM §7, F-5) · scrub as a concealment class (T-8) · tree felling (CE-3426) · destruction build (UTM §8, design READY) · the map background (OSM §8 — ⛔ the USER decides) | |

**How work was done here** (keep it): designs first in the owning doc (diagram first); big well-specified builds to ONE warm Sonnet worker with a full
brief; the coordinator reviews the diff, spot-checks surprising claims, re-measures only what it doubts; commit only own files by path.

## 4. Lanes in flight *(at writing)*

| lane | doing | handoff |
|---|---|---|
| **ui** | the OSM wizard LIVE: real tile source + Nominatim search (CE-4381 — un-greys the menu), the preview's missing details (CE-4382), CE-1466 | [HANDOFF_Coordinator_To_Ui_Osm_Wizard_Live](batches/HANDOFF_Coordinator_To_Ui_Osm_Wizard_Live.md) |
| **backend** | large-terrain performance (many-core); the user approved **C-1** (slow modules on wall-clock cadence) and **G-1** (server GC on headless nodes) | [RESUME_Backend_Large_Terrain_Perf](RESUME_Backend_Large_Terrain_Perf.md) |
| **behaviors** | its own queue (vehicle gun spread etc.) | — |

**Facts that cost time to learn** (measured):
- Overpass times out from the cloud VM; the OSM API, Nominatim, the OSM tiles and the Copernicus bucket answer. ⇒ OSM tests are offline (`--osm-file`, `--dem-dir`).
- netconvert must be 1.20.0 (`tools/sumo/version.txt`); the locator order is `HROT_SUMO_HOME` → `.local/sumo/<v>` → `SUMO_HOME` → `PATH`; another version is REFUSED.
- Copernicus GLO-30: 1° COGs, float32, deflate + predictor 3, 1 024² internal tiles, range reads; tile WIDTH varies with latitude — read the tags.
- A real Czech town box (1.8 × 1.7 km) held 2 974 buildings (23 % garages → solid, R-4193) and 1 852 shared walls: towns must be picked ≈ 1.5 km wide.
- UTM round trip: 0.29 mm on the central meridian, 0.47 mm at a 6 km box's far corner on a zone edge (rail in metres, not degree decimals).
- HROT building templates have no pitched roof: the importer writes a flat roof + a solid cap prism to the ridge; ui draws a gable over it (presentation only).
