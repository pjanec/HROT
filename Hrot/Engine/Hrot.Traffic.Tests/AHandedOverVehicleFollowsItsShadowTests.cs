using System.Numerics;
using CarKinem.Core;
using Fdp.Core;
using Fdp.Toolkit.Replication.Components;
using VehicleHandle = Sim.Core.VehicleHandle;

namespace Hrot.Traffic.Tests;

/// <summary>⭐ AQ93 §14 G4–G6 (R-4023) — a body in <see cref="KinematicsMode.Traffic"/> follows its SUMO shadow <c>hrot:&lt;id&gt;</c> on the node
/// that moves it; it is taken back when the shadow is gone; a shadow is never a replica.</summary>
public sealed class AHandedOverVehicleFollowsItsShadowTests
{
    private const int Id = 4242;

    [Fact]
    public void G4_NoShadowYet_TheBodyHolds_AsADeliberateHold()
    {
        using var world = World(out var e);
        Frame(world, cars: Array.Empty<TrafficCarPose>(), now: 1.0);

        new TrafficShadowFollowSystem().Execute(world, 0.05f);

        Assert.Equal(1, world.GetComponent<NavState>(e).IsBlocked);   // NavigationExecutionSystem: not "stuck"
        Assert.Equal<Vector3>(new Vector3(10, 10, 0), world.GetComponent<SimTransform>(e).Position);
    }

    [Fact]
    public void G4_TheBody_TakesTheShadowsPose_AndItsSpeed()
    {
        using var world = World(out var e);
        var follow = new TrafficShadowFollowSystem();
        Frame(world, new[] { Shadow(100, 50, 0.5f) }, now: 1.0);
        follow.Execute(world, 0.05f);
        Frame(world, new[] { Shadow(105, 50, 0.5f) }, now: 1.5);
        follow.Execute(world, 0.05f);

        var tf = world.GetComponent<SimTransform>(e);
        Assert.Equal<Vector3>(new Vector3(105, 50, 0), tf.Position);
        Assert.Equal(0.5f, 2f * MathF.Atan2(tf.Rotation.Z, tf.Rotation.W), 3);
        Assert.Equal(10f, world.GetComponent<VehicleState>(e).Speed, 3);   // 5 m in 0.5 s
        Assert.Equal(0, world.GetComponent<NavState>(e).IsBlocked);
    }

    [Theory]
    [InlineData(395f, 400f, 1)]   // gone at the destination ⇒ Arrived
    [InlineData(150f, 50f, 0)]    // gone far from it (the traffic node died) ⇒ not arrived
    public void G5_TheShadowGone_EndsTheMode_ArrivedOnlyNearTheDestination(float x, float y, byte arrived)
    {
        using var world = World(out var e);
        var follow = new TrafficShadowFollowSystem();
        Frame(world, new[] { Shadow(x, y, 0f) }, now: 1.0);
        follow.Execute(world, 0.05f);
        Frame(world, Array.Empty<TrafficCarPose>(), now: 1.5);
        follow.Execute(world, 0.05f);

        var nav = world.GetComponent<NavState>(e);
        Assert.Equal(KinematicsMode.None, nav.Mode);
        Assert.Equal(arrived, nav.HasArrived);
        Assert.Equal(0f, world.GetComponent<VehicleState>(e).Speed);
    }

    /// <summary>⭐ <c>CE-9020</c> ③ — a hand-over SUMO will not take (no shadow ever comes) holds only <see cref="TrafficShadowFollowSystem.RefusedHoldSeconds"/>:
    /// then the traffic leg ends where the body stands (arrived — the move's executor drives on itself). 🔴 Red-proof: no time-out ⇒ it holds for ever
    /// (CE-9020, measured on <c>cv-mode-1-to-2</c>) ⇒ red.</summary>
    [Fact]
    public void CE9020_ARefusedHandover_HoldsOnlyUntilTheTimeOut()
    {
        using var world = World(out var e);
        var follow = new TrafficShadowFollowSystem();
        Frame(world, Array.Empty<TrafficCarPose>(), now: 1.0);
        follow.Execute(world, 0.05f);
        Frame(world, Array.Empty<TrafficCarPose>(), now: 1.0 + TrafficShadowFollowSystem.RefusedHoldSeconds - 0.1);
        follow.Execute(world, 0.05f);
        Assert.Equal(KinematicsMode.Traffic, world.GetComponent<NavState>(e).Mode);   // still waiting
        Assert.Equal(1, world.GetComponent<NavState>(e).IsBlocked);

        Frame(world, Array.Empty<TrafficCarPose>(), now: 1.0 + TrafficShadowFollowSystem.RefusedHoldSeconds + 0.1);
        follow.Execute(world, 0.05f);
        var nav = world.GetComponent<NavState>(e);
        Assert.Equal(KinematicsMode.None, nav.Mode);
        Assert.Equal(1, nav.HasArrived);
        Assert.Equal(0, nav.IsBlocked);
        Assert.False(world.HasComponent<TrafficShadowFollow>(e));
    }

