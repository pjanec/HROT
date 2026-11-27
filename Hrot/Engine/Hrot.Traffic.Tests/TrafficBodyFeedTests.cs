using System.Linq;
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
    /// <summary>⭐ <c>CE-4623</c> AB-2 (Animals AR-17) — a cow on the road is a body SUMO's cars brake for, at its own footprint; a bird in the air
    /// is nothing to traffic. 🔴 Red-proof: drop the Quadruped arm of <c>KindOf</c> ⇒ the cow is not sent ⇒ red.</summary>
    [Fact]
    public void CE4623_ACowOnTheRoad_IsABodyCarsBrakeFor_ABirdIsNothing()
    {
        using var world = World();
        Body(world, new Vector3(30, 0, 0), LocomotionClass.Quadruped, new VehicleParams { Class = VehicleClass.Quadruped, Length = 2.4f, Width = 0.9f });
        Body(world, new Vector3(0, 0, 20), LocomotionClass.Bird, vp: null);
        var bodies = TrafficBodyFeedSystem.Gather(world, Anchored());
        var cow = Assert.Single(bodies);
        Assert.Equal(TrafficBodyKind.Vehicle, cow.Kind);
        Assert.Equal((2.4f, 0.9f), (cow.Length, cow.Width));
    }

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

    /// <summary>
    /// ⭐⭐ <c>CE-9002</c> (C-5; docs/designs/vehicle-tactics/DESIGN_Road_Convoy.md) — a road convoy is ONE continuous obstacle: the gap between a vehicle and the one ahead
    /// (here 20 m centre to centre, two 6 m trucks ⇒ 14 m) is told as squares filling it end to end, with synthetic NEGATIVE ids, AT REST (⭐ CE-1972, R-5063 — they carried the follower's velocity, which SumoSharp projected into the lead's shadow: the weave); a
    /// vehicle not in a convoy gets none. 🔴 Red-proof: drop the <c>AddConvoyGaps</c> call ⇒ only the two trucks are told ⇒ red.
    /// </summary>
    [Fact]
    public void CE9002_AConvoysGap_IsToldAsSquares_FillingItEndToEnd()
    {
        using var world = World();
        if (!world.IsComponentTypeRegistered<CarKinem.Formation.FormationController>()) world.RegisterComponent<CarKinem.Formation.FormationController>();
        if (!world.IsComponentTypeRegistered<CarKinem.Formation.FormationFollower>()) world.RegisterComponent<CarKinem.Formation.FormationFollower>();
        var truck = new VehicleParams { Class = VehicleClass.Truck, Length = 6f, Width = 2.5f };
        var lead = Body(world, new Vector3(20, 0, 0), LocomotionClass.Wheeled, truck, velocity: new Vector3(5, 0, 0));
        var follower = Body(world, new Vector3(0, 0, 0), LocomotionClass.Wheeled, truck, velocity: new Vector3(5, 0, 0));
        Body(world, new Vector3(0, 40, 0), LocomotionClass.Wheeled, truck);   // not in the convoy
        world.AddComponent(lead, new CarKinem.Formation.FormationController { Type = CarKinem.Formation.FormationType.Column, Params = new CarKinem.Formation.FormationParams { Spacing = 20f } });
        world.AddComponent(follower, new CarKinem.Formation.FormationFollower { Leader = lead, SlotIndex = 1, IsInFormation = 1 });

        var bodies = TrafficBodyFeedSystem.Gather(world, Anchored());

        var squares = bodies.Where(b => b.NetworkId < 0).OrderBy(b => b.X).ToArray();
        Assert.Equal(3, bodies.Length - squares.Length);                  // the three trucks themselves
        Assert.NotEmpty(squares);
        Assert.All(squares, q => Assert.Equal(2.5f, q.Width));
        Assert.All(squares, q => { Assert.Equal(0.0, q.Vx, 3); Assert.Equal(0.0, q.Vy, 3); });   // ⭐ CE-1972 (R-5063) — AT REST: a moving square was projected into the lead's shadow
        Assert.Contains(bodies, b => b.NetworkId > 0 && b.Vx > 4.9);                          // the trucks themselves keep their velocity
        Assert.All(squares, q => Assert.Equal(2200.0, q.Y, 3));          // on the column's line (net = local + anchor)
        double covered = squares.Sum(q => (double)q.Length);
        Assert.Equal(14.0, covered, 2);                                    // follower front (x 3) to lead rear (x 17)
        Assert.InRange(squares[0].X - squares[0].Length / 2, 2203.0 - 0.01, 2203.0 + 0.01);
        Assert.InRange(squares[^1].X + squares[^1].Length / 2, 2217.0 - 0.01, 2217.0 + 0.01);
        Assert.Equal(squares.Length, squares.Select(q => q.NetworkId).Distinct().Count());
    }

    /// <summary>
    /// ⭐ <c>CE-11072</c> — a RIDER is no body: an APC's gunner (enclosed) and a rider on an open cabin, both at the APC's place, are not told to SUMO — only the APC is. 📌 Measured
    /// on va-dozer-breach-wall: the gunner was told as a pedestrian ON the APC, and the APC's shadow braked for it for good. 🔴 Red-proof: drop the rider skip in <c>Gather</c> ⇒ three
    /// bodies, two of them persons at the vehicle.
    /// </summary>
    [Fact]
    public void CE11072_ARider_IsNoBody_OnlyItsVehicleIs()
    {
        using var world = World();
        world.RegisterComponent<Fdp.Toolkit.Behavior.Components.IsEmbarkedTag>();
        var apc = Body(world, new Vector3(30, 0, 0), LocomotionClass.Wheeled, new VehicleParams { Class = VehicleClass.Truck, Length = 7f, Width = 3.5f });
        var gunner = Body(world, new Vector3(30, 0, 0), LocomotionClass.Pedestrian, vp: null);
        world.AddComponent(gunner, new Fdp.Toolkit.Behavior.Components.IsEmbarkedTag { VehicleEntity = apc, Exposed = 0, Seat = 8 });
        var rider = Body(world, new Vector3(30, 0, 0), LocomotionClass.Pedestrian, vp: null);
        world.AddComponent(rider, new Fdp.Toolkit.Behavior.Components.IsEmbarkedTag { VehicleEntity = apc, Exposed = 1, Seat = 0 });
        Body(world, new Vector3(60, 0, 0), LocomotionClass.Pedestrian, vp: null);   // a man on foot on the road: still a body

        var bodies = TrafficBodyFeedSystem.Gather(world, Anchored());

        Assert.Equal(2, bodies.Length);
        Assert.Single(bodies, b => b.Kind == TrafficBodyKind.Vehicle);
        var person = Assert.Single(bodies, b => b.Kind == TrafficBodyKind.Person);
        Assert.Equal(2260.0, person.X);
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
