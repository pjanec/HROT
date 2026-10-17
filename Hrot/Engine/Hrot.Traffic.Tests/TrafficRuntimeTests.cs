using System.Numerics;
using Fdp.Core;
using Fdp.Toolkit.Physics.Components;
using Fdp.Toolkit.Replication.Components;
using Fdp.Toolkit.Scenario;
using Fdp.Toolkit.Terrain;
using Fdp.Toolkit.Traffic;
using Hrot.Map.Common;
using Hrot.Map.Common.Services;
using VehicleHandle = Sim.Core.VehicleHandle;

namespace Hrot.Traffic.Tests;

/// <summary>
/// AQ93 slice 1 steps 4–5 — SUMO stepping on the HROT clock (EM-5, R-4047), the receiving-side dead reckoning (R-4020) and the
/// vicinity replicas (R-4038, R-4041), run over the demo-city terrain's real net.
/// </summary>
public sealed class TrafficRuntimeTests
{
    // ── the session ───────────────────────────────────────────────────────────────────────────

    [Fact]
    public void EM5_ASession_StepsToItsTarget_AndAHeldTargetStepsNothing()
    {
        using var session = new TrafficSession(Key());

        int first = session.StepTo(10.0);
        int again = session.StepTo(10.0);

        Assert.Equal(20, first);                      // 0.5 s steps
        Assert.Equal(0, again);                       // paused: the target did not move
        Assert.Equal(10.0, session.Time, 6);
        session.Source.Pump();
        Assert.True(session.Source.Dims.Count > 0, "SUMO put no car on the demo city in ten seconds.");
    }

    [Fact]
    public void EM5_ACatchUp_IsBoundedPerCall()
    {
        using var session = new TrafficSession(Key());

        Assert.Equal(TrafficSession.MaxStepsPerCall, session.StepTo(1000.0));
    }

    // ── the mirror ────────────────────────────────────────────────────────────────────────────

    /// <summary>R-4020 + the heading conversion: a moving car's resolved yaw points where it actually goes (SumoSharp heading is a
    /// compass bearing; HROT yaw is 0 = east, counter-clockwise — a sign or quarter-turn slip fails this).</summary>
    [Fact]
    public void R4020_ResolvedCars_MoveAlongTheirYaw_InTerrainLocalMetres()
    {
        var cache = LoadDemoCity(out _);
        using var session = new TrafficSession(Key(cache));
        var mirror = new TrafficMirror(cache.Placement);
        var a = new List<TrafficCarPose>();
        var b = new List<TrafficCarPose>();

        session.StepTo(60.0);
        session.Source.Pump();
        mirror.Resolve(session.Source, session.Lanes, 58.0, a);
        mirror.Resolve(session.Source, session.Lanes, 58.5, b);

        Assert.NotEmpty(a);
        int checkedCars = 0;
        foreach (var p1 in b)
        {
            var p0 = a.FirstOrDefault(p => p.Handle.Equals(p1.Handle));
            if (p0.Id is null) continue;
            var move = new Vector2(p1.X - p0.X, p1.Y - p0.Y);
            if (move.Length() < 2f) continue;   // stopped or crawling: no heading to check
            var dir = Vector2.Normalize(move);
            var facing = new Vector2(MathF.Cos(p1.YawRad), MathF.Sin(p1.YawRad));
            Assert.True(Vector2.Dot(dir, facing) > 0.8f, $"car {p1.Id} moves {dir} but faces {facing}");
            checkedCars++;
        }
        Assert.True(checkedCars >= 5, $"only {checkedCars} cars were moving fast enough to check");

        // Placement: local = net − anchor (2200, 2200), and the whole 4.75 km net is around the 800 m terrain box.
        Assert.All(b, p => Assert.InRange(p.X, -2400f, 2800f));
        Assert.All(b, p => Assert.InRange(p.Y, -2400f, 2800f));
    }

