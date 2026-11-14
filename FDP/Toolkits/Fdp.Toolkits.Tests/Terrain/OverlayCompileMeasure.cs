using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Text.Json.Nodes;
using Fdp.Toolkit.Orchestration;
using Xunit;
using Xunit.Abstractions;

namespace Fdp.Toolkit.Terrain.Tests
{
    /// <summary>
    /// ⭐ T-6 (CE-3433) — MEASUREMENT, not a gate (set <c>HROT_MEASURE_COMPILE=1</c>): how long the overlay compile takes — the part of the scenario save the "saved" answer waited for — on test-town (a hamlet + a copse) and on scale-1 (≈ 20 blocks), with and without the navtile bake.
    /// </summary>
    public sealed class OverlayCompileMeasure
    {
        private readonly ITestOutputHelper _out;
        public OverlayCompileMeasure(ITestOutputHelper o) => _out = o;

        private static JsonObject Block(string recipe, uint seed, float x, float y) => new()
        {
            ["TerrainBlock"] = new JsonObject { ["Recipe"] = recipe, ["Seed"] = seed, ["HeadingDeg"] = 0, ["Fit"] = 15 },
            ["SimTransform"] = new JsonObject { ["Position"] = new JsonArray(x, y, 0) },
        };

        private long Time(JsonObject dom, string terrain, bool navtiles)
        {
            string root = ContentLayout.TerrainAssets(ContentLayout.TryFindSourceTree()!);
            var catalog = new TerrainCatalog(new[] { root });
            string dir = Path.Combine(Path.GetTempPath(), "cm-" + Guid.NewGuid().ToString("N")[..8]); Directory.CreateDirectory(dir);
            try
            {
                var sw = Stopwatch.StartNew();
                var r = OverlayCompiler.Compile(dom, dir, catalog.ResolveDefinition, bakeNavtiles: navtiles);
                sw.Stop();
                _out.WriteLine($"{terrain} navtiles={navtiles}: {sw.ElapsedMilliseconds} ms — blocks {r.Blocks}, houses {r.Houses}, woods {r.Woods}, navtiles {r.NavTiles} (written {r.NavtilesWritten}, seed hits {r.NavtilesSeedHits})");
                return sw.ElapsedMilliseconds;
            }
            finally { try { Directory.Delete(dir, true); } catch { } }
        }

        [Fact]
        public void Measure()
        {
            if (Environment.GetEnvironmentVariable("HROT_MEASURE_COMPILE") != "1") return;
            var town = new JsonObject { ["header"] = new JsonObject { ["terrainName"] = "test-town" }, ["entities"] = new JsonObject {
                ["b1000000-0000-4000-8000-000000000001"] = Block("hamlet", 21, 345, 180), ["b1000000-0000-4000-8000-000000000002"] = Block("copse", 4, 275, 200) } };
            Time(town, "test-town", false); Time(town, "test-town", false); Time(town, "test-town", true); Time(town, "test-town", true);

            string root = ContentLayout.TerrainAssets(ContentLayout.TryFindSourceTree()!);
            string def = Path.Combine(root, "scale-1", "terrain.json");
            var world = TerrainFolderWorld.ReadWorld(def, TerrainFolderWorld.ReadDefinition(def), "scale-1")!;
            var ents = new JsonObject();
            string[] recipes = { "hamlet", "farmstead", "village-street", "copse", "hedgerow-fields" };
            for (int i = 0; i < 20; i++)
            {
                float fx = (i % 5 + 0.5f) / 5f, fy = (i / 5 + 0.5f) / 4f;
                ents[$"b2000000-0000-4000-8000-{i + 1:000000000000}"] = Block(recipes[i % recipes.Length], (uint)(100 + i), world.BoundsMin.X + (world.BoundsMax.X - world.BoundsMin.X) * fx, world.BoundsMin.Y + (world.BoundsMax.Y - world.BoundsMin.Y) * fy);
            }
            var big = new JsonObject { ["header"] = new JsonObject { ["terrainName"] = "scale-1" }, ["entities"] = ents };
            _out.WriteLine($"scale-1 bounds {world.BoundsMin} .. {world.BoundsMax}");
            Time(big, "scale-1 x20", false); Time(big, "scale-1 x20", true);
        }
    }
}
