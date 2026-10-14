using System;
using System.IO;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Fdp.Core;
using Fdp.Toolkit.Behavior;
using Fdp.Toolkit.Behavior.Components;
using Hrot.Common.Orders;
using Hrot.NED.Descriptors.Orchestration;
using Xunit;
using Xunit.Abstractions;
using ClusterOpType = Hrot.NED.Descriptors.Orchestration.ClusterOpType;

namespace Hrot.ClusterRunner.Integration.Tests;

/// <summary>
/// ⭐⭐ CE-3167 (docs/DESIGN_Entity_Control.md §2.3, R-271 (backend)) — the SECOND rail of the order path, after the networkless
/// editor one (<see cref="DebugApiOrdersTests"/>): an order issued on a node that does NOT own the unit's brain travels as an
/// FDP event → the tactical-intent egress translator → DDS → the brain owner's ingress, and the unit runs it there.
/// </summary>
[Collection("HeavyE2ETests")]
public sealed class EntityOrdersAcrossNodesTests : IDisposable
{
    private const int DomainBase = 71;    // 52–58 posture/shots/weapons/grenade, 60–69 sensors, 81 save (grep of DomainBase, 2026-10-11)
    private static int _domainSeq = DomainBase - 1;
    private static int NextDomainId() => Interlocked.Increment(ref _domainSeq);

    private readonly string _scenarioId = "orders_" + Guid.NewGuid().ToString("N");
    private readonly ITestOutputHelper _out;

    public EntityOrdersAcrossNodesTests(ITestOutputHelper output) => _out = output;
    public void Dispose() => NasScenarioStaging.Remove(_scenarioId);

