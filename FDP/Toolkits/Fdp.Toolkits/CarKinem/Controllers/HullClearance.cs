using System;
using System.Collections.Generic;
using System.Numerics;
using CarKinem.Core;
using CarKinem.Trajectory;
using Fdp.Toolkit.World;

namespace CarKinem.Controllers
{
    /// <summary>
    /// ⭐⭐ <c>CE-3286</c> (R-3025; 📄 docs/designs/motion-models/DESIGN_Motion_Domains.md §5f) — the HULL against the world's closed barriers:
    /// a ground vehicle's step is traced for its four corners and its centre (<see cref="StepBlocked"/>), and the way on from a pose is a
    /// rollout of the path follower itself (<see cref="WayOnClear"/>). 📌 Live: the CE-2198 guard traced only the centre, so the front half of
    /// a tank stood inside a building, and a hull that turns only while driving forward could not leave it.
    /// </summary>
    /// <summary>
    /// ⭐ <c>CE-4600</c> (B-3; 📄 docs/DESIGN_Vehicle_Breaching.md §3.1) — what held a hull's step: a wall PANEL (or a closed door) crossed — its opaque
    /// <see cref="Piece"/> from the world query — or a SOLID piece the step rose into (<see cref="Rise"/>, <see cref="Piece"/> −1: the terrain names it at
    /// <see cref="At"/>); where, and the barrier's outward normal toward the hull (2-D).
    /// </summary>
    public readonly record struct BarrierHit(bool Rise, int Piece, Vector2 At, Vector2 Normal);

    public static class HullClearance
    {
        /// <summary>A crossing that begins further along the line than this (of its length) ENTERS a barrier; one at 0 began inside it.</summary>
        public const float EnterEpsilon = 1e-3f;

        /// <summary>The rollout's step (m).</summary>
        public const float RolloutStepM = 0.5f;

        /// <summary>The rollout is done once the hull heads within this of its aim (rad) and has driven a hull length.</summary>
        public const float AlignedRad = 0.26f;

        [ThreadStatic] private static List<TraceCrossing>? t_crossings;

        /// <summary>Does this vehicle have a hull to trace (a length and a width; a person has none)?</summary>
        public static bool HasHull(in VehicleParams p) => p.Class != VehicleClass.Pedestrian && p.Class != VehicleClass.Quadruped && p.Length > 0f && p.Width > 0f;   // ⭐ CE-4623: an animal backs out of nothing

        /// <summary>
        /// ⭐ <c>CE-1958</c> — does this mover have a BODY to trace against walls — a length and a width: a vehicle's hull AND an animal's body
        /// (a <see cref="VehicleClass.Quadruped"/>); a person has none. ⚠ Not <see cref="HasHull"/>: an animal is traced by its body but never backs
        /// out (it has no reverse — it turns on the spot instead, <see cref="TurnInPlace"/>). 📌 The user: a pig stood half through its pen's wall
        /// — the centre-only trace stopped its centre on the wall line, half the body each side.
        /// </summary>
        public static bool HasBody(in VehicleParams p) => p.Class != VehicleClass.Pedestrian && p.Length > 0f && p.Width > 0f;

        /// <summary>An animal held at a wall turns on the spot only when its aim is further off its heading than this (rad).</summary>
        public const float PivotMinRad = 0.17f;

