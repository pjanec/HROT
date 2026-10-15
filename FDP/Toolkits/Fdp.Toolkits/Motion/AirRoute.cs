using System;
using System.Collections.Generic;
using System.Numerics;
using Fdp.Core;
using Fdp.Toolkit.Navigation;

namespace Fdp.Toolkit.Motion
{
    /// <summary>One place an aircraft stops on its route: a Ground waypoint, a hold, or the route's end.</summary>
    public struct AirStop
    {
        /// <summary>Where on the path (metres along it, XY).</summary>
        public float S;
        /// <summary>The route waypoint it is (index into the route's waypoints).</summary>
        public int Waypoint;
        public RouteWaypointKind Kind;
        public RouteHold Hold;
        public float HoldSeconds;
        public long HoldUnit;
        /// <summary>The route's end (a non-looping route): the order is done here.</summary>
        public bool Last;
    }

    /// <summary>What the follower is doing — the reported <see cref="NavigationPhase"/> derives from it.</summary>
    public enum AirStage : byte { Start, TakingOff, Flying, Landing, Holding, Done }

    /// <summary>
    /// ⭐⭐ Air Ops slice 2 (docs/designs/air-attack/DESIGN_Air_Operations.md §5, as-built §5b) — an aircraft's route as the Muscle flies
    /// it: the route's waypoints with their corners rounded to the aircraft's turn radius (<see cref="CornerRounding.Round"/>), sampled
    /// densely, each point at its height over the terrain; the stops; and the follower's state. Compiled by <see cref="AirRoute.Compile"/>
    /// from the route entity the intent names (<see cref="NavigationIntent.RouteEntityId"/>). Muscle-local: never saved, never on the wire.
    /// </summary>
    [ComponentId(GlobalComponentIds.AirPath)]
    [DataPolicy(DataPolicy.Transient)]
    public sealed class AirPath
    {
        // ── what it was compiled from ──
        public long RouteEntityId;
        public int RouteVersion;
        public uint IntentId;
        public bool Loop;

        // ── the path ──
        public Vector3[] Points = Array.Empty<Vector3>();
        /// <summary>Metres along the path (XY) at each point.</summary>
        public float[] S = Array.Empty<float>();
        /// <summary>The route speed from each point on (0 = the intent's or the top speed).</summary>
        public float[] Speed = Array.Empty<float>();
        /// <summary>Signed curvature (1/m, + = turning left) at each point.</summary>
        public float[] Curvature = Array.Empty<float>();
        public AirStop[] Stops = Array.Empty<AirStop>();

        public float Length => S.Length == 0 ? 0f : S[S.Length - 1];

        // ── the follower ──
        public AirStage Stage;
        public float Progress;
        public float SpeedNow;
        public int NextStop;
        public float HoldLeft;
        public bool ReleaseArmed = true;
        /// <summary>⭐ Airborne insertion B-3 — past a <see cref="RouteHold.Drop"/> waypoint and not released yet: the leg is flown
        /// reporting <see cref="NavigationPhase.Dropping"/>.</summary>
        public bool Dropping;
        public float Pitch;
        public float Roll;
        /// <summary>⭐ Air Ops slice 4 — a plane's circle after its route's end (<see cref="FixedWingModel"/>).</summary>
        public Vector2 OrbitCenter;
        public float OrbitAngle;
    }

    /// <summary>
    /// ⭐⭐ Air Ops slice 2 (DESIGN_Air_Operations §5 B1/B2, as-built §5b) — the route → the path an aircraft flies. ONE smoother: the
    /// road-path <see cref="CornerRounding.Round"/> — WITHOUT its outward shift (C3 buys ground clearance from the navmesh; a shifted
    /// vertex also kinks the leg to the next arc), so each corner is a plain fillet of the turn radius passing the waypoint at
    /// R·(1/cos(φ/2) − 1) — and with the stops kept exactly. Heights:
    /// a waypoint's <c>HeightAgl</c> above the ground under each path point, else its <c>Position.Z</c> but never below the hover height
    /// over the ground; blended linearly between waypoints.
    /// </summary>
    public static class AirRoute
    {
        /// <summary>Straight legs are sampled this far apart, so the height follows the terrain under them.</summary>
        public const float SampleSpacingM = 20f;

        /// <summary>The body is put on the route through an approach leg when it is farther than this from the first waypoint.</summary>
        public const float ApproachMinM = 1f;

        private const byte StopMark = 1;

        /// <summary>True when the aircraft stops at <paramref name="w"/> (it lands there or holds).</summary>
        public static bool IsStop(in NavRouteWaypoint w) => w.Kind == RouteWaypointKind.Ground || w.Hold != RouteHold.None;

        /// <summary>⭐ Airborne insertion B-3 — a <see cref="RouteHold.Drop"/> stop is flown THROUGH (the DZ leg begins there), never stopped at.</summary>
        public static bool IsFlownThrough(in AirStop s) => s.Hold == RouteHold.Drop && s.Kind != RouteWaypointKind.Ground && !s.Last;

