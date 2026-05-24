using System.Numerics;
using System.Runtime.InteropServices;
using Fbt;
using Fdp.Core;
using Fdp.Toolkit.Behavior;
using Fdp.Toolkit.Behavior.Components;
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
        var state = new BehaviorTreeState();
        var ctx   = new BTreeContext { Self = _entity, World = _repo };

        // Add EqsCognitiveBuffer with one ready candidate
        var buf = new EqsCognitiveBuffer { Count = 1, LastUpdateTick = 1 };
        buf.GetSpanRW()[0] = new EqsResult { PositionX = 10f, PositionY = 20f, Score = 1f };
        _repo.AddComponent(_entity, buf);

        // Add LocomotionChannel (default zero state)
        _repo.AddComponent(_entity, new LocomotionChannel());

        var result = EqsCombatNodes.Action_MoveToOptimalCover(ref p, ref state, ref ctx);

        Assert.Equal(NodeStatus.Running, result);

        ref readonly var channel = ref _repo.GetComponentRO<LocomotionChannel>(_entity);
        Assert.Equal(NavigationConstants.ActionIdMoveTo, channel.ActiveAction);
        unsafe
        {
            MoveToParams mp;
            fixed (byte* src = channel.Params) mp = *(MoveToParams*)src;
            Assert.Equal(new Vector2(10f, 20f), mp.Destination);
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
        var state = new BehaviorTreeState();
        var ctx   = new BTreeContext { Self = _entity, World = _repo };

        // Add a buffer that is not ready (Count=0, LastUpdateTick=0)
        _repo.AddComponent(_entity, new EqsCognitiveBuffer { Count = 0, LastUpdateTick = 0 });
        _repo.AddComponent(_entity, new LocomotionChannel());

        var result = EqsCombatNodes.Action_MoveToOptimalCover(ref p, ref state, ref ctx);

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
        var state = new BehaviorTreeState();
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

        var result = EqsCombatNodes.Action_MoveToOptimalCover(ref p, ref state, ref ctx);

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
        var state = new BehaviorTreeState();
        var ctx   = new BTreeContext { Self = _entity, World = _repo };

        // Step 1: no TargetMemory component
        Assert.Equal(NodeStatus.Failure,
            EqsCombatNodes.Condition_HasTarget(ref p, ref state, ref ctx));

        // Step 2: component present but Count=0 (no entries)
        _repo.AddComponent(_entity, new TargetMemory());
        Assert.Equal(NodeStatus.Failure,
            EqsCombatNodes.Condition_HasTarget(ref p, ref state, ref ctx));

        // Step 3: add a live threat entry
        ref var mem = ref _repo.GetComponentRW<TargetMemory>(_entity);
        unsafe { mem.ThreatScores[0] = 1.5f; }
        mem.Count = 1;
        Assert.Equal(NodeStatus.Success,
            EqsCombatNodes.Condition_HasTarget(ref p, ref state, ref ctx));
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
        var state      = new BehaviorTreeState();
        var ctx        = new BTreeContext { Self = _entity, World = _repo };

        // ── Phase A: threat present, buffer ready ──────────────────────────────

        // Step 1: add TargetMemory with a live threat
        var mem = new TargetMemory();
        unsafe { mem.ThreatScores[0] = 2f; mem.EntityIds[0] = 99L; }
        mem.Count = 1;
        _repo.AddComponent(_entity, mem);

        // Step 2: simulate Action_MaintainEqsSensor (adds EqsSensor on first tick)
        var maintainResult = EqsLifecycleNodes.Action_MaintainEqsSensor(ref eqsParams, ref state, ref ctx);
        Assert.Equal(NodeStatus.Running, maintainResult);
        Assert.True(_repo.HasComponent<EqsSensor>(_entity));

        // Step 3: pre-populate EqsCognitiveBuffer as the solver would
        var buf = new EqsCognitiveBuffer { Count = 1, LastUpdateTick = 1 };
        buf.GetSpanRW()[0] = new EqsResult { PositionX = 30f, PositionY = 40f, Score = 1f };
        _repo.AddComponent(_entity, buf);

        // Step 4: add LocomotionChannel
        _repo.AddComponent(_entity, new LocomotionChannel());

        // Step 5: call Action_MoveToOptimalCover
        var moveResult = EqsCombatNodes.Action_MoveToOptimalCover(ref moveParams, ref state, ref ctx);
        Assert.Equal(NodeStatus.Running, moveResult);
        Assert.Equal(NavigationConstants.ActionIdMoveTo,
            _repo.GetComponentRO<LocomotionChannel>(_entity).ActiveAction);

        // ── Phase B: threat removed -> deactivator clears sensor ──────────────

        // Remove threat from TargetMemory
        ref var memW = ref _repo.GetComponentRW<TargetMemory>(_entity);
        memW.Count = 0;
        unsafe { memW.ThreatScores[0] = 0f; }

        // The ObserverSelector would abort the branch and call the deactivator
        EqsLifecycleNodes.Deactivate_MaintainEqsSensor(ref eqsParams, ref state, ref ctx);

        Assert.False(_repo.HasComponent<EqsSensor>(_entity),
            "EqsSensor must be removed when the branch is aborted");
        Assert.False(_repo.HasComponent<EqsCognitiveBuffer>(_entity),
            "EqsCognitiveBuffer must be removed when the branch is aborted");
    }
}
