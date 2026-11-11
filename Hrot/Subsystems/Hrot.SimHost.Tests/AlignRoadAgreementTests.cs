using System;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Text.Json.Nodes;
using CarKinem.Road;
using Fdp.Core;
using Fdp.Toolkit.Scenario;
using Fdp.Toolkit.Terrain;
using Hrot.Map.Common.Services;
using Xunit;

namespace Hrot.SimHost.Tests
{
    /// <summary>
    /// ⭐⭐ T-5 (CE-3431, docs/DESIGN_Terrain_Authoring.md §18h H-5) — AlignRoad on the TWO callers of the one expansion: the scenario-save <c>OverlayCompiler</c> and the editor's <c>LiveOverlay</c> both read the terrain folder's road
    /// network (<see cref="TerrainRoads"/>), so a farmstead placed near the track turns to face it THE SAME WAY in both — the same parts, the same boxes — and the houses stand on the track's direction (a quarter turn aside).
    /// 🔴 Red-proof: hand the live overlay no roads (<c>LiveOverlay.Make</c> passes <c>null</c>) ⇒ its houses keep the placed heading ⇒ the boxes differ ⇒ red; the same for the compiler.
    /// </summary>
    public sealed class AlignRoadAgreementTests : IDisposable
    {
        private const string Hut = """{"footprint":[[0,0],[10,0],[10,8],[0,8]],"storeys":[{"height":3,"walls":[{"from":[0,0],"to":[10,0],"openings":[{"kind":"door","at":4.4,"width":1.2,"doorId":"front"}]},{"from":[10,0],"to":[10,8]},{"from":[10,8],"to":[0,8]},{"from":[0,8],"to":[0,0]}]}]}""";
        private const float TrackDeg = 30f;
        private static readonly Guid BlockGuid = Guid.Parse("11111111-2222-4333-8444-555555555555");

        private readonly EntityRepository _world = new();
        private readonly string _staging = Path.Combine(Path.GetTempPath(), "t5-" + Guid.NewGuid().ToString("N")[..10]);
        private readonly TerrainResidency _residency;

        public AlignRoadAgreementTests()
        {
            SimHostComponentRegistry.RegisterAll(_world);
            string terrain = Path.Combine(_staging, "Terrain", "base-t");
            Directory.CreateDirectory(Path.Combine(terrain, "buildings")); Directory.CreateDirectory(Path.Combine(_staging, "terrain-recipes"));
            File.WriteAllText(Path.Combine(terrain, "terrain.json"), """{"schemaVersion":2,"name":"base-t","world":"w.geojson","roadNetworks":["roads.json"]}""");
            File.WriteAllText(Path.Combine(terrain, "w.geojson"), """{"type":"FeatureCollection","hrot":{"schemaVersion":1,"bounds":[0,0,400,400],"groundZ":0},"features":[]}""");
            File.WriteAllText(Path.Combine(terrain, "buildings", "hut.building.json"), Hut);
            File.WriteAllText(Path.Combine(_staging, "terrain-recipes", "farmstead.recipe.json"), """{"id":"farmstead","houses":{"templates":["hut"],"count":[3,3],"radius":45,"minSpacing":12},"woods":{"count":[1,1],"size":[10,14]}}""");
            // one straight track at TrackDeg through (200, 210): the block at (200, 200) is 8.7 m from it
            float r = TrackDeg * MathF.PI / 180f; var d = new Vector2(MathF.Cos(r), MathF.Sin(r));
            var a = new Vector2(200, 210) - d * 100f; var b = new Vector2(200, 210) + d * 100f; var t = b - a;
            JsonObject P(Vector2 v) => new() { ["x"] = v.X, ["y"] = v.Y };
            var roads = new JsonObject
            {
                ["nodes"] = new JsonArray(new JsonObject { ["id"] = 0, ["position"] = P(a) }, new JsonObject { ["id"] = 1, ["position"] = P(b) }),
                ["segments"] = new JsonArray(new JsonObject { ["id"] = 0, ["startNodeId"] = 0, ["endNodeId"] = 1, ["controlPoints"] = new JsonObject { ["p0"] = P(a), ["t0"] = P(t), ["p1"] = P(b), ["t1"] = P(t) }, ["speedLimit"] = 10, ["laneWidth"] = 3.5, ["laneCount"] = 1 }),
                ["metadata"] = new JsonObject { ["worldBounds"] = new JsonObject { ["min"] = P(Vector2.Zero), ["max"] = P(new Vector2(400, 400)) }, ["gridCellSize"] = 5 },
            };
            File.WriteAllText(Path.Combine(terrain, "roads.json"), roads.ToJsonString());
            _residency = new TerrainResidency(_staging, new RoadNetworkHolder());
            _residency.Commit(_world, _residency.Prepare("base-t"));
            var e = _world.CreateEntity();
            _world.AddComponent(e, new SimTransform { Position = new Vector3(200, 200, 0), Rotation = Quaternion.Identity });
            _world.AddComponent(e, new TerrainBlock { Recipe = new FixedString32("farmstead"), Seed = 5, HeadingDeg = 0, Fit = TerrainBlock.FitAll });
            _world.AddComponent(e, new EntityGuid(BlockGuid));
        }

