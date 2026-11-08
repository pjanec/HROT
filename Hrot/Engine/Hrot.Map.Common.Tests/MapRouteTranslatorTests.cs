using System.Linq;
using System.Collections.Generic;
using System.Numerics;
using Hrot.NED.Messages;
using Hrot.NED.Descriptors;
using Hrot.NED.Common;
using Hrot.Map.Common.Components;
using Hrot.Map.Common.Dds;
using Hrot.Map.Common.Replication.Egress;
using Hrot.Map.Common.Replication.Ingress;
using Fdp.Core;
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
        var translator   = new MapRouteEgressTranslator(writer, entityMap);

        var entity = world.CreateEntity();
        world.AddComponent(entity, new NetworkIdentity(1));
        world.AddComponent(entity, new NetworkAuthority(primaryOwnerId: 1, localNodeId: 1));
        world.AddComponent(entity, new SimTransform());
        entityMap.Register(1, entity);

        var plan = new RoutePlan();
        plan = plan.WithWaypoints(wps =>
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

    /// <summary>
    /// ⭐ <c>CE-2209</c> — a route with NO <c>SimTransform</c> (a hand-authored scenario) is still published: the translator never reads it.
    /// 📐 Measured on two real nodes before: created on CGF without one, the route was promoted on SimHost with no plan, and the route
    /// source answered nothing (the aircraft sat AwaitingPath). 🔴 Red-proof: the old <c>.With&lt;SimTransform&gt;()</c> ⇒ no publish.
    /// </summary>
    [Fact]
    public void CE2209_ARouteWithoutATransform_IsStillPublished()
    {
        using var world  = CreateEcsWorld();
        var entityMap    = new NetworkEntityMap();
        var writer       = new CapturingWriter<MapRoute>();
        var translator   = new MapRouteEgressTranslator(writer, entityMap);
        var entity = world.CreateEntity();
        world.AddComponent(entity, new NetworkIdentity(7));
        world.AddComponent(entity, new NetworkAuthority(primaryOwnerId: 1, localNodeId: 1));
        entityMap.Register(7, entity);
        var plan = new RoutePlan();
        plan = plan.WithWaypoints(wps => { wps.Add(new RouteWaypoint { Position = new Vector3(10, 10, 0) }); wps.Add(new RouteWaypoint { Position = new Vector3(200, 10, 0) }); });
        world.SetManagedComponent(entity, plan);

        translator.ScanAndPublish(world);

        Assert.Equal(1, writer.Publishes.Count);
        Assert.Equal(2, writer.Publishes[0].Points!.Count);
    }

    [Fact]
    public void Egress_IsLoop_And_SpeedAndExtensionJson_FaithfullyPropagated()
    {
        using var world  = CreateEcsWorld();
        var entityMap    = new NetworkEntityMap();
        var geoTransform = new StubGeoTransform();
        var writer       = new CapturingWriter<MapRoute>();
        var translator   = new MapRouteEgressTranslator(writer, entityMap);

        var entity = world.CreateEntity();
        world.AddComponent(entity, new NetworkIdentity(2));
        world.AddComponent(entity, new NetworkAuthority(primaryOwnerId: 1, localNodeId: 1));
        world.AddComponent(entity, new SimTransform());
        entityMap.Register(2, entity);

        var plan = new RoutePlan { IsLoop = true };
        plan = plan.WithWaypoints(wps => wps.Add(new RouteWaypoint
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

    /// <summary>⭐ Air Ops B1 (handoff E2; R-169 — the fields are APPENDED to <c>MapRoute.Waypoint</c>) — the air fields cross the wire:
    /// egress writes them, ingress reads them back onto the route plan.</summary>
    [Fact]
    public void B1_TheAirFields_CrossTheWire_OutAndIn()
    {
        using var world  = CreateEcsWorld();
        var entityMap    = new NetworkEntityMap();
        var geoTransform = new StubGeoTransform();
        var writer       = new CapturingWriter<MapRoute>();
        var egress       = new MapRouteEgressTranslator(writer, entityMap);
        var entity = world.CreateEntity();
        world.AddComponent(entity, new NetworkIdentity(3));
        world.AddComponent(entity, new NetworkAuthority(primaryOwnerId: 1, localNodeId: 1));
        world.AddComponent(entity, new SimTransform());
        entityMap.Register(3, entity);
        var plan = new RoutePlan();
        plan = plan.WithWaypoints(wps => wps.Add(new RouteWaypoint
        {
            Position = new Vector3(1, 2, 3), Kind = Fdp.Toolkit.Navigation.RouteWaypointKind.Ground, HeightAgl = 45f,
            Hold = Fdp.Toolkit.Navigation.RouteHold.Unload, HoldSeconds = 4f, HoldUnit = new Fdp.Toolkit.Replication.EntityLink(new Fdp.Toolkit.Replication.EntityRef(7701)),
        }));
        world.SetManagedComponent(entity, plan);
        egress.ScanAndPublish(world);
        var wire = writer.Publishes[0].Points![0];
        Assert.Equal(((byte)1, 45f, (byte)2, 4f, 7701), (wire.Kind, wire.HeightAgl, wire.Hold, wire.HoldSeconds, wire.HoldUnit));

        using var other = CreateEcsWorld();
        var otherMap = new NetworkEntityMap();
        var replica = other.CreateEntity();
        otherMap.Register(3, replica);
        new MapRouteIngressTranslator(null, otherMap).ApplyToEntity(replica, writer.Publishes[0], other);
        var back = ((ISimulationView)other).GetManagedComponentRO<RoutePlan>(replica).Waypoints[0];
        Assert.Equal((Fdp.Toolkit.Navigation.RouteWaypointKind.Ground, 45f, Fdp.Toolkit.Navigation.RouteHold.Unload, 4f, 7701),
                     (back.Kind, back.HeightAgl, back.Hold, back.HoldSeconds.Seconds, back.HoldUnit.Ref.NetworkId));
    }

    [Fact]
    public void Egress_LocalPosition_IsCopiedOntoTheWire()
    {
        // ⭐ AQ107 B — was Egress_GeoPosition_RoundTripWithinOneMm (a WGS84 conversion). A waypoint is local ENU on the wire: a copy.
        using var world  = CreateEcsWorld();
        var entityMap    = new NetworkEntityMap();
        var writer       = new CapturingWriter<MapRoute>();
        var translator   = new MapRouteEgressTranslator(writer, entityMap);

        var originalPos = new Vector3(500f, 1000f, 50f);

        var entity = world.CreateEntity();
        world.AddComponent(entity, new NetworkIdentity(3));
        world.AddComponent(entity, new NetworkAuthority(primaryOwnerId: 1, localNodeId: 1));
        world.AddComponent(entity, new SimTransform());
        entityMap.Register(3, entity);

        var plan = new RoutePlan();
        plan = plan.WithWaypoints(wps => wps.Add(new RouteWaypoint { Position = originalPos }));
        world.SetManagedComponent(entity, plan);

        translator.ScanAndPublish(world);

        var onWire = writer.Publishes[0].Points![0].Position;
        Assert.Equal(500.0, onWire.X, 6);
        Assert.Equal(1000.0, onWire.Y, 6);
        Assert.Equal(50.0, onWire.Z, 6);
    }

    [Fact]
    public void Egress_UnchangedVersion_NoPublish()
    {
        using var world  = CreateEcsWorld();
        var entityMap    = new NetworkEntityMap();
        var geoTransform = new StubGeoTransform();
        var writer       = new CapturingWriter<MapRoute>();
        var translator   = new MapRouteEgressTranslator(writer, entityMap);

        var entity = world.CreateEntity();
        world.AddComponent(entity, new NetworkIdentity(4));
        world.AddComponent(entity, new NetworkAuthority(primaryOwnerId: 1, localNodeId: 1));
        world.AddComponent(entity, new SimTransform());
        entityMap.Register(4, entity);

        var plan = new RoutePlan();
        plan = plan.WithWaypoints(wps => wps.Add(new RouteWaypoint { Position = Vector3.Zero }));
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
        var translator   = new MapRouteEgressTranslator(writer, entityMap);

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
        var translator   = new MapRouteIngressTranslator(null, entityMap);

        var entity = world.CreateEntity();
        entityMap.Register(10, entity);

        var sample = MakeMapRoute(entityId: 10, waypointCount: 5);
        translator.ApplyToEntity(entity, sample, world);

        var plan = ((ISimulationView)world).GetManagedComponentRO<RoutePlan>(entity);
        Assert.Equal(5, plan.Waypoints.Count);
    }

    [Fact]
    public void Ingress_LocalPosition_IsCopiedIntoTheRoute()
    {
        // ⭐ AQ107 B — was Ingress_GeoPosition_RoundTripWithinOneMm (a WGS84 conversion). The wire waypoint is local ENU: a copy.
        using var world = CreateEcsWorld();
        var entityMap   = new NetworkEntityMap();
        var translator  = new MapRouteIngressTranslator(null, entityMap);

        var originalPos = new Vector3(300f, 700f, 20f);

        var entity = world.CreateEntity();
        entityMap.Register(11, entity);

        var sample = new MapRoute
        {
            EntityId = 11,
            IsLoop   = false,
            Points   = new List<Waypoint>
            {
                new Waypoint { Position = new Vec3d { X = originalPos.X, Y = originalPos.Y, Z = originalPos.Z } }
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
        var translator   = new MapRouteIngressTranslator(null, entityMap);

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
                    Position          = new Vec3d { X = 2.0, Y = 1.0, Z = 3.0 },
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
        var translator   = new MapRouteIngressTranslator(null, entityMap);

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
        var translator   = new MapRouteIngressTranslator(null, entityMap);

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
        var translator   = new MapRouteIngressTranslator(null, entityMap);

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
        var translator   = new MapRouteIngressTranslator(null, entityMap);

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

    // ── S8 / F-5: the recorded owner does not take its own route back ─────────

    /// <summary>⭐ Ownership build S8 / F-5 — the creator owns <c>dtMapRoute</c> (CREATOR group); its own sample looping
    /// back must not overwrite the route it holds (a newer local edit). 📄 <c>DESIGN_Ownership_Groups_And_Grants.md</c>
    /// §3 F-5, §5.6 S8.</summary>
    [Fact]
    public void Ingress_OnTheRecordedOwner_DoesNotOverwriteItsOwnRoute()
    {
        var (world, translator, entity) = RouteOwnedBy(primaryOwner: 1, local: 1);
        ProcessAndPlay(world, translator, MakeMapRoute(entityId: 10, waypointCount: 5));

        Assert.Equal(3, ((ISimulationView)world).GetManagedComponentRO<RoutePlan>(entity).Waypoints.Count);
    }

    /// <summary>The converse: a replica takes the owner's route.</summary>
    [Fact]
    public void Ingress_OnAReplica_TakesTheOwnersRoute()
    {
        var (world, translator, entity) = RouteOwnedBy(primaryOwner: 1, local: 2);
        ProcessAndPlay(world, translator, MakeMapRoute(entityId: 10, waypointCount: 5));

        Assert.Equal(5, ((ISimulationView)world).GetManagedComponentRO<RoutePlan>(entity).Waypoints.Count);
    }

    private static (EntityRepository, MapRouteIngressTranslator, Entity) RouteOwnedBy(int primaryOwner, int local)
    {
        var world = CreateEcsWorld();
        world.RegisterManagedComponent<DescriptorOwnership>();
        var entityMap = new NetworkEntityMap();
        var entity    = world.CreateEntity();
        world.AddComponent(entity, new NetworkAuthority(primaryOwnerId: primaryOwner, localNodeId: local));
        var plan = new RoutePlan();
        plan = plan.WithWaypoints(wps => { for (int i = 0; i < 3; i++) wps.Add(new RouteWaypoint { Position = new Vector3(i, i, 0) }); });
        world.SetManagedComponent(entity, plan);
        entityMap.Register(10, entity);
        return (world, new MapRouteIngressTranslator(null, entityMap), entity);
    }

    private static void ProcessAndPlay(EntityRepository world, MapRouteIngressTranslator translator, MapRoute sample)
    {
        var view = (ISimulationView)world;
        var cmd  = (EntityCommandBuffer)view.GetCommandBuffer();
        translator.ProcessSample(in sample, cmd, view);
        cmd.Playback(world);
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
                Position          = new Vec3d { X = i, Y = i, Z = 0 },
                SpeedMetersPerSec = 5.0,
            });
        }
        return new MapRoute { EntityId = entityId, Points = points, IsLoop = isLoop };
    }

    // ── ⭐ CE-4284 — a route edited on a node that does not own it ───────────────────────────────────────────────────────

    private sealed class AckSink : IDdsWriter<UpdateEntityDescriptorAck>
    {
        public List<UpdateEntityDescriptorAck> Acks { get; } = new();
        public void Write(UpdateEntityDescriptorAck sample) => Acks.Add(sample);
        public void DisposeInstance(UpdateEntityDescriptorAck key) { }
    }

    private static (EntityRepository World, Entity Entity, NetworkEntityMap Map) RouteNode(int localNode, RoutePlan plan)
    {
        const int Net = 7;
        var world = CreateEcsWorld();
        var e = world.CreateEntity();
        world.AddComponent(e, new NetworkIdentity(Net));
        world.AddComponent(e, new NetworkAuthority(primaryOwnerId: 1, localNodeId: localNode));   // node 1 owns the route
        world.SetManagedComponent(e, plan);
        var map = new NetworkEntityMap();
        map.Register(Net, e);
        return (world, e, map);
    }

    private static RoutePlan Route(params float[] xs) => new RoutePlan().WithWaypoints(wps =>
    {
        foreach (var x in xs) wps.Add(new RouteWaypoint { Position = new Vector3(x, 2f * x, 0f), TargetSpeed = 4f, Hold = Fdp.Toolkit.Navigation.RouteHold.Seconds, HoldSeconds = 3f });
    });

    /// <summary>
    /// ⭐ CE-4284 — the whole round on two nodes: a route edited on a station that does NOT own it (the gizmo's write: the local plan + an
    /// <see cref="UpdateEntityCommand"/>) goes to the owner as ONE <c>UpdateEntityDescriptorRequest(dtMapRoute)</c>; the owner applies it and answers;
    /// the owner's publish carries it back; the requester's route then reads the edit — it STAYS. The owner's own edit sends no request (its
    /// publish is the route topic). 🔴 Mutation: the egress ignores a <see cref="RoutePlan"/> (the old code) ⇒ no request, the owner keeps the old
    /// route and its publish overwrites the edit ⇒ red.
    /// </summary>
    [Fact]
    public void CE4284_ARouteEditedOnANonOwner_ReachesTheOwner_AndStays()
    {
        var original = Route(10f, 20f);
        var edited = Route(10f, 25f, 40f);
        var owner = RouteNode(localNode: 1, original);
        var requester = RouteNode(localNode: 2, original);

        // ① the requester edits (RouteWaypointGizmo.WriteBackAndPublish's two steps)
        requester.World.SetManagedComponent(requester.Entity, edited);
        requester.World.Bus.PublishManaged(new Fdp.Toolkit.NetworkSpawning.Events.UpdateEntityCommand
        {
            NetworkId = 7, ComponentsToUpdate = new List<object> { edited }, RequestId = System.Guid.NewGuid(),
        });
        requester.World.Bus.SwapBuffers();
        var sent = new CapturingWriter<UpdateEntityDescriptorRequest>();
        new UpdateEntityCommandEgressTranslator(sent, requester.World.Bus, requester.Map, localNodeId: 2).PollIngress(null!, requester.World);
        var req = Assert.Single(sent.Publishes);
        Assert.Equal(EDescriptorType.dtMapRoute, req.DescriptorType);
        Assert.Equal(3, req.Payload.MapRoute.Points!.Count);

        // ② the owner applies it and answers Success
        var acks = new AckSink();
        using (var participant = new CycloneDDS.Runtime.DdsParticipant(219u))
        using (var system = new UpdateEntityDescriptorRequestSystem(participant, owner.Map, acks))
            system.ProcessRequest(req, owner.World);
        var ownerPlan = ((ISimulationView)owner.World).GetManagedComponentRO<RoutePlan>(owner.Entity);
        Assert.Equal(new[] { 10f, 25f, 40f }, ownerPlan.Waypoints.Select(w => w.Position.X).ToArray());
        Assert.Equal(Fdp.Toolkit.Navigation.RouteHold.Seconds, ownerPlan.Waypoints[1].Hold);   // the whole waypoint, not just X/Y
        Assert.Equal(original.Waypoints[1].HoldSeconds, ownerPlan.Waypoints[1].HoldSeconds);
        Assert.Equal((int)NedStatusCode.Success, Assert.Single(acks.Acks).ErrorCode);

        // ③ the owner's publish carries it to every node; the requester's route reads the edit
        var topic = new CapturingWriter<MapRoute>();
        new MapRouteEgressTranslator(topic, owner.Map).ScanAndPublish(owner.World);
        new MapRouteIngressTranslator(null, requester.Map).ApplyToEntity(requester.Entity, Assert.Single(topic.Publishes), requester.World);
        var stays = ((ISimulationView)requester.World).GetManagedComponentRO<RoutePlan>(requester.Entity);
        Assert.Equal(new[] { 10f, 25f, 40f }, stays.Waypoints.Select(w => w.Position.X).ToArray());

        // the OWNER's own edit sends no request: its route topic is the publish
        owner.World.Bus.PublishManaged(new Fdp.Toolkit.NetworkSpawning.Events.UpdateEntityCommand
        {
            NetworkId = 7, ComponentsToUpdate = new List<object> { edited }, RequestId = System.Guid.NewGuid(),
        });
        owner.World.Bus.SwapBuffers();
        var fromOwner = new CapturingWriter<UpdateEntityDescriptorRequest>();
        new UpdateEntityCommandEgressTranslator(fromOwner, owner.World.Bus, owner.Map, localNodeId: 1).PollIngress(null!, owner.World);
        Assert.Empty(fromOwner.Publishes);
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
