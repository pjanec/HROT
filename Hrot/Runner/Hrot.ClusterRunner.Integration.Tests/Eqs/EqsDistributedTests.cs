using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Threading;
using Fdp.Core;
using Fdp.Toolkit.Combat.Components;
using Fdp.Toolkit.Replication.Components;
using Fdp.Toolkit.Replication.Extensions;
using Fdp.ModuleHost.Abstractions;
using Fdp.Toolkit.Perception.Components;
using Fdp.Toolkit.Perception.Sensors;
using Fdp.Toolkit.Spatial.Eqs;
using Fdp.Toolkit.Tkb.Domain;
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
///   <item>T-DIS4 -- The area query in EQS 1.3 (<see cref="EntitiesOfForceInArea"/>), computed on the
///         Muscle and read on the Brain, reports exactly the live hostiles inside, and drops one that
///         leaves.</item>
///   <item>T-DIS5 -- A later sensor parameter change (a new area) reaches the Muscle without the
///         remove/re-add workaround T-DIS2 needs.</item>
///   <item>T-DIS6..10 -- the scenario matrix: runtime changes, concave / irregular areas with concurrent
///         sensors, more than 16 targets, an area with no polygon yet, and areas outside the 0..1000 m
///         perception-grid footprint. ⭐ These were PARITY rails against the old AreaQuery until it was
///         retired (2026-10-01); each now states its expected set, which is exactly what the old
///         pipeline answered on the run that last compared them (EQS design §17.5).</item>
///   <item>CE-486 / CE-487 / CE-490 -- the sensor lifecycle on the wire: an end is a Suspended write (never a
///         dispose), a reused part id reaches the new sensor, and the authority suspends inherited orphans
///         (docs/blueprints/DESIGN_Behaviour_Fault_And_Teardown.md §1 D5).</item>
/// </list>
/// <para>Domain range: 201-210; the lifecycle rails 40-43.</para>
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
    /// T-DIS4: one area polygon on the Muscle, targets inside / outside / friendly / wrecked. The EQS
    /// sensor (child of a networked commander, as the blueprint SpawnEqsSensor node makes it) must report
    /// exactly the two live hostiles inside, as Brain-local entities, and drop one that leaves.
    /// </summary>
    [Fact(Timeout = 90_000)]
    public void EqsEntitiesOfForceInArea_ReportsTheLiveHostilesInside_AcrossHosts()
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
        Place(sim, SimEntity(harness, wreckIn),    120f,  80f, ForceId.Hostile);
        // ⭐ S8 (push-only): Health is in the BRAIN group, so on a Muscle-created T72 it is owned by CGF, and the
        //   Muscle's copy is a replica that follows CGF's dtEntityDamage. Kill the wreck on the node that CLAIMS Health
        //   — as damage is applied in production. (Writing it on the Muscle only stuck while the Muscle wrongly
        //   ignored the owner's health — the CE-523 replica bug.) 📄 DESIGN_Ownership_Groups_And_Grants.md §5.6 S8.
        KillOnTheHealthOwner(harness, wreckIn);
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

        // Stage 2: EQS reports the set on the Brain.
        Assert.True(harness.PumpUntil(() => SameSet(EqsTargets(harness, sensor), expected), timeoutFrames: 3000),
            $"EQS must report exactly the two live hostiles inside the area. Got: [{string.Join(",", EqsTargets(harness, sensor))}]; " +
            $"muscle state: {Describe(harness, everything)}");

        // ⭐ The Brain-side buffer holds BRAIN-LOCAL entities (the split fix): each is alive here.
        ref readonly var buf = ref cgf.GetComponentRO<EqsCognitiveBuffer>(sensor);
        for (int i = 0; i < buf.Count; i++)
            Assert.True(cgf.IsAlive(new Entity((ulong)buf.GetSpanRO()[i].EntityId)),
                "EqsCognitiveBuffer.EntityId must be a Brain-local entity, not a network id.");

        // ── A target leaves the area: EQS must drop it ──
        Place(sim, SimEntity(harness, hostileB), 400f, -300f, ForceId.Hostile);
        var afterMove = new SortedSet<long> { hostileA };
        Assert.True(harness.PumpUntil(() => SameSet(EqsTargets(harness, sensor), afterMove), timeoutFrames: 3000),
            "EQS must drop a hostile that left the area.");
    }

    /// <summary>
    /// ⭐ EQS §19 — cover over the TERRAIN across hosts. A child sensor of a Brain commander asks <c>FindCoverFromTarget</c>
    /// with a real threat in slot 1; on the Muscle the carrier has no position (§19.1 H3 — it must resolve its parent), the
    /// solver reads the terrain from its background snapshot (TerrainWorld now synced), the cover comes from the terrain's
    /// buildings, and every answer that reaches the Brain must be hidden from the threat. tt-nav-los geometry on test-town.
    /// </summary>
    [Fact(Timeout = 90_000)]
    public void FindCoverFromTarget_OverTheTerrain_AnswersTheBrainWithPointsHiddenFromTheThreat()
    {
        int domainId = Interlocked.Increment(ref _domainCounter);
        using var harness = new HrotRunnerHarness("simhost,cgf", domainId);
        var sim = harness.SimHost.World!;

        var dir = new System.IO.DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !System.IO.Directory.Exists(System.IO.Path.Combine(dir.FullName, "Hrot", "Subsystems"))) dir = dir.Parent;
        var town = Fdp.Toolkit.Terrain.TerrainWorldParser.Parse(System.IO.File.ReadAllText(System.IO.Path.Combine(dir!.FullName,
            "Hrot", "Subsystems", "Hrot.AI.Behaviors", "Recipes", "Terrain", "test-town", "test-town.world.geojson")), "test-town");
        sim.RegisterManagedComponent<Fdp.Toolkit.Terrain.TerrainWorld>();   // idempotent (TerrainResidency does the same)
        sim.SetSingletonManaged(town);
        sim.SetSingletonManaged<ICoverProvider>(TerrainCoverProvider.Build(town));

        long threatNet    = SpawnOnMuscle(harness, TkbEntityTypes.Tank_T72);
        long commanderNet = SpawnOnMuscle(harness, TkbEntityTypes.Tank_M1Abrams);   // Muscle-owned: its position is placed there
        Assert.True(harness.PumpUntil(() => new[] { threatNet, commanderNet }.All(n =>
                harness.SimHost.TestHook_EntityMap.TryGetEntity(n, out _)
             && harness.Cgf!.GhostEntityMap!.TryGetEntity(n, out _)), timeoutFrames: 3000),
            "Both entities must exist on the Muscle and the Brain.");
        Place(sim, SimEntity(harness, threatNet), 360f, 175f, ForceId.Hostile);

        var cgf = harness.Cgf!.World!;
        harness.Cgf!.GhostEntityMap!.TryGetEntity(commanderNet, out Entity cgfCommander);
        Place(sim, SimEntity(harness, commanderNet), 300f, 80f, ForceId.Friend);
        harness.Cgf!.GhostEntityMap!.TryGetEntity(threatNet, out Entity cgfThreat);
        Entity sensor = cgf.CreateEntity();
        cgf.AddComponent(sensor, new PartMetadata { ParentEntity = cgfCommander, InstanceId = 3 });
        cgf.AddComponent(sensor, new EqsSensor
        {
            BlueprintId = FindCoverFromTarget.BlueprintId, Epoch = 1, SearchRadius = 60f, ContextSlot1 = cgfThreat,
        });
        cgf.AddComponent(sensor, new EqsCognitiveBuffer());

        Assert.True(harness.PumpUntil(() => cgf.GetComponentRO<EqsCognitiveBuffer>(sensor).IsReady
                                         && cgf.GetComponentRO<EqsCognitiveBuffer>(sensor).Count > 0, timeoutFrames: 3000),
            "The Muscle must answer the Brain with cover points — the carrier resolves its parent's position (§19.1 H3). "
            + DiagnoseCover(harness, sensor) + $" commander@sim={SimEntity(harness, commanderNet).Index}:{sim.GetComponent<SimTransform>(SimEntity(harness, commanderNet)).Position}");

        ref readonly var buf = ref cgf.GetComponentRO<EqsCognitiveBuffer>(sensor);
        var span = buf.GetSpanRO();
        // The rule (§19.5): the threat's standing eye → the self's crouched eye at the point, from their SensorMounts.
        var threatEye = sim.GetComponent<SimTransform>(SimEntity(harness, threatNet)).Position
                      + new Vector3(0, 0, EqsTerrainSight.Mount(sim, SimEntity(harness, threatNet)).Standing);
        float crouched = EqsTerrainSight.Mount(sim, SimEntity(harness, commanderNet)).Crouched;
        for (int i = 0; i < buf.Count; i++)
        {
            var p = new Vector3(span[i].PositionX, span[i].PositionY, span[i].PositionZ + crouched);
            Assert.True(town.SegmentBlocked(threatEye, p),
                $"Cover point ({span[i].PositionX:F1},{span[i].PositionY:F1},{span[i].PositionZ:F1}) is in the threat's view (eye {threatEye}, crouched {crouched}).");
            Assert.True(Vector2.Distance(new Vector2(300f, 80f), new Vector2(span[i].PositionX, span[i].PositionY)) <= 60.5f);
        }
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

    /// <summary>
    /// ⭐ S6 / <c>CE-507</c>: the commander is created by the MUSCLE (SimHost), so the Brain holds its brain group only
    /// by GRANT — the sensor config's record names CGF while the primary owner stays SimHost. The config sender used
    /// to gate on the raw descriptor ordinal, which matches no record entry and fell to the primary owner, so CGF never
    /// published the sensor and EQS never answered for a Muscle-created entity (the CE-500 class, for EQS). 📄
    /// <c>docs/DESIGN_Ownership_Groups_And_Grants.md</c> §5.6 S6, §5.7 E7.
    /// </summary>
    [Fact(Timeout = 90_000)]
    public void EqsSensor_OnAMuscleCreatedCommander_ReachesTheMuscle_AndAnswersTheBrain()
    {
        int domainId = Interlocked.Increment(ref _domainCounter);
        using var harness = new HrotRunnerHarness("simhost,cgf", domainId);
        var sim = harness.SimHost.World!;

        long area         = SpawnOnMuscle(harness, TkbEntityTypes.TacGraphic_Area);
        long inside       = SpawnOnMuscle(harness, TkbEntityTypes.Tank_T72);
        long commanderNet = SpawnOnMuscle(harness, TkbEntityTypes.Tank_M1Abrams);   // ⭐ created by SimHost

        long[] everything = { area, inside, commanderNet };
        Assert.True(harness.PumpUntil(() => everything.All(n =>
                harness.SimHost.TestHook_EntityMap.TryGetEntity(n, out _)
             && harness.Cgf!.GhostEntityMap!.TryGetEntity(n, out _)), timeoutFrames: 3000),
            "All entities must exist on both nodes.");

        var square = new List<Vector2> { new(-20, -20), new(20, -20), new(20, 20), new(-20, 20) };
        Place(sim, SimEntity(harness, area), 100f, 100f, ForceId.Neutral);
        sim.SetManagedComponent(SimEntity(harness, area), new EditablePolyline { Points = new List<Vector2>(square) });
        Place(sim, SimEntity(harness, inside), 105f, 95f, ForceId.Hostile);
        Assert.True(harness.PumpUntil(() => ForceIs(harness, ForceId.Hostile, inside), timeoutFrames: 2000),
            "Forces must settle on the Muscle after republishing.");

        var cgf = harness.Cgf!.World!;
        harness.Cgf!.GhostEntityMap!.TryGetEntity(area, out Entity cgfArea);
        harness.Cgf!.GhostEntityMap!.TryGetEntity(commanderNet, out Entity cgfCommander);

        // The Brain holds the commander's brain group by grant, not as its primary owner.
        long sensorKey = OwnershipExtensions.PackKey((long)Hrot.NED.Descriptors.EDescriptorType.dtEqsSensorConfig, 0);
        Assert.True(harness.PumpUntil(() => ((ISimulationView)cgf).HasAuthority(cgfCommander, sensorKey), timeoutFrames: 3000),
            "CGF must hold the commander's sensor-config descriptor by the brain-group grant.");
        Assert.NotEqual(cgf.GetComponentRO<NetworkAuthority>(cgfCommander).LocalNodeId,
                        cgf.GetComponentRO<NetworkAuthority>(cgfCommander).PrimaryOwnerId);

        Entity sensor = cgf.CreateEntity();
        cgf.AddComponent(sensor, new PartMetadata { ParentEntity = cgfCommander, InstanceId = AreaChildIndex });
        cgf.AddComponent(sensor, EntitiesOfForceInArea.SensorFor(cgfArea, ForceId.Hostile, epoch: 1u));
        cgf.AddComponent(sensor, new EqsCognitiveBuffer());

        Assert.True(harness.PumpUntil(() => SameSet(EqsTargets(harness, sensor), new SortedSet<long> { inside }), timeoutFrames: 3000),
            $"EQS must answer for a sensor on a Muscle-created commander. muscle carrier slot1={MuscleCarrierArea(harness, commanderNet)}; " +
            $"brain answer=[{string.Join(",", EqsTargets(harness, sensor))}]");
    }

    // ── T-DIS6..10: the SCENARIO MATRIX ─────────────────────────────────────────────────
    // 📄 docs/designs/eqs-2/EQS_Design_v1.3_final.md §17.5 + §18. Every step: change the Muscle-owned
    //    world, then pump until EQS settles on the EXPECTED set on the Brain. Order is not compared.
    // ⭐ Until 2026-10-01 each step also asked the old AreaQuery and required the same answer (10/10,
    //    twice). The old pipeline is retired; every expected set below is the one both agreed on.
    // ⚠ The scenarios sit inside x, y ∈ [0, 1000) m only because the old query could not see outside
    //    it; T-DIS10 covers outside.

    /// <summary>
    /// T-DIS6: one area, then a sequence of runtime changes — a target enters, one dies, one turns
    /// hostile, one turns friendly, one is deleted, one leaves, and finally the AREA moves.
    /// </summary>
    [Fact(Timeout = 240_000)]
    public void Parity_UnderRuntimeChanges()
    {
        using var rig = new ParityRig();
        long area     = rig.Spawn(TkbEntityTypes.TacGraphic_Area);
        long stays    = rig.Spawn(TkbEntityTypes.Tank_T72);
        long enters   = rig.Spawn(TkbEntityTypes.Tank_T72);
        long dies     = rig.Spawn(TkbEntityTypes.Tank_T72);
        long turnsHostile = rig.Spawn(TkbEntityTypes.Tank_M1Abrams);
        long turnsFriend  = rig.Spawn(TkbEntityTypes.Tank_T72);
        long deleted  = rig.Spawn(TkbEntityTypes.Tank_T72);
        long leaves   = rig.Spawn(TkbEntityTypes.Tank_T72);
        long atNewArea = rig.Spawn(TkbEntityTypes.Tank_T72);
        long commander = rig.Commander();
        rig.WaitReplicated();

        rig.Area(area, 100f, 100f, Square(50f));
        rig.Put(stays,        110f, 120f, ForceId.Hostile);
        rig.Put(enters,       300f, 300f, ForceId.Hostile);
        rig.Put(dies,          80f,  90f, ForceId.Hostile);
        rig.Put(turnsHostile,  95f, 105f, ForceId.Friend);
        rig.Put(turnsFriend,  120f,  80f, ForceId.Hostile);
        rig.Put(deleted,       70f, 130f, ForceId.Hostile);
        rig.Put(leaves,       140f, 140f, ForceId.Hostile);
        rig.Put(atNewArea,    710f, 690f, ForceId.Hostile);
        var sensor = rig.Sensor(commander, AreaChildIndex, area, ForceId.Hostile);

        var expected = Set(stays, dies, turnsFriend, deleted, leaves);
        rig.Converge("initial", sensor, commander, area, ForceId.Hostile, expected);

        rig.Put(enters, 105f, 95f, ForceId.Hostile);
        expected.Add(enters);
        rig.Converge("a target enters", sensor, commander, area, ForceId.Hostile, expected);

        rig.Put(dies, 80f, 90f, ForceId.Hostile, health: 0f);
        expected.Remove(dies);
        rig.Converge("a target dies inside", sensor, commander, area, ForceId.Hostile, expected);

        rig.Put(turnsHostile, 95f, 105f, ForceId.Hostile);
        expected.Add(turnsHostile);
        rig.Converge("a friendly turns hostile", sensor, commander, area, ForceId.Hostile, expected);

        rig.Put(turnsFriend, 120f, 80f, ForceId.Friend);
        expected.Remove(turnsFriend);
        rig.Converge("a hostile turns friendly", sensor, commander, area, ForceId.Hostile, expected);

        rig.Delete(deleted);
        expected.Remove(deleted);
        rig.Converge("a target is deleted", sensor, commander, area, ForceId.Hostile, expected);

        rig.Put(leaves, 400f, -300f, ForceId.Hostile);
        expected.Remove(leaves);
        rig.Converge("a target leaves", sensor, commander, area, ForceId.Hostile, expected);

        rig.Area(area, 700f, 700f, Square(50f));
        rig.Converge("the area moves", sensor, commander, area, ForceId.Hostile, Set(atNewArea));
    }

    /// <summary>
    /// T-DIS7: a concave L-shaped area (a target in its NOTCH is inside the bounding box but outside the
    /// polygon), a triangle, a target exactly on an edge, and THREE sensors live at once — two children
    /// of one commander (different areas) and one of a second commander asking for the other force.
    /// </summary>
    [Fact(Timeout = 240_000)]
    public void Parity_OnConcaveAndIrregularAreas_WithConcurrentSensors()
    {
        using var rig = new ParityRig();
        long lArea     = rig.Spawn(TkbEntityTypes.TacGraphic_Area);
        long triangle  = rig.Spawn(TkbEntityTypes.TacGraphic_Area);
        long inArmOne  = rig.Spawn(TkbEntityTypes.Tank_T72);
        long inArmTwo  = rig.Spawn(TkbEntityTypes.Tank_T72);
        long inNotch   = rig.Spawn(TkbEntityTypes.Tank_T72);
        long onEdge    = rig.Spawn(TkbEntityTypes.Tank_T72);
        long friendInL = rig.Spawn(TkbEntityTypes.Tank_M1Abrams);
        long inTri     = rig.Spawn(TkbEntityTypes.Tank_T72);
        long bboxOnly  = rig.Spawn(TkbEntityTypes.Tank_T72);
        long commanderA = rig.Commander();
        long commanderB = rig.Commander();
        rig.WaitReplicated();

        // L: arms along +x and +y, the notch is the square (20..60, 20..60).
        rig.Area(lArea, 600f, 600f, new(0, 0), new(60, 0), new(60, 20), new(20, 20), new(20, 60), new(0, 60));
        rig.Area(triangle, 850f, 600f, new(0, 0), new(50, 0), new(0, 50));
        rig.Put(inArmOne,   640f,  610f, ForceId.Hostile);
        rig.Put(inArmTwo,   610f,  640f, ForceId.Hostile);
        rig.Put(inNotch,    640f,  640f, ForceId.Hostile);
        rig.Put(onEdge,     660f,  610f, ForceId.Hostile);   // exactly on the x = 60 edge
        rig.Put(friendInL,  605f,  605f, ForceId.Friend);
        rig.Put(inTri,      860f,  610f, ForceId.Hostile);
        rig.Put(bboxOnly,   890f,  640f, ForceId.Hostile);   // inside the triangle's box, outside it

        var lHostile = rig.Sensor(commanderA, AreaChildIndex,     lArea,    ForceId.Hostile);
        var triSens  = rig.Sensor(commanderA, AreaChildIndex + 1, triangle, ForceId.Hostile);
        var lFriend  = rig.Sensor(commanderB, AreaChildIndex,     lArea,    ForceId.Friend);

        // ⭐ The edge point (60, 10) is OUTSIDE: on the edge (60,0)–(60,20) the ray-cast's intersection x is
        //   60 and the test is strict (60 < 60 is false). The old AreaQuery answered the same on the last
        //   compared run.
        rig.WaitForces((ForceId.Hostile, new[] { inArmOne, inArmTwo, inNotch, onEdge, inTri, bboxOnly }),
                       (ForceId.Friend,  new[] { friendInL }));
        rig.Converge("L, hostile", lHostile, commanderA, lArea, ForceId.Hostile, Set(inArmOne, inArmTwo));
        rig.Converge("triangle, hostile", triSens, commanderA, triangle, ForceId.Hostile, Set(inTri));
        rig.Converge("L, friendly (second commander)", lFriend, commanderB, lArea, ForceId.Friend, Set(friendInL));
    }

    /// <summary>
    /// T-DIS8: more targets than EQS keeps — 20 live hostiles inside. EQS returns exactly
    /// <c>EqsResultPool.MaxTopK</c> = 16 of them (the designed cap, §17.5 / §16 H8); the retired AreaQuery
    /// returned all 20.
    /// </summary>
    [Fact(Timeout = 240_000)]
    public void MoreThan16Targets_EqsReturns16_AllOfThemInside()
    {
        using var rig = new ParityRig();
        long area = rig.Spawn(TkbEntityTypes.TacGraphic_Area);
        var targets = Enumerable.Range(0, 20).Select(_ => rig.Spawn(TkbEntityTypes.Tank_T72)).ToArray();
        long commander = rig.Commander();
        rig.WaitReplicated();

        rig.Area(area, 100f, 100f, Square(50f));
        for (int i = 0; i < targets.Length; i++)
            rig.Put(targets[i], 60f + 4f * i, 100f + (i % 2 == 0 ? 10f : -10f), ForceId.Hostile);
        var sensor = rig.Sensor(commander, AreaChildIndex, area, ForceId.Hostile);
        rig.WaitForces((ForceId.Hostile, targets));

        Assert.True(rig.H.PumpUntil(() => EqsTargets(rig.H, sensor).Count == EqsResultPool.MaxTopK, timeoutFrames: 3000),
            $"EQS must report {EqsResultPool.MaxTopK} targets. Got {EqsTargets(rig.H, sensor).Count}.");
        var eqs = EqsTargets(rig.H, sensor);
        Assert.True(eqs.IsSubsetOf(Set(targets)), $"every EQS target must be one of the 20 inside: [{string.Join(",", eqs.Except(Set(targets)))}]");
    }

    /// <summary>
    /// T-DIS9: the area exists but has no usable polygon yet (two points). EQS publishes NOTHING, so a
    /// reader keeps waiting (no false "area clear" — the retired AreaQuery answered READY with 0 targets
    /// here); once the polygon arrives, EQS reports the target.
    /// </summary>
    [Fact(Timeout = 240_000)]
    public void AnAreaWithoutAPolygon_EqsPublishesNothing_UntilThePolygonArrives()
    {
        using var rig = new ParityRig();
        long area   = rig.Spawn(TkbEntityTypes.TacGraphic_Area);
        long inside = rig.Spawn(TkbEntityTypes.Tank_T72);
        long commander = rig.Commander();
        rig.WaitReplicated();

        rig.Area(area, 100f, 100f, new(-50, -50), new(50, 50));
        rig.Put(inside, 110f, 110f, ForceId.Hostile);
        var sensor = rig.Sensor(commander, AreaChildIndex, area, ForceId.Hostile);
        rig.WaitForces((ForceId.Hostile, new[] { inside }));

        rig.H.PumpFrames(300);                                                          // ~30 solver refreshes
        Assert.False(rig.Cgf.GetComponentRO<EqsCognitiveBuffer>(sensor).IsReady,
            "EQS must publish nothing for an area with no polygon (no false 'area clear').");

        rig.Area(area, 100f, 100f, Square(50f));
        rig.Converge("the polygon arrives", sensor, commander, area, ForceId.Hostile, Set(inside));
    }

    /// <summary>
    /// T-DIS10: targets OUTSIDE the perception-grid footprint (x = 1510 and x = -190). EQS walks the
    /// entities, so it sees them. ⭐ The retired AreaQuery did not: its broad phase was the perception grid
    /// — 200 × 200 cells of 5 m anchored at the world origin — and <c>SpatialHashGrid.Add</c> skips
    /// anything outside it.
    /// </summary>
    [Fact(Timeout = 240_000)]
    public void BeyondThePerceptionGrid_EqsSeesTargets()
    {
        using var rig = new ParityRig();
        long farArea  = rig.Spawn(TkbEntityTypes.TacGraphic_Area);
        long westArea = rig.Spawn(TkbEntityTypes.TacGraphic_Area);
        long far  = rig.Spawn(TkbEntityTypes.Tank_T72);
        long west = rig.Spawn(TkbEntityTypes.Tank_T72);
        long commander = rig.Commander();
        rig.WaitReplicated();

        rig.Area(farArea,  1500f, 500f, Square(50f));   // beyond x = 1000
        rig.Area(westArea, -200f, 500f, Square(50f));   // negative x
        rig.Put(far,  1510f, 505f, ForceId.Hostile);
        rig.Put(west, -190f, 505f, ForceId.Hostile);
        var farSensor  = rig.Sensor(commander, AreaChildIndex,     farArea,  ForceId.Hostile);
        var westSensor = rig.Sensor(commander, AreaChildIndex + 1, westArea, ForceId.Hostile);

        foreach (var (sensor, area, target, label) in new[] { (farSensor, farArea, far, "x = 1510"), (westSensor, westArea, west, "x = -190") })
            rig.Converge(label, sensor, commander, area, ForceId.Hostile, Set(target));
    }

    private static Vector2[] Square(float half)
        => new Vector2[] { new(-half, -half), new(half, -half), new(half, half), new(-half, half) };

    private static SortedSet<long> Set(params long[] nets) => new(nets);

    /// <summary>A real CGF Brain + SimHost Muscle over DDS, and the moves a parity scenario makes.</summary>
    private sealed class ParityRig : IDisposable
    {
        public readonly HrotRunnerHarness H;
        public EntityRepository Sim => H.SimHost.World!;
        public EntityRepository Cgf => H.Cgf!.World!;
        private readonly List<long> _all = new();

        public ParityRig() => H = new HrotRunnerHarness("simhost,cgf", Interlocked.Increment(ref _domainCounter));
        public void Dispose() => H.Dispose();

        public long Spawn(long tkbType) { long n = SpawnOnMuscle(H, tkbType); _all.Add(n); return n; }

        // Brain-owned, with a NetworkIdentity — every sensor parent has one.
        public long Commander()
        {
            long n = H.Cgf!.TestHook_SpawnEntityWithSplitAuthority(TkbEntityTypes.Tank_M1Abrams, muscleNodeId: 1);
            _all.Add(n);
            return n;
        }

        public void WaitReplicated()
            => Assert.True(H.PumpUntil(() => _all.All(n =>
                    H.SimHost.TestHook_EntityMap.TryGetEntity(n, out _)
                 && H.Cgf!.GhostEntityMap!.TryGetEntity(n, out _)), timeoutFrames: 3000),
                "All entities must exist on both the Muscle and the Brain.");

        public Entity Brain(long net)
        {
            Assert.True(H.Cgf!.GhostEntityMap!.TryGetEntity(net, out Entity e), $"{net} has no Brain entity");
            return e;
        }

        // Polygon points are RELATIVE to the area's position.
        public void Area(long net, float x, float y, params Vector2[] points)
        {
            var e = SimEntity(H, net);
            Place(Sim, e, x, y, ForceId.Neutral);
            Sim.SetManagedComponent(e, new EditablePolyline { Points = new List<Vector2>(points) });
        }

        public void Put(long net, float x, float y, ForceId force, float? health = null)
        {
            Place(Sim, SimEntity(H, net), x, y, force);
            // ⭐ S8: Health is owned by the Brain group's holder — kill on that node, never on a replica.
            if (health is float h) { Assert.Equal(0f, h); KillOnTheHealthOwner(H, net); }
        }

        public void Delete(long net)
        {
            var e = SimEntity(H, net);
            H.SimHost.World!.Bus.PublishManaged(new Fdp.Toolkit.NetworkSpawning.Events.DestroyEntityCommand
            {
                NetworkId = net,
                Reason    = "EQS parity: a target is deleted",
            });
            Assert.True(H.PumpUntil(() => !Sim.IsAlive(e) && !H.Cgf!.GhostEntityMap!.TryGetEntity(net, out _),
                timeoutFrames: 3000), $"{net} must be gone on both nodes.");
            _all.Remove(net);
        }

        public Entity Sensor(long commanderNet, int childIndex, long areaNet, ForceId force)
        {
            Entity sensor = Cgf.CreateEntity();
            Cgf.AddComponent(sensor, new PartMetadata { ParentEntity = Brain(commanderNet), InstanceId = childIndex });
            Cgf.AddComponent(sensor, EntitiesOfForceInArea.SensorFor(Brain(areaNet), force));
            Cgf.AddComponent(sensor, new EqsCognitiveBuffer());
            return sensor;
        }

        public void WaitForces(params (ForceId Force, long[] Nets)[] groups)
            => Assert.True(H.PumpUntil(() => groups.All(g => ForceIs(H, g.Force, g.Nets)), timeoutFrames: 2000),
                $"Forces must settle on the Muscle. {Describe(H, _all.ToArray())}");

        /// <summary>EQS settles on <paramref name="expected"/> on the Brain.</summary>
        /// <remarks>⚠ <paramref name="commanderNet"/>, <paramref name="areaNet"/> and <paramref name="force"/>
        /// name the question for the failure message; they are what the retired AreaQuery comparison asked.</remarks>
        public void Converge(string step, Entity sensor, long commanderNet, long areaNet, ForceId force, SortedSet<long> expected)
            => Assert.True(H.PumpUntil(() => SameSet(EqsTargets(H, sensor), expected), timeoutFrames: 3000),
                $"[{step}] EQS ({force} in area {areaNet}, commander {commanderNet}): expected [{string.Join(",", expected)}], " +
                $"got [{string.Join(",", EqsTargets(H, sensor))}]. Muscle: {Describe(H, _all.ToArray())}");
    }

    // ══ CE-486 / CE-487 / CE-490 — the sensor lifecycle on the wire ═════════════════════════════════════════════
    //  📄 docs/blueprints/DESIGN_Behaviour_Fault_And_Teardown.md §1 D5, §2 (the races), §3 (the third sequence).
    //  A child sensor's descriptor instance is NEVER disposed while its parent lives: an end is a Suspended write, a part
    //  id is reused by the next lifetime, and a new authority suspends the instances it inherited.
    //  Domain range: 40-43. ⚠ NOT 146-149: the harness auto-range is documented as 100-145, but 50 call sites use it, so a
    //  full run walks it past 145 into the explicit ranges above — measured as a CE-490 timeout in the folder-wide run.

    private static int _lifecycleDomain = 39;

    private sealed class LifecycleRig : IDisposable
    {
        public readonly HrotRunnerHarness H;
        public readonly long Commander;
        public EntityRepository Sim => H.SimHost.World!;
        public EntityRepository Cgf => H.Cgf!.World!;

        /// <param name="createdThroughThePack">⭐ R-179: create the commander as production does — a request to CGF's
        /// creation pack, so the ownership strategy decides the grants — instead of the split-authority hook, which
        /// grants the Perception group to the Muscle by hand.</param>
        public LifecycleRig(long? createdThroughThePack = null)
        {
            H = new HrotRunnerHarness("simhost,cgf", Interlocked.Increment(ref _lifecycleDomain));
            Commander = createdThroughThePack is long tkb
                ? CreateThroughThePack(tkb)
                : H.Cgf!.TestHook_SpawnEntityWithSplitAuthority(TkbEntityTypes.Tank_M1Abrams, muscleNodeId: 1);
            Assert.True(H.PumpUntil(() => H.SimHost.TestHook_EntityMap.TryGetEntity(Commander, out _)
                                       && H.Cgf!.GhostEntityMap!.TryGetEntity(Commander, out _), timeoutFrames: 3000),
                "The commander must exist on both nodes.");
        }

        private long CreateThroughThePack(long tkb)
        {
            var creation = ((Hrot.Common.EntityCreation.IEntityCreationHost)H.Cgf!).EntityCreation!;
            creation.LocalRequests.Enqueue(new Hrot.Core.Network.EntityCreationRequest
            {
                RequestId = Guid.NewGuid(), OwnerAppInstanceId = H.Cgf!.TestHook_NodeId, TkbType = tkb,
            });
            long net = 0;
            Assert.True(H.PumpUntil(() =>
            {
                foreach (var e in Cgf.Query().With<TkbIdentity>().With<NetworkIdentity>().Build())
                    if (Cgf.GetComponentRO<TkbIdentity>(e).TkbType == tkb) { net = Cgf.GetComponentRO<NetworkIdentity>(e).Value; return true; }
                return false;
            }, timeoutFrames: 3000), $"CGF must create tkb {tkb} through its pack.");
            return net;
        }

        /// <summary>The recorded owner of result part <paramref name="part"/> of the commander on a node.</summary>
        public static int? ResultOwner(EntityRepository world, Entity parent, int part)
            => world.HasManagedComponent<DescriptorOwnership>(parent) &&
               world.GetComponent<DescriptorOwnership>(parent).TryGetOwner(
                   OwnershipExtensions.PackKey((long)Hrot.NED.Descriptors.EDescriptorType.dtEqsResult, part), out int o) ? o : null;

        public void Dispose() => H.Dispose();

        // A Brain child sensor on the commander. Template 1 is unknown ⇒ the Muscle answers "empty" every solve —
        // a steady stream of answers this rig can watch (EqsTranslatorTests T9 relies on the same stub).
        public Entity Sensor(int part, uint epoch, float radius)
        {
            H.Cgf!.GhostEntityMap!.TryGetEntity(Commander, out Entity parent);
            var e = Cgf.CreateEntity();
            Cgf.AddComponent(e, new PartMetadata { ParentEntity = parent, InstanceId = part });
            Cgf.AddComponent(e, new EqsSensor { BlueprintId = 1u, Epoch = epoch, SearchRadius = radius });
            Cgf.AddComponent(e, new EqsCognitiveBuffer());
            return e;
        }

        // The Muscle carrier of (commander, part), or Null.
        public Entity Carrier(int part)
        {
            if (!H.SimHost.TestHook_EntityMap.TryGetEntity(Commander, out Entity parent)) return Entity.Null;
            foreach (var e in Sim.Query().With<PartMetadata>().With<EqsSensor>().Build())
            {
                var meta = Sim.GetComponentRO<PartMetadata>(e);
                if (meta.ParentEntity == parent && meta.InstanceId == part) return e;
            }
            return Entity.Null;
        }

        public EqsSensor CarrierSensor(int part) => Sim.GetComponentRO<EqsSensor>(Carrier(part));

        public bool Ready(Entity brainSensor)
            => Cgf.IsAlive(brainSensor) && Cgf.HasComponent<EqsCognitiveBuffer>(brainSensor)
            && Cgf.GetComponentRO<EqsCognitiveBuffer>(brainSensor).IsReady;
    }

    /// <summary>
    /// ⭐ <c>CE-486</c> acceptance ① — ending a sensor publishes NO dispose: the Muscle carrier stays, <c>Suspended</c>,
    /// and the solver publishes nothing for it. 🔴 Before: the Brain disposed the instance and the Muscle destroyed the
    /// carrier — against the descriptor rules (a dispose means the parent died).
    /// </summary>
    [Fact(Timeout = 120_000)]
    public void CE486_ASensorThatEnds_IsSuspendedOnTheMuscle_NotDestroyed_AndPublishesNothing()
    {
        using var rig = new LifecycleRig();
        const int part = 3;
        var sensor = rig.Sensor(part, epoch: 1, radius: 25f);
        Assert.True(rig.H.PumpUntil(() => !rig.Carrier(part).IsNull && rig.Ready(sensor), timeoutFrames: 3000),
            "The sensor must reach the Muscle and answer before it ends.");
        var carrier = rig.Carrier(part);

        rig.Cgf.DestroyEntity(sensor);   // the behaviour run ended (CE-485 releases its parts this way)

        Assert.True(rig.H.PumpUntil(() => rig.Sim.IsAlive(carrier) && rig.CarrierSensor(part).Suspended, timeoutFrames: 3000),
            $"The carrier must stay and become Suspended (alive={rig.Sim.IsAlive(carrier)}).");
        Assert.Equal(carrier, rig.Carrier(part));

        // ⭐ "publishes nothing": no answer for this key after the end. A fresh reader is drained first (TransientLocal
        //   hands it the last pre-end answer), then watched for 120 frames — several 10 Hz solves.
        using var participant = new CycloneDDS.Runtime.DdsParticipant((uint)rig.H.DomainId);
        using var results     = new CycloneDDS.Runtime.DdsReader<EqsResultTopic>(participant, "EqsResult");
        rig.H.PumpUntil(() => false, timeoutFrames: 10);
        CountAnswers(results, rig.Commander, part);
        rig.H.PumpUntil(() => false, timeoutFrames: 120);
        Assert.Equal(0, CountAnswers(results, rig.Commander, part));
        Assert.True(rig.Sim.IsAlive(carrier), "The suspended carrier must still be there.");
    }

    /// <summary>
    /// ⭐ <c>CE-486</c> acceptance ② + <c>CE-487</c> acceptance ④ — a sensor ends and a new one takes the SAME part id in the
    /// SAME scan (design §2 ①, the reuse D5 ① makes routine): the Muscle solves the NEW one, and its answer reaches the
    /// NEW local sensor. 🔴 Before: the egress wrote the new config, then disposed the old one's identical key, and the
    /// Muscle destroyed the carrier (②); the result ingress cache kept answering the dead entity (④).
    /// </summary>
    [Fact(Timeout = 120_000)]
    public void CE486_CE487_ASensorReplacedOnTheSameKeyInOneScan_IsSolved_AndAnsweredOnTheNewSensor()
    {
        using var rig = new LifecycleRig();
        const int part = 4;
        var first = rig.Sensor(part, epoch: 1, radius: 25f);
        Assert.True(rig.H.PumpUntil(() => !rig.Carrier(part).IsNull && rig.Ready(first), timeoutFrames: 3000),
            "The first sensor must reach the Muscle and answer.");

        // ⭐ One scan: the old lifetime ends and the next one takes the same part id (epoch carries the new run).
        rig.Cgf.DestroyEntity(first);
        var second = rig.Sensor(part, epoch: 2, radius: 40f);

        Assert.True(rig.H.PumpUntil(() =>
        {
            var c = rig.Carrier(part);
            if (c.IsNull) return false;
            var s = rig.Sim.GetComponentRO<EqsSensor>(c);
            return !s.Suspended && s.Epoch == 2 && s.SearchRadius == 40f;
        }, timeoutFrames: 3000), $"The Muscle must solve the NEW sensor (carrier={rig.Carrier(part)}).");

        Assert.True(rig.H.PumpUntil(() => rig.Ready(second), timeoutFrames: 3000),
            "The answer for the new lifetime must reach the NEW local sensor, not the destroyed one (CE-487).");
    }

    /// <summary>
    /// ⭐ <c>CE-486</c> acceptance ③ — a child-sensor DISPOSE does not destroy the Muscle carrier; a write that follows
    /// updates that same carrier (design §2 ②). 🔴 Before: the ingress read a child dispose as "destroy carrier".
    /// </summary>
    /// <remarks>⚠ <b>Measured while red-proving:</b> the topic is KeepLast-1, so a dispose and a write of ONE key written
    /// back-to-back collapse into a single valid sample on a real reader — the literal "both in one Take" cannot be
    /// produced over DDS (a rail written that way stayed green on the old ingress). ⇒ this rail lets the Muscle TAKE the
    /// dispose first, then writes: the old ingress destroyed the carrier at the dispose, the new one keeps it.</remarks>
    [Fact(Timeout = 120_000)]
    public void CE486_AChildDispose_DoesNotDestroyTheCarrier_AndAFollowingWriteUpdatesIt()
    {
        using var rig = new LifecycleRig();
        const int part = 5;
        rig.Sensor(part, epoch: 1, radius: 25f);
        Assert.True(rig.H.PumpUntil(() => !rig.Carrier(part).IsNull, timeoutFrames: 3000), "The carrier must exist.");
        var carrier = rig.Carrier(part);

        using var participant = new CycloneDDS.Runtime.DdsParticipant((uint)rig.H.DomainId);
        using var writer      = new CycloneDDS.Runtime.DdsWriter<EqsSensorConfigTopic>(participant, "EqsSensorConfig");
        writer.DisposeInstance(new EqsSensorConfigTopic { ParentNetworkId = rig.Commander, LocalChildIndex = part });
        rig.H.PumpUntil(() => false, timeoutFrames: 60);   // the Muscle takes the dispose and plays its commands back
        Assert.True(rig.Sim.IsAlive(carrier), "A child-sensor dispose must not destroy the carrier (only the parent's death does).");

        writer.Write(new EqsSensorConfigTopic
        {
            ParentNetworkId = rig.Commander, LocalChildIndex = part, BlueprintId = 1u, Epoch = 9, SearchRadius = 55f,
        });
        Assert.True(rig.H.PumpUntil(() => rig.Sim.IsAlive(carrier) && rig.CarrierSensor(part).SearchRadius == 55f,
            timeoutFrames: 3000), $"The same carrier must be updated (alive={rig.Sim.IsAlive(carrier)}).");
    }

    /// <summary>
    /// ⭐ <c>CE-490</c> acceptance ⑤ — an instance on the wire under an entity THIS node holds authority over, with no
    /// local sensor (what a previous owner leaves behind after an authority move — it stops ticking but never ends its
    /// behaviour), is SUSPENDED by this node. 🔴 Before: nobody ended it, and the Muscle solved it forever.
    /// </summary>
    [Fact(Timeout = 120_000)]
    public void CE490_AnInheritedInstanceWithNoLocalSensor_IsSuspendedByTheAuthority()
    {
        using var rig = new LifecycleRig();
        const int part = 8;

        // The previous owner's last word: an ACTIVE config for a sensor no node runs any more.
        using var participant = new CycloneDDS.Runtime.DdsParticipant((uint)rig.H.DomainId);
        using var writer      = new CycloneDDS.Runtime.DdsWriter<EqsSensorConfigTopic>(participant, "EqsSensorConfig");
        using var reader      = new CycloneDDS.Runtime.DdsReader<EqsSensorConfigTopic>(participant, "EqsSensorConfig");
        // ⚠ Let this writer finish discovery FIRST. Measured: written at once, it could reach the Muscle only after the
        //   Brain had already suspended the key (the Brain matched sooner) — TransientLocal then delivered the stale active
        //   sample LAST and the carrier stayed active (3 of 5 runs passed). In production the old owner's sample is long
        //   delivered before an authority move; the late-joiner ordering hazard that remains is in the batch report.
        rig.H.PumpUntil(() => false, timeoutFrames: 150);
        writer.Write(new EqsSensorConfigTopic
        {
            ParentNetworkId = rig.Commander, LocalChildIndex = part, BlueprintId = 1u, Epoch = 1, SearchRadius = 25f,
        });

        bool suspendedOnTheWire = false;
        Assert.True(rig.H.PumpUntil(() =>
        {
            using var loan = reader.Take();
            foreach (var sample in loan)
                if (sample.IsValid && sample.Data.ParentNetworkId == rig.Commander
                    && sample.Data.LocalChildIndex == part && sample.Data.Suspended)
                    suspendedOnTheWire = true;
            var c = rig.Carrier(part);
            return suspendedOnTheWire && (c.IsNull || rig.Sim.GetComponentRO<EqsSensor>(c).Suspended);
        }, timeoutFrames: 3000), $"The authority must suspend the orphan (wire={suspendedOnTheWire}, carrier={rig.Carrier(part)}).");
    }

    // A Muscle template whose answer never moves (one positional candidate, score 1) — under ScoreDelta every solve after
    // an epoch's first answer is suppressed, so the ONLY answers are the ones the epoch rule must force out.
    private sealed class ConstantScoreGenerator : IEqsGenerator
    {
        public int Generate(Entity observer, ref EqsSensor sensor, ISimulationView view, Span<EqsResult> candidates)
        {
            candidates[0] = new EqsResult { EntityId = 0L, PositionX = 1f, PositionY = 0f, Score = 1f };
            return 1;
        }
    }

    private const uint ConstantScoreTemplate = 230u;

    private static Entity ScoreDeltaSensor(LifecycleRig rig, int part, uint epoch)
    {
        var registry = new SimpleEqsTemplateRegistry();
        registry.Register(new EqsQueryTemplate
        {
            BlueprintId = ConstantScoreTemplate, Generator = new ConstantScoreGenerator(), MaxCandidates = 4,
        });
        rig.Sim.SetSingletonManaged<IEqsTemplateRegistry>(registry);

        rig.H.Cgf!.GhostEntityMap!.TryGetEntity(rig.Commander, out Entity parent);
        var e = rig.Cgf.CreateEntity();
        rig.Cgf.AddComponent(e, new PartMetadata { ParentEntity = parent, InstanceId = part });
        rig.Cgf.AddComponent(e, new EqsSensor
        {
            BlueprintId = ConstantScoreTemplate, Epoch = epoch, SearchRadius = 25f,
            PublishPolicy = (byte)EqsPublishPolicy.ScoreDelta, ScoreDeltaThreshold = 0.5f,
        });
        rig.Cgf.AddComponent(e, new EqsCognitiveBuffer());
        return e;
    }

    /// <summary>
    /// ⭐ An epoch bump is a request for a NEW answer — EQS 1.3 §17.6 (<i>"for a guaranteed-new answer bump Epoch"</i>),
    /// and what <c>EqsChildSensor.Refresh</c> waits for. 🔴 Before: a <c>ScoreDelta</c> sensor's soft reset kept the
    /// last-published scores, so a refresh whose scores had not moved was NEVER answered.
    /// </summary>
    [Fact(Timeout = 120_000)]
    public void ScoreDelta_AnEpochBump_IsAnswered_EvenWhenNoScoreMoved()
    {
        using var rig = new LifecycleRig();
        const int part = 6;
        var sensor = ScoreDeltaSensor(rig, part, epoch: 1);
        Assert.True(rig.H.PumpUntil(() => rig.Ready(sensor), timeoutFrames: 3000), "The first answer must arrive.");

        // Refresh (EqsChildSensor.Refresh's shape): a new epoch and a cleared buffer.
        var s = rig.Cgf.GetComponentRO<EqsSensor>(sensor);
        s.Epoch = 2;
        rig.Cgf.SetComponent(sensor, s);
        rig.Cgf.SetComponent(sensor, new EqsCognitiveBuffer());

        Assert.True(rig.H.PumpUntil(() => rig.Ready(sensor), timeoutFrames: 3000),
            "The refreshed epoch must be answered although no score moved.");
    }

    /// <summary>
    /// ⭐ <c>CE-486</c> — a NEW lifetime on a reused part id starts exactly as a fresh carrier did. The carrier now OUTLIVES
    /// a lifetime (never disposed, only suspended), so its evaluation state must not: a suspended carrier drops it.
    /// 🔴 Without that, a <c>ScoreDelta</c> sensor's next lifetime — same parameters, same scores — was never answered
    /// (before CE-486 the carrier was destroyed and re-created, which reset it by accident). The epoch is kept EQUAL on
    /// purpose: it is the case a creator that does not stamp its run into the epoch produces.
    /// </summary>
    [Fact(Timeout = 120_000)]
    public void CE486_ANewLifetimeOnASuspendedCarrier_IsAnswered_LikeAFreshCarrier()
    {
        using var rig = new LifecycleRig();
        const int part = 7;
        var first = ScoreDeltaSensor(rig, part, epoch: 1);
        Assert.True(rig.H.PumpUntil(() => rig.Ready(first), timeoutFrames: 3000), "The first lifetime must be answered.");

        rig.Cgf.DestroyEntity(first);
        Assert.True(rig.H.PumpUntil(() => !rig.Carrier(part).IsNull && rig.CarrierSensor(part).Suspended,
            timeoutFrames: 3000), "The first lifetime must end Suspended on the Muscle.");
        rig.H.PumpUntil(() => false, timeoutFrames: 30);   // several solves of the suspended carrier

        var second = ScoreDeltaSensor(rig, part, epoch: 1);
        Assert.True(rig.H.PumpUntil(() => rig.Ready(second), timeoutFrames: 3000),
            "The next lifetime on the same part id must be answered like a fresh carrier's.");
    }

    /// <summary>
    /// ⭐ <c>CE-492</c> — an instance with TWO writers (after an authority move: the old owner's last ACTIVE sample, the new
    /// owner's SUSPEND from its orphan sweep) ends in the state of the NEWEST sample by source time, whatever order a
    /// late-joining Muscle receives them in. 🔴 Before: the sample that ARRIVED last won, so an ended sensor could be solved
    /// forever (case ①) or a new lifetime silenced (case ③).
    /// </summary>
    /// <remarks>Real DDS gives a test control over neither arrival order nor source time, so the rail plays chosen
    /// arrivals into a Muscle ingress over the rig's REAL Muscle world (its entity map, parent ghost, command playback).
    /// The epochs are EQUAL on purpose: the owner run is not unique across an authority move (behaviours lane, design
    /// §3a), so the epoch cannot tell the cases apart.</remarks>
    [Fact(Timeout = 120_000)]
    public void CE492_TwoWritersOnOneInstance_TheNewestSourceTimeWins_WhateverTheArrivalOrder()
    {
        using var rig = new LifecycleRig();
        var ingress = new Hrot.Network.NED.SimHost.EqsSensorConfigIngressTranslator(participant: null, rig.H.SimHost.TestHook_EntityMap);

        EqsSensorConfigTopic Config(int part, bool suspended) => new()
        {
            ParentNetworkId = rig.Commander, LocalChildIndex = part, BlueprintId = 1u, Epoch = 0x0002_0001u,
            SearchRadius = 25f, Suspended = suspended,
        };
        void Arrive(params (EqsSensorConfigTopic Data, long SourceTime)[] arrivals)
        {
            var cmd = new EntityCommandBuffer();
            foreach (var (data, sourceTime) in arrivals) ingress.Receive(cmd, data, valid: true, disposed: false, sourceTime);
            ingress.ApplyPendingForRail(cmd, rig.Sim);
            cmd.Playback(rig.Sim);
        }

        // ① the hazard: the new owner suspended (t=200); the old owner's last ACTIVE (t=100) arrives after it ⇒ stays ended.
        var before = rig.Carrier(11);
        Arrive((Config(11, suspended: true), 200), (Config(11, suspended: false), 100));
        var c1 = rig.Carrier(11);
        Assert.True(c1.IsNull || rig.CarrierSensor(11).Suspended,
            "A stale ACTIVE sample from the old owner must not revive a sensor the new owner ended. "
            + $"(carrier before={before}, after={c1}, stale samples={ingress.StaleSampleCount}"
            + (c1.IsNull ? ")" : $", its epoch={rig.CarrierSensor(11).Epoch:X} radius={rig.CarrierSensor(11).SearchRadius} template={rig.CarrierSensor(11).BlueprintId})"));

        // ② one writer ends a lifetime and starts the next (same epoch): the later one wins ⇒ solved.
        Arrive((Config(12, suspended: true), 100), (Config(12, suspended: false), 200));
        Assert.False(rig.Carrier(12).IsNull, "The new lifetime must get a carrier.");
        Assert.False(rig.CarrierSensor(12).Suspended, "The new lifetime must be solved.");

        // ③ the old owner had ended its sensor (t=100); the new owner's first run is ACTIVE (t=200) and arrives first ⇒
        //    the old suspend, arriving last, must not silence it.
        Arrive((Config(13, suspended: false), 200), (Config(13, suspended: true), 100));
        Assert.False(rig.Carrier(13).IsNull, "The new owner's sensor must get a carrier.");
        Assert.False(rig.CarrierSensor(13).Suspended, "A stale SUSPEND from the old owner must not silence the new owner's sensor.");

        Assert.Equal(2, ingress.StaleSampleCount);
    }

    /// <summary>
    /// ⭐ <c>CE-3002</c> / <c>R-179</c> — the live defect (§5.7.1 E7): a brain WITHOUT vision sensors, created through the
    /// pack as production does, gets no Perception group grant — yet its EQS sensor is answered, because the brain names
    /// the solver (the least-loaded Perception node) in the config, and every node records it as the owner of the result
    /// part. 🔴 Before: the result part fell back to the creator (CGF, no solver) and SimHost's gated egress published 0.
    /// 📄 <c>docs/DESIGN_Ownership_Groups_And_Grants.md</c> §5.8.
    /// </summary>
    [Fact(Timeout = 120_000)]
    public void R179_ABrainWithoutVisionSensors_IsAnswered_ByTheSolverItsBrainNamed()
    {
        using var rig = new LifecycleRig(createdThroughThePack: TkbEntityTypes.Unit_TankPlatoon);
        const int part = 4;
        var sensor = rig.Sensor(part, epoch: 1, radius: 25f);

        Assert.True(rig.H.PumpUntil(() => rig.Ready(sensor), timeoutFrames: 3000),
            "A brain without vision sensors must still get its EQS answer.");

        rig.H.Cgf!.GhostEntityMap!.TryGetEntity(rig.Commander, out Entity onCgf);
        rig.H.SimHost.TestHook_EntityMap.TryGetEntity(rig.Commander, out Entity onSim);
        int simNode = rig.Sim.GetComponentRO<NetworkAuthority>(onSim).LocalNodeId;
        Assert.Equal(simNode, LifecycleRig.ResultOwner(rig.Cgf, onCgf, part));    // the brain recorded its pick…
        Assert.Equal(simNode, LifecycleRig.ResultOwner(rig.Sim, onSim, part));    // …and so did the solver, from the config
    }

    /// <summary>
    /// ⭐ <c>R-179</c> — a Perception node builds a carrier ONLY for a sensor whose config names it; a config naming
    /// another node builds none here, and moving the solver away stops the carrier this node had. Every node records the
    /// named solver as the result part's owner. (Driven into a Muscle ingress over the rig's real Muscle world, as the
    /// CE-492 rail does — real DDS gives no control over who is named.)
    /// </summary>
    [Fact(Timeout = 120_000)]
    public void R179_OnlyTheNamedSolverBuildsACarrier_AndEveryNodeRecordsIt()
    {
        using var rig = new LifecycleRig();
        rig.H.SimHost.TestHook_EntityMap.TryGetEntity(rig.Commander, out Entity onSim);
        Assert.True(rig.H.PumpUntil(() => rig.Sim.HasComponent<NetworkAuthority>(onSim), timeoutFrames: 3000));
        int me = rig.Sim.GetComponentRO<NetworkAuthority>(onSim).LocalNodeId;
        const int elsewhere = 999;
        var ingress = new Hrot.Network.NED.SimHost.EqsSensorConfigIngressTranslator(participant: null,
                                                                                    rig.H.SimHost.TestHook_EntityMap, me);
        long t = 0;
        void Arrive(int part, int solver)
        {
            var cmd = new EntityCommandBuffer();
            ingress.Receive(cmd, new EqsSensorConfigTopic
            {
                ParentNetworkId = rig.Commander, LocalChildIndex = part, BlueprintId = 1u, Epoch = 1u, SearchRadius = 25f,
                SolverNodeId = solver,
            }, valid: true, disposed: false, ++t);
            ingress.ApplyPendingForRail(cmd, rig.Sim);
            cmd.Playback(rig.Sim);
        }

        Arrive(21, elsewhere);
        Assert.True(rig.Carrier(21).IsNull, "Another node's sensor must get no carrier here.");
        Assert.Equal(elsewhere, LifecycleRig.ResultOwner(rig.Sim, onSim, 21));

        Arrive(22, me);
        Arrive(22, me);   // a second sample finds the carrier the first one's playback created
        Assert.False(rig.Carrier(22).IsNull, "This node's sensor must get a carrier.");
        Assert.False(rig.CarrierSensor(22).Suspended);
        Assert.Equal(me, LifecycleRig.ResultOwner(rig.Sim, onSim, 22));

        Arrive(22, elsewhere);
        Assert.True(rig.CarrierSensor(22).Suspended, "A carrier whose sensor moved to another solver must stop solving.");
        Assert.Equal(elsewhere, LifecycleRig.ResultOwner(rig.Sim, onSim, 22));
    }

    // ── CE-3036 (S3) — TKB sensors: built on every node, silent on the wire until overridden ─────────────────────────

    private static readonly SensorEntryDto TkbRadar = new()
    {
        Kind = SensorModality.Radar, Template = new Guid("5e4501a0-3036-4000-8000-000000000001"),   // unknown ⇒ "empty" answers
        Radar = new RadarSensorDto { Range = 300f },
    };

    private static int ConfigSamples(CycloneDDS.Runtime.DdsReader<EqsSensorConfigTopic> reader, long parent, int part,
                                     List<EqsSensorConfigTopic>? into = null)
    {
        int n = 0;
        using var loan = reader.Take();
        foreach (var sample in loan)
            if (sample.IsValid && sample.Data.ParentNetworkId == parent && sample.Data.LocalChildIndex == part)
            {
                n++;
                into?.Add(sample.Data);
            }
        return n;
    }

    /// <summary>
    /// ⭐⭐ <c>CE-3036</c> acceptance — a TKB sensor is built from the TKB on BOTH nodes under the same part id
    /// (<c>1000 + index</c>, R-185 K), is solved and answered with NO config on the wire; switching it off is an override
    /// that reaches the Muscle, and clearing the override sends one "default" sample that switches it back on (R-187 N′).
    /// 📄 docs/DESIGN_Sensors_And_Doctrine.md §5.
    /// </summary>
    [Fact(Timeout = 120_000)]
    public void CE3036_ATkbSensor_IsAnsweredWithNoConfigOnTheWire_AndAnOverrideReachesTheMuscle()
    {
        using var rig = new LifecycleRig();
        const int part = SensorChildFactory.FirstTkbPartId;
        rig.H.Cgf!.GhostEntityMap!.TryGetEntity(rig.Commander, out Entity onCgf);
        rig.H.SimHost.TestHook_EntityMap.TryGetEntity(rig.Commander, out Entity onSim);

        using var participant = new CycloneDDS.Runtime.DdsParticipant((uint)rig.H.DomainId);
        using var configs     = new CycloneDDS.Runtime.DdsReader<EqsSensorConfigTopic>(participant, "EqsSensorConfig");

        // What the PerceptionTkbTranslator does for list entry 0, on each node.
        var cgfSensor = SensorChildFactory.EnsureTkbChild(rig.Cgf, onCgf, 0, TkbRadar);
        var simSensor = SensorChildFactory.EnsureTkbChild(rig.Sim, onSim, 0, TkbRadar);
        Assert.False(cgfSensor.IsNull, "CGF must build the TKB sensor.");
        Assert.False(simSensor.IsNull, "SimHost must build the TKB sensor.");

        Assert.True(rig.H.PumpUntil(() => rig.Ready(cgfSensor), timeoutFrames: 3000),
            "The Brain's TKB sensor must be answered by the Muscle's own TKB carrier.");
        rig.H.PumpUntil(() => false, timeoutFrames: 30);
        Assert.Equal(0, ConfigSamples(configs, rig.Commander, part));   // ⭐ R-185 K — no config traffic for a TKB sensor

        // Override: switch it OFF on the Brain ⇒ one override sample ⇒ the Muscle stops solving it.
        UnitSensors.SetEnabled(rig.Cgf, cgfSensor, enabled: false);
        Assert.True(UnitSensors.IsOverridden(rig.Cgf, cgfSensor));
        var seen = new List<EqsSensorConfigTopic>();
        Assert.True(rig.H.PumpUntil(() => { ConfigSamples(configs, rig.Commander, part, seen); return rig.CarrierSensor(part).Suspended; },
            timeoutFrames: 3000), "The override must switch the Muscle's TKB carrier off.");
        Assert.Contains(seen, s => s.Override && s.Suspended);
        Assert.Equal(simSensor, rig.Carrier(part));   // the override updated the TKB carrier; it built none of its own

        // Clear: back to the TKB default (ON) through one Override=false sample.
        UnitSensors.ClearOverride(rig.Cgf, cgfSensor);
        Assert.False(UnitSensors.IsOverridden(rig.Cgf, cgfSensor));
        Assert.True(rig.H.PumpUntil(() => { ConfigSamples(configs, rig.Commander, part, seen); return !rig.CarrierSensor(part).Suspended; },
            timeoutFrames: 3000), "Clearing the override must restore the TKB default on the Muscle.");
        Assert.Contains(seen, s => !s.Override);
    }

    /// <summary>
    /// ⭐ <c>CE-3036</c> / R-186 M′ — the per-kind config travels as JSON. A behaviour sensor with a valid config gets its
    /// capability on the Muscle; malformed JSON is REFUSED (no carrier, nothing half-configured solves). A TKB override
    /// carries the new capability, and an Override=false sample restores the TKB default. (Driven into a Muscle ingress
    /// over the rig's real Muscle world, as the CE-492 rail does.)
    /// </summary>
    [Fact(Timeout = 120_000)]
    public void CE3036_TheConfigTravelsAsJson_BadJsonIsRefused_AndATkbOverrideRestores()
    {
        using var rig = new LifecycleRig();
        rig.H.SimHost.TestHook_EntityMap.TryGetEntity(rig.Commander, out Entity onSim);
        var ingress = new Hrot.Network.NED.SimHost.EqsSensorConfigIngressTranslator(participant: null, rig.H.SimHost.TestHook_EntityMap);
        long t = 0;
        void Arrive(EqsSensorConfigTopic data)
        {
            var cmd = new EntityCommandBuffer();
            ingress.Receive(cmd, data, valid: true, disposed: false, ++t);
            ingress.ApplyPendingForRail(cmd, rig.Sim);
            cmd.Playback(rig.Sim);
        }
        EqsSensorConfigTopic Config(int part, string kind, string json, bool @override = false, bool suspended = false) => new()
        {
            ParentNetworkId = rig.Commander, LocalChildIndex = part, BlueprintId = 1u, Epoch = 1u, SearchRadius = 25f,
            ConfigKind = kind, ConfigJson = json, Override = @override, Suspended = suspended,
        };

        // ① malformed JSON ⇒ refused, no carrier.
        Arrive(Config(31, SensorConfigCodec.KindSensorEntry, "{ not json"));
        Assert.True(rig.Carrier(31).IsNull, "A refused config must build no carrier.");
        Assert.Equal(1, ingress.RefusedConfigCount);

        // ② a valid behaviour-sensor config ⇒ carrier with its kind and capability.
        var thermal = new SensorEntryDto
        {
            Kind = SensorModality.Thermal, Template = TkbRadar.Template, Thermal = new ThermalSensorDto { Range = 150f },
        };
        Arrive(Config(32, SensorConfigCodec.KindSensorEntry, SensorConfigCodec.Encode(thermal)));
        var c32 = rig.Carrier(32);
        Assert.False(c32.IsNull, "A valid config must build the carrier.");
        Assert.Equal(SensorModality.Thermal, rig.Sim.GetComponentRO<SensorTag>(c32).Kind);
        Assert.Equal(150f, rig.Sim.GetManagedComponentRO<SensorCapability>(c32).Current.Range);

        // ③ a TKB override with JSON ⇒ Current changes, Default stays; ④ Override=false ⇒ back to the default.
        var tkb = SensorChildFactory.EnsureTkbChild(rig.Sim, onSim, 0, TkbRadar);
        const int tkbPart = SensorChildFactory.FirstTkbPartId;
        var longer = TkbRadar with { Radar = new RadarSensorDto { Range = 800f } };
        Arrive(Config(tkbPart, SensorConfigCodec.KindSensorEntry, SensorConfigCodec.Encode(longer), @override: true));
        Assert.Equal(tkb, rig.Carrier(tkbPart));
        Assert.Equal(800f, rig.Sim.GetManagedComponentRO<SensorCapability>(tkb).Current.Range);
        Assert.Equal(300f, rig.Sim.GetManagedComponentRO<SensorCapability>(tkb).Default.Range);

        Arrive(Config(tkbPart, string.Empty, string.Empty, @override: false));
        Assert.Equal(300f, rig.Sim.GetManagedComponentRO<SensorCapability>(tkb).Current.Range);
        Assert.Equal(300f, rig.CarrierSensor(tkbPart).SearchRadius);
        Assert.Equal(1, ingress.RefusedConfigCount);
    }

    private static int CountAnswers(CycloneDDS.Runtime.DdsReader<EqsResultTopic> reader, long parent, int part)
    {
        int n = 0;
        using var loan = reader.Take();
        foreach (var sample in loan)
            if (sample.IsValid && sample.Data.ParentNetworkId == parent && sample.Data.LocalChildIndex == part) n++;
        return n;
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

    private static string DiagnoseCover(HrotRunnerHarness harness, Entity brainSensor)
    {
        var sim = harness.SimHost.World!;
        var cgf = harness.Cgf!.World!;
        var sb = new System.Text.StringBuilder();
        ref readonly var b = ref cgf.GetComponentRO<EqsCognitiveBuffer>(brainSensor);
        sb.Append($"brain ready={b.IsReady} count={b.Count}; ");
        foreach (var c in sim.Query().With<PartMetadata>().With<EqsSensor>().WithLifecycle(EntityLifecycle.All).Build())
        {
            var sensor = sim.GetComponent<EqsSensor>(c);
            var self = EqsContext.Self(sim, c, sensor);
            sb.Append($"carrier {c.Index} bp=0x{sensor.BlueprintId:X} r={sensor.SearchRadius} slot1={sensor.ContextSlot1.Index} self={(self.IsNull ? "null" : self.Index + "@" + sim.GetComponent<SimTransform>(self).Position)} cover={sim.GetSingletonManaged<ICoverProvider>()?.GetType().Name} ");
            if (!sim.GetSingletonManaged<IEqsTemplateRegistry>()!.TryGetTemplate(sensor.BlueprintId, out var t)) { sb.Append("no template; "); continue; }
            var buf = new EqsResult[t.MaxCandidates];
            int n = t.Generator.Generate(c, ref sensor, sim, buf);
            sb.Append($"gen={n} ");
            if (n <= 0) continue;
            var span = buf.AsSpan(0, n);
            foreach (var x in t.FilterCheap ?? Array.Empty<IEqsTest>()) x.ExecuteBatch(c, ref sensor, sim, span);
            int kept = 0; foreach (var r in span) if (r.EntityId != -1L) kept++;
            sb.Append($"afterLos={kept} terrain={sim.HasSingletonManaged<Fdp.Toolkit.Terrain.TerrainWorld>()}; ");
        }
        return sb.ToString();
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
    /// <summary>Sets <see cref="Health"/> to 0 on whichever node claims it (the Brain group's owner); every replica then
    /// follows through <c>dtEntityDamage</c>.</summary>
    private static void KillOnTheHealthOwner(HrotRunnerHarness harness, long net)
    {
        Assert.True(harness.PumpUntil(() => Owner() != null, timeoutFrames: 2000),
            $"Some node must claim Health of {net}.");
        var (world, e) = Owner()!.Value;
        world.GetComponentRW<Health>(e) = new Health { Current = 0f, Max = world.GetComponent<Health>(e).Max };
        Assert.True(harness.PumpUntil(() => harness.SimHost.World!.GetComponent<Health>(SimEntity(harness, net)).Current <= 0f,
                timeoutFrames: 2000),
            $"The Muscle's replica of {net} must follow its owner's Health to 0.");

        (EntityRepository, Entity)? Owner()
        {
            var sim = harness.SimHost.World!;
            Entity s = SimEntity(harness, net);
            if (sim.HasComponent<Health>(s) && sim.HasAuthority<Health>(s)) return (sim, s);
            var cgf = harness.Cgf!.World!;
            if (harness.Cgf!.GhostEntityMap!.TryGetEntity(net, out Entity c) && cgf.HasComponent<Health>(c) && cgf.HasAuthority<Health>(c))
                return (cgf, c);
            return null;
        }
    }

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


    private static bool SameSet(SortedSet<long> actual, SortedSet<long> expected) => actual.SetEquals(expected);
}
