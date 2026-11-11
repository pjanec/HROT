using System;
using System.IO;
using System.Linq;
using Fdp.Toolkit.Navigation;
using Fdp.Toolkit.Navigation.Recast;
using Fdp.Toolkit.Orchestration;
using Xunit;

namespace Fdp.Toolkit.Terrain.Tests
{
    /// <summary>
    /// ⭐ O-2 (CE-4416, docs/DESIGN_Terrain_Authoring.md §18a) — the general world MERGE (<see cref="TerrainWorldMerge"/>) and the overlay reader
    /// (<see cref="TerrainFolderWorld.ReadOverlay"/>): the overlay APPENDS, base indices never move, every cross-index of an overlay part is offset.
    /// </summary>
    public sealed class TerrainWorldMergeTests
    {
        // base: a closed house "B" at (20,20) with a door; overlay: another house "O" with a door and a roof slab at (80,20) (indices from 0 in its own world)
        internal const string Base = """
            {"type":"FeatureCollection","hrot":{"schemaVersion":1,"bounds":[0,0,60,60],"groundZ":0},"features":[
              {"type":"Feature","properties":{"kind":"building","label":"B","doors":{"front":"open"},"building":{
                 "footprint":[[0,0],[10,0],[10,8],[0,8]],
                 "storeys":[{"height":3,"walls":[
                      {"from":[0,0],"to":[10,0],"openings":[{"kind":"door","at":4.4,"width":1.2,"doorId":"front"}]},
                      {"from":[10,0],"to":[10,8]},{"from":[10,8],"to":[0,8]},{"from":[0,8],"to":[0,0]}]}]}},
               "geometry":{"type":"Point","coordinates":[20,20]}}]}
            """;

        internal const string Overlay = """
            {"type":"FeatureCollection","hrot":{"schemaVersion":1,"bounds":[60,0,120,60],"groundZ":0},"features":[
              {"type":"Feature","properties":{"kind":"building","label":"O","doors":{"front":"open"},"building":{
                 "footprint":[[0,0],[10,0],[10,8],[0,8]],
                 "storeys":[{"height":3,"walls":[
                      {"from":[0,0],"to":[10,0],"openings":[{"kind":"door","at":4.4,"width":1.2,"doorId":"front"}]},
                      {"from":[10,0],"to":[10,8]},{"from":[10,8],"to":[0,8]},{"from":[0,8],"to":[0,0]}]}]}},
               "geometry":{"type":"Point","coordinates":[80,20]}},
              {"type":"Feature","properties":{"kind":"surface","surface":"forest"},"geometry":{"type":"Polygon","coordinates":[[[100,40],[110,40],[110,50],[100,50],[100,40]]]}}]}
            """;

        internal static TerrainWorld BaseWorld() => TerrainWorldParser.Parse(Base, "range");
        internal static TerrainWorld OverlayWorld() => TerrainWorldParser.Parse(Overlay, "range");

