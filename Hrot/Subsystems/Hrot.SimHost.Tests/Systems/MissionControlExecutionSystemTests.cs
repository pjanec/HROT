using System;
using System.Collections.Generic;
using Hrot.Core.Mission;
using Hrot.Common.Events;
using Hrot.Common.Systems;
using Fdp.Core;
using Fdp.Toolkit.Behavior;
using Fdp.Toolkit.Behavior.Components;
using Fdp.Toolkit.Behavior.TacticalOrderMapper;
using Fdp.Toolkit.Replication.Services;
using Fdp.ModuleHost.Abstractions;
using Xunit;

namespace Hrot.SimHost.Tests.Systems;

/// <summary>
/// Unit tests for <see cref="MissionControlExecutionSystem"/>.
/// These tests exercise the pure-ECS execution logic in isolation â€” no DDS.
/// </summary>
public class MissionControlExecutionSystemTests
{
    // â”€â”€ Helpers â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

    private static EntityRepository CreateWorld()
    {
        var repo = new EntityRepository();
        repo.RegisterComponent<MissionPlanQueue>();
        repo.RegisterComponent<BehaviorState>();
        repo.RegisterManagedComponent<ActiveMissionPlan>();
        repo.SetSingletonUnmanaged(new GlobalTime { DeltaTime = 0.016f, TimeScale = 1.0f });
        repo.RegisterEvent<MissionControlAckEvent>();
        return repo;
    }

    private static BehaviorRegistry CreateBehaviorRegistry()
    {
        var registry = new BehaviorRegistry();
        registry.Register(101, "MoveToLocation",
            new BehaviorDefinition { Name = "MoveToLocation", BrainTier = BehaviorConstants.BrainTierBTree });
        return registry;
    }

    private static MissionPlan MakePlan(params Guid[] taskIds)
    {
        var tasks = new List<MissionTask>();
        foreach (var id in taskIds)
        {
            tasks.Add(new MissionTask
            {
                TaskId          = id,
                BehaviorId      = "MoveToLocation",
                BehaviorParams  = "{}",
                ExecutingEngine = "CGFX",
                State           = eTaskState.TASK_PLANNED,
                Triggers        = new List<Hrot.Core.Mission.MissionTrigger>()
            });
        }
        return new MissionPlan
        {
            ActiveTaskId = taskIds.Length > 0 ? taskIds[0] : Guid.Empty,
            Tasks = tasks
        };
    }

    private static MissionControlAckEvent? FindAck(EntityRepository repo, Guid requestId)
    {
        foreach (var evt in repo.Bus.Read<MissionControlAckEvent>())
        {
            if (evt.RequestId == requestId)
                return evt;
        }
        return null;
    }

    // â”€â”€ SC-1: Successful mission replace publishes ACK with ErrorCode == 0 â”€â”€â”€â”€

    /// <summary>
    /// SC-1: Construct system without DDS; create entity; publish
    /// <see cref="MissionControlIntent"/> via TestHook; verify <see cref="MissionPlanQueue"/>
    /// is updated and a <see cref="MissionControlAckEvent"/> with ErrorCode 0 is emitted.
    /// </summary>
    [Fact]
    public void ReplaceMission_ValidEntity_UpdatesQueueAndPublishesSuccessAck()
    {
        var entityMap = new NetworkEntityMap();
        using var repo = CreateWorld();
        var entity = repo.CreateEntity();
        entityMap.Register(1L, entity);

        var system = new Hrot.Common.Systems.MissionControlExecutionSystem(entityMap, CreateBehaviorRegistry(), new TacticalIntentMapperRegistry());

        var taskA = Guid.NewGuid();
        var requestId = Guid.NewGuid();

        system.TestHook_ProcessIntent(repo, new MissionControlIntent
        {
            RequestId      = requestId,
            TargetEntityId = 1L,
            BaseVersion    = 0,
            Payload = new MissionCommandPayload
            {
                CommandType     = eMissionCommandType.CMD_REPLACE_MISSION,
                FullMissionData = MakePlan(taskA)
            }
        });

        // Verify MissionPlanQueue updated.
        ref var queue = ref repo.GetComponentRW<MissionPlanQueue>(entity);
        Assert.Equal(1, queue.PhaseCount);
        Assert.Equal(0, queue.CurrentPhase);

        // Verify ACK event published with ErrorCode 0 (Success).
        repo.Bus.SwapBuffers();
        var ack = FindAck(repo, requestId);
        Assert.NotNull(ack);
        Assert.Equal(0, ack!.Value.ErrorCode);
        Assert.True(ack.Value.NewVersion > 0);
    }

