using System;
using System.Threading;
using Fdp.Core;
using Fdp.Toolkit.Perception;
using Fdp.Toolkit.Perception.Components;
using Hrot.Map.Common;
using Xunit;

namespace Hrot.ClusterRunner.Integration.Tests;

/// <summary>
/// Integration tests for the sensor mechanism end-to-end pipeline.
///
/// <para>
/// Proves that the full CQRS sensor pipeline works:
/// <list type="number">
///   <item>SimHost <c>SensorContactList</c> (Acquired) is picked up by
///     <c>SensorTrackStateEgressTranslator</c> and published as a
///     <c>SensorTrackState</c> DDS sample.</item>
///   <item>CGF <c>SensorTrackStateIngressTranslator</c> receives the sample and
///     writes <c>ActiveSensorTracks</c> onto the observer entity.</item>
///   <item><c>CgfThreatEvaluationSystem</c> (<c>ThreatEvaluationSystem</c> wrapped as a
///     <c>ComponentSystem</c>) boosts <c>TargetMemory</c> scores on the CGF entity.</item>
///   <item>After the contact is cleared (Count = 0), decay logic runs and the score
///     decreases, proving the temporal-forgetting logic also works end-to-end.</item>
/// </list>
/// </para>
///
/// <para>Domain range: 60-69.</para>
/// </summary>
public sealed class SensorMechanismIntegrationTests
{
    private static int _domainCounter = 59;

    private const int SpawnTimeoutMs          = 8_000;
    private const int SensorPipelineTimeoutMs = 8_000;
    private const int DecayTimeoutMs          = 5_000;
    private const int PumpSleepMs             = 5;

