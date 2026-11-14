using System;
using System.Collections.Generic;
using System.Numerics;
using System.Runtime.CompilerServices;

namespace Fdp.Toolkit.Terrain
{
    /// <summary>What joins two rooms (or a room and OUTSIDE). 📄 docs/DESIGN_World_Survey_Sensor.md §6.</summary>
    public enum BuildingPortalKind : byte { Door = 0, Window = 1, Gap = 2, Stair = 3 }

    /// <summary>One room of a building level: the cells a flood fill joined, bounded by that level's walls (openings closed).</summary>
    public readonly record struct BuildingRoom(int Level, float FloorZ, Vector2 Centre, float Area, Vector2 C0, Vector2 C1, Vector2 C2, Vector2 C3,
                                               bool Indoors);

    /// <summary>One opening or stair between two rooms; a room index of −1 is OUTSIDE. <see cref="Door"/> is the index into
    /// <see cref="TerrainWorld.Doors"/> (−1 for a plain opening or a stair) — its state is read LIVE, never cached here.</summary>
    public readonly record struct BuildingPortal(BuildingPortalKind Kind, int RoomA, int RoomB, Vector3 Centre, float Width, float SillZ, float HeadZ,
                                                 int Door, int Face)
    {
        /// <summary>A person can pass it (a window cannot be walked through — it can be fired and looked through).</summary>
        public bool Walkable => Kind != BuildingPortalKind.Window;
    }

    /// <summary>An outside face of the building: a wall panel with OUTSIDE on its <see cref="Outward"/> side.</summary>
    public readonly record struct BuildingFace(int Level, Vector2 A, Vector2 B, Vector2 Outward, float BaseZ, float TopZ, int Panel);

    /// <summary>
    /// ⭐ <c>CE-3249</c> (R-2038, WS-3) — a building's rooms, portals and faces, DERIVED from its current walls and openings on the
    /// muscle (no authored room data). Levels come from where floors ARE (each <see cref="TerrainBuilding.StoreyZ"/> with a floor or a
    /// roof inside the footprint), so a roof with a stair house is simply the top level. 📄 docs/DESIGN_World_Survey_Sensor.md §6.
    /// <para>⚠ The stand-in's model: a production engine derives the same answer from its own scene behind the survey sensor; the
    /// brain only ever sees the survey's answer (R-252).</para>
    /// </summary>
    public sealed class BuildingLayout
    {
        /// <summary>The grid the rooms are filled on (m).</summary>
        public const float Cell = 0.25f;
        /// <summary>A component smaller than this is a wall sliver, not a room (m²).</summary>
        public const float MinRoomArea = 1.0f;
        /// <summary>How far off a wall's centre line a portal looks for the room on each side (m, beyond half the thickness).</summary>
        public const float PortalProbe = 0.45f;
        /// <summary>How far inside a room its corners sit (m).</summary>
        public const float CornerInset = 0.4f;
        private const float LevelTolerance = 0.05f;

        public int Building { get; }
        public bool Solid { get; }
        /// <summary>The floor Z of each level that has a floor (the roof included when it has one), lowest first.</summary>
        public IReadOnlyList<float> Levels { get; }
        /// <summary>Which level is the roof: the topmost with a room open to the sky (−1 = none — a solid building, or no walkable roof).</summary>
        public int RoofLevel { get; }
        public IReadOnlyList<BuildingRoom> Rooms { get; }
        public IReadOnlyList<BuildingPortal> Portals { get; }
        public IReadOnlyList<BuildingFace> Faces { get; }

        // the grid: room id per cell per level (−1 none), in the building's frame
        private readonly Vector2 _origin;      // local min corner
        private readonly int _nx, _ny;
        private readonly short[][] _cells;     // [level][iy * nx + ix]
        private readonly float _cos, _sin;     // building rotation
        private readonly Vector2 _position;
        private readonly Vector2[] _footprint;

