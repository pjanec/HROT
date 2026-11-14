using System;
using System.Collections.Generic;
using System.Numerics;
using System.Runtime.CompilerServices;
using Fdp.Toolkit.Terrain;
using Fdp.Toolkit.Tkb.Domain;
using Fdp.Toolkit.World;

namespace Fdp.Toolkit.Perception.Sensors
{
    /// <summary>How a sound reached the ear (<see cref="SoundRoute.Via"/>).</summary>
    public enum SoundVia : byte
    {
        /// <summary>The straight line: nothing between, or the walls and slabs it crosses (v1, a halving each).</summary>
        Straight = 0,
        /// <summary>⭐ AQ106 A–J — through a building's openings (doors, windows, gaps, stairs), room to room.</summary>
        Openings = 1,
        /// <summary>⭐ AQ106 K — round the corners of the buildings between, outdoors.</summary>
        AroundBuildings = 2,
    }

    /// <summary>
    /// ⭐ AQ106 (R-3050) — what <see cref="SoundPaths.Route"/> decided for one sound at one ear. <see cref="Factor"/> scales the sound's carry
    /// (heard when the straight distance ≤ carry × Factor); <see cref="HeardFrom"/> is where it seems to come from — the last opening or corner
    /// on the chosen path, the source itself on the straight line.
    /// </summary>
    public readonly record struct SoundRoute(float Factor, Vector3 HeardFrom, SoundVia Via, float StraightDb, float PathMetres, float PathDb,
                                             int Openings, int Corners, int Crossings);

    /// <summary>
    /// ⭐⭐ AQ106 (R-3050; docs/blueprints/Architect_Question_106_Sound_Paths.md, World Query Seam WQ-E v2) — <b>sound round corners and
    /// through doors and windows</b>, with no raycast beyond the one straight line: indoors over each building's cached room / portal graph
    /// (<see cref="BuildingLayout"/>, the survey sensor's own), outdoors round the footprints of the buildings in the way. The louder of the
    /// path and the straight line wins (B). ⭐ ONE solver: the hearing sensor calls <see cref="Route"/>, the debug route calls the same with a
    /// list for the waypoints (L), so the two cannot disagree. Runs where hearing runs — the Perception node (Node Roles §3.0a).
    /// <para>⚠ The stand-in's solver: it reads the terrain's own structure (buildings, rooms, doors); a production engine answers the same
    /// question from its own scene.</para>
    /// </summary>
    public static class SoundPaths
    {
        /// <summary>C — a corner the path turns through (a turn of at least <see cref="CornerTurnDegrees"/>) costs this many dB, by band.</summary>
        public const float CornerDbHigh = 3f, CornerDbMid = 2f, CornerDbLow = 0.5f;

        /// <summary>C — the smallest turn that counts as a corner (degrees); a straight pass through a doorway is free.</summary>
        public const float CornerTurnDegrees = 45f;

        /// <summary>K — the most buildings a sound is taken round outdoors; past that, the through-walls value stands.</summary>
        public const int MaxBuildingsRound = 2;

        /// <summary>The search's exchange rate: one dB of loss weighs as this many metres when choosing a path (the chosen path's exact
        /// factor is then computed from its true length and loss).</summary>
        public const float MetresPerDb = 1f;

        private static float CosCorner => MathF.Cos(CornerTurnDegrees * MathF.PI / 180f);

        /// <summary>C — the corner loss of a sound of <paramref name="sourceClass"/>'s band (Q95 B3).</summary>
        public static float CornerDb(byte sourceClass) => SoundBands.Of((SoundSourceClass)sourceClass) switch
        {
            SoundBand.Low  => CornerDbLow,
            SoundBand.High => CornerDbHigh,
            _              => CornerDbMid,
        };

        [ThreadStatic] private static List<TraceCrossing>? t_crossed;
        [ThreadStatic] private static List<Node>? t_nodes;
        [ThreadStatic] private static float[]? t_cost;
        [ThreadStatic] private static int[]? t_pred;
        [ThreadStatic] private static bool[]? t_done;

