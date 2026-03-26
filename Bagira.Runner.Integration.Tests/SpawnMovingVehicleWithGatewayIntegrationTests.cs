using System;
using System.Numerics;
using Bagira.BDC.SSTD;
using Bagira.BDC.SSTM;
using Bagira.DDS.DM;
using Bagira.IG.Components;
using Bagira.Map.Common;
using CycloneDDS.Runtime;
using FDP.Toolkit.Replication.Components;
using Fdp.Kernel;
using ModuleHost.Core.Abstractions;
using Xunit;
using Xunit.Abstractions;

namespace Bagira.Runner.Integration.Tests;

/// <summary>
/// Integration tests for the "Spawn Moving Vehicle" UI button (Task 31).
///
/// <para>
/// The button front-end calls <c>MiniIosPanelState.SubmitWithWanderMissionViaGateway</c>,
/// which sends a <c>CreateEntityRequest</c> to SimHost via a <c>BdcCommandGateway</c>,
/// awaits the <c>CreateEntityAck</c> to learn the allocated entity ID, then sends a
/// <c>MissionControlRequest</c> with a <c>WanderMilitary</c> mission.  On success the
/// spawned entity should start moving continuously on the IG map.
/// </para>
///
/// <para>
/// These tests drive the exact same code path via
/// <c>IgApplication.TestHook_SubmitMiniIosSpawnWithWanderMission</c> and assert that the
/// IG <see cref="SimTransform"/> position changes within
/// <see cref="MovementTimeoutFrames"/> frames — far less than the 600-tick rolling-window
/// heartbeat, so the test only passes when both the spawn <i>and</i> wander-mission
/// pipelines are fully functional end-to-end.
/// </para>
/// </summary>
public class SpawnMovingVehicleWithGatewayIntegrationTests
{
    private const int GatewayTimeoutFrames  = 250;  // spawn DDS round-trip + mission ACK
    private const int SpawnTimeoutFrames    = 150;  // entity appearing on IG
    private const int MovementTimeoutFrames = 300;  // first position change
    private const float MovementThresholdMetres = 0.05f;

    private readonly ITestOutputHelper _out;

    public SpawnMovingVehicleWithGatewayIntegrationTests(ITestOutputHelper output)
        => _out = output;

    /// <summary>
    /// Verifies the full "Spawn Moving Vehicle" button flow end-to-end via the DDS
    /// <c>BdcCommandGateway</c>:
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
        using var harness = new BagiraRunnerHarness();

        var igApp = harness.Ig.App;

        // Observe CreateEntityAck to learn the allocated network ID,
        // and MissionControlAck to assert there is no ERR_ENTITY_NOT_FOUND race.
        using var observerParticipant = new DdsParticipant((uint)harness.DomainId);
        using var reqReader           = new DdsReader<CreateEntityRequest>(observerParticipant, "CreateEntityRequest");
        using var ackReader           = new DdsReader<CreateUpdateDeleteEntityAck>(observerParticipant, "CreateUpdateDeleteEntityAck");
        using var missionAckReader    = new DdsReader<MissionControlAck>(observerParticipant, "MissionControlAck");

        long tkbType = TkbEntityTypes.Tank_M1Abrams;

        // ── 1. Fire the gateway spawn+wander flow (mirrors the UI button press) ──
        // Do NOT await — PumpUntil drives the DDS event loop that lets the async
        // Task progress through CreateEntityAck → MissionControlAck.
        var spawnTask = igApp.TestHook_SubmitMiniIosSpawnWithWanderMission(
            tkbType, ForceId.Friend, 100f, 200f);
        _out.WriteLine("[G1] TestHook_SubmitMiniIosSpawnWithWanderMission fired.");

        // ── 2. Capture the CreateEntityRequest to correlate with the Ack ─────
        CreateEntityRequest spawnReq = default;
        bool reqObserved = harness.PumpUntil(
            () => TryTakeAnyCreateRequest(reqReader, out spawnReq),
            GatewayTimeoutFrames);
        Assert.True(reqObserved,
            "CreateEntityRequest did not reach DDS in time. " +
            "BdcCommandGateway or DDS writer may not be initialised.");

        // ── 3. Capture the Phase-2 Success ACK to get the allocated network ID ───
        CreateUpdateDeleteEntityAck spawnAck = default;
        bool ackObserved = harness.PumpUntil(
            () => RunnerTestHelpers.TryTakeCreateAck(ackReader, spawnReq.RequestId, out spawnAck),
            GatewayTimeoutFrames);
        Assert.True(ackObserved,
            $"CreateUpdateDeleteEntityAck for requestId={spawnReq.RequestId} did not arrive in time. " +
            $"CreateEntityRequestSystem on SimHost may not have processed the request.");
        Assert.True(spawnAck.StatusCode < (int)SstStatusCode.UnknownDescriptorType,
            $"Expected Success or InProgress status, got {spawnAck.StatusCode}.");;
        Assert.True(spawnAck.EntityId > 0,
            "CreateUpdateDeleteEntityAck returned a zero/negative entity ID.");

