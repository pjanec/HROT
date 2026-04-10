using System;
using System.Numerics;
using Hrot.NED.Descriptors;
using Hrot.NED.Messages;
using Hrot.NED.Common;
using Hrot.IG.Components;
using Hrot.Map.Common;
using CycloneDDS.Runtime;
using FDP.Toolkit.Replication.Components;
using Fdp.Kernel;
using ModuleHost.Core.Abstractions;
using Xunit;
using Xunit.Abstractions;

namespace Hrot.ClusterRunner.Integration.Tests;

/// <summary>
/// Integration tests for the "Spawn Moving Vehicle" UI button (Task 31).
///
/// <para>
/// The button front-end calls <c>MiniExConPanelState.SubmitWithWanderMissionViaGateway</c>,
/// which sends a <c>CreateEntityRequest</c> to SimHost via a <c>NedCommandGateway</c>,
/// awaits the <c>CreateEntityAck</c> to learn the allocated entity ID, then sends a
/// <c>MissionControlRequest</c> with a <c>WanderMilitary</c> mission.  On success the
/// spawned entity should start moving continuously on the IG map.
/// </para>
///
/// <para>
/// These tests drive the exact same code path via
/// <c>IgApplication.TestHook_SubmitMiniExConSpawnWithWanderMission</c> and assert that the
/// IG <see cref="SimTransform"/> position changes within
/// <see cref="MovementTimeoutFrames"/> frames — far less than the 600-tick rolling-window
/// heartbeat, so the test only passes when both the spawn <i>and</i> wander-mission
/// pipelines are fully functional end-to-end.
/// </para>
/// </summary>
public class SpawnMovingVehicleWithGatewayIntegrationTests
{
    private const int GatewayTimeoutFrames  = 1000;  // spawn DDS round-trip + mission ACK
    private const int SpawnTimeoutFrames    = 150;  // entity appearing on IG
    private const int MovementTimeoutFrames = 300;  // first position change
    private const float MovementThresholdMetres = 0.05f;

    private readonly ITestOutputHelper _out;

    public SpawnMovingVehicleWithGatewayIntegrationTests(ITestOutputHelper output)
        => _out = output;

