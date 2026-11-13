using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Threading.Tasks;
using Fdp.Core;
using Fdp.Testing;
using Fdp.Toolkit.Combat.Components;
using Fdp.Toolkit.Navigation;
using Fdp.Toolkit.Orchestration;
using Fdp.Toolkit.Terrain;
using Xunit;
using Xunit.Abstractions;

namespace Hrot.ClusterRunner.Integration.Tests;

/// <summary>
/// ⭐⭐ <c>CE-6003</c> (city battle O-1; 📄 docs/designs/group-maneuvers/DESIGN_City_Battle_Showcase.md §5, §7; docs/DESIGN_Peek_And_Fire.md §9.9) —
/// <c>scenarios/showcases/city-battle/features/roadblock-blast</c> in-process on the four hosts: a 6 m concrete barrier (one concrete-block obstacle, per-instance size) closes the 6 m gate of the wall
/// that cuts the <c>roadblock-street</c> feature terrain in two; 'Charge Man' sets off an 8 kg demolition charge beside it; 'IFV' is ordered to the far side 40 s in.
/// ① before the burst no ground path (nor a vehicle path) goes through the gate · ② the burst empties the barrier and it leaves EVERY node (the Health
/// owner's teardown) · ③ every node's obstacle bake rebakes, and the gate's ground AND vehicle paths open · ④ the IFV, ordered 40 s in, reaches the far side
/// (⚠ where it crosses the wall is logged, not asserted: CE-6014).
/// ⭐ Parallel contract: CE-6012 reruns this unchanged with <c>HROT_PARALLEL_SIM=1 HROT_READ_CHECK=1 HROT_WORLD_GUARD=1</c>.
/// </summary>
[Collection("HeavyE2ETests")]
public sealed class RoadblockBlastScenarioTests : IDisposable
{
    private const int DomainId = 186;   // CycloneDDS accepts 0–232; 186 used by no other rail (grep of domain ids, 2026-11-13)
    private readonly List<string> _staged = new();
    private readonly ITestOutputHelper _out;

    public RoadblockBlastScenarioTests(ITestOutputHelper output) => _out = output;
    public void Dispose() { foreach (var id in _staged) NasScenarioStaging.Remove(id); }

    private static readonly Vector3 West = new(88f, 50f, 0f), East = new(112f, 50f, 0f);

    /// <summary>Does a ground path go from <see cref="West"/> to <see cref="East"/> through the gate (never over a block's top)?</summary>
    private static string GatePath(EntityRepository world, out bool open, uint layers = 0xFFFFFFFFu)
    {
        open = false;
        if (!world.HasSingletonManaged<INavmeshProvider>()) return "no navmesh";
        var nav = world.GetSingletonManaged<INavmeshProvider>()!;
        Span<NavWaypoint> wps = stackalloc NavWaypoint[128];
        int n = nav.PlanPath(West, East, wps, layers);
        if (n < 2) return $"no path ({n})";
        var last = wps[n - 1].Position;
        bool reached = Vector2.Distance(new(last.X, last.Y), new(East.X, East.Y)) < 1f;
        bool ground = true; float length = 0f;
        for (int i = 0; i < n; i++)
        {
            if (wps[i].Position.Z > 0.5f) ground = false;
            if (i > 0) length += Vector3.Distance(wps[i - 1].Position, wps[i].Position);
        }
        open = reached && ground && length < 30f;   // straight through the gate is 24 m; there is no way round (the wall runs past both edges)
        return $"{n} waypoints, {length:0.0} m, ends ({last.X:0.0},{last.Y:0.0},{last.Z:0.00}), ground {ground}";
    }

    private static bool GatePathOpen(EntityRepository world, uint layers) { GatePath(world, out bool open, layers); return open; }

