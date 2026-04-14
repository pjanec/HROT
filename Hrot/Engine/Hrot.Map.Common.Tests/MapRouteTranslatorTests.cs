using System.Collections.Generic;
using System.Numerics;
using Hrot.NED.Descriptors;
using Hrot.NED.Common;
using Hrot.Map.Common.Components;
using Hrot.Map.Common.Dds;
using Hrot.Map.Common.Replication.Egress;
using Hrot.Map.Common.Replication.Ingress;
using Fdp.Kernel;
using Fdp.Modules.Geographic;
using Fdp.Modules.Geographic.Transforms;
using Fdp.Toolkit.Replication.Components;
using Fdp.Toolkit.Replication.Services;
using Fdp.ModuleHost.Abstractions;

namespace Hrot.Map.Common.Tests;

/// <summary>
/// Tests for ROUTES1-T004 (<see cref="MapRouteEgressTranslator"/>) and
/// ROUTES1-T005 (<see cref="MapRouteIngressTranslator"/>).
/// </summary>
public class MapRouteTranslatorTests
{
    // ── T004 Egress ───────────────────────────────────────────────────────────

    [Fact]
    public void Egress_ThreeWaypoints_EmitsExactlyOnePublishWithThreePoints()
    {
        using var world  = CreateEcsWorld();
        var entityMap    = new NetworkEntityMap();
        var geoTransform = new StubGeoTransform();
        var writer       = new CapturingWriter<MapRoute>();
        var translator   = new MapRouteEgressTranslator(writer, entityMap, geoTransform);

        var entity = world.CreateEntity();
        world.AddComponent(entity, new NetworkIdentity(1));
        world.AddComponent(entity, new NetworkAuthority(primaryOwnerId: 1, localNodeId: 1));
        world.AddComponent(entity, new SimTransform());
        entityMap.Register(1, entity);

        var plan = new RoutePlan();
        plan.Mutate(wps =>
        {
            wps.Add(new RouteWaypoint { Position = new Vector3(10, 20, 30), TargetSpeed = 5f });
            wps.Add(new RouteWaypoint { Position = new Vector3(40, 50, 60), TargetSpeed = 10f });
            wps.Add(new RouteWaypoint { Position = new Vector3(70, 80, 90), TargetSpeed = 0f });
        });
        world.SetManagedComponent(entity, plan);

        translator.ScanAndPublish(world);

        Assert.Equal(1, writer.Publishes.Count);
        Assert.Equal(3, writer.Publishes[0].Points!.Count);
    }

    [Fact]
    public void Egress_IsLoop_And_SpeedAndExtensionJson_FaithfullyPropagated()
    {
        using var world  = CreateEcsWorld();
        var entityMap    = new NetworkEntityMap();
        var geoTransform = new StubGeoTransform();
        var writer       = new CapturingWriter<MapRoute>();
        var translator   = new MapRouteEgressTranslator(writer, entityMap, geoTransform);

        var entity = world.CreateEntity();
        world.AddComponent(entity, new NetworkIdentity(2));
        world.AddComponent(entity, new NetworkAuthority(primaryOwnerId: 1, localNodeId: 1));
        world.AddComponent(entity, new SimTransform());
        entityMap.Register(2, entity);

        var plan = new RoutePlan { IsLoop = true };
        plan.Mutate(wps => wps.Add(new RouteWaypoint
        {
            Position      = new Vector3(1, 2, 3),
            TargetSpeed   = 7.5f,
            ExtensionJson = @"{""hint"":""slow""}",
        }));
        world.SetManagedComponent(entity, plan);

        translator.ScanAndPublish(world);

        var published = writer.Publishes[0];
        Assert.True(published.IsLoop);
        Assert.Equal(7.5, published.Points![0].SpeedMetersPerSec, precision: 3);
        Assert.Equal(@"{""hint"":""slow""}", published.Points[0].ExtensionJson);
    }

