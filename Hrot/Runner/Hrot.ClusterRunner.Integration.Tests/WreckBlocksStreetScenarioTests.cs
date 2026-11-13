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
using Fdp.Toolkit.Replication.Components;
using Fdp.Toolkit.Terrain;
using Xunit;
using Xunit.Abstractions;

namespace Hrot.ClusterRunner.Integration.Tests;

/// <summary>
/// ⭐⭐ <c>CE-6005</c> (city battle O-2; 📄 docs/designs/group-maneuvers/DESIGN_City_Battle_Showcase.md §11 O-2; docs/DESIGN_Peek_And_Fire.md §9.10) —
/// <c>scenarios/showcases/city-battle/features/wreck-blocks-street</c> in-process on the four hosts: a hostile T-72 stands in the 6 m gate of the
/// <c>roadblock-street</c> wall; a friendly M1 kills it; a demolition man clears the wreck 60 s in.
/// ① while the T-72 lives the gate is open to vehicles · ② it dies (a real kill) · ③ ONE wreck obstacle exists on every node, on the hulk, and never a
/// second · ④ the bake closes the gate to vehicles · ⑤ the charge removes the wreck on every node and the gate opens again · ⑥ the hulk stays.
/// ⭐ Parallel contract: CE-6012 reruns this unchanged with <c>HROT_PARALLEL_SIM=1 HROT_READ_CHECK=1 HROT_WORLD_GUARD=1</c>.
/// </summary>
[Collection("HeavyE2ETests")]
public sealed class WreckBlocksStreetScenarioTests : IDisposable
{
    private const int DomainId = 177;   // CycloneDDS accepts 0–232; 177 used by no other rail (ClusterOpE2e owns 170–173, the AI debug surface 181–189; grep 2026-11-13). Was 187 — it collided with 181–189
    private const long WreckType = 8819;
    private readonly List<string> _staged = new();
    private readonly ITestOutputHelper _out;

    public WreckBlocksStreetScenarioTests(ITestOutputHelper output) => _out = output;
    public void Dispose() { foreach (var id in _staged) NasScenarioStaging.Remove(id); }

    private static readonly Vector3 West = new(88f, 50f, 0f), East = new(112f, 50f, 0f);

    /// <summary>Does a VEHICLE path go from <see cref="West"/> to <see cref="East"/> straight through the gate, on the ground?</summary>
    private static bool VehicleGateOpen(EntityRepository world, out string what)
    {
        what = "no navmesh";
        if (!world.HasSingletonManaged<INavmeshProvider>()) return false;
        Span<NavWaypoint> wps = stackalloc NavWaypoint[128];
        int n = world.GetSingletonManaged<INavmeshProvider>()!.PlanPath(West, East, wps, (uint)NavLayerMask.Vehicle);
        if (n < 2) { what = $"no path ({n})"; return false; }
        var last = wps[n - 1].Position;
        bool ground = true; float length = 0f;
        for (int i = 0; i < n; i++)
        {
            if (wps[i].Position.Z > 0.5f) ground = false;
            if (i > 0) length += Vector3.Distance(wps[i - 1].Position, wps[i].Position);
        }
        what = $"{n} waypoints, {length:0.0} m, ends ({last.X:0.0},{last.Y:0.0},{last.Z:0.00})";
        return Vector2.Distance(new(last.X, last.Y), new(East.X, East.Y)) < 1f && ground && length < 30f;
    }

    /// <summary>The wreck obstacles in <paramref name="world"/>: every lifecycle (for "never a second"), and the Active ones.</summary>
    private static (int All, int Active, Vector3 At) Wrecks(EntityRepository world)
    {
        int all = 0, active = 0; Vector3 at = default;
        if (!world.IsComponentTypeRegistered<StaticObstacle>()) return (0, 0, at);
        foreach (var e in world.Query().With<StaticObstacle>().With<TkbIdentity>().WithLifecycle(EntityLifecycle.All).Build())
        {
            if ((long)world.GetComponentRO<TkbIdentity>(e).TkbType != WreckType) continue;
            var s = world.GetLifecycleState(e);
            if (s is not (EntityLifecycle.Active or EntityLifecycle.Constructing)) continue;
            all++;
            if (s == EntityLifecycle.Active) { active++; at = world.GetComponentRO<SimTransform>(e).Position; }
        }
        return (all, active, at);
    }