        public void Dispose()
        {
            _world.Dispose();
            try { Directory.Delete(_staging, recursive: true); } catch { }
        }

        private static float OffAxis(float rotation) { float d = (((rotation - TrackDeg) % 90f) + 90f) % 90f; return MathF.Min(d, 90f - d); }

        [Fact]
        public void T5_TheCompilerAndTheLiveOverlay_TurnAFarmsteadToTheTrackTheSameWay()
        {
            // the compiler: the block as the scenario JSON holds it
            var dom = new JsonObject
            {
                ["header"] = new JsonObject { ["terrainName"] = "base-t" },
                ["entities"] = new JsonObject
                {
                    [BlockGuid.ToString("D")] = new JsonObject
                    {
                        ["TerrainBlock"] = new JsonObject { ["Recipe"] = "farmstead", ["Seed"] = 5, ["HeadingDeg"] = 0, ["Fit"] = 15 },
                        ["SimTransform"] = new JsonObject { ["Position"] = new JsonArray(200, 200, 0), ["Rotation"] = new JsonArray(0, 0, 0, 1) },
                    },
                },
            };
            string scenarioDir = Path.Combine(_staging, "scenario-x"); Directory.CreateDirectory(scenarioDir);
            string definition = Path.Combine(_staging, "Terrain", "base-t", "terrain.json");
            var compiled = OverlayCompiler.Compile(dom, scenarioDir, name => name == "base-t" ? definition : null, bakeNavtiles: false, recipeRoots: new[] { Path.Combine(_staging, "terrain-recipes") });
            Assert.True(compiled.Wrote);
            var file = JsonNode.Parse(File.ReadAllText(Path.Combine(compiled.Folder!, "overlay.world.geojson")))!["features"]!.AsArray().Select(f => (JsonObject)f!).ToList();
            var houses = file.Where(f => (string?)((JsonObject)f["properties"]!)["kind"] == "building").ToList();
            Assert.Equal(3, houses.Count);
            Assert.All(houses, h => { float rot = (float)((JsonObject)h["properties"]!)["rotation"]!.GetValue<double>(); Assert.True(OffAxis(rot) < 1f, $"the compiled house rotation {rot} is not on the track's {TrackDeg}°"); });

            // the live overlay: the same block as components
            var live = new LiveOverlay();
            var (@base, version, folder) = _residency.ExpansionBase();
            var result = live.Expand(live.Snapshot(_world), @base, version, folder)!;

            var fromCompiler = file.Select(f => (Label: OverlayExpansion.Label(f), Box: OverlayExpansion.BoundsOf(f))).OrderBy(t => t.Label, StringComparer.Ordinal).ToList();
            var fromLive = result.Parts.OrderBy(p => p.Key, StringComparer.Ordinal).ToList();
            Assert.Equal(fromCompiler.Select(t => t.Label), fromLive.Select(p => p.Key));
            for (int i = 0; i < fromLive.Count; i++)
            {
                Assert.Equal(fromCompiler[i].Box.Min.X, fromLive[i].Min.X, 2); Assert.Equal(fromCompiler[i].Box.Min.Y, fromLive[i].Min.Y, 2);
                Assert.Equal(fromCompiler[i].Box.Max.X, fromLive[i].Max.X, 2); Assert.Equal(fromCompiler[i].Box.Max.Y, fromLive[i].Max.Y, 2);
            }
        }
    }
}
