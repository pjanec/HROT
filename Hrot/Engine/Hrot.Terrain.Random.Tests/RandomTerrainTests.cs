using System.Security.Cryptography;
using Hrot.Terrain.Import;
using Hrot.Terrain.InputSet;
using Hrot.Terrain.Random;
using Xunit;

namespace Hrot.Terrain.Random.Tests;

/// <summary>A throw-away <c>assets</c> tree: the repo's shipped recipes and building templates copied beside an empty <c>terrain</c> folder, so the pipeline finds them exactly as it does for a real terrain.</summary>
public sealed class Env : IDisposable
{
    public string Root { get; } = Path.Combine(Path.GetTempPath(), "random-terrain-tests-" + Guid.NewGuid().ToString("N")[..10]);
    public string Assets => Path.Combine(Root, "assets");
    public string Terrains => Path.Combine(Assets, "terrain");
    public string Scenarios => Path.Combine(Root, "scenarios");

    public Env()
    {
        Directory.CreateDirectory(Terrains); Directory.CreateDirectory(Scenarios);
        string repo = Netconvert.RepoRoot() ?? throw new InvalidOperationException("HROT.sln was not found");
        foreach (var sub in new[] { "buildings", "terrain-recipes" })
        {
            Directory.CreateDirectory(Path.Combine(Assets, sub));
            foreach (var f in Directory.EnumerateFiles(Path.Combine(repo, "assets", sub), "*.json")) File.Copy(f, Path.Combine(Assets, sub, Path.GetFileName(f)));
        }
    }

    public RandomTerrainPipeline Pipeline() => new(Terrains);
    public void Dispose() { try { Directory.Delete(Root, true); } catch (IOException) { } catch (UnauthorizedAccessException) { } }

    /// <summary>Every file of a folder: relative path → SHA-256 (so two input sets compare byte for byte).</summary>
    public static SortedDictionary<string, string> Hashes(string folder)
    {
        var d = new SortedDictionary<string, string>(StringComparer.Ordinal);
        foreach (var f in Directory.EnumerateFiles(folder, "*", SearchOption.AllDirectories))
            d[Path.GetRelativePath(folder, f).Replace('\\', '/')] = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(f)));
        return d;
    }
}

/// <summary>
/// ⭐⭐ CE-4428 (docs/DESIGN_Terrain_Authoring.md §18f) — the random-terrain engine: the same options are byte-identical ground files and an identical layout; <i>Shuffle layout</i> keeps the ground and changes the layout;
/// plain terrain has no blocks. Fast (an 800 m terrain). The 3 km budget rail is <see cref="TownBudgetTests"/>.
/// </summary>
public sealed class RandomTerrainTests : IDisposable
{
    private readonly Env _env = new();
    public void Dispose() => _env.Dispose();

    private static RandomTerrainOptions Villages(ulong ground = 11, ulong layout = 5) => new()
    {
        Preset = TerrainPreset.Villages, SizeMetres = 1500, Relief = ReliefLevel.Rolling, Vegetation = VegetationLevel.Light, Water = WaterLevel.Ponds, GroundSeed = ground, LayoutSeed = layout,
    };

    private SortedDictionary<string, string> Ground(RandomTerrainPipeline p, RandomTerrainPreview preview, string name)
    {
        string folder = Path.Combine(_env.Root, name);
        p.WriteGroundInputSet(preview, folder);
        return Env.Hashes(folder);
    }

    /// <summary>
    /// (a) the SAME options give BYTE-IDENTICAL ground input-set files and an identical layout DOM. 🔴 Red-proof: derive a block's guid from <c>Guid.NewGuid()</c> ⇒ the DOMs differ; mix the clock into the ground seed
    /// (<c>Preview</c>) ⇒ the files differ.
    /// </summary>
    [Fact]
    public void TheSameOptions_GiveByteIdenticalGroundFiles_AndAnIdenticalLayout()
    {
        var pipe = _env.Pipeline();
        var a = pipe.Preview(Villages()); var b = pipe.Preview(Villages());
        Assert.NotEmpty(a.Layout);
        var ha = Ground(pipe, a, "ground-a"); var hb = Ground(pipe, b, "ground-b");
        Assert.Contains("heights/heights.f32", ha.Keys);
        Assert.Contains("vegetation/woods.geojson", ha.Keys);
        Assert.Equal(ha, hb);
        Assert.Equal(BlockLayout.Serialize(a.ScenarioDom("t")), BlockLayout.Serialize(b.ScenarioDom("t")));
        Assert.Equal(a.Layout, b.Layout);
        Assert.Equal(a.Preview.Counts, b.Preview.Counts);
    }