    /// <summary>⭐ <c>CE-9020</c> ② — the destination (400, 400) is 100 m off the only road (y = 300): the hand-over sent SUMO the nearest road
    /// point (400, 300), and a shadow gone THERE is the traffic leg's arrival (the executor drives on); gone elsewhere it is still lost.
    /// 🔴 Red-proof: the follow judging only the destination ⇒ "lost" at the road point ⇒ red.</summary>
    [Theory]
    [InlineData(395f, 300f, 1)]   // at the road point
    [InlineData(150f, 300f, 0)]   // on the road, far from it
    public void CE9020_TheShadowGoneAtTheRoadPoint_IsTheTrafficLegsArrival(float x, float y, byte arrived)
    {
        using var world = World(out var e);
        world.RegisterManagedComponent<TrafficNetCache>();
        var model = Sim.Ingest.NetworkParser.ParseXml(
            "<net><location netOffset=\"0,0\" convBoundary=\"0,0,600,600\" projParameter=\"!\"/>"
            + "<edge id=\"e\" from=\"a\" to=\"b\"><lane id=\"e_0\" index=\"0\" speed=\"13.9\" length=\"600\" shape=\"0,300 600,300\"/></edge></net>");
        var place = SumoNetPlacement.For(model, new Fdp.Toolkit.Terrain.TerrainSumoNet("x.net.xml", 0, 0), null);
        world.SetSingletonManaged(new TrafficNetCache("x.net.xml", "x.net.xml", model, "0", place));
        Assert.True(TrafficRoadPoint.Near(model, place, new Vector2(400, 400), out var road, out bool onNet));
        Assert.False(onNet);
        Assert.Equal(400f, road.X, 1); Assert.Equal(300f, road.Y, 1);

        var follow = new TrafficShadowFollowSystem();
        Frame(world, new[] { Shadow(x, y, 0f) }, now: 1.0);
        follow.Execute(world, 0.05f);
        Frame(world, Array.Empty<TrafficCarPose>(), now: 1.5);
        follow.Execute(world, 0.05f);

        Assert.Equal(KinematicsMode.None, world.GetComponent<NavState>(e).Mode);
        Assert.Equal(arrived, world.GetComponent<NavState>(e).HasArrived);
    }

    [Fact]
    public void G4_ABodyThisNodeDoesNotMove_IsLeftAlone()
    {
        using var world = World(out var e);
        world.SetAuthority<SimTransform>(e, false);
        Frame(world, new[] { Shadow(100, 50, 0f) }, now: 1.0);

        new TrafficShadowFollowSystem().Execute(world, 0.05f);

        Assert.Equal<Vector3>(new Vector3(10, 10, 0), world.GetComponent<SimTransform>(e).Position);
    }

    [Fact]
    public void G6_AShadow_IsNeverAReplica_AndIsDrawnAsAnEntity()
    {
        using var world = new EntityRepository();
        TrafficRuntimeTests.RegisterReplicaComponents(world);
        var body = world.CreateEntity();
        world.AddComponent(body, new SimTransform { Position = new Vector3(100, 50, 0), Rotation = Quaternion.Identity });
        world.AddComponent(body, new Fdp.Toolkit.Physics.Components.PhysicsCollider());
        var replicas = new TrafficReplicas(world, entityMap: null, localNodeId: 1);
        var shadow = Shadow(101, 50, 0f);

        replicas.Update(new[] { shadow });

        Assert.Empty(replicas.Replicas);
        Assert.True(replicas.IsEntity(shadow));
    }

