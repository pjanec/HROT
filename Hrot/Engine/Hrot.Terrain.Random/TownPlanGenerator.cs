using Fdp.Toolkit.Navigation.Recast;
using Fdp.Toolkit.Terrain;
using Hrot.Terrain.Import;
using Hrot.Tools.TerrainSynth;

namespace Hrot.Terrain.Random;

/// <summary>What <see cref="TownPlanGenerator.GenerateAsync"/> made.</summary>
public sealed record TownPlanResult(string TerrainFolder, TerrainSaveResult Saved, ImportReport? Import, double Seconds);

/// <summary>
/// ⭐ CE-6001 (docs/DESIGN_Terrain_Authoring.md §18l) — the town plan → a terrain, with nothing new but the plan: ① the plan's ground (<see cref="TownPlanGround"/>) is written as an input set by the synth's own writer and imported by
/// the importer as the terrain <c>&lt;name&gt;-ground</c>, ② the plan's blocks and bridges are a source scenario on that ground, ③ <see cref="TerrainBake.SaveAs"/> bakes them into the terrain <c>&lt;name&gt;</c> (the same step as the editor's
/// <i>Save terrain as</i>). The temporary ground and scenario are removed; the saved terrain carries its own kept ground (<c>source/</c>). Deterministic: the same plan is the same terrain.
/// </summary>
public static class TownPlanGenerator
{
    /// <param name="bridgeNet">the host's bridge-net builder (<c>Hrot.Traffic.BridgeNetBuilder</c>) — the bridges' SUMO net; null ⇒ road for the vehicles' net only</param>
    /// <param name="terrainsRoot">where terrains live (<c>assets/terrain</c>; the shared building templates and recipes are found beside it)</param>
    public static async Task<TownPlanResult> GenerateAsync(TownPlan plan, string terrainsRoot, IBridgeNet? bridgeNet = null, CancellationToken ct = default)
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        string groundName = plan.Name + "-ground";
        string groundFolder = Path.Combine(terrainsRoot, groundName), target = Path.Combine(terrainsRoot, plan.Name);
        string work = Path.Combine(Path.GetTempPath(), "town-plan-" + Guid.NewGuid().ToString("N")[..10]);
        string setFolder = Path.Combine(work, "set"), scenarioDir = Path.Combine(work, "scenarios", plan.Name);
        var recipeRoots = TerrainRecipe.RootsFor(Path.Combine(terrainsRoot, "_preview")).ToList();
        try
        {
            var o = TownPlanGround.OptionsOf(plan, setFolder);
            TerrainSynthesizer.Write(TownPlanGround.Build(plan), o, TextWriter.Null);

            var importer = new TerrainImporter(terrainsRoot, Path.Combine(work, "scenarios"));
            await importer.ImportAsync(setFolder, groundName, replace: true, null, ct).ConfigureAwait(false);

            Directory.CreateDirectory(scenarioDir);
            File.WriteAllText(Path.Combine(scenarioDir, "scenario.json"), BlockLayout.Serialize(TownPlanGround.ScenarioDom(plan, groundName)));

            var catalog = new TerrainCatalog(new[] { terrainsRoot });
            var saved = TerrainBake.SaveAs(scenarioDir, target, plan.Name, catalog.ResolveDefinition, recipeRoots, bakeNavtiles: false, bridgeNet: bridgeNet,
                reElevateNet: (net, ground) => Hrot.Terrain.InputSet.NetElevation.Apply(net, ground, force: true));   // ⭐ CE-4597 (G-20)
            // a template the SHARED folder (assets/buildings, beside the terrains) already holds is not kept in the terrain too: no template lives in two places (TerrainWorldTests.UI1_*)
            string shared = Path.Combine(Path.GetDirectoryName(Path.GetFullPath(terrainsRoot))!, "buildings"), own = Path.Combine(target, "buildings");
            if (Directory.Exists(own) && Directory.Exists(shared))
            {
                foreach (var f in Directory.GetFiles(own)) if (File.Exists(Path.Combine(shared, Path.GetFileName(f)))) File.Delete(f);
                if (!Directory.EnumerateFileSystemEntries(own).Any()) Directory.Delete(own);
            }
            return new TownPlanResult(target, saved, importer.LastReport, sw.Elapsed.TotalSeconds);
        }
        finally
        {
            try { if (Directory.Exists(groundFolder)) Directory.Delete(groundFolder, true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
            try { if (Directory.Exists(work)) Directory.Delete(work, true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
        }
    }
}