    /// <summary>
    /// ⭐⭐ <c>R-3039</c> S1 (docs/DESIGN_System_State_Inventory.md §6 #7) — the mission's version and task order survive a checkpoint restored
    /// into a FRESH system: a three-task plan (version 1), keyframe, applied to a new world with a NEW system; then a jump to the third task lands
    /// on phase 2, and a replace built on version 2 is accepted. 🔴 Red-proof: either back in a system field ⇒ the jump lands on 0 / the replace
    /// is refused as a conflict ⇒ red.
    /// </summary>
    [Fact]
    public void R3039_TheMissionsVersionAndTaskOrder_SurviveACheckpointRestoredIntoAFreshSystem()
    {
        using var repo = CreateWorld();
        var entity = repo.CreateEntity();
        var map = new NetworkEntityMap();
        map.Register(1L, entity);
        Guid a = Guid.NewGuid(), b = Guid.NewGuid(), c = Guid.NewGuid();
        new Hrot.Common.Systems.MissionControlExecutionSystem(map, CreateBehaviorRegistry(), new TacticalIntentMapperRegistry())
            .TestHook_ProcessIntent(repo, new MissionControlIntent
            {
                RequestId = Guid.NewGuid(), TargetEntityId = 1L, BaseVersion = 0,
                Payload = new MissionCommandPayload { CommandType = eMissionCommandType.CMD_REPLACE_MISSION, FullMissionData = MakePlan(a, b, c) },
            });

        var kf = Fdp.Toolkit.Replay.WorldKeyframes.Capture(repo, Fdp.Toolkit.Replay.CaptureOptions.Recording);
        using var fresh = CreateWorld();
        Fdp.Toolkit.Replay.WorldKeyframes.Apply(fresh, kf);
        var freshMap = new NetworkEntityMap();
        freshMap.Register(1L, entity);
        var sys = new Hrot.Common.Systems.MissionControlExecutionSystem(freshMap, CreateBehaviorRegistry(), new TacticalIntentMapperRegistry());

        sys.TestHook_ProcessIntent(fresh, new MissionControlIntent
        {
            RequestId = Guid.NewGuid(), TargetEntityId = 1L,
            Payload = new MissionCommandPayload { CommandType = eMissionCommandType.CMD_JUMP_TO_TASK, TargetTaskId = c },
        });
        Assert.Equal(2, fresh.GetComponent<MissionPlanQueue>(entity).CurrentPhase);

        var replace = Guid.NewGuid();
        sys.TestHook_ProcessIntent(fresh, new MissionControlIntent
        {
            RequestId = replace, TargetEntityId = 1L, BaseVersion = 2,
            Payload = new MissionCommandPayload { CommandType = eMissionCommandType.CMD_REPLACE_MISSION, FullMissionData = MakePlan(a) },
        });
        fresh.Bus.SwapBuffers();
        var ack = FindAck(fresh, replace);
        Assert.NotNull(ack);
        Assert.Equal(0, ack!.Value.ErrorCode);
        Assert.Equal(3L, ack.Value.NewVersion);
    }

    // â”€â”€ SC-2: Wrong BaseVersion emits version-conflict NACK â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

