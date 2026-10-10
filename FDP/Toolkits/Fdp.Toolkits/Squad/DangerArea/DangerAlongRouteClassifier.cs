using System;
using System.Numerics;
using Fdp.Toolkit.Terrain;

namespace Fdp.Toolkit.Squad.DangerArea
{
    /// <summary>
    /// ⭐⭐ <c>CE-3072</c> B3 (R-213, B1″) — the danger areas along ONE route: a pure function of (route, road graph), run on the
    /// node that holds the route (<c>DangerAlongRouteSolve</c>). 📄 docs/DESIGN_Utility_AI_Demo_Scenarios.md §10.7.
    /// <para>⭐⭐ <c>CE-3128</c> (R-231, Nav v2 §5.2a D7) — the roads are the road GRAPH, not <c>surface: road</c> polygons (retired):
    /// a point is ON a road when it lies within a segment's BAND (half of <c>LaneWidth × LaneCount</c> from the centre line), and in
    /// a JUNCTION when it lies within the widest band of a node with three or more segments.</para>
    /// <para>v1 kinds: <see cref="DangerAreaKind.StreetCrossing"/> — the route is on a road for a run no longer than
    /// <see cref="MaxCrossingLength"/> (a longer run is walking ALONG the street, not crossing it) — and
    /// <see cref="DangerAreaKind.Intersection"/> — such a run through a junction. ⚠ OpenGround, ChokePoint and CrestLine are
    /// <c>CE-3081</c>.</para>
    /// <para>⛔ No threat: the answer carries geometry only; the Brain rates it from what the unit knows (B2, R-194).</para>
    /// <para>⭐ <see cref="DangerAreaDescriptor.FeatureId"/> is stable across re-plans: it hashes the road elements (segments, or the
    /// junction) and the run's EXIT point on a 10 m grid — the exit does not move while the unit walks up to and through the
    /// crossing (the route is re-planned from the unit each time, so its START does).</para>
    /// </summary>
    public static class DangerAlongRouteClassifier
    {
        /// <summary>A road run longer than this (m) is not a crossing.</summary>
        public const float MaxCrossingLength = 40f;
        /// <summary>How far before / after the run the near / far handles stand (m).</summary>
        public const float StandOff = 4f;
        /// <summary>Sampling step along the route (m).</summary>
        public const float Step = 1f;
        /// <summary>The grid the FeatureId quantises the exit point to (m).</summary>
        public const float FeatureGrid = 10f;

        /// <summary>
        /// Classifies <paramref name="route"/> (a polyline from the unit) over <paramref name="terrain"/>; writes up to
        /// <c>areas.Length</c> descriptors in route order and returns how many.
        /// </summary>
        public static int Classify(ReadOnlySpan<Vector3> route, in global::CarKinem.Road.RoadNetworkBlob roads, Fdp.Toolkit.World.IWorldQuery? terrain,
            float corridorHalfWidth, Span<DangerAreaDescriptor> areas)
        {
            if (route.Length < 2 || areas.Length == 0 || !Fdp.Toolkit.Navigation.RoadGraphRouter.HasRoads(roads)) return 0;

            Span<float> cumulative = route.Length <= 256 ? stackalloc float[route.Length] : new float[route.Length];
            cumulative[0] = 0f;
            for (int i = 1; i < route.Length; i++)
                cumulative[i] = cumulative[i - 1] + Vector2.Distance(Xy(route[i - 1]), Xy(route[i]));
            float total = cumulative[^1];
            if (total <= 0f) return 0;

            int written = 0;
            bool inRun = false;
            float runStart = 0f;
            ulong runMask = 0;
            for (float d = 0f; ; d += Step)
            {
                bool last = d >= total;
                float at = last ? total : d;
                ulong mask = RoadMask(roads, Xy(PointAt(route, cumulative, at)));

                if (mask != 0 && !inRun) { inRun = true; runStart = at; runMask = mask; }
                else if (mask != 0) runMask |= mask;

                if (inRun && (mask == 0 || last))
                {
                    float runEnd = mask == 0 ? at - Step * 0.5f : at;
                    if (runEnd < runStart) runEnd = runStart;
                    if (runEnd - runStart <= MaxCrossingLength)
                    {
                        areas[written++] = Describe(route, cumulative, total, terrain, runStart, runEnd, runMask, corridorHalfWidth);
                        if (written == areas.Length) return written;
                    }
                    inRun = false;
                    runMask = 0;
                }
                if (last) break;
            }
            return written;
        }

        private static DangerAreaDescriptor Describe(ReadOnlySpan<Vector3> route, ReadOnlySpan<float> cumulative, float total,
            Fdp.Toolkit.World.IWorldQuery? terrain, float start, float end, ulong mask, float corridor)
        {
            float mid = (start + end) * 0.5f;
            var center = PointAt(route, cumulative, mid);
            var exit = PointAt(route, cumulative, end);
            var dir = Xy(PointAt(route, cumulative, MathF.Min(total, mid + 0.5f)) - PointAt(route, cumulative, MathF.Max(0f, mid - 0.5f)));
            float ground = terrain?.SurfaceZ(center.X, center.Y, center.Z) ?? center.Z;
            center.Z = ground;
            return new DangerAreaDescriptor
            {
                FeatureId          = FeatureIdOf(mask, exit),
                ThreatRating       = 0f,
                Kind               = (mask & JunctionBit) != 0 ? DangerAreaKind.Intersection : DangerAreaKind.StreetCrossing,
                Center             = center,
                ExtentsXY          = new Vector2(MathF.Max(0.5f, (end - start) * 0.5f), MathF.Max(1f, corridor)),
                AngleRad           = dir.LengthSquared() > 1e-6f ? MathF.Atan2(dir.Y, dir.X) : 0f,
                ZFloor             = ground - 1f,
                ZCeiling           = ground + 3f,
                NearSideHandle     = Grounded(terrain, PointAt(route, cumulative, MathF.Max(0f, start - StandOff))),
                FarSideHandle      = Grounded(terrain, PointAt(route, cumulative, MathF.Min(total, end + StandOff))),
                DistanceAlongRoute = start,
            };
        }

