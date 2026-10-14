using System;
using System.Collections.Generic;
using System.Text.Json;
using Fdp.Interfaces;
using Hrot.Common.Events;
using Hrot.Core.Mission;
using Hrot.Map.Common.Components;
using Hrot.Map.Common.Helpers;
using Hrot.SimHost.Systems;
using Fdp.Core;
using Fdp.Toolkit.Behavior;
using Fdp.Toolkit.Behavior.Components;
using Fdp.Toolkit.Behavior.TacticalOrderMapper;
using Fdp.Toolkit.Replication.Components;
using Fdp.Toolkit.Replication.Services;
using Fdp.ModuleHost.Abstractions;

namespace Hrot.SimHost.Tests.Systems;

/// <summary>
/// FollowRoute on the mission path. ⭐ CE-2145 (docs/DESIGN_Orders_Params_Entity_Classes.md §10, R-273) — the mission system
/// passes a FollowRoute task's params through unchanged; the BEHAVIOUR converts the route entity to its trajectory
/// (CgfNodes.ResolveFollowRoute) and waits while it is not compiled. ⛔ OC1-S001's sender-side rewrite + retry are gone.
/// These tests use the internal test constructor to avoid DDS setup.
/// </summary>
public class MissionControlRequestSystemFollowRouteTests
{
    // â”€â”€ Helpers â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

    private static EntityRepository CreateWorld()
    {
        var repo = new EntityRepository();
        repo.RegisterComponent<MissionPlanQueue>();
        repo.RegisterComponent<BehaviorState>();
        repo.RegisterManagedComponent<ActiveMissionPlan>();
        repo.RegisterComponent<NetworkIdentity>();
        repo.RegisterComponent<RouteTrajectoryCache>();
        repo.RegisterManagedComponent<RoutePlan>();
        repo.SetSingletonUnmanaged(new GlobalTime { DeltaTime = 0.016f, TimeScale = 1.0f });
        repo.RegisterEvent<MissionControlAckEvent>();
        return repo;
    }

    private static BehaviorRegistry CreateBehaviorRegistry()
    {
        var registry = new BehaviorRegistry();
        registry.Register(42, "FollowRoute",
            new BehaviorDefinition { Name = "FollowRoute", BrainTier = BehaviorConstants.BrainTierBTree });
        return registry;
    }

    private static (Entity vehicle, Entity routeEntity) SetupEntities(
        EntityRepository repo, NetworkEntityMap entityMap,
        long vehicleNetId, long routeNetId, int trajectoryId)
    {
        var vehicle = repo.CreateEntity();
        repo.AddComponent(vehicle, new MissionPlanQueue());
        repo.SetManagedComponent(vehicle, new ActiveMissionPlan());
        entityMap.Register(vehicleNetId, vehicle);

        var routeEntity = repo.CreateEntity();
        repo.AddComponent(routeEntity, new NetworkIdentity { Value = routeNetId });
        repo.AddComponent(routeEntity, new RouteTrajectoryCache { TrajectoryId = trajectoryId, CompiledVersion = trajectoryId > 0 ? 1 : 0 });
        repo.SetManagedComponent(routeEntity, new RoutePlan());

        return (vehicle, routeEntity);
    }

    private static MissionControlIntent MakeFollowRouteRequest(long targetEntityId, long routeEntityId, double speed = 5.0, bool loop = false)
    {
        return new MissionControlIntent
        {
            RequestId      = Guid.NewGuid(),
            TargetEntityId = targetEntityId,
            BaseVersion    = 0,
            Payload        = new MissionCommandPayload
            {
                CommandType     = eMissionCommandType.CMD_REPLACE_MISSION,
                FullMissionData = new MissionPlan
                {
                    Tasks = new List<MissionTask>
                    {
                        new MissionTask
                        {
                            TaskId          = Guid.NewGuid(),
                            BehaviorId      = "FollowRoute",
                            BehaviorParams  = $"{{\"routeEntityId\":{routeEntityId},\"Speed\":{speed},\"Loop\":{(loop ? "true" : "false")}}}",
                            ExecutingEngine = string.Empty,
                            State           = eTaskState.TASK_PLANNED,
                            Triggers        = new List<Hrot.Core.Mission.MissionTrigger>()
                        }
                    }
                }
            }
        };
    }

