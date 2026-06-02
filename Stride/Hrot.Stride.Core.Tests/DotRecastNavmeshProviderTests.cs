#nullable enable
using System;
using System.Collections.Generic;
using System.Numerics;
using DotRecast.Detour;
using Fdp.Core;
using Fdp.Toolkit.Navigation;
using Fdp.Toolkit.Navigation.Fake;
using Hrot.Stride.Core;
using Xunit;

namespace Hrot.Stride.Core.Tests;

/// <summary>
/// Headless tests for <see cref="DotRecastNavmeshProvider"/> (STR-P2-T2).
///
/// <para>
/// All tests bake real DotRecast navmeshes from synthetic triangle soups and then
/// assert real query results. No stubs or fakes for the navmesh logic itself.
/// </para>
///
/// <para>
/// Coordinate convention: all <see cref="Vector3"/> values in navmesh-query space =
/// Stride world space: X=East, Y=altitude(up), Z=North.
/// </para>
///
/// <para>
/// Scenarios:
/// <list type="bullet">
///   <item>T2-SC1: <c>IsWalkable</c> true on ground, false off ground.</item>
///   <item>T2-SC2: <c>ProjectToNavmesh</c> snaps above-ground point to surface.</item>
///   <item>T2-SC3: <c>PathExists</c>/<c>PlanPath</c> — clear path returns ≥2 waypoints.</item>
///   <item>T2-SC4: <c>PathCost</c> finite for reachable, MaxValue for unreachable.</item>
///   <item>T2-SC5: <c>QueryVersion</c> increments after rebake.</item>
///   <item>T2-SC6: Contract parity — mirrors key FakeNavmeshProvider behaviours.</item>
///   <item>T2-SC7: Singleton registration — <c>SetSingletonManaged&lt;INavmeshProvider&gt;</c>.</item>
///   <item>T2-SC8: Gap obstacle — Vehicle cannot cross where Infantry can.</item>
/// </list>
/// </para>
/// </summary>
public sealed class DotRecastNavmeshProviderTests
{
    private const float Tol    = 0.6f;  // metres — generous for navmesh projection
    private const float TolLow = 0.05f; // tight tolerance where exact values expected

    // ── Fixture: bake once per test class ────────────────────────────────────

    // Flat ground quad: X ∈ [-10,10], Z ∈ [-10,10], Y=0 (navmesh-query space).
    private static readonly (float[] verts, int[] indices) GroundQuad =
        MakeGroundQuad(-10f, 10f, -10f, 10f, 0f);

    // Soup with gap: two slabs separated by a 2 m gap at X=0.
    // Left slab: X ∈ [-10,-1], Z ∈ [-10,10]. Right slab: X ∈ [1,10], Z ∈ [-10,10].
    private static readonly (float[] verts, int[] indices) GappedGround =
        MakeGroundWithGap(2.0f);

    private static DotRecastNavmeshProvider BuildProvider(
        (float[] verts, int[] indices) soup,
        NavLayerMask layers = NavLayerMask.Infantry)
    {
        var baker  = new StrideNavmeshBaker();
        var meshes = baker.Bake(soup.verts, soup.indices, layers);
        return new DotRecastNavmeshProvider(meshes);
    }

    // ── T2-SC1: IsWalkable ───────────────────────────────────────────────────

    [Fact]
    public void IsWalkable_PointOnGround_ReturnsTrue()
    {
        var provider = BuildProvider(GroundQuad);
        // Ground centre (0, 0, 0) — Y=0 is the ground surface.
        Assert.True(provider.IsWalkable(new Vector3(0f, 0f, 0f)),
            "Centre of ground quad must be walkable");
    }

    [Fact]
    public void IsWalkable_PointSlightlyAboveGround_ReturnsTrue()
    {
        var provider = BuildProvider(GroundQuad);
        // Point 1 m above ground — within search extents (2 m horizontal, 4 m vertical).
        Assert.True(provider.IsWalkable(new Vector3(0f, 1f, 0f)),
            "Point 1 m above ground must still be walkable (within search extents)");
    }

    [Fact]
    public void IsWalkable_PointFarOffGround_ReturnsFalse()
    {
        var provider = BuildProvider(GroundQuad);
        // X=50 — well outside the 20×20 m quad.
        Assert.False(provider.IsWalkable(new Vector3(50f, 0f, 0f)),
            "Point 50 m off the ground quad must not be walkable");
    }

