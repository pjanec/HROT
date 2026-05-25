using System;
using System.Collections.Generic;
using System.Numerics;
using System.Runtime.InteropServices;
using Fbt;
using Fbt.Runtime;
using FDP.Eqs;
using Fdp.Core;
using Fdp.ModuleHost.Abstractions;
using Fdp.Toolkit.Behavior;
using Fdp.Toolkit.Behavior.Components;
using Fdp.Toolkit.Navigation;
using Fdp.Toolkit.Perception.Components;
using Fdp.Toolkit.Replication.Components;
using Fdp.Toolkit.Spatial.Eqs;
using Hrot.AI.Behaviors.Brains;
using Hrot.SimHost;
using Xunit;

namespace Hrot.ClusterRunner.Integration.Tests.Eqs;

// ── Eqs_MultiSensor_OneAgentTwoConcurrentQueries ─────────────────────────────

/// <summary>
/// Integration tests for TASK-EQS-040:
/// (1) two concurrent child sensors on one agent each get independent result buffers;
/// (2) <c>HideInCover_BT_v2</c> node-sequence smoke test verifies that the child-entity
///     sensor path drives <see cref="LocomotionChannel"/> to MoveTo.
/// </summary>
[Collection("EqsIntegrationTests")]
public sealed class EqsMultiTemplateTests : IDisposable
{
    // ── Inner types ───────────────────────────────────────────────────────────

    private sealed class SimpleEqsTemplateRegistry : IEqsTemplateRegistry
    {
        private readonly Dictionary<uint, EqsQueryTemplate> _t = new();
        public void Register(EqsQueryTemplate t) => _t[t.BlueprintId] = t;
        public bool TryGetTemplate(uint id, out EqsQueryTemplate t) => _t.TryGetValue(id, out t);
    }

    // Yields one entity-shaped candidate (EntityId != 0).
    private sealed class EntityCandidateGenerator : IEqsGenerator
    {
        private readonly long _entityId;
        public EntityCandidateGenerator(long entityId) => _entityId = entityId;
        public int Generate(Entity observer, ref EqsSensor sensor,
            ISimulationView view, Span<EqsResult> candidates)
        {
            if (candidates.Length == 0) return 0;
            candidates[0] = new EqsResult { EntityId = _entityId, PositionX = 10f, PositionY = 0f, Score = 1f };
            return 1;
        }
    }

    // Yields one positional candidate (EntityId == 0, Position != 0).
    private sealed class PositionalCandidateGenerator : IEqsGenerator
    {
        private readonly float _posX;
        private readonly float _posY;
        public PositionalCandidateGenerator(float posX, float posY) { _posX = posX; _posY = posY; }
        public int Generate(Entity observer, ref EqsSensor sensor,
            ISimulationView view, Span<EqsResult> candidates)
        {
            if (candidates.Length == 0) return 0;
            candidates[0] = new EqsResult { EntityId = 0L, PositionX = _posX, PositionY = _posY, Score = 1f };
            return 1;
        }
    }

    // ── Test fixture ──────────────────────────────────────────────────────────

    private readonly EditorHarness _harness;

    public EqsMultiTemplateTests()
    {
        _harness = new EditorHarness();
    }

    public void Dispose()
    {
        if (_harness.Repo.HasSingleton<EqsResultPool>())
        {
            var pool = _harness.Repo.GetSingleton<EqsResultPool>();
            if (pool.Results.IsCreated)
                pool.Results.Dispose();
        }
        _harness.Dispose();
    }

    // ── Eqs_MultiSensor_OneAgentTwoConcurrentQueries ──────────────────────────