    /// <summary>
    /// SC-2: Submit two missions; second uses wrong BaseVersion â€” must emit
    /// a <see cref="MissionControlAckEvent"/> with a non-zero ErrorCode and
    /// NOT mutate the <see cref="MissionPlanQueue"/>.
    /// </summary>
    [Fact]
    public void ReplaceMission_WrongBaseVersion_PublishesVersionConflictNack()
    {
        var entityMap = new NetworkEntityMap();
        using var repo = CreateWorld();
        var entity = repo.CreateEntity();
        entityMap.Register(1L, entity);

        var system = new Hrot.Common.Systems.MissionControlExecutionSystem(entityMap, CreateBehaviorRegistry(), new TacticalIntentMapperRegistry());

        // First mission succeeds (BaseVersion 0).
        var taskA = Guid.NewGuid();
        system.TestHook_ProcessIntent(repo, new MissionControlIntent
        {
            RequestId      = Guid.NewGuid(),
            TargetEntityId = 1L,
            BaseVersion    = 0,
            Payload = new MissionCommandPayload
            {
                CommandType     = eMissionCommandType.CMD_REPLACE_MISSION,
                FullMissionData = MakePlan(taskA)
            }
        });

        // Snapshot phase count after first mission.
        int phaseCountAfterFirst = repo.GetComponent<MissionPlanQueue>(entity).PhaseCount;
        Assert.Equal(1, phaseCountAfterFirst);

        // Second mission with wrong BaseVersion (should be 1, but using 99).
        var taskB = Guid.NewGuid();
        var conflictId = Guid.NewGuid();
        system.TestHook_ProcessIntent(repo, new MissionControlIntent
        {
            RequestId      = conflictId,
            TargetEntityId = 1L,
            BaseVersion    = 99,
            Payload = new MissionCommandPayload
            {
                CommandType     = eMissionCommandType.CMD_REPLACE_MISSION,
                FullMissionData = MakePlan(taskA, taskB)
            }
        });

        // Queue must NOT be mutated.
        Assert.Equal(phaseCountAfterFirst, repo.GetComponent<MissionPlanQueue>(entity).PhaseCount);

        // NACK with non-zero ErrorCode.
        repo.Bus.SwapBuffers();
        var ack = FindAck(repo, conflictId);
        Assert.NotNull(ack);
        Assert.NotEqual(0, ack!.Value.ErrorCode);
    }

    // â”€â”€ SC-3: Unknown entity queues for retry, eventually NACKs â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

    [Fact]
    public void ProcessIntent_UnknownEntity_EnqueuesForRetryThenNacks()
    {
        var entityMap = new NetworkEntityMap();
        using var repo = CreateWorld();
        // Do NOT register entity â€” simulate unknown entity.

        var system = new Hrot.Common.Systems.MissionControlExecutionSystem(entityMap, CreateBehaviorRegistry(), new TacticalIntentMapperRegistry());

        var requestId = Guid.NewGuid();
        system.TestHook_ProcessIntent(repo, new MissionControlIntent
        {
            RequestId      = requestId,
            TargetEntityId = 999L,
            BaseVersion    = 0,
            Payload = new MissionCommandPayload
            {
                CommandType = eMissionCommandType.CMD_ABORT_ALL
            }
        });

        // After first call, entity queued with framesLeft=10.
        Assert.True(system.TestHook_RetryQueueCount > 0);

        // Drain 11 cycles to exhaust.
        for (int i = 0; i < 11; i++)
            system.TestHook_DrainRetryQueue(repo);

        Assert.Equal(0, system.TestHook_RetryQueueCount);

        repo.Bus.SwapBuffers();
        var ack = FindAck(repo, requestId);
        Assert.NotNull(ack);
        Assert.NotEqual(0, ack!.Value.ErrorCode);
    }

    // â”€â”€ SC-4: Entity found on retry publishes success â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

    [Fact]
    public void ProcessIntent_EntityAppearsOnRetry_PublishesSuccessAck()
    {
        var entityMap = new NetworkEntityMap();
        using var repo = CreateWorld();
        // Entity NOT yet registered.

        var system = new Hrot.Common.Systems.MissionControlExecutionSystem(entityMap, CreateBehaviorRegistry(), new TacticalIntentMapperRegistry());

        var taskA = Guid.NewGuid();
        var requestId = Guid.NewGuid();

        system.TestHook_ProcessIntent(repo, new MissionControlIntent
        {
            RequestId      = requestId,
            TargetEntityId = 42L,
            BaseVersion    = 0,
            Payload = new MissionCommandPayload
            {
                CommandType     = eMissionCommandType.CMD_REPLACE_MISSION,
                FullMissionData = MakePlan(taskA)
            }
        });

        Assert.True(system.TestHook_RetryQueueCount > 0, "Expected intent in retry queue.");

        // Register entity before retry drains.
        var entity = repo.CreateEntity();
        entityMap.Register(42L, entity);

        system.TestHook_DrainRetryQueue(repo);

        Assert.Equal(0, system.TestHook_RetryQueueCount);

        repo.Bus.SwapBuffers();
        var ack = FindAck(repo, requestId);
        Assert.NotNull(ack);
        Assert.Equal(0, ack!.Value.ErrorCode);
    }