    /// <summary>⭐ CE-2145 (was OC1-S001 Scenario 1) — a FollowRoute task's params are stored as sent: the published contract,
    /// not a rewritten {trajectoryId}. 🔴 Red-proof: restore the sender-side rewrite → the stored JSON names trajectoryId.</summary>
    [Fact]
    public void CE2145_FollowRoute_TheMissionStoresTheAuthoredParams_Unchanged()
    {
        using var repo = CreateWorld();
        var entityMap  = new NetworkEntityMap();
        var system     = new Hrot.Common.Systems.MissionControlExecutionSystem(entityMap, CreateBehaviorRegistry(), new TacticalIntentMapperRegistry());
        var (vehicle, _) = SetupEntities(repo, entityMap, vehicleNetId: 1L, routeNetId: 99L, trajectoryId: 5);

        var request = MakeFollowRouteRequest(targetEntityId: 1L, routeEntityId: 99L, speed: 12.0, loop: true);
        string sent = request.Payload.FullMissionData!.Tasks[0].BehaviorParams;
        system.TestHook_ProcessIntent(repo, request);

        var stored = ((ISimulationView)repo).GetManagedComponentRO<ActiveMissionPlan>(vehicle).Plan.Tasks[0].BehaviorParams;
        Assert.Equal(sent, stored);
        Assert.Equal(0, system.TestHook_RetryQueueCount);
    }

    /// <summary>⭐ CE-2145 (was OC1-S001 Scenarios 2–3) — an unknown or not-yet-compiled route no longer holds the MISSION back:
    /// it commits at once, and the behaviour waits instead (rail below).</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void CE2145_FollowRoute_ARouteNotReady_DoesNotHoldTheMission(bool routeExists)
    {
        using var repo = CreateWorld();
        var entityMap  = new NetworkEntityMap();
        var system     = new Hrot.Common.Systems.MissionControlExecutionSystem(entityMap, CreateBehaviorRegistry(), new TacticalIntentMapperRegistry());
        Entity vehicle;
        if (routeExists)
            (vehicle, _) = SetupEntities(repo, entityMap, vehicleNetId: 1L, routeNetId: 99L, trajectoryId: 0);
        else
        {
            vehicle = repo.CreateEntity();
            repo.AddComponent(vehicle, new MissionPlanQueue());
            repo.SetManagedComponent(vehicle, new ActiveMissionPlan());
            entityMap.Register(1L, vehicle);
        }

        system.TestHook_ProcessIntent(repo, MakeFollowRouteRequest(targetEntityId: 1L, routeEntityId: 99L));

        Assert.Equal(0, system.TestHook_RetryQueueCount);
        Assert.Single(((ISimulationView)repo).GetManagedComponentRO<ActiveMissionPlan>(vehicle).Plan.Tasks);
    }

    /// <summary>⭐ CE-2145 (was OC1-S001 Scenario 6) — the behaviour's typed resolver turns the authored contract into the
    /// block: the route's compiled trajectory, the speed and the loop flag, and the route id kept for a later look-up.</summary>
    [Fact]
    public void CE2145_TheResolver_FindsTheCompiledTrajectory_AndKeepsSpeedAndLoop()
    {
        using var repo = CreateWorld();
        var route = repo.CreateEntity();
        repo.AddComponent(route, new NetworkIdentity { Value = 42L });
        repo.AddComponent(route, new RouteTrajectoryCache { TrajectoryId = 5, CompiledVersion = 1 });

        var authored = JsonSerializer.Deserialize<Hrot.Map.Definitions.Behavior.FollowRouteParamsJsonDto>(
            "{\"routeEntityId\":42,\"Speed\":12.0,\"Loop\":true}", Fdp.Toolkit.Behavior.BehaviorParams.JsonOptions)!;
        var block = default(Hrot.AI.Behaviors.Brains.CgfNodes.FollowRouteParams);
        Hrot.AI.Behaviors.Brains.CgfNodes.ResolveFollowRoute(authored, ref block, repo, Entity.Null);

        Assert.Equal(5, block.TrajectoryId);
        Assert.Equal(42L, block.RouteNetworkId);
        Assert.Equal(12f, block.Speed);
        Assert.True(block.Loop);
    }

