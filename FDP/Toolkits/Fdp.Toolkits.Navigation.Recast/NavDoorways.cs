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
    public readonly record struct Volume(int Door, float[] Verts, float MinY, float MaxY)
    {
        /// <summary>⭐ CE-4369 — the area the volume marks (<see cref="DoorArea"/> for a doorway, <see cref="WoodsArea"/> for a wood).</summary>
        public int Area { get; init; } = DoorArea;
    }

    /// <summary>
    /// ⭐ <c>CE-4369</c> (L-7, R-4190 T-3, docs/DESIGN_Terrain_World.md §11) — the area id a wood's spans carry: the polygons stay walkable and
    /// the query filter prices them per mover (<see cref="DoorAwareQueryFilter"/>). A doorway's area wins where both overlap (marked after).
    /// </summary>
    public const int WoodsArea = 3;

    /// <summary>⭐ CE-4369 — the woods of <paramref name="world"/> as convex volumes (each forest surface's triangles), in RECAST
    /// coordinates, spanning the ground's whole height range: the bake marks their spans <see cref="WoodsArea"/>.</summary>
    public static IReadOnlyList<Volume> Woods(TerrainWorld world)
    {
        var list = new List<Volume>();
        float lo = (world.Height?.MinZ ?? world.GroundZ) - 2f, hi = (world.Height?.MaxZ ?? world.GroundZ) + 2f;
        foreach (var s in world.Surfaces)
        {
            if (s.Type != TerrainSurfaceType.Forest) continue;
            for (int t = 0; t + 2 < s.Triangles.Length; t += 3)
            {
                var verts = new float[9];
                for (int k = 0; k < 3; k++)
                {
                    var p = s.Polygon[s.Triangles[t + k]];
                    verts[k * 3 + 0] = p.X; verts[k * 3 + 1] = lo; verts[k * 3 + 2] = p.Y;
                }
                list.Add(new Volume(-1, verts, lo, hi) { Area = WoodsArea });
            }
        }
        return list;
    }

    /// <summary>
    /// ⭐⭐ <c>CE-4591</c> (P-4 part 2; 📄 docs/DESIGN_Ground_Patches_And_Road_Blockage.md G-9, D-2 R-4233) — the area id a crater's spans carry: EVERY vehicle keeps
    /// out (<see cref="DoorAwareQueryFilter"/> refuses it to a mover that cannot open doors), infantry crosses at its plain cost. Marked after the woods, before
    /// the doorways.
    /// </summary>
    public const int CraterArea = 4;

    /// <summary>The sides of the polygon a crater's disc is marked with.</summary>
    public const int CraterSides = 12;

    /// <summary>
    /// ⭐ <c>CE-4591</c> — the craters baked into <paramref name="world"/>'s height grid (<see cref="TerrainWorld.HeightCraters"/>) as convex volumes in RECAST
    /// coordinates: a fan of <see cref="CraterSides"/> triangles round each changed disc (rim included), spanning from below its bowl to above its rim.
    /// </summary>
    public static IReadOnlyList<Volume> Craters(TerrainWorld world)
    {
        var craters = world.HeightCraters;
        if (craters == null || craters.Count == 0) return Array.Empty<Volume>();
        var list = new List<Volume>(craters.Count * CraterSides);
        for (int i = 0; i < craters.Count; i++)
        {
            var c = craters[i];
            float o = c.OuterRadius / MathF.Cos(MathF.PI / CraterSides);   // the polygon holds the whole disc
            float ground = world.BareGroundHeightAt(c.Centre.X, c.Centre.Y);
            float lo = ground - c.Depth - 2f, hi = ground + c.Rim + 2f;
            for (int k = 0; k < CraterSides; k++)
            {
                float a0 = 2f * MathF.PI * k / CraterSides, a1 = 2f * MathF.PI * (k + 1) / CraterSides;
                var verts = new float[9]
                {
                    c.Centre.X, lo, c.Centre.Y,
                    c.Centre.X + o * MathF.Cos(a0), lo, c.Centre.Y + o * MathF.Sin(a0),
                    c.Centre.X + o * MathF.Cos(a1), lo, c.Centre.Y + o * MathF.Sin(a1),
                };
                list.Add(new Volume(-1, verts, lo, hi) { Area = CraterArea });
            }
        }
        return list;
    }

    /// <summary>
    /// ⭐ <c>CE-4593</c> (P-6, G-28, D-2) — every OBSTACLE-class ground shape (a ditch, an anti-tank ditch) as a convex volume of <see cref="CraterArea"/> (the area every
    /// vehicle keeps out of, infantry crosses): its quad grown by its banks, spanning below its floor to above the ground.
    /// </summary>
    public static IReadOnlyList<Volume> Ditches(TerrainWorld world)
    {
        List<Volume>? list = null;
        foreach (var s in world.GroundShapes)
        {
            if (!s.Obstacle || s.Polygon.Length != 4) continue;
            var c = (s.Polygon[0] + s.Polygon[2]) * 0.5f;
            float ground = world.BareGroundHeightAt(c.X, c.Y);
            float lo = ground - MathF.Abs(s.Value) - 2f, hi = ground + 2f;
            float grow = 0.6f * MathF.Abs(s.Value) + 0.3f;
            var q = new Vector2[4];
            for (int k = 0; k < 4; k++) { var v = s.Polygon[k] - c; q[k] = s.Polygon[k] + (v.LengthSquared() > 1e-9f ? Vector2.Normalize(v) * grow : Vector2.Zero); }
            foreach (var (a, b, d) in new[] { (q[0], q[1], q[2]), (q[0], q[2], q[3]) })
                (list ??= new List<Volume>()).Add(new Volume(-1, new float[] { a.X, lo, a.Y, b.X, lo, b.Y, d.X, lo, d.Y }, lo, hi) { Area = CraterArea });
        }
        return (IReadOnlyList<Volume>?)list ?? Array.Empty<Volume>();
    }

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
/// ⭐ Buildings Stage 5c (§3j "5c", N1–N3) — the query filter that judges a doorway polygon by its door's state in the CALLER's view
/// (<see cref="DoorStates"/>, R-219): built per query from the table the caller passes, or per layer with the authored states. Infantry: a Locked door is
/// impassable, a Closed one costs <see cref="ClosedDoorPenaltyMetres"/> on entry. Every other layer (vehicles): no doorway polygon.
/// Everything else defers to <see cref="DtQueryDefaultFilter"/>.
/// </summary>
public sealed class DoorAwareQueryFilter : IDtQueryFilter
{
    private const int WoodsAreaId = NavDoorways.WoodsArea;

