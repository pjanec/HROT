# hrot-terrain-synth Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** A deterministic C# console tool that writes a synthetic terrain import input set (Terrain Import UTM §10 format) with the real set's statistics.

**Architecture:** One standalone exe project (no HROT references) made of small single-purpose generators (heights, roads, buildings, trees) fed by one seeded RNG and one profile, plus writers for each file of the input set and a manifest with SHA-256s. An xunit project checks format, rules (spec §3), statistics and determinism.

**Tech Stack:** .NET 8 (`global.json` 8.0.408), C#, xunit 2.6.2, `System.Text.Json`; optional SUMO `netconvert` at run time.

**Spec:** `docs/DESIGN_Terrain_Synth.md` (format: `docs/DESIGN_Terrain_Import_UTM.md` §10, §10.6, §10.7)

## Global Constraints

- Projects: `Hrot/Tools/Hrot.Tools.TerrainSynth/Hrot.Tools.TerrainSynth.csproj` (`<OutputType>Exe</OutputType>`, `<AssemblyName>hrot-terrain-synth</AssemblyName>`) and `Hrot/Tools/Hrot.Tools.TerrainSynth.Tests/Hrot.Tools.TerrainSynth.Tests.csproj`; both `net8.0`, `TreatWarningsAsErrors=true`, `Nullable=enable`, `ImplicitUsings=enable` — copy the property groups of `Hrot/Tools/Hrot.Tools.ContentMigrate*`; test package versions exactly as in `Hrot.Tools.ContentMigrate.Tests.csproj`. Add both to `HROT.sln` with `dotnet sln add`.
- Deterministic: same arguments → byte-identical files. Use only `Rng` (Task 1) for randomness, never `System.Random` or `Guid`; no timestamps in output.
- Every number written with `CultureInfo.InvariantCulture`; coordinates rounded to 0.01 m, headings to 0.1°.
- Local metres, x east, y north, z up (absolute); headings compass degrees (0 = +y, clockwise).
- Default output `artifacts/terrain-synth/<preset>-<seed>` (already gitignored). Never write into the repo elsewhere.
- Comments: ASCII only in code (AGENTS.md "Avoid unicode characters").
- Generic wording only: nothing in code, comments or commit messages names the source engine or its data.

## Review Focus

- A `cs-CZ` (decimal comma) machine: files must still contain `1.5`, not `1,5` — test runs the generator under `CultureInfo.CurrentCulture = new("cs-CZ")` (Task 7).
- A building placed on or next to a nodata patch: `baseZ` must never be NaN or the nodata value — test in Task 5.
- `netconvert` missing, or `SUMO_HOME` with spaces in the path: the run still succeeds, plain files written, one warning — test in Task 4.
- A footprint near the bounds or a road: no overlap with roads, other footprints or the bounds edge — test in Task 5.
- The `full` heights grid (11 265² floats, ~508 MB): generation must stream rows to disk, never hold more than one band of rows — test that `HeightField.Write` works row-by-row via a 4-row test grid and a spacing override in Task 2; `full` itself is exercised only by the stats test with `--height-spacing 8`.

---

### Task 1: Project skeleton, RNG, profile, options

**Files:**
- Create: `Hrot/Tools/Hrot.Tools.TerrainSynth/Hrot.Tools.TerrainSynth.csproj`, `Program.cs`, `Rng.cs`, `SynthProfile.cs`, `SynthOptions.cs`
- Create: `Hrot/Tools/Hrot.Tools.TerrainSynth.Tests/Hrot.Tools.TerrainSynth.Tests.csproj` (+ `ProjectReference` to the tool), `RngTests.cs`, `OptionsTests.cs`
- Modify: `HROT.sln` (`dotnet sln add` both)

**Interfaces:**
- Produces: `sealed class Rng(ulong seed)` — SplitMix64; `ulong NextU64()`, `double NextDouble()` in [0,1), `double Range(double lo, double hi)`, `int Int(int loInclusive, int hiExclusive)`, `Rng Fork(string stream)` (seed = hash of parent seed + stream name; FNV-1a 64 over UTF-8). `sealed record SynthProfile(...)` with `static SynthProfile Tiny`, `static SynthProfile Full` holding every number in spec §2 (bounds, spacing, zMin 450, zMax 980, nodata rectangles, building count, footprint area quantiles, rect share 0.93, storey shares, floor-to-floor 2.6..3.6, doors 2.6/house, windows ~37/house, pitched share 0.73, ridge rise 4.0, unassigned 1.0/house, tree count, woods share 0.45, density 65/ha, lone share 0.07, road km per class, frame e0 500000 / n0 5600000). `sealed record SynthOptions(string Preset, ulong Seed, string OutDir, double? Size, double? HeightSpacing, int? Buildings, int? Trees)` with `static SynthOptions Parse(string[] args)` and `SynthProfile Resolve()` (preset + overrides; `--size` rescales bounds and scales counts by area).

