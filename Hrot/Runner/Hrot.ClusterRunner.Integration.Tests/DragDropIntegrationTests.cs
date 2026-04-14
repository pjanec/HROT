using System.Numerics;
using Hrot.NED.Descriptors;
using Hrot.NED.Common;
using CoreGeoPoint = Hrot.Core.Mission.GeoPoint;
using Hrot.Map.Common;
using FDP.Toolkit.Replication.Components;
using FDP.Toolkit.Replication.Extensions;
using Fdp.Kernel;
using Fdp.ModuleHost.Abstractions;
using Xunit;
using Xunit.Abstractions;

namespace Hrot.ClusterRunner.Integration.Tests;

/// <summary>
/// Integration tests that verify the full drag-and-drop position-update round trip:
/// IG drag end → DDS UpdateEntityDescriptorRequest → SimHost → DDS GeoSpatial → IG.
///
/// <para>Key invariants tested:</para>
/// <list type="bullet">
///   <item>SimHost applies the new position in the same kernel frame it receives the request
///   (ordering fix: kernelGroup.Run before kernel.Update ensures MarkDirty is set before
///   the egress ScanAndPublish pass).</item>
///   <item>The IG entity's <see cref="SimTransform"/> is updated well within the 600-tick
///   rolling-window heartbeat window — proving the dirty-flag mechanism is working.</item>
///   <item>Round-trip completes in far fewer frames than the 600-tick rolling window
///   (~10 s at 60 Hz), confirming no 10-second delay regression.</item>
/// </list>
/// </summary>
public class DragDropIntegrationTests
{
    // Round-trip budget: SimHost processes + publishes on the frame it receives the request;
    // IG applies on the next frame.  We allow generous 120 frames (~2 s) to account for
    // DDS loopback latency in CI environments.  The IMPORTANT assertion is that it settles
    // well before the 600-tick rolling window.
    private const int RoundTripTimeoutFrames = 120;
    private const int SpawnTimeoutFrames     = 120;

    // Acceptable Cartesian position error after the geodetic round-trip (lat/lon → Cartesian
    // → lat/lon → Cartesian).  WGS84 round-trip precision at Berlin origin is << 1 mm.
    private const float PositionTolerance = 0.5f; // metres

    private readonly ITestOutputHelper _out;

    public DragDropIntegrationTests(ITestOutputHelper output) => _out = output;

