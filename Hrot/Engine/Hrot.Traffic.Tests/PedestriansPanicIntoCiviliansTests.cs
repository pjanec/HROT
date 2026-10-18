using System.Numerics;
using Fdp.Core;
using Fdp.Toolkit.Combat.Contracts;
using Fdp.Toolkit.Combat.Events;
using Fdp.Toolkit.NetworkSpawning.Events;
using Fdp.Toolkit.Physics.Components;
using Fdp.Toolkit.Replication.Components;
using Fdp.Toolkit.Replication.Services;
using Fdp.Toolkit.Scenario;
using Fdp.Toolkit.Terrain;
using Fdp.Toolkit.Traffic;
using Hrot.Core.Tkb;
using Hrot.Map.Common;
using Hrot.Map.Common.Services;

namespace Hrot.Traffic.Tests;

/// <summary>
/// ⭐ AQ93 §15 (slice 5, R-4031/R-4032, CV-12) over demo-city's real net, through the runtime and mirror every serving node runs:
/// pedestrians walk named by OUR allocator (K2) and are civilian replicas near our bodies (K3); a shot near them gives them up from SUMO
/// (K4) as creation requests for terrified civilians keeping their ids (K5), never more than the cap (K6).
/// </summary>
public sealed class PedestriansPanicIntoCiviliansTests
{
    private const int Walkers = 60;

    [Fact]
    public void K2K3_PedestriansWalk_NamedByOurAllocator_AndAreCivilianReplicasNearOurBodies()
    {
        using var h = Harness.Start(Walkers);
        h.Run(60.0);
        Assert.NotEmpty(h.Mirror.Pedestrians);
        Assert.All(h.Mirror.Pedestrians, p => Assert.InRange(p.NetworkId, Harness.FirstId + 1, h.LastId));

        var ped = h.Mirror.Pedestrians[0];
        h.Body(ped.X, ped.Y);
        h.Run(0.5);

        var e = Assert.Single(h.Mirror.Replicas!.Pedestrians.Where(kv => kv.Key.NetworkId == ped.NetworkId)).Value;
        Assert.Equal(TrafficPanicSystem.TypeOf(ped.NetworkId), h.World.GetComponentRO<TkbIdentity>(e).TkbType);   // CE-4031: the type he converts into
        Assert.Equal(TrafficCar.KindPedestrian, h.World.GetComponentRO<TrafficCar>(e).Kind);
        Assert.Equal(ForceId.Neutral, h.World.GetComponentRO<EntityInfo>(e).ForceId);
        Assert.True(h.Map.TryGetEntity(ped.NetworkId, out var mapped) && mapped == e);
        Assert.True(h.Mirror.Replicas.IsPedReplica(ped));
        Assert.Equal(TrafficReplicas.ReplicaHealth, h.World.GetComponentRO<Fdp.Toolkit.Combat.Components.Health>(e).Current);   // a burst can hit him (K7)
    }

    [Fact]
    public void K4K5_AShotNearPedestrians_GivesThemUp_AsTerrifiedCivilianRequests_KeepingTheirIds()
    {
        using var h = Harness.Start(Walkers);
        h.Run(60.0);
        var ped = h.Mirror.Pedestrians[0];
        var at = new Vector3(ped.X, ped.Y, 0f);

        h.Shoot(at);
        var requests = h.RunCollecting(2.0);

        Assert.NotEmpty(requests);
        Assert.Contains(requests, r => r.PreAllocatedNetworkId == ped.NetworkId);
        foreach (var r in requests)
        {
            Assert.Equal(0, r.OwnerAppInstanceId);   // the system picks the brain node (R-4029)
            Assert.True(r.IsTransient);
            Assert.InRange(r.TkbType, CivilianTkbCatalog.TkbCivilianAdultMale, CivilianTkbCatalog.TkbCivilianElderly);
            Assert.Equal(TrafficPanicSystem.TypeOf(r.PreAllocatedNetworkId), r.TkbType);
            var tf = Assert.IsType<SimTransform>(Assert.Single(r.InitialComponents!));
            Assert.True(Vector2.Distance(new(tf.Position.X, tf.Position.Y), new(at.X, at.Y)) <= TrafficDemand.DefaultShotPanicRadius + 5f);
            Assert.Contains("\"Fear\":1", r.InitialAttributesJson);
        }
        Assert.DoesNotContain(h.Mirror.Pedestrians, p => p.NetworkId == ped.NetworkId);   // SUMO gave him up (the Despawn, H14 S2)
        Assert.Equal(requests.Count, requests.Select(r => r.PreAllocatedNetworkId).Distinct().Count());
    }

