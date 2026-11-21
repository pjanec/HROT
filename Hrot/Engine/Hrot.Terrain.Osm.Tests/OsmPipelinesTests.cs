using Hrot.Terrain.InputSet;
using Xunit;
using Hrot.Terrain.Osm;

namespace Hrot.Terrain.Osm.Tests;

/// <summary>
/// ⭐ CE-4375 (ui) — <see cref="OsmPipelines.ForAreas"/>, the ONE composition the CLI and the editor's wizard share: the road builder is made for EACH area
/// (netconvert keeps edges inside that area's bounds), Preview then Write of one box reuse one pipeline and fetch the map once, a second box gets its own.
/// 🔴 Red-proof (compiles): drop the <c>_last</c> reuse in <c>For</c> ⇒ the factory runs twice for one box.
/// </summary>
public sealed class OsmPipelinesTests
{
    private static readonly OsmImportOptions Opt = new() { Name = "t" };

    [Fact]
    public async Task ForAreas_MakesTheRoadBuilderPerArea_AndPreviewThenWriteShareOneDownload()
    {
        using var tmp = new TempDir();
        var src = new CountingSource(new FileOsmSource(Fx.FixturePath));
        var made = new List<AreaBounds>();
        var p = OsmPipelines.ForAreas(src, FuncDem.Flat(), new DownloadCache(tmp.Combine("cache")), roads: b => { made.Add(b); return new TestRoads(); });

        Assert.Equal(AreaFrame.MaxSideMetres, p.MaxSideMetres);
        Assert.Null(p.Measure(Fx.Box).Refusal);
        await p.PreviewAsync(Fx.Box, Opt, null, default);
        await p.WriteInputSetAsync(Fx.Box, Opt, tmp.Combine("out"), null, default);
        Assert.Equal(1, src.Fetches);                                      // one download for Preview + Write
        var only = Assert.Single(made);                                    // one road builder for the one box …
        Assert.Equal(AreaFrame.FromBox(Fx.Box), only);                     // … bound to THAT box's local bounds

        var other = new GeoBox(Fx.Box.South, Fx.Box.West, Fx.Box.North - 0.001, Fx.Box.East - 0.001);
        await p.PreviewAsync(other, Opt, null, default);
        Assert.Equal(2, made.Count);                                       // a second box ⇒ its own builder
        Assert.Equal(AreaFrame.FromBox(other), made[1]);
    }
}