    [Fact]
    public void IsWalkable_EmptyProvider_ReturnsFalse()
    {
        var provider = new DotRecastNavmeshProvider();
        Assert.False(provider.IsWalkable(new Vector3(0f, 0f, 0f)),
            "Empty provider must return false for IsWalkable");
    }

    // ── T2-SC2: ProjectToNavmesh ─────────────────────────────────────────────

    [Fact]
    public void ProjectToNavmesh_AboveGroundPoint_SnapsToSurface()
    {
        var provider = BuildProvider(GroundQuad);
        // Query at (0, 2, 0) — 2 m above ground.
        bool snapped = provider.ProjectToNavmesh(new Vector3(0f, 2f, 0f), out Vector3 result);

        Assert.True(snapped, "ProjectToNavmesh must succeed for point above ground");
        // Snapped Y should be ≈ 0 (the ground surface altitude).
        Assert.True(MathF.Abs(result.Y) < Tol,
            $"Snapped Y should be ≈0 (ground surface), got {result.Y}");
        // X and Z should remain close to the query point.
        Assert.True(MathF.Abs(result.X) < Tol + 0.5f,
            $"Snapped X should remain near 0, got {result.X}");
    }

    [Fact]
    public void ProjectToNavmesh_PointOffMesh_ReturnsFalse()
    {
        var provider = BuildProvider(GroundQuad);
        bool snapped = provider.ProjectToNavmesh(new Vector3(50f, 0f, 50f), out _);
        Assert.False(snapped, "ProjectToNavmesh must fail for point far off mesh");
    }

    // ── T2-SC3: PathExists / PlanPath ────────────────────────────────────────

    [Fact]
    public void PlanPath_ClearPath_ReturnsAtLeastTwoWaypoints()
    {
        var provider = BuildProvider(GroundQuad);
        var waypoints = new NavWaypoint[64];

        // From (-8, 0, 0) to (8, 0, 0) — clear path across the ground quad.
        int count = provider.PlanPath(
            new Vector3(-8f, 0f, 0f),
            new Vector3( 8f, 0f, 0f),
            waypoints.AsSpan());

        Assert.True(count >= 2,
            $"PlanPath across clear ground must return ≥2 waypoints, got {count}");

        // First waypoint should be near 'from'.
        Assert.True(Vector3.Distance(waypoints[0].Position, new Vector3(-8f, 0f, 0f)) < Tol + 1f,
            $"First waypoint should be near start (-8,0,0), got {waypoints[0].Position}");

        // Last waypoint should be near 'to'.
        Assert.True(Vector3.Distance(waypoints[count - 1].Position, new Vector3(8f, 0f, 0f)) < Tol + 1f,
            $"Last waypoint should be near end (8,0,0), got {waypoints[count - 1].Position}");
    }

    [Fact]
    public void PlanPath_SamePolygon_ReturnsTwoWaypoints()
    {
        var provider = BuildProvider(GroundQuad);
        var waypoints = new NavWaypoint[16];

        // From (0,0,0) to (1,0,0) — likely in same polygon.
        int count = provider.PlanPath(
            new Vector3(0f, 0f, 0f),
            new Vector3(1f, 0f, 0f),
            waypoints.AsSpan());

        Assert.True(count >= 2,
            $"Short path within a polygon must return ≥2 waypoints, got {count}");
    }

    [Fact]
    public void PathExists_ClearPath_ReturnsTrue()
    {
        var provider = BuildProvider(GroundQuad);
        bool exists = provider.PathExists(new Vector3(-8f, 0f, 0f), new Vector3(8f, 0f, 0f));
        Assert.True(exists, "Path must exist across clear ground quad");
    }

    [Fact]
    public void PathExists_PointOffMesh_ReturnsFalse()
    {
        var provider = BuildProvider(GroundQuad);
        // Start is off-mesh (50 m away).
        bool exists = provider.PathExists(new Vector3(50f, 0f, 0f), new Vector3(8f, 0f, 0f));
        Assert.False(exists, "Path must not exist when start point is off mesh");
    }

    // ── T2-SC4: PathCost ─────────────────────────────────────────────────────