    /// <summary>⭐⭐ <c>CE-3248</c> (docs/DESIGN_Civilians_And_Crowd_Control.md §7.17a TC-6) — a stone THROWN where the shot above was fired
    /// frightens nobody off the street: a throw is no gunshot. ✅ Red-proof: <c>Threw</c> dropped from <c>TrafficPanicSystem</c> ⇒ he is
    /// given up as K4 ⇒ red.</summary>
    [Fact]
    public void CE3248_AThrowNearPedestrians_GivesNobodyUp()
    {
        using var h = Harness.Start(Walkers);
        h.Run(60.0);
        var ped = h.Mirror.Pedestrians[0];
        var at = new Vector3(ped.X, ped.Y, 0f);

        h.Throw(at);
        Assert.Empty(h.RunCollecting(2.0));
    }

    [Fact]
    public void K6_NoMoreThanTheCap_AreGivenUp()
    {
        using var h = Harness.Start(Walkers, maxConverted: 2);
        h.Run(60.0);
        var ped = h.Mirror.Pedestrians[0];

        h.Blast(new Vector3(ped.X, ped.Y, 0f));
        var first = h.RunCollecting(1.0);
        h.Blast(new Vector3(ped.X, ped.Y, 0f));
        var second = h.RunCollecting(1.0);

        Assert.Equal(2, first.Count + second.Count);
        Assert.Equal(2, h.Runtime.Session!.Converted);
    }

    /// <summary>⭐ CE-4027 (AQ93 §8 P7's second half) — above the cap the frightened pedestrians stay SUMO's and FLEE there (SumoSharp H16):
    /// same ids, still replicas on the wire, running away from the blast. ⛔ Before: they stayed in SUMO calm, walking their old trips.</summary>
    [Fact]
    public void K6_AboveTheCap_TheRestFleeInsideSumo_KeepingTheirIds()
    {
        using var h = Harness.Start(Walkers, maxConverted: 1);
        h.Run(60.0);
        const float Inside = TrafficDemand.DefaultBlastPanicRadius - 10f;
        var all = h.Mirror.Pedestrians;
        var ped = all.MaxBy(c => all.Count(p => Vector2.Distance(new(p.X, p.Y), new(c.X, c.Y)) < Inside))!;   // the blast where most walk
        var at = new Vector2(ped.X, ped.Y);
        float Dist(TrafficPedPose p) => Vector2.Distance(new(p.X, p.Y), at);
        var near = all.Where(p => Dist(p) < Inside).ToDictionary(p => p.NetworkId, Dist);
        Assert.True(near.Count >= 3, $"only {near.Count} pedestrians near the blast");

        h.Blast(new Vector3(at, 0f));
        var requests = h.RunCollecting(1.0);
        Assert.Single(requests);
        Assert.Equal(1, h.Runtime.Session!.Converted);
        Assert.True(h.Runtime.Session.Fled >= near.Count - 2, $"fled {h.Runtime.Session.Fled} of {near.Count - 1} left in SUMO");

        h.Run(10.0);
        var stayed = h.Mirror.Pedestrians.Where(p => near.ContainsKey(p.NetworkId) && p.NetworkId != requests[0].PreAllocatedNetworkId).ToList();
        Assert.NotEmpty(stayed);   // SUMO's still: same ids on the wire (a fleeing ped is a new leg, not a despawn)
        // ⚠ not EVERY one: H16's second pass takes any route ENDING ≥ minDistance off when no route leads straight away, and a street can
        //   bend back past the blast first (📌 measured: one of four, 81.7 → 58.8 m at 10 s). Most run away, and on average they do.
        Assert.True(stayed.Count(p => Dist(p) > near[p.NetworkId]) * 2 > stayed.Count,
            "most did not run away: " + string.Join(", ", stayed.Select(p => $"{p.NetworkId} {near[p.NetworkId]:0.#}→{Dist(p):0.#}")));
        Assert.True(stayed.Average(p => Dist(p) - near[p.NetworkId]) > 10f, "the rest did not run away from the blast");
    }