        /// <summary>
        /// Compiles <paramref name="waypoints"/> into an <see cref="AirPath"/> flown from <paramref name="from"/> (an approach leg is added
        /// when the body is not at the first waypoint). <paramref name="ground"/> null ⇒ flat ground at 0.
        /// </summary>
        public static AirPath Compile(Vector3 from, IReadOnlyList<NavRouteWaypoint> waypoints, bool loop, float turnRadius, float hoverAgl,
                                      Func<float, float, float>? ground)
        {
            ground ??= static (_, _) => 0f;
            var path = new AirPath { Loop = loop };
            int n = waypoints.Count;
            if (n == 0) return path;

            // the polyline to round: [approach] + waypoints + [the first again, closing a loop]; idx maps each to its waypoint (−1 = approach)
            var pts = new List<Vector3>(n + 2);
            var idx = new List<int>(n + 2);
            var heightAt = new List<Func<float, float, float>>(n + 2);
            float fromAgl = MathF.Max(from.Z - ground(from.X, from.Y), hoverAgl);
            if (Vector2.Distance(Xy(from), Xy(waypoints[0].Position)) > ApproachMinM)
            {
                pts.Add(from); idx.Add(-1);
                heightAt.Add((x, y) => ground(x, y) + fromAgl);
            }
            for (int i = 0; i < n; i++) { pts.Add(waypoints[i].Position); idx.Add(i); heightAt.Add(HeightOf(waypoints[i], hoverAgl, ground)); }
            if (loop && n >= 2) { pts.Add(waypoints[0].Position); idx.Add(0); heightAt.Add(HeightOf(waypoints[0], hoverAgl, ground)); }

            var marks = new byte[pts.Count];
            for (int k = 0; k < pts.Count; k++) if (idx[k] >= 0 && IsStop(waypoints[idx[k]])) marks[k] = StopMark;

            var rounded = new List<Vector3>();
            var roundedMarks = new List<byte>();
            CornerRounding.Round(Flat(pts), marks, turnRadius, null, 0u, rounded, roundedMarks, shiftOut: false);   // a plain fillet: no ground clearance to buy

            // each listed point's place along the rounded line (a kept vertex projects onto itself; a rounded one onto its arc's apex)
            var sAt = new float[pts.Count];
            {
                float cum = 0f; int seg = 0;
                var cumAt = new float[rounded.Count];
                for (int j = 1; j < rounded.Count; j++) cumAt[j] = cumAt[j - 1] + Vector2.Distance(Xy(rounded[j - 1]), Xy(rounded[j]));
                for (int k = 0; k < pts.Count; k++)
                {
                    if (k == 0) { sAt[k] = 0f; continue; }
                    if (k == pts.Count - 1) { sAt[k] = cumAt[^1]; continue; }
                    float best = float.MaxValue, bestS = cum; int bestSeg = seg;
                    for (int j = seg; j < rounded.Count - 1; j++)
                    {
                        var a = Xy(rounded[j]); var b = Xy(rounded[j + 1]);
                        float t = Project(Xy(pts[k]), a, b);
                        float d = Vector2.Distance(Xy(pts[k]), Vector2.Lerp(a, b, t));
                        if (d < best - 1e-4f) { best = d; bestS = cumAt[j] + t * (cumAt[j + 1] - cumAt[j]); bestSeg = j; }
                    }
                    sAt[k] = MathF.Max(bestS, cum); cum = sAt[k]; seg = bestSeg;
                }
            }

            // densify the straight legs, then put every point at its height
            var dense = new List<Vector2> { Xy(rounded[0]) };
            for (int j = 1; j < rounded.Count; j++)
            {
                var a = Xy(rounded[j - 1]); var b = Xy(rounded[j]);
                int parts = Math.Max(1, (int)MathF.Ceiling(Vector2.Distance(a, b) / SampleSpacingM));
                for (int q = 1; q <= parts; q++) dense.Add(Vector2.Lerp(a, b, q / (float)parts));
            }
            int m = dense.Count;
            path.Points = new Vector3[m];
            path.S = new float[m];
            path.Speed = new float[m];
            path.Curvature = new float[m];
            for (int j = 1; j < m; j++) path.S[j] = path.S[j - 1] + Vector2.Distance(dense[j - 1], dense[j]);
            int leg = 0;
            for (int j = 0; j < m; j++)
            {
                float s = path.S[j];
                while (leg < pts.Count - 2 && s > sAt[leg + 1]) leg++;
                float span = sAt[leg + 1] - sAt[leg];
                float t = span > 1e-4f ? Math.Clamp((s - sAt[leg]) / span, 0f, 1f) : 1f;
                var p = dense[j];
                float z = heightAt[leg](p.X, p.Y) + (heightAt[leg + 1](p.X, p.Y) - heightAt[leg](p.X, p.Y)) * t;
                path.Points[j] = new Vector3(p, z);
                int w = idx[leg] >= 0 ? idx[leg] : (idx[leg + 1] >= 0 ? idx[leg + 1] : 0);
                path.Speed[j] = waypoints[w].TargetSpeed;
            }
            // the circle through each point and its neighbours: exact on an arc, gentler where an arc meets a straight leg
            for (int j = 1; j < m - 1; j++)
            {
                var d0 = dense[j] - dense[j - 1]; var d1 = dense[j + 1] - dense[j]; var d2 = dense[j + 1] - dense[j - 1];
                float l0 = d0.Length(), l1 = d1.Length(), l2 = d2.Length();
                if (l0 < 1e-4f || l1 < 1e-4f || l2 < 1e-4f) continue;
                float cross = d0.X * d1.Y - d0.Y * d1.X;   // twice the triangle's signed area
                path.Curvature[j] = 2f * cross / (l0 * l1 * l2);
            }

            // the stops, in order along the path (a loop's first waypoint is the closing point, never s = 0 of the next lap)
            var stops = new List<AirStop>();
            for (int k = 0; k < pts.Count; k++)
            {
                if (idx[k] < 0) continue;
                if (loop && k == FirstListed(idx) && pts.Count > 1) continue;
                bool last = !loop && k == pts.Count - 1;
                var wp = waypoints[idx[k]];
                if (!IsStop(wp) && !last) continue;
                stops.Add(new AirStop { S = sAt[k], Waypoint = idx[k], Kind = wp.Kind, Hold = wp.Hold, HoldSeconds = wp.HoldSeconds, HoldUnit = wp.HoldUnit, Last = last });
            }
            path.Stops = stops.ToArray();
            return path;
        }

