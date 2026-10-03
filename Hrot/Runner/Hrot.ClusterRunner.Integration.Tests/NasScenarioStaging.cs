using System.IO;
using Fdp.Core;
using Fdp.Toolkit.Orchestration;
using Fdp.Toolkit.Scenario;
using Hrot.Map.Common.Scenario;
using Hrot.ScenarioEditor.Handlers;
using Hrot.ScenarioEditor.Services;

namespace Hrot.ClusterRunner.Integration.Tests;

/// <summary>
/// ⭐ Puts an authored scenario where a live cluster LOADS it from — <c>{shared}/scenarios/{id}/scenario.json</c> — for
/// rails that author offline and then load into an in-process cluster.
/// </summary>
/// <remarks>
/// <para>🔴 <b>Why it exists.</b> Going live starts with the orchestrator PREFETCHING the scenario from the shared (NAS)
/// root (<c>StorageGatewayModule.PrefetchScenario</c>); a scenario that is not there abandons the transition and the
/// cluster never leaves state 0. Two rails staged the OLD way and sat red for weeks:
/// <c>DistributedScenarioLoadTests</c> relied on <c>IEditorLogic.SaveScenarioAs</c>, which since the distributed
/// save writes NO file (it publishes a storage request a cluster handler serves — 📄
/// <c>DESIGN_Distributed_Scenario_Persistence.md</c> §4); <c>UrbanCombatFileLifecycleTests</c> wrote to a hard-coded
/// <c>C:\FDP_Temp\{id}</c>.</para>
/// <para>⭐ Writes through <see cref="ScenarioSaveCore"/> — the ONE host-neutral scenario writer (CE-275) — and the root
/// comes from <see cref="OrchestrationConstants.GetSharedScenariosRoot()"/>, the same one every host lists.</para>
/// </remarks>
internal static class NasScenarioStaging
{
    /// <summary>The scenario's directory under the shared scenarios root.</summary>
    public static string DirectoryOf(string scenarioId)
        => Path.Combine(OrchestrationConstants.GetSharedScenariosRoot(), scenarioId);

    /// <summary>Serializes <paramref name="world"/> as scenario <paramref name="scenarioId"/> into the shared root.</summary>
    public static void Write(ScenarioSerializer serializer, EntityRepository world, string scenarioId)
    {
        var dir = DirectoryOf(scenarioId);
        Directory.CreateDirectory(dir);
        ScenarioSaveCore.Write(serializer, world, Path.Combine(dir, HrotScenarioSaveHandler.ScenarioFileName),
            new ScenarioHeader("Hrot.Scenario"));
    }

    /// <summary>The same, through an editor's <see cref="ScenarioFileService"/> (a thin shim over <see cref="ScenarioSaveCore"/>).</summary>
    public static void Write(ScenarioFileService fileService, EntityRepository world, string scenarioId)
    {
        var dir = DirectoryOf(scenarioId);
        Directory.CreateDirectory(dir);
        fileService.SaveScenario(world, Path.Combine(dir, HrotScenarioSaveHandler.ScenarioFileName));
    }

    /// <summary>Best-effort removal of the staged scenario (test cleanup).</summary>
    public static void Remove(string scenarioId)
    {
        var dir = DirectoryOf(scenarioId);
        if (!Directory.Exists(dir)) return;
        try { Directory.Delete(dir, recursive: true); } catch { /* best-effort */ }
    }
}
