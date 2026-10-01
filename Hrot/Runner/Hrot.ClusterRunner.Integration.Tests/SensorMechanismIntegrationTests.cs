using System;
using System;
using System.Threading;
using Fdp.Core;
using Fdp.Toolkit.Perception;
using Fdp.Toolkit.Perception.Components;
using Fdp.Toolkit.Perception.Events;
using Hrot.Map.Common;
using Xunit;

namespace Hrot.ClusterRunner.Integration.Tests;

/// <summary>
/// Integration tests for the sensor mechanism end-to-end pipeline.
///
/// <para>
/// Proves that the full CQRS sensor pipeline works:
/// <list type="number">
///   <item>SimHost <c>TargetVisibleEvent</c> sightings drive <c>SensorTrackDebounceSystem</c> to an
///     Acquired transition, whose <c>SensorTrackStateEvent</c> <c>SensorTrackStateEgressTranslator</c>
///     publishes as a <c>SensorTrackState</c> DDS sample.</item>
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
    /// SimHost sightings -> debounce (Acquired) -> DDS SensorTrackState ->
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

        // ⭐⭐ CE-158 — drive a real SIGHTING STREAM, not an end state. SensorTrackStateEgressTranslator forwards
        //   SensorTrackStateEvent, which SensorTrackDebounceSystem emits only on a TRANSITION
        //   (Pending/Lost → Acquired when a TargetVisibleEvent lands this tick; Acquired → Lost after
        //   TrackLostThresholdTicks of silence). ⛔ This test used to inject a SensorContactList already in
        //   state Acquired with LastSeenTick = 1 — a contact that can only AGE into Lost — so no Acquired
        //   event ever crossed the wire and ActiveSensorTracks stayed empty (measured 2026-10-01).
        var simBus = harness.SimHost.World!.Bus;
        bool sighting = true;
        bool Sighted(Func<bool> condition)
        {
            if (sighting)
                simBus.Publish(new TargetVisibleEvent { Observer = observerSimEntity, Target = targetSimEntity });
            return condition();
        }

        // Diagnostic step 2: wait for CGF entity to gain ActiveSensorTracks.
        bool activeSensorTracksPopulated = harness.PumpUntil(
            () => Sighted(() =>
            {
                var cgfWorld = harness.Cgf!.World;
                if (cgfWorld == null || !cgfWorld.IsAlive(cgfObserverEntity)) return false;
                if (!cgfWorld.HasComponent<ActiveSensorTracks>(cgfObserverEntity)) return false;
                var tracks = cgfWorld.GetComponent<ActiveSensorTracks>(cgfObserverEntity);
                return tracks.Count > 0;
            }),
            SensorPipelineTimeoutMs / PumpSleepMs);

        Assert.True(activeSensorTracksPopulated,
            "CGF entity must gain ActiveSensorTracks with Count > 0 after SensorTrackState(Acquired) " +
            "is transmitted from SimHost. Pipeline: TargetVisibleEvent -> SensorTrackDebounceSystem -> SensorTrackStateEgressTranslator " +
            "-> DDS -> SensorTrackStateIngressTranslator -> ActiveSensorTracks.");

        // Diagnostic: verify CGF entity has TargetMemory (required for ThreatEvaluationSystem).
        Assert.True(harness.Cgf!.World!.HasComponent<TargetMemory>(cgfObserverEntity),
            "CGF observer entity must have TargetMemory component (added by M1Abrams blueprint via WithCombat).");

        // ── Assert: CGF TargetMemory is populated after the sensor pipeline fires ──────
        //
        // Pipeline: ActiveSensorTracks -> CgfThreatEvaluationSystem (boost 50/s) -> TargetMemory.Count > 0
        bool targetMemoryPopulated = harness.PumpUntil(
            () => Sighted(() =>
            {
                var cgfWorld = harness.Cgf!.World;
                if (cgfWorld == null || !cgfWorld.IsAlive(cgfObserverEntity)) return false;
                if (!cgfWorld.HasComponent<TargetMemory>(cgfObserverEntity))  return false;
                var mem = cgfWorld.GetComponent<TargetMemory>(cgfObserverEntity);
                // First check: at least one entry must exist.
                return mem.Count > 0;
            }),
            SensorPipelineTimeoutMs / PumpSleepMs);

        Assert.True(targetMemoryPopulated,
            "CGF TargetMemory must be populated (Count > 0) after " +
            "the SimHost observer starts sighting the target. " +
            "The pipeline: TargetVisibleEvent -> SensorTrackDebounceSystem -> SensorTrackStateEgressTranslator -> " +
            "DDS SensorTrackState(Acquired) -> SensorTrackStateIngressTranslator -> " +
            "ActiveSensorTracks -> CgfThreatEvaluationSystem -> TargetMemory must fire.");

        // Wait for the continuous boost to accumulate a positive threat score.
        // The boost rate is 50 threat-score units per second; even at minimum DeltaTime
        // a non-zero score must appear within a few frames.
        bool scorePositive = harness.PumpUntil(
            () => Sighted(() =>
            {
                var cgfWorld = harness.Cgf!.World;
                if (cgfWorld == null || !cgfWorld.IsAlive(cgfObserverEntity)) return false;
                if (!cgfWorld.HasComponent<TargetMemory>(cgfObserverEntity))  return false;
                var mem = cgfWorld.GetComponent<TargetMemory>(cgfObserverEntity);
                return mem.Count > 0 && mem.ThreatScores[0] > 0f;
            }),
            SensorPipelineTimeoutMs / PumpSleepMs);

        Assert.True(scorePositive,
            "CgfThreatEvaluationSystem must boost ThreatScores[0] to a positive value " +
            "while ActiveSensorTracks is populated. Boost rate = 50 * deltaTime per second.");

        // Capture the score at the high-water mark for decay comparison.
        float scoreAtAcquisition = harness.Cgf!.World!.GetComponent<TargetMemory>(cgfObserverEntity).ThreatScores[0];

        // ── Assert: decay logic runs after the contact is cleared ────────────────────
        //
        // Stop sighting: after TrackLostThresholdTicks of silence the debounce emits Acquired → Lost, the
        // egress sends SensorTrackState(Lost), the CGF ingress drops the track, and only decay applies.
        sighting = false;

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
            $"After the sightings stop (track Lost), CgfThreatEvaluationSystem must " +
            $"stop boosting TargetMemory. The decay rate " +
            $"({PerceptionConstants.ThreatScoreDecayPerSecond * 100f:F0}% per second) must " +
            $"reduce the score below {scoreAtAcquisition:F1} within {DecayTimeoutMs} ms.");
    }
}