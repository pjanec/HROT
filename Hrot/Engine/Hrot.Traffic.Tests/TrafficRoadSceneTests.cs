using System.Numerics;
using Fdp.Core;
using Fdp.Toolkit.Terrain;
using Hrot.Map.Common.Services;
using Sim.Scene;

namespace Hrot.Traffic.Tests;

/// <summary>AQ93 V7/V8 (R-4049) — the demo city's road scene, as the terrain load builds it.</summary>
public sealed class TrafficRoadSceneTests
{
    [Fact]
    public void V7_TheDemoCityScene_HasEveryKind_AndLightsAtItsSignalisedJunctions()
    {
        var roads = Load().Roads;

        var kinds = roads.Meshes.Select(m => m.Kind).ToHashSet();
        foreach (var k in new[] { SceneKind.Asphalt, SceneKind.Junction, SceneKind.LaneMarking, SceneKind.Crosswalk, SceneKind.AmbientGround })
            Assert.Contains(k, kinds);
        Assert.True(roads.Signals.Count > 0);
        // junction d_4_4 (net 2600, 2600) is a traffic light at local (400, 400): a lamp stands within 30 m of it.
        Assert.Contains(roads.Signals, s => Vector2.Distance(new Vector2(s.Head.X, s.Head.Y), new Vector2(400f, 400f)) < 30f);
    }

    /// <summary>V8 — the ambient ground stops at OUR terrain: no ground cell is centred inside the 800 m demo-city box, and the roads
    /// themselves run through it (placed by the anchor: local = net − (2200, 2200)).</summary>
    [Fact]
    public void V8_TheAmbientGround_LeavesOurTerrainOut_TheRoadsDoNot()
    {
        var roads = Load().Roads;
        var ground = roads.Meshes.Single(m => m.Kind == SceneKind.AmbientGround);
        for (int t = 0; t + 2 < ground.Indices.Length; t += 6)   // each cell is two triangles
        {
            var c = (ground.Vertices[ground.Indices[t]] + ground.Vertices[ground.Indices[t + 1]] + ground.Vertices[ground.Indices[t + 2]]) / 3f;
            Assert.False(c.X > 20f && c.X < 780f && c.Y > 20f && c.Y < 780f, $"a ground cell sits at ({c.X}, {c.Y}), inside our terrain");
        }

        var asphalt = roads.Meshes.Single(m => m.Kind == SceneKind.Asphalt && !m.Sidewalk);
        Assert.Contains(asphalt.Vertices, v => v.X > 0f && v.X < 800f && v.Y > 0f && v.Y < 800f);
        Assert.Contains(asphalt.Vertices, v => v.X < -500f);   // and beyond it: the net is 4.75 km
    }

    /// <summary>
    /// ⭐ <c>CE-1197</c> — 🔒 user, live: <i>"side-walks on sumo roads not rendered in different shade of gray"</i>. The pedestrian-only lanes
    /// (sidewalks, walking areas) are their own mesh; the carriageway keeps every other ribbon; together they are exactly what
    /// <c>Sim.Scene</c> merged into Asphalt (nothing lost, nothing drawn twice). ✅ Red-proof: <c>IsSidewalk</c> ⇒ false.
    /// </summary>
    [Fact]
    public void CE1197_TheSidewalks_AreTheirOwnMesh_SplitFromTheCarriageway()
    {
        var cache = Load();
        var roads = cache.Roads;
        var road = roads.Meshes.Single(m => m.Kind == SceneKind.Asphalt && !m.Sidewalk);
        var walk = roads.Meshes.Single(m => m.Sidewalk);
        Assert.Equal(SceneKind.Asphalt, walk.Kind);

        var net = cache.Model;
        var whole = RoadSceneBuilder.Build(net, new SceneFrame(0, 0)).Meshes.Single(m => m.Kind == SceneKind.Asphalt).Mesh;
        // the carriageway is exactly Sim.Scene's asphalt less the pedestrian ribbons (the walking areas are filled as polygons instead)
        int walkRibbons = RoadMeshBuilder.BuildAll(new SceneFrame(0, 0), net, includeInternal: true)
            .Where(x => TrafficRoadScene.IsSidewalk(net.LanesByHandle.First(l => l.Handle == x.Handle))).Sum(x => x.Mesh.TriangleCount);
        // (the scene is laid on the ground now, R-4030: a few triangles at the net's edge, where the ground beyond rises into hills, are cut — never fewer)
        int expected = whole.TriangleCount - walkRibbons;
        Assert.InRange(road.TriangleCount, expected, expected + expected / 100);

        int walkLanes = net.LanesByHandle.Count(TrafficRoadScene.IsSidewalk);
        Assert.True(walkLanes > 500, $"pedestrian-only lanes in the demo city: {walkLanes}");
        Assert.True(walk.TriangleCount > walkLanes, $"sidewalk triangles {walk.TriangleCount} for {walkLanes} lanes");
        Assert.DoesNotContain(net.LanesByHandle, l => TrafficRoadScene.IsSidewalk(l) && (l.AllowsRoadVehicle || CrosswalkBuilder.IsCrossingLaneId(l.Id)));
    }