    /// <summary>N3 — what entering a closed door costs, in metres of walking: a detour shorter than this wins.</summary>
    public const float ClosedDoorPenaltyMetres = 10f;

    // ⭐ R-220 — ONE default filter for every instance: DtQueryDefaultFilter's PassFilter/GetCost only READ its flags and area costs.
    private static readonly DtQueryDefaultFilter Base = new();
    private readonly IReadOnlyDictionary<long, int> _doorPolys;
    private DoorStates? _doors;   // ⛔ written only on a working copy (JudgeBy) — a published filter never changes
    private readonly bool _canOpenDoors;
    private readonly bool _isWorkingCopy;
    private readonly float _woodsCost;   // ⭐ CE-4369 — the cost factor on a woods polygon; ≤ 0 ⇒ the mover may not enter it

    /// <summary>⭐ CE-4369 (R-4190 T-3 / Q-T1 / Q-T2) — what a woods polygon costs each mover, as a factor on its length: infantry a little,
    /// a tracked vehicle a lot; a wheeled vehicle may not enter (0). Tuning values.</summary>
    public const float InfantryWoodsCost = 1.5f, TrackedWoodsCost = 4f, WheeledWoodsCost = 0f;

    /// <param name="doors">the door states to judge by; null = every door open (no terrain doors)</param>
    public DoorAwareQueryFilter(IReadOnlyDictionary<long, int> doorPolys, DoorStates? doors, bool canOpenDoors)
        : this(doorPolys, doors, canOpenDoors, isWorkingCopy: false, canOpenDoors ? InfantryWoodsCost : WheeledWoodsCost) { }

    /// <summary>⭐ CE-4369 — with an explicit woods cost factor (≤ 0 ⇒ woods are closed to this mover).</summary>
    public DoorAwareQueryFilter(IReadOnlyDictionary<long, int> doorPolys, DoorStates? doors, bool canOpenDoors, float woodsCost)
        : this(doorPolys, doors, canOpenDoors, isWorkingCopy: false, woodsCost) { }