        private BuildingLayout(int building, bool solid, float[] levels, int roofLevel, BuildingRoom[] rooms, BuildingPortal[] portals,
                               BuildingFace[] faces, Vector2 origin, int nx, int ny, short[][] cells, TerrainBuilding b)
        {
            Building = building; Solid = solid; Levels = levels; RoofLevel = roofLevel; Rooms = rooms; Portals = portals; Faces = faces;
            _origin = origin; _nx = nx; _ny = ny; _cells = cells;
            float rad = b.RotationDegrees * MathF.PI / 180f;
            _cos = MathF.Cos(rad); _sin = MathF.Sin(rad);
            _position = b.Position;
            _footprint = b.Footprint;
        }

        /// <summary>Is (x, y) inside the building's footprint?</summary>
        public bool Contains(Vector2 p) => PolygonMath.Contains(_footprint, p);

        /// <summary>The level a point at height <paramref name="z"/> stands on: the highest whose floor is at most 0.5 m above it
        /// (−1 below the first floor).</summary>
        public int LevelAt(float z)
        {
            int best = -1;
            for (int i = 0; i < Levels.Count; i++) if (Levels[i] <= z + 0.5f) best = i;
            return best;
        }

        /// <summary>The room under (x, y) on <paramref name="level"/>, else −1 (outside, in a wall, over a stairwell).</summary>
        public int RoomAt(Vector2 p, int level)
        {
            if (Solid || level < 0 || level >= _cells.Length) return -1;
            var l = ToLocal(p);
            int ix = (int)MathF.Floor((l.X - _origin.X) / Cell), iy = (int)MathF.Floor((l.Y - _origin.Y) / Cell);
            if (ix < 0 || iy < 0 || ix >= _nx || iy >= _ny) return -1;
            return _cells[level][iy * _nx + ix];
        }

        /// <summary>The room near (x, y) on <paramref name="level"/>: the cell itself, else the nearest room cell within
        /// <paramref name="radius"/> (a point that lands in a wall's band).</summary>
        public int RoomNear(Vector2 p, int level, float radius = 0.5f)
        {
            int r = RoomAt(p, level);
            if (r >= 0 || Solid || level < 0 || level >= _cells.Length) return r;
            int steps = (int)MathF.Ceiling(radius / Cell);
            var l = ToLocal(p);
            int cx = (int)MathF.Floor((l.X - _origin.X) / Cell), cy = (int)MathF.Floor((l.Y - _origin.Y) / Cell);
            float bestD = float.MaxValue; int best = -1;
            for (int dy = -steps; dy <= steps; dy++)
                for (int dx = -steps; dx <= steps; dx++)
                {
                    int ix = cx + dx, iy = cy + dy;
                    if (ix < 0 || iy < 0 || ix >= _nx || iy >= _ny) continue;
                    int id = _cells[level][iy * _nx + ix];
                    if (id < 0) continue;
                    float d = dx * dx + dy * dy;
                    if (d < bestD) { bestD = d; best = id; }
                }
            return best;
        }

        private Vector2 ToLocal(Vector2 world)
        {
            var d = world - _position;
            return new Vector2(d.X * _cos + d.Y * _sin, -d.X * _sin + d.Y * _cos);
        }

        private static Vector2 ToWorld(Vector2 local, Vector2 position, float cos, float sin)
            => position + new Vector2(local.X * cos - local.Y * sin, local.X * sin + local.Y * cos);

        // ── the derivation ────────────────────────────────────────────────────────────────────────────────────────────

