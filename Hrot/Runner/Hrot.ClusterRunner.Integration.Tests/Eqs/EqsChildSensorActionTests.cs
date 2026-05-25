using Fbt;
using Fbt.Runtime;
using FDP.Eqs;
using Fdp.Core;
using Fdp.ModuleHost.Abstractions;
using Fdp.Toolkit.Behavior;
using Fdp.Toolkit.Behavior.Components;
using Fdp.Toolkit.Replication.Components;
using Fdp.Toolkit.Replication.Systems;
using Fdp.Toolkit.Spatial.Eqs;
using Hrot.AI.Behaviors.Brains;
using Hrot.SimHost;
using Xunit;

namespace Hrot.ClusterRunner.Integration.Tests.Eqs;

/// <summary>
/// Unit tests for <see cref="EqsLifecycleNodes"/> child-sensor spawn/deactivate actions
/// (TASK-EQS-039: T-CS-A1 through T-CS-A5).
///
/// <para>Uses direct method invocation on a raw <see cref="EntityRepository"/> with manual
/// ECB playback -- same pattern as <c>EqsLifecycleNodesTests</c>.
/// <c>SubEntityCleanupSystem</c> is called directly for T-CS-A5 to avoid dependency on the
/// full simulation pipeline.</para>
///
/// <para>Domain range: 250-259.</para>
///
/// <para>NOTE: Tests avoid calling <c>Action_SpawnEqsSensorChild</c> a second time after
/// ECB playback in order to prevent the static <c>_childScanQuery</c> on
/// <see cref="EqsLifecycleNodes"/> from being set to a stale (disposed) repo by a previous
/// test. Idempotency (T-CS-A2) is tested via the steady-state
/// <c>SpawnedHandle.IsValid</c> fast path, which does not use the query.</para>
/// </summary>
[Collection("EqsIntegrationTests")]
public sealed class EqsChildSensorActionTests : IDisposable
{
    private readonly EntityRepository _repo;
    private readonly Entity _parent;

