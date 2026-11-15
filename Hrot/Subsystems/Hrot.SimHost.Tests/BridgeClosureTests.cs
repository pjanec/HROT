using System.Numerics;
using System.Text.Json.Nodes;
using CarKinem.Core;
using CarKinem.Road;
using CarKinem.Trajectory;
using Fdp.Core;
using Fdp.Toolkit.Navigation;
using Fdp.Toolkit.Terrain;
using Hrot.Map.Common.Services;
using Hrot.Traffic;
using Xunit;

namespace Hrot.SimHost.Tests;

/// <summary>
/// ⭐ T-7b (CE-4423, docs/DESIGN_Terrain_Authoring.md §18k-3) — a FALLEN BRIDGE stops being road. The terrain: a west road and an east road cut by a river, a long DETOUR road round it, and a scenario overlay with a Bridge between
/// the banks (T-7a: snapped to the bank nodes, a road segment appended after the base's). The structure is collapsed the way the damage system does it (a <c>BuildingDamage</c> entity with <c>Collapsed = 1</c>) and the
/// <see cref="BridgeClosureSystem"/> is run on the world the residency committed.
/// </summary>
public sealed class BridgeClosureTests : IDisposable
{
    private const string Key = "c2000000-0000-4000-8000-000000000002", SumoThere = "bridge-" + Key, SumoBack = "bridge-" + Key + "-r";
    private readonly string _root = Path.Combine(Path.GetTempPath(), "bcl-" + Guid.NewGuid().ToString("N")[..10]);

    /// <summary>Stands in for netconvert: the overlay's bridge row names SUMO edges without needing SUMO installed.</summary>
    private sealed class FakeBridgeNet : IBridgeNet
    {
        public IReadOnlyList<IReadOnlyList<string>>? Build(TerrainDefinition definition, string terrainFolder, IReadOnlyList<BridgeNetRequest> bridges, string outNetPath, out string? warning)
        {
            warning = null;
            File.WriteAllText(outNetPath, "<net/>");
            return bridges.Select(b => (IReadOnlyList<string>)new[] { SumoThere, SumoBack }).ToList();
        }
    }

    private sealed class World : IDisposable
    {
        public EntityRepository Repo = new();
        public RoadNetworkHolder Holder = new();
        public TrajectoryPoolManager Pool = new();
        public TerrainWorld Terrain = null!;
        public int Bridge;
        public void Dispose() { Holder.Dispose(); }
    }

