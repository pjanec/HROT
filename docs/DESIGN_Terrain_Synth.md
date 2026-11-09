<!--STATUS
state: LIVE
build-state: APPROVED by the user 2026-11-09 — plan: docs/superpowers/plans/2026-11-09-terrain-synth.md
updated: 2026-11-09 (source-data session)
related-designs:
  - DESIGN_Terrain_Import_UTM.md §10 — the import input set this tool writes (format, frame, bounds); §10.6 unique buildings; §10.7 the real set's numbers
-->

# DESIGN — `hrot-terrain-synth`: an artificial terrain input set

## 0. Why

🔒 User, `2026-11-09`: *"Could we create an artificial terrain with similar parameters? So the coordinator can freely check in that one into Git if
it wants to. I would like the coordinator will develop his import and make all his tests and performance tuning on an artificial terrain. To avoid
any licensing issues. It could be also some kind of script that creates such a terrain to avoid storing big amount of artificially generated data
into Git."* — and: *"write it in C#, into suitable folder in hrot repo"*.

The real input set (Terrain Import UTM §10.7) stays off git. This tool writes a SYNTHETIC input set in exactly the same format, with the same
statistics, from a seed — so the importer can be built, tested and tuned in the open.

## 1. Decisions

| # | decision |
|---|---|
| 1 | a console tool `Hrot/Tools/Hrot.Tools.TerrainSynth` (assembly `hrot-terrain-synth`, net8.0, warnings as errors) + `Hrot.Tools.TerrainSynth.Tests` (xunit), both in `HROT.sln`; no project references — it depends on nothing in HROT |
| 2 | output = the §10 input set, byte for byte the same layout: `manifest.json`, `heights/heights.f32` + `.hdr` + `.json`, `buildings/buildings.geojson`, `buildings/unassigned.geojson`, `trees/trees.csv`, `roads/roads.net.xml` (+ the plain `roads.nod.xml` / `.edg.xml` / `.typ.xml` it is built from), `walls/walls.geojson` |
| 3 | CLI `--preset tiny\|full --seed N --out DIR`, overrides `--size M`, `--height-spacing M`, `--buildings N`, `--trees N`; default `--out artifacts/terrain-synth/<preset>-<seed>` (gitignored) |
| 4 | DETERMINISTIC: the same arguments give byte-identical files (own SplitMix64 RNG, invariant culture, no clock in the output) |
| 5 | statistics from a profile in code (aggregate numbers only — no source geometry): see §2 |
| 6 | roads: the tool writes SUMO plain XML and runs `netconvert` when found (`SUMO_HOME/bin` or `PATH`); without it the plain files are written and a warning says so |
| 7 | in git: the code and the profile only. Whether a generated `tiny` set is committed as a fixture is the coordinator's call |

## 2. The profile (`full` = the real set's statistics; `tiny` = a 500 m slice)