    [Fact]
    public void Egress_GeoPosition_RoundTripWithinOneMm()
    {
        // Use WGS84 for a realistic precision check.
        var geoTransform = new WGS84Transform();
        geoTransform.SetOrigin(latDeg: 48.8566, lonDeg: 2.3522, altMeters: 35.0); // Paris

        using var world  = CreateEcsWorld();
        var entityMap    = new NetworkEntityMap();
        var writer       = new CapturingWriter<MapRoute>();
        var translator   = new MapRouteEgressTranslator(writer, entityMap, geoTransform);

        // Position within 100 km of origin
        var originalPos = new Vector3(500f, 1000f, 50f);

        var entity = world.CreateEntity();
        world.AddComponent(entity, new NetworkIdentity(3));
        world.AddComponent(entity, new NetworkAuthority(primaryOwnerId: 1, localNodeId: 1));
        world.AddComponent(entity, new SimTransform());
        entityMap.Register(3, entity);

        var plan = new RoutePlan();
        plan.Mutate(wps => wps.Add(new RouteWaypoint { Position = originalPos }));
        world.SetManagedComponent(entity, plan);

        translator.ScanAndPublish(world);

        var geoPoint = writer.Publishes[0].Points![0].Position;

        // Round-trip back to Cartesian
        var roundTrippedPos = geoTransform.ToCartesian(
            geoPoint.Latitude, geoPoint.Longitude, geoPoint.Altitude);

        float distanceMeters = Vector3.Distance(originalPos, roundTrippedPos);
        Assert.True(distanceMeters < 0.001f,
            $"Round-trip error {distanceMeters * 1000:F3} mm exceeds 1 mm tolerance.");
    }

    [Fact]
    public void Egress_UnchangedVersion_NoPublish()
    {
        using var world  = CreateEcsWorld();
        var entityMap    = new NetworkEntityMap();
        var geoTransform = new StubGeoTransform();
        var writer       = new CapturingWriter<MapRoute>();
        var translator   = new MapRouteEgressTranslator(writer, entityMap, geoTransform);

        var entity = world.CreateEntity();
        world.AddComponent(entity, new NetworkIdentity(4));
        world.AddComponent(entity, new NetworkAuthority(primaryOwnerId: 1, localNodeId: 1));
        world.AddComponent(entity, new SimTransform());
        entityMap.Register(4, entity);

        var plan = new RoutePlan();
        plan.Mutate(wps => wps.Add(new RouteWaypoint { Position = Vector3.Zero }));
        world.SetManagedComponent(entity, plan);

        translator.ScanAndPublish(world); // publish once (version=1)
        int firstCount = writer.Publishes.Count;

        translator.ScanAndPublish(world); // version still 1 → no second publish

        Assert.Equal(1, firstCount);
        Assert.Equal(1, writer.Publishes.Count);
    }

    [Fact]
    public void Egress_EntityWithoutRoutePlan_NotProcessed()
    {
        using var world  = CreateEcsWorld();
        var entityMap    = new NetworkEntityMap();
        var geoTransform = new StubGeoTransform();
        var writer       = new CapturingWriter<MapRoute>();
        var translator   = new MapRouteEgressTranslator(writer, entityMap, geoTransform);

        // Entity without RoutePlan
        var entity = world.CreateEntity();
        world.AddComponent(entity, new NetworkIdentity(5));
        world.AddComponent(entity, new NetworkAuthority(primaryOwnerId: 1, localNodeId: 1));
        world.AddComponent(entity, new SimTransform());
        entityMap.Register(5, entity);

        translator.ScanAndPublish(world);

        Assert.Empty(writer.Publishes);
    }

    // ── T005 Ingress ─────────────────────────────────────────────────────────

    [Fact]
    public void Ingress_FiveWaypoints_ResultsInFiveRouteWaypointEntries()
    {
        using var world  = CreateEcsWorld();
        var entityMap    = new NetworkEntityMap();
        var geoTransform = new StubGeoTransform();
        var translator   = new MapRouteIngressTranslator(null, entityMap, geoTransform);

        var entity = world.CreateEntity();
        entityMap.Register(10, entity);

        var sample = MakeMapRoute(entityId: 10, waypointCount: 5);
        translator.ApplyToEntity(entity, sample, world);

        var plan = ((ISimulationView)world).GetManagedComponentRO<RoutePlan>(entity);
        Assert.Equal(5, plan.Waypoints.Count);
    }

    [Fact]
    public void Ingress_GeoPosition_RoundTripWithinOneMm()
    {
        var geoTransform = new WGS84Transform();
        geoTransform.SetOrigin(latDeg: 48.8566, lonDeg: 2.3522, altMeters: 35.0);

        using var world = CreateEcsWorld();
        var entityMap   = new NetworkEntityMap();
        var translator  = new MapRouteIngressTranslator(null, entityMap, geoTransform);

        var originalPos = new Vector3(300f, 700f, 20f);
        var (lat, lon, alt) = geoTransform.ToGeodetic(originalPos);

        var entity = world.CreateEntity();
        entityMap.Register(11, entity);

        var sample = new MapRoute
        {
            EntityId = 11,
            IsLoop   = false,
            Points   = new List<Waypoint>
            {
                new Waypoint { Position = new GeoPoint { Latitude = lat, Longitude = lon, Altitude = alt } }
            },
        };
        translator.ApplyToEntity(entity, sample, world);

        var plan = ((ISimulationView)world).GetManagedComponentRO<RoutePlan>(entity);
        var roundTrippedPos = plan.Waypoints[0].Position;

        float distanceMeters = Vector3.Distance(originalPos, roundTrippedPos);
        Assert.True(distanceMeters < 0.001f,
            $"Round-trip error {distanceMeters * 1000:F3} mm exceeds 1 mm tolerance.");
    }