    /// <summary>
    /// ⭐ <c>CE-4122</c> (<c>docs/DESIGN_Map_3D_Mode.md</c> §6ad-2) — the ground beyond our terrain follows the SUMO net's own height field where the net is:
    /// loading a terrain with a net attaches a <c>BeyondGround</c> to the PUBLISHED world (the loader attaches in Prepare, before publish), in local metres through
    /// the placement, its footprint the net's bounds. A point deep inside the footprint, far from our terrain, is exactly the field there.
    /// </summary>
    [Fact]
    public void CE4122_TheNetsHeightField_IsAttachedToThePublishedWorld_AsTheGroundBeyond()
    {
        using var world = new EntityRepository();
        var residency = new TerrainResidency(new TerrainCatalog(new[] { TerrainRoot() }), new CarKinem.Road.RoadNetworkHolder());
        residency.AttachContent(new SumoNetTerrainLoader());
        residency.Commit(world, residency.Prepare("demo-city"));
        var cache = TrafficNetCache.Of(world)!;
        var terrain = world.GetSingletonManaged<Fdp.Toolkit.Terrain.TerrainWorld>()!;
        var beyond = terrain.Beyond;
        Assert.NotNull(beyond);
        Assert.True(beyond!.HasField);

        var field = Sim.Scene.TerrainField.FromNetwork(cache.Model);
        var (minX, minY, maxX, maxY) = TrafficRoadScene.NetBounds(cache.Model);
        // the footprint's corners are the net's corners, placed
        foreach (var (nx, ny) in new[] { (minX, minY), (maxX, minY), (maxX, maxY), (minX, maxY) })
        {
            var (lx, ly) = cache.Placement.ToLocal(nx, ny);
            Assert.True(beyond.DistanceToField((float)lx, (float)ly) < 0.01f, $"the net corner ({nx}, {ny}) is on the footprint");
        }
        // a point of the net, far (> 300 m) from our terrain's rectangle: the ground there IS the net's field
        int checkedPoints = 0;
        var (rmin, rmax, _) = terrain.GroundRect;
        for (double nx = minX; nx <= maxX; nx += (maxX - minX) / 23.0)
            for (double ny = minY; ny <= maxY; ny += (maxY - minY) / 19.0)
            {
                var (lx, ly) = cache.Placement.ToLocal(nx, ny);
                float fx = (float)lx, fy = (float)ly;
                float dx = System.Math.Max(System.Math.Max(rmin.X - fx, fx - rmax.X), 0f), dy = System.Math.Max(System.Math.Max(rmin.Y - fy, fy - rmax.Y), 0f);
                if (System.Math.Sqrt(dx * dx + dy * dy) < 320.0 || !beyond.InField(fx, fy)) continue;
                Assert.Equal((float)field.HeightAt(nx, ny), terrain.GroundHeightAt(fx, fy), 2);
                checkedPoints++;
            }
        Assert.True(checkedPoints > 0, "the net reaches more than 300 m past our terrain (the demo city's net is 4.75 km)");
    }

    // ── ⭐ R-4030 (AQ93 E2b) — a FLAT net is laid on OUR ground ─────────────────────────────────────────────────────────────