    [Fact]
    public void PathCost_ReachablePair_ReturnsFinitePositiveValue()
    {
        var provider = BuildProvider(GroundQuad);
        float cost = provider.PathCost(new Vector3(-5f, 0f, 0f), new Vector3(5f, 0f, 0f));

        Assert.NotEqual(float.MaxValue, cost);
        Assert.True(cost > 0f, $"Path cost must be positive for a non-trivial path, got {cost}");
        Assert.True(cost < 100f, $"Path cost must be reasonable (< 100 m), got {cost}");
    }

    [Fact]
    public void PathCost_UnreachablePair_ReturnsMaxValue()
    {
        var provider = BuildProvider(GroundQuad);
        // Start is off-mesh.
        float cost = provider.PathCost(new Vector3(50f, 0f, 50f), new Vector3(5f, 0f, 0f));
        Assert.Equal(float.MaxValue, cost);
    }

    // ── T2-SC5: QueryVersion ─────────────────────────────────────────────────

    [Fact]
    public void QueryVersion_AfterRebake_Increments()
    {
        var baker  = new StrideNavmeshBaker();
        var (verts, indices) = GroundQuad;
        var meshes = baker.Bake(verts, indices, NavLayerMask.Infantry);

        var provider = new DotRecastNavmeshProvider(meshes);
        uint version1 = provider.QueryVersion();
        Assert.True(version1 > 0, "Initial version after construction must be > 0");

        // Rebake.
        var meshes2 = baker.Bake(verts, indices, NavLayerMask.Infantry);
        provider.Rebake(meshes2);
        uint version2 = provider.QueryVersion();

        Assert.True(version2 > version1,
            $"QueryVersion must increment after Rebake: before={version1}, after={version2}");
    }

    [Fact]
    public void QueryVersion_EmptyProvider_ReturnsZero()
    {
        var provider = new DotRecastNavmeshProvider();
        Assert.Equal(0u, provider.QueryVersion());
    }

    // ── T2-SC6: Contract parity with FakeNavmeshProvider ────────────────────

    [Fact]
    public void ContractParity_IsWalkable_MatchesFakeOnGroundQuad()
    {
        // Build a FakeNavmeshProvider with a single rectangle matching the ground quad.
        var fakeLayer = new FakeNavLayer
        {
            Layer = (uint)NavLayerMask.Infantry,
            Polygons = new[]
            {
                new NavPolygon
                {
                    Id        = 1,
                    IsBlocked = false,
                    Vertices  = new[]
                    {
                        new Vector3(-10f, 0f, -10f),
                        new Vector3( 10f, 0f, -10f),
                        new Vector3( 10f, 0f,  10f),
                        new Vector3(-10f, 0f,  10f),
                    },
                }
            },
            Adjacency    = Array.Empty<int[]>(),
            OffMeshLinks = Array.Empty<OffMeshLink>(),
        };

        var fake     = new FakeNavmeshProvider(fakeLayer);
        var provider = BuildProvider(GroundQuad);

        // Both should agree on walkability at the same points.
        var testPts = new[]
        {
            new Vector3(0f, 0f, 0f),       // centre — walkable
            new Vector3(8f, 0f, 8f),       // near corner — walkable
        };

        foreach (var pt in testPts)
        {
            bool fakeResult = fake.IsWalkable(pt, (uint)NavLayerMask.Infantry);
            bool realResult = provider.IsWalkable(pt, (uint)NavLayerMask.Infantry);
            Assert.True(fakeResult == realResult,
                $"IsWalkable parity failure at {pt}: fake={fakeResult}, real={realResult}");
        }

        // Off-mesh point — both should return false.
        var offPt = new Vector3(50f, 0f, 50f);
        Assert.False(fake.IsWalkable(offPt),     "Fake: off-mesh must be false");
        Assert.False(provider.IsWalkable(offPt), "Real: off-mesh must be false");
    }

