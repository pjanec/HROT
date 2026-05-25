using System;
using System.Collections.Generic;
using System.Numerics;
using System.Threading;
using Fbt;
using Fdp.Core;
using Fdp.ModuleHost.Abstractions;
using Fdp.Toolkit.Behavior;
using Fdp.Toolkit.Behavior.Components;
using Fdp.Toolkit.Perception.Components;
using Fdp.Toolkit.Physics;
using Fdp.Toolkit.Physics.Components;
using Fdp.Toolkit.Replication.Components;
using Fdp.Toolkit.Replication.Services;
using Fdp.Toolkit.Spatial.Eqs;
using Hrot.AI.Behaviors.Brains;
using Hrot.Map.Common;
using Hrot.Network.NED.SimHost;
using Hrot.SimHost;
using Hrot.SimHost.Systems;
using Xunit;

namespace Hrot.ClusterRunner.Integration.Tests.Eqs;

/// <summary>
/// Integration and unit tests for TASK-EQS-035 and TASK-EQS-036:
/// EqsSensor context slots (ContextSlot0/1/2) and the LOS tests that read
/// threat position from the slot entity's <see cref="SimTransform"/> instead of
/// the hardcoded <c>TargetMemory[0]</c> position.
///
/// <para>Domain range: 231-239.</para>
///
/// <list type="number">
///   <item>T-CS1 -- Context slot value survives DDS Brain-to-Muscle round-trip.</item>
///   <item>T-CS2 -- Null context slots survive DDS round-trip (arrive as Entity.Null on Muscle).</item>
///   <item>T-CS3 -- Ingress translator returns Entity.Null for an unresolved network ID.</item>
///   <item>T-CS4 -- Action_MaintainEqsSensor increments Epoch when a context slot changes.</item>
///   <item>T-CS5 -- CheapLineOfSightTest reads threat position from ContextSlot1's SimTransform.</item>
///   <item>T-CS6 -- CheapLineOfSightTest bypasses (all candidates survive) when ContextSlot1 is null.</item>
///   <item>T-CS7 -- AccurateLineOfSightTest reads threat position from ContextSlot1's SimTransform.</item>
/// </list>
/// </summary>
[Collection("EqsIntegrationTests")]
public sealed class EqsContextSlotTests : IDisposable
{
    // Domain range: 231-239.
    private static int _domainCounter = 230;
    private static int NextDomain() => Interlocked.Increment(ref _domainCounter);

    // ── Inner types ────────────────────────────────────────────────────────────

    private sealed class SimpleEqsTemplateRegistry : IEqsTemplateRegistry
    {
        private readonly Dictionary<uint, EqsQueryTemplate> _t = new();
        public void Register(EqsQueryTemplate t) => _t[t.BlueprintId] = t;
        public bool TryGetTemplate(uint id, out EqsQueryTemplate t) => _t.TryGetValue(id, out t);
    }

    // LOS service: returns true (exposed) when the target position has X ~= 20.
    // Used in T-CS5/T-CS6 to verify which position the test reads.
    private sealed class PositionCaptureLosService : ILosService
    {
        public float LastToX { get; private set; } = float.NaN;

        // Returns true (exposed) when to.X is near 20.
        public bool HasCheapLineOfSight(Vector2 from, Vector2 to)
        {
            LastToX = to.X;
            return MathF.Abs(to.X - 20f) < 1f; // exposed only when target is near x=20
        }
    }

    // Mock raycast solver: captures RaycastRequestEvent.End and writes a synthetic hit.
    [UpdateInPhase(SystemPhase.PostSimulation)]
    private sealed class CapturingMockRaycastSolverSystem : IEcsModuleSystem
    {
        public Vector3 LastRaycastEnd { get; private set; }
        public bool    RaycastSubmitted { get; private set; }

        public void Execute(ISimulationView view, float deltaTime)
        {
            if (view is not EntityRepository repo) return;
            if (!repo.HasSingleton<RaycastBatchData>()) return;

            ref var batch  = ref repo.GetSingleton<RaycastBatchData>();
            var     events = view.ReadEvents<RaycastRequestEvent>();

            for (int i = 0; i < events.Length; i++)
            {
                ref readonly var evt = ref events[i];
                LastRaycastEnd   = evt.End;
                RaycastSubmitted = true;

                int slot = (int)((uint)evt.RayId % (uint)PhysicsConstants.RaycastBatchCapacity);
                batch.Hits[slot] = new RaycastHit { RayId = evt.RayId, HasHit = 1 };
            }
        }
    }

