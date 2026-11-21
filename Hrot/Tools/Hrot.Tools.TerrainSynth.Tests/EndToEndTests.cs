using System.Diagnostics;
using System.Globalization;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;
using Hrot.Tools.TerrainSynth;
using Xunit;

namespace Hrot.Tools.TerrainSynth.Tests;

// one class: some tests change the process culture, and the run is not cheap
public class EndToEndTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "terrain-synth-tests", "e" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        try { Directory.Delete(_root, true); } catch (IOException) { }
    }

    private string RunTiny(string name, ulong seed, string spacing, out string logText)
    {
        string dir = Path.Combine(_root, name);
        var log = new StringWriter();
        var o = SynthOptions.Parse(new[] { "--preset", "tiny", "--seed", seed.ToString(CultureInfo.InvariantCulture), "--out", dir, "--height-spacing", spacing });
        Assert.Equal(0, TerrainSynthesizer.Run(o, log));
        logText = log.ToString();
        return dir;
    }

    private static List<string> Files(string dir) =>
        Directory.EnumerateFiles(dir, "*", SearchOption.AllDirectories).Select(f => Path.GetRelativePath(dir, f).Replace('\\', '/')).OrderBy(f => f, StringComparer.Ordinal).ToList();

    private static string Sha(string path) => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))).ToLowerInvariant();

    [Fact]
    public void Tiny_WritesAllFiles_HashesMatch()
    {
        string dir = RunTiny("a", 1, "2", out var log);
        var files = Files(dir);
        foreach (var f in new[]
        {
            "manifest.json", "heights/heights.f32", "heights/heights.hdr", "heights/heights.json",
            "buildings/buildings.geojson", "buildings/unassigned.geojson", "trees/trees.csv",
            "roads/roads.nod.xml", "roads/roads.edg.xml", "roads/roads.typ.xml", "walls/walls.geojson",
        })
            Assert.Contains(f, files);
        Assert.Equal(SumoWriter.FindNetconvert() != null, files.Contains("roads/roads.net.xml"));

        var m = JsonDocument.Parse(File.ReadAllText(Path.Combine(dir, "manifest.json"))).RootElement;
        Assert.Equal(1, m.GetProperty("schemaVersion").GetInt32());
        Assert.Equal("synth-tiny-1", m.GetProperty("name").GetString());
        var utm = m.GetProperty("frame").GetProperty("utm");
        Assert.Equal(32, utm.GetProperty("zone").GetInt32());
        Assert.Equal("N", utm.GetProperty("hemisphere").GetString());
        Assert.Equal(500000.0, utm.GetProperty("e0").GetDouble());
        Assert.Equal(5600000.0, utm.GetProperty("n0").GetDouble());
        Assert.Contains("x east, y north, z up", m.GetProperty("axes").GetString());
        Assert.Equal(new[] { -250.0, -250.0, 250.0, 250.0 }, m.GetProperty("bounds").EnumerateArray().Select(x => x.GetDouble()));
        Assert.Equal(new[] { 0.0, 0.0 }, m.GetProperty("sumoAnchor").EnumerateArray().Select(x => x.GetDouble()));
        var gen = m.GetProperty("generator");
        Assert.Equal("hrot-terrain-synth", gen.GetProperty("name").GetString());
        Assert.Equal("tiny", gen.GetProperty("preset").GetString());
        Assert.Equal(1, gen.GetProperty("seed").GetInt32());
        Assert.Equal(new[] { "heightSpacing" }, gen.GetProperty("overrides").EnumerateObject().Select(x => x.Name));
        Assert.DoesNotContain("prepared", File.ReadAllText(Path.Combine(dir, "manifest.json")));

        var listed = m.GetProperty("files").EnumerateObject().ToList();
        Assert.Equal(files.Where(f => f != "manifest.json").ToList(), listed.Select(x => x.Name).ToList());
        foreach (var e in listed)
        {
            string path = Path.Combine(dir, e.Name);
            Assert.Equal(Sha(path), e.Value.GetProperty("sha256").GetString());
            Assert.Equal(new FileInfo(path).Length, e.Value.GetProperty("bytes").GetInt64());
        }
        var hf = listed.Single(x => x.Name == "heights/heights.f32").Value;
        Assert.Equal(new FileInfo(Path.Combine(dir, "heights/heights.f32")).Length, hf.GetProperty("rows").GetInt64() * hf.GetProperty("cols").GetInt64() * 4);
        var bf = listed.Single(x => x.Name == "buildings/buildings.geojson").Value;
        Assert.Equal(JsonDocument.Parse(File.ReadAllText(Path.Combine(dir, "buildings/buildings.geojson"))).RootElement.GetProperty("features").GetArrayLength(), bf.GetProperty("features").GetInt32());
        Assert.Equal(0, listed.Single(x => x.Name == "walls/walls.geojson").Value.GetProperty("features").GetInt32());
        Assert.Equal(File.ReadAllLines(Path.Combine(dir, "trees/trees.csv")).Length - 1, listed.Single(x => x.Name == "trees/trees.csv").Value.GetProperty("rows").GetInt32());

        Assert.Contains("heights", log);
        Assert.Contains("trees", log);
    }

    [Fact]
    public void SameArgs_ByteIdentical()
    {
        string a = RunTiny("a", 3, "4", out _);
        string b = RunTiny("b", 3, "4", out _);
        var fa = Files(a);
        Assert.Equal(fa, Files(b));
        foreach (var f in fa)
            Assert.True(File.ReadAllBytes(Path.Combine(a, f)).AsSpan().SequenceEqual(File.ReadAllBytes(Path.Combine(b, f))), $"{f} differs");
    }

    [Fact]
    public void DifferentSeed_Differs()
    {
        string a = RunTiny("a", 1, "8", out _);
        string b = RunTiny("b", 2, "8", out _);
        Assert.NotEqual(File.ReadAllText(Path.Combine(a, "buildings/buildings.geojson")), File.ReadAllText(Path.Combine(b, "buildings/buildings.geojson")));
        Assert.NotEqual(Sha(Path.Combine(a, "heights/heights.f32")), Sha(Path.Combine(b, "heights/heights.f32")));
    }

    [Fact]
    public void DecimalCommaCulture_StillWritesDots()
    {
        var old = CultureInfo.CurrentCulture;
        var oldUi = CultureInfo.CurrentUICulture;
        string dir;
        try
        {
            CultureInfo.CurrentCulture = new CultureInfo("cs-CZ");
            CultureInfo.CurrentUICulture = new CultureInfo("cs-CZ");
            dir = RunTiny("cz", 1, "8", out _);
        }
        finally
        {
            CultureInfo.CurrentCulture = old;
            CultureInfo.CurrentUICulture = oldUi;
        }
        // a decimal comma would break the JSON, and shows up as digit-comma-digit after a colon in the indented file
        string heights = File.ReadAllText(Path.Combine(dir, "heights/heights.json"));
        JsonDocument.Parse(heights);
        Assert.DoesNotMatch(@":\s*-?\d+,\d", heights);
        Assert.Contains("\"cellSize\": 8", heights);
        foreach (var f in new[] { "buildings/buildings.geojson", "buildings/unassigned.geojson", "manifest.json" })
            JsonDocument.Parse(File.ReadAllText(Path.Combine(dir, f)));
        foreach (var line in File.ReadAllLines(Path.Combine(dir, "trees/trees.csv")).Skip(1))
            Assert.Matches(@"^-?\d+\.\d\d,-?\d+\.\d\d,\d+\.\d\d$", line);
        Assert.DoesNotContain(",", File.ReadAllText(Path.Combine(dir, "heights/heights.hdr")));
        Assert.DoesNotContain(",", File.ReadAllText(Path.Combine(dir, "roads/roads.nod.xml")));
    }

    [Fact]
    public void Tiny_RunsUnderTenSeconds()
    {
        var sw = Stopwatch.StartNew();
        RunTiny("t", 1, "0.48828125", out _);
        sw.Stop();
        Assert.True(sw.Elapsed.TotalSeconds < 10, $"took {sw.Elapsed.TotalSeconds:F1} s");
    }

    [Fact]
    public void Run_LogsOneLinePerStage()
    {
        RunTiny("l", 1, "8", out var log);
        var lines = log.Split('\n', StringSplitOptions.RemoveEmptyEntries).Select(l => l.TrimEnd('\r')).ToList();
        foreach (var stage in new[] { "heights", "roads", "buildings", "trees", "walls", "manifest" })
            Assert.Single(lines, l => l.StartsWith(stage + ":", StringComparison.Ordinal));
        Assert.All(lines.Where(l => !l.StartsWith("WARNING")), l => Assert.Matches(@"\d+\.\d\d s", l));
    }
}
