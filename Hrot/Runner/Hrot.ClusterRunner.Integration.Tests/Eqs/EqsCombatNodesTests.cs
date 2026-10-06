using System.Numerics;
using System.Runtime.InteropServices;
using Fbt;
using Fdp.Core;
using Fdp.Toolkit.Behavior;
using Fdp.Toolkit.Behavior.Components;
using Fdp.Toolkit.Combat;
using Fdp.Toolkit.Navigation;
using Fdp.Toolkit.Perception.Components;
using Fdp.Toolkit.Spatial.Eqs;
using Hrot.AI.Behaviors.Brains;
using Hrot.SimHost;
using Xunit;

namespace Hrot.ClusterRunner.Integration.Tests.Eqs;

/// <summary>
/// Integration tests for <see cref="EqsCombatNodes"/> and the
/// <c>HideInCover_BT</c> behavior node sequence.
///
/// <para>Tests call node methods directly using a real <see cref="EntityRepository"/>
/// with a manually constructed <see cref="BTreeContext"/>
/// (same pattern as <c>EqsLifecycleNodesTests</c>).</para>
/// </summary>
[Collection("EqsIntegrationTests")]
public sealed class EqsCombatNodesTests : IDisposable
{
    private readonly EntityRepository _repo;
    private readonly Entity _entity;

    public EqsCombatNodesTests()
    {
        _repo   = new EntityRepository();
        SimHostComponentRegistry.RegisterAll(_repo);
        // ⭐ CE-493: these tests drive BRAIN nodes; SimHost stopped registering the brain set on 2026-09-12 (0cda0caf9).
        CognitiveComponentRegistry.RegisterAll(_repo);
        _entity = _repo.CreateEntity();
    }

    public void Dispose()
    {
        if (_repo.HasSingleton<EqsResultPool>())
        {
            var rp = _repo.GetSingleton<EqsResultPool>();
            if (rp.Results.IsCreated) rp.Results.Dispose();
        }
        _repo.Dispose();
    }

    // ── T-COV1: MoveTo activated when buffer is ready ─────────────────────────

    /// <summary>
    /// T-COV1 (EQS-030 SC1): <c>Action_MoveToOptimalCover</c> writes the locomotion
    /// channel with the correct destination when the buffer is ready.
    /// </summary>
    [Fact]
    public void EqsCombatNodes_MoveToOptimalCover_WritesChannelWithCorrectDestination()
    {
        var p     = new MoveToOptimalCoverParams { Speed = 3f, ArrivalRadius = 0.5f };
        var ctx   = new BTreeContext { Self = _entity, World = _repo };

        // Add EqsCognitiveBuffer with one ready candidate
        var buf = new EqsCognitiveBuffer { Count = 1, LastUpdateTick = 1 };
        buf.GetSpanRW()[0] = new EqsResult { PositionX = 10f, PositionY = 20f, Score = 1f };
        _repo.AddComponent(_entity, buf);

        // Add LocomotionChannel (default zero state)
        _repo.AddComponent(_entity, new LocomotionChannel());

        var result = EqsCombatNodes.Action_MoveToOptimalCover(ref p, ctx.Self, ctx.World);

        Assert.Equal(NodeStatus.Running, result);

        ref readonly var channel = ref _repo.GetComponentRO<LocomotionChannel>(_entity);
        Assert.Equal(NavigationConstants.ActionIdMoveTo, channel.ActiveAction);
        unsafe
        {
            MoveToParams mp;
            fixed (byte* src = channel.Params) mp = *(MoveToParams*)src;
            Assert.Equal(new Vector3(10f, 20f, 0f), mp.Destination);
        }
    }

    // ── T-COV2: Returns Failure when buffer not ready ─────────────────────────

