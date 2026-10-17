using System.Numerics;
using Fdp.Core;
using Fdp.Interfaces;
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
/// AQ93 slice 1 steps 4–5 and §12 (R-4054, R-4056) — SUMO stepped on the cluster clock (EM-5, N3), its cars named by the cluster's
/// allocator (N2) and typed by the TKB (T1), the receiving-side dead reckoning (R-4020) and the vicinity replicas owned by their sender
/// (R-4038, R-4041, N5), run over the demo-city terrain's real net.
/// </summary>
public sealed class TrafficRuntimeTests
{
    // ── the session ───────────────────────────────────────────────────────────────────────────

    /// <summary>N3 — SUMO's clock starts AT the cluster time, so every sample is stamped in cluster time; a held target steps nothing.</summary>
    [Fact]
    public void N3_ASession_StartsAtTheClusterTime_AndAHeldTargetStepsNothing()
    {
        using var session = new TrafficSession(Key(), Options(), NoTypes, startTime: 1000.0);

        Assert.Equal(1000.0, session.Time, 6);
        Assert.Equal(20, session.StepTo(1010.0));   // 0.5 s steps
        Assert.Equal(0, session.StepTo(1010.0));    // paused
        session.Source.Pump();
        Assert.True(session.Source.Dims.Count > 0, "SUMO put no car on the demo city in ten seconds.");
        Assert.All(session.Source.History.Values, h => Assert.All(h, s => Assert.InRange(s.TimestampSeconds, 1000.0, 1010.0 + 1e-6)));
    }

    [Fact]
    public void EM5_ACatchUp_IsBoundedPerCall()
    {
        using var session = new TrafficSession(Key(), Options(), NoTypes, startTime: 0.0);

        Assert.Equal(TrafficSession.MaxStepsPerCall, session.StepTo(1000.0));
    }

    /// <summary>N2 — every car's SUMO vehicle id is a network id from the allocator; T1 — every car is one of the TKB's mapped types,
    /// at that type's footprint.</summary>
    [Fact]
    public void N2_T1_CarsAreNamedByTheAllocator_AndTypedByTheTkb()
    {
        long next = 5000;
        var types = TrafficVehicleCatalog.From(HrotEnvironment.CreateTkb());
        Assert.Equal(new long[] { 1002, 1050, 1051, 1052, 1053 }, types.Select(t => t.TkbType));
        using var session = new TrafficSession(Key(), Options(() => Interlocked.Increment(ref next)), types, startTime: 0.0);

        session.StepTo(30.0);
        session.Source.Pump();

        Assert.NotEmpty(session.Source.Names);
        Assert.All(session.Source.Names.Values, id => Assert.InRange(long.Parse(id), 5001, next));
        Assert.All(session.Source.Dims, kv =>
        {
            Assert.True(session.Source.Tags.TryGetValue(kv.Key, out var tag), "a car arrived with no TKB type");
            var t = types.Single(x => x.TkbType == tag.ExternalType);
            Assert.Equal(t.Length, kv.Value.Length, 2);
            Assert.Equal(t.Width, kv.Value.Width, 2);
        });
    }

    // ── the mirror ────────────────────────────────────────────────────────────────────────────

    /// <summary>R-4020 + the heading conversion: a moving car's resolved yaw points where it actually goes (SumoSharp heading is a
    /// compass bearing; HROT yaw is 0 = east, counter-clockwise — a sign or quarter-turn slip fails this). N5 — the pose names its owner.</summary>
    [Fact]
    public void R4020_ResolvedCars_MoveAlongTheirYaw_AndNameTheirOwner()
    {
        var cache = LoadDemoCity(out var w);
        using var _ = w;
        using var session = new TrafficSession(Key(cache), Options(), NoTypes, startTime: 0.0);
        var mirror = new TrafficMirror(cache.Placement, owner: 7);
        var a = new List<TrafficCarPose>();
        var b = new List<TrafficCarPose>();

        session.StepTo(60.0);
        session.Source.Pump();
        mirror.Resolve(session.Source, session.Lanes, 58.0, a);
        mirror.Resolve(session.Source, session.Lanes, 58.5, b);

        Assert.NotEmpty(a);
        Assert.All(b, p => Assert.Equal(7, p.Owner));
        Assert.All(b, p => Assert.NotEqual(0, p.NetworkId));
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
        Assert.All(b, p => Assert.InRange(p.X, -2400f, 2800f));
        Assert.All(b, p => Assert.InRange(p.Y, -2400f, 2800f));
    }

