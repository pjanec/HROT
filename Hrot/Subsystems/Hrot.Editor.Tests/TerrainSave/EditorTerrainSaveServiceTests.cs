using System;
using System.IO;
using System.Linq;
using System.Text.Json.Nodes;
using Fdp.Toolkit.Terrain;
using Hrot.Editor.TerrainSave;
using Xunit;

namespace Hrot.Editor.Tests.TerrainSave;

/// <summary>
/// ⭐ Terrain authoring U-5 (docs/DESIGN_Terrain_Authoring.md §13 W-3/W-4, §18d) — the editor's save service over backend's <c>TerrainBake.SaveAs</c>, on a throw-away content tree: a source scenario with ONE block is saved as a terrain; the summary the dialog
/// shows is the one the save makes; the new scenario is written on the new terrain (empty of the blocks, no units) and the new terrain, loaded, HAS the block's houses. ✅ Red-proof (compiling mutation): <c>Save</c> not writing the scenario ⇒ the scenario cell red.
/// </summary>
public sealed class EditorTerrainSaveServiceTests : IDisposable
{
    private const string Hut = """{"footprint":[[0,0],[10,0],[10,8],[0,8]],"storeys":[{"height":3,"walls":[{"from":[0,0],"to":[10,0],"openings":[{"kind":"door","at":4.4,"width":1.2,"doorId":"front"}]},{"from":[10,0],"to":[10,8]},{"from":[10,8],"to":[0,8]},{"from":[0,8],"to":[0,0]}]}]}""";
    private readonly string _root = Path.Combine(Path.GetTempPath(), "etss-" + Guid.NewGuid().ToString("N")[..10]);

    public EditorTerrainSaveServiceTests()
    {
        string assets = Path.Combine(_root, "assets"), terrain = Path.Combine(assets, "terrain", "base-t");
        Directory.CreateDirectory(terrain); Directory.CreateDirectory(Path.Combine(assets, "buildings")); Directory.CreateDirectory(Path.Combine(assets, "terrain-recipes"));
        File.WriteAllText(Path.Combine(terrain, "terrain.json"), """{"schemaVersion":2,"name":"base-t","world":"w.geojson"}""");
        File.WriteAllText(Path.Combine(terrain, "w.geojson"), """{"type":"FeatureCollection","hrot":{"schemaVersion":1,"bounds":[0,0,400,400],"groundZ":0},"features":[]}""");
        File.WriteAllText(Path.Combine(assets, "buildings", "hut.building.json"), Hut);
        File.WriteAllText(Path.Combine(assets, "terrain-recipes", "village.recipe.json"), """{"id":"village","houses":{"templates":["hut"],"count":[6,6],"radius":60,"minSpacing":4},"woods":{"count":[1,1],"size":[8,12]}}""");
        string scen = Path.Combine(_root, "scenarios", "s1");
        Directory.CreateDirectory(scen);
        var dom = new JsonObject
        {
            ["header"] = new JsonObject { ["terrainName"] = "base-t" },
            ["entities"] = new JsonObject
            {
                ["b1000000-0000-4000-8000-000000000001"] = new JsonObject
                {
                    ["TerrainBlock"] = new JsonObject { ["Recipe"] = "village", ["Seed"] = 7, ["HeadingDeg"] = 0, ["Fit"] = 15 },
                    ["SimTransform"] = new JsonObject { ["Position"] = new JsonArray(200, 200, 0) },
                },
                ["f0000000-0000-4000-8000-000000000001"] = new JsonObject { ["SimTransform"] = new JsonObject { ["Position"] = new JsonArray(5, 5, 0) } },   // a unit: not terrain
            },
        };
        File.WriteAllText(Path.Combine(scen, "scenario.json"), dom.ToJsonString());
    }

    public void Dispose() { try { Directory.Delete(_root, true); } catch { } }

    [Fact]
    public void U5_ASourceScenarioIsSavedAsATerrain_TheSummaryIsWhatTheSaveMakes_AndTheNewScenarioIsOnIt()
    {
        var svc = new EditorTerrainSaveService(() => "s1", _root);
        Assert.Equal("s1", svc.SourceScenarioName);
        Assert.Equal("base-t", svc.SourceTerrainName);
        Assert.Null(new EditorTerrainSaveService(() => "no-such", _root).SourceScenarioName);   // nothing to save from: the menu item greys

        var preview = svc.Preview("town-1");
        Assert.Equal(1, preview.Blocks);
        Assert.True(preview.Houses >= 3, $"the block placed {preview.Houses} houses");
        Assert.Equal(1, preview.UnitsNotBaked);   // the unit stays in the source (E-4)
        Assert.False(svc.Exists("town-1"), "the preview left nothing behind");

        var outcome = svc.Save("town-1", prebuildMesh: false, scenarioName: "town-1-mission");
        Assert.True(svc.Exists("town-1"));
        Assert.True(File.Exists(Path.Combine(outcome.TerrainFolder, "terrain.json")));
        Assert.True(Directory.Exists(Path.Combine(outcome.TerrainFolder, "source")), "the source travels with the terrain (E-1)");

        // the new scenario: on the new terrain, no blocks, no units (E-5)
        var scenario = JsonNode.Parse(File.ReadAllText(Path.Combine(_root, "scenarios", "town-1-mission", "scenario.json")))!.AsObject();
        Assert.Equal("town-1", (string?)scenario["header"]!["terrainName"] ?? (string?)scenario["Header"]?["TerrainName"]);
        Assert.DoesNotContain("TerrainBlock", scenario.ToJsonString());

        // the new terrain, loaded, HAS the block's houses (found by label)
        var def = Path.Combine(outcome.TerrainFolder, "terrain.json");
        var world = TerrainFolderWorld.ReadWorld(def, TerrainFolderWorld.ReadDefinition(def), "town-1")!;
        Assert.Equal(preview.Houses, world.Buildings.Count);

        Assert.Contains("town-1-mission", svc.UsersOf("town-1"));
        Assert.Throws<IOException>(() => svc.Save("town-2", false, "town-1-mission"));   // a scenario of that name exists: refused, nothing half-written
        Assert.False(svc.Exists("town-2"));
    }
}
