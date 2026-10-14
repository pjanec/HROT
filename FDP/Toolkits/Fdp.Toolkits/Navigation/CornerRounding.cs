using System;
using System.Collections.Generic;
using System.Numerics;

namespace Fdp.Toolkit.Navigation
{
    /// <summary>
    /// ⭐⭐ <c>CE-3029</c> / R-3006 — a VEHICLE's planned path gets its corners rounded for its turn radius.
    /// 📄 docs/designs/navig-2/Navigation_Design_v2_0.md §5.2b (C3–C6).
    ///
    /// <para>Detour's straight path puts a vertex ON the eroded mesh corner; a car's tightest arc (radius R) tangent to both legs passes
    /// R·(1/cos(φ/2) − 1) inside it — into the clearance the bake bought. So each interior vertex turning ≥ <see cref="MinTurnDeg"/> is
    /// shifted OUT along its bisector by that amount (C3) — when the navmesh has room there (C4: the full shift, else ½, else ¼, else the
    /// vertex stays) — and replaced by an arc of radius R tangent to both new legs, whose innermost point is the original vertex. The
    /// tangent length is clamped to half of each adjacent leg (C5); a vertex with a non-Walk traversal (a door) is kept exactly (C6).</para>
    /// </summary>
    public static class CornerRounding
    {
        /// <summary>A vertex turning less than this is left alone — road legs are sampled a few degrees at a time (§5.2a D4).</summary>
        public const float MinTurnDeg = 10f;

        /// <summary>A hairpin past this is left alone: an arc tangent to two nearly opposite legs has no sensible shape.</summary>
        public const float MaxTurnDeg = 170f;

        /// <summary>The arc is sampled at most this many degrees apart.</summary>
        public const float ArcStepDeg = 15f;

        /// <summary>C4 — a shifted vertex counts as on the mesh when it projects within one Vehicle-layer cell (0.3 m, §14).</summary>
        public const float OnMeshTolerance = 0.3f;

        /// <summary>The radius a vehicle's corners are rounded for: WheelBase / tan(MaxSteerAngle) — 0 (do not round) when either is unset.</summary>
        public static float TurnRadius(float wheelBase, float maxSteerAngle)
            => wheelBase > 0f && maxSteerAngle > 0.01f ? wheelBase / MathF.Tan(maxSteerAngle) : 0f;

        /// <summary>
        /// Rounds <paramref name="points"/> (with optional per-point <paramref name="traversals"/>) for <paramref name="radius"/> into
        /// <paramref name="outPoints"/> / <paramref name="outTraversals"/> (cleared first). <paramref name="navmesh"/> null ⇒ every shift
        /// has room. Returns how many corners were rounded.
        /// </summary>
        public static int Round(IReadOnlyList<Vector3> points, byte[]? traversals, float radius, INavmeshProvider? navmesh, uint layerMask,
                                List<Vector3> outPoints, List<byte> outTraversals)
        {
            outPoints.Clear(); outTraversals.Clear();
            int n = points.Count;
            byte Trav(int i) => traversals != null && i < traversals.Length ? traversals[i] : (byte)0;
            if (n == 0) return 0;
            outPoints.Add(points[0]); outTraversals.Add(Trav(0));
            if (n < 3 || radius <= 0f)
            {
                for (int i = 1; i < n; i++) { outPoints.Add(points[i]); outTraversals.Add(Trav(i)); }
                return 0;
            }

            int rounded = 0;
            float minTurn = MinTurnDeg * MathF.PI / 180f, maxTurn = MaxTurnDeg * MathF.PI / 180f;
            for (int i = 1; i < n - 1; i++)
            {
                Vector3 p0 = points[i - 1], v = points[i], p1 = points[i + 1];
                if (Trav(i) != 0 || !TryRound(p0, v, p1, radius, minTurn, maxTurn, navmesh, layerMask, outPoints))
                {
                    outPoints.Add(v);
                    outTraversals.Add(Trav(i));
                    continue;
                }
                rounded++;
                while (outTraversals.Count < outPoints.Count) outTraversals.Add(0);
            }
            outPoints.Add(points[n - 1]); outTraversals.Add(Trav(n - 1));
            return rounded;
        }