    /// <summary>
    /// T-COV2 (EQS-030 SC2): <c>Action_MoveToOptimalCover</c> returns Failure when
    /// the <see cref="EqsCognitiveBuffer"/> is not ready (Count=0, LastUpdateTick=0).
    /// </summary>
    [Fact]
    public void EqsCombatNodes_MoveToOptimalCover_ReturnsFailureWhenBufferNotReady()
    {
        var p     = new MoveToOptimalCoverParams { Speed = 3f, ArrivalRadius = 0.5f };
        var ctx   = new BTreeContext { Self = _entity, World = _repo };

        // Add a buffer that is not ready (Count=0, LastUpdateTick=0)
        _repo.AddComponent(_entity, new EqsCognitiveBuffer { Count = 0, LastUpdateTick = 0 });
        _repo.AddComponent(_entity, new LocomotionChannel());

        var result = EqsCombatNodes.Action_MoveToOptimalCover(ref p, ctx.Self, ctx.World);

        Assert.Equal(NodeStatus.Failure, result);
    }

    // ── T-COV3: Forwards Success from channel ─────────────────────────────────

    /// <summary>
    /// T-COV3 (EQS-030 SC3): <c>Action_MoveToOptimalCover</c> forwards Success from
    /// the channel when the executor reports the action completed successfully.
    /// </summary>
    [Fact]
    public void EqsCombatNodes_MoveToOptimalCover_ForwardsSuccessFromChannel()
    {
        var p     = new MoveToOptimalCoverParams { Speed = 3f, ArrivalRadius = 0.5f };
        var ctx   = new BTreeContext { Self = _entity, World = _repo };

        // Ready buffer with one candidate
        var buf = new EqsCognitiveBuffer { Count = 1, LastUpdateTick = 1 };
        buf.GetSpanRW()[0] = new EqsResult { PositionX = 5f, PositionY = 5f, Score = 1f };
        _repo.AddComponent(_entity, buf);

        // Channel already reporting Success for the MoveTo action
        _repo.AddComponent(_entity, new LocomotionChannel
        {
            ActiveAction = NavigationConstants.ActionIdMoveTo,
            Status       = NodeStatus.Success,
        });

        var result = EqsCombatNodes.Action_MoveToOptimalCover(ref p, ctx.Self, ctx.World);

        Assert.Equal(NodeStatus.Success, result);
    }

    // ── T-COV4: Condition_HasTarget ───────────────────────────────────────────

    /// <summary>
    /// T-COV4 (EQS-031 SC-related): <c>Condition_HasTarget</c> returns Failure when
    /// no threat exists and Success when a threat is present.
    /// </summary>
    [Fact]
    public void EqsCombatNodes_ConditionHasTarget_SucceedsWithThreatFailsWithout()
    {
        var p     = new MoveToOptimalCoverParams();
        var ctx   = new BTreeContext { Self = _entity, World = _repo };

        // Step 1: no TargetMemory component
        Assert.Equal(NodeStatus.Failure,
            EqsCombatNodes.Condition_HasTarget(ref p, ctx.Self, ctx.World));

        // Step 2: component present but Count=0 (no entries)
        _repo.AddComponent(_entity, new TargetMemory());
        Assert.Equal(NodeStatus.Failure,
            EqsCombatNodes.Condition_HasTarget(ref p, ctx.Self, ctx.World));

        // Step 3: add a live threat entry
        ref var mem = ref _repo.GetComponentRW<TargetMemory>(_entity);
        unsafe { mem.Freshness[0] = 1.5f; }
        mem.Count = 1;
        Assert.Equal(NodeStatus.Success,
            EqsCombatNodes.Condition_HasTarget(ref p, ctx.Self, ctx.World));
    }

    // ── T-COV5: HideInCover node sequence smoke test ──────────────────────────

