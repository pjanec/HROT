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
    private const long Id = 4242;

    [Fact]
    public void G4_NoShadowYet_TheBodyHolds_AsADeliberateHold()
    {
        using var world = World(out var e);
        Frame(world, cars: Array.Empty<TrafficCarPose>(), now: 1.0);

        new TrafficShadowFollowSystem().Execute(world, 0.05f);

        Assert.Equal(1, world.GetComponent<NavState>(e).IsBlocked);   // NavigationExecutionSystem: not "stuck"
        Assert.Equal(new Vector3(10, 10, 0), world.GetComponent<SimTransform>(e).Position);
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
        Assert.Equal(new Vector3(105, 50, 0), tf.Position);
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

    [Fact]
    public void G4_ABodyThisNodeDoesNotMove_IsLeftAlone()
    {
        using var world = World(out var e);
        world.SetAuthority<SimTransform>(e, false);
        Frame(world, new[] { Shadow(100, 50, 0f) }, now: 1.0);

        new TrafficShadowFollowSystem().Execute(world, 0.05f);

        Assert.Equal(new Vector3(10, 10, 0), world.GetComponent<SimTransform>(e).Position);
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

    private static TrafficCarPose Shadow(float x, float y, float yaw)
        => new(Owner: 800, Handle: new VehicleHandle(7, 1), Id: TrafficShadowFollowSystem.ShadowIdOf(Id), NetworkId: Id, TkbType: 0, Color: 0,
               X: x, Y: y, Z: 0f, YawRad: yaw, Length: 5f, Width: 2f);

    private static void Frame(EntityRepository world, IReadOnlyList<TrafficCarPose> cars, double now)
    {
        world.SetSingleton(new GlobalTime { TotalTime = now, DeltaTime = 0.05f, TimeScale = 1f });
        world.SetSingletonManaged(new TrafficFrame(cars, _ => true, new Dictionary<int, byte>()));
    }

    private static EntityRepository World(out Entity e)
    {
        var world = new EntityRepository();
        world.RegisterComponent<NavState>();
        world.RegisterComponent<SimTransform>();
        world.RegisterComponent<SimVelocity>();
        world.RegisterComponent<VehicleState>();
        world.RegisterComponent<NetworkIdentity>();
        world.RegisterManagedComponent<TrafficFrame>();
        e = world.CreateEntity();
        world.AddComponent(e, new NavState { Mode = KinematicsMode.Traffic, FinalDestination = new Vector3(400, 400, 0), ArrivalRadius = 5f });
        world.AddComponent(e, new SimTransform { Position = new Vector3(10, 10, 0), Rotation = Quaternion.Identity });
        world.AddComponent(e, new SimVelocity());
        world.AddComponent(e, new VehicleState());
        world.AddComponent(e, new NetworkIdentity(Id));
        world.SetAuthority<SimTransform>(e, true);
        return world;
    }
}
