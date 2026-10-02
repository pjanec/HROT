using System;
using System.Linq;
using System.Text.Json.Nodes;
using System.Threading;
using Fdp.Core;
using Fdp.Toolkit.Behavior.Events;
using Hrot.Map.Common;
using Xunit;

namespace Hrot.ClusterRunner.Integration.Tests;

/// <summary>
/// ⭐⭐⭐ <c>CE-476</c> — the AI debug surface answers on a CONSTRUCTED, HEADLESS cluster: a real CGF + SimHost, the debug API
/// built by the same <c>ClusterDebugApiComposition</c> <c>Hrot.ClusterRunner/Program.cs</c> calls (providers → dispatcher →
/// the cluster ctor, with the world-keyed <c>aiDebugSurface</c> Func). 📄 <c>docs/blueprints/DESIGN_Cluster_Ai_Debug_Surface.md</c>.
/// </summary>
/// <remarks>🔴 Before: <c>/trace/observe</c> → "Trace coordinator not available", <c>/trace</c> → <c>tier: unknown</c> for
/// both commanders, <c>/variables</c> → "No blueprint debug session". And a headless CGF composed no BTree/HSM session at all.</remarks>
[Collection("EqsIntegrationTests")]
public sealed class TheClusterAiDebugSurfaceAnswersTests
{
    // Domain range: 181-189 (free; below CgfHarness's 200 base, above the 162 block).
    private static int _domain = 180;

    // The curated hill-attack-close-bp params (scenarios/hill-attack-close-bp/scenario.json), with the area pointed at a
    // REAL area entity: with none, the commander's area node fails at once and the behaviour ends (measured).
    private static string HillAttackParams(long areaNet) =>
        "{\"firingLineStart\":[52.5239942191481,13.413540814869096],\"firingLineEnd\":[52.524533409890886,13.413578805321581]," +
        "\"baselineStart\":[52.52360336114987,13.412705470497283],\"baselineEnd\":[52.52492437655515,13.412838305005167]," +
        "\"tankSpacing\":20,\"targetAreaNetworkId\":" + areaNet + "}";

    private sealed record Rig(HrotRunnerHarness H, Hrot.Editor.DebugApi.DebugApiService Api, Func<string> Perspective) : IDisposable
    {
        public void Dispose() => H.Dispose();
    }

    private static Rig Boot(Func<string> perspective)
    {
        var h = new HrotRunnerHarness("simhost,cgf", Interlocked.Increment(ref _domain));
        var subsystems = new object[] { h.SimHost, h.Cgf! };

        // ⭐ The composition root's OWN code (ClusterDebugApiComposition — what Program.Main calls), not a copy of it.
        var api = new Hrot.Editor.DebugApi.DebugApiService(
            Hrot.Runner.ClusterDebugApiComposition.Dispatcher(subsystems, perspective),
            behaviorRegistry: Hrot.Runner.ClusterDebugApiComposition.BehaviorRegistry(subsystems),
            aiDebugSurface: Hrot.Runner.ClusterDebugApiComposition.AiDebugSurface(subsystems));
        return new Rig(h, api, perspective);
    }