    [Fact]
    public void DragDrop_EntityPositionUpdatesOnIgWithinFewFrames()
    {
        using var harness = new HrotRunnerHarness();

        // ── 1. Spawn a tank on SimHost at a known geo position ────────────────
        long tkbType  = TkbEntityTypes.Tank_M1Abrams;
        var  spawnGeo = new CoreGeoPoint { Latitude = 52.521, Longitude = 13.406, Altitude = 0 };
        long networkId = harness.SimHost.TestHook_SpawnEntity(tkbType, spawnGeo);
        _out.WriteLine($"[D1] Spawned entity networkId={networkId}");

        // ── 2. Wait until the entity propagates to the IG ─────────────────────
        bool igHasEntity = harness.PumpUntil(
            () => IgHasNetworkEntity(harness, networkId),
            SpawnTimeoutFrames);
        Assert.True(igHasEntity,
            $"IG did not receive entity (netId={networkId}) within {SpawnTimeoutFrames} frames.");

        // ── 2b. Diagnostic: verify IG entity has NetworkIdentity ─────────────
        var igEntityMap = harness.Ig.App.TestHook_EntityMap;
        igEntityMap.TryGetEntity(networkId, out var igEntity);
        var igWorld = harness.Ig.App.World;
        bool igHasNetId = igWorld.HasComponent<NetworkIdentity>(igEntity);
        _out.WriteLine($"[D2] IG entity alive={igWorld.IsAlive(igEntity)}, hasNetworkIdentity={igHasNetId}, hasST={igWorld.HasComponent<SimTransform>(igEntity)}");
        Assert.True(igHasNetId, "IG entity is missing NetworkIdentity — OnEntityDragEnded will bail early.");

        // ── 2c. Diagnostic: verify SimHost entity has correct NetworkAuthority ─
        bool shHasEntity = harness.SimHost.TestHook_EntityMap.TryGetEntity(networkId, out var shEntity);
        Assert.True(shHasEntity, "SimHost entity not found in entity map.");
        var shWorld = harness.SimHost.World!;
        bool shHasNetAuth = shWorld.HasComponent<NetworkAuthority>(shEntity);
        bool shHasNetId   = shWorld.HasComponent<NetworkIdentity>(shEntity);
        bool shHasST      = shWorld.HasComponent<SimTransform>(shEntity);
        _out.WriteLine($"[D2c] SimHost entity alive={shWorld.IsAlive(shEntity)}, hasNetworkAuthority={shHasNetAuth}, hasNetworkIdentity={shHasNetId}, hasSimTransform={shHasST}");
        if (shHasNetAuth)
        {
            var auth = shWorld.GetComponent<NetworkAuthority>(shEntity);
            _out.WriteLine($"[D2c] NetworkAuthority: PrimaryOwner={auth.PrimaryOwnerId}, Local={auth.LocalNodeId}, HasAuthority={auth.HasAuthority}");
        }

        // ── 2d. Diagnostic: check HasAuthority for GeoSpatial directly ────────
        // Confirm via extension method used by ScanAndPublish.
        bool shHasDescriptorOwnership = shWorld.HasManagedComponent<FDP.Toolkit.Replication.Components.DescriptorOwnership>(shEntity);
        bool shHasAuthForGeoSpatial   = ((Fdp.ModuleHost.Abstractions.ISimulationView)shWorld).HasAuthority(shEntity, (long)EDescriptorType.dtWorldPos);
        _out.WriteLine($"[D2d] HasDescriptorOwnership={shHasDescriptorOwnership}, HasAuthority(entity,dtGeoSpatial)={shHasAuthForGeoSpatial}");
        Assert.True(shHasAuthForGeoSpatial,
            "SimHost entity does NOT have authority for GeoSpatial. " +
            "ScanAndPublish will skip this entity in the egress loop.");

        // ── 3. Capture the IG entity's current world-space position ───────────
        var initialPos = GetIgSimTransform(harness, networkId).Position;
        _out.WriteLine($"[D3] Initial IG position: ({initialPos.X:F2}, {initialPos.Y:F2}, {initialPos.Z:F2})");

        // ── 3b. Capture SimHost entity's current SimTransform ─────────────────
        var shTransformBefore = shWorld.GetComponent<SimTransform>(shEntity);
        _out.WriteLine($"[D3b] SimHost position before drag: ({shTransformBefore.Position.X:F2}, {shTransformBefore.Position.Y:F2})");

        // ── 4. Choose a clearly different drop position (200 m east, 150 m north) ──
        var dropWorldPos = new Vector2(initialPos.X + 200f, initialPos.Y + 150f);
        _out.WriteLine($"[D4] Drop target: ({dropWorldPos.X:F2}, {dropWorldPos.Y:F2})");

        // ── 5. Create DDS observer BEFORE drag so it's subscribed when SimHost publishes ──
        //        GeoSpatial uses Volatile QoS — a reader created after the write misses the sample.
        var expectedGeoPos = new Vector3(dropWorldPos.X, dropWorldPos.Y, 0f);
        bool geoSpatialObserved = false;
        using (var observerParticipant = new CycloneDDS.Runtime.DdsParticipant((uint)harness.DomainId))
        using (var geoReader = new CycloneDDS.Runtime.DdsReader<Hrot.NED.Descriptors.WorldPos>(observerParticipant))
        {
            // Allow DDS discovery to complete before triggering the publish.
            harness.PumpFrames(10);

            // ── 5. Simulate the drag-end on the IG ───────────────────────────────
            harness.Ig.App.TestHook_SimulateDragDrop(networkId, dropWorldPos);
            _out.WriteLine("[D5] TestHook_SimulateDragDrop called");

            // ── 5b. Check SimHost entity SimTransform updates within a few frames ─
            bool shUpdated = harness.PumpUntil(() =>
            {
                var tf = shWorld.GetComponent<SimTransform>(shEntity);
                return Vector3.Distance(tf.Position, shTransformBefore.Position) > PositionTolerance;
            }, 60);
            var shTransformAfter = shWorld.GetComponent<SimTransform>(shEntity);
            _out.WriteLine($"[D5b] SimHost updated={shUpdated}, new position: ({shTransformAfter.Position.X:F2}, {shTransformAfter.Position.Y:F2})");
            Assert.True(shUpdated,
                $"SimHost entity SimTransform did not change after drag — " +
                $"UpdateEntityDescriptorRequestSystem may not have processed the request. " +
                $"Position still: ({shTransformAfter.Position.X:F1}, {shTransformAfter.Position.Y:F1})");

            // ── 5c. DDS observer: verify SimHost published updated GeoSpatial ─────
            harness.PumpUntil(() =>
            {
                using var checkLoan = geoReader.Take();
                foreach (var s in checkLoan)
                {
                    if (!s.IsValid) continue;
                    if (s.Data.EntityId != (int)networkId) continue;
                    var igGeo = HrotEnvironment.CreateGeoTransform();
                    var pos = igGeo.ToCartesian(s.Data.Pos.Latitude, s.Data.Pos.Longitude, s.Data.Pos.Altitude);
                    var observedPos = new Vector3((float)pos.X, (float)pos.Y, (float)pos.Z);
                    _out.WriteLine($"[D5c] Observer saw GeoSpatial for entity {s.Data.EntityId}: ({observedPos.X:F2}, {observedPos.Y:F2})");
                    if (Vector3.Distance(observedPos, expectedGeoPos) <= PositionTolerance)
                    {
                        geoSpatialObserved = true;
                        return true;
                    }
                }
                return false;
            }, 60);
        }
        _out.WriteLine($"[D5c] Observer detected updated GeoSpatial={geoSpatialObserved}");
        Assert.True(geoSpatialObserved,
            $"DDS observer did NOT see an updated GeoSpatial for entity {networkId} with position ≈{expectedGeoPos}. " +
            $"This means GeoSpatialEgressTranslator did NOT publish after MarkDirty.");

        // ── 6. Pump frames and wait for the position to be reflected on the IG ─
        var  expectedPos             = new Vector3(dropWorldPos.X, dropWorldPos.Y, 0f);
        bool positionReached         = harness.PumpUntil(
            () => IgPositionWithinTolerance(harness, networkId, expectedPos, PositionTolerance),
            RoundTripTimeoutFrames);

        var actualPos = GetIgSimTransform(harness, networkId).Position;
        _out.WriteLine($"[D6] IG final position: ({actualPos.X:F2}, {actualPos.Y:F2}), reached={positionReached}");
        Assert.True(positionReached,
            $"IG entity position did not update after drag-drop within {RoundTripTimeoutFrames} frames. " +
            $"Expected ≈({expectedPos.X:F1}, {expectedPos.Y:F1}), " +
            $"got ({actualPos.X:F1}, {actualPos.Y:F1}).");
    }