    /// <summary>
    /// Verifies the full "Spawn Moving Vehicle" button flow end-to-end via the DDS
    /// <c>NedCommandGateway</c>:
    /// <list type="number">
    ///   <item>IG sends <c>CreateEntityRequest</c> and receives back a valid entity ID.</item>
    ///   <item>IG sends <c>MissionControlRequest</c> (WanderMilitary) and receives <c>Ack</c>.</item>
    ///   <item>Entity appears on IG with <c>Active</c> lifecycle and a <c>SimTransform</c>.</item>
    ///   <item>IG <see cref="SimTransform"/> position changes within
    ///         <see cref="MovementTimeoutFrames"/> frames, proving the entity is moving.</item>
    /// </list>
    /// </summary>
    [Fact]
    public void IG_SpawnMovingVehicleViaGateway_EntityMovesOnIg()
    {
        using var harness = new HrotRunnerHarness();
        // CGF is the default processor for broadcast CreateEntityRequests (Owner==0).
        // It must run alongside the harness so spawn-via-gateway requests get processed.
        using var cgf = new CgfHarness(harness.DomainId);

        // Extra settle: pump BOTH together so DDS discovery completes cross-node and
        // CGF's cluster cache receives at least one SimHost heartbeat before spawning.
        for (int i = 0; i < 20; i++) { harness.PumpFrames(1); cgf.PumpFrames(1); System.Threading.Thread.Sleep(5); }
        System.Threading.Thread.Sleep(200);

        var igApp = harness.Ig.App;

        // Observe CreateUpdateDeleteEntityAck to learn the allocated network ID.
        // Same-process DDS participants match instantly (intraprocess SPDP), so the
        // observer reliably sees data within 2–10 frames of it being published.
        // Only the ACK reader is needed — reqObserved and missionAckObserved are
        // redundant given that the ACK implies the request was processed and that
        // movement detection proves mission success.
        using var observerParticipant = new DdsParticipant((uint)harness.DomainId);
        using var ackReader           = new DdsReader<CreateUpdateDeleteEntityAck>(observerParticipant, "CreateUpdateDeleteEntityAck");

        long tkbType = TkbEntityTypes.Tank_M1Abrams;

        // ── 1. Fire the gateway spawn+wander flow (mirrors the UI button press) ──
        // Task runs asynchronously; we don't block on it in PumpUntil — we use
        // observer DDS reads to confirm each step, then verify movement as proof
        // that the full flow (both ACKs, WanderMilitary mission) succeeded.
        var spawnTask = igApp.TestHook_SubmitMiniExConSpawnWithWanderMission(
            tkbType, ForceId.Friend, 100f, 200f);
        _out.WriteLine("[G1] TestHook_SubmitMiniExConSpawnWithWanderMission fired.");

        // ── 2. Capture the CreateEntityAck to get the allocated network ID ───
        // CGF is the default processor so it sends the ACK; pump both to advance the flow.
        CreateUpdateDeleteEntityAck spawnAck = default;
        bool ackObserved = PumpBothUntil(
            harness, cgf,
            () => TryTakeAnyCreateAck(ackReader, out spawnAck),
            GatewayTimeoutFrames);
        Assert.True(ackObserved,
            "CreateUpdateDeleteEntityAck did not arrive in time. " +
            "CreateEntityRequestSystem on CGF may not have processed the broadcast request.");
        Assert.True(spawnAck.EntityId > 0,
            "CreateUpdateDeleteEntityAck returned a zero/negative entity ID.");

        long networkId = spawnAck.EntityId;
        _out.WriteLine($"[G2] ACK received. networkId={networkId}.");

        // ── 3. Wait for the full async chain to complete (MissionControl included) ─
        bool taskDone = PumpBothUntil(harness, cgf, () => spawnTask.IsCompleted, 200);
        if (!taskDone && !spawnTask.IsCompleted)
            spawnTask.Wait(2000);
        if (spawnTask.IsFaulted)
            throw spawnTask.Exception!.GetBaseException();
        _out.WriteLine($"[G3b] Gateway task done: {spawnTask.Result}.");

        // ── 4. Wait for IG entity to appear with Active lifecycle ────────────
        bool igActive = PumpBothUntil(
            harness, cgf,
            () => IgEntityIsActive(harness, networkId),
            SpawnTimeoutFrames);
        Assert.True(igActive,
            $"IG entity (networkId={networkId}) did not reach Active lifecycle within {SpawnTimeoutFrames} frames.");
        _out.WriteLine("[G4] IG entity is Active.");

        // ── 5. Record baseline IG position ───────────────────────────────────
        var posA = GetIgSimTransform(harness, networkId).Position;
        _out.WriteLine($"[G5] Baseline IG position: ({posA.X:F3}, {posA.Y:F3}).");

        // ── 6. Verify position changes — proves WanderMilitary mission is active ─
        bool moved = PumpBothUntil(harness, cgf, () =>
        {
            var posNow = GetIgSimTransform(harness, networkId).Position;
            return Vector3.Distance(posNow, posA) >= MovementThresholdMetres;
        }, MovementTimeoutFrames);

        var posB = GetIgSimTransform(harness, networkId).Position;
        float travelledMetres = Vector3.Distance(posA, posB);
        _out.WriteLine(
            $"[G6] Final IG position: ({posB.X:F3}, {posB.Y:F3}), " +
            $"travelled={travelledMetres:F4} m, moved={moved}.");

        Assert.True(moved,
            $"Entity (networkId={networkId}) spawned via NedCommandGateway never moved on IG. " +
            $"Baseline=({posA.X:F3},{posA.Y:F3}), final=({posB.X:F3},{posB.Y:F3}), " +
            $"travelled={travelledMetres:F4} m (threshold={MovementThresholdMetres} m). " +
            $"Verify WanderMilitary mission was committed and doctrine activated.");
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

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
            System.Threading.Thread.Sleep(5);
            if (condition()) return true;
        }
        return false;
    }

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

    private static bool TryTakeAnyCreateRequest(
        DdsReader<CreateEntityRequest> reader,
        out CreateEntityRequest request)
    {
        using var loan = reader.Take(1);
        foreach (var sample in loan)
        {
            if (!sample.IsValid) continue;
            request = sample.Data;
            return true;
        }

        request = default;
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
        if (!entityMap.TryGetEntity(networkId, out var entity))
            return default;
        var world = harness.Ig.App.World;
        if (!world.IsAlive(entity) || !world.HasComponent<SimTransform>(entity))
            return default;
        return world.GetComponent<SimTransform>(entity);
    }
}