    /// <summary>
    /// T-COV5 (EQS-031 SC2+SC3): Simulates the two key branches of
    /// <c>HideInCover_BT</c> without running the full BTree runtime.
    ///
    /// Phase A: threat present + buffer ready -> channel set to MoveTo.
    /// Phase B: threat removed + deactivator fires -> sensor and buffer cleaned up.
    /// </summary>
    [Fact]
    public void HideInCoverBehavior_NodeSequence_SetsChannelThenCleansUpOnThreatRemoval()
    {
        var eqsParams  = new EqsParams { BlueprintId = 1, SearchRadius = 50f };
        var moveParams = new MoveToOptimalCoverParams { Speed = 5f, ArrivalRadius = 1f };
        var ctx        = new BTreeContext { Self = _entity, World = _repo };

        // ── Phase A: threat present, buffer ready ──────────────────────────────

        // Step 1: add TargetMemory with a live threat
        var mem = new TargetMemory();
        unsafe { mem.Freshness[0] = 2f; mem.EntityIds[0] = 99L; }
        mem.Count = 1;
        _repo.AddComponent(_entity, mem);

        // Step 2: simulate Action_MaintainEqsSensor (adds EqsSensor on first tick)
        var maintainResult = EqsLifecycleNodes.Action_MaintainEqsSensor(ref eqsParams, ctx.Self, ctx.World);
        Assert.Equal(NodeStatus.Running, maintainResult);
        Assert.True(_repo.HasComponent<EqsSensor>(_entity));

        // Step 3: pre-populate EqsCognitiveBuffer as the solver would
        var buf = new EqsCognitiveBuffer { Count = 1, LastUpdateTick = 1 };
        buf.GetSpanRW()[0] = new EqsResult { PositionX = 30f, PositionY = 40f, Score = 1f };
        _repo.AddComponent(_entity, buf);

        // Step 4: add LocomotionChannel
        _repo.AddComponent(_entity, new LocomotionChannel());

        // Step 5: call Action_MoveToOptimalCover
        var moveResult = EqsCombatNodes.Action_MoveToOptimalCover(ref moveParams, ctx.Self, ctx.World);
        Assert.Equal(NodeStatus.Running, moveResult);
        Assert.Equal(NavigationConstants.ActionIdMoveTo,
            _repo.GetComponentRO<LocomotionChannel>(_entity).ActiveAction);

        // ── Phase B: threat removed -> deactivator clears sensor ──────────────

        // Remove threat from TargetMemory
        ref var memW = ref _repo.GetComponentRW<TargetMemory>(_entity);
        memW.Count = 0;
        unsafe { memW.Freshness[0] = 0f; }

        // The ObserverSelector would abort the branch and call the deactivator
        EqsLifecycleNodes.Deactivate_MaintainEqsSensor(ctx.Self, ctx.World);

        Assert.False(_repo.HasComponent<EqsSensor>(_entity),
            "EqsSensor must be removed when the branch is aborted");
        Assert.False(_repo.HasComponent<EqsCognitiveBuffer>(_entity),
            "EqsCognitiveBuffer must be removed when the branch is aborted");
    }

    // ── CE-2092 / CE-2093: EqsTacticsNodes (take cover, fall back) ─────────────
    //   📄 docs/DESIGN_Eqs_Consuming_Behaviours.md §2 (R-204). Called directly, as the rails above.

    private Entity Remember(params Entity[] threats)
    {
        if (!_repo.HasComponent<TargetMemory>(_entity)) _repo.AddComponent(_entity, new TargetMemory());
        ref var mem = ref _repo.GetComponentRW<TargetMemory>(_entity);
        unsafe
        {
            for (int i = 0; i < threats.Length; i++) { mem.EntityIds[i] = (long)threats[i].PackedValue; mem.Freshness[i] = 10f; }
        }
        mem.Count = threats.Length;
        return threats.Length > 0 ? threats[0] : Entity.Null;
    }

    private void Answer(Entity sensor, uint tick, float x, float y)
    {
        var buf = new EqsCognitiveBuffer { Count = 1, LastUpdateTick = tick };
        buf.GetSpanRW()[0] = new EqsResult { PositionX = x, PositionY = y, Score = 1f };
        _repo.SetComponent(sensor, buf);
    }

    private unsafe Vector3 Destination()
    {
        ref readonly var ch = ref _repo.GetComponentRO<LocomotionChannel>(_entity);
        fixed (byte* src = ch.Params) return ((MoveToParams*)src)->Destination;
    }

    private static EqsTacticsParams Tunables() => new()
    {
        SearchRadius = 60f, MinRepositionMetres = 5f, Speed = 2.5f, ArrivalRadius = 1f, ScoreDeltaThreshold = 0.05f,
    };

