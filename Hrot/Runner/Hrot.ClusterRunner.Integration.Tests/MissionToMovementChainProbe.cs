using System;
using System.Numerics;
using System.Threading;
using CoreGeoPoint = Hrot.Core.Mission.GeoPoint;
using Hrot.NED.Descriptors;
using Hrot.NED.Messages;
using Hrot.Map.Common;
using CycloneDDS.Runtime;
using Fdp.Core;
using Fdp.Toolkit.Behavior.Components;
using Xunit;
using Xunit.Abstractions;

using DdsMissionTrigger = Hrot.NED.Descriptors.MissionTrigger;
using EcsNavigationIntent = Fdp.Toolkit.Navigation.NavigationIntent;

namespace Hrot.ClusterRunner.Integration.Tests;

/// <summary>
/// ⭐⭐⭐ <b>DIAGNOSTIC PROBE — WHICH HOP of the mission→movement chain breaks?</b>
///
/// <para>📌 <b>The chain, as the DESIGN specifies it</b>
/// (<c>docs/designs/navig-2/Navigation_Design_v2_0.md</c> §3.1, and restated verbatim in
/// <c>CgfSubsystemHeadlessTests.SimHost_MoveToLocationMission_EntityMovesWithoutGhostTick</c>
/// at its own comment):</para>
/// <code>
///   MissionControlRequest (DDS)
///     → MissionControlExecutionSystem      (CGF / Brain)
///     → MissionAdapterSystem               → AssignTacticalIntentEvent
///     → TacticalIntentResolutionSystem     → AssignBehaviorEvent (JSON params)
///     → BehaviorIngressSystem              → BehaviorState.ActiveBehaviorHash
///     → BTree tick                         → LocomotionChannel.ActiveAction = MoveTo
///     → LocomotionDispatcherSystem         → MoveToExecutor.OnEnter
///     → NavigationIntent{Mode,IntentId++}  (CGF-side ECS component)
///     → NavigationIntentEgressTranslator   → DDS "NavigationIntent" topic
///     → NavigationIntentIngressTranslator  → SimHost-side ECS NavigationIntent
///     → NavigationIntentBridgeSystem       → NavState{Mode=Direct,FinalDestination}
///     → CarKinematicsSystem                → SimTransform advances
/// </code>
///
/// <para>⭐ <b>Why this probe exists.</b> The earlier probe drove the tail of this chain with
/// <c>TestHook_SetMovementIntent</c>, a direct component write that SKIPS the first nine hops.
/// It could therefore only ever indict the last two — and did, misleadingly. This probe issues
/// the SAME <c>MissionControlRequest</c> the failing test issues and reads every intermediate
/// component on BOTH nodes, so the break is located rather than guessed.</para>
///
/// <para>⚠ <b>It asserts only its own preconditions</b> (the entity exists on both nodes and the
/// mission was ACKed). The chain state is REPORTED, never gated — a diagnostic must not present
/// as a red.</para>
/// </summary>
public sealed class MissionToMovementChainProbe
{
    private static int _domainCounter = 208;

    private readonly ITestOutputHelper _out;
    public MissionToMovementChainProbe(ITestOutputHelper output) => _out = output;

