using System.Globalization;
using System.Security.Cryptography;
using Hrot.Tools.TerrainSynth;
using Xunit;

namespace Hrot.Tools.TerrainSynth.Tests;

// The clean set must not change when the realism options are off: these hashes were recorded from the tool before any option existed.
public class CleanPinTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "terrain-synth-tests", "p" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        try { Directory.Delete(_root, true); } catch (IOException) { }
    }

    private static string Sha(string path) => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))).ToLowerInvariant();

    private string Run(params string[] args)
    {
        string dir = Path.Combine(_root, Guid.NewGuid().ToString("N"));
        var o = SynthOptions.Parse(args.Concat(new[] { "--out", dir }).ToArray());
        Assert.Equal(0, TerrainSynthesizer.Run(o, new StringWriter()));
        return dir;
    }

    [Fact]
    public void Tiny_Seed1_DefaultsOff_MatchesRecordedHashes()
    {
        string dir = Run("--preset", "tiny", "--seed", "1", "--height-spacing", "4");
        var expected = new Dictionary<string, string>
        {
            ["buildings/buildings.geojson"] = "a8cfa63c57b5766e95356f4d7306e138ce523f2457683a3c56928d48cfba5ba9",
            ["buildings/unassigned.geojson"] = "2638d83d51e63c65c37a20d995859ee82e2d3b6de96b0ddbacd81c7f0786484e",
            ["heights/heights.f32"] = "d9b850e4e2c09033f5d168d9ae67c65ebee60d13a1eaa260f58b75cfae458653",
            ["heights/heights.hdr"] = "785e9099738af9130ecbcff65b3ab86d1b6367d297b866240a9e59dc882dd607",
            ["heights/heights.json"] = "014320e6b8629a75d674196d4708b927253ae15d21d4daf32fdb4b3adf9e9a36",
            ["roads/roads.edg.xml"] = "29c5a848d7981c96d987561d65c8ee666bd21b10d9be17bbb938b6a2247bfbdc",
            ["roads/roads.nod.xml"] = "14050ed6750159e2a7c2fc55d3c6bc1608e6b384079514c20f81e265f42e6a9a",
            ["roads/roads.typ.xml"] = "37b193a05467ec985dc7070cc3ef97e1175ca6441a2322c98869c66f8af273f2",
            ["trees/trees.csv"] = "60de5f08a530ff64809c8432b50e31b1e62a6fecc7dc172ac7e176c270b866a9",
            ["walls/walls.geojson"] = "7a34043ac25b9bcf033d23afce2f766c036f200d84f000a60808595654fbfba3",
        };
        foreach (var (file, sha) in expected)
            Assert.True(sha == Sha(Path.Combine(dir, file)), $"{file} changed");
        Assert.DoesNotContain("noise", File.ReadAllText(Path.Combine(dir, "manifest.json")));
    }

    [Fact]
    public void Full_Seed1_OptionsOff_MatchesRecordedHashes()
    {
        string dir = Run("--preset", "full", "--seed", "1", "--height-spacing", "8", "--noise", "none");
        var expected = new Dictionary<string, string>
        {
            ["buildings/buildings.geojson"] = "9ed54f0e12caf5b0799b10dc97370e92e668cb8621fd1161997748d62788c049",
            ["buildings/unassigned.geojson"] = "692dd60f87393affec276e879c2820e22d6bd2f65e0ba9b75a4f8b89c54ad07d",
            ["trees/trees.csv"] = "98ea50a3f8a4d765d6c85a3a5bad4e770fe3e1e69ae4bec014332c79f0923e4c",
            ["roads/roads.nod.xml"] = "06600a6956a77192d0be1e77151d474769d1ac9e32cf07bc1147fd7c83e723a8",
            ["roads/roads.edg.xml"] = "5a97dbbaf020287c2016d7d3091d46ce5a865b4ee12cf6023225887735204297",
        };
        foreach (var (file, sha) in expected)
            Assert.True(sha == Sha(Path.Combine(dir, file)), $"{file} changed");
    }

    [Fact]
    public void Tiny_NoiseReal_ChangesOnlyTheOpenings_AndIsRecorded()
    {
        string clean = Run("--preset", "tiny", "--seed", "1", "--height-spacing", "4");
        string noisy = Run("--preset", "tiny", "--seed", "1", "--height-spacing", "4", "--noise", "real");
        foreach (var f in new[] { "heights/heights.f32", "roads/roads.nod.xml", "roads/roads.edg.xml", "buildings/unassigned.geojson", "trees/trees.csv" })
            Assert.Equal(Sha(Path.Combine(clean, f)), Sha(Path.Combine(noisy, f)));
        Assert.NotEqual(Sha(Path.Combine(clean, "buildings/buildings.geojson")), Sha(Path.Combine(noisy, "buildings/buildings.geojson")));
        string manifest = File.ReadAllText(Path.Combine(noisy, "manifest.json"));
        Assert.Contains("\"noise\": \"real\"", manifest);
    }
}