    // â”€â”€ SC-S301: ActiveMissionPlan stored and removed via managed component API â”€

    /// <summary>
    /// S301-SC1/SC2/SC3: After CMD_REPLACE_MISSION, <see cref="ActiveMissionPlan"/>
    /// must be retrievable via <c>HasManagedComponent</c> and <c>GetManagedComponent</c>.
    /// After CMD_ABORT_ALL it must be absent.
    /// </summary>
    [Fact]
    public void ReplaceMission_SetsManagedComponent_AbortAll_ClearsIt()
    {
        var entityMap = new NetworkEntityMap();
        using var repo = CreateWorld();
        var entity = repo.CreateEntity();
        entityMap.Register(1L, entity);

        var system = new Hrot.Common.Systems.MissionControlExecutionSystem(entityMap, CreateBehaviorRegistry(), new TacticalIntentMapperRegistry());

        var taskA = Guid.NewGuid();
        var taskB = Guid.NewGuid();

        // â”€â”€ Replace mission â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
        system.TestHook_ProcessIntent(repo, new MissionControlIntent
        {
            RequestId      = Guid.NewGuid(),
            TargetEntityId = 1L,
            BaseVersion    = 0,
            Payload = new MissionCommandPayload
            {
                CommandType     = eMissionCommandType.CMD_REPLACE_MISSION,
                FullMissionData = MakePlan(taskA, taskB)
            }
        });

        // SC1: HasManagedComponent returns true.
        Assert.True(repo.HasManagedComponent<ActiveMissionPlan>(entity),
            "ActiveMissionPlan must be stored via SetManagedComponent after CMD_REPLACE_MISSION");

        // SC2: GetManagedComponentRO is not null and Tasks count matches.
        var plan = ((ISimulationView)repo).GetManagedComponentRO<ActiveMissionPlan>(entity);
        Assert.NotNull(plan);
        Assert.NotNull(plan!.Plan);
        Assert.Equal(2, plan.Plan.Tasks.Count);

        // â”€â”€ Abort all â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
        system.TestHook_ProcessIntent(repo, new MissionControlIntent
        {
            RequestId      = Guid.NewGuid(),
            TargetEntityId = 1L,
            BaseVersion    = 1,
            Payload        = new MissionCommandPayload { CommandType = eMissionCommandType.CMD_ABORT_ALL }
        });

        // SC3: HasManagedComponent returns false after CMD_ABORT_ALL.
        Assert.False(repo.HasManagedComponent<ActiveMissionPlan>(entity),
            "ActiveMissionPlan must be cleared after CMD_ABORT_ALL");
    }

    // â”€â”€ SC-S302: TryBuildQueue â€” Span mutation produces correct PhaseCount â”€â”€â”€â”€â”€â”€

    /// <summary>
    /// S302-SC1/SC2: Processing a 3-task plan produces a <see cref="MissionPlanQueue"/>
    /// with <c>PhaseCount == 3</c> and each phase bearing the expected behavior ID.
    /// </summary>
    [Fact]
    public void ReplaceMission_3TaskPlan_PhaseCountAndBehaviorIdCorrect()
    {
        var entityMap = new NetworkEntityMap();
        using var repo = CreateWorld();
        var entity = repo.CreateEntity();
        entityMap.Register(1L, entity);

        var system = new Hrot.Common.Systems.MissionControlExecutionSystem(entityMap, CreateBehaviorRegistry(), new TacticalIntentMapperRegistry());

        var taskA = Guid.NewGuid();
        var taskB = Guid.NewGuid();
        var taskC = Guid.NewGuid();

        system.TestHook_ProcessIntent(repo, new MissionControlIntent
        {
            RequestId      = Guid.NewGuid(),
            TargetEntityId = 1L,
            BaseVersion    = 0,
            Payload = new MissionCommandPayload
            {
                CommandType     = eMissionCommandType.CMD_REPLACE_MISSION,
                FullMissionData = MakePlan(taskA, taskB, taskC)
            }
        });

        // SC1: PhaseCount == 3.
        var queue = repo.GetComponent<MissionPlanQueue>(entity);
        Assert.Equal(3, queue.PhaseCount);

        // SC2: Each phase has the MoveToLocation behavior ID (101).
        Span<MissionPhase> phases = queue.Phases;
        for (int i = 0; i < 3; i++)
            Assert.Equal(101, phases[i].BehaviorId);
    }

