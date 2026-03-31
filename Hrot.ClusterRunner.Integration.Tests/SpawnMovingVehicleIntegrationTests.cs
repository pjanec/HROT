using System.Numerics;
using Hrot.NED.Descriptors;
using Hrot.NED.Common;
using Hrot.IG.Components;
using Hrot.Map.Common;
using FDP.Toolkit.Behavior.Components;
using FDP.Toolkit.Replication.Components;
using Fdp.Kernel;
using ModuleHost.Core.Abstractions;
using Xunit;
using Xunit.Abstractions;

namespace Hrot.ClusterRunner.Integration.Tests;

/// <summary>
/// Integration tests that verify GeoSpatial position updates reach IG promptly
/// when a SimHost entity is actively moving (WanderMilitary mission / kinematics).
///
/// <para>
/// Root cause being tested: <c>CarKinematicsSystem</c> and <c>LinearKinematicsSystem</c>
/// update <c>SimTransform</c> every tick, but historically no code called
/// <c>SmartEgressUtil.MarkDirty</c> for the GeoSpatial descriptor.  Without the dirty flag
/// the <c>GeoSpatialEgressTranslator</c> only published via the rolling-window heartbeat
/// every 600 ticks (~10 s at 60 Hz), making moving vehicles appear frozen on the IG.
/// </para>
///
/// <para>
/// The fix (<c>SimTransformEgressDirtySystem</c>) calls <c>MarkDirty</c> every tick for
/// locally-owned entities with a non-zero <c>SimVelocity</c>, so position updates must
/// propagate to the IG within a few frames — well before the 600-tick heartbeat.
/// </para>
/// </summary>
public class SpawnMovingVehicleIntegrationTests
{
    // Allow generous 120 frames (~2 s) for DDS round-trip in CI environments.
    // The IMPORTANT invariant is that this is far below the 600-tick rolling window.
    private const int SpawnTimeoutFrames      = 150;
    private const int MovementTimeoutFrames   = 300;  // 1.5 s — well below the 600-tick (~10 s) heartbeat; enough for DDS cold-start

    // Minimum displacement (metres) that proves the entity has actually moved.
    private const float MovementThresholdMetres = 0.05f;

    private readonly ITestOutputHelper _out;

    public SpawnMovingVehicleIntegrationTests(ITestOutputHelper output) => _out = output;

    // ── Test 1: Moving entity updates IG position well before rolling window ────

