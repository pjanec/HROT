using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Fdp.Core;
using Fdp.Core.CommandHierarchy;
using Fdp.Testing;
using Fdp.Toolkit.Orchestration;
using Fdp.Toolkit.Replication.Components;
using Fdp.Toolkit.Scenario;
using Xunit;
using Xunit.Abstractions;

namespace Hrot.ClusterRunner.Integration.Tests;

/// <summary>
/// ⭐⭐ <c>CE-6009</c> (city battle M-1; 📄 docs/designs/group-maneuvers/DESIGN_City_Battle_Showcase.md §4, §11 M-1; docs/DESIGN_Scenario_Macros.md §16) —
/// <c>scenarios/showcases/city-battle/features/macro-platoon</c> in-process on the four hosts: a trigger inserts <c>macros/city-battle/rifle-platoon</c> at
/// runtime (3 s) at @Drop. On EVERY node: ① one platoon 'Blue 1st Platoon', 6 squads, 36 men (CE-6010), all Active (the ELM acked them), all Friend, all marked
/// as the instance's (<c>MacroOrigin</c>); ③ they stand round the drop point. On the brain node (CGF): ② the hierarchy is wired — every man under a squad
/// of this platoon, six each, every squad under the platoon (⚠ CE-6015: no platoon's hierarchy reaches SimHost / the Player); ④ the expansion is Done.
/// ⭐ Parallel contract: CE-6012 reruns this unchanged with <c>HROT_PARALLEL_SIM=1 HROT_READ_CHECK=1 HROT_WORLD_GUARD=1</c>.
/// </summary>
[Collection("HeavyE2ETests")]
public sealed class MacroPlatoonScenarioTests : IDisposable
{
    private const int DomainId = 178;   // CycloneDDS accepts 0–232; 178 used by no other rail (ClusterOpE2e owns 170–173, the AI debug surface 181–189; grep 2026-11-13)
    private readonly List<string> _staged = new();
    private readonly ITestOutputHelper _out;

    public MacroPlatoonScenarioTests(ITestOutputHelper output) => _out = output;
    public void Dispose() { foreach (var id in _staged) NasScenarioStaging.Remove(id); }

    private static IEnumerable<Entity> All(EntityRepository w)
    {
        for (int i = 0; i <= w.MaxEntityIndex; i++)
        {
            var e = w.GetEntityByIndex(i);
            if (w.IsAlive(e)) yield return e;
        }
    }

    private static long TypeOf(EntityRepository w, Entity e) => w.HasComponent<TkbIdentity>(e) ? (long)w.GetComponentRO<TkbIdentity>(e).TkbType : 0;

    /// <summary>
    /// The platoon on one node: its 43 parts by type (1 platoon 305, 6 squads 302, 36 men), Active, Friend, marked with <c>MacroOrigin</c>, round the drop
    /// point — and, where <paramref name="hierarchy"/>, wired: every man under a squad of this platoon (six each), every squad under the platoon.
    /// </summary>
    private static string Tree(EntityRepository w, bool hierarchy, out bool whole)
    {
        whole = false;
        bool Origin(Entity e) => w.IsComponentTypeRegistered<MacroOrigin>() && w.HasComponent<MacroOrigin>(e);
        var parts = All(w).Where(e => Origin(e) && TypeOf(w, e) is 305 or 302 or 2002 or 2004).ToList();
        var platoon = parts.FirstOrDefault(e => TypeOf(w, e) == 305 && w.GetComponentRO<EntityInfo>(e).Name.ToString() == "Blue 1st Platoon");
        if (platoon.IsNull) return $"no platoon ({parts.Count} parts)";
        var squads = parts.Where(e => TypeOf(w, e) == 302).ToList();
        var men = parts.Where(e => TypeOf(w, e) is 2002 or 2004).ToList();
        bool Under(Entity e, Entity commander) => w.HasComponent<UnitSubordinate>(e) && w.GetComponentRO<UnitSubordinate>(e).Commander == commander;
        int wiredSquads = squads.Count(s => Under(s, platoon));
        int wiredMen = men.Count(m => squads.Any(s => Under(m, s)));
        bool sixEach = squads.All(s => men.Count(m => Under(m, s)) == 6);
        int active = parts.Count(e => w.GetLifecycleState(e) == EntityLifecycle.Active);
        int friend = parts.Count(e => w.GetComponentRO<EntityInfo>(e).ForceId == ForceId.Friend);
        float far = men.Count == 0 ? -1f : men.Max(m => System.Numerics.Vector2.Distance(
            new(w.GetComponentRO<SimTransform>(m).Position.X, w.GetComponentRO<SimTransform>(m).Position.Y), new(50f, 50f)));
        whole = squads.Count == 6 && men.Count == 36 && active == 43 && friend == 43 && far is > 0f and < 30f
                && (!hierarchy || (wiredSquads == 6 && wiredMen == 36 && sixEach));
        return $"{squads.Count} squads ({wiredSquads} under the platoon), {men.Count} men ({wiredMen} under a squad), {active}/43 active, {friend}/43 Friend, farthest man {far:0.0} m from (50,50)";
    }

    [Fact(Timeout = 600_000)]
    public async Task CE6009_MacroPlatoon_ATriggerInsertsAPlatoon_WiredAndActiveOnEveryNode()
    {
        using var harness = await ScenarioTwin.Live("macro-platoon", _staged, DomainId);
        var hosts = new[] { ("CGF", harness.Cgf!.World!), ("SimHost", harness.SimHost.World!), ("Player", harness.Player.App.World) };
        var cgf = hosts[0].Item2;

        // ② the hierarchy is asserted where it lives: the brain node (CGF). ⚠ It does not reach SimHost / the Player for ANY platoon — a scenario-loaded
        //   one included (📐 measured on pc-attack-dry: 15 of 15 wired on CGF, 0 on SimHost and the Player) — CE-6015, not this item.
        bool Hier(string node) => node == "CGF";
        string State() => string.Join("; ", hosts.Select(h => $"{h.Item1}: {Tree(h.Item2, Hier(h.Item1), out _)}"));
        bool allWhole = false;
        harness.PumpUntil(() => { allWhole = hosts.All(h => { Tree(h.Item2, Hier(h.Item1), out bool ok); return ok; }); return allWhole || cgf.SimulationTime > 25.0; }, timeoutFrames: 12000);
        _out.WriteLine($"sim {cgf.SimulationTime:0.0}: {State()}");
        Assert.True(allWhole, $"①③ the platoon active and in place on every node, ② wired on the brain node: {State()}");

        // ④ the instance's expansion is Done (where its status is held)
        var instances = All(cgf).Where(e => cgf.IsComponentTypeRegistered<MacroExpansionStatus>() && cgf.HasComponent<MacroExpansionStatus>(e)).ToList();
        _out.WriteLine($"instances on CGF: {string.Join(", ", instances.Select(e => cgf.GetComponentRO<MacroExpansionStatus>(e).State))}");
        Assert.Contains(instances, e => cgf.GetComponentRO<MacroExpansionStatus>(e).State == MacroExpansionState.Done);
    }
}