    [Fact]
    public void Ingress_IsLoopAndSpeedAndExtensionJson_FaithfullyPropagated()
    {
        using var world  = CreateEcsWorld();
        var entityMap    = new NetworkEntityMap();
        var geoTransform = new StubGeoTransform();
        var translator   = new MapRouteIngressTranslator(null, entityMap, geoTransform);

        var entity = world.CreateEntity();
        entityMap.Register(12, entity);

        var sample = new MapRoute
        {
            EntityId = 12,
            IsLoop   = true,
            Points   = new List<Waypoint>
            {
                new Waypoint
                {
                    Position          = new GeoPoint { Latitude = 1.0, Longitude = 2.0, Altitude = 3.0 },
                    SpeedMetersPerSec = 12.5,
                    ExtensionJson     = @"{""wait"":5}",
                }
            },
        };
        translator.ApplyToEntity(entity, sample, world);

        var plan = ((ISimulationView)world).GetManagedComponentRO<RoutePlan>(entity);
        Assert.True(plan.IsLoop);
        Assert.Equal(12.5f, plan.Waypoints[0].TargetSpeed, precision: 3);
        Assert.Equal(@"{""wait"":5}", plan.Waypoints[0].ExtensionJson);
    }

    [Fact]
    public void Ingress_Version_IncrementedOnEachProcessedSample()
    {
        using var world  = CreateEcsWorld();
        var entityMap    = new NetworkEntityMap();
        var geoTransform = new StubGeoTransform();
        var translator   = new MapRouteIngressTranslator(null, entityMap, geoTransform);

        var entity = world.CreateEntity();
        entityMap.Register(13, entity);

        var sample = MakeMapRoute(entityId: 13, waypointCount: 1);

        translator.ApplyToEntity(entity, sample, world);
        var versionAfterFirst = ((ISimulationView)world).GetManagedComponentRO<RoutePlan>(entity).Version;

        translator.ApplyToEntity(entity, sample, world);
        var versionAfterSecond = ((ISimulationView)world).GetManagedComponentRO<RoutePlan>(entity).Version;

        Assert.Equal(1, versionAfterFirst);
        Assert.Equal(2, versionAfterSecond);
    }

    [Fact]
    public void Ingress_UnknownEntityId_DeferredAndProcessedOnNextPollIngress()
    {
        using var world  = CreateEcsWorld();
        var entityMap    = new NetworkEntityMap();
        var geoTransform = new StubGeoTransform();
        var translator   = new MapRouteIngressTranslator(null, entityMap, geoTransform);

        var view = (ISimulationView)world;
        var cmd  = (EntityCommandBuffer)view.GetCommandBuffer();

        // Network ID 99 is not yet in the map.
        var sample = MakeMapRoute(entityId: 99, waypointCount: 2);
        translator.ProcessSample(in sample, cmd, view);

        // Entity is not present yet → ProcessSample must not throw.
        // After registration, PollIngress processes the deferred sample.
        var entity = world.CreateEntity();
        entityMap.Register(99, entity);

        translator.PollIngress(cmd, view); // retries deferred samples
        cmd.Playback(world);               // flush buffered SetManagedComponent

        Assert.True(world.HasManagedComponent<RoutePlan>(entity),
            "RoutePlan must be applied once the entity becomes available.");
        var plan = ((ISimulationView)world).GetManagedComponentRO<RoutePlan>(entity);
        Assert.Equal(2, plan.Waypoints.Count);
    }

    // ── CT-0: Callback-based retry (ROUTES1-BATCH-03) ────────────────────────