        /// <summary>
        /// ⭐ <c>CE-1958</c> — an ANIMAL held at a wall TURNS ON THE SPOT toward <paramref name="aimDir"/> (by at most <paramref name="maxTurn"/>): its
        /// forequarters swing round while it shuffles back by what the swinging corner would push forward (half its width × sin of the turn), so no
        /// corner enters the wall. The pose is traced like any step (<see cref="StepBlocked"/>); false = no turn (aim ahead, or the turn is blocked).
        /// 📌 Without it an animal (no reverse, turning only while it walks) facing a fence with its way on behind it stood there for good.
        /// </summary>
        public static bool TurnInPlace(IWorldQuery world, Vector2 pos, Vector2 fwd, float z, Vector2 aimDir, float length, float width, float maxTurn,
                                       out Vector2 newPos, out Vector2 newFwd)
        {
            newPos = pos; newFwd = fwd;
            float ang = MathF.Atan2(fwd.X * aimDir.Y - fwd.Y * aimDir.X, Vector2.Dot(fwd, aimDir));
            if (MathF.Abs(ang) < PivotMinRad || maxTurn <= 0f) return false;
            float turn = Math.Clamp(ang, -maxTurn, maxTurn);
            float c = MathF.Cos(turn), s = MathF.Sin(turn);
            var f = new Vector2(fwd.X * c - fwd.Y * s, fwd.X * s + fwd.Y * c);
            var p = pos - fwd * (width * 0.5f * MathF.Abs(s) + 0.005f);
            if (StepBlocked(world, pos, fwd, z, p, f, z, length, width)) return false;
            newPos = p; newFwd = f;
            return true;
        }

        /// <summary>The smallest turning radius (m) — the bicycle model's, at full lock.</summary>
        public static float TurnRadius(in VehicleParams p)
            => p.WheelBase > 0f && p.MaxSteerAngle > 0f ? p.WheelBase / MathF.Sin(MathF.Min(p.MaxSteerAngle, 1.5f)) : 0f;

        /// <summary>
        /// Does the step from (<paramref name="from"/>, <paramref name="fromFwd"/>) to (<paramref name="to"/>, <paramref name="toFwd"/>) carry the
        /// centre or a corner of a <paramref name="length"/> × <paramref name="width"/> hull INTO a hull barrier (any wall panel — a building's or,
        /// ⭐ CE-4598, a garden wall's, a fence's, a hedge's — a closed door: <see cref="TracePurpose.Hull"/>) at <see cref="CarKinem.Systems.CarKinematicsSystem.HullProbeM"/> above its wheels? A point already inside may move — so a
        /// hull left half inside can still leave. A hull with no size is traced at its centre only (the CE-2198 rule).
        /// </summary>
        public static bool StepBlocked(IWorldQuery world, Vector2 from, Vector2 fromFwd, float fromZ, Vector2 to, Vector2 toFwd, float toZ,
                                       float length, float width)
            => StepBarrier(world, from, fromFwd, fromZ, to, toFwd, toZ, length, width, out _);

