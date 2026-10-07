#nullable enable
using System;
using System.Linq;
using System.Numerics;
using Fdp.Toolkit.Navigation;
using Fdp.Toolkit.Navigation.Recast;
using Fdp.Toolkit.Terrain;
using Xunit;

namespace Fdp.Toolkit.Navigation.Recast.Tests;

/// <summary>
/// ⭐ W6 — the navmesh baked from a TERRAIN WORLD (docs/DESIGN_Terrain_World.md §4.1): engine-space (Z-up) queries
/// against a mesh built from the world file, through the swappable node navmesh.
/// </summary>
public sealed class RecastNavmeshFactoryTests
{
    // A 60×60 m world with a 20×20 m, 10 m-high building in the middle.
    private const string BlockWorld = """
        {"type":"FeatureCollection","hrot":{"schemaVersion":1,"bounds":[0,0,60,60],"groundZ":0},"features":[
          {"type":"Feature","properties":{"kind":"building","height":10},
           "geometry":{"type":"Polygon","coordinates":[[[20,20],[40,20],[40,40],[20,40],[20,20]]]}}]}
        """;

    private static INavmeshProvider Bake(string json)
        => new RecastNavmeshFactory { Layers = NavLayerMask.Infantry }.Build(TerrainWorldParser.Parse(json))
           ?? throw new InvalidOperationException("bake produced no mesh");

    [Fact]
    public void GeometrySource_EmitsRecastYUp_WithWalkableFacesPointingUp()
    {
        Assert.True(new TerrainWorldGeometrySource(TerrainWorldParser.Parse(BlockWorld))
            .TryGetTriangles(out var v, out var idx));
        for (int t = 0; t < idx.Length; t += 3)
        {
            Vector3 P(int i) => new(v[idx[i] * 3], v[(idx[i] * 3) + 1], v[(idx[i] * 3) + 2]);
            var n = Vector3.Cross(P(t + 1) - P(t), P(t + 2) - P(t));
            Assert.True(n.Y >= -1e-4f, $"triangle {t / 3} faces down in Recast space: {n}");
        }
    }

    [Fact]
    public void Bake_TheBuildingIsNotWalkable_AndThePathGoesAroundIt()
    {
        var nav = Bake(BlockWorld);
        Assert.True(nav.IsWalkable(new Vector3(5, 30, 0)));
        Assert.False(nav.IsWalkable(new Vector3(30, 30, 0)), "the building footprint must not be ground");

        // West to east straight through the building — the plan must go AROUND it.
        Span<NavWaypoint> wps = stackalloc NavWaypoint[64];
        int n = nav.PlanPath(new Vector3(5, 30, 0), new Vector3(55, 30, 0), wps, (uint)NavLayerMask.Infantry);
        Assert.True(n >= 3, $"expected a detour, got {n} waypoint(s)");
        for (int i = 0; i < n; i++)
        {
            var p = wps[i].Position;
            Assert.False(p.X > 20.5f && p.X < 39.5f && p.Y > 20.5f && p.Y < 39.5f,
                $"waypoint {i} {p} is inside the building");
            Assert.InRange(p.Z, -0.5f, 0.5f);   // Z-up: the ground is at Z = 0
        }
        Assert.Equal(55f, wps[n - 1].Position.X, 0.5f);
    }

    [Fact]
    public void SwitchableProvider_AnswersStraightLinesUntilABakeIsPublished_ThenTheBake()
    {
        var node = new SwitchableNavmeshProvider();
        Assert.False(node.HasBakedMesh);
        Assert.True(node.IsWalkable(new Vector3(30, 30, 0)));      // fallback: everywhere walkable
        uint before = node.QueryVersion();

        node.Publish(Bake(BlockWorld));
        Assert.True(node.HasBakedMesh);
        Assert.False(node.IsWalkable(new Vector3(30, 30, 0)));
        Assert.NotEqual(before, node.QueryVersion());

        node.Publish(null);                                         // terrain unload
        Assert.False(node.HasBakedMesh);
    }

    // ── ⭐ Buildings programme Stage 2 — walk inside (docs/DESIGN_Building_Interiors.md §6 B-1) ──────────────────