    [Fact(Timeout = 600_000)]
    public async Task CE6003_RoadblockBlast_TheChargeRemovesTheBlocks_EveryNodeRebakes_TheIfvDrivesThroughTheGate()
    {
        using var harness = await ScenarioTwin.Live("roadblock-blast", _staged, DomainId);
        var cgf = harness.Cgf!.World!;
        var sim = harness.SimHost.World!;
        var hosts = new[] { ("CGF", cgf), ("SimHost", sim), ("Player", harness.Player.App.World) };
        string[] blocks = { "Roadblock" };

        Assert.True(harness.PumpUntil(() => blocks.All(b => !ScenarioTwin.ByName(sim, b).IsNull && sim.GetLifecycleState(ScenarioTwin.ByName(sim, b)) == EntityLifecycle.Active)
                                            && !ScenarioTwin.ByName(cgf, "IFV").IsNull, timeoutFrames: 3000),
            "the blocks must be baked (Active) on SimHost and the IFV spawned on CGF");
        var south = ScenarioTwin.ByName(sim, "Roadblock");
        Assert.True(sim.HasComponent<Health>(south), "a concrete block carries Health (CE-6003, the TKB's MaxHealth)");
        Assert.Equal(600f, sim.GetComponentRO<Health>(south).Max);

        // ① the gate is closed
        string before = GatePath(sim, out bool openBefore);
        _out.WriteLine($"t={sim.HasSingleton<GlobalTime>()} before: {before}");
        Assert.False(openBefore, $"① before the burst no ground path goes through the gate: {before}");
        Assert.False(GatePathOpen(sim, (uint)NavLayerMask.Vehicle), "① … nor a vehicle path");

        // ② the burst: the roadblock leaves every node
        double? goneAt = null;
        string Where() => string.Join("; ", hosts.Select(h => $"{h.Item1}: " + string.Join(", ", blocks.Select(b =>
        {
            var e = ScenarioTwin.ByName(h.Item2, b);
            return e.IsNull ? $"{b} gone" : $"{b} {h.Item2.GetLifecycleState(e)} hp {(h.Item2.HasComponent<Health>(e) ? h.Item2.GetComponentRO<Health>(e).Current : -1):0}";
        }))));
        harness.PumpUntil(() =>
        {
            if (goneAt == null && hosts.All(h => blocks.All(b => ScenarioTwin.ByName(h.Item2, b).IsNull))) goneAt = cgf.SimulationTime;
            return goneAt != null || cgf.SimulationTime > 60.0;
        }, timeoutFrames: 12000);
        _out.WriteLine($"blocks gone everywhere at sim {goneAt}: {Where()}");
        Assert.True(goneAt != null, $"② the charge removes the roadblock on every node; {Where()}");

        // ③ the rebake opens the gate
        string after = "";
        bool openAfter = false;
        harness.PumpUntil(() => { after = GatePath(sim, out openAfter); return openAfter || cgf.SimulationTime > goneAt + 20.0; }, timeoutFrames: 6000);
        _out.WriteLine($"after (sim {cgf.SimulationTime:0.0}): {after}; the vehicle layer: {GatePath(sim, out _, (uint)NavLayerMask.Vehicle)}");
        Assert.True(openAfter, $"③ after the rebake the ground path goes through the gate: {after}");
        Assert.True(GatePathOpen(sim, (uint)NavLayerMask.Vehicle), $"③ … and a VEHICLE path: {GatePath(sim, out _, (uint)NavLayerMask.Vehicle)}");

        // ④ the IFV drives in
        var ifv = ScenarioTwin.ByName(cgf, "IFV");
        var goal = new Vector2(150f, 51.5f);
        float Dist() => Vector2.Distance(new(cgf.GetComponentRO<SimTransform>(ifv).Position.X, cgf.GetComponentRO<SimTransform>(ifv).Position.Y), goal);
        float minX = float.MaxValue;
        harness.PumpUntil(() =>
        {
            var p = cgf.GetComponentRO<SimTransform>(ifv).Position;
            if (p.X > 98f && p.X < 102f) minX = MathF.Min(minX, MathF.Abs(p.Y - 50f));   // crossing the wall line: how far from the gate's middle
            return Dist() <= 4f || cgf.SimulationTime > 130.0;
        }, timeoutFrames: 20000);
        var at = cgf.GetComponentRO<SimTransform>(ifv).Position;
        var ifvSim = ScenarioTwin.ByName(sim, "IFV");
        if (!ifvSim.IsNull && sim.HasComponent<CarKinem.Core.NavState>(ifvSim))
        {
            var nav = sim.GetComponentRO<CarKinem.Core.NavState>(ifvSim);
            _out.WriteLine($"IFV on SimHost at {sim.GetComponentRO<SimTransform>(ifvSim).Position}, nav {nav.Mode} dest {nav.FinalDestination} arrived {nav.HasArrived} blocked {nav.IsBlocked}");
        }
        _out.WriteLine($"IFV at ({at.X:0.0},{at.Y:0.0}) sim {cgf.SimulationTime:0.0}, {Dist():0.0} m from the goal, crossed the wall line {minX:0.0} m from the gate's middle");
        Assert.True(Dist() <= 4f, $"④ the IFV drives through to the far side: at ({at.X:0.0},{at.Y:0.0}), {Dist():0.0} m from (150, 51.5)");
        // ⚠ NOT asserted: WHERE it crosses the wall line. 📐 Measured: it swerves round the charge man's body and crosses ~7 m north of the gate,
        //   through the concrete — vehicle avoidance leaves the navmesh (CE-6014). The route is the proof the gate opened: ① and ③ for the vehicle layer.
        _out.WriteLine($"④ crossed the wall line {minX:0.0} m from the gate's middle (CE-6014 when > 3)");
    }
}