    // ── the replicas ──────────────────────────────────────────────────────────────────────────

    /// <summary>R-4041 band + N2/N5/T1/T2 — a replica carries the car's network id, is owned by its sender, has the car's TKB type and
    /// colour, and answers to its network id in the entity map.</summary>
    [Fact]
    public void R4041_ACarNearABody_IsAReplica_OfTheSendersCar_KeptThroughTheBand_DroppedBeyondIt()
    {
        using var world = World();
        Body(world, 0f, 0f);
        var map = new Fdp.Toolkit.Replication.Services.NetworkEntityMap();
        var replicas = new TrafficReplicas(world, map, localNodeId: 3);
        var car = new VehicleHandle(7, 1);

        replicas.Update(new[] { Pose(car, 200f, owner: 9, netId: 4711, tkb: 1050, color: 0x336699) });
        var e = replicas.Replicas[(9, car)];
        Assert.Equal(4711, world.GetComponentRO<NetworkIdentity>(e).Value);
        Assert.Equal(9, world.GetComponentRO<NetworkAuthority>(e).PrimaryOwnerId);
        Assert.False(world.GetComponentRO<NetworkAuthority>(e).HasAuthority);   // a replica of node 9's car
        Assert.Equal(1050, world.GetComponentRO<TkbIdentity>(e).TkbType);
        Assert.Equal(EntityAppearance.Of(0x33, 0x66, 0x99).ColorRgba, world.GetComponentRO<EntityAppearance>(e).ColorRgba);
        Assert.Equal(ReplicationRoute.SumoSharp, world.GetComponentRO<ReplicationRoute>(e).RouteId);
        Assert.True(world.HasComponent<ScenarioIgnoreTag>(e));
        Assert.Equal(ForceId.Neutral, world.GetComponentRO<EntityInfo>(e).ForceId);
        Assert.Equal(2.25f, world.GetComponentRO<PhysicsCollider>(e).Radius, 3);
        Assert.True(map.TryGetEntity(4711, out var mapped) && mapped == e);

        replicas.Update(new[] { Pose(car, 340f, owner: 9, netId: 4711) });   // past R, inside R + hysteresis: kept, and posed
        Assert.True(world.IsAlive(e));
        Assert.Equal(340f, world.GetComponentRO<SimTransform>(e).Position.X, 3);

        replicas.Update(new[] { Pose(car, 360f, owner: 9, netId: 4711) });   // beyond the band
        Assert.Empty(replicas.Replicas);
        Assert.False(world.IsAlive(e));
        Assert.False(map.TryGetEntity(4711, out _));
    }

    /// <summary>R-4055 — two senders' cars with the SAME handle are two replicas; the sender's own copy is owned; an unmapped type
    /// falls back to the inert SUMO car.</summary>
    [Fact]
    public void R4055_TwoSendersSameHandle_AreTwoReplicas_AndTheSendersOwnIsOwned()
    {
        using var world = World();
        Body(world, 0f, 0f);
        var replicas = new TrafficReplicas(world, null, localNodeId: 3);
        var h = new VehicleHandle(1, 1);

        replicas.Update(new[] { Pose(h, 50f, owner: 3, netId: 1), Pose(h, 60f, owner: 9, netId: 2) });

        Assert.Equal(2, replicas.Replicas.Count);
        Assert.True(world.GetComponentRO<NetworkAuthority>(replicas.Replicas[(3, h)]).HasAuthority);
        Assert.False(world.GetComponentRO<NetworkAuthority>(replicas.Replicas[(9, h)]).HasAuthority);
        Assert.Equal(TkbEntityTypes.TrafficCar, world.GetComponentRO<TkbIdentity>(replicas.Replicas[(9, h)]).TkbType);
    }