    private World Build()
    {
        string terrains = Path.Combine(_root, "assets", "terrain"), folder = Path.Combine(terrains, "t"), scenario = Path.Combine(_root, "scn");
        Directory.CreateDirectory(folder); Directory.CreateDirectory(scenario);
        File.WriteAllText(Path.Combine(folder, "terrain.json"), """{"schemaVersion":2,"world":"w.geojson","roadNetworks":["roads.json"],"sumoNet":{"file":"t.net.xml","anchor":[0,0]}}""");
        File.WriteAllText(Path.Combine(folder, "t.net.xml"), "<net/>");
        // the river: water x 200..230 across the whole terrain
        File.WriteAllText(Path.Combine(folder, "w.geojson"), """{"type":"FeatureCollection","hrot":{"schemaVersion":1,"bounds":[0,0,400,400],"groundZ":0},"features":[{"type":"Feature","properties":{"kind":"surface","surface":"water"},"geometry":{"type":"Polygon","coordinates":[[[200,0],[230,0],[230,400],[200,400],[200,0]]]}}]}""");
        // west road 0-1, east road 2-3, and a DETOUR 1-4-2 round the south (so a route exists with the bridge gone, a longer one)
        File.WriteAllText(Path.Combine(folder, "roads.json"), """
            {"$meta":{"docType":"Fdp.RoadNetwork","schemaVersion":1},
             "nodes":[{"id":0,"position":{"x":150,"y":200}},{"id":1,"position":{"x":190,"y":200}},{"id":2,"position":{"x":240,"y":200}},{"id":3,"position":{"x":300,"y":200}},{"id":4,"position":{"x":215,"y":60}}],
             "segments":[
              {"id":0,"startNodeId":0,"endNodeId":1,"controlPoints":{"p0":{"x":150,"y":200},"t0":{"x":40,"y":0},"p1":{"x":190,"y":200},"t1":{"x":40,"y":0}},"speedLimit":14,"laneWidth":3.5,"laneCount":1},
              {"id":1,"startNodeId":2,"endNodeId":3,"controlPoints":{"p0":{"x":240,"y":200},"t0":{"x":60,"y":0},"p1":{"x":300,"y":200},"t1":{"x":60,"y":0}},"speedLimit":14,"laneWidth":3.5,"laneCount":1},
              {"id":2,"startNodeId":1,"endNodeId":4,"controlPoints":{"p0":{"x":190,"y":200},"t0":{"x":25,"y":-140},"p1":{"x":215,"y":60},"t1":{"x":25,"y":-140}},"speedLimit":14,"laneWidth":3.5,"laneCount":1},
              {"id":3,"startNodeId":4,"endNodeId":2,"controlPoints":{"p0":{"x":215,"y":60},"t0":{"x":25,"y":140},"p1":{"x":240,"y":200},"t1":{"x":25,"y":140}},"speedLimit":14,"laneWidth":3.5,"laneCount":1}]}
            """);
        var dom = new JsonObject
        {
            ["header"] = new JsonObject { ["terrainName"] = "t" },
            ["entities"] = new JsonObject { [Key] = new JsonObject
            {
                ["TerrainFeature"] = new JsonObject { ["Kind"] = TerrainFeature.Bridge, ["Template"] = "", ["HeadingDeg"] = 0, ["Pad"] = 4, ["Seed"] = 0 },
                ["SimTransform"] = new JsonObject { ["Position"] = new JsonArray(192, 200, 0), ["Rotation"] = new JsonArray(0, 0, 0, 1) },
                ["EditablePolyline"] = new JsonObject { ["Points"] = new JsonArray(new JsonObject { ["X"] = 0, ["Y"] = 0 }, new JsonObject { ["X"] = 46, ["Y"] = 0 }), ["Version"] = 0 },
            } },
        };
        var catalog = new TerrainCatalog(new[] { terrains });
        Assert.True(OverlayCompiler.Compile(dom, scenario, catalog.ResolveDefinition, bakeNavtiles: false, bridgeNet: new FakeBridgeNet()).Wrote);

        var w = new World();
        var residency = new TerrainResidency(catalog, w.Holder);
        residency.Commit(w.Repo, residency.Prepare("t", Path.Combine(scenario, OverlayCompiler.FolderName)));
        w.Terrain = w.Repo.GetSingletonManaged<TerrainWorld>()!;
        w.Bridge = w.Terrain.Buildings.ToList().FindIndex(b => b.Label == Key);
        Assert.True(w.Bridge >= 0, "the bridge is a structure building of the merged world");
        w.Repo.RegisterComponent<BuildingDamage>();
        w.Repo.RegisterManagedComponent<TerrainObjectKey>();
        w.Repo.RegisterComponent<NavigationIntent>(); w.Repo.RegisterComponent<NavigationStatus>(); w.Repo.RegisterComponent<NavState>(); w.Repo.RegisterComponent<SimTransform>();
        return w;
    }

    private static void Collapse(World w)
    {
        var d = new BuildingDamage { Building = w.Bridge, Collapsed = 1 };
        var e = w.Repo.CreateEntity();
        w.Repo.AddComponent(e, d);
        w.Repo.SetManagedComponent(e, new TerrainObjectKey { Key = BuildingDamages.KeyOf(w.Terrain, w.Bridge) });
    }

    private static float RoadMetres(RoadNetworkHolder holder, Vector2 from, Vector2 to)
    {
        using var lease = holder.Borrow();
        var a = RoadGraphRouter.NearestAccess(lease.Value, from, 30f); var b = RoadGraphRouter.NearestAccess(lease.Value, to, 30f);
        return a.IsValid && b.IsValid && new RoadGraphRouter().Route(lease.Value, a, b, out float m) ? m : float.MaxValue;
    }