    [Fact]
    public void CE2092_TakeCover_WithNothingRemembered_Succeeds_AndMakesNoSensor()
    {
        _repo.AddComponent(_entity, new LocomotionChannel());
        var p = Tunables(); var ws = default(EqsTacticsState);
        Assert.Equal(NodeStatus.Success, EqsTacticsNodes.TakeCover(ref p, ref ws, _entity, _repo));
        Assert.False(ws.Sensor.IsValid);
    }

    [Fact]
    public void CE2092_TakeCover_PointsItsOwnSensorAtTheThreat_AndMovesOnTheFirstAnswer()
    {
        _repo.AddComponent(_entity, new LocomotionChannel());
        var threat = Remember(_repo.CreateEntity());
        var p = Tunables(); var ws = default(EqsTacticsState);

        Assert.Equal(NodeStatus.Running, EqsTacticsNodes.TakeCover(ref p, ref ws, _entity, _repo));
        Assert.True(ws.Sensor.IsValid);
        var sensor = _repo.GetComponentRO<EqsSensor>(ws.Sensor.ChildId);
        Assert.Equal(FindCoverFromTarget.BlueprintId, sensor.BlueprintId);
        Assert.Equal(threat, sensor.ContextSlot1);
        Assert.Equal((byte)EqsPublishPolicy.ScoreDelta, sensor.PublishPolicy);
        Assert.Equal(0, _repo.GetComponentRO<LocomotionChannel>(_entity).ActiveAction);   // no answer yet ⇒ no move

        Answer(ws.Sensor.ChildId, 5, 10f, 20f);
        Assert.Equal(NodeStatus.Running, EqsTacticsNodes.TakeCover(ref p, ref ws, _entity, _repo));
        Assert.Equal(NavigationConstants.ActionIdMoveTo, _repo.GetComponentRO<LocomotionChannel>(_entity).ActiveAction);
        Assert.Equal(new Vector3(10f, 20f, 0f), Destination());
    }

    [Fact]
    public void CE2092_TakeCover_ANewAnswer_MovesAgainOnlyPastTheRepositionDistance()
    {
        _repo.AddComponent(_entity, new LocomotionChannel());
        Remember(_repo.CreateEntity());
        var p = Tunables(); var ws = default(EqsTacticsState);
        EqsTacticsNodes.TakeCover(ref p, ref ws, _entity, _repo);
        Answer(ws.Sensor.ChildId, 5, 10f, 20f);
        EqsTacticsNodes.TakeCover(ref p, ref ws, _entity, _repo);
        uint first = _repo.GetComponentRO<LocomotionChannel>(_entity).ActionInstanceId;

        Answer(ws.Sensor.ChildId, 6, 12f, 20f);   // 2 m away
        EqsTacticsNodes.TakeCover(ref p, ref ws, _entity, _repo);
        Assert.Equal(first, _repo.GetComponentRO<LocomotionChannel>(_entity).ActionInstanceId);

        Answer(ws.Sensor.ChildId, 7, 20f, 20f);   // 10 m away
        EqsTacticsNodes.TakeCover(ref p, ref ws, _entity, _repo);
        Assert.NotEqual(first, _repo.GetComponentRO<LocomotionChannel>(_entity).ActionInstanceId);
        Assert.Equal(new Vector3(20f, 20f, 0f), Destination());

        uint second = _repo.GetComponentRO<LocomotionChannel>(_entity).ActionInstanceId;
        EqsTacticsNodes.TakeCover(ref p, ref ws, _entity, _repo);   // the same answer again: looked at once
        Assert.Equal(second, _repo.GetComponentRO<LocomotionChannel>(_entity).ActionInstanceId);
    }