    /// <summary>
    /// Verifies the complete sensor mechanism pipeline end-to-end:
    /// SimHost SensorContactList (Acquired) -> DDS SensorTrackState ->
    /// CGF ActiveSensorTracks -> CGF TargetMemory boosted -> then decay after contact lost.
    /// </summary>
    [Fact]
    public unsafe void SensorMechanism_EndToEnd_CGFTargetMemoryPopulatesAndDecays()
    {
        int domainId = Interlocked.Increment(ref _domainCounter);
        using var harness = new HrotRunnerHarness("simhost,cgf", domainId);

        // Spawn observer entity: M1 Abrams blueprint includes PerceptionReceptor + TargetMemory.
        // After split-authority spawn: SimHost has SimTransform authority; CGF keeps TargetMemory.
        long observerNetId = harness.Cgf!.TestHook_SpawnEntityWithSplitAuthority(
            TkbEntityTypes.Tank_M1Abrams, muscleNodeId: 1);

        // Spawn target entity: any entity registered in both EntityMaps is sufficient.
        long targetNetId = harness.Cgf!.TestHook_SpawnEntityWithSplitAuthority(
            TkbEntityTypes.Tank_M1Abrams, muscleNodeId: 1);

        // Wait for both entities to be ready on both SimHost and CGF.
        bool entitiesReady = harness.PumpUntil(
            () =>
            {
                var simMap   = harness.SimHost.TestHook_EntityMap;
                var simWorld = harness.SimHost.World;
                if (simWorld == null) return false;
                if (!simMap.TryGetEntity(observerNetId, out Entity obs)) return false;
                if (!simWorld.IsAlive(obs))                              return false;
                if (!simWorld.HasAuthority<SimTransform>(obs))           return false;

                if (!simMap.TryGetEntity(targetNetId, out Entity tgt)) return false;
                if (!simWorld.IsAlive(tgt))                            return false;

                var cgfMap = harness.Cgf!.GhostEntityMap;
                if (cgfMap == null) return false;
                if (!cgfMap.TryGetEntity(observerNetId, out _)) return false;
                if (!cgfMap.TryGetEntity(targetNetId,   out _)) return false;
                return true;
            },
            SpawnTimeoutMs / PumpSleepMs);

        Assert.True(entitiesReady,
            $"Both entities must be ready on SimHost (SimTransform authority) and CGF within " +
            $"{SpawnTimeoutMs} ms after split-authority spawn.");

        // Resolve ECS handles.
        harness.SimHost.TestHook_EntityMap.TryGetEntity(observerNetId, out Entity observerSimEntity);
        harness.SimHost.TestHook_EntityMap.TryGetEntity(targetNetId,   out Entity targetSimEntity);
        harness.Cgf!.GhostEntityMap!.TryGetEntity(observerNetId, out Entity cgfObserverEntity);

        // Inject SensorContactList on the SimHost observer.
        // SensorContactList.EntityIds stores packed ECS entity handles (not network IDs).
        // SensorTrackStateEgressTranslator reconstructs the Entity from the packed value and
        // maps it to a network ID via _entityMap.TryGetNetworkId.
        var sc = new SensorContactList();
        sc.EntityIds[0]    = (long)targetSimEntity.PackedValue;
        sc.LastSeenTick[0] = 1u;
        sc.State[0]        = (byte)SensorContactState.Acquired;
        sc.Count           = 1;

        // AddComponent has upsert semantics in FDP's EntityRepository.
        harness.SimHost.World!.AddComponent(observerSimEntity, sc);

        // Diagnostic step 1: verify SensorContactList is visible on SimHost.
        Assert.True(harness.SimHost.World!.HasComponent<SensorContactList>(observerSimEntity),
            "SensorContactList must be visible on the SimHost entity immediately after AddComponent.");

        // Diagnostic step 2: wait for CGF entity to gain ActiveSensorTracks.
        bool activeSensorTracksPopulated = harness.PumpUntil(
            () =>
            {
                var cgfWorld = harness.Cgf!.World;
                if (cgfWorld == null || !cgfWorld.IsAlive(cgfObserverEntity)) return false;
                if (!cgfWorld.HasComponent<ActiveSensorTracks>(cgfObserverEntity)) return false;
                var tracks = cgfWorld.GetComponent<ActiveSensorTracks>(cgfObserverEntity);
                return tracks.Count > 0;
            },
            SensorPipelineTimeoutMs / PumpSleepMs);

        Assert.True(activeSensorTracksPopulated,
            "CGF entity must gain ActiveSensorTracks with Count > 0 after SensorTrackState(Acquired) " +
            "is transmitted from SimHost. Pipeline: SensorContactList -> SensorTrackStateEgressTranslator " +
            "-> DDS -> SensorTrackStateIngressTranslator -> ActiveSensorTracks.");

        // Diagnostic: verify CGF entity has TargetMemory (required for ThreatEvaluationSystem).
        Assert.True(harness.Cgf!.World!.HasComponent<TargetMemory>(cgfObserverEntity),
            "CGF observer entity must have TargetMemory component (added by M1Abrams blueprint via WithCombat).");

        // ── Assert: CGF TargetMemory is populated after the sensor pipeline fires ──────
        //
        // Pipeline: ActiveSensorTracks -> CgfThreatEvaluationSystem (boost 50/s) -> TargetMemory.Count > 0
        bool targetMemoryPopulated = harness.PumpUntil(
            () =>
            {
                var cgfWorld = harness.Cgf!.World;
                if (cgfWorld == null || !cgfWorld.IsAlive(cgfObserverEntity)) return false;
                if (!cgfWorld.HasComponent<TargetMemory>(cgfObserverEntity))  return false;
                var mem = cgfWorld.GetComponent<TargetMemory>(cgfObserverEntity);
                // First check: at least one entry must exist.
                return mem.Count > 0;
            },
            SensorPipelineTimeoutMs / PumpSleepMs);

        Assert.True(targetMemoryPopulated,
            "CGF TargetMemory must be populated (Count > 0) after " +
            "SensorContactList(Acquired) is injected on the SimHost observer. " +
            "The pipeline: SensorContactList -> SensorTrackStateEgressTranslator -> " +
            "DDS SensorTrackState(Acquired) -> SensorTrackStateIngressTranslator -> " +
            "ActiveSensorTracks -> CgfThreatEvaluationSystem -> TargetMemory must fire.");

        // Wait for the continuous boost to accumulate a positive threat score.
        // The boost rate is 50 threat-score units per second; even at minimum DeltaTime
        // a non-zero score must appear within a few frames.
        bool scorePositive = harness.PumpUntil(
            () =>
            {
                var cgfWorld = harness.Cgf!.World;
                if (cgfWorld == null || !cgfWorld.IsAlive(cgfObserverEntity)) return false;
                if (!cgfWorld.HasComponent<TargetMemory>(cgfObserverEntity))  return false;
                var mem = cgfWorld.GetComponent<TargetMemory>(cgfObserverEntity);
                return mem.Count > 0 && mem.ThreatScores[0] > 0f;
            },
            SensorPipelineTimeoutMs / PumpSleepMs);

        Assert.True(scorePositive,
            "CgfThreatEvaluationSystem must boost ThreatScores[0] to a positive value " +
            "while ActiveSensorTracks is populated. Boost rate = 50 * deltaTime per second.");

        // Capture the score at the high-water mark for decay comparison.
        float scoreAtAcquisition = harness.Cgf!.World!.GetComponent<TargetMemory>(cgfObserverEntity).ThreatScores[0];

        // ── Assert: decay logic runs after the contact is cleared ────────────────────
        //
        // Clear SensorContactList (Count = 0): SensorTrackStateEgressTranslator detects the
        // Acquired -> Lost transition and emits SensorTrackState(Lost). On CGF:
        // SensorTrackStateIngressTranslator removes the target from ActiveSensorTracks, so
        // ThreatEvaluationSystem applies only decay (no boost) and the score falls.
        var emptyList = new SensorContactList(); // Count = 0; AddComponent has upsert semantics
        harness.SimHost.World!.AddComponent(observerSimEntity, emptyList);

        bool scoreDecayed = harness.PumpUntil(
            () =>
            {
                var cgfWorld = harness.Cgf!.World;
                if (cgfWorld == null || !cgfWorld.IsAlive(cgfObserverEntity)) return false;
                if (!cgfWorld.HasComponent<TargetMemory>(cgfObserverEntity))  return false;
                var mem = cgfWorld.GetComponent<TargetMemory>(cgfObserverEntity);
                if (mem.Count == 0) return true; // entry was evicted (future eviction policy)
                // Score must have decreased below the acquisition high-water mark.
                return mem.ThreatScores[0] < scoreAtAcquisition;
            },
            DecayTimeoutMs / PumpSleepMs);

        Assert.True(scoreDecayed,
            $"After SensorContactList is cleared (Lost state), CgfThreatEvaluationSystem must " +
            $"stop boosting TargetMemory. The decay rate " +
            $"({PerceptionConstants.ThreatScoreDecayPerSecond * 100f:F0}% per second) must " +
            $"reduce the score below {scoreAtAcquisition:F1} within {DecayTimeoutMs} ms.");
    }
}