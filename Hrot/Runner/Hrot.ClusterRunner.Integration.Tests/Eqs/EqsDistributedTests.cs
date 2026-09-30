using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Threading;
using Fdp.Core;
using Fdp.Toolkit.Combat.Components;
using Fdp.Toolkit.Replication.Components;
using Fdp.ModuleHost.Abstractions;
using Fdp.Toolkit.Spatial.Eqs;
using Fdp.Toolkit.Spatial.Eqs.Topics;
using Hrot.IG.Components;
using Hrot.Map.Common;
using Hrot.SimHost.Systems;
using Xunit;
using CoreGeoPoint = Hrot.Core.Mission.GeoPoint;

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
///   <item>T-DIS4 -- The area query in EQS 1.3 (<see cref="EntitiesOfForceInArea"/>) returns the SAME
///         targets as the old AreaQuery pipeline, both computed on the Muscle and read on the Brain,
///         and they agree again after a target leaves the area.</item>
///   <item>T-DIS5 -- A later sensor parameter change (a new area) reaches the Muscle without the
///         remove/re-add workaround T-DIS2 needs.</item>
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

    // ── T-DIS4 / T-DIS5: the area query inside EQS 1.3, across hosts ─────────────
    //
    // 📄 docs/designs/eqs-2/EQS_Design_v1.3_final.md §17. ⭐ NO test registry is installed: the Muscle
    //    answers from the PRODUCTION registry (EqsTemplateRegistry.InstallDefault in the SimHost
    //    PerceptionSolver capability) — CE-465's red-proof is that this rail times out without it.

    private const int AreaChildIndex = 7;

    /// <summary>
    /// T-DIS4: one area polygon on the Muscle, targets inside / outside / friendly / wrecked. The old
    /// AreaQuery (request → Muscle solver → Brain ring) and the EQS sensor (child of a networked
    /// commander, as the blueprint SpawnEqsSensor node makes it) must report the SAME network ids —
    /// exactly the two live hostiles inside — and must agree again after one of them leaves the area.
    /// </summary>
    [Fact(Timeout = 90_000)]
    public void AreaQuery_And_EqsEntitiesOfForceInArea_ReportTheSameTargetsAcrossHosts()
    {
        int domainId = Interlocked.Increment(ref _domainCounter);
        using var harness = new HrotRunnerHarness("simhost,cgf", domainId);
        var sim = harness.SimHost.World!;

        // ── Muscle-owned world: the area and the targets (SimHost simulates them) ──
        long areaNet     = SpawnOnMuscle(harness, TkbEntityTypes.TacGraphic_Area);
        long hostileA    = SpawnOnMuscle(harness, TkbEntityTypes.Tank_T72);
        long hostileB    = SpawnOnMuscle(harness, TkbEntityTypes.Tank_T72);
        long hostileOut  = SpawnOnMuscle(harness, TkbEntityTypes.Tank_T72);
        long friendlyIn  = SpawnOnMuscle(harness, TkbEntityTypes.Tank_M1Abrams);
        long wreckIn     = SpawnOnMuscle(harness, TkbEntityTypes.Tank_T72);

        // ── Brain-owned commander (every parent has a NetworkIdentity) ──
        long commanderNet = harness.Cgf!.TestHook_SpawnEntityWithSplitAuthority(
            TkbEntityTypes.Tank_M1Abrams, muscleNodeId: 1);

        long[] everything = { areaNet, hostileA, hostileB, hostileOut, friendlyIn, wreckIn, commanderNet };
        Assert.True(harness.PumpUntil(() => everything.All(n =>
                harness.SimHost.TestHook_EntityMap.TryGetEntity(n, out _)
             && harness.Cgf!.GhostEntityMap!.TryGetEntity(n, out _)), timeoutFrames: 3000),
            "All entities must exist on both the Muscle and the Brain.");

        // Area: square ±50 m around (100, 100); points are RELATIVE to the area's SimTransform.
        Entity simArea = SimEntity(harness, areaNet);
        Place(sim, simArea, 100f, 100f, ForceId.Neutral);
        sim.SetManagedComponent(simArea, new EditablePolyline
        {
            Points = new List<Vector2> { new(-50, -50), new(50, -50), new(50, 50), new(-50, 50) },
        });

        Place(sim, SimEntity(harness, hostileA),   110f, 120f, ForceId.Hostile);
        Place(sim, SimEntity(harness, hostileB),    80f,  90f, ForceId.Hostile);
        Place(sim, SimEntity(harness, hostileOut), 300f, 300f, ForceId.Hostile);
        Place(sim, SimEntity(harness, friendlyIn),  95f, 105f, ForceId.Friend);
        Place(sim, SimEntity(harness, wreckIn),    120f,  80f, ForceId.Hostile, health: 0f);
        Assert.True(harness.PumpUntil(() => ForceIs(harness, ForceId.Hostile, hostileA, hostileB, hostileOut, wreckIn)
                                          && ForceIs(harness, ForceId.Friend, friendlyIn), timeoutFrames: 2000),
            $"Forces must settle on the Muscle after republishing. {Describe(harness, everything)}");
        // The owner's change must STICK (not be overwritten by its own loopback sample) and reach the Brain.
        harness.PumpFrames(60);
        Assert.True(ForceIs(harness, ForceId.Hostile, hostileA, hostileB, hostileOut, wreckIn),
            $"An owner's force change must not revert. {Describe(harness, everything)}");

        var cgf = harness.Cgf!.World!;
        harness.Cgf!.GhostEntityMap!.TryGetEntity(areaNet, out Entity cgfArea);
        harness.Cgf!.GhostEntityMap!.TryGetEntity(commanderNet, out Entity cgfCommander);

        // ── EQS 1.3: a child sensor of the commander, as the blueprint node spawns it ──
        Entity sensor = cgf.CreateEntity();
        cgf.AddComponent(sensor, new PartMetadata { ParentEntity = cgfCommander, InstanceId = AreaChildIndex });
        cgf.AddComponent(sensor, EntitiesOfForceInArea.SensorFor(cgfArea, ForceId.Hostile));
        cgf.AddComponent(sensor, new EqsCognitiveBuffer());

        var expected = new SortedSet<long> { hostileA, hostileB };

        // Stage 1: the Muscle holds the carrier, with the area resolved, and a production registry.
        Assert.True(harness.PumpUntil(() => MuscleCarrierArea(harness, commanderNet) == SimEntity(harness, areaNet),
                timeoutFrames: 3000),
            $"The sensor carrier must reach the Muscle with ContextSlot1 = the area. Carrier slot: {MuscleCarrierArea(harness, commanderNet)}");
        Assert.True(sim.HasSingletonManaged<IEqsTemplateRegistry>()
                 && sim.GetSingletonManaged<IEqsTemplateRegistry>()!.TryGetTemplate(EntitiesOfForceInArea.BlueprintId, out _),
            "The Muscle must answer from the production registry (CE-465).");

        // Stage 2: the old AreaQuery answers the same question.
        var oldFirst = AreaQueryTargets(harness, cgfCommander, cgfArea);
        Assert.True(expected.SetEquals(oldFirst),
            $"old AreaQuery: [{string.Join(",", oldFirst)}]; muscle state: {Describe(harness, everything)}");

        // Stage 3: EQS reports the same set on the Brain.
        Assert.True(harness.PumpUntil(() => SameSet(EqsTargets(harness, sensor), expected), timeoutFrames: 3000),
            $"EQS must report exactly the two live hostiles inside the area. Got: [{string.Join(",", EqsTargets(harness, sensor))}]");
        var oldAnswer = AreaQueryTargets(harness, cgfCommander, cgfArea);
        Assert.Equal(expected, oldAnswer);

        // ⭐ The Brain-side buffer holds BRAIN-LOCAL entities (the split fix): each is alive here.
        ref readonly var buf = ref cgf.GetComponentRO<EqsCognitiveBuffer>(sensor);
        for (int i = 0; i < buf.Count; i++)
            Assert.True(cgf.IsAlive(new Entity((ulong)buf.GetSpanRO()[i].EntityId)),
                "EqsCognitiveBuffer.EntityId must be a Brain-local entity, not a network id.");

        // ── A target leaves the area: both must drop it ──
        Place(sim, SimEntity(harness, hostileB), 400f, -300f, ForceId.Hostile);
        var afterMove = new SortedSet<long> { hostileA };
        Assert.True(harness.PumpUntil(() => SameSet(EqsTargets(harness, sensor), afterMove), timeoutFrames: 3000),
            "EQS must drop a hostile that left the area.");
        Assert.Equal(afterMove, AreaQueryTargets(harness, cgfCommander, cgfArea));
    }

    /// <summary>
    /// T-DIS5: pointing a live sensor at a different area (epoch bump) must reach the Muscle and change
    /// the answer. Before the egress published on change, a reliable config sample was sent ONCE and a
    /// later parameter change never left the Brain (T-DIS2 had to remove and re-add the sensor).
    /// </summary>
    [Fact(Timeout = 90_000)]
    public void EqsSensor_ParameterChange_ReachesTheMuscle_WithoutReAdding()
    {
        int domainId = Interlocked.Increment(ref _domainCounter);
        using var harness = new HrotRunnerHarness("simhost,cgf", domainId);
        var sim = harness.SimHost.World!;

        long areaOne  = SpawnOnMuscle(harness, TkbEntityTypes.TacGraphic_Area);
        long areaTwo  = SpawnOnMuscle(harness, TkbEntityTypes.TacGraphic_Area);
        long inOne    = SpawnOnMuscle(harness, TkbEntityTypes.Tank_T72);
        long inTwo    = SpawnOnMuscle(harness, TkbEntityTypes.Tank_T72);
        long commanderNet = harness.Cgf!.TestHook_SpawnEntityWithSplitAuthority(
            TkbEntityTypes.Tank_M1Abrams, muscleNodeId: 1);

        long[] everything = { areaOne, areaTwo, inOne, inTwo, commanderNet };
        Assert.True(harness.PumpUntil(() => everything.All(n =>
                harness.SimHost.TestHook_EntityMap.TryGetEntity(n, out _)
             && harness.Cgf!.GhostEntityMap!.TryGetEntity(n, out _)), timeoutFrames: 3000),
            "All entities must exist on both nodes.");

        var square = new List<Vector2> { new(-20, -20), new(20, -20), new(20, 20), new(-20, 20) };
        Place(sim, SimEntity(harness, areaOne), 100f, 100f, ForceId.Neutral);
        sim.SetManagedComponent(SimEntity(harness, areaOne), new EditablePolyline { Points = new List<Vector2>(square) });
        Place(sim, SimEntity(harness, areaTwo), 500f, 100f, ForceId.Neutral);
        sim.SetManagedComponent(SimEntity(harness, areaTwo), new EditablePolyline { Points = new List<Vector2>(square) });
        Place(sim, SimEntity(harness, inOne), 105f, 95f,  ForceId.Hostile);
        Place(sim, SimEntity(harness, inTwo), 495f, 105f, ForceId.Hostile);
        Assert.True(harness.PumpUntil(() => ForceIs(harness, ForceId.Hostile, inOne, inTwo), timeoutFrames: 2000),
            "Forces must settle on the Muscle after republishing.");

        var cgf = harness.Cgf!.World!;
        harness.Cgf!.GhostEntityMap!.TryGetEntity(areaOne, out Entity cgfAreaOne);
        harness.Cgf!.GhostEntityMap!.TryGetEntity(areaTwo, out Entity cgfAreaTwo);
        harness.Cgf!.GhostEntityMap!.TryGetEntity(commanderNet, out Entity cgfCommander);

        Entity sensor = cgf.CreateEntity();
        cgf.AddComponent(sensor, new PartMetadata { ParentEntity = cgfCommander, InstanceId = AreaChildIndex });
        cgf.AddComponent(sensor, EntitiesOfForceInArea.SensorFor(cgfAreaOne, ForceId.Hostile, epoch: 1u));
        cgf.AddComponent(sensor, new EqsCognitiveBuffer());

        Assert.True(harness.PumpUntil(() => SameSet(EqsTargets(harness, sensor), new SortedSet<long> { inOne }), timeoutFrames: 3000),
            "The sensor must first report the hostile inside area one.");

        // Same sensor, new area, next epoch — no remove / re-add.
        cgf.SetComponent(sensor, EntitiesOfForceInArea.SensorFor(cgfAreaTwo, ForceId.Hostile, epoch: 2u));

        Assert.True(harness.PumpUntil(() => SameSet(EqsTargets(harness, sensor), new SortedSet<long> { inTwo }), timeoutFrames: 3000),
            $"A parameter change must reach the Muscle and switch the answer to area two. " +
            $"muscle carrier slot1={MuscleCarrierArea(harness, commanderNet)} (areaTwo={SimEntity(harness, areaTwo)}, " +
            $"epoch={MuscleCarrierEpoch(harness, commanderNet)}); brain answer=[{string.Join(",", EqsTargets(harness, sensor))}] " +
            $"brain epoch={cgf.GetComponentRO<EqsSensor>(sensor).Epoch}");
    }

    // ── helpers ──────────────────────────────────────────────────────────────────

    // The Muscle-side carrier of the commander's area sensor, and the area it resolved.
    private static Entity MuscleCarrierArea(HrotRunnerHarness harness, long commanderNet)
    {
        var sim = harness.SimHost.World!;
        if (!harness.SimHost.TestHook_EntityMap.TryGetEntity(commanderNet, out Entity parent)) return Entity.Null;
        foreach (var e in sim.Query().With<PartMetadata>().With<EqsSensor>().Build())
        {
            var meta = sim.GetComponentRO<PartMetadata>(e);
            if (meta.ParentEntity == parent && meta.InstanceId == AreaChildIndex)
                return sim.GetComponentRO<EqsSensor>(e).ContextSlot1;
        }
        return new Entity(ulong.MaxValue); // no carrier yet
    }

    private static bool ForceIs(HrotRunnerHarness harness, ForceId force, params long[] nets)
    {
        var sim = harness.SimHost.World!;
        foreach (var n in nets)
        {
            if (!harness.SimHost.TestHook_EntityMap.TryGetEntity(n, out var e)) return false;
            if (!sim.HasComponent<EntityInfo>(e) || sim.GetComponentRO<EntityInfo>(e).ForceId != force) return false;
        }
        return true;
    }

    private static string Describe(HrotRunnerHarness harness, long[] nets)
    {
        var sim = harness.SimHost.World!;
        var parts = new List<string>();
        foreach (var n in nets)
        {
            if (!harness.SimHost.TestHook_EntityMap.TryGetEntity(n, out var e)) { parts.Add($"{n}:missing"); continue; }
            string pos = sim.HasComponent<SimTransform>(e) ? sim.GetComponentRO<SimTransform>(e).Position.ToString() : "noTf";
            string force = sim.HasComponent<EntityInfo>(e) ? sim.GetComponentRO<EntityInfo>(e).ForceId.ToString() : "noInfo";
            long key = Fdp.Toolkit.Replication.Extensions.OwnershipExtensions.PackKey(
                (long)Hrot.NED.Descriptors.EDescriptorType.dtEntityInfo, 0);
            bool auth = Fdp.Toolkit.Replication.Extensions.AuthorityExtensions.HasAuthority((Fdp.ModuleHost.Abstractions.ISimulationView)sim, e, key);
            string cgfForce = harness.Cgf!.GhostEntityMap!.TryGetEntity(n, out var ce)
                && harness.Cgf!.World!.HasComponent<EntityInfo>(ce)
                ? harness.Cgf!.World!.GetComponentRO<EntityInfo>(ce).ForceId.ToString() : "-";
            parts.Add($"{n}:{pos}/{force}/simAuth={auth}/cgf={cgfForce}");
        }
        return string.Join(" ", parts);
    }

    private static uint MuscleCarrierEpoch(HrotRunnerHarness harness, long commanderNet)
    {
        var sim = harness.SimHost.World!;
        if (!harness.SimHost.TestHook_EntityMap.TryGetEntity(commanderNet, out Entity parent)) return 0;
        foreach (var e in sim.Query().With<PartMetadata>().With<EqsSensor>().Build())
        {
            var meta = sim.GetComponentRO<PartMetadata>(e);
            if (meta.ParentEntity == parent && meta.InstanceId == AreaChildIndex)
                return sim.GetComponentRO<EqsSensor>(e).Epoch;
        }
        return 0;
    }

    private static long SpawnOnMuscle(HrotRunnerHarness harness, long tkbType)
        => harness.SimHost.TestHook_SpawnEntity(
            tkbType, new CoreGeoPoint { Latitude = 52.521, Longitude = 13.406, Altitude = 0 });

    private static Entity SimEntity(HrotRunnerHarness harness, long networkId)
    {
        Assert.True(harness.SimHost.TestHook_EntityMap.TryGetEntity(networkId, out Entity e));
        return e;
    }

    // Sets position, force and (optionally) health on the Muscle, which owns these entities.
    private static void Place(EntityRepository world, Entity e, float x, float y, ForceId force, float? health = null)
    {
        var tf = new SimTransform { Position = new Vector3(x, y, 0f), Rotation = Quaternion.Identity };
        if (world.HasComponent<SimTransform>(e)) world.GetComponentRW<SimTransform>(e) = tf;
        else world.AddComponent(e, tf);

        // EntityInfo is REPLICATED: a local write is overwritten by the descriptor ingress. ⭐ Set the
        // force through the product's attribute write — the path an authoring gesture takes — which
        // installs it on the owner and republishes the descriptor.
        var ordinal = force switch
        {
            ForceId.Friend  => Fdp.Toolkit.Replication.ForceIdentifier.Friendly,
            ForceId.Hostile => Fdp.Toolkit.Replication.ForceIdentifier.Opposing,
            _               => Fdp.Toolkit.Replication.ForceIdentifier.Neutral,
        };
        Fdp.Toolkit.Replication.Attributes.EntityWriteRouter.For(world).Write(e, new[]
        {
            new Fdp.Toolkit.Replication.Patching.EntityAttributeChange
            {
                AttributeId = Fdp.Toolkit.Replication.Patching.AttributeIds.Affiliation,
                Value       = Fdp.Toolkit.Replication.Patching.AttributeValue.FromInt((int)ordinal),
            },
        });

        if (health is float h)
        {
            var hp = new Health { Current = h, Max = 100f };
            if (world.HasComponent<Health>(e)) world.GetComponentRW<Health>(e) = hp;
            else world.AddComponent(e, hp);
        }
    }

    // The EQS answer on the Brain, as network ids (empty until the buffer is ready).
    private static SortedSet<long> EqsTargets(HrotRunnerHarness harness, Entity sensor)
    {
        var result = new SortedSet<long>();
        var world  = harness.Cgf!.World!;
        if (!world.HasComponent<EqsCognitiveBuffer>(sensor)) return result;
        ref readonly var buf = ref world.GetComponentRO<EqsCognitiveBuffer>(sensor);
        if (!buf.IsReady) return result;
        var span = buf.GetSpanRO();
        for (int i = 0; i < buf.Count; i++)
        {
            var local = new Entity((ulong)span[i].EntityId);
            result.Add(harness.Cgf!.GhostEntityMap!.TryGetNetworkId(local, out long net) ? net : -span[i].EntityId);
        }
        return result;
    }

    // The old AreaQuery's answer on the Brain for the same area and force, as network ids.
    private static SortedSet<long> AreaQueryTargets(HrotRunnerHarness harness, Entity commander, Entity area)
    {
        var world = harness.Cgf!.World!;
        long requestId = AreaQueryBatchHelper.RequestAreaQuery(world, commander, area, ForceId.Hostile);
        Assert.NotEqual(-1L, requestId);
        Assert.True(harness.PumpUntil(() => AreaQueryBatchHelper.GetAreaQueryResult(world, requestId).IsReady,
                timeoutFrames: 3000), "The old AreaQuery must answer across hosts.");

        var answer = AreaQueryBatchHelper.GetAreaQueryResult(world, requestId);
        var result = new SortedSet<long>();
        for (int i = 0; i < answer.TargetCount; i++)
        {
            long packed = AreaQueryBatchHelper.GetTargetFromPool(world, answer.TargetGroupHandle, i);
            var local = new Entity((ulong)packed);
            result.Add(harness.Cgf!.GhostEntityMap!.TryGetNetworkId(local, out long net) ? net : -packed);
        }
        AreaQueryBatchHelper.FreeAreaQuerySlot(world, requestId);
        return result;
    }

    private static bool SameSet(SortedSet<long> actual, SortedSet<long> expected) => actual.SetEquals(expected);
}