    /// <summary>⭐ CE-2145 — an EMPTY params string reaches the typed resolver as a null DTO (BehaviorParams.FromBlockResolver
    /// passes default for a class); it resolves to "no route" instead of throwing. 🔴 Red-proof: drop the null guard → NRE.</summary>
    [Fact]
    public void CE2145_TheResolver_WithNoParams_ResolvesToNoRoute_WithoutThrowing()
    {
        using var repo = CreateWorld();
        var block = default(Hrot.AI.Behaviors.Brains.CgfNodes.FollowRouteParams);
        Hrot.AI.Behaviors.Brains.CgfNodes.ResolveFollowRoute(null!, ref block, repo, Entity.Null);
        Assert.Equal(0L, block.RouteNetworkId);
        Assert.Equal(0, block.TrajectoryId);
    }

    /// <summary>⭐ CE-2145 (was OC1-S001 Scenario 4), re-pinned by ⭐⭐ CE-2171 (Air Ops E2, DESIGN_Air_Operations §5a) — a route
    /// compiled AFTER the start. The action no longer waits for the LOCAL trajectory (a pool id only the motion node makes, which
    /// never crosses the wire — on a split cluster it waited for good): it starts at once carrying the route ENTITY, and the motion
    /// side resolves its own trajectory. When this node does compile the route (an all-in-one host) the local id rides along.
    /// 🔴 Red-proof: restore the wait → the first tick leaves the channel untouched.</summary>
    [Fact]
    public unsafe void CE2145_TheAction_StartsWithTheRouteEntity_ThenAddsTheLocalTrajectory()
    {
        using var repo = CreateWorld();
        repo.RegisterComponent<Fdp.Toolkit.Behavior.Components.LocomotionChannel>();
        var (vehicle, route) = SetupEntities(repo, new NetworkEntityMap(), vehicleNetId: 1L, routeNetId: 99L, trajectoryId: 0);
        repo.AddComponent(vehicle, new Fdp.Toolkit.Behavior.Components.LocomotionChannel());

        var authored = JsonSerializer.Deserialize<Hrot.Map.Definitions.Behavior.FollowRouteParamsJsonDto>("{\"routeEntityId\":99}", Fdp.Toolkit.Behavior.BehaviorParams.JsonOptions)!;
        var p = default(Hrot.AI.Behaviors.Brains.CgfNodes.FollowRouteParams);
        Hrot.AI.Behaviors.Brains.CgfNodes.ResolveFollowRoute(authored, ref p, repo, vehicle);
        Assert.Equal(0, p.TrajectoryId);

        Assert.Equal(Fbt.NodeStatus.Running, Hrot.AI.Behaviors.Brains.CgfNodes.Action_WriteFollowRouteChannel(ref p, vehicle, repo));
        var first = repo.GetComponent<Fdp.Toolkit.Behavior.Components.LocomotionChannel>(vehicle);
        Assert.Equal(Fdp.Toolkit.Navigation.NavigationConstants.ActionIdFollowRoute, first.ActiveAction);
        Assert.Equal(99L, (*(Fdp.Toolkit.Navigation.FollowRouteParams*)first.Params).RouteEntityId);
        Assert.Equal(0, (*(Fdp.Toolkit.Navigation.FollowRouteParams*)first.Params).TrajectoryId);
        repo.GetComponentRW<Fdp.Toolkit.Behavior.Components.LocomotionChannel>(vehicle).Status = Fbt.NodeStatus.Running;   // the executor took it

        repo.SetComponent(route, new RouteTrajectoryCache { TrajectoryId = 7, CompiledVersion = 1 });
        Hrot.AI.Behaviors.Brains.CgfNodes.Action_WriteFollowRouteChannel(ref p, vehicle, repo);

        var ch = repo.GetComponent<Fdp.Toolkit.Behavior.Components.LocomotionChannel>(vehicle);
        Assert.Equal(first.ActionInstanceId, ch.ActionInstanceId);   // the same order, not a restart
        Assert.Equal(7, (*(Fdp.Toolkit.Navigation.FollowRouteParams*)ch.Params).TrajectoryId);
        Assert.Equal(99L, (*(Fdp.Toolkit.Navigation.FollowRouteParams*)ch.Params).RouteEntityId);
    }