    /// <summary>
    /// S302-SC3: A plan with zero tasks produces <c>PhaseCount == 0</c>.
    /// </summary>
    [Fact]
    public void ReplaceMission_EmptyPlan_PhaseCountIsZero()
    {
        var entityMap = new NetworkEntityMap();
        using var repo = CreateWorld();
        var entity = repo.CreateEntity();
        entityMap.Register(1L, entity);

        var system = new Hrot.Common.Systems.MissionControlExecutionSystem(entityMap, CreateBehaviorRegistry(), new TacticalIntentMapperRegistry());

        system.TestHook_ProcessIntent(repo, new MissionControlIntent
        {
            RequestId      = Guid.NewGuid(),
            TargetEntityId = 1L,
            BaseVersion    = 0,
            Payload = new MissionCommandPayload
            {
                CommandType     = eMissionCommandType.CMD_REPLACE_MISSION,
                FullMissionData = new MissionPlan { Tasks = new List<MissionTask>() }
            }
        });

        var queue = repo.GetComponent<MissionPlanQueue>(entity);
        Assert.Equal(0, queue.PhaseCount);
    }

    // ── CE-3167: an immediate order clears the plan through IIntentClearProvider ─────────────────

    /// <summary>
    /// ⭐ CE-3167 (Entity Commanding §2.5d) — the system is the mission plan's clear provider: ClearFor empties the queue and the
    /// plan exactly as CMD_ABORT_ALL does, but publishes NO ClearBehaviorEvent (the order's assign replaces the behaviour), and a
    /// second call on the cleared unit changes nothing.
    /// </summary>
    [Fact]
    public void CE3167_ClearFor_EmptiesThePlan_WithoutClearingTheBehaviour_AndIsIdempotent()
    {
        var entityMap = new NetworkEntityMap();
        using var repo = CreateWorld();
        repo.RegisterEvent<Fdp.Toolkit.Behavior.Events.ClearBehaviorEvent>();
        var entity = repo.CreateEntity();
        entityMap.Register(1L, entity);
        var system = new Hrot.Common.Systems.MissionControlExecutionSystem(entityMap, CreateBehaviorRegistry(), new TacticalIntentMapperRegistry());
        system.TestHook_ProcessIntent(repo, new MissionControlIntent
        {
            RequestId = Guid.NewGuid(), TargetEntityId = 1L, BaseVersion = 0,
            Payload = new MissionCommandPayload { CommandType = eMissionCommandType.CMD_REPLACE_MISSION, FullMissionData = MakePlan(Guid.NewGuid(), Guid.NewGuid()) },
        });
        Assert.Equal(2, repo.GetComponentRO<MissionPlanQueue>(entity).PhaseCount);
        repo.Bus.SwapBuffers();

        IIntentClearProvider provider = system;
        provider.ClearFor(entity, repo);

        Assert.Equal(0, repo.GetComponentRO<MissionPlanQueue>(entity).PhaseCount);
        Assert.False(repo.HasManagedComponent<ActiveMissionPlan>(entity));
        repo.Bus.SwapBuffers();
        Assert.Empty(repo.Bus.Read<Fdp.Toolkit.Behavior.Events.ClearBehaviorEvent>().ToArray());

        provider.ClearFor(entity, repo);   // idempotent: no plan, nothing to do
        Assert.Equal(0, repo.GetComponentRO<MissionPlanQueue>(entity).PhaseCount);
    }
}