        /// <summary>
        /// Base parts keep their index (same objects, same positions); overlay part i lands at base.Count + i and EVERY cross-index is offset: a prism's Panel / Building, a walkable's
        /// Building, a panel's Building, a door's Panel (its Opening is panel-local, NOT offset) — the door still names ITS panel's opening. 🔴 Red-proof: drop the <c>Panel</c> offset of
        /// the door (or the prism's <c>Building</c>) in <see cref="TerrainWorldMerge.Merge"/> ⇒ this fails.
        /// </summary>
        [Fact]
        public void Merge_AppendsTheOverlay_AndOffsetsEveryCrossIndex_WhileTheBaseKeepsItsIndices()
        {
            var b = BaseWorld(); var o = OverlayWorld();
            Assert.NotEmpty(o.Walkables);   // the house has a roof slab
            var m = TerrainWorldMerge.Merge(b, o);

            Assert.Equal(b.Prisms.Count + o.Prisms.Count, m.Prisms.Count);
            Assert.Equal(b.Walkables.Count + o.Walkables.Count, m.Walkables.Count);
            Assert.Equal(b.Panels.Count + o.Panels.Count, m.Panels.Count);
            Assert.Equal(b.Buildings.Count + o.Buildings.Count, m.Buildings.Count);
            Assert.Equal(b.Doors.Count + o.Doors.Count, m.Doors.Count);
            Assert.Equal(b.Surfaces.Count + o.Surfaces.Count, m.Surfaces.Count);

            for (int i = 0; i < b.Prisms.Count; i++) Assert.Same(b.Prisms[i], m.Prisms[i]);          // the base's own indices, unchanged
            for (int i = 0; i < b.Panels.Count; i++) Assert.Same(b.Panels[i], m.Panels[i]);
            for (int i = 0; i < b.Doors.Count; i++) Assert.Same(b.Doors[i], m.Doors[i]);
            for (int i = 0; i < b.Buildings.Count; i++) Assert.Same(b.Buildings[i], m.Buildings[i]);
            for (int i = 0; i < b.Walkables.Count; i++) Assert.Same(b.Walkables[i], m.Walkables[i]);

            for (int i = 0; i < o.Prisms.Count; i++)
            {
                var p = m.Prisms[b.Prisms.Count + i];
                Assert.Equal(o.Prisms[i].Panel < 0 ? -1 : o.Prisms[i].Panel + b.Panels.Count, p.Panel);
                Assert.Equal(o.Prisms[i].Building < 0 ? -1 : o.Prisms[i].Building + b.Buildings.Count, p.Building);
                Assert.Equal(o.Prisms[i].Footprint, p.Footprint);
            }
            for (int i = 0; i < o.Walkables.Count; i++)
                Assert.Equal(o.Walkables[i].Building < 0 ? -1 : o.Walkables[i].Building + b.Buildings.Count, m.Walkables[b.Walkables.Count + i].Building);
            for (int i = 0; i < o.Panels.Count; i++)
                Assert.Equal(o.Panels[i].Building < 0 ? -1 : o.Panels[i].Building + b.Buildings.Count, m.Panels[b.Panels.Count + i].Building);
            for (int i = 0; i < o.Doors.Count; i++)
            {
                var d = m.Doors[b.Doors.Count + i];
                Assert.Equal(o.Doors[i].Panel + b.Panels.Count, d.Panel);
                Assert.Equal(o.Doors[i].Opening, d.Opening);                                           // panel-local: not offset
                Assert.Equal(d.Key, m.Panels[d.Panel].Openings[d.Opening].DoorKey);                    // it still names ITS panel's opening
                Assert.Equal("O", m.Buildings[m.Panels[d.Panel].Building].Label);                      // ...of the overlay's building
                Assert.Equal(b.Doors.Count + i, m.DoorIndexOf(d.Key));
            }

            Assert.Equal(new System.Numerics.Vector2(0, 0), m.BoundsMin);                              // the union
            Assert.Equal(new System.Numerics.Vector2(120, 60), m.BoundsMax);
            Assert.Equal("range", m.Name);
        }

        /// <summary>A key must stay globally unique: a building label or a door key present in both is a thrown error naming it. 🔴 Red-proof: drop either check ⇒ no throw.</summary>
        [Fact]
        public void Merge_ADuplicateBuildingLabelOrDoorKey_Throws_NamingIt()
        {
            var b = BaseWorld();
            var sameLabel = Assert.Throws<ArgumentException>(() => TerrainWorldMerge.Merge(b, BaseWorld()));
            Assert.Contains("'B'", sameLabel.Message);
            var sameDoor = Assert.Throws<ArgumentException>(() => TerrainWorldMerge.Merge(b, new TerrainWorld { Doors = b.Doors }));   // no building: only the door key clashes
            Assert.Contains(b.Doors[0].Key, sameDoor.Message);
        }

        /// <summary>The seed folders of the merged world are the base's, then the overlay's; a derived world (obstacles) keeps them.</summary>
        [Fact]
        public void Merge_SeedFoldersAreTheBasesThenTheOverlays_AndASingleFolderWorldDefaultsToItsSourceFolder()
        {
            var b = new TerrainWorld { SourceFolder = "/t/base" };
            var o = new TerrainWorld { SourceFolder = "/t/overlay" };
            Assert.Equal(new[] { "/t/base" }, b.SeedFolders);
            Assert.Empty(new TerrainWorld().SeedFolders);
            var m = TerrainWorldMerge.Merge(b, o);
            Assert.Equal(new[] { "/t/base", "/t/overlay" }, m.SeedFolders);
            Assert.Equal("/t/base", m.SourceFolder);
            var car = TerrainObstacles.PrismOf(new System.Numerics.Vector3(1, 1, 0), 0f, new ObstacleShape { Length = 4f, Width = 2f, Height = 1.5f }, m.Materials.Get("concrete", "t"), "o");
            Assert.Equal(m.SeedFolders, TerrainObstacles.With(m, new[] { car }).SeedFolders);
        }