        /// <summary>
        /// ⭐ <c>CE-4600</c> (B-3; 📄 docs/DESIGN_Vehicle_Breaching.md §3.1, §7.2 V-12) — <see cref="StepBlocked"/> that also says WHAT held the step: the first
        /// barrier the centre or a corner met (<see cref="BarrierHit"/>). The contact rule reads it; the plain guard ignores it.
        /// </summary>
        public static bool StepBarrier(IWorldQuery world, Vector2 from, Vector2 fromFwd, float fromZ, Vector2 to, Vector2 toFwd, float toZ,
                                       float length, float width, out BarrierHit hit)
        {
            if (Enters(world, from, fromZ, to, toZ, out hit)) return true;
            if (length <= 0f || width <= 0f) return false;
            float a = length * 0.5f, b = width * 0.5f;
            for (int i = 0; i < 4; i++)
            {
                float along = (i & 1) == 0 ? a : -a, side = (i & 2) == 0 ? b : -b;
                if (Enters(world, Corner(from, fromFwd, along, side), fromZ, Corner(to, toFwd, along, side), toZ, out hit)) return true;
            }
            // ⭐ CE-3473 — and the NOSE and the TAIL (the front and rear edges' middles): an obstacle narrower than the hull, on its line (a drum, a post),
            //   passes between the corners and met only the centre — 📌 measured: a car held at a 0.6 m drum with its nose 2.25 m inside it
            for (int i = 0; i < 2; i++)
            {
                float along = i == 0 ? a : -a;
                if (Enters(world, Corner(from, fromFwd, along, 0f), fromZ, Corner(to, toFwd, along, 0f), toZ, out hit)) return true;
            }
            // ⭐ CE-5052 — and the hull's four EDGES at the new pose: a convex building corner between two traced points (a long side is 7.9 m on an M1)
            //   is met by no point's line when the hull TURNS — 📐 measured live (Stechovice): a recovery reversing while turning swung the tail onto a
            //   house's corner, ~1 m inside the left-rear quarter, and the hull was held both ways for good. An edge that crosses a wall or a door at `to`
            //   and did not at `from` refuses the step; one already crossing may move (as a corner already inside may move out). Walls and doors only:
            //   a ramp's or a slab's rise stays the corner traces' (CE-4041) — a 7.9 m chord over a ramp's crest would dip into it.
            if (Vector2.DistanceSquared(from, to) < 1e-10f && Vector2.DistanceSquared(fromFwd, toFwd) < 1e-10f) return false;
            for (int i = 0; i < 4; i++)
            {
                // 0 left side, 1 right side (front → rear) · 2 nose, 3 tail (left → right)
                float a0 = i < 2 ? a : (i == 2 ? a : -a), s0 = i < 2 ? (i == 0 ? b : -b) : b;
                float a1 = i < 2 ? -a : a0, s1 = i < 2 ? s0 : -b;
                if (EdgeCrossesWall(world, Corner(to, toFwd, a0, s0), Corner(to, toFwd, a1, s1), toZ, out hit)
                    && !EdgeCrossesWall(world, Corner(from, fromFwd, a0, s0), Corner(from, fromFwd, a1, s1), fromZ, out _))
                {
                    if (hit.Normal.LengthSquared() < 1e-6f && Vector2.DistanceSquared(to, hit.At) > 1e-8f)
                        hit = hit with { Normal = Vector2.Normalize(to - hit.At) };
                    return true;
                }
            }
            return false;
        }

        /// <summary>
        /// ⭐ <c>CE-5052</c> — does the hull's edge <paramref name="p"/> → <paramref name="q"/> (at a pose) cross a building's or a free wall's panel or a closed
        /// door, or a solid block taller than a step, at its ends' own ground + the probe (the corner traces' height)? Ramps and slabs are left out (see <see cref="StepBarrier"/>). Allocation-free.
        /// </summary>
        private static bool EdgeCrossesWall(IWorldQuery world, Vector2 p, Vector2 q, float z, out BarrierHit hit)
        {
            hit = default;
            var crossings = t_crossings ??= new List<TraceCrossing>();
            float probe = CarKinem.Systems.CarKinematicsSystem.HullProbeM;
            float gp = world.HullSurfaceZ(p.X, p.Y, z), gq = world.HullSurfaceZ(q.X, q.Y, z);
            world.Trace(new Vector3(p, gp + probe), new Vector3(q, gq + probe), TracePurpose.Hull, crossings);
            float step = MathF.Max(gp, gq) + CarKinem.Systems.CarKinematicsSystem.VehicleMaxStepM;
            for (int i = 0; i < crossings.Count; i++)
            {
                var c = crossings[i];
                // a wall panel or a closed door; or a SOLID block taller than a step (a stand-in building — the corner traces meet it by its rise)
                bool wall = c.ClosedBarrier ? c.Kind is "panel" or "door" : c.Kind == "prism" && c.TopZ > step;
                if (!wall) continue;
                var n = new Vector2(c.EntryNormal.X, c.EntryNormal.Y);
                hit = new BarrierHit(false, c.Piece, p + (q - p) * c.T, n.LengthSquared() > 1e-6f ? Vector2.Normalize(n) : Vector2.Zero);
                return true;
            }
            return false;
        }

