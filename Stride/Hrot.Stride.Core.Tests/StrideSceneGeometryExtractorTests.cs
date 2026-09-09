#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using DotRecast.Detour;
using Fdp.Toolkit.Navigation;
using Hrot.Stride.Core;
using Xunit;
using SMath = Stride.Core.Mathematics;

namespace Hrot.Stride.Core.Tests;

/// <summary>
/// Headless tests for <see cref="BoxGeometryHelper"/> (the pure math layer of the
/// scene geometry extractor) and for the PlanPath-around-obstacle integration
/// (BATCH-18, STR-D19).
///
/// <para>
/// <b>What is tested headlessly:</b>
/// <list type="bullet">
///   <item><see cref="BoxGeometryHelper.ExtractBoxTriangles"/> — a box with a known transform
///     produces exactly 12 triangles (6 faces × 2) whose corners match the expected
///     world-space positions; the top face is wound CCW from above (+Y normal = walkable).</item>
///   <item><see cref="BoxGeometryHelper.AabbToBox"/> — AABB fallback from a scale-only
///     world matrix produces 12 triangles centred at the matrix origin.</item>
///   <item>PlanPath-around-obstacle: a synthetic triangle soup (floor + wall obstacle) is
///     baked via <see cref="StrideNavmeshBaker"/> and queried via
///     <see cref="DotRecastNavmeshProvider"/>; the path from south-of-wall to north-of-wall
///     must include a corner that detours outside the direct line, proving the navmesh
///     routes around the obstacle rather than through it.</item>
/// </list>
/// </para>
///
/// <para>
/// <b>Why <c>StrideSceneGeometrySource.TryGetTriangles</c> is not tested here:</b>
/// that method walks a live Stride <c>Scene</c> requiring a running <c>PhysicsProcessor</c> —
/// GPU-only. The math helpers in <see cref="BoxGeometryHelper"/> are pure and fully testable
/// headlessly.
/// </para>
/// </summary>
public sealed class StrideSceneGeometryExtractorTests
{
    // ── ExtractBoxTriangles: vertex/index count ───────────────────────────────

    /// <summary>
    /// A unit box (half-extents all 1) at the world origin must produce exactly
    /// 8 vertices and 12 triangles (6 faces × 2).
    /// </summary>
    [Fact]
    public void ExtractBoxTriangles_UnitBox_Produces8VertsAnd12Tris()
    {
        var verts  = new List<float>();
        var idx    = new List<int>();
        var matrix = SMath.Matrix.Translation(SMath.Vector3.Zero);

        BoxGeometryHelper.ExtractBoxTriangles(matrix, new SMath.Vector3(1f, 1f, 1f), verts, idx);

        Assert.Equal(8 * 3,  verts.Count);   // 8 vertices × 3 floats
        Assert.Equal(12 * 3, idx.Count);     // 12 triangles × 3 indices
    }

    // ── ExtractBoxTriangles: corner positions ─────────────────────────────────

    /// <summary>
    /// Unit box at origin: the 8 corners must be exactly (±1, ±1, ±1).
    /// </summary>
    [Fact]
    public void ExtractBoxTriangles_UnitBoxAtOrigin_CornersAreUnitCube()
    {
        var verts = new List<float>();
        var idx   = new List<int>();
        BoxGeometryHelper.ExtractBoxTriangles(
            SMath.Matrix.Translation(SMath.Vector3.Zero),
            new SMath.Vector3(1f, 1f, 1f),
            verts, idx);

        var corners = new HashSet<(int x, int y, int z)>();
        for (int i = 0; i < 8; i++)
        {
            int rx = (int)MathF.Round(verts[i * 3 + 0]);
            int ry = (int)MathF.Round(verts[i * 3 + 1]);
            int rz = (int)MathF.Round(verts[i * 3 + 2]);
            corners.Add((rx, ry, rz));
        }

        var expected = new HashSet<(int, int, int)>
        {
            (-1,-1,-1), ( 1,-1,-1), ( 1,-1, 1), (-1,-1, 1),
            (-1, 1,-1), ( 1, 1,-1), ( 1, 1, 1), (-1, 1, 1),
        };
        Assert.Equal(expected, corners);
    }