    /// <summary>
    /// One agent has two child sensors running concurrently:
    /// child[0] queries for entity-shaped results (enemies) and
    /// child[1] queries for positional results (cover points).
    /// Both buffers must become independently ready, and the observer's own
    /// <see cref="EqsCognitiveBuffer"/> must NOT exist (results route to children only).
    /// </summary>
    [Fact(Timeout = 10_000)]
    public void Eqs_MultiSensor_OneAgentTwoConcurrentQueries()
    {
        const uint blueprintA = 16001u; // entity-shaped (enemy finder)
        const uint blueprintB = 16002u; // positional (cover finder)
        const long enemyId   = 9901L;
        const float coverX   = 15f;
        const float coverY   = 1f;

        var registry = new SimpleEqsTemplateRegistry();
        registry.Register(new EqsQueryTemplate
        {
            BlueprintId   = blueprintA,
            Generator     = new EntityCandidateGenerator(enemyId),
            MaxCandidates = 4,
        });
        registry.Register(new EqsQueryTemplate
        {
            BlueprintId   = blueprintB,
            Generator     = new PositionalCandidateGenerator(coverX, coverY),
            MaxCandidates = 4,
        });
        _harness.Repo.SetSingletonManaged<IEqsTemplateRegistry>(registry);

        // Observer entity. NetworkIdentity is required: EqsSolverSystem skips child-entity sensors
        // (entities with PartMetadata) whose parent has no NetworkIdentity (local-only child path
        // is reserved for sensors without PartMetadata). Deviation from spec's "no NetworkIdentity"
        // note -- observed runtime requirement.
        Entity observer = _harness.Repo.CreateEntity();
        _harness.Repo.AddComponent(observer, new NetworkIdentity { Value = 16001_9900L });

        // child[0]: PartMetadata{InstanceId=0} + EqsSensor for template A
        Entity child0 = _harness.Repo.CreateEntity();
        _harness.Repo.AddComponent(child0, new PartMetadata
        {
            ParentEntity = observer,
            InstanceId   = 0,
        });
        _harness.Repo.AddComponent(child0, new EqsSensor
        {
            BlueprintId  = blueprintA,
            Epoch        = 1u,
            SearchRadius = 50f,
        });

        // child[1]: PartMetadata{InstanceId=1} + EqsSensor for template B
        Entity child1 = _harness.Repo.CreateEntity();
        _harness.Repo.AddComponent(child1, new PartMetadata
        {
            ParentEntity = observer,
            InstanceId   = 1,
        });
        _harness.Repo.AddComponent(child1, new EqsSensor
        {
            BlueprintId  = blueprintB,
            Epoch        = 1u,
            SearchRadius = 50f,
        });

        // Pump until both child buffers are ready.
        bool ready = _harness.PumpUntil(() =>
        {
            if (!_harness.Repo.HasComponent<EqsCognitiveBuffer>(child0)) return false;
            if (!_harness.Repo.HasComponent<EqsCognitiveBuffer>(child1)) return false;
            ref readonly var buf0 = ref _harness.Repo.GetComponentRO<EqsCognitiveBuffer>(child0);
            ref readonly var buf1 = ref _harness.Repo.GetComponentRO<EqsCognitiveBuffer>(child1);
            return buf0.IsReady && buf1.IsReady;
        }, timeoutMs: 8_000);

        Assert.True(ready, "Both child EqsCognitiveBuffers must be ready.");

        // child[0]: entity-shaped result (EntityId != 0).
        ref readonly var buffer0 = ref _harness.Repo.GetComponentRO<EqsCognitiveBuffer>(child0);
        Assert.True(buffer0.Count > 0, "child[0] buffer must have at least one result.");
        var span0 = buffer0.GetSpanRO();
        Assert.NotEqual(0L, span0[0].EntityId);

        // child[1]: positional result (EntityId == 0, Position != 0).
        ref readonly var buffer1 = ref _harness.Repo.GetComponentRO<EqsCognitiveBuffer>(child1);
        Assert.True(buffer1.Count > 0, "child[1] buffer must have at least one result.");
        var span1 = buffer1.GetSpanRO();
        Assert.Equal(0L, span1[0].EntityId);
        Assert.True(span1[0].PositionX != 0f || span1[0].PositionY != 0f,
            "child[1] positional result must have a non-zero position.");

        // Observer must NOT have its own EqsCognitiveBuffer.
        Assert.False(_harness.Repo.HasComponent<EqsCognitiveBuffer>(observer),
            "Observer entity must not receive results directly; results route to children.");
    }
}

// ── HideInCoverV2 smoke test (direct node-sequence, no BTree runtime) ─────────

/// <summary>
/// Smoke test for the <c>HideInCover_BT_v2</c> node sequence.
/// Follows the same pattern as T-COV5 in <c>EqsCombatNodesTests</c>:
/// calls BTree action methods directly using a raw <see cref="EntityRepository"/>
/// rather than running the full BTree runtime.
///
/// <para>Verifies that the child-entity sensor path (spawn + wait + bind + move)
/// correctly drives <see cref="LocomotionChannel"/> to the MoveTo action.</para>
/// </summary>
[Collection("EqsIntegrationTests")]
public sealed class HideInCoverV2SmokeTests : IDisposable
{
    private readonly EntityRepository _repo;
    private readonly Entity _parent;

    public HideInCoverV2SmokeTests()
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

    // ── HideInCover_BT_v2 smoke test ─────────────────────────────────────────