    private static long Commander(Rig rig, string behaviour)
    {
        // The area: Muscle-owned, a polygon around the origin, replicated to the Brain before the assignment.
        long areaNet = rig.H.SimHost.TestHook_SpawnEntity(
            TkbEntityTypes.TacGraphic_Area, new Hrot.Core.Mission.GeoPoint { Latitude = 52.521, Longitude = 13.406, Altitude = 0 });
        Assert.True(rig.H.PumpUntil(() => rig.H.SimHost.TestHook_EntityMap.TryGetEntity(areaNet, out _), timeoutFrames: 3000));
        rig.H.SimHost.TestHook_EntityMap.TryGetEntity(areaNet, out Entity simArea);
        rig.H.SimHost.World!.SetManagedComponent(simArea, new Hrot.IG.Components.EditablePolyline
        {
            Points = new System.Collections.Generic.List<System.Numerics.Vector2> { new(-50, -50), new(50, -50), new(50, 50), new(-50, 50) },
        });

        long net = rig.H.Cgf!.TestHook_SpawnEntityWithSplitAuthority(TkbEntityTypes.Tank_M1Abrams, muscleNodeId: 1);
        Assert.True(rig.H.PumpUntil(() => rig.H.Cgf!.GhostEntityMap!.TryGetEntity(net, out _)
                                       && rig.H.Cgf!.GhostEntityMap!.TryGetEntity(areaNet, out _), timeoutFrames: 3000));
        rig.H.Cgf!.GhostEntityMap!.TryGetEntity(net, out Entity e);
        var w = rig.H.Cgf!.World!;

        // A platoon commander needs a platoon: with an EMPTY roster the doctrine has nothing to command and finishes at
        // once (measured: FINISHED(Success) on frame 3). Two arrived subordinates — the shape HillAttackBlueprintTests
        // uses — keep it running (it asks its EQS area sensor and waits), which is when an agent reads the trace.
        var roster = new Fdp.Core.CommandHierarchy.UnitRoster { Count = 2 };
        for (int i = 0; i < 2; i++)
        {
            var sub = w.CreateEntity();
            w.AddComponent<Fdp.Toolkit.Behavior.Components.BehaviorState>(sub, default);
            w.AddComponent(sub, new Fdp.Toolkit.Navigation.NavigationStatus { Result = Fdp.Toolkit.Navigation.NavigationResult.Arrived });
            roster.SubordinateEntities[i] = sub;
        }
        if (w.HasComponent<Fdp.Core.CommandHierarchy.UnitRoster>(e)) w.GetComponentRW<Fdp.Core.CommandHierarchy.UnitRoster>(e) = roster;
        else w.AddComponent(e, roster);

        // ⭐ Assigned THROUGH A MISSION, as production does (CMD_REPLACE_MISSION over DDS — the CgfSubsystemHeadlessTests
        //   pattern). ⚠ A direct AssignBehaviorEvent is cleared two frames later: CGF's MissionAdapterSystem reads the
        //   entity's empty MissionPlanQueue as an exhausted plan (measured: CLEAR@3).
        using var participant = new CycloneDDS.Runtime.DdsParticipant((uint)rig.H.DomainId);
        using var writer = new CycloneDDS.Runtime.DdsWriter<Hrot.NED.Messages.MissionControlRequest>(participant, "MissionControlRequest");
        var taskId = Guid.NewGuid();
        writer.Write(new Hrot.NED.Messages.MissionControlRequest
        {
            RequestId      = Guid.NewGuid(),
            TargetEntityId = net,
            BaseVersion    = 0,
            Payload        = new Hrot.NED.Messages.MissionCommandUnion
            {
                _d              = Hrot.NED.Messages.eMissionCommandType.CMD_REPLACE_MISSION,
                FullMissionData = new Hrot.NED.Descriptors.MissionPlan
                {
                    ActiveTaskId = taskId,
                    Tasks = new System.Collections.Generic.List<Hrot.NED.Descriptors.MissionTask>
                    {
                        new Hrot.NED.Descriptors.MissionTask
                        {
                            TaskId          = taskId,
                            BehaviorId      = behaviour,
                            BehaviorParams  = HillAttackParams(areaNet),
                            ExecutingEngine = "CGFX",
                            State           = Hrot.NED.Descriptors.eTaskState.TASK_ACTIVE,
                            Triggers        = new System.Collections.Generic.List<Hrot.NED.Descriptors.MissionTrigger>
                            {
                                new Hrot.NED.Descriptors.MissionTrigger { Type = "BehaviorFinished", Params = "" },
                            },
                        },
                    },
                },
            },
        });

        Assert.True(rig.H.PumpUntil(() =>
                w.HasComponent<Fdp.Toolkit.Behavior.Components.BehaviorState>(e)
             && w.GetComponentRO<Fdp.Toolkit.Behavior.Components.BehaviorState>(e).ActiveBehaviorHash != 0,
                timeoutFrames: 1200),
            $"'{behaviour}' must be assigned on CGF through its mission.");
        // ⚠ The routes are read IN THIS FRAME, while the behaviour runs. In this harness the platoon is two bare local
        //   entities, so the doctrine concludes within a few frames and the mission then clears it (measured) — a
        //   long-running commander is the live --mode all run's job, not this rail's. Nothing advances between the
        //   assignment frame and the reads below, so they see the running behaviour deterministically.
        return net;
    }

