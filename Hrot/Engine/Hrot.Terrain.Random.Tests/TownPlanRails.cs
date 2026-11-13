using System.Diagnostics;
using System.Numerics;
using CarKinem.Road;
using Fdp.Core;
using Fdp.Toolkit.Navigation;
using Fdp.Toolkit.Navigation.Recast;
using Fdp.Toolkit.Terrain;
using Hrot.Map.Common.Services;
using Hrot.Terrain.Random;
using Xunit;
using Xunit.Abstractions;

namespace Hrot.Terrain.Random.Tests;

/// <summary>
/// ⭐⭐ <c>CE-6001</c> (showcase world; feature scenario <c>scenarios/showcases/city-battle/features/town-loads</c>) — the COMMITTED terrain <c>assets/terrain/city-battle</c> against the picture (DESIGN_City_Battle_Showcase §2) and the world rules: it LOADS through
/// the node's own <see cref="TerrainResidency"/> inside R-4192's 30 s (here COLD — no navtiles are committed, so the navmesh bakes at load: the stricter case); the river is crossable ONLY at the two bridges, by men and by ground vehicles (CE-6002's approach on this
/// terrain); the old town's square and town hall stand where §2 draws them. 🔴 Red-proofs: the plan's bridges removed + regenerated ⇒ no crossing (the "only at the bridges" rail's first assert); the hall block moved off the square + regenerated ⇒ the hall rail.
/// ⚠ These read the committed terrain: change the plan, then regenerate (<c>HROT_GEN_CITY_BATTLE=1 … --filter TownPlanTests</c>).
/// </summary>
public sealed class TownPlanRails : IDisposable
{
    private readonly ITestOutputHelper _out;
    private readonly string _staging = Path.Combine(Path.GetTempPath(), "city-battle-rail-" + Guid.NewGuid().ToString("N")[..10]);
    public TownPlanRails(ITestOutputHelper? output) => _out = output!;
    public void Dispose() { try { Directory.Delete(_staging, true); } catch (IOException) { } catch (UnauthorizedAccessException) { } }

    private static readonly TownPlan Plan = TownPlan.Load(TownPlanTests.PlanFile);

    // the node's staging root: {staging}/Terrain/<name> — what the residency resolves terrains through. The committed folder without its kept source (git does not carry it).
    // the river and square rails share ONE load (a cold navmesh bake is ≈ 25 s); the budget rail loads for itself
    private static readonly Lazy<(TerrainWorld World, INavmeshProvider Nav, double Seconds)> Shared = new(() => { var r = new TownPlanRails(null!); return r.Load("shared"); });

    private (TerrainWorld World, INavmeshProvider Nav, double Seconds) Load(string cache = "cache")
    {
        string terrains = Path.Combine(_staging, "Terrain"), target = Path.Combine(terrains, "city-battle");
        Directory.CreateDirectory(target);
        if (!Directory.EnumerateFileSystemEntries(target).Any())
        foreach (var f in Directory.EnumerateFiles(TownPlanTests.TerrainFolder, "*", SearchOption.AllDirectories))
        {
            string rel = Path.GetRelativePath(TownPlanTests.TerrainFolder, f);
            if (rel.StartsWith("source", StringComparison.Ordinal)) continue;
            Directory.CreateDirectory(Path.GetDirectoryName(Path.Combine(target, rel))!);
            File.Copy(f, Path.Combine(target, rel));
        }
        var world = new EntityRepository();
        var residency = new TerrainResidency(_staging, new RoadNetworkHolder());
        var nav = new SwitchableNavmeshProvider();
        residency.AttachNavmesh(new RecastNavmeshFactory { Layers = NavLayerMask.Infantry | NavLayerMask.Vehicle, TileCache = new NavTileCache(Path.Combine(_staging, cache)) }, nav);
        var sw = Stopwatch.StartNew();
        var staged = residency.Prepare("city-battle");
        residency.Commit(world, staged);
        double seconds = sw.Elapsed.TotalSeconds;
        return (world.GetSingletonManaged<TerrainWorld>()!, nav, seconds);
    }

    [Fact]
    public void ThePlan_IsPureData_TheSameGroundTwice_AndNamesTheHallTheBridgesAndTheRiver()
    {
        Assert.Equal(2, Plan.Bridges.Length);
        Assert.Single(Plan.Rivers);
        Assert.Single(Plan.Blocks, b => b.Recipe == "town-hall");
        var a = TownPlanGround.Build(Plan); var b2 = TownPlanGround.Build(Plan);
        foreach (var (x, y) in new[] { (100.0, 100.0), (-300.0, 200.0), (650.0, 0.0) })   // a hill sample (the hill is at local 1402, 737 ⇒ synth 652, -13)
            Assert.Equal(a.Heights.Sample(x, y), b2.Heights.Sample(x, y));
        Assert.True(a.Heights.Sample(Plan.Hills[0].X - 750, Plan.Hills[0].Y - 750) > Plan.PlainHeight + 30, "the hill stands");
        Assert.Equal(a.Water!.Features.Count, b2.Water!.Features.Count);
    }