        long networkId = spawnAck.EntityId;
        _out.WriteLine($"[G2] Allocated networkId={networkId}.");

        // ── 4. Wait for the gateway Task to complete (both spawn + mission ACKs) ─
        bool taskComplete = harness.PumpUntil(() => spawnTask.IsCompleted, GatewayTimeoutFrames);
        Assert.True(taskComplete,
            $"SubmitWithWanderMissionViaGateway Task did not complete within {GatewayTimeoutFrames} frames. " +
            $"MissionControlAck may not have been received by the gateway.");
        if (spawnTask.IsFaulted)
            throw spawnTask.Exception!.GetBaseException();
        _out.WriteLine("[G3] Gateway Task completed.");

        // ── 4b. Verify MissionControlAck arrived with Error=0 ────────────────
        // This assertion specifically guards against the "ERR_ENTITY_NOT_FOUND" race where
        // MissionControlRequest arrives before NetworkSpawningSystem has registered the
        // entity in NetworkEntityMap (Error=2). After the MissionControlRequestSystem
        // retry-queue fix, the retry loop handles the 1-2 frame lag so the ACK must
        // always come back with ErrorCode=0.
        MissionControlAck missionAck = default;
        bool missionAckObserved = harness.PumpUntil(
            () => TryTakeMissionAck(missionAckReader, networkId, out missionAck),
            GatewayTimeoutFrames);
        Assert.True(missionAckObserved,
            $"MissionControlAck for entity {networkId} did not arrive on DDS. " +
            $"MissionControlRequestSystem may not have processed the request.");
        Assert.True(missionAck.ErrorCode == 0,
            $"MissionControlAck returned Error={missionAck.ErrorCode} (ErrorMessage='{missionAck.ErrorMessage}'). " +
            $"Expected 0. This is the 'ERR_ENTITY_NOT_FOUND' race: MissionControlRequest arrived " +
            $"before NetworkSpawningSystem registered the entity. " +
            $"Fix: retry queue in MissionControlRequestSystem should have prevented this.");
        _out.WriteLine($"[G3b] MissionControlAck received: Error={missionAck.ErrorCode}.");

        // ── 5. Wait for IG entity to appear with Active lifecycle ────────────
        bool igActive = harness.PumpUntil(
            () => IgEntityIsActive(harness, networkId),
            SpawnTimeoutFrames);
        Assert.True(igActive,
            $"IG entity (networkId={networkId}) did not reach Active lifecycle within {SpawnTimeoutFrames} frames.");
        _out.WriteLine("[G4] IG entity is Active.");

        // ── 6. Record baseline IG position ───────────────────────────────────
        var posA = GetIgSimTransform(harness, networkId).Position;
        _out.WriteLine($"[G5] Baseline IG position: ({posA.X:F3}, {posA.Y:F3}).");

        // ── 7. Verify position changes — entity must be moving ───────────────
        // The WanderMilitary doctrine drives CarKinematicsSystem which updates
        // SimTransform every tick; GeoSpatialEgressTranslator publishes the change
        // immediately via shadow-state comparison.
        bool moved = harness.PumpUntil(() =>
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
            $"Entity (networkId={networkId}) spawned via BdcCommandGateway never moved on IG. " +
            $"Baseline=({posA.X:F3},{posA.Y:F3}), final=({posB.X:F3},{posB.Y:F3}), " +
            $"travelled={travelledMetres:F4} m (threshold={MovementThresholdMetres} m). " +
            $"Verify WanderMilitary mission was committed and doctrine activated " +
            $"(Task 31: CreateEntity DDS → SimHost → GeoSpatial DDS → IG).");
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

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

    private static bool TryTakeMissionAck(
        DdsReader<MissionControlAck> reader,
        long networkId,
        out MissionControlAck ack)
    {
        // MissionControlAck carries only RequestId (no EntityId), so we take the first
        // valid sample.  The test domain is isolated — no other mission requests are
        // in-flight — so the first ACK belongs to our entity.
        _ = networkId; // unused; kept in signature for readability

        using var loan = reader.Take(20);
        foreach (var sample in loan)
        {
            if (!sample.IsValid) continue;

            ack = sample.Data;
            return true;
        }

        ack = default;
        return false;
    }

    private static bool IgEntityIsActive(BagiraRunnerHarness harness, long networkId)
    {
        var entityMap = harness.Ig.App.TestHook_EntityMap;
        if (!entityMap.TryGetEntity(networkId, out var entity)) return false;
        var world = harness.Ig.App.World;
        if (!world.IsAlive(entity)) return false;
        return world.GetLifecycleState(entity) == EntityLifecycle.Active;
    }

    private static SimTransform GetIgSimTransform(BagiraRunnerHarness harness, long networkId)
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