    [Fact]
    public void R4041_AFarCar_IsNoEntity_AndARetiredCarsReplicaIsDestroyed()
    {
        using var world = World();
        Body(world, 0f, 0f);
        var replicas = new TrafficReplicas(world, null, localNodeId: 3);
        var near = new VehicleHandle(1, 1);
        var far = new VehicleHandle(2, 1);

        replicas.Update(new[] { Pose(near, 50f, netId: 1), Pose(far, 1000f, netId: 2) });
        Assert.Single(replicas.Replicas);
        var e = replicas.Replicas[(3, near)];

        replicas.Update(Array.Empty<TrafficCarPose>());   // SUMO retired it
        Assert.False(world.IsAlive(e));
    }

    /// <summary>Only an HROT BODY anchors: a map graphic (no collider) does not pull cars in.</summary>
    [Fact]
    public void R4041_OnlyAnHrotBody_Anchors()
    {
        using var world = World();
        var graphic = world.CreateEntity();
        world.AddComponent(graphic, new SimTransform { Position = Vector3.Zero, Rotation = Quaternion.Identity });
        var replicas = new TrafficReplicas(world, null, localNodeId: 3);

        replicas.Update(new[] { Pose(new VehicleHandle(1, 1), 10f, netId: 1) });

        Assert.Empty(replicas.Replicas);
    }

    // ── the whole loop on the cluster clock ───────────────────────────────────────────────────

    /// <summary>R-4047 + EM-5 + N3 — the serving node, frame by frame: no traffic entity ⇒ no SUMO; with it (owned here), cars appear and
    /// are stamped in cluster time; a held clock steps nothing; a body by a road gets replicas named by the allocator.</summary>
    [Fact]
    public void R4047_SumoRuns_OnlyWithTheTrafficEntity_AndHoldsWhenTheClockHolds()
    {
        var cache = LoadDemoCity(out var world);
        using var _ = world;
        RegisterReplicaComponents(world);
        world.RegisterComponent<TrafficDemand>();
        long next = 70000;
        using var runtime = new TrafficRuntime(Options(() => Interlocked.Increment(ref next)));
        var mirror = new TrafficMirrorSystem(runtime, participant: null, entityMap: null, localNodeId: 1);

        Frame(world, mirror, runtime, 500.0);
        Frame(world, mirror, runtime, 505.0);
        Assert.Null(runtime.Requested);
        Assert.Equal(0, runtime.StepsTaken);

        var traffic = world.CreateEntity();
        world.AddComponent(traffic, TrafficDemand.Default);
        double t = 505.0;
        for (int i = 0; i < 160; i++) Frame(world, mirror, runtime, t += 0.25);   // 40 s of cluster time
        Assert.NotEmpty(mirror.Cars);
        Assert.Equal(505.25, runtime.Session!.StartTime, 2);   // N3 — SUMO started at the cluster time it was first asked at (the first frame with the entity)
        Assert.Same(mirror.Cars, TrafficFrame.Of(world)!.Cars);
        long steps = runtime.StepsTaken;

        for (int i = 0; i < 10; i++) Frame(world, mirror, runtime, t);            // paused
        Assert.Equal(steps, runtime.StepsTaken);

        var c = mirror.Cars[0];
        Body(world, c.X, c.Y);
        Frame(world, mirror, runtime, t += 0.25);
        Assert.NotEmpty(mirror.Replicas!.Replicas);
        Assert.All(mirror.Replicas.Replicas.Values, e => Assert.InRange(world.GetComponentRO<NetworkIdentity>(e).Value, 70001, next));

        world.DestroyEntity(traffic);                                               // the scenario drops traffic
        Frame(world, mirror, runtime, t += 0.25);
        Assert.Null(runtime.Requested);
        Assert.Empty(mirror.Replicas!.Replicas);
    }

