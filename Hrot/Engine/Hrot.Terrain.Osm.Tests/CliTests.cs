using System.Globalization;
using Hrot.Tools.TerrainOsm;
using Xunit;

namespace Hrot.Terrain.Osm.Tests;

public class CliTests
{
    private static string Bbox => string.Create(CultureInfo.InvariantCulture, $"{Fx.Box.South},{Fx.Box.West},{Fx.Box.North},{Fx.Box.East}");

    private static async Task<(int Code, string Out, string Err)> Run(params string[] args)
    {
        var o = new StringWriter(); var e = new StringWriter();
        int code = await OsmCli.RunAsync(args, o, e, default);
        return (code, o.ToString(), e.ToString());
    }

    [Fact]
    public async Task PreviewOnly_RunsOffline_FromAnOsmFile_AndNeedsNoDemNoNetconvert()
    {
        var (code, o, e) = await Run("--bbox", Bbox, "--name", "fixture", "--osm-file", Fx.FixturePath, "--preview-only");
        Assert.Equal(0, code); Assert.Equal("", e);
        Assert.Contains("enterable buildings 11", o);
        Assert.Contains("budget: Ok", o);
        Assert.Contains("© OpenStreetMap contributors · Copernicus DEM GLO-30", o);
        Assert.Contains("SOLID", o);
    }

    [Fact]
    public async Task MissingOrBadArguments_PrintTheUsage_Exit2()
    {
        var r = await Run("--name", "x");
        Assert.Equal(2, r.Code); Assert.Contains("--bbox is required", r.Err); Assert.Contains("usage:", r.Err);
        Assert.Equal(2, (await Run("--bbox", "1,2,3", "--name", "x")).Code);
        Assert.Equal(2, (await Run("--bbox", Bbox, "--name", "x", "--frobnicate")).Code);
        var big = await Run("--bbox", "49.0,15.0,49.1,15.1", "--name", "x");
        Assert.Equal(2, big.Code); Assert.Contains("6000", big.Err);
    }

    [Fact]
    public async Task AMissingOsmFile_FailsWithItsMessage()
    {
        var r = await Run("--bbox", Bbox, "--name", "x", "--osm-file", "/nonexistent/none.osm", "--preview-only");
        Assert.Equal(1, r.Code); Assert.Contains("none.osm", r.Err);
    }

    [Fact]
    public void TheDefaultOutput_IsTerrainInputSlashName()
    {
        var a = OsmCli.Parse(new[] { "--bbox", Bbox, "--name", "alpha" });
        Assert.Equal(Path.Combine("terrain-input", "alpha"), a.Out);
        Assert.False(a.PreviewOnly);
    }
}