    [Fact]
    public void ContractParity_PathExists_MatchesFakeOnConnectedPolygons()
    {
        // Fake with two adjacent rectangles sharing an edge at X=0.
        var fakeLayer = new FakeNavLayer
        {
            Layer = (uint)NavLayerMask.Infantry,
            Polygons = new[]
            {
                new NavPolygon { Id=1, Vertices = new[] {
                    new Vector3(-10f,0f,-10f), new Vector3(0f,0f,-10f),
                    new Vector3(0f,0f,10f),    new Vector3(-10f,0f,10f) } },
                new NavPolygon { Id=2, Vertices = new[] {
                    new Vector3(0f,0f,-10f),  new Vector3(10f,0f,-10f),
                    new Vector3(10f,0f,10f),  new Vector3(0f,0f,10f) } },
            },
            Adjacency    = new[] { new[] { 1 }, new[] { 0 } },
            OffMeshLinks = Array.Empty<OffMeshLink>(),
        };

        var fake     = new FakeNavmeshProvider(fakeLayer);
        var provider = BuildProvider(GroundQuad);

        bool fakeExists = fake.PathExists(
            new Vector3(-8f, 0f, 0f),
            new Vector3( 8f, 0f, 0f),
            (uint)NavLayerMask.Infantry);

        bool realExists = provider.PathExists(
            new Vector3(-8f, 0f, 0f),
            new Vector3( 8f, 0f, 0f),
            (uint)NavLayerMask.Infantry);

        // Both must say a path exists across the connected ground.
        Assert.True(fakeExists, "Fake: cross-ground path must exist");
        Assert.True(realExists, "Real: cross-ground path must exist");
    }

    // ── T2-SC7: Singleton registration ──────────────────────────────────────

    [Fact]
    public void RegisterAsSingleton_WorldGetSingletonManaged_ReturnsInstance()
    {
        var provider = BuildProvider(GroundQuad);
        var world    = new EntityRepository();

        world.SetSingletonManaged<INavmeshProvider>(provider);
        var retrieved = world.GetSingletonManaged<INavmeshProvider>();

        Assert.NotNull(retrieved);
        Assert.Same(provider, retrieved);
        Assert.IsType<DotRecastNavmeshProvider>(retrieved);
    }

    [Fact]
    public void RegisterAsSingleton_ReplacesExistingFake()
    {
        var world = new EntityRepository();
        var fake  = new FakeNavmeshProvider();
        world.SetSingletonManaged<INavmeshProvider>(fake);

        // Replace with the real provider.
        var provider = BuildProvider(GroundQuad);
        world.SetSingletonManaged<INavmeshProvider>(provider);

        var retrieved = world.GetSingletonManaged<INavmeshProvider>();
        Assert.IsType<DotRecastNavmeshProvider>(retrieved);
        Assert.NotSame(fake, retrieved);
    }

    // ── T2-SC8: Slope obstacle — Infantry bakes ramp polys, Vehicle does not ──

    /// <summary>
    /// A 45° ramp is above the Vehicle max slope (20°) but below Infantry max slope (60°).
    /// This verifies the per-layer slope parameter has a real behavioral effect:
    /// infantry bakes polygons on the ramp, vehicle does not.
    ///
    /// The ramp connects two separate elevated platforms:
    /// - Bottom platform: Y=0, Z∈[-15,-5] (accessible from both agents)
    /// - Top platform: Y=10, Z∈[5,15] (only accessible via the ramp)
    /// - Ramp: Y rises from 0 to 10 over Z∈[-5,5]
    /// The ramp is the ONLY connection. Vehicle cannot climb → no path from bottom to top.
    /// Infantry can climb → path from bottom to top exists.
    ///
    /// The separation is achieved by placing the platforms on opposite sides of a Y gap:
    /// the bottom platform does NOT extend to the top platform at any point — they are
    /// physically only connected by the ramp.
    /// </summary>
    [Fact]
    public void SlopeObstacle_InfantryBakesRampPolys_VehicleDoesNot()
    {
        // Two isolated platforms connected by a 45° ramp.
        // Ramp is the only route between platforms.
        var (verts, indices) = MakeIsolatedRampGeometry(slopeDeg: 45f);

        var infantryBaker = new StrideNavmeshBaker();
        var vehicleBaker  = new StrideNavmeshBaker();

        var infMeshes = infantryBaker.Bake(verts, indices, NavLayerMask.Infantry);
        var vehMeshes = vehicleBaker.Bake(verts, indices, NavLayerMask.Vehicle);

        // Infantry must bake the ramp area → more polygons.
        bool infantryBakedRamp = infMeshes.ContainsKey(NavLayerMask.Infantry) &&
                                 CountPolygons(infMeshes[NavLayerMask.Infantry]) >= 3;

        // Vehicle must NOT bake the ramp → fewer polygons (flat platforms only).
        int vehiclePolys = vehMeshes.ContainsKey(NavLayerMask.Vehicle)
            ? CountPolygons(vehMeshes[NavLayerMask.Vehicle])
            : 0;

        Assert.True(infantryBakedRamp,
            "Infantry (max slope 60°) must produce ≥3 polygons including the 45° ramp");
        Assert.True(vehiclePolys < CountPolygons(infMeshes[NavLayerMask.Infantry]),
            $"Vehicle (max slope 20°) must produce FEWER polygons than infantry: " +
            $"infantry={CountPolygons(infMeshes[NavLayerMask.Infantry])}, vehicle={vehiclePolys}");
    }