    private static readonly Lazy<(Sim.Ingest.NetworkModel Model, SumoNetPlacement Placement)> DemoNet = new(() =>
    {
        string dir = Path.Combine(TerrainRoot(), "demo-city");
        var model = Sim.Ingest.NetworkParser.ParseXml(File.ReadAllText(Path.Combine(dir, "demo-city.net.xml")));
        return (model, SumoNetPlacement.For(model, new TerrainSumoNet("demo-city.net.xml", 2200, 2200), null));
    });

    /// <summary>Hills of ~8 m on a 350 m plateau — the terrain the user's OSM import and random generator produce.</summary>
    private static float Hilly(float x, float y) => 350f + 8f * MathF.Sin(x / 60f) + 5f * MathF.Cos(y / 80f);

    /// <summary>
    /// ⭐ R-4030 / E2b — 🔒 user, live: <i>"the Sumo road network is rendered at zero level while the rest of the terrain was generated to some
    /// much higher level, like 300-400 meters"</i>. A FLAT net on hills around 350 m: EVERY vertex of EVERY mesh (roads, sidewalks, junctions, paint,
    /// crosswalks, the ambient ground) lies on the ground — within a few centimetres of <c>GroundHeightAt</c> — and every triangle
    /// also follows the hill BETWEEN its corners (its centre and its edges' middles are on the ground too). ✅ Red-proof: the scene's drape ⇒ null ⇒ z 0 ⇒ red.
    /// </summary>
    [Fact]
    public void E2b_AFlatNet_IsLaidOnTheGround_EveryVertex_AndBetweenTheCorners()
    {
        var (model, placement) = DemoNet.Value;
        Assert.True(TrafficDrape.IsFlat(model));

        var scene = TrafficRoadScene.Build(model, placement, new Vector2(0, 0), new Vector2(800, 800), Hilly);

        double worst = 0;
        foreach (var mesh in scene.Meshes)
        {
            foreach (var v in mesh.Vertices) worst = Math.Max(worst, Math.Abs(v.Z - Hilly(v.X, v.Y)));
            for (int t = 0; t + 2 < mesh.Indices.Length; t += 3)
            {
                var a = mesh.Vertices[mesh.Indices[t]]; var b = mesh.Vertices[mesh.Indices[t + 1]]; var c = mesh.Vertices[mesh.Indices[t + 2]];
                // BETWEEN the corners: the flat facet's own height at its centre and at each edge's middle against the real ground there — a
                // road triangle that spanned a hill (the 2 km demo-city ribbons) would be metres off
                foreach (var m in new[] { (a + b + c) / 3f, (a + b) / 2f, (b + c) / 2f, (c + a) / 2f })
                    worst = Math.Max(worst, Math.Abs(m.Z - Hilly(m.X, m.Y)) - (mesh.Kind == SceneKind.AmbientGround ? 4f : 0.0));
            }
        }
        Assert.True(worst < 0.2, $"the worst vertex / facet is {worst} m off the ground (the ambient ground's 20 m facets are allowed 4 m of hill)");
        Assert.All(scene.Signals, s => { Assert.InRange(s.Pole.Z, Hilly(s.Pole.X, s.Pole.Y) - 0.2f, Hilly(s.Pole.X, s.Pole.Y) + 0.5f); Assert.True(s.Head.Z > s.Pole.Z); });
    }