        /// <summary>Derive building <paramref name="index"/>'s layout from <paramref name="world"/> as it is now.</summary>
        public static BuildingLayout Derive(TerrainWorld world, int index)
        {
            var b = world.Buildings[index];
            float rad = b.RotationDegrees * MathF.PI / 180f;
            float cos = MathF.Cos(rad), sin = MathF.Sin(rad);
            Vector2 ToL(Vector2 w) { var d = w - b.Position; return new Vector2(d.X * cos + d.Y * sin, -d.X * sin + d.Y * cos); }

            if (b.Solid)
                return new BuildingLayout(index, true, new[] { b.BaseZ }, -1, Array.Empty<BuildingRoom>(), Array.Empty<BuildingPortal>(),
                    Array.Empty<BuildingFace>(), Vector2.Zero, 0, 0, Array.Empty<short[]>(), b);

            // the footprint's local box, one cell of margin
            var min = new Vector2(float.MaxValue); var max = new Vector2(float.MinValue);
            foreach (var p in b.Footprint) { var l = ToL(p); min = Vector2.Min(min, l); max = Vector2.Max(max, l); }
            min -= new Vector2(Cell); max += new Vector2(Cell);
            int nx = Math.Max(1, (int)MathF.Ceiling((max.X - min.X) / Cell)), ny = Math.Max(1, (int)MathF.Ceiling((max.Y - min.Y) / Cell));
            Vector2 CellCentre(int ix, int iy) => new(min.X + (ix + 0.5f) * Cell, min.Y + (iy + 0.5f) * Cell);

            // the slabs of this building (by footprint box overlap) — floors, the roof, stair-house roofs
            var slabs = new List<TerrainWalkable>();
            var ramps = new List<TerrainWalkable>();
            var fpMin = new Vector2(float.MaxValue); var fpMax = new Vector2(float.MinValue);
            foreach (var p in b.Footprint) { fpMin = Vector2.Min(fpMin, p); fpMax = Vector2.Max(fpMax, p); }
            foreach (var w in world.Walkables)
            {
                if (w.Max.X < fpMin.X || w.Min.X > fpMax.X || w.Max.Y < fpMin.Y || w.Min.Y > fpMax.Y) continue;
                if (w.Kind == TerrainWalkableKind.Ramp) ramps.Add(w); else slabs.Add(w);
            }

            // levels: each StoreyZ with a floor inside the footprint (the ground storey stands on the ground)
            var levels = new List<float>();
            var covers = new List<Func<Vector2, bool>>();   // world point → floor here
            for (int k = 0; k < b.StoreyZ.Count; k++)
            {
                float z = b.StoreyZ[k];
                var at = slabs.FindAll(s => MathF.Abs(s.MinZ - z) < LevelTolerance && MathF.Abs(s.MaxZ - z) < LevelTolerance);
                if (at.Count == 0 && k > 0) continue;   // no floor at that height (a "none" roof)
                // the last entry is the ROOF's height: a level only when the roof is walked on — a lid over a stair house (house-3f's
                // roof rings) covers a corner of the footprint and is no level
                if (k == b.StoreyZ.Count - 1 && k > 0 && Coverage(b.Footprint, at) < 0.5f) continue;
                levels.Add(z);
                covers.Add(at.Count == 0 ? (p => PolygonMath.Contains(b.Footprint, p)) : (p => PolygonMath.Contains(b.Footprint, p) && OnAny(at, p)));
            }

            var rooms = new List<BuildingRoom>();
            var cells = new short[levels.Count][];
            for (int L = 0; L < levels.Count; L++)
            {
                float z = levels[L];
                var grid = new short[nx * ny];
                // −2 = no floor / wall, −1 = free and unvisited
                for (int iy = 0; iy < ny; iy++)
                    for (int ix = 0; ix < nx; ix++)
                        grid[iy * nx + ix] = covers[L](ToWorld(CellCentre(ix, iy), b.Position, cos, sin)) ? (short)-1 : (short)-2;

                // the walls standing on this level close every cell they touch (openings closed for the fill)
                float band = Cell * 0.71f;
                foreach (var panel in world.Panels)
                {
                    if (panel.Building != index || MathF.Abs(panel.BaseZ - z) > LevelTolerance) continue;
                    var a = ToL(panel.A); var c = ToL(panel.B);
                    float half = panel.Thickness / 2f + band;
                    var lo = Vector2.Min(a, c) - new Vector2(half); var hi = Vector2.Max(a, c) + new Vector2(half);
                    int x0 = Math.Max(0, (int)((lo.X - min.X) / Cell)), x1 = Math.Min(nx - 1, (int)((hi.X - min.X) / Cell));
                    int y0 = Math.Max(0, (int)((lo.Y - min.Y) / Cell)), y1 = Math.Min(ny - 1, (int)((hi.Y - min.Y) / Cell));
                    for (int iy = y0; iy <= y1; iy++)
                        for (int ix = x0; ix <= x1; ix++)
                            if (DistanceToSegment(CellCentre(ix, iy), a, c) <= half) grid[iy * nx + ix] = -2;
                }

                // flood fill
                var stack = new Stack<int>();
                for (int start = 0; start < grid.Length; start++)
                {
                    if (grid[start] != -1) continue;
                    var members = new List<int>();
                    short id = (short)rooms.Count;
                    grid[start] = id; stack.Push(start);
                    while (stack.Count > 0)
                    {
                        int c = stack.Pop(); members.Add(c);
                        int cx = c % nx, cy = c / nx;
                        void Visit(int x, int y)
                        {
                            if (x < 0 || y < 0 || x >= nx || y >= ny) return;
                            int n = y * nx + x;
                            if (grid[n] != -1) return;
                            grid[n] = id; stack.Push(n);
                        }
                        Visit(cx + 1, cy); Visit(cx - 1, cy); Visit(cx, cy + 1); Visit(cx, cy - 1);
                    }
                    float area = members.Count * Cell * Cell;
                    if (area < MinRoomArea) { foreach (var m in members) grid[m] = -2; continue; }
                    var rmin = new Vector2(float.MaxValue); var rmax = new Vector2(float.MinValue); var sum = Vector2.Zero;
                    foreach (var m in members)
                    {
                        var cc = CellCentre(m % nx, m / nx);
                        sum += cc; rmin = Vector2.Min(rmin, cc); rmax = Vector2.Max(rmax, cc);
                    }
                    var inset = new Vector2(Math.Max(0f, CornerInset - Cell / 2f));
                    var q0 = rmin + inset; var q1 = rmax - inset;
                    if (q1.X < q0.X) q0.X = q1.X = (rmin.X + rmax.X) / 2f;
                    if (q1.Y < q0.Y) q0.Y = q1.Y = (rmin.Y + rmax.Y) / 2f;
                    var centre = ToWorld(sum / members.Count, b.Position, cos, sin);
                    rooms.Add(new BuildingRoom(L, z, centre, area,
                        ToWorld(q0, b.Position, cos, sin), ToWorld(new Vector2(q1.X, q0.Y), b.Position, cos, sin),
                        ToWorld(q1, b.Position, cos, sin), ToWorld(new Vector2(q0.X, q1.Y), b.Position, cos, sin),
                        Covered(slabs, centre, z)));
                }
                for (int i = 0; i < grid.Length; i++) if (grid[i] < 0) grid[i] = -1;
                cells[L] = grid;
            }

            // the roof: the topmost level with a room open to the sky (house-2f's flat roof; house-3f's roof storey, beside its stair house)
            int roofLevel = -1;
            foreach (var r in rooms) if (!r.Indoors && r.Level > 0) roofLevel = Math.Max(roofLevel, r.Level);

            var layout = new BuildingLayout(index, false, levels.ToArray(), roofLevel, rooms.ToArray(), Array.Empty<BuildingPortal>(),
                Array.Empty<BuildingFace>(), min, nx, ny, cells, b);

            // portals + faces: every opening of every panel standing on a level; every stair ramp
            var portals = new List<BuildingPortal>();
            var faces = new List<BuildingFace>();
            for (int pi = 0; pi < world.Panels.Count; pi++)
            {
                var panel = world.Panels[pi];
                if (panel.Building != index) continue;
                int L = IndexOfLevel(levels, panel.BaseZ);
                if (L < 0) continue;
                float len = panel.Length;
                if (len < 1e-3f) continue;
                var dir = (panel.B - panel.A) / len;
                var normal = new Vector2(-dir.Y, dir.X);
                float off = panel.Thickness / 2f + PortalProbe;
                var mid = panel.A + dir * (len / 2f);
                bool leftOut = !PolygonMath.Contains(b.Footprint, mid + normal * off), rightOut = !PolygonMath.Contains(b.Footprint, mid - normal * off);
                int face = -1;
                if (leftOut != rightOut)
                {
                    face = faces.Count;
                    faces.Add(new BuildingFace(L, panel.A, panel.B, leftOut ? normal : -normal, panel.BaseZ, panel.TopZ, pi));
                }
                foreach (var o in panel.Openings)
                {
                    var c2 = panel.A + dir * (o.At + o.Width / 2f);
                    int ra = SideRoom(layout, c2 + normal * off, L), rb = SideRoom(layout, c2 - normal * off, L);
                    if (ra == rb) continue;   // both sides the same room (or both outside)
                    if (ra < 0) (ra, rb) = (rb, ra);   // RoomA is a room; RoomB may be OUTSIDE
                    var kind = o.Kind switch
                    {
                        TerrainOpeningKind.Door => BuildingPortalKind.Door,
                        TerrainOpeningKind.Window => BuildingPortalKind.Window,
                        _ => BuildingPortalKind.Gap,
                    };
                    int door = o.DoorKey is null ? -1 : world.DoorIndexOf(o.DoorKey);
                    portals.Add(new BuildingPortal(kind, ra, rb, new Vector3(c2, (o.SillZ + o.HeadZ) / 2f), o.Width, o.SillZ, o.HeadZ, door,
                        rb < 0 ? face : -1));
                }
            }
            foreach (var ramp in ramps)
            {
                // the ramp's foot (its lowest edge) and head (its highest edge)
                Vector2 foot = Vector2.Zero, head = Vector2.Zero, first = Vector2.Zero; int nf = 0, nh = 0; float width = 0f;
                foreach (var v in ramp.Vertices)
                {
                    var v2 = new Vector2(v.X, v.Y);
                    if (MathF.Abs(v.Z - ramp.MinZ) < LevelTolerance)
                    {
                        if (nf == 0) first = v2; else width = MathF.Max(width, Vector2.Distance(first, v2));
                        foot += v2; nf++;
                    }
                    else if (MathF.Abs(v.Z - ramp.MaxZ) < LevelTolerance) { head += v2; nh++; }
                }
                if (nf == 0 || nh == 0) continue;
                foot /= nf; head /= nh;
                int lf = IndexOfLevel(levels, ramp.MinZ), lh = IndexOfLevel(levels, ramp.MaxZ);
                if (lf < 0 || lh < 0 || !PolygonMath.Contains(b.Footprint, (foot + head) / 2f)) continue;
                var along = Vector2.Normalize(head - foot);
                int ra = layout.RoomNear(foot - along * 0.3f, lf, 1.0f), rb = layout.RoomNear(head + along * 0.4f, lh, 1.0f);
                if (ra < 0 || rb < 0) continue;
                portals.Add(new BuildingPortal(BuildingPortalKind.Stair, ra, rb, new Vector3((foot + head) / 2f, (ramp.MinZ + ramp.MaxZ) / 2f),
                    width, ramp.MinZ, ramp.MaxZ, -1, -1));
            }

            return new BuildingLayout(index, false, levels.ToArray(), roofLevel, rooms.ToArray(), portals.ToArray(), faces.ToArray(),
                min, nx, ny, cells, b);
        }

