using System;
using System.Collections.Generic;
using System.Numerics;
using DotRecast.Core.Numerics;
using Fdp.Toolkit.Terrain;

namespace Fdp.Toolkit.Navigation.Recast;

/// <summary>
/// ⭐ <c>CE-4366</c> (L-4, docs/DESIGN_Scale_Rig.md §5) — where the navmesh bake gets a tile's triangles: in Recast space (x east, y up,
/// z north), wound as the baker expects. <see cref="Gather"/> may return MORE than the tile needs (the baker keeps exactly what the
/// whole-mesh binning kept) but must return them in the source's one fixed order, so a tile's content — and its cache key — never
/// depends on how it was asked for.
/// </summary>
public interface ITileTriangles
{
    /// <summary>The extent of every vertex (the tile grid is laid over it); false when there is no triangle at all.</summary>
    bool TryGetBounds(out RcVec3f min, out RcVec3f max);

    /// <summary>Appends the triangles that can reach the x/z rectangle [x0, z0]–[x1, z1] (vertices xyz, three indices per triangle).</summary>
    void Gather(float x0, float z0, float x1, float z1, List<float> verts, List<int> indices);
}

/// <summary>A triangle soup in memory (the small inputs: tests, the flat harness ground) — binned once on the tile grid.</summary>
public sealed class ArrayTileTriangles : ITileTriangles
{
    private readonly float[] _v;
    private readonly int[] _i;
    private readonly float _bin;
    private readonly Dictionary<(int, int), List<int>> _bins = new();

    public ArrayTileTriangles(float[] verts, int[] indices, float binMetres)
    {
        _v = verts ?? throw new ArgumentNullException(nameof(verts));
        _i = indices ?? throw new ArgumentNullException(nameof(indices));
        _bin = binMetres;
        for (int t = 0; t + 2 < _i.Length; t += 3)
        {
            Box(t, out float x0, out float z0, out float x1, out float z1);
            for (int z = (int)MathF.Floor(z0 / _bin); z <= (int)MathF.Floor(z1 / _bin); z++)
                for (int x = (int)MathF.Floor(x0 / _bin); x <= (int)MathF.Floor(x1 / _bin); x++)
                {
                    if (!_bins.TryGetValue((x, z), out var l)) _bins[(x, z)] = l = new List<int>();
                    l.Add(t);
                }
        }
    }

    private void Box(int t, out float x0, out float z0, out float x1, out float z1)
    {
        x0 = z0 = float.MaxValue; x1 = z1 = float.MinValue;
        for (int k = 0; k < 3; k++)
        {
            int b = _i[t + k] * 3;
            x0 = MathF.Min(x0, _v[b]); x1 = MathF.Max(x1, _v[b]);
            z0 = MathF.Min(z0, _v[b + 2]); z1 = MathF.Max(z1, _v[b + 2]);
        }
    }

    public bool TryGetBounds(out RcVec3f min, out RcVec3f max)
    {
        // every vertex, as RcSampleInputGeomProvider measured it for the whole-mesh bake
        min = new RcVec3f(float.MaxValue, float.MaxValue, float.MaxValue);
        max = new RcVec3f(float.MinValue, float.MinValue, float.MinValue);
        for (int k = 0; k + 2 < _v.Length; k += 3)
        {
            min = RcVec3f.Min(min, new RcVec3f(_v[k], _v[k + 1], _v[k + 2]));
            max = RcVec3f.Max(max, new RcVec3f(_v[k], _v[k + 1], _v[k + 2]));
        }
        return _i.Length > 0;
    }

    public void Gather(float x0, float z0, float x1, float z1, List<float> verts, List<int> indices)
    {
        var set = new SortedSet<int>();
        for (int z = (int)MathF.Floor(z0 / _bin) - 1; z <= (int)MathF.Floor(z1 / _bin) + 1; z++)
            for (int x = (int)MathF.Floor(x0 / _bin) - 1; x <= (int)MathF.Floor(x1 / _bin) + 1; x++)
                if (_bins.TryGetValue((x, z), out var l)) foreach (int t in l) set.Add(t);
        foreach (int t in set)
            for (int k = 0; k < 3; k++)
            {
                int b = _i[t + k] * 3;
                indices.Add(verts.Count / 3);
                verts.Add(_v[b]); verts.Add(_v[b + 1]); verts.Add(_v[b + 2]);
            }
    }
}

/// <summary>
/// A terrain world as a tile source: each rectangle built on demand by <see cref="TerrainWorldMesh.BuildRegion"/> (the same pieces, the
/// same order as the whole soup) and swizzled into Recast space as <see cref="TerrainWorldGeometrySource"/> does.
/// </summary>
public sealed class TerrainTileTriangles : ITileTriangles
{
    private readonly TerrainWorld _world;
    public TerrainTileTriangles(TerrainWorld world) => _world = world ?? throw new ArgumentNullException(nameof(world));

    public bool TryGetBounds(out RcVec3f min, out RcVec3f max)
    {
        var (gmin, gmax) = TerrainWorldMesh.SoupExtent(_world, out float zLo, out float zHi, out bool any);
        min = new RcVec3f(gmin.X, zLo, gmin.Y);
        max = new RcVec3f(gmax.X, zHi, gmax.Y);
        return any;
    }

    public void Gather(float x0, float z0, float x1, float z1, List<float> verts, List<int> indices)
    {
        var v = new List<Vector3>(); var t = new List<int>();
        TerrainWorldMesh.BuildRegion(_world, new Vector2(x0, z0), new Vector2(x1, z1), v, t);
        int baseV = verts.Count / 3;
        foreach (var p in v) { verts.Add(p.X); verts.Add(p.Z); verts.Add(p.Y); }   // east, up, north
        for (int k = 0; k + 2 < t.Count; k += 3)
        {
            indices.Add(baseV + t[k]);
            indices.Add(baseV + t[k + 2]);   // re-wind: the swizzle mirrored the triangle
            indices.Add(baseV + t[k + 1]);
        }
    }
}
