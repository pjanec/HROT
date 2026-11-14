using System;
using System.IO;
using System.Linq;
using System.Text.Json.Nodes;
using Fdp.Toolkit.Navigation.Recast;
using Xunit;
using Xunit.Abstractions;

namespace Fdp.Toolkit.Terrain.Tests
{
    /// <summary>⭐ O-4 (CE-4415, DESIGN_Terrain_Authoring §18c) — <see cref="OverlayCompiler"/>: scenario DOM → <c>terrain-overlay/</c> (world file, header with hashes and <c>blocks[]</c>, the navtiles the base lacks).</summary>
    public sealed class OverlayCompilerTests
    {
        private readonly ITestOutputHelper _out;
        public OverlayCompilerTests(ITestOutputHelper output) => _out = output;

        private const string BlockGuid = "b1000000-0000-4000-8000-000000000001", HouseGuid = "a1000000-0000-4000-8000-000000000002";

        private static JsonObject Dom(OverlayFixture f) => f.Scenario((BlockGuid, OverlayFixture.Block("village", 11, 100, 100)), (HouseGuid, OverlayFixture.House("hut", 300, 300, 90)));

        /// <summary>
        /// A block and a house compile; <c>overlay.json</c> names the base and the hashes (contentHash == <c>OfOverlay(folder, baseHash)</c>) and <c>blocks[]</c> carries the right house / wood counts and a non-empty outline;
        /// the world file's features are in canonical label order. 🔴 Red-proof: count houses from the recipe instead of the placed ones ⇒ the counts disagree with the world file; skip the sort ⇒ order fails.
        /// </summary>
        [Fact]
        public void ABlockAndAHouse_Compile_WithHashesBlocksAndCanonicalOrder()
        {
            using var f = new OverlayFixture();
            var r = f.Compile(Dom(f));
            Assert.True(r.Wrote); Assert.Equal(1, r.Blocks);
            var header = JsonNode.Parse(File.ReadAllText(Path.Combine(f.Overlay, "overlay.json")))!.AsObject();
            var world = JsonNode.Parse(File.ReadAllText(Path.Combine(f.Overlay, "overlay.world.geojson")))!.AsObject();
            string baseHash = TerrainContentHash.OfTerrain(f.Definition);
            Assert.Equal(baseHash, (string)header["base"]!["contentHash"]!);
            Assert.Equal("base-t", (string)header["base"]!["name"]!);
            Assert.Equal((string)header["contentHash"]!, TerrainContentHash.OfOverlay(f.Overlay, baseHash));
            Assert.Equal(r.ContentHash, (string)header["contentHash"]!);

            var labels = world["features"]!.AsArray().Select(x => (string)x!["properties"]!["label"]!).ToList();
            Assert.Equal(labels.OrderBy(l => l, StringComparer.Ordinal), labels);
            Assert.Contains(HouseGuid + "/0", labels);
            int houses = labels.Count(l => l.StartsWith(BlockGuid + "/") && !l.Contains("/w")), woods = labels.Count(l => l.StartsWith(BlockGuid + "/w"));
            var b = Assert.Single(header["blocks"]!.AsArray())!.AsObject();
            Assert.Equal(BlockGuid, (string)b["key"]!); Assert.Equal("village", (string)b["recipe"]!); Assert.Equal(11, (int)b["seed"]!);
            Assert.Equal(houses, (int)b["houses"]!); Assert.Equal(woods, (int)b["woods"]!); Assert.Equal(0, (int)b["clutter"]!);
            Assert.Equal(10, houses);
            Assert.True(b["outline"]!.AsArray().Count >= 3);
            Assert.Equal(11, r.Houses);
        }

        /// <summary>The same scenario compiled twice gives identical folder bytes (the navtiles included — measured, see the output).</summary>
        [Fact]
        public void CompilingTwice_GivesIdenticalFolderBytes()
        {
            using var f = new OverlayFixture();
            var dom = Dom(f);
            f.Compile(dom, navtiles: true);
            var first = Snapshot(f.Overlay);
            f.Compile(dom, navtiles: true);
            var second = Snapshot(f.Overlay);
            _out.WriteLine($"{first.Count} files: {string.Join(", ", first.Keys.Select(k => k.Split('/')[0]).Distinct())}; navtile files {first.Keys.Count(k => k.EndsWith(".navtile"))}");
            Assert.Equal(first.Keys, second.Keys);
            foreach (var k in first.Keys) Assert.True(first[k].SequenceEqual(second[k]), $"{k} differs between two compiles");
            Assert.False(Directory.Exists(f.Overlay + ".part"));
        }

        /// <summary>No block and no feature ⇒ a stale overlay folder is deleted and nothing is written. 🔴 Red-proof: skip the delete ⇒ the folder stays.</summary>
        [Fact]
        public void NoBlocks_DeletesAStaleOverlay()
        {
            using var f = new OverlayFixture();
            f.Compile(Dom(f));
            Assert.True(File.Exists(Path.Combine(f.Overlay, "overlay.json")));
            var r = f.Compile(f.Scenario());
            Assert.False(r.Wrote);
            Assert.False(Directory.Exists(f.Overlay));
        }