        /// <summary>
        /// ⭐ The merged world's navmesh bake seeds from EVERY folder's <c>navtiles/</c> (the base's, then the overlay's): each part's tiles, prefilled alone, are cache hits in the merged bake;
        /// with the overlay's folder gone only the base's are. 🔴 Red-proof: make <c>RecastNavmeshFactory</c> seed from the first folder only ⇒ the two counts are equal.
        /// </summary>
        [Fact]
        public void TheMergedBake_SeedsFromTheBasesAndTheOverlaysTiles()
        {
            string root = Path.Combine(Path.GetTempPath(), "seeds-" + Guid.NewGuid().ToString("N"));
            string a = Path.Combine(root, "base"), o = Path.Combine(root, "overlay");
            Directory.CreateDirectory(a); Directory.CreateDirectory(o);
            try
            {
                var wa = TerrainWorldParser.Parse(Base, "range", new TerrainAssets { Folder = a });
                var wo = TerrainWorldParser.Parse(Overlay, "range", new TerrainAssets { Folder = o });
                var layers = NavLayerMask.Infantry;
                new RecastNavmeshFactory { Layers = layers, TileCache = NavTileCache.ForPrefill(Path.Combine(a, NavTileCache.TerrainFolderName)) }.Build(wa);
                new RecastNavmeshFactory { Layers = layers, TileCache = NavTileCache.ForPrefill(Path.Combine(o, NavTileCache.TerrainFolderName)) }.Build(wo);
                var merged = TerrainWorldMerge.Merge(wa, wo);
                Assert.Equal(new[] { a, o }, merged.SeedFolders);

                var both = new RecastNavmeshFactory { Layers = layers, TileCache = new NavTileCache() };
                both.Build(merged);
                Directory.Move(Path.Combine(o, NavTileCache.TerrainFolderName), Path.Combine(o, "gone"));
                var baseOnly = new RecastNavmeshFactory { Layers = layers, TileCache = new NavTileCache() };
                baseOnly.Build(merged);

                Assert.True(baseOnly.LastStats.FromCache > 0, "the base's prefilled tiles are hits");
                Assert.True(both.LastStats.FromCache > baseOnly.LastStats.FromCache, $"the overlay's tiles are hits too ({both.LastStats.FromCache} vs {baseOnly.LastStats.FromCache})");
            }
            finally { try { Directory.Delete(root, true); } catch { } }
        }

        /// <summary>
        /// <c>ReadOverlay</c> reads <c>overlay.json</c> + the world file it names through the one parser: a template placement resolves from the shared
        /// buildings root passed in (the base terrain's <c>assets/buildings</c> in production), SourceFolder is the overlay folder, and the header fields come back.
        /// 🔴 Red-proof: pass no shared root (or parse with <c>TerrainAssets.None</c>) ⇒ "building template 'shared-hut' was not found".
        /// </summary>
        [Fact]
        public void ReadOverlay_ReadsTheHeaderAndTheWorld_ResolvingSharedTemplates()
        {
            string dir = Path.Combine(Path.GetTempPath(), "ovl-" + Guid.NewGuid().ToString("N"));
            string shared = Path.Combine(Path.GetTempPath(), "ovl-shared-" + Guid.NewGuid().ToString("N"));   // stands for the base terrain's shared assets/buildings
            Directory.CreateDirectory(dir); Directory.CreateDirectory(shared);
            try
            {
                File.WriteAllText(Path.Combine(shared, "shared-hut.building.json"), """{"solid":true,"footprint":[[0,0],[4,0],[4,4],[0,4]],"height":3}""");
                File.WriteAllText(Path.Combine(dir, "overlay.json"),
                    """{"format":1,"contentHash":"abc","base":{"name":"bt-range","contentHash":"def"},"scenario":"s1","world":"overlay.world.geojson"}""");
                File.WriteAllText(Path.Combine(dir, "overlay.world.geojson"), """
                    {"type":"FeatureCollection","hrot":{"schemaVersion":1,"bounds":[0,0,200,200],"groundZ":0},"features":[
                     {"type":"Feature","properties":{"kind":"building","template":"shared-hut","label":"g1/0"},"geometry":{"type":"Point","coordinates":[100,100]}}]}
                    """);
                var ov = TerrainFolderWorld.ReadOverlay(dir, "bt-range", shared);
                Assert.Equal(1, ov.Header.Format); Assert.Equal("abc", ov.Header.ContentHash);
                Assert.Equal("bt-range", ov.Header.BaseName); Assert.Equal("def", ov.Header.BaseHash); Assert.Equal("s1", ov.Header.ScenarioId);
                Assert.Equal(Path.GetFullPath(dir), ov.World.SourceFolder);
                Assert.Equal("g1/0", Assert.Single(ov.World.Buildings).Label);
                Assert.NotEmpty(ov.World.Prisms);
                Assert.Empty(ov.World.Doors);

                File.WriteAllText(Path.Combine(dir, "overlay.json"), """{"format":2}""");
                Assert.Throws<ArgumentException>(() => TerrainFolderWorld.ReadOverlay(dir, "bt-range"));
            }
            finally { try { Directory.Delete(dir, true); Directory.Delete(shared, true); } catch { } }
        }
    
