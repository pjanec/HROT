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
