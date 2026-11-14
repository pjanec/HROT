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
        entityMap.Register(1, entity);

        var system = new Hrot.Common.Systems.MissionControlExecutionSystem(entityMap, CreateBehaviorRegistry(), new TacticalIntentMapperRegistry());

        var taskA = Guid.NewGuid();
        var requestId = Guid.NewGuid();

        system.TestHook_ProcessIntent(repo, new MissionControlIntent
        {
            RequestId      = requestId,
            TargetEntityId = 1,
            BaseVersion    = 0,
            Payload = new MissionCommandPayload
            {
                CommandType     = eMissionCommandType.CMD_REPLACE_MISSION,
                FullMissionData = MakePlan(taskA)
            }
        });

        // Verify MissionPlanQueue updated.
        ref var queue = ref repo.GetComponentRW<MissionPlanQueue>(entity);
        Assert.Equal(1, queue.TaskCount);
        Assert.Equal(0, queue.CurrentTask);

        // Verify ACK event published with ErrorCode 0 (Success).
        repo.Bus.SwapBuffers();
        var ack = FindAck(repo, requestId);
        Assert.NotNull(ack);
        Assert.Equal(0, ack!.Value.ErrorCode);
        Assert.True(ack.Value.NewVersion > 0);
    }

    /// <summary>⭐ CE-4157 — a mission request with an unknown trigger type (or two) is REFUSED (ValidationFailed ack) and the entity's queue is
    /// left exactly as it was. ✅ Red-proof: the catch removed ⇒ the exception escapes ⇒ red; the fallback restored ⇒ a queue is applied ⇒ red.</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void CE4157_ReplaceMission_WithABadTrigger_IsRefused_AndTheQueueIsUnchanged(bool twoTriggers)
    {
        var entityMap = new NetworkEntityMap();
        using var repo = CreateWorld();
        var entity = repo.CreateEntity();
        entityMap.Register(1, entity);
        var system = new Hrot.Common.Systems.MissionControlExecutionSystem(entityMap, CreateBehaviorRegistry(), new TacticalIntentMapperRegistry());

        system.TestHook_ProcessIntent(repo, new MissionControlIntent
        {
            RequestId = Guid.NewGuid(), TargetEntityId = 1, BaseVersion = 0,
            Payload = new MissionCommandPayload { CommandType = eMissionCommandType.CMD_REPLACE_MISSION, FullMissionData = MakePlan(Guid.NewGuid()) },
        });
        long versionBefore = repo.GetComponentRO<MissionPlanQueue>(entity).Version;

        var bad = MakePlan(Guid.NewGuid(), Guid.NewGuid());
        bad.Tasks[1].Triggers = twoTriggers
            ? new List<Hrot.Core.Mission.MissionTrigger> { new() { Type = "BehaviorFinished" }, new() { Type = "UnderAttack" } }
            : new List<Hrot.Core.Mission.MissionTrigger> { new() { Type = "NoSuchTrigger" } };
        var requestId = Guid.NewGuid();
        system.TestHook_ProcessIntent(repo, new MissionControlIntent
        {
            RequestId = requestId, TargetEntityId = 1, BaseVersion = 0,
            Payload = new MissionCommandPayload { CommandType = eMissionCommandType.CMD_REPLACE_MISSION, FullMissionData = bad },
        });

        repo.Bus.SwapBuffers();
        var ack = FindAck(repo, requestId);
        Assert.NotNull(ack);
        Assert.NotEqual(0, ack!.Value.ErrorCode);
        var queue = repo.GetComponentRO<MissionPlanQueue>(entity);
        Assert.Equal((1, versionBefore), (queue.TaskCount, queue.Version));   // the first plan is still there
    }

    /// <summary>⭐ CE-4298 rest (mission plan §5.9 E9 / §5.10) — a plan whose WAIT task (no behaviour) has no <i>then</i> loads (the queue is applied) with exactly ONE warning naming the entity and the task; a Wait WITH a then, and an ordinary task with no condition, log none.
    /// 🔴 Red-proof (compiles): the warning not logged ⇒ the first case reads 0 warnings ⇒ red; the test dropped from <c>ResolveTrigger</c>'s result (always warning) ⇒ the other cases red.</summary>
    [Theory]
    [InlineData("", false, 1)]       // a Wait, no then: warns
    [InlineData("", true, 0)]        // a Wait WITH a then: none
    [InlineData("MoveToLocation", false, 0)]   // an ordinary task with no condition (= when done): none
    public void CE4298_AWaitWithNoThen_LoadsWithOneWarning_NamingTheEntityAndTheTask(string behavior, bool withThen, int warnings)
    {
        var entityMap = new NetworkEntityMap();
        using var repo = CreateWorld();
        repo.RegisterComponent<Fdp.Core.EntityInfo>();
        var entity = repo.CreateEntity();
        repo.AddComponent(entity, new Fdp.Core.EntityInfo { Name = new Fdp.Core.FixedString64("Alpha 1-1") });
        entityMap.Register(7, entity);
        var system = new Hrot.Common.Systems.MissionControlExecutionSystem(entityMap, CreateBehaviorRegistry(), new TacticalIntentMapperRegistry());
        var plan = MakePlan(Guid.NewGuid(), Guid.NewGuid());
        plan.Tasks[1].BehaviorId = behavior;
        if (withThen) plan.Tasks[1].Triggers = new List<Hrot.Core.Mission.MissionTrigger> { new() { Type = Hrot.Core.Mission.MissionTriggerHelper.ThenType, Params = Fdp.Toolkit.ReplayBrowser.Search.ConditionCache.ToJson(new Fdp.Toolkit.ReplayBrowser.Search.InputAbovePredicateDto { Input = "UnderFire", Threshold = 0f }) } };

        var target = new NLog.Targets.MemoryTarget("mission") { Layout = "${message}" };
        var original = NLog.LogManager.Configuration;
        var config = new NLog.Config.LoggingConfiguration();
        config.AddRule(NLog.LogLevel.Warn, NLog.LogLevel.Fatal, target);
        NLog.LogManager.Configuration = config; NLog.LogManager.ReconfigExistingLoggers();
        try
        {
            system.TestHook_ProcessIntent(repo, new MissionControlIntent
            {
                RequestId = Guid.NewGuid(), TargetEntityId = 7, BaseVersion = 0,
                Payload = new MissionCommandPayload { CommandType = eMissionCommandType.CMD_REPLACE_MISSION, FullMissionData = plan },
            });
            NLog.LogManager.Flush();
        }
        finally { NLog.LogManager.Configuration = original; NLog.LogManager.ReconfigExistingLoggers(); }

        Assert.Equal(2, repo.GetComponentRO<MissionPlanQueue>(entity).TaskCount);   // the load went on
        var waits = target.Logs.Where(l => l.Contains("is a Wait with no 'then'")).ToList();
        Assert.Equal(warnings, waits.Count);
        if (warnings == 1) { Assert.Contains("Alpha 1-1 (#7)", waits[0]); Assert.Contains("task 2", waits[0]); Assert.Contains(plan.Tasks[1].TaskId.ToString(), waits[0]); }
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
        map.Register(1, entity);
        Guid a = Guid.NewGuid(), b = Guid.NewGuid(), c = Guid.NewGuid();
        new Hrot.Common.Systems.MissionControlExecutionSystem(map, CreateBehaviorRegistry(), new TacticalIntentMapperRegistry())
            .TestHook_ProcessIntent(repo, new MissionControlIntent
            {
                RequestId = Guid.NewGuid(), TargetEntityId = 1, BaseVersion = 0,
                Payload = new MissionCommandPayload { CommandType = eMissionCommandType.CMD_REPLACE_MISSION, FullMissionData = MakePlan(a, b, c) },
            });

        var kf = Fdp.Toolkit.Replay.WorldKeyframes.Capture(repo, Fdp.Toolkit.Replay.CaptureOptions.Recording);
        using var fresh = CreateWorld();
        Fdp.Toolkit.Replay.WorldKeyframes.Apply(fresh, kf);
        var freshMap = new NetworkEntityMap();
        freshMap.Register(1, entity);
        var sys = new Hrot.Common.Systems.MissionControlExecutionSystem(freshMap, CreateBehaviorRegistry(), new TacticalIntentMapperRegistry());

        sys.TestHook_ProcessIntent(fresh, new MissionControlIntent
        {
            RequestId = Guid.NewGuid(), TargetEntityId = 1,
            Payload = new MissionCommandPayload { CommandType = eMissionCommandType.CMD_JUMP_TO_TASK, TargetTaskId = c },
        });
        Assert.Equal(2, fresh.GetComponent<MissionPlanQueue>(entity).CurrentTask);

        var replace = Guid.NewGuid();
        sys.TestHook_ProcessIntent(fresh, new MissionControlIntent
        {
            RequestId = replace, TargetEntityId = 1, BaseVersion = 2,
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
        entityMap.Register(1, entity);

        var system = new Hrot.Common.Systems.MissionControlExecutionSystem(entityMap, CreateBehaviorRegistry(), new TacticalIntentMapperRegistry());

        // First mission succeeds (BaseVersion 0).
        var taskA = Guid.NewGuid();
        system.TestHook_ProcessIntent(repo, new MissionControlIntent
        {
            RequestId      = Guid.NewGuid(),
            TargetEntityId = 1,
            BaseVersion    = 0,
            Payload = new MissionCommandPayload
            {
                CommandType     = eMissionCommandType.CMD_REPLACE_MISSION,
                FullMissionData = MakePlan(taskA)
            }
        });

        // Snapshot phase count after first mission.
        int phaseCountAfterFirst = repo.GetComponent<MissionPlanQueue>(entity).TaskCount;
        Assert.Equal(1, phaseCountAfterFirst);

        // Second mission with wrong BaseVersion (should be 1, but using 99).
        var taskB = Guid.NewGuid();
        var conflictId = Guid.NewGuid();
        system.TestHook_ProcessIntent(repo, new MissionControlIntent
        {
            RequestId      = conflictId,
            TargetEntityId = 1,
            BaseVersion    = 99,
            Payload = new MissionCommandPayload
            {
                CommandType     = eMissionCommandType.CMD_REPLACE_MISSION,
                FullMissionData = MakePlan(taskA, taskB)
            }
        });

        // Queue must NOT be mutated.
        Assert.Equal(phaseCountAfterFirst, repo.GetComponent<MissionPlanQueue>(entity).TaskCount);

        // NACK with non-zero ErrorCode.
        repo.Bus.SwapBuffers();
        var ack = FindAck(repo, conflictId);
        Assert.NotNull(ack);
        Assert.NotEqual(0, ack!.Value.ErrorCode);

        // ⭐ Edit framework F6 (CE-4277, Q3) — the same answer in the ONE outcome stream: a failed version precondition is a CONFLICT with its words
        //   (the notification offers Reload / Overwrite), not a plain refusal. ✅ Red-proof: VersionConflict mapped to Refused ⇒ red.
        Assert.True(Fdp.Toolkit.Replication.Edits.EditOutcomeLog.Of(repo).TryGet(conflictId, out var outcome));
        Assert.Equal(Fdp.Toolkit.Replication.Edits.EditStatus.Conflict, outcome.Status);
        Assert.Contains("changed by someone else", outcome.Reason);
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
            TargetEntityId = 999,
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
            TargetEntityId = 42,
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
        entityMap.Register(42, entity);

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
        entityMap.Register(1, entity);

        var system = new Hrot.Common.Systems.MissionControlExecutionSystem(entityMap, CreateBehaviorRegistry(), new TacticalIntentMapperRegistry());

        var taskA = Guid.NewGuid();
        var taskB = Guid.NewGuid();

        // â”€â”€ Replace mission â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
        system.TestHook_ProcessIntent(repo, new MissionControlIntent
        {
            RequestId      = Guid.NewGuid(),
            TargetEntityId = 1,
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
            TargetEntityId = 1,
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
        entityMap.Register(1, entity);

        var system = new Hrot.Common.Systems.MissionControlExecutionSystem(entityMap, CreateBehaviorRegistry(), new TacticalIntentMapperRegistry());

        var taskA = Guid.NewGuid();
        var taskB = Guid.NewGuid();
        var taskC = Guid.NewGuid();

        system.TestHook_ProcessIntent(repo, new MissionControlIntent
        {
            RequestId      = Guid.NewGuid(),
            TargetEntityId = 1,
            BaseVersion    = 0,
            Payload = new MissionCommandPayload
            {
                CommandType     = eMissionCommandType.CMD_REPLACE_MISSION,
                FullMissionData = MakePlan(taskA, taskB, taskC)
            }
        });

        // SC1: PhaseCount == 3.
        var queue = repo.GetComponent<MissionPlanQueue>(entity);
        Assert.Equal(3, queue.TaskCount);

        // SC2: Each phase has the MoveToLocation behavior ID (101).
        Span<MissionTaskSlot> phases = queue.Tasks;
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
        entityMap.Register(1, entity);

        var system = new Hrot.Common.Systems.MissionControlExecutionSystem(entityMap, CreateBehaviorRegistry(), new TacticalIntentMapperRegistry());

        system.TestHook_ProcessIntent(repo, new MissionControlIntent
        {
            RequestId      = Guid.NewGuid(),
            TargetEntityId = 1,
            BaseVersion    = 0,
            Payload = new MissionCommandPayload
            {
                CommandType     = eMissionCommandType.CMD_REPLACE_MISSION,
                FullMissionData = new MissionPlan { Tasks = new List<MissionTask>() }
            }
        });

        var queue = repo.GetComponent<MissionPlanQueue>(entity);
        Assert.Equal(0, queue.TaskCount);
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
        entityMap.Register(1, entity);
        var system = new Hrot.Common.Systems.MissionControlExecutionSystem(entityMap, CreateBehaviorRegistry(), new TacticalIntentMapperRegistry());
        system.TestHook_ProcessIntent(repo, new MissionControlIntent
        {
            RequestId = Guid.NewGuid(), TargetEntityId = 1, BaseVersion = 0,
            Payload = new MissionCommandPayload { CommandType = eMissionCommandType.CMD_REPLACE_MISSION, FullMissionData = MakePlan(Guid.NewGuid(), Guid.NewGuid()) },
        });
        Assert.Equal(2, repo.GetComponentRO<MissionPlanQueue>(entity).TaskCount);
        repo.Bus.SwapBuffers();

        IIntentClearProvider provider = system;
        provider.ClearFor(entity, repo);

        Assert.Equal(0, repo.GetComponentRO<MissionPlanQueue>(entity).TaskCount);
        Assert.False(repo.HasManagedComponent<ActiveMissionPlan>(entity));
        repo.Bus.SwapBuffers();
        Assert.Empty(repo.Bus.Read<Fdp.Toolkit.Behavior.Events.ClearBehaviorEvent>().ToArray());

        provider.ClearFor(entity, repo);   // idempotent: no plan, nothing to do
        Assert.Equal(0, repo.GetComponentRO<MissionPlanQueue>(entity).TaskCount);
    }

    // ── ⭐ Edit framework F6 (CE-4277): only the BRAIN's owner applies a commit; a commit for a unit owned elsewhere goes there ─────────────────

    private sealed class CapturingRequests : Hrot.Map.Common.Dds.IDdsWriter<Hrot.NED.Messages.MissionControlRequest>
    {
        public readonly List<Hrot.NED.Messages.MissionControlRequest> Written = new();
        public void Write(Hrot.NED.Messages.MissionControlRequest sample) => Written.Add(sample);
        public void DisposeInstance(Hrot.NED.Messages.MissionControlRequest key) { }
    }

    /// <summary>A commit on a node that does not own the unit's brain is NOT applied to the replica and answers nothing; the egress forwards it with the
    /// user's request id; a unit owned here is not forwarded. ✅ Red-proof: the executor's OwnsBrain gate removed ⇒ the replica's queue is written ⇒ red.</summary>
    [Fact]
    public void F6_ACommitForAUnitOwnedElsewhere_IsForwarded_NotAppliedToTheReplica()
    {
        var entityMap = new NetworkEntityMap();
        using var repo = CreateWorld();
        repo.RegisterComponent<Fdp.Toolkit.Replication.Components.NetworkAuthority>();
        repo.Bus.RegisterManaged<MissionControlIntent>();
        var replica = repo.CreateEntity();
        repo.AddComponent(replica, new Fdp.Toolkit.Replication.Components.NetworkAuthority(primaryOwnerId: 2, localNodeId: 1));
        entityMap.Register(1, replica);
        var owned = repo.CreateEntity();
        repo.AddComponent(owned, new Fdp.Toolkit.Replication.Components.NetworkAuthority(primaryOwnerId: 1, localNodeId: 1));
        entityMap.Register(2, owned);
        var system = new Hrot.Common.Systems.MissionControlExecutionSystem(entityMap, CreateBehaviorRegistry(), new TacticalIntentMapperRegistry());
        MissionControlIntent Commit(int target) => new()
        {
            RequestId = Guid.NewGuid(), TargetEntityId = target, BaseVersion = 0,
            Payload = new MissionCommandPayload { CommandType = eMissionCommandType.CMD_REPLACE_MISSION, FullMissionData = MakePlan(Guid.NewGuid()) },
        };

        var toReplica = Commit(1);
        system.TestHook_ProcessIntent(repo, toReplica);
        Assert.False(repo.HasComponent<MissionPlanQueue>(replica) && repo.GetComponent<MissionPlanQueue>(replica).TaskCount > 0);
        repo.Bus.SwapBuffers();
        Assert.Null(FindAck(repo, toReplica.RequestId));                                      // the owner answers, not this node

        var toOwned = Commit(2);
        repo.Bus.PublishManaged(toReplica);
        repo.Bus.PublishManaged(toOwned);
        repo.Bus.PublishManaged(new MissionControlIntent { RequestId = Guid.NewGuid(), TargetEntityId = 1, IsRemote = true, Payload = toReplica.Payload });
        repo.Bus.SwapBuffers();
        var writer = new CapturingRequests();
        new Hrot.Network.NED.SimHost.MissionControlEgressTranslator(writer, entityMap).ScanAndPublish(repo);
        var sent = Assert.Single(writer.Written);                                              // the owned one stays, the remote one is never re-sent
        Assert.Equal((toReplica.RequestId, 1), (sent.RequestId, sent.TargetEntityId));
    }

    // ── ⭐⭐ CE-4300 — mission plan E1 / E7 / D9 (📄 docs/UX/DESIGN_Mission_Plan_Editor.md §5.8): a Live edit carries progress BY TASK ID; the version
    //    precondition makes a second station's stale commit a Conflict. ─────────────────────────────────────────────────────────────────────

    private static (Hrot.Common.Systems.MissionControlExecutionSystem Sys, Entity E) Unit(EntityRepository repo)
    {
        var map = new NetworkEntityMap();
        var e = repo.CreateEntity();
        map.Register(1, e);
        return (new Hrot.Common.Systems.MissionControlExecutionSystem(map, CreateBehaviorRegistry(), new TacticalIntentMapperRegistry()), e);
    }

    private static long Commit(Hrot.Common.Systems.MissionControlExecutionSystem sys, EntityRepository repo, MissionPlan plan, long baseVersion, out int error)
    {
        var id = Guid.NewGuid();
        sys.TestHook_ProcessIntent(repo, new MissionControlIntent
        {
            RequestId = id, TargetEntityId = 1, BaseVersion = baseVersion,
            Payload = new MissionCommandPayload { CommandType = eMissionCommandType.CMD_REPLACE_MISSION, FullMissionData = plan },
        });
        repo.Bus.SwapBuffers();
        var ack = FindAck(repo, id)!.Value;
        error = ack.ErrorCode;
        return ack.NewVersion;
    }

    /// <summary>Task 1 done, task 2 running for 7 s.</summary>
    private static void RunningTask2(EntityRepository repo, Entity e)
    {
        ref var q = ref repo.GetComponentRW<MissionPlanQueue>(e);
        Span<MissionPhaseOutcome> o = q.Outcomes;
        o[0] = MissionPhaseOutcome.Done;
        q.CurrentTask = 1;
        q.TaskElapsedSeconds = 7f;
    }

    [Fact]
    public void CE4300_E1_AnEditOfTask4_WhileTask2Runs_LeavesTask2Running_ElapsedKept()
    {
        using var repo = CreateWorld();
        var (sys, e) = Unit(repo);
        var ids = new[] { Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid() };
        long v = Commit(sys, repo, MakePlan(ids), 0, out _);
        RunningTask2(repo, e);

        var edited = MakePlan(ids);
        edited.Tasks[3].BehaviorParams = "{\"Speed\":3}";
        Commit(sys, repo, edited, v, out int err);

        Assert.Equal(0, err);
        var q = repo.GetComponentRO<MissionPlanQueue>(e);
        Assert.Equal(1, q.CurrentTask);                                   // ⛔ was 0: the plan restarted from task 1
        Assert.Equal(7f, (float)q.TaskElapsedSeconds, 3);
        Assert.Equal(MissionPhaseOutcome.Done, q.Outcomes[0]);
        Assert.Equal(MissionPhaseOutcome.None, q.Outcomes[1]);
    }

    [Fact]
    public void CE4300_E1_AnInsertBeforeTheRunningTask_FollowsIt_AChangedRunningTaskRestarts_ARemovedOneGoesOn()
    {
        using var repo = CreateWorld();
        var (sys, e) = Unit(repo);
        var ids = new[] { Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid() };
        long v = Commit(sys, repo, MakePlan(ids), 0, out _);
        RunningTask2(repo, e);

        var inserted = MakePlan(ids[0], Guid.NewGuid(), ids[1], ids[2]);   // a new task before the running one
        v = Commit(sys, repo, inserted, v, out _);
        var q = repo.GetComponentRO<MissionPlanQueue>(e);
        Assert.Equal(2, q.CurrentTask);                                   // the SAME task, now third
        Assert.Equal(7f, (float)q.TaskElapsedSeconds, 3);

        var changed = MakePlan(ids[0], inserted.Tasks[1].TaskId, ids[1], ids[2]);
        changed.Tasks[2].BehaviorParams = "{\"Speed\":9}";             // the running task itself edited
        v = Commit(sys, repo, changed, v, out _);
        q = repo.GetComponentRO<MissionPlanQueue>(e);
        Assert.Equal(1, q.CurrentTask);                                   // restarts: the plan goes on after the last task that ended (task 1)
        Assert.Equal(0f, (float)q.TaskElapsedSeconds, 3);

        RunningTask2(repo, e);                                            // now the new task (index 1) runs
        Commit(sys, repo, MakePlan(ids[0], ids[1], ids[2]), v, out _);   // ...and is removed
        q = repo.GetComponentRO<MissionPlanQueue>(e);
        Assert.Equal(1, q.CurrentTask);                                   // the task after the last one done
        Assert.Equal(MissionPhaseOutcome.Done, q.Outcomes[0]);
    }

    [Fact]
    public void CE4300_E7_AppendingToAFinishedPlan_MakesTheNewTaskCurrent()
    {
        using var repo = CreateWorld();
        var (sys, e) = Unit(repo);
        var ids = new[] { Guid.NewGuid(), Guid.NewGuid() };
        long v = Commit(sys, repo, MakePlan(ids), 0, out _);
        ref var q = ref repo.GetComponentRW<MissionPlanQueue>(e);
        Span<MissionPhaseOutcome> o = q.Outcomes;
        o[0] = MissionPhaseOutcome.Done; o[1] = MissionPhaseOutcome.Failed;
        q.CurrentTask = 2;                                                // ran out

        Commit(sys, repo, MakePlan(ids[0], ids[1], Guid.NewGuid()), v, out _);
        var after = repo.GetComponentRO<MissionPlanQueue>(e);
        Assert.Equal(2, after.CurrentTask);
        Assert.Equal(MissionPhaseOutcome.Failed, after.Outcomes[1]);
    }

    [Fact]
    public void CE4300_D9_TwoStations_OnTheSameVersion_TheSecondCommitIsAConflict_AndChangesNothing()
    {
        using var repo = CreateWorld();
        var (sys, e) = Unit(repo);
        long v = Commit(sys, repo, MakePlan(Guid.NewGuid()), 0, out _);

        Commit(sys, repo, MakePlan(Guid.NewGuid(), Guid.NewGuid()), v, out int first);       // station A, opened at v
        Commit(sys, repo, MakePlan(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid()), v, out int second);   // station B, opened at v too

        Assert.Equal(0, first);
        Assert.Equal((int)Hrot.NED.Messages.NedStatusCode.VersionConflict, second);
        Assert.Equal(2, repo.GetComponentRO<MissionPlanQueue>(e).TaskCount);               // A's plan stands
    }
}