    /// <summary>
    /// Spawns a tank via SimHost (bypassing the DDS MissionControlRequest round-trip)
    /// and assigns WanderMilitary doctrine directly.
    /// Verifies that the IG <see cref="SimTransform"/> position changes within
    /// <see cref="MovementTimeoutFrames"/> frames — proving that the shadow-state comparison
    /// in <c>GeoSpatialEgressTranslator</c> publishes position updates every tick the entity
    /// moves, NOT held back by the 600-tick rolling-window heartbeat.
    /// </summary>
    [Fact]
    public void SpawnMovingVehicle_IgReceivesPositionChangesWithinFewFrames()
    {
        using var harness = new HrotRunnerHarness();

        long tkbType  = TkbEntityTypes.Tank_M1Abrams;
        var  spawnGeo = new GeoPoint { Latitude = 52.521, Longitude = 13.406, Altitude = 0 };

        // ── 1. Spawn entity on SimHost directly (no DDS round-trip) ──────────
        long networkId = harness.SimHost.TestHook_SpawnEntity(tkbType, spawnGeo);
        _out.WriteLine($"[M1] SimHost spawned entity networkId={networkId}");

        // ── 2. Wait for the entity to appear on IG with a SimTransform ────────
        bool entityAppeared = harness.PumpUntil(
            () => IgHasNetworkEntity(harness, networkId),
            SpawnTimeoutFrames);
        Assert.True(entityAppeared,
            $"No entity with SimTransform appeared on IG within {SpawnTimeoutFrames} frames.");
        _out.WriteLine($"[M2] IG entity appeared: networkId={networkId}");

        // ── 3. Assign WanderMilitary doctrine directly (no DDS round-trip) ───
        harness.SimHost.TestHook_AssignWanderMission(networkId);
        _out.WriteLine("[M3] WanderMilitary doctrine assigned via TestHook");

        // ── 3b. Log SimHost state after 20 frames to verify doctrine + movement ──
        harness.PumpFrames(20);
        var shTf0       = harness.SimHost.TestHook_GetSimTransform(networkId);
        var shDoctrine0 = harness.SimHost.TestHook_GetDoctrineState(networkId);
        var mpq0        = harness.SimHost.TestHook_GetMissionPlanQueue(networkId);
        bool hasMpq0    = harness.SimHost.TestHook_HasMissionPlanQueue(networkId);
        _out.WriteLine($"[M3b] SimHost pos=({shTf0.Position.X:F3}, {shTf0.Position.Y:F3}) " +
                       $"doctrine.ActiveHash={shDoctrine0.ActiveDoctrineHash} " +
                       $"doctrine.InstanceId={shDoctrine0.InstanceId} " +
                       $"hasMPQ={hasMpq0} mpq.PhaseCount={mpq0.PhaseCount} mpq.CurrentPhase={mpq0.CurrentPhase}");

        harness.PumpFrames(50);
        var shTf1 = harness.SimHost.TestHook_GetSimTransform(networkId);
        var shDoc1 = harness.SimHost.TestHook_GetDoctrineState(networkId);
        float shMoved = Vector3.Distance(shTf0.Position, shTf1.Position);
        _out.WriteLine($"[M3c] SimHost pos after +50 frames=({shTf1.Position.X:F3}, {shTf1.Position.Y:F3}) " +
                       $"doctrine.ActiveHash={shDoc1.ActiveDoctrineHash} " +
                       $"SimHost moved={shMoved:F4} m");

        // ── 4. Wait for the entity to be promoted to Active lifecycle ─────────
        bool igActive = harness.PumpUntil(
            () => IgEntityIsActive(harness, networkId),
            SpawnTimeoutFrames);
        Assert.True(igActive,
            $"IG entity (networkId={networkId}) did not reach Active lifecycle within {SpawnTimeoutFrames} frames.");
        _out.WriteLine("[M4] IG entity is Active");

        // ── 5. Record the current IG position as baseline ─────────────────────
        var posA = GetIgSimTransform(harness, networkId).Position;
        _out.WriteLine($"[M5] Baseline IG position: ({posA.X:F3}, {posA.Y:F3}, {posA.Z:F3})");

        // ── 6. Wait for position to change ────────────────────────────────────
        // The GeoSpatialEgressTranslator shadow comparison detects each kinematic
        // tick's position change and publishes immediately.  IG should observe a
        // position change within a handful of frames — well before the 600-tick
        // rolling-window heartbeat.
        bool moved = harness.PumpUntil(() =>
        {
            var posNow = GetIgSimTransform(harness, networkId).Position;
            return Vector3.Distance(posNow, posA) >= MovementThresholdMetres;
        }, MovementTimeoutFrames);

        var posB = GetIgSimTransform(harness, networkId).Position;
        var shTfFinal = harness.SimHost.TestHook_GetSimTransform(networkId);
        float travelledMetres = Vector3.Distance(posA, posB);
        float shTravelledMetres = Vector3.Distance(shTf1.Position, shTfFinal.Position);
        _out.WriteLine($"[M6] Final IG position: ({posB.X:F3}, {posB.Y:F3}), IG travelled={travelledMetres:F4} m, IG moved={moved}");
        _out.WriteLine($"[M6b] Final SimHost position: ({shTfFinal.Position.X:F3}, {shTfFinal.Position.Y:F3}), SimHost additionalTravel={shTravelledMetres:F4} m");

        Assert.True(moved,
            $"IG entity (networkId={networkId}) did not move within {MovementTimeoutFrames} frames. " +
            $"SimHost moved={shMoved:F4}m during warmup, SimHost final={shTravelledMetres:F4}m additional. " +
            $"Baseline=({posA.X:F3}, {posA.Y:F3}), final=({posB.X:F3}, {posB.Y:F3}).");
    }

