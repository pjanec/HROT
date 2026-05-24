using System;
using System.Collections.Generic;
using System.Threading;
using Fdp.Core;
using Fdp.ModuleHost.Abstractions;
using Fdp.Toolkit.Spatial.Eqs;
using Fdp.Toolkit.Spatial.Eqs.Topics;
using Hrot.Map.Common;
using Xunit;

namespace Hrot.ClusterRunner.Integration.Tests.Eqs;

/// <summary>
/// Integration tests for the EQS distributed pipeline (TASK-EQS-023 distributed leg,
/// TASK-EQS-027, TASK-EQS-028).
///
/// <para>Domain range: 201-210 (above EqsTranslatorTests 71-79 and EqsRoundTripTests 92-95).</para>
///
/// <list type="number">
///   <item>T-DIS1 (EQS-023) -- Distributed round-trip: solver runs on Muscle, result populates Brain.</item>
///   <item>T-DIS2 (EQS-027) -- Stale epoch results are silently rejected by EqsResultUpdateSystem.</item>
///   <item>T-DIS3 (EQS-028) -- Mid-evaluation abort: sensor removal replicates without crashing.</item>
/// </list>
/// </summary>
[Collection("EqsIntegrationTests")]
public sealed class EqsDistributedTests
{
    private static int _domainCounter = 200;

    // Simple in-memory template registry used by all tests.
    private sealed class SimpleEqsTemplateRegistry : IEqsTemplateRegistry
    {
        private readonly Dictionary<uint, EqsQueryTemplate> _t = new();
        public void Register(EqsQueryTemplate t) => _t[t.BlueprintId] = t;
        public bool TryGetTemplate(uint id, out EqsQueryTemplate t) => _t.TryGetValue(id, out t);
    }

    // Mock generator that yields a different number of candidates based on SearchRadius.
    // SearchRadius <= 10f => 1 candidate; SearchRadius > 10f => 2 candidates.
    private sealed class DynamicRadiusGeneratorMock : IEqsGenerator
    {
        public int Generate(Entity observer, ref EqsSensor sensor,
            ISimulationView view, Span<EqsResult> candidates)
        {
            int count = sensor.SearchRadius <= 10f ? 1 : 2;
            count = Math.Min(count, candidates.Length);
            for (int i = 0; i < count; i++)
                candidates[i] = new EqsResult { EntityId = 0L, PositionX = (float)i, PositionY = 0f };
            return count;
        }
    }

    /// <summary>
    /// T-DIS1 (EQS-023): Verifies the full distributed round-trip.  The EqsSensor is attached to
    /// the Brain (CGF) entity, replicates to the Muscle (SimHost) via DDS, the Muscle solver
    /// evaluates cover candidates, and the result is bridged back to the Brain EqsCognitiveBuffer.
    /// </summary>
    [Fact(Timeout = 30_000)]
    public void Eqs_DistributedTopology_EvaluatesOnMuscleAndPopulatesBrain()
    {
        int domainId = Interlocked.Increment(ref _domainCounter);
        using var harness = new HrotRunnerHarness("simhost,cgf", domainId);

        // Register template on the Muscle (SimHost) world.
        var registry = new SimpleEqsTemplateRegistry();
        registry.Register(new EqsQueryTemplate
        {
            BlueprintId   = 200u,
            Generator     = new DynamicRadiusGeneratorMock(),
            MaxCandidates = 8,
        });
        harness.SimHost.World!.SetSingletonManaged<IEqsTemplateRegistry>(registry);

        // Spawn entity with split authority: Brain owns cognition, Muscle owns kinematics.
        long networkId = harness.Cgf!.TestHook_SpawnEntityWithSplitAuthority(
            TkbEntityTypes.Tank_M1Abrams, muscleNodeId: 1);

        // Wait for the Muscle ghost entity to appear.
        bool entityReady = harness.PumpUntil(
            () => harness.SimHost.TestHook_EntityMap.TryGetEntity(networkId, out _),
            timeoutFrames: 2000);
        Assert.True(entityReady, "Muscle ghost entity must appear within timeout.");

        // Look up the corresponding Brain entity and attach an EqsSensor.
        harness.Cgf!.GhostEntityMap!.TryGetEntity(networkId, out Entity cgfEntity);
        harness.Cgf!.World!.AddComponent(cgfEntity, new EqsSensor
        {
            BlueprintId  = 200u,
            Epoch        = 1u,
            SearchRadius = 50f,
        });

        // Pump until the Brain EqsCognitiveBuffer is populated with at least one candidate.
        bool bufferReady = harness.PumpUntil(() =>
        {
            var world = harness.Cgf!.World;
            if (world == null) return false;
            if (!world.HasComponent<EqsCognitiveBuffer>(cgfEntity)) return false;
            ref readonly var buf = ref world.GetComponentRO<EqsCognitiveBuffer>(cgfEntity);
            return buf.IsReady && buf.Count > 0;
        }, timeoutFrames: 2000);

        Assert.True(bufferReady, "Brain EqsCognitiveBuffer must be ready with at least one candidate.");

        ref readonly var buffer = ref harness.Cgf!.World!.GetComponentRO<EqsCognitiveBuffer>(cgfEntity);
        Assert.True(buffer.Count > 0, "Buffer must contain at least one positional candidate.");
        // Cover-point candidates are positional (EntityId == 0).
        Assert.Equal(0L, buffer.GetTop().EntityId);
    }