    [Fact]
    public void CE2092_TakeCover_ADifferentThreat_RePointsTheSameSensor_WithANewEpoch()
    {
        _repo.AddComponent(_entity, new LocomotionChannel());
        Remember(_repo.CreateEntity());
        var p = Tunables(); var ws = default(EqsTacticsState);
        EqsTacticsNodes.TakeCover(ref p, ref ws, _entity, _repo);
        Answer(ws.Sensor.ChildId, 5, 10f, 20f);
        var child = ws.Sensor.ChildId;
        uint epoch = _repo.GetComponentRO<EqsSensor>(child).Epoch;

        var second = Remember(_repo.CreateEntity());   // the first threat is forgotten, another remembered
        EqsTacticsNodes.TakeCover(ref p, ref ws, _entity, _repo);

        Assert.Equal(child, ws.Sensor.ChildId);
        var sensor = _repo.GetComponentRO<EqsSensor>(child);
        Assert.Equal(second, sensor.ContextSlot1);
        Assert.Equal(EqsChildSensor.NextEpoch(epoch), sensor.Epoch);
        Assert.False(_repo.GetComponentRO<EqsCognitiveBuffer>(child).IsReady);   // the old answer is gone
    }

    [Fact]
    public void CE2092_LeavingTheNode_DestroysItsSensor_AndStopsItsMove()
    {
        _repo.AddComponent(_entity, new LocomotionChannel());
        Remember(_repo.CreateEntity());
        var p = Tunables(); var ws = default(EqsTacticsState);
        EqsTacticsNodes.TakeCover(ref p, ref ws, _entity, _repo);
        Answer(ws.Sensor.ChildId, 5, 10f, 20f);
        EqsTacticsNodes.TakeCover(ref p, ref ws, _entity, _repo);
        var child = ws.Sensor.ChildId;

        EqsTacticsNodes.Deactivate_TakeCover(ref p, ref ws, _entity, _repo);
        _repo.FlushCommandBuffers();   // the sensor is destroyed through the command buffer

        Assert.False(_repo.IsAlive(child));
        Assert.Equal(0, _repo.GetComponentRO<LocomotionChannel>(_entity).ActiveAction);
        Assert.False(ws.Sensor.IsValid);
    }

    [Fact]
    public void CE2092_TakeCover_AFailedMove_IsRetriedOnTheNextAnswer_NotEveryTick()
    {
        _repo.AddComponent(_entity, new LocomotionChannel());
        Remember(_repo.CreateEntity());
        var p = Tunables(); var ws = default(EqsTacticsState);
        EqsTacticsNodes.TakeCover(ref p, ref ws, _entity, _repo);
        Answer(ws.Sensor.ChildId, 5, 10f, 20f);
        EqsTacticsNodes.TakeCover(ref p, ref ws, _entity, _repo);
        uint first = _repo.GetComponentRO<LocomotionChannel>(_entity).ActionInstanceId;

        _repo.GetComponentRW<LocomotionChannel>(_entity).Status = NodeStatus.Failure;   // e.g. unreachable
        EqsTacticsNodes.TakeCover(ref p, ref ws, _entity, _repo);
        EqsTacticsNodes.TakeCover(ref p, ref ws, _entity, _repo);
        Assert.Equal(first, _repo.GetComponentRO<LocomotionChannel>(_entity).ActionInstanceId);   // the same answer: no churn

        Answer(ws.Sensor.ChildId, 6, 11f, 20f);   // the next answer, even close by, moves again
        EqsTacticsNodes.TakeCover(ref p, ref ws, _entity, _repo);
        Assert.NotEqual(first, _repo.GetComponentRO<LocomotionChannel>(_entity).ActionInstanceId);
    }

    [Fact]
    public void CE2093_FallBack_MovesOnce_AndSucceedsOnArrival()
    {
        _repo.AddComponent(_entity, new LocomotionChannel());
        Remember(_repo.CreateEntity());
        var p = Tunables(); var ws = default(EqsTacticsState);
        EqsTacticsNodes.FallBack(ref p, ref ws, _entity, _repo);
        Assert.Equal(FindSafeRetreatPoint.BlueprintId, _repo.GetComponentRO<EqsSensor>(ws.Sensor.ChildId).BlueprintId);

        Answer(ws.Sensor.ChildId, 5, 40f, 50f);
        Assert.Equal(NodeStatus.Running, EqsTacticsNodes.FallBack(ref p, ref ws, _entity, _repo));
        uint move = _repo.GetComponentRO<LocomotionChannel>(_entity).ActionInstanceId;

        Answer(ws.Sensor.ChildId, 6, 90f, 90f);   // a later, different answer does not turn the unit around
        Assert.Equal(NodeStatus.Running, EqsTacticsNodes.FallBack(ref p, ref ws, _entity, _repo));
        Assert.Equal(move, _repo.GetComponentRO<LocomotionChannel>(_entity).ActionInstanceId);
        Assert.Equal(new Vector3(40f, 50f, 0f), Destination());

        _repo.GetComponentRW<LocomotionChannel>(_entity).Status = NodeStatus.Success;   // the executor reports arrival
        Assert.Equal(NodeStatus.Success, EqsTacticsNodes.FallBack(ref p, ref ws, _entity, _repo));
    }

