using System;
using System.Collections.Generic;
using System.Numerics;
using Fdp.Examples.Common.Systems;
using Fdp.Core;
using Fdp.Toolkit.Replication.Components;
using Fdp.ModuleHost.Abstractions;
using Xunit;

namespace Hrot.IG.Tests;

/// <summary>
/// P3D-104 — Dead-reckoning / replication regression probe (Design §6 Axis-3, §8).
///
/// <para>Tier 1 removed the wall that kept a moving terrain altitude out of dead-reckoning, so the
/// authoritative <c>SimTransform.Position.Z</c> now changes as an entity traverses sloped/stepped
/// terrain. This fixture drives a <em>remote</em> entity through <see cref="TransformSyncSystem"/>
/// (the <c>driveFromNetwork</c> path, P3D-103) against a truth trajectory that ramps up a slope and
/// then climbs a stepped deck, asserting:</para>
/// <list type="number">
///   <item>On the ramp, predicted Z tracks authoritative Z within tolerance every tick.</item>
///   <item>On a step transition, the smoothing never overshoots/oscillates past the target.</item>
///   <item>Replication converges to the authoritative Z and stays there (no jitter/divergence).</item>
/// </list>
///
/// <para><b>Tolerance bands (documented):</b> the system lerps toward the network position at
/// <c>rate × dt = 10 × (1/60) ≈ 0.167</c> per tick. For the ramp (Δz ≈ <see cref="RampDzPerTick"/>
/// per tick) the steady-state lag is <c>Δz·(1−f)/f ≈ 0.083 m</c>, so <see cref="RampTolerance"/> =
/// 0.25 m comfortably bounds it after a short warm-up while still failing if a flat-Z assumption
/// silently re-enters prediction (which would peg predicted Z at 0 and blow past the band).</para>
/// </summary>
public sealed class DeadReckoningSlopeStepTests
{
    private const float Dt            = 1f / 60f;
    private const float SmoothingRate = 10f;             // must match TransformSyncSystem.SMOOTHING_RATE
    private const float RampDzPerTick = 0.02f;           // ramp slope 0.2 × 0.1 m/tick
    private const float RampTolerance = 0.25f;
    private const float StepTolerance = 0.02f;

    private static EntityRepository CreateWorld()
    {
        var repo = new EntityRepository();
        repo.RegisterComponent<SimTransform>();
        repo.RegisterComponent<NetworkTransform>();
        repo.RegisterComponent<NetworkAuthority>();
        return repo;
    }

    private static void Playback(EntityRepository repo)
    {
        if (((ISimulationView)repo).GetCommandBuffer() is EntityCommandBuffer ecb)
            ecb.Playback(repo);
    }

    /// <summary>
    /// Builds the truth trajectory: a ramp (X and Z rising together) followed by a stepped deck
    /// (X holds, Z steps up in fixed increments and holds between steps).
    /// </summary>
    private static List<Vector3> BuildTruthTrajectory()
    {
        var truth = new List<Vector3>();

        // Ramp: 150 ticks, X advances 0.1 m/tick, Z = 0.2 × X.
        float x = 0f;
        for (int i = 0; i < 150; i++)
        {
            x += 0.1f;
            truth.Add(new Vector3(x, 0f, 0.2f * x)); // Z rises with the slope
        }

        // Stepped deck: X holds; three +1 m steps at 60-tick intervals, holding between.
        float z = truth[^1].Z;
        float deckX = x;
        for (int step = 0; step < 3; step++)
        {
            z += 1.0f; // discrete step up
            for (int i = 0; i < 60; i++)
                truth.Add(new Vector3(deckX, 0f, z));
        }
        return truth;
    }

    [Fact]
    public void RemoteSmoothing_TracksAuthoritativeZ_OnSlopeAndSteps_NoOvershoot()
    {
        using var repo = CreateWorld();
        var truth = BuildTruthTrajectory();

        var entity = repo.CreateEntity();
        repo.AddComponent(entity, new SimTransform { Position = Vector3.Zero, Rotation = Quaternion.Identity });
        repo.AddComponent(entity, new NetworkTransform { LastPosition = Vector3.Zero, LastRotation = Quaternion.Identity });
        repo.AddComponent(entity, new NetworkAuthority(primaryOwnerId: 2, localNodeId: 1)); // remote

        var system = new TransformSyncSystem(driveFromNetwork: true);

        const int rampLen = 150;
        const int warmup  = 30; // allow the lerp to reach steady-state lag before asserting the band

        float prevZ = 0f;
        for (int tick = 0; tick < truth.Count; tick++)
        {
            // Authoritative truth arrives from the network for this tick.
            repo.SetComponent(entity, new NetworkTransform
            {
                LastPosition = truth[tick],
                LastRotation = Quaternion.Identity,
            });

            system.Execute((ISimulationView)repo, Dt);
            Playback(repo);

            float z       = repo.GetComponent<SimTransform>(entity).Position.Z;
            float targetZ = truth[tick].Z;

            // (1) Ramp: predicted Z tracks authoritative Z within the documented band.
            if (tick >= warmup && tick < rampLen)
            {
                Assert.True(MathF.Abs(z - targetZ) <= RampTolerance,
                    $"Ramp tick {tick}: predicted Z={z:F4} vs authoritative {targetZ:F4} exceeded ±{RampTolerance}");
            }

            // (2) No overshoot: a lerp toward a higher target never exceeds it (monotone, non-oscillating).
            Assert.True(z <= targetZ + StepTolerance,
                $"Tick {tick}: predicted Z={z:F4} overshot target {targetZ:F4}");
            Assert.True(z >= prevZ - StepTolerance,
                $"Tick {tick}: predicted Z={z:F4} regressed below previous {prevZ:F4} (oscillation)");

            prevZ = z;
        }

        // (3) Replication converges to the final authoritative deck altitude and stays there.
        float finalTruthZ = truth[^1].Z;
        float finalZ      = repo.GetComponent<SimTransform>(entity).Position.Z;
        Assert.True(MathF.Abs(finalZ - finalTruthZ) <= StepTolerance,
            $"Final Z={finalZ:F4} did not converge to authoritative {finalTruthZ:F4}");
    }
}