        /// <summary>
        /// ⭐ O-1 (CE-4415, Terrain Authoring §18b) — the MEASUREMENT, opt-in (<c>HROT_MEASURE_OVERLAY=1</c>; input by
        /// <c>scratchpad/o1/split.py</c>: scale-1 split into a 951-house base and a 700-house + 40-woods overlay). Times the overlay read and the merge, and
        /// counts the navmesh tiles the overlay's parts touch (the tiles a compile must bake; every other tile is the base's). Writes the numbers to
        /// <c>HROT_MEASURE_OUT</c> (a JSON file). Skips silently when not asked for.
        /// </summary>
        [Fact]
        public void O1_Measure_AScaleOneOverlay()
        {
            if (Environment.GetEnvironmentVariable("HROT_MEASURE_OVERLAY") != "1") return;
            string root = Environment.GetEnvironmentVariable("HROT_REPO") ?? throw new InvalidOperationException("HROT_REPO");
            string baseDef = Path.Combine(root, "assets/terrain/scale-1-o1base/terrain.json");
            string ovDir = Path.Combine(root, "scenarios/scale-rig-o1/terrain-overlay");
            var sw = System.Diagnostics.Stopwatch.StartNew();
            var def = TerrainFolderWorld.ReadDefinition(baseDef);
            var baseWorld = TerrainFolderWorld.ReadWorld(baseDef, def, "scale-1-o1base")!;
            long baseMs = sw.ElapsedMilliseconds; sw.Restart();
            var overlay = TerrainFolderWorld.ReadOverlay(ovDir, "scale-1-o1base", TerrainAssets.RecipesSibling(Path.GetDirectoryName(baseDef)!));
            long ovMs = sw.ElapsedMilliseconds; sw.Restart();
            var merged = TerrainWorldMerge.Merge(baseWorld, overlay.World);
            long mergeMs = sw.ElapsedMilliseconds;
            float t = RecastNavmeshBaker.DefaultTileMetres;
            var tiles = new System.Collections.Generic.HashSet<(int, int)>();
            void Box(System.Numerics.Vector2 min, System.Numerics.Vector2 max)
            {
                for (int x = (int)MathF.Floor(min.X / t); x <= (int)MathF.Floor(max.X / t); x++)
                    for (int y = (int)MathF.Floor(min.Y / t); y <= (int)MathF.Floor(max.Y / t); y++) tiles.Add((x, y));
            }
            foreach (var p in overlay.World.Prisms) Box(p.Min, p.Max);
            foreach (var w in overlay.World.Walkables) Box(w.Min, w.Max);
            foreach (var s in overlay.World.Surfaces) Box(s.Min, s.Max);
            var b = merged.BoundsMax - merged.BoundsMin;
            long allTiles = (long)MathF.Ceiling(b.X / t) * (long)MathF.Ceiling(b.Y / t);
            long bytes = 0; foreach (var f in Directory.EnumerateFiles(ovDir, "*", SearchOption.AllDirectories)) bytes += new FileInfo(f).Length;
            string json = $"{{\"baseReadMs\":{baseMs},\"overlayReadMs\":{ovMs},\"mergeMs\":{mergeMs},\"overlayBuildings\":{overlay.World.Buildings.Count},"
                + $"\"overlayPrisms\":{overlay.World.Prisms.Count},\"overlayPanels\":{overlay.World.Panels.Count},\"overlayDoors\":{overlay.World.Doors.Count},"
                + $"\"overlayWalkables\":{overlay.World.Walkables.Count},\"overlaySurfaces\":{overlay.World.Surfaces.Count},\"overlayFolderBytes\":{bytes},"
                + $"\"tilesTouched\":{tiles.Count},\"tilesInTerrainPerLayer\":{allTiles},\"mergedPrisms\":{merged.Prisms.Count}}}";
            File.WriteAllText(Environment.GetEnvironmentVariable("HROT_MEASURE_OUT") ?? Path.Combine(Path.GetTempPath(), "o1.json"), json);
        }