- [ ] **Step 1: Write failing tests** — `Rng_SameSeed_SameSequence`, `Rng_Fork_IsIndependentAndStable` (`new Rng(7).Fork("trees").NextU64()` equals a constant you record on first green run and pin), `Options_Defaults` (`Parse(["--preset","tiny"])` → Seed 1, OutDir `artifacts/terrain-synth/tiny-1`), `Options_SizeOverride_ScalesCounts` (`--preset full --size 2750` → buildings ≈ Full/4 ±1).
- [ ] **Step 2: Run** `dotnet test Hrot/Tools/Hrot.Tools.TerrainSynth.Tests` → FAIL (types missing).
- [ ] **Step 3: Implement** the types above; `Program.Main` parses options and calls `TerrainSynthesizer.Run(options)` (Task 7) — until then a stub returning 0.
- [ ] **Step 4: Run** the tests → PASS; `dotnet build HROT.sln` → 0 warnings.
- [ ] **Step 5: Commit** `feat(TerrainSynth): project, seeded RNG, profile and options`

### Task 2: Height field

**Files:**
- Create: `Noise.cs`, `HeightField.cs`; Test: `HeightFieldTests.cs`

**Interfaces:**
- Consumes: `Rng`, `SynthProfile`
- Produces: `sealed class HeightField` — `HeightField(SynthProfile p, Rng rng)`; `int Cols`, `int Rows`, `double Spacing`, `double OriginX`, `double OriginY` (south-west post); `float Sample(double x, double y)` (bilinear; returns `float.NaN` inside nodata); `bool IsNoData(double x, double y)`; `void Write(string heightsDir)` writing `heights.f32` (north row first, little-endian), `heights.hdr` (ESRI BIL keys as in the real set: BYTEORDER I, LAYOUT BIL, NROWS, NCOLS, NBANDS 1, NBITS 32, PIXELTYPE FLOAT, BANDROWBYTES, TOTALROWBYTES, BANDGAPBYTES 0, NODATA -9999, ULXMAP, ULYMAP, XDIM, YDIM), `heights.json` (`originX, originY, originIs, cellSize, cols, rows, rowOrder "north first", nodata -9999.0, units, frame, minZ, maxZ, nodataCells`). `Sample` must be a pure function of (x, y) (fBm value noise, 6 octaves, base wavelength 1 200 m, plus 2 ridge terms) so other generators query it without the grid in memory.

- [ ] **Step 1: Failing tests** — `Write_SizesAgree` (spacing override 50 m on tiny: file length == cols*rows*4, hdr NROWS/NCOLS == json rows/cols); `Values_InRange` (all non-nodata values in [450, 980]); `NoData_PatchPresent` (tiny: json `nodataCells` > 0 and `Sample` inside the patch is NaN); `RowOrder_NorthFirst` (first row in the file equals `Sample(x, OriginY + (Rows-1)*Spacing)`).
- [ ] **Step 2: Run** → FAIL.
- [ ] **Step 3: Implement.** Stream: compute and write one row at a time (`Parallel.For` over a band of 64 rows is fine; write bands in order). Map noise into [450, 980] with a fixed affine map and clamp.
- [ ] **Step 4: Run** → PASS.
- [ ] **Step 5: Commit** `feat(TerrainSynth): procedural height field and BIL writer`

### Task 3: Road layout

**Files:**
- Create: `RoadNetwork.cs`; Test: `RoadNetworkTests.cs`

**Interfaces:**
- Consumes: `Rng`, `SynthProfile`
- Produces: `enum RoadClass { Motorway, Trunk, Primary, Secondary, Tertiary, Unclassified, Residential, Rail }`; `sealed record RoadNode(string Id, double X, double Y)`; `sealed record RoadEdge(string Id, string From, string To, RoadClass Class, int Lanes, double SpeedMs, double WidthM)`; `sealed class RoadNetwork` — `static RoadNetwork Generate(SynthProfile p, Rng rng)`, `IReadOnlyList<RoadNode> Nodes`, `IReadOnlyList<RoadEdge> Edges`, `IReadOnlyList<Village> Villages` (`sealed record Village(double X, double Y, double Radius)`), `double LengthKm(RoadClass c)`, `double DistanceToRoad(double x, double y)` (centreline distance minus half width; spatial hash, cell 50 m).
- Layout: villages placed by rejection sampling (count = buildings / 60, radius 150–350 m); each village a jittered street grid (residential); villages joined by tertiary/unclassified; 2 primary + 2 secondary through-roads across the area; one motorway and one trunk crossing it; one rail line. Scale each class to its profile km (±10 %) by adding / dropping connector edges. All nodes inside bounds.