        /// <summary>
        /// The route a sound of <paramref name="sourceClass"/> made at <paramref name="source"/> takes to <paramref name="ear"/> (both feet
        /// positions). <paramref name="query"/> gives the straight line (null ⇒ nothing between); <paramref name="world"/> and
        /// <paramref name="doors"/> the buildings (null ⇒ the straight line only). <paramref name="waypoints"/>, when given, receives the chosen
        /// path's points from the source to the ear (the debug route, L).
        /// </summary>
        public static SoundRoute Route(IWorldQuery? query, TerrainWorld? world, DoorStates? doors, Vector3 ear, Vector3 source, byte sourceClass,
                                       List<Vector3>? waypoints = null)
        {
            waypoints?.Clear();
            float band = AcousticPerception.BandFactor(sourceClass);
            float d = Vector3.Distance(ear, source);
            var straight = new SoundRoute(1f, source, SoundVia.Straight, 0f, d, 0f, 0, 0, 0);
            if (query is null) { Straight(waypoints, source, ear); return straight; }

            // ① the straight line (v1): a halving per wall or slab, at the body's height
            var crossed = t_crossed ??= new List<TraceCrossing>();
            var lift = new Vector3(0f, 0f, AcousticPerception.BodyHeight);
            query.Trace(source + lift, ear + lift, TracePurpose.Sound, crossed);
            float straightDb = 0f;
            for (int i = 0; i < crossed.Count; i++) straightDb += MathF.Min(MathF.Max(0f, crossed[i].Loss), AcousticPerception.WallCapDb);
            straightDb *= band;
            straight = straight with { Factor = Db(straightDb), StraightDb = straightDb, PathDb = straightDb, Crossings = crossed.Count };
            if (straightDb <= 0f || world is null || world.Buildings.Count == 0) { Straight(waypoints, source, ear); return straight; }   // open air: done

            // ② a path that goes round (A–K), when there is one; the louder wins (B)
            int be = BuildingLayoutCache.BuildingAt(world, Xy(ear)), bs = BuildingLayoutCache.BuildingAt(world, Xy(source));
            var path = be < 0 && bs < 0
                ? AroundBuildings(world, ear, source, sourceClass, d, waypoints)                  // K — both outdoors
                : ThroughOpenings(world, doors, ear, source, be, bs, sourceClass, d, waypoints);  // A–J — one of them inside
            if (path is { } p && p.Factor > straight.Factor) return p with { StraightDb = straightDb, Crossings = crossed.Count };
            Straight(waypoints, source, ear);
            return straight;
        }

        private static float Db(float db) => MathF.Pow(10f, -db / 20f);

        // the factor a path of length `metres` with `db` of loss gives, against the straight distance d: heard when metres ≤ carry × 10^(−db/20)
        private static float PathFactor(float d, float metres, float db) => metres <= 1e-3f ? 1f : MathF.Min(1f, d / metres) * Db(db);

        private static Vector2 Xy(Vector3 p) => new(p.X, p.Y);

        private static void Straight(List<Vector3>? waypoints, Vector3 source, Vector3 ear)
        {
            if (waypoints == null) return;
            waypoints.Clear();
            waypoints.Add(source);
            waypoints.Add(ear);
        }

        // ── A–J: room to room through the openings ───────────────────────────────────────────────────────────────────────────

        private readonly record struct Node(Vector3 At, int Building, int RoomA, int RoomB, bool Closed);

        private const int Outside = -1;

        // where a point is: (building, room) or OUTSIDE (building −1)
        private static (int Building, int Room) Locate(TerrainWorld world, int b, Vector3 p)
        {
            if (b < 0) return (Outside, Outside);
            var layout = BuildingLayoutCache.Of(world, b);
            int level = Math.Max(0, layout.LevelAt(p.Z));
            int room = layout.RoomAt(Xy(p), level);
            if (room < 0) room = layout.RoomNear(Xy(p), level, 1f);
            return room < 0 ? (Outside, Outside) : (b, room);
        }

        // can a point at `loc` reach portal node `n` without passing another opening?
        private static bool Reaches((int Building, int Room) loc, in Node n)
            => loc.Building == Outside ? n.RoomB == Outside
             : n.Building == loc.Building && (n.RoomA == loc.Room || n.RoomB == loc.Room);