    /// <summary>
    /// Translated box (offset 3,4,5) with half-extents (2,1,3):
    /// all corner X must be in {1,5}, Y in {3,5}, Z in {2,8}.
    /// </summary>
    [Fact]
    public void ExtractBoxTriangles_TranslatedBox_CornersAtCorrectWorldPositions()
    {
        var verts = new List<float>();
        var idx   = new List<int>();
        BoxGeometryHelper.ExtractBoxTriangles(
            SMath.Matrix.Translation(new SMath.Vector3(3f, 4f, 5f)),
            new SMath.Vector3(2f, 1f, 3f),
            verts, idx);

        // X ∈ {1,5} = 3±2, Y ∈ {3,5} = 4±1, Z ∈ {2,8} = 5±3.
        for (int i = 0; i < 8; i++)
        {
            float x = verts[i * 3 + 0];
            float y = verts[i * 3 + 1];
            float z = verts[i * 3 + 2];
            Assert.True(MathF.Abs(MathF.Abs(x - 3f) - 2f) < 0.01f,
                $"Corner {i} X={x:F3} should be 1 or 5 (3±2)");
            Assert.True(MathF.Abs(MathF.Abs(y - 4f) - 1f) < 0.01f,
                $"Corner {i} Y={y:F3} should be 3 or 5 (4±1)");
            Assert.True(MathF.Abs(MathF.Abs(z - 5f) - 3f) < 0.01f,
                $"Corner {i} Z={z:F3} should be 2 or 8 (5±3)");
        }
    }

    // ── ExtractBoxTriangles: top-face winding (+Y normal) ─────────────────────

    /// <summary>
    /// Unit box at origin: at least one triangle must have all three vertices with Y=+1
    /// and its cross-product normal pointing +Y (CCW from above = walkable).
    /// </summary>
    [Fact]
    public void ExtractBoxTriangles_TopFaceHasUpwardNormal()
    {
        var verts = new List<float>();
        var idx   = new List<int>();
        BoxGeometryHelper.ExtractBoxTriangles(
            SMath.Matrix.Translation(SMath.Vector3.Zero),
            new SMath.Vector3(1f, 1f, 1f),
            verts, idx);

        int triCount   = idx.Count / 3;
        bool foundTop  = false;

        for (int t = 0; t < triCount; t++)
        {
            int i0 = idx[t * 3 + 0];
            int i1 = idx[t * 3 + 1];
            int i2 = idx[t * 3 + 2];

            float y0 = verts[i0 * 3 + 1];
            float y1 = verts[i1 * 3 + 1];
            float y2 = verts[i2 * 3 + 1];

            if (MathF.Abs(y0 - 1f) < 0.01f &&
                MathF.Abs(y1 - 1f) < 0.01f &&
                MathF.Abs(y2 - 1f) < 0.01f)
            {
                // Found a candidate top-face triangle. Check normal direction.
                var p0 = new Vector3(verts[i0*3], verts[i0*3+1], verts[i0*3+2]);
                var p1 = new Vector3(verts[i1*3], verts[i1*3+1], verts[i1*3+2]);
                var p2 = new Vector3(verts[i2*3], verts[i2*3+1], verts[i2*3+2]);

                var normal = Vector3.Cross(p1 - p0, p2 - p0);
                Assert.True(normal.Y > 0f,
                    $"Top-face triangle {t} has downward normal Y={normal.Y:F3}; " +
                    "should be CCW from above (winding: 0→1→2 viewed from +Y).");
                foundTop = true;
                break;
            }
        }

        Assert.True(foundTop, "No triangle with all Y=+1 found — top face is missing.");
    }

    // ── ExtractBoxTriangles: index coherence for multiple boxes ───────────────