- [ ] **Step 1: Failing tests** — `Full_KmPerClass_WithinTenPercent` (each class in spec §2 within ±10 %); `AllNodesInsideBounds`; `EdgesReferenceExistingNodes`; `DistanceToRoad_ZeroOnCentrelineMinusHalfWidth`.
- [ ] **Step 2: Run** → FAIL. **Step 3: Implement.** **Step 4: Run** → PASS.
- [ ] **Step 5: Commit** `feat(TerrainSynth): village street grids and through-roads`

### Task 4: SUMO writer

**Files:**
- Create: `SumoWriter.cs`; Test: `SumoWriterTests.cs`

**Interfaces:**
- Consumes: `RoadNetwork`
- Produces: `static class SumoWriter` — `void WritePlain(RoadNetwork net, string roadsDir)` writes `roads.nod.xml`, `roads.edg.xml` (`type` = `highway.<class>` / `railway.rail`), `roads.typ.xml` (one `<type>` per class with `numLanes`, `speed`, `priority`, `width`; rail `allow="rail"`); `bool TryNetconvert(string roadsDir, TextWriter log)` — finds `netconvert[.exe]` in `%SUMO_HOME%/bin` then `PATH`, runs `netconvert -n roads.nod.xml -e roads.edg.xml -t roads.typ.xml --offset.disable-normalization true -o roads.net.xml` (arguments via `ProcessStartInfo.ArgumentList`, so paths with spaces work), returns false + one `WARNING: netconvert not found ...` line when missing.

