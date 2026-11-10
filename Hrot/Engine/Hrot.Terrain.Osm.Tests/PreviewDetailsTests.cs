using Xunit;
using Xunit.Abstractions;

namespace Hrot.Terrain.Osm.Tests;

/// <summary>
/// ⭐ CE-4382 (E-2) — the preview carries what the rules already knew: every tree point, per building its type / storeys source / roof (+ its tag) / doors, windows and the
/// door rule / the footprints it shares a wall with; per note its severity and the box round what it is about; the OSM data timestamp. On the synthetic town.
/// 🔴 Red-proof (compiles): <c>SharedWith</c> not filled in <c>FindParties</c> ⇒ the shared-wall rail reds.
/// </summary>
public class PreviewDetailsTests
{
    private readonly ITestOutputHelper _out;
    public PreviewDetailsTests(ITestOutputHelper output) => _out = output;

    private static async Task<OsmPreview> Preview()
    {
        using var tmp = new TempDir();
        return await Fx.Pipeline(FuncDem.EastSlope(), new TestRoads(), new DownloadCache(tmp.Combine("cache"))).PreviewAsync(Fx.Box, Fx.Options, null, default);
    }

    [Fact]
    public async Task TheTrees_AndTheDataTimestamp()
    {
        var p = await Preview();
        Assert.Equal(p.Counts.LoneTrees, p.Trees.Count);   // lone + row trees, one point each
        Assert.All(p.Trees, t => Assert.InRange(t.X, (float)p.Bounds[0], (float)p.Bounds[2]));
        Assert.Equal("2026-01-01T00:00:00Z", p.DataTimestamp);
    }

    [Fact]
    public async Task EachBuilding_SaysItsType_WhereItsStoreysAndRoofCameFrom_ItsOpenings_AndItsDoorRule()
    {
        var p = await Preview();
        foreach (var b in p.Buildings) _out.WriteLine($"{b.Id} {b.Type} {b.Storeys} ({b.StoreysSource}) {b.Roof} ({b.RoofShape ?? "-"}, {b.RoofSource}) {b.Doors}d {b.Windows}w {b.DoorRule} [{string.Join(",", b.SharedWallsWith)}]");
        var draft = Fx.Buildings.Value.Buildings;
        Assert.All(p.Buildings, b =>
        {
            var d = draft.Single(x => x.Id == b.Id);
            Assert.Equal(d.Type, b.Type); Assert.Equal(d.Doors, b.Doors); Assert.Equal(d.Windows, b.Windows);
            Assert.Contains(b.StoreysSource, new[] { "tagged", "from height", "from type", "default" });
            Assert.Contains(b.RoofSource, new[] { "tagged", "from type", "storeys rule" });
            Assert.Contains(b.DoorRule, new[] { "entrance node", "road-facing", "road-facing + back door", "longest wall", "none" });
        });
        Assert.Contains(p.Buildings, b => b.RoofShape == "gabled" && b.RoofSource == "tagged" && b.Roof == "pitched");
        Assert.Contains(p.Buildings, b => b.RoofShape == "hipped" && b.Roof == "pitched");
        Assert.Equal("tagged", p.Buildings.Single(b => b.Id == Fx.Levels3).StoreysSource);
        Assert.Equal("default", p.Buildings.Single(b => b.Id == Fx.PlainYes).StoreysSource);
        Assert.Single(p.Buildings, b => b.DoorRule == "entrance node");   // the fixture's one entrance=main
        Assert.All(p.Buildings.Where(b => !b.Enterable), b => Assert.Equal("none", b.DoorRule));
        Assert.All(p.Buildings.Where(b => b.Enterable && b.Doors > 0), b => Assert.NotEqual("none", b.DoorRule));
    }

    [Fact]
    public async Task SharedWalls_NameTheNeighbour_BothWays()
    {
        var p = await Preview();
        var shared = p.Buildings.Where(b => b.SharedWallsWith.Count > 0).ToList();
        Assert.NotEmpty(shared);
        var byId = p.Buildings.ToDictionary(b => b.Id);
        Assert.All(shared, b => Assert.All(b.SharedWallsWith, n => Assert.Contains(b.Id, byId[n].SharedWallsWith)));
    }

    [Fact]
    public async Task EachNote_HasASeverity_AndABoxRoundWhatItIsAbout()
    {
        var p = await Preview();
        Assert.Equal(p.Warnings, p.Notes.Select(n => n.Text).ToList());   // one source
        Assert.NotEmpty(p.Notes);
        var solid = p.Notes.Single(n => n.Text.Contains("SOLID"));
        Assert.Equal(NoteSeverity.Info, solid.Severity);
        Assert.NotNull(solid.Box);
        foreach (var b in p.Buildings.Where(b => !b.Enterable))   // the box holds every solid building
            Assert.All(b.Footprint, v => Assert.True(v.X >= solid.Box![0] - 1e-3 && v.X <= solid.Box[2] + 1e-3 && v.Y >= solid.Box[1] - 1e-3 && v.Y <= solid.Box[3] + 1e-3));
        Assert.All(p.Notes.Where(n => n.Text.Contains("dropped")), n => Assert.Equal(NoteSeverity.Warning, n.Severity));
        Assert.All(p.Notes.Where(n => n.Box != null), n => Assert.True(n.Box![0] <= n.Box[2] && n.Box[1] <= n.Box[3]));
    }
}
