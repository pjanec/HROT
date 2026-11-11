using System;
using System.IO;
using System.Numerics;
using System.Text.Json.Nodes;
using Fdp.Toolkit.Navigation.Recast;

namespace Fdp.Toolkit.Terrain.Tests
{
    /// <summary>
    /// ⭐ O-4 (CE-4415) — a throw-away assets tree: <c>assets/terrain/base-t</c> (a 400 × 400 m base with one inline house "B" at (100,100) and a lake east of it), <c>assets/buildings/hut</c> (a 10 × 8 m house
    /// with a door) and <c>assets/terrain-recipes/{village,woodlot}</c>; plus helpers to author a scenario DOM with a block / a feature.
    /// </summary>
    internal sealed class OverlayFixture : IDisposable
    {
        public const string Hut = """{"footprint":[[0,0],[10,0],[10,8],[0,8]],"storeys":[{"height":3,"walls":[{"from":[0,0],"to":[10,0],"openings":[{"kind":"door","at":4.4,"width":1.2,"doorId":"front"}]},{"from":[10,0],"to":[10,8]},{"from":[10,8],"to":[0,8]},{"from":[0,8],"to":[0,0]}]}]}""";

        public readonly string Root = Path.Combine(Path.GetTempPath(), "ovf-" + Guid.NewGuid().ToString("N")[..10]);
        public string Assets => Path.Combine(Root, "assets");
        public string TerrainFolder => Path.Combine(Assets, "terrain", "base-t");
        public string Definition => Path.Combine(TerrainFolder, "terrain.json");
        public string ScenarioDir => Path.Combine(Root, "scenarios", "s1");
        public string Overlay => Path.Combine(ScenarioDir, OverlayCompiler.FolderName);
        public string Recipes => Path.Combine(Assets, "terrain-recipes");

        public OverlayFixture()
        {
            Directory.CreateDirectory(TerrainFolder); Directory.CreateDirectory(Path.Combine(Assets, "buildings")); Directory.CreateDirectory(Recipes); Directory.CreateDirectory(ScenarioDir);
            File.WriteAllText(Definition, """{"schemaVersion":2,"name":"base-t","world":"w.geojson"}""");
            File.WriteAllText(Path.Combine(TerrainFolder, "w.geojson"), """
                {"type":"FeatureCollection","hrot":{"schemaVersion":1,"bounds":[0,0,400,400],"groundZ":0},"features":[
                  {"type":"Feature","properties":{"kind":"building","label":"B","building":HUT},"geometry":{"type":"Point","coordinates":[100,100]}},
                  {"type":"Feature","properties":{"kind":"surface","surface":"water"},"geometry":{"type":"Polygon","coordinates":[[[125,70],[175,70],[175,130],[125,130],[125,70]]]}}]}
                """.Replace("HUT", Hut));
            File.WriteAllText(Path.Combine(Assets, "buildings", "hut.building.json"), Hut);
            File.WriteAllText(Path.Combine(Recipes, "village.recipe.json"), """{"id":"village","houses":{"templates":["hut"],"count":[10,10],"radius":60,"minSpacing":4},"woods":{"count":[2,2],"size":[8,12]}}""");
            File.WriteAllText(Path.Combine(Recipes, "woodlot.recipe.json"), """{"id":"woodlot","woods":{"count":[3,3],"size":[8,12]}}""");
        }

        public TerrainRecipe Recipe(string id) => TerrainRecipe.Find(id, new[] { Recipes });
        public TerrainWorld BaseWorld() => TerrainFolderWorld.ReadWorld(Definition, TerrainFolderWorld.ReadDefinition(Definition), "base-t")!;
        public string? Resolve(string name) => name == "base-t" ? Definition : null;

        public static JsonObject Block(string recipe, uint seed, float x, float y, float heading = 0f, int fit = 15) => new()
        {
            ["TerrainBlock"] = new JsonObject { ["Recipe"] = recipe, ["Seed"] = seed, ["HeadingDeg"] = heading, ["Fit"] = fit },
            ["SimTransform"] = new JsonObject { ["Position"] = new JsonArray(x, y, 0), ["Rotation"] = new JsonArray(0, 0, 0, 1) },
        };

        public static JsonObject House(string template, float x, float y, float heading = 0f) => new()
        {
            ["TerrainFeature"] = new JsonObject { ["Kind"] = 1, ["Template"] = template, ["HeadingDeg"] = heading, ["Pad"] = 0 },
            ["SimTransform"] = new JsonObject { ["Position"] = new JsonArray(x, y, 0), ["Rotation"] = new JsonArray(0, 0, 0, 1) },
        };

        public JsonObject Scenario(params (string Guid, JsonObject Entity)[] entities)
        {
            var e = new JsonObject();
            foreach (var (g, o) in entities) e[g] = o;
            e["f0000000-0000-4000-8000-000000000001"] = new JsonObject { ["SimTransform"] = new JsonObject { ["Position"] = new JsonArray(5, 5, 0) } };   // an ordinary unit: not a terrain entity
            return new JsonObject { ["header"] = new JsonObject { ["terrainName"] = "base-t" }, ["entities"] = e };
        }

        public OverlayCompileResult Compile(JsonObject dom, bool navtiles = false) => OverlayCompiler.Compile(dom, ScenarioDir, Resolve, navtiles, new[] { Recipes });

        public void Dispose() { try { Directory.Delete(Root, true); } catch { } }
    }
}