        // the share of the footprint the slabs cover, sampled on a 0.5 m grid
        private static float Coverage(Vector2[] footprint, List<TerrainWalkable> slabs)
        {
            var min = new Vector2(float.MaxValue); var max = new Vector2(float.MinValue);
            foreach (var p in footprint) { min = Vector2.Min(min, p); max = Vector2.Max(max, p); }
            int inside = 0, covered = 0;
            for (float y = min.Y + 0.25f; y < max.Y; y += 0.5f)
                for (float x = min.X + 0.25f; x < max.X; x += 0.5f)
                {
                    var p = new Vector2(x, y);
                    if (!PolygonMath.Contains(footprint, p)) continue;
                    inside++;
                    if (OnAny(slabs, p)) covered++;
                }
            return inside == 0 ? 0f : covered / (float)inside;
        }

        private static int SideRoom(BuildingLayout layout, Vector2 p, int level)
            => layout.Contains(p) ? layout.RoomNear(p, level, 0.5f) : -1;

        private static int IndexOfLevel(List<float> levels, float z)
        {
            for (int i = 0; i < levels.Count; i++) if (MathF.Abs(levels[i] - z) < LevelTolerance) return i;
            return -1;
        }

        private static bool Covered(List<TerrainWalkable> slabs, Vector2 p, float z)
        {
            foreach (var s in slabs) if (s.MinZ > z + 1.5f && OnSlab(s, p)) return true;
            return false;
        }