        /// <summary>A point of the hull: <paramref name="along"/> ahead of the centre, <paramref name="side"/> to its left.</summary>
        public static Vector2 Corner(Vector2 centre, Vector2 fwd, float along, float side)
            => centre + fwd * along + new Vector2(-fwd.Y, fwd.X) * side;

        /// <summary>
        /// Does the point's step from <paramref name="a"/> to <paramref name="b"/> go into a wall — CE-2198's two tests, per point: ① the
        /// surface under <paramref name="b"/> RISES like a wall (inside a solid block the sampler offers only its roof; the stand-in's block is
        /// not a "closed barrier", which is a blast meaning — W-7′) where it did not under <paramref name="a"/>; ② the line ENTERS a closed
        /// barrier (a building's wall panel, a closed door).
        /// </summary>
        private static bool Enters(IWorldQuery world, Vector2 a, float za, Vector2 b, float zb, out BarrierHit hit)
        {
            hit = default;
            if (Vector2.DistanceSquared(a, b) < 1e-8f) return false;
            float step = Vector2.Distance(a, b);
            // ⭐ CE-4041 — the rise is from the POINT'S OWN ground under a, never from the hull's centre height: on a slope a corner half a
            //   hull ahead stands L/2 × slope above the centre, and read against the centre it "rose like a wall" the moment that passed
            //   VehicleMaxStepM. 📌 ua-hill-attack: every tank was held at the ridge's foot (slope ~0.1, a 9.8 m hull) and backed out again,
            //   no round fired for minutes. A corner stepping onto a solid block still rises by its height; one already on it rises 0.
            // ⭐ CE-4598 (D-4) — the HULL's surface: a wall panel's top is never one, so a low garden wall is met, not climbed
            float ground = world.HullSurfaceZ(a.X, a.Y, za), groundB = world.HullSurfaceZ(b.X, b.Y, za);
            float rise = groundB - ground;
            if (rise > CarKinem.Systems.CarKinematicsSystem.VehicleMaxStepM && rise > CarKinem.Systems.CarKinematicsSystem.VehicleMaxClimbSlope * step)
            {
                hit = new BarrierHit(true, -1, b, Vector2.Normalize(a - b));   // a SOLID piece rose under b: which one is the terrain's to say (TerrainWorld.SolidUnder)
                return true;
            }
            // ⭐ CE-4692 BL-12 — a laid bridge's deck has a KERB: a point that stands on a deck may not step off its side where the surface drops by more than a
            //   step (into the river) — avoidance would otherwise push a 3.6 m hull off a 4 m deck. The ramps run down to the ground, so the ends are no drop.
            if (-rise > CarKinem.Systems.CarKinematicsSystem.VehicleMaxStepM && -rise > CarKinem.Systems.CarKinematicsSystem.VehicleMaxClimbSlope * step && world.OnDeck(a.X, a.Y, ground))
            {
                hit = new BarrierHit(false, -1, b, Vector2.Normalize(a - b));
                return true;
            }
            var crossings = t_crossings ??= new List<TraceCrossing>();   // ⭐ one per thread: the kinematics runs ForEachParallel
            float probe = CarKinem.Systems.CarKinematicsSystem.HullProbeM;
            // ⭐ CE-4041 — and the barrier trace runs at the point's OWN ground + the probe: at the centre's height a front corner's line
            //   on a slope ran INTO the ramp (measured: held at the ramp's foot with the rise test fixed)
            // ⭐ CE-4598 (B-1, V-1) — the HULL purpose: every wall panel blocks, a free wall's too (the fire purpose's ClosedBarrier is the
            //   blast meaning and saw only buildings, so every hull passed every garden wall); a building's breach never opens for a hull (V-5)
            world.Trace(new Vector3(a, ground + probe), new Vector3(b, groundB + probe), TracePurpose.Hull, crossings);
            foreach (var c in crossings)
                // ⭐⭐ CE-3471 — a point that starts ON a wall's face or inside it (its crossing at T ≤ EnterEpsilon) may move OUT, never DEEPER:
                //   📐 measured — a pig held at farm-under-fire's pen wall restarts from rest each frame, so its steps shrink until one lands EXACTLY on
                //   the face (a float32 coordinate near y = 88 moves in 7.6 µm steps, and the face 87.875 lies on that grid); the next step then
                //   crossed at T = 0, was taken for "already inside", and the point walked through the 0.25 m wall and out of the far side.
                if (c.ClosedBarrier && (c.T > EnterEpsilon || Deepens(world, in c, a, b)))
                {
                    var n = new Vector2(c.EntryNormal.X, c.EntryNormal.Y);
                    hit = new BarrierHit(false, c.Piece, a + (b - a) * c.T, n.LengthSquared() > 1e-6f ? Vector2.Normalize(n) : Vector2.Normalize(a - b));
                    return true;
                }
            return false;
        }

