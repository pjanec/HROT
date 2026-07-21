using System;
using System.Numerics;
using Hrot.Editor.AiShared;

namespace Hrot.AI.Behaviors.Brains
{
    /// <summary>
    /// Curated, reflection-free firing-line segment math for blueprints. Bundles the
    /// clamp/cast/conditional-default arithmetic that has no visual-node expression (architect Q#6-A:
    /// non-trivial math stays in reviewable curated helpers; only plain arithmetic is a visual
    /// <c>BinaryOp</c>). Mirrors the <c>VectorOps</c>/<c>UnitRosterOps</c> off-graph-helper shape.
    /// </summary>
    public static class SegmentMath
    {
        /// <summary>
        /// Firing-line slot count for a start-&gt;end segment at the given tank spacing, clamped to
        /// [1, 16]. Reproduces the C# oracle
        /// <c>HillAttackCommanderNodes.Action_CalculateSegments</c>:
        /// <c>spacing = rawSpacing &gt; 0 ? rawSpacing : 30</c>, then
        /// <c>clamp(max(1, (int)(distance(start,end) / spacing)), 1, 16)</c>. A non-positive
        /// <paramref name="rawSpacing"/> falls back to the oracle's 30 m default (never divides by 0).
        /// </summary>
        [BlueprintCallable("Segment", DisplayName = "Total Slots")]
        public static int TotalSlots(float startX, float startY, float endX, float endY, float rawSpacing)
        {
            float segLen  = Vector2.Distance(new Vector2(startX, startY), new Vector2(endX, endY));
            float spacing = rawSpacing > 0f ? rawSpacing : 30f;
            int totalSlots = Math.Max(1, (int)(segLen / spacing));
            if (totalSlots > 16) totalSlots = 16;
            return totalSlots;
        }

        /// <summary>
        /// 0..1 interpolation parameter for the <paramref name="index"/>-th of <paramref name="count"/>
        /// evenly-spaced tanks along the baseline. Reproduces the C# oracle
        /// <c>HillAttackCommanderNodes.Action_DispatchAllToBaseline</c>'s
        /// <c>t = count &gt; 1 ? (float)i / (count - 1) : 0.5f</c> — a single tank sits at the midpoint
        /// (0.5), otherwise the tanks span <c>[0, 1]</c> endpoint-inclusive. The conditional (guarding the
        /// <c>count - 1</c> divisor) has no visual-node form, so it stays in this curated helper (architect
        /// Q#6-A).
        /// </summary>
        [BlueprintCallable("Segment", DisplayName = "Lerp Param (0..1)")]
        public static float LerpParam(int index, int count)
            => count > 1 ? (float)index / (count - 1) : 0.5f;

        /// <summary>
        /// Linear interpolation <c>a + (b - a) * t</c> — the per-axis baseline position from the two
        /// endpoints and the <see cref="LerpParam"/> parameter. Plain arithmetic a visual <c>BinaryOp</c>
        /// chain could express (Subtract→Multiply→Add), bundled here with its <see cref="LerpParam"/>
        /// sibling as one reviewable "baseline interpolation" helper to keep the dispatch graph tractable
        /// (the <c>BinaryOp</c> node is separately proven by its coverage fixture).
        /// </summary>
        [BlueprintCallable("Segment", DisplayName = "Lerp")]
        public static float Lerp(float a, float b, float t) => a + (b - a) * t;
    }
}