    /// <summary>
    /// (b) <i>Shuffle layout</i> keeps the ground BYTE-IDENTICAL and changes the layout (and a shuffle back is the first layout). 🔴 Red-proof: have <c>BlockLayout.Place</c> ignore the seed ⇒ the layout is unchanged; have
    /// <c>ShuffleLayout</c> take the new seed as the GROUND seed ⇒ the ground files differ.
    /// </summary>
    [Fact]
    public void ShuffleLayout_KeepsTheGroundByteIdentical_AndChangesTheLayout()
    {
        var pipe = _env.Pipeline();
        var first = pipe.Preview(Villages(layout: 5));
        var shuffled = pipe.ShuffleLayout(first, 99);

        Assert.Equal(Ground(pipe, first, "g1"), Ground(pipe, shuffled, "g2"));
        Assert.NotEqual(BlockLayout.Serialize(first.ScenarioDom("t")), BlockLayout.Serialize(shuffled.ScenarioDom("t")));
        Assert.NotEqual(first.Layout.Select(b => (b.X, b.Y)), shuffled.Layout.Select(b => (b.X, b.Y)));   // the PLACES differ, not just the guids
        Assert.NotEmpty(shuffled.Layout);
        Assert.Equal(99UL, shuffled.Options.LayoutSeed);
        Assert.Equal(BlockLayout.Serialize(first.ScenarioDom("t")), BlockLayout.Serialize(pipe.ShuffleLayout(shuffled, 5).ScenarioDom("t")));
        Assert.Equal(first.Preview.Roads.Count, shuffled.Preview.Roads.Count);   // the same roads: the ground
    }

    /// <summary>(c) Plain terrain is GROUND ONLY: no blocks, no houses — and the other presets do place blocks.</summary>
    [Fact]
    public void PlainTerrain_HasNoBlocks_AndTheOtherPresetsDo()
    {
        var pipe = _env.Pipeline();
        var plain = pipe.Preview(Villages() with { Preset = TerrainPreset.Plain });
        Assert.Empty(plain.Layout);
        Assert.Equal(0, plain.Preview.Counts.Buildings);
        Assert.Empty(plain.Preview.Buildings);
        Assert.Empty(plain.ScenarioDom("t")["entities"]!.AsObject());

        foreach (var preset in new[] { TerrainPreset.OpenCountryside, TerrainPreset.Villages, TerrainPreset.SmallTown, TerrainPreset.TownWithSuburbs, TerrainPreset.Mixed })
        {
            var p = pipe.Preview(Villages() with { Preset = preset, SizeMetres = 1500 });
            Assert.True(p.Layout.Count > 0, $"{preset} placed no block");
        }
    }

    /// <summary>The blocks keep clear: off the roads (the disc's edge never on a carriageway), off water, and off each other; every guid is derived, never random, and the recipes are the shipped ones.</summary>
    [Fact]
    public void TheBlocksKeepOffRoadsWaterAndEachOther()
    {
        var pipe = _env.Pipeline();
        var p = pipe.Preview(Villages(ground: 7) with { Water = WaterLevel.River, SizeMetres = 1000 });
        Assert.NotEmpty(p.Layout);
        var g = p.Ground;
        foreach (var b in p.Layout)
        {
            Assert.True(g.Roads.DistanceToRoad(b.X, b.Y) >= BlockLayout.Clearance(b.Radius), $"block {b.Guid} sits on a road");
            Assert.False(g.Water!.Near(b.X, b.Y), $"block {b.Guid} sits in water");
            Assert.Contains(b.Recipe, new[] { "hamlet", "farmstead", "copse" });
            Assert.Equal(BlockLayout.GuidOf(p.Options.LayoutSeed, p.Layout.ToList().IndexOf(b)), b.Guid);
        }
        for (int i = 0; i < p.Layout.Count; i++)
            for (int j = i + 1; j < p.Layout.Count; j++)
            {
                var a = p.Layout[i]; var c = p.Layout[j];
                double d = Math.Sqrt((a.X - c.X) * (a.X - c.X) + (a.Y - c.Y) * (a.Y - c.Y));
                Assert.True(d >= 0.8 * (a.Radius + c.Radius) - 1e-6, $"blocks {i} and {j} overlap");
            }
    }

