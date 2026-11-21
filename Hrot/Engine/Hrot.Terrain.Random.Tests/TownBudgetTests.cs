using System.Diagnostics;
using CarKinem.Road;
using Fdp.Core;
using Fdp.Toolkit.Navigation;
using Fdp.Toolkit.Navigation.Recast;
using Fdp.Toolkit.Terrain;
using Hrot.Map.Common.Services;
using Hrot.Terrain.Import;
using Hrot.Terrain.Random;
using Xunit;
using Xunit.Abstractions;

namespace Hrot.Terrain.Random.Tests;

/// <summary>
/// ⭐ CE-4428 (R-4192's budget, docs/DESIGN_Terrain_Authoring.md §18f acceptance) — OPT-IN (<c>HROT_RUN_TOWN_BUDGET=1</c>): a 3 km <i>Town with suburbs</i> generates, imports (navmesh prefill included) and LOADS through the node's
/// own <see cref="TerrainResidency"/> (Prepare + Commit, the navmesh baked from the prefilled tiles) within 30 s warm. Reports each stage's time. Slow, so never part of the usual run.
/// </summary>
public sealed class TownBudgetTests : IDisposable
{
    private readonly Env _env = new();
    private readonly ITestOutputHelper _out;
    public TownBudgetTests(ITestOutputHelper output) => _out = output;
    public void Dispose() => _env.Dispose();

    [SkippableFact]
    public async Task ATownWithSuburbsOf3Km_GeneratesImportsAndLoadsWithinTheBudget()
    {
        Skip.IfNot(Environment.GetEnvironmentVariable("HROT_RUN_TOWN_BUDGET") == "1", "opt-in: set HROT_RUN_TOWN_BUDGET=1");
        var pipe = _env.Pipeline();
        var options = new RandomTerrainOptions { Preset = TerrainPreset.TownWithSuburbs, SizeMetres = 3000, Relief = ReliefLevel.Rolling, Vegetation = VegetationLevel.Light, Water = WaterLevel.River, GroundSeed = 3, LayoutSeed = 3 };

        var sw = Stopwatch.StartNew();
        var preview = pipe.Preview(options);
        double previewSeconds = sw.Elapsed.TotalSeconds;
        _out.WriteLine($"preview: {previewSeconds:F1} s (ground {preview.Timings.GroundSeconds:F1}, layout {preview.Timings.LayoutSeconds:F1}, preview records {preview.Timings.PreviewSeconds:F1}) — {preview.Layout.Count} blocks, {preview.Preview.Counts.Buildings} houses, {preview.Preview.Roads.Count} road edges, verdict {preview.Preview.Verdict}");

        // the staging root of a node: {staging}/Terrain/<name> — the layout the residency resolves terrains through
        string staging = Path.Combine(_env.Root, "node");
        string terrains = Path.Combine(staging, "Terrain");
        Directory.CreateDirectory(terrains);
        var r = await pipe.OpenInEditorAsync(preview, "rnd-town", _env.Scenarios, terrains, new NavmeshPrefill());
        _out.WriteLine($"write ground: {r.WriteGroundSeconds:F1} s · import (heights, roads, vegetation, world, navmesh prefill): {r.ImportSeconds:F1} s · scenario: {r.ScenarioSeconds:F2} s");
        if (r.Import != null) foreach (var (stage, secs) in r.Import.StageSeconds) _out.WriteLine($"  import stage {stage}: {secs:F1} s");

        // the load: warm = the navmesh comes from the tiles the import prefilled
        var world = new EntityRepository();
        var residency = new TerrainResidency(staging, new RoadNetworkHolder());
        var nav = new SwitchableNavmeshProvider();
        residency.AttachNavmesh(new RecastNavmeshFactory { TileCache = new NavTileCache() }, nav);
        sw.Restart();
        var staged = residency.Prepare("rnd-town");
        double prepare = sw.Elapsed.TotalSeconds;
        sw.Restart();
        residency.Commit(world, staged);
        double commit = sw.Elapsed.TotalSeconds;
        _out.WriteLine($"load: Prepare {prepare:F1} s (world parse + navmesh from the prefilled tiles) · Commit {commit:F2} s");
        Assert.NotNull(world.GetSingletonManaged<TerrainWorld>());

        double load = prepare + commit;
        double total = previewSeconds + r.WriteGroundSeconds + r.ImportSeconds + load;
        _out.WriteLine($"TOTAL generate+import+load: {total:F1} s (budget 30 s warm, R-4192)");
        // R-4192 / PLAN_Large_Terrain Q-L2: "load <= 30 s warm (navmesh from the cache)" — the LOAD is what is budgeted; the one-time navmesh prefill is part of the import, reported above
        Assert.True(load < 30, $"the load took {load:F1} s");
    }
}