- [ ] **Step 1: Failing tests** — `Plain_FilesParseAndCountsMatch` (XDocument loads; node/edge counts == network's); `Netconvert_Missing_WarnsAndReturnsFalse` (set `SUMO_HOME` to a temp dir with spaces, `PATH` to empty for the call → false, log contains `netconvert not found`); `Netconvert_WhenPresent_WritesNet` (if not found: return early with `Console.WriteLine("SKIP")`; else `roads.net.xml` exists and its `<location netOffset="0.00,0.00"`).
- [ ] **Step 2–4:** FAIL → implement → PASS.
- [ ] **Step 5: Commit** `feat(TerrainSynth): SUMO plain XML and optional netconvert`

### Task 5: Buildings

**Files:**
- Create: `BuildingGenerator.cs`, `GeoJsonWriter.cs`; Test: `BuildingTests.cs`

**Interfaces:**
- Consumes: `Rng`, `SynthProfile`, `HeightField.Sample/IsNoData`, `RoadNetwork.DistanceToRoad/Villages`
- Produces: `sealed record Opening(string Type, double X, double Y, double Z, double Heading, int Storey)`; `sealed record Building(string Id, IReadOnlyList<(double X, double Y)> Footprint, double BaseZ, double Height, string Roof, double? RidgeZ, IReadOnlyList<double> StoreyZ, IReadOnlyList<Opening> Openings)`; `static (IReadOnlyList<Building> Buildings, IReadOnlyList<Opening> Unassigned) BuildingGenerator.Generate(SynthProfile p, Rng rng, HeightField h, RoadNetwork r)`; `static class GeoJsonWriter` — `void WriteBuildings(string path, IReadOnlyList<Building> b)`, `void WriteUnassigned(string path, IReadOnlyList<Opening> u)`, `void WriteEmpty(string path, string note)`; same property names and `hrotInput` block as the real `buildings.geojson` (§10.7).
- Placement: in villages, along residential streets, front facade parallel to the street at 4–8 m setback; rect / L / U per profile; area drawn from a log-normal fitted to p10/p50/p90; reject when overlapping (2 m gap), `DistanceToRoad < 3`, outside bounds, or any corner on nodata. `BaseZ` = ground at the door (front-centre) point. Storeys from the profile shares; floors `BaseZ + cumulative floor-to-floor`. Doors: 1 on the street facade + 0–3 more (mean 2.6), z = their storey floor (most on storey 0). Windows: per storey, per wall, every 2.5–3.0 m with 1 m margins and not within 1 m of a door, z = floor + 1.0. Heading = outward wall normal as compass. `Height` = top floor − BaseZ + floor-to-floor; pitched (73 %) → `RidgeZ = BaseZ + Height + Range(3, 5)`. `Id` = first 12 hex of SHA-1 over the footprint rounded to 0.1 m (as the real set). Unassigned: ≈ 1 per house, windows / doors 0.5–2 m off a random facade.

- [ ] **Step 1: Failing tests** (tiny preset, spacing override 4 m) — `Footprints_ClosedCcwNoOverlapInsideBoundsOffRoads`; `Ids_Unique`; `StoreyZ_AscendingFromBase`; `Doors_OnTheirFloor_Windows_FloorPlusOne` (±0.01); `Headings_PointOutward` (1 m along heading leaves the footprint, 100 %); `Roofs_PitchedHaveRidgeAboveEaves`; `NoNaN_NearNoData` (every BaseZ finite and ≥ 450); `Full_Stats` (full preset, spacing 8: count ±10 % of 1 800, rect share 0.88–0.98, storey shares ±5 points, doors/house 2.2–3.0, windows/house 30–45, pitched 0.68–0.78).
- [ ] **Step 2–4:** FAIL → implement → PASS.
- [ ] **Step 5: Commit** `feat(TerrainSynth): village buildings with storeys, doors and windows`

### Task 6: Trees

**Files:**
- Create: `TreeGenerator.cs`; Test: `TreeTests.cs`

**Interfaces:**
- Consumes: `Rng`, `SynthProfile`, `HeightField`, `RoadNetwork.DistanceToRoad`, buildings' footprints
- Produces: `static IReadOnlyList<(double X, double Y, double Z)> TreeGenerator.Generate(SynthProfile p, Rng rng, HeightField h, RoadNetwork r, IReadOnlyList<Building> b)`; `static void TreeGenerator.WriteCsv(string path, IReadOnlyList<(double,double,double)> t)` (header `x,y,z_ground`).
- Woods: a low-frequency noise mask (wavelength ≈ 900 m) thresholded so the woods share is 0.45 ± 0.05, excluding villages; inside, a jittered grid at spacing `100 / sqrt(65)` m. Lone: rows along tertiary / unclassified roads every 10–15 m on one side, plus scattered field trees, together 7 % of the total. Reject on nodata, `DistanceToRoad < 2`, or within 2 m of a footprint. Z = ground.

- [ ] **Step 1: Failing tests** — `NoTreeOnRoadOrInBuilding`; `Full_Stats` (spacing 8: count ±10 % of 176 000, woods share 0.40–0.50 measured on a 25 m grid with ≥ 45 trees/ha, nearest-neighbour median 4–8 m).
- [ ] **Step 2–4:** FAIL → implement → PASS.
- [ ] **Step 5: Commit** `feat(TerrainSynth): woods and lone trees`

### Task 7: Orchestration, manifest, determinism

**Files:**
- Create: `TerrainSynthesizer.cs`, `ManifestWriter.cs`; Modify: `Program.cs`; Test: `EndToEndTests.cs`; Create: `Hrot/Tools/Hrot.Tools.TerrainSynth/README.md` (usage, presets, what is and is not in git)

**Interfaces:**
- Consumes: everything above.
- Produces: `static int TerrainSynthesizer.Run(SynthOptions o, TextWriter log)` — order: profile → heights (write) → roads (plain + netconvert) → buildings → trees → walls (empty) → manifest; returns 0. `static void ManifestWriter.Write(string outDir, SynthProfile p, SynthOptions o)` — `schemaVersion 1, name "synth-<preset>-<seed>", frame, axes, bounds, sumoAnchor [0,0], generator {name "hrot-terrain-synth", preset, seed, overrides}, files {<relative path>: {sha256, bytes, rows|features|cols}}` (paths sorted, `/` separators). No date.

- [ ] **Step 1: Failing tests** — `Tiny_WritesAllFiles_HashesMatch`; `SameArgs_ByteIdentical` (two runs into two temp dirs, every file equal); `DifferentSeed_Differs` (buildings.geojson differs); `DecimalCommaCulture_StillWritesDots` (run under `cs-CZ`, no `,` inside any number in `heights.json`, `buildings.geojson`, `trees.csv`); `Tiny_RunsUnderTenSeconds`.
- [ ] **Step 2–4:** FAIL → implement → PASS. Then run `dotnet run --project Hrot/Tools/Hrot.Tools.TerrainSynth -- --preset full --seed 1` once and record runtime + output size in the README.
- [ ] **Step 5: Commit** `feat(TerrainSynth): end-to-end run, manifest and README`