        /// <summary>A block over a terrain the resolver does not know, or a recipe that does not exist, throws naming it, and leaves no half-written folder behind.</summary>
        [Fact]
        public void ABadSite_Throws_AndLeavesNoPartFolder()
        {
            using var f = new OverlayFixture();
            var dom = f.Scenario((BlockGuid, OverlayFixture.Block("nope", 1, 100, 100)));
            Assert.Contains("nope", Assert.Throws<FileNotFoundException>(() => f.Compile(dom)).Message);
            Assert.False(Directory.Exists(f.Overlay + ".part")); Assert.False(Directory.Exists(f.Overlay));
            var unknown = f.Scenario((BlockGuid, OverlayFixture.Block("village", 1, 100, 100)));
            unknown["header"]!["terrainName"] = "elsewhere";
            Assert.Contains("elsewhere", Assert.Throws<FileNotFoundException>(() => f.Compile(unknown)).Message);
        }

        /// <summary>
        /// Navtiles: the overlay's <c>navtiles/</c> holds ONLY tiles the base's seed lacks (no file equal to one of the base's), the seed's tiles are hits, and the counts add up.
        /// 🔴 Red-proof: drop <c>SeedPrefill</c> ⇒ every tile is written (no seed hits), and the overlay then repeats the base's tiles.
        /// </summary>
        [Fact]
        public void TheOverlaysNavtiles_HoldOnlyTilesTheBaseSeedLacks()
        {
            using var f = new OverlayFixture();
            string baseTiles = Path.Combine(f.TerrainFolder, NavTileCache.TerrainFolderName);
            new RecastNavmeshFactory { TileCache = NavTileCache.ForPrefill(baseTiles) }.Build(f.BaseWorld());
            var baseFiles = Directory.GetFiles(baseTiles, "*.navtile", SearchOption.AllDirectories).Select(p => Path.GetRelativePath(baseTiles, p)).ToHashSet();
            Assert.NotEmpty(baseFiles);

            var r = f.Compile(Dom(f), navtiles: true);
            string ovTiles = Path.Combine(f.Overlay, NavTileCache.TerrainFolderName);
            var ovFiles = Directory.Exists(ovTiles) ? Directory.GetFiles(ovTiles, "*.navtile", SearchOption.AllDirectories).Select(p => Path.GetRelativePath(ovTiles, p)).ToList() : new System.Collections.Generic.List<string>();
            _out.WriteLine($"merged world {r.NavTiles} tiles: {r.NavtilesSeedHits} seed hits, {r.NavtilesWritten} baked; base seed {baseFiles.Count} files, overlay folder {ovFiles.Count} files");
            Assert.Equal(r.NavTiles, r.NavtilesSeedHits + r.NavtilesWritten);
            Assert.True(r.NavtilesSeedHits > 0, "the base's tiles are hits");
            Assert.True(r.NavtilesWritten > 0 && ovFiles.Count > 0);
            Assert.True(r.NavtilesWritten < r.NavTiles / 4, $"only the tiles the overlay touches are baked ({r.NavtilesWritten} of {r.NavTiles})");
            Assert.Empty(ovFiles.Where(baseFiles.Contains));
        }

        private static System.Collections.Generic.SortedDictionary<string, byte[]> Snapshot(string dir)
        {
            var d = new System.Collections.Generic.SortedDictionary<string, byte[]>(StringComparer.Ordinal);
            foreach (var p in Directory.GetFiles(dir, "*", SearchOption.AllDirectories)) d[Path.GetRelativePath(dir, p).Replace('\\', '/')] = File.ReadAllBytes(p);
            return d;
        }
    
        /// <summary>
        /// ⭐ <c>CE-4592</c> (P-5, G-16) — a Raise shape on a terrain with a SUMO net: the overlay carries the net RE-ELEVATED over the shaped ground (the caller's hook sees the hill
        /// under the road) and its header names it; a scenario that does not touch the ground writes no net copy. 🔴 Red-proof: skip <c>ChangesTheGround</c> ⇒ no net in the overlay.
        /// </summary>
        [Fact]
        public void CE4592_AShapeOnATerrainWithANet_PutsAReElevatedNetInTheOverlay()
        {
            using var f = new OverlayFixture();
            f.WithSlopeAndNet();
            float sampled = float.NaN; int calls = 0;
            var hill = OverlayFixture.Feature(TerrainFeature.Ground, 200f, 200f, "raise", pad: 5f, seed: 1, points: new[] { (-10f, -10f), (10f, -10f), (10f, 10f), (-10f, 10f) });
            var r = OverlayCompiler.Compile(f.Scenario(("c1000000-0000-4000-8000-000000000003", hill)), f.ScenarioDir, f.Resolve, false, new[] { f.Recipes },
                reElevateNet: (net, ground) => { calls++; sampled = (float)ground(200.0, 200.0); Assert.True(File.Exists(net)); });
            Assert.True(r.Wrote);
            Assert.Equal(1, calls);
            Assert.Equal(0.2f * 200f + 5f, sampled, 2);                                   // the slope + the 5 m raise
            var header = (JsonObject)JsonNode.Parse(File.ReadAllText(Path.Combine(f.Overlay, TerrainOverlayHeader.FileName)))!;
            Assert.Equal("base-t.net.xml", (string?)header["sumoNet"]);
            Assert.True(File.Exists(Path.Combine(f.Overlay, "base-t.net.xml")));

            calls = 0;
            var wall = OverlayFixture.Feature(TerrainFeature.Wall, 100f, 100f, points: new[] { (0f, 0f), (10f, 0f) });
            OverlayCompiler.Compile(f.Scenario(("c1000000-0000-4000-8000-000000000004", wall)), f.ScenarioDir, f.Resolve, false, new[] { f.Recipes },
                reElevateNet: (net, ground) => calls++);
            Assert.Equal(0, calls);
        }
    }
}