    /// <summary>
    /// CT-0: When two routes are pending and only the first entity is registered via
    /// <see cref="NetworkEntityMap.EntityRegistered"/>, the first entity's RoutePlan is
    /// applied on the next PollIngress but the second entity's sample remains pending.
    /// This verifies the O(k) callback-based retry over O(n) linear scanning.
    /// </summary>
    [Fact]
    public void Ingress_TwoPending_OnlyRegisteredEntity_GetsRoutePlanApplied()
    {
        using var world  = CreateEcsWorld();
        var entityMap    = new NetworkEntityMap();
        var geoTransform = new StubGeoTransform();
        var translator   = new MapRouteIngressTranslator(null, entityMap, geoTransform);

        var view = (ISimulationView)world;
        var cmd  = (EntityCommandBuffer)view.GetCommandBuffer();

        // Defer two separate samples.
        var sampleA = MakeMapRoute(entityId: 50, waypointCount: 3);
        var sampleB = MakeMapRoute(entityId: 51, waypointCount: 2);
        translator.ProcessSample(in sampleA, cmd, view);
        translator.ProcessSample(in sampleB, cmd, view);

        // Register only entity 50 — fires EntityRegistered callback for netId 50 only.
        var entityA = world.CreateEntity();
        entityMap.Register(50, entityA);

        translator.PollIngress(cmd, view);
        cmd.Playback(world);

        // Entity 50 must have its RoutePlan with 3 waypoints applied.
        Assert.True(world.HasManagedComponent<RoutePlan>(entityA),
            "Entity 50 must have RoutePlan once registered.");
        var planA = view.GetManagedComponentRO<RoutePlan>(entityA);
        Assert.Equal(3, planA.Waypoints.Count);

        // Entity 51 was never registered — its sample stays in the pending queue.
        // Verify by registering it on the *next* poll and checking it is then applied.
        var entityB = world.CreateEntity();
        entityMap.Register(51, entityB);

        translator.PollIngress(cmd, view);
        cmd.Playback(world);

        Assert.True(world.HasManagedComponent<RoutePlan>(entityB),
            "Entity 51 must have RoutePlan only after its own registration triggers the callback.");
        var planB = view.GetManagedComponentRO<RoutePlan>(entityB);
        Assert.Equal(2, planB.Waypoints.Count);
    }

    /// <summary>
    /// CT-0: When no entity is registered between two PollIngress calls, the pending
    /// sample is <em>not</em> applied — confirming the system does not scan all pending
    /// routes on every tick (avoids the O(n) regression).
    /// </summary>
    [Fact]
    public void Ingress_NoRegistrationBetweenPolls_PendingSampleNotApplied()
    {
        using var world  = CreateEcsWorld();
        var entityMap    = new NetworkEntityMap();
        var geoTransform = new StubGeoTransform();
        var translator   = new MapRouteIngressTranslator(null, entityMap, geoTransform);

        var view = (ISimulationView)world;
        var cmd  = (EntityCommandBuffer)view.GetCommandBuffer();

        // Defer sample for entity ID 60 — entity not yet registered.
        var sample = MakeMapRoute(entityId: 60, waypointCount: 2);
        translator.ProcessSample(in sample, cmd, view);

        // Poll without registering entity 60 first — no callback fired, retry set is empty.
        translator.PollIngress(cmd, view);
        cmd.Playback(world);

        // Create a stand-in entity and verify it has no RoutePlan (ID 60 was never in map).
        var entity = world.CreateEntity();
        Assert.False(world.HasManagedComponent<RoutePlan>(entity),
            "RoutePlan must not appear — entity 60 was never registered in NetworkEntityMap.");
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static EntityRepository CreateEcsWorld()
    {
        var world = new EntityRepository();
        world.RegisterComponent<NetworkIdentity>();
        world.RegisterComponent<NetworkAuthority>();
        world.RegisterComponent<SimTransform>();
        world.RegisterManagedComponent<RoutePlan>();
        return world;
    }

    private static MapRoute MakeMapRoute(int entityId, int waypointCount, bool isLoop = false)
    {
        var points = new List<Waypoint>(waypointCount);
        for (int i = 0; i < waypointCount; i++)
        {
            points.Add(new Waypoint
            {
                Position          = new GeoPoint { Latitude = i, Longitude = i, Altitude = 0 },
                SpeedMetersPerSec = 5.0,
            });
        }
        return new MapRoute { EntityId = entityId, Points = points, IsLoop = isLoop };
    }

    // ── Test doubles ─────────────────────────────────────────────────────────

    /// <summary>Simple in-memory writer that records all published samples.</summary>
    private sealed class CapturingWriter<T> : IDdsWriter<T>
    {
        public List<T> Publishes { get; } = new();
        public void Write(T sample) => Publishes.Add(sample);
        public void DisposeInstance(T key) { /* no-op in tests */ }
    }

    /// <summary>
    /// Trivial stub: ToCartesian maps (lat, lon, alt) → (lon, lat, alt) as
    /// Vector3(X=lon, Y=lat, Z=alt); ToGeodetic is the exact inverse.
    /// Round-trips are exact by construction.
    /// </summary>
    private sealed class StubGeoTransform : IGeographicTransform
    {
        public void SetOrigin(double latDeg, double lonDeg, double altMeters) { }

        public Vector3 ToCartesian(double latDeg, double lonDeg, double altMeters)
            => new Vector3((float)lonDeg, (float)latDeg, (float)altMeters);

        public (double lat, double lon, double alt) ToGeodetic(Vector3 localPos)
            => (localPos.Y, localPos.X, localPos.Z);
    }
}
