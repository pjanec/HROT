using System.Globalization;
using System.Text.Json;
using Hrot.Tools.TerrainSynth;
using Xunit;

namespace Hrot.Tools.TerrainSynth.Tests;

public class HeightFieldTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "terrain-synth-tests", Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        try { Directory.Delete(_dir, true); } catch (IOException) { }
    }

    private static SynthProfile Tiny(double spacing) =>
        SynthOptions.Parse(new[] { "--preset", "tiny", "--height-spacing", spacing.ToString(CultureInfo.InvariantCulture) }).Resolve();

    private HeightField WriteTiny(double spacing, out JsonElement json)
    {
        var hf = new HeightField(Tiny(spacing), new Rng(1));
        hf.Write(_dir);
        json = JsonDocument.Parse(File.ReadAllText(Path.Combine(_dir, "heights.json"))).RootElement.Clone();
        return hf;
    }

    [Fact]
    public void Write_SizesAgree()
    {
        var hf = WriteTiny(50, out var json);
        int cols = json.GetProperty("cols").GetInt32(), rows = json.GetProperty("rows").GetInt32();
        Assert.Equal(hf.Cols, cols);
        Assert.Equal(hf.Rows, rows);
        Assert.Equal((long)cols * rows * 4, new FileInfo(Path.Combine(_dir, "heights.f32")).Length);
        var hdr = File.ReadAllLines(Path.Combine(_dir, "heights.hdr")).Select(l => l.Split(' ')).ToDictionary(a => a[0], a => a[1]);
        Assert.Equal(rows.ToString(CultureInfo.InvariantCulture), hdr["NROWS"]);
        Assert.Equal(cols.ToString(CultureInfo.InvariantCulture), hdr["NCOLS"]);
        Assert.Equal((cols * 4).ToString(CultureInfo.InvariantCulture), hdr["BANDROWBYTES"]);
        Assert.Equal("-9999", hdr["NODATA"]);
    }

    [Fact]
    public void Values_InRange()
    {
        var hf = WriteTiny(4, out var json);
        var bytes = File.ReadAllBytes(Path.Combine(_dir, "heights.f32"));
        int valid = 0;
        for (int o = 0; o < bytes.Length; o += 4)
        {
            float v = BitConverter.ToSingle(bytes, o);
            if (v == -9999f) continue;
            Assert.InRange(v, 450f, 980f);
            valid++;
        }
        Assert.True(valid > 0);
        Assert.InRange(json.GetProperty("minZ").GetDouble(), 450, 980);
        Assert.InRange(json.GetProperty("maxZ").GetDouble(), 450, 980);
        Assert.Equal((long)hf.Cols * hf.Rows, valid + json.GetProperty("nodataCells").GetInt64());
    }

    [Fact]
    public void NoData_PatchPresent()
    {
        var hf = WriteTiny(4, out var json);
        Assert.True(json.GetProperty("nodataCells").GetInt64() > 0);
        Assert.True(float.IsNaN(hf.Sample(110, 110)));
        Assert.True(hf.IsNoData(110, 110));
        Assert.False(hf.IsNoData(0, 0));
        Assert.False(float.IsNaN(hf.Sample(0, 0)));
        // next to the patch a sample is a real height, never NaN or the nodata value
        Assert.InRange(hf.Sample(98, 110), 450f, 980f);
    }

    [Fact]
    public void RowOrder_NorthFirst()
    {
        var hf = WriteTiny(50, out _);
        var bytes = File.ReadAllBytes(Path.Combine(_dir, "heights.f32"));
        double yNorth = hf.OriginY + (hf.Rows - 1) * hf.Spacing;
        for (int i = 0; i < hf.Cols; i++)
            Assert.Equal(hf.Sample(hf.OriginX + i * hf.Spacing, yNorth), BitConverter.ToSingle(bytes, i * 4));
        int last = (hf.Rows - 1) * hf.Cols * 4;
        for (int i = 0; i < hf.Cols; i++)
            Assert.Equal(hf.Sample(hf.OriginX + i * hf.Spacing, hf.OriginY), BitConverter.ToSingle(bytes, last + i * 4));
    }

    [Fact]
    public void Write_IsDeterministic()
    {
        var a = Path.Combine(_dir, "a");
        var b = Path.Combine(_dir, "b");
        new HeightField(Tiny(8), new Rng(5)).Write(a);
        new HeightField(Tiny(8), new Rng(5)).Write(b);
        foreach (var f in new[] { "heights.f32", "heights.hdr", "heights.json" })
            Assert.Equal(File.ReadAllBytes(Path.Combine(a, f)), File.ReadAllBytes(Path.Combine(b, f)));
    }

    [Fact]
    public void Grid_FullProfileGeometry()
    {
        var hf = new HeightField(SynthProfile.Full, new Rng(1));
        Assert.Equal(11265, hf.Cols);
        Assert.Equal(11265, hf.Rows);
    }

    [Fact]
    public void Spread_IsNotDegenerate()
    {
        // coarse sampling of the whole full-size area, no grid written
        var hf = new HeightField(SynthProfile.Full, new Rng(1));
        var vals = new List<float>();
        for (double y = -2700; y < 2750; y += 50)
            for (double x = -2700; x < 2750; x += 50)
            {
                float v = hf.Sample(x, y);
                if (!float.IsNaN(v)) vals.Add(v);
            }
        vals.Sort();
        float p10 = vals[vals.Count / 10], p90 = vals[vals.Count * 9 / 10];
        Assert.True(p10 < 520, $"p10 {p10}");
        Assert.True(p90 > 650, $"p90 {p90}");
        Assert.True(vals[^1] > 850, $"max {vals[^1]}");
    }

    [Fact]
    public void Sample_IsPure()
    {
        var a = new HeightField(SynthProfile.Tiny, new Rng(3));
        var b = new HeightField(SynthProfile.Tiny, new Rng(3));
        Assert.Equal(a.Sample(12.34, -56.78), b.Sample(12.34, -56.78));
        Assert.Equal(a.Sample(12.34, -56.78), a.Sample(12.34, -56.78));
    }
}