    /// <summary>⭐ CE-2145's wait survives where it still means something: with NO route entity to send, the action waits (Running,
    /// the channel untouched) — there is nothing the motion side could resolve.</summary>
    [Fact]
    public void CE2145_WithoutARouteEntity_TheActionWaits()
    {
        using var repo = CreateWorld();
        repo.RegisterComponent<Fdp.Toolkit.Behavior.Components.LocomotionChannel>();
        var vehicle = repo.CreateEntity();
        repo.AddComponent(vehicle, new Fdp.Toolkit.Behavior.Components.LocomotionChannel());
        var p = default(Hrot.AI.Behaviors.Brains.CgfNodes.FollowRouteParams);

        Assert.Equal(Fbt.NodeStatus.Running, Hrot.AI.Behaviors.Brains.CgfNodes.Action_WriteFollowRouteChannel(ref p, vehicle, repo));
        Assert.NotEqual(Fdp.Toolkit.Navigation.NavigationConstants.ActionIdFollowRoute,
            repo.GetComponent<Fdp.Toolkit.Behavior.Components.LocomotionChannel>(vehicle).ActiveAction);
    }

    /// <summary>
    /// OC1-S001 Scenario 5 â€” non-FollowRoute task: BehaviorParams left unchanged, no ECS query.
    /// </summary>
    [Fact]
    public void NonFollowRouteTask_BehaviorParamsUnchanged()
    {
        using var repo = CreateWorld();
        var entityMap  = new NetworkEntityMap();

        var registry = new BehaviorRegistry();
        registry.Register(1, "Wander",
            new BehaviorDefinition { Name = "Wander", BrainTier = BehaviorConstants.BrainTierBTree });

        var system = new Hrot.Common.Systems.MissionControlExecutionSystem(entityMap, registry, new TacticalIntentMapperRegistry());

        var vehicle = repo.CreateEntity();
        repo.AddComponent(vehicle, new MissionPlanQueue());
        repo.SetManagedComponent(vehicle, new ActiveMissionPlan());
        entityMap.Register(1L, vehicle);

        const string originalParams = "{\"radius\":100}";
        var request = new MissionControlIntent
        {
            RequestId      = Guid.NewGuid(),
            TargetEntityId = 1L,
            BaseVersion    = 0,
            Payload        = new MissionCommandPayload
            {
                CommandType     = eMissionCommandType.CMD_REPLACE_MISSION,
                FullMissionData = new MissionPlan
                {
                    Tasks = new List<MissionTask>
                    {
                        new MissionTask
                        {
                            TaskId          = Guid.NewGuid(),
                            BehaviorId      = "Wander",
                            BehaviorParams  = originalParams,
                            ExecutingEngine = string.Empty,
                            State           = eTaskState.TASK_PLANNED,
                            Triggers        = new List<Hrot.Core.Mission.MissionTrigger>()
                        }
                    }
                }
            }
        };

        system.TestHook_ProcessIntent(repo, request);

        var activePlan = ((ISimulationView)repo).GetManagedComponentRO<ActiveMissionPlan>(vehicle);
        Assert.Equal(originalParams, activePlan.Plan.Tasks[0].BehaviorParams);
        Assert.Equal(0, system.TestHook_RetryQueueCount);
    }
}