    [Fact]
    public void TheTerrain_LoadsWithinTheBudget_WithTheTownOnIt()
    {
        var (world, _, cold) = Load("cache");        // the first load: no tile cache, the navmesh is baked (infantry + vehicle)
        var (_, _, warm) = Load("cache");            // R-4192's own case: warm — the navmesh comes from the cache
        _out.WriteLine($"city-battle load (Prepare + Commit): cold {cold:F1} s (navmesh baked) · warm {warm:F1} s · {world.Buildings.Count} buildings · bounds {world.BoundsMin}–{world.BoundsMax}");
        Assert.True(warm < 30, $"the warm load took {warm:F1} s (R-4192: 30 s)");
        Assert.True(cold < 90, $"the cold load took {cold:F1} s");
        Assert.True(world.Buildings.Count >= 400, $"{world.Buildings.Count} buildings: a town for ≈ 1 050 men and 50 vehicles needs ≥ 400");
        Assert.Equal(1500f, world.BoundsMax.X - world.BoundsMin.X, 1f);
        Assert.Contains(world.Surfaces, s => s.Type == TerrainSurfaceType.Water);
    }

    [Fact]
    public void TheRiver_IsCrossableOnlyAtTheTwoBridges_ByMenAndByGroundVehicles()
    {
        var (world, nav, _) = Shared.Value;
        var river = Plan.Rivers[0];
        double RiverX(double y)
        {
            for (int i = 0; i + 1 < river.Points.Length; i++)
            {
                double y0 = river.Points[i][1], y1 = river.Points[i + 1][1];
                if (y >= Math.Min(y0, y1) && y <= Math.Max(y0, y1) && y0 != y1) return river.Points[i][0] + (river.Points[i + 1][0] - river.Points[i][0]) * (y - y0) / (y1 - y0);
            }
            return river.Points[^1][0];
        }
        double[] bridgeYs = Plan.Bridges.Select(b => b.From[1]).ToArray();
        float z = Plan.PlainHeight > 0 ? (float)Plan.PlainHeight : 0f;
        Span<NavWaypoint> wps = stackalloc NavWaypoint[512];
        int checkedRows = 0;
        foreach (var layer in new[] { NavLayerMask.Infantry, NavLayerMask.Vehicle })
        {
            Assert.False(nav.IsWalkable(new Vector3((float)RiverX(300), 300, z), (uint)layer), $"{layer}: the river is walkable");
            for (double y = 150; y <= 1450; y += 100)
            {
                if (y > 480 && y < 1030) continue;   // the rows through the old town are blocked by houses on a bank, not by the river: the open ground north and south is the proof
                var from = new Vector3((float)RiverX(y) - 70, (float)y, z); var to = new Vector3((float)RiverX(y) + 70, (float)y, z);
                Assert.True(nav.IsWalkable(from, (uint)layer) && nav.IsWalkable(to, (uint)layer), $"{layer}: row {y}: a bank is not land");
                Assert.True(nav.PathExists(from, to, (uint)layer), $"{layer}: row {y}: no way across at all (the bridges must carry it)");
                int n = nav.PlanPath(from, to, wps, (uint)layer);
                float length = 0f;
                for (int i = 1; i < n; i++)
                {
                    length += Vector3.Distance(wps[i - 1].Position, wps[i].Position);
                    var a = wps[i - 1].Position; var b = wps[i].Position;
                    if (a.X < RiverX(a.Y) - 20 && b.X > RiverX(b.Y) + 20)   // a leg that jumps the river band
                        Assert.True(bridgeYs.Any(by => Math.Abs(a.Y - by) < 12 && Math.Abs(b.Y - by) < 12), $"{layer}: row {y}: crosses the river at y {a.Y:F0}→{b.Y:F0}, not at a bridge");
                }
                Assert.True(length > 140f + 30f, $"{layer}: row {y}: a {length:F0} m path is not the detour to a bridge");   // the straight way is 140 m
                checkedRows++;
            }
        }
        Assert.True(checkedRows >= 16, $"only {checkedRows} rows checked");
    }

    [Fact]
    public void TheOldTownSquare_AndTheTownHall_StandWhereTheDesignDrawsThem()
    {
        var (world, _, _) = Shared.Value;
        int hallIndex = Array.FindIndex(Plan.Blocks, b => b.Recipe == "town-hall");
        string hallKey = BlockLayout.GuidOf(Plan.Seed, hallIndex).ToString("D");
        var hall = world.Buildings.Where(b => b.Label.StartsWith(hallKey, StringComparison.Ordinal)).ToList();
        Assert.Single(hall);
        // §2's picture, in metres (1.25 m per px, DESIGN_City_Battle_Showcase §2 / city-battle-overview.svg): the square x 870…970, y 712…800; the hall on it
        var (minX, maxX, minY, maxY) = (870f, 970f, 712f, 800f);
        Assert.InRange(hall[0].Position.X, minX, maxX); Assert.InRange(hall[0].Position.Y, minY, maxY);
        var inSquare = world.Buildings.Where(b => b.Position.X > minX && b.Position.X < maxX && b.Position.Y > minY && b.Position.Y < maxY).ToList();
        Assert.True(inSquare.Count == 1, $"the square holds {inSquare.Count} buildings; only the hall belongs on it");
        // …inside the old town (x 782…1057, y 625…887), which is built up
        int oldTown = world.Buildings.Count(b => b.Position.X > 782 && b.Position.X < 1057 && b.Position.Y > 625 && b.Position.Y < 887);
        Assert.True(oldTown >= 60, $"the old town holds {oldTown} buildings");
        // …the open fields west of the ring road are not built up (the picture's three bands)
        int fields = world.Buildings.Count(b => b.Position.X < 480);
        Assert.True(fields < 60, $"{fields} buildings in the west fields");
    }
}