    private static Entity Vehicle(World w, int handle, Vector3[] path)
    {
        w.Pool.RegisterTrajectoryWithKey(path, handle);
        var e = w.Repo.CreateEntity();
        w.Repo.AddComponent(e, new NavigationIntent { Mode = NavigationMode.PathToPoint, IntentId = 1, FinalDestination = path[^1] });
        w.Repo.AddComponent(e, new NavigationStatus { IntentId = 1, Result = NavigationResult.InProgress });
        w.Repo.AddComponent(e, new NavState { Mode = KinematicsMode.CustomTrajectory, TrajectoryId = handle });
        w.Repo.AddComponent(e, new SimTransform { Position = path[0] });
        return e;
    }

    /// <summary>
    /// (a) A route between the banks takes the bridge (≈ 50 m of road); after the collapse the road network is republished WITHOUT the bridge's segment, so a new route takes the detour (> 150 m); and a vehicle whose baked
    /// trajectory ran over the bridge is RE-PLANNED while one on the west road is not. The base's road segments keep their indices (the bridge was the last). 🔴 Red-proof: skip the republish ⇒ the route still takes the bridge.
    /// 🔴 Red-proof: never re-plan ⇒ the vehicle on the bridge keeps its route.
    /// </summary>
    [Fact]
    public void AFallenBridge_IsRemovedFromTheRoadNet_AndTheVehiclesOnItAreReplanned()
    {
        using var w = Build();
        var west = new Vector2(160, 200); var east = new Vector2(290, 200);
        Assert.InRange(RoadMetres(w.Holder, west, east), 100f, 160f);                       // over the bridge: 160 → 190 → 240 → 290 ≈ 130 m
        int segments = w.Holder.Current.Segments.Length;
        Assert.Equal(5, segments);                                                          // 4 base segments + the bridge

        var onBridge = Vehicle(w, 7001, new[] { new Vector3(160, 200, 0), new Vector3(200, 200, 0), new Vector3(215, 200, 0), new Vector3(230, 200, 0), new Vector3(290, 200, 0) });
        var onWestRoad = Vehicle(w, 7002, new[] { new Vector3(152, 200, 0), new Vector3(170, 200, 0), new Vector3(185, 200, 0) });
        var system = new BridgeClosureSystem(runtime: null, () => w.Pool);

        system.Execute(w.Repo, 0.1f);
        Assert.Equal(0, system.Fallen); Assert.Equal(0, system.Replans);                    // nothing fell yet

        Collapse(w);
        system.Execute(w.Repo, 0.1f);

        Assert.Equal(1, system.Fallen);
        Assert.Equal(4, w.Holder.Current.Segments.Length);                                  // the bridge's segment is gone
        Assert.Equal(4, w.Repo.GetSingleton<ZoneEnvironmentData>().RoadNetwork.Segments.Length);
        Assert.True(RoadMetres(w.Holder, west, east) > 150f, "a new route avoids the bridge: the detour");
        Assert.Equal(1, system.Replans);
        Assert.Equal(1, w.Repo.GetComponentRO<NavigationStatus>(onBridge).ReplanCount);
        Assert.Equal(0, w.Repo.GetComponentRO<NavigationStatus>(onWestRoad).ReplanCount);
    }

    /// <summary>(a′) The base road segments keep their indices through the removal: segment 2 (the detour's first) is the same segment before and after.</summary>
    [Fact]
    public void TheBaseSegments_KeepTheirIndices_WhenTheBridgeIsRemoved()
    {
        using var w = Build();
        var before = new List<(Vector2, Vector2)>();
        for (int i = 0; i < 4; i++) before.Add((w.Holder.Current.Segments[i].P0, w.Holder.Current.Segments[i].P1));
        var system = new BridgeClosureSystem(null, () => w.Pool);
        Collapse(w);
        system.Execute(w.Repo, 0.1f);
        for (int i = 0; i < 4; i++) { Assert.Equal(before[i].Item1, w.Holder.Current.Segments[i].P0); Assert.Equal(before[i].Item2, w.Holder.Current.Segments[i].P1); }
    }