    /// <summary>⭐ R-4025 stays: a net WITH z is placed exactly as the net says — the ground function is ignored, the vertices are those of the undraped build.
    /// ✅ Red-proof: <c>IsFlat</c> ⇒ true ⇒ the ground is added to a net that has its own heights ⇒ red.</summary>
    [Fact]
    public void E2b_ANetWithZ_IsUntouched()
    {
        var (flat, placement) = DemoNet.Value;
        string xml = System.Text.RegularExpressions.Regex.Replace(File.ReadAllText(Path.Combine(TerrainRoot(), "demo-city", "demo-city.net.xml")),
            "(<lane [^>]*shape=\")([^\"]*)(\")",
            m => m.Groups[1].Value + string.Join(' ', m.Groups[2].Value.Split(' ').Select(p => p + ",12.5")) + m.Groups[3].Value);
        var withZ = Sim.Ingest.NetworkParser.ParseXml(xml);
        Assert.False(TrafficDrape.IsFlat(withZ));

        var drapedCall = TrafficRoadScene.Build(withZ, placement, new Vector2(0, 0), new Vector2(800, 800), Hilly);
        var plain = TrafficRoadScene.Build(withZ, placement, new Vector2(0, 0), new Vector2(800, 800));
        Assert.Equal(plain.TriangleCount, drapedCall.TriangleCount);
        for (int i = 0; i < plain.Meshes.Count; i++) Assert.Equal(plain.Meshes[i].Vertices, drapedCall.Meshes[i].Vertices);
        Assert.Contains(plain.Meshes.Single(m => m.Kind == SceneKind.Asphalt && !m.Sidewalk).Vertices, v => Math.Abs(v.Z - 12.5f) < 0.01f);
    }

    /// <summary>⭐ R-4030 end to end — the terrain LOAD, on a terrain whose ground is 350 m: the cache carries the drape (the ground function the roads were laid with),
    /// the roads and the ambient ground beyond sit at 350, and the ground beyond our footprint meets our terrain's level instead of dropping to 0 (AQ93 §8.8 H2).</summary>
    [Fact]
    public void E2b_TheLoad_LaysTheRoadsAndTheGroundBeyond_OnTheTerrainsLevel()
    {
        using var city = ElevatedDemoCity.Load(350f);
        var cache = city.Cache;
        Assert.NotNull(cache.Drape);
        Assert.Equal(350f, cache.Drape!(100f, 100f), 3);
        foreach (var mesh in cache.Roads.Meshes)
            Assert.All(mesh.Vertices, v => Assert.InRange(v.Z, 349.9f, 350.2f));
        // a plateau costs no extra triangles: nothing is cut where the ground is a plane
        var undraped = TrafficRoadScene.Build(cache.Model, cache.Placement, new Vector2(0, 0), new Vector2(800, 800));
        Assert.InRange(cache.Roads.TriangleCount, undraped.TriangleCount, undraped.TriangleCount + undraped.TriangleCount / 100);
        var terrain = city.World.GetSingletonManaged<Fdp.Toolkit.Terrain.TerrainWorld>()!;
        Assert.Equal(350f, terrain.GroundHeightAt(-1500f, 400f), 2);   // 1.5 km beyond the edge, inside the net: the origin's altitude, not 0
        Assert.Equal(350f, terrain.GroundHeightAt(-5f, 400f), 2);
    }