        private static bool OnAny(List<TerrainWalkable> slabs, Vector2 p)
        {
            foreach (var s in slabs) if (OnSlab(s, p)) return true;
            return false;
        }

        private static bool OnSlab(TerrainWalkable s, Vector2 p)
        {
            if (p.X < s.Min.X || p.X > s.Max.X || p.Y < s.Min.Y || p.Y > s.Max.Y) return false;
            var v = s.Vertices; var t = s.Triangles;
            for (int i = 0; i + 2 < t.Length; i += 3)
                if (InTriangle(p, new Vector2(v[t[i]].X, v[t[i]].Y), new Vector2(v[t[i + 1]].X, v[t[i + 1]].Y), new Vector2(v[t[i + 2]].X, v[t[i + 2]].Y)))
                    return true;
            return false;
        }

        private static bool InTriangle(Vector2 p, Vector2 a, Vector2 b, Vector2 c)
        {
            float d1 = Cross(p, a, b), d2 = Cross(p, b, c), d3 = Cross(p, c, a);
            bool neg = d1 < 0 || d2 < 0 || d3 < 0, pos = d1 > 0 || d2 > 0 || d3 > 0;
            return !(neg && pos);
        }

        private static float Cross(Vector2 p, Vector2 a, Vector2 b) => (p.X - b.X) * (a.Y - b.Y) - (a.X - b.X) * (p.Y - b.Y);