    /// <summary>
    /// Two sequential box calls must produce 16 vertices (2 × 8) and 24 triangles (2 × 12),
    /// with the second box's indices offset by 8 (no overlap with first box's vertices).
    /// </summary>
    [Fact]
    public void ExtractBoxTriangles_TwoBoxes_IndicesAreNonOverlapping()
    {
        var verts = new List<float>();
        var idx   = new List<int>();
        var m     = SMath.Matrix.Translation(SMath.Vector3.Zero);

        BoxGeometryHelper.ExtractBoxTriangles(m, new SMath.Vector3(1,1,1), verts, idx);
        BoxGeometryHelper.ExtractBoxTriangles(m, new SMath.Vector3(1,1,1), verts, idx);

        Assert.Equal(48, verts.Count);  // 2 × 8 × 3
        Assert.Equal(72, idx.Count);    // 2 × 12 × 3

        // All indices must be in [0, 15].
        foreach (int i in idx)
            Assert.True(i >= 0 && i <= 15, $"Index {i} out of range [0,15]");

        // Second box (triangles 12–23) must use some indices ≥ 8.
        bool secondBoxUsesHighIndices = false;
        for (int t = 12; t < 24 && !secondBoxUsesHighIndices; t++)
            if (idx[t * 3] >= 8) secondBoxUsesHighIndices = true;

        Assert.True(secondBoxUsesHighIndices,
            "Second box indices are all < 8 — base index was not advanced.");
    }

    // ── AabbToBox tests ───────────────────────────────────────────────────────

    /// <summary>
    /// Identity matrix → 8 vertices and 12 triangles (AABB of a unit box at origin).
    /// </summary>
    [Fact]
    public void AabbToBox_IdentityMatrix_Produces12Triangles()
    {
        var verts = new List<float>();
        var idx   = new List<int>();
        BoxGeometryHelper.AabbToBox(SMath.Matrix.Identity, verts, idx);

        Assert.Equal(8 * 3,  verts.Count);
        Assert.Equal(12 * 3, idx.Count);
    }

    /// <summary>
    /// Translation-only matrix (centre 10,20,30): centre-of-mass of the 8 corners must
    /// match the translation.
    /// </summary>
    [Fact]
    public void AabbToBox_TranslationOnly_CornersAreCentredAtTranslation()
    {
        var verts = new List<float>();
        var idx   = new List<int>();
        BoxGeometryHelper.AabbToBox(
            SMath.Matrix.Translation(new SMath.Vector3(10f, 20f, 30f)),
            verts, idx);

        float avgX = 0f, avgY = 0f, avgZ = 0f;
        for (int i = 0; i < 8; i++)
        {
            avgX += verts[i * 3 + 0];
            avgY += verts[i * 3 + 1];
            avgZ += verts[i * 3 + 2];
        }
        avgX /= 8f; avgY /= 8f; avgZ /= 8f;

        Assert.True(MathF.Abs(avgX - 10f) < 0.01f, $"AABB centre X={avgX} expected 10");
        Assert.True(MathF.Abs(avgY - 20f) < 0.01f, $"AABB centre Y={avgY} expected 20");
        Assert.True(MathF.Abs(avgZ - 30f) < 0.01f, $"AABB centre Z={avgZ} expected 30");
    }

    // ── PlanPath-around-obstacle integration ──────────────────────────────────

