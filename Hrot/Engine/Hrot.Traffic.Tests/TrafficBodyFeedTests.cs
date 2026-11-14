using System.Numerics;
using CarKinem.Core;
using Fdp.Core;
using Fdp.Toolkit.Motion;
using Fdp.Toolkit.Physics.Components;
using Fdp.Toolkit.Replication.Components;
using Fdp.Toolkit.Terrain;
using Fdp.Toolkit.Tkb.Domain;
using Sim.Ingest;

namespace Hrot.Traffic.Tests;

/// <summary>⭐ AQ93 §13 F2/F3 — which of our bodies SUMO is told about, and as what.</summary>
public sealed class TrafficBodyFeedTests
{
    [Fact]
    public void F2_F3_ARiflemanIsAPerson_ATruckAVehicle_AnAircraftAndASumoCarNothing()
    {
        using var world = World();
        var rifleman = Body(world, new Vector3(10, 20, 0), LocomotionClass.Pedestrian, vp: null);
        var truck = Body(world, new Vector3(50, 0, 0), LocomotionClass.Wheeled, new VehicleParams { Class = VehicleClass.Truck, Length = 8f, Width = 2.5f },
                         velocity: new Vector3(3, 0, 0));
        Body(world, new Vector3(0, 0, 100), LocomotionClass.Rotorcraft, vp: null);
        var sumoCar = Body(world, new Vector3(5, 5, 0), LocomotionClass.Wheeled, vp: null);
        world.AddComponent(sumoCar, new ReplicationRoute(ReplicationRoute.SumoSharp));

        var bodies = TrafficBodyFeedSystem.Gather(world, Anchored());

        Assert.Equal(2, bodies.Length);
        var person = Assert.Single(bodies, b => b.NetworkId == 101);
        Assert.Equal(TrafficBodyKind.Person, person.Kind);
        Assert.Equal(2f * TrafficBodyFeedSystem.PersonRadius, person.Width);
        Assert.Equal((2210.0, 2220.0), (person.X, person.Y));   // net = local + anchor
        var vehicle = Assert.Single(bodies, b => b.NetworkId == 102);
        Assert.Equal(TrafficBodyKind.Vehicle, vehicle.Kind);
        Assert.Equal((8f, 2.5f), (vehicle.Length, vehicle.Width));
        Assert.Equal(3.0, vehicle.Vx, 3);
        _ = rifleman; _ = truck;
    }

    [Fact]
    public void F2_ANodeWithoutVehicleParams_StillFeedsAVehicle_AtTheDefaultFootprint()
    {
        using var world = new EntityRepository();
        world.RegisterComponent<SimTransform>();
        world.RegisterComponent<PhysicsCollider>();
        world.RegisterComponent<Locomotion>();
        var e = world.CreateEntity();
        world.AddComponent(e, new SimTransform { Position = new Vector3(1, 2, 0), Rotation = Quaternion.Identity });
        world.AddComponent(e, new PhysicsCollider());
        world.AddComponent(e, new Locomotion { Class = LocomotionClass.Tracked });

        var body = Assert.Single(TrafficBodyFeedSystem.Gather(world, Anchored()));

        Assert.Equal(TrafficBodyKind.Vehicle, body.Kind);
        Assert.Equal(Fdp.Toolkit.Diagnostics.Gizmos.EntityFootprint.DefaultLengthMetres, body.Length);
    }

    private static int _next = 100;

    private static Entity Body(EntityRepository world, Vector3 at, LocomotionClass c, VehicleParams? vp, Vector3 velocity = default)
    {
        var e = world.CreateEntity();
        world.AddComponent(e, new SimTransform { Position = at, Rotation = Quaternion.Identity });
        world.AddComponent(e, new SimVelocity { Linear = velocity });
        world.AddComponent(e, new PhysicsCollider());
        world.AddComponent(e, new Locomotion { Class = c });
        world.AddComponent(e, new NetworkIdentity(++_next));
        if (vp is { } p) world.AddComponent(e, p);
        return e;
    }

    private static EntityRepository World()
    {
        _next = 100;
        var world = new EntityRepository();
        world.RegisterComponent<SimTransform>();
        world.RegisterComponent<SimVelocity>();
        world.RegisterComponent<PhysicsCollider>();
        world.RegisterComponent<Locomotion>();
        world.RegisterComponent<VehicleParams>();
        world.RegisterComponent<NetworkIdentity>();
        world.RegisterComponent<ReplicationRoute>();
        return world;
    }

    private static SumoNetPlacement Anchored()
        => SumoNetPlacement.For(NetworkParser.ParseXml("<net/>"), new TerrainSumoNet("x.net.xml", 2200, 2200), null);
}
