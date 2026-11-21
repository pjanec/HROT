using Hrot.Terrain.InputSet;
using Hrot.Tools.TerrainSynth;
using Xunit;

namespace Hrot.Terrain.InputSet.Tests;

/// <summary>CE-4371 — the shared input-set library: the reader returns what a producer wrote, and the format checks accept a good set and
/// name each kind of broken one. The producer here is hrot-terrain-synth (tiny, generated in the test — nothing committed).</summary>
public sealed class InputSetTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "inputset-tests-" + Guid.NewGuid().ToString("N"));
    public void Dispose() { try { Directory.Delete(_root, true); } catch (IOException) { } }

    private string Synth(params string[] extra)
    {
        string dir = Path.Combine(_root, Guid.NewGuid().ToString("N"));
        var args = new[] { "--preset", "tiny", "--seed", "3", "--height-spacing", "4", "--out", dir }.Concat(extra).ToArray();
        Assert.Equal(0, TerrainSynthesizer.Run(SynthOptions.Parse(args), new StringWriter()));
        return dir;
    }

    [Fact]
    public void TheReader_ReturnsWhatTheProducerWrote()
    {
        string dir = Synth("--extras", "--terraces", "on", "--materials", "on");
        var set = InputSetReader.Open(dir);
        Assert.Equal(1, set.Manifest.SchemaVersion);
        Assert.Equal("synth-tiny-3", set.Manifest.Name);
        Assert.Equal(32, set.Manifest.Frame.Zone);
        Assert.False(set.Manifest.Frame.South);
        var b = set.ReadBuildings();
        Assert.NotEmpty(b);
        Assert.All(b, x => Assert.Equal(x.Footprint[0], x.Footprint[^1]));
        Assert.Contains(b, x => x.Material != null);
        Assert.Contains(b, x => x.Roof == "pitched" && x.RidgeZ != null);
        Assert.Equal(set.Manifest.Files[InputSetLayout.Buildings].Bytes, new FileInfo(set.PathOf(InputSetLayout.Buildings)).Length);
        Assert.NotEmpty(set.ReadAreas(InputSetLayout.Woods));   // with --extras the woods are polygons (trees.csv only outside them)
        Assert.All(set.ReadAreas(InputSetLayout.Woods), w => Assert.True(w.Density > 0));
        Assert.NotEmpty(set.ReadAreas(InputSetLayout.Water));
        Assert.Empty(set.ReadWalls());

        var (info, z) = HeightsGrid.ReadAll(dir);
        Assert.Equal((long)info.Cols * info.Rows, z.LongLength);
        // ReadAll is SOUTH row first; the file is NORTH row first — the first file row lands in the last array row
        float[]? firstFileRow = null;
        HeightsGrid.ReadRows(dir, (r, row) => { if (r == 0) firstFileRow = (float[])row.Clone(); });
        Assert.Equal(firstFileRow, z.AsSpan((info.Rows - 1) * info.Cols, info.Cols).ToArray());
    }

    [Theory]
    [InlineData(null)]                         // no frame in heights.json (§10 does not list it)
    [InlineData("\"local metres (a note)\"")]  // a producer's text note under the same key
    public void HeightsJson_WithoutAFrameObject_TakesTheManifestFrame(string? frameValue)
    {
        string dir = Synth();
        string path = Path.Combine(dir, InputSetLayout.HeightsJson);
        var node = System.Text.Json.Nodes.JsonNode.Parse(File.ReadAllText(path))!.AsObject();
        node.Remove("frame");
        if (frameValue != null) node["frame"] = System.Text.Json.Nodes.JsonNode.Parse(frameValue);
        File.WriteAllText(path, node.ToJsonString());

        var info = HeightsGrid.ReadInfo(dir);
        Assert.Equal(InputSetReader.Open(dir).Manifest.Frame, info.Frame);
    }

    [Fact]
    public void AnOmittedOptionalFile_ReadsAsEmpty()
    {
        var set = InputSetReader.Open(Synth());
        Assert.NotEmpty(set.ReadTrees());   // without woods polygons every tree is a point
        Assert.Empty(set.ReadAreas(InputSetLayout.Woods));
        Assert.Empty(set.ReadAreas(InputSetLayout.Water));
    }

    [Theory]
    [InlineData()]
    [InlineData("--noise", "real", "--terraces", "on", "--materials", "on", "--extras")]
    public void GoodSets_PassTheChecks(params string[] options)
        => Assert.Empty(InputSetChecks.Check(Synth(options)));

    [Fact]
    public void AChangedFile_FailsItsHash()
    {
        string dir = Synth();
        File.AppendAllText(Path.Combine(dir, InputSetLayout.Trees), "1.00,2.00,3.00\n");
        Assert.Contains(InputSetChecks.Check(dir), p => p.Contains("trees/trees.csv") && p.Contains("differs"));
    }

    [Fact]
    public void AMissingRequiredFile_IsNamed()
    {
        string dir = Synth();
        File.Delete(Path.Combine(dir, InputSetLayout.Unassigned));
        Assert.Contains("missing buildings/unassigned.geojson", InputSetChecks.Check(dir));
    }

    [Fact]
    public void AClockwiseFootprint_AndADuplicateId_AreNamed()
    {
        string dir = Synth();
        var set = InputSetReader.Open(dir);
        var b = set.ReadBuildings();
        b[0] = b[0] with { Footprint = b[0].Footprint.Reverse().ToList() };
        b[1] = b[1] with { Id = b[2].Id };
        GeoJsonWriter.WriteBuildings(set.PathOf(InputSetLayout.Buildings), b);
        var problems = InputSetChecks.Check(dir);
        Assert.Contains(problems, p => p.Contains("not counter-clockwise"));
        Assert.Contains(problems, p => p.Contains("duplicate id"));
    }

    [Fact]
    public void ATruncatedHeightGrid_IsNamed()
    {
        string dir = Synth();
        string f32 = Path.Combine(dir, InputSetLayout.HeightsF32);
        using (var fs = new FileStream(f32, FileMode.Open)) fs.SetLength(fs.Length - 4);
        Assert.Contains(InputSetChecks.Check(dir), p => p.StartsWith("heights:") && p.Contains("bytes"));
    }

    [Fact]
    public void TheHeightsWriter_RefusesTooFewRows()
    {
        string dir = Path.Combine(_root, "h");
        var w = new HeightsWriter(dir, new HeightsInfo(0, 0, 1, 3, 2, new UtmFrame(32, false, 0, 0)));
        w.WriteRow(new float[] { 1, 2, 3 });
        Assert.Throws<InvalidOperationException>(() => w.Dispose());
    }

    [Fact]
    public void TheHeightsWriter_RoundTrips_WithSourceResolution()
    {
        string dir = Path.Combine(_root, "h2");
        var info = new HeightsInfo(-10, 20, 1, 3, 2, new UtmFrame(33, true, 500000, 5000000)) { SourceResolution = 30 };
        using (var w = new HeightsWriter(Path.Combine(dir, "heights"), info))
        {
            w.WriteRow(new float[] { 5, 6, 7 });          // north
            w.WriteRow(new float[] { 1, HeightsInfo.NoData, 3 }); // south
        }
        var (back, z) = HeightsGrid.ReadAll(dir);
        Assert.Equal(info, back);
        Assert.Equal(new float[] { 1, HeightsInfo.NoData, 3, 5, 6, 7 }, z);
    }

    [Fact]
    public void Netconvert_FindsTheRepoInstall_OfTheExpectedVersion()
    {
        var found = Netconvert.Locate();
        if (found.Path == null) return;   // not installed here (scripts/install-sumo.*); the synth tests cover the "not found" message
        Assert.Equal(Netconvert.ExpectedVersion(), found.Version);
        Assert.Null(found.Problem);
    }
}