    /// <summary>
    /// Builds a synthetic scene: a 30×30 m floor at Y=0 plus a wide east-west wall
    /// (10 m wide in X, 0.5 m thick in Z) at Z=5 that blocks the direct N-S route.
    ///
    /// After baking with the Vehicle layer (radius 1.5 m) and querying from (0,0,0)
    /// to (0,0,10), the path must detour: at least one corner must have |X| > 4 m
    /// (outside the wall's half-width of 5 m minus 1.5 m vehicle radius erosion ≈ 3.5 m).
    ///
    /// This proves the navmesh pipeline correctly routes around an obstacle.
    /// </summary>
    [Fact]
    public void PlanPath_WallObstacle_PathDetoursMidpoint()
    {
        // ── Build geometry soup ───────────────────────────────────────────────
        var verts = new List<float>();
        var idx   = new List<int>();

        // Floor: X∈[-15,15], Z∈[-1,20], Y=0. Deliberately large so erosion doesn't cut it.
        AddGroundQuad(verts, idx, -15f, 15f, -1f, 20f, 0f);

        // E-W wall: centre (0,1,5), half-extents (5,1,0.25).
        // This creates a 10 m × 2 m × 0.5 m solid wall at Z=5 blocking X∈[-5,5].
        // Vehicle radius 1.5 m erodes the wall: passes only where |X| > 5+1.5 = 6.5 m.
        // Arena provides 15-6.5 = 8.5 m of open passage on each side → path exists.
        AddBoxToSoup(verts, idx, 0f, 1f, 5f, 5f, 1f, 0.25f);

        var baker  = new StrideNavmeshBaker();
        var meshes = baker.Bake(verts.ToArray(), idx.ToArray(), NavLayerMask.Vehicle);

        Assert.True(meshes.ContainsKey(NavLayerMask.Vehicle),
            "Vehicle navmesh must bake from floor+wall soup");

        // ── Query path ────────────────────────────────────────────────────────
        var provider  = new DotRecastNavmeshProvider(meshes);
        var waypoints = new NavWaypoint[64];

        // Start: (0,0,0) south of wall. Goal: (0,0,10) north of wall.
        int count = provider.PlanPath(
            new Vector3(0f, 0f, 0f),
            new Vector3(0f, 0f, 10f),
            waypoints.AsSpan(),
            layerMask: (uint)NavLayerMask.Vehicle);

        Assert.True(count >= 2,
            $"PlanPath must return ≥2 corners around the wall obstacle, got {count}. " +
            "If 0, the navmesh did not bake a vehicle-accessible route.");

        // ── Detour assertion: at least one corner is off the direct X=0 line ──────
        // The wall spans X∈[-5,5]. After 1.5 m vehicle erosion the clear passage starts
        // at |X| > 6.5 m. A straight path (X≈0) is blocked; detour corners have |X| > 4.
        bool detourFound = false;
        for (int i = 0; i < count; i++)
        {
            if (MathF.Abs(waypoints[i].Position.X) > 4f)
            {
                detourFound = true;
                break;
            }
        }

        Assert.True(detourFound,
            $"PlanPath returned {count} corners but none had |X| > 4 m — " +
            "path went straight through the wall instead of routing around it. " +
            "Corner X values: " + string.Join(", ",
                System.Linq.Enumerable.Range(0, count)
                    .Select(i => waypoints[i].Position.X.ToString("F2"))));
    }

    /// <summary>
    /// Empty provider → PlanPath returns 0 corners. Exercises the F4 demo's "no navmesh"
    /// guard path.
    /// </summary>
    [Fact]
    public void PlanPath_EmptyProvider_ReturnsZeroCorners()
    {
        var provider  = new DotRecastNavmeshProvider();
        var waypoints = new NavWaypoint[16];

        int count = provider.PlanPath(
            new Vector3(0f, 0f, 0f),
            new Vector3(0f, 0f, 10f),
            waypoints.AsSpan());

        Assert.Equal(0, count);
    }

    // ── Private geometry helpers ──────────────────────────────────────────────

    private static void AddGroundQuad(
        List<float> verts, List<int> idx,
        float minX, float maxX, float minZ, float maxZ, float y)
    {
        int b = verts.Count / 3;
        verts.AddRange(new float[]
        {
            minX, y, minZ,
            maxX, y, minZ,
            maxX, y, maxZ,
            minX, y, maxZ,
        });
        // CCW from above → normal +Y (walkable).
        idx.AddRange(new int[] { b, b+2, b+1,  b, b+3, b+2 });
    }

    private static void AddBoxToSoup(
        List<float> verts, List<int> idx,
        float cx, float cy, float cz,
        float hx, float hy, float hz)
    {
        var matrix = SMath.Matrix.Translation(new SMath.Vector3(cx, cy, cz));
        BoxGeometryHelper.ExtractBoxTriangles(
            matrix, new SMath.Vector3(hx, hy, hz), verts, idx);
    }
}