    /// <summary>The preview is the OSM wizard's own records: roads, water / woods / block-outline areas, the houses as buildings, counts from the blocks' expansion and the budget's verdict.</summary>
    [Fact]
    public void ThePreview_IsTheOsmWizardsRecords()
    {
        var pipe = _env.Pipeline();
        var p = pipe.Preview(Villages());
        var o = p.Preview;
        Assert.NotEmpty(o.Roads);
        Assert.Contains(o.Areas, a => a.Kind == "block");
        Assert.Contains(o.Areas, a => a.Kind == "woods");
        Assert.NotEmpty(o.Buildings);
        Assert.Equal(o.Buildings.Count(b => b.Enterable), o.Counts.Buildings);
        Assert.Equal(Hrot.Terrain.Osm.BudgetVerdict.Ok, o.Verdict);
        Assert.Equal(new[] { 0.0, 0.0, 1500.0, 1500.0 }, o.Bounds);
        Assert.All(o.Buildings, b => Assert.All(b.Footprint, v => Assert.InRange(v.X, -50f, 1550f)));   // inside the terrain's local metres
        Assert.Equal(40.0, p.ReliefRange.Max - p.ReliefRange.Min, 3);
        Assert.Equal(16 * 16, p.ReliefGrid(16).Length);
    }

    /// <summary>The ground option mapping: no vegetation = no trees, woods or scrub; relief scales the height range; the water modes differ.</summary>
    [Fact]
    public void TheOptionsMapOntoTheGround()
    {
        var pipe = _env.Pipeline();
        var bare = pipe.Preview(Villages() with { Vegetation = VegetationLevel.None, Water = WaterLevel.None, Relief = ReliefLevel.Flat });
        Assert.Empty(bare.Ground.Trees.Trees); Assert.Empty(bare.Ground.Trees.Woods); Assert.Empty(bare.Ground.Scrub);
        Assert.Empty(bare.Ground.Water!.Features);
        Assert.Equal(5.0, bare.ReliefRange.Max - bare.ReliefRange.Min, 3);
        var wet = pipe.Preview(Villages() with { Water = WaterLevel.River, Vegetation = VegetationLevel.Heavy, Relief = ReliefLevel.Hilly });
        Assert.Contains(wet.Ground.Water!.Features, f => f.Kind == "river");
        Assert.DoesNotContain(wet.Ground.Water.Features, f => f.Kind == "pond");
        Assert.NotEmpty(wet.Ground.Trees.Woods);
        Assert.Equal(150.0, wet.ReliefRange.Max - wet.ReliefRange.Min, 3);
        Assert.Empty(wet.Ground.Buildings);   // ground only: no buildings, no village houses
    }

    /// <summary>OpenInEditor writes the terrain (the importer) and the scenario (header on the terrain, the TerrainBlock entities); a second call with the same name is refused, never overwriting.</summary>
    [Fact]
    public async Task OpenInEditor_ImportsTheGround_AndWritesTheScenario()
    {
        var pipe = _env.Pipeline();
        var p = pipe.Preview(Villages() with { SizeMetres = 600 });
        var r = await pipe.OpenInEditorAsync(p, "rnd-t", _env.Scenarios, _env.Terrains);

        Assert.True(File.Exists(Path.Combine(r.TerrainFolder, "terrain.json")));
        var dom = System.Text.Json.Nodes.JsonNode.Parse(File.ReadAllText(r.ScenarioFile))!.AsObject();
        Assert.Equal("rnd-t", (string?)dom["header"]!["terrainName"]);
        Assert.Equal(p.Layout.Count, dom["entities"]!.AsObject().Count);
        Assert.All(dom["entities"]!.AsObject(), kv => Assert.NotNull(kv.Value!["TerrainBlock"]));
        await Assert.ThrowsAsync<InvalidOperationException>(() => pipe.OpenInEditorAsync(p, "rnd-t", _env.Scenarios, _env.Terrains));
    }
}