    // ── Test fixture (EditorHarness tests) ────────────────────────────────────

    private readonly CapturingMockRaycastSolverSystem _mockSolver;
    private readonly EditorHarness _harness;

    public EqsContextSlotTests()
    {
        _mockSolver = new CapturingMockRaycastSolverSystem();
        _harness    = new EditorHarness(extraGlobalSystems: new IEcsModuleSystem[] { _mockSolver });
    }

    public void Dispose()
    {
        if (_harness.Repo.HasSingleton<EqsResultPool>())
        {
            var pool = _harness.Repo.GetSingleton<EqsResultPool>();
            if (pool.Results.IsCreated) pool.Results.Dispose();
        }
        // Do NOT dispose RaycastBatchData -- owned by PhysicsToolkitModule inside EditorHarness.
        _harness.Dispose();
    }

    // ── T-CS1: Context slot round-trips through DDS ───────────────────────────

    /// <summary>
    /// T-CS1: Attaches an <see cref="EqsSensor"/> with <c>ContextSlot1</c> pointing to a
    /// second Brain entity.  After DDS replication the Muscle ghost entity's
    /// <c>EqsSensor.ContextSlot1</c> must resolve to the corresponding Muscle-side ghost.
    ///
    /// <para>
    /// The wire format carries a network ID (NOT the Brain-side entity handle, since
    /// Entity.Index values differ between processes).
    /// </para>
    /// </summary>
    [Fact(Timeout = 30_000)]
    public void ContextSlot_RoundTrip_PreservesEntityValue()
    {
        int domainId = NextDomain();
        using var harness = new HrotRunnerHarness("simhost,cgf", domainId);

        // Spawn the observer (parent) and the slot target on Brain.
        long parentNetworkId = harness.Cgf!.TestHook_SpawnEntityWithSplitAuthority(
            TkbEntityTypes.Tank_M1Abrams, muscleNodeId: 1);
        long targetNetworkId = harness.Cgf!.TestHook_SpawnEntityWithSplitAuthority(
            TkbEntityTypes.Tank_M1Abrams, muscleNodeId: 1);

        // Wait for both Muscle ghosts to appear.
        bool entitiesReady = harness.PumpUntil(
            () => harness.SimHost.TestHook_EntityMap.TryGetEntity(parentNetworkId, out _)
               && harness.SimHost.TestHook_EntityMap.TryGetEntity(targetNetworkId, out _),
            timeoutFrames: 2000);
        Assert.True(entitiesReady, "Both Muscle ghost entities must appear within timeout.");

        // Resolve Brain-side entity handles.
        harness.Cgf!.GhostEntityMap!.TryGetEntity(parentNetworkId, out Entity cgfParent);
        harness.Cgf!.GhostEntityMap!.TryGetEntity(targetNetworkId, out Entity cgfTarget);

        // Register a minimal EQS template so the sensor can be created on the Muscle.
        var registry = new SimpleEqsTemplateRegistry();
        registry.Register(new EqsQueryTemplate
        {
            BlueprintId   = 231u,
            Generator     = new DummyGenerator(),
            MaxCandidates = 4,
        });
        harness.SimHost.World!.SetSingletonManaged<IEqsTemplateRegistry>(registry);

        // Attach EqsSensor with ContextSlot1 = Brain-side target entity.
        harness.Cgf!.World!.AddComponent(cgfParent, new EqsSensor
        {
            BlueprintId  = 231u,
            Epoch        = 1u,
            SearchRadius = 50f,
            ContextSlot1 = cgfTarget,
        });

        // Resolve the Muscle-side target ghost for the assertion.
        harness.SimHost.TestHook_EntityMap.TryGetEntity(targetNetworkId, out Entity muscleTarget);

        // Pump until the Muscle ghost EqsSensor is present and ContextSlot1 is not null.
        bool slotReady = harness.PumpUntil(
            () =>
            {
                harness.SimHost.TestHook_EntityMap.TryGetEntity(parentNetworkId, out Entity muscleParent);
                if (muscleParent.IsNull) return false;
                if (!harness.SimHost.World!.HasComponent<EqsSensor>(muscleParent)) return false;
                ref readonly var s = ref harness.SimHost.World!.GetComponentRO<EqsSensor>(muscleParent);
                return !s.ContextSlot1.IsNull;
            },
            timeoutFrames: 2000);
        Assert.True(slotReady, "Muscle EqsSensor.ContextSlot1 must be non-null after DDS replication.");

        harness.SimHost.TestHook_EntityMap.TryGetEntity(parentNetworkId, out Entity muscleParent);
        ref readonly var sensor = ref harness.SimHost.World!.GetComponentRO<EqsSensor>(muscleParent);
        Assert.Equal(muscleTarget, sensor.ContextSlot1);
    }