    [Fact]
    public void DragDrop_SimHostReceivesRequestAndMarksDirty_PublishesWithoutRollingWindow()
    {
        using var harness = new HrotRunnerHarness();

        // ── 1. Spawn and wait for entity on IG ───────────────────────────────
        long tkbType   = TkbEntityTypes.Tank_M1Abrams;
        var  spawnGeo  = new CoreGeoPoint { Latitude = 52.522, Longitude = 13.407, Altitude = 0 };
        long networkId = harness.SimHost.TestHook_SpawnEntity(tkbType, spawnGeo);

        bool igHasEntity = harness.PumpUntil(
            () => IgHasNetworkEntity(harness, networkId),
            SpawnTimeoutFrames);
        Assert.True(igHasEntity, $"IG entity (netId={networkId}) not received in time.");

        var initialPos  = GetIgSimTransform(harness, networkId).Position;
        var dropWorldPos = new Vector2(initialPos.X + 300f, initialPos.Y + 300f);

        // Record position before drag so we can detect the change.
        var posBeforeDrag = GetIgSimTransform(harness, networkId).Position;

        // ── 2. Simulate drag ─────────────────────────────────────────────────
        harness.Ig.App.TestHook_SimulateDragDrop(networkId, dropWorldPos);

        // ── 3. Verify the position changes WELL BEFORE the rolling-window ────
        //      The rolling window fires every 600 ticks. We assert the update arrives
        //      within RoundTripTimeoutFrames (60), proving the dirty mechanism works
        //      and we are NOT waiting for the periodic heartbeat.
        bool changed = harness.PumpUntil(
            () =>
            {
                var pos = GetIgSimTransform(harness, networkId).Position;
                return Vector3.Distance(pos, posBeforeDrag) > PositionTolerance;
            },
            RoundTripTimeoutFrames);

        Assert.True(changed,
            $"IG entity did not move within {RoundTripTimeoutFrames} frames — " +
            $"dirty-flag publish mechanism may be broken (falling back to rolling window).");

        // Additionally verify it moved to (approximately) the correct position.
        var finalPos     = GetIgSimTransform(harness, networkId).Position;
        var expectedPos  = new Vector3(dropWorldPos.X, dropWorldPos.Y, 0f);
        float dist       = Vector3.Distance(finalPos, expectedPos);
        Assert.True(dist <= PositionTolerance,
            $"IG entity moved but landed at wrong position. " +
            $"Expected ≈({expectedPos.X:F1}, {expectedPos.Y:F1}), " +
            $"got ({finalPos.X:F1}, {finalPos.Y:F1}), distance={dist:F3} m.");
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static bool IgHasNetworkEntity(HrotRunnerHarness harness, long networkId)
    {
        var entityMap = harness.Ig.App.TestHook_EntityMap;
        if (!entityMap.TryGetEntity(networkId, out var entity)) return false;

        // Also require that SimTransform is present so we can read position.
        var world = harness.Ig.App.World;
        return world.IsAlive(entity) && world.HasComponent<SimTransform>(entity);
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

    private static bool IgPositionWithinTolerance(
        HrotRunnerHarness harness,
        long               networkId,
        Vector3            expected,
        float              tolerance)
    {
        var st = GetIgSimTransform(harness, networkId);
        return Vector3.Distance(st.Position, expected) <= tolerance;
    }
}