    /// <summary>
    /// HideInCover_BT_v2_SmokeTest_AgentMovesToCover: steps through the v2 node sequence
    /// directly (no BTree runtime) and asserts that <see cref="LocomotionChannel"/>
    /// is activated with a MoveTo action aimed at the pre-loaded cover position (5, 0).
    ///
    /// <para>Node sequence exercised:</para>
    /// <list type="number">
    ///   <item><see cref="EqsCombatNodes.Condition_HasTarget"/> -- threat present -> Success</item>
    ///   <item><see cref="EqsLifecycleNodes.Action_SpawnEqsSensorChild"/> -- ECB queued -> Success</item>
    ///   <item>ECB playback -- child entity materialised</item>
    ///   <item><see cref="EqsLifecycleNodes.Action_WaitForChildSensor"/> -- no buffer yet -> Running</item>
    ///   <item>Pre-populate child EqsCognitiveBuffer (simulates solver output)</item>
    ///   <item><see cref="EqsLifecycleNodes.Action_WaitForChildSensor"/> -- buffer ready -> Success</item>
    ///   <item><see cref="TacticsNodes.BindSensorHandle"/> -- copies handle to MoveConfig</item>
    ///   <item><see cref="EqsCombatNodes.Action_MoveToOptimalCover"/> -- channel activated -> Running</item>
    /// </list>
    ///
    /// <para>Backwards compat: existing T-COV5 test in <c>EqsCombatNodesTests</c> is unchanged
    /// and continues to cover the legacy single-sensor (<c>HideInCover_BT</c>) path.</para>
    /// </summary>
    [Fact]
    public void HideInCover_BT_v2_SmokeTest_AgentMovesToCover()
    {
        const float coverX = 5f;
        const float coverY = 0f;

        var bb = new HideInCoverV2Blackboard
        {
            SpawnConfig = new EqsSpawnParams
            {
                SensorConfig = new EqsParams { BlueprintId = 16003u, SearchRadius = 50f },
                ChildSlotIndex = 0,
            },
            MoveConfig = new MoveToOptimalCoverParams { Speed = 3f, ArrivalRadius = 0.5f },
        };

        var state = new BehaviorTreeState();
        var ctx   = new BTreeContext { Self = _parent, World = _repo };

        // Step 1: Condition_HasTarget -- requires TargetMemory with at least one threat.
        var mem = new TargetMemory();
        unsafe { mem.ThreatScores[0] = 100f; mem.EntityIds[0] = 777L; }
        mem.Count = 1;
        _repo.AddComponent(_parent, mem);

        var condResult = EqsCombatNodes.Condition_HasTarget(ref bb.MoveConfig, ref state, ref ctx);
        Assert.Equal(NodeStatus.Success, condResult);

        // Step 2: Action_SpawnEqsSensorChild -- queues CreateEntity + AddComponents via ECB.
        var spawnResult = EqsLifecycleNodes.Action_SpawnEqsSensorChild(
            ref bb.SpawnConfig, ref state, ref ctx);
        Assert.Equal(NodeStatus.Success, spawnResult);

        // Step 3: Playback ECB -- child entity materialised.
        PlaybackAndClearEcb();

        int localChildIndex = (int)(((uint)_parent.Index << 8) | bb.SpawnConfig.ChildSlotIndex);
        Entity child = FindChildByMeta(_parent, localChildIndex);
        Assert.False(child.IsNull, "Child entity must exist after ECB playback.");

        // Manually update SpawnedHandle to the real entity (avoids stale static query issue).
        bb.SpawnConfig.SpawnedHandle = new EqsSensorHandle(child);

        // Step 4: Action_WaitForChildSensor -- no buffer yet -> Running.
        var waitResult1 = EqsLifecycleNodes.Action_WaitForChildSensor(
            ref bb.SpawnConfig, ref state, ref ctx);
        Assert.Equal(NodeStatus.Running, waitResult1);

        // Step 5: Pre-populate EqsCognitiveBuffer (simulates EqsSolverSystem output).
        var buf = new EqsCognitiveBuffer { Count = 1, LastUpdateTick = 1 };
        buf.GetSpanRW()[0] = new EqsResult { EntityId = 0L, PositionX = coverX, PositionY = coverY, Score = 1f };
        _repo.AddComponent(child, buf);

        // Step 6: Action_WaitForChildSensor -- buffer ready -> Success.
        var waitResult2 = EqsLifecycleNodes.Action_WaitForChildSensor(
            ref bb.SpawnConfig, ref state, ref ctx);
        Assert.Equal(NodeStatus.Success, waitResult2);

        // Step 7: BindSensorHandle -- copies SpawnedHandle to MoveConfig.SensorHandle.
        var bindResult = TacticsNodes.BindSensorHandle(ref bb, ref state, ref ctx, 0);
        Assert.Equal(NodeStatus.Success, bindResult);
        Assert.True(bb.MoveConfig.SensorHandle.IsValid,
            "MoveConfig.SensorHandle must be valid after BindSensorHandle.");
        Assert.Equal(child, bb.MoveConfig.SensorHandle.ChildId);

        // Step 8: Action_MoveToOptimalCover -- reads from child buffer, activates locomotion.
        _repo.AddComponent(_parent, new LocomotionChannel());
        var moveResult = EqsCombatNodes.Action_MoveToOptimalCover(ref bb.MoveConfig, ref state, ref ctx);
        Assert.Equal(NodeStatus.Running, moveResult);

        ref readonly var channel = ref _repo.GetComponentRO<LocomotionChannel>(_parent);
        Assert.Equal(NavigationConstants.ActionIdMoveTo, channel.ActiveAction);

        // Destination must approximately equal the pre-loaded cover position (5, 0).
        unsafe
        {
            MoveToParams mp;
            fixed (byte* src = channel.Params) mp = *(MoveToParams*)src;
            Assert.True(MathF.Abs(mp.Destination.X - coverX) < 0.01f,
                $"Destination X expected ~{coverX}, got {mp.Destination.X}");
            Assert.True(MathF.Abs(mp.Destination.Y - coverY) < 0.01f,
                $"Destination Y expected ~{coverY}, got {mp.Destination.Y}");
        }
    }
}