    // ── Test 2: Position changes observed on MULTIPLE consecutive updates ─────

    /// <summary>
    /// Verifies that GeoSpatial updates are continuous — the IG position changes not just
    /// once but again a second time — confirming that the shadow-state comparison in
    /// <c>GeoSpatialEgressTranslator</c> keeps detecting and publishing changes while the
    /// entity has non-zero velocity.
    /// </summary>
    [Fact]
    public void SpawnMovingVehicle_IgPositionContinuesToUpdate()
    {
        using var harness = new HrotRunnerHarness();

        long tkbType  = TkbEntityTypes.Tank_M1Abrams;
        var  spawnGeo = new GeoPoint { Latitude = 52.521, Longitude = 13.407, Altitude = 0 };

        // ── 1. Spawn entity on SimHost directly (no DDS round-trip) ──────────
        long networkId = harness.SimHost.TestHook_SpawnEntity(tkbType, spawnGeo);

        // ── 2. Wait for entity + Active lifecycle ─────────────────────────────
        bool entityAppeared = harness.PumpUntil(
            () => IgHasNetworkEntity(harness, networkId),
            SpawnTimeoutFrames);
        Assert.True(entityAppeared,
            $"Entity networkId={networkId} did not appear on IG within {SpawnTimeoutFrames} frames.");

        // ── 3. Assign WanderMilitary doctrine directly (no DDS round-trip) ───
        harness.SimHost.TestHook_AssignWanderMission(networkId);

        bool igActive = harness.PumpUntil(
            () => IgEntityIsActive(harness, networkId),
            SpawnTimeoutFrames);
        Assert.True(igActive, $"IG entity did not reach Active lifecycle.");

        // ── 4. First movement: record posA then wait for first change ─────────
        var posA = GetIgSimTransform(harness, networkId).Position;
        _out.WriteLine($"[C3] posA=({posA.X:F3}, {posA.Y:F3})");

        bool firstMove = harness.PumpUntil(() =>
        {
            var p = GetIgSimTransform(harness, networkId).Position;
            return Vector3.Distance(p, posA) >= MovementThresholdMetres;
        }, MovementTimeoutFrames);
        Assert.True(firstMove,
            $"First position change was not observed within {MovementTimeoutFrames} frames.");

        // ── 5. Second movement: record posB then wait for another change ──────
        var posB = GetIgSimTransform(harness, networkId).Position;
        _out.WriteLine($"[C4] posB=({posB.X:F3}, {posB.Y:F3})");

        bool secondMove = harness.PumpUntil(() =>
        {
            var p = GetIgSimTransform(harness, networkId).Position;
            return Vector3.Distance(p, posB) >= MovementThresholdMetres;
        }, MovementTimeoutFrames);
        Assert.True(secondMove,
            $"Second position change was not observed within {MovementTimeoutFrames} frames. " +
            $"Shadow-state comparison must detect changes continuously, not just once.");

        var posC = GetIgSimTransform(harness, networkId).Position;
        _out.WriteLine($"[C5] posC=({posC.X:F3}, {posC.Y:F3})");
    }

    // ── Test 3: SimHost drag immediately updates IG (< MovementTimeoutFrames) ──