        /// <summary>
        /// ⭐ <c>CE-3471</c> — does the step <paramref name="a"/> → <paramref name="b"/>, which starts on or inside the crossed piece, go DEEPER into it — the
        /// middle of its part inside the piece lies farther from the piece's outline than <paramref name="a"/> does? A point can then never pass a wall's
        /// middle, so it can only leave the way it came in. False for a door leaf or a world without prisms (the old rule: an inside start moves freely).
        /// </summary>
        private static bool Deepens(IWorldQuery world, in TraceCrossing c, Vector2 a, Vector2 b)
        {
            if (world is not Fdp.Toolkit.Terrain.TerrainWorldQuery q || c.Piece < 0 || c.Piece >= q.World.Prisms.Count) return false;
            var footprint = q.World.Prisms[c.Piece].Footprint;
            if (footprint.Length < 3) return false;
            float step = Vector2.Distance(a, b);
            if (step < 1e-9f) return false;
            float tMid = MathF.Min(1f, c.T + 0.5f * MathF.Min(c.PathMetres / step, 1f));
            return Depth(footprint, a + (b - a) * tMid) > Depth(footprint, a) + 1e-6f;
        }

        /// <summary>How far <paramref name="p"/> lies inside <paramref name="poly"/> — its distance to the nearest edge; 0 outside. Allocation-free.</summary>
        private static float Depth(Vector2[] poly, Vector2 p)
        {
            if (!Fdp.Toolkit.Terrain.PolygonMath.Contains(poly, p)) return 0f;
            float best = float.MaxValue;
            for (int i = 0, j = poly.Length - 1; i < poly.Length; j = i++)
            {
                Vector2 e0 = poly[j], e = poly[i] - e0;
                float len2 = e.LengthSquared();
                float t = len2 > 1e-12f ? Math.Clamp(Vector2.Dot(p - e0, e) / len2, 0f, 1f) : 0f;
                best = MathF.Min(best, Vector2.Distance(p, e0 + e * t));
            }
            return best;
        }