        // are two portals in one space (a room they share, or OUTSIDE)?
        private static bool Adjacent(in Node a, in Node b)
        {
            if (a.RoomB == Outside && b.RoomB == Outside) return true;   // through OUTSIDE (any buildings)
            if (a.Building != b.Building) return false;
            return a.RoomA == b.RoomA || a.RoomA == b.RoomB && b.RoomB != Outside || a.RoomB != Outside && (a.RoomB == b.RoomA || a.RoomB == b.RoomB);
        }

        private static SoundRoute? ThroughOpenings(TerrainWorld world, DoorStates? doors, Vector3 ear, Vector3 source, int be, int bs, byte cls,
                                                   float d, List<Vector3>? waypoints)
        {
            var from = Locate(world, bs, source);
            var to = Locate(world, be, ear);
            if (from == to) return null;   // one room (or both outside the buildings): the straight line is the path

            // the portals of the (one or two) buildings involved
            var nodes = t_nodes ??= new List<Node>();
            nodes.Clear();
            float doorDb = MathF.Min(world.Materials.TryGet(TerrainWorld.DoorMaterial, out var m) ? m.SoundAttenuationDb : TerrainWorldQuery.DefaultSoundDb,
                                     AcousticPerception.WallCapDb) * AcousticPerception.BandFactor(cls);
            foreach (int b in bs == be || be < 0 ? new[] { bs } : bs < 0 ? new[] { be } : new[] { bs, be })
            {
                if (b < 0) continue;
                foreach (var p in BuildingLayoutCache.Of(world, b).Portals)
                {
                    bool closed = p.Door >= 0 && (doors != null ? doors[p.Door] : world.Doors[p.Door].Initial)
                                  is TerrainDoorState.Closed or TerrainDoorState.Locked;
                    nodes.Add(new Node(p.Centre, b, p.RoomA, p.RoomB, closed));
                }
            }
            int n = nodes.Count;
            if (n == 0) return null;
            var cost = t_cost is { } c0 && c0.Length >= n ? c0 : (t_cost = new float[Math.Max(n, 64)]);
            var pred = t_pred is { } p0 && p0.Length >= n ? p0 : (t_pred = new int[Math.Max(n, 64)]);
            var done = t_done is { } q0 && q0.Length >= n ? q0 : (t_done = new bool[Math.Max(n, 64)]);
            float cornerDb = CornerDb(cls), cosCorner = CosCorner;
            for (int i = 0; i < n; i++)
            {
                done[i] = false; pred[i] = -2;
                cost[i] = Reaches(from, nodes[i]) ? Vector3.Distance(source, nodes[i].At) + (nodes[i].Closed ? doorDb : 0f) * MetresPerDb : float.PositiveInfinity;
                if (!float.IsPositiveInfinity(cost[i])) pred[i] = -1;   // −1 = the source
            }
            // Dijkstra over ~10–50 nodes; the corner at a node is charged when the path leaves it (its turn needs the next point)
            int best = -1; float bestCost = float.PositiveInfinity;
            for (int round = 0; round < n; round++)
            {
                int u = -1; float cu = float.PositiveInfinity;
                for (int i = 0; i < n; i++) if (!done[i] && cost[i] < cu) { cu = cost[i]; u = i; }
                if (u < 0) break;
                done[u] = true;
                var prevAt = pred[u] >= 0 ? nodes[pred[u]].At : source;
                if (Reaches(to, nodes[u]))
                {
                    float total = cu + Vector3.Distance(nodes[u].At, ear) + (Turns(prevAt, nodes[u].At, ear, cosCorner) ? cornerDb * MetresPerDb : 0f);
                    if (total < bestCost) { bestCost = total; best = u; }
                }
                for (int v = 0; v < n; v++)
                {
                    if (done[v] || v == u || !Adjacent(nodes[u], nodes[v])) continue;
                    float cv = cu + Vector3.Distance(nodes[u].At, nodes[v].At) + (nodes[v].Closed ? doorDb : 0f) * MetresPerDb
                             + (Turns(prevAt, nodes[u].At, nodes[v].At, cosCorner) ? cornerDb * MetresPerDb : 0f);
                    if (cv < cost[v]) { cost[v] = cv; pred[v] = u; }
                }
            }
            if (best < 0) return null;

            // the chosen path, exactly: its length, its loss, its openings and corners
            Span<int> chain = stackalloc int[n];
            int k = 0;
            for (int i = best; i >= 0; i = pred[i]) chain[k++] = i;
            float metres = 0f, db = 0f; int corners = 0;
            var at = source;
            for (int j = k - 1; j >= 0; j--)
            {
                var node = nodes[chain[j]];
                var next = j > 0 ? nodes[chain[j - 1]].At : ear;
                metres += Vector3.Distance(at, node.At);
                if (node.Closed) db += doorDb;
                if (Turns(at, node.At, next, cosCorner)) { db += cornerDb; corners++; }
                at = node.At;
            }
            metres += Vector3.Distance(at, ear);
            if (waypoints != null)
            {
                waypoints.Add(source);
                for (int j = k - 1; j >= 0; j--) waypoints.Add(nodes[chain[j]].At);
                waypoints.Add(ear);
            }
            return new SoundRoute(PathFactor(d, metres, db), nodes[chain[0]].At, SoundVia.Openings, 0f, metres, db, k, corners, 0);   // F — from the last opening
        }