    /// <summary>
    /// ⭐ <c>R-3039</c> S1 (docs/DESIGN_System_State_Inventory.md §6 #14) — the follow's memory survives a checkpoint restored into a fresh
    /// process: the shadow seen before the checkpoint and gone after it ends the mode (the body is taken back), and a shadow still there
    /// gives the body the speed of the step from the restored sighting. 🔴 Red-proof: as a field of the system, the fresh system read
    /// "never seen" — the body held (IsBlocked) forever instead of being taken back, and resumed at speed 0.
    /// </summary>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void R3039_TheFollow_SurvivesACheckpointRestoredIntoAFreshSystem(bool shadowStillThere)
    {
        using var world = World(out var e);
        Frame(world, new[] { Shadow(100, 50, 0f) }, now: 1.0);
        new TrafficShadowFollowSystem().Execute(world, 0.05f);

        var kf = Fdp.Toolkit.Replay.WorldKeyframes.Capture(world, Fdp.Toolkit.Replay.CaptureOptions.Recording);
        using var fresh = new EntityRepository();
        Register(fresh);
        Fdp.Toolkit.Replay.WorldKeyframes.Apply(fresh, kf);
        fresh.SetAuthority<SimTransform>(e, true);
        Assert.True(fresh.HasComponent<TrafficShadowFollow>(e), "the keyframe carries the follow");

        Frame(fresh, shadowStillThere ? new[] { Shadow(105, 50, 0f) } : Array.Empty<TrafficCarPose>(), now: 1.5);
        new TrafficShadowFollowSystem().Execute(fresh, 0.05f);

        var nav = fresh.GetComponent<NavState>(e);
        if (shadowStillThere)
        {
            Assert.Equal(KinematicsMode.Traffic, nav.Mode);
            Assert.Equal(10f, fresh.GetComponent<VehicleState>(e).Speed, 3);   // 5 m in 0.5 s, from the restored sighting
        }
        else
        {
            Assert.Equal(KinematicsMode.None, nav.Mode);   // taken back, not held
            Assert.Equal(0, nav.IsBlocked);
            Assert.False(fresh.HasComponent<TrafficShadowFollow>(e));
        }
    }

    /// <summary>⭐ <c>CE-9000</c> (C-1) — a body moving <c>InTrafficPriority</c> (a convoy's escort, beacon and siren on) is handed over FLAGGED priority; plain
    /// <c>InTraffic</c> is not. 🔴 Red-proof: drop the flag in <c>TrafficHandoverSystem.Gather</c> ⇒ red.</summary>
    [Fact]
    public void CE9000_TheEscortsHandover_CarriesItsPriority_APlainOneDoesNot()
    {
        using var world = new EntityRepository();
        world.RegisterComponent<SimTransform>(); world.RegisterComponent<NetworkIdentity>();
        world.RegisterComponent<Fdp.Toolkit.Navigation.NavigationIntent>();
        TrafficHandover[] Hand(Fdp.Toolkit.Navigation.RoadUse use)
        {
            var e = world.CreateEntity();
            world.AddComponent(e, new SimTransform { Position = new Vector3(10, 10, 0), Rotation = Quaternion.Identity });
            world.AddComponent(e, new NetworkIdentity(7000 + e.Index));
            world.AddComponent(e, new Fdp.Toolkit.Navigation.NavigationIntent
            {
                Mode = Fdp.Toolkit.Navigation.NavigationMode.Traffic, IntentId = 1,
                Flags = Fdp.Toolkit.Navigation.NavigationConstants.WithRoadUse(0, use),
            });
            var all = TrafficHandoverSystem.Gather(world, SumoNetPlacement.For(Sim.Ingest.NetworkParser.ParseXml("<net/>"), new Fdp.Toolkit.Terrain.TerrainSumoNet("x.net.xml", 0, 0), null));
            world.DestroyEntity(e);
            return all;
        }
        Assert.True(Assert.Single(Hand(Fdp.Toolkit.Navigation.RoadUse.InTrafficPriority)).Priority);
        Assert.False(Assert.Single(Hand(Fdp.Toolkit.Navigation.RoadUse.InTraffic)).Priority);
    }

