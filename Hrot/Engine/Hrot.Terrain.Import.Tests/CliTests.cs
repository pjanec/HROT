using Hrot.Tools.TerrainImport;
using Hrot.Tools.TerrainOsm;
using Xunit;

namespace Hrot.Terrain.Import.Tests;

public class CliTests : IDisposable
{
    private readonly Env _env = new();
    public void Dispose() => _env.Dispose();

    private static async Task<(int Code, string Out, string Err)> Run(params string[] a)
    {
        var o = new StringWriter(); var e = new StringWriter();
        int code = await ImportCli.RunAsync(a, o, e, default);
        return (code, o.ToString(), e.ToString());
    }

    [Fact]
    public async Task TheImportCli_PrintsCountsAndSecondsPerStage()
    {
        var mini = new MiniSet(); mini.Buildings.Add(MiniSet.Rect("b", 0, 0, 10, 8, storeys: 2));
        string set = mini.Write(Path.Combine(_env.Root, "set"));
        var r = await Run("--set", set, "--name", "cli", "--terrains", _env.Terrains, "--scenarios", _env.Scenarios);
        Assert.Equal(0, r.Code); Assert.Equal("", r.Err);
        Assert.Contains("buildings: 1 enterable", r.Out); Assert.Contains("seconds per stage:", r.Out);
        Assert.True(File.Exists(Path.Combine(_env.Terrains, "cli", "terrain.json")));
        var again = await Run("--set", set, "--name", "cli", "--terrains", _env.Terrains);
        Assert.Equal(1, again.Code); Assert.Contains("already exists", again.Err);
        Assert.Equal(0, (await Run("--set", set, "--name", "cli", "--replace", "--terrains", _env.Terrains)).Code);
    }

    [Fact]
    public async Task BadArguments_PrintTheUsage_Exit2()
    {
        var r = await Run("--name", "x");
        Assert.Equal(2, r.Code); Assert.Contains("--set is required", r.Err); Assert.Contains("usage:", r.Err);
        Assert.Equal(2, (await Run("--set", "a", "--name", "b", "--frob")).Code);
    }

    [Fact]
    public void TheOsmCli_KnowsTheImportFlag()
    {
        var a = OsmCli.Parse(new[] { "--bbox", "49,15,49.01,15.01", "--name", "t", "--import", "--replace", "--terrains", "/x" });
        Assert.True(a.Import); Assert.True(a.Replace); Assert.Equal("/x", a.Terrains);
        Assert.Throws<ArgumentException>(() => OsmCli.Parse(new[] { "--bbox", "49,15,49.01,15.01", "--name", "t", "--import", "--preview-only" }));
    }
}
