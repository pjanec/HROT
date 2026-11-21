using Hrot.Terrain.Import;
using Hrot.Terrain.InputSet;
using Hrot.Terrain.Random;
using Hrot.Tools.TerrainSynth;
using Xunit;
using Xunit.Abstractions;

namespace Hrot.Terrain.Random.Tests;

/// <summary>
/// ⭐⭐ <c>CE-6001</c> (showcase world, docs/DESIGN_Terrain_Authoring.md §18l) — the AUTHORED TOWN PLAN: <c>assets/terrain-plans/city-battle.plan.json</c> is the committed input, <c>assets/terrain/city-battle</c> its output.
/// REGENERATE the terrain with <c>HROT_GEN_CITY_BATTLE=1 dotnet test … --filter TownPlanTests</c> (the same idiom as <c>HROT_GEN_VEHICLE_BODY</c>): it rebuilds the terrain from the plan, replacing the folder. ⚠ The rails below read the COMMITTED
/// terrain, so a plan change without a regeneration fails them.
/// </summary>
public sealed class TownPlanTests
{
    private readonly ITestOutputHelper _out;
    public TownPlanTests(ITestOutputHelper output) => _out = output;

    internal static string Repo => Netconvert.RepoRoot() ?? throw new InvalidOperationException("HROT.sln was not found");
    internal static string PlanFile => Path.Combine(Repo, "assets", "terrain-plans", "city-battle.plan.json");
    internal static string TerrainFolder => Path.Combine(Repo, "assets", "terrain", "city-battle");

    [Fact]
    public async Task Regenerate_TheCityBattleTerrain_FromItsPlan_WhenAsked()
    {
        if (Environment.GetEnvironmentVariable("HROT_GEN_CITY_BATTLE") != "1") return;
        var plan = TownPlan.Load(PlanFile);
        var r = await TownPlanGenerator.GenerateAsync(plan, Path.Combine(Repo, "assets", "terrain"), new Hrot.Traffic.BridgeNetBuilder());
        _out.WriteLine($"generated {r.TerrainFolder} in {r.Seconds:F1} s · {r.Import?.WaterAreas} water area(s)");
        foreach (var (stage, secs) in r.Import?.StageSeconds ?? new()) _out.WriteLine($"  import stage {stage}: {secs:F1} s");
        _out.WriteLine(r.Saved.Summary.ToString());
    }
}