    // ── T-CS2: Null slots survive DDS round-trip ──────────────────────────────

    /// <summary>
    /// T-CS2: An <see cref="EqsSensor"/> with all three context slots set to
    /// <see cref="Entity.Null"/> must arrive on the Muscle with all three slots null.
    /// Wire value 0 must deserialise to <see cref="Entity.Null"/>.
    /// </summary>
    [Fact(Timeout = 30_000)]
    public void ContextSlot_NullEntity_Survives()
    {
        int domainId = NextDomain();
        using var harness = new HrotRunnerHarness("simhost,cgf", domainId);

        long networkId = harness.Cgf!.TestHook_SpawnEntityWithSplitAuthority(
            TkbEntityTypes.Tank_M1Abrams, muscleNodeId: 1);

        bool entityReady = harness.PumpUntil(
            () => harness.SimHost.TestHook_EntityMap.TryGetEntity(networkId, out _),
            timeoutFrames: 2000);
        Assert.True(entityReady, "Muscle ghost entity must appear within timeout.");

        harness.Cgf!.GhostEntityMap!.TryGetEntity(networkId, out Entity cgfEntity);

        var registry = new SimpleEqsTemplateRegistry();
        registry.Register(new EqsQueryTemplate
        {
            BlueprintId   = 232u,
            Generator     = new DummyGenerator(),
            MaxCandidates = 4,
        });
        harness.SimHost.World!.SetSingletonManaged<IEqsTemplateRegistry>(registry);

        // Attach EqsSensor with all context slots explicitly null.
        harness.Cgf!.World!.AddComponent(cgfEntity, new EqsSensor
        {
            BlueprintId  = 232u,
            Epoch        = 1u,
            SearchRadius = 50f,
            ContextSlot0 = Entity.Null,
            ContextSlot1 = Entity.Null,
            ContextSlot2 = Entity.Null,
        });

        // Pump until EqsSensor appears on Muscle.
        bool sensorReady = harness.PumpUntil(
            () =>
            {
                harness.SimHost.TestHook_EntityMap.TryGetEntity(networkId, out Entity muscleEntity);
                if (muscleEntity.IsNull) return false;
                return harness.SimHost.World!.HasComponent<EqsSensor>(muscleEntity);
            },
            timeoutFrames: 2000);
        Assert.True(sensorReady, "Muscle ghost EqsSensor must appear after DDS replication.");

        harness.SimHost.TestHook_EntityMap.TryGetEntity(networkId, out Entity muscleEntity);
        ref readonly var s = ref harness.SimHost.World!.GetComponentRO<EqsSensor>(muscleEntity);
        Assert.True(s.ContextSlot0.IsNull, "ContextSlot0 must be null after round-trip");
        Assert.True(s.ContextSlot1.IsNull, "ContextSlot1 must be null after round-trip");
        Assert.True(s.ContextSlot2.IsNull, "ContextSlot2 must be null after round-trip");
    }

    // ── T-CS3: Unresolved network ID stays null (unit test) ───────────────────

    /// <summary>
    /// T-CS3: <see cref="EqsSensorConfigIngressTranslator.ResolveSlot"/> must return
    /// <see cref="Entity.Null"/> when the network ID is not registered in the entity map.
    /// No exception must be thrown.
    /// </summary>
    [Fact]
    public void ContextSlot_UnresolvedEntity_StaysNull()
    {
        // Empty entity map: no entity registered for network ID 999.
        var entityMap  = new NetworkEntityMap();
        var translator = new EqsSensorConfigIngressTranslator(participant: null, entityMap: entityMap);

        Entity result = translator.ResolveSlot(999L);

        Assert.True(result.IsNull, "Unresolved network ID must resolve to Entity.Null");
    }

    // ── T-CS4: Epoch increments when context slot changes (unit test) ─────────

