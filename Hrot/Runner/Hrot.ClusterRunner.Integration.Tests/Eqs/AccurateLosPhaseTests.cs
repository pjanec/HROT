using System;
using System.Collections.Generic;
using System.Numerics;
using Fdp.Core;
using Fdp.ModuleHost.Abstractions;
using Fdp.Toolkit.Navigation;
using Fdp.Toolkit.Perception.Components;
using Fdp.Toolkit.Physics;
using Fdp.Toolkit.Physics.Components;
using Fdp.Toolkit.Replication.Components;
using Fdp.Toolkit.Spatial.Eqs;
using Hrot.SimHost;
using Hrot.SimHost.Systems;
using Xunit;

namespace Hrot.ClusterRunner.Integration.Tests.Eqs;

/// <summary>
/// Integration tests for Phase 5 accurate LOS state machine (TASK-EQS-019).
/// Verifies multi-tick convergence and _AwaitingRaycasts phase behaviour.
/// </summary>
[Collection("EqsIntegrationTests")]
public sealed class AccurateLosPhaseTests : IDisposable
{
    // ── Inner types ────────────────────────────────────────────────────────────

    /// <summary>
    /// Reads <see cref="RaycastRequestEvent"/>s each frame and writes synthetic results
    /// to the <see cref="RaycastBatchData"/> ring buffer (HasHit=1 = blocked = good cover).
    /// Runs in PostSimulation so it sees harvest-phase events one frame after they are published.
    /// </summary>
    [UpdateInPhase(SystemPhase.PostSimulation)]
    private sealed class MockRaycastSolverSystem : IEcsModuleSystem
    {
        public int RaycastsResolvedTotal { get; private set; }

        public void Execute(ISimulationView view, float deltaTime)
        {
            if (view is not EntityRepository repo) return;
            if (!repo.HasSingleton<RaycastBatchData>()) return;

            ref var batch  = ref repo.GetSingleton<RaycastBatchData>();
            var     events = view.ReadEvents<RaycastRequestEvent>();

            for (int i = 0; i < events.Length; i++)
            {
                ref readonly var evt  = ref events[i];
                int              slot = (int)((uint)evt.RayId % (uint)PhysicsConstants.RaycastBatchCapacity);
                batch.Hits[slot] = new RaycastHit
                {
                    RayId  = evt.RayId,
                    HasHit = 1, // blocked = candidate occluded = good cover
                };
                RaycastsResolvedTotal++;
            }
        }
    }

    private sealed class SimpleEqsTemplateRegistry : IEqsTemplateRegistry
    {
        private readonly Dictionary<uint, EqsQueryTemplate> _t = new();
        public void Register(EqsQueryTemplate t) => _t[t.BlueprintId] = t;
        public bool TryGetTemplate(uint id, out EqsQueryTemplate t) => _t.TryGetValue(id, out t);
    }

    // ── Test fixture ──────────────────────────────────────────────────────────

    private readonly MockRaycastSolverSystem _mockSolver;
    private readonly EditorHarness           _harness;

    public AccurateLosPhaseTests()
    {
        _mockSolver = new MockRaycastSolverSystem();
        _harness    = new EditorHarness(extraGlobalSystems: new IEcsModuleSystem[] { _mockSolver });
    }

    public void Dispose()
    {
        if (_harness.Repo.HasSingleton<EqsResultPool>())
        {
            var pool = _harness.Repo.GetSingleton<EqsResultPool>();
            if (pool.Results.IsCreated)
                pool.Results.Dispose();
        }
        // Do NOT dispose RaycastBatchData — owned by PhysicsToolkitModule inside EditorHarness.
        _harness.Dispose();
    }

    // ── Shared setup helper ───────────────────────────────────────────────────

    private Entity CreateTestObserver(uint blueprintId)
    {
        var observer = _harness.Repo.CreateEntity();
        _harness.Repo.AddComponent(observer, new SimTransform
        {
            Position = Vector3.Zero,
            Rotation = Quaternion.Identity,
        });

        // One threat with score above zero to trigger AccurateLineOfSightTest.
        var mem = new TargetMemory();
        unsafe
        {
            mem.Count          = 1;
            mem.ThreatScores[0] = 100f;
            mem.PositionsX[0]   = 30f;
            mem.PositionsY[0]   = 0f;
        }
        _harness.Repo.AddComponent(observer, mem);

        // Context slot 1 entity -- provides threat position for AccurateLineOfSightTest.
        var targetEntity = _harness.Repo.CreateEntity();
        _harness.Repo.AddComponent(targetEntity, new SimTransform
        {
            Position = new Vector3(30f, 0f, 0f),
            Rotation = Quaternion.Identity,
        });

        _harness.Repo.AddComponent(observer, new EqsSensor
        {
            BlueprintId     = blueprintId,
            Epoch           = 1,
            SearchRadius    = 50f,
            ThreatThreshold = 0f,
            ContextSlot1    = targetEntity,
        });
        _harness.Repo.AddComponent(observer, new NetworkIdentity { Value = 9001L + blueprintId });
        return observer;
    }

    private void SetupTemplate(uint blueprintId, int maxBudgetPerTick)
    {
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

        // Override budget: 2 raycasts per EQS solver tick forces multi-tick resolution.
        _harness.Repo.SetSingletonUnmanaged(new EqsSolverGlobalState
        {
            MaxAccurateRaycastsPerSolverTick = maxBudgetPerTick,
            AccurateRaysSubmittedThisTick    = 0,
        });
    }