    // ── the replicas ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public void R4041_ACarNearABody_IsAReplica_KeptThroughTheBand_DroppedBeyondIt()
    {
        using var world = World();
        Body(world, 0f, 0f);
        var replicas = new TrafficReplicas(world, ids: null, localNodeId: 3);
        var car = new VehicleHandle(7, 1);

        replicas.Update(new[] { Pose(car, 200f) });
        Assert.True(replicas.IsReplica(car));
        var e = replicas.Replicas[car];
        Assert.Equal(ReplicationRoute.SumoSharp, world.GetComponentRO<ReplicationRoute>(e).RouteId);
        Assert.True(world.HasComponent<ScenarioIgnoreTag>(e));
        Assert.True(world.GetComponentRO<NetworkAuthority>(e).HasAuthority);
        Assert.Equal(TkbEntityTypes.TrafficCar, world.GetComponentRO<TkbIdentity>(e).TkbType);
        Assert.Equal(ForceId.Neutral, world.GetComponentRO<EntityInfo>(e).ForceId);
        Assert.Equal(2.25f, world.GetComponentRO<PhysicsCollider>(e).Radius, 3);

        replicas.Update(new[] { Pose(car, 340f) });   // past R, inside R + hysteresis: kept, and posed
        Assert.True(replicas.IsReplica(car));
        Assert.Equal(340f, world.GetComponentRO<SimTransform>(e).Position.X, 3);

        replicas.Update(new[] { Pose(car, 360f) });   // beyond the band
        Assert.False(replicas.IsReplica(car));
        Assert.False(world.IsAlive(e));
    }

    [Fact]
    public void R4041_AFarCar_IsNoEntity_AndARetiredCarsReplicaIsDestroyed()
    {
        using var world = World();
        Body(world, 0f, 0f);
        var replicas = new TrafficReplicas(world, ids: null, localNodeId: 3);
        var near = new VehicleHandle(1, 1);
        var far = new VehicleHandle(2, 1);

        replicas.Update(new[] { Pose(near, 50f), Pose(far, 1000f) });
        Assert.True(replicas.IsReplica(near));
        Assert.False(replicas.IsReplica(far));
        var e = replicas.Replicas[near];

        replicas.Update(Array.Empty<TrafficCarPose>());   // SUMO retired it
        Assert.False(world.IsAlive(e));
    }

    /// <summary>Only an HROT BODY anchors: a map graphic (no collider) does not pull cars in, and a replica does not anchor another.</summary>
    [Fact]
    public void R4041_OnlyAnHrotBody_Anchors()
    {
        using var world = World();
        var graphic = world.CreateEntity();
        world.AddComponent(graphic, new SimTransform { Position = Vector3.Zero, Rotation = Quaternion.Identity });
        var replicas = new TrafficReplicas(world, ids: null, localNodeId: 3);

        replicas.Update(new[] { Pose(new VehicleHandle(1, 1), 10f) });

        Assert.Empty(replicas.Replicas);
    }

    // ── the whole loop on the editor's clock ──────────────────────────────────────────────────

    /// <summary>R-4047 + EM-5 — the mirror system and the stepping tick, frame by frame: no traffic entity ⇒ no SUMO at all; with it,
    /// cars appear; a held clock (pause) steps nothing; a body by a road gets replicas.</summary>
    [Fact]
    public void R4047_SumoRuns_OnlyWithTheTrafficEntity_AndHoldsWhenTheClockHolds()
    {
        var cache = LoadDemoCity(out var world);
        using var _ = world;
        RegisterReplicaComponents(world);
        world.RegisterComponent<TrafficDemand>();
        using var runtime = new TrafficRuntime();
        var mirror = new TrafficMirrorSystem(runtime, ids: null, localNodeId: 1);

        Frame(world, mirror, runtime, 0.0);
        Frame(world, mirror, runtime, 5.0);
        Assert.Null(runtime.Requested);
        Assert.Equal(0, runtime.StepsTaken);

        var traffic = world.CreateEntity();
        world.AddComponent(traffic, TrafficDemand.Default);
        double t = 5.0;
        for (int i = 0; i < 160; i++) Frame(world, mirror, runtime, t += 0.25);   // 40 s of editor time
        Assert.NotEmpty(mirror.Cars);
        Assert.Same(mirror.Cars, TrafficFrame.Of(world)!.Cars);                 // the maps read this frame's cars from the world
        long steps = runtime.StepsTaken;
        Assert.True(steps > 0);

        for (int i = 0; i < 10; i++) Frame(world, mirror, runtime, t);            // paused
        Assert.Equal(steps, runtime.StepsTaken);

        // A body where the cars are: it gets replicas.
        var c = mirror.Cars[0];
        Body(world, c.X, c.Y);
        Frame(world, mirror, runtime, t += 0.25);
        Assert.NotEmpty(mirror.Replicas!.Replicas);

        world.DestroyEntity(traffic);                                               // the scenario drops traffic
        Frame(world, mirror, runtime, t += 0.25);
        Assert.Null(runtime.Requested);
        Assert.Empty(mirror.Replicas!.Replicas);
        Assert.Null(TrafficFrame.Of(world));
    }