    /// <summary>
    /// T-CS4: <see cref="EqsLifecycleNodes.Action_MaintainEqsSensor"/> must increment
    /// <see cref="EqsSensor.Epoch"/> exactly once when <c>ContextSlot1</c> changes,
    /// and must not increment when the slot stays the same.
    /// </summary>
    [Fact]
    public void MaintainEqsSensor_ContextSlotChange_IncrementsEpoch()
    {
        var repo = new EntityRepository();
        SimHostComponentRegistry.RegisterAll(repo);
        try
        {
            var entityA = new Entity(1, 1);
            var entityB = new Entity(2, 1);

            var entity = repo.CreateEntity();
            var p      = new EqsParams { BlueprintId = 1u, SearchRadius = 50f, ContextSlot1 = entityA };
            var state  = new BehaviorTreeState();
            var ctx    = new BTreeContext { Self = entity, World = repo };

            // First tick: sensor added with Epoch=1 and ContextSlot1=entityA.
            EqsLifecycleNodes.Action_MaintainEqsSensor(ref p, ref state, ref ctx);
            Assert.Equal(1u, repo.GetComponentRO<EqsSensor>(entity).Epoch);
            Assert.Equal(entityA, repo.GetComponentRO<EqsSensor>(entity).ContextSlot1);

            // Second tick: same params, Epoch stays at 1.
            EqsLifecycleNodes.Action_MaintainEqsSensor(ref p, ref state, ref ctx);
            Assert.Equal(1u, repo.GetComponentRO<EqsSensor>(entity).Epoch);

            // Third tick: change ContextSlot1 to entityB. Epoch must increment to 2.
            p.ContextSlot1 = entityB;
            EqsLifecycleNodes.Action_MaintainEqsSensor(ref p, ref state, ref ctx);
            Assert.Equal(2u, repo.GetComponentRO<EqsSensor>(entity).Epoch);
            Assert.Equal(entityB, repo.GetComponentRO<EqsSensor>(entity).ContextSlot1);

            // Fourth tick: same params again. Epoch stays at 2.
            EqsLifecycleNodes.Action_MaintainEqsSensor(ref p, ref state, ref ctx);
            Assert.Equal(2u, repo.GetComponentRO<EqsSensor>(entity).Epoch);
        }
        finally
        {
            repo.Dispose();
        }
    }

    // ── T-CS5: CheapLosTest reads position from ContextSlot1 ─────────────────

    /// <summary>
    /// T-CS5: <see cref="CheapLineOfSightTest"/> must pass the <see cref="SimTransform"/>
    /// position of the <c>ContextSlot1</c> entity as the threat position to
    /// <see cref="ILosService.HasCheapLineOfSight"/>.
    /// When the service reports all candidates are exposed (no cover), all are rejected and
    /// the buffer is empty.
    /// </summary>
    [Fact(Timeout = 6_000)]
    public void CheapLosTest_ReadsPositionFromContextSlot()
    {
        const uint blueprintId = 2350001u;

        // LOS service: exposes all candidates when threat is at x~=20.
        var los = new PositionCaptureLosService();
        var provider = new ManualCoverProvider(new[]
        {
            new CoverPoint { PositionX = 1f, PositionY = 0f, Quality = 1f },
            new CoverPoint { PositionX = 2f, PositionY = 0f, Quality = 1f },
            new CoverPoint { PositionX = 3f, PositionY = 0f, Quality = 1f },
        });

        _harness.Repo.SetSingletonManaged<ICoverProvider>(provider);

        var registry = new SimpleEqsTemplateRegistry();
        registry.Register(new EqsQueryTemplate
        {
            BlueprintId   = blueprintId,
            Generator     = new CoverPointsGenerator(),
            FilterCheap   = new IEqsTest[] { new CheapLineOfSightTest(los) },
            MaxCandidates = 8,
        });
        _harness.Repo.SetSingletonManaged<IEqsTemplateRegistry>(registry);

        // Observer at origin.
        var observer = _harness.Repo.CreateEntity();
        _harness.Repo.AddComponent(observer, new SimTransform
        {
            Position = Vector3.Zero, Rotation = Quaternion.Identity,
        });

        // TargetMemory: one threat with score > ThreatThreshold=0 to pass the gate.
        var mem = new TargetMemory();
        unsafe
        {
            mem.Count          = 1;
            mem.ThreatScores[0] = 100f;
            mem.PositionsX[0]   = 99f; // irrelevant -- position is read from slot entity
            mem.PositionsY[0]   = 0f;
        }
        _harness.Repo.AddComponent(observer, mem);

        // Context slot 1 entity at x=20: the LOS service will report "exposed" for this position.
        var targetEntity = _harness.Repo.CreateEntity();
        _harness.Repo.AddComponent(targetEntity, new SimTransform
        {
            Position = new Vector3(20f, 0f, 0f), Rotation = Quaternion.Identity,
        });

        _harness.Repo.AddComponent(observer, new EqsSensor
        {
            BlueprintId     = blueprintId,
            Epoch           = 1,
            SearchRadius    = 25f,
            ThreatThreshold = 0f,
            ContextSlot1    = targetEntity,
        });
        _harness.Repo.AddComponent(observer, new NetworkIdentity { Value = 8350L });

        // Pump until buffer is ready (even if empty).
        bool ready = _harness.PumpUntil(
            () => _harness.Repo.HasComponent<EqsCognitiveBuffer>(observer)
               && _harness.Repo.GetComponentRO<EqsCognitiveBuffer>(observer).IsReady,
            timeoutMs: 5000);
        Assert.True(ready, "CognitiveBuffer must become ready within timeout");

        // All 3 candidates are exposed (LOS returns true) -> all rejected -> buffer empty.
        ref readonly var buffer = ref _harness.Repo.GetComponentRO<EqsCognitiveBuffer>(observer);
        Assert.Equal(0, buffer.Count);

        // The LOS service must have been called with to.X near 20 (from the slot SimTransform).
        Assert.True(
            MathF.Abs(los.LastToX - 20f) < 1f,
            $"LOS service must receive threat position from ContextSlot1 (x~=20), got {los.LastToX}");
    }

