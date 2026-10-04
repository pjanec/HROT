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
/// Proves the full sensor pipeline with REAL perception (CE-3038, CE-3050): SimHost's EQS solver runs a unit's visual
/// sensor → its memory stage reports Acquired / Lost → <c>SensorTrackState</c> on the wire → CGF
/// <c>ActiveSensorTracks</c> → <c>TargetMemory</c> boosted, then decaying once the target is out of sight.
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
    /// ⭐⭐ <c>CE-3050</c> / <c>CE-3038</c> — the sensor mechanism END TO END, driven by REAL perception: an M1 and a T-72
    /// (two forces) 100 m apart. SimHost's EQS solver runs the M1's implicit visual sensor, its memory stage reports the
    /// unit's Acquired → <c>SensorTrackState</c> on the wire → CGF <c>ActiveSensorTracks</c> → <c>TargetMemory</c> boosted.
    /// Then the T-72 leaves the perception grid: the memory stage reports Lost and the score decays.
    /// <para>🔴 Before: the rail injected <c>TargetVisibleEvent</c>s on the SimHost WORLD bus, which perception never read
    /// (its sightings lived on a private bus), and spawned two tanks of ONE force, which real perception cannot see —
    /// so it was red on every base. 📄 docs/DESIGN_Sensors_And_Doctrine.md §5.5.</para>
    /// </summary>
    [Fact]
    public unsafe void SensorMechanism_EndToEnd_CGFTargetMemoryPopulatesAndDecays()
    {
        int domainId = Interlocked.Increment(ref _domainCounter);
        using var harness = new HrotRunnerHarness("simhost,cgf", domainId);

        long observerNetId = harness.Cgf!.TestHook_SpawnEntityWithSplitAuthority(TkbEntityTypes.Tank_M1Abrams, muscleNodeId: 1);
        long targetNetId   = harness.Cgf!.TestHook_SpawnEntityWithSplitAuthority(TkbEntityTypes.Tank_T72,     muscleNodeId: 1);

        bool entitiesReady = harness.PumpUntil(
            () =>
            {
                var simMap   = harness.SimHost.TestHook_EntityMap;
                var simWorld = harness.SimHost.World;
                if (simWorld == null) return false;
                if (!simMap.TryGetEntity(observerNetId, out Entity obs) || !simWorld.IsAlive(obs)) return false;
                if (!simWorld.HasAuthority<SimTransform>(obs)) return false;
                if (!simMap.TryGetEntity(targetNetId, out Entity tgt) || !simWorld.IsAlive(tgt)) return false;
                if (!simWorld.HasAuthority<SimTransform>(tgt)) return false;
                var cgfMap = harness.Cgf!.GhostEntityMap;
                return cgfMap != null && cgfMap.TryGetEntity(observerNetId, out _) && cgfMap.TryGetEntity(targetNetId, out _);
            },
            SpawnTimeoutMs / PumpSleepMs);
        Assert.True(entitiesReady, $"Both tanks must be ready on SimHost (SimTransform authority) and CGF within {SpawnTimeoutMs} ms.");

        var sim = harness.SimHost.World!;
        harness.SimHost.TestHook_EntityMap.TryGetEntity(observerNetId, out Entity observerSim);
        harness.SimHost.TestHook_EntityMap.TryGetEntity(targetNetId,   out Entity targetSim);
        harness.Cgf!.GhostEntityMap!.TryGetEntity(observerNetId, out Entity observerCgf);

        void PlaceTarget(float x, float y)
        {
            var tf = sim.GetComponentRO<SimTransform>(targetSim);
            tf.Position = new System.Numerics.Vector3(x, y, tf.Position.Z);
            sim.SetComponent(targetSim, tf);
        }
        var obsPos = sim.GetComponentRO<SimTransform>(observerSim).Position;
        PlaceTarget(obsPos.X + 100f, obsPos.Y);

        Assert.True(Fdp.Toolkit.Perception.Sensors.UnitSensors.Of(sim, observerSim, SensorModality.Visual) != Entity.Null,
            "The M1 must have its implicit visual sensor on SimHost (built from its TKB vision range).");

        bool tracked = harness.PumpUntil(
            () =>
            {
                var cgf = harness.Cgf!.World;
                return cgf != null && cgf.IsAlive(observerCgf) && cgf.HasComponent<ActiveSensorTracks>(observerCgf)
                    && cgf.GetComponent<ActiveSensorTracks>(observerCgf).Count > 0;
            },
            SensorPipelineTimeoutMs / PumpSleepMs);
        Assert.True(tracked, "CGF must gain ActiveSensorTracks once SimHost's visual sensor sees the T-72 (memory stage → " +
            "SensorTrackState(Acquired) → SensorTrackStateIngressTranslator → ActiveSensorTracks). " + Diagnose(sim, observerSim, targetSim));

        bool scored = harness.PumpUntil(
            () =>
            {
                var cgf = harness.Cgf!.World;
                if (cgf == null || !cgf.HasComponent<TargetMemory>(observerCgf)) return false;
                var mem = cgf.GetComponent<TargetMemory>(observerCgf);
                return mem.Count > 0 && mem.ThreatScores[0] > 0f;
            },
            SensorPipelineTimeoutMs / PumpSleepMs);
        Assert.True(scored, "CgfThreatEvaluationSystem must boost TargetMemory while the track is active.");
        // The T-72 leaves the perception grid (far outside its footprint): unseen ⇒ the memory stage reports Lost after its
        // hysteresis window ⇒ CGF drops the track. (The score keeps rising until then, so decay is measured from THAT point.)
        PlaceTarget(obsPos.X + 50_000f, obsPos.Y + 50_000f);
        bool lost = harness.PumpUntil(
            () =>
            {
                var cgf = harness.Cgf!.World;
                return cgf != null && (!cgf.HasComponent<ActiveSensorTracks>(observerCgf) || cgf.GetComponent<ActiveSensorTracks>(observerCgf).Count == 0);
            },
            SensorPipelineTimeoutMs / PumpSleepMs);
        Assert.True(lost, "Once the T-72 is out of sight the memory stage must report Lost and CGF must drop the track. " + Diagnose(sim, observerSim, targetSim));

        float scoreWhenLost = harness.Cgf!.World!.GetComponent<TargetMemory>(observerCgf).ThreatScores[0];
        bool decayed = harness.PumpUntil(
            () =>
            {
                var mem = harness.Cgf!.World!.GetComponent<TargetMemory>(observerCgf);
                return mem.Count == 0 || mem.ThreatScores[0] < scoreWhenLost;
            },
            DecayTimeoutMs / PumpSleepMs);
        Assert.True(decayed, $"With the track lost the score must decay below {scoreWhenLost:F1} within {DecayTimeoutMs} ms " +
            $"({PerceptionConstants.ThreatScoreDecayPerSecond * 100f:F0}%/s).");
    }

    // Every hop on the Muscle, so a red names the one that broke.
    private static unsafe string Diagnose(EntityRepository sim, Entity obs, Entity tgt)
    {
        var sb = new System.Text.StringBuilder("[Muscle] ");
        string Info(Entity e) => (sim.HasComponent<Fdp.Core.EntityInfo>(e) ? $"force={sim.GetComponentRO<Fdp.Core.EntityInfo>(e).ForceId}" : "NO EntityInfo")
            + (sim.HasComponent<SimTransform>(e) ? $" pos={sim.GetComponentRO<SimTransform>(e).Position}" : " NO SimTransform")
            + (sim.HasComponent<PerceptionReceptor>(e) ? $" range={sim.GetComponentRO<PerceptionReceptor>(e).VisionRange}" : " NO receptor");
        sb.Append($"observer: {Info(obs)}; target: {Info(tgt)}; ");
        var s = Fdp.Toolkit.Perception.Sensors.UnitSensors.Of(sim, obs, SensorModality.Visual);
        if (s.IsNull) return sb.Append("NO visual sensor").ToString();
        var es = sim.GetComponentRO<Fdp.Toolkit.Spatial.Eqs.EqsSensor>(s);
        sb.Append($"sensor bp={es.BlueprintId:X} suspended={es.Suspended} epoch={es.Epoch:X}; ");
        sb.Append(sim.HasComponent<Fdp.Toolkit.Spatial.Eqs.SensorEvalState>(s)
            ? $"lastSolved={sim.GetComponentRO<Fdp.Toolkit.Spatial.Eqs.SensorEvalState>(s).LastSolvedTick} cost={sim.GetComponentRO<Fdp.Toolkit.Spatial.Eqs.SensorEvalState>(s).LastCost} (now {((Fdp.ModuleHost.Abstractions.ISimulationView)sim).Tick}); "
            : "NEVER SOLVED; ");
        if (sim.HasComponent<SensorContactList>(s))
        {
            var l = sim.GetComponentRO<SensorContactList>(s);
            sb.Append($"contacts={l.Count}");
            for (int i = 0; i < l.Count; i++) sb.Append($" [{l.EntityIds[i]:X} st={l.State[i]}]");
        }
        else sb.Append("NO contact list");
        sb.Append($"; target packed={tgt.PackedValue:X}");
        return sb.ToString();
    }
}