    [Fact]
    [Trait("Category", "Diagnostic")]
    public void WhereDoesTheMissionToMovementChainBreak()
    {
        int domainId = Interlocked.Increment(ref _domainCounter);
        using var harness = new HrotRunnerHarness("simhost,cgf", domainId);

        long tkbType = TkbEntityTypes.Tank_M1Abrams;
        var spawnPos = new CoreGeoPoint { Latitude = 52.524, Longitude = 13.415, Altitude = 0 };
        long networkId = harness.SimHost.TestHook_SpawnEntity(tkbType, spawnPos);

        bool cgfReady = harness.PumpUntil(
            () => harness.Cgf?.GhostEntityMap is { } m && m.TryGetEntity(networkId, out _),
            60);
        Assert.True(cgfReady, "probe precondition: the CGF ghost never appeared.");

        var initialPos = harness.SimHost.TestHook_GetSimTransform(networkId).Position;

        // ── issue the SAME mission the failing test issues ─────────────────────────────
        using var participant = new DdsParticipant((uint)harness.DomainId);
        using var reqWriter   = new DdsWriter<MissionControlRequest>(participant, "MissionControlRequest");
        using var ackReader   = new DdsReader<MissionControlAck>(participant, "MissionControlAck");

        var taskId = Guid.NewGuid();
        reqWriter.Write(new MissionControlRequest
        {
            RequestId      = Guid.NewGuid(),
            TargetEntityId = networkId,
            BaseVersion    = 0,
            Payload        = new MissionCommandUnion
            {
                _d              = eMissionCommandType.CMD_REPLACE_MISSION,
                FullMissionData = new MissionPlan
                {
                    ActiveTaskId = taskId,
                    Tasks        = new System.Collections.Generic.List<MissionTask>
                    {
                        new MissionTask
                        {
                            TaskId          = taskId,
                            BehaviorId      = "MoveToLocation",
                            BehaviorParams  = "{\"targetLat\":52.535,\"targetLon\":13.42,\"speed\":15,\"arrivalRadius\":5}",
                            ExecutingEngine = "CGFX",
                            State           = eTaskState.TASK_ACTIVE,
                            Triggers        = new System.Collections.Generic.List<DdsMissionTrigger>
                            {
                                new DdsMissionTrigger { Type = "BehaviorFinished", Params = "" }
                            }
                        }
                    }
                }
            }
        });

        MissionControlAck ack = default;
        bool acked = harness.PumpUntil(
            () =>
            {
                using var loan = ackReader.Take();
                foreach (var s in loan) if (s.IsValid) { ack = s.Data; return true; }
                return false;
            },
            120);
        _out.WriteLine($"ACK received={acked} ErrorCode={ack.ErrorCode} NewVersion={ack.NewVersion}");
        Assert.True(acked, "probe precondition: no MissionControlAck — the CGF never processed the request.");

        harness.PumpFrames(30);
        DumpEntity(harness.Cgf!.World!, harness.Cgf!.GhostEntityMap!, networkId, "CGF ghost");
        DumpEntity(harness.SimHost.World!, harness.SimHost.TestHook_EntityMap, networkId, "SimHost");

        // ── walk the chain, one frame at a time ────────────────────────────────────────
        _out.WriteLine("");
        _out.WriteLine("frame │ CGF: behHash  btree  chan(act/inst/disp/st)  intent(mode/id/dest)      │ SIM: intent(mode/id)  nav(mode/spd/dest)   pos            ");
        _out.WriteLine("──────┼──────────────────────────────────────────────────────────────────────┼───────────────────────────────────────────────────────────");

        for (int f = 0; f < 40; f++)
        {
            harness.PumpFrames(5);
            _out.WriteLine($"{(f + 1) * 5,5} │ {CgfRow(harness, networkId)} │ {SimRow(harness, networkId, initialPos)}");
        }

        var finalPos = harness.SimHost.TestHook_GetSimTransform(networkId).Position;
        _out.WriteLine("");
        _out.WriteLine($"moved {Vector3.Distance(finalPos, initialPos):F3} m over 200 frames "
                     + $"(initial=({initialPos.X:F1},{initialPos.Y:F1}) final=({finalPos.X:F1},{finalPos.Y:F1}))");
        _out.WriteLine("");
        _out.WriteLine("⭐ READ THE TABLE LEFT TO RIGHT. The FIRST column that never becomes non-default");
        _out.WriteLine("  is the broken hop. Columns, in chain order:");
        _out.WriteLine("   behHash  — BehaviorIngressSystem assigned a behaviour   (0 ⇒ CGF cognitive tier never fired)");
        _out.WriteLine("   btree    — BrainBTreeState is present/non-zero          (⇒ the BTree is actually ticking)");
        _out.WriteLine("   chan     — LocomotionChannel: the BTree issued MoveTo   (act=0 ⇒ the BTree never reached the action)");
        _out.WriteLine("   CGF intent — MoveToExecutor.OnEnter wrote it            (mode=None ⇒ the dispatcher/executor did not run)");
        _out.WriteLine("   SIM intent — egress+DDS+ingress delivered it            (differs from CGF ⇒ the WIRE is the break)");
        _out.WriteLine("   nav        — NavigationIntentBridgeSystem applied it    (mode=None with SIM intent set ⇒ the BRIDGE is the break)");
        _out.WriteLine("   pos        — CarKinematicsSystem integrated it          (static with nav set ⇒ KINEMATICS is the break)");
    }