    [Fact]
    public void K7_AHitPedestrian_IsGivenUp_AndTheHitReachesTheCivilianHeBecomes()
    {
        using var h = Harness.Start(Walkers, maxConverted: 1);
        h.Run(60.0);
        var ped = h.Mirror.Pedestrians[0];
        h.Body(ped.X, ped.Y);
        h.Run(0.5);
        var replica = h.Mirror.Replicas!.Pedestrians.First(kv => kv.Key.NetworkId == ped.NetworkId).Value;

        h.World.Bus.Publish(new DamageAssessedEvent { HitEntity = replica, TotalDamage = 35f, IsRemote = true });
        var requests = h.RunCollecting(1.0);

        var r = Assert.Single(requests);   // only him: a hit is not an incident, and it is never capped
        Assert.Equal(ped.NetworkId, r.PreAllocatedNetworkId);
        Assert.Contains(ped.NetworkId, h.Panic.HeldHits);

        // the brain node made him (here: by hand, under the same id) — the held hit is re-published to him, as this node's own
        var civilian = h.World.CreateEntity();
        h.World.AddComponent(civilian, new NetworkIdentity(ped.NetworkId));
        h.Map.Register(ped.NetworkId, civilian);
        var hits = h.RunCollectingHits(0.5);

        var delivered = Assert.Single(hits, x => x.HitEntity == civilian);
        Assert.Equal(35f, delivered.TotalDamage);
        Assert.False(delivered.IsRemote);
        Assert.Empty(h.Panic.HeldHits);
    }

    [Fact]
    public void K7_AHitCar_LeavesSumo_AsOurStoppedCar_KeepingItsId_AndItsDriverGetsOutInPanic()
    {
        using var h = Harness.Start(Walkers);
        h.Run(60.0);
        var car = h.Mirror.Cars.First(c => c.NetworkId > 0 && !TrafficReplicas.IsShadow(c));
        h.Body(car.X + 3f, car.Y + 3f);
        h.Run(0.5);
        var replica = h.Mirror.Replicas!.Replicas.Values.First(e => h.World.GetComponentRO<NetworkIdentity>(e).Value == car.NetworkId);

        h.World.Bus.Publish(new DamageAssessedEvent { HitEntity = replica, TotalDamage = 120f, IsRemote = true });
        var requests = h.RunCollecting(1.0);

        var ours = Assert.Single(requests, r => r.PreAllocatedNetworkId == car.NetworkId);
        Assert.Equal(Hrot.Core.Tkb.UrbanCombatTkbCatalog.TkbCivilianCar, ours.TkbType);   // no TKB car types here (T1): our civilian car
        Assert.True(ours.IsTransient);
        var at = Assert.IsType<SimTransform>(Assert.Single(ours.InitialComponents!)).Position;
        Assert.True(Vector2.Distance(new(at.X, at.Y), new(car.X, car.Y)) < 15f, "our car stands where SUMO's was");
        var driver = Assert.Single(requests, r => r.PreAllocatedNetworkId == 0);
        Assert.InRange(driver.TkbType, CivilianTkbCatalog.TkbCivilianAdultMale, CivilianTkbCatalog.TkbCivilianElderly);
        Assert.Contains("\"Fear\":1", driver.InitialAttributesJson);
        Assert.DoesNotContain(h.Mirror.Cars, c => c.NetworkId == car.NetworkId);   // gone from SUMO's wire (H15)
        Assert.Contains(car.NetworkId, h.Panic.HeldHits);                          // its hit waits for our car
        Assert.False(h.World.IsAlive(replica));
    }