    private DoorAwareQueryFilter(IReadOnlyDictionary<long, int> doorPolys, DoorStates? doors, bool canOpenDoors, bool isWorkingCopy, float woodsCost)
    {
        _doorPolys = doorPolys;
        _doors = doors;
        _canOpenDoors = canOpenDoors;
        _isWorkingCopy = isWorkingCopy;
        _woodsCost = woodsCost;
    }

    /// <summary>⭐ CE-4369 — the same doorways and doors, priced for another mover's woods cost.</summary>
    public DoorAwareQueryFilter WithWoodsCost(float woodsCost) => new(_doorPolys, _doors, _canOpenDoors, isWorkingCopy: false, woodsCost);

    /// <summary>The same doorway polygons and agent class, judged by <paramref name="doors"/> instead. ⚠ Allocates — a per-query
    /// caller uses a <see cref="WorkingCopy"/> (R-220).</summary>
    public DoorAwareQueryFilter With(DoorStates? doors) => ReferenceEquals(doors, _doors) ? this : new(_doorPolys, doors, _canOpenDoors, isWorkingCopy: false, _woodsCost);

    /// <summary>⭐ R-220 — a copy whose doors one THREAD re-points per query (<see cref="JudgeBy"/>): the provider keeps one per
    /// thread and layer, so a query judged by its caller's doors allocates nothing.</summary>
    public DoorAwareQueryFilter WorkingCopy() => new(_doorPolys, _doors, _canOpenDoors, isWorkingCopy: true, _woodsCost);

    /// <summary>⭐ R-220 — judge by <paramref name="doors"/> from now on. Only on a <see cref="WorkingCopy"/>, owned by one thread.</summary>
    public DoorAwareQueryFilter JudgeBy(DoorStates? doors)
    {
        if (!_isWorkingCopy) throw new InvalidOperationException("Only a WorkingCopy may be re-pointed — a shared filter never changes.");
        _doors = doors;
        return this;
    }

    /// <summary>How many doorway polygons this filter knows.</summary>
    public int DoorPolyCount => _doorPolys.Count;

    /// <summary>The door index of a doorway polygon, or −1.</summary>
    public int DoorOf(long polyRef) => _doorPolys.TryGetValue(polyRef, out int d) ? d : -1;

    private TerrainDoorState StateOf(int door)
        => _doors != null && door < _doors.Count ? _doors[door] : TerrainDoorState.Closed;   // ⭐ CE-3290 — an unknown door is closed (passable, at its cost)

    public bool PassFilter(long refs, DtMeshTile tile, DtPoly poly)
    {
        if (!Base.PassFilter(refs, tile, poly)) return false;
        if (poly.GetArea() == WoodsAreaId && _woodsCost <= 0f) return false;   // ⭐ CE-4369 — a wheeled vehicle never enters a wood (Q-T1)
        if (poly.GetArea() == NavDoorways.CraterArea && !_canOpenDoors) return false;   // ⭐ CE-4591 (D-2) — no vehicle drives through a crater; infantry crosses
        if (!_doorPolys.TryGetValue(refs, out int door)) return true;
        if (!_canOpenDoors) return false;                                   // N2 — a vehicle never uses a doorway
        return StateOf(door) != TerrainDoorState.Locked;                    // a locked door is a wall
    }

    public float GetCost(RcVec3f pa, RcVec3f pb,
        long prevRef, DtMeshTile prevTile, DtPoly prevPoly,
        long curRef, DtMeshTile curTile, DtPoly curPoly,
        long nextRef, DtMeshTile nextTile, DtPoly nextPoly)
    {
        float cost = Base.GetCost(pa, pb, prevRef, prevTile, prevPoly, curRef, curTile, curPoly, nextRef, nextTile, nextPoly);
        if (curPoly.GetArea() == WoodsAreaId && _woodsCost > 0f) cost *= _woodsCost;   // ⭐ CE-4369 — a wood priced per mover
        if (_doorPolys.TryGetValue(curRef, out int door)
            && !(prevRef != 0 && _doorPolys.TryGetValue(prevRef, out int prevDoor) && prevDoor == door)   // once per door, on entry
            && StateOf(door) == TerrainDoorState.Closed)
            cost += ClosedDoorPenaltyMetres;
        return cost;
    }
}
