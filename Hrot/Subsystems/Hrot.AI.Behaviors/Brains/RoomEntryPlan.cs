using System;
using System.Numerics;

namespace Hrot.AI.Behaviors.Brains
{
    /// <summary>
    /// ⭐ <c>CE-2257</c> BC-1 (CB-50, CB-51, CB-52; Battle Drill 6; 📄 docs/designs/group-maneuvers/DESIGN_Building_Clearing.md §7.1) — pure:
    /// where a squad stacks on a room's door, where each man of the entry goes (his point of domination) and the arc he covers there,
    /// from the room's four corners, the door's centre and width, and how many men are in the fight.
    /// </summary>
    public static class RoomEntryPlan
    {
        public const int MaxEntry = 4;
        /// <summary>CB-50 — the stack stands this far off the wall (m), #1 this far past the jamb (m), the men this far apart (m).</summary>
        public const float StackOffWallMetres = 0.6f, StackPastJambMetres = 0.6f, StackSpacingMetres = 0.8f;
        /// <summary>CB-51 — a point is this far off a side wall (m) and stays this far inside the room (m); a room under this (m²), or
        /// fewer than <see cref="FourManFrom"/> men in the fight, is a two-man entry.</summary>
        public const float OffWallMetres = 1f, InsideMetres = 0.8f, TwoManAreaSquareMetres = 12f;
        public const int FourManFrom = 3;
        /// <summary>CB-52 — the sector's half width: the corner men (#1, #2) · the centre men (#3, #4) (°).</summary>
        public const float CornerHalfWidth = 50f, CentreHalfWidth = 45f;

        /// <summary>⭐ CE-2334 — the threshold point lies this far into the room from the way in (m).</summary>
        public const float ThresholdMetres = 1f;

        /// <summary>The plan: the stack points (outside, #1 first), how many go in, each one's point of domination and the compass bearing
        /// his sector faces; <see cref="Into"/> is the unit vector into the room through the door.</summary>
        public struct Result
        {
            public int Entry;
            public Vector3 Point0, Point1, Point2, Point3;
            public float Facing0, Facing1, Facing2, Facing3;
            public Vector2 Into;
            /// <summary>The door's centre, the unit vector along the wall toward the stack, the door's half width, the floor.</summary>
            public Vector2 Door, Along;
            public float Half, FloorZ;
            /// <summary>The floor the stack stands on — the room's, or for a stair the floor at its foot (CB-60).</summary>
            public float StackZ;

            /// <summary>The <paramref name="k"/>-th man's stack point (#1 = 0, nearest the door; any number of men).</summary>
            public Vector3 Stack(int k)
            {
                var at = Door - Into * StackOffWallMetres + Along * (Half + StackPastJambMetres + StackSpacingMetres * k);
                return new Vector3(at, StackZ);
            }

            /// <summary>⭐ CE-2334 — the THRESHOLD: <see cref="ThresholdMetres"/> into the room straight through the way in, on the room's
            /// floor — where an entry man crosses before he turns to his point (Battle Drill 6: through the fatal funnel, then the corner).</summary>
            public Vector3 Threshold => new(Door + Into * ThresholdMetres, FloorZ);

            public Vector3 Point(int k) => k switch { 0 => Point0, 1 => Point1, 2 => Point2, _ => Point3 };
            public float Facing(int k) => k switch { 0 => Facing0, 1 => Facing1, 2 => Facing2, _ => Facing3 };
            public static float HalfWidth(int k) => k < 2 ? CornerHalfWidth : CentreHalfWidth;
        }

        /// <summary>
        /// The plan for a room of four <paramref name="corners"/> (x, y pairs, in order round it) whose floor is at <paramref name="floorZ"/>,
        /// entered through the portal at <paramref name="door"/> of <paramref name="width"/>, by <paramref name="inFight"/> men. The stack
        /// stands on the side of the door with LESS room behind the wall, so #1 flows to the open side (CB-50).
        /// </summary>
        public static Result Plan(ReadOnlySpan<float> corners, float floorZ, Vector2 door, float width, int inFight)
        {
            var c = Quad(corners, out var centroid);

            // the door's wall: the room edge nearest the door
            int best = 0;
            float bestD = float.MaxValue;
            for (int i = 0; i < 4; i++)
            {
                float d = DistanceToSegment(door, c[i], c[(i + 1) % 4]);
                if (d < bestD) { bestD = d; best = i; }
            }
            var t = c[(best + 1) % 4] - c[best];
            t = t.LengthSquared() > 1e-6f ? Vector2.Normalize(t) : Vector2.UnitX;
            var n = new Vector2(-t.Y, t.X);
            if (Vector2.Dot(centroid - door, n) < 0f) n = -n;   // into the room
            return Core(c, floorZ, floorZ, door, width, t, n, inFight);
        }

        /// <summary>
        /// ⭐ <c>CE-2259</c> BC-3 (CB-60) — the plan for a room entered from INSIDE it, by a stair whose centre is <paramref name="from"/>:
        /// no wall to stack on, so "into the room" points from the stair toward the room's middle; the stack gathers beside the stair on the
        /// floor below (<paramref name="stackZ"/>).
        /// </summary>
        public static Result PlanFrom(ReadOnlySpan<float> corners, float floorZ, Vector2 from, float stackZ, int inFight)
        {
            var c = Quad(corners, out var centroid);
            var n = centroid - from;
            n = n.LengthSquared() > 1e-4f ? Vector2.Normalize(n) : Vector2.UnitY;
            var t = new Vector2(n.Y, -n.X);
            return Core(c, floorZ, stackZ, from, 1f, t, n, inFight);
        }

