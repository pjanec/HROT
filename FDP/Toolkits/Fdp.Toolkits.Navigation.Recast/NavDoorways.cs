using System;
using System.Collections.Generic;
using System.Numerics;
using DotRecast.Core.Numerics;
using DotRecast.Detour;
using Fdp.Toolkit.Terrain;

namespace Fdp.Toolkit.Navigation.Recast;

/// <summary>
/// ⭐ Buildings Stage 5c (📄 docs/DESIGN_Building_Interiors.md §3j "5c") — the doorways of a <see cref="TerrainWorld"/> as navmesh
/// convex volumes. The navmesh is baked with every door OPEN (§3a); each doorway's spans are marked with <see cref="DoorArea"/>, so
/// its passage becomes polygons of their own that the query filter (<see cref="DoorAwareQueryFilter"/>) can recognise and judge by
/// the door's LIVE state — nothing is written into the mesh after the bake (CE-2122: it is shared read-only by two threads).
/// </summary>
public static class NavDoorways
{
    /// <summary>The Recast area id of a doorway's polygons (the ordinary walkable area is 63).</summary>
    public const int DoorArea = 2;

    /// <summary>How far the marked box reaches beyond the wall on each side (≥ the infantry radius, so the passage is covered).</summary>
    public const float ReachMetres = 0.4f;

    /// <summary>The marked box's height above the sill (a person's height, with margin).</summary>
    public const float HeightMetres = 2.2f;

    /// <summary>One doorway: its door index in <see cref="TerrainWorld.Doors"/> and its box in RECAST space (Y-up).</summary>
    public readonly record struct Volume(int Door, float[] Verts, float MinY, float MaxY);

    /// <summary>The doorway boxes of <paramref name="world"/>, in RECAST coordinates (x east, y up, z north).</summary>
    public static IReadOnlyList<Volume> For(TerrainWorld world)
    {
        var list = new List<Volume>(world.Doors.Count);
        for (int i = 0; i < world.Doors.Count; i++)
        {
            var d = world.Doors[i];
            var panel = world.Panels[d.Panel];
            var o = panel.Openings[d.Opening];
            var dir = panel.B - panel.A;
            float len = dir.Length();
            dir = len > 0f ? dir / len : Vector2.UnitX;
            var n = new Vector2(-dir.Y, dir.X) * (panel.Thickness * 0.5f + ReachMetres);
            var p0 = panel.A + dir * o.At;
            var p1 = panel.A + dir * (o.At + o.Width);
            var fp = new[] { p0 - n, p1 - n, p1 + n, p0 + n };
            var verts = new float[fp.Length * 3];
            for (int k = 0; k < fp.Length; k++)
            {
                verts[k * 3 + 0] = fp[k].X;      // east
                verts[k * 3 + 1] = o.SillZ;      // up (ignored by the marking, kept for a valid triple)
                verts[k * 3 + 2] = fp[k].Y;      // north
            }
            list.Add(new Volume(i, verts, o.SillZ - 0.5f, o.SillZ + HeightMetres));
        }
        return list;
    }

    /// <summary>
    /// The polygons of <paramref name="mesh"/> that carry <see cref="DoorArea"/>, each assigned to the doorway whose box contains its
    /// centre. Built once after the bake; read-only afterwards (safe from every query thread).
    /// </summary>
    public static IReadOnlyDictionary<long, int> DoorPolys(DtNavMesh mesh, IReadOnlyList<Volume> doorways)
    {
        var map = new Dictionary<long, int>();
        if (doorways.Count == 0) return map;
        for (int t = 0; t < mesh.GetMaxTiles(); t++)
        {
            var tile = mesh.GetTile(t);
            if (tile?.data?.header == null) continue;
            long baseRef = mesh.GetPolyRefBase(tile);
            for (int p = 0; p < tile.data.header.polyCount; p++)
            {
                var poly = tile.data.polys[p];
                if (poly.GetArea() != DoorArea) continue;
                long r = baseRef | (long)p;
                var c = mesh.GetPolyCenter(r);
                int best = -1; float bestD = float.MaxValue;
                foreach (var v in doorways)
                {
                    if (c.Y < v.MinY - 0.5f || c.Y > v.MaxY) continue;
                    float cx = 0, cz = 0;
                    for (int k = 0; k < 4; k++) { cx += v.Verts[k * 3]; cz += v.Verts[k * 3 + 2]; }
                    float dx = c.X - cx / 4f, dz = c.Z - cz / 4f, dd = dx * dx + dz * dz;
                    if (dd < bestD) { bestD = dd; best = v.Door; }
                }
                if (best >= 0) map[r] = best;
            }
        }
        return map;
    }
}

/// <summary>
/// ⭐ Buildings Stage 5c (§3j "5c", N1–N3) — the per-layer query filter that judges a doorway polygon by its door's LIVE state, read
/// from <see cref="TerrainWorld.DoorState"/> (a <c>Volatile</c> byte the door mirror keeps current, 5b). Infantry: a Locked door is
/// impassable, a Closed one costs <see cref="ClosedDoorPenaltyMetres"/> on entry. Every other layer (vehicles): no doorway polygon.
/// Everything else defers to <see cref="DtQueryDefaultFilter"/>.
/// </summary>
public sealed class DoorAwareQueryFilter : IDtQueryFilter
{
    /// <summary>N3 — what entering a closed door costs, in metres of walking: a detour shorter than this wins.</summary>
    public const float ClosedDoorPenaltyMetres = 10f;

    private readonly DtQueryDefaultFilter _base = new();
    private readonly IReadOnlyDictionary<long, int> _doorPolys;
    private readonly TerrainWorld? _world;
    private readonly bool _canOpenDoors;

    public DoorAwareQueryFilter(IReadOnlyDictionary<long, int> doorPolys, TerrainWorld? world, bool canOpenDoors)
    {
        _doorPolys = doorPolys;
        _world = world;
        _canOpenDoors = canOpenDoors;
    }

    /// <summary>How many doorway polygons this filter knows.</summary>
    public int DoorPolyCount => _doorPolys.Count;

    /// <summary>The door index of a doorway polygon, or −1.</summary>
    public int DoorOf(long polyRef) => _doorPolys.TryGetValue(polyRef, out int d) ? d : -1;

    private TerrainDoorState StateOf(int door)
        => _world != null && door < _world.Doors.Count ? _world.DoorState(door) : TerrainDoorState.Open;

    public bool PassFilter(long refs, DtMeshTile tile, DtPoly poly)
    {
        if (!_base.PassFilter(refs, tile, poly)) return false;
        if (!_doorPolys.TryGetValue(refs, out int door)) return true;
        if (!_canOpenDoors) return false;                                   // N2 — a vehicle never uses a doorway
        return StateOf(door) != TerrainDoorState.Locked;                    // a locked door is a wall
    }

    public float GetCost(RcVec3f pa, RcVec3f pb,
        long prevRef, DtMeshTile prevTile, DtPoly prevPoly,
        long curRef, DtMeshTile curTile, DtPoly curPoly,
        long nextRef, DtMeshTile nextTile, DtPoly nextPoly)
    {
        float cost = _base.GetCost(pa, pb, prevRef, prevTile, prevPoly, curRef, curTile, curPoly, nextRef, nextTile, nextPoly);
        if (_doorPolys.TryGetValue(curRef, out int door)
            && !(prevRef != 0 && _doorPolys.TryGetValue(prevRef, out int prevDoor) && prevDoor == door)   // once per door, on entry
            && StateOf(door) == TerrainDoorState.Closed)
            cost += ClosedDoorPenaltyMetres;
        return cost;
    }
}