    // ── CE-2108: Flank / FiringPosition — the same ONE body (§9.6 F4), ending on arrival, re-positioning on the way ──────
    //   📄 docs/DESIGN_Eqs_Consuming_Behaviours.md §9.

    /// <summary>🔴 F2 — a flank needs a threat it can SEE: nothing remembered, or only a HEARD contact ⇒ Failure (the parent picks
    /// something else) and no sensor; TakeCover in the same state hides from the heard point instead.</summary>
    [Fact]
    public void CE2108_Flank_WithNoIdentifiedThreat_Fails_AndMakesNoSensor()
    {
        _repo.AddComponent(_entity, new LocomotionChannel());
        var p = Tunables(); var ws = default(EqsTacticsState);
        Assert.Equal(NodeStatus.Failure, EqsTacticsNodes.Flank(ref p, ref ws, _entity, _repo));

        _repo.AddComponent(_entity, new TargetMemory());
        TargetMemory.HearContact(ref _repo.GetComponentRW<TargetMemory>(_entity), 0f, 50f, 0f, 8f, sourceClass: 1, scoreBoost: 5f, tick: 1);
        Assert.Equal(NodeStatus.Failure, EqsTacticsNodes.Flank(ref p, ref ws, _entity, _repo));
        Assert.Equal(NodeStatus.Failure, EqsTacticsNodes.FiringPosition(ref p, ref ws, _entity, _repo));
        Assert.False(ws.Sensor.IsValid);

        var cover = default(EqsTacticsState);
        Assert.Equal(NodeStatus.Running, EqsTacticsNodes.TakeCover(ref p, ref cover, _entity, _repo));   // the heard point is enough to hide from
    }

    /// <summary>🔴 F3 as built (§9.8 G6) — Flank points its own sensor (the flank template) at the threat and moves on the first
    /// answer. A new answer far away does NOT re-route it while the threat stays put — the template scores relative to the
    /// unit's own position, so its walk alone moves the best point (re-routing on that would chase it round the target);
    /// once the THREAT has moved past the re-position distance, the next far answer re-routes. It ends on arrival, its sensor gone.</summary>
    [Fact]
    public void CE2108_Flank_ReroutesOnlyWhenTheThreatMoves_AndSucceedsOnArrival_WithItsSensorGone()
    {
        _repo.AddComponent(_entity, new LocomotionChannel());
        var threat = Remember(_repo.CreateEntity());
        void ThreatAt(float x, float y) { unsafe { ref var m = ref _repo.GetComponentRW<TargetMemory>(_entity); m.PositionsX[0] = x; m.PositionsY[0] = y; } }
        ThreatAt(50f, 50f);
        var p = Tunables(); var ws = default(EqsTacticsState);

        Assert.Equal(NodeStatus.Running, EqsTacticsNodes.Flank(ref p, ref ws, _entity, _repo));
        var child = ws.Sensor.ChildId;
        var sensor = _repo.GetComponentRO<EqsSensor>(child);
        Assert.Equal(FindFlankingPosition.BlueprintId, sensor.BlueprintId);
        Assert.Equal(threat, sensor.ContextSlot1);

        Answer(child, 5, 10f, 20f);
        EqsTacticsNodes.Flank(ref p, ref ws, _entity, _repo);
        uint first = _repo.GetComponentRO<LocomotionChannel>(_entity).ActionInstanceId;
        Assert.Equal(new Vector3(10f, 20f, 0f), Destination());

        Answer(child, 6, 30f, 20f);   // 20 m away, the threat has not moved: the unit's own walk moved the answer — stay on course
        Assert.Equal(NodeStatus.Running, EqsTacticsNodes.Flank(ref p, ref ws, _entity, _repo));
        Assert.Equal(first, _repo.GetComponentRO<LocomotionChannel>(_entity).ActionInstanceId);

        ThreatAt(70f, 50f);           // the threat moved 20 m …
        Answer(child, 7, 40f, 30f);   // … and the next far answer follows it: re-route
        Assert.Equal(NodeStatus.Running, EqsTacticsNodes.Flank(ref p, ref ws, _entity, _repo));
        Assert.NotEqual(first, _repo.GetComponentRO<LocomotionChannel>(_entity).ActionInstanceId);
        Assert.Equal(new Vector3(40f, 30f, 0f), Destination());

        _repo.GetComponentRW<LocomotionChannel>(_entity).Status = NodeStatus.Success;   // arrived
        Assert.Equal(NodeStatus.Success, EqsTacticsNodes.Flank(ref p, ref ws, _entity, _repo));
        _repo.FlushCommandBuffers();
        Assert.False(_repo.IsAlive(child), "the sensor goes on arrival");
    }