    // ── helpers ───────────────────────────────────────────────────────────────────────────────

    private static void Frame(EntityRepository world, TrafficMirrorSystem mirror, TrafficRuntime runtime, double now)
    {
        world.SetSingleton(new GlobalTime { TotalTime = now, DeltaTime = 0.25f, TimeScale = 1f });
        mirror.Execute(world, 0.25f);
        runtime.SteppingTick();   // the stepping thread, run inline
        mirror.Execute(world, 0f);
    }

    private static TrafficSessionKey Key(TrafficNetCache? cache = null)
    {
        cache ??= LoadDemoCity(out var w).Also(w.Dispose);
        return new TrafficSessionKey(cache.Path, Path.GetDirectoryName(cache.Path)!, cache.NetHash, TrafficDemand.Default, 0);
    }

    private static TrafficNetCache LoadDemoCity(out EntityRepository world)
    {
        world = new EntityRepository();
        var residency = new TerrainResidency(new TerrainCatalog(new[] { TerrainRoot() }), new CarKinem.Road.RoadNetworkHolder());
        residency.AttachContent(new SumoNetTerrainLoader());
        residency.Commit(world, residency.Prepare("demo-city"));
        return TrafficNetCache.Of(world)!;
    }

    private static EntityRepository World()
    {
        var world = new EntityRepository();
        RegisterReplicaComponents(world);
        return world;
    }

    private static void RegisterReplicaComponents(EntityRepository world)
    {
        world.RegisterComponent<SimTransform>();
        world.RegisterComponent<PhysicsCollider>();
        world.RegisterComponent<NetworkIdentity>();
        world.RegisterComponent<NetworkAuthority>();
        world.RegisterComponent<TkbIdentity>();
        world.RegisterComponent<ReplicationRoute>();
        world.RegisterComponent<TrafficCar>();
        world.RegisterComponent<ScenarioIgnoreTag>();
        world.RegisterComponent<EntityInfo>();
    }

    private static void Body(EntityRepository world, float x, float y)
    {
        var e = world.CreateEntity();
        world.AddComponent(e, new SimTransform { Position = new Vector3(x, y, 0f), Rotation = Quaternion.Identity });
        world.AddComponent(e, new PhysicsCollider { Radius = 0.4f, Height = 1.8f, CollisionLayer = 1 });
    }

    private static TrafficCarPose Pose(VehicleHandle h, float x) => new(h, "car" + h.Index, x, 0f, 0f, 0f, 4.5f, 1.8f);

    private static string TerrainRoot()
    {
        string dir = AppContext.BaseDirectory;
        while (dir != null && !Directory.Exists(Path.Combine(dir, "Hrot", "Subsystems")))
            dir = Path.GetDirectoryName(dir)!;
        return Path.Combine(dir!, "Hrot", "Subsystems", "Hrot.AI.Behaviors", "Recipes", "Terrain");
    }
}

internal static class TestExtensions
{
    public static T Also<T>(this T value, Action after) { after(); return value; }
}
