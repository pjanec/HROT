using System;
using System.Numerics;
using System.Threading;
using Hrot.ClusterRunner.Configuration;
using Hrot.ClusterRunner.Services;
using RunMode = Hrot.ClusterRunner.Configuration.RunMode;
using Hrot.IG.Components;
using Hrot.Map.Common;
using Hrot.NED.Common;
using Hrot.NED.Messages;
using CycloneDDS.Runtime;
using FDP.Toolkit.Replication.Components;
using Fdp.Kernel;
using ModuleHost.Core.Abstractions;
using Xunit;
using Xunit.Abstractions;

namespace Hrot.ClusterRunner.Integration.Tests;

/// <summary>
/// Integration test verifying that "Spawn Moving Vehicle" (MiniExCon gateway flow)
/// produces a moving entity when all five subsystems are running together
/// (Orchestrator + SimHost + IG + ExCon + CGF), replicating "clusterrunner -m all".
///
/// <para>
/// <b>Regression guard:</b> A bug was introduced during the HROT Editor design-batch
/// implementation whereby the entity is spawned but does NOT start moving when CGF
/// is present on the same DDS domain.  This test must pass for the fix to be
/// considered complete.
/// </para>
/// </summary>
public sealed class AllSubsystemsSpawnMovingVehicleTests
{
    // Domain range: 150–159 — between HrotRunnerHarness auto-range (100–145) and
    // AllSubsystemsClusterTransitionTests (160+). CycloneDDS max domain is 232.
    private const int DomainBase = 150;
    private static int _domainCounter = DomainBase - 1;

    private const int GatewayTimeoutFrames  = 1000;
    private const int SpawnTimeoutFrames    = 150;
    private const int MovementTimeoutFrames = 300;
    private const float MovementThresholdMetres = 0.05f;
    private const int PumpSleepMs = 5;

    private readonly ITestOutputHelper _out;

    public AllSubsystemsSpawnMovingVehicleTests(ITestOutputHelper output)
        => _out = output;

    /// <summary>
    /// Verifies the full "Spawn Moving Vehicle" button flow with all five subsystems
    /// active (Orchestrator, SimHost, IG, ExCon, CGF) — the same topology as
    /// "clusterrunner -m all".
    /// <list type="number">
    ///   <item>IG sends <c>CreateEntityRequest</c> and receives a valid entity ID.</item>
    ///   <item>IG sends <c>MissionControlRequest</c> (WanderMilitary) and receives <c>Ack</c>.</item>
    ///   <item>Entity appears on IG with <c>Active</c> lifecycle and a <c>SimTransform</c>.</item>
    ///   <item>IG <see cref="SimTransform"/> position changes within
    ///         <see cref="MovementTimeoutFrames"/> frames, proving the entity is moving
    ///         even with CGF present.</item>
    /// </list>
    /// </summary>
    [Fact]
    public void AllSubsystems_SpawnMovingVehicleViaGateway_EntityMovesOnIg()
    {
        int domainId = Interlocked.Increment(ref _domainCounter);
        _out.WriteLine($"[A0] Domain: {domainId}");

        // ── Boot all five subsystems ──────────────────────────────────────────
        // HrotRunnerHarness(RunMode.All, domainId) adds: Orchestrator + SimHost + IG + ExCon.
        // CgfHarness(domainId) adds: CGF on the same DDS domain.
        using var harness = new HrotRunnerHarness(RunMode.All, domainId);
        using var cgf     = new CgfHarness(domainId);

        // Extra settle: give CGF DDS discovery time to find SimHost's topics.
        PumpBoth(harness, cgf, 20);
        Thread.Sleep(200);

        var igApp = harness.Ig.App;

        using var observerParticipant = new DdsParticipant((uint)domainId);
        using var ackReader = new DdsReader<CreateUpdateDeleteEntityAck>(observerParticipant, "CreateUpdateDeleteEntityAck");

        long tkbType = TkbEntityTypes.Tank_M1Abrams;

        // ── 1. Fire the gateway spawn+wander flow ─────────────────────────────
        var spawnTask = igApp.TestHook_SubmitMiniExConSpawnWithWanderMission(
            tkbType, ForceId.Friend, 100f, 200f);
        _out.WriteLine("[A1] TestHook_SubmitMiniExConSpawnWithWanderMission fired.");

        // ── 2. Capture the CreateEntityAck ───────────────────────────────────
        CreateUpdateDeleteEntityAck spawnAck = default;
        bool ackObserved = PumpBothUntil(
            harness, cgf,
            () => TryTakeAnyCreateAck(ackReader, out spawnAck),
            GatewayTimeoutFrames);
        Assert.True(ackObserved,
            "CreateUpdateDeleteEntityAck did not arrive in time. " +
            "CreateEntityRequestSystem on SimHost may not have processed the request.");
        Assert.True(spawnAck.EntityId > 0,
            "CreateUpdateDeleteEntityAck returned a zero/negative entity ID.");

        long networkId = spawnAck.EntityId;
        _out.WriteLine($"[A2] ACK received. networkId={networkId}.");

        // ── 3. Wait for the full async chain (MissionControl included) ────────
        bool taskDone = PumpBothUntil(harness, cgf, () => spawnTask.IsCompleted, 200);
        if (!taskDone && !spawnTask.IsCompleted)
            spawnTask.Wait(2000);
        if (spawnTask.IsFaulted)
            throw spawnTask.Exception!.GetBaseException();
        _out.WriteLine($"[A3] Gateway task done: {spawnTask.Result}.");

        // ── 4. Wait for IG entity to appear with Active lifecycle ─────────────
        bool igActive = PumpBothUntil(
            harness, cgf,
            () => IgEntityIsActive(harness, networkId),
            SpawnTimeoutFrames);
        Assert.True(igActive,
            $"IG entity (networkId={networkId}) did not reach Active lifecycle within {SpawnTimeoutFrames} frames.");
        _out.WriteLine("[A4] IG entity is Active.");

        // ── 5. Record baseline IG position ───────────────────────────────────
        var posA = GetIgSimTransform(harness, networkId).Position;
        _out.WriteLine($"[A5] Baseline IG position: ({posA.X:F3}, {posA.Y:F3}).");

        // ── 6. Verify position changes ────────────────────────────────────────
        bool moved = PumpBothUntil(harness, cgf, () =>
        {
            var posNow = GetIgSimTransform(harness, networkId).Position;
            return Vector3.Distance(posNow, posA) >= MovementThresholdMetres;
        }, MovementTimeoutFrames);

        var posB = GetIgSimTransform(harness, networkId).Position;
        float travelledMetres = Vector3.Distance(posA, posB);
        _out.WriteLine(
            $"[A6] Final IG position: ({posB.X:F3}, {posB.Y:F3}), " +
            $"travelled={travelledMetres:F4} m, moved={moved}.");

        Assert.True(moved,
            $"Entity (networkId={networkId}) spawned via NedCommandGateway never moved on IG " +
            $"when all five subsystems (including CGF) are running. " +
            $"Baseline=({posA.X:F3},{posA.Y:F3}), final=({posB.X:F3},{posB.Y:F3}), " +
            $"travelled={travelledMetres:F4} m (threshold={MovementThresholdMetres} m). " +
            $"This is the 'clusterrunner -m all' spawn-vehicle regression.");
    }