    /// <summary>N2 — the cluster's allocator is main-thread only (DdsIdAllocator is not thread-safe): SUMO steps on ANOTHER thread
    /// here, and every allocation still happens on the thread that runs the mirror.</summary>
    [Fact]
    public void N2_TheAllocator_IsOnlyCalledOnTheMainThread()
    {
        LoadDemoCity(out var world);
        using var _ = world;
        RegisterReplicaComponents(world);
        world.RegisterComponent<TrafficDemand>();
        int main = Environment.CurrentManagedThreadId;
        var callers = new System.Collections.Concurrent.ConcurrentBag<int>();
        long next = 0;
        using var runtime = new TrafficRuntime(Options(() => { callers.Add(Environment.CurrentManagedThreadId); return Interlocked.Increment(ref next); }));
        var mirror = new TrafficMirrorSystem(runtime, participant: null, entityMap: null, localNodeId: 1);
        var traffic = world.CreateEntity();
        world.AddComponent(traffic, TrafficDemand.Default);

        double t = 0;
        for (int i = 0; i < 120; i++)
        {
            world.SetSingleton(new GlobalTime { TotalTime = t += 0.25, DeltaTime = 0.25f, TimeScale = 1f });
            mirror.Execute(world, 0.25f);
            Task.Run(runtime.SteppingTick).Wait();   // the stepping thread, really another thread
        }

        Assert.NotEmpty(mirror.Cars);
        Assert.NotEmpty(callers);
        Assert.All(callers, id => Assert.Equal(main, id));
        Assert.True(runtime.IdsReady > 0);
    }

    /// <summary>S2 — a node serves only the traffic settings it OWNS: settings another node owns (primary owner 2, nothing granted here)
    /// start no SUMO on this node.</summary>
    [Fact]
    public void S2_SettingsOwnedElsewhere_AreNotServedHere()
    {
        var cache = LoadDemoCity(out var world);
        using var _ = world;
        RegisterReplicaComponents(world);
        world.RegisterComponent<TrafficDemand>();
        using var runtime = new TrafficRuntime(Options());
        var mirror = new TrafficMirrorSystem(runtime, participant: null, entityMap: null, localNodeId: 1);
        var traffic = world.CreateEntity();
        world.AddComponent(traffic, TrafficDemand.Default);
        world.AddComponent(traffic, new NetworkAuthority(primaryOwnerId: 2, localNodeId: 1));

        for (int i = 0; i < 8; i++) Frame(world, mirror, runtime, 10.0 + i);

        Assert.Null(runtime.Requested);
        Assert.Equal(0, runtime.StepsTaken);
    }

    // ── helpers ───────────────────────────────────────────────────────────────────────────────

    private static readonly IReadOnlyList<TrafficVehicleType> NoTypes = Array.Empty<TrafficVehicleType>();

    private static TrafficServeOptions Options(Func<long>? ids = null)
    {
        long n = 0;
        return new TrafficServeOptions(ids ?? (() => Interlocked.Increment(ref n)), Participant: null, Source: 1);
    }

    private static void Frame(EntityRepository world, TrafficMirrorSystem mirror, TrafficRuntime runtime, double now)
    {
        world.SetSingleton(new GlobalTime { TotalTime = now, DeltaTime = 0.25f, TimeScale = 1f });
        mirror.Execute(world, 0.25f);
        runtime.SteppingTick();   // the stepping thread, run inline
        mirror.Execute(world, 0f);
    }

    private static TrafficSessionKey Key(TrafficNetCache? cache = null)
    {
        if (cache is null) { cache = LoadDemoCity(out var w); w.Dispose(); }
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
        world.RegisterComponent<EntityAppearance>();
    }

    private static void Body(EntityRepository world, float x, float y)
    {
        var e = world.CreateEntity();
        world.AddComponent(e, new SimTransform { Position = new Vector3(x, y, 0f), Rotation = Quaternion.Identity });
        world.AddComponent(e, new PhysicsCollider { Radius = 0.4f, Height = 1.8f, CollisionLayer = 1 });
    }

    private static TrafficCarPose Pose(VehicleHandle h, float x, int owner = 3, long netId = 0, long tkb = 0, uint color = 0)
        => new(owner, h, "car" + h.Index, netId, tkb, color, x, 0f, 0f, 0f, 4.5f, 1.8f);

    private static string TerrainRoot()
    {
        string dir = AppContext.BaseDirectory;
        while (dir != null && !Directory.Exists(Path.Combine(dir, "Hrot", "Subsystems")))
            dir = Path.GetDirectoryName(dir)!;
        return Path.Combine(dir!, "Hrot", "Subsystems", "Hrot.AI.Behaviors", "Recipes", "Terrain");
    }
}
