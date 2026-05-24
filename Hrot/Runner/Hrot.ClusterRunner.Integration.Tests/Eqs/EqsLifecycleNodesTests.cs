using Fbt;
using Fdp.Core;
using Fdp.Toolkit.Behavior;
using Fdp.Toolkit.Behavior.Components;
using Fdp.Toolkit.Spatial.Eqs;
using Hrot.AI.Behaviors.Brains;
using Hrot.SimHost;
using Xunit;

namespace Hrot.ClusterRunner.Integration.Tests.Eqs;

/// <summary>
/// Unit tests for <see cref="EqsLifecycleNodes"/> BTree action and deactivator nodes.
///
/// <para>Tests call the node methods directly using a real <see cref="EntityRepository"/>
/// with a manually constructed <see cref="BTreeContext"/>
/// (same pattern used in <c>HillAttackNodeTests</c>).</para>
/// </summary>
[Collection("EqsIntegrationTests")]
public sealed class EqsLifecycleNodesTests : IDisposable
{
    private readonly EntityRepository _repo;
    private readonly Entity _entity;

    public EqsLifecycleNodesTests()
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

    // ── T5: Action_WaitForSensor ──────────────────────────────────────────────

    /// <summary>
    /// T5: <c>Action_WaitForSensor</c> returns Running while no buffer exists,
    /// then Success once <see cref="EqsCognitiveBuffer.IsReady"/> is true.
    /// </summary>
    [Fact]
    public void EqsLifecycleNodes_WaitForSensor_ReturnsSuccessWhenReady()
    {
        var p     = new EqsParams { BlueprintId = 1, SearchRadius = 50f };
        var state = new BehaviorTreeState();
        var ctx   = new BTreeContext { Self = _entity, World = _repo };

        // No buffer yet -> Running
        Assert.Equal(NodeStatus.Running,
            EqsLifecycleNodes.Action_WaitForSensor(ref p, ref state, ref ctx));

        // Add buffer with LastUpdateTick=0 -> IsReady=false -> Running
        _repo.AddComponent(_entity, new EqsCognitiveBuffer { LastUpdateTick = 0 });
        Assert.Equal(NodeStatus.Running,
            EqsLifecycleNodes.Action_WaitForSensor(ref p, ref state, ref ctx));

        // Set LastUpdateTick > 0 -> IsReady=true -> Success
        ref var buffer = ref _repo.GetComponentRW<EqsCognitiveBuffer>(_entity);
        buffer.LastUpdateTick = 1;
        Assert.Equal(NodeStatus.Success,
            EqsLifecycleNodes.Action_WaitForSensor(ref p, ref state, ref ctx));
    }

    // ── T6: Deactivate_MaintainEqsSensor ─────────────────────────────────────

    /// <summary>
    /// T6: Calling the deactivator after <c>Action_MaintainEqsSensor</c> has added the
    /// sensor removes both <see cref="EqsSensor"/> and <see cref="EqsCognitiveBuffer"/>
    /// (simulating a BTree branch abort).
    /// </summary>
    [Fact]
    public void EqsLifecycleNodes_Deactivator_RemovesComponentsOnAbort()
    {
        var p     = new EqsParams { BlueprintId = 2, SearchRadius = 75f, FactionFilter = 1 };
        var state = new BehaviorTreeState();
        var ctx   = new BTreeContext { Self = _entity, World = _repo };

        // First tick adds EqsSensor
        var result = EqsLifecycleNodes.Action_MaintainEqsSensor(ref p, ref state, ref ctx);
        Assert.Equal(NodeStatus.Running, result);
        Assert.True(_repo.HasComponent<EqsSensor>(_entity), "EqsSensor must be added on first tick");

        // Add a buffer too (as the solver would)
        _repo.AddComponent(_entity, new EqsCognitiveBuffer { LastUpdateTick = 1 });

        // Deactivator fires (branch abort)
        EqsLifecycleNodes.Deactivate_MaintainEqsSensor(ref p, ref state, ref ctx);

        Assert.False(_repo.HasComponent<EqsSensor>(_entity),
            "EqsSensor must be removed by deactivator");
        Assert.False(_repo.HasComponent<EqsCognitiveBuffer>(_entity),
            "EqsCognitiveBuffer must be removed by deactivator");
    }

    // ── T7: Epoch increments only on param change ─────────────────────────────

    /// <summary>
    /// T7: <see cref="EqsSensor.Epoch"/> is set to 1 on first add, stays at 1 on
    /// repeated ticks with identical params, and increments to 2 when params change.
    /// A subsequent tick with unchanged params keeps the epoch at 2.
    /// </summary>
    [Fact]
    public void EqsLifecycleNodes_MaintainSensor_EpochIncrementsOnlyOnParamChange()
    {
        var p     = new EqsParams { BlueprintId = 3, SearchRadius = 100f, FactionFilter = 2 };
        var state = new BehaviorTreeState();
        var ctx   = new BTreeContext { Self = _entity, World = _repo };

        // Tick 1: sensor added, Epoch=1
        EqsLifecycleNodes.Action_MaintainEqsSensor(ref p, ref state, ref ctx);
        Assert.Equal(1u, _repo.GetComponentRO<EqsSensor>(_entity).Epoch);

        // Tick 2: same params, Epoch stays at 1
        EqsLifecycleNodes.Action_MaintainEqsSensor(ref p, ref state, ref ctx);
        Assert.Equal(1u, _repo.GetComponentRO<EqsSensor>(_entity).Epoch);

        // Tick 3: SearchRadius changed -> Epoch increments to 2
        p.SearchRadius = 200f;
        EqsLifecycleNodes.Action_MaintainEqsSensor(ref p, ref state, ref ctx);
        Assert.Equal(2u, _repo.GetComponentRO<EqsSensor>(_entity).Epoch);

        // Tick 4: same params again -> Epoch stays at 2
        EqsLifecycleNodes.Action_MaintainEqsSensor(ref p, ref state, ref ctx);
        Assert.Equal(2u, _repo.GetComponentRO<EqsSensor>(_entity).Epoch);
    }
}