    // ── T-CS6: Null slot bypasses CheapLosTest ────────────────────────────────

    /// <summary>
    /// T-CS6: When <c>ContextSlot1</c> is <see cref="Entity.Null"/>,
    /// <see cref="CheapLineOfSightTest"/> must bypass evaluation and all candidates
    /// must survive (buffer count > 0).
    /// </summary>
    [Fact(Timeout = 6_000)]
    public void CheapLosTest_NullSlot_Bypasses()
    {
        const uint blueprintId = 2360001u;

        // LOS service: would expose everything if it were called.
        var los = new PositionCaptureLosService();
        var provider = new ManualCoverProvider(new[]
        {
            new CoverPoint { PositionX = 1f, PositionY = 0f, Quality = 1f },
            new CoverPoint { PositionX = 2f, PositionY = 0f, Quality = 1f },
            new CoverPoint { PositionX = 3f, PositionY = 0f, Quality = 1f },
        });

        _harness.Repo.SetSingletonManaged<ICoverProvider>(provider);

        var registry = new SimpleEqsTemplateRegistry();
        registry.Register(new EqsQueryTemplate
        {
            BlueprintId   = blueprintId,
            Generator     = new CoverPointsGenerator(),
            FilterCheap   = new IEqsTest[] { new CheapLineOfSightTest(los) },
            MaxCandidates = 8,
        });
        _harness.Repo.SetSingletonManaged<IEqsTemplateRegistry>(registry);

        var observer = _harness.Repo.CreateEntity();
        _harness.Repo.AddComponent(observer, new SimTransform
        {
            Position = Vector3.Zero, Rotation = Quaternion.Identity,
        });

        // TargetMemory with a high-scoring threat (score > threshold=0) to rule out
        // the threshold bypass and verify it is the null slot that triggers bypass.
        var mem = new TargetMemory();
        unsafe
        {
            mem.Count          = 1;
            mem.ThreatScores[0] = 100f;
            mem.PositionsX[0]   = 20f;
            mem.PositionsY[0]   = 0f;
        }
        _harness.Repo.AddComponent(observer, mem);

        // ContextSlot1 is null: bypass must fire before touching TargetMemory.
        _harness.Repo.AddComponent(observer, new EqsSensor
        {
            BlueprintId     = blueprintId,
            Epoch           = 1,
            SearchRadius    = 25f,
            ThreatThreshold = 0f,
            ContextSlot1    = Entity.Null,
        });
        _harness.Repo.AddComponent(observer, new NetworkIdentity { Value = 8360L });

        bool ready = _harness.PumpUntil(
            () => _harness.Repo.HasComponent<EqsCognitiveBuffer>(observer)
               && _harness.Repo.GetComponentRO<EqsCognitiveBuffer>(observer).IsReady,
            timeoutMs: 5000);
        Assert.True(ready, "CognitiveBuffer must become ready within timeout");

        // Null slot -> bypass -> all 3 candidates survive.
        ref readonly var buffer = ref _harness.Repo.GetComponentRO<EqsCognitiveBuffer>(observer);
        Assert.True(buffer.Count > 0, "All candidates must survive when ContextSlot1 is null");

        // LOS service must NOT have been called (bypass fires before slot lookup).
        Assert.True(float.IsNaN(los.LastToX), "LOS service must not be called when slot is null");
    }