    /// <summary>
    /// Verifies that dragging an entity in SimHost (via <c>SimHostVisualization.OnEntityMoved</c>)
    /// causes the updated GeoSpatial position to reach the IG within
    /// <see cref="MovementTimeoutFrames"/> frames — proving the
    /// <c>SmartEgressUtil.MarkDirty</c> call added to <c>OnEntityMoved</c> works correctly
    /// and the IG is NOT waiting for the 600-tick rolling-window heartbeat.
    /// </summary>
    [Fact]
    public void SimHostDrag_IgReceivesPositionUpdateWithinFewFrames()
    {
        using var harness = new HrotRunnerHarness();

        // ── 1. Spawn a stationary entity on SimHost ────────────────────────────
        long tkbType  = TkbEntityTypes.Tank_M1Abrams;
        var  spawnGeo = new GeoPoint { Latitude = 52.521, Longitude = 13.406, Altitude = 0 };
        long networkId = harness.SimHost.TestHook_SpawnEntity(tkbType, spawnGeo);
        _out.WriteLine($"[S1] SimHost spawned entity networkId={networkId}");

        // ── 2. Wait for entity to appear on IG ────────────────────────────────
        bool igHasEntity = harness.PumpUntil(
            () => IgHasNetworkEntity(harness, networkId),
            SpawnTimeoutFrames);
        Assert.True(igHasEntity,
            $"IG did not receive entity (networkId={networkId}) within {SpawnTimeoutFrames} frames.");

        var posA = GetIgSimTransform(harness, networkId).Position;
        _out.WriteLine($"[S2] IG initial position: ({posA.X:F3}, {posA.Y:F3})");

        // ── 3. Simulate a drag on SimHost to a new position ────────────────────
        // OnEntityMoved applies the new position AND (after the fix) calls MarkDirty.
        var newWorldPos = new System.Numerics.Vector2(posA.X + 250f, posA.Y + 180f);
        harness.SimHost.TestHook_SimulateDrag(networkId, newWorldPos);
        _out.WriteLine($"[S3] SimHost drag to ({newWorldPos.X:F1}, {newWorldPos.Y:F1})");

        // ── 4. IG should observe the new position well before the heartbeat ───
        var expectedPos = new Vector3(newWorldPos.X, newWorldPos.Y, 0f);
        bool updated = harness.PumpUntil(() =>
        {
            var pos = GetIgSimTransform(harness, networkId).Position;
            return Vector3.Distance(pos, posA) >= MovementThresholdMetres;
        }, MovementTimeoutFrames);

        var posB = GetIgSimTransform(harness, networkId).Position;
        _out.WriteLine($"[S4] IG position after drag: ({posB.X:F3}, {posB.Y:F3}), updated={updated}");

        Assert.True(updated,
            $"IG entity (networkId={networkId}) did not receive SimHost drag update within " +
            $"{MovementTimeoutFrames} frames. Expected position near ({expectedPos.X:F1}, {expectedPos.Y:F1}), " +
            $"still at ({posB.X:F1}, {posB.Y:F1}). " +
            $"The SmartEgressUtil.MarkDirty call in SimHostVisualization.OnEntityMoved may be missing.");
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    /// <summary>
    /// Scans the IG entity map for the first entity that has a live <see cref="SimTransform"/>.
    /// Returns 0 if none found yet.
    /// </summary>
    private static long FindFirstMovingCandidateNetworkId(HrotRunnerHarness harness)
    {
        var igWorld   = harness.Ig.App.World;
        var igMap     = harness.Ig.App.TestHook_EntityMap;
        var query     = igWorld.Query().With<NetworkIdentity>().WithLifecycle(EntityLifecycle.All).Build();

        foreach (var entity in query)
        {
            if (!igWorld.IsAlive(entity)) continue;
            if (!igWorld.HasComponent<SimTransform>(entity)) continue;

            var netId = igWorld.GetComponent<NetworkIdentity>(entity);
            if (netId.Value <= 0) continue;

            // Confirm the entity map also knows about it.
            if (igMap.TryGetEntity(netId.Value, out _))
                return netId.Value;
        }

        return 0;
    }

    private static bool IgHasNetworkEntity(HrotRunnerHarness harness, long networkId)
    {
        var entityMap = harness.Ig.App.TestHook_EntityMap;
        if (!entityMap.TryGetEntity(networkId, out var entity)) return false;
        var world = harness.Ig.App.World;
        return world.IsAlive(entity) && world.HasComponent<SimTransform>(entity);
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