    /// <summary>
    /// ⭐ AQ93 E2b "adjust after netconvert" — a net given OUR heights by <see cref="Hrot.Terrain.InputSet.NetElevation"/> is no longer flat: SumoSharp parses
    /// <c>ShapeZ</c> on EVERY lane (it needs all points of a shape to carry a z), <see cref="TrafficDrape.IsFlat"/> is false, and the road scene — built WITHOUT any ground
    /// function, so no drape applies — stands on the ground: the lanes' own z for asphalt, paint and crosswalks, the field baked from them for the junction fills and walking
    /// areas. ✅ Red-proof: <c>NetElevation</c> writes no z ⇒ the net stays flat ⇒ red.
    /// </summary>
    [Fact]
    public void E2b_ANetGivenOurHeights_IsNotFlat_AndItsSceneStandsOnTheGround()
    {
        float Gentle(float x, float y) => 350f + 0.05f * x + 3f * MathF.Sin(x / 200f) * MathF.Cos(y / 170f);
        string tmp = Path.Combine(Path.GetTempPath(), "e2b-" + Guid.NewGuid().ToString("N")[..10] + ".net.xml");
        try
        {
            File.Copy(Path.Combine(TerrainRoot(), "demo-city", "demo-city.net.xml"), tmp);
            var result = Hrot.Terrain.InputSet.NetElevation.Apply(tmp, (x, y) => (double)Gentle((float)(x - 2200), (float)(y - 2200)));   // net metres = local + the anchor
            Assert.True(result.PointsAfter > result.PointsBefore);
            var model = Sim.Ingest.NetworkParser.ParseXml(File.ReadAllText(tmp));
            Assert.False(TrafficDrape.IsFlat(model));
            Assert.All(model.LanesByHandle, l => Assert.NotNull(l.ShapeZ));
            Assert.All(model.LanesByHandle, l => Assert.Equal(l.Shape.Count, l.ShapeZ!.Count));

            var placement = SumoNetPlacement.For(model, new TerrainSumoNet("demo-city.net.xml", 2200, 2200), null);
            var scene = TrafficRoadScene.Build(model, placement, new Vector2(0, 0), new Vector2(800, 800));   // no ground function: the net carries its heights
            foreach (var mesh in scene.Meshes.Where(m => m.Kind != SceneKind.AmbientGround))
            {
                float tol = mesh.Kind == SceneKind.Junction || mesh.Sidewalk ? 0.6f : 0.15f;   // a junction fill takes its z from the baked field, not from a lane
                var bad = mesh.Vertices.Where(v => Math.Abs(v.Z - Gentle(v.X, v.Y)) > tol).ToList();
                Assert.True(bad.Count == 0, $"{mesh.Kind}{(mesh.Sidewalk ? " (walk)" : "")}: {bad.Count} of {mesh.Vertices.Length} vertices off the ground by more than {tol} m, e.g. {(bad.Count > 0 ? bad[0].Z - Gentle(bad[0].X, bad[0].Y) : 0)}");
            }
        }
        finally { try { File.Delete(tmp); } catch (IOException) { } }
    }

    /// <summary>A copy of the demo city (its flat net) whose ground is <c>groundZ</c> metres, loaded through the real residency; the folder is removed on dispose.</summary>
    internal sealed class ElevatedDemoCity : IDisposable
    {
        private readonly string _root;
        public EntityRepository World { get; }
        public TrafficNetCache Cache { get; }

        private ElevatedDemoCity(string root, EntityRepository world, TrafficNetCache cache) { _root = root; World = world; Cache = cache; }

        public static ElevatedDemoCity Load(float groundZ)
        {
            string src = Path.Combine(TerrainRoot(), "demo-city");
            string root = Path.Combine(Path.GetTempPath(), "e2b-" + Guid.NewGuid().ToString("N")[..10]);
            string dst = Path.Combine(root, "demo-city");
            Directory.CreateDirectory(dst);
            foreach (var f in new[] { "terrain.json", "demo-city.net.xml" }) File.Copy(Path.Combine(src, f), Path.Combine(dst, f));
            File.WriteAllText(Path.Combine(dst, "demo-city.world.geojson"),
                "{\"type\":\"FeatureCollection\",\"hrot\":{\"schemaVersion\":1,\"bounds\":[0,0,800,800],\"groundZ\":" + groundZ.ToString(System.Globalization.CultureInfo.InvariantCulture) + "},\"features\":[]}");
            var world = new EntityRepository();
            var residency = new TerrainResidency(new TerrainCatalog(new[] { root }), new CarKinem.Road.RoadNetworkHolder());
            residency.AttachContent(new SumoNetTerrainLoader());
            residency.Commit(world, residency.Prepare("demo-city"));
            return new ElevatedDemoCity(root, world, TrafficNetCache.Of(world)!);
        }

        public void Dispose()
        {
            World.Dispose();
            try { Directory.Delete(_root, true); } catch (IOException) { }
        }
    }

    internal static TrafficNetCache Load()
    {
        using var world = new EntityRepository();
        var residency = new TerrainResidency(new TerrainCatalog(new[] { TerrainRoot() }), new CarKinem.Road.RoadNetworkHolder());
        residency.AttachContent(new SumoNetTerrainLoader());
        residency.Commit(world, residency.Prepare("demo-city"));
        return TrafficNetCache.Of(world)!;
    }

    internal static string TerrainRoot()
    {
        return Fdp.Toolkit.Orchestration.ContentLayout.TerrainAssets(Fdp.Toolkit.Orchestration.ContentLayout.TryFindSourceTree()!);
    }
}