    /// <summary>
    /// Dumps every component actually present on the entity, plus the mission-tier state the
    /// cognitive pipeline reads. ⭐ The component LIST is the point: <c>MissionAdapterSystem</c>
    /// queries <c>MissionPlanQueue AND BehaviorState</c>, so a missing one of those explains a
    /// silent no-op that no log line reports.
    /// </summary>
    private void DumpEntity(EntityRepository world, dynamic map, long networkId, string label)
    {
        _out.WriteLine("");
        _out.WriteLine($"═══ {label}: components present on entity {networkId} ═══");
        if (!map.TryGetEntity(networkId, out Entity e) || !world.IsAlive(e))
        {
            _out.WriteLine("  <entity not present>");
            return;
        }

        ref var mask = ref world.GetComponentMask(e.Index);
        var names = new System.Collections.Generic.List<string>();
        foreach (var kv in world.GetRegisteredComponentTypes())
            if (mask.IsSet(kv.Value.ComponentTypeId))
                names.Add(kv.Key.Name);
        names.Sort();
        _out.WriteLine($"  [{names.Count}] " + string.Join(", ", names));

        foreach (var want in new[]
                 {
                     "MissionPlanQueue", "BehaviorState", "MissionAdapterState", "ActiveMissionPlan",
                     "LocomotionChannel", "ActorCapabilityState", "NavigationIntent", "NavState",
                     "BrainBTreeState", "NetworkIdentity", "SimTransform",
                 })
            if (!names.Contains(want))
                _out.WriteLine($"  ⛔ MISSING: {want}");

        // ⭐ TacticalIntentResolutionSystem:94 gates on HasAuthority<BehaviorState>; BehaviorIngressSystem:65
        //   gates on the behaviour NAME resolving in the registry. Both `continue` SILENTLY.
        _out.WriteLine($"  HasAuthority<BehaviorState> = {world.HasAuthority<BehaviorState>(e)}");
        _out.WriteLine($"  HasAuthority<LocomotionChannel> = {world.HasAuthority<LocomotionChannel>(e)}");
        _out.WriteLine($"  HasAuthority<EcsNavigationIntent> = {world.HasAuthority<EcsNavigationIntent>(e)}");

        if (world.HasComponent<MissionPlanQueue>(e))
        {
            var q = world.GetComponent<MissionPlanQueue>(e);
            _out.WriteLine($"  MissionPlanQueue: CurrentPhase={q.CurrentPhase} PhaseCount={q.PhaseCount}");
        }
        if (world.HasComponent<ActorCapabilityState>(e))
            _out.WriteLine($"  ActorCapabilityState: {world.GetComponent<ActorCapabilityState>(e).Capabilities}");
        var plan = world.HasManagedComponent<ActiveMissionPlan>(e)
            ? world.GetComponent<ActiveMissionPlan>(e)
            : null;
        if (plan?.Plan?.Tasks != null)
        {
            _out.WriteLine($"  ActiveMissionPlan: {plan.Plan.Tasks.Count} task(s)");
            foreach (var t in plan.Plan.Tasks)
                _out.WriteLine($"    BehaviorName='{t.BehaviorName}' Params='{t.BehaviorParams}'");
        }
        else
        {
            _out.WriteLine("  ActiveMissionPlan: <null or no tasks>");
        }
    }

    // ── row renderers ─────────────────────────────────────────────────────────────────

    private static string CgfRow(HrotRunnerHarness harness, long networkId)
    {
        var world = harness.Cgf?.World;
        var map   = harness.Cgf?.GhostEntityMap;
        if (world == null || map == null || !map.TryGetEntity(networkId, out var e) || !world.IsAlive(e))
            return "  <no cgf ghost>                                                     ";

        int  behHash = world.HasComponent<BehaviorState>(e) ? world.GetComponent<BehaviorState>(e).ActiveBehaviorHash : 0;
        bool hasBt   = world.IsComponentTypeRegistered<BrainBTreeState>() && world.HasComponent<BrainBTreeState>(e);

        string chan = "  none      ";
        if (world.HasComponent<LocomotionChannel>(e))
        {
            var ch = world.GetComponent<LocomotionChannel>(e);
            chan = $"{ch.ActiveAction}/{ch.ActionInstanceId}/{ch.DispatchedInstanceId}/{ch.Status}";
        }

        string intent = " none                ";
        if (world.HasComponent<EcsNavigationIntent>(e))
        {
            var i = world.GetComponent<EcsNavigationIntent>(e);
            intent = $"{i.Mode}/{i.IntentId}/({i.FinalDestination.X:F0},{i.FinalDestination.Y:F0})";
        }

        return $"{behHash,10}  {(hasBt ? "yes" : " no "),5}  {chan,-22}  {intent,-24}";
    }

    private static string SimRow(HrotRunnerHarness harness, long networkId, Vector3 initial)
    {
        var world = harness.SimHost.World;
        var map   = harness.SimHost.TestHook_EntityMap;
        if (world == null || map == null || !map.TryGetEntity(networkId, out var e) || !world.IsAlive(e))
            return "  <no sim entity>";

        string intent = " none            ";
        if (world.HasComponent<EcsNavigationIntent>(e))
        {
            var i = world.GetComponent<EcsNavigationIntent>(e);
            intent = $"{i.Mode}/{i.IntentId}";
        }

        string nav = " none                ";
        if (world.HasComponent<CarKinem.Core.NavState>(e))
        {
            var n = world.GetComponent<CarKinem.Core.NavState>(e);
            nav = $"{n.Mode}/{n.TargetSpeed:F0}/({n.FinalDestination.X:F0},{n.FinalDestination.Y:F0})";
        }

        var pos = world.HasComponent<SimTransform>(e) ? world.GetComponent<SimTransform>(e).Position : default;
        return $"{intent,-18}  {nav,-24}  d={Vector3.Distance(pos, initial),7:F2}m";
    }
}