        /// <summary>
        /// ⭐ O-7 (CE-4418) step 1 — the MEASUREMENT of a live edit on scale-1, opt-in (<c>HROT_MEASURE_OVERLAY=1</c>; <c>HROT_MEASURE_OUT</c> = the table's file, else the temp folder; <c>HROT_REPO</c> = the repo root).
        /// A: today's path (<c>TerrainResidency.BakeObstacles</c>'s four stages: <c>TerrainObstacles.With</c>, <c>BepuTerrainIndex.AttachTo</c>, <c>RecastNavmeshFactory.Build</c> through an in-memory
        /// <c>NavTileCache</c>, <c>TerrainCoverProvider.Build</c>) for one wall, then the wall moved 2 m. B: a hamlet block expanded + merged + indexed + baked + covered, then moved 20 m.
        /// C: tiles touched vs total, and the all-hit navmesh build (hash + reassemble, no bake) as the floor. Skips silently when not asked for. ⚠ The first bake over scale-1 is COLD (≈ 9 min on 4 cores).
        /// </summary>
        [Fact]
        public void O7_Measure_ALiveEditOnScaleOne()
        {
            if (Environment.GetEnvironmentVariable("HROT_MEASURE_OVERLAY") != "1") return;
            string root = Environment.GetEnvironmentVariable("HROT_REPO") ?? ContentLayout.TryFindSourceTree() ?? throw new InvalidOperationException("HROT_REPO");
            string baseDef = Path.Combine(root, "assets", "terrain", "scale-1", "terrain.json");
            string baseFolder = Path.GetDirectoryName(baseDef)!;
            var table = new System.Text.StringBuilder();
            void Row(string k, string v) { table.AppendLine($"{k,-58} {v}"); Console.WriteLine($"[O7] {k,-58} {v}"); }
            long Ms(Action a) { var w = System.Diagnostics.Stopwatch.StartNew(); a(); return w.ElapsedMilliseconds; }
            RecastNavmeshFactory factory = null!;
            void Stages(string tag) { var st = factory!.LastStats; Row($"{tag} stages: wall total / parallel / assemble ms", $"{st.Milliseconds} / {st.ParallelMs} / {st.AssembleMs}"); Row($"{tag} thread-ms: gather / hash / cache(read+deserialise) / bake", $"{st.GatherMs} / {st.HashMs} / {st.CacheMs} / {st.BakeMs}"); }
            long Med(Action a) { var t = new long[3]; for (int i = 0; i < 3; i++) t[i] = Ms(a); Array.Sort(t); return t[1]; }

            TerrainWorld baseWorld = null!;
            Row("read scale-1 (TerrainFolderWorld.ReadWorld) ms", Ms(() => baseWorld = TerrainFolderWorld.ReadWorld(baseDef, TerrainFolderWorld.ReadDefinition(baseDef), "scale-1")!).ToString());
            Row("base: prisms / buildings / walkables / surfaces", $"{baseWorld.Prisms.Count} / {baseWorld.Buildings.Count} / {baseWorld.Walkables.Count} / {baseWorld.Surfaces.Count}");
            factory = new RecastNavmeshFactory { TileCache = new NavTileCache() };   // memory only: what a warm editor's cache is, without the disk
            var concrete = baseWorld.Materials.Get("concrete", "measure");
            var site = baseWorld.Buildings[baseWorld.Buildings.Count / 2].Position + new System.Numerics.Vector2(40f, 0f);
            Row("edit site (m)", $"{site.X:0},{site.Y:0}");
            TerrainPrism Wall(float dx) => TerrainObstacles.PrismOf(new System.Numerics.Vector3(site.X + dx, site.Y, 0f), 0f, new ObstacleShape { Length = 5f, Width = 0.3f, Height = 2f }, concrete, "obstacle:1");

            // ── A. today's baseline ─────────────────────────────────────────────────────────────────────────────────────────────────────────
            TerrainWorld w1 = null!;
            Row("A0 With(base, 1 wall) ms", Ms(() => w1 = TerrainObstacles.With(baseWorld, new[] { Wall(0f) })).ToString());
            Row("A0 index (BepuTerrainIndex.AttachTo) ms", Ms(() => Fdp.Toolkit.Spatial.Bepu.BepuTerrainIndex.AttachTo(w1)).ToString());
            Row("A0 navmesh COLD (first bake of the town) ms", Ms(() => factory.Build(w1)).ToString());
            Row("A0 navmesh tiles / baked / from cache", $"{factory.LastStats.Tiles} / {factory.LastStats.Baked} / {factory.LastStats.FromCache}");
            Row("A0 cover ms", Ms(() => Fdp.Toolkit.Spatial.Eqs.TerrainCoverProvider.Build(w1)).ToString());

            TerrainWorld w2 = null!;
            Row("A1 With(base, wall moved 2 m) ms (median of 3)", Med(() => w2 = TerrainObstacles.With(baseWorld, new[] { Wall(2f) })).ToString());
            Row("A1 index ms (median of 3)", Med(() => Fdp.Toolkit.Spatial.Bepu.BepuTerrainIndex.AttachTo(w2 = TerrainObstacles.With(baseWorld, new[] { Wall(2f) }))).ToString());
            Row("A1 navmesh, warm cache ms", Ms(() => factory.Build(w2)).ToString());
            Row("A1 navmesh tiles / baked / from cache", $"{factory.LastStats.Tiles} / {factory.LastStats.Baked} / {factory.LastStats.FromCache}"); Stages("A1");
            Row("A1 cover ms (median of 3)", Med(() => Fdp.Toolkit.Spatial.Eqs.TerrainCoverProvider.Build(w2)).ToString());
            long floorMs = Ms(() => factory.Build(w2));
            Row("C  navmesh, EVERY tile a hit (no bake) ms", floorMs.ToString());
            Row("C  … tiles / baked / from cache", $"{factory.LastStats.Tiles} / {factory.LastStats.Baked} / {factory.LastStats.FromCache}");

            // ── B. a block edit ────────────────────────────────────────────────────────────────────────────────────────────────────────────
            var recipe = TerrainRecipe.Find("hamlet", new[] { Path.Combine(root, "assets", "terrain-recipes") });
            var assets = TerrainAssets.ForFolder(baseFolder);
            (TerrainWorld Merged, TerrainWorld Overlay) Edit(float dx, string tag, bool timed)
            {
                FeatureExpansion x = null!; TerrainWorld overlay = null!, merged = null!;
                long te = Ms(() => x = FeatureExpander.Expand(recipe, 21, site + new System.Numerics.Vector2(dx, 0f), 0f, TerrainBlock.FitAll, baseWorld, "g1", assets));
                string json = "{\"type\":\"FeatureCollection\",\"hrot\":{\"schemaVersion\":1,\"groundZ\":" + baseWorld.GroundZ.ToString(System.Globalization.CultureInfo.InvariantCulture)
                    + ",\"bounds\":[" + string.Join(",", new[] { baseWorld.BoundsMin.X, baseWorld.BoundsMin.Y, baseWorld.BoundsMax.X, baseWorld.BoundsMax.Y }.Select(v => v.ToString(System.Globalization.CultureInfo.InvariantCulture))) + "]},\"features\":["
                    + string.Join(",", x.Features.Select(f => f.ToJsonString())) + "]}";
                long tp = Ms(() => overlay = TerrainWorldParser.Parse(json, "scale-1", assets));
                long tm = Ms(() => merged = TerrainWorldMerge.Merge(baseWorld, overlay));
                if (timed) { Row($"{tag} expand ({x.Houses} houses, {x.Woods} woods) ms", te.ToString()); Row($"{tag} parse overlay world ms", tp.ToString()); Row($"{tag} merge ms", tm.ToString()); }
                return (merged, overlay);
            }
            var (m1, o1) = Edit(0f, "B0", true);
            Row("B0 index ms", Ms(() => Fdp.Toolkit.Spatial.Bepu.BepuTerrainIndex.AttachTo(m1)).ToString());
            Row("B0 navmesh (warm for the base; the block's tiles are new) ms", Ms(() => factory.Build(m1)).ToString());
            Row("B0 navmesh tiles / baked / from cache", $"{factory.LastStats.Tiles} / {factory.LastStats.Baked} / {factory.LastStats.FromCache}");
            Row("B0 cover ms", Ms(() => Fdp.Toolkit.Spatial.Eqs.TerrainCoverProvider.Build(m1)).ToString());

            var (m2, o2) = Edit(20f, "B1 (moved 20 m)", true);
            Row("B1 index ms", Ms(() => Fdp.Toolkit.Spatial.Bepu.BepuTerrainIndex.AttachTo(m2)).ToString());
            Row("B1 navmesh (old + new block tiles in the cache) ms", Ms(() => factory.Build(m2)).ToString());
            Row("B1 navmesh tiles / baked / from cache", $"{factory.LastStats.Tiles} / {factory.LastStats.Baked} / {factory.LastStats.FromCache}");
            Stages("B1"); int bakedByMove = factory.LastStats.Baked, tilesAll = factory.LastStats.Tiles;
            Row("B1 cover ms", Ms(() => Fdp.Toolkit.Spatial.Eqs.TerrainCoverProvider.Build(m2)).ToString());

            // ── C. tiles touched vs total; the cost of looking at every tile ───────────────────────────────────────────────────────────────
            float t = RecastNavmeshBaker.DefaultTileMetres;
            var touched = new System.Collections.Generic.HashSet<(int, int)>();
            void Box(System.Numerics.Vector2 lo, System.Numerics.Vector2 hi)
            {
                for (int tx = (int)MathF.Floor(lo.X / t); tx <= (int)MathF.Floor(hi.X / t); tx++)
                    for (int tz = (int)MathF.Floor(lo.Y / t); tz <= (int)MathF.Floor(hi.Y / t); tz++) touched.Add((tx, tz));
            }
            foreach (var o in new[] { o1, o2 })
            { foreach (var p in o.Prisms) Box(p.Min, p.Max); foreach (var w in o.Walkables) Box(w.Min, w.Max); foreach (var sf in o.Surfaces) Box(sf.Min, sf.Max); }
            Row("C  tile squares touched by the old + new block (boxes, 24 m)", touched.Count.ToString());
            Row("C  navmesh work tiles in the town (layers x squares with geometry)", tilesAll.ToString());
            Row("C  tiles the move actually baked (changed keys)", bakedByMove.ToString());
            long floor2 = Ms(() => factory.Build(m2));
            Row("C  EVERY tile a hit on the merged block world ms", floor2.ToString()); Stages("C floor");
            // the gather half of that floor, sampled: TerrainTileTriangles.Gather over 200 squares around the site, scaled to every square
            var tri = new TerrainTileTriangles(m2);
            var verts = new System.Collections.Generic.List<float>(); var idx = new System.Collections.Generic.List<int>();
            int sx = (int)MathF.Floor(site.X / t), sz = (int)MathF.Floor(site.Y / t), n = 0;
            long gather = Ms(() => { for (int dx = -7; dx <= 7; dx++) for (int dz = -7; dz <= 7; dz++) { verts.Clear(); idx.Clear(); tri.Gather((sx + dx) * t, (sz + dz) * t, (sx + dx + 1) * t, (sz + dz + 1) * t, verts, idx); n++; } });
            long squares = (long)MathF.Ceiling((m2.BoundsMax.X - m2.BoundsMin.X) / t) * (long)MathF.Ceiling((m2.BoundsMax.Y - m2.BoundsMin.Y) / t);
            Row("C  Gather over 225 squares around the site ms (a dense town part)", gather.ToString());
            Row("C  … scaled to every square of the terrain ms (upper bound: the rest is sparser)", (gather * squares / Math.Max(1, n)).ToString());

            // ── D. incremental-capable? (by reading the code) ──────────────────────────────────────────────────────────────────────────────
            Row("D  BepuTerrainIndex", "whole only: ctor builds the tree from every prism/walkable; AttachTo replaces it (BepuTerrainIndex.cs:17-64); no Add/Remove");
            Row("D  TerrainCoverProvider", "whole only: Build loops every prism and panel (TerrainCoverProvider.cs:47-69); PointsAround (:77) is per piece but the provider is an immutable array");
            File.WriteAllText(Environment.GetEnvironmentVariable("HROT_MEASURE_OUT") ?? Path.Combine(Path.GetTempPath(), "o7.txt"), table.ToString());
        }
}
}