    // A 10 x 8 m two-storey house at (20,20): a door in the south wall, stairs (8,1)→(8,6) to storey 2, the upper floor
    // split around the stairwell so the stairs have headroom.
    private const string HouseWorld = """
        {"type":"FeatureCollection","hrot":{"schemaVersion":1,"bounds":[0,0,60,60],"groundZ":0},"features":[
          {"type":"Feature","properties":{"kind":"building","label":"H","building":{
             "footprint":[[0,0],[10,0],[10,8],[0,8]],
             "storeys":[
               {"height":3,"walls":[
                  {"from":[0,0],"to":[10,0],"openings":[{"kind":"door","at":4.5,"width":1.2}]},
                  {"from":[10,0],"to":[10,8]},{"from":[10,8],"to":[0,8]},{"from":[0,8],"to":[0,0]}],
                "stairs":[{"from":[8,1],"to":[8,6],"width":1.2}]},
               {"height":3,"floor":[ [[0,0],[7.3,0],[7.3,8],[0,8]], [[8.7,0],[10,0],[10,8],[8.7,8]],
                                     [[7.3,0],[8.7,0],[8.7,1],[7.3,1]], [[7.3,6],[8.7,6],[8.7,8],[7.3,8]] ],
                "walls":[{"from":[0,0],"to":[10,0]},{"from":[10,0],"to":[10,8]},{"from":[10,8],"to":[0,8]},{"from":[0,8],"to":[0,0]}]}]}},
           "geometry":{"type":"Point","coordinates":[20,20]}}]}
        """;

    [Fact]
    public void Stage2_InsideTheHouse_TheGroundIsWalkable_AndAPathFromOutsideReachesTheUpperStoreyByTheStairs()
    {
        var nav = Bake(HouseWorld);
        Assert.True(nav.IsWalkable(new Vector3(22, 24, 0)), "the ground floor inside an enterable building must be walkable");

        Span<NavWaypoint> wps = stackalloc NavWaypoint[128];
        var upstairs = new Vector3(22, 24, 3);
        int n = nav.PlanPath(new Vector3(25, 10, 0), upstairs, wps, (uint)NavLayerMask.Infantry);
        Assert.True(n >= 2, $"no path to the upper storey ({n} waypoints)");
        var last = wps[n - 1].Position;
        Assert.InRange(last.Z, 2.6f, 3.4f);                                       // it ENDS on storey 2, not under it
        Assert.InRange(last.X, 21f, 23f);
        for (int i = 0; i < n; i++)
        {
            var p = wps[i].Position;
            // between floors only ON the stair ramp (x 27.4..28.6, y 21..26) — never floating elsewhere
            if (p.Z > 0.4f && p.Z < 2.6f)
                Assert.True(p.X > 27f && p.X < 29f && p.Y > 20.5f && p.Y < 26.5f, $"waypoint {i} {p} is between floors off the stairs");
            // through the south wall (y = 20) only at the doorway (x 24.5..25.7)
            if (MathF.Abs(p.Y - 20f) < 0.15f && p.Z < 2.6f)
                Assert.True(p.X > 24.3f && p.X < 25.9f, $"waypoint {i} {p} crosses the south wall outside the door");
        }
    }

    // ── ⭐ Buildings Stage 5c — doors in the navmesh (docs/DESIGN_Building_Interiors.md §3j "5c") ──────────────────

    // A closed 10 x 8 m room at (20,20) whose ONLY way in is one door ("front", 1.2 m) in the south wall.
    private const string OneDoorRoom = """
        {"type":"FeatureCollection","hrot":{"schemaVersion":1,"bounds":[0,0,60,60],"groundZ":0},"features":[
          {"type":"Feature","properties":{"kind":"building","label":"R","doors":{"front":"open"},"building":{
             "footprint":[[0,0],[10,0],[10,8],[0,8]],
             "storeys":[{"height":3,"walls":[
                  {"from":[0,0],"to":[10,0],"openings":[{"kind":"door","at":4.4,"width":1.2,"doorId":"front"}]},
                  {"from":[10,0],"to":[10,8]},{"from":[10,8],"to":[0,8]},{"from":[0,8],"to":[0,0]}]}]}},
           "geometry":{"type":"Point","coordinates":[20,20]}}]}
        """;

    [Fact]
    public void Stage5c_TheDoorwayBakesIntoDoorPolygons_AndAPathThroughItIsMarkedDoor()
    {
        var world = TerrainWorldParser.Parse(OneDoorRoom, "range");
        var nav = (DotRecastNavmeshProvider)new RecastNavmeshFactory { Layers = NavLayerMask.Infantry }.Build(world)!;
        Assert.True(nav.DoorPolyCount(NavLayerMask.Infantry) > 0, "the doorway must bake into polygons of its own (DoorArea)");

        Span<NavWaypoint> wps = stackalloc NavWaypoint[64];
        int n = nav.PlanPath(new Vector3(25, 10, 0), new Vector3(25, 25, 0), wps, (uint)NavLayerMask.Infantry);
        Assert.True(n >= 2, $"no path into the room ({n})");
        bool door = false;
        for (int i = 0; i < n; i++) door |= wps[i].Traversal == TraversalKind.Door;
        Assert.True(door, "a corner in the doorway must be a Door waypoint (N4)");
    }