| | `full` | `tiny` |
|---|---|---|
| area | 5 500 × 5 500 m, bounds `[-2750, -2750, 2750, 2750]`, frame `utm {zone 32, N, e0 500000, n0 5600000}` (synthetic: on the zone's central meridian, ≈ 50.5°N) | 500 × 500 m, bounds `[-250, -250, 250, 250]` |
| heights | spacing 0.48828125 m, z 450–980 m, rolling hills; nodata: two 500 m cells at the east edge | same spacing; one 20 × 20 m nodata patch |
| buildings | ≈ 1 800 in villages along the streets; footprint area p10/p50/p90 ≈ 64 / 144 / 384 m²; ≈ 93 % rectangles, the rest L / U; storeys 1:17 %, 2:40 %, 3:32 %, 4:6 %, 5:5 %, 6:0.3 %; floor-to-floor 2.6–3.6 m (median ≈ 2.95); ≈ 2.6 doors and ≈ 37 windows per house; ≈ 73 % pitched roofs, ridge ≈ 4 m above the eaves; ≈ 1 unassigned point per house | ≈ 40 buildings |
| trees | ≈ 176 000; woods ≈ 45 % of the area holding ≈ 90 % of the trees ⇒ ≈ 110–120 trees / ha inside woods (the measured 65 / ha is the median over every hectare that has any tree, not the woods mean); nearest neighbour median ≈ 6 m; ≈ 7 % lone trees and rows | ≈ 1 500 |
| roads (km) | residential 57, unclassified 20, secondary 20, primary 16, motorway 12, tertiary 9, trunk 7.5, rail 8.7 (≈ 150 total) | a street grid + one primary road (≈ 3 km) |
| walls | none (empty file, as in the real set) | none |

## 3. Rules every generated set obeys (the tests check them)

- every file is in the §10 format; `manifest.files.*.sha256` matches the file; the `.hdr` and `.json` agree with the `.f32` size
- footprints: closed, counter-clockwise, no holes, no two overlapping, none crossing a road or the bounds; ids unique
- `storeyZ` ascending, `storeyZ[0] == baseZ`; a door's `z` == its storey's floor; a window's `z` == its floor + 1.0; every heading points OUT of the footprint; `height` > top floor − `baseZ`; a pitched roof has `ridgeZ` > `baseZ + height`
- trees: none inside a footprint or on a road; heights z ∈ [450, 980] except nodata
- numbers are written with the invariant culture (a `cs-CZ` machine still writes `1.5`)

## 4. Coordinator review *(`2026-11-09`; the importer side)*

✅ **Built `2026-11-09` (S-1…S-4, commits 6d63fa91a, 80da751ba, 3a756ce78, e0fea1245; 87 tests):** `--noise real|none`, `--terraces on|off`, `--materials on|off` (each defaults ON for `full`, OFF for `tiny` — separate switches so the clean output stays byte-identical, pinned by `CleanPinTests`) and `--extras` (water, scrub, woods polygons; off by default; with it `trees.csv` holds only trees outside woods). Measured on `full`: door z offset p10 −0.33 / median +0.10 / p90 +0.99 m, 3.1 % headings flipped, 0.9 % loose doors; 10 % of houses in 40 terraced rows; 33 % with a material; extras: a pond, a 19 m river, 3 scrub and 20 woods polygons (45 % of the area); a full run with `--extras` ≈ 11 s. Details in the tool README.

✅ **ACCEPTED as the importer's development terrain.** 📐 Measured here: the 53 tests pass; `tiny` (seed 1) generates in ≈ 1 s (1 025² heights, 40 buildings, 1 406 trees) and is byte-identical on a second run (`diff -r`); `full` (seed 1) generates in ≈ 16 s on this VM (495 MB) (11 265² heights, 1 800 buildings, 30 villages). The output's property names and frame notes match §10 of Terrain Import UTM (`GeoJsonWriter.cs:35-53`).

| # | finding | lean |
|---|---|---|
| **S-1** | ⚠ **the synthetic set is CLEANER than the real one.** Real (§10.7): door points sit a median 0.11 m above the grid (98 % within −1.2…+3.6 m), **3 % of headings point INTO the footprint**, ≈ 1 unassigned point per house off any outline. Synthetic: every opening exactly on its wall, `z` exactly on the floor, every heading exactly outward (`BuildingGenerator.cs:273, 403, 413`) ⇒ the importer's tolerance code would never be exercised | ⭐ a `--noise real` option (default on for `full`, off for `tiny`): openings jittered off the wall by ≤ 0.3 m and in `z` by the measured spread, ≈ 3 % headings flipped, ≈ 1 % of doors on no wall. The conformance tests then check the CLEAN set only |
| **S-2** | ⚠ **no touching houses**: placement keeps a 2 m gap (`BuildingGenerator.cs:293`). OSM towns (Terrain From OSM F-4) have terraces with shared walls | ⭐ a share of terraced rows (e.g. 10 % of village houses in rows of 3–6 sharing walls) so the party-wall rule is tested before OSM needs it |
| **S-3** | ⚠ **`material` is never written** (the real set omits it too) ⇒ the importer's material path is untested | ⭐ write `material` on ≈ 30 % of buildings (brick / concrete / stone / wood) |
| **S-4** | ⚠ **no `water/`, `vegetation/scrub`, `vegetation/woods`** (the optional files of UTM §10.8 / OSM F-3) | ⭐ an `--extras` option writing a pond, a river strip, scrub and woods polygons — off by default so `full` stays like the real set |
| **S-5** | ✅ the synthetic FRAME (e0 500 000 / n0 5 600 000, bounds centred on 0) differs from the real one on purpose — the importer must read the frame and bounds from the manifest and assume nothing | keep |
| **S-6** | ✅ §1 decision 7 — **do not commit a generated set**: `tiny` takes ≈ 1 s and is deterministic, so the importer's tests GENERATE it in a fixture (the synth library referenced in-process) | — |
| **S-7** | ⚠ `roads.net.xml` needs `netconvert`; the importer's tests must not depend on it | ⭐ the importer reads only `roads.net.xml` (one reader — not also the plain files); its tests run the road stage when `netconvert` is present and SKIP it, reported, when not; dev machines install SUMO 1.20.0 by Terrain From OSM §9.1 |
