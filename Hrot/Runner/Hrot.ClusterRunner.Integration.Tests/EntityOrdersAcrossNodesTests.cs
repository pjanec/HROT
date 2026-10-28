using System;
using System.IO;
using System.Linq;
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

    // ⭐ CE-4089 — the one repository walk-up (ContentLayout), not a private copy.
    private static string RepoRoot()
        => Fdp.Toolkit.Orchestration.ContentLayout.TryFindSourceTree() ?? throw new DirectoryNotFoundException("repo root not found above " + AppContext.BaseDirectory);

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

        // ⭐ the order is published on SimHost's own bus — exactly what an HTTP order on that node does — with production's explicit
        //   event registration ON (Program.cs sets it): 🔴 without it this rail was blind to SimHost never registering the intent event
        //   (live: POST /entities/{id}/order on the SimHost perspective → 500 "published without being explicitly registered")
        bool previousStrict = FdpConfig.EnforceExplicitEventRegistration;
        FdpConfig.EnforceExplicitEventRegistration = true;
        OrderResult result;
        try { result = new EntityOrders(sim, simMap, geo: null).Hold(id); }
        finally { FdpConfig.EnforceExplicitEventRegistration = previousStrict; }
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

        // ⭐ CE-1148 S3 (docs/DESIGN_Node_Roles_And_Policies.md §3.5) — the IG station takes the order egress from the same auxiliary
        //   pack as every node: a release given on the IG reaches the brain and ends the operator's order there.
        var ig = harness.Ig.App.World;
        var igMap = harness.Ig.App.TestHook_EntityMap;
        Assert.True(harness.PumpUntil(() => igMap.TryGetEntity(id, out _), timeoutFrames: 600), "the Rifleman must exist on the IG");
        previousStrict = FdpConfig.EnforceExplicitEventRegistration;
        FdpConfig.EnforceExplicitEventRegistration = true;
        try { result = new EntityOrders(ig, igMap, geo: null).Release(id); }
        finally { FdpConfig.EnforceExplicitEventRegistration = previousStrict; }
        Assert.True(result.Accepted, result.Detail);
        Assert.True(harness.PumpUntil(() => cgf.GetComponentRO<BehaviorState>(onBrain).Origin != BehaviorOrigin.Operator, timeoutFrames: 600),
            "a release issued on the IG must reach the CGF brain and end the operator's order");
    }

    /// <summary>
    /// ⭐⭐ CE-3251 (BR-13, docs/DESIGN_Entity_Control.md §2.3a) — an ROE change raised on a node that does NOT own the unit's brain
    /// travels the order's road (kind SetRoe) to the brain's owner, whose RoeSystem gate applies it: CGF's Roe turns HoldFire; a
    /// LOWER origin sent the same way is refused there. ⛔ Before BR-13 the event was applied to the local replica only.
    /// </summary>
    [Fact(Timeout = 600_000)]
    public async Task CE3251_AnRoeSetOnAnotherNode_ReachesTheBrainsOwner_AndItsGate()
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
        var onMuscle = ByName(sim, "Rifleman");
        Assert.True(cgf.HasComponent<BehaviorState>(onBrain) && cgf.HasAuthority<BehaviorState>(onBrain), "CGF must own the Rifleman's brain");
        Assert.False(sim.IsComponentTypeRegistered<BehaviorState>() && sim.HasAuthority<BehaviorState>(onMuscle),
            "SimHost must NOT own the Rifleman's brain — otherwise this rail proves nothing about the network");

        Roe RoeOnBrain() => cgf.HasComponent<Roe>(onBrain) ? cgf.GetComponentRO<Roe>(onBrain) : default;
        void RaiseOnSimHost(RoeFire fire, BehaviorOrigin origin)
        {
            if (!sim.Bus.IsRegistered<Fdp.Toolkit.Behavior.Events.SetRoeEvent>()) sim.Bus.Register<Fdp.Toolkit.Behavior.Events.SetRoeEvent>();
            sim.Bus.Publish(new Fdp.Toolkit.Behavior.Events.SetRoeEvent { Entity = onMuscle, Fire = fire, Origin = origin });
        }

        RaiseOnSimHost(RoeFire.HoldFire, BehaviorOrigin.Operator);
        bool held = harness.PumpUntil(() => RoeOnBrain().Fire == RoeFire.HoldFire, timeoutFrames: 600);
        _out.WriteLine($"CGF Roe: {RoeOnBrain().Fire} set by {RoeOnBrain().SetBy}");
        Assert.True(held, "an ROE raised on SimHost must reach the CGF brain and change its Roe");
        Assert.Equal(BehaviorOrigin.Operator, RoeOnBrain().SetBy);

        RaiseOnSimHost(RoeFire.FireAtWill, BehaviorOrigin.Superior);   // lower than the operator who set it
        harness.PumpFrames(120);
        Assert.Equal(RoeFire.HoldFire, RoeOnBrain().Fire);
    }

    /// <summary>
    /// ⭐⭐ CE-2299 (behaviors, Command Chain CC-2; the CE3251 twin) — a superior's reporting requirements raised on a node that does NOT own
    /// the unit's brain travel the order's road as kind 3 to the brain's owner, whose ReportingRequirementsSystem gate applies them; a
    /// LOWER origin sent the same way is refused there.
    /// </summary>
    [Fact(Timeout = 600_000)]
    public async Task CE2299_ReportingSetOnAnotherNode_ReachesTheBrainsOwner_AndItsGate()
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
        var onMuscle = ByName(sim, "Rifleman");
        Assert.True(cgf.HasComponent<BehaviorState>(onBrain) && cgf.HasAuthority<BehaviorState>(onBrain), "CGF must own the Rifleman's brain");

        Fdp.Toolkit.Squad.Command.ReportingRequirements OnBrain()
            => cgf.HasComponent<Fdp.Toolkit.Squad.Command.ReportingRequirements>(onBrain)
                ? cgf.GetComponentRO<Fdp.Toolkit.Squad.Command.ReportingRequirements>(onBrain) : default;
        void RaiseOnSimHost(float sitrep, BehaviorOrigin origin)
        {
            if (!sim.Bus.IsRegistered<Fdp.Toolkit.Squad.Command.SetReportingEvent>()) sim.Bus.Register<Fdp.Toolkit.Squad.Command.SetReportingEvent>();
            sim.Bus.Publish(new Fdp.Toolkit.Squad.Command.SetReportingEvent
            {
                Entity = onMuscle, Origin = origin, SitrepEverySeconds = sitrep, Pir = Fdp.Toolkit.Squad.Command.PirFlags.AnyElement,
            });
        }

        RaiseOnSimHost(15f, BehaviorOrigin.Operator);
        bool set = harness.PumpUntil(() => OnBrain().SitrepEverySeconds == 15f, timeoutFrames: 600);
        _out.WriteLine($"CGF requirements: sitrep {OnBrain().SitrepEverySeconds} pir {OnBrain().Pir} set by {OnBrain().SetBy}");
        Assert.True(set, "requirements raised on SimHost must reach the CGF brain");
        Assert.Equal((Fdp.Toolkit.Squad.Command.PirFlags.AnyElement, BehaviorOrigin.Operator), (OnBrain().Pir, OnBrain().SetBy));

        RaiseOnSimHost(60f, BehaviorOrigin.Superior);   // lower than the operator who set them
        harness.PumpFrames(120);
        Assert.Equal(15f, OnBrain().SitrepEverySeconds);
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
    /// ⭐⭐ <c>R-4096</c> (docs/DESIGN_Entity_Control.md C-3′, §8q; Cluster Membership UC-18) — TWO players, two nodes: SimHost possesses the
    /// Rifleman (his brain is on CGF); the IG, a second STATION, then asks for the same man. ① The IG answers at once from the replicated
    /// <c>dtPossessedBy</c>: refused, naming the holder. ② A request that reaches the brain anyway (the race of two stations — published on
    /// the IG's bus, carried by the interaction transport) is ignored: the holder is still SimHost's, its epoch unchanged and its frames
    /// still drive the man. ③ Once SimHost lets him go the IG takes him — a free unit is free. ✅ Red-proof: <c>PossessionSystem</c> without
    /// its <c>StationClaims</c> guard (the old C-3) ⇒ step ② ends with the IG holding the man.
    /// </summary>
    [Fact(Timeout = 600_000)]
    public async Task R4096_ASecondStationOnAnotherNode_IsRefused_TheFirstKeepsTheMan_UntilHeLetsGo()
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
        Assert.True(harness.PumpUntil(() => !ByName(cgf, "Rifleman").IsNull && !ByName(sim, "Rifleman").IsNull && !ByName(ig, "Rifleman").IsNull,
            timeoutFrames: 2000), "the Rifleman must exist on every node");
        var onBrain = ByName(cgf, "Rifleman");
        var onMuscle = ByName(sim, "Rifleman");
        var onIg = ByName(ig, "Rifleman");
        var simMap = harness.SimHost.App.TestHook_EntityMap;
        var igMap = harness.Ig.App.TestHook_EntityMap;
        Assert.True(simMap.TryGetNetworkId(onMuscle, out long id), "the Rifleman has a network id on SimHost");
        var first = new EntityOrders(sim, simMap, geo: null);
        var second = new EntityOrders(ig, igMap, geo: null);
        long firstStation = first.StationFor(onMuscle), secondStation = second.StationFor(onIg);
        Assert.NotEqual(firstStation, secondStation);

        Assert.True(first.Possess(id).Accepted);
        Assert.True(harness.PumpUntil(() => cgf.HasComponent<Fdp.Toolkit.Behavior.Possession.PossessedBy>(onBrain)
            && second.HolderOf(id) == firstStation, timeoutFrames: 900), "SimHost's hold must reach the CGF brain and then the IG");
        uint epoch = cgf.GetComponentRO<Fdp.Toolkit.Behavior.Possession.PossessedBy>(onBrain).Epoch;

        // ① the second station is told at once, from replicated state
        var refused = second.Possess(id);
        Assert.Equal(OrderRefusal.Possessed, refused.Refusal);
        Assert.Contains($"station {firstStation}", refused.Detail);

        // ② a request that arrives at the brain anyway (published on the IG's bus, carried to CGF) is ignored
        if (!ig.Bus.IsRegistered<Fdp.Toolkit.Behavior.Possession.PossessRequestEvent>()) ig.Bus.Register<Fdp.Toolkit.Behavior.Possession.PossessRequestEvent>();
        ig.Bus.Publish(new Fdp.Toolkit.Behavior.Possession.PossessRequestEvent
        {
            Unit = onIg, Mode = Fdp.Toolkit.Behavior.Possession.PossessionMode.Fps, StationNodeId = secondStation,
        });
        var start = sim.GetComponentRO<SimTransform>(onMuscle).Position;
        uint seq = 1;
        bool moved = harness.PumpUntil(() =>
        {
            first.Control(id, new Fdp.Toolkit.Behavior.Possession.OperatorControlFrameEvent { Seq = seq++, Forward = 1f, Yaw = 0f });
            var p = sim.GetComponentRO<SimTransform>(onMuscle).Position;
            return System.Numerics.Vector2.Distance(new(p.X, p.Y), new(start.X, start.Y)) > 1.5f;
        }, timeoutFrames: 1500);
        Assert.True(moved, "the holder's frames must still drive the man after the second station's request");
        var held = cgf.GetComponentRO<Fdp.Toolkit.Behavior.Possession.PossessedBy>(onBrain);
        Assert.Equal((firstStation, epoch), (held.StationNodeId, held.Epoch));          // never taken, never re-taken
        Assert.Equal(firstStation, second.HolderOf(id));

        // ③ he lets go; the second station may now have him
        Assert.True(first.Unpossess(id).Accepted);
        Assert.True(harness.PumpUntil(() => second.HolderOf(id) is null && first.HolderOf(id) is null, timeoutFrames: 600), "released on every node");
        Assert.True(second.Possess(id).Accepted);
        Assert.True(harness.PumpUntil(() => cgf.HasComponent<Fdp.Toolkit.Behavior.Possession.PossessedBy>(onBrain)
            && cgf.GetComponentRO<Fdp.Toolkit.Behavior.Possession.PossessedBy>(onBrain).StationNodeId == secondStation, timeoutFrames: 900),
            "the second station holds him once the first let go");
    }

    /// <summary>
    /// ⭐⭐ <c>CE-1154</c> (docs/DESIGN_Entity_Control.md §8h U-2, U-7, §8h.6) — the USE key across the cluster: SimHost is the station;
    /// its frame's use press (counter + the door's network id) crosses to CGF, whose <c>OperatorControl</c> asks the shared rule and
    /// starts the door action on the soldier's interaction channel; the door's state comes back to SimHost, and the brain's verdict
    /// (<c>dtOperatorUseStatus</c>) reaches the station AND the IG (the live IG check the backend's <c>CE-1153</c> owed).
    /// </summary>
    [Fact(Timeout = 600_000)]
    public async Task CE1154_AUsePressOnTheStation_TogglesADoorThroughTheBrain_AndTheVerdictReachesTheStationAndTheIg()
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
        Assert.True(station.Possess(id).Accepted);
        Assert.True(harness.PumpUntil(() =>
        {
            var bs = cgf.GetComponentRO<BehaviorState>(onBrain);
            return bs.ActiveBehaviorHash == operatorControl && bs.Origin == BehaviorOrigin.Possession && station.HolderOf(id) is not null;
        }, timeoutFrames: 600), "the possession must reach the CGF brain");

        // the bt-range door nearest the Rifleman, as SimHost sees it
        var me = sim.GetComponentRO<SimTransform>(onMuscle).Position;
        Entity door = Entity.Null;
        float best = float.MaxValue;
        for (int i = 0; i <= sim.MaxEntityIndex; i++)
        {
            var e = sim.GetEntityByIndex(i);
            if (!sim.IsAlive(e) || !sim.IsComponentTypeRegistered<Fdp.Toolkit.Terrain.DoorState>() || !sim.HasComponent<Fdp.Toolkit.Terrain.DoorState>(e)) continue;
            float d = System.Numerics.Vector3.Distance(sim.GetComponentRO<SimTransform>(e).Position, me);
            if (d < best) { best = d; door = e; }
        }
        Assert.False(door.IsNull, "bt-range must ship doors");
        Assert.True(simMap.TryGetNetworkId(door, out long doorId), "a door is a network entity");
        var before = sim.GetComponentRO<Fdp.Toolkit.Terrain.DoorState>(door).State;
        var expected = before == Fdp.Toolkit.Terrain.TerrainDoorState.Open ? Fdp.Toolkit.Terrain.TerrainDoorState.Closed : Fdp.Toolkit.Terrain.TerrainDoorState.Open;
        _out.WriteLine($"door {doorId} at {sim.GetComponentRO<SimTransform>(door).Position} is {before}; the Rifleman at {me}, {best:0.0} m");

        // stand him beside it (SimHost owns his pose) and hold still until the brain's copy is within reach
        var doorAt = sim.GetComponentRO<SimTransform>(door).Position;
        uint seq = 1;
        Assert.True(harness.PumpUntil(() =>
        {
            sim.GetComponentRW<SimTransform>(onMuscle).Position = doorAt + new System.Numerics.Vector3(0.8f, 0f, 0f);
            station.Control(id, new Fdp.Toolkit.Behavior.Possession.OperatorControlFrameEvent { Seq = seq++ });
            var a = cgf.GetComponentRO<SimTransform>(onBrain).Position;
            return System.Numerics.Vector2.Distance(new(a.X, a.Y), new(doorAt.X, doorAt.Y)) <= Fdp.Toolkit.Terrain.DoorRules.ReachMetres - 0.5f;
        }, timeoutFrames: 600), "the brain must see the Rifleman beside the door");

        // ONE use press: the counter goes to 1 and stays there in every later frame
        bool toggled = harness.PumpUntil(() =>
        {
            sim.GetComponentRW<SimTransform>(onMuscle).Position = doorAt + new System.Numerics.Vector3(0.8f, 0f, 0f);
            station.Control(id, new Fdp.Toolkit.Behavior.Possession.OperatorControlFrameEvent { Seq = seq++, UseSeq = 1, UseTarget = doorId });
            return sim.GetComponentRO<Fdp.Toolkit.Terrain.DoorState>(door).State == expected
                && sim.IsComponentTypeRegistered<Fdp.Toolkit.Behavior.Possession.OperatorUseStatus>()
                && sim.HasComponent<Fdp.Toolkit.Behavior.Possession.OperatorUseStatus>(onMuscle)
                && sim.GetComponentRO<Fdp.Toolkit.Behavior.Possession.OperatorUseStatus>(onMuscle) is { UseSeq: 1, Outcome: Fdp.Toolkit.Behavior.Possession.UseOutcome.Done };
        }, timeoutFrames: 900);
        var onBrainStatus = cgf.HasComponent<Fdp.Toolkit.Behavior.Possession.OperatorUseStatus>(onBrain) ? cgf.GetComponentRO<Fdp.Toolkit.Behavior.Possession.OperatorUseStatus>(onBrain) : default;
        _out.WriteLine($"door now {sim.GetComponentRO<Fdp.Toolkit.Terrain.DoorState>(door).State}; verdict on CGF {onBrainStatus.UseSeq}/{onBrainStatus.ActionId}/{onBrainStatus.Outcome}/{onBrainStatus.Reason}");
        Assert.True(toggled, "one use press on the station must toggle the door through the brain, and its verdict (Done) must come back to SimHost");

        var onIg = ByName(ig, "Rifleman");
        Assert.True(!onIg.IsNull && harness.PumpUntil(() => ig.IsComponentTypeRegistered<Fdp.Toolkit.Behavior.Possession.OperatorUseStatus>()
            && ig.HasComponent<Fdp.Toolkit.Behavior.Possession.OperatorUseStatus>(onIg)
            && ig.GetComponentRO<Fdp.Toolkit.Behavior.Possession.OperatorUseStatus>(onIg).Outcome == Fdp.Toolkit.Behavior.Possession.UseOutcome.Done, timeoutFrames: 300),
            "the IG must receive the verdict too (dtOperatorUseStatus on a pure IG)");
    }

    /// <summary>
    /// ⭐⭐ <c>CE-1158</c> (docs/DESIGN_Entity_Control.md §8h U-4, §8h.9) — the use key's DRIVE and GET OUT across the cluster, with a pure
    /// IG as the station (the user's "CGF + IG" run): the IG possesses a soldier brained on CGF; one use press (Drive, the IFV) boards him in
    /// the technical's driver seat through his brain; the IG then follows <c>PossessionStation</c>'s hand-over exactly — Riding the soldier once
    /// <see cref="Hrot.UI.Common.Map3D.PossessionStation.InDriverSeat"/> says so on the IG, then possessing the IFV — and drives it on SimHost
    /// with the rider still held; Get out reverses it (possess the rider back as Fps, which releases the IFV; one press). Scenario
    /// <c>ua-tank-ammo</c> (Rifleman 1 and the Technical, one force; the pickup has a real, empty Driver seat — the Bradley does NOT: its crew
    /// is abstract, R-2009 B5, so E there offers Board).
    /// </summary>
    [Fact(Timeout = 600_000)]
    public async Task CE1158_DriveAndGetOut_FromAnIgStation_AcrossTheCluster()
    {
        Directory.CreateDirectory(NasScenarioStaging.DirectoryOf(_scenarioId));
        File.Copy(Path.Combine(RepoRoot(), "scenarios", "ua-tank-ammo", "scenario.json"),
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
        const string Man = "Rifleman 1", Ifv = "Technical";
        Assert.True(harness.PumpUntil(() => !ByName(cgf, Man).IsNull && !ByName(sim, Man).IsNull && !ByName(ig, Man).IsNull
            && !ByName(sim, Ifv).IsNull && !ByName(ig, Ifv).IsNull, timeoutFrames: 2000), "Rifleman 1 and the Technical must exist on every node");
        var manOnBrain = ByName(cgf, Man);
        var manOnSim = ByName(sim, Man);
        var manOnIg = ByName(ig, Man);
        var ifvOnSim = ByName(sim, Ifv);
        var ifvOnIg = ByName(ig, Ifv);
        var igMap = harness.Ig.App.TestHook_EntityMap;
        Assert.True(igMap.TryGetNetworkId(manOnIg, out long manId), "the Rifleman's network id on the IG");
        Assert.True(igMap.TryGetNetworkId(ifvOnIg, out long ifvId), "the IFV's network id on the IG");
        var station = new EntityOrders(ig, igMap, geo: null);
        int operatorControl = BehaviorHash.FromName("OperatorControl");

        Assert.True(station.Possess(manId).Accepted);
        Assert.True(harness.PumpUntil(() =>
        {
            var bs = cgf.GetComponentRO<BehaviorState>(manOnBrain);
            return bs.ActiveBehaviorHash == operatorControl && bs.Origin == BehaviorOrigin.Possession && station.HolderOf(manId) is not null;
        }, timeoutFrames: 600), "the IG's possession must reach the CGF brain and come back to the IG");

        // beside the IFV (SimHost owns his pose), until the brain's copy is within the rule's reach
        var ifvAt = sim.GetComponentRO<SimTransform>(ifvOnSim).Position;
        var beside = ifvAt + new System.Numerics.Vector3(Fdp.Toolkit.Behavior.Possession.OperatorUseActions.VehicleReach - 1f, 0f, 0f);
        uint seq = 1;
        Assert.True(harness.PumpUntil(() =>
        {
            sim.GetComponentRW<SimTransform>(manOnSim).Position = beside;
            station.Control(manId, new Fdp.Toolkit.Behavior.Possession.OperatorControlFrameEvent { Seq = seq++ });
            var a = cgf.GetComponentRO<SimTransform>(manOnBrain).Position;
            return System.Numerics.Vector2.Distance(new(a.X, a.Y), new(ifvAt.X, ifvAt.Y)) <= Fdp.Toolkit.Behavior.Possession.OperatorUseActions.VehicleReach - 0.5f;
        }, timeoutFrames: 600), "the brain must see the Rifleman beside the IFV");

        // ONE Drive press — the station's frame carries counter + verb + target — and the IG sees him in the driver seat
        bool boarded = harness.PumpUntil(() =>
        {
            if (!ig.IsComponentTypeRegistered<IsEmbarkedTag>() || !ig.HasComponent<IsEmbarkedTag>(manOnIg))
                sim.GetComponentRW<SimTransform>(manOnSim).Position = beside;
            station.Control(manId, new Fdp.Toolkit.Behavior.Possession.OperatorControlFrameEvent
            {
                Seq = seq++, UseSeq = 1, UseVerb = 0, UseTarget = ifvId,   // the PRIMARY verb (an index into OperatorUseActions.For): Drive, the driver seat being free
            });
            return Hrot.UI.Common.Map3D.PossessionStation.InDriverSeat(ig, manOnIg, ifvOnIg);
        }, timeoutFrames: 900);
        var status = cgf.HasComponent<Fdp.Toolkit.Behavior.Possession.OperatorUseStatus>(manOnBrain)
            ? cgf.GetComponentRO<Fdp.Toolkit.Behavior.Possession.OperatorUseStatus>(manOnBrain) : default;
        _out.WriteLine($"verdict on CGF {status.UseSeq}/{status.ActionId}/{status.Outcome}/{status.Reason}; embarked on IG {ig.HasComponent<IsEmbarkedTag>(manOnIg)}");
        foreach (var (node, w, man, ifv) in new[] { ("IG", ig, manOnIg, ifvOnIg), ("SimHost", sim, manOnSim, ifvOnSim), ("CGF", cgf, manOnBrain, ByName(cgf, Ifv)) })
        {
            string tag = w.IsComponentTypeRegistered<IsEmbarkedTag>() && w.HasComponent<IsEmbarkedTag>(man)
                ? $"vehicle {w.GetComponentRO<IsEmbarkedTag>(man).VehicleEntity} (ifv {ifv}) seat {w.GetComponentRO<IsEmbarkedTag>(man).Seat}" : "none";
            string seats = w.IsComponentTypeRegistered<Fdp.Toolkit.Behavior.Components.CarrierSeats>() && w.HasComponent<Fdp.Toolkit.Behavior.Components.CarrierSeats>(ifv)
                ? string.Join(",", Enumerable.Range(0, w.GetComponentRO<Fdp.Toolkit.Behavior.Components.CarrierSeats>(ifv).SeatCount).Select(i => w.GetComponentRO<Fdp.Toolkit.Behavior.Components.CarrierSeats>(ifv).Roles[i].ToString())) : "no CarrierSeats";
            _out.WriteLine($"  {node}: tag {tag}; seats {seats}");
        }
        Assert.True(boarded, "one Drive press must board the Rifleman in the Technical's driver seat, seen on the IG");

        // the station's hand-over: ride him, then — only once he rides — drive the IFV (one Fps unit per station)
        Assert.True(station.Possess(manId, Fdp.Toolkit.Behavior.Possession.PossessionMode.Riding).Accepted);
        Assert.True(harness.PumpUntil(() => ig.HasComponent<Fdp.Toolkit.Behavior.Possession.PossessedBy>(manOnIg)
            && ig.GetComponentRO<Fdp.Toolkit.Behavior.Possession.PossessedBy>(manOnIg).Mode == Fdp.Toolkit.Behavior.Possession.PossessionMode.Riding, timeoutFrames: 600),
            "the Riding hold must come back to the IG");
        var ifvPossessed = station.Possess(ifvId, Fdp.Toolkit.Behavior.Possession.PossessionMode.Fps);
        _out.WriteLine($"possess the vehicle: {ifvPossessed.Accepted} {ifvPossessed.Detail}");
        Assert.True(ifvPossessed.Accepted, $"the Technical must be possessable: {ifvPossessed.Detail}");
        Assert.True(harness.PumpUntil(() => station.HolderOf(ifvId) is not null
            && ig.HasComponent<Fdp.Toolkit.Behavior.Possession.PossessedBy>(manOnIg)
            && ig.GetComponentRO<Fdp.Toolkit.Behavior.Possession.PossessedBy>(manOnIg).Mode == Fdp.Toolkit.Behavior.Possession.PossessionMode.Riding, timeoutFrames: 600),
            "the IG holds the Technical and still holds its rider");

        var start = sim.GetComponentRO<SimTransform>(ifvOnSim).Position;
        bool drove = harness.PumpUntil(() =>
        {
            station.Control(ifvId, new Fdp.Toolkit.Behavior.Possession.OperatorControlFrameEvent { Seq = seq++, Forward = 1f, Yaw = 0f });
            var p = sim.GetComponentRO<SimTransform>(ifvOnSim).Position;
            return System.Numerics.Vector2.Distance(new(p.X, p.Y), new(start.X, start.Y)) > 3f;
        }, timeoutFrames: 1500);
        var end = sim.GetComponentRO<SimTransform>(ifvOnSim).Position;
        _out.WriteLine($"vehicle {start} -> {end} after {seq} frames");
        Assert.True(drove, $"the Technical must drive on SimHost from the IG's frames: {start} -> {end}");

        // Get out: the rider back as Fps (it releases the IFV), then ONE press
        Assert.True(station.Possess(manId, Fdp.Toolkit.Behavior.Possession.PossessionMode.Fps).Accepted);
        Assert.True(harness.PumpUntil(() => station.HolderOf(ifvId) is null
            && ig.HasComponent<Fdp.Toolkit.Behavior.Possession.PossessedBy>(manOnIg)
            && ig.GetComponentRO<Fdp.Toolkit.Behavior.Possession.PossessedBy>(manOnIg).Mode == Fdp.Toolkit.Behavior.Possession.PossessionMode.Fps, timeoutFrames: 600),
            "the Technical is let go and the rider is driven again");
        bool out_ = harness.PumpUntil(() =>
        {
            station.Control(manId, new Fdp.Toolkit.Behavior.Possession.OperatorControlFrameEvent
            {
                Seq = seq++, UseSeq = 2, UseVerb = 0, UseTarget = ifvId,   // aboard, the one action is Get out
            });
            return !sim.HasComponent<IsEmbarkedTag>(manOnSim) && !ig.HasComponent<IsEmbarkedTag>(manOnIg);
        }, timeoutFrames: 900);
        Assert.True(out_, "one Get-out press must take him out of the Technical, on SimHost and the IG");
    }

    /// <summary>
    /// ⭐⭐ <c>C3</c> (handoff 2; DESIGN_FPV_Drone §6, Motion Domains §5; R-3005) — an FPV DRONE possessed from another node flies there:
    /// SimHost is the station; its frames cross to CGF as <c>ControlFrameInteractionPayload</c> (with the B10 <c>Vertical</c> axis);
    /// CGF's <c>OperatorControl</c> writes <c>ManualDrive</c> + <c>NavigationIntent.Manual</c>, which come back as dtManualDrive /
    /// dtNavigationIntent; SimHost — the ONE Muscle role, <c>MuscleGround</c>, owns the drone's position (R-3005) — climbs and flies it
    /// through <c>AirMotionSystem</c> → <c>RotorcraftModel.Manual</c>. 🔒 User: <i>"Drone will need to be controllable also from FPS
    /// possess mode."</i> The scenario is <c>ua-grenade-or-rifle</c> plus one drone (no drone scenario ships yet).
    /// </summary>
    /// <summary>⭐ <c>CE-3224</c> (civilians B-6, CV-13; docs/DESIGN_Civilians_And_Crowd_Control.md §7.7) — a gesture the brain sets on
    /// CGF is seen on SimHost and on the IG, and lowering it is seen too.</summary>
    [Fact(Timeout = 600_000)]
    public async Task CE3224_AGestureSetOnTheBrain_ReachesSimHostAndTheIg()
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
        Assert.True(harness.PumpUntil(() => !ByName(cgf, "Rifleman").IsNull && !ByName(sim, "Rifleman").IsNull && !ByName(ig, "Rifleman").IsNull,
            timeoutFrames: 2000), "the Rifleman must exist on CGF, SimHost and the IG");
        var onBrain = ByName(cgf, "Rifleman");

        Assert.True(Fdp.Toolkit.Combat.Gestures.Set(cgf, onBrain, Fdp.Toolkit.Combat.GestureKind.HandsUp));
        Assert.True(harness.PumpUntil(() =>
            Fdp.Toolkit.Combat.Gestures.Of(sim, ByName(sim, "Rifleman")) == Fdp.Toolkit.Combat.GestureKind.HandsUp
            && Fdp.Toolkit.Combat.Gestures.Of(ig, ByName(ig, "Rifleman")) == Fdp.Toolkit.Combat.GestureKind.HandsUp, timeoutFrames: 600),
            "hands up on CGF must reach SimHost and the IG");

        Assert.True(Fdp.Toolkit.Combat.Gestures.Set(cgf, onBrain, Fdp.Toolkit.Combat.GestureKind.None));
        Assert.True(harness.PumpUntil(() =>
            Fdp.Toolkit.Combat.Gestures.Of(sim, ByName(sim, "Rifleman")) == Fdp.Toolkit.Combat.GestureKind.None
            && Fdp.Toolkit.Combat.Gestures.Of(ig, ByName(ig, "Rifleman")) == Fdp.Toolkit.Combat.GestureKind.None, timeoutFrames: 600),
            "lowering the hands must reach both too");
    }

    private static int Clouds(EntityRepository world)
    {
        if (!world.IsComponentTypeRegistered<Fdp.Toolkit.Combat.AreaEffectCloud>()) return 0;
        int n = 0;
        foreach (var _ in world.Query().With<Fdp.Toolkit.Combat.AreaEffectCloud>().Build()) n++;
        return n;
    }

    private static Entity FirstCloud(EntityRepository world)
    {
        foreach (var e in world.Query().With<Fdp.Toolkit.Combat.AreaEffectCloud>().Build()) return e;
        return Entity.Null;
    }

    /// <summary>⭐⭐ <c>CE-3219</c> part 3 (civilians B-1, R-3014; docs/DESIGN_Civilians_And_Crowd_Control.md §7.3b) + <c>CE-3228</c> (§3.4d) —
    /// a cloud a burst asks for on SimHost (the entity-creation request on the bus, owner 0) is forwarded to the default processor, which
    /// creates and owns it; it reaches SimHost and the IG with its fields, and when its time is up its owner ends it and it is gone on every
    /// node (no node but the owner ends it).</summary>
    [Fact(Timeout = 600_000)]
    public async Task CE3219_ACloudAskedForOnSimHost_ReachesEveryNode_AndEndsEverywhere()
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
        Assert.Equal(0, Clouds(sim) + Clouds(cgf) + Clouds(ig));

        Fdp.Toolkit.Combat.AreaEffectClouds.Request(sim, new System.Numerics.Vector3(5, 5, 0), new Fdp.Toolkit.Combat.AreaEffectCloud
        {
            Kind = Fdp.Toolkit.Combat.CloudKind.TearGas, Radius = 6f, Height = 3f, SightTransmittance = 0.4f, IrritantSeconds = 2f,
            BornAtSimSeconds = Fdp.Toolkit.Time.SimClock.Of(sim).TotalTime, Seconds = 3f,
        });
        Assert.True(harness.PumpUntil(() => Clouds(sim) == 1 && Clouds(cgf) == 1 && Clouds(ig) == 1, timeoutFrames: 600),
            $"the cloud must exist on SimHost, CGF and the IG (sim {Clouds(sim)}, cgf {Clouds(cgf)}, ig {Clouds(ig)})");
        foreach (var w in new[] { cgf, ig })
        {
            var c = w.GetComponentRO<Fdp.Toolkit.Combat.AreaEffectCloud>(FirstCloud(w));
            Assert.Equal(Fdp.Toolkit.Combat.CloudKind.TearGas, c.Kind);
            Assert.Equal(6f, c.Radius);
            Assert.Equal(3f, c.Seconds);
        }
        // ⭐ CE-3228 — asked for with owner 0 on SimHost (not the default processor) ⇒ forwarded; the default processor (CGF) creates and owns it
        Assert.True(Fdp.Toolkit.Replication.Extensions.AuthorityExtensions.HasAuthority(cgf, FirstCloud(cgf)), "the default processor (CGF) owns it");
        Assert.False(Fdp.Toolkit.Replication.Extensions.AuthorityExtensions.HasAuthority(sim, FirstCloud(sim)), "a replica is not its owner");
        Assert.False(Fdp.Toolkit.Replication.Extensions.AuthorityExtensions.HasAuthority(ig, FirstCloud(ig)), "a replica is not its owner");

        Assert.True(harness.PumpUntil(() => Clouds(sim) == 0 && Clouds(cgf) == 0 && Clouds(ig) == 0, timeoutFrames: 2000),
            $"its owner ends it at its time and it is gone everywhere (sim {Clouds(sim)}, cgf {Clouds(cgf)}, ig {Clouds(ig)})");
    }

    private static Entity Stone(EntityRepository world)
    {
        if (!world.IsComponentTypeRegistered<Fdp.Toolkit.Replication.Components.LightweightRound>()) return Entity.Null;
        foreach (var e in world.Query().With<Fdp.Toolkit.Replication.Components.LightweightRound>()
                     .With<Fdp.Toolkit.Replication.Components.TkbIdentity>().Build())
            if (world.GetComponentRO<Fdp.Toolkit.Replication.Components.TkbIdentity>(e).TkbType == Hrot.Core.Tkb.MunitionTkbCatalog.TkbStone) return e;
        return Entity.Null;
    }

    /// <summary>⭐⭐ <c>CE-3235</c> (B-13, AW-8; docs/DESIGN_Civilians_And_Crowd_Control.md §7.10 PU-3/PU-4) — a stone LYING on SimHost (the
    /// node that flies it) is seen on the brain's node (CGF); the Rifleman's brain asks for it twice in one frame; SimHost grants it ONCE and
    /// removes it everywhere; the grant crosses back and his hands on CGF hold exactly one stone.</summary>
    [Fact(Timeout = 600_000)]
    public async Task CE3235_AStoneLyingOnSimHost_IsPickedUpByABrainOnCgf_Once()
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
            "the Rifleman must exist on CGF and SimHost");
        var onBrain = ByName(cgf, "Rifleman");
        Assert.Equal(0, Fdp.Toolkit.Combat.PersonHands.Rounds(cgf, onBrain, Hrot.Core.Tkb.MunitionTkbCatalog.TkbStone));

        // a stone lying at his feet, as the flight leaves one (Landed, lingering) — on SimHost, which flies it; seen because it has a look
        var at = sim.GetComponentRO<SimTransform>(ByName(sim, "Rifleman")).Position;
        var stone = sim.CreateEntity();
        sim.AddComponent(stone, new SimTransform { Position = at + new System.Numerics.Vector3(0.3f, 0f, 0f), Rotation = System.Numerics.Quaternion.Identity });
        sim.AddComponent(stone, new SimVelocity());
        sim.AddComponent(stone, new Fdp.Toolkit.Combat.Components.BallisticProjectile
        {
            PreviousPosition = at, Muzzle = at, SpawnTick = 1, StoppedTick = 1, TerrainFlags = 1, Ammo = Hrot.Core.Tkb.MunitionTkbCatalog.TkbStone,
            Warhead = (byte)(Fdp.Toolkit.Combat.Components.WarheadRound.Arc | Fdp.Toolkit.Combat.Components.WarheadRound.Lingers
                             | Fdp.Toolkit.Combat.Components.WarheadRound.Landed),
            FuzeRemaining = 60f,
        });
        Assert.True(Fdp.Toolkit.Combat.Systems.FireProcessingSystem.MarkIfVisible(sim, stone, Hrot.Core.Tkb.MunitionTkbCatalog.TkbStone),
            "the stone has a look ⇒ it replicates");
        Assert.True(harness.PumpUntil(() => !Stone(cgf).IsNull, timeoutFrames: 600), "the lying stone must be seen on the brain's node");

        // two asks in one frame (as two men reaching at once): one grant
        Assert.True(harness.PumpUntil(() => Fdp.Toolkit.Combat.RoundPickUp.CanAsk(cgf, onBrain, Stone(cgf)), timeoutFrames: 300),
            "the Rifleman can ask for the stone at his feet");
        var replica = Stone(cgf);
        Assert.True(Fdp.Toolkit.Combat.RoundPickUp.Ask(cgf, onBrain, replica));
        Assert.True(Fdp.Toolkit.Combat.RoundPickUp.Ask(cgf, onBrain, replica));
        Assert.True(harness.PumpUntil(() => Fdp.Toolkit.Combat.PersonHands.Rounds(cgf, onBrain, Hrot.Core.Tkb.MunitionTkbCatalog.TkbStone) > 0,
            timeoutFrames: 600), "the grant must reach his hands on CGF");
        Assert.True(harness.PumpUntil(() => Stone(sim).IsNull && Stone(cgf).IsNull && Stone(ig).IsNull, timeoutFrames: 600),
            "the stone is gone on every node");
        harness.PumpFrames(30);
        Assert.Equal(1, Fdp.Toolkit.Combat.PersonHands.Rounds(cgf, onBrain, Hrot.Core.Tkb.MunitionTkbCatalog.TkbStone));   // ⭐ ONE winner
    }

    /// <summary>⭐⭐ <c>CE-1148</c> S2 (docs/DESIGN_Node_Roles_And_Policies.md §3.5, R-3015) — a burst simulated on SimHost reaches the
    /// brain node through the ONE shared event pair, exactly once, and stays once (no loop-back, no second list).</summary>
    [Fact(Timeout = 600_000)]
    public async Task CE1148_ABurstOnSimHost_ReachesTheBrainExactlyOnce()
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
        const long Tag = 987_654_321L;   // a munition id no scenario uses, so only this burst is counted
        int Seen()
        {
            Span<Fdp.Toolkit.Combat.RecentBurst> kept = stackalloc Fdp.Toolkit.Combat.RecentBurst[Fdp.Toolkit.Combat.RecentDetonations.Capacity];
            int n = Fdp.Toolkit.Combat.RecentDetonations.Since(cgf, double.MinValue, kept), hits = 0;
            for (int i = 0; i < n; i++) if (kept[i].Ammo == Tag) hits++;
            return hits;
        }

        // the shooter must be a network entity: the egress drops a burst nobody fired (MunitionDetonationEgressTranslator)
        Assert.True(harness.PumpUntil(() => !ByName(sim, "Rifleman").IsNull, timeoutFrames: 2000), "the Rifleman must exist on SimHost");
        sim.Bus.Publish(new Fdp.Toolkit.Combat.Contracts.DetonationNotification
        {
            Shooter = ByName(sim, "Rifleman"), HitX = 3f, HitY = 4f, HitZ = 0f, Ammo = Tag,
        });
        Assert.True(harness.PumpUntil(() => Seen() >= 1, timeoutFrames: 600), "a burst simulated on SimHost must reach the brain node");
        harness.PumpFrames(120);
        Assert.Equal(1, Seen());
    }

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

    /// <summary>
    /// ⭐⭐ <c>CE-2171</c> (Air Ops handoff E2; DESIGN_Air_Operations §5, B1) — a ground unit told <c>FollowRoute</c> on a SPLIT cluster
    /// drives its route: the brain (CGF) and the body (SimHost) are on two nodes, the route entity is replicated (MapRoute), and the
    /// body ends near the route's last point. 🔴 Before: the brain waited for a LOCAL trajectory-pool id that only SimHost makes and
    /// that never crosses the wire, so the order never started. The scenario is <c>ua-grenade-or-rifle</c> plus a route and a tank.
    /// </summary>
    [Fact(Timeout = 600_000)]
    public async Task CE2171_AFollowRoute_OnASplitCluster_DrivesTheRoute()
    {
        Directory.CreateDirectory(NasScenarioStaging.DirectoryOf(_scenarioId));
        var scenario = System.Text.Json.Nodes.JsonNode.Parse(File.ReadAllText(Path.Combine(RepoRoot(), "scenarios", "ua-grenade-or-rifle", "scenario.json")))!;
        scenario["entities"]!["a4120000-0000-4000-8000-0000000000e1"] = System.Text.Json.Nodes.JsonNode.Parse("""
            {
              "SimTransform": { "Position": [60, -60, 0], "Rotation": [0, 0, 0, 1] },
              "NetworkIdentity": { "Value": 7800 },
              "TkbIdentity": { "TkbType": 8802 },
              "EntityInfo": { "Name": "Route east" },
              "RoutePlan": { "IsLoop": false, "Waypoints": [
                { "X": 60, "Y": -60, "Z": 0, "TargetSpeed": 6 },
                { "X": 110, "Y": -60, "Z": 0, "TargetSpeed": 6 },
                { "X": 110, "Y": -20, "Z": 0, "TargetSpeed": 6 } ] }
            }
            """);
        scenario["entities"]!["a4120000-0000-4000-8000-0000000000e2"] = System.Text.Json.Nodes.JsonNode.Parse("""
            {
              "SimTransform": { "Position": [60, -60, 0], "Rotation": [0, 0, 0, 1] },
              "Health": { "Current": 3000, "Max": 3000 },
              "NetworkIdentity": { "Value": 7801 },
              "TkbIdentity": { "TkbType": 100 },
              "EntityInfo": { "Name": "Route tank", "ForceId": "Friend" },
              "Behavior": { "Name": "FollowRoute", "Params": "{\"routeEntityId\":7800,\"Speed\":6}", "Origin": "Superior" }
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
        Assert.True(harness.PumpUntil(() => !ByName(cgf, "Route tank").IsNull && !ByName(sim, "Route tank").IsNull, timeoutFrames: 2000),
            "the tank must exist on both nodes");
        var onMuscle = ByName(sim, "Route tank");
        var start = sim.GetComponentRO<SimTransform>(onMuscle).Position;
        var end = new System.Numerics.Vector2(110f, -20f);
        bool arrived = harness.PumpUntil(() =>
        {
            var p = sim.GetComponentRO<SimTransform>(onMuscle).Position;
            return System.Numerics.Vector2.Distance(new(p.X, p.Y), end) < 8f;
        }, timeoutFrames: 6000);
        var now = sim.GetComponentRO<SimTransform>(onMuscle).Position;
        var intent = sim.HasComponent<Fdp.Toolkit.Navigation.NavigationIntent>(onMuscle)
            ? sim.GetComponentRO<Fdp.Toolkit.Navigation.NavigationIntent>(onMuscle) : default;
        var nav = sim.HasComponent<CarKinem.Core.NavState>(onMuscle) ? sim.GetComponentRO<CarKinem.Core.NavState>(onMuscle) : default;
        var routeOnSim = Fdp.Toolkit.Replication.Services.NetworkIdResolver.FindEntityByNetworkId(sim, intent.RouteEntityId);
        bool plan = !routeOnSim.IsNull && sim.HasManagedComponent<Hrot.Map.Common.Components.RoutePlan>(routeOnSim);
        int compiled = !routeOnSim.IsNull && sim.IsComponentTypeRegistered<Hrot.Map.Common.Components.RouteTrajectoryCache>()
            && sim.HasComponent<Hrot.Map.Common.Components.RouteTrajectoryCache>(routeOnSim)
            ? sim.GetComponentRO<Hrot.Map.Common.Components.RouteTrajectoryCache>(routeOnSim).TrajectoryId : -1;
        _out.WriteLine($"tank {start} -> {now}; SimHost intent Mode {intent.Mode} TrajectoryId {intent.TrajectoryId} RouteEntityId {intent.RouteEntityId}; "
            + $"NavState Mode {nav.Mode} Traj {nav.TrajectoryId} Speed {nav.TargetSpeed}; route on SimHost {routeOnSim} plan {plan} compiled {compiled}");
        var routeOnCgf = ByName(cgf, "Route east");
        string Owner(EntityRepository w, Entity e) => e.IsNull || !w.HasComponent<Fdp.Toolkit.Replication.Components.NetworkAuthority>(e) ? "?"
            : $"owner {w.GetComponentRO<Fdp.Toolkit.Replication.Components.NetworkAuthority>(e).PrimaryOwnerId} local {w.GetComponentRO<Fdp.Toolkit.Replication.Components.NetworkAuthority>(e).LocalNodeId}";
        _out.WriteLine($"route on CGF: SimTransform {(!routeOnCgf.IsNull && cgf.HasComponent<SimTransform>(routeOnCgf))} "
            + $"authority dtMapRoute {(!routeOnCgf.IsNull && Fdp.Toolkit.Replication.Extensions.AuthorityExtensions.HasAuthority(cgf, routeOnCgf, Fdp.Toolkit.Replication.Extensions.OwnershipExtensions.PackKey((long)Hrot.NED.Descriptors.EDescriptorType.dtMapRoute, 0)))}");
        _out.WriteLine($"route on CGF: plan {(!routeOnCgf.IsNull && cgf.HasManagedComponent<Hrot.Map.Common.Components.RoutePlan>(routeOnCgf))} {Owner(cgf, routeOnCgf)}; on SimHost {Owner(sim, routeOnSim)}");
        foreach (var (name, w) in new[] { ("ig", harness.Ig.App.World) })
        {
            var r = ByName(w, "Route east");
            _out.WriteLine($"route on {name}: {r} plan {(!r.IsNull && w.HasManagedComponent<Hrot.Map.Common.Components.RoutePlan>(r))} {Owner(w, r)}");
        }
        var routeOnSimByName = ByName(sim, "Route east");
        _out.WriteLine($"route by name: CGF {routeOnCgf} SimHost {routeOnSimByName}; SimHost RoutePlan {( !routeOnSimByName.IsNull && sim.HasManagedComponent<Hrot.Map.Common.Components.RoutePlan>(routeOnSimByName))}"
            + $" netId on SimHost {(routeOnSimByName.IsNull ? -1 : (sim.HasComponent<Fdp.Toolkit.Replication.Components.NetworkIdentity>(routeOnSimByName) ? sim.GetComponentRO<Fdp.Toolkit.Replication.Components.NetworkIdentity>(routeOnSimByName).Value : -2))}");
        Assert.True(arrived, $"the tank must drive its route on SimHost to (110, -20): {start} -> {now} (intent {intent.Mode})");
    }
}