    [Fact]
    public void Stage5c_ALockedDoorIsAWall_AClosedOneIsPassable_AndTheLiveStateIsReadAtQueryTime()
    {
        var world = TerrainWorldParser.Parse(OneDoorRoom, "range");
        var nav = new RecastNavmeshFactory { Layers = NavLayerMask.Infantry }.Build(world)!;
        var outside = new Vector3(25, 10, 0); var inside = new Vector3(25, 25, 0);
        int door = world.DoorIndexOf("range/R/front");

        Assert.True(nav.PathExists(outside, inside, (uint)NavLayerMask.Infantry));
        world.SetDoorState(door, TerrainDoorState.Locked);               // what the door mirror does (5b)
        Assert.False(nav.PathExists(outside, inside, (uint)NavLayerMask.Infantry), "a locked door is impassable");
        world.SetDoorState(door, TerrainDoorState.Closed);
        Assert.True(nav.PathExists(outside, inside, (uint)NavLayerMask.Infantry), "infantry may open a closed door (N2)");
        world.SetDoorState(door, TerrainDoorState.Destroyed);
        Assert.True(nav.PathExists(outside, inside, (uint)NavLayerMask.Infantry));
    }

    [Fact]
    public void Stage5c_TheFilter_ChargesAClosedDoorOnceOnEntry_AndKeepsVehiclesOut()
    {
        var world = TerrainWorldParser.Parse(OneDoorRoom, "range");
        int door = world.DoorIndexOf("range/R/front");
        var doorPolys = new System.Collections.Generic.Dictionary<long, int> { [5] = door, [6] = door };
        var infantry = new DoorAwareQueryFilter(doorPolys, world, canOpenDoors: true);
        var vehicle  = new DoorAwareQueryFilter(doorPolys, world, canOpenDoors: false);
        var poly = new DotRecast.Detour.DtPoly(0, 6) { flags = 1 };
        poly.SetArea(NavDoorways.DoorArea);
        var a = new DotRecast.Core.Numerics.RcVec3f(0, 0, 0); var b = new DotRecast.Core.Numerics.RcVec3f(1, 0, 0);
        float Cost(DoorAwareQueryFilter f, long prev, long cur) => f.GetCost(a, b, prev, null!, poly, cur, null!, poly, 0, null!, poly);

        Assert.Equal(1f, Cost(infantry, 1, 5), 3);                                   // open: the plain distance
        world.SetDoorState(door, TerrainDoorState.Closed);
        Assert.Equal(1f + DoorAwareQueryFilter.ClosedDoorPenaltyMetres, Cost(infantry, 1, 5), 3);   // entering it
        Assert.Equal(1f, Cost(infantry, 5, 6), 3);                                   // already inside the same door
        Assert.Equal(1f, Cost(infantry, 1, 9), 3);                                   // not a doorway polygon

        Assert.True(infantry.PassFilter(5, null!, poly));
        Assert.False(vehicle.PassFilter(5, null!, poly));                            // N2 — vehicles never use a doorway
        Assert.True(vehicle.PassFilter(9, null!, poly));
        world.SetDoorState(door, TerrainDoorState.Locked);
        Assert.False(infantry.PassFilter(5, null!, poly));
    }

    // ── ⭐ CE-2122 — the EQS and NavigationSolver modules query ONE provider from two background threads ─────────

    [Fact]
    public void CE2122_ConcurrentPlanPathAndPathCost_FromSeveralThreads_NeverCorruptTheQuery()
    {
        var nav = Bake(BlockWorld);
        var errors = new System.Collections.Concurrent.ConcurrentQueue<Exception>();
        var threads = new System.Threading.Thread[4];
        for (int t = 0; t < threads.Length; t++)
        {
            int seed = t;
            threads[t] = new System.Threading.Thread(() =>
            {
                try
                {
                    Span<NavWaypoint> wps = stackalloc NavWaypoint[64];
                    for (int i = 0; i < 400; i++)
                    {
                        float y = 5f + ((i + seed) % 50);
                        int n = nav.PlanPath(new Vector3(5, y, 0), new Vector3(55, 60 - y, 0), wps, (uint)NavLayerMask.Infantry);
                        if (n < 2) throw new InvalidOperationException($"no path ({n}) at i={i}");
                        if (!(nav.PathCost(new Vector3(5, y, 0), new Vector3(55, y, 0), (uint)NavLayerMask.Infantry) > 0f))
                            throw new InvalidOperationException($"no cost at i={i}");
                    }
                }
                catch (Exception ex) { errors.Enqueue(ex); }
            });
        }
        foreach (var th in threads) th.Start();
        foreach (var th in threads) th.Join();
        Assert.True(errors.IsEmpty, string.Join(" | ", errors.Select(e => e.GetType().Name + ": " + e.Message).Take(3)));
    }
}