    public EqsChildSensorActionTests()
    {
        _repo   = new EntityRepository();
        SimHostComponentRegistry.RegisterAll(_repo);
        _parent = _repo.CreateEntity();
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

    // ── Helpers ───────────────────────────────────────────────────────────────

    private void PlaybackAndClearEcb()
    {
        var ecb = (EntityCommandBuffer)((ISimulationView)_repo).GetCommandBuffer();
        ecb.Playback(_repo);
        ecb.Clear();
    }

    // Finds the first entity in _repo with PartMetadata matching (parent, instanceId).
    private Entity FindChildByMeta(Entity parent, int instanceId)
    {
        var query = _repo.Query().With<PartMetadata>().Build();
        foreach (var candidate in query)
        {
            var meta = _repo.GetComponent<PartMetadata>(candidate);
            if (meta.ParentEntity.Equals(parent) && meta.InstanceId == instanceId)
                return candidate;
        }
        return Entity.Null;
    }

    private int CountChildrenByMeta(Entity parent, int instanceId)
    {
        int count = 0;
        var query = _repo.Query().With<PartMetadata>().Build();
        foreach (var candidate in query)
        {
            var meta = _repo.GetComponent<PartMetadata>(candidate);
            if (meta.ParentEntity.Equals(parent) && meta.InstanceId == instanceId)
                count++;
        }
        return count;
    }

    // Computes the same deterministic localChildIndex used by Action_SpawnEqsSensorChild.
    private static int LocalChildIndex(Entity parent, byte slot)
        => (int)(((uint)parent.Index << 8) | slot);

    // Default spawn params for a given slot.
    private EqsSpawnParams MakeParams(byte slot = 0) => new EqsSpawnParams
    {
        SensorConfig = new EqsParams { BlueprintId = 2501u + slot, SearchRadius = 50f },
        ChildSlotIndex = slot,
    };

    // ── T-CS-A1: Spawn action, slot 0 ─────────────────────────────────────────

    /// <summary>
    /// T-CS-A1: Calling <c>Action_SpawnEqsSensorChild</c> once queues an entity creation
    /// via ECB. After playback the child entity carries the expected <see cref="PartMetadata"/>
    /// and an <see cref="EqsSensor"/> matching the spawn params.
    /// </summary>
    [Fact]
    public void SpawnAction_Slot0_ChildHasCorrectPartMetadata()
    {
        byte slot = 0;
        int  expectedInstanceId = LocalChildIndex(_parent, slot);

        var p     = MakeParams(slot);
        var state = new BehaviorTreeState();
        var ctx   = new BTreeContext { Self = _parent, World = _repo };

        // Tick 1: queues CreateEntity + AddComponent via ECB.
        var result = EqsLifecycleNodes.Action_SpawnEqsSensorChild(ref p, ref state, ref ctx);

        Assert.Equal(NodeStatus.Success, result);

        PlaybackAndClearEcb();

        Entity child = FindChildByMeta(_parent, expectedInstanceId);
        Assert.False(child.IsNull, "Child entity must exist after ECB playback.");

        var meta = _repo.GetComponent<PartMetadata>(child);
        Assert.Equal(_parent, meta.ParentEntity);
        Assert.Equal(expectedInstanceId, meta.InstanceId);

        var sensor = _repo.GetComponent<EqsSensor>(child);
        Assert.Equal(p.SensorConfig.BlueprintId, sensor.BlueprintId);
        Assert.Equal(p.SensorConfig.SearchRadius, sensor.SearchRadius);
    }

    // ── T-CS-A2: Idempotency -- same slot twice ────────────────────────────────

    /// <summary>
    /// T-CS-A2: Calling the action a second time with a valid <see cref="EqsSensorHandle"/>
    /// (steady-state fast path) must NOT spawn a second child entity.
    /// </summary>
    [Fact]
    public void SpawnAction_SameSlotTwice_ExactlyOneChild()
    {
        byte slot = 0;
        int  expectedInstanceId = LocalChildIndex(_parent, slot);

        var p     = MakeParams(slot);
        var state = new BehaviorTreeState();
        var ctx   = new BTreeContext { Self = _parent, World = _repo };

        // Tick 1: spawn via ECB.
        EqsLifecycleNodes.Action_SpawnEqsSensorChild(ref p, ref state, ref ctx);
        PlaybackAndClearEcb();

        Entity realChild = FindChildByMeta(_parent, expectedInstanceId);
        Assert.False(realChild.IsNull, "Precondition: child must exist after first spawn.");

        // Simulate what tick 2's FindExistingChild would do: update the handle to the real entity.
        p.SpawnedHandle = new EqsSensorHandle(realChild);

        // Tick 2 (steady-state): valid handle -- must return Success without queuing anything.
        var result2 = EqsLifecycleNodes.Action_SpawnEqsSensorChild(ref p, ref state, ref ctx);
        Assert.Equal(NodeStatus.Success, result2);

        // ECB must be empty (no new CreateEntity).
        PlaybackAndClearEcb();

        // Exactly 1 child with matching PartMetadata.
        Assert.Equal(1, CountChildrenByMeta(_parent, expectedInstanceId));
    }

    // ── T-CS-A3: Two different slots ─────────────────────────────────────────

    /// <summary>
    /// T-CS-A3: Spawning two child sensors with different <c>ChildSlotIndex</c> values
    /// produces two distinct entities.
    /// </summary>
    [Fact]
    public void SpawnAction_TwoDifferentSlots_TwoDistinctChildren()
    {
        var state = new BehaviorTreeState();
        var ctx   = new BTreeContext { Self = _parent, World = _repo };

        // Slot 0.
        var p0 = MakeParams(0);
        EqsLifecycleNodes.Action_SpawnEqsSensorChild(ref p0, ref state, ref ctx);
        PlaybackAndClearEcb();

        // Slot 1.
        var p1 = MakeParams(1);
        EqsLifecycleNodes.Action_SpawnEqsSensorChild(ref p1, ref state, ref ctx);
        PlaybackAndClearEcb();

        int id0 = LocalChildIndex(_parent, 0);
        int id1 = LocalChildIndex(_parent, 1);

        Entity child0 = FindChildByMeta(_parent, id0);
        Entity child1 = FindChildByMeta(_parent, id1);

        Assert.False(child0.IsNull, "Child for slot 0 must exist.");
        Assert.False(child1.IsNull, "Child for slot 1 must exist.");
        Assert.NotEqual(child0, child1);
    }

    // ── T-CS-A4: Deactivate after spawn ──────────────────────────────────────

    /// <summary>
    /// T-CS-A4: <c>Deactivate_SpawnEqsSensorChild</c> must destroy the child entity via
    /// ECB and reset <see cref="EqsSpawnParams.SpawnedHandle"/> to default.
    /// </summary>
    [Fact]
    public void Deactivate_AfterSpawn_ChildDestroyedAndHandleCleared()
    {
        byte slot = 0;
        int  expectedInstanceId = LocalChildIndex(_parent, slot);

        var p     = MakeParams(slot);
        var state = new BehaviorTreeState();
        var ctx   = new BTreeContext { Self = _parent, World = _repo };

        // Spawn and materialise.
        EqsLifecycleNodes.Action_SpawnEqsSensorChild(ref p, ref state, ref ctx);
        PlaybackAndClearEcb();

        Entity child = FindChildByMeta(_parent, expectedInstanceId);
        Assert.False(child.IsNull, "Precondition: child must exist after spawn.");

        // Set the handle to the real entity (tick 2 steady-state assignment).
        p.SpawnedHandle = new EqsSensorHandle(child);

        // Deactivate: queues DestroyEntity via ECB, clears handle.
        EqsLifecycleNodes.Deactivate_SpawnEqsSensorChild(ref p, ref state, ref ctx);

        Assert.False(p.SpawnedHandle.IsValid, "SpawnedHandle must be cleared to default.");

        PlaybackAndClearEcb();

        Assert.False(_repo.IsAlive(child), "Child entity must be destroyed after deactivation.");
    }

    // ── T-CS-A5: Parent death + SubEntityCleanupSystem ───────────────────────

    /// <summary>
    /// T-CS-A5: When the parent entity is destroyed, <see cref="SubEntityCleanupSystem"/>
    /// must destroy the orphaned child entity on the next PostSimulation pass.
    /// </summary>
    [Fact]
    public void ParentDeath_SubEntityCleanupSystem_ChildDestroyed()
    {
        byte slot = 0;
        int  expectedInstanceId = LocalChildIndex(_parent, slot);

        var p     = MakeParams(slot);
        var state = new BehaviorTreeState();
        var ctx   = new BTreeContext { Self = _parent, World = _repo };

        // Spawn and materialise.
        EqsLifecycleNodes.Action_SpawnEqsSensorChild(ref p, ref state, ref ctx);
        PlaybackAndClearEcb();

        Entity child = FindChildByMeta(_parent, expectedInstanceId);
        Assert.False(child.IsNull, "Precondition: child must exist.");
        Assert.True(_repo.IsAlive(child), "Precondition: child must be alive.");

        // Destroy parent directly (simulates what happens in production when the unit dies).
        _repo.DestroyEntity(_parent);
        Assert.False(_repo.IsAlive(_parent), "Precondition: parent must be dead.");

        // Run SubEntityCleanupSystem directly (PostSimulation phase).
        var cleanupSystem = new SubEntityCleanupSystem();
        cleanupSystem.Execute(_repo, 0f);

        Assert.False(_repo.IsAlive(child),
            "Child entity must be destroyed by SubEntityCleanupSystem after parent death.");
    }
}
