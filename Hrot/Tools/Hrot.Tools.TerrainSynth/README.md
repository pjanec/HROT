# hrot-terrain-synth

A console tool that writes a **synthetic terrain input set**: the same folder layout and file formats as a real
prepared input set, with the same aggregate statistics (areas, counts, shares), generated from a seed. It exists so
the terrain importer can be built, tested and tuned in the open, without any real data in the repository.

Nothing is read from disk and nothing in the output is derived from real geometry. The only inputs are the numbers
in the profile (`SynthProfile.cs`), the command line and the seed.

Design: `docs/DESIGN_Terrain_Synth.md`. Format of the set: `docs/DESIGN_Terrain_Import_UTM.md` section 10.

## Usage

```
dotnet run -c Release --project Hrot/Tools/Hrot.Tools.TerrainSynth -- --preset full --seed 1
dotnet run --project Hrot/Tools/Hrot.Tools.TerrainSynth -- --preset tiny --seed 7 --out some/folder
```

| argument | meaning | default |
|---|---|---|
| `--preset tiny\|full` | the profile to follow | `tiny` |
| `--seed N` | seed of the generator (unsigned integer) | `1` |
| `--out DIR` | output folder | `artifacts/terrain-synth/<preset>-<seed>` |
| `--size M` | side length of the area in metres; building, tree and road counts scale by area | preset |
| `--height-spacing M` | height grid spacing in metres | preset |
| `--buildings N` | number of buildings | preset |
| `--trees N` | number of trees | preset |
| `--noise real\|none` | jitter the doors and windows like a real input set (see Realism options) | `real` for `full`, `none` for `tiny` |
| `--terraces on\|off` | about 10 % of the houses in terraced rows with shared walls | `on` for `full`, `off` for `tiny` |
| `--materials on\|off` | a `material` on about 30 % of the buildings | `on` for `full`, `off` for `tiny` |
| `--extras` | also write water, scrub and woods polygons (see Optional files) | off |

A bad argument prints the usage to stderr and exits with 2. A normal run prints one line per stage with its
elapsed time and counts, and exits with 0.

## Presets

| | `full` | `tiny` |
|---|---|---|
| area | 5 500 x 5 500 m, bounds `[-2750, -2750, 2750, 2750]` | 500 x 500 m, bounds `[-250, -250, 250, 250]` |
| heights | 0.48828125 m spacing, 450 to 980 m, two 500 m nodata cells at the east edge | same spacing, one 20 x 20 m nodata patch |
| buildings | about 1 800 in villages along the streets | about 40 |
| trees | about 176 000 (woods plus lone trees) | about 1 500 |
| roads | about 150 km in 8 classes, one rail line | a street grid and a primary road, about 3 km |
| walls | none (empty file) | none |

The frame is a synthetic UTM frame (zone 32, north, e0 500000, n0 5600000) with local metres: x east, y north, z up
(absolute). Headings are compass degrees, 0 = north, clockwise, pointing out of the building.

## Realism options

The clean set obeys every rule exactly (openings on their wall, heights on the floor, headings outward). Real data does
not, so options add the imperfections an importer has to tolerate. Every option is applied after the clean generation,
from a random stream of its own, so switching an option off leaves the clean geometry byte-identical for the same seed.

`--noise real` changes the door and window records of the buildings only, never footprints, floors or roofs:

- door `z` = floor + an offset with median +0.1 m, p10 -0.3 m, p90 +1.0 m, clipped to -1.2 .. +3.6 m;
  window `z` = floor + 1.0 m + a uniform +-0.2 m;
- position: half of the openings exactly on the wall, the rest moved perpendicular to it by |N(0, 0.1)| m, at most
  0.3 m, to a random side;