    /// <summary>(b′) Where the node runs SUMO the bridge's two SUMO edges (the overlay's <c>bridges[]</c>) go into the runtime's closed set — the stepping thread tells the session. 🔴 Red-proof: leave it out ⇒ the set stays empty.</summary>
    [Fact]
    public void WhereSumoRuns_TheBridgesSumoEdges_AreClosed()
    {
        using var w = Build();
        using var runtime = new TrafficRuntime(new TrafficServeOptions(() => 1, Participant: null, Source: 1));
        var system = new BridgeClosureSystem(runtime, () => w.Pool);
        system.Execute(w.Repo, 0.1f);
        Assert.Empty(runtime.ClosedEdges);
        Collapse(w);
        system.Execute(w.Repo, 0.1f);
        Assert.Equal(new[] { SumoThere, SumoBack }, runtime.ClosedEdges.OrderBy(e => e.Length));
    }

    /// <summary>(d) A STEADY frame — nothing collapsed, and again after the collapse was handled — allocates 0 B (the CE-2394 pattern).</summary>
    [Fact]
    public void ASteadyFrame_AllocatesNothing()
    {
        using var w = Build();
        var system = new BridgeClosureSystem(null, () => w.Pool);
        foreach (var phase in new[] { "before", "after" })
        {
            if (phase == "after") { Collapse(w); system.Execute(w.Repo, 0.1f); }
            for (int i = 0; i < 5; i++) system.Execute(w.Repo, 0.1f);                        // warm
            long before = GC.GetAllocatedBytesForCurrentThread();
            for (int i = 0; i < 200; i++) system.Execute(w.Repo, 0.1f);
            Assert.Equal(0L, GC.GetAllocatedBytesForCurrentThread() - before);
        }
    }

    // ── CE-4590 (P-2; docs/DESIGN_Ground_Patches_And_Road_Blockage.md §4 G-10, §9.2 V-19, V-20, V-24) — THE road-blockage step ──────────────────

    private static bool Registered(EntityRepository repo)
    {
        if (!repo.IsComponentTypeRegistered<StaticObstacle>()) repo.RegisterComponent<StaticObstacle>();
        if (!repo.IsComponentTypeRegistered<ObstacleShape>()) repo.RegisterComponent<ObstacleShape>();
        return true;
    }

    /// <summary>A static obstacle (a concrete barrier, a wreck) at <paramref name="at"/>, <paramref name="length"/> × <paramref name="width"/> m, its length along <paramref name="yaw"/>.</summary>
    private static Entity Obstacle(World w, Vector2 at, float length, float width, float yaw)
    {
        Registered(w.Repo);
        var e = w.Repo.CreateEntity();
        w.Repo.AddComponent(e, new SimTransform { Position = new Vector3(at, 0f), Rotation = Quaternion.CreateFromAxisAngle(Vector3.UnitZ, yaw) });
        w.Repo.AddComponent(e, new StaticObstacle { Material = new FixedString32("concrete") });
        w.Repo.AddComponent(e, new ObstacleShape { Length = length, Width = width, Height = 1.2f });
        return e;
    }

    /// <summary>
    /// ⭐⭐ <c>CE-4590</c> (G-10, V-19; acceptance §6 item 2) — a BARRIER across the east road (6 m across a 3.5 m road) cuts its segment from the vehicles' net: a
    /// route from the east bank's start to its end no longer exists, and a vehicle whose way crossed it re-plans; REMOVING the barrier (the charge, CE-6003)
    /// RE-OPENS the road — the net is rebuilt from the base, all five segments back. 🔴 Red-proof: publish only when segments are ADDED to the cut (the old
    /// monotonic step) ⇒ the road never re-opens.
    /// </summary>
    [Fact]
    public void CE4590_ABarrierAcrossARoad_CutsIt_AndRemovingItReOpensIt()
    {
        using var w = Build();
        var system = new BridgeClosureSystem(runtime: null, () => w.Pool);
        var from = new Vector2(245, 200); var to = new Vector2(295, 200);
        system.Execute(w.Repo, 0.1f);
        Assert.InRange(RoadMetres(w.Holder, from, to), 40f, 60f);
        var crossing = Vehicle(w, 7003, new[] { new Vector3(245, 200, 0), new Vector3(270, 200, 0), new Vector3(295, 200, 0) });

        var barrier = Obstacle(w, new Vector2(270, 200), 6f, 1f, MathF.PI / 2f);   // its length across the road
        system.Execute(w.Repo, 0.1f);
        Assert.Equal(1, system.Blocked);
        Assert.Equal(4, w.Holder.Current.Segments.Length);
        Assert.True(RoadMetres(w.Holder, from, to) > 1000f, "the road is cut: no route along it");
        Assert.Equal(1, w.Repo.GetComponentRO<NavigationStatus>(crossing).ReplanCount);

        w.Repo.DestroyEntity(barrier);
        system.Execute(w.Repo, 0.1f);
        Assert.Equal(0, system.Blocked);
        Assert.Equal(5, w.Holder.Current.Segments.Length);
        Assert.InRange(RoadMetres(w.Holder, from, to), 40f, 60f);
    }