    /// <summary>
    /// T-DIS2 (EQS-027): Verifies that stale epoch results (Epoch N-1 arriving after the sensor
    /// has advanced to Epoch N) are silently rejected by EqsResultUpdateSystem and do not corrupt
    /// the EqsCognitiveBuffer.
    /// </summary>
    [Fact(Timeout = 30_000)]
    public void Eqs_DistributedTopology_RejectsStaleEpochResults()
    {
        int domainId = Interlocked.Increment(ref _domainCounter);
        using var harness = new HrotRunnerHarness("simhost,cgf", domainId);

        // Register DynamicRadiusGeneratorMock template on the Muscle world.
        var registry = new SimpleEqsTemplateRegistry();
        registry.Register(new EqsQueryTemplate
        {
            BlueprintId   = 201u,
            Generator     = new DynamicRadiusGeneratorMock(),
            MaxCandidates = 8,
        });
        harness.SimHost.World!.SetSingletonManaged<IEqsTemplateRegistry>(registry);

        // Spawn entity with split authority.
        long networkId = harness.Cgf!.TestHook_SpawnEntityWithSplitAuthority(
            TkbEntityTypes.Tank_M1Abrams, muscleNodeId: 1);

        bool entityReady = harness.PumpUntil(
            () => harness.SimHost.TestHook_EntityMap.TryGetEntity(networkId, out _),
            timeoutFrames: 2000);
        Assert.True(entityReady, "Muscle ghost entity must appear within timeout.");

        harness.Cgf!.GhostEntityMap!.TryGetEntity(networkId, out Entity cgfEntity);

        // Add EqsSensor epoch=1, radius=10 (DynamicRadiusGeneratorMock yields 1 candidate).
        harness.Cgf!.World!.AddComponent(cgfEntity, new EqsSensor
        {
            BlueprintId  = 201u,
            Epoch        = 1u,
            SearchRadius = 10f,
        });

        // Wait for epoch-1 result: Count == 1.
        bool epoch1Ready = harness.PumpUntil(() =>
        {
            var world = harness.Cgf!.World;
            if (world == null) return false;
            if (!world.HasComponent<EqsCognitiveBuffer>(cgfEntity)) return false;
            ref readonly var buf = ref world.GetComponentRO<EqsCognitiveBuffer>(cgfEntity);
            return buf.IsReady && buf.Count == 1;
        }, timeoutFrames: 2000);
        Assert.True(epoch1Ready, "Brain buffer must show Count == 1 for epoch-1 result.");

        // Advance sensor to epoch=2: remove and re-add so the egress translator sees
        // a new first-publish (reliable DDS topics do not re-publish on mutation alone).
        harness.Cgf!.World!.RemoveComponent<EqsSensor>(cgfEntity);
        // Pump a few frames so ScanAndPublish emits NOT_ALIVE_DISPOSED and clears the
        // published-tick record, enabling a fresh first-publish when the sensor is re-added.
        harness.PumpFrames(5);
        harness.Cgf!.World!.AddComponent(cgfEntity, new EqsSensor
        {
            BlueprintId  = 201u,
            Epoch        = 2u,
            SearchRadius = 20f,
        });

        // Inject a stale EqsResultUpdateEvent (epoch=1, 99 fake results) directly on the Brain bus.
        var staleResults = new List<EqsResultEntry>();
        for (int i = 0; i < 99; i++)
            staleResults.Add(new EqsResultEntry { EntityId = 0L });
        harness.Cgf!.World!.Bus.PublishManaged(new EqsResultUpdateEvent
        {
            Observer    = cgfEntity,
            Epoch       = 1u,
            RefreshTick = 1u,
            Results     = staleResults,
        });

        // Pump 2 frames -- the stale event must be rejected without updating the buffer.
        harness.PumpFrames(2);

        ref readonly var bufAfterStale = ref harness.Cgf!.World!.GetComponentRO<EqsCognitiveBuffer>(cgfEntity);
        Assert.NotEqual(99, bufAfterStale.Count);

        // Pump until the genuine epoch-2 result arrives (Count == 2).
        bool epoch2Ready = harness.PumpUntil(() =>
        {
            var world = harness.Cgf!.World;
            if (world == null) return false;
            if (!world.HasComponent<EqsCognitiveBuffer>(cgfEntity)) return false;
            ref readonly var buf = ref world.GetComponentRO<EqsCognitiveBuffer>(cgfEntity);
            return buf.IsReady && buf.Count == 2;
        }, timeoutFrames: 2000);
        Assert.True(epoch2Ready, "Brain buffer must show Count == 2 for genuine epoch-2 result.");
    }