        private static Vector2[] Quad(ReadOnlySpan<float> corners, out Vector2 centroid)
        {
            var c = new Vector2[4];
            for (int i = 0; i < 4; i++) c[i] = new Vector2(corners[i * 2], corners[i * 2 + 1]);
            centroid = (c[0] + c[1] + c[2] + c[3]) * 0.25f;
            return c;
        }

        /// <summary>The plan from the way in (<paramref name="door"/>), the unit vector along the wall (<paramref name="t"/>) and the one
        /// into the room (<paramref name="n"/>).</summary>
        private static Result Core(Vector2[] c, float floorZ, float stackZ, Vector2 door, float width, Vector2 t, Vector2 n, int inFight)
        {
            // the room as seen from the door: how far it reaches each way along the wall, and how deep it is
            float minS = float.MaxValue, maxS = float.MinValue, depth = 0f;
            foreach (var p in c)
            {
                float s = Vector2.Dot(p - door, t);
                minS = MathF.Min(minS, s); maxS = MathF.Max(maxS, s);
                depth = MathF.Max(depth, Vector2.Dot(p - door, n));
            }
            float area = PolygonArea(c);
            // the open side is the one with more room; the stack stands on the other (ties: the stack on −t)
            float open = maxS >= -minS ? 1f : -1f;
            float stackSide = -open;
            float openReach = open > 0f ? maxS : -minS, stackReach = open > 0f ? -minS : maxS;

            var r = new Result
            {
                Into = n, Door = door, Along = t * stackSide, Half = MathF.Max(0.3f, width * 0.5f), FloorZ = floorZ, StackZ = stackZ,
                Entry = inFight < FourManFrom || area < TwoManAreaSquareMetres ? Math.Min(2, inFight) : Math.Min(MaxEntry, inFight),
            };

            float third = Math.Clamp(depth / 3f, InsideMetres, MathF.Max(InsideMetres, depth - InsideMetres));
            float mid = Math.Clamp(depth / 2f, InsideMetres, MathF.Max(InsideMetres, depth - InsideMetres));
            Span<Vector2> points = stackalloc Vector2[MaxEntry];
            points[0] = door + n * third + t * (open * Lateral(openReach - OffWallMetres));
            points[1] = door + n * third + t * (stackSide * Lateral(stackReach - OffWallMetres));
            points[2] = door + n * mid + t * (open * Lateral((openReach - OffWallMetres) * 0.5f));
            points[3] = door + n * mid + t * (stackSide * Lateral((stackReach - OffWallMetres) * 0.5f));

            // CB-52 — #1 / #2 face the far half of their own side, #3 / #4 the room's middle: its centre line, deep in — a point at their
            // own depth would turn them side-on to the room
            var farOpen = door + n * (depth * 0.75f) + t * (open * MathF.Max(0f, openReach) * 0.5f);
            var farStack = door + n * (depth * 0.75f) + t * (stackSide * MathF.Max(0f, stackReach) * 0.5f);
            var middle = door + n * (depth * 0.85f) + t * ((minS + maxS) * 0.5f);
            for (int k = 0; k < MaxEntry; k++)
            {
                var aim = k == 0 ? farOpen : k == 1 ? farStack : middle;
                var from = points[k];
                if (r.Entry == 2 && k < 2)
                    aim = door + n * (depth * 0.5f) + t * ((k == 0 ? open : stackSide) * MathF.Max(0f, k == 0 ? openReach : stackReach) * 0.5f);
                float facing = LookAt.BearingTo(new Vector3(from, 0f), new Vector3(aim, 0f));
                Set(ref r, k, new Vector3(from, floorZ), facing);
            }
            return r;
        }

        /// <summary>The flat quad holds <paramref name="at"/> (corners as x, y pairs).</summary>
        public static bool Inside(ReadOnlySpan<float> corners, Vector2 at)
        {
            bool inside = false;
            for (int i = 0, j = 3; i < 4; j = i++)
            {
                float xi = corners[i * 2], yi = corners[i * 2 + 1], xj = corners[j * 2], yj = corners[j * 2 + 1];
                if ((yi > at.Y) != (yj > at.Y) && at.X < (xj - xi) * (at.Y - yi) / (yj - yi) + xi) inside = !inside;
            }
            return inside;
        }

        private static float Lateral(float reach) => MathF.Max(0.3f, reach);

        private static void Set(ref Result r, int k, Vector3 point, float facing)
        {
            switch (k)
            {
                case 0: r.Point0 = point; r.Facing0 = facing; break;
                case 1: r.Point1 = point; r.Facing1 = facing; break;
                case 2: r.Point2 = point; r.Facing2 = facing; break;
                default: r.Point3 = point; r.Facing3 = facing; break;
            }
        }

        private static float DistanceToSegment(Vector2 p, Vector2 a, Vector2 b)
        {
            var ab = b - a;
            float t = ab.LengthSquared() < 1e-9f ? 0f : Math.Clamp(Vector2.Dot(p - a, ab) / ab.LengthSquared(), 0f, 1f);
            return Vector2.Distance(p, a + ab * t);
        }

        private static float PolygonArea(Vector2[] c)
        {
            float a = 0f;
            for (int i = 0, j = c.Length - 1; i < c.Length; j = i++) a += c[j].X * c[i].Y - c[i].X * c[j].Y;
            return MathF.Abs(a) * 0.5f;
        }
    }
}