        private static float DistanceToSegment(Vector2 p, Vector2 a, Vector2 b)
        {
            var ab = b - a; float l2 = ab.LengthSquared();
            if (l2 < 1e-9f) return Vector2.Distance(p, a);
            float t = Math.Clamp(Vector2.Dot(p - a, ab) / l2, 0f, 1f);
            return Vector2.Distance(p, a + ab * t);
        }
    }

    /// <summary>
    /// ⭐ <c>CE-3249</c> (WS-5) — one derived <see cref="BuildingLayout"/> per building per <see cref="TerrainWorld"/> instance (a new terrain
    /// is a new instance). <see cref="Invalidate"/> is the hook a structural change calls (a breached wall — Structure Damage, not built);
    /// door state is never part of the layout.
    /// </summary>
    public static class BuildingLayoutCache
    {
        private static readonly ConditionalWeakTable<TerrainWorld, BuildingLayout?[]> s_layouts = new();
        private static readonly object s_gate = new();

        /// <summary>Building <paramref name="index"/>'s layout, derived on first ask.</summary>
        public static BuildingLayout Of(TerrainWorld world, int index)
        {
            lock (s_gate)
            {
                var all = s_layouts.GetValue(world, w => new BuildingLayout?[w.Buildings.Count]);
                return all[index] ??= BuildingLayout.Derive(world, index);
            }
        }

        /// <summary>Forget building <paramref name="index"/>'s layout: the next ask derives it again from the walls as they are then.</summary>
        public static void Invalidate(TerrainWorld world, int index)
        {
            lock (s_gate)
                if (s_layouts.TryGetValue(world, out var all) && index >= 0 && index < all.Length) all[index] = null;
        }

        /// <summary>The non-solid building whose footprint holds (x, y), else −1.</summary>
        public static int BuildingAt(TerrainWorld world, Vector2 p)
        {
            for (int i = 0; i < world.Buildings.Count; i++)
                if (!world.Buildings[i].Solid && PolygonMath.Contains(world.Buildings[i].Footprint, p)) return i;
            return -1;
        }
    }
}