    // ── T-CS7: AccurateLosTest reads position from ContextSlot1 ──────────────

    /// <summary>
    /// T-CS7: <see cref="AccurateLineOfSightTest"/> must use the <see cref="SimTransform"/>
    /// position of the <c>ContextSlot1</c> entity as the raycast end point,
    /// rather than <c>TargetMemory.PositionsX[0]</c>.
    /// </summary>
    [Fact(Timeout = 8_000)]
    public void AccurateLosTest_ReadsPositionFromContextSlot()
    {
        const uint blueprintId = 2370001u;

        var registry = new SimpleEqsTemplateRegistry();
        registry.Register(new EqsQueryTemplate
        {
            BlueprintId    = blueprintId,
            Generator      = new NavmeshSamplesGenerator(),
            ScoreExpensive = new IEqsTest[] { new AccurateLineOfSightTest() },
            MaxCandidates  = 4,
        });
        _harness.Repo.SetSingletonManaged<IEqsTemplateRegistry>(registry);
        _harness.Repo.SetSingletonManaged<INavmeshProvider>(new StubNavmeshProvider());

        // Budget: 1 raycast per solver tick -- forces slow convergence so we can observe End.
        _harness.Repo.SetSingletonUnmanaged(new EqsSolverGlobalState
        {
            MaxAccurateRaycastsPerSolverTick = 1,
            AccurateRaysSubmittedThisTick    = 0,
        });

        // Observer at origin.
        var observer = _harness.Repo.CreateEntity();
        _harness.Repo.AddComponent(observer, new SimTransform
        {
            Position = Vector3.Zero, Rotation = Quaternion.Identity,
        });

        // TargetMemory: threat at x=100 (old code would use this; new code must NOT).
        var mem = new TargetMemory();
        unsafe
        {
            mem.Count          = 1;
            mem.ThreatScores[0] = 100f;
            mem.PositionsX[0]   = 100f; // decoy -- must not appear in raycast End
            mem.PositionsY[0]   = 0f;
        }
        _harness.Repo.AddComponent(observer, mem);

        // Context slot 1 entity at x=30: new code must raycast to this position.
        var targetEntity = _harness.Repo.CreateEntity();
        _harness.Repo.AddComponent(targetEntity, new SimTransform
        {
            Position = new Vector3(30f, 0f, 0f), Rotation = Quaternion.Identity,
        });

        _harness.Repo.AddComponent(observer, new EqsSensor
        {
            BlueprintId     = blueprintId,
            Epoch           = 1,
            SearchRadius    = 50f,
            ThreatThreshold = 0f,
            ContextSlot1    = targetEntity,
        });
        _harness.Repo.AddComponent(observer, new NetworkIdentity { Value = 8370L });

        // Pump until the mock solver captures at least one raycast request.
        bool raySubmitted = _harness.PumpUntil(
            () => _mockSolver.RaycastSubmitted,
            timeoutMs: 7000);
        Assert.True(raySubmitted, "AccurateLineOfSightTest must submit at least one raycast");

        // End.X must come from ContextSlot1's SimTransform (x=30), not TargetMemory (x=100).
        Assert.Equal(30f, _mockSolver.LastRaycastEnd.X);
    }

    // ── DummyGenerator: yields one zero-score positional candidate ────────────

    // Used by T-CS1/T-CS2 where we only test replication, not scoring.
    private sealed class DummyGenerator : IEqsGenerator
    {
        public int Generate(Entity observer, ref EqsSensor sensor,
            ISimulationView view, Span<EqsResult> candidates)
        {
            if (candidates.Length == 0) return 0;
            candidates[0] = new EqsResult { EntityId = 0L, PositionX = 0f, PositionY = 0f, Score = 0f };
            return 1;
        }
    }
}