    // ── Pump helpers ──────────────────────────────────────────────────────────

    private static void PumpBoth(HrotRunnerHarness harness, CgfHarness cgf, int frames)
    {
        for (int i = 0; i < frames; i++)
        {
            harness.PumpFrames(1);
            cgf.PumpFrames(1);
        }
    }

    private static bool PumpBothUntil(
        HrotRunnerHarness harness,
        CgfHarness        cgf,
        Func<bool>        condition,
        int               timeoutFrames)
    {
        if (condition()) return true;
        for (int i = 0; i < timeoutFrames; i++)
        {
            harness.PumpFrames(1);
            cgf.PumpFrames(1);
            Thread.Sleep(PumpSleepMs);
            if (condition()) return true;
        }
        return false;
    }

    // ── Assertion helpers ─────────────────────────────────────────────────────

    private static bool TryTakeAnyCreateAck(
        DdsReader<CreateUpdateDeleteEntityAck> reader,
        out CreateUpdateDeleteEntityAck ack)
    {
        using var loan = reader.Take(1);
        foreach (var sample in loan)
        {
            if (!sample.IsValid) continue;
            ack = sample.Data;
            return true;
        }
        ack = default;
        return false;
    }

    private static bool IgEntityIsActive(HrotRunnerHarness harness, long networkId)
    {
        var entityMap = harness.Ig.App.TestHook_EntityMap;
        if (!entityMap.TryGetEntity(networkId, out var entity)) return false;
        var world = harness.Ig.App.World;
        if (!world.IsAlive(entity)) return false;
        return world.GetLifecycleState(entity) == EntityLifecycle.Active;
    }

    private static SimTransform GetIgSimTransform(HrotRunnerHarness harness, long networkId)
    {
        var entityMap = harness.Ig.App.TestHook_EntityMap;
        if (!entityMap.TryGetEntity(networkId, out var entity)) return default;
        var world = harness.Ig.App.World;
        if (!world.IsAlive(entity) || !world.HasComponent<SimTransform>(entity)) return default;
        return world.GetComponent<SimTransform>(entity);
    }
}