        private static bool Turns(Vector3 a, Vector3 b, Vector3 c, float cosCorner)
        {
            var u = Xy(b) - Xy(a); var v = Xy(c) - Xy(b);
            float lu = u.Length(), lv = v.Length();
            if (lu < 1e-3f || lv < 1e-3f) return false;   // a vertical step (a stair) or a point on the opening: no turn in plan
            return Vector2.Dot(u, v) / (lu * lv) < cosCorner;
        }

        // ── K: round the buildings in the way, outdoors ──────────────────────────────────────────────────────────────────────

        private static readonly ConditionalWeakTable<TerrainWorld, Hulls> s_hulls = new();

        private sealed class Hulls
        {
            public readonly Vector2[][] Hull;
            public readonly (Vector2 Min, Vector2 Max)[] Box;
            public Hulls(TerrainWorld world)
            {
                Hull = new Vector2[world.Buildings.Count][];
                Box = new (Vector2, Vector2)[world.Buildings.Count];
                for (int i = 0; i < Hull.Length; i++)
                {
                    Hull[i] = ConvexHull(world.Buildings[i].Footprint);
                    var mn = new Vector2(float.MaxValue); var mx = new Vector2(float.MinValue);
                    foreach (var p in Hull[i]) { mn = Vector2.Min(mn, p); mx = Vector2.Max(mx, p); }
                    Box[i] = (mn, mx);
                }
            }
        }

        private static SoundRoute? AroundBuildings(TerrainWorld world, Vector3 ear, Vector3 source, byte cls, float d, List<Vector3>? waypoints)
        {
            var hulls = s_hulls.GetValue(world, w => new Hulls(w));
            Vector2 a = Xy(source), b = Xy(ear);
            var segMin = Vector2.Min(a, b); var segMax = Vector2.Max(a, b);
            Span<int> hit = stackalloc int[MaxBuildingsRound + 1];
            Span<float> along = stackalloc float[MaxBuildingsRound + 1];
            int count = 0;
            for (int i = 0; i < hulls.Hull.Length && count <= MaxBuildingsRound; i++)
            {
                var (mn, mx) = hulls.Box[i];
                if (mx.X < segMin.X || mn.X > segMax.X || mx.Y < segMin.Y || mn.Y > segMax.Y) continue;
                if (!CrossesInterior(hulls.Hull[i], a, b, out float t)) continue;
                hit[count] = i; along[count] = t; count++;
            }
            if (count == 0 || count > MaxBuildingsRound) return null;   // nothing to go round, or too many: through the walls
            if (count == 2 && along[1] < along[0]) { (hit[0], hit[1]) = (hit[1], hit[0]); }

            // each building in turn, from where the path stands to the ear: the shorter way round its hull
            float cornerDb = CornerDb(cls), cosCorner = CosCorner;
            var pts = new List<Vector2>(16) { a };
            for (int h = 0; h < count; h++)
            {
                var round = Round(hulls.Hull[hit[h]], pts[^1], b);
                if (round == null) return null;
                pts.AddRange(round);
            }
            pts.Add(b);
            float metres = 0f, db = 0f; int corners = 0;
            for (int i = 1; i < pts.Count; i++)
            {
                metres += Vector2.Distance(pts[i - 1], pts[i]);
                if (i < pts.Count - 1 && Turns(new Vector3(pts[i - 1], 0), new Vector3(pts[i], 0), new Vector3(pts[i + 1], 0), cosCorner)) { db += cornerDb; corners++; }
            }
            metres = MathF.Max(metres, d);
            float z = (source.Z + ear.Z) * 0.5f;
            if (waypoints != null)
            {
                waypoints.Add(source);
                for (int i = 1; i < pts.Count - 1; i++) waypoints.Add(new Vector3(pts[i], z));
                waypoints.Add(ear);
            }
            var lastCorner = pts.Count > 2 ? new Vector3(pts[^2], ear.Z) : source;
            return new SoundRoute(PathFactor(d, metres, db), lastCorner, SoundVia.AroundBuildings, 0f, metres, db, 0, corners, 0);
        }