- about 3 % of all headings turned by 180 degrees;
- about 1 % of the doors moved 1 to 3 m off any wall (outside the footprint, still in the building's openings).

The manifest records `noise` under `generator.overrides` whenever it is on, also by default.

`--terraces on` turns about 10 % of the village houses into rows of 3 to 6 houses that share party walls:

- the row stands on the street line of the house it replaces, parallel to the street; a unit is 5.5 to 8.5 m wide;
- two neighbours use the same two vertices for their shared edge (identical coordinates), their footprints touch but
  do not overlap, and no door or window is placed on a shared wall;
- the 2 m gap rule still applies between every pair of houses except along a shared edge;
- plain houses are dropped at random afterwards so the total stays at the building count.

The manifest records `terraces` under `generator.overrides` whenever it is on.

`--materials on` writes a `material` property on about 30 % of the buildings (the rest have none, as in the real set):
of those, brick 50 %, concrete 25 %, stone 15 %, wood 10 %. In `buildings.geojson` the property sits after `ridgeZ`
(when there is one) and before `storeyZ`. The manifest records `materials` whenever it is on.

## Output

```
<out>/
  manifest.json                 frame, bounds, generator settings, sha256 + size + counts per file (no date)
  heights/heights.f32 .hdr .json   float32 little-endian, north row first, ESRI BIL header
  buildings/buildings.geojson   unique buildings: footprint, baseZ, height, roof, storeyZ, doors and windows
  buildings/unassigned.geojson  door / window points that belong to no footprint
  trees/trees.csv               x,y,z_ground
  roads/roads.nod.xml roads.edg.xml roads.typ.xml   plain network input
  roads/roads.net.xml           built from the plain files, only when netconvert is available
  walls/walls.geojson           empty collection
  water/water.geojson           only with --extras: one pond and one river strip
  vegetation/scrub.geojson      only with --extras: scrub polygons
  vegetation/woods.geojson      only with --extras: the woods as polygons
```

The default output folder is under `artifacts/`, which is ignored by git. The generated `full` set is about 500 MB
(almost all of it the height grid), so it is never committed. Whether a generated `tiny` set is committed as a
fixture is a separate decision.

## Optional files (`--extras`)

`--extras` (off by default, so `full` stays like the real set) writes three more files, all listed in the manifest with
their feature counts, in the same GeoJSON layout as the others (an `hrotInput` block, compact JSON, closed
counter-clockwise rings, no holes, coordinates to 0.01 m):

- `water/water.geojson`: one pond (an irregular polygon of 2 000 to 8 000 m2, `kind` `pond`) and one river strip (15 to
  30 m wide, running from one side of the area to the other, `kind` `river`). Roads may cross the river (a bridge). The
  pond stays away from villages, roads, the river and nodata. Nothing is built or planted on or next to water: the
  building and tree placement is repeated with the water as an exclusion, so the building count stays on target.
- `vegetation/scrub.geojson`: 3 to 8 scrub polygons (`kind` `scrub`) outside woods, villages, water, buildings and nodata.
- `vegetation/woods.geojson`: the woods as polygons, traced from the woods mask on a 5 m raster and simplified to about
  2.5 m, with the woods density in trees per hectare as `density`. A woods region with a hole is cut along a column
  through the hole into pieces without holes; pieces may touch along the cut. Buildings and water are kept out with a
  6 m strip.

With `--extras` the woods are described by the polygons, so `trees/trees.csv` holds only the trees outside woods (the
lone trees and rows), none of them inside a woods polygon. The manifest records `extras` under `generator.overrides`.

## Determinism

The same arguments give byte-identical files: the tool has its own seeded generator (no `System.Random`), writes
every number with the invariant culture (a machine with a decimal comma still writes `1.5`), uses LF line endings, and
puts no clock value in any output. Height rows are computed in parallel but written in order.

Each generator draws from its own named stream of the seed, so changing one count (for example `--trees`) does not
change the heights, roads or buildings.

## Roads and netconvert

The tool writes the road network as SUMO plain XML. It then runs `netconvert` when it finds one in
`%SUMO_HOME%/bin` or on `PATH`, and writes `roads.net.xml`. Without it the plain files are still written and a
`WARNING: netconvert not found` line is printed; a netconvert failure is a warning as well, not an error. The run
date that netconvert puts in a comment at the top of its output is removed so the file stays reproducible.

Every road class except the motorway is two-way: the plain edge file holds a reverse edge `<id>-r` for each such
edge. The motorway is a pair of one-way edges. Roads cross at grade only through shared junction nodes, except
motorway and rail, which pass over other roads.

## Tests

```
dotnet test Hrot/Tools/Hrot.Tools.TerrainSynth.Tests/Hrot.Tools.TerrainSynth.Tests.csproj
```

The tests check the format and the rules every set must obey (closed counter-clockwise footprints, no overlaps, no
tree on a road or in a building, door and window heights, outward headings, hashes in the manifest), the statistics of
the `full` preset (counts, shares, nearest-neighbour spacing) and byte-identical output for equal arguments.

## Measured full run

`--preset full --seed 1`, Release build, one run on a development machine (the times depend on the core count; the
height grid is computed in parallel):

| stage | time | result |
|---|---|---|
| heights | 2.24 s | 11 265 x 11 265 posts, 507.6 MB |
| roads | 0.08 s | 818 nodes, 1 075 edges, 30 villages |
| buildings | 0.26 s | 1 800 buildings, 1 751 unassigned openings (noise, terraces and materials on, the `full` default) |
| trees | 2.64 s | 176 112 trees |
| walls | 0.00 s | empty |
| manifest | 1.04 s | hashes of the 507 MB grid and the other files |

Total wall time 6.95 s including process start and netconvert (the roads stage line does not include netconvert).
Peak working set 127 MB, because the height grid is streamed to disk in bands of 64 rows. Output size 499 MiB on disk
(`heights.f32` 507 600 900 bytes, `buildings.geojson` 6.7 MB, `trees.csv` 4.1 MB, `roads.net.xml` 4.2 MB).

With `--extras` the same run takes about 11 s: water 0.29 s, buildings 0.41 s, trees 6.5 s (the woods mask is traced into
polygons), vegetation 0.01 s, and writes 2 water polygons, 3 scrub polygons, 20 woods polygons (about 45 % of the area,
density 125 trees/ha) and 11 313 trees outside woods.