    [Fact]
    public void K5_AReleasedPedestrian_LosesItsReplica_AndIsNotRemade()
    {
        var world = new EntityRepository();
        TrafficRuntimeTests.RegisterReplicaComponents(world);
        var body = world.CreateEntity();
        world.AddComponent(body, new SimTransform { Rotation = Quaternion.Identity });
        world.AddComponent(body, new PhysicsCollider { Radius = 0.4f, Height = 1.8f, CollisionLayer = 1 });
        var map = new NetworkEntityMap();
        var replicas = new TrafficReplicas(world, map, localNodeId: 3);
        var ped = new TrafficPedPose(3, 4242, "4242", 10f, 0f, 0f, 0f);

        replicas.Update(Array.Empty<TrafficCarPose>(), new[] { ped });
        var e = replicas.Pedestrians[(3, 4242)];

        replicas.Release(4242);
        Assert.False(world.IsAlive(e));
        Assert.False(map.TryGetEntity(4242, out _));

        replicas.Update(Array.Empty<TrafficCarPose>(), new[] { ped });   // SUMO still shows him for a frame
        Assert.Empty(replicas.Pedestrians);
        replicas.Update(Array.Empty<TrafficCarPose>(), Array.Empty<TrafficPedPose>());
        replicas.Update(Array.Empty<TrafficCarPose>(), new[] { ped });   // a new pedestrian with that name later is a replica again
        Assert.Single(replicas.Pedestrians);
        world.Dispose();
    }

    [Fact]
    public void K5_TheFright_IsTheCreationAttributes()
    {
        Assert.Equal("{\"Heading\":90,\"Fear\":1,\"FrightFrom\":{\"X\":1.5,\"Y\":-2,\"Z\":0}}",
            TrafficPanicSystem.FrightJson(0.0, new Vector3(1.5f, -2f, 0f)));
        Assert.Equal(CivilianTkbCatalog.TkbCivilianChild, TrafficPanicSystem.TypeOf(18));
        Assert.Equal(CivilianTkbCatalog.TkbCivilianElderly, TrafficPanicSystem.TypeOf(29));
    }

    private sealed class Harness : IDisposable
    {
        public const long FirstId = 90000;
        private long _next = FirstId;
        private double _t = 100.0;
        public EntityRepository World = null!;
        public TrafficRuntime Runtime = null!;
        public TrafficMirrorSystem Mirror = null!;
        public TrafficPanicSystem Panic = null!;
        public NetworkEntityMap Map = new();
        public long LastId => Interlocked.Read(ref _next);

        public static Harness Start(int walkers, int maxConverted = 0)
        {
            var h = new Harness();
            h.World = new EntityRepository();
            var residency = new TerrainResidency(new TerrainCatalog(new[] { TrafficRoadSceneTests.TerrainRoot() }), new CarKinem.Road.RoadNetworkHolder());
            residency.AttachContent(new SumoNetTerrainLoader());
            residency.Commit(h.World, residency.Prepare("demo-city"));
            TrafficRuntimeTests.RegisterReplicaComponents(h.World);
            h.World.RegisterComponent<Fdp.Toolkit.Combat.Components.Health>();
            h.World.RegisterComponent<TrafficDemand>();
            h.World.Bus.Register<WeaponFireNotification>();
            h.World.Bus.Register<DetonationNotification>();
            h.World.Bus.RegisterManaged<EntityCreationRequest>();
            h.World.Bus.Register<DamageAssessedEvent>();
            h.Runtime = new TrafficRuntime(new TrafficServeOptions(() => Interlocked.Increment(ref h._next), Participant: null, Source: 1));
            h.Mirror = new TrafficMirrorSystem(h.Runtime, participant: null, entityMap: h.Map, localNodeId: 1);
            var mirror = h.Mirror;
            h.Panic = new TrafficPanicSystem(h.Runtime, () => mirror.Replicas, h.Map);
            var traffic = h.World.CreateEntity();
            var demand = TrafficDemand.Default;
            demand.TargetCars = 20;
            demand.TargetPedestrians = walkers;
            demand.MaxConverted = maxConverted;
            h.World.AddComponent(traffic, demand);
            return h;
        }