        /// <summary>The path at <paramref name="s"/>: the point, the XY heading (unit), the signed curvature and the route speed.</summary>
        public static (Vector3 Point, Vector2 Heading, float Curvature, float Speed) Sample(AirPath path, float s)
        {
            var p = path.Points;
            if (p.Length == 0) return (Vector3.Zero, Vector2.UnitX, 0f, 0f);
            if (p.Length == 1) return (p[0], Vector2.UnitX, 0f, path.Speed[0]);
            s = Math.Clamp(s, 0f, path.Length);
            int hi = Array.BinarySearch(path.S, s);
            if (hi < 0) hi = ~hi;
            hi = Math.Clamp(hi, 1, p.Length - 1);
            int lo = hi - 1;
            float span = path.S[hi] - path.S[lo];
            float t = span > 1e-5f ? (s - path.S[lo]) / span : 0f;
            var d = Xy(p[hi]) - Xy(p[lo]);
            var heading = d.LengthSquared() > 1e-8f ? Vector2.Normalize(d) : Vector2.UnitX;
            return (Vector3.Lerp(p[lo], p[hi], t), heading, path.Curvature[lo] + (path.Curvature[hi] - path.Curvature[lo]) * t, path.Speed[lo]);
        }

        /// <summary>The tightest |curvature| on the path between <paramref name="s0"/> and <paramref name="s1"/>.</summary>
        public static float MaxCurvature(AirPath path, float s0, float s1)
        {
            float k = 0f;
            for (int j = 0; j < path.S.Length; j++)
            {
                if (path.S[j] < s0) continue;
                if (path.S[j] > s1) break;
                k = MathF.Max(k, MathF.Abs(path.Curvature[j]));
            }
            return k;
        }

        private static Func<float, float, float> HeightOf(NavRouteWaypoint w, float hoverAgl, Func<float, float, float> ground)
        {
            if (w.HeightAgl > 0f) { float agl = w.HeightAgl; return (x, y) => ground(x, y) + agl; }
            if (w.Kind == RouteWaypointKind.Ground) return (x, y) => ground(x, y) + hoverAgl;   // the approach height; the landing descends
            float z = w.Position.Z;
            return (x, y) => MathF.Max(z, ground(x, y) + hoverAgl);
        }

        private static int FirstListed(List<int> idx) { for (int k = 0; k < idx.Count; k++) if (idx[k] >= 0) return k; return -1; }

        private static List<Vector3> Flat(List<Vector3> pts)
        {
            var flat = new List<Vector3>(pts.Count);
            foreach (var p in pts) flat.Add(new Vector3(p.X, p.Y, 0f));
            return flat;
        }

        private static float Project(Vector2 p, Vector2 a, Vector2 b)
        {
            var ab = b - a;
            float l2 = ab.LengthSquared();
            return l2 < 1e-8f ? 0f : Math.Clamp(Vector2.Dot(p - a, ab) / l2, 0f, 1f);
        }

        private static Vector2 Xy(Vector3 v) => new(v.X, v.Y);
    }
}
