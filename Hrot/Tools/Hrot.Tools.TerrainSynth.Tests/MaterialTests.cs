using System.Globalization;
using System.Text.Json;
using Hrot.Tools.TerrainSynth;
using Xunit;

namespace Hrot.Tools.TerrainSynth.Tests;

public class MaterialTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "terrain-synth-tests", "m" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        try { Directory.Delete(_dir, true); } catch (IOException) { }
    }

    private static IReadOnlyList<Building> Make(string preset, double spacing, ulong seed, bool materials)
    {
        var p = SynthOptions.Parse(new[] { "--preset", preset, "--height-spacing", spacing.ToString(CultureInfo.InvariantCulture) }).Resolve();
        var h = new HeightField(p, new Rng(seed));
        var r = RoadNetwork.Generate(p, new Rng(seed));
        return BuildingGenerator.Generate(p, new Rng(seed), h, r, new BuildingOptions { Materials = materials }).Buildings;
    }

    private static readonly Lazy<IReadOnlyList<Building>> Full = new(() => Make("full", 8, 1, true));

    [Fact]
    public void AboutThirtyPercentHaveAMaterial_WithTheWantedMix()
    {
        var b = Full.Value;
        var with = b.Where(x => x.Material != null).ToList();
        double share = with.Count / (double)b.Count;
        Assert.InRange(share, 0.27, 0.33);
        Assert.All(with, x => Assert.Contains(x.Material, new[] { "brick", "concrete", "stone", "wood" }));
        double Of(string m) => with.Count(x => x.Material == m) / (double)with.Count;
        Assert.InRange(Of("brick"), 0.44, 0.56);
        Assert.InRange(Of("concrete"), 0.20, 0.30);
        Assert.InRange(Of("stone"), 0.10, 0.20);
        Assert.InRange(Of("wood"), 0.05, 0.15);
        Console.WriteLine($"MATERIAL withMaterial={share:F3} brick={Of("brick"):F3} concrete={Of("concrete"):F3} stone={Of("stone"):F3} wood={Of("wood"):F3}");
    }

    [Fact]
    public void MaterialsOff_WritesNone_AndOnChangesNothingElse()
    {
        var off = Make("tiny", 4, 1, false);
        Assert.All(off, x => Assert.Null(x.Material));
        var on = Make("tiny", 4, 1, true);
        Assert.Equal(off.Select(x => x.Id), on.Select(x => x.Id));
        Assert.Equal(off.SelectMany(x => x.Openings), on.SelectMany(x => x.Openings));
        Assert.Equal(off.SelectMany(x => x.StoreyZ), on.SelectMany(x => x.StoreyZ));
        Assert.Contains(on, x => x.Material != null);
    }

    [Fact]
    public void Materials_AreDeterministic()
    {
        Assert.Equal(Make("tiny", 4, 3, true).Select(x => x.Material), Make("tiny", 4, 3, true).Select(x => x.Material));
        Assert.NotEqual(Make("full", 8, 1, true).Select(x => x.Material), Make("full", 8, 2, true).Select(x => x.Material));
    }

    [Fact]
    public void GeoJson_PutsMaterialAfterRidgeZAndBeforeStoreyZ_OnlyWhenSet()
    {
        var b = Make("tiny", 4, 1, true);
        Directory.CreateDirectory(_dir);
        string path = Path.Combine(_dir, "buildings.geojson");
        GeoJsonWriter.WriteBuildings(path, b);
        var feats = JsonDocument.Parse(File.ReadAllText(path)).RootElement.GetProperty("features").EnumerateArray().ToList();
        for (int i = 0; i < feats.Count; i++)
        {
            var names = feats[i].GetProperty("properties").EnumerateObject().Select(x => x.Name).ToList();
            var expect = new List<string> { "id", "baseZ", "height", "roof" };
            if (b[i].Roof == "pitched") expect.Add("ridgeZ");
            if (b[i].Material != null) expect.Add("material");
            expect.Add("storeyZ");
            expect.Add("openings");
            Assert.Equal(expect, names);
            if (b[i].Material != null) Assert.Equal(b[i].Material, feats[i].GetProperty("properties").GetProperty("material").GetString());
        }
        Assert.Contains(feats, f => f.GetProperty("properties").TryGetProperty("material", out _));
        Assert.Contains(feats, f => !f.GetProperty("properties").TryGetProperty("material", out _));
    }

    [Fact]
    public void Options_MaterialsDefaultByPreset_AndAreValidated()
    {
        Assert.True(SynthOptions.Parse(new[] { "--preset", "full" }).MaterialsEnabled);
        Assert.False(SynthOptions.Parse(new[] { "--preset", "tiny" }).MaterialsEnabled);
        Assert.False(SynthOptions.Parse(new[] { "--preset", "full", "--materials", "off" }).MaterialsEnabled);
        Assert.True(SynthOptions.Parse(new[] { "--preset", "tiny", "--materials", "on" }).MaterialsEnabled);
        Assert.Throws<ArgumentException>(() => SynthOptions.Parse(new[] { "--materials", "some" }));
    }
}
