using System;
using System.Collections.Generic;
using System.Numerics;

namespace Fdp.Toolkit.Terrain
{
    /// <summary>
    /// ⭐ CE-5012 (docs/DESIGN_Terrain_Editor_UX.md §5 TE-7; §8 R-5; the recipe <c>enclosure</c> precedent, <c>FeatureElements.cs</c>) — a GATE in a closed wall: the ring's
    /// start is ROTATED into a gap <see cref="Width"/> wide on the segment nearest the click, so the wall stays ONE line (one entity, one label, one wall key) that runs from one
    /// gate post round the plot to the other. ⭐ Breaches survive it: <see cref="BuildingPanelIndex.TryMapBreach"/> follows a re-drawn line by geometry. One gate per wall (v1).
    /// </summary>
    public static class WallGate
    {
        /// <summary>The gate's width (m).</summary>
        public const float Width = 3f;
        /// <summary>The least wall left between a gate post and a corner (m).</summary>
        public const float PostMargin = 0.25f;

        /// <summary>True when <paramref name="line"/> is a closed wall: at least three corners and its first point repeated at the end.</summary>
        public static bool IsClosed(IReadOnlyList<Vector2> line)
            => line.Count >= 4 && Vector2.DistanceSquared(line[0], line[line.Count - 1]) < 1e-6f;

        /// <summary>
        /// The OPEN line of closed wall <paramref name="ring"/> with a gate at the point of its nearest segment to <paramref name="at"/> (kept a post margin from each corner):
        /// the far gate post, every corner in order round the plot, the near gate post. Null (with <paramref name="reason"/>) when the ring is not closed or the segment is too short.
        /// </summary>
        public static List<Vector2>? Open(IReadOnlyList<Vector2> ring, Vector2 at, out string? reason)
        {
            reason = null;
            if (!IsClosed(ring)) { reason = "only a closed wall takes a gate"; return null; }
            int n = ring.Count - 1;   // the corners (the repeated last point dropped)
            int k = -1; float best = float.MaxValue, along = 0f;
            for (int i = 0; i < n; i++)
            {
                var a = ring[i]; var d = ring[i + 1] - a; float len2 = d.LengthSquared();
                if (len2 < 1e-8f) continue;
                float t = Math.Clamp(Vector2.Dot(at - a, d) / len2, 0f, 1f);
                float d2 = Vector2.DistanceSquared(a + d * t, at);
                if (d2 < best) { best = d2; k = i; along = t * MathF.Sqrt(len2); }
            }
            if (k < 0) { reason = "the wall has no length"; return null; }
            var p = ring[k]; var q = ring[k + 1]; float segLen = Vector2.Distance(p, q);
            float half = Width * 0.5f;
            if (segLen < Width + 2f * PostMargin) { reason = $"this side is too short for a {Width:0} m gate"; return null; }
            along = Math.Clamp(along, half + PostMargin, segLen - half - PostMargin);
            var dir = (q - p) / segLen;
            var near = p + dir * (along - half);
            var far = p + dir * (along + half);
            var open = new List<Vector2>(n + 2) { far };
            for (int j = 1; j <= n; j++) open.Add(ring[(k + j) % n]);
            open.Add(near);
            return open;
        }
    }
}