        /// <summary>
        /// ⭐⭐ The EXIT TEST of a recovery — "smart" is here (R-3025): can the hull drive on from (<paramref name="pos"/>, <paramref name="fwd"/>)?
        /// The path follower is rolled out — pure pursuit at the path's lookahead (the same <see cref="PurePursuitController"/>,
        /// <see cref="CarKinem.Systems.CarKinematicsSystem.PathLookahead"/> and chord cap as the live follower), the bicycle model in
        /// <see cref="RolloutStepM"/> steps, every step through <see cref="StepBlocked"/> — over a quarter turn at full lock plus one hull length,
        /// or until it heads along its aim having driven a hull length, or reaches the path's end. ⛔ Never a fixed back-up distance: one metre
        /// is "the same situation one metre away".
        /// </summary>
        public static bool WayOnClear(IWorldQuery world, TrajectoryPoolManager? pool, in NavState nav, in VehicleParams p,
                                      Vector2 pos, Vector2 fwd, float z)
        {
            bool path = nav.Mode == KinematicsMode.CustomTrajectory && pool != null && pool.TryGetTrajectory(nav.TrajectoryId, out _);
            Vector2 end = EndPoint(pool, in nav, path);
            float speed = nav.TargetSpeed > 0f ? nav.TargetSpeed : 5f;
            float tol = nav.ArrivalRadius > 0f ? nav.ArrivalRadius : 2f;
            float horizon = MathF.PI * 0.5f * TurnRadius(in p) + p.Length;
            float s = nav.ProgressS, travelled = 0f;
            var at = pos;
            var f = fwd;
            while (travelled < horizon)
            {
                if (Vector2.Distance(at, end) <= tol) return true;
                var aim = AimPoint(pool, in nav, in p, path, s, speed, at);
                var to = aim - at;
                if (to.LengthSquared() < 1e-6f) return true;
                float steer = PurePursuitController.CalculateSteering(at, f, Vector2.Normalize(to) * speed, speed, p.WheelBase,
                    p.LookaheadTimeMin, p.LookaheadTimeMax, p.MaxSteerAngle, false);
                float turn = p.WheelBase > 0f ? RolloutStepM / p.WheelBase * MathF.Tan(steer) : 0f;
                float c = MathF.Cos(turn), sn = MathF.Sin(turn);
                var nf = Vector2.Normalize(new Vector2(f.X * c - f.Y * sn, f.X * sn + f.Y * c));
                var nat = at + nf * RolloutStepM;
                float nz = world.HullSurfaceZ(nat.X, nat.Y, z);   // ⭐ CE-4598 (D-4)
                if (StepBlocked(world, at, f, z, nat, nf, nz, p.Length, p.Width)) return false;
                if (path)
                {
                    var (_, tangent, _) = pool!.SampleTrajectory(nav.TrajectoryId, s);
                    s += RolloutStepM * MathF.Max(0f, Vector2.Dot(nf, tangent));
                }
                at = nat; f = nf; z = nz;
                travelled += RolloutStepM;
                if (travelled >= p.Length && MathF.Abs(VectorMath.SignedAngle(f, aim - at)) <= AlignedRad) return true;
            }
            return true;
        }

        /// <summary>Where the follower aims from progress <paramref name="s"/>: the path point a (chord-capped) lookahead on, else the destination.</summary>
        public static Vector2 AimPoint(TrajectoryPoolManager? pool, in NavState nav, in VehicleParams p, bool path, float s, float speed, Vector2 at)
        {
            if (!path) return new Vector2(nav.FinalDestination.X, nav.FinalDestination.Y);
            float ld = CarKinem.Systems.CarKinematicsSystem.CapLookaheadByChord(pool!, nav.TrajectoryId, s,
                CarKinem.Systems.CarKinematicsSystem.PathLookahead(in p, speed),
                MathF.Max(CarKinem.Systems.CarKinematicsSystem.MinPathLookaheadMetres, p.WheelBase * CarKinem.Systems.CarKinematicsSystem.ChordCapFloorWheelBases));
            var (ahead, _, _) = pool!.SampleTrajectory(nav.TrajectoryId, s + ld);
            return new Vector2(ahead.X, ahead.Y);
        }

        /// <summary>The move's end: the path's last point, else its destination.</summary>
        public static Vector2 EndPoint(TrajectoryPoolManager? pool, in NavState nav, bool path)
        {
            if (path && pool!.TryGetTrajectory(nav.TrajectoryId, out var traj) && traj.IsLooped == 0 && traj.Waypoints.Length > 0)
            {
                var last = traj.Waypoints[traj.Waypoints.Length - 1].Position;
                return new Vector2(last.X, last.Y);
            }
            return new Vector2(nav.FinalDestination.X, nav.FinalDestination.Y);
        }
    }
}
