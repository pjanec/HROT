using System.Numerics;
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
            // O-7 A — the same edit with the DIRTY path: the wall moved 2 m again, told where it changed (its old and new box)
            var w3 = TerrainObstacles.With(baseWorld, new[] { Wall(4f) });
            Row("A2 (dirty) navmesh ms", Ms(() => factory.Build(w3, new[] { (w2.Prisms[^1].Min, w2.Prisms[^1].Max), (w3.Prisms[^1].Min, w3.Prisms[^1].Max) })).ToString());
            Row("A2 (dirty) tiles / baked / from cache / reused", $"{factory.LastStats.Tiles} / {factory.LastStats.Baked} / {factory.LastStats.FromCache} / {factory.LastStats.Reused}"); Stages("A2 dirty");
            w2 = w3;
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
            // O-7 A — the block moved another 20 m, told where it changed (the old and new block's parts)
            var (m3, o3) = Edit(40f, "B2 (moved 20 m more)", true);
            var dirtyB = new System.Collections.Generic.List<(System.Numerics.Vector2, System.Numerics.Vector2)>();
            foreach (var o in new[] { o2, o3 }) { foreach (var p in o.Prisms) dirtyB.Add((p.Min, p.Max)); foreach (var w in o.Walkables) dirtyB.Add((w.Min, w.Max)); foreach (var sf in o.Surfaces) dirtyB.Add((sf.Min, sf.Max)); }
            Row("B2 (dirty) index ms", Ms(() => Fdp.Toolkit.Spatial.Bepu.BepuTerrainIndex.AttachTo(m3)).ToString());
            Row("B2 (dirty) navmesh ms", Ms(() => factory.Build(m3, dirtyB)).ToString());
            Row("B2 (dirty) tiles / baked / from cache / reused", $"{factory.LastStats.Tiles} / {factory.LastStats.Baked} / {factory.LastStats.FromCache} / {factory.LastStats.Reused}"); Stages("B2 dirty");
            Row("B2 (dirty) cover ms", Ms(() => Fdp.Toolkit.Spatial.Eqs.TerrainCoverProvider.Build(m3)).ToString());
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


        // ── CE-4597 (P-3; docs/DESIGN_Ground_Patches_And_Road_Blockage.md §4 G-5, G-6) — a scenario house stands on its pad wherever the overlay merges ──

        /// <summary>A 1 m grid over 0..100 rising 0.2 m per metre to the east.</summary>
        private static TerrainWorld SlopeBase()
        {
            var sb = new System.Text.StringBuilder("ncols 101\nnrows 101\nxllcenter 0\nyllcenter 0\ncellsize 1\nNODATA_value -9999\n");
            for (int row = 0; row < 101; row++)
                for (int c = 0; c < 101; c++) sb.Append((0.2f * c).ToString(System.Globalization.CultureInfo.InvariantCulture)).Append(c < 100 ? " " : "\n");
            string grid = sb.ToString();
            const string geojson = "{ \"type\": \"FeatureCollection\", \"hrot\": { \"schemaVersion\": 1, \"bounds\": [0, 0, 100, 100], \"groundZ\": 0, \"heightGrid\": \"s.height.asc\" }, \"features\": [] }";
            return TerrainWorldParser.Parse(geojson, "slope", new TerrainAssets { ReadFile = n => n == "s.height.asc" ? grid : null });
        }

        private static string House(string label, float x, float baseZ, bool pad) =>
            "{\"type\":\"Feature\",\"properties\":{\"kind\":\"building\",\"label\":\"" + label + "\",\"baseZ\":" + baseZ.ToString(System.Globalization.CultureInfo.InvariantCulture) +
            (pad ? ",\"pad\":" + baseZ.ToString(System.Globalization.CultureInfo.InvariantCulture) : "") +
            ",\"building\":{\"footprint\":[[0,0],[8,0],[8,6],[0,6]],\"storeys\":[{\"height\":3,\"walls\":[{\"from\":[0,0],\"to\":[8,0]},{\"from\":[8,0],\"to\":[8,6]},{\"from\":[8,6],\"to\":[0,6]},{\"from\":[0,6],\"to\":[0,0]}]}]}}," +
            "\"geometry\":{\"type\":\"Point\",\"coordinates\":[" + x.ToString(System.Globalization.CultureInfo.InvariantCulture) + ",40]}}";

        /// <summary>
        /// ⭐⭐ <c>CE-4597</c> (G-5, G-6) — an overlay house that stands on a pad (its feature carries <c>"pad"</c>) is cut into the base's ground at its base where the overlay
        /// merges — the same on every node, the base grid unchanged and its far tiles shared; an overlay house with no pad (a bridge, say) leaves the ground alone.
        /// 🔴 Red-proof: merge with the base's Height as it is ⇒ the uphill corner stays 1.6 m up.
        /// </summary>
        [Fact]
        public void CE4597_AnOverlayHouseOnAPad_IsCutIntoTheBase_AnotherWithoutOne_IsNot()
        {
            var b = SlopeBase();
            var o = TerrainWorldParser.Parse("{\"type\":\"FeatureCollection\",\"hrot\":{\"schemaVersion\":1,\"bounds\":[0,0,100,100],\"groundZ\":0},\"features\":[" +
                House("p", 20f, 4f, pad: true) + "," + House("n", 60f, 12f, pad: false) + "]}", "slope");
            Assert.True(o.Buildings[0].Pad); Assert.False(o.Buildings[1].Pad);
            var m = TerrainWorldMerge.Merge(b, o);
            Assert.NotSame(b.Height, m.Height);
            Assert.InRange(m.Height!.Sample(28f, 46f), 3.99f, 4.01f);             // the pad house's uphill corner: on its pad (natural 5.6)
            Assert.InRange(b.Height!.Sample(28f, 46f), 5.59f, 5.61f);             // the base is unchanged
            Assert.InRange(m.Height.Sample(68f, 46f), 13.59f, 13.61f);            // the house with no pad: untouched slope
            Assert.Equal(TerrainWorldMerge.PadHeights(b.Height, o)!.Sample(24f, 43f), m.Height.Sample(24f, 43f));   // the same answer every merge
        }
    
        // ── ⭐ CE-4592 (P-5; docs/DESIGN_Ground_Patches_And_Road_Blockage.md §4a G-14, G-18, V-28) — authored ground shapes ─────────────────────────────

        private static TerrainWorld FlatBase()
        {
            int n = 201;
            return new TerrainWorld { Name = "flat", BoundsMin = new Vector2(-100f), BoundsMax = new Vector2(100f), Height = new TerrainHeightGrid(new Vector2(-100f), 1f, n, n, new float[n * n]) };
        }

        private static TerrainWorld ShapeOverlay(params System.Text.Json.Nodes.JsonObject[] features)
        {
            var doc = new System.Text.Json.Nodes.JsonObject { ["type"] = "FeatureCollection", ["features"] = new System.Text.Json.Nodes.JsonArray(features.Select(f => (System.Text.Json.Nodes.JsonNode)f).ToArray()) };
            return TerrainWorldParser.Parse(doc.ToJsonString(), "overlay");
        }

        private static System.Text.Json.Nodes.JsonObject Ground(string key, string mode, float value, uint order, params Vector2[] square)
            => FeatureExpander.OfKind(TerrainFeature.Ground, key, mode, 0f, value, order, Vector2.Zero, square, FlatBase(), new TerrainAssets(), Array.Empty<string?>())[0];

        private static Vector2[] Square(float cx, float cy, float half) => new[] { new Vector2(cx - half, cy - half), new Vector2(cx + half, cy - half), new Vector2(cx + half, cy + half), new Vector2(cx - half, cy + half) };

        /// <summary>
        /// ⭐ G-14 — a Raise 5 m square compiled (the ONE expander), parsed and merged: inside +5, the skirt blends to the natural ground (≥ 1.5 × 5 = 7.5 m), outside
        /// untouched; a Level shape sets its height; where two overlap the NEWEST (the higher order) wins whichever is listed first (V-28). 🔴 Red-proof: drop
        /// <c>GroundShapes.Apply</c> from the merge ⇒ the hill is gone.
        /// </summary>
        [Fact]
        public void CE4592_AGroundShape_RaisesLevelsAndBlends_AndTheNewestWins()
        {
            var merged = TerrainWorldMerge.Merge(FlatBase(), ShapeOverlay(Ground("g1", "raise", 5f, 1, Square(0f, 0f, 10f))));
            Assert.Equal(5f, merged.GroundHeightAt(0f, 0f), 3);
            Assert.Equal(5f, merged.GroundHeightAt(9.5f, 0f), 3);
            float mid = merged.GroundHeightAt(10f + 3.75f, 0f);
            Assert.InRange(mid, 0.5f, 4.5f);                       // in the skirt
            Assert.Equal(0f, merged.GroundHeightAt(10f + 8f, 0f), 3);   // past the 7.5 m skirt

            var both = TerrainWorldMerge.Merge(FlatBase(), ShapeOverlay(Ground("b", "level", 2f, 2, Square(5f, 0f, 6f)), Ground("a", "raise", 5f, 1, Square(0f, 0f, 10f))));
            Assert.Equal(2f, both.GroundHeightAt(5f, 0f), 3);       // the level (order 2) is the newest: it wins over the raise
            Assert.Equal(5f, both.GroundHeightAt(-8f, 0f), 3);
        }

        /// <summary>⭐ V-28 / G-18 — a house inside a raised area keeps its ground (an island with a metre's margin); an unknown mode fails at the compile.</summary>
        [Fact]
        public void CE4592_AHouseInsideAShape_IsAnIsland_AndAnUnknownModeFails()
        {
            const string House = """{ "type": "Feature", "properties": { "kind": "building", "label": "H", "building": { "footprint": [[0,0],[6,0],[6,6],[0,6]], "storeys": [ { "height": 3, "walls": [ { "from": [0,0], "to": [6,0] }, { "from": [6,0], "to": [6,6] }, { "from": [6,6], "to": [0,6] }, { "from": [0,6], "to": [0,0] } ] } ] } }, "geometry": { "type": "Point", "coordinates": [-3, -3] } }""";
            var house = (System.Text.Json.Nodes.JsonObject)System.Text.Json.Nodes.JsonNode.Parse(House)!;
            var merged = TerrainWorldMerge.Merge(FlatBase(), ShapeOverlay(house, Ground("g1", "raise", 4f, 1, Square(0f, 0f, 15f))));
            Assert.Equal(0f, merged.GroundHeightAt(0f, 0f), 3);     // the house's floor
            Assert.Equal(4f, merged.GroundHeightAt(10f, 10f), 3);   // the hill round it
            Assert.Throws<ArgumentException>(() => Ground("bad", "dig", 1f, 1, Square(0f, 0f, 2f)));
        }
    
        // ── ⭐ CE-4593 (P-6; §4c G-26…G-28, §4c-1 G-36) — ditches and foxholes ─────────────────────────────

        private static System.Text.Json.Nodes.JsonObject Ditch(string key, string mode, float depth, uint order, Vector2 at, float heading, params Vector2[] line)
            => FeatureExpander.OfKind(TerrainFeature.Ground, key, mode, heading, depth, order, at, line, FlatBase(), new TerrainAssets(), Array.Empty<string?>())[0];

        /// <summary>
        /// ⭐ G-26 / G-28 — a ditch drawn along a line is 2 m wide × 1 m deep with steep banks, an obstacle area (every vehicle keeps out, D-2); an anti-tank ditch 4 × 2 m;
        /// a foxhole at a click and a heading a 2 m dip, no obstacle. 🔴 Red-proof: a ditch's skirt the Lower's (1.5 × depth) ⇒ the bank point 1.5 m out is too deep.
        /// </summary>
        [Fact]
        public void CE4593_ADitch_IsASteepBankedStrip_AndAnObstacle_AFoxholeADip()
        {
            var merged = TerrainWorldMerge.Merge(FlatBase(), ShapeOverlay(
                Ditch("d", "ditch", 0f, 1, Vector2.Zero, 0f, new Vector2(-20f, 0f), new Vector2(20f, 0f)),
                Ditch("a", "at-ditch", 0f, 2, Vector2.Zero, 0f, new Vector2(-20f, 30f), new Vector2(20f, 30f)),
                Ditch("f", "foxhole", 0f, 3, new Vector2(0f, -40f), 90f)));
            Assert.Equal(-1f, merged.GroundHeightAt(0f, 0f), 3);          // the floor
            Assert.Equal(-1f, merged.GroundHeightAt(5f, 0.9f), 2);        // still the floor at the edge of its 2 m
            Assert.Equal(0f, merged.GroundHeightAt(0f, 2.0f), 3);          // past the 0.9 m bank: untouched
            Assert.Equal(-2f, merged.GroundHeightAt(0f, 30f), 3);          // the anti-tank ditch
            Assert.Equal(-1f, merged.GroundHeightAt(0f, -40f), 3);         // the foxhole
            Assert.Equal(-1f, merged.GroundHeightAt(0.9f, -40f), 2);       // along its heading (east)
            Assert.Equal(0f, merged.GroundHeightAt(0f, -42.5f), 3);        // across it, past its bank
            var obstacles = merged.GroundShapes.Where(g => g.Obstacle).ToList();
            Assert.Equal(2, obstacles.Count);                              // the two ditches, not the foxhole
            Assert.Equal(4, Fdp.Toolkit.Navigation.Recast.NavDoorways.Ditches(merged).Count);   // two triangles each
            Assert.All(Fdp.Toolkit.Navigation.Recast.NavDoorways.Ditches(merged), v => Assert.Equal(Fdp.Toolkit.Navigation.Recast.NavDoorways.CraterArea, v.Area));
        }
    
        /// <summary>
        /// ⭐ G-27 / G-36 / V-29 / V-30 — cover: a 1 m ditch 40 m long gives crouch points along BOTH banks, each facing its bank, on the ditch's floor; a foxhole gives four
        /// all-round points. 🔴 Red-proof: drop <c>AddGroundCover</c> from the build ⇒ none.
        /// </summary>
        [Fact]
        public void CE4593_ADitchGivesCoverAlongBothBanks_AFoxholeFourAllRound()
        {
            var merged = TerrainWorldMerge.Merge(FlatBase(), ShapeOverlay(
                Ditch("d", "ditch", 0f, 1, Vector2.Zero, 0f, new Vector2(-20f, 0f), new Vector2(20f, 0f)),
                Ditch("f", "foxhole", 0f, 2, new Vector2(0f, -40f), 90f)));
            var cover = Fdp.Toolkit.Spatial.Eqs.TerrainCoverProvider.Build(merged).Points;
            var ditch = cover.Where(p => MathF.Abs(p.PositionY) < 1.5f).ToList();
            Assert.True(ditch.Count >= 30, $"{ditch.Count} ditch points");
            Assert.Contains(ditch, p => p.DirectionY > 0.99f && p.PositionY > 0f);   // the north bank, facing north
            Assert.Contains(ditch, p => p.DirectionY < -0.99f && p.PositionY < 0f);  // the south bank, facing south
            Assert.All(ditch, p => { Assert.Equal((byte)1, p.StanceHeight); Assert.Equal(-1f, p.PositionZ, 2); });
            var fox = cover.Where(p => MathF.Abs(p.PositionY + 40f) < 0.1f && MathF.Abs(p.PositionX) < 0.1f).ToList();
            Assert.Equal(4, fox.Count);
        }
    
        /// <summary>
        /// ⭐ G-19 — a road running straight across a 5 m hill: the shape's steepest road grade is the skirt's (5 m over ≈ 7.5 m, smoothstepped — over the 15 % warning); a road
        /// 60 m away reads 0; a gentle 0.5 m rise stays under the warning.
        /// </summary>
        [Fact]
        public void CE4592_TheGradeReadOut_WarnsForAHillAcrossARoad()
        {
            var b = new global::CarKinem.Road.RoadNetworkBuilder();
            b.AddNode(new Vector2(-50f, 0f), 0f); b.AddNode(new Vector2(50f, 0f), 0f); b.AddNode(new Vector2(-50f, 60f), 0f); b.AddNode(new Vector2(50f, 60f), 0f);
            b.AddSegment(new Vector2(-50f, 0f), new Vector2(100f, 0f), new Vector2(50f, 0f), new Vector2(100f, 0f), laneWidth: 3.5f, laneCount: 2, startNodeIdx: 0, endNodeIdx: 1);
            b.AddSegment(new Vector2(-50f, 60f), new Vector2(100f, 0f), new Vector2(50f, 60f), new Vector2(100f, 0f), laneWidth: 3.5f, laneCount: 2, startNodeIdx: 2, endNodeIdx: 3);
            var roads = b.Build(cellSize: 10f, gridWidth: 20, gridHeight: 20);
            try
            {
                var hill = Ground("g", "raise", 5f, 1, Square(0f, 0f, 10f));
                var merged = TerrainWorldMerge.Merge(FlatBase(), ShapeOverlay(hill));
                var shape = merged.GroundShapes[0];
                Assert.True(GroundShapes.SteepestRoadGrade(merged, in roads, in shape) > GroundShapes.GradeWarning);
                var gentle = TerrainWorldMerge.Merge(FlatBase(), ShapeOverlay(Ground("h", "raise", 0.5f, 1, Square(0f, 0f, 10f))));
                Assert.True(GroundShapes.SteepestRoadGrade(gentle, in roads, gentle.GroundShapes[0]) < GroundShapes.GradeWarning);
                var far = TerrainWorldMerge.Merge(FlatBase(), ShapeOverlay(Ground("f", "raise", 5f, 1, Square(0f, -60f, 5f))));
                Assert.Equal(0f, GroundShapes.SteepestRoadGrade(far, in roads, far.GroundShapes[0]));
            }
            finally { roads.Dispose(); }
        }
    }
}