    /// <summary>⭐ FiringPosition is the same body with its own template and site.</summary>
    [Fact]
    public void CE2108_FiringPosition_PointsTheFiringTemplateAtTheThreat()
    {
        _repo.AddComponent(_entity, new LocomotionChannel());
        var threat = Remember(_repo.CreateEntity());
        var p = Tunables(); var ws = default(EqsTacticsState);
        Assert.Equal(NodeStatus.Running, EqsTacticsNodes.FiringPosition(ref p, ref ws, _entity, _repo));
        var sensor = _repo.GetComponentRO<EqsSensor>(ws.Sensor.ChildId);
        Assert.Equal(FindOpenFiringPosition.BlueprintId, sensor.BlueprintId);
        Assert.Equal(threat, sensor.ContextSlot1);
        Assert.Equal(ws.Sensor.ChildId, EqsChildSensor.Find(_repo, _entity, EqsTacticsNodes.FiringPositionSite));
    }

    /// <summary>🔴 §9.6a — with <c>FireWhileMoving</c> the unit fires at the threat while the move runs (the posture's ONE fire
    /// step), and the weapon stops when it arrives; without it, no shot is aimed on the way.</summary>
    [Fact]
    public void CE2108_FireWhileMoving_AimsTheWeaponWhileTheMoveRuns_AndStopsOnArrival()
    {
        _repo.AddComponent(_entity, new LocomotionChannel());
        _repo.AddComponent(_entity, new WeaponChannel());
        var threat = Remember(_repo.CreateEntity());
        var quiet = Tunables(); var ws = default(EqsTacticsState);
        EqsTacticsNodes.FiringPosition(ref quiet, ref ws, _entity, _repo);
        Answer(ws.Sensor.ChildId, 5, 10f, 20f);
        EqsTacticsNodes.FiringPosition(ref quiet, ref ws, _entity, _repo);
        Assert.Equal(NodeStatus.Running, _repo.GetComponentRO<LocomotionChannel>(_entity).Status);
        Assert.NotEqual(CombatConstants.ActionIdAimAndFire, _repo.GetComponentRO<WeaponChannel>(_entity).ActiveAction);   // off ⇒ silent

        var p = Tunables(); p.FireWhileMoving = 1;
        EqsTacticsNodes.FiringPosition(ref p, ref ws, _entity, _repo);
        Assert.Equal(CombatConstants.ActionIdAimAndFire, _repo.GetComponentRO<WeaponChannel>(_entity).ActiveAction);
        Assert.Equal(threat, ws.Fire.Threat);

        _repo.GetComponentRW<LocomotionChannel>(_entity).Status = NodeStatus.Success;   // arrived: the tree's Engage takes over
        Assert.Equal(NodeStatus.Success, EqsTacticsNodes.FiringPosition(ref p, ref ws, _entity, _repo));
        Assert.NotEqual(CombatConstants.ActionIdAimAndFire, _repo.GetComponentRO<WeaponChannel>(_entity).ActiveAction);
    }
}