    [Fact(Timeout = 600_000)]
    public async Task CE6005_WreckBlocksStreet_AKilledTankLeavesOneWreck_ThatClosesTheGate_UntilACharge()
    {
        using var harness = await ScenarioTwin.Live("wreck-blocks-street", _staged, DomainId);
        var cgf = harness.Cgf!.World!;
        var sim = harness.SimHost.World!;
        var hosts = new[] { ("CGF", cgf), ("SimHost", sim), ("Player", harness.Player.App.World) };

        Assert.True(harness.PumpUntil(() => !ScenarioTwin.ByName(cgf, "Enemy Tank").IsNull && !ScenarioTwin.ByName(sim, "Enemy Tank").IsNull
                                            && sim.HasSingletonManaged<INavmeshProvider>(), timeoutFrames: 3000), "the T-72 must spawn");
        var t72 = ScenarioTwin.ByName(cgf, "Enemy Tank");

        // ① a live vehicle is not in the navmesh (P-7a O5): the gate is open
        Assert.True(VehicleGateOpen(sim, out var open0), $"① while the T-72 lives the gate is open to vehicles: {open0}");

        // ② the M1 kills it
        double? deadAt = null;
        harness.PumpUntil(() => { if (cgf.GetComponentRO<Health>(t72).Current <= 0f) deadAt ??= cgf.SimulationTime; return deadAt != null || cgf.SimulationTime > 58.0; }, timeoutFrames: 20000);
        _out.WriteLine($"T-72 dead at sim {deadAt}, health {cgf.GetComponentRO<Health>(t72).Current}");
        Assert.True(deadAt != null, $"② the M1 kills the T-72 before the charge man moves (health {cgf.GetComponentRO<Health>(t72).Current})");

        // ③ ONE wreck on every node, on the hulk — ④ the gate closes to vehicles
        int most = 0;
        string Count() => string.Join("; ", hosts.Select(h => { var w = Wrecks(h.Item2); return $"{h.Item1}: {w.All} ({w.Active} active)"; }));
        bool closed = false; string closedPath = "";
        harness.PumpUntil(() =>
        {
            foreach (var h in hosts) most = Math.Max(most, Wrecks(h.Item2).All);
            closed = hosts.All(h => Wrecks(h.Item2).Active == 1) && !VehicleGateOpen(sim, out closedPath);
            return closed || cgf.SimulationTime > 65.0;
        }, timeoutFrames: 20000);
        var wreckAt = Wrecks(sim).At;
        var hulkAt = sim.GetComponentRO<SimTransform>(ScenarioTwin.ByName(sim, "Enemy Tank")).Position;
        _out.WriteLine($"sim {cgf.SimulationTime:0.0}: wrecks {Count()}; wreck at {wreckAt}, hulk at {hulkAt}; vehicle path {closedPath}");
        Assert.True(closed, $"③④ one ACTIVE wreck on every node and the gate closed to vehicles: {Count()}; path {closedPath}");
        Assert.True(Vector2.Distance(new(wreckAt.X, wreckAt.Y), new(hulkAt.X, hulkAt.Y)) < 0.5f, $"③ the wreck sits on the hulk: {wreckAt} vs {hulkAt}");

        // ⑤ the charge clears it, the gate opens
        bool reopened = false; string openPath = "";
        harness.PumpUntil(() =>
        {
            foreach (var h in hosts) most = Math.Max(most, Wrecks(h.Item2).All);
            reopened = hosts.All(h => Wrecks(h.Item2).All == 0) && VehicleGateOpen(sim, out openPath);
            return reopened || cgf.SimulationTime > 110.0;
        }, timeoutFrames: 30000);
        _out.WriteLine($"sim {cgf.SimulationTime:0.0}: wrecks {Count()}; vehicle path {openPath}; most wrecks seen on one node {most}");
        Assert.True(reopened, $"⑤ the charge removes the wreck on every node and the gate opens: {Count()}; path {openPath}");
        Assert.Equal(1, most);   // ③ never a second wreck, on any node, at any time

        // ⑥ the hulk stays
        Assert.All(hosts, h => Assert.False(ScenarioTwin.ByName(h.Item2, "Enemy Tank").IsNull, $"⑥ the hulk stays on {h.Item1}"));
    }
}
