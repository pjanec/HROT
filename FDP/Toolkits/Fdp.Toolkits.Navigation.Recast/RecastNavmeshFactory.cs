using System.Numerics;
using Fdp.Core;
using Fdp.Toolkit.Terrain;

namespace Fdp.Toolkit.Navigation.Recast;

/// <summary>
/// ⭐ <b>The terrain world as Recast input</b> — <see cref="TerrainWorldMesh"/> (engine, Z-up) swizzled to Recast's
/// Y-up and re-wound so walkable faces point +Y (docs/DESIGN_Terrain_World.md §3, W7: the conversion lives only
/// INSIDE the Recast implementation).
/// <para>⚠ The swizzle <c>(x, y, z) → (x, z, y)</c> is a reflection, so it flips handedness: an engine triangle
/// wound counter-clockwise seen from above would face DOWN in Recast. Every triangle is therefore re-wound.</para>
/// </summary>
public sealed class TerrainWorldGeometrySource : ISceneGeometrySource
{
    private readonly TerrainWorld _world;
    private readonly float _cellSize;

    /// <param name="cellSize">Ground grid cell size; 0 ⇒ <see cref="TerrainWorldMesh.DefaultCellSize"/>.</param>
    public TerrainWorldGeometrySource(TerrainWorld world, float cellSize = 0f)
    {
        _world = world ?? throw new ArgumentNullException(nameof(world));
        _cellSize = cellSize;
    }

    public bool TryGetTriangles(out float[] verts, out int[] indices)
    {
        TerrainWorldMesh.Build(_world, out Vector3[] v, out int[] i, _cellSize);
        verts = new float[v.Length * 3];
        for (int k = 0; k < v.Length; k++)
        {
            verts[(k * 3) + 0] = v[k].X;   // east
            verts[(k * 3) + 1] = v[k].Z;   // up
            verts[(k * 3) + 2] = v[k].Y;   // north
        }
        indices = new int[i.Length];
        for (int t = 0; t + 2 < i.Length; t += 3)
        {
            indices[t]     = i[t];
            indices[t + 1] = i[t + 2];      // re-wind: the swizzle mirrored the triangle
            indices[t + 2] = i[t + 1];
        }
        return indices.Length > 0;
    }
}

/// <summary>
/// ⭐⭐ <b>W6 — the DotRecast navmesh for a terrain world</b> (docs/DESIGN_Terrain_World.md §4.1). Called OFF the main
/// thread from <c>TerrainResidency.Prepare</c>; the returned provider is published into the node's
/// <see cref="SwitchableNavmeshProvider"/> at commit.
/// <para>⚠ One tile per layer, at the baker's 0.3 m cell — right for the shipped test towns (≤ 400 m); a tiled bake
/// is the follow-up for larger worlds (docs/DESIGN_Terrain_World.md §8).</para>
/// </summary>
public sealed class RecastNavmeshFactory : INavmeshFactory
{
    /// <summary>The movement layers baked (default: infantry + vehicles).</summary>
    public NavLayerMask Layers { get; init; } = NavLayerMask.Infantry | NavLayerMask.Vehicle;

    public INavmeshProvider? Build(TerrainWorld world)
    {
        if (!new TerrainWorldGeometrySource(world).TryGetTriangles(out var verts, out var indices)) return null;
        // ⭐ Stage 5c — the doorways become their own polygons, judged at query time by the door's live state (§3j "5c").
        var doorways = NavDoorways.For(world);
        var meshes = new RecastNavmeshBaker().Bake(verts, indices, Layers, doorways);
        return meshes.Count == 0 ? null : new DotRecastNavmeshProvider(meshes, world, doorways);
    }
}