    /// <summary>
    /// ⭐⭐ <c>CE-4591</c> (P-4, G-10) — a bomb's CRATER across the east road (its 3.6 m bowl over a 3.5 m road) cuts the road like a barrier: the segment is dropped
    /// from the vehicles' net the tick its record appears; a small one beside the road (a 1 m bowl at the kerb) leaves it open; the record gone (a seek back) re-opens
    /// it. 🔴 Red-proof: drop <c>RoadBlockage.AddCraters</c> in <c>Recompute</c> ⇒ the road stays open.
    /// </summary>
    [Fact]
    public void CE4591_ACraterAcrossARoad_CutsIt_ASmallOneBesideItDoesNot_AndASeekBackReOpensIt()
    {
        using var w = Build();
        w.Repo.RegisterComponent<GroundDamage>();
        var system = new BridgeClosureSystem(runtime: null, () => w.Pool);
        var from = new Vector2(245, 200); var to = new Vector2(295, 200);
        system.Execute(w.Repo, 0.1f);

        Entity Crater(Crater c)
        {
            var d = new GroundDamage(); (d.CellX, d.CellY) = GroundDamages.CellOf(c.Centre);
            GroundDamages.Add(ref d, in c, out _);
            var e = w.Repo.CreateEntity(); w.Repo.AddComponent(e, d); return e;
        }
        var small = Crater(new Crater(new Vector2(260, 202.5f), 1f, 0.4f, 0.1f, (SimInstant)1.0, CraterKind.Small));
        system.Execute(w.Repo, 0.1f);
        Assert.Equal(0, system.Blocked);

        var big = Crater(new Crater(new Vector2(270, 200), 3.6f, 1.6f, 0.3f, (SimInstant)2.0, CraterKind.Large));
        system.Execute(w.Repo, 0.1f);
        Assert.Equal(1, system.Blocked);
        Assert.True(RoadMetres(w.Holder, from, to) > 1000f, "the crater cuts the road");

        w.Repo.DestroyEntity(big);
        system.Execute(w.Repo, 0.1f);
        Assert.Equal(0, system.Blocked);
        Assert.InRange(RoadMetres(w.Holder, from, to), 40f, 60f);
    }

    /// <summary>
    /// ⭐ <c>CE-4590</c> (G-10) — a car parked on HALF the road (4.5 × 1.8 along it, 1 m off the centre) leaves the road open for our net (only SUMO's lane obstacle
    /// sees it, through the body feed); a barrier across it closes it. 🔴 Red-proof: cut on any covered point ⇒ the parked car cuts the road.
    /// </summary>
    [Fact]
    public void CE4590_APartlyCoveredRoad_StaysOpen()
    {
        using var w = Build();
        var system = new BridgeClosureSystem(runtime: null, () => w.Pool);
        Obstacle(w, new Vector2(270, 201f), 4.5f, 1.8f, 0f);
        system.Execute(w.Repo, 0.1f);
        Assert.Equal(0, system.Blocked);
        Assert.Equal(5, w.Holder.Current.Segments.Length);
    }

