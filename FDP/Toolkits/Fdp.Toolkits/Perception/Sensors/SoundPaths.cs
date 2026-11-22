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
    /// ⭐ <c>CE-2475</c> (R-2142, Sensing §8.6e) — <see cref="Smear"/> 0…1: how far the direction was pulled between the straight way and the way round
    /// (the first wavefront vs the louder later one); the listener is that much less sure (the answer's radius grows with it).
    public readonly record struct SoundRoute(float Factor, Vector3 HeardFrom, SoundVia Via, float StraightDb, float PathMetres, float PathDb,
                                             int Openings, int Corners, int Crossings, float Smear = 0f);

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
            // ⭐ CE-2479 (R-2143) — each piece's REAL transmission loss (materials.json soundDb), scaled by the band below: ⛔ no longer capped at one
            //   halving (AQ104 E ②'s v1), which made a concrete wall and a hedge alike and every way round barely louder than straight through
            for (int i = 0; i < crossed.Count; i++) straightDb += MathF.Max(0f, crossed[i].Loss);
            straightDb *= band;
            straight = straight with { Factor = Db(straightDb), StraightDb = straightDb, PathDb = straightDb, Crossings = crossed.Count };
            if (straightDb <= 0f || world is null || world.Buildings.Count == 0) { Straight(waypoints, source, ear); return straight; }   // open air: done

            // ② a path that goes round (A–K), when there is one; the louder wins (B)
            int be = BuildingLayoutCache.BuildingAt(world, Xy(ear)), bs = BuildingLayoutCache.BuildingAt(world, Xy(source));
            var path = be < 0 && bs < 0
                ? AroundBuildings(world, ear, source, sourceClass, d, waypoints)                  // K — both outdoors
                : ThroughOpenings(world, doors, ear, source, be, bs, sourceClass, d, waypoints);  // A–J — one of them inside
            if (path is { } p && p.Factor > straight.Factor) return FirstWavefront(p with { StraightDb = straightDb, Crossings = crossed.Count }, straight, ear, source);
            Straight(waypoints, source, ear);
            return straight;
        }

        private static float Db(float db) => MathF.Pow(10f, -db / 20f);

        /// <summary>⭐ <c>CE-2475</c> — the straight way's lead below which the ear still takes its DIRECTION from it (dB; the precedence effect holds
        /// to ~10 dB), and the lead past which the way round alone sets it; between, the image smears from one to the other.</summary>
        public const float FirstWavefrontDb = 10f, SmearedToDb = 20f;

        /// <summary>⭐ <c>CE-2483</c> — an opening (or a corner) closer to the ear than this IS where he stands (m): the sound came from beyond it.</summary>
        public const float StandingInOpeningM = 1.5f;

        /// <summary>
        /// ⭐⭐ <c>CE-2475</c> (R-2142; Sensing §8.6e; 🔒 user: "in reality the sound propagates through walls as well as around them and the human
        /// aggregates those") — the way round is LOUDER (it decides whether the sound is heard, AQ106 B), but the straight way through the walls is the
        /// SHORTEST and arrives first: the ear takes the direction from that first wavefront unless the way round is ≥ <see cref="FirstWavefrontDb"/>
        /// louder (Wallach 1949, Haas 1951), and from the way round alone past <see cref="SmearedToDb"/>; between, the heard-from point moves from the
        /// source's direction to the opening's (and the distance with it), <see cref="SoundRoute.Smear"/> says how far, and it counts as heard
        /// through the opening only past halfway. ⛔ Not a weighted mix of reflections: one way round is what is solved.
        /// </summary>
        internal static SoundRoute FirstWavefront(SoundRoute round, SoundRoute straight, Vector3 ear, Vector3 source)
        {
            float lead = 20f * MathF.Log10(round.Factor / MathF.Max(straight.Factor, 1e-9f));   // how much louder the way round is (dB)
            if (lead >= SmearedToDb) return round;
            if (lead <= FirstWavefrontDb) return round with { HeardFrom = source, Via = SoundVia.Straight, Smear = 0f };
            float t = (lead - FirstWavefrontDb) / (SmearedToDb - FirstWavefrontDb);
            Vector3 toSource = source - ear, toOpening = round.HeardFrom - ear;
            float ds = toSource.Length(), dO = toOpening.Length();
            Vector3 dir = ds > 1e-3f && dO > 1e-3f ? Vector3.Lerp(toSource / ds, toOpening / dO, t) : toOpening;
            float len = dir.Length();
            var from = len > 1e-4f ? ear + dir / len * (ds + (dO - ds) * t) : round.HeardFrom;
            return round with { HeardFrom = from, Via = t > 0.5f ? round.Via : SoundVia.Straight, Smear = t };
        }

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

        private readonly record struct Node(Vector3 At, int Building, int RoomA, int RoomB, bool Closed, float ClosedDb = 0f);

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
            // ⭐ CE-2479 — a closed door and an intact pane at their material's real loss (no one-halving cap)
            float doorDb = (world.Materials.TryGet(TerrainWorld.DoorMaterial, out var m) ? m.SoundAttenuationDb : TerrainWorldQuery.DefaultSoundDb)
                           * AcousticPerception.BandFactor(cls);
            float glassDb = (world.Materials.TryGet(TerrainWorld.PaneMaterial, out var gm) ? gm.SoundAttenuationDb : TerrainWorldQuery.DefaultSoundDb)
                            * AcousticPerception.BandFactor(cls);
            foreach (int b in bs == be || be < 0 ? new[] { bs } : bs < 0 ? new[] { be } : new[] { bs, be })
            {
                if (b < 0) continue;
                foreach (var p in BuildingLayoutCache.Of(world, b).Portals)
                {
                    bool closed = p.Door >= 0 && (doors != null ? doors[p.Door] : world.Doors[p.Door].Initial)
                                  is TerrainDoorState.Closed or TerrainDoorState.Locked;
                    // ⭐ CE-5033 (glass windows D-5) — a glazed window is CLOSED while its pane is intact (glass's dB), OPEN once broken: one broken window lets the sound in
                    if (!closed && p.Glazed && !(doors?.Windows?.PaneBroken(p.Panel, p.Opening) ?? false))
                        nodes.Add(new Node(p.Centre, b, p.RoomA, p.RoomB, true, glassDb));
                    else nodes.Add(new Node(p.Centre, b, p.RoomA, p.RoomB, closed, doorDb));
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
                cost[i] = Reaches(from, nodes[i]) ? Vector3.Distance(source, nodes[i].At) + (nodes[i].Closed ? nodes[i].ClosedDb : 0f) * MetresPerDb : float.PositiveInfinity;
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
                    float cv = cu + Vector3.Distance(nodes[u].At, nodes[v].At) + (nodes[v].Closed ? nodes[v].ClosedDb : 0f) * MetresPerDb
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
                if (node.Closed) db += node.ClosedDb;
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
            // ⭐ CE-2483 — F's "from the last opening", unless he STANDS IN it: then it came from beyond it — the opening before, or the source itself
            //   (📐 ia-house-hunt: the attacker on the stair heard the defender's steps "at his own feet", the stairwell he stood in, and turned on it)
            int last = 0;
            while (last < k && Vector3.Distance(nodes[chain[last]].At, ear) < StandingInOpeningM) last++;
            return last < k
                ? new SoundRoute(PathFactor(d, metres, db), nodes[chain[last]].At, SoundVia.Openings, 0f, metres, db, k, corners, 0)   // F — from the last opening
                : new SoundRoute(PathFactor(d, metres, db), source, SoundVia.Straight, 0f, metres, db, k, corners, 0);
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
            // ⭐ CE-2483 — the last corner he does not stand at (as F's openings)
            int lc = pts.Count - 2;
            while (lc >= 1 && Vector2.Distance(pts[lc], Xy(ear)) < StandingInOpeningM) lc--;
            return lc >= 1
                ? new SoundRoute(PathFactor(d, metres, db), new Vector3(pts[lc], ear.Z), SoundVia.AroundBuildings, 0f, metres, db, 0, corners, 0)
                : new SoundRoute(PathFactor(d, metres, db), source, SoundVia.Straight, 0f, metres, db, 0, corners, 0);
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