        /// <summary>The stable id of a crossing: its road elements and its exit point on a <see cref="FeatureGrid"/> grid (FNV-1a).</summary>
        public static uint FeatureIdOf(ulong roadMask, Vector3 exit)
        {
            unchecked
            {
                uint h = 2166136261u;
                void Mix(uint v) { for (int b = 0; b < 4; b++) { h ^= (v >> (8 * b)) & 0xFFu; h *= 16777619u; } }
                Mix((uint)roadMask); Mix((uint)(roadMask >> 32));
                Mix((uint)(int)MathF.Round(exit.X / FeatureGrid));
                Mix((uint)(int)MathF.Round(exit.Y / FeatureGrid));
                return h == 0 ? 1u : h;
            }
        }

        /// <summary>The top bit of a road mask: the point is inside a junction (a node with three or more segments).</summary>
        public const ulong JunctionBit = 1ul << 63;

        /// <summary>
        /// Bit (i mod 63) = the point lies in segment i's band; <see cref="JunctionBit"/> = it lies inside a junction, whose node
        /// index then also sets bit (node mod 63) so two junctions hash apart. 0 = off the roads.
        /// </summary>
        private static ulong RoadMask(in global::CarKinem.Road.RoadNetworkBlob roads, Vector2 p)
        {
            ulong mask = 0;
            for (int i = 0; i < roads.Segments.Length; i++)
            {
                var seg = roads.Segments[i];
                float half = HalfWidth(seg);
                float reach = half + 0.25f * (seg.T0.Length() + seg.T1.Length());   // the curve stays within its chord box + this
                if (p.X < MathF.Min(seg.P0.X, seg.P1.X) - reach || p.X > MathF.Max(seg.P0.X, seg.P1.X) + reach) continue;
                if (p.Y < MathF.Min(seg.P0.Y, seg.P1.Y) - reach || p.Y > MathF.Max(seg.P0.Y, seg.P1.Y) + reach) continue;
                if (DistanceToCurve(seg, p) <= half) mask |= 1ul << (i % 63);
            }
            if (mask == 0) return 0;
            for (int n = 0; n < roads.Nodes.Length; n++)
            {
                int degree = 0; float radius = 0f;
                for (int i = 0; i < roads.Segments.Length; i++)
                {
                    var seg = roads.Segments[i];
                    if (seg.StartNodeIndex != n && seg.EndNodeIndex != n) continue;
                    degree++;
                    radius = MathF.Max(radius, HalfWidth(seg));
                }
                if (degree >= 3 && Vector2.Distance(p, roads.Nodes[n].Position) <= radius)
                    return JunctionBit | (1ul << (n % 63));
            }
            return mask;
        }

        private static float HalfWidth(in global::CarKinem.Road.RoadSegment seg) => 0.5f * seg.LaneWidth * Math.Max(1, seg.LaneCount);

        private static float DistanceToCurve(in global::CarKinem.Road.RoadSegment seg, Vector2 p)
        {
            const int Samples = 16;
            float best = float.MaxValue;
            var prev = seg.P0;
            for (int k = 1; k <= Samples; k++)
            {
                var cur = global::CarKinem.Road.RoadGraphNavigator.EvaluateHermite(k / (float)Samples, seg.P0, seg.T0, seg.P1, seg.T1);
                best = MathF.Min(best, DistanceToSegment(p, prev, cur));
                prev = cur;
            }
            return best;
        }

        private static float DistanceToSegment(Vector2 p, Vector2 a, Vector2 b)
        {
            var ab = b - a;
            float len2 = ab.LengthSquared();
            float t = len2 > 1e-9f ? Math.Clamp(Vector2.Dot(p - a, ab) / len2, 0f, 1f) : 0f;
            return Vector2.Distance(p, a + ab * t);
        }

        private static Vector3 PointAt(ReadOnlySpan<Vector3> route, ReadOnlySpan<float> cumulative, float d)
        {
            if (d <= 0f) return route[0];
            for (int i = 1; i < route.Length; i++)
            {
                if (cumulative[i] < d) continue;
                float seg = cumulative[i] - cumulative[i - 1];
                float t = seg <= 0f ? 0f : (d - cumulative[i - 1]) / seg;
                return Vector3.Lerp(route[i - 1], route[i], t);
            }
            return route[^1];
        }

        private static Vector3 Grounded(Fdp.Toolkit.World.IWorldQuery? terrain, Vector3 p) => terrain == null ? p : new(p.X, p.Y, terrain.SurfaceZ(p.X, p.Y, p.Z));

        private static Vector2 Xy(Vector3 v) => new(v.X, v.Y);
    }
}