    /// <summary>
    /// T-DIS3 (EQS-028): Verifies that removing an EqsSensor from the Brain entity mid-evaluation
    /// does not crash the solver.  Simplified path: add sensor, wait for replication to Muscle,
    /// remove sensor from Brain, verify the removal propagates without exception.
    /// </summary>
    [Fact(Timeout = 30_000)]
    public void Eqs_MidEvaluationAbort_SilentlyDropsQueryWithoutLeaking()
    {
        int domainId = Interlocked.Increment(ref _domainCounter);
        using var harness = new HrotRunnerHarness("simhost,cgf", domainId);

        // Register a simple template on Muscle.
        var registry = new SimpleEqsTemplateRegistry();
        registry.Register(new EqsQueryTemplate
        {
            BlueprintId   = 202u,
            Generator     = new DynamicRadiusGeneratorMock(),
            MaxCandidates = 8,
        });
        harness.SimHost.World!.SetSingletonManaged<IEqsTemplateRegistry>(registry);

        // Spawn entity with split authority.
        long networkId = harness.Cgf!.TestHook_SpawnEntityWithSplitAuthority(
            TkbEntityTypes.Tank_M1Abrams, muscleNodeId: 1);

        bool entityReady = harness.PumpUntil(
            () => harness.SimHost.TestHook_EntityMap.TryGetEntity(networkId, out _),
            timeoutFrames: 2000);
        Assert.True(entityReady, "Muscle ghost entity must appear within timeout.");

        harness.Cgf!.GhostEntityMap!.TryGetEntity(networkId, out Entity cgfEntity);

        // Add EqsSensor to Brain -- triggers DDS replication to Muscle.
        harness.Cgf!.World!.AddComponent(cgfEntity, new EqsSensor
        {
            BlueprintId  = 202u,
            Epoch        = 1u,
            SearchRadius = 20f,
        });

        // Wait for EqsSensor to replicate to the Muscle entity.
        bool sensorReplicated = harness.PumpUntil(() =>
        {
            if (!harness.SimHost.TestHook_EntityMap.TryGetEntity(networkId, out Entity simEntity))
                return false;
            return harness.SimHost.World!.HasComponent<EqsSensor>(simEntity);
        }, timeoutFrames: 2000);
        Assert.True(sensorReplicated, "EqsSensor must replicate from Brain to Muscle.");

        // Remove EqsSensor from Brain (simulates BTree deactivation / abort).
        harness.Cgf!.World!.RemoveComponent<EqsSensor>(cgfEntity);

        // Wait for the removal to propagate to Muscle (or entity gone).
        bool sensorRemoved = harness.PumpUntil(() =>
        {
            if (!harness.SimHost.TestHook_EntityMap.TryGetEntity(networkId, out Entity simEntity))
                return true; // entity gone -- cleanup complete
            if (!harness.SimHost.World!.IsAlive(simEntity))
                return true;
            return !harness.SimHost.World.HasComponent<EqsSensor>(simEntity);
        }, timeoutFrames: 2000);
        Assert.True(sensorRemoved, "EqsSensor removal must propagate to Muscle without crash.");

        // Pump additional frames to confirm the solver handles the absent sensor without exception.
        harness.PumpFrames(20);
        // Reaching here without exception is the primary success condition for EQS-028.
    }
}