    /// <summary>⭐ <c>CE-9008</c> (C-6, generic) — a shadow's top speed is the speed its brain ASKED (the intent's <c>TargetSpeed</c>; none ⇒ SUMO's own), and a
    /// road column's lead is held lower while its tail lags — measured on the traffic node from the members' positions (no trail there on a cluster, C-8):
    /// a tight column keeps the asked speed, a stretched one is capped, a lagging tail at three gaps stops it. 🔴 Red-proofs: <c>Gather</c> ignoring
    /// <c>TargetSpeed</c> ⇒ the first row red; the column cap dropped ⇒ the stretched rows red.</summary>
    [Fact]
    public void CE9008_TheShadowsTopSpeed_IsTheAskedSpeed_AndAStretchedColumnsLeadIsHeldLower()
    {
        using var world = new EntityRepository();
        world.RegisterComponent<SimTransform>(); world.RegisterComponent<NetworkIdentity>();
        world.RegisterComponent<Fdp.Toolkit.Navigation.NavigationIntent>();
        world.RegisterComponent<CarKinem.Formation.FormationController>(); world.RegisterComponent<CarKinem.Formation.FormationFollower>();
        var place = SumoNetPlacement.For(Sim.Ingest.NetworkParser.ParseXml("<net/>"), new Fdp.Toolkit.Terrain.TerrainSumoNet("x.net.xml", 0, 0), null);
        Entity At(float x)
        {
            var e = world.CreateEntity();
            world.AddComponent(e, new SimTransform { Position = new Vector3(x, 10, 0), Rotation = Quaternion.Identity });
            world.AddComponent(e, new NetworkIdentity(7100 + e.Index));
            return e;
        }
        var lead = At(100f);
        world.AddComponent(lead, new Fdp.Toolkit.Navigation.NavigationIntent { Mode = Fdp.Toolkit.Navigation.NavigationMode.Traffic, IntentId = 1, TargetSpeed = 10f });
        double? Cap() => Assert.Single(TrafficHandoverSystem.Gather(world, place)).MaxSpeed;

        Assert.Equal(10.0, Cap()!.Value, 3);                                                  // the asked speed, no column

        world.AddComponent(lead, new CarKinem.Formation.FormationController
            { Type = CarKinem.Formation.FormationType.Column, Params = new CarKinem.Formation.FormationParams { Spacing = 15f } });
        var f1 = At(85f); var f2 = At(70f);
        world.AddComponent(f1, new CarKinem.Formation.FormationFollower { Leader = lead, SlotIndex = 1, IsInFormation = 1 });
        world.AddComponent(f2, new CarKinem.Formation.FormationFollower { Leader = lead, SlotIndex = 2, IsInFormation = 1 });
        Assert.Equal(10.0, Cap()!.Value, 3);                                                  // tight: 30 m for two gaps of 15

        world.GetComponentRW<SimTransform>(f2).Position = new Vector3(45f, 10, 0);           // the tail 25 m behind its place: 1⅔ gaps
        double held = Cap()!.Value;
        Assert.InRange(held, 4.0, 8.0);                                                       // 1 − (25 − 15)/30 = ⅔ of 10

        world.GetComponentRW<SimTransform>(f2).Position = new Vector3(10f, 10, 0);           // 60 m behind: past three gaps
        Assert.Equal(0.0, Cap()!.Value, 3);

        world.GetComponentRW<Fdp.Toolkit.Navigation.NavigationIntent>(lead).TargetSpeed = 0f;   // asked nothing: SUMO's own speed …
        world.GetComponentRW<SimTransform>(f2).Position = new Vector3(70f, 10, 0);
        Assert.Null(Cap());                                                                   // … and a tight column adds no cap
    }

    private static TrafficCarPose Shadow(float x, float y, float yaw)
        => new(Owner: 800, Handle: new VehicleHandle(7, 1), Id: TrafficShadowFollowSystem.ShadowIdOf(Id), NetworkId: Id, TkbType: (Fdp.Core.TkbTypeId)(0), Color: 0,
               X: x, Y: y, Z: 0f, YawRad: yaw, Length: 5f, Width: 2f);

    private static void Frame(EntityRepository world, IReadOnlyList<TrafficCarPose> cars, double now)
    {
        world.SetSingleton(new GlobalTime { TotalTime = now, DeltaTime = 0.05f, TimeScale = 1f });
        world.SetSingletonManaged(new TrafficFrame(cars, _ => true, new Dictionary<int, byte>()));
    }

    private static EntityRepository World(out Entity e)
    {
        var world = new EntityRepository();
        Register(world);
        e = world.CreateEntity();
        world.AddComponent(e, new NavState { Mode = KinematicsMode.Traffic, FinalDestination = new Vector3(400, 400, 0), ArrivalRadius = 5f });
        world.AddComponent(e, new SimTransform { Position = new Vector3(10, 10, 0), Rotation = Quaternion.Identity });
        world.AddComponent(e, new SimVelocity());
        world.AddComponent(e, new VehicleState());
        world.AddComponent(e, new NetworkIdentity(Id));
        world.SetAuthority<SimTransform>(e, true);
        return world;
    }

    private static void Register(EntityRepository world)
    {
        world.RegisterComponent<NavState>();
        world.RegisterComponent<SimTransform>();
        world.RegisterComponent<SimVelocity>();
        world.RegisterComponent<VehicleState>();
        world.RegisterComponent<NetworkIdentity>();
        world.RegisterComponent<TrafficShadowFollow>();
        world.RegisterManagedComponent<TrafficFrame>();
    }
}
