using System;
using System.Collections.Generic;
using System.Numerics;
using Fdp.Toolkit.Perception.Survey;

namespace Hrot.AI.Behaviors.Brains
{
    /// <summary>
    /// ⭐⭐ <c>CE-2373</c> (AQ104 L, R-2079; Defend §8c D-2m) — pure: how much of a defended area each candidate post SEES, the posts in the
    /// order whose views UNION to the area (greedy), and a lone man's guard round over the first few. The area is <see cref="ProbeCount"/>
    /// probes — <see cref="Bearings"/> bearings round the centre at the search radius and <see cref="ApproachMetres"/> beyond it; a post
    /// sees a probe when the visibility-fan ray nearest its bearing is free past it.
    /// </summary>
    public static class CoveragePosts
    {
        public const int Bearings = 16;
        public const int ProbeCount = 2 * Bearings;

        /// <summary>The approaches: the outer probe ring this far beyond the search radius (m).</summary>
        public const float ApproachMetres = 25f;

        /// <summary>A ray stopped this close short of a probe still sees it (m).</summary>
        public const float ToleranceMetres = 1f;

        /// <summary>Rays per candidate, all round.</summary>
        public const int Rays = 16;

        /// <summary>A round post must add at least this many probes nobody earlier sees.</summary>
        public const int MinRoundGain = 3;

        /// <summary>At most this many posts in a round.</summary>
        public const int MaxRound = 3;

        /// <summary>The probes round <paramref name="centre"/> (inner ring first).</summary>
        public static void Probes(Vector3 centre, float radius, Span<Vector3> probes)
        {
            float inner = MathF.Max(radius, 5f), outer = inner + ApproachMetres;
            for (int b = 0; b < Bearings; b++)
            {
                float a = 2f * MathF.PI * b / Bearings;
                var dir = new Vector3(MathF.Cos(a), MathF.Sin(a), 0f);
                probes[b] = centre + dir * inner;
                probes[Bearings + b] = centre + dir * outer;
            }
        }

        /// <summary>How far a fan ray must reach: the farthest probe from any candidate.</summary>
        public static float Range(Vector3 centre, float radius, float farthestCandidate)
            => Math.Clamp(MathF.Max(radius, 5f) + ApproachMetres + farthestCandidate + ToleranceMetres, 30f, 150f);

        /// <summary>The fan's half arc so its <see cref="Rays"/> rays cover the circle once (the solver spreads them end to end).</summary>
        public static float HalfArc => MathF.PI * (Rays - 1) / Rays;

        /// <summary>A bit per probe that candidate <c>o</c> sees, from the fan's rays (origin index = candidate index).</summary>
        public static void Seen(ReadOnlySpan<Vector3> candidates, IReadOnlyList<SurveyRay> rays, ReadOnlySpan<Vector3> probes, Span<uint> sees)
        {
            sees.Clear();
            for (int o = 0; o < candidates.Length; o++)
            {
                for (int q = 0; q < probes.Length && q < 32; q++)
                {
                    var d = probes[q] - candidates[o];
                    float dist = MathF.Sqrt(d.X * d.X + d.Y * d.Y);
                    float bearing = MathF.Atan2(d.Y, d.X);
                    SurveyRay? best = null;
                    float bestGap = float.MaxValue;
                    foreach (var ray in rays)
                    {
                        if (ray.Origin != o) continue;
                        float gap = MathF.Abs(Wrap(ray.Bearing - bearing));
                        if (gap < bestGap) { bestGap = gap; best = ray; }
                    }
                    if (best != null && best.Free + ToleranceMetres >= dist) sees[o] |= 1u << q;
                }
            }
        }

        /// <summary>
        /// The greedy union: <paramref name="order"/> gets every candidate, each next the one seeing the most probes nobody before it sees (ties ⇒
        /// the lower index — the ask's own rank); returns how many of the first ones belong to a round (each adds ≥ <see cref="MinRoundGain"/>,
        /// at most <see cref="MaxRound"/>).
        /// </summary>
        public static int Greedy(ReadOnlySpan<uint> sees, Span<int> order)
        {
            int n = sees.Length;
            Span<bool> used = stackalloc bool[n];
            uint seen = 0;
            int round = 0;
            bool roundOpen = true;
            for (int r = 0; r < n; r++)
            {
                int best = -1, bestGain = -1;
                for (int k = 0; k < n; k++)
                {
                    if (used[k]) continue;
                    int gain = System.Numerics.BitOperations.PopCount(sees[k] & ~seen);
                    if (gain > bestGain) { bestGain = gain; best = k; }
                }
                used[best] = true;
                order[r] = best;
                seen |= sees[best];
                if (roundOpen && round < MaxRound && (r == 0 ? bestGain > 0 : bestGain >= MinRoundGain)) round++;
                else roundOpen = false;
            }
            return round;
        }

        /// <summary>The round post to go to next from <paramref name="current"/>: the one whose probes were watched longest ago in total
        /// (an unwatched probe counts from <paramref name="since"/>); -1 when there is no other.</summary>
        public static int NextRound(ReadOnlySpan<uint> roundSees, int current, ReadOnlySpan<double> watchedAt, double now, double since)
        {
            int best = -1;
            double bestStale = -1;
            for (int k = 0; k < roundSees.Length; k++)
            {
                if (k == current) continue;
                double stale = 0;
                for (int q = 0; q < watchedAt.Length && q < 32; q++)
                    if ((roundSees[k] & (1u << q)) != 0) stale += now - Math.Max(watchedAt[q], since);
                if (stale > bestStale) { bestStale = stale; best = k; }
            }
            return best;
        }

        private static float Wrap(float a)
        {
            while (a > MathF.PI) a -= 2f * MathF.PI;
            while (a < -MathF.PI) a += 2f * MathF.PI;
            return a;
        }
    }
}