        public void Body(float x, float y)
        {
            var e = World.CreateEntity();
            World.AddComponent(e, new SimTransform { Position = new Vector3(x, y, 0f), Rotation = Quaternion.Identity });
            World.AddComponent(e, new PhysicsCollider { Radius = 0.4f, Height = 1.8f, CollisionLayer = 1 });
        }

        public void Shoot(Vector3 at) => World.Bus.Publish(new WeaponFireNotification { Origin = at, Quantity = 1 });

        // ⭐ CE-3248 — a man throwing from his HANDS mount (his type's last): the one test every fire reader asks is PersonHands.Threw
        public void Throw(Vector3 at)
        {
            const long ThrowerType = 9302;
            if (!World.IsComponentTypeRegistered<TkbIdentity>()) World.RegisterComponent<TkbIdentity>();
            var thrower = new Fdp.Interfaces.TkbTemplate("Thrower", ThrowerType);
            thrower.AddDescriptor(new Fdp.Toolkit.Tkb.Domain.WeaponSuiteDto { Mounts = { Fdp.Toolkit.Combat.PersonHands.Mount() } });
            if (World.HasSingletonManaged<Fdp.Interfaces.ITkbDatabase>() && World.GetSingletonManaged<Fdp.Interfaces.ITkbDatabase>() is Fdp.Toolkit.Tkb.TkbDatabase db)
                db.Register(thrower);
            else
            {
                var fresh = new Fdp.Toolkit.Tkb.TkbDatabase();
                fresh.Register(thrower);
                World.SetSingletonManaged<Fdp.Interfaces.ITkbDatabase>(fresh);
            }
            var man = World.CreateEntity();
            World.AddComponent(man, new SimTransform { Position = at, Rotation = Quaternion.Identity });
            World.AddComponent(man, new TkbIdentity { TkbType = ThrowerType });
            World.Bus.Publish(new WeaponFireNotification { Shooter = man, WeaponIndex = 0, Origin = at, Quantity = 1 });
        }

        public void Blast(Vector3 at) => World.Bus.Publish(new DetonationNotification { HitX = at.X, HitY = at.Y, HitZ = at.Z });

        public void Run(double seconds) => RunCollecting(seconds);

        // One frame: the mirror and the panic system on the main thread, the stepping thread inline, as TrafficRuntimeTests.Frame.
        public List<EntityCreationRequest> RunCollecting(double seconds)
        {
            var got = new List<EntityCreationRequest>();
            for (double end = _t + seconds; _t < end;)
            {
                World.SetSingleton(new GlobalTime { TotalTime = _t += 0.25, DeltaTime = 0.25f, TimeScale = 1f });
                World.Bus.SwapBuffers();
                got.AddRange(World.Bus.ReadManaged<EntityCreationRequest>());
                Mirror.Execute(World, 0.25f);
                Panic.Execute(World, 0.25f);
                Runtime.SteppingTick();
            }
            World.Bus.SwapBuffers();
            got.AddRange(World.Bus.ReadManaged<EntityCreationRequest>());
            Mirror.Execute(World, 0f);
            Panic.Execute(World, 0f);
            World.Bus.SwapBuffers();
            got.AddRange(World.Bus.ReadManaged<EntityCreationRequest>());
            return got;
        }

        public List<DamageAssessedEvent> RunCollectingHits(double seconds)
        {
            var got = new List<DamageAssessedEvent>();
            for (double end = _t + seconds; _t < end;)
            {
                World.SetSingleton(new GlobalTime { TotalTime = _t += 0.25, DeltaTime = 0.25f, TimeScale = 1f });
                World.Bus.SwapBuffers();
                foreach (var hit in World.Bus.Read<DamageAssessedEvent>()) got.Add(hit);
                Mirror.Execute(World, 0.25f);
                Panic.Execute(World, 0.25f);
                Runtime.SteppingTick();
            }
            World.Bus.SwapBuffers();
            foreach (var hit in World.Bus.Read<DamageAssessedEvent>()) got.Add(hit);
            return got;
        }

        public void Dispose()
        {
            Runtime.Dispose();
            World.Dispose();
        }
    }
}
