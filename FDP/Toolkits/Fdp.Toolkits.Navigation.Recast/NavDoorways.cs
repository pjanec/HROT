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

    // ── ⭐⭐ CE-4624 MS-T3 (DESIGN_Minefields.md MR-3, MR-20) — MINEFIELDS: composite areas, stamped after the woods and craters ─────────────────────────────────────────────────────

    /// <summary>
    /// The area id of a minefield's spans over plain ground (= <see cref="Minefields.NavArea"/>). ⚠ ONE area per span and a later stamp wins, so a field over a wood or a crater would
    /// have erased what is underneath (wheels into the wood, vehicles through the crater): the field is stamped as COMPOSITES — <see cref="MinefieldWoodsArea"/> over a wood,
    /// <see cref="MinefieldCraterArea"/> over a crater or ditch — cut by GEOMETRY (the field's triangles clipped against the wood's and the crater's volumes), so every area test
    /// that asks "is this a wood / a crater?" must ask <see cref="IsWoods"/> / <see cref="IsCrater"/>, never <c>== WoodsArea</c>.
    /// </summary>
    public const int MinefieldArea = Fdp.Toolkit.Terrain.Minefields.NavArea;   // 5
    /// <summary>The composite area of a minefield over a wood (free ids 6, 7 measured: only 2, 3, 4, 5 and Recast's 63 are used).</summary>
    public const int MinefieldWoodsArea = 6;
    /// <summary>The composite area of a minefield over a crater or a ditch.</summary>
    public const int MinefieldCraterArea = 7;
    /// <summary>Recast's walkable ground (<c>RC_WALKABLE_AREA</c>) — what a lane restores where nothing else lies underneath.</summary>
    public const int GroundArea = 63;

    /// <summary>True for the plain wood area and its minefield composite.</summary>
    public static bool IsWoods(int area) => area == WoodsArea || area == MinefieldWoodsArea;
    /// <summary>True for the plain crater area and its minefield composite.</summary>
    public static bool IsCrater(int area) => area == CraterArea || area == MinefieldCraterArea;
    /// <summary>True for a minefield area (plain or composite).</summary>
    public static bool IsMinefield(int area) => area == MinefieldArea || area == MinefieldWoodsArea || area == MinefieldCraterArea;

    /// <summary>
    /// ⭐ <c>CE-4624</c> MS-T3 — the minefields of <paramref name="world"/> as convex volumes in RECAST coordinates, to be stamped AFTER <paramref name="woods"/> and <paramref name="craters"/> and before the doorways:
    /// ① each field's ear-clipped triangles as <see cref="MinefieldArea"/>; ② each triangle ∩ each wood triangle as <see cref="MinefieldWoodsArea"/>, ∩ each crater / ditch piece as <see cref="MinefieldCraterArea"/>
    /// (a crater wins over a wood, as the plain stamps do); ③ each LANE (a strip, authored or cleared at runtime) ∩ the field's triangles restoring what lies underneath — <see cref="GroundArea"/>, then the wood
    /// and the crater pieces again — so a lane is a passage in the mesh whose polygons the filter can price for who may USE it. Spans the ground's whole height range, like the woods.
    /// </summary>
    public static IReadOnlyList<Volume> Minefields(TerrainWorld world, IReadOnlyList<Volume> woods, IReadOnlyList<Volume> craters)
    {
        if (world.Minefields.Count == 0) return Array.Empty<Volume>();
        float lo = (world.Height?.MinZ ?? world.GroundZ) - 2f, hi = (world.Height?.MaxZ ?? world.GroundZ) + 2f;
        var list = new List<Volume>();
        var woodTris = TrianglesOf(woods);
        var craterTris = TrianglesOf(craters);
        var fieldTris = new List<Vector2[]>[world.Minefields.Count];
        for (int f = 0; f < fieldTris.Length; f++)
        {
            var field = world.Minefields[f];
            var tris = fieldTris[f] = new List<Vector2[]>();
            var idx = PolygonMath.Triangulate(field.Outline);
            for (int t = 0; t + 2 < idx.Length; t += 3)
                tris.Add(new[] { field.Outline[idx[t]], field.Outline[idx[t + 1]], field.Outline[idx[t + 2]] });
            AddClearanceRing(field.Outline, MinePattern.RouteClearance(field.Params), tris);   // ⭐ CE-4655 — the ring outside: a route round the field keeps its clearance
            foreach (var tri in tris) list.Add(Vol(tri, lo, hi, MinefieldArea));
        }
        for (int f = 0; f < fieldTris.Length; f++)
            foreach (var tri in fieldTris[f])
                foreach (var w in woodTris) if (Clip(tri, w) is { } p) list.Add(Vol(p, lo, hi, MinefieldWoodsArea));
        for (int f = 0; f < fieldTris.Length; f++)
            foreach (var tri in fieldTris[f])
                foreach (var c in craterTris) if (Clip(tri, c) is { } p) list.Add(Vol(p, lo, hi, MinefieldCraterArea));
        for (int f = 0; f < fieldTris.Length; f++)
            foreach (var lane in world.Minefields[f].Lanes)
            {
                if (!(lane.Width > 0f)) continue;
                var strip = LaneStrip(lane, MinePattern.RouteClearance(world.Minefields[f].Params));   // ⭐ CE-4655 — on through the ring at both ends
                foreach (var tri in fieldTris[f])
                {
                    if (Clip(tri, strip) is not { } piece) continue;
                    list.Add(Vol(piece, lo, hi, GroundArea));
                    foreach (var w in woodTris) if (Clip(piece, w) is { } p) list.Add(Vol(p, lo, hi, WoodsArea));
                    foreach (var c in craterTris) if (Clip(piece, c) is { } p) list.Add(Vol(p, lo, hi, CraterArea));
                }
            }
        return list;
    }

    private static Volume Vol(Vector2[] poly, float lo, float hi, int area)
    {
        var verts = new float[poly.Length * 3];
        for (int i = 0; i < poly.Length; i++) { verts[i * 3] = poly[i].X; verts[i * 3 + 1] = lo; verts[i * 3 + 2] = poly[i].Y; }
        return new Volume(-1, verts, lo, hi) { Area = area };
    }

    /// <summary>The XZ footprints (engine x, y) of volumes' triangles / convex polygons.</summary>
    private static List<Vector2[]> TrianglesOf(IReadOnlyList<Volume> volumes)
    {
        var list = new List<Vector2[]>(volumes.Count);
        foreach (var v in volumes)
        {
            int n = v.Verts.Length / 3;
            var p = new Vector2[n];
            for (int i = 0; i < n; i++) p[i] = new Vector2(v.Verts[i * 3], v.Verts[i * 3 + 2]);
            list.Add(p);
        }
        return list;
    }

    /// <summary>The strip a lane covers: its segment grown to its width on both sides (a convex quad).</summary>
    public static Vector2[] LaneStrip(MinefieldLane lane) => LaneStrip(lane, 0f);

    /// <summary>⭐ CE-4655 — the strip run on by <paramref name="extend"/> m past both ends (through the field's clearance ring).</summary>
    public static Vector2[] LaneStrip(MinefieldLane lane, float extend)
    {
        var d = lane.B - lane.A; float len = d.Length();
        var dir = len > 1e-4f ? d / len : Vector2.UnitX;
        var n = new Vector2(-dir.Y, dir.X) * (lane.Width * 0.5f);
        var a = lane.A - dir * extend; var b = lane.B + dir * extend;
        return new[] { a - n, b - n, b + n, a + n };
    }

    /// <summary>
    /// ⭐ CE-4655 — the field's CLEARANCE RING as convex pieces (each a quad): per outline edge the band <paramref name="clearance"/> m outside it, and per vertex the
    /// parallelogram between its two edges' outward normals (fills a convex corner; at a reflex one it lies over the field, the same area). Stamped as the field.
    /// </summary>
    public static void AddClearanceRing(Vector2[] outline, float clearance, List<Vector2[]> into)
    {
        int n = outline.Length;
        if (n < 3 || !(clearance > 0f)) return;
        float area2 = 0f;
        for (int i = 0, j = n - 1; i < n; j = i++) area2 += outline[j].X * outline[i].Y - outline[i].X * outline[j].Y;
        float side = area2 >= 0f ? 1f : -1f;   // counter-clockwise ⇒ the outward normal of a→b is (dy, −dx)
        Vector2 Out(Vector2 a, Vector2 b)
        {
            var d = b - a; float len = d.Length();
            return len < 1e-5f ? Vector2.Zero : new Vector2(d.Y, -d.X) / len * side * clearance;
        }
        for (int i = 0; i < n; i++)
        {
            var a = outline[i]; var b = outline[(i + 1) % n];
            var o = Out(a, b);
            if (o == Vector2.Zero) continue;
            into.Add(new[] { a, b, b + o, a + o });
            var prev = outline[(i - 1 + n) % n];
            var op = Out(prev, a);
            if (op != Vector2.Zero) into.Add(new[] { a, a + op, a + op + o, a + o });
        }
    }

    /// <summary>
    /// The intersection of two CONVEX polygons (Sutherland–Hodgman, either winding), or null when it is empty or a sliver (area &lt; 0.05 m²).
    /// </summary>
    public static Vector2[]? Clip(Vector2[] subject, Vector2[] clip)
    {
        // quick reject on the boxes
        float sx0 = float.MaxValue, sy0 = float.MaxValue, sx1 = float.MinValue, sy1 = float.MinValue, cx0 = float.MaxValue, cy0 = float.MaxValue, cx1 = float.MinValue, cy1 = float.MinValue;
        foreach (var p in subject) { sx0 = MathF.Min(sx0, p.X); sx1 = MathF.Max(sx1, p.X); sy0 = MathF.Min(sy0, p.Y); sy1 = MathF.Max(sy1, p.Y); }
        foreach (var p in clip) { cx0 = MathF.Min(cx0, p.X); cx1 = MathF.Max(cx1, p.X); cy0 = MathF.Min(cy0, p.Y); cy1 = MathF.Max(cy1, p.Y); }
        if (sx0 > cx1 || sx1 < cx0 || sy0 > cy1 || sy1 < cy0) return null;

        bool ccw = PolygonMath.SignedArea2(clip) > 0f;
        var output = new List<Vector2>(subject);
        for (int e = 0; e < clip.Length && output.Count > 0; e++)
        {
            var a = clip[e]; var b = clip[(e + 1) % clip.Length];
            if (!ccw) (a, b) = (b, a);
            var input = output; output = new List<Vector2>(input.Count + 2);
            for (int i = 0; i < input.Count; i++)
            {
                var cur = input[i]; var prev = input[(i + input.Count - 1) % input.Count];
                float sCur = Side(a, b, cur), sPrev = Side(a, b, prev);
                if (sCur >= 0f)
                {
                    if (sPrev < 0f) output.Add(Cross(a, b, prev, cur));
                    output.Add(cur);
                }
                else if (sPrev >= 0f) output.Add(Cross(a, b, prev, cur));
            }
        }
        if (output.Count < 3 || MathF.Abs(PolygonMath.SignedArea2(output)) < 0.1f) return null;
        return output.ToArray();

        static float Side(Vector2 a, Vector2 b, Vector2 p) => (b.X - a.X) * (p.Y - a.Y) - (b.Y - a.Y) * (p.X - a.X);
        static Vector2 Cross(Vector2 a, Vector2 b, Vector2 p, Vector2 q)
        {
            float sp = Side(a, b, p), sq = Side(a, b, q), t = sp / (sp - sq);
            return p + (q - p) * t;
        }
    }

    /// <summary>
    /// ⭐ <c>CE-4624</c> MS-T3 (MR-3, KR-14) — the polygons of <paramref name="mesh"/> that belong to a minefield: a polygon with a minefield area (<see cref="IsMinefield"/>) or lying in a lane strip of a field it is inside,
    /// each with its field, its lane (−1 = the field proper) and the knowledge cells it covers. Built once after the bake (the <see cref="DoorPolys"/> precedent), read-only afterwards.
    /// </summary>
    public static MinefieldPolyMap MinefieldPolys(DtNavMesh mesh, TerrainWorld world)
    {
        var fields = world.Minefields;
        if (fields.Count == 0) return MinefieldPolyMap.Empty;
        var map = new Dictionary<long, MinePoly>();
        var pts = new List<Vector2>(8);
        for (int t = 0; t < mesh.GetMaxTiles(); t++)
        {
            var tile = mesh.GetTile(t);
            if (tile?.data?.header == null) continue;
            var data = tile.data;
            long baseRef = mesh.GetPolyRefBase(tile);
            for (int p = 0; p < data.header.polyCount; p++)
            {
                var poly = data.polys[p];
                if (poly.GetPolyType() == DtPolyTypes.DT_POLYTYPE_OFFMESH_CONNECTION) continue;
                pts.Clear();
                var c = Vector2.Zero;
                for (int j = 0; j < poly.vertCount; j++)
                {
                    int v = poly.verts[j] * 3;
                    var q = new Vector2(data.verts[v], data.verts[v + 2]);   // engine x, y = Recast x, z
                    pts.Add(q); c += q;
                }
                if (pts.Count < 3) continue;
                c /= pts.Count;
                bool mine = IsMinefield(poly.GetArea());
                int fi = -1;
                for (int f = 0; f < fields.Count; f++) if (fields[f].Contains(c)) { fi = f; break; }
                if (fi < 0)
                {
                    if (!mine) continue;
                    // a thin polygon along the outline can have its centre just outside it: take the field whose box (grown 2 m) holds the centre
                    for (int f = 0; f < fields.Count; f++)
                    {
                        float g = MinePattern.RouteClearance(fields[f].Params) + 2f;   // ⭐ CE-4655 — a ring polygon lies up to the clearance outside the outline
                        if (c.X >= fields[f].Min.X - g && c.X <= fields[f].Max.X + g && c.Y >= fields[f].Min.Y - g && c.Y <= fields[f].Max.Y + g) { fi = f; break; }
                    }
                    if (fi < 0) continue;
                }
                int lane = fields[fi].LaneAt(c, 0.25f, MinePattern.RouteClearance(fields[fi].Params));   // ⭐ CE-4655 — a lane runs on through the ring
                if (!mine && lane < 0) continue;
                map[baseRef | (long)p] = new MinePoly { Field = fi, Lane = lane, Cells = CellsOf(fields[fi], pts, c) };
            }
        }
        return map.Count == 0 ? MinefieldPolyMap.Empty : new MinefieldPolyMap(fields, map);
    }

    /// <summary>The knowledge cells a (convex) polygon touches: those holding a vertex, an edge midpoint or the centre, and every cell whose centre lies inside it.</summary>
    private static int[] CellsOf(TerrainMinefield field, List<Vector2> poly, Vector2 centre)
    {
        var g = field.Grid;
        var cells = new SortedSet<int>();
        void Add(Vector2 p)
        {
            MinefieldKnowledgeOps.CellOf(g, p, out int col, out int row);
            col = Math.Clamp(col, 0, g.Cols - 1); row = Math.Clamp(row, 0, g.Rows - 1);
            cells.Add(row * MinefieldKnowledge.MaxCols + col);
        }
        Add(centre);
        float x0 = float.MaxValue, y0 = float.MaxValue, x1 = float.MinValue, y1 = float.MinValue;
        for (int i = 0; i < poly.Count; i++)
        {
            Add(poly[i]); Add((poly[i] + poly[(i + 1) % poly.Count]) * 0.5f);
            x0 = MathF.Min(x0, poly[i].X); x1 = MathF.Max(x1, poly[i].X); y0 = MathF.Min(y0, poly[i].Y); y1 = MathF.Max(y1, poly[i].Y);
        }
        // every cell whose centre is inside: scan the cells of the box's corners in the field frame
        var corners = new[] { new Vector2(x0, y0), new Vector2(x1, y0), new Vector2(x1, y1), new Vector2(x0, y1) };
        float u0 = float.MaxValue, v0 = float.MaxValue, u1 = float.MinValue, v1 = float.MinValue;
        foreach (var k in corners)
        {
            var uv = MinePattern.ToField(k, g.Anchor, g.OrientationDeg) - g.OriginUv;
            u0 = MathF.Min(u0, uv.X); u1 = MathF.Max(u1, uv.X); v0 = MathF.Min(v0, uv.Y); v1 = MathF.Max(v1, uv.Y);
        }
        int c0 = Math.Max(0, (int)MathF.Floor(u0 / g.Cell)), c1 = Math.Min(g.Cols - 1, (int)MathF.Floor(u1 / g.Cell));
        int r0 = Math.Max(0, (int)MathF.Floor(v0 / g.Cell)), r1 = Math.Min(g.Rows - 1, (int)MathF.Floor(v1 / g.Cell));
        for (int r = r0; r <= r1; r++)
            for (int col = c0; col <= c1; col++)
                if (PolygonMath.Contains(poly, MinefieldKnowledgeOps.CentreOf(g, col, r))) cells.Add(r * MinefieldKnowledge.MaxCols + col);
        var arr = new int[cells.Count];
        cells.CopyTo(arr);
        return arr;
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
    private readonly bool _allowJump;    // ⭐ CE-4623 AB-6 — a JUMPER's filter includes the off-mesh jump links (NavPolyFlags.Jump); every other filter excludes them
    private readonly float _woodsCost;   // ⭐ CE-4369 — the cost factor on a woods polygon; ≤ 0 ⇒ the mover may not enter it
    private readonly MinefieldPolyMap? _mines;   // ⭐ CE-4624 MS-T3 — the minefield polygons of this mesh (null = none): priced per side by what it knows

    /// <summary>⭐ CE-4369 (R-4190 T-3 / Q-T1 / Q-T2) — what a woods polygon costs each mover, as a factor on its length: infantry a little,
    /// a tracked vehicle a lot; a wheeled vehicle may not enter (0). Tuning values.</summary>
    public const float InfantryWoodsCost = 1.5f, TrackedWoodsCost = 4f, WheeledWoodsCost = 0f;

    /// <param name="doors">the door states to judge by; null = every door open (no terrain doors)</param>
    public DoorAwareQueryFilter(IReadOnlyDictionary<long, int> doorPolys, DoorStates? doors, bool canOpenDoors)
        : this(doorPolys, doors, canOpenDoors, isWorkingCopy: false, canOpenDoors ? InfantryWoodsCost : WheeledWoodsCost) { }

    /// <summary>⭐ CE-4369 — with an explicit woods cost factor (≤ 0 ⇒ woods are closed to this mover).</summary>
    public DoorAwareQueryFilter(IReadOnlyDictionary<long, int> doorPolys, DoorStates? doors, bool canOpenDoors, float woodsCost)
        : this(doorPolys, doors, canOpenDoors, isWorkingCopy: false, woodsCost) { }

    /// <summary>⭐ CE-4624 MS-T3 — with the mesh's minefield polygons (<see cref="NavDoorways.MinefieldPolys"/>): a query that carries a side prices them by that side's knowledge.</summary>
    public DoorAwareQueryFilter(IReadOnlyDictionary<long, int> doorPolys, DoorStates? doors, bool canOpenDoors, MinefieldPolyMap? mines)
        : this(doorPolys, doors, canOpenDoors, isWorkingCopy: false, canOpenDoors ? InfantryWoodsCost : WheeledWoodsCost, allowJump: false, mines) { }

    private DoorAwareQueryFilter(IReadOnlyDictionary<long, int> doorPolys, DoorStates? doors, bool canOpenDoors, bool isWorkingCopy, float woodsCost, bool allowJump = false, MinefieldPolyMap? mines = null)
    {
        _mines = mines;
        _allowJump = allowJump;
        _doorPolys = doorPolys;
        _doors = doors;
        _canOpenDoors = canOpenDoors;
        _isWorkingCopy = isWorkingCopy;
        _woodsCost = woodsCost;
    }

    /// <summary>⭐ CE-4369 — the same doorways and doors, priced for another mover's woods cost.</summary>
    public DoorAwareQueryFilter WithWoodsCost(float woodsCost) => new(_doorPolys, _doors, _canOpenDoors, isWorkingCopy: false, woodsCost, _allowJump, _mines);

    /// <summary>⭐ CE-4623 AB-6 — the same filter for a JUMPER: the navmesh's off-mesh jump links are open to it.</summary>
    public DoorAwareQueryFilter WithJump() => new(_doorPolys, _doors, _canOpenDoors, isWorkingCopy: false, _woodsCost, allowJump: true, _mines);

    /// <summary>The same doorway polygons and agent class, judged by <paramref name="doors"/> instead. ⚠ Allocates — a per-query
    /// caller uses a <see cref="WorkingCopy"/> (R-220).</summary>
    public DoorAwareQueryFilter With(DoorStates? doors) => ReferenceEquals(doors, _doors) ? this : new(_doorPolys, doors, _canOpenDoors, isWorkingCopy: false, _woodsCost, _allowJump, _mines);

    /// <summary>⭐ R-220 — a copy whose doors one THREAD re-points per query (<see cref="JudgeBy"/>): the provider keeps one per
    /// thread and layer, so a query judged by its caller's doors allocates nothing.</summary>
    public DoorAwareQueryFilter WorkingCopy() => new(_doorPolys, _doors, _canOpenDoors, isWorkingCopy: true, _woodsCost, _allowJump, _mines);

    /// <summary>⭐ R-220 — judge by <paramref name="doors"/> from now on (no side: no minefield knowledge applied). Only on a <see cref="WorkingCopy"/>, owned by one thread.</summary>
    public DoorAwareQueryFilter JudgeBy(DoorStates? doors) => JudgeBy(doors, 0);

    /// <summary>⭐ CE-4624 MS-T3 (MR-4, KR-8) — judge by <paramref name="doors"/> AND price minefields by what side <paramref name="sidePlus1"/> − 1 knows (<see cref="DoorStates.Knowledge"/>);
    /// 0 = no knowledge applied. Only on a <see cref="WorkingCopy"/>, owned by one thread.</summary>
    public DoorAwareQueryFilter JudgeBy(DoorStates? doors, byte sidePlus1)
    {
        if (!_isWorkingCopy) throw new InvalidOperationException("Only a WorkingCopy may be re-pointed — a shared filter never changes.");
        _doors = doors;
        _sidePlus1 = sidePlus1;
        return this;
    }

    private byte _sidePlus1;   // ⛔ written only on a working copy (JudgeBy)

    /// <summary>⭐ CE-4624 — the side + 1 this filter prices minefields for (0 = none).</summary>
    public byte SidePlus1 => _sidePlus1;

    /// <summary>⭐ CE-4624 MS-T3 — how many minefield polygons this filter prices (a rail reads it).</summary>
    public int MinefieldPolyCount => _mines?.Count ?? 0;

    /// <summary>⭐ CE-4624 MS-T3 — the mesh's minefield polygon map (null = none).</summary>
    public MinefieldPolyMap? Minefields => _mines;

    /// <summary>How many doorway polygons this filter knows.</summary>
    public int DoorPolyCount => _doorPolys.Count;

    /// <summary>The door index of a doorway polygon, or −1.</summary>
    public int DoorOf(long polyRef) => _doorPolys.TryGetValue(polyRef, out int d) ? d : -1;

    private TerrainDoorState StateOf(int door)
        => _doors != null && door < _doors.Count ? _doors[door] : TerrainDoorState.Closed;   // ⭐ CE-3290 — an unknown door is closed (passable, at its cost)

    public bool PassFilter(long refs, DtMeshTile tile, DtPoly poly)
    {
        if (!Base.PassFilter(refs, tile, poly)) return false;
        if ((poly.flags & Fdp.Toolkit.Navigation.NavPolyFlags.Jump) != 0 && !_allowJump) return false;   // ⭐ CE-4623 AB-6 — a jump link is for a jumper alone
        if (NavDoorways.IsWoods(poly.GetArea()) && _woodsCost <= 0f) return false;   // ⭐ CE-4369 — a wheeled vehicle never enters a wood (Q-T1); ⭐ CE-4624 — a minefield over a wood is still a wood
        if (NavDoorways.IsCrater(poly.GetArea()) && !_canOpenDoors) return false;   // ⭐ CE-4591 (D-2) — no vehicle drives through a crater; infantry crosses; ⭐ CE-4624 — nor through a mined one
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
        if (NavDoorways.IsWoods(curPoly.GetArea()) && _woodsCost > 0f) cost *= _woodsCost;   // ⭐ CE-4369 — a wood priced per mover
        // ⭐ CE-4624 MS-T3 (KR-10, MR-20) — a minefield polygon priced by what THIS mover's side knows of the cells it covers (never closed: a route goes round when it can, through only when it must); no side ⇒ no knowledge
        if (_sidePlus1 != 0 && _doors != null && _mines != null && _mines.TryGet(curRef, out var mine)) cost *= _mines.FactorFor(mine, _doors.Knowledge, _sidePlus1);
        if (_doorPolys.TryGetValue(curRef, out int door)
            && !(prevRef != 0 && _doorPolys.TryGetValue(prevRef, out int prevDoor) && prevDoor == door)   // once per door, on entry
            && StateOf(door) == TerrainDoorState.Closed)
            cost += ClosedDoorPenaltyMetres;
        return cost;
    }
}
