using System;
using System.IO;
using System.Text.Json.Nodes;
using Fdp.Toolkit.Orchestration;
using Fdp.Toolkit.Terrain;
using Hrot.Editor.TerrainSave;
using Hrot.UI.Common.TerrainSave;
using Xunit;

namespace Hrot.Editor.Tests.TerrainSave;

/// <summary>
/// ⭐ Terrain authoring U-5 / E-2 (docs/DESIGN_Terrain_Authoring.md §14; mockup 4) — <i>Edit terrain…</i> on a throw-away content tree: a terrain saved from a source scenario keeps its source (E-1) and offers it; <c>Edit</c> stages a copy under
/// <c>opened/terrain/&lt;terrain&gt;</c> and opens THAT by name; the title says TERRAIN SOURCE; <i>Save terrain</i> bakes the staged source back over the same terrain (the overwrite names its users first). A terrain with no source, or whose ground is gone, is greyed with its reason.
/// 🔴 Red-proof (compiles): <c>Edit</c> not staging (opening the terrain's own name) ⇒ the staged-copy cell red.
/// </summary>
public sealed class EditorTerrainSourceServiceTests : IDisposable
{
    private const string Hut = """{"footprint":[[0,0],[10,0],[10,8],[0,8]],"storeys":[{"height":3,"walls":[{"from":[0,0],"to":[10,0],"openings":[{"kind":"door","at":4.4,"width":1.2,"doorId":"front"}]},{"from":[10,0],"to":[10,8]},{"from":[10,8],"to":[0,8]},{"from":[0,8],"to":[0,0]}]}]}""";
    private readonly string _root = Path.Combine(Path.GetTempPath(), "etsrc-" + Guid.NewGuid().ToString("N")[..10]);

    public EditorTerrainSourceServiceTests()
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
            },
        };
        File.WriteAllText(Path.Combine(scen, "scenario.json"), dom.ToJsonString());
        new EditorTerrainSaveService(() => "s1", _root).Save("town-1", prebuildMesh: false, scenarioName: null);   // a terrain that keeps its source
    }

    public void Dispose() { try { Directory.Delete(_root, true); } catch { } }

    private TerrainCatalog Catalog() => new(new[] { ContentLayout.TerrainAssets(_root) });

    [Fact]
    public void E2_ATerrainThatKeepsItsSourceOffersIt_EditStagesACopyUnderOpened_AndOpensItByName()
    {
        string? opened = null;
        var svc = new EditorTerrainSourceService(Catalog, n => opened = n, _root);
        var offer = svc.Offer("town-1");
        Assert.True(offer.HasSource);
        Assert.Null(offer.EditDisabledReason);

        string name = svc.Edit("town-1");
        Assert.Equal("opened/terrain/town-1", name);
        Assert.Equal(name, opened);
        Assert.Equal("town-1", TerrainSourceNames.TerrainOf(name));
        Assert.Null(TerrainSourceNames.TerrainOf("s1"));
        string staged = ContentLayout.ResolveScenarioDirectory(_root, name);
        Assert.Contains("TerrainBlock", File.ReadAllText(Path.Combine(staged, "scenario.json")));   // the block is there to edit
        Assert.StartsWith("TERRAIN SOURCE · town-1", Hrot.Common.Scenarios.ScenarioWindowTitle.Detail(name, "base-t")!);   // the title says so (the mockup's chip)
        Assert.Equal("s1", Hrot.Common.Scenarios.ScenarioWindowTitle.Detail("s1", null));

        // the staged source is the save's source: Save terrain bakes it back over the SAME terrain (the dialog's name is the terrain's, so it asks first)
        var save = new EditorTerrainSaveService(() => name, _root);
        Assert.Equal(name, save.SourceScenarioName);
        Assert.Equal("base-t", save.SourceTerrainName);
        var model = new TerrainSaveAsModel(save);
        Assert.Equal("town-1", model.Name);
        Assert.Equal(1, save.Preview("town-1").Blocks);
        Assert.True(save.Exists("town-1"));
        var outcome = save.Save("town-1", prebuildMesh: false, scenarioName: null);
        Assert.True(File.Exists(Path.Combine(outcome.TerrainFolder, "terrain.json")));
    }

    [Fact]
    public void E2_ATerrainWithNoSource_OrWhoseGroundIsGone_IsGreyedWithItsReason()
    {
        var svc = new EditorTerrainSourceService(Catalog, _ => { }, _root);
        var plain = svc.Offer("base-t");   // the ground terrain keeps no source
        Assert.False(plain.HasSource);
        Assert.Equal(EditorTerrainSourceService.NoSourceReason, plain.EditDisabledReason);
        Assert.Throws<InvalidOperationException>(() => svc.Edit("base-t"));
        Assert.False(svc.Offer("not-a-terrain").HasSource);   // an unknown terrain has none either

        Directory.Delete(Path.Combine(ContentLayout.TerrainAssets(_root), "base-t"), true);   // the ground the source is built on is gone
        var gone = svc.Offer("town-1");
        Assert.True(gone.HasSource);
        Assert.Contains("no longer in the catalogue", gone.EditDisabledReason);
        Assert.Contains("NOTE_Ui_To_Backend_Edit_Terrain", gone.EditDisabledReason);
    }
}
