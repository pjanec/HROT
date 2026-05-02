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
/// Unit tests for the FollowRoute network-ID translation in
/// <see cref="MissionControlExecutionSystem"/> â€” OC1-S001.
/// These tests use the internal test constructor to avoid DDS setup.
/// </summary>
public class MissionControlRequestSystemFollowRouteTests
{
    // â”€â”€ Helpers â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

    private static EntityRepository CreateWorld()
    {
        var repo = new EntityRepository();
        repo.RegisterComponent<MissionPlanQueue>();
        repo.RegisterComponent<DoctrineState>();
        repo.RegisterComponent<BrainBTreeState>();
        repo.RegisterManagedComponent<ActiveMissionPlan>();
        repo.RegisterComponent<NetworkIdentity>();
        repo.RegisterComponent<RouteTrajectoryCache>();
        repo.RegisterManagedComponent<RoutePlan>();
        repo.SetSingletonUnmanaged(new GlobalTime { DeltaTime = 0.016f, TimeScale = 1.0f });
        repo.RegisterEvent<MissionControlAckEvent>();
        return repo;
    }

    private static DoctrineRegistry CreateDoctrineRegistry()
    {
        var registry = new DoctrineRegistry();
        registry.Register(42, "FollowRoute",
            new DoctrineDefinition { Name = "FollowRoute", BrainTier = BehaviorConstants.BrainTierBTree });
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

    /// <summary>
    /// OC1-S001 Scenario 1 â€” route entity found with compiled trajectory:
    /// BehaviorParams must be rewritten to contain trajectoryId instead of routeEntityId.
    /// </summary>
    [Fact]
    public void FollowRoute_RouteEntityFound_RewritesBehaviorParams()
    {
        using var repo = CreateWorld();
        var entityMap  = new NetworkEntityMap();
        var registry   = CreateDoctrineRegistry();
        var system     = new Hrot.Common.Systems.MissionControlExecutionSystem(entityMap, registry, new TacticalIntentMapperRegistry());

        var (vehicle, _) = SetupEntities(repo, entityMap, vehicleNetId: 1L, routeNetId: 99L, trajectoryId: 5);

        var request = MakeFollowRouteRequest(targetEntityId: 1L, routeEntityId: 99L, speed: 12.0, loop: true);
        system.TestHook_ProcessIntent(repo, request);

        // Verify rewritten BehaviorParams stored in ActiveMissionPlan.
        var activePlan = ((ISimulationView)repo).GetManagedComponentRO<ActiveMissionPlan>(vehicle);
        var storedTask = activePlan.Plan.Tasks[0];

        using var doc = JsonDocument.Parse(storedTask.BehaviorParams);
        Assert.True(doc.RootElement.TryGetProperty("trajectoryId", out var tidEl));
        Assert.Equal(5, tidEl.GetInt32());
        Assert.True(doc.RootElement.TryGetProperty("Speed", out var speedEl));
        Assert.Equal(12.0, speedEl.GetDouble(), precision: 3);
        Assert.True(doc.RootElement.TryGetProperty("Loop", out var loopEl));
        Assert.True(loopEl.GetBoolean());
    }

    /// <summary>
    /// OC1-S001 Scenario 2 â€” route entity not found: request must be placed in the retry queue.
    /// </summary>
    [Fact]
    public void FollowRoute_RouteEntityNotFound_EnqueuesForRetry()
    {
        using var repo = CreateWorld();
        var entityMap  = new NetworkEntityMap();
        var registry   = CreateDoctrineRegistry();
        var system     = new Hrot.Common.Systems.MissionControlExecutionSystem(entityMap, registry, new TacticalIntentMapperRegistry());

        // Register only the vehicle; no route entity.
        var vehicle = repo.CreateEntity();
        repo.AddComponent(vehicle, new MissionPlanQueue());
        repo.SetManagedComponent(vehicle, new ActiveMissionPlan());
        entityMap.Register(1L, vehicle);

        var request = MakeFollowRouteRequest(targetEntityId: 1L, routeEntityId: 99L);
        system.TestHook_ProcessIntent(repo, request);

        Assert.True(system.TestHook_RetryQueueCount > 0, "Expected request in retry queue.");
    }

    /// <summary>
    /// OC1-S001 Scenario 3 â€” route entity present but TrajectoryId == 0: retry.
    /// </summary>
    [Fact]
    public void FollowRoute_TrajectoryIdZero_EnqueuesForRetry()
    {
        using var repo = CreateWorld();
        var entityMap  = new NetworkEntityMap();
        var registry   = CreateDoctrineRegistry();
        var system     = new Hrot.Common.Systems.MissionControlExecutionSystem(entityMap, registry, new TacticalIntentMapperRegistry());

        var (_, routeEntity) = SetupEntities(repo, entityMap, vehicleNetId: 1L, routeNetId: 99L, trajectoryId: 0);

        var request = MakeFollowRouteRequest(targetEntityId: 1L, routeEntityId: 99L);
        system.TestHook_ProcessIntent(repo, request);

        Assert.True(system.TestHook_RetryQueueCount > 0, "Expected request in retry queue.");
    }

    /// <summary>
    /// OC1-S001 Scenario 4 â€” route compiles between retries: mission committed on second cycle.
    /// </summary>
    [Fact]
    public void FollowRoute_RouteCompilesOnRetry_CommitsOnSecondCycle()
    {
        using var repo = CreateWorld();
        var entityMap  = new NetworkEntityMap();
        var registry   = CreateDoctrineRegistry();
        var system     = new Hrot.Common.Systems.MissionControlExecutionSystem(entityMap, registry, new TacticalIntentMapperRegistry());

        var (vehicle, routeEntity) = SetupEntities(repo, entityMap, vehicleNetId: 1L, routeNetId: 99L, trajectoryId: 0);

        // First cycle â€” should retry.
        var request = MakeFollowRouteRequest(targetEntityId: 1L, routeEntityId: 99L, speed: 5.0, loop: false);
        system.TestHook_ProcessIntent(repo, request);
        Assert.True(system.TestHook_RetryQueueCount > 0);

        // Route compiles â€” update TrajectoryId.
        repo.SetComponent(routeEntity, new RouteTrajectoryCache { TrajectoryId = 7, CompiledVersion = 1 });

        // Second cycle (drain retry queue).
        system.TestHook_DrainRetryQueue(repo);

        // Mission should now be committed.
        var activePlan = ((ISimulationView)repo).GetManagedComponentRO<ActiveMissionPlan>(vehicle);
        var storedTask = activePlan.Plan.Tasks[0];
        using var doc  = JsonDocument.Parse(storedTask.BehaviorParams);
        Assert.Equal(7, doc.RootElement.GetProperty("trajectoryId").GetInt32());
    }

    /// <summary>
    /// OC1-S001 Scenario 5 â€” non-FollowRoute task: BehaviorParams left unchanged, no ECS query.
    /// </summary>
    [Fact]
    public void NonFollowRouteTask_BehaviorParamsUnchanged()
    {
        using var repo = CreateWorld();
        var entityMap  = new NetworkEntityMap();

        var registry = new DoctrineRegistry();
        registry.Register(1, "Wander",
            new DoctrineDefinition { Name = "Wander", BrainTier = BehaviorConstants.BrainTierBTree });

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

    /// <summary>
    /// OC1-S001 Scenario 6 â€” ParseFollowRouteParams roundtrip:
    /// Verifies <see cref="MissionControlRequestSystem.TryTranslateFollowRouteBehaviorParams"/>
    /// produces JSON consumable by the existing <c>ParseFollowRouteParams</c> logic.
    /// </summary>
    [Fact]
    public void TryTranslateFollowRouteBehaviorParams_ProducesCorrectJson()
    {
        using var repo = CreateWorld();

        var routeEntity = repo.CreateEntity();
        repo.AddComponent(routeEntity, new NetworkIdentity { Value = 42L });
        repo.AddComponent(routeEntity, new RouteTrajectoryCache { TrajectoryId = 5, CompiledVersion = 1 });

        const string input = "{\"routeEntityId\":42,\"Speed\":12.0,\"Loop\":true}";
        bool ok = MissionControlBehaviorParamsHelper.TryTranslateFollowRouteBehaviorParams(repo, input, out var translated);

        Assert.True(ok);
        using var doc = JsonDocument.Parse(translated);
        Assert.Equal(5, doc.RootElement.GetProperty("trajectoryId").GetInt32());
        Assert.Equal(12.0, doc.RootElement.GetProperty("Speed").GetDouble(), precision: 3);
        Assert.True(doc.RootElement.GetProperty("Loop").GetBoolean());
    }
}