    private static string RepoRoot()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir != null; dir = dir.Parent)
            if (Directory.Exists(Path.Combine(dir.FullName, "Hrot", "Subsystems"))) return dir.FullName;
        throw new DirectoryNotFoundException("repo root not found above " + AppContext.BaseDirectory);
    }

    private static Entity ByName(EntityRepository world, string name)
    {
        for (int i = 0; i <= world.MaxEntityIndex; i++)
        {
            var e = world.GetEntityByIndex(i);
            if (!world.IsAlive(e) || !world.HasComponent<EntityInfo>(e)) continue;
            if (world.GetComponent<EntityInfo>(e).Name.ToString() == name) return e;
        }
        return Entity.Null;
    }

    [Fact(Timeout = 600_000)]
    public async Task CE3167_AnOrderIssuedOnTheMuscleNode_RunsOnTheBrainNode()
    {
        Directory.CreateDirectory(NasScenarioStaging.DirectoryOf(_scenarioId));
        File.Copy(Path.Combine(RepoRoot(), "scenarios", "ua-grenade-or-rifle", "scenario.json"),
            Path.Combine(NasScenarioStaging.DirectoryOf(_scenarioId), "scenario.json"), overwrite: true);

        using var harness = new HrotRunnerHarness("simhost,ig,excon,cgf", NextDomainId());
        var master = harness.OrchestratorSvc.TestHook_ClusterMaster!;
        var rosterDeadline = DateTime.UtcNow.AddSeconds(10.0);
        while (master.NodeRoster.ActiveNodes.Count == 0 && DateTime.UtcNow < rosterDeadline) { harness.PumpFrames(1); Thread.Sleep(10); }
        await master.HandleClusterOpRequestAsync(new ClusterOpRequest
        {
            RequestId     = Guid.NewGuid(),
            OperationType = ClusterOpType.TransitionState,
            PayloadJson   = JsonSerializer.Serialize(new { TargetState = nameof(ClusterState.OperatingLive), ScenarioId = _scenarioId }),
        }).ConfigureAwait(false);
        Assert.True(harness.PumpUntil(() => (int)master.CurrentClusterState == 31, timeoutFrames: 4000), "cluster must reach OperatingLive");

        var cgf = harness.Cgf!.World!;
        var sim = harness.SimHost.World!;
        Assert.True(harness.PumpUntil(() => !ByName(cgf, "Rifleman").IsNull && !ByName(sim, "Rifleman").IsNull, timeoutFrames: 2000),
            "the Rifleman must exist on both nodes");
        var onBrain = ByName(cgf, "Rifleman");
        _out.WriteLine($"CGF registers BehaviorState: {cgf.IsComponentTypeRegistered<BehaviorState>()}; SimHost: {sim.IsComponentTypeRegistered<BehaviorState>()}");
        Assert.True(cgf.IsComponentTypeRegistered<BehaviorState>() && cgf.HasComponent<BehaviorState>(onBrain),
            "the CGF node must hold the Rifleman's brain");
        var onMuscle = ByName(sim, "Rifleman");
        var simMap = harness.SimHost.App.TestHook_EntityMap;
        Assert.True(simMap.TryGetNetworkId(onMuscle, out long id), "the Rifleman has a network id on SimHost");
        Assert.False(sim.IsComponentTypeRegistered<BehaviorState>() && sim.HasAuthority<BehaviorState>(onMuscle),
            "SimHost must NOT own the Rifleman's brain — otherwise this rail proves nothing about the network");

        // ⭐ the order is published on SimHost's own bus — exactly what an HTTP order on that node does
        var result = new EntityOrders(sim, simMap, geo: null).Hold(id);
        Assert.True(result.Accepted, result.Detail);

        int idle = BehaviorHash.FromName(Hrot.Map.Definitions.Behavior.BehaviorNames.PersonHold);   // ⭐ CE-2143 — the Rifleman's class runs ITS hold
        bool ran = harness.PumpUntil(() =>
        {
            var bs = cgf.GetComponentRO<BehaviorState>(onBrain);
            return bs.ActiveBehaviorHash == idle && bs.Origin == BehaviorOrigin.Operator;
        }, timeoutFrames: 600);
        var now = cgf.GetComponentRO<BehaviorState>(onBrain);
        _out.WriteLine($"brain node runs hash {now.ActiveBehaviorHash} origin {now.Origin}; Idle = {idle}");
        Assert.True(ran, "the order issued on SimHost must reach the CGF brain and run there at origin Operator");
    }

    /// <summary>
    /// ⭐⭐ CE-3172 / CE-3173 S4e (docs/DESIGN_Entity_Control.md §8c) — possession across the cluster: SimHost is the STATION (it does
    /// not own the brain). Its request, frames and release cross as interaction cases to CGF; CGF runs <c>OperatorControl</c> at origin
    /// Possession; <c>dtPossessedBy</c> tells every node who holds the unit; <c>dtManualDrive</c> + <c>dtNavigationIntent</c> (Manual)
    /// drive the body on SimHost; a release resumes the brain's task and every node forgets the hold.
    /// </summary>
    [Fact(Timeout = 600_000)]
    public async Task CE3172_ASoldierPossessedFromAnotherNode_IsDrivenThere_AndReleaseResumesItsTask()
    {
        Directory.CreateDirectory(NasScenarioStaging.DirectoryOf(_scenarioId));
        File.Copy(Path.Combine(RepoRoot(), "scenarios", "ua-grenade-or-rifle", "scenario.json"),
            Path.Combine(NasScenarioStaging.DirectoryOf(_scenarioId), "scenario.json"), overwrite: true);

        using var harness = new HrotRunnerHarness("simhost,ig,excon,cgf", NextDomainId());
        var master = harness.OrchestratorSvc.TestHook_ClusterMaster!;
        var rosterDeadline = DateTime.UtcNow.AddSeconds(10.0);
        while (master.NodeRoster.ActiveNodes.Count == 0 && DateTime.UtcNow < rosterDeadline) { harness.PumpFrames(1); Thread.Sleep(10); }
        await master.HandleClusterOpRequestAsync(new ClusterOpRequest
        {
            RequestId     = Guid.NewGuid(),
            OperationType = ClusterOpType.TransitionState,
            PayloadJson   = JsonSerializer.Serialize(new { TargetState = nameof(ClusterState.OperatingLive), ScenarioId = _scenarioId }),
        }).ConfigureAwait(false);
        Assert.True(harness.PumpUntil(() => (int)master.CurrentClusterState == 31, timeoutFrames: 4000), "cluster must reach OperatingLive");

        var cgf = harness.Cgf!.World!;
        var sim = harness.SimHost.World!;
        var ig = harness.Ig.App.World;
        Assert.True(harness.PumpUntil(() => !ByName(cgf, "Rifleman").IsNull && !ByName(sim, "Rifleman").IsNull, timeoutFrames: 2000),
            "the Rifleman must exist on both nodes");
        var onBrain = ByName(cgf, "Rifleman");
        var onMuscle = ByName(sim, "Rifleman");
        var simMap = harness.SimHost.App.TestHook_EntityMap;
        Assert.True(simMap.TryGetNetworkId(onMuscle, out long id), "the Rifleman has a network id on SimHost");
        var station = new EntityOrders(sim, simMap, geo: null);
        int operatorControl = BehaviorHash.FromName("OperatorControl");

        // possess from SimHost: CGF runs OperatorControl at origin Possession, and SimHost learns it holds the unit
        Assert.True(station.Possess(id).Accepted);
        bool possessed = harness.PumpUntil(() =>
        {
            var bs = cgf.GetComponentRO<BehaviorState>(onBrain);
            return bs.ActiveBehaviorHash == operatorControl && bs.Origin == BehaviorOrigin.Possession && station.HolderOf(id) is not null;
        }, timeoutFrames: 600);
        _out.WriteLine($"brain runs {cgf.GetComponentRO<BehaviorState>(onBrain).ActiveBehaviorHash} ({cgf.GetComponentRO<BehaviorState>(onBrain).Origin}); holder seen on SimHost {station.HolderOf(id)}");
        Assert.True(possessed, "the possession must reach the CGF brain, and dtPossessedBy must come back to the station");

        // drive: frames from SimHost; the axes come back from CGF as dtManualDrive and the body walks on SimHost
        var start = sim.GetComponentRO<SimTransform>(onMuscle).Position;
        uint seq = 1;
        bool moved = harness.PumpUntil(() =>
        {
            station.Control(id, new Fdp.Toolkit.Behavior.Possession.OperatorControlFrameEvent { Seq = seq++, Forward = 1f, Yaw = 0f });
            var p = sim.GetComponentRO<SimTransform>(onMuscle).Position;
            return System.Numerics.Vector2.Distance(new(p.X, p.Y), new(start.X, start.Y)) > 1.5f;
        }, timeoutFrames: 1500);
        var end = sim.GetComponentRO<SimTransform>(onMuscle).Position;
        _out.WriteLine($"body {start} -> {end} after {seq} frames; ManualDrive on SimHost: {(sim.HasComponent<Fdp.Toolkit.Navigation.ManualDrive>(onMuscle) ? sim.GetComponentRO<Fdp.Toolkit.Navigation.ManualDrive>(onMuscle).Forward : -9f)}");
        Assert.True(moved, $"the possessed body must walk on SimHost: {start} -> {end}");

        var onIg = ByName(ig, "Rifleman");
        bool igSeesIt = !onIg.IsNull && harness.PumpUntil(() => ig.IsComponentTypeRegistered<Fdp.Toolkit.Behavior.Possession.PossessedBy>()
            && ig.HasComponent<Fdp.Toolkit.Behavior.Possession.PossessedBy>(onIg), timeoutFrames: 300);
        Assert.True(igSeesIt, "the IG must see who holds the Rifleman (dtPossessedBy on a pure IG)");

        // release: the brain's task resumes, every node forgets the hold, the body stops being driven
        Assert.True(station.Unpossess(id).Accepted);
        bool released = harness.PumpUntil(() =>
            cgf.GetComponentRO<BehaviorState>(onBrain).Origin != BehaviorOrigin.Possession
            && station.HolderOf(id) is null
            && !ig.HasComponent<Fdp.Toolkit.Behavior.Possession.PossessedBy>(onIg), timeoutFrames: 600);
        Assert.True(released, $"released: brain origin {cgf.GetComponentRO<BehaviorState>(onBrain).Origin}, holder on SimHost {station.HolderOf(id)}");
        Assert.True(harness.PumpUntil(() => !sim.HasComponent<Fdp.Toolkit.Navigation.ManualDrive>(onMuscle)
            || sim.GetComponentRO<Fdp.Toolkit.Navigation.ManualDrive>(onMuscle).Forward == 0f, timeoutFrames: 300),
            "the brain's let-go must reach the muscle: no axes left after the release");
    }

    /// <summary>
    /// ⭐⭐ <c>C3</c> (handoff 2; DESIGN_FPV_Drone §6, Motion Domains §5; R-3005) — an FPV DRONE possessed from another node flies there:
    /// SimHost is the station; its frames cross to CGF as <c>ControlFrameInteractionPayload</c> (with the B10 <c>Vertical</c> axis);
    /// CGF's <c>OperatorControl</c> writes <c>ManualDrive</c> + <c>NavigationIntent.Manual</c>, which come back as dtManualDrive /
    /// dtNavigationIntent; SimHost — the ONE Muscle role, <c>MuscleGround</c>, owns the drone's position (R-3005) — climbs and flies it
    /// through <c>AirMotionSystem</c> → <c>RotorcraftModel.Manual</c>. 🔒 User: <i>"Drone will need to be controllable also from FPS
    /// possess mode."</i> The scenario is <c>ua-grenade-or-rifle</c> plus one drone (no drone scenario ships yet).
    /// </summary>
    [Fact(Timeout = 600_000)]
    public async Task M7_APossessedDrone_FollowsTheStation_AcrossNodes()
    {
        Directory.CreateDirectory(NasScenarioStaging.DirectoryOf(_scenarioId));
        var scenario = System.Text.Json.Nodes.JsonNode.Parse(File.ReadAllText(Path.Combine(RepoRoot(), "scenarios", "ua-grenade-or-rifle", "scenario.json")))!;
        scenario["entities"]!["a4120000-0000-4000-8000-0000000000d1"] = System.Text.Json.Nodes.JsonNode.Parse("""
            {
              "SimTransform": { "Position": [120, 0, 20], "Rotation": [0, 0, 0, 1] },
              "Health": { "Current": 10, "Max": 10 },
              "NetworkIdentity": { "Value": 7790 },
              "TkbIdentity": { "TkbType": 403 },
              "EntityInfo": { "Name": "FPV", "ForceId": "Hostile" }
            }
            """);
        File.WriteAllText(Path.Combine(NasScenarioStaging.DirectoryOf(_scenarioId), "scenario.json"), scenario.ToJsonString());

        using var harness = new HrotRunnerHarness("simhost,ig,excon,cgf", NextDomainId());
        var master = harness.OrchestratorSvc.TestHook_ClusterMaster!;
        var rosterDeadline = DateTime.UtcNow.AddSeconds(10.0);
        while (master.NodeRoster.ActiveNodes.Count == 0 && DateTime.UtcNow < rosterDeadline) { harness.PumpFrames(1); Thread.Sleep(10); }
        await master.HandleClusterOpRequestAsync(new ClusterOpRequest
        {
            RequestId     = Guid.NewGuid(),
            OperationType = ClusterOpType.TransitionState,
            PayloadJson   = JsonSerializer.Serialize(new { TargetState = nameof(ClusterState.OperatingLive), ScenarioId = _scenarioId }),
        }).ConfigureAwait(false);
        Assert.True(harness.PumpUntil(() => (int)master.CurrentClusterState == 31, timeoutFrames: 4000), "cluster must reach OperatingLive");

        var cgf = harness.Cgf!.World!;
        var sim = harness.SimHost.World!;
        Assert.True(harness.PumpUntil(() => !ByName(cgf, "FPV").IsNull && !ByName(sim, "FPV").IsNull, timeoutFrames: 2000),
            "the drone must exist on both nodes");
        var onBrain = ByName(cgf, "FPV");
        var onMuscle = ByName(sim, "FPV");
        var simMap = harness.SimHost.App.TestHook_EntityMap;
        Assert.True(simMap.TryGetNetworkId(onMuscle, out long id), "the drone has a network id on SimHost");
        Assert.True(harness.PumpUntil(() => sim.HasComponent<Fdp.Toolkit.Motion.FlightState>(onMuscle), timeoutFrames: 300),
            "SimHost provisioned the drone's flight state (Gen.Flight)");
        var station = new EntityOrders(sim, simMap, geo: null);
        int operatorControl = BehaviorHash.FromName("OperatorControl");

        Assert.True(station.Possess(id).Accepted);
        bool possessed = harness.PumpUntil(() =>
        {
            var bs = cgf.GetComponentRO<BehaviorState>(onBrain);
            return bs.ActiveBehaviorHash == operatorControl && bs.Origin == BehaviorOrigin.Possession && station.HolderOf(id) is not null;
        }, timeoutFrames: 600);
        Assert.True(possessed, "the possession must reach the CGF brain, and dtPossessedBy must come back to the station");

        // climb: the Vertical axis only — the drone rises on SimHost
        var start = sim.GetComponentRO<SimTransform>(onMuscle).Position;
        uint seq = 1;
        bool climbed = harness.PumpUntil(() =>
        {
            station.Control(id, new Fdp.Toolkit.Behavior.Possession.OperatorControlFrameEvent { Seq = seq++, Vertical = 1f });
            return sim.GetComponentRO<SimTransform>(onMuscle).Position.Z > start.Z + 2f;
        }, timeoutFrames: 1500);
        var afterClimb = sim.GetComponentRO<SimTransform>(onMuscle).Position;
        _out.WriteLine($"drone {start} -> {afterClimb} after {seq} frames; ManualDrive.Vertical on SimHost: "
            + $"{(sim.HasComponent<Fdp.Toolkit.Navigation.ManualDrive>(onMuscle) ? sim.GetComponentRO<Fdp.Toolkit.Navigation.ManualDrive>(onMuscle).Vertical : -9f)}");
        Assert.True(climbed, $"the possessed drone must climb on SimHost: {start} -> {afterClimb}");

        // fly: forward at the same height
        bool flew = harness.PumpUntil(() =>
        {
            station.Control(id, new Fdp.Toolkit.Behavior.Possession.OperatorControlFrameEvent { Seq = seq++, Forward = 1f });
            var p = sim.GetComponentRO<SimTransform>(onMuscle).Position;
            return System.Numerics.Vector2.Distance(new(p.X, p.Y), new(afterClimb.X, afterClimb.Y)) > 3f;
        }, timeoutFrames: 1500);
        Assert.True(flew, $"the possessed drone must fly forward on SimHost: {afterClimb} -> {sim.GetComponentRO<SimTransform>(onMuscle).Position}");
        Assert.Equal(1, sim.GetComponentRO<Fdp.Toolkit.Motion.FlightState>(onMuscle).Airborne);

        Assert.True(station.Unpossess(id).Accepted);
        Assert.True(harness.PumpUntil(() => cgf.GetComponentRO<BehaviorState>(onBrain).Origin != BehaviorOrigin.Possession
            && station.HolderOf(id) is null, timeoutFrames: 600), "released");
    }
}