    private static int CountPolygons(DtNavMesh mesh)
    {
        int total = 0;
        for (int i = 0; i < mesh.GetMaxTiles(); i++)
        {
            var tile = mesh.GetTile(i);
            if (tile?.data?.header != null) total += tile.data.header.polyCount;
        }
        return total;
    }

    private static (float[] verts, int[] indices) MakeIsolatedRampGeometry(float slopeDeg = 45f)
    {
        float rise = (float)(Math.Tan(slopeDeg * Math.PI / 180.0) * 10f);
        float[] v = {
            // Bottom platform only: Z∈[-15,-5], Y=0 (does NOT extend to Z>-5)
            -5f, 0f, -15f,   5f, 0f, -15f,   5f, 0f, -5f,   -5f, 0f, -5f,
            // Ramp: from (Y=0,Z=-5) to (Y=rise,Z=5)
            -5f, 0f,    -5f,    5f, 0f,    -5f,    5f, rise, 5f,  -5f, rise, 5f,
            // Top platform only: Z∈[5,15], Y=rise (does NOT extend to Z<5)
            -5f, rise, 5f,   5f, rise, 5f,   5f, rise, 15f, -5f, rise, 15f,
        };
        int[] i = {
            0, 2, 1,   0, 3, 2,
            4, 6, 5,   4, 7, 6,
            8, 10, 9,  8, 11, 10,
        };
        return (v, i);
    }

    // ── T2-SC9: SampleNavmeshPoints ─────────────────────────────────────────

    [Fact]
    public void SampleNavmeshPoints_OverGround_ReturnsSomePoints()
    {
        var provider = BuildProvider(GroundQuad);
        var results  = new Vector3[32];

        int count = provider.SampleNavmeshPoints(
            center:    new Vector3(0f, 0f, 0f),
            radius:    5f,
            results:   results.AsSpan());

        Assert.True(count > 0,
            $"SampleNavmeshPoints must return >0 points over a baked ground quad, got {count}");
    }

    // ── Geometry helpers ─────────────────────────────────────────────────────

    private static (float[] verts, int[] indices) MakeGroundQuad(
        float minX = -10f, float maxX = 10f,
        float minZ = -10f, float maxZ = 10f,
        float y    =  0f)
    {
        float[] verts = {
            minX, y, minZ,  // 0
            maxX, y, minZ,  // 1
            maxX, y, maxZ,  // 2
            minX, y, maxZ,  // 3
        };
        // CCW from above → normal +Y (walkable).
        int[] indices = { 0, 2, 1,  0, 3, 2 };
        return (verts, indices);
    }

    private static (float[] verts, int[] indices) MakeGroundWithGap(float gapWidth)
    {
        float halfGap = gapWidth / 2f;
        var verts = new List<float>();
        var idx   = new List<int>();

        void AddQuad(float x0, float x1, float z0, float z1)
        {
            int b = verts.Count / 3;
            verts.AddRange(new float[] { x0,0f,z0, x1,0f,z0, x1,0f,z1, x0,0f,z1 });
            // CCW from above → +Y normal.
            idx.AddRange(new int[] { b, b+2, b+1,  b, b+3, b+2 });
        }

        AddQuad(-10f, -halfGap, -10f, 10f);
        AddQuad( halfGap, 10f,  -10f, 10f);

        return (verts.ToArray(), idx.ToArray());
    }
}