    private static string Brain(Rig rig, long net)
    {
        rig.H.Cgf!.GhostEntityMap!.TryGetEntity(net, out Entity e);
        var w = rig.H.Cgf!.World!;
        var st = w.GetComponentRO<Fdp.Toolkit.Behavior.Components.BehaviorState>(e);
        return $"BehaviorState{{hash={st.ActiveBehaviorHash}, tier={st.BrainTier}}} surface={(rig.H.Cgf!.AiDebugSurface is null ? "null" : "ok")}";
    }

    [Fact(Timeout = 120_000)]
    public void A_headless_CGF_publishes_its_AI_debug_surface()
    {
        using var rig = Boot(() => "Scenario");
        var surface = rig.H.Cgf!.AiDebugSurface;

        Assert.NotNull(surface);
        Assert.Same(rig.H.Cgf!.World, surface!.World);
        Assert.NotNull(surface.BTree);
        Assert.NotNull(surface.Hsm);
        Assert.NotNull(surface.Blueprint);
        Assert.NotNull(surface.Blueprints);
    }

    [Fact(Timeout = 120_000)]
    public void The_trace_arms_and_reads_the_CSharp_BTree_commander()
    {
        using var rig = Boot(() => "Scenario");
        long commander = Commander(rig, "PlatoonHillAttack");

        var trace = rig.Api.GetEntityTrace(commander);
        Assert.True("BTree" == (string?)trace["tier"], $"trace: {trace.ToJsonString()} · {Brain(rig, commander)}");

        // Arming: the tracer exists for this world and publishes the patch (the flag lands a frame later).
        var armed = rig.Api.ObserveTrace(commander, on: true);
        Assert.Equal(true, (bool?)armed["armed"]);
    }

    [Fact(Timeout = 120_000)]
    public void The_trace_and_the_variables_read_the_blueprint_behaviour_commander()
    {
        using var rig = Boot(() => "Scenario");
        long commander = Commander(rig, "PlatoonHillAttackBp");

        var trace = rig.Api.GetEntityTrace(commander);
        Assert.True("Blueprint" == (string?)trace["tier"], $"trace: {trace.ToJsonString()} · {Brain(rig, commander)}");
        Assert.Equal("Behavior", (string?)trace["dispatch"]);
        Assert.Equal("PlatoonHillAttackBp", (string?)trace["behavior"]);
        var traceVariables = Assert.IsType<JsonObject>(trace["variables"]);
        Assert.True(traceVariables.ContainsKey("Phase"), $"trace variables: {traceVariables.ToJsonString()}");
        Assert.True(traceVariables.ContainsKey("Sensor"), $"trace variables: {traceVariables.ToJsonString()}");

        var (result, error, _) = rig.Api.GetEntityVariables(commander, asset: null);
        Assert.Null(error);
        var names = result!["variables"]!.AsArray().Select(v => (string?)v!["path"]).ToList();
        Assert.Contains("Phase", names);
        Assert.Contains("Sensor", names);

        var (one, oneError, _) = rig.Api.GetEntityVariable(commander, asset: "PlatoonHillAttackBp", path: "Phase");
        Assert.Null(oneError);
        Assert.Equal("HillAttackPhase", (string?)one!["type"]);
    }

    [Fact(Timeout = 120_000)]
    public void A_perspective_whose_node_owns_no_AI_sessions_borrows_none()
    {
        string perspective = "Scenario";
        using var rig = Boot(() => perspective);
        long commander = Commander(rig, "PlatoonHillAttackBp");

        perspective = "SimHost";   // the Muscle node: its world has a ghost of the commander, but no AI sessions
        var armed = rig.Api.ObserveTrace(commander, on: true);
        Assert.Equal(false, (bool?)armed["armed"]);
    }
}
