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

    private World Build() => Build(authoredBridge: true);

    /// <param name="authoredBridge">false ⇒ the river and the roads only (no authored Bridge feature, no overlay): the ground a LAID bridge is placed on (CE-4692).</param>
    private World Build(bool authoredBridge, JsonObject? extraEntities = null, bool baseWall = false, bool baseBridge = false)
    {
        string terrains = Path.Combine(_root, "assets", "terrain"), folder = Path.Combine(terrains, "t"), scenario = Path.Combine(_root, "scn");
        Directory.CreateDirectory(folder); Directory.CreateDirectory(scenario);
        File.WriteAllText(Path.Combine(folder, "terrain.json"), """{"schemaVersion":2,"world":"w.geojson","roadNetworks":["roads.json"],"sumoNet":{"file":"t.net.xml","anchor":[0,0]}}""");
        File.WriteAllText(Path.Combine(folder, "t.net.xml"), "<net/>");
        // the river: water x 200..230 across the whole terrain
        File.WriteAllText(Path.Combine(folder, "w.geojson"), """{"type":"FeatureCollection","hrot":{"schemaVersion":1,"bounds":[0,0,400,400],"groundZ":0},"features":[{"type":"Feature","properties":{"kind":"surface","surface":"water"},"geometry":{"type":"Polygon","coordinates":[[[200,0],[230,0],[230,400],[200,400],[200,0]]]}}]}""");
        if (baseWall)   // ⭐ CE-11070 — a BASE (imported) wall across the west road: it must never close it (V-7)
            File.WriteAllText(Path.Combine(folder, "w.geojson"), """{"type":"FeatureCollection","hrot":{"schemaVersion":1,"bounds":[0,0,400,400],"groundZ":0},"features":[{"type":"Feature","properties":{"kind":"surface","surface":"water"},"geometry":{"type":"Polygon","coordinates":[[[200,0],[230,0],[230,400],[200,400],[200,0]]]}},{"type":"Feature","properties":{"kind":"wall","material":"brick","height":2,"label":"Garden wall"},"geometry":{"type":"LineString","coordinates":[[170,190],[170,210]]}}]}""");
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
        if (baseBridge)
        {
            // ⭐ CE-11075 — an IMPORTED terrain's bridge: a base deck structure "osm-bridge/e" over the river, the base road graph's crossing segment 4 on it, and the
            //   importer's bridge row in terrain.json (the overlay header's shape) naming segment 4 and the two SUMO edges
            File.WriteAllText(Path.Combine(folder, "terrain.json"), """{"schemaVersion":2,"world":"w.geojson","roadNetworks":["roads.json"],"sumoNet":{"file":"t.net.xml","anchor":[0,0]},"bridges":[{"key":"osm-bridge/e","snapped":true,"segments":[4],"sumoEdges":["-e","e"]}]}""");
            File.WriteAllText(Path.Combine(folder, "w.geojson"), """{"type":"FeatureCollection","hrot":{"schemaVersion":1,"bounds":[0,0,400,400],"groundZ":0},"features":[{"type":"Feature","properties":{"kind":"surface","surface":"water"},"geometry":{"type":"Polygon","coordinates":[[[200,0],[230,0],[230,400],[200,400],[200,0]]]}},{"type":"Feature","properties":{"kind":"slab","zRef":"ground","structure":"osm-bridge/e","label":"osm-bridge/e/0"},"geometry":{"type":"Polygon","coordinates":[[[190,196,1],[240,196,1],[240,204,1],[190,204,1],[190,196,1]]]}}]}""");
            var roadsDoc = JsonNode.Parse(File.ReadAllText(Path.Combine(folder, "roads.json")))!.AsObject();
            roadsDoc["segments"]!.AsArray().Add(JsonNode.Parse("""{"id":4,"startNodeId":1,"endNodeId":2,"controlPoints":{"p0":{"x":190,"y":200},"t0":{"x":50,"y":0},"p1":{"x":240,"y":200},"t1":{"x":50,"y":0}},"speedLimit":14,"laneWidth":3.5,"laneCount":1}"""));
            File.WriteAllText(Path.Combine(folder, "roads.json"), roadsDoc.ToJsonString());
        }
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
        if (extraEntities != null) foreach (var (k, v) in extraEntities.ToList()) { extraEntities.Remove(k); ((JsonObject)dom["entities"]!)[k] = v; }
        var catalog = new TerrainCatalog(new[] { terrains });
        if (!authoredBridge)
        {
            var plain = new World();
            var r = new TerrainResidency(catalog, plain.Holder);
            r.Commit(plain.Repo, r.Prepare("t"));
            plain.Terrain = plain.Repo.GetSingletonManaged<TerrainWorld>()!;
            plain.Bridge = -1;
            plain.Repo.RegisterComponent<NavigationIntent>(); plain.Repo.RegisterComponent<NavigationStatus>(); plain.Repo.RegisterComponent<NavState>(); plain.Repo.RegisterComponent<SimTransform>();
            if (baseBridge)
            {
                plain.Bridge = plain.Terrain.Buildings.ToList().FindIndex(b => b.Label == "osm-bridge/e");
                plain.Repo.RegisterComponent<BuildingDamage>();
                plain.Repo.RegisterManagedComponent<TerrainObjectKey>();
            }
            return plain;
        }
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

    /// <summary>
    /// ⭐⭐ <c>CE-11075</c> (DESIGN_Terrain_OSM_Bridges.md §8) — an IMPORTED terrain's OSM bridge (a base deck structure, no overlay) is closed when it falls, like an authored one: the
    /// residency publishes terrain.json's bridge rows in <c>ResolvedBridges</c>, and the collapse of the structure drops its segment from the vehicles' net (the route takes the
    /// detour) and names its SUMO edges for closure. 🔴 Red-proof: the residency ignores <c>definition.Bridges</c> ⇒ no row, the road stays.
    /// </summary>
    [Fact]
    public void CE11075_AFallenImportedBridge_IsDroppedFromTheRoadNet_AndNamesItsSumoEdges()
    {
        using var w = Build(authoredBridge: false, baseBridge: true);
        Assert.True(w.Bridge >= 0, "the base deck is a structure building");
        var row = Assert.Single(w.Repo.GetSingletonManaged<TerrainDefinition>()!.ResolvedBridges.Items);
        Assert.Equal("osm-bridge/e", row.Key);
        Assert.Equal(new[] { "-e", "e" }, row.SumoEdges);
        var system = new BridgeClosureSystem(runtime: null, () => w.Pool);
        var west = new Vector2(160, 200); var east = new Vector2(290, 200);
        system.Execute(w.Repo, 0.1f);
        Assert.True(RoadMetres(w.Holder, west, east) < 150f, "standing, the bridge carries the road");
        Collapse(w);
        system.Execute(w.Repo, 0.1f);
        Assert.Equal(1, system.Fallen);
        Assert.Equal(1, system.Blocked);
        Assert.True(RoadMetres(w.Holder, west, east) > 250f, "fallen, the route takes the detour");
    }

    // ── ⭐⭐ CE-11070 (docs/designs/engineering/DESIGN_Breachable_Obstacle_Walls.md §8 V-1, V-2, V-7) — scenario feature WALLS close lanes ─────────────────

    private const string WallKey = "c9000000-0000-4000-8000-000000000009";

    /// <summary>A scenario WALL feature (sandbags, 1 m) from <paramref name="a"/> to <paramref name="b"/> (world points).</summary>
    private static JsonObject WallFeature(Vector2 a, Vector2 b) => new()
    {
        [WallKey] = new JsonObject
        {
            ["TerrainFeature"] = new JsonObject { ["Kind"] = TerrainFeature.Wall, ["Template"] = "sandbags", ["HeadingDeg"] = 0, ["Pad"] = 1.0, ["Seed"] = 0 },
            ["SimTransform"] = new JsonObject { ["Position"] = new JsonArray(a.X, a.Y, 0), ["Rotation"] = new JsonArray(0, 0, 0, 1) },
            ["EditablePolyline"] = new JsonObject { ["Points"] = new JsonArray(new JsonObject { ["X"] = 0, ["Y"] = 0 }, new JsonObject { ["X"] = b.X - a.X, ["Y"] = b.Y - a.Y }), ["Version"] = 0 },
        },
    };

    /// <summary>A breach record on the scenario wall: <paramref name="from"/>..<paramref name="to"/> m along its first panel, base to top, of <paramref name="kind"/>.</summary>
    private static Entity BreachWall(World w, float from, float to, BreachKind kind)
    {
        int panel = w.Terrain.Panels.ToList().FindIndex(p => p.Label == WallKey + "/0");
        Assert.True(panel >= 0, "the scenario wall is a free-wall panel of the merged world");
        var p = w.Terrain.Panels[panel];
        int group = WallGroups.Of(w.Terrain, panel);
        var d = new BuildingDamage { Building = group };
        BuildingDamages.Add(ref d, new WallBreach(panel, from, to, p.BaseZ, p.TopZ, kind), null);
        var e = w.Repo.CreateEntity();
        w.Repo.AddComponent(e, d);
        w.Repo.SetManagedComponent(e, new TerrainObjectKey { Key = BuildingDamages.KeyOf(w.Terrain, group) });
        return e;
    }

    /// <summary>
    /// ⭐⭐ <c>CE-11070</c> — a scenario sandbag WALL across the east road (a 0.6 m line, 20 m long, square to the road) closes it — on two nodes built apart, the same (the panels and the breach
    /// record, no navmesh); a 0.8 m infantry Passage at the crossing keeps it closed; a HOLE (a shell's, not to the ground) does not even recompute; a 4.7 m cut (the dozer's) across the
    /// road re-opens it; and a steady frame after it allocates nothing. 🔴 Red-proof: drop <c>WallClosedSegments</c> in <c>Recompute</c> ⇒ the road stays open with the wall standing.
    /// 🔴 Red-proof: count every Passage as a drive-through gap (<c>minGapM</c> 0) ⇒ the 0.8 m hole re-opens it.
    /// </summary>
    [Fact]
    public void CE11070_AScenarioWallAcrossARoad_ClosesIt_AHoleKeepsIt_ADriveThroughCutReOpensIt_OnEveryNode()
    {
        using var w = Build(true, WallFeature(new Vector2(270, 190), new Vector2(270, 210)));
        using var other = Build(true, WallFeature(new Vector2(270, 190), new Vector2(270, 210)));
        var system = new BridgeClosureSystem(runtime: null, () => w.Pool);
        var second = new BridgeClosureSystem(runtime: null, () => other.Pool);
        var from = new Vector2(245, 200); var to = new Vector2(295, 200);
        system.Execute(w.Repo, 0.1f); second.Execute(other.Repo, 0.1f);
        Assert.Equal(1, system.Walls);
        Assert.Equal(1, system.Blocked);
        Assert.Equal(system.Blocked, second.Blocked);
        Assert.True(RoadMetres(w.Holder, from, to) > 1000f, "the wall closes the road");

        // a 0.8 m man-sized Passage at the crossing (the wall's panel runs y 190 → 210, so the road's 3.5 m is 8.25..11.75 m along it)
        BreachWall(w, 9.6f, 10.4f, BreachKind.Passage); BreachWall(other, 9.6f, 10.4f, BreachKind.Passage);
        system.Execute(w.Repo, 0.1f); second.Execute(other.Repo, 0.1f);
        Assert.Equal(1, system.Blocked);
        Assert.Equal(1, second.Blocked);

        // a HOLE elsewhere on the wall: no Passage changed ⇒ no recompute at all
        int recomputes = system.Recomputes;
        BreachWall(w, 2f, 3f, BreachKind.Hole);
        system.Execute(w.Repo, 0.1f);
        Assert.Equal(recomputes, system.Recomputes);

        // the dozer's 4.7 m cut across the road ⇒ the road re-opens
        BreachWall(w, 7.6f, 12.3f, BreachKind.Passage); BreachWall(other, 7.6f, 12.3f, BreachKind.Passage);
        system.Execute(w.Repo, 0.1f); second.Execute(other.Repo, 0.1f);
        Assert.Equal(0, system.Blocked);
        Assert.Equal(0, second.Blocked);
        Assert.InRange(RoadMetres(w.Holder, from, to), 40f, 60f);

        for (int i = 0; i < 5; i++) system.Execute(w.Repo, 0.1f);
        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 200; i++) system.Execute(w.Repo, 0.1f);
        Assert.Equal(0L, GC.GetAllocatedBytesForCurrentThread() - before);
    }

    /// <summary>
    /// ⭐ <c>CE-11070</c> V-7 — a BASE wall (the terrain's own, an imported garden wall) across the west road never closes it; and a scenario wall that ENDS inside the road (half across)
    /// closes nothing. 🔴 Red-proof: <c>IsScenarioWallLabel</c> returns true for every label ⇒ the base wall closes the west road.
    /// </summary>
    [Fact]
    public void CE11070_ABaseWallAcrossARoad_AndAScenarioWallHalfAcross_CloseNothing()
    {
        using var w = Build(true, WallFeature(new Vector2(270, 190), new Vector2(270, 200.5f)), baseWall: true);
        var system = new BridgeClosureSystem(runtime: null, () => w.Pool);
        system.Execute(w.Repo, 0.1f);
        Assert.Contains(w.Terrain.Panels, p => p.Label == "Garden wall");
        Assert.Equal(1, system.Walls);
        Assert.Equal(0, system.Blocked);
    }

    /// <summary>
    /// ⭐ <c>CE-11070</c> — SUMO, PER LANE, on a 7 m two-way road (two edges, one 3.5 m lane each): a wall square across closes both; a DIAGONAL wall closes both; a 4.7 m cut over the
    /// north lane re-opens that edge only; a 0.8 m hole re-opens neither; a 2-lane one-way edge with one lane cut stays OPEN (SumoSharp closes edges, never lanes — the closed lane is
    /// reported). Pure, over <see cref="RoadBlockage.WallLine"/>s. 🔴 Red-proof: drop the gap test in <c>LaneClosedByWalls</c> ⇒ the cut re-opens nothing.
    /// </summary>
    [Fact]
    public void CE11070_SumoLanes_AWallClosesEach_ADiagonalOneToo_ADriveThroughCutOpensOneLane()
    {
        var net = Sim.Ingest.NetworkParser.ParseXml("""
            <net version="1.20"><location netOffset="0.00,0.00" convBoundary="0,-4,200,4" origBoundary="0,-4,200,4" projParameter="!"/>
            <edge id="east" from="a" to="b"><lane id="east_0" index="0" speed="13.9" length="200" width="3.5" shape="0,-1.75 200,-1.75"/></edge>
            <edge id="west" from="b" to="a"><lane id="west_0" index="0" speed="13.9" length="200" width="3.5" shape="200,1.75 0,1.75"/></edge>
            <edge id="wide" from="c" to="d"><lane id="wide_0" index="0" speed="13.9" length="200" width="3.5" shape="0,48.25 200,48.25"/><lane id="wide_1" index="1" speed="13.9" length="200" width="3.5" shape="0,51.75 200,51.75"/></edge>
            <junction id="a" type="dead_end" x="0" y="0" incLanes="west_0" intLanes="" shape="0,-3.5 0,3.5"/><junction id="b" type="dead_end" x="200" y="0" incLanes="east_0" intLanes="" shape="200,-3.5 200,3.5"/>
            <junction id="c" type="dead_end" x="0" y="50" incLanes="" intLanes="" shape="0,46.5 0,53.5"/><junction id="d" type="dead_end" x="200" y="50" incLanes="wide_0 wide_1" intLanes="" shape="200,46.5 200,53.5"/>
            </net>
            """);
        (double, double) Local(double x, double y) => (x, y);
        static RoadBlockage.WallLine Wall(Vector2 a, Vector2 b, params (float, float)[] gaps) => new("wall:t", new[] { a, b }, new[] { 0f, Vector2.Distance(a, b) }, gaps);
        List<string> Closed(params RoadBlockage.WallLine[] walls) { var l = new List<string>(); RoadBlockage.WallClosedEdges(net, Local, walls, l); l.Sort(StringComparer.Ordinal); return l; }

        Assert.Equal(new[] { "east", "west" }, Closed(Wall(new Vector2(100, -6), new Vector2(100, 6))));
        Assert.Equal(new[] { "east", "west" }, Closed(Wall(new Vector2(95, -6), new Vector2(105, 6))));   // diagonal
        // the wall's u = y + 6: a 4.7 m cut y -0.6 .. 4.1 covers the north (west-bound) lane's centre (1.75), not the south one's (-1.75)
        Assert.Equal(new[] { "east" }, Closed(Wall(new Vector2(100, -6), new Vector2(100, 6), (5.4f, 10.1f))));
        Assert.Equal(new[] { "east", "west" }, Closed(Wall(new Vector2(100, -6), new Vector2(100, 6))));   // (a 0.8 m Passage is no gap: FeatureWallLines drops it below minDriveThroughM)

        var lanes = new List<string>(); var edges = new List<string>();
        RoadBlockage.WallClosedEdges(net, Local, new[] { Wall(new Vector2(100, 44), new Vector2(100, 56), (5.4f, 10.1f)) }, edges, lanes);
        Assert.Empty(edges);
        Assert.Equal(new[] { "wide_0" }, lanes);
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
    /// ⭐ <c>CE-4624</c> MS-T3 (DESIGN_Minefields.md MR-16) — a minefield across the east road closes it to the cars only once it is MARKED on the ground (here: known at
    /// start — an own field, marked by its owners): an unknown field leaves the road open (cars drive in and set mines off). 🔴 Red-proof: drop <c>AddMinefields</c> from the
    /// closure ⇒ the known field never closes the road.
    /// </summary>
    [Fact]
    public void CE4624_AMarkedMinefieldAcrossARoad_ClosesIt_AnUnknownOneDoesNot()
    {
        using var w = Build();
        var r = w.Repo;
        if (!r.IsComponentTypeRegistered<Minefield>()) r.RegisterComponent<Minefield>();
        if (!r.IsComponentTypeRegistered<Fdp.Toolkit.Scenario.EntityGuid>()) r.RegisterComponent<Fdp.Toolkit.Scenario.EntityGuid>();
        if (!r.IsComponentTypeRegistered<MinefieldKnowledge>()) r.RegisterComponent<MinefieldKnowledge>();
        r.RegisterManagedComponent<Hrot.Map.Common.Components.EditablePolyline>();
        var system = new BridgeClosureSystem(runtime: null, () => w.Pool);
        system.Execute(r, 0.1f);
        var field = r.CreateEntity();
        r.AddComponent(field, new SimTransform { Position = new Vector3(270, 200, 0), Rotation = Quaternion.Identity });
        r.AddComponent(field, new Minefield { Type = MineType.AntiTank, SpacingM = 6f });
        r.AddComponent(field, new Fdp.Toolkit.Scenario.EntityGuid(Guid.NewGuid()));
        r.SetManagedComponent(field, new Hrot.Map.Common.Components.EditablePolyline { Points = new[] { new Vector2(-10, -10), new Vector2(10, -10), new Vector2(10, 10), new Vector2(-10, 10) } });
        r.Tick(); system.Execute(r, 0.1f);
        Assert.Equal(0, system.Blocked);                                   // unknown, unmarked: the road stays open
        var f = r.GetComponentRO<Minefield>(field); f.KnownAtStartSides = 1 << 1; r.SetComponent(field, f);
        r.Tick(); system.Execute(r, 0.1f);
        Assert.Equal(1, system.Blocked);                                   // marked: closed like a roadblock
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

    /// <summary>
    /// ⭐ CE-5008 / CE-4594 item 3 — a ROW of obstacle ENTITIES laid out like the editor's obstacle row, each given the components the TKB gives its type (<see cref="ObstacleTkbTranslator.Inject"/> over the concrete block's template — the translator the spawn path runs) and one
    /// <see cref="PlacementGroup"/>. ⚠ The row planner (<c>ObstacleRowOptions.Places</c>, in Hrot.Presentation) is not referenced by this project, so the places come from <c>FeatureExpander.LinePlacesSpaced</c> — the routine the planner calls — at the row's own spacing
    /// (a concrete block's length 2 m + the gap), facing ALONG the line (the line runs across the road).
    /// </summary>
    private static List<Entity> Row(World w, Vector2 from, Vector2 to, float gap, uint group)
    {
        Registered(w.Repo);
        if (!w.Repo.IsComponentTypeRegistered<SimTransform>()) w.Repo.RegisterComponent<SimTransform>();
        if (!w.Repo.IsComponentTypeRegistered<PlacementGroup>()) w.Repo.RegisterComponent<PlacementGroup>();
        var db = new Fdp.Toolkit.Tkb.TkbDatabase();
        Hrot.Map.Definitions.Tkb.NedTkbCatalog.RegisterObstacles(db);
        var template = db.GetByType(Hrot.Map.Common.TkbEntityTypes.Obstacle_ConcreteBlock)!;
        var box = ClutterTypes.ConcreteBlock;
        var members = new List<Entity>();
        foreach (var (p, deg) in FeatureExpander.LinePlacesSpaced(new[] { from, to }, box.Length + gap, FeatureExpander.MaxScatterItems))
        {
            var e = w.Repo.CreateEntity();
            w.Repo.AddComponent(e, new SimTransform { Position = new Vector3(p, 0f), Rotation = Quaternion.CreateFromAxisAngle(Vector3.UnitZ, deg * (MathF.PI / 180f)) });
            w.Repo.AddComponent(e, new PlacementGroup { Id = group });
            new ObstacleTkbTranslator().Inject(w.Repo, e, template);
            members.Add(e);
        }
        return members;
    }

    /// <summary>
    /// ⭐⭐ <c>CE-4594</c> item 3 (P-2) — FOUR concrete blocks end to end ACROSS the east road (an 8 m row over a 3.5 m road, one <c>PlacementGroup</c>) close it exactly as one barrier does: the road graph's <c>Blocked</c> = 1 and SUMO's two-lane edge is closed; deleting EVERY member (what
    /// deleting the group does) re-opens both. MEASURED, one member out of the tight row: a MIDDLE block gone leaves a 2 m hole inside the 3.5 m road ⇒ the road is OPEN again (a gap a car fits through re-opens it — closure needs the full width covered); an OUTER block gone (it stood
    /// beyond the kerb) leaves it CLOSED. And the planner's default 1.5 m gap row (3.5 m spacing) leaves holes: OPEN. 🔴 Red-proof: close on any covered point ⇒ the one-member and the gapped rows stay closed ⇒ red.
    /// </summary>
    [Fact]
    public void CE4594_ARowOfConcreteBlocksAcrossARoad_ClosesIt_TheWholeGroupGoneReOpensIt_OneMemberDependsOnWhich()
    {
        using var w = Build();
        var system = new BridgeClosureSystem(runtime: null, () => w.Pool);
        system.Execute(w.Repo, 0.1f);
        var from = new Vector2(245, 200); var to = new Vector2(295, 200);
        var row = Row(w, new Vector2(270, 196), new Vector2(270, 204), gap: 0f, group: 77u);
        Assert.Equal(4, row.Count);
        Assert.All(row, e => { Assert.Equal(77u, w.Repo.GetComponentRO<PlacementGroup>(e).Id); Assert.Equal("concrete", w.Repo.GetComponentRO<StaticObstacle>(e).Material.ToString()); Assert.Equal(2f, w.Repo.GetComponentRO<ObstacleShape>(e).Length); });
        system.Execute(w.Repo, 0.1f);
        Assert.Equal(1, system.Blocked);
        Assert.True(RoadMetres(w.Holder, from, to) > 1000f, "the row cuts the road");

        // an OUTER block gone (it stood beyond the kerb): still closed
        w.Repo.DestroyEntity(row[3]); system.Execute(w.Repo, 0.1f);
        Assert.Equal(1, system.Blocked);
        // a MIDDLE block gone: a 2 m hole in the 3.5 m road — open
        w.Repo.DestroyEntity(row[1]); system.Execute(w.Repo, 0.1f);
        Assert.Equal(0, system.Blocked);
        Assert.InRange(RoadMetres(w.Holder, from, to), 40f, 60f);
        // the rest of the group gone: open (nothing changes), the net is the base's
        w.Repo.DestroyEntity(row[0]); w.Repo.DestroyEntity(row[2]); system.Execute(w.Repo, 0.1f);
        Assert.Equal(0, system.Blocked); Assert.Equal(5, w.Holder.Current.Segments.Length);

        // the whole group at once: closed, then every member deleted ⇒ re-opened
        var again = Row(w, new Vector2(270, 196), new Vector2(270, 204), 0f, 78u);
        system.Execute(w.Repo, 0.1f);
        Assert.Equal(1, system.Blocked); Assert.Equal(4, w.Holder.Current.Segments.Length);
        foreach (var e in again) w.Repo.DestroyEntity(e);
        system.Execute(w.Repo, 0.1f);
        Assert.Equal(0, system.Blocked); Assert.Equal(5, w.Holder.Current.Segments.Length);
        Assert.InRange(RoadMetres(w.Holder, from, to), 40f, 60f);

        // the planner's default gap (1.5 m): 3.5 m spacing across 8 m — blocks with holes between them: the road stays open
        var gapped = Row(w, new Vector2(270, 196), new Vector2(270, 204), 1.5f, 79u);
        system.Execute(w.Repo, 0.1f);
        Assert.True(gapped.Count >= 2);
        Assert.Equal(0, system.Blocked);
    }

    /// <summary>
    /// ⭐ <c>CE-4594</c> item 3 (V-18) — SUMO's side of the same row: four tight concrete blocks across a two-lane edge close it (<c>ClosedEdges</c>, the footprints from <c>RoadBlockage.Blockers</c> over the entities); one MIDDLE member gone leaves one lane's width of hole ⇒ open.
    /// 🔴 Red-proof: <c>Blockers</c> ignoring <c>StaticObstacle</c> ⇒ the row closes nothing ⇒ red.
    /// </summary>
    [Fact]
    public void CE4594_ARowAcrossASumoEdge_ClosesIt_AndAMissingMiddleMemberReOpensIt()
    {
        var net = Sim.Ingest.NetworkParser.ParseXml("""
            <net version="1.20"><location netOffset="0.00,0.00" convBoundary="0,0,200,10" origBoundary="0,0,200,10" projParameter="!"/>
            <edge id="e" from="a" to="b"><lane id="e_0" index="0" speed="13.9" length="200" width="3.2" shape="0,-1.6 200,-1.6"/><lane id="e_1" index="1" speed="13.9" length="200" width="3.2" shape="0,1.6 200,1.6"/></edge>
            <junction id="a" type="dead_end" x="0" y="0" incLanes="" intLanes="" shape="0,-3.2 0,3.2"/><junction id="b" type="dead_end" x="200" y="0" incLanes="e_0 e_1" intLanes="" shape="200,-3.2 200,3.2"/>
            </net>
            """);
        (double, double) Local(double x, double y) => (x, y);
        using var w = new World();
        var row = Row(w, new Vector2(100, -4), new Vector2(100, 4), 0f, 80u);
        Assert.Equal(4, row.Count);
        var closed = new List<string>(); var footprints = new List<Vector2[]>();
        RoadBlockage.Blockers(w.Repo, footprints);
        Assert.Equal(4, footprints.Count);
        RoadBlockage.ClosedEdges(net, Local, footprints, closed);
        Assert.Equal(new[] { "e" }, closed);

        w.Repo.DestroyEntity(row[1]);
        RoadBlockage.Blockers(w.Repo, footprints);
        Assert.Equal(3, footprints.Count);
        RoadBlockage.ClosedEdges(net, Local, footprints, closed);
        Assert.Empty(closed);
    }

    public void Dispose() { try { Directory.Delete(_root, true); } catch { } }

    /// <summary>
    /// ⭐⭐ <c>CE-4692</c> BL-7 — a LAID bridge (a WalkableDeck obstacle over the river, its deck ends near the west and east roads' ends) is ROAD while it stands open: held Constructing it is
    /// not (the bake has not committed), once Active a route between the banks takes it (≈ 150 m instead of the ≈ 380 m detour) and the net has ONE added segment; it never blocks a road
    /// (a deck is exempt from the blocker walk); once it is gone the published net is the base again, segment for segment. 🔴 Red-proof: publish <c>Without</c> instead of <c>Edited</c>
    /// (no added segment) ⇒ the route keeps the detour.
    /// </summary>
    [Fact]
    public void CE4692_ALaidBridge_IsRoadWhileItStandsOpen_AndTheBaseIsUnchangedAfterIt()
    {
        using var w = Build(authoredBridge: false);
        w.Repo.RegisterComponent<StaticObstacle>(); w.Repo.RegisterComponent<ObstacleShape>();
        var sys = new BridgeClosureSystem(null, () => w.Pool);
        sys.Execute(w.Repo, 0.1f);
        int baseSegments; using (var l = w.Holder.Borrow()) baseSegments = l.Value.Segments.Length;
        var west = new Vector2(150, 200); var east = new Vector2(300, 200);
        float detour = RoadMetres(w.Holder, west, east);
        Assert.True(detour > 300f && detour < 500f, $"the base route between the banks is the detour ({detour:F0} m)");

        var deck = w.Repo.CreateEntity();
        w.Repo.AddComponent(deck, new SimTransform { Position = new Vector3(215, 200, 0), Rotation = Quaternion.Identity });
        w.Repo.AddComponent(deck, new StaticObstacle { Material = new FixedString32("steel-plate"), Kind = ObstacleKind.WalkableDeck });
        w.Repo.AddComponent(deck, new ObstacleShape { Length = 46f, Width = 4f, Height = 1.2f });
        w.Repo.SetLifecycleState(deck, EntityLifecycle.Constructing);
        sys.Execute(w.Repo, 0.1f);
        Assert.Equal(0, sys.Added);                                                       // not open before its bake commits
        Assert.Equal(detour, RoadMetres(w.Holder, west, east), 1);

        w.Repo.SetLifecycleState(deck, EntityLifecycle.Active);
        sys.Execute(w.Repo, 0.1f);
        Assert.Equal(1, sys.Added);
        Assert.Equal(0, sys.Blocked);                                                     // ⛔ the deck closes no road
        float over = RoadMetres(w.Holder, west, east);
        Assert.True(over < 200f, $"the route takes the laid bridge ({over:F0} m, detour {detour:F0} m)");
        using (var l = w.Holder.Borrow()) Assert.Equal(baseSegments + 1, l.Value.Segments.Length);

        w.Repo.DestroyEntity(deck);
        sys.Execute(w.Repo, 0.1f);
        Assert.Equal(0, sys.Added);
        using (var l = w.Holder.Borrow()) Assert.Equal(baseSegments, l.Value.Segments.Length);
        Assert.Equal(detour, RoadMetres(w.Holder, west, east), 1);
    }
}