    /// <summary>
    /// ⭐⭐ <c>CE-4590</c> (G-10, V-18) — SUMO's side, in SUMO's OWN lanes (no edge-id mapping): a two-lane edge is CLOSED when a barrier spans both its lanes at one
    /// station, left open when it covers one lane (a lane obstacle). And the body feed tells each obstacle as a CHAIN of squares at rest with NEGATIVE ids (V-24):
    /// a 6 × 1 barrier is six 1 m squares along its length. 🔴 Red-proof: require all lanes covered at ANY (not the same) station ⇒ two staggered one-lane cars close it.
    /// </summary>
    [Fact]
    public void CE4590_SumoEdges_CloseOnlyWhenEveryLaneIsCovered_AndObstaclesAreFedAsBodiesAtRest()
    {
        var net = Sim.Ingest.NetworkParser.ParseXml("""
            <net version="1.20"><location netOffset="0.00,0.00" convBoundary="0,0,200,10" origBoundary="0,0,200,10" projParameter="!"/>
            <edge id="e" from="a" to="b"><lane id="e_0" index="0" speed="13.9" length="200" width="3.2" shape="0,-1.6 200,-1.6"/><lane id="e_1" index="1" speed="13.9" length="200" width="3.2" shape="0,1.6 200,1.6"/></edge>
            <junction id="a" type="dead_end" x="0" y="0" incLanes="" intLanes="" shape="0,-3.2 0,3.2"/><junction id="b" type="dead_end" x="200" y="0" incLanes="e_0 e_1" intLanes="" shape="200,-3.2 200,3.2"/>
            </net>
            """);
        (double, double) Local(double x, double y) => (x, y);
        var closed = new List<string>();
        var across = TerrainObstacles.PrismOf(new Vector3(100, 0, 0), MathF.PI / 2f, new ObstacleShape { Length = 8, Width = 1, Height = 1 }, TerrainMaterialLibrary.Shared.Get("concrete", "t")).Footprint;
        RoadBlockage.ClosedEdges(net, Local, new[] { across }, closed);
        Assert.Equal(new[] { "e" }, closed);
        var oneLane = TerrainObstacles.PrismOf(new Vector3(60, -1.6f, 0), 0f, new ObstacleShape { Length = 4.5f, Width = 3.4f, Height = 1 }, TerrainMaterialLibrary.Shared.Get("concrete", "t")).Footprint;
        var otherLane = TerrainObstacles.PrismOf(new Vector3(140, 1.6f, 0), 0f, new ObstacleShape { Length = 4.5f, Width = 3.4f, Height = 1 }, TerrainMaterialLibrary.Shared.Get("concrete", "t")).Footprint;
        RoadBlockage.ClosedEdges(net, Local, new[] { oneLane, otherLane }, closed);
        Assert.Empty(closed);

        using var repo = new EntityRepository();
        repo.RegisterComponent<SimTransform>(); repo.RegisterComponent<StaticObstacle>(); repo.RegisterComponent<ObstacleShape>();
        var e = repo.CreateEntity();
        repo.AddComponent(e, new SimTransform { Position = new Vector3(100, 0, 0), Rotation = Quaternion.CreateFromAxisAngle(Vector3.UnitZ, MathF.PI / 2f) });
        repo.AddComponent(e, new StaticObstacle { Material = new FixedString32("concrete") });
        repo.AddComponent(e, new ObstacleShape { Length = 6, Width = 1, Height = 1 });
        var placement = SumoNetPlacement.For(net, new TerrainSumoNet("n.net.xml", 0, 0), null);
        var bodies = TrafficBodyFeedSystem.Gather(repo, placement);
        Assert.Equal(6, bodies.Length);
        Assert.All(bodies, b => { Assert.True(b.NetworkId < 0); Assert.Equal(0, b.Vx); Assert.Equal(TrafficBodyKind.Vehicle, b.Kind); Assert.Equal(1f, b.Width, 3); });
        Assert.InRange(bodies.Min(b => b.Y), -2.6, -2.4); Assert.InRange(bodies.Max(b => b.Y), 2.4, 2.6);   // along its length, ACROSS the road
    }

    public void Dispose() { try { Directory.Delete(_root, true); } catch { } }
}