        private static bool TryRound(Vector3 p0, Vector3 v, Vector3 p1, float radius, float minTurn, float maxTurn,
                                     INavmeshProvider? navmesh, uint layerMask, List<Vector3> outPoints)
        {
            var a = Xy(v - p0); var b = Xy(p1 - v);
            float la = a.Length(), lb = b.Length();
            if (la < 1e-3f || lb < 1e-3f) return false;
            var da = a / la; var db = b / lb;
            float turn = MathF.Acos(Math.Clamp(Vector2.Dot(da, db), -1f, 1f));
            if (turn < minTurn || turn > maxTurn) return false;

            var inner = db - da;
            if (inner.LengthSquared() < 1e-8f) return false;
            inner = Vector2.Normalize(inner);
            float shift = radius * (1f / MathF.Cos(turn * 0.5f) - 1f);

            // C4 — room for the shift on the request's layer: full, ½, ¼, else keep the vertex as planned.
            Vector3 shifted = v;
            float shiftFraction = 0f;
            foreach (float fraction in Fractions)
            {
                var c = v - new Vector3(inner * (shift * fraction), 0f);
                if (HasRoom(navmesh, c, layerMask)) { shifted = c; shiftFraction = fraction; break; }
            }
            if (shiftFraction == 0f) return false;

            // C3 / C5 — the arc of radius R tangent to P0→V' and V'→P1, its tangent length clamped to half of each leg. ⚠ The
            //   neighbours stay put, so shifting V tilts both legs and the turn at V' grows: with the full shift the shift is
            //   re-solved (a few fixed-point steps, each re-checked for room) until the arc's innermost point sits ON the original
            //   vertex — else a large radius ends up inside it (measured: R 10 m cut 0.7 m inside V before).
            if (!Fillet(p0, shifted, p1, radius, out var f)) return false;
            if (shiftFraction == 1f)
            {
                for (int iter = 0; iter < 4; iter++)
                {
                    float inside = Vector2.Dot(f.Apex - Xy(v), inner);   // > 0 ⇒ the apex is inside the corner
                    if (MathF.Abs(inside) < 0.01f) break;
                    var candidate = shifted - new Vector3(inner * inside, 0f);
                    if (!HasRoom(navmesh, candidate, layerMask) || !Fillet(p0, candidate, p1, radius, out var fc)) break;
                    shifted = candidate; f = fc;
                }
            }

            float start = MathF.Atan2(f.T1.Y - f.Centre.Y, f.T1.X - f.Centre.X);
            float sweep = f.LeftTurn ? f.Turn : -f.Turn;
            int steps = Math.Max(1, (int)MathF.Ceiling(f.Turn / (ArcStepDeg * MathF.PI / 180f)));
            float r = f.Radius, t = f.Tangent;
            var centre = f.Centre;

            // Z: along the legs to the tangent points, then linearly across the arc.
            float la3 = Vector2.Distance(Xy(p0), Xy(shifted)), lb3 = Vector2.Distance(Xy(shifted), Xy(p1));
            float z1 = Lerp(p0.Z, shifted.Z, 1f - t / la3), z2 = Lerp(shifted.Z, p1.Z, t / lb3);
            for (int j = 0; j <= steps; j++)
            {
                float u = j / (float)steps;
                float ang = start + sweep * u;
                outPoints.Add(new Vector3(centre.X + r * MathF.Cos(ang), centre.Y + r * MathF.Sin(ang), Lerp(z1, z2, u)));
            }
            return true;
        }

        private static readonly float[] Fractions = { 1f, 0.5f, 0.25f };

        private struct Arc { public Vector2 T1, Centre, Apex; public float Turn, Radius, Tangent; public bool LeftTurn; }

        /// <summary>The arc of radius ≤ <paramref name="radius"/> tangent to P0→V and V→P1 (C5: tangent length ≤ half of each leg).</summary>
        private static bool Fillet(Vector3 p0, Vector3 v, Vector3 p1, float radius, out Arc arc)
        {
            arc = default;
            var a = Xy(v - p0); var b = Xy(p1 - v);
            float la = a.Length(), lb = b.Length();
            if (la < 1e-3f || lb < 1e-3f) return false;
            var da = a / la; var db = b / lb;
            float turn = MathF.Acos(Math.Clamp(Vector2.Dot(da, db), -1f, 1f));
            if (turn < 1e-3f || (db - da).LengthSquared() < 1e-8f) return false;
            float half = turn * 0.5f;
            float r = radius, t = r * MathF.Tan(half);
            float tMax = 0.5f * MathF.Min(la, lb);
            if (t > tMax) { t = tMax; r = t / MathF.Tan(half); }
            var inner = Vector2.Normalize(db - da);
            var s = Xy(v);
            var centre = s + inner * (r / MathF.Cos(half));
            arc = new Arc
            {
                T1 = s - da * t, Centre = centre, Apex = centre - inner * r,
                Turn = turn, Radius = r, Tangent = t, LeftTurn = da.X * db.Y - da.Y * db.X >= 0f,
            };
            return true;
        }

        private static bool HasRoom(INavmeshProvider? navmesh, Vector3 p, uint layerMask)
        {
            if (navmesh == null) return true;
            if (!navmesh.ProjectToNavmesh(p, out var snapped, layerMask)) return false;
            return Vector2.DistanceSquared(Xy(snapped), Xy(p)) <= OnMeshTolerance * OnMeshTolerance;
        }

        private static Vector2 Xy(Vector3 v) => new(v.X, v.Y);
        private static float Lerp(float a, float b, float u) => a + (b - a) * u;
    }
}