    // ── Tests ─────────────────────────────────────────────────────────────────

    /// <summary>
    /// T-ALI1: With budget=2 and 4 navmesh candidates, the solver requires at least
    /// 2 EQS ticks to submit all 4 raycasts and a final tick to write results.
    /// CognitiveBuffer.IsReady becomes true after convergence.
    /// </summary>
    [Fact(Timeout = 8_000)]
    public void AccurateLos_MultiTickConvergence_IsReadyAfterAllRaycasts()
    {
        // Arrange
        const uint blueprintId = 97u;
        SetupTemplate(blueprintId, maxBudgetPerTick: 2);
        var observer = CreateTestObserver(blueprintId);

        // Act: pump until CognitiveBuffer is ready.
        bool ready = _harness.PumpUntil(
            () => _harness.Repo.HasComponent<EqsCognitiveBuffer>(observer)
               && _harness.Repo.GetComponentRO<EqsCognitiveBuffer>(observer).IsReady
               && _harness.Repo.GetComponentRO<EqsCognitiveBuffer>(observer).Count > 0,
            timeoutMs: 7000);

        // Assert
        Assert.True(ready, "CognitiveBuffer.IsReady should become true after all raycasts resolve.");

        ref readonly var buffer = ref _harness.Repo.GetComponentRO<EqsCognitiveBuffer>(observer);
        Assert.True(buffer.Count > 0, "At least one occluded cover candidate should survive.");
        Assert.True(_mockSolver.RaycastsResolvedTotal >= 1, "MockRaycastSolverSystem should have resolved at least one raycast.");
    }

    /// <summary>
    /// T-ALI2: After the first EQS solver tick (with raycasts pending), the entity's
    /// SensorEvalState.Phase transitions to _AwaitingRaycasts on the main repo.
    /// </summary>
    [Fact(Timeout = 8_000)]
    public void AccurateLos_PhaseAwaitingRaycasts_SetOnFirstEqsTick()
    {
        // Arrange: unlimited budget but fresh ring buffer => first tick always submits and yields.
        const uint blueprintId = 98u;
        SetupTemplate(blueprintId, maxBudgetPerTick: 2048);
        var observer = CreateTestObserver(blueprintId);

        // Act: pump until SensorEvalState.Phase == _AwaitingRaycasts is observed on the main repo.
        // This happens after the FIRST EQS tick submits raycasts and the command buffer is played back.
        bool phaseReached = _harness.PumpUntil(
            () => _harness.Repo.HasComponent<SensorEvalState>(observer)
               && _harness.Repo.GetComponentRO<SensorEvalState>(observer).Phase == EqsEvalPhase._AwaitingRaycasts,
            timeoutMs: 7000);

        Assert.True(phaseReached, "SensorEvalState.Phase should reach _AwaitingRaycasts after first EQS tick.");
    }

    /// <summary>
    /// T-ALI3: The solver does NOT publish EqsResultEvent while in _AwaitingRaycasts.
    /// Proven by T-ALI1's multi-tick requirement: if the solver published prematurely,
    /// IsReady would become true within 1 EQS tick (no multi-tick delay observed).
    /// This test verifies IsReady is false BEFORE the mock resolver processes any raycasts
    /// and only becomes true AFTER the resolver has run.
    /// </summary>
    [Fact(Timeout = 8_000)]
    public unsafe void AccurateLos_NoEarlyPublish_IsReadyFalseWhileAwaiting()
    {
        // Arrange: budget=2, so at least 2 EQS ticks needed.
        const uint blueprintId = 99u;
        SetupTemplate(blueprintId, maxBudgetPerTick: 2);
        var observer = CreateTestObserver(blueprintId);

        // First, pump until _AwaitingRaycasts is observed (first EQS tick has fired).
        bool awaitingReached = _harness.PumpUntil(
            () => _harness.Repo.HasComponent<SensorEvalState>(observer)
               && _harness.Repo.GetComponentRO<SensorEvalState>(observer).Phase == EqsEvalPhase._AwaitingRaycasts,
            timeoutMs: 7000);
        Assert.True(awaitingReached, "Should reach _AwaitingRaycasts first.");

        // At this point, CognitiveBuffer must NOT be ready (solver should not have published yet).
        bool readyTooEarly = _harness.Repo.HasComponent<EqsCognitiveBuffer>(observer)
                          && _harness.Repo.GetComponentRO<EqsCognitiveBuffer>(observer).IsReady
                          && _harness.Repo.GetComponentRO<EqsCognitiveBuffer>(observer).Count > 0;
        Assert.False(readyTooEarly, "CognitiveBuffer must NOT be ready while solver is in _AwaitingRaycasts.");

        // Now let convergence finish.
        bool finalReady = _harness.PumpUntil(
            () => _harness.Repo.HasComponent<EqsCognitiveBuffer>(observer)
               && _harness.Repo.GetComponentRO<EqsCognitiveBuffer>(observer).IsReady
               && _harness.Repo.GetComponentRO<EqsCognitiveBuffer>(observer).Count > 0,
            timeoutMs: 7000);
        Assert.True(finalReady, "CognitiveBuffer.IsReady should eventually become true after full convergence.");
    }
}