        // does a→b pass through the convex polygon's interior (not just touch it)? t = where it enters (0..1)
        private static bool CrossesInterior(Vector2[] hull, Vector2 a, Vector2 b, out float t)
        {
            t = 0f;
            if (hull.Length < 3) return false;
            Span<float> ts = stackalloc float[hull.Length + 2];
            Span<(float T0, float T1)> iv = stackalloc (float, float)[hull.Length + 1];
            int k = PolygonMath.InsideIntervals(hull, a, b, ts, iv);
            float len = Vector2.Distance(a, b);
            for (int i = 0; i < k; i++)
                if ((iv[i].T1 - iv[i].T0) * len > 0.05f) { t = iv[i].T0; return true; }
            return false;
        }

        // the shorter way from `from` to `to` round a convex hull: the hull vertices it passes, in order (null = no way found)
        private static List<Vector2>? Round(Vector2[] hull, Vector2 from, Vector2 to)
        {
            int n = hull.Length;
            List<Vector2>? best = null; float bestLen = float.MaxValue;
            for (int dir = -1; dir <= 1; dir += 2)
                for (int i = 0; i < n; i++)
                {
                    if (CrossesInterior(hull, from, hull[i], out _)) continue;
                    float len = Vector2.Distance(from, hull[i]);
                    for (int s = 0, j = i; s < n; s++, j = ((j + dir) % n + n) % n)
                    {
                        if (s > 0) len += Vector2.Distance(hull[(j - dir + n) % n], hull[j]);
                        if (len >= bestLen) break;
                        if (CrossesInterior(hull, hull[j], to, out _)) continue;
                        float total = len + Vector2.Distance(hull[j], to);
                        if (total < bestLen)
                        {
                            bestLen = total;
                            best = new List<Vector2>();
                            for (int q = 0, r = i; q <= s; q++, r = ((r + dir) % n + n) % n) best.Add(hull[r]);
                        }
                        break;   // the first vertex that sees `to` ends this way round
                    }
                }
            return best;
        }

        /// <summary>The convex hull of a footprint, counter-clockwise (Andrew's monotone chain).</summary>
        internal static Vector2[] ConvexHull(Vector2[] points)
        {
            if (points.Length < 3) return (Vector2[])points.Clone();
            var p = (Vector2[])points.Clone();
            Array.Sort(p, (u, v) => u.X != v.X ? u.X.CompareTo(v.X) : u.Y.CompareTo(v.Y));
            var h = new Vector2[2 * p.Length];
            int k = 0;
            static float Cross(Vector2 o, Vector2 a, Vector2 b) => (a.X - o.X) * (b.Y - o.Y) - (a.Y - o.Y) * (b.X - o.X);
            for (int i = 0; i < p.Length; i++) { while (k >= 2 && Cross(h[k - 2], h[k - 1], p[i]) <= 0) k--; h[k++] = p[i]; }
            for (int i = p.Length - 2, t = k + 1; i >= 0; i--) { while (k >= t && Cross(h[k - 2], h[k - 1], p[i]) <= 0) k--; h[k++] = p[i]; }
            Array.Resize(ref h, k - 1);
            return h;
        }
    }
}
